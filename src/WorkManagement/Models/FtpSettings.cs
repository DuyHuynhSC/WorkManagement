using System.Collections.Generic;
using System.IO;

namespace WorkManagement.Models;

public class FtpSettings
{
    public string SelectedProfileName { get; set; } = "Default";
    public List<string> SelectedScanProfileNames { get; set; } = new();
    public string Host { get; set; } = "ftp.xxx.net";
    public int Port { get; set; } = 6021;
    public string EncryptionMode { get; set; } = "Implicit";
    public string Username { get; set; } = "XXX-USER";
    public string Password { get; set; } = string.Empty;
    public bool UseProxy { get; set; } = true;
    public string ProxyHost { get; set; } = "proxy.xxx.vn";
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUser { get; set; } = string.Empty;
    public string ProxyPassword { get; set; } = string.Empty;
    public List<string> RemotePaths { get; set; } = new() { "/" };
    public int RetentionDays { get; set; } = 1;
    public string ExcludePatterns { get; set; } = string.Empty;
    public string IncludeExtensions { get; set; } = string.Empty;
    public double MinFileSizeMb { get; set; } = 0;
    public bool EnableBackup { get; set; } = true;
    public string LocalBackupPath { get; set; } = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Backups");
    public string ProjectBackupPath { get; set; } = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "WorkManagement_Backups");
    public string LogFolderPath { get; set; } = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "logs");
    public bool IsDarkTheme { get; set; } = false;
    public string ThemeName { get; set; } = string.Empty;
    public bool MinimizeToTrayOnClose { get; set; } = true;

    // Email Notification Settings
    public string SmtpHost { get; set; } = "mail.fujinet.net";
    public int SmtpPort { get; set; } = 25;
    public bool SmtpEnableSsl { get; set; } = false;
    public string SmtpUser { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public List<string> RecipientEmails { get; set; } = new();
    public bool AutoSendEmailOnPreview { get; set; } = false;

    public List<FtpSiteProfile> Profiles { get; set; } = new();

    // AI Integration Settings (OpenAI-compatible)
    public AiSettings Ai { get; set; } = new();
}
