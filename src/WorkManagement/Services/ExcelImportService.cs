using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WorkManagement.Models;
using MiniExcelLibs;

namespace WorkManagement.Services;

public enum ExcelImportConflictAction
{
    OverwriteExisting,
    SkipExisting,
    AppendAll
}

public class ExcelSheetData
{
    public string SheetName { get; set; } = string.Empty;
    public List<VirtualMachineEntry> Entries { get; set; } = new();
    public int Score { get; set; }
    public bool IsRecommended { get; set; }

    public string DisplayText => IsRecommended
        ? $"{SheetName} ({Entries.Count} máy) [Khuyến nghị]"
        : $"{SheetName} ({Entries.Count} dòng)";
}

public class ExcelImportResult
{
    public int TotalRead { get; set; }
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Errors { get; } = new();
    public List<VirtualMachineEntry> ImportedEntries { get; } = new();
}

public class ExcelImportService
{
    public async Task<List<VirtualMachineEntry>> ReadExcelAsync(string filePath)
    {
        var sheets = await ReadSheetsFromExcelAsync(filePath);
        return sheets.Count > 0 ? sheets[0].Entries : new List<VirtualMachineEntry>();
    }

    public Task<List<ExcelSheetData>> ReadSheetsFromExcelAsync(string filePath)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Không tìm thấy file Excel", filePath);
            }

            // Lấy danh sách tất cả các sheet trong file Excel
            List<string> sheetNames;
            try
            {
                sheetNames = MiniExcel.GetSheetNames(filePath).ToList();
            }
            catch
            {
                sheetNames = new List<string>();
            }

            if (sheetNames.Count == 0)
            {
                sheetNames.Add(string.Empty);
            }

            var sheetResults = new List<ExcelSheetData>();

            foreach (var sheetName in sheetNames)
            {
                var sheetData = ReadSheetData(filePath, string.IsNullOrEmpty(sheetName) ? null : sheetName);
                if (sheetData.Entries.Count > 0)
                {
                    sheetResults.Add(sheetData);
                }
            }

            // Nếu không tìm thấy bằng danh sách tên sheet, thử fallback query mặc định
            if (sheetResults.Count == 0)
            {
                var fallbackData = ReadSheetData(filePath, null);
                if (fallbackData.Entries.Count > 0)
                {
                    sheetResults.Add(fallbackData);
                }
            }

            // Sắp xếp các sheet theo điểm khớp (Score) giảm dần
            sheetResults = sheetResults.OrderByDescending(s => s.Score).ThenByDescending(s => s.Entries.Count).ToList();

            if (sheetResults.Count > 0)
            {
                sheetResults[0].IsRecommended = true;
            }

            return sheetResults;
        });
    }

    private ExcelSheetData ReadSheetData(string filePath, string? sheetName)
    {
        var result = new ExcelSheetData
        {
            SheetName = string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName
        };

        var rawRows = new List<IDictionary<string, object>>();

        try
        {
            var queryResult = (sheetName == null
                ? MiniExcel.Query(filePath, useHeaderRow: false)
                : MiniExcel.Query(filePath, useHeaderRow: false, sheetName: sheetName));

            foreach (var row in queryResult)
            {
                if (row is IDictionary<string, object> dict)
                {
                    rawRows.Add(dict);
                }
            }
        }
        catch
        {
            return result;
        }

        if (rawRows.Count == 0) return result;

        int headerRowIndex = -1;
        var colMapping = new Dictionary<string, string>(); // ColumnKey (ví dụ "A", "B") -> Property Name
        int bestScore = 0;

        // Ưu tiên dòng thứ 2 (index 1) theo yêu cầu: Dòng 1 bỏ qua, Dòng 2 là title của các item
        if (rawRows.Count > 1)
        {
            var (mappingRow2, scoreRow2) = DetectHeaderRow(rawRows[1]);
            if (scoreRow2 >= 20 && mappingRow2.Count >= 2)
            {
                headerRowIndex = 1;
                colMapping = mappingRow2;
                bestScore = scoreRow2;
            }
        }

        // Nếu dòng 2 không phải header, quét tìm dòng tiêu đề trong tối đa 20 dòng đầu
        if (headerRowIndex == -1)
        {
            int searchLimit = Math.Min(20, rawRows.Count);
            for (int i = 0; i < searchLimit; i++)
            {
                var row = rawRows[i];
                var (mapping, score) = DetectHeaderRow(row);
                if (score > bestScore && mapping.Count >= 2)
                {
                    bestScore = score;
                    headerRowIndex = i;
                    colMapping = mapping;
                }
            }
        }

        if (headerRowIndex == -1 || colMapping.Count == 0)
        {
            return result;
        }

        result.Score = bestScore;

        // Đọc tất cả các dòng dữ liệu nằm sau dòng Header
        for (int i = headerRowIndex + 1; i < rawRows.Count; i++)
        {
            var row = rawRows[i];

            // Bỏ qua dòng rỗng hoàn toàn
            if (row.Values.All(v => v == null || string.IsNullOrWhiteSpace(v.ToString())))
            {
                continue;
            }

            var entry = new VirtualMachineEntry();

            foreach (var mapping in colMapping)
            {
                string colKey = mapping.Key;
                string propName = mapping.Value;

                if (!row.TryGetValue(colKey, out var rawVal) || rawVal == null)
                {
                    continue;
                }

                string valStr = FormatCellValue(rawVal);
                if (string.IsNullOrWhiteSpace(valStr)) continue;

                switch (propName)
                {
                    case nameof(VirtualMachineEntry.No):
                        entry.No = valStr;
                        break;
                    case nameof(VirtualMachineEntry.Ip):
                        entry.Ip = valStr;
                        break;
                    case nameof(VirtualMachineEntry.MachineName):
                        entry.MachineName = valStr;
                        break;
                    case nameof(VirtualMachineEntry.DomainOrWorkgroup):
                        entry.DomainOrWorkgroup = valStr;
                        break;
                    case nameof(VirtualMachineEntry.Os):
                        entry.Os = valStr;
                        break;
                    case nameof(VirtualMachineEntry.Username):
                        entry.Username = valStr;
                        break;
                    case nameof(VirtualMachineEntry.Password):
                        entry.Password = valStr;
                        break;
                    case nameof(VirtualMachineEntry.Tns):
                        entry.Tns = valStr;
                        break;
                    case nameof(VirtualMachineEntry.DatabaseName):
                        entry.DatabaseName = valStr;
                        break;
                    case nameof(VirtualMachineEntry.OracleVersion):
                        entry.OracleVersion = valStr;
                        break;
                    case nameof(VirtualMachineEntry.Tester):
                        entry.Tester = valStr;
                        break;
                    case nameof(VirtualMachineEntry.Notes):
                        entry.Notes = valStr;
                        break;
                    case nameof(VirtualMachineEntry.ExpirationDate):
                        entry.ExpirationDate = valStr;
                        break;
                    case nameof(VirtualMachineEntry.DbaUser):
                        entry.DbaUser = valStr;
                        break;
                    case nameof(VirtualMachineEntry.DbaPassword):
                        entry.DbaPassword = valStr;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(entry.ExpirationDate))
            {
                entry.ExpirationDate = "Vô hạn";
            }

            // Nếu file Excel không có cột No hoặc No bị trống, tự động gán theo số thứ tự dòng
            if (string.IsNullOrWhiteSpace(entry.No))
            {
                entry.No = (result.Entries.Count + 1).ToString();
            }

            // Dòng được xem là hợp lệ nếu có ít nhất IP hoặc Tên máy
            if (!string.IsNullOrWhiteSpace(entry.Ip) || !string.IsNullOrWhiteSpace(entry.MachineName))
            {
                result.Entries.Add(entry);
            }
        }

        return result;
    }

    private static (Dictionary<string, string> Mapping, int Score) DetectHeaderRow(IDictionary<string, object> row)
    {
        var mapping = new Dictionary<string, string>();
        int score = 0;
        bool seenDbaUser = false;
        bool seenOsPassword = false;

        foreach (var kv in row)
        {
            if (kv.Value == null) continue;
            string text = kv.Value.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) continue;

            string normalized = NormalizeKey(text);
            string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string? prop = MatchProperty(normalized, words, seenDbaUser, seenOsPassword);
            if (prop != null && !mapping.ContainsValue(prop))
            {
                mapping[kv.Key] = prop;

                if (prop == nameof(VirtualMachineEntry.DbaUser))
                {
                    seenDbaUser = true;
                    score += 15;
                }
                else if (prop == nameof(VirtualMachineEntry.DbaPassword))
                {
                    score += 15;
                }
                else if (prop == nameof(VirtualMachineEntry.Password))
                {
                    seenOsPassword = true;
                    score += 10;
                }
                else if (prop == nameof(VirtualMachineEntry.Ip))
                {
                    score += 30;
                }
                else if (prop == nameof(VirtualMachineEntry.MachineName))
                {
                    score += 25;
                }
                else if (prop == nameof(VirtualMachineEntry.Os))
                {
                    score += 15;
                }
                else if (prop == nameof(VirtualMachineEntry.DomainOrWorkgroup))
                {
                    score += 10;
                }
                else if (prop == nameof(VirtualMachineEntry.Username))
                {
                    score += 10;
                }
                else
                {
                    score += 5;
                }
            }
        }

        return (mapping, score);
    }

    private static string? MatchProperty(string norm, string[] words, bool seenDbaUser, bool seenOsPassword)
    {
        // DBA USER
        if (norm == "dba user" || norm == "dba username" || norm == "user dba" || norm == "dba" ||
            norm.Contains("dba user") || norm.Contains("user dba"))
            return nameof(VirtualMachineEntry.DbaUser);

        // DBA PASSWORD: nếu chứa rõ dba password hoặc xuất hiện sau DBA User
        if (norm == "dba password" || norm == "dba pass" || norm == "mat khau dba" || norm == "pass dba" || norm == "password dba" ||
            norm.Contains("dba pass") || norm.Contains("pass dba"))
            return nameof(VirtualMachineEntry.DbaPassword);

        // PASSWORD / P: phải là 'p' chính xác hoặc từ pass/password
        if (norm == "p" || norm == "pass" || norm == "password" || norm == "mat khau" ||
            words.Contains("pass") || words.Contains("password"))
        {
            if (seenDbaUser || seenOsPassword)
            {
                return nameof(VirtualMachineEntry.DbaPassword);
            }
            return nameof(VirtualMachineEntry.Password);
        }

        // NO / STT: so khớp chính xác
        if (norm == "no" || norm == "stt" || norm == "id" || norm == "no." || words.Contains("stt"))
            return nameof(VirtualMachineEntry.No);

        // IP: phải là 'ip', 'dia chi ip', 'ip address' hoặc từ 'ip' riêng biệt (tránh Description, Equipment, Equipment...)
        if (norm == "ip" || norm == "dia chi ip" || norm == "ip address" || words.Contains("ip"))
            return nameof(VirtualMachineEntry.Ip);

        // TÊN MÁY
        if (norm == "ten may" || norm == "machine name" || norm == "hostname" || norm == "host name" ||
            norm == "ten server" || norm == "server name" || norm == "machine" ||
            norm.Contains("ten may") || norm.Contains("machine name"))
            return nameof(VirtualMachineEntry.MachineName);

        // DOMAIN / WORKGROUP
        if (norm.Contains("domain") || norm.Contains("workgroup"))
            return nameof(VirtualMachineEntry.DomainOrWorkgroup);

        // OS / HỆ ĐIỀU HÀNH: so khớp chính xác hoặc theo từ
        if (norm == "os" || norm == "he dieu hanh" || norm == "operating system" || words.Contains("os"))
            return nameof(VirtualMachineEntry.Os);

        // USER / USERNAME / TÀI KHOẢN
        if (norm == "user" || norm == "username" || norm == "tai khoan" || norm == "nguoi dung" || words.Contains("user"))
            return nameof(VirtualMachineEntry.Username);

        // TNS: so khớp chính xác hoặc từ
        if (norm == "tns" || norm == "tns name" || words.Contains("tns"))
            return nameof(VirtualMachineEntry.Tns);

        // DATABASE NAME: tránh nhầm với Database Oracle Version
        if (norm == "database name" || norm == "db name" || norm == "ten db" || norm == "database" || norm == "db" || norm == "dbname" ||
            norm.StartsWith("database name") || norm.StartsWith("db name"))
            return nameof(VirtualMachineEntry.DatabaseName);

        // ORACLE VERSION
        if (norm.Contains("version") || norm.Contains("phien ban oracle") || norm.Contains("version oracl"))
            return nameof(VirtualMachineEntry.OracleVersion);

        // NGƯỜI KIỂM TRA
        if (norm.Contains("nguoi kiem") || norm.Contains("tester") || norm.Contains("quan tri") || norm.Contains("nguoi kt"))
            return nameof(VirtualMachineEntry.Tester);

        // GHI CHÚ
        if (norm.Contains("ghi chu") || norm == "note" || norm == "notes" || norm == "mo ta")
            return nameof(VirtualMachineEntry.Notes);

        // NGÀY HẾT HẠN
        if (norm.Contains("het han") || norm.Contains("expire") || norm.Contains("expiration"))
            return nameof(VirtualMachineEntry.ExpirationDate);

        return null;
    }

    private static string FormatCellValue(object val)
    {
        if (val is DateTime dt)
        {
            return dt.ToString("yyyy/MM/dd");
        }
        return val.ToString()?.Trim() ?? string.Empty;
    }

    private static bool MatchAny(string text, params string[] patterns)
    {
        return patterns.Any(p => text == p || text.StartsWith(p));
    }

    public ExcelImportResult MergeIntoList(
        List<VirtualMachineEntry> currentList,
        IEnumerable<VirtualMachineEntry> importedList,
        ExcelImportConflictAction conflictAction)
    {
        var result = new ExcelImportResult();

        foreach (var incoming in importedList)
        {
            result.TotalRead++;

            // Tìm máy trùng theo IP (hoặc Tên máy nếu IP trống)
            var existing = currentList.FirstOrDefault(e =>
                (!string.IsNullOrWhiteSpace(e.Ip) && string.Equals(e.Ip.Trim(), incoming.Ip.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(e.MachineName) && string.Equals(e.MachineName.Trim(), incoming.MachineName.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                if (conflictAction == ExcelImportConflictAction.SkipExisting)
                {
                    result.SkippedCount++;
                }
                else if (conflictAction == ExcelImportConflictAction.OverwriteExisting)
                {
                    // Luôn ghi đè số thứ tự từ file Excel
                    existing.No = string.IsNullOrWhiteSpace(incoming.No) ? existing.No : incoming.No;
                    if (!string.IsNullOrWhiteSpace(incoming.No))
                    {
                        existing.No = incoming.No;
                    }
                    existing.MachineName = string.IsNullOrWhiteSpace(incoming.MachineName) ? existing.MachineName : incoming.MachineName;
                    existing.DomainOrWorkgroup = string.IsNullOrWhiteSpace(incoming.DomainOrWorkgroup) ? existing.DomainOrWorkgroup : incoming.DomainOrWorkgroup;
                    existing.Os = string.IsNullOrWhiteSpace(incoming.Os) ? existing.Os : incoming.Os;
                    existing.Username = string.IsNullOrWhiteSpace(incoming.Username) ? existing.Username : incoming.Username;
                    existing.Password = string.IsNullOrWhiteSpace(incoming.Password) ? existing.Password : incoming.Password;
                    existing.Tns = string.IsNullOrWhiteSpace(incoming.Tns) ? existing.Tns : incoming.Tns;
                    existing.DatabaseName = string.IsNullOrWhiteSpace(incoming.DatabaseName) ? existing.DatabaseName : incoming.DatabaseName;
                    existing.OracleVersion = string.IsNullOrWhiteSpace(incoming.OracleVersion) ? existing.OracleVersion : incoming.OracleVersion;
                    existing.Tester = string.IsNullOrWhiteSpace(incoming.Tester) ? existing.Tester : incoming.Tester;
                    existing.Notes = string.IsNullOrWhiteSpace(incoming.Notes) ? existing.Notes : incoming.Notes;
                    existing.ExpirationDate = string.IsNullOrWhiteSpace(incoming.ExpirationDate) ? existing.ExpirationDate : incoming.ExpirationDate;
                    existing.DbaUser = string.IsNullOrWhiteSpace(incoming.DbaUser) ? existing.DbaUser : incoming.DbaUser;
                    existing.DbaPassword = string.IsNullOrWhiteSpace(incoming.DbaPassword) ? existing.DbaPassword : incoming.DbaPassword;

                    result.UpdatedCount++;
                    result.ImportedEntries.Add(existing);
                }
                else // AppendAll
                {
                    var newEntry = incoming.Clone();
                    newEntry.Id = Guid.NewGuid().ToString();
                    currentList.Add(newEntry);
                    result.AddedCount++;
                    result.ImportedEntries.Add(newEntry);
                }
            }
            else
            {
                var newEntry = incoming.Clone();
                if (string.IsNullOrEmpty(newEntry.Id)) newEntry.Id = Guid.NewGuid().ToString();
                currentList.Add(newEntry);
                result.AddedCount++;
                result.ImportedEntries.Add(newEntry);
            }
        }

        return result;
    }

    public async Task ExportTemplateAsync(string destinationPath)
    {
        var rows = new List<Dictionary<string, object>>
        {
            // Dòng 1: Tiêu đề tổng quát (Hệ thống sẽ tự động bỏ qua dòng đầu tiên này khi Import)
            new()
            {
                ["A"] = "DANH SÁCH MÁY ẢO VÀ THÔNG TIN DBA (DÒNG 1 NÀY SẼ BỎ QUA KHI IMPORT)",
                ["B"] = string.Empty,
                ["C"] = string.Empty,
                ["D"] = string.Empty,
                ["E"] = string.Empty,
                ["F"] = string.Empty,
                ["G"] = string.Empty,
                ["H"] = string.Empty,
                ["I"] = string.Empty,
                ["J"] = string.Empty,
                ["K"] = string.Empty,
                ["L"] = string.Empty,
                ["M"] = string.Empty,
                ["N"] = string.Empty,
                ["O"] = string.Empty
            },
            // Dòng 2: Tiêu đề các cột (Title của các item)
            new()
            {
                ["A"] = "No",
                ["B"] = "IP",
                ["C"] = "Tên máy",
                ["D"] = "Domain / Workgroup",
                ["E"] = "OS",
                ["F"] = "User",
                ["G"] = "Password",
                ["H"] = "TNS",
                ["I"] = "Database Name",
                ["J"] = "Version Oracle",
                ["K"] = "Người kiểm tra",
                ["L"] = "Ghi chú",
                ["M"] = "Ngày hết hạn",
                ["N"] = "DBA User",
                ["O"] = "Password"
            },
            // Dòng 3: Dòng dữ liệu mẫu
            new()
            {
                ["A"] = "1",
                ["B"] = "172.16.6.16",
                ["C"] = "D2-SVR15-6016",
                ["D"] = "FUJINET.vn",
                ["E"] = "Windows Server 2008 64bit",
                ["F"] = "Administrator",
                ["G"] = "D2-20100802",
                ["H"] = "ORCL11GR2K",
                ["I"] = "ORCL11GR2K",
                ["J"] = "11.2.0.4.0",
                ["K"] = "Duy",
                ["L"] = "Oracle Server 11gr2",
                ["M"] = "Vô hạn",
                ["N"] = "SYSTEM",
                ["O"] = "manager"
            }
        };

        var workbook = new Dictionary<string, object>
        {
            ["Danh sách máy ảo"] = rows
        };

        await Task.Run(() =>
        {
            MiniExcel.SaveAs(destinationPath, workbook, overwriteFile: true);
        });
    }

    public async Task ExportVmListAsync(string destinationPath, IEnumerable<VirtualMachineEntry> entries)
    {
        var rows = new List<Dictionary<string, object>>
        {
            // Dòng 1: Tiêu đề tổng quát (bỏ qua khi import)
            new()
            {
                ["A"] = "DANH SÁCH QUẢN LÝ MÁY ẢO",
                ["B"] = string.Empty,
                ["C"] = string.Empty,
                ["D"] = string.Empty,
                ["E"] = string.Empty,
                ["F"] = string.Empty,
                ["G"] = string.Empty,
                ["H"] = string.Empty,
                ["I"] = string.Empty,
                ["J"] = string.Empty,
                ["K"] = string.Empty,
                ["L"] = string.Empty,
                ["M"] = string.Empty,
                ["N"] = string.Empty,
                ["O"] = string.Empty
            },
            // Dòng 2: Tiêu đề các cột
            new()
            {
                ["A"] = "No",
                ["B"] = "IP",
                ["C"] = "Tên máy",
                ["D"] = "Domain / Workgroup",
                ["E"] = "OS",
                ["F"] = "User",
                ["G"] = "Password",
                ["H"] = "TNS",
                ["I"] = "Database Name",
                ["J"] = "Version Oracle",
                ["K"] = "Người kiểm tra",
                ["L"] = "Ghi chú",
                ["M"] = "Ngày hết hạn",
                ["N"] = "DBA User",
                ["O"] = "Password"
            }
        };

        foreach (var v in entries)
        {
            rows.Add(new Dictionary<string, object>
            {
                ["A"] = v.No ?? string.Empty,
                ["B"] = v.Ip ?? string.Empty,
                ["C"] = v.MachineName ?? string.Empty,
                ["D"] = v.DomainOrWorkgroup ?? string.Empty,
                ["E"] = v.Os ?? string.Empty,
                ["F"] = v.Username ?? string.Empty,
                ["G"] = v.Password ?? string.Empty,
                ["H"] = v.Tns ?? string.Empty,
                ["I"] = v.DatabaseName ?? string.Empty,
                ["J"] = v.OracleVersion ?? string.Empty,
                ["K"] = v.Tester ?? string.Empty,
                ["L"] = v.Notes ?? string.Empty,
                ["M"] = v.ExpirationDate ?? string.Empty,
                ["N"] = v.DbaUser ?? string.Empty,
                ["O"] = v.DbaPassword ?? string.Empty
            });
        }

        var workbook = new Dictionary<string, object>
        {
            ["Danh sách máy ảo"] = rows
        };

        await Task.Run(() =>
        {
            MiniExcel.SaveAs(destinationPath, workbook, overwriteFile: true);
        });
    }

    private static string NormalizeKey(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return string.Empty;

        // Loại bỏ dấu tiếng Việt để so khớp dễ dàng
        string withoutDiacritics = RemoveDiacritics(str);

        return withoutDiacritics.Trim().ToLowerInvariant()
            .Replace("_", " ")
            .Replace("-", " ")
            .Replace("/", " ")
            .Replace(".", " ")
            .Replace(":", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC)
            .Replace("đ", "d").Replace("Đ", "D");
    }
}
