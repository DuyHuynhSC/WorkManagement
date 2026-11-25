using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorkManagement.Helpers;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string DefaultConfigPath => SqliteDatabaseHelper.DefaultDatabasePath;
    public string LegacyJsonConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    private (string DbPath, string? LegacyJsonPath) ResolveTargetPaths(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return (DefaultConfigPath, LegacyJsonConfigPath);
        }

        if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(filePath) &&
            !SqliteDatabaseHelper.IsFileSqliteDatabase(filePath))
        {
            return (filePath, filePath);
        }

        return (filePath, null);
    }

    public FtpSettings Load(string? filePath = null)
    {
        var (dbPath, legacyJsonPath) = ResolveTargetPaths(filePath);

        // 1. Nếu chỉ định đường dẫn trực tiếp tới một file JSON legacy (hoặc file mặc định chưa có DB nhưng có config.json cũ)
        if (!string.IsNullOrWhiteSpace(legacyJsonPath) &&
            File.Exists(legacyJsonPath) &&
            !SqliteDatabaseHelper.IsFileSqliteDatabase(legacyJsonPath))
        {
            bool shouldMigrateFromLegacy = string.Equals(dbPath, legacyJsonPath, StringComparison.OrdinalIgnoreCase) ||
                                           !HasAppSettingsInSqlite(dbPath);
            if (shouldMigrateFromLegacy)
            {
                try
                {
                    string json = File.ReadAllText(legacyJsonPath);
                    var legacySettings = JsonSerializer.Deserialize<FtpSettings>(json, JsonOptions) ?? new FtpSettings();
                    UnprotectSettingsSecrets(legacySettings);
                    EnsureProfilesInitialized(legacySettings);

                    // Nếu đường dẫn mặc định (dbPath != legacyJsonPath), đổi tên config.json -> config.json.bak
                    if (!string.Equals(dbPath, legacyJsonPath, StringComparison.OrdinalIgnoreCase))
                    {
                        SqliteDatabaseHelper.BackupLegacyJsonFile(legacyJsonPath);
                    }
                    else
                    {
                        // Nếu truyền trực tiếp đường dẫn file .json (ví dụ trong test), tạo bản .json.bak rồi ghi đè file đó bằng SQLite
                        string bakPath = legacyJsonPath + ".bak";
                        File.Copy(legacyJsonPath, bakPath, overwrite: true);
                        File.Delete(legacyJsonPath);
                    }

                    SaveToSqlite(legacySettings, dbPath);
                    return legacySettings;
                }
                catch
                {
                    // Fallback xuống khởi tạo SQLite mới nếu file JSON lỗi
                }
            }
        }

        if (!File.Exists(dbPath) || !HasAppSettingsInSqlite(dbPath))
        {
            var defaultSettings = new FtpSettings();
            EnsureProfilesInitialized(defaultSettings);
            Save(defaultSettings, dbPath);
            return defaultSettings;
        }

        var settings = LoadFromSqlite(dbPath);
        UnprotectSettingsSecrets(settings);
        EnsureProfilesInitialized(settings);
        return settings;
    }

    public void Save(FtpSettings settings, string? filePath = null)
    {
        string targetDbPath = string.IsNullOrWhiteSpace(filePath) ? DefaultConfigPath : filePath;
        string? dir = Path.GetDirectoryName(targetDbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Nếu file đích đang là file text JSON cũ chưa migrate, tạo bản sao .bak và xóa trước khi mở SQLite
        if (File.Exists(targetDbPath) && !SqliteDatabaseHelper.IsFileSqliteDatabase(targetDbPath))
        {
            SqliteDatabaseHelper.BackupLegacyJsonFile(targetDbPath);
        }

        UpsertProfile(settings, settings.SelectedProfileName);
        SyncEmailSettingsAcrossAllProfiles(settings);
        SyncFolderSettingsAcrossAllProfiles(settings);
        NormalizeThemeSettings(settings);

        SaveToSqlite(settings, targetDbPath);
    }

    private static bool HasAppSettingsInSqlite(string dbPath)
    {
        try
        {
            if (!File.Exists(dbPath) || !SqliteDatabaseHelper.IsFileSqliteDatabase(dbPath))
            {
                return false;
            }

            using var conn = SqliteDatabaseHelper.OpenConnection(dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM AppSettings WHERE Id = 1;";
            long count = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static FtpSettings LoadFromSqlite(string dbPath)
    {
        var settings = new FtpSettings();
        using var conn = SqliteDatabaseHelper.OpenConnection(dbPath);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    SelectedProfileName, SelectedScanProfileNamesJson, Host, Port, EncryptionMode,
                    Username, Password, UseProxy, ProxyHost, ProxyPort, ProxyUser, ProxyPassword,
                    RemotePathsJson, RetentionDays, ExcludePatterns, IncludeExtensions, MinFileSizeMb,
                    EnableBackup, LocalBackupPath, ProjectBackupPath, LastDownloadDirectory, LogFolderPath,
                    IsDarkTheme, ThemeName, MinimizeToTrayOnClose,
                    SmtpHost, SmtpPort, SmtpEnableSsl, SmtpUser, SmtpPassword, SenderEmail,
                    RecipientEmailsJson, AutoSendEmailOnPreview,
                    AiIsEnabled, AiBaseUrl, AiApiKey, AiModelName, AiTimeoutSeconds, AiBypassProxy, AiTemperature
                FROM AppSettings
                WHERE Id = 1;
                """;

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                settings.SelectedProfileName = reader.GetString(0);
                settings.SelectedScanProfileNames = DeserializeStringList(reader.GetString(1));
                settings.Host = reader.GetString(2);
                settings.Port = reader.GetInt32(3);
                settings.EncryptionMode = reader.GetString(4);
                settings.Username = reader.GetString(5);
                settings.Password = reader.GetString(6);
                settings.UseProxy = reader.GetInt32(7) != 0;
                settings.ProxyHost = reader.GetString(8);
                settings.ProxyPort = reader.GetInt32(9);
                settings.ProxyUser = reader.GetString(10);
                settings.ProxyPassword = reader.GetString(11);
                settings.RemotePaths = DeserializeStringList(reader.GetString(12));
                settings.RetentionDays = reader.GetInt32(13);
                settings.ExcludePatterns = reader.GetString(14);
                settings.IncludeExtensions = reader.GetString(15);
                settings.MinFileSizeMb = reader.GetDouble(16);
                settings.EnableBackup = reader.GetInt32(17) != 0;
                settings.LocalBackupPath = reader.GetString(18);
                settings.ProjectBackupPath = reader.GetString(19);
                settings.LastDownloadDirectory = reader.IsDBNull(20) ? null : reader.GetString(20);
                settings.LogFolderPath = reader.GetString(21);
                settings.IsDarkTheme = reader.GetInt32(22) != 0;
                settings.ThemeName = reader.GetString(23);
                settings.MinimizeToTrayOnClose = reader.GetInt32(24) != 0;
                settings.SmtpHost = reader.GetString(25);
                settings.SmtpPort = reader.GetInt32(26);
                settings.SmtpEnableSsl = reader.GetInt32(27) != 0;
                settings.SmtpUser = reader.GetString(28);
                settings.SmtpPassword = reader.GetString(29);
                settings.SenderEmail = reader.GetString(30);
                settings.RecipientEmails = DeserializeStringList(reader.GetString(31));
                settings.AutoSendEmailOnPreview = reader.GetInt32(32) != 0;

                settings.Ai = new AiSettings
                {
                    IsEnabled = reader.GetInt32(33) != 0,
                    BaseUrl = reader.GetString(34),
                    ApiKey = reader.GetString(35),
                    ModelName = reader.GetString(36),
                    TimeoutSeconds = reader.GetInt32(37),
                    BypassProxy = reader.GetInt32(38) != 0,
                    Temperature = reader.GetDouble(39)
                };
            }
        }

        settings.Profiles = new List<FtpSiteProfile>();
        using (var profCmd = conn.CreateCommand())
        {
            profCmd.CommandText = """
                SELECT
                    Name, Host, Port, EncryptionMode, Username, Password,
                    UseProxy, ProxyHost, ProxyPort, ProxyUser, ProxyPassword,
                    RemotePathsJson, RetentionDays, ExcludePatterns, IncludeExtensions, MinFileSizeMb,
                    EnableBackup, LocalBackupPath,
                    SmtpHost, SmtpPort, SmtpEnableSsl, SmtpUser, SmtpPassword, SenderEmail,
                    RecipientEmailsJson, AutoSendEmailOnPreview
                FROM FtpProfiles
                ORDER BY SortOrder ASC, Name ASC;
                """;

            using var reader = profCmd.ExecuteReader();
            while (reader.Read())
            {
                settings.Profiles.Add(new FtpSiteProfile
                {
                    Name = reader.GetString(0),
                    Host = reader.GetString(1),
                    Port = reader.GetInt32(2),
                    EncryptionMode = reader.GetString(3),
                    Username = reader.GetString(4),
                    Password = reader.GetString(5),
                    UseProxy = reader.GetInt32(6) != 0,
                    ProxyHost = reader.GetString(7),
                    ProxyPort = reader.GetInt32(8),
                    ProxyUser = reader.GetString(9),
                    ProxyPassword = reader.GetString(10),
                    RemotePaths = DeserializeStringList(reader.GetString(11)),
                    RetentionDays = reader.GetInt32(12),
                    ExcludePatterns = reader.GetString(13),
                    IncludeExtensions = reader.GetString(14),
                    MinFileSizeMb = reader.GetDouble(15),
                    EnableBackup = reader.GetInt32(16) != 0,
                    LocalBackupPath = reader.GetString(17),
                    SmtpHost = reader.GetString(18),
                    SmtpPort = reader.GetInt32(19),
                    SmtpEnableSsl = reader.GetInt32(20) != 0,
                    SmtpUser = reader.GetString(21),
                    SmtpPassword = reader.GetString(22),
                    SenderEmail = reader.GetString(23),
                    RecipientEmails = DeserializeStringList(reader.GetString(24)),
                    AutoSendEmailOnPreview = reader.GetInt32(25) != 0
                });
            }
        }

        return settings;
    }

    private static void SaveToSqlite(FtpSettings settings, string dbPath)
    {
        // Tạo bản sao để mã hóa toàn bộ mật khẩu khi lưu xuống SQLite mà không làm ảnh hưởng tới đối tượng đang chạy trên RAM
        var enc = CreateEncryptedCopy(settings);

        using var conn = SqliteDatabaseHelper.OpenConnection(dbPath);
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO AppSettings (
                    Id, SelectedProfileName, SelectedScanProfileNamesJson, Host, Port, EncryptionMode,
                    Username, Password, UseProxy, ProxyHost, ProxyPort, ProxyUser, ProxyPassword,
                    RemotePathsJson, RetentionDays, ExcludePatterns, IncludeExtensions, MinFileSizeMb,
                    EnableBackup, LocalBackupPath, ProjectBackupPath, LastDownloadDirectory, LogFolderPath,
                    IsDarkTheme, ThemeName, MinimizeToTrayOnClose,
                    SmtpHost, SmtpPort, SmtpEnableSsl, SmtpUser, SmtpPassword, SenderEmail,
                    RecipientEmailsJson, AutoSendEmailOnPreview,
                    AiIsEnabled, AiBaseUrl, AiApiKey, AiModelName, AiTimeoutSeconds, AiBypassProxy, AiTemperature
                ) VALUES (
                    1, $SelectedProfileName, $SelectedScanProfileNamesJson, $Host, $Port, $EncryptionMode,
                    $Username, $Password, $UseProxy, $ProxyHost, $ProxyPort, $ProxyUser, $ProxyPassword,
                    $RemotePathsJson, $RetentionDays, $ExcludePatterns, $IncludeExtensions, $MinFileSizeMb,
                    $EnableBackup, $LocalBackupPath, $ProjectBackupPath, $LastDownloadDirectory, $LogFolderPath,
                    $IsDarkTheme, $ThemeName, $MinimizeToTrayOnClose,
                    $SmtpHost, $SmtpPort, $SmtpEnableSsl, $SmtpUser, $SmtpPassword, $SenderEmail,
                    $RecipientEmailsJson, $AutoSendEmailOnPreview,
                    $AiIsEnabled, $AiBaseUrl, $AiApiKey, $AiModelName, $AiTimeoutSeconds, $AiBypassProxy, $AiTemperature
                );
                """;

            cmd.Parameters.AddWithValue("$SelectedProfileName", enc.SelectedProfileName ?? "Default");
            cmd.Parameters.AddWithValue("$SelectedScanProfileNamesJson", SerializeStringList(enc.SelectedScanProfileNames));
            cmd.Parameters.AddWithValue("$Host", enc.Host ?? string.Empty);
            cmd.Parameters.AddWithValue("$Port", enc.Port);
            cmd.Parameters.AddWithValue("$EncryptionMode", enc.EncryptionMode ?? "Implicit");
            cmd.Parameters.AddWithValue("$Username", enc.Username ?? string.Empty);
            cmd.Parameters.AddWithValue("$Password", enc.Password ?? string.Empty);
            cmd.Parameters.AddWithValue("$UseProxy", enc.UseProxy ? 1 : 0);
            cmd.Parameters.AddWithValue("$ProxyHost", enc.ProxyHost ?? string.Empty);
            cmd.Parameters.AddWithValue("$ProxyPort", enc.ProxyPort);
            cmd.Parameters.AddWithValue("$ProxyUser", enc.ProxyUser ?? string.Empty);
            cmd.Parameters.AddWithValue("$ProxyPassword", enc.ProxyPassword ?? string.Empty);
            cmd.Parameters.AddWithValue("$RemotePathsJson", SerializeStringList(enc.RemotePaths));
            cmd.Parameters.AddWithValue("$RetentionDays", enc.RetentionDays);
            cmd.Parameters.AddWithValue("$ExcludePatterns", enc.ExcludePatterns ?? string.Empty);
            cmd.Parameters.AddWithValue("$IncludeExtensions", enc.IncludeExtensions ?? string.Empty);
            cmd.Parameters.AddWithValue("$MinFileSizeMb", enc.MinFileSizeMb);
            cmd.Parameters.AddWithValue("$EnableBackup", enc.EnableBackup ? 1 : 0);
            cmd.Parameters.AddWithValue("$LocalBackupPath", enc.LocalBackupPath ?? string.Empty);
            cmd.Parameters.AddWithValue("$ProjectBackupPath", enc.ProjectBackupPath ?? string.Empty);
            cmd.Parameters.AddWithValue("$LastDownloadDirectory", (object?)enc.LastDownloadDirectory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$LogFolderPath", enc.LogFolderPath ?? string.Empty);
            cmd.Parameters.AddWithValue("$IsDarkTheme", enc.IsDarkTheme ? 1 : 0);
            cmd.Parameters.AddWithValue("$ThemeName", enc.ThemeName ?? string.Empty);
            cmd.Parameters.AddWithValue("$MinimizeToTrayOnClose", enc.MinimizeToTrayOnClose ? 1 : 0);
            cmd.Parameters.AddWithValue("$SmtpHost", enc.SmtpHost ?? string.Empty);
            cmd.Parameters.AddWithValue("$SmtpPort", enc.SmtpPort);
            cmd.Parameters.AddWithValue("$SmtpEnableSsl", enc.SmtpEnableSsl ? 1 : 0);
            cmd.Parameters.AddWithValue("$SmtpUser", enc.SmtpUser ?? string.Empty);
            cmd.Parameters.AddWithValue("$SmtpPassword", enc.SmtpPassword ?? string.Empty);
            cmd.Parameters.AddWithValue("$SenderEmail", enc.SenderEmail ?? string.Empty);
            cmd.Parameters.AddWithValue("$RecipientEmailsJson", SerializeStringList(enc.RecipientEmails));
            cmd.Parameters.AddWithValue("$AutoSendEmailOnPreview", enc.AutoSendEmailOnPreview ? 1 : 0);

            var ai = enc.Ai ?? new AiSettings();
            cmd.Parameters.AddWithValue("$AiIsEnabled", ai.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$AiBaseUrl", ai.BaseUrl ?? string.Empty);
            cmd.Parameters.AddWithValue("$AiApiKey", ai.ApiKey ?? string.Empty);
            cmd.Parameters.AddWithValue("$AiModelName", ai.ModelName ?? string.Empty);
            cmd.Parameters.AddWithValue("$AiTimeoutSeconds", ai.TimeoutSeconds);
            cmd.Parameters.AddWithValue("$AiBypassProxy", ai.BypassProxy ? 1 : 0);
            cmd.Parameters.AddWithValue("$AiTemperature", ai.Temperature);

            cmd.ExecuteNonQuery();
        }

        using (var delCmd = conn.CreateCommand())
        {
            delCmd.Transaction = tx;
            delCmd.CommandText = "DELETE FROM FtpProfiles;";
            delCmd.ExecuteNonQuery();
        }

        if (enc.Profiles != null)
        {
            for (int i = 0; i < enc.Profiles.Count; i++)
            {
                var p = enc.Profiles[i];
                using var insCmd = conn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = """
                    INSERT OR REPLACE INTO FtpProfiles (
                        Name, SortOrder, Host, Port, EncryptionMode, Username, Password,
                        UseProxy, ProxyHost, ProxyPort, ProxyUser, ProxyPassword,
                        RemotePathsJson, RetentionDays, ExcludePatterns, IncludeExtensions, MinFileSizeMb,
                        EnableBackup, LocalBackupPath,
                        SmtpHost, SmtpPort, SmtpEnableSsl, SmtpUser, SmtpPassword, SenderEmail,
                        RecipientEmailsJson, AutoSendEmailOnPreview
                    ) VALUES (
                        $Name, $SortOrder, $Host, $Port, $EncryptionMode, $Username, $Password,
                        $UseProxy, $ProxyHost, $ProxyPort, $ProxyUser, $ProxyPassword,
                        $RemotePathsJson, $RetentionDays, $ExcludePatterns, $IncludeExtensions, $MinFileSizeMb,
                        $EnableBackup, $LocalBackupPath,
                        $SmtpHost, $SmtpPort, $SmtpEnableSsl, $SmtpUser, $SmtpPassword, $SenderEmail,
                        $RecipientEmailsJson, $AutoSendEmailOnPreview
                    );
                    """;

                insCmd.Parameters.AddWithValue("$Name", p.Name ?? "Default");
                insCmd.Parameters.AddWithValue("$SortOrder", i);
                insCmd.Parameters.AddWithValue("$Host", p.Host ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Port", p.Port);
                insCmd.Parameters.AddWithValue("$EncryptionMode", p.EncryptionMode ?? "Implicit");
                insCmd.Parameters.AddWithValue("$Username", p.Username ?? string.Empty);
                insCmd.Parameters.AddWithValue("$Password", p.Password ?? string.Empty);
                insCmd.Parameters.AddWithValue("$UseProxy", p.UseProxy ? 1 : 0);
                insCmd.Parameters.AddWithValue("$ProxyHost", p.ProxyHost ?? string.Empty);
                insCmd.Parameters.AddWithValue("$ProxyPort", p.ProxyPort);
                insCmd.Parameters.AddWithValue("$ProxyUser", p.ProxyUser ?? string.Empty);
                insCmd.Parameters.AddWithValue("$ProxyPassword", p.ProxyPassword ?? string.Empty);
                insCmd.Parameters.AddWithValue("$RemotePathsJson", SerializeStringList(p.RemotePaths));
                insCmd.Parameters.AddWithValue("$RetentionDays", p.RetentionDays);
                insCmd.Parameters.AddWithValue("$ExcludePatterns", p.ExcludePatterns ?? string.Empty);
                insCmd.Parameters.AddWithValue("$IncludeExtensions", p.IncludeExtensions ?? string.Empty);
                insCmd.Parameters.AddWithValue("$MinFileSizeMb", p.MinFileSizeMb);
                insCmd.Parameters.AddWithValue("$EnableBackup", p.EnableBackup ? 1 : 0);
                insCmd.Parameters.AddWithValue("$LocalBackupPath", p.LocalBackupPath ?? string.Empty);
                insCmd.Parameters.AddWithValue("$SmtpHost", p.SmtpHost ?? string.Empty);
                insCmd.Parameters.AddWithValue("$SmtpPort", p.SmtpPort);
                insCmd.Parameters.AddWithValue("$SmtpEnableSsl", p.SmtpEnableSsl ? 1 : 0);
                insCmd.Parameters.AddWithValue("$SmtpUser", p.SmtpUser ?? string.Empty);
                insCmd.Parameters.AddWithValue("$SmtpPassword", p.SmtpPassword ?? string.Empty);
                insCmd.Parameters.AddWithValue("$SenderEmail", p.SenderEmail ?? string.Empty);
                insCmd.Parameters.AddWithValue("$RecipientEmailsJson", SerializeStringList(p.RecipientEmails));
                insCmd.Parameters.AddWithValue("$AutoSendEmailOnPreview", p.AutoSendEmailOnPreview ? 1 : 0);

                insCmd.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    private static string SerializeStringList(List<string>? list) =>
        JsonSerializer.Serialize(list ?? new List<string>());

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void UnprotectSettingsSecrets(FtpSettings settings)
    {
        settings.Password = SecretProtector.Unprotect(settings.Password);
        settings.ProxyPassword = SecretProtector.Unprotect(settings.ProxyPassword);
        settings.SmtpPassword = SecretProtector.Unprotect(settings.SmtpPassword);
        if (settings.Ai != null)
        {
            settings.Ai.ApiKey = SecretProtector.Unprotect(settings.Ai.ApiKey);
        }

        if (settings.Profiles != null)
        {
            foreach (var p in settings.Profiles)
            {
                p.Password = SecretProtector.Unprotect(p.Password);
                p.ProxyPassword = SecretProtector.Unprotect(p.ProxyPassword);
                p.SmtpPassword = SecretProtector.Unprotect(p.SmtpPassword);
            }
        }
    }

    private static FtpSettings CreateEncryptedCopy(FtpSettings source)
    {
        var copy = new FtpSettings
        {
            SelectedProfileName = source.SelectedProfileName,
            SelectedScanProfileNames = new List<string>(source.SelectedScanProfileNames ?? new List<string>()),
            Host = source.Host,
            Port = source.Port,
            EncryptionMode = source.EncryptionMode,
            Username = source.Username,
            Password = SecretProtector.Protect(source.Password),
            UseProxy = source.UseProxy,
            ProxyHost = source.ProxyHost,
            ProxyPort = source.ProxyPort,
            ProxyUser = source.ProxyUser,
            ProxyPassword = SecretProtector.Protect(source.ProxyPassword),
            RemotePaths = new List<string>(source.RemotePaths ?? new List<string> { "/" }),
            RetentionDays = source.RetentionDays,
            ExcludePatterns = source.ExcludePatterns ?? string.Empty,
            IncludeExtensions = source.IncludeExtensions ?? string.Empty,
            MinFileSizeMb = source.MinFileSizeMb,
            EnableBackup = source.EnableBackup,
            LocalBackupPath = source.LocalBackupPath,
            ProjectBackupPath = source.ProjectBackupPath,
            LastDownloadDirectory = source.LastDownloadDirectory,
            LogFolderPath = source.LogFolderPath,
            IsDarkTheme = source.IsDarkTheme,
            ThemeName = source.ThemeName,
            MinimizeToTrayOnClose = source.MinimizeToTrayOnClose,
            SmtpHost = source.SmtpHost,
            SmtpPort = source.SmtpPort,
            SmtpEnableSsl = source.SmtpEnableSsl,
            SmtpUser = source.SmtpUser,
            SmtpPassword = SecretProtector.Protect(source.SmtpPassword),
            SenderEmail = source.SenderEmail,
            RecipientEmails = new List<string>(source.RecipientEmails ?? new List<string>()),
            AutoSendEmailOnPreview = source.AutoSendEmailOnPreview,
            Profiles = new List<FtpSiteProfile>(),
            Ai = source.Ai != null ? new AiSettings
            {
                IsEnabled = source.Ai.IsEnabled,
                BaseUrl = source.Ai.BaseUrl,
                ApiKey = SecretProtector.Protect(source.Ai.ApiKey),
                ModelName = source.Ai.ModelName,
                TimeoutSeconds = source.Ai.TimeoutSeconds,
                BypassProxy = source.Ai.BypassProxy,
                Temperature = source.Ai.Temperature
            } : new AiSettings()
        };

        if (source.Profiles != null)
        {
            foreach (var p in source.Profiles)
            {
                copy.Profiles.Add(new FtpSiteProfile
                {
                    Name = p.Name,
                    Host = p.Host,
                    Port = p.Port,
                    EncryptionMode = p.EncryptionMode,
                    Username = p.Username,
                    Password = SecretProtector.Protect(p.Password),
                    UseProxy = p.UseProxy,
                    ProxyHost = p.ProxyHost,
                    ProxyPort = p.ProxyPort,
                    ProxyUser = p.ProxyUser,
                    ProxyPassword = SecretProtector.Protect(p.ProxyPassword),
                    RemotePaths = new List<string>(p.RemotePaths ?? new List<string> { "/" }),
                    RetentionDays = p.RetentionDays,
                    ExcludePatterns = p.ExcludePatterns ?? string.Empty,
                    IncludeExtensions = p.IncludeExtensions ?? string.Empty,
                    MinFileSizeMb = p.MinFileSizeMb,
                    EnableBackup = p.EnableBackup,
                    LocalBackupPath = p.LocalBackupPath,
                    SmtpHost = p.SmtpHost,
                    SmtpPort = p.SmtpPort,
                    SmtpEnableSsl = p.SmtpEnableSsl,
                    SmtpUser = p.SmtpUser,
                    SmtpPassword = SecretProtector.Protect(p.SmtpPassword),
                    SenderEmail = p.SenderEmail,
                    RecipientEmails = new List<string>(p.RecipientEmails ?? new List<string>()),
                    AutoSendEmailOnPreview = p.AutoSendEmailOnPreview
                });
            }
        }

        return copy;
    }

    public void EnsureProfilesInitialized(FtpSettings settings)
    {
        NormalizeThemeSettings(settings);

        if (settings.RemotePaths == null || settings.RemotePaths.Count == 0)
        {
            settings.RemotePaths = new List<string> { "/" };
        }

        settings.Profiles ??= new List<FtpSiteProfile>();

        // Backward compatibility: If root email settings are empty but an existing profile has email config, adopt it
        if ((settings.RecipientEmails == null || settings.RecipientEmails.Count == 0) &&
            string.IsNullOrWhiteSpace(settings.SenderEmail) &&
            string.IsNullOrWhiteSpace(settings.SmtpUser))
        {
            var profWithEmail = settings.Profiles.FirstOrDefault(p =>
                (p.RecipientEmails != null && p.RecipientEmails.Count > 0) ||
                !string.IsNullOrWhiteSpace(p.SenderEmail) ||
                !string.IsNullOrWhiteSpace(p.SmtpUser));

            if (profWithEmail != null)
            {
                settings.SmtpHost = profWithEmail.SmtpHost;
                settings.SmtpPort = profWithEmail.SmtpPort;
                settings.SmtpEnableSsl = profWithEmail.SmtpEnableSsl;
                settings.SmtpUser = profWithEmail.SmtpUser;
                settings.SmtpPassword = profWithEmail.SmtpPassword;
                settings.SenderEmail = profWithEmail.SenderEmail;
                settings.RecipientEmails = new List<string>(profWithEmail.RecipientEmails ?? new List<string>());
                settings.AutoSendEmailOnPreview = profWithEmail.AutoSendEmailOnPreview;
            }
        }

        if (settings.Profiles.Count == 0)
        {
            string defaultName = !string.IsNullOrWhiteSpace(settings.SelectedProfileName)
                ? settings.SelectedProfileName.Trim()
                : (!string.IsNullOrWhiteSpace(settings.Username) ? settings.Username.Trim() : "Default");

            settings.SelectedProfileName = defaultName;
            settings.Profiles.Add(FtpSiteProfile.FromSettings(defaultName, settings));
        }
        else if (string.IsNullOrWhiteSpace(settings.SelectedProfileName))
        {
            settings.SelectedProfileName = settings.Profiles[0].Name;
        }

        settings.SelectedScanProfileNames ??= new List<string>();
        var validProfileNames = new HashSet<string>(settings.Profiles.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        settings.SelectedScanProfileNames = settings.SelectedScanProfileNames
            .Where(name => !string.IsNullOrWhiteSpace(name) && validProfileNames.Contains(name.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (settings.SelectedScanProfileNames.Count == 0 && !string.IsNullOrWhiteSpace(settings.SelectedProfileName))
        {
            settings.SelectedScanProfileNames.Add(settings.SelectedProfileName);
        }

        SyncEmailSettingsAcrossAllProfiles(settings);
        SyncFolderSettingsAcrossAllProfiles(settings);
    }

    public static void SyncEmailSettingsAcrossAllProfiles(FtpSettings settings)
    {
        if (settings.Profiles == null) return;
        foreach (var profile in settings.Profiles)
        {
            profile.SyncEmailSettings(settings);
        }
    }

    public static void SyncFolderSettingsAcrossAllProfiles(FtpSettings settings)
    {
        if (settings.Profiles == null) return;
        foreach (var profile in settings.Profiles)
        {
            profile.LocalBackupPath = settings.LocalBackupPath;
        }
    }

    public BackupFolderSettings GetBackupFolderSettings(FtpSettings? settings = null)
    {
        settings ??= Load();
        var projService = new ProjectManagerService();
        var projSettings = projService.LoadSettings();

        string projPath = !string.IsNullOrWhiteSpace(settings.ProjectBackupPath)
            ? settings.ProjectBackupPath
            : (!string.IsNullOrWhiteSpace(projSettings.BackupRootDirectory)
                ? projSettings.BackupRootDirectory
                : BackupFolderSettings.DefaultProjectBackupPath);

        string logPath = !string.IsNullOrWhiteSpace(settings.LogFolderPath)
            ? settings.LogFolderPath
            : BackupFolderSettings.DefaultLogFolderPath;

        string ftpPath = !string.IsNullOrWhiteSpace(settings.LocalBackupPath)
            ? settings.LocalBackupPath
            : BackupFolderSettings.DefaultFtpBackupPath;

        return new BackupFolderSettings
        {
            LogFolderPath = logPath,
            ProjectBackupPath = projPath,
            FtpBackupPath = ftpPath
        };
    }

    public void SaveBackupFolderSettings(BackupFolderSettings folderSettings, FtpSettings? currentSettings = null)
    {
        var settings = currentSettings ?? Load();
        settings.LogFolderPath = string.IsNullOrWhiteSpace(folderSettings.LogFolderPath)
            ? BackupFolderSettings.DefaultLogFolderPath
            : folderSettings.LogFolderPath.Trim();
        settings.ProjectBackupPath = string.IsNullOrWhiteSpace(folderSettings.ProjectBackupPath)
            ? BackupFolderSettings.DefaultProjectBackupPath
            : folderSettings.ProjectBackupPath.Trim();
        settings.LocalBackupPath = string.IsNullOrWhiteSpace(folderSettings.FtpBackupPath)
            ? BackupFolderSettings.DefaultFtpBackupPath
            : folderSettings.FtpBackupPath.Trim();

        SyncFolderSettingsAcrossAllProfiles(settings);
        Save(settings);

        try
        {
            var projService = new ProjectManagerService();
            var projSettings = projService.LoadSettings();
            projSettings.BackupRootDirectory = settings.ProjectBackupPath;
            projService.SaveSettings(projSettings);
        }
        catch { }
    }

    public FtpSiteProfile UpsertProfile(FtpSettings settings, string profileName)
    {
        settings.Profiles ??= new List<FtpSiteProfile>();
        string cleanName = string.IsNullOrWhiteSpace(profileName) ? "Default" : profileName.Trim();
        settings.SelectedProfileName = cleanName;

        var updated = FtpSiteProfile.FromSettings(cleanName, settings);
        int idx = settings.Profiles.FindIndex(p => string.Equals(p.Name, cleanName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            settings.Profiles[idx] = updated;
        }
        else
        {
            settings.Profiles.Add(updated);
        }

        SyncEmailSettingsAcrossAllProfiles(settings);
        return updated;
    }

    public bool SelectProfile(FtpSettings settings, string profileName)
    {
        if (settings.Profiles == null || string.IsNullOrWhiteSpace(profileName))
        {
            return false;
        }

        var found = settings.Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, profileName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found == null)
        {
            return false;
        }

        found.ApplyTo(settings);
        return true;
    }

    public bool DeleteProfile(FtpSettings settings, string profileName)
    {
        if (settings.Profiles == null || string.IsNullOrWhiteSpace(profileName))
        {
            return false;
        }

        int removed = settings.Profiles.RemoveAll(p =>
            string.Equals(p.Name, profileName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return false;
        }

        if (settings.Profiles.Count == 0)
        {
            var fallback = new FtpSettings();
            fallback.SmtpHost = settings.SmtpHost;
            fallback.SmtpPort = settings.SmtpPort;
            fallback.SmtpEnableSsl = settings.SmtpEnableSsl;
            fallback.SmtpUser = settings.SmtpUser;
            fallback.SmtpPassword = settings.SmtpPassword;
            fallback.SenderEmail = settings.SenderEmail;
            fallback.RecipientEmails = new List<string>(settings.RecipientEmails ?? new List<string>());
            fallback.AutoSendEmailOnPreview = settings.AutoSendEmailOnPreview;

            var defaultProfile = FtpSiteProfile.FromSettings("Default", fallback);
            settings.Profiles.Add(defaultProfile);
        }

        settings.Profiles[0].ApplyTo(settings);

        if (settings.SelectedScanProfileNames != null)
        {
            settings.SelectedScanProfileNames.RemoveAll(p => string.Equals(p, profileName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (settings.SelectedScanProfileNames.Count == 0 && !string.IsNullOrWhiteSpace(settings.SelectedProfileName))
            {
                settings.SelectedScanProfileNames.Add(settings.SelectedProfileName);
            }
        }

        return true;
    }

    public static void NormalizeThemeSettings(FtpSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ThemeName))
        {
            settings.ThemeName = settings.IsDarkTheme
                ? AppThemeKind.MaterialDark.ToString()
                : AppThemeKind.MaterialLight.ToString();
        }
        else
        {
            var resolved = ThemeService.ParseTheme(settings.ThemeName, settings.IsDarkTheme);
            settings.ThemeName = resolved.ToString();
            settings.IsDarkTheme = ThemeService.GetThemeOption(resolved).IsDarkBase;
        }
    }
}
