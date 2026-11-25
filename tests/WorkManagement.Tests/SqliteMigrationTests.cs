using System;
using System.Collections.Generic;
using System.IO;
using WorkManagement.Helpers;
using WorkManagement.Models;
using WorkManagement.Services;
using Xunit;

namespace WorkManagement.Tests;

public class SqliteMigrationTests
{
    [Fact]
    public void DefaultDatabasePath_PointsToAppDataWorkManagementDb()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WorkManagement",
            "workmanagement.db");

        Assert.Equal(expected, SqliteDatabaseHelper.DefaultDatabasePath);

        var configService = new ConfigService();
        Assert.Equal(expected, configService.DefaultConfigPath);
    }

    [Fact]
    public void ConfigService_AutoMigratesLegacyJsonToSqlite_AndCreatesJsonBak()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"wm_sqlite_cfg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string legacyJsonPath = Path.Combine(tempDir, "config.json");

        try
        {
            string legacyJson = """
            {
              "SelectedProfileName": "LEGACY_SITE",
              "Host": "ftp.legacy.local",
              "Port": 2121,
              "Username": "legacy_user",
              "Password": "PlainLegacyPassword123",
              "RemotePaths": [ "/in", "/out" ],
              "RecipientEmails": [ "ops@legacy.local" ]
            }
            """;
            File.WriteAllText(legacyJsonPath, legacyJson);

            var service = new ConfigService();
            var loaded = service.Load(legacyJsonPath);

            Assert.Equal("LEGACY_SITE", loaded.SelectedProfileName);
            Assert.Equal("ftp.legacy.local", loaded.Host);
            Assert.Equal(2121, loaded.Port);
            Assert.Equal("legacy_user", loaded.Username);
            Assert.Equal("PlainLegacyPassword123", loaded.Password);
            Assert.Equal(new[] { "/in", "/out" }, loaded.RemotePaths);
            Assert.Equal(new[] { "ops@legacy.local" }, loaded.RecipientEmails);

            // File gốc đã chuyển thành SQLite DB và có bản sao lưu *.json.bak
            Assert.True(SqliteDatabaseHelper.IsFileSqliteDatabase(legacyJsonPath));
            Assert.True(File.Exists(legacyJsonPath + ".bak"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void ProjectManagerService_AutoMigratesLegacyActiveAndDeletedJson_AndCreatesJsonBak()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"wm_sqlite_proj_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string activePath = Path.Combine(tempDir, "projects.json");
        string deletedPath = Path.Combine(tempDir, "deleted_projects.json");
        string settingsPath = Path.Combine(tempDir, "project_cleanup_settings.json");

        try
        {
            File.WriteAllText(activePath, """
            [
              {
                "Id": "ACT-01",
                "No": "1",
                "ProjectName": "ACTIVE_PRJ",
                "HostServer": "10.0.0.10",
                "DbaPassword": "PlainDbaPassword1"
              }
            ]
            """);

            File.WriteAllText(deletedPath, """
            [
              {
                "Id": "DEL-01",
                "No": "2",
                "ProjectName": "DELETED_PRJ",
                "HostServer": "10.0.0.20",
                "DbaPassword": "PlainDbaPassword2"
              }
            ]
            """);

            File.WriteAllText(settingsPath, """
            {
              "BackupRootDirectory": "D:\\LegacyBackups",
              "EnableBackupFolder": false,
              "DefaultDbaUser": "SYS"
            }
            """);

            var service = new ProjectManagerService(activePath, deletedPath);

            var active = service.LoadAll();
            var deleted = service.LoadAllDeleted();
            var cleanupSettings = service.LoadSettings();

            Assert.Single(active);
            Assert.Equal("ACTIVE_PRJ", active[0].ProjectName);
            Assert.Equal("PlainDbaPassword1", active[0].DbaPassword);

            Assert.Single(deleted);
            Assert.Equal("DELETED_PRJ", deleted[0].ProjectName);
            Assert.Equal("PlainDbaPassword2", deleted[0].DbaPassword);

            Assert.Equal(@"D:\LegacyBackups", cleanupSettings.BackupRootDirectory);
            Assert.False(cleanupSettings.EnableBackupFolder);
            Assert.Equal("SYS", cleanupSettings.DefaultDbaUser);

            // Kiểm tra đã tạo các file *.json.bak và lưu vào SQLite
            Assert.True(SqliteDatabaseHelper.IsFileSqliteDatabase(activePath));
            Assert.True(File.Exists(activePath + ".bak"));
            Assert.True(File.Exists(deletedPath + ".bak"));
            Assert.True(File.Exists(settingsPath + ".bak"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void VmManagerService_AutoMigratesLegacyJsonToSqlite_AndCreatesJsonBak()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"wm_sqlite_vm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string vmPath = Path.Combine(tempDir, "virtual_machines.json");

        try
        {
            File.WriteAllText(vmPath, """
            [
              {
                "Id": "VM-01",
                "No": "10",
                "Ip": "172.16.10.99",
                "MachineName": "VM-SQLITE-01",
                "Password": "VmSecretPassword!",
                "DbaPassword": "VmDbaPassword!",
                "IsFavorite": true
              }
            ]
            """);

            var service = new VmManagerService(vmPath);
            var vms = service.LoadAll();

            Assert.Single(vms);
            Assert.Equal("172.16.10.99", vms[0].Ip);
            Assert.Equal("VM-SQLITE-01", vms[0].MachineName);
            Assert.Equal("VmSecretPassword!", vms[0].Password);
            Assert.Equal("VmDbaPassword!", vms[0].DbaPassword);
            Assert.True(vms[0].IsFavorite);

            Assert.True(SqliteDatabaseHelper.IsFileSqliteDatabase(vmPath));
            Assert.True(File.Exists(vmPath + ".bak"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
