using System;
using System.IO;

namespace WorkManagement.Models;

public class BackupFolderSettings
{
    public static string DefaultLogFolderPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    public static string DefaultProjectBackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "WorkManagement_Backups");

    public static string DefaultFtpBackupPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");

    public string LogFolderPath { get; set; } = DefaultLogFolderPath;

    public string ProjectBackupPath { get; set; } = DefaultProjectBackupPath;

    public string FtpBackupPath { get; set; } = DefaultFtpBackupPath;

    public BackupFolderSettings Clone()
    {
        return new BackupFolderSettings
        {
            LogFolderPath = LogFolderPath,
            ProjectBackupPath = ProjectBackupPath,
            FtpBackupPath = FtpBackupPath
        };
    }
}
