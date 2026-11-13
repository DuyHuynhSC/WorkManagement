using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class BackupFolderSettingsView : UserControl
{
    private ConfigService _configService;
    private AppLogger? _logger;

    public event EventHandler? SettingsSaved;

    public BackupFolderSettingsView()
    {
        InitializeComponent();
        _configService = new ConfigService();
        TxtFtpDefaultHint.Text = $"Mặc định: {BackupFolderSettings.DefaultFtpBackupPath}";
        TxtProjectDefaultHint.Text = $"Mặc định: {BackupFolderSettings.DefaultProjectBackupPath}";
        TxtLogDefaultHint.Text = $"Mặc định: {BackupFolderSettings.DefaultLogFolderPath}";
        Loaded += (_, _) => LoadSettings();
    }

    public void Initialize(ConfigService? configService, AppLogger? logger = null)
    {
        if (configService != null) _configService = configService;
        _logger = logger;
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            var folderSettings = _configService.GetBackupFolderSettings();
            TxtFtpBackupPath.Text = folderSettings.FtpBackupPath;
            TxtProjectBackupPath.Text = folderSettings.ProjectBackupPath;
            TxtLogFolderPath.Text = folderSettings.LogFolderPath;
        }
        catch (Exception ex)
        {
            _logger?.Error($"Lỗi khi tải cấu hình thư mục sao lưu: {ex.Message}");
        }
    }

    public void SetPaths(string ftp, string project, string log)
    {
        TxtFtpBackupPath.Text = ftp;
        TxtProjectBackupPath.Text = project;
        TxtLogFolderPath.Text = log;
    }

    public BackupFolderSettings ReadFromUi()
    {
        string ftp = string.IsNullOrWhiteSpace(TxtFtpBackupPath.Text)
            ? BackupFolderSettings.DefaultFtpBackupPath
            : TxtFtpBackupPath.Text.Trim();

        string proj = string.IsNullOrWhiteSpace(TxtProjectBackupPath.Text)
            ? BackupFolderSettings.DefaultProjectBackupPath
            : TxtProjectBackupPath.Text.Trim();

        string log = string.IsNullOrWhiteSpace(TxtLogFolderPath.Text)
            ? BackupFolderSettings.DefaultLogFolderPath
            : TxtLogFolderPath.Text.Trim();

        return new BackupFolderSettings
        {
            FtpBackupPath = ftp,
            ProjectBackupPath = proj,
            LogFolderPath = log
        };
    }

    private void BtnBrowseFtpBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục sao lưu khi xóa file trên FTP",
            InitialDirectory = Directory.Exists(TxtFtpBackupPath.Text.Trim()) ? TxtFtpBackupPath.Text.Trim() : string.Empty
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            TxtFtpBackupPath.Text = dialog.FolderName;
        }
    }

    private void BtnBrowseProjectBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục sao lưu mã nguồn dự án",
            InitialDirectory = Directory.Exists(TxtProjectBackupPath.Text.Trim()) ? TxtProjectBackupPath.Text.Trim() : string.Empty
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            TxtProjectBackupPath.Text = dialog.FolderName;
        }
    }

    private void BtnBrowseLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục lưu trữ nhật ký (Logs)",
            InitialDirectory = Directory.Exists(TxtLogFolderPath.Text.Trim()) ? TxtLogFolderPath.Text.Trim() : string.Empty
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            TxtLogFolderPath.Text = dialog.FolderName;
        }
    }

    private void BtnOpenFtpBackup_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(TxtFtpBackupPath.Text.Trim());
    }

    private void BtnOpenProjectBackup_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(TxtProjectBackupPath.Text.Trim());
    }

    private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(TxtLogFolderPath.Text.Trim());
    }

    private void OpenFolderInExplorer(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        try
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            Process.Start("explorer.exe", folderPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Không thể mở thư mục:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            "Bạn có chắc muốn khôi phục toàn bộ đường dẫn thư mục về mặc định ban đầu?",
            "Xác nhận",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            TxtFtpBackupPath.Text = BackupFolderSettings.DefaultFtpBackupPath;
            TxtProjectBackupPath.Text = BackupFolderSettings.DefaultProjectBackupPath;
            TxtLogFolderPath.Text = BackupFolderSettings.DefaultLogFolderPath;
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderSettings = ReadFromUi();

            // Đảm bảo tạo thư mục nếu chưa có
            EnsureDirectoryExists(folderSettings.FtpBackupPath);
            EnsureDirectoryExists(folderSettings.ProjectBackupPath);
            EnsureDirectoryExists(folderSettings.LogFolderPath);

            _configService.SaveBackupFolderSettings(folderSettings);
            _logger?.SetLogDirectory(folderSettings.LogFolderPath);
            _logger?.Info($"Đã lưu và đồng bộ cấu hình thư mục sao lưu & log: FTP='{folderSettings.FtpBackupPath}', Project='{folderSettings.ProjectBackupPath}', Logs='{folderSettings.LogFolderPath}'");

            TxtStatusMessage.Text = "Đã lưu thành công!";
            TxtStatusMessage.Visibility = Visibility.Visible;

            SettingsSaved?.Invoke(this, EventArgs.Empty);

            MessageBox.Show(
                Window.GetWindow(this),
                "Đã lưu thành công cấu hình thư mục sao lưu và nhật ký!",
                "Thông Báo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Lỗi khi lưu cấu hình thư mục: {ex.Message}");
            MessageBox.Show(
                Window.GetWindow(this),
                $"Không thể lưu cấu hình thư mục:\n{ex.Message}",
                "Lỗi",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void EnsureDirectoryExists(string dirPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(dirPath) && !Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }
        }
        catch { }
    }
}
