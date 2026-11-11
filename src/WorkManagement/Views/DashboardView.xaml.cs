using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public class ExpiringVmRow
{
    public string Ip { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string Tester { get; set; } = string.Empty;
    public string ExpirationDate { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public int DaysRemaining { get; set; }
}

public class MissingSourceFolderRow
{
    public string No { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string SourceFolder { get; set; } = string.Empty;
    public string PersonInCharge { get; set; } = string.Empty;
    public string StatusText { get; set; } = "Thư mục không tồn tại";
}

public partial class DashboardView : UserControl
{
    private readonly VmManagerService _vmService = new();
    private readonly ProjectManagerService _projectService = new();
    private readonly ConfigService _configService = new();
    private readonly ObservableCollection<ExpiringVmRow> _expiringVms = new();
    private readonly ObservableCollection<MissingSourceFolderRow> _missingSourceFolders = new();

    public event EventHandler? OpenCommandPaletteRequested;

    public DashboardView()
    {
        InitializeComponent();
        GridExpiringVms.ItemsSource = _expiringVms;
        GridMissingSourceFolders.ItemsSource = _missingSourceFolders;
    }

    public void LoadDashboard()
    {
        try
        {
            var vms = _vmService.LoadAll();
            var projects = _projectService.LoadAll();
            var projCleanupSettings = _projectService.LoadSettings();
            var ftpSettings = _configService.Load();

            // 1. VM KPI & Expiring VMs
            _expiringVms.Clear();
            DateTime today = DateTime.Today;

            foreach (var vm in vms)
            {
                if (TryParseExpirationDate(vm.ExpirationDate, out DateTime expDate))
                {
                    int days = (expDate.Date - today).Days;
                    if (days <= 14)
                    {
                        string status = days < 0
                            ? $"Đã quá hạn {Math.Abs(days)} ngày"
                            : (days == 0 ? "Hết hạn hôm nay!" : $"Còn {days} ngày");

                        _expiringVms.Add(new ExpiringVmRow
                        {
                            Ip = vm.Ip,
                            MachineName = vm.MachineName,
                            Tester = vm.Tester,
                            ExpirationDate = vm.ExpirationDate,
                            StatusText = status,
                            DaysRemaining = days
                        });
                    }
                }
            }

            var sortedExpiring = _expiringVms.OrderBy(x => x.DaysRemaining).ToList();
            _expiringVms.Clear();
            foreach (var item in sortedExpiring) _expiringVms.Add(item);

            TxtKpiVmTotal.Text = vms.Count.ToString();
            TxtKpiVmSub.Text = _expiringVms.Count > 0
                ? $"⚠️ {_expiringVms.Count} máy sắp/đã hết hạn"
                : "✅ Tất cả còn hạn sử dụng";

            // 2. Project KPI & Missing Q:\ Source Folders
            int distinctDbHosts = projects
                .Select(p => p.HostServer?.Trim())
                .Where(h => !string.IsNullOrEmpty(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            _missingSourceFolders.Clear();
            var missingFolders = CheckMissingSourceFolders(projects);
            foreach (var mf in missingFolders)
            {
                _missingSourceFolders.Add(mf);
            }

            TxtKpiProjectTotal.Text = projects.Count.ToString();
            TxtKpiProjectSub.Text = _missingSourceFolders.Count > 0
                ? $"{distinctDbHosts} DB servers | ⚠️ {_missingSourceFolders.Count} folder Q: lỗi"
                : $"{distinctDbHosts} máy chủ Oracle DB";

            if (TxtMissingFoldersCountBadge != null)
            {
                TxtMissingFoldersCountBadge.Text = _missingSourceFolders.Count > 0
                    ? $"⚠️ Phát hiện {_missingSourceFolders.Count} thư mục không tồn tại"
                    : string.Empty;
                TxtMissingFoldersCountBadge.Visibility = _missingSourceFolders.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            // 3. FTP Profiles & Security KPI
            int profileCount = ftpSettings.Profiles?.Count ?? 1;
            TxtKpiFtpProfiles.Text = profileCount.ToString();
            TxtKpiSecuritySub.Text = "Bảo vệ mật khẩu chuẩn DPAPI";

            // 4. Local Backup & Log Storage KPI
            string ftpBackupDir = ftpSettings.LocalBackupPath;
            string projBackupDir = projCleanupSettings.BackupRootDirectory;
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

            var (ftpSize, ftpFiles) = GetDirectoryStats(ftpBackupDir);
            var (projSize, projFiles) = GetDirectoryStats(projBackupDir);
            var (logSize, logFiles) = GetDirectoryStats(logDir);

            long totalBytes = ftpSize + projSize + logSize;
            int totalFiles = ftpFiles + projFiles + logFiles;

            TxtKpiBackupSize.Text = FileManagerEntry.FormatFileSize(totalBytes);
            TxtKpiBackupSub.Text = $"{totalFiles:N0} tập tin lưu trữ cục bộ";

            TxtFtpBackupFolderInfo.Text = $"{FileManagerEntry.FormatFileSize(ftpSize)} ({ftpFiles} file)";
            TxtProjBackupFolderInfo.Text = $"{FileManagerEntry.FormatFileSize(projSize)} ({projFiles} file)";
            TxtLogFolderInfo.Text = $"{FileManagerEntry.FormatFileSize(logSize)} ({logFiles} file)";

            LoadAuditLogFiles(logDir);
            TxtLastRefreshed.Text = $"Cập nhật lúc: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        }
        catch
        {
        }
    }

    public static bool TryParseExpirationDate(string? raw, out DateTime parsed)
    {
        parsed = DateTime.MaxValue;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string trimmed = raw.Trim();
        if (trimmed.Contains("vô hạn", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("vo han", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("unlimited", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] formats = { "yyyy/MM/dd", "yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy", "M/d/yyyy" };
        if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            return true;
        }

        return DateTime.TryParse(trimmed, out parsed);
    }

    public static bool IsQDrivePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string trimmed = path.Trim();
        return trimmed.StartsWith(@"Q:\", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith(@"Q:/", StringComparison.OrdinalIgnoreCase);
    }

    public static List<MissingSourceFolderRow> CheckMissingSourceFolders(IEnumerable<ProjectEntry> projects, Func<string, bool>? directoryExists = null)
    {
        directoryExists ??= Directory.Exists;
        var result = new List<MissingSourceFolderRow>();
        if (projects == null) return result;

        foreach (var p in projects)
        {
            if (IsQDrivePath(p.SourceFolder))
            {
                string folder = p.SourceFolder.Trim();
                bool exists;
                try
                {
                    exists = directoryExists(folder);
                }
                catch
                {
                    exists = false;
                }

                if (!exists)
                {
                    result.Add(new MissingSourceFolderRow
                    {
                        No = p.No,
                        ProjectName = p.ProjectName,
                        SourceFolder = folder,
                        PersonInCharge = p.PersonInCharge,
                        StatusText = "Thư mục không tồn tại"
                    });
                }
            }
        }

        return result;
    }

    private static (long TotalBytes, int FileCount) GetDirectoryStats(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return (0, 0);
        }

        try
        {
            long bytes = 0;
            int count = 0;
            var dirInfo = new DirectoryInfo(path);
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += file.Length;
                    count++;
                }
                catch { }
            }
            return (bytes, count);
        }
        catch
        {
            return (0, 0);
        }
    }

    private void LoadAuditLogFiles(string logDir)
    {
        if (!Directory.Exists(logDir))
        {
            CboLogFiles.ItemsSource = null;
            TxtAuditLogViewer.Text = "[Chưa có file nhật ký nào]";
            return;
        }

        var logFiles = Directory.GetFiles(logDir, "*.log")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .Select(Path.GetFileName)
            .ToList();

        CboLogFiles.ItemsSource = logFiles;
        if (logFiles.Count > 0)
        {
            CboLogFiles.SelectedIndex = 0;
        }
        else
        {
            TxtAuditLogViewer.Text = "[Chưa có file nhật ký nào trong thư mục logs]";
        }
    }

    private void CboLogFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboLogFiles.SelectedItem is not string fileName) return;

        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", fileName);
        if (!File.Exists(logPath)) return;

        try
        {
            // Đọc tối đa 250 dòng cuối để mượt mà
            var lines = File.ReadAllLines(logPath);
            var tail = lines.Length > 250 ? lines.Skip(lines.Length - 250) : lines;
            TxtAuditLogViewer.Text = string.Join(Environment.NewLine, tail);
            TxtAuditLogViewer.ScrollToEnd();
        }
        catch (Exception ex)
        {
            TxtAuditLogViewer.Text = $"[Không thể đọc file log: {ex.Message}]";
        }
    }

    private void BtnRefreshDashboard_Click(object sender, RoutedEventArgs e)
    {
        LoadDashboard();
    }

    private void BtnOpenCommandPalette_Click(object sender, RoutedEventArgs e)
    {
        OpenCommandPaletteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnCleanOldLocalBackups_Click(object sender, RoutedEventArgs e)
    {
        int keepDays = 30;
        if (CboRetentionLocalDays.SelectedItem is ComboBoxItem cbi &&
            int.TryParse(cbi.Tag?.ToString(), out int parsedDays))
        {
            keepDays = parsedDays;
        }

        var parent = Window.GetWindow(this);
        var confirm = MessageBox.Show(
            parent,
            $"Bạn có chắc chắn muốn xóa các file Backup và Log cục bộ đã cũ hơn {keepDays} ngày không?",
            "Xác nhận dọn dẹp ổ đĩa Backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        DateTime cutoff = DateTime.Now.AddDays(-keepDays);
        var ftpSettings = _configService.Load();
        var projSettings = _projectService.LoadSettings();
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        var (del1, bytes1) = CleanDirectoryOlderThan(ftpSettings.LocalBackupPath, cutoff);
        var (del2, bytes2) = CleanDirectoryOlderThan(projSettings.BackupRootDirectory, cutoff);
        var (del3, bytes3) = CleanDirectoryOlderThan(logDir, cutoff);

        int totalDeleted = del1 + del2 + del3;
        long totalFreed = bytes1 + bytes2 + bytes3;

        LoadDashboard();
        MessageBox.Show(
            parent,
            $"Hoàn tất dọn dẹp bản sao lưu cục bộ cũ hơn {keepDays} ngày!\n- Đã xóa: {totalDeleted} file\n- Dung lượng giải phóng: {FileManagerEntry.FormatFileSize(totalFreed)}",
            "Kết quả Dọn Dẹp",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public static (int DeletedFiles, long FreedBytes) CleanDirectoryOlderThan(string? rootDir, DateTime cutoff)
    {
        if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
        {
            return (0, 0);
        }

        int deleted = 0;
        long freed = 0;

        try
        {
            var dirInfo = new DirectoryInfo(rootDir);
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).ToList())
            {
                try
                {
                    if (file.LastWriteTime < cutoff)
                    {
                        long len = file.Length;
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                        deleted++;
                        freed += len;
                    }
                }
                catch { }
            }

            // Xóa các thư mục con rỗng sau khi dọn file
            foreach (var subDir in dirInfo.EnumerateDirectories("*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.FullName.Length))
            {
                try
                {
                    if (!subDir.EnumerateFileSystemInfos().Any())
                    {
                        subDir.Delete();
                    }
                }
                catch { }
            }
        }
        catch { }

        return (deleted, freed);
    }

    private void BtnOpenFtpBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var ftpSettings = _configService.Load();
        OpenLocalDirectory(ftpSettings.LocalBackupPath);
    }

    private void BtnOpenProjectBackupFolder_Click(object sender, RoutedEventArgs e)
    {
        var projSettings = _projectService.LoadSettings();
        OpenLocalDirectory(projSettings.BackupRootDirectory);
    }

    private void BtnOpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        OpenLocalDirectory(logDir);
    }

    private void OpenLocalDirectory(string? dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath)) return;
        try
        {
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }
            Process.Start("explorer.exe", dirPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Không thể mở thư mục:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
