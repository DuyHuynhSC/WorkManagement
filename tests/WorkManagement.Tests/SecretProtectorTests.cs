using System;
using System.Collections.Generic;
using System.IO;
using WorkManagement.Helpers;
using WorkManagement.Models;
using WorkManagement.Services;
using Xunit;

namespace WorkManagement.Tests;

public class SecretProtectorTests
{
    [Fact]
    public void Protect_And_Unprotect_RoundtripsCorrectly()
    {
        string raw = "MySuperSecret@123!#";
        string encrypted = SecretProtector.Protect(raw);

        Assert.NotEqual(raw, encrypted);
        Assert.True(SecretProtector.IsEncrypted(encrypted));
        Assert.StartsWith(SecretProtector.EncryptedPrefix, encrypted);

        string decrypted = SecretProtector.Unprotect(encrypted);
        Assert.Equal(raw, decrypted);
    }

    [Fact]
    public void Protect_IsIdempotent_DoesNotDoubleEncrypt()
    {
        string raw = "Pass#2026";
        string encryptedOnce = SecretProtector.Protect(raw);
        string encryptedTwice = SecretProtector.Protect(encryptedOnce);

        Assert.Equal(encryptedOnce, encryptedTwice);
        Assert.Equal(raw, SecretProtector.Unprotect(encryptedTwice));
    }

    [Fact]
    public void Unprotect_PlainText_ReturnsOriginalForBackwardCompatibility()
    {
        string legacyPlainText = "LegacyPlaintextPassword123";
        Assert.False(SecretProtector.IsEncrypted(legacyPlainText));

        string result = SecretProtector.Unprotect(legacyPlainText);
        Assert.Equal(legacyPlainText, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Protect_And_Unprotect_EmptyOrNull_ReturnsEmptyString(string? input)
    {
        Assert.Equal(string.Empty, SecretProtector.Protect(input));
        Assert.Equal(string.Empty, SecretProtector.Unprotect(input));
    }

    [Fact]
    public void ConfigService_Save_EncryptsPasswordsOnDisk_WhileKeepingPlainTextInMemory()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"ftp_enc_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new ConfigService();
            var settings = new FtpSettings
            {
                SelectedProfileName = "PROD_FTP",
                Host = "ftp.corp.local",
                Username = "admin",
                Password = "FtpSecretPassword!",
                ProxyPassword = "ProxySecretPassword!",
                SmtpPassword = "SmtpSecretPassword!"
            };

            service.Save(settings, tempFile);

            // 1. Trong bộ nhớ RAM vẫn phải giữ nguyên mật khẩu giải mã để dùng ngay
            Assert.Equal("FtpSecretPassword!", settings.Password);
            Assert.Equal("ProxySecretPassword!", settings.ProxyPassword);
            Assert.Equal("SmtpSecretPassword!", settings.SmtpPassword);

            // 2. Nội dung file JSON trên ổ cứng tuyệt đối không chứa plain-text password
            string rawJson = File.ReadAllText(tempFile);
            Assert.DoesNotContain("FtpSecretPassword!", rawJson);
            Assert.DoesNotContain("ProxySecretPassword!", rawJson);
            Assert.DoesNotContain("SmtpSecretPassword!", rawJson);
            Assert.Contains(SecretProtector.EncryptedPrefix, rawJson);

            // 3. Khi Load lại từ đĩa phải giải mã chính xác
            var loaded = service.Load(tempFile);
            Assert.Equal("FtpSecretPassword!", loaded.Password);
            Assert.Equal("ProxySecretPassword!", loaded.ProxyPassword);
            Assert.Equal("SmtpSecretPassword!", loaded.SmtpPassword);
            Assert.Equal("FtpSecretPassword!", loaded.Profiles[0].Password);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VmManagerService_SaveAll_EncryptsVmPasswordOnDisk_AndAutoMigratesPlainText()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"vm_enc_test_{Guid.NewGuid():N}.json");
        try
        {
            // Giả lập file JSON cũ lưu mật khẩu plain-text
            string legacyJson = """
            [
              {
                "Id": "11111111-1111-1111-1111-111111111111",
                "No": "1",
                "Ip": "10.10.10.5",
                "MachineName": "SVR-DB01",
                "Username": "Administrator",
                "Password": "PlainTextVmPassword999"
              }
            ]
            """;
            File.WriteAllText(tempFile, legacyJson);

            var service = new VmManagerService(tempFile);

            // Khi LoadAll(), service phải đọc được plain-text VÀ tự động mã hóa lại file trên đĩa
            var loaded = service.LoadAll();
            Assert.Single(loaded);
            Assert.Equal("PlainTextVmPassword999", loaded[0].Password);

            string migratedJson = File.ReadAllText(tempFile);
            Assert.DoesNotContain("PlainTextVmPassword999", migratedJson);
            Assert.Contains(SecretProtector.EncryptedPrefix, migratedJson);

            // Load lần 2 từ file đã migrate
            var reloaded = service.LoadAll();
            Assert.Single(reloaded);
            Assert.Equal("PlainTextVmPassword999", reloaded[0].Password);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ProjectManagerService_SaveAll_EncryptsDbaPasswordOnDisk_AndAutoMigratesPlainText()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"proj_enc_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new ProjectManagerService(tempFile);
            var list = new List<ProjectEntry>
            {
                new()
                {
                    No = "101",
                    ProjectName = "PRJ_SEC",
                    HostServer = "172.16.1.10",
                    DbaUsername = "SYS",
                    DbaPassword = "OracleSysPassword#2026"
                }
            };

            service.SaveAll(list);

            // Trên RAM vẫn là plain-text
            Assert.Equal("OracleSysPassword#2026", list[0].DbaPassword);

            // Trên đĩa đã được mã hóa DPAPI
            string diskJson = File.ReadAllText(tempFile);
            Assert.DoesNotContain("OracleSysPassword#2026", diskJson);
            Assert.Contains(SecretProtector.EncryptedPrefix, diskJson);

            // Load lại giải mã chuẩn xác
            var loaded = service.LoadAll();
            Assert.Single(loaded);
            Assert.Equal("OracleSysPassword#2026", loaded[0].DbaPassword);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VmManagerService_PreservesIsFavoriteFlag_ForQuickSearchFilter()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"vm_fav_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new VmManagerService(tempFile);
            var vms = new List<VirtualMachineEntry>
            {
                new() { Ip = "10.0.0.1", MachineName = "FAV-VM", IsFavorite = true },
                new() { Ip = "10.0.0.2", MachineName = "NORMAL-VM", IsFavorite = false }
            };

            service.SaveAll(vms);
            var loaded = service.LoadAll();

            Assert.Equal(2, loaded.Count);
            Assert.True(loaded[0].IsFavorite);
            Assert.False(loaded[1].IsFavorite);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
