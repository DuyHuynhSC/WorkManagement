using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkManagement.Helpers;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class VmManagerService
{
    private readonly string _dbPath;
    private readonly string? _legacyJsonPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public VmManagerService(string? customStoragePath = null)
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        if (!string.IsNullOrWhiteSpace(customStoragePath))
        {
            _dbPath = customStoragePath;
            _legacyJsonPath = customStoragePath;
        }
        else
        {
            string appDir = SqliteDatabaseHelper.DefaultAppDataDirectory;
            _dbPath = SqliteDatabaseHelper.DefaultDatabasePath;
            _legacyJsonPath = Path.Combine(appDir, "virtual_machines.json");
        }
    }

    private void TryMigrateLegacyJsonIfNeeded()
    {
        if (string.IsNullOrWhiteSpace(_legacyJsonPath) ||
            !File.Exists(_legacyJsonPath) ||
            SqliteDatabaseHelper.IsFileSqliteDatabase(_legacyJsonPath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(_legacyJsonPath);
            var list = JsonSerializer.Deserialize<List<VirtualMachineEntry>>(json, _jsonOptions);
            if (list != null)
            {
                foreach (var vm in list)
                {
                    vm.Password = SecretProtector.Unprotect(vm.Password);
                    vm.DbaPassword = SecretProtector.Unprotect(vm.DbaPassword);
                }

                if (!string.Equals(_dbPath, _legacyJsonPath, StringComparison.OrdinalIgnoreCase))
                {
                    SqliteDatabaseHelper.BackupLegacyJsonFile(_legacyJsonPath);
                }
                else
                {
                    File.Copy(_legacyJsonPath, _legacyJsonPath + ".bak", overwrite: true);
                    File.Delete(_legacyJsonPath);
                }

                SaveAll(list);
            }
        }
        catch { }
    }

    public List<VirtualMachineEntry> LoadAll()
    {
        TryMigrateLegacyJsonIfNeeded();

        var list = new List<VirtualMachineEntry>();
        try
        {
            if (!File.Exists(_dbPath) || !SqliteDatabaseHelper.IsFileSqliteDatabase(_dbPath))
            {
                return list;
            }

            using var conn = SqliteDatabaseHelper.OpenConnection(_dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    Id, No, Ip, MachineName, DomainOrWorkgroup, Os, Username, Password,
                    Tns, DatabaseName, OracleVersion, Tester, Notes, ExpirationDate,
                    DbaUser, DbaPassword, IsFavorite, IsPasswordMasked
                FROM VirtualMachines
                ORDER BY SortOrder ASC;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new VirtualMachineEntry
                {
                    Id = reader.GetString(0),
                    No = reader.GetString(1),
                    Ip = reader.GetString(2),
                    MachineName = reader.GetString(3),
                    DomainOrWorkgroup = reader.GetString(4),
                    Os = reader.GetString(5),
                    Username = reader.GetString(6),
                    Password = SecretProtector.Unprotect(reader.GetString(7)),
                    Tns = reader.GetString(8),
                    DatabaseName = reader.GetString(9),
                    OracleVersion = reader.GetString(10),
                    Tester = reader.GetString(11),
                    Notes = reader.GetString(12),
                    ExpirationDate = reader.GetString(13),
                    DbaUser = reader.GetString(14),
                    DbaPassword = SecretProtector.Unprotect(reader.GetString(15)),
                    IsFavorite = reader.GetInt32(16) != 0,
                    IsPasswordMasked = reader.GetInt32(17) != 0,
                    IsSelected = false
                });
            }
        }
        catch
        {
            // Trả về danh sách rỗng nếu file bị lỗi hoặc không thể đọc
        }

        return list;
    }

    public void ClearAll()
    {
        SaveAll(new List<VirtualMachineEntry>());
    }

    public void SaveAll(IEnumerable<VirtualMachineEntry> list)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(_dbPath) && !SqliteDatabaseHelper.IsFileSqliteDatabase(_dbPath))
            {
                SqliteDatabaseHelper.BackupLegacyJsonFile(_dbPath);
            }

            // Tạo bản sao có mật khẩu đã mã hóa DPAPI để lưu xuống SQLite, giữ nguyên mật khẩu giải mã trên RAM
            var encryptedList = list.Select(vm =>
            {
                var copy = vm.Clone();
                copy.Password = SecretProtector.Protect(vm.Password);
                copy.DbaPassword = SecretProtector.Protect(vm.DbaPassword);
                return copy;
            }).ToList();

            using var conn = SqliteDatabaseHelper.OpenConnection(_dbPath);
            using var tx = conn.BeginTransaction();

            using (var delCmd = conn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM VirtualMachines;";
                delCmd.ExecuteNonQuery();
            }

            for (int i = 0; i < encryptedList.Count; i++)
            {
                var vm = encryptedList[i];
                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = """
                    INSERT OR REPLACE INTO VirtualMachines (
                        Id, SortOrder, No, Ip, MachineName, DomainOrWorkgroup, Os,
                        Username, Password, Tns, DatabaseName, OracleVersion,
                        Tester, Notes, ExpirationDate, DbaUser, DbaPassword,
                        IsFavorite, IsPasswordMasked
                    ) VALUES (
                        $Id, $SortOrder, $No, $Ip, $MachineName, $DomainOrWorkgroup, $Os,
                        $Username, $Password, $Tns, $DatabaseName, $OracleVersion,
                        $Tester, $Notes, $ExpirationDate, $DbaUser, $DbaPassword,
                        $IsFavorite, $IsPasswordMasked
                    );
                    """;

                insCmd.Parameters.AddWithValue("$Id", string.IsNullOrWhiteSpace(vm.Id) ? Guid.NewGuid().ToString() : vm.Id);
                insCmd.Parameters.AddWithValue("$SortOrder", i);
                insCmd.Parameters.AddWithValue("$No", vm.No ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Ip", vm.Ip ?? string.Empty);
                insCmd.Parameters.AddWithValue("$MachineName", vm.MachineName ?? string.Empty);
                insCmd.Parameters.AddWithValue("$DomainOrWorkgroup", vm.DomainOrWorkgroup ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Os", vm.Os ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Username", vm.Username ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Password", vm.Password ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Tns", vm.Tns ?? string.Empty);
                insCmd.Parameters.AddWithValue("$DatabaseName", vm.DatabaseName ?? string.Empty);
                insCmd.Parameters.AddWithValue("$OracleVersion", vm.OracleVersion ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Tester", vm.Tester ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Notes", vm.Notes ?? string.Empty);
                insCmd.Parameters.AddWithValue("$ExpirationDate", vm.ExpirationDate ?? "Vô hạn");
                insCmd.Parameters.AddWithValue("$DbaUser", vm.DbaUser ?? string.Empty);
                insCmd.Parameters.AddWithValue("$DbaPassword", vm.DbaPassword ?? string.Empty);
                insCmd.Parameters.AddWithValue("$IsFavorite", vm.IsFavorite ? 1 : 0);
                insCmd.Parameters.AddWithValue("$IsPasswordMasked", vm.IsPasswordMasked ? 1 : 0);

                insCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không thể lưu dữ liệu máy ảo: {ex.Message}", ex);
        }
    }
}
