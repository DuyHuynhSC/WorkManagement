using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using WorkManagement.Models;
using WorkManagement.Services;
using WorkManagement.Views;
using Xunit;

namespace WorkManagement.Tests;

[Collection("WpfApp")]
public class BackupFolderSettingsTests
{
    [Fact]
    public void BackupFolderSettings_Defaults_AreValidAndNonEmpty()
    {
        var settings = new BackupFolderSettings();

        Assert.False(string.IsNullOrWhiteSpace(settings.LogFolderPath));
        Assert.False(string.IsNullOrWhiteSpace(settings.ProjectBackupPath));
        Assert.False(string.IsNullOrWhiteSpace(settings.FtpBackupPath));

        Assert.Contains("logs", settings.LogFolderPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Backups", settings.ProjectBackupPath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Backups", settings.FtpBackupPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackupFolderSettings_Clone_CreatesExactCopy()
    {
        var original = new BackupFolderSettings
        {
            LogFolderPath = @"C:\TestLogs",
            ProjectBackupPath = @"D:\TestProjBackup",
            FtpBackupPath = @"E:\TestFtpBackup"
        };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.LogFolderPath, clone.LogFolderPath);
        Assert.Equal(original.ProjectBackupPath, clone.ProjectBackupPath);
        Assert.Equal(original.FtpBackupPath, clone.FtpBackupPath);
    }

    [Fact]
    public void ConfigService_GetAndSaveBackupFolderSettings_SyncsProperly()
    {
        string tempConfigFile = Path.Combine(Path.GetTempPath(), $"cfg_backup_{Guid.NewGuid()}.json");
        try
        {
            var configService = new ConfigService();
            var ftpSettings = new FtpSettings
            {
                Host = "ftp.example.com",
                LocalBackupPath = @"C:\CustomFtpBackup",
                ProjectBackupPath = @"C:\CustomProjBackup",
                LogFolderPath = @"C:\CustomLogs",
                Profiles = new List<FtpSiteProfile>
                {
                    new() { Name = "Profile1", LocalBackupPath = @"C:\OldFtpBackup" },
                    new() { Name = "Profile2", LocalBackupPath = @"C:\OldFtpBackup" }
                }
            };

            configService.Save(ftpSettings, tempConfigFile);

            var loadedFolders = configService.GetBackupFolderSettings(ftpSettings);
            Assert.Equal(@"C:\CustomFtpBackup", loadedFolders.FtpBackupPath);
            Assert.Equal(@"C:\CustomProjBackup", loadedFolders.ProjectBackupPath);
            Assert.Equal(@"C:\CustomLogs", loadedFolders.LogFolderPath);

            // Save new folder settings
            var newFolders = new BackupFolderSettings
            {
                FtpBackupPath = @"D:\UnifiedFtpBackup",
                ProjectBackupPath = @"D:\UnifiedProjBackup",
                LogFolderPath = @"D:\UnifiedLogs"
            };

            configService.SaveBackupFolderSettings(newFolders, ftpSettings);

            // Verify updated in ftpSettings and all profiles
            Assert.Equal(@"D:\UnifiedFtpBackup", ftpSettings.LocalBackupPath);
            Assert.Equal(@"D:\UnifiedProjBackup", ftpSettings.ProjectBackupPath);
            Assert.Equal(@"D:\UnifiedLogs", ftpSettings.LogFolderPath);
            Assert.All(ftpSettings.Profiles, p => Assert.Equal(@"D:\UnifiedFtpBackup", p.LocalBackupPath));
        }
        finally
        {
            if (File.Exists(tempConfigFile))
            {
                try { File.Delete(tempConfigFile); } catch { }
            }
        }
    }

    [Fact]
    public void AppLogger_SetLogDirectory_WritesToNewDirectory()
    {
        string tempDir1 = Path.Combine(Path.GetTempPath(), $"logger_test1_{Guid.NewGuid()}");
        string tempDir2 = Path.Combine(Path.GetTempPath(), $"logger_test2_{Guid.NewGuid()}");

        try
        {
            var logger = new AppLogger(tempDir1);
            Assert.Equal(tempDir1, logger.LogDirectory);
            logger.Info("Message 1");

            string expectedLog1 = Path.Combine(tempDir1, $"WorkManagement_{DateTime.Now:yyyyMMdd}.log");
            Assert.True(File.Exists(expectedLog1));

            logger.SetLogDirectory(tempDir2);
            Assert.Equal(tempDir2, logger.LogDirectory);
            logger.Info("Message 2");

            string expectedLog2 = Path.Combine(tempDir2, $"WorkManagement_{DateTime.Now:yyyyMMdd}.log");
            Assert.True(File.Exists(expectedLog2));
        }
        finally
        {
            try { if (Directory.Exists(tempDir1)) Directory.Delete(tempDir1, true); } catch { }
            try { if (Directory.Exists(tempDir2)) Directory.Delete(tempDir2, true); } catch { }
        }
    }

    [Fact]
    public void ConnectionSettingsView_PreservesLocalBackupPath_WithoutUiElement()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                WpfTestHelper.EnsureApplication();

                var originalSettings = new FtpSettings
                {
                    Host = "ftp.preserved.net",
                    LocalBackupPath = @"C:\Preserved\BackupPath",
                    EnableBackup = true
                };

                var view = new ConnectionSettingsView();
                view.Initialize(originalSettings, new ConfigService(), new FtpCleanupService(new AppLogger()), new AppLogger());

                var read = view.ReadSettingsFromUi();
                Assert.Equal(@"C:\Preserved\BackupPath", read.LocalBackupPath);
                Assert.True(read.EnableBackup);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;
    }

    [Fact]
    public void BackupFolderSettingsView_LoadsAndReadsUiCorrectly()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                WpfTestHelper.EnsureApplication();

                var view = new BackupFolderSettingsView();
                view.SetPaths(@"D:\FtpBkp", @"D:\ProjBkp", @"D:\Logs");

                var read = view.ReadFromUi();
                Assert.Equal(@"D:\FtpBkp", read.FtpBackupPath);
                Assert.Equal(@"D:\ProjBkp", read.ProjectBackupPath);
                Assert.Equal(@"D:\Logs", read.LogFolderPath);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;
    }

    [Fact]
    public void BackupFolderSettingsWindow_CanInstantiate()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                WpfTestHelper.EnsureApplication();

                var window = new BackupFolderSettingsWindow(new ConfigService(), new AppLogger());
                var read = window.ReadFromUi();
                Assert.NotNull(read);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx != null) throw threadEx;
    }
}
