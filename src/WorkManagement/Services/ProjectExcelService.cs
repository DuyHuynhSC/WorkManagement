using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MiniExcelLibs;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class ProjectExcelService
{
    public Task<List<ProjectEntry>> ReadProjectsFromExcelAsync(string filePath)
    {
        return Task.Run(() =>
        {
            var result = new List<ProjectEntry>();
            if (!File.Exists(filePath)) return result;

            List<IDictionary<string, object>> rawRows;
            try
            {
                rawRows = MiniExcel.Query(filePath, useHeaderRow: false)
                    .OfType<IDictionary<string, object>>()
                    .ToList();
            }
            catch
            {
                return result;
            }

            if (rawRows.Count < 3)
            {
                // Thử đọc theo kiểu có header row nếu file quá ngắn
                return FallbackReadWithHeader(filePath);
            }

            // Theo yêu cầu: Bỏ qua 2 dòng đầu tiên (index 0, 1). Dòng thứ 3 (index 2) là Title.
            // Dữ liệu import từ dòng thứ 4 (index 3 trở đi).
            int titleRowIndex = -1;
            var colMapping = new Dictionary<string, string>(); // colKey ("A", "B") -> Property Name

            // 1. Ưu tiên kiểm tra dòng thứ 3 (index 2)
            if (rawRows.Count >= 3)
            {
                var mappingRow3 = DetectProjectHeader(rawRows[2]);
                if (mappingRow3.Count >= 2)
                {
                    titleRowIndex = 2;
                    colMapping = mappingRow3;
                }
            }

            // 2. Dự phòng tương thích ngược: nếu dòng 3 không phải header, quét các dòng khác từ dòng 0 đến 5
            if (titleRowIndex == -1)
            {
                int limit = Math.Min(5, rawRows.Count);
                for (int i = 0; i < limit; i++)
                {
                    var m = DetectProjectHeader(rawRows[i]);
                    if (m.Count >= 2 && m.Count > colMapping.Count)
                    {
                        titleRowIndex = i;
                        colMapping = m;
                    }
                }
            }

            if (titleRowIndex == -1 || colMapping.Count == 0)
            {
                return FallbackReadWithHeader(filePath);
            }

            return ExtractProjectEntries(rawRows, titleRowIndex, colMapping);
        });
    }

    public async Task<(List<ProjectEntry> Entries, AiSchemaMappingResult AiResult)> ReadProjectsWithAiAsync(
        string filePath,
        AiSettings aiSettings,
        OpenAiClientService? aiClient = null)
    {
        var aiService = aiClient ?? new OpenAiClientService();
        var entries = new List<ProjectEntry>();

        if (!File.Exists(filePath))
        {
            return (entries, new AiSchemaMappingResult { Success = false, ErrorMessage = "Không tìm thấy file Excel." });
        }

        List<IDictionary<string, object>> rawRows;
        try
        {
            rawRows = MiniExcel.Query(filePath, useHeaderRow: false)
                .OfType<IDictionary<string, object>>()
                .ToList();
        }
        catch (Exception ex)
        {
            return (entries, new AiSchemaMappingResult { Success = false, ErrorMessage = $"Lỗi khi đọc file Excel: {ex.Message}" });
        }

        if (rawRows.Count == 0)
        {
            return (entries, new AiSchemaMappingResult { Success = false, ErrorMessage = "File Excel không có dữ liệu." });
        }

        int candidateHeaderIdx = 2;
        if (rawRows.Count < 3) candidateHeaderIdx = 0;

        int maxCells = 0;
        int checkLimit = Math.Min(5, rawRows.Count);
        for (int i = 0; i < checkLimit; i++)
        {
            int nonNullCount = rawRows[i].Values.Count(v => v != null && !string.IsNullOrWhiteSpace(v.ToString()));
            if (nonNullCount > maxCells)
            {
                maxCells = nonNullCount;
                candidateHeaderIdx = i;
            }
        }

        var candidateHeaders = new Dictionary<string, string>();
        foreach (var kv in rawRows[candidateHeaderIdx])
        {
            if (kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.ToString()))
            {
                candidateHeaders[kv.Key] = kv.Value.ToString()!.Trim();
            }
        }

        var sampleRows = new List<Dictionary<string, string>>();
        int sampleLimit = Math.Min(rawRows.Count, candidateHeaderIdx + 4);
        for (int i = candidateHeaderIdx + 1; i < sampleLimit; i++)
        {
            var rowDict = new Dictionary<string, string>();
            foreach (var kv in rawRows[i])
            {
                if (kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.ToString()))
                {
                    rowDict[kv.Key] = kv.Value.ToString()!.Trim();
                }
            }
            if (rowDict.Count > 0)
            {
                sampleRows.Add(rowDict);
            }
        }

        var aiResult = await aiService.InferProjectSchemaMappingAsync(aiSettings, candidateHeaders, sampleRows);
        if (aiResult.Success && aiResult.ColumnMapping.Count > 0)
        {
            int titleRowIndex = aiResult.HeaderRowIndex >= 0 ? aiResult.HeaderRowIndex : candidateHeaderIdx;
            entries = ExtractProjectEntries(rawRows, titleRowIndex, aiResult.ColumnMapping);
        }

        return (entries, aiResult);
    }

    public static List<ProjectEntry> ExtractProjectEntries(
        List<IDictionary<string, object>> rawRows,
        int titleRowIndex,
        Dictionary<string, string> colMapping)
    {
        var result = new List<ProjectEntry>();
        int startDataRow = titleRowIndex + 1;
        for (int i = startDataRow; i < rawRows.Count; i++)
        {
            var row = rawRows[i];
            if (row.Values.All(v => v == null || string.IsNullOrWhiteSpace(v.ToString())))
            {
                continue;
            }

            var entry = new ProjectEntry();

            foreach (var mapping in colMapping)
            {
                string colKey = mapping.Key.ToUpperInvariant();
                string propName = mapping.Value;

                object? rawVal = null;
                if (row.TryGetValue(colKey, out var v1)) rawVal = v1;
                else if (row.TryGetValue(mapping.Key, out var v2)) rawVal = v2;

                if (rawVal == null) continue;
                string valStr = rawVal.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(valStr)) continue;

                switch (propName)
                {
                    case nameof(ProjectEntry.No):
                        entry.No = valStr;
                        break;
                    case nameof(ProjectEntry.ProjectName):
                        entry.ProjectName = valStr;
                        break;
                    case nameof(ProjectEntry.Description):
                        entry.Description = valStr;
                        break;
                    case nameof(ProjectEntry.SourceFolder):
                        entry.SourceFolder = valStr;
                        break;
                    case nameof(ProjectEntry.TnsName):
                        entry.TnsName = valStr;
                        break;
                    case nameof(ProjectEntry.HostServer):
                        entry.HostServer = valStr;
                        break;
                    case nameof(ProjectEntry.ServiceName):
                        entry.ServiceName = valStr;
                        break;
                    case nameof(ProjectEntry.Port):
                        entry.Port = valStr;
                        break;
                    case nameof(ProjectEntry.DbUsers):
                        entry.DbUsers = valStr;
                        break;
                    case nameof(ProjectEntry.Tablespace):
                        entry.Tablespace = valStr;
                        break;
                    case nameof(ProjectEntry.DbaUsername):
                        entry.DbaUsername = valStr;
                        break;
                    case nameof(ProjectEntry.DbaPassword):
                        entry.DbaPassword = valStr;
                        break;
                    case nameof(ProjectEntry.PersonInCharge):
                        entry.PersonInCharge = valStr;
                        break;
                    case nameof(ProjectEntry.Notes):
                        entry.Notes = valStr;
                        break;
                    case nameof(ProjectEntry.Notes2):
                        entry.Notes2 = valStr;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(entry.ServiceName)) entry.ServiceName = "ORCL";
            if (string.IsNullOrWhiteSpace(entry.Port)) entry.Port = "1521";
            if (string.IsNullOrWhiteSpace(entry.DbaUsername)) entry.DbaUsername = "SYSTEM";

            // Chỉ nhận dòng nếu có thông tin dự án tối thiểu (Tên dự án, Source folder hoặc Host)
            if (!string.IsNullOrWhiteSpace(entry.ProjectName) || !string.IsNullOrWhiteSpace(entry.SourceFolder) || !string.IsNullOrWhiteSpace(entry.HostServer))
            {
                result.Add(entry);
            }
        }
        return result;
    }

    private static Dictionary<string, string> DetectProjectHeader(IDictionary<string, object> row)
    {
        var mapping = new Dictionary<string, string>();
        foreach (var kv in row)
        {
            if (kv.Value == null) continue;
            string text = kv.Value.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) continue;

            string norm = NormalizeKey(text);
            string? prop = MatchProjectProperty(norm);
            if (prop != null && !mapping.ContainsValue(prop))
            {
                mapping[kv.Key] = prop;
            }
        }
        return mapping;
    }

    private static string? MatchProjectProperty(string norm)
    {
        if (norm.Contains("ghi chu 2") || norm.Contains("notes 2")) return nameof(ProjectEntry.Notes2);
        if (norm == "no" || norm == "stt" || norm == "ma") return nameof(ProjectEntry.No);
        if (norm == "project" || norm.Contains("ten du an") || norm == "du an" || norm == "project name" || norm == "projectname") return nameof(ProjectEntry.ProjectName);
        if (norm.Contains("giai thich") || norm.Contains("description") || norm == "mo ta" || norm.Contains("version")) return nameof(ProjectEntry.Description);
        if (norm.Contains("source") || norm.Contains("thu muc") || norm.Contains("folder")) return nameof(ProjectEntry.SourceFolder);
        if (norm.Contains("tns")) return nameof(ProjectEntry.TnsName);
        if (norm.Contains("host") || norm == "ip" || norm.Contains("may ao") || norm.Contains("server")) return nameof(ProjectEntry.HostServer);
        if (norm.Contains("service") || norm == "sid" || norm.Contains("database name")) return nameof(ProjectEntry.ServiceName);
        if (norm == "port" || norm == "cong") return nameof(ProjectEntry.Port);
        if (norm.Contains("user") && (norm.Contains("db") || norm.Contains("nguoi dung") || norm == "user")) return nameof(ProjectEntry.DbUsers);
        if (norm.Contains("tablespace")) return nameof(ProjectEntry.Tablespace);
        if (norm.Contains("dba user") || norm.Contains("tai khoan dba") || norm.Contains("admin user") || norm == "dba") return nameof(ProjectEntry.DbaUsername);
        if (norm.Contains("dba password") || norm.Contains("mat khau dba") || norm.Contains("dba pass")) return nameof(ProjectEntry.DbaPassword);
        if (norm.Contains("nguoi phu trach") || norm.Contains("person in charge") || norm.Contains("tester") || norm.Contains("pic") || norm.Contains("nguoi kt")) return nameof(ProjectEntry.PersonInCharge);
        if (norm.Contains("ghi chu") || norm.Contains("notes")) return nameof(ProjectEntry.Notes);

        return null;
    }

    private static string NormalizeKey(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return string.Empty;
        var normalizedString = str.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalizedString)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC)
            .Replace("đ", "d").Replace("Đ", "D")
            .Trim().ToLowerInvariant()
            .Replace("_", " ").Replace("-", " ").Replace("/", " ").Replace(".", " ").Replace(":", " ");
    }

    private static List<ProjectEntry> FallbackReadWithHeader(string filePath)
    {
        var result = new List<ProjectEntry>();
        try
        {
            using var stream = File.OpenRead(filePath);
            var rows = stream.Query(useHeaderRow: true).ToList();

            foreach (IDictionary<string, object> row in rows)
            {
                if (row == null || row.Count == 0) continue;

                string GetVal(params string[] columnKeys)
                {
                    foreach (var key in columnKeys)
                    {
                        var match = row.FirstOrDefault(kvp =>
                            kvp.Key != null &&
                            kvp.Key.Trim().Equals(key, StringComparison.OrdinalIgnoreCase));
                        if (match.Key != null && match.Value != null)
                        {
                            string s = match.Value.ToString()?.Trim() ?? string.Empty;
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }

                    foreach (var key in columnKeys)
                    {
                        var match = row.FirstOrDefault(kvp =>
                            kvp.Key != null &&
                            kvp.Key.Contains(key, StringComparison.OrdinalIgnoreCase));
                        if (match.Key != null && match.Value != null)
                        {
                            string s = match.Value.ToString()?.Trim() ?? string.Empty;
                            if (!string.IsNullOrEmpty(s)) return s;
                        }
                    }

                    return string.Empty;
                }

                string projectName = GetVal("Project", "Tên Dự Án", "TenDuAn", "ProjectName");
                string sourceFolder = GetVal("Source Folder", "SourceFolder", "Thư mục source", "Folder");
                string host = GetVal("Host Server", "HostServer", "Host", "IP", "Máy ảo");

                if (string.IsNullOrWhiteSpace(projectName) && string.IsNullOrWhiteSpace(sourceFolder) && string.IsNullOrWhiteSpace(host))
                {
                    continue;
                }

                result.Add(new ProjectEntry
                {
                    No = GetVal("No", "STT", "Mã"),
                    ProjectName = projectName,
                    Description = GetVal("Giải thích thêm (Version - Mục đích)", "Giải thích thêm", "Description", "Mô tả", "Version"),
                    SourceFolder = sourceFolder,
                    TnsName = GetVal("tnsname (tham khảo file:", "tnsname", "TNS", "TnsName"),
                    HostServer = host,
                    ServiceName = string.IsNullOrWhiteSpace(GetVal("Service Name", "ServiceName", "Service", "SID", "Database Name")) ? "ORCL" : GetVal("Service Name", "ServiceName", "Service", "SID", "Database Name"),
                    Port = string.IsNullOrWhiteSpace(GetVal("Port", "Cổng")) ? "1521" : GetVal("Port", "Cổng"),
                    DbUsers = GetVal("User", "DB User", "DB Users", "Người dùng"),
                    Tablespace = GetVal("Tablespace", "Tên Tablespace", "Tablespace Name"),
                    DbaUsername = string.IsNullOrWhiteSpace(GetVal("DBA User", "Tài khoản DBA", "Admin User")) ? "SYSTEM" : GetVal("DBA User", "Tài khoản DBA", "Admin User"),
                    DbaPassword = GetVal("DBA Password", "Mật khẩu DBA", "DBA Pass"),
                    Notes = GetVal("Ghi chú", "Notes", "GhiChu"),
                    PersonInCharge = GetVal("Người phụ trách", "Person In Charge", "Tester", "Người KT", "PIC"),
                    Notes2 = GetVal("Ghi chú 2", "Notes 2", "GhiChu2")
                });
            }
        }
        catch { }

        return result;
    }

    public Task ExportTemplateAsync(string destinationPath)
    {
        return Task.Run(() =>
        {
            var rows = new List<Dictionary<string, object>>
            {
                // Dòng 1: Tiêu đề danh mục (Bỏ qua khi import)
                new()
                {
                    ["A"] = "DANH MỤC THÔNG TIN DỰ ÁN (DÒNG 1 NÀY SẼ BỎ QUA KHI IMPORT)",
                    ["B"] = "", ["C"] = "", ["D"] = "", ["E"] = "", ["F"] = "",
                    ["G"] = "", ["H"] = "", ["I"] = "", ["J"] = "", ["K"] = "",
                    ["L"] = "", ["M"] = "", ["N"] = ""
                },
                // Dòng 2: Hướng dẫn / phiên bản (Bỏ qua khi import)
                new()
                {
                    ["A"] = "DÒNG 2: THÔNG TIN PHIÊN BẢN (HỆ THỐNG CŨNG SẼ BỎ QUA DÒNG NÀY)",
                    ["B"] = "", ["C"] = "", ["D"] = "", ["E"] = "", ["F"] = "",
                    ["G"] = "", ["H"] = "", ["I"] = "", ["J"] = "", ["K"] = "",
                    ["L"] = "", ["M"] = "", ["N"] = ""
                },
                // Dòng 3: Title các item
                new()
                {
                    ["A"] = "No",
                    ["B"] = "Project",
                    ["C"] = "Giải thích thêm (Version - Mục đích)",
                    ["D"] = "Source Folder",
                    ["E"] = "tnsname (tham khảo file:",
                    ["F"] = "Host Server",
                    ["G"] = "Service Name",
                    ["H"] = "Port",
                    ["I"] = "User",
                    ["J"] = "Tablespace",
                    ["K"] = "DBA User",
                    ["L"] = "Ghi chú",
                    ["M"] = "Người phụ trách",
                    ["N"] = "Ghi chú 2"
                },
                // Dòng 4: Dữ liệu mẫu 1
                new()
                {
                    ["A"] = "316",
                    ["B"] = "UTSUGI",
                    ["C"] = "Customize V10",
                    ["D"] = @"Q:\V10_UTG",
                    ["E"] = "ORCL19F",
                    ["F"] = "172.16.6.23",
                    ["G"] = "ORCL",
                    ["H"] = "1521",
                    ["I"] = "UTG_RPS, UTG_USR1, UTG_USR9, UTG_LOG",
                    ["J"] = "TS_UTSUGI",
                    ["K"] = "SYSTEM",
                    ["L"] = "2026/06/23 Tạo mới",
                    ["M"] = "Duy",
                    ["N"] = ""
                },
                // Dòng 5: Dữ liệu mẫu 2
                new()
                {
                    ["A"] = "324",
                    ["B"] = "CUORE",
                    ["C"] = "Customize V10",
                    ["D"] = @"Q:\V10_CUORE",
                    ["E"] = "ORCL19F",
                    ["F"] = "172.16.6.23",
                    ["G"] = "ORCL",
                    ["H"] = "1521",
                    ["I"] = "COR_RPS, COR_USR1, COR_USR9, COR_LOG",
                    ["J"] = "TS_CUORE",
                    ["K"] = "SYSTEM",
                    ["L"] = "2026/07/14 Tạo mới",
                    ["M"] = "Duy",
                    ["N"] = ""
                }
            };

            var workbook = new Dictionary<string, object>
            {
                ["Danh mục dự án"] = rows
            };

            MiniExcel.SaveAs(destinationPath, workbook, overwriteFile: true);
        });
    }

    public Task ExportProjectsToExcelAsync(string destinationPath, IEnumerable<ProjectEntry> projects)
    {
        return Task.Run(() =>
        {
            var rows = projects.Select(p => new Dictionary<string, object>
            {
                ["No"] = p.No ?? string.Empty,
                ["Project"] = p.ProjectName ?? string.Empty,
                ["Giải thích thêm (Version - Mục đích)"] = p.Description ?? string.Empty,
                ["Source Folder"] = p.SourceFolder ?? string.Empty,
                ["tnsname (tham khảo file:"] = p.TnsName ?? string.Empty,
                ["Host Server"] = p.HostServer ?? string.Empty,
                ["Service Name"] = p.ServiceName ?? string.Empty,
                ["Port"] = p.Port ?? string.Empty,
                ["User"] = p.DbUsers ?? string.Empty,
                ["Tablespace"] = p.Tablespace ?? string.Empty,
                ["DBA User"] = p.DbaUsername ?? string.Empty,
                ["Ghi chú"] = p.Notes ?? string.Empty,
                ["Người phụ trách"] = p.PersonInCharge ?? string.Empty,
                ["Ghi chú 2"] = p.Notes2 ?? string.Empty
            }).ToList();

            MiniExcel.SaveAs(destinationPath, rows, overwriteFile: true);
        });
    }
}
