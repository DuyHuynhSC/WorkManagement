using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using WorkManagement.Helpers;
using WorkManagement.Models;

namespace WorkManagement.Services;

public class ProjectManagerService
{
    private readonly string _storagePath;
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProjectManagerService(string? customStoragePath = null)
    {
        if (!string.IsNullOrWhiteSpace(customStoragePath))
        {
            _storagePath = customStoragePath;
            _settingsPath = Path.Combine(Path.GetDirectoryName(customStoragePath) ?? string.Empty, "project_cleanup_settings.json");
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appDir = Path.Combine(appData, "WorkManagement");
            Directory.CreateDirectory(appDir);
            _storagePath = Path.Combine(appDir, "projects.json");
            _settingsPath = Path.Combine(appDir, "project_cleanup_settings.json");
        }

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public List<ProjectEntry> LoadAll()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                string json = File.ReadAllText(_storagePath);
                var list = JsonSerializer.Deserialize<List<ProjectEntry>>(json, _jsonOptions);
                if (list != null)
                {
                    bool hadPlaintextPassword = false;
                    foreach (var proj in list)
                    {
                        if (!string.IsNullOrEmpty(proj.DbaPassword) && !SecretProtector.IsEncrypted(proj.DbaPassword))
                        {
                            hadPlaintextPassword = true;
                        }
                        proj.DbaPassword = SecretProtector.Unprotect(proj.DbaPassword);
                    }

                    // Tự động nâng cấp file JSON cũ (chứa mật khẩu DBA plain-text) sang chuẩn mã hóa DPAPI
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
            // Trả về rỗng nếu file lỗi
        }

        return new List<ProjectEntry>();
    }

    public void SaveAll(IEnumerable<ProjectEntry> list)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Tạo bản sao có mật khẩu DBA đã mã hóa DPAPI để lưu xuống đĩa, giữ nguyên mật khẩu giải mã trên RAM
            var encryptedList = list.Select(p =>
            {
                var copy = p.Clone();
                copy.DbaPassword = SecretProtector.Protect(p.DbaPassword);
                return copy;
            }).ToList();

            string json = JsonSerializer.Serialize(encryptedList, _jsonOptions);
            File.WriteAllText(_storagePath, json);
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

    public ProjectCleanupSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<ProjectCleanupSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    return settings;
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
            string? dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(settings, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
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
