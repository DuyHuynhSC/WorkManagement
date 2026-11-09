using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using WorkManagement.Helpers;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class VmManagerService
{
    private readonly string _storagePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public VmManagerService(string? customStoragePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customStoragePath))
        {
            _storagePath = customStoragePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "WorkManagement");
            Directory.CreateDirectory(appDir);
            _storagePath = Path.Combine(appDir, "virtual_machines.json");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public List<VirtualMachineEntry> LoadAll()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                string json = File.ReadAllText(_storagePath);
                var list = JsonSerializer.Deserialize<List<VirtualMachineEntry>>(json, _jsonOptions);
                if (list != null)
                {
                    bool hadPlaintextPassword = false;
                    foreach (var vm in list)
                    {
                        if (!string.IsNullOrEmpty(vm.Password) && !SecretProtector.IsEncrypted(vm.Password))
                        {
                            hadPlaintextPassword = true;
                        }
                        if (!string.IsNullOrEmpty(vm.DbaPassword) && !SecretProtector.IsEncrypted(vm.DbaPassword))
                        {
                            hadPlaintextPassword = true;
                        }
                        vm.Password = SecretProtector.Unprotect(vm.Password);
                        vm.DbaPassword = SecretProtector.Unprotect(vm.DbaPassword);
                    }

                    // Tự động nâng cấp file JSON cũ (chứa mật khẩu plain-text) sang chuẩn mã hóa DPAPI
                    if (hadPlaintextPassword)
                    {
                        try
                        {
                            SaveAll(list);
                        }
                        catch { }
                    }

                    return list;
                }
            }
        }
        catch
        {
            // Trả về danh sách rỗng nếu file bị lỗi hoặc không thể đọc
        }

        return new List<VirtualMachineEntry>();
    }

    public void ClearAll()
    {
        SaveAll(new List<VirtualMachineEntry>());
    }

    public void SaveAll(IEnumerable<VirtualMachineEntry> list)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Tạo bản sao có mật khẩu đã mã hóa DPAPI để lưu xuống đĩa, giữ nguyên mật khẩu giải mã trên RAM
            var encryptedList = list.Select(vm =>
            {
                var copy = vm.Clone();
                copy.Password = SecretProtector.Protect(vm.Password);
                copy.DbaPassword = SecretProtector.Protect(vm.DbaPassword);
                return copy;
            }).ToList();

            string json = JsonSerializer.Serialize(encryptedList, _jsonOptions);
            File.WriteAllText(_storagePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không thể lưu dữ liệu máy ảo: {ex.Message}", ex);
        }
    }
}
