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

public class ProjectManagerService
{
    private readonly string _dbPath;
    private readonly string? _legacyActiveJsonPath;
    private readonly string? _legacyDeletedJsonPath;
    private readonly string? _legacySettingsJsonPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProjectManagerService(string? customStoragePath = null, string? customDeletedStoragePath = null)
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        if (!string.IsNullOrWhiteSpace(customStoragePath))
        {
            _dbPath = customStoragePath;
            string dir = Path.GetDirectoryName(customStoragePath) ?? string.Empty;
            _legacyActiveJsonPath = customStoragePath;
            _legacyDeletedJsonPath = !string.IsNullOrWhiteSpace(customDeletedStoragePath)
                ? customDeletedStoragePath
                : Path.Combine(dir, "deleted_projects.json");
            _legacySettingsJsonPath = Path.Combine(dir, "project_cleanup_settings.json");
        }
        else
        {
            string appDir = SqliteDatabaseHelper.DefaultAppDataDirectory;
            _dbPath = SqliteDatabaseHelper.DefaultDatabasePath;
            _legacyActiveJsonPath = Path.Combine(appDir, "projects.json");
            _legacyDeletedJsonPath = Path.Combine(appDir, "deleted_projects.json");
            _legacySettingsJsonPath = Path.Combine(appDir, "project_cleanup_settings.json");
        }
    }

    private void TryMigrateLegacyJsonIfNeeded()
    {
        // 1. Migrate projects.json
        if (!string.IsNullOrWhiteSpace(_legacyActiveJsonPath) &&
            File.Exists(_legacyActiveJsonPath) &&
            !SqliteDatabaseHelper.IsFileSqliteDatabase(_legacyActiveJsonPath))
        {
            try
            {
                string json = File.ReadAllText(_legacyActiveJsonPath);
                var list = JsonSerializer.Deserialize<List<ProjectEntry>>(json, _jsonOptions);
                if (list != null)
                {
                    foreach (var proj in list)
                    {
                        proj.DbaPassword = SecretProtector.Unprotect(proj.DbaPassword);
                    }

                    if (!string.Equals(_dbPath, _legacyActiveJsonPath, StringComparison.OrdinalIgnoreCase))
                    {
                        SqliteDatabaseHelper.BackupLegacyJsonFile(_legacyActiveJsonPath);
                    }
                    else
                    {
                        File.Copy(_legacyActiveJsonPath, _legacyActiveJsonPath + ".bak", overwrite: true);
                        File.Delete(_legacyActiveJsonPath);
                    }

                    SaveProjectsByState(list, isDeleted: false);
                }
            }
            catch { }
        }

        // 2. Migrate deleted_projects.json
        if (!string.IsNullOrWhiteSpace(_legacyDeletedJsonPath) &&
            File.Exists(_legacyDeletedJsonPath) &&
            !SqliteDatabaseHelper.IsFileSqliteDatabase(_legacyDeletedJsonPath))
        {
            try
            {
                string json = File.ReadAllText(_legacyDeletedJsonPath);
                var list = JsonSerializer.Deserialize<List<ProjectEntry>>(json, _jsonOptions);
                if (list != null)
                {
                    foreach (var proj in list)
                    {
                        proj.DbaPassword = SecretProtector.Unprotect(proj.DbaPassword);
                    }

                    SqliteDatabaseHelper.BackupLegacyJsonFile(_legacyDeletedJsonPath);
                    SaveProjectsByState(list, isDeleted: true);
                }
            }
            catch { }
        }

        // 3. Migrate project_cleanup_settings.json
        if (!string.IsNullOrWhiteSpace(_legacySettingsJsonPath) &&
            File.Exists(_legacySettingsJsonPath) &&
            !SqliteDatabaseHelper.IsFileSqliteDatabase(_legacySettingsJsonPath))
        {
            try
            {
                string json = File.ReadAllText(_legacySettingsJsonPath);
                var settings = JsonSerializer.Deserialize<ProjectCleanupSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    SqliteDatabaseHelper.BackupLegacyJsonFile(_legacySettingsJsonPath);
                    SaveSettings(settings);
                }
            }
            catch { }
        }
    }

    public List<ProjectEntry> LoadAll()
    {
        TryMigrateLegacyJsonIfNeeded();
        return LoadProjectsByState(isDeleted: false);
    }

    public void SaveAll(IEnumerable<ProjectEntry> list)
    {
        try
        {
            if (File.Exists(_dbPath) && !SqliteDatabaseHelper.IsFileSqliteDatabase(_dbPath))
            {
                SqliteDatabaseHelper.BackupLegacyJsonFile(_dbPath);
            }
            SaveProjectsByState(list, isDeleted: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không thể lưu danh sách dự án: {ex.Message}", ex);
        }
    }

    public void ClearAll()
    {
        SaveAll(new List<ProjectEntry>());
    }

    #region Quản Lý Dự Án Đã Xóa

    public List<ProjectEntry> LoadAllDeleted()
    {
        TryMigrateLegacyJsonIfNeeded();
        return LoadProjectsByState(isDeleted: true);
    }

    public void SaveAllDeleted(IEnumerable<ProjectEntry> list)
    {
        try
        {
            if (File.Exists(_dbPath) && !SqliteDatabaseHelper.IsFileSqliteDatabase(_dbPath))
            {
                SqliteDatabaseHelper.BackupLegacyJsonFile(_dbPath);
            }
            SaveProjectsByState(list, isDeleted: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không thể lưu danh sách dự án đã xóa: {ex.Message}", ex);
        }
    }

    private List<ProjectEntry> LoadProjectsByState(bool isDeleted)
    {
        var list = new List<ProjectEntry>();
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
                    Id, No, ProjectName, Description, SourceFolder, TnsName, HostServer,
                    ServiceName, Port, DbUsers, Tablespace, DbaUsername, DbaPassword,
                    Notes, PersonInCharge, Notes2
                FROM Projects
                WHERE IsDeleted = $IsDeleted
                ORDER BY SortOrder ASC;
                """;
            cmd.Parameters.AddWithValue("$IsDeleted", isDeleted ? 1 : 0);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ProjectEntry
                {
                    Id = reader.GetString(0),
                    No = reader.GetString(1),
                    ProjectName = reader.GetString(2),
                    Description = reader.GetString(3),
                    SourceFolder = reader.GetString(4),
                    TnsName = reader.GetString(5),
                    HostServer = reader.GetString(6),
                    ServiceName = reader.GetString(7),
                    Port = reader.GetString(8),
                    DbUsers = reader.GetString(9),
                    Tablespace = reader.GetString(10),
                    DbaUsername = reader.GetString(11),
                    DbaPassword = SecretProtector.Unprotect(reader.GetString(12)),
                    Notes = reader.GetString(13),
                    PersonInCharge = reader.GetString(14),
                    Notes2 = reader.GetString(15),
                    IsSelected = false
                });
            }
        }
        catch
        {
            // Trả về danh sách rỗng nếu file DB lỗi
        }

        return list;
    }

    private void SaveProjectsByState(IEnumerable<ProjectEntry> list, bool isDeleted)
    {
        string? dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Tạo bản sao có mật khẩu DBA đã mã hóa DPAPI để lưu xuống SQLite, giữ nguyên mật khẩu giải mã trên RAM
        var encryptedList = list.Select(p =>
        {
            var copy = p.Clone();
            copy.DbaPassword = SecretProtector.Protect(p.DbaPassword);
            return copy;
        }).ToList();

        using var conn = SqliteDatabaseHelper.OpenConnection(_dbPath);
        using var tx = conn.BeginTransaction();

        using (var delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = tx;
            delCmd.CommandText = "DELETE FROM Projects WHERE IsDeleted = $IsDeleted;";
            delCmd.Parameters.AddWithValue("$IsDeleted", isDeleted ? 1 : 0);
            delCmd.ExecuteNonQuery();
        }

        for (int i = 0; i < encryptedList.Count; i++)
        {
            var p = encryptedList[i];
            using var insCmd = conn.CreateCommand();
            insCmd.Transaction = tx;
            insCmd.CommandText = """
                INSERT OR REPLACE INTO Projects (
                    Id, IsDeleted, SortOrder, No, ProjectName, Description, SourceFolder,
                    TnsName, HostServer, ServiceName, Port, DbUsers, Tablespace,
                    DbaUsername, DbaPassword, Notes, PersonInCharge, Notes2
                ) VALUES (
                    $Id, $IsDeleted, $SortOrder, $No, $ProjectName, $Description, $SourceFolder,
                    $TnsName, $HostServer, $ServiceName, $Port, $DbUsers, $Tablespace,
                    $DbaUsername, $DbaPassword, $Notes, $PersonInCharge, $Notes2
                );
                """;

            insCmd.Parameters.AddWithValue("$Id", string.IsNullOrWhiteSpace(p.Id) ? Guid.NewGuid().ToString() : p.Id);
            insCmd.Parameters.AddWithValue("$IsDeleted", isDeleted ? 1 : 0);
            insCmd.Parameters.AddWithValue("$SortOrder", i);
            insCmd.Parameters.AddWithValue("$No", p.No ?? string.Empty);
            insCmd.Parameters.AddWithValue("$ProjectName", p.ProjectName ?? string.Empty);
            insCmd.Parameters.AddWithValue("$Description", p.Description ?? string.Empty);
            insCmd.Parameters.AddWithValue("$SourceFolder", p.SourceFolder ?? string.Empty);
            insCmd.Parameters.AddWithValue("$TnsName", p.TnsName ?? string.Empty);
            insCmd.Parameters.AddWithValue("$HostServer", p.HostServer ?? string.Empty);
            insCmd.Parameters.AddWithValue("$ServiceName", p.ServiceName ?? "ORCL");
            insCmd.Parameters.AddWithValue("$Port", p.Port ?? "1521");
            insCmd.Parameters.AddWithValue("$DbUsers", p.DbUsers ?? string.Empty);
            insCmd.Parameters.AddWithValue("$Tablespace", p.Tablespace ?? string.Empty);
            insCmd.Parameters.AddWithValue("$DbaUsername", p.DbaUsername ?? "SYSTEM");
            insCmd.Parameters.AddWithValue("$DbaPassword", p.DbaPassword ?? string.Empty);
            insCmd.Parameters.AddWithValue("$Notes", p.Notes ?? string.Empty);
            insCmd.Parameters.AddWithValue("$PersonInCharge", p.PersonInCharge ?? string.Empty);
            insCmd.Parameters.AddWithValue("$Notes2", p.Notes2 ?? string.Empty);

            insCmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void MoveToDeleted(IEnumerable<ProjectEntry> projectsToMove)
    {
        var toMoveList = projectsToMove.ToList();
        if (toMoveList.Count == 0) return;

        var moveIds = new HashSet<string>(toMoveList.Select(x => x.Id));

        // 1. Loại bỏ khỏi danh sách chính
        var currentActive = LoadAll();
        currentActive.RemoveAll(x => moveIds.Contains(x.Id));
        SaveAll(currentActive);

        // 2. Thêm vào danh sách đã xóa (tránh trùng ID)
        var currentDeleted = LoadAllDeleted();
        currentDeleted.RemoveAll(x => moveIds.Contains(x.Id));

        foreach (var p in toMoveList)
        {
            p.IsSelected = false;
            currentDeleted.Insert(0, p);
        }
        SaveAllDeleted(currentDeleted);
    }

    public void RestoreProjects(IEnumerable<ProjectEntry> projectsToRestore)
    {
        var toRestoreList = projectsToRestore.ToList();
        if (toRestoreList.Count == 0) return;

        var restoreIds = new HashSet<string>(toRestoreList.Select(x => x.Id));

        // 1. Loại bỏ khỏi danh sách đã xóa
        var currentDeleted = LoadAllDeleted();
        currentDeleted.RemoveAll(x => restoreIds.Contains(x.Id));
        SaveAllDeleted(currentDeleted);

        // 2. Thêm lại vào danh sách chính
        var currentActive = LoadAll();
        currentActive.RemoveAll(x => restoreIds.Contains(x.Id));

        foreach (var p in toRestoreList)
        {
            p.IsSelected = false;
            currentActive.Add(p);
        }
        SaveAll(currentActive);
    }

    public void PermanentlyDelete(IEnumerable<string> projectIds)
    {
        var idSet = new HashSet<string>(projectIds);
        if (idSet.Count == 0) return;

        var currentDeleted = LoadAllDeleted();
        currentDeleted.RemoveAll(x => idSet.Contains(x.Id));
        SaveAllDeleted(currentDeleted);
    }

    public void PermanentlyDelete(IEnumerable<ProjectEntry> projects)
    {
        if (projects == null) return;
        PermanentlyDelete(projects.Select(p => p.Id));
    }

    public void ClearAllDeleted()
    {
        SaveAllDeleted(new List<ProjectEntry>());
    }

    #endregion

    public ProjectCleanupSettings LoadSettings()
    {
        TryMigrateLegacyJsonIfNeeded();

        try
        {
            if (File.Exists(_dbPath) && SqliteDatabaseHelper.IsFileSqliteDatabase(_dbPath))
            {
                using var conn = SqliteDatabaseHelper.OpenConnection(_dbPath);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT
                        BackupRootDirectory, EnableBackupFolder, EnableBackupDatabase,
                        EnableDeleteFolder, EnableDeleteUsers, EnableDeleteTablespace, DefaultDbaUser
                    FROM ProjectCleanupSettings
                    WHERE Id = 1;
                    """;

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new ProjectCleanupSettings
                    {
                        BackupRootDirectory = reader.GetString(0),
                        EnableBackupFolder = reader.GetInt32(1) != 0,
                        EnableBackupDatabase = reader.GetInt32(2) != 0,
                        EnableDeleteFolder = reader.GetInt32(3) != 0,
                        EnableDeleteUsers = reader.GetInt32(4) != 0,
                        EnableDeleteTablespace = reader.GetInt32(5) != 0,
                        DefaultDbaUser = reader.GetString(6)
                    };
                }
            }
        }
        catch { }

        var def = new ProjectCleanupSettings();
        SaveSettings(def);
        return def;
    }

    public void SaveSettings(ProjectCleanupSettings settings)
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

            using var conn = SqliteDatabaseHelper.OpenConnection(_dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO ProjectCleanupSettings (
                    Id, BackupRootDirectory, EnableBackupFolder, EnableBackupDatabase,
                    EnableDeleteFolder, EnableDeleteUsers, EnableDeleteTablespace, DefaultDbaUser
                ) VALUES (
                    1, $BackupRootDirectory, $EnableBackupFolder, $EnableBackupDatabase,
                    $EnableDeleteFolder, $EnableDeleteUsers, $EnableDeleteTablespace, $DefaultDbaUser
                );
                """;

            cmd.Parameters.AddWithValue("$BackupRootDirectory", settings.BackupRootDirectory ?? string.Empty);
            cmd.Parameters.AddWithValue("$EnableBackupFolder", settings.EnableBackupFolder ? 1 : 0);
            cmd.Parameters.AddWithValue("$EnableBackupDatabase", settings.EnableBackupDatabase ? 1 : 0);
            cmd.Parameters.AddWithValue("$EnableDeleteFolder", settings.EnableDeleteFolder ? 1 : 0);
            cmd.Parameters.AddWithValue("$EnableDeleteUsers", settings.EnableDeleteUsers ? 1 : 0);
            cmd.Parameters.AddWithValue("$EnableDeleteTablespace", settings.EnableDeleteTablespace ? 1 : 0);
            cmd.Parameters.AddWithValue("$DefaultDbaUser", settings.DefaultDbaUser ?? "SYSTEM");

            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    /// <summary>
    /// Sinh tên dự án mới dạng [Tên gốc] - Copy, tự động tăng số nếu tên đã tồn tại
    /// </summary>
    public static string GenerateUniqueCloneName(string originalName, IEnumerable<string?> existingNames)
    {
        string baseName = (originalName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(baseName)) baseName = "Project";

        string candidate = $"{baseName} - Copy";
        var existingSet = new HashSet<string>(
            existingNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (!existingSet.Contains(candidate))
        {
            return candidate;
        }

        int counter = 2;
        while (existingSet.Contains($"{baseName} - Copy ({counter})"))
        {
            counter++;
        }
        return $"{baseName} - Copy ({counter})";
    }

    /// <summary>
    /// Tạo một bản sao dự án mới với ID duy nhất và trạng thái chưa chọn
    /// </summary>
    public static ProjectEntry CreateClone(ProjectEntry source, string? newProjectName = null, string? newNo = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.CloneAsNew(newProjectName, newNo);
    }
}
