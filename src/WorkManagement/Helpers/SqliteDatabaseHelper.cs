using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace WorkManagement.Helpers;

public static class SqliteDatabaseHelper
{
    public static string DefaultAppDataDirectory
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "WorkManagement");
            Directory.CreateDirectory(appDir);
            return appDir;
        }
    }

    public static string DefaultDatabasePath => Path.Combine(DefaultAppDataDirectory, "workmanagement.db");

    /// <summary>
    /// Chuẩn hóa đường dẫn truyền vào: nếu truyền file .json (ví dụ từ legacy code hoặc unit test),
    /// trả về đường dẫn file .db tương ứng và đường dẫn file .json để kiểm tra auto-migration.
    /// </summary>
    public static (string DbPath, string? LegacyJsonPath) ResolvePaths(string? customPath, string defaultLegacyJsonPath)
    {
        if (string.IsNullOrWhiteSpace(customPath))
        {
            return (DefaultDatabasePath, defaultLegacyJsonPath);
        }

        if (customPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            string dbPath = Path.ChangeExtension(customPath, ".db");
            return (dbPath, customPath);
        }

        return (customPath, defaultLegacyJsonPath);
    }

    public static SqliteConnection OpenConnection(string dbPath)
    {
        string? dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };

        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        EnsureSchema(conn);
        return conn;
    }

    public static void EnsureSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                SelectedProfileName TEXT NOT NULL DEFAULT 'Default',
                SelectedScanProfileNamesJson TEXT NOT NULL DEFAULT '[]',
                Host TEXT NOT NULL DEFAULT '',
                Port INTEGER NOT NULL DEFAULT 6021,
                EncryptionMode TEXT NOT NULL DEFAULT 'Implicit',
                Username TEXT NOT NULL DEFAULT '',
                Password TEXT NOT NULL DEFAULT '',
                UseProxy INTEGER NOT NULL DEFAULT 1,
                ProxyHost TEXT NOT NULL DEFAULT '',
                ProxyPort INTEGER NOT NULL DEFAULT 8080,
                ProxyUser TEXT NOT NULL DEFAULT '',
                ProxyPassword TEXT NOT NULL DEFAULT '',
                RemotePathsJson TEXT NOT NULL DEFAULT '["/"]',
                RetentionDays INTEGER NOT NULL DEFAULT 1,
                ExcludePatterns TEXT NOT NULL DEFAULT '',
                IncludeExtensions TEXT NOT NULL DEFAULT '',
                MinFileSizeMb REAL NOT NULL DEFAULT 0,
                EnableBackup INTEGER NOT NULL DEFAULT 1,
                LocalBackupPath TEXT NOT NULL DEFAULT '',
                ProjectBackupPath TEXT NOT NULL DEFAULT '',
                LastDownloadDirectory TEXT NULL,
                LogFolderPath TEXT NOT NULL DEFAULT '',
                IsDarkTheme INTEGER NOT NULL DEFAULT 0,
                ThemeName TEXT NOT NULL DEFAULT '',
                MinimizeToTrayOnClose INTEGER NOT NULL DEFAULT 1,
                SmtpHost TEXT NOT NULL DEFAULT '',
                SmtpPort INTEGER NOT NULL DEFAULT 25,
                SmtpEnableSsl INTEGER NOT NULL DEFAULT 0,
                SmtpUser TEXT NOT NULL DEFAULT '',
                SmtpPassword TEXT NOT NULL DEFAULT '',
                SenderEmail TEXT NOT NULL DEFAULT '',
                RecipientEmailsJson TEXT NOT NULL DEFAULT '[]',
                AutoSendEmailOnPreview INTEGER NOT NULL DEFAULT 0,
                AiIsEnabled INTEGER NOT NULL DEFAULT 0,
                AiBaseUrl TEXT NOT NULL DEFAULT '',
                AiApiKey TEXT NOT NULL DEFAULT '',
                AiModelName TEXT NOT NULL DEFAULT '',
                AiTimeoutSeconds INTEGER NOT NULL DEFAULT 60,
                AiBypassProxy INTEGER NOT NULL DEFAULT 1,
                AiTemperature REAL NOT NULL DEFAULT 0.1
            );

            CREATE TABLE IF NOT EXISTS FtpProfiles (
                Name TEXT PRIMARY KEY COLLATE NOCASE,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Host TEXT NOT NULL DEFAULT '',
                Port INTEGER NOT NULL DEFAULT 6021,
                EncryptionMode TEXT NOT NULL DEFAULT 'Implicit',
                Username TEXT NOT NULL DEFAULT '',
                Password TEXT NOT NULL DEFAULT '',
                UseProxy INTEGER NOT NULL DEFAULT 1,
                ProxyHost TEXT NOT NULL DEFAULT '',
                ProxyPort INTEGER NOT NULL DEFAULT 8080,
                ProxyUser TEXT NOT NULL DEFAULT '',
                ProxyPassword TEXT NOT NULL DEFAULT '',
                RemotePathsJson TEXT NOT NULL DEFAULT '["/"]',
                RetentionDays INTEGER NOT NULL DEFAULT 1,
                ExcludePatterns TEXT NOT NULL DEFAULT '',
                IncludeExtensions TEXT NOT NULL DEFAULT '',
                MinFileSizeMb REAL NOT NULL DEFAULT 0,
                EnableBackup INTEGER NOT NULL DEFAULT 1,
                LocalBackupPath TEXT NOT NULL DEFAULT '',
                SmtpHost TEXT NOT NULL DEFAULT '',
                SmtpPort INTEGER NOT NULL DEFAULT 25,
                SmtpEnableSsl INTEGER NOT NULL DEFAULT 0,
                SmtpUser TEXT NOT NULL DEFAULT '',
                SmtpPassword TEXT NOT NULL DEFAULT '',
                SenderEmail TEXT NOT NULL DEFAULT '',
                RecipientEmailsJson TEXT NOT NULL DEFAULT '[]',
                AutoSendEmailOnPreview INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ProjectCleanupSettings (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                BackupRootDirectory TEXT NOT NULL DEFAULT '',
                EnableBackupFolder INTEGER NOT NULL DEFAULT 1,
                EnableBackupDatabase INTEGER NOT NULL DEFAULT 1,
                EnableDeleteFolder INTEGER NOT NULL DEFAULT 1,
                EnableDeleteUsers INTEGER NOT NULL DEFAULT 1,
                EnableDeleteTablespace INTEGER NOT NULL DEFAULT 1,
                DefaultDbaUser TEXT NOT NULL DEFAULT 'SYSTEM'
            );

            CREATE TABLE IF NOT EXISTS Projects (
                Id TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                No TEXT NOT NULL DEFAULT '',
                ProjectName TEXT NOT NULL DEFAULT '',
                Description TEXT NOT NULL DEFAULT '',
                SourceFolder TEXT NOT NULL DEFAULT '',
                TnsName TEXT NOT NULL DEFAULT '',
                HostServer TEXT NOT NULL DEFAULT '',
                ServiceName TEXT NOT NULL DEFAULT 'ORCL',
                Port TEXT NOT NULL DEFAULT '1521',
                DbUsers TEXT NOT NULL DEFAULT '',
                Tablespace TEXT NOT NULL DEFAULT '',
                DbaUsername TEXT NOT NULL DEFAULT 'SYSTEM',
                DbaPassword TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                PersonInCharge TEXT NOT NULL DEFAULT '',
                Notes2 TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (Id, IsDeleted)
            );

            CREATE TABLE IF NOT EXISTS VirtualMachines (
                Id TEXT PRIMARY KEY,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                No TEXT NOT NULL DEFAULT '',
                Ip TEXT NOT NULL DEFAULT '',
                MachineName TEXT NOT NULL DEFAULT '',
                DomainOrWorkgroup TEXT NOT NULL DEFAULT '',
                Os TEXT NOT NULL DEFAULT '',
                Username TEXT NOT NULL DEFAULT '',
                Password TEXT NOT NULL DEFAULT '',
                Tns TEXT NOT NULL DEFAULT '',
                DatabaseName TEXT NOT NULL DEFAULT '',
                OracleVersion TEXT NOT NULL DEFAULT '',
                Tester TEXT NOT NULL DEFAULT '',
                Notes TEXT NOT NULL DEFAULT '',
                ExpirationDate TEXT NOT NULL DEFAULT 'Vô hạn',
                DbaUser TEXT NOT NULL DEFAULT '',
                DbaPassword TEXT NOT NULL DEFAULT '',
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                IsPasswordMasked INTEGER NOT NULL DEFAULT 1
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public static bool IsFileSqliteDatabase(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;
            var info = new FileInfo(filePath);
            if (info.Length < 16) return false;

            Span<byte> header = stackalloc byte[15];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int read = fs.Read(header);
            if (read < 15) return false;

            return System.Text.Encoding.ASCII.GetString(header) == "SQLite format 3";
        }
        catch
        {
            return false;
        }
    }

    public static void BackupLegacyJsonFile(string? jsonFilePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jsonFilePath) || !File.Exists(jsonFilePath))
            {
                return;
            }

            if (IsFileSqliteDatabase(jsonFilePath))
            {
                return;
            }

            string bakPath = jsonFilePath + ".bak";
            File.Move(jsonFilePath, bakPath, overwrite: true);
        }
        catch
        {
            // Bỏ qua nếu file đang bị khóa hoặc không có quyền ghi
        }
    }
}
