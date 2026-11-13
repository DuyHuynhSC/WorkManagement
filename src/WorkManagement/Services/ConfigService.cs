using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorkManagement.Helpers;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string DefaultConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public FtpSettings Load(string? filePath = null)
    {
        string targetPath = filePath ?? DefaultConfigPath;
        if (!File.Exists(targetPath))
        {
            var defaultSettings = new FtpSettings();
            EnsureProfilesInitialized(defaultSettings);
            Save(defaultSettings, targetPath);
            return defaultSettings;
        }

        string json = File.ReadAllText(targetPath);
        bool hadPlaintextSecrets = HasAnyPlaintextSecretsInJson(json);

        var settings = JsonSerializer.Deserialize<FtpSettings>(json, JsonOptions) ?? new FtpSettings();
        UnprotectSettingsSecrets(settings);
        EnsureProfilesInitialized(settings);

        // Tự động nâng cấp (Auto-migrate) các mật khẩu plain-text cũ sang chuẩn mã hóa DPAPI
        if (hadPlaintextSecrets)
        {
            try
            {
                Save(settings, targetPath);
            }
            catch
            {
                // Bỏ qua nếu file đang chỉ đọc (Read-only)
            }
        }

        return settings;
    }

    public void Save(FtpSettings settings, string? filePath = null)
    {
        string targetPath = filePath ?? DefaultConfigPath;
        string? dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        UpsertProfile(settings, settings.SelectedProfileName);
        SyncEmailSettingsAcrossAllProfiles(settings);
        SyncFolderSettingsAcrossAllProfiles(settings);
        NormalizeThemeSettings(settings);

        // Tạo bản sao để mã hóa toàn bộ mật khẩu khi lưu xuống đĩa mà không làm ảnh hưởng tới đối tượng đang chạy trên RAM
        var settingsToWrite = CreateEncryptedCopy(settings);
        string json = JsonSerializer.Serialize(settingsToWrite, JsonOptions);
        File.WriteAllText(targetPath, json);
    }

    private static bool HasAnyPlaintextSecretsInJson(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<FtpSettings>(json, JsonOptions);
            if (raw == null) return false;

            if (IsPlaintextNonEmpty(raw.Password) ||
                IsPlaintextNonEmpty(raw.ProxyPassword) ||
                IsPlaintextNonEmpty(raw.SmtpPassword) ||
                (raw.Ai != null && IsPlaintextNonEmpty(raw.Ai.ApiKey)))
            {
                return true;
            }

            if (raw.Profiles != null)
            {
                foreach (var p in raw.Profiles)
                {
                    if (IsPlaintextNonEmpty(p.Password) ||
                        IsPlaintextNonEmpty(p.ProxyPassword) ||
                        IsPlaintextNonEmpty(p.SmtpPassword))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    private static bool IsPlaintextNonEmpty(string? val) =>
        !string.IsNullOrEmpty(val) && !SecretProtector.IsEncrypted(val);

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
