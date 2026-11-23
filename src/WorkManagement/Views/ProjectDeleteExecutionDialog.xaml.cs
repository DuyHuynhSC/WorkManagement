using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class ProjectDeleteExecutionDialog : Window
{
    private readonly List<ProjectEntry> _projectsToDelete;
    private readonly ProjectManagerService _projectService;
    private readonly ProjectCleanupService _cleanupService;
    private readonly ProjectCleanupSettings _settings;
    private CancellationTokenSource? _cts;
    private bool _isExecuting;
    private bool _isPasswordRevealed;
    private Dictionary<string, List<string>>? _selectedUsersByProjectId;
    public bool HasDeletedProjects { get; private set; }

    public ProjectDeleteExecutionDialog(List<ProjectEntry> projects, ProjectManagerService projectService)
    {
        InitializeComponent();
        _projectsToDelete = projects;
        _projectService = projectService;
        _cleanupService = new ProjectCleanupService();
        _settings = _projectService.LoadSettings();

        _cleanupService.OnProgress += CleanupService_OnProgress;

        InitDialog();
    }

    private void InitDialog()
    {
        TxtSelectedProjectsSummary.Text = $"Dự án chọn xóa: {string.Join(", ", _projectsToDelete.Select(x => x.ProjectName))} ({_projectsToDelete.Count} dự án)";

        TxtBackupDir.Text = _settings.BackupRootDirectory;
        ChkBackupFolder.IsChecked = _settings.EnableBackupFolder;
        ChkBackupDatabase.IsChecked = _settings.EnableBackupDatabase;
        ChkDeleteFolder.IsChecked = _settings.EnableDeleteFolder;
        ChkDeleteUsers.IsChecked = _settings.EnableDeleteUsers;
        ChkDeleteTablespace.IsChecked = _settings.EnableDeleteTablespace;

        // Điền trước thông tin DBA từ dự án đầu tiên nếu có
        var first = _projectsToDelete.FirstOrDefault();
        string firstDbaUser = !string.IsNullOrWhiteSpace(first?.ResolvedDbaUser) ? first.ResolvedDbaUser : (first?.DbaUsername ?? string.Empty);
        string firstDbaPass = !string.IsNullOrWhiteSpace(first?.ResolvedDbaPassword) ? first.ResolvedDbaPassword : (first?.DbaPassword ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(firstDbaUser))
        {
            TxtDbaUser.Text = firstDbaUser;
            PbDbaPassword.Password = firstDbaPass;
            TxtDbaPasswordRevealed.Text = firstDbaPass;
        }
        else
        {
            TxtDbaUser.Text = _settings.DefaultDbaUser;
        }

        UpdateSelectedUsersBadge();
    }

    private void UpdateSelectedUsersBadge()
    {
        int totalUsers = _projectsToDelete.Sum(p => ProjectCleanupService.ParseUserList(p.DbUsers).Count);
        int selectedUsers = _selectedUsersByProjectId != null
            ? _selectedUsersByProjectId.Values.Sum(list => list.Count)
            : totalUsers;

        TxtSelectedUsersBadge.Text = totalUsers > 0 ? $"({selectedUsers}/{totalUsers} user)" : "(0 user)";
        BtnSelectUsers.IsEnabled = ChkDeleteUsers.IsChecked == true && totalUsers > 0;
    }

    private void ChkDeleteUsers_CheckedChanged(object sender, RoutedEventArgs e)
    {
        UpdateSelectedUsersBadge();
    }

    private void BtnSelectUsers_Click(object sender, RoutedEventArgs e)
    {
        var selectDialog = new SelectUsersToDeleteDialog(_projectsToDelete, _selectedUsersByProjectId)
        {
            Owner = this
        };

        if (selectDialog.ShowDialog() == true)
        {
            _selectedUsersByProjectId = selectDialog.GetSelectedUsersByProjectId();
            UpdateSelectedUsersBadge();
        }
    }

    private void CleanupService_OnProgress(object? sender, ProjectCleanupProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressBarExecution.Value = e.ProgressPercentage;
            TxtProgressStatus.Text = $"Trạng thái: {e.Message}";

            string line = $"[{DateTime.Now:HH:mm:ss}] {(e.IsError ? "[LỖI] " : "")}{e.Message}\n";
            TxtLogOutput.AppendText(line);
            ScrollerLog.ScrollToEnd();
        });
    }

    private void BtnChooseBackupDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục lưu Backup",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            TxtBackupDir.Text = dialog.FolderName;
        }
    }

    private void BtnToggleDbaPassword_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            TxtDbaPasswordRevealed.Text = PbDbaPassword.Password;
            PbDbaPassword.Visibility = Visibility.Collapsed;
            TxtDbaPasswordRevealed.Visibility = Visibility.Visible;
            IconDbaPassEye.Kind = PackIconKind.EyeOffOutline;
        }
        else
        {
            PbDbaPassword.Password = TxtDbaPasswordRevealed.Text;
            TxtDbaPasswordRevealed.Visibility = Visibility.Collapsed;
            PbDbaPassword.Visibility = Visibility.Visible;
            IconDbaPassEye.Kind = PackIconKind.EyeOutline;
        }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        TxtLogOutput.Text = string.Empty;
    }

    private async void BtnStartCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (_isExecuting) return;

        // Lưu cài đặt cho lần sau
        _settings.BackupRootDirectory = TxtBackupDir.Text.Trim();
        _settings.EnableBackupFolder = ChkBackupFolder.IsChecked == true;
        _settings.EnableBackupDatabase = ChkBackupDatabase.IsChecked == true;
        _settings.EnableDeleteFolder = ChkDeleteFolder.IsChecked == true;
        _settings.EnableDeleteUsers = ChkDeleteUsers.IsChecked == true;
        _settings.EnableDeleteTablespace = ChkDeleteTablespace.IsChecked == true;
        _settings.DefaultDbaUser = TxtDbaUser.Text.Trim();
        _projectService.SaveSettings(_settings);

        string dbaUser = TxtDbaUser.Text.Trim();
        string dbaPass = _isPasswordRevealed ? TxtDbaPasswordRevealed.Text : PbDbaPassword.Password;

        if ((_settings.EnableDeleteUsers || _settings.EnableDeleteTablespace || _settings.EnableBackupDatabase) &&
            string.IsNullOrWhiteSpace(dbaUser))
        {
            MessageBox.Show(this, "Vui lòng nhập Tài khoản DBA để kết nối Oracle.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Nếu người dùng chọn xóa User trong Oracle DB, mở màn hình xác nhận và chọn từng user để tránh xóa nhầm
        if (_settings.EnableDeleteUsers)
        {
            int totalDbUsers = _projectsToDelete.Sum(p => ProjectCleanupService.ParseUserList(p.DbUsers).Count);
            if (totalDbUsers > 0 && _selectedUsersByProjectId == null)
            {
                var selectDialog = new SelectUsersToDeleteDialog(_projectsToDelete, _selectedUsersByProjectId)
                {
                    Owner = this
                };

                if (selectDialog.ShowDialog() != true)
                {
                    // Người dùng hủy ở màn hình chọn User
                    return;
                }

                _selectedUsersByProjectId = selectDialog.GetSelectedUsersByProjectId();
                UpdateSelectedUsersBadge();
            }
        }

        var confirm = MessageBox.Show(this,
            $"CẢNH BÁO QUAN TRỌNG:\nBạn có chắc chắn muốn tiến hành xóa {_projectsToDelete.Count} dự án đã chọn?\n\nThao tác này sẽ thực thi tuần tự xóa Source Folder trên ổ mạng, xóa User và xóa Tablespace trong Database!",
            "Xác nhận hành động xóa",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _isExecuting = true;
        BtnStartCleanup.IsEnabled = false;
        BtnClose.Content = "Dừng Lại";
        _cts = new CancellationTokenSource();

        try
        {
            int successCount = 0;
            bool wasCancelled = false;

            for (int i = 0; i < _projectsToDelete.Count; i++)
            {
                if (_cts?.IsCancellationRequested == true)
                {
                    wasCancelled = true;
                    break;
                }

                var proj = _projectsToDelete[i];
                // Sử dụng mật khẩu riêng của từng dự án nếu có, nếu không thì dùng mật khẩu vừa nhập trên form
                string userForThis = !string.IsNullOrWhiteSpace(proj.ResolvedDbaUser) ? proj.ResolvedDbaUser : (!string.IsNullOrWhiteSpace(proj.DbaUsername) ? proj.DbaUsername : dbaUser);
                string passForThis = !string.IsNullOrWhiteSpace(proj.ResolvedDbaPassword) ? proj.ResolvedDbaPassword : (!string.IsNullOrWhiteSpace(proj.DbaPassword) ? proj.DbaPassword : dbaPass);

                List<string>? specificUsers = null;
                if (_selectedUsersByProjectId != null && _selectedUsersByProjectId.TryGetValue(proj.Id, out var uList))
                {
                    specificUsers = uList;
                }

                bool ok = await _cleanupService.ExecuteProjectCleanupAsync(
                    proj,
                    _settings,
                    userForThis,
                    passForThis,
                    specificUsers,
                    _cts!.Token);

                if (_cts?.IsCancellationRequested == true)
                {
                    wasCancelled = true;
                    break;
                }

                if (ok)
                {
                    successCount++;
                    HasDeletedProjects = true;
                }
            }

            if (wasCancelled)
            {
                MessageBox.Show(this,
                    "Quy trình dọn dẹp đã được dừng lại theo yêu cầu của bạn.",
                    "Đã dừng",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    $"Quy trình dọn dẹp đã kết thúc!\nĐã xử lý: {successCount}/{_projectsToDelete.Count} dự án thành công.\nVui lòng kiểm tra màn hình Log để xem chi tiết.",
                    "Hoàn tất dọn dẹp",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(this, "Quy trình dọn dẹp đã được dừng lại theo yêu cầu.", "Đã dừng", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (_cts?.IsCancellationRequested == true || ex.Message.Contains("ORA-01013"))
        {
            MessageBox.Show(this, "Quy trình dọn dẹp đã được dừng lại theo yêu cầu.", "Đã dừng", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Lỗi trong quá trình dọn dẹp: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isExecuting = false;
            BtnStartCleanup.IsEnabled = true;
            BtnClose.Content = "Đóng";
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_isExecuting)
        {
            var confirm = MessageBox.Show(this, "Bạn có muốn dừng quy trình dọn dẹp đang chạy không?", "Xác nhận dừng", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                _cts?.Cancel();
            }
            return;
        }

        DialogResult = HasDeletedProjects;
        Close();
    }

    private void LogMessage(string message)
    {
        TxtLogOutput.AppendText(message + "\n");
        ScrollerLog.ScrollToEnd();
    }

    private async void BtnAiSafetyCheck_Click(object sender, RoutedEventArgs e)
    {
        BtnAiSafetyCheck.IsEnabled = false;
        AiSafetyBusyBar.Visibility = Visibility.Visible;
        TxtAiSafetyLabel.Text = "Đang phân tích...";

        try
        {
            var configService = new ConfigService();
            var settings = configService.Load();
            var allProjects = _projectService.LoadAll();
            var aiClient = new OpenAiClientService();

            LogMessage("\n================================================================================");
            LogMessage($"[AI SAFETY ADVISOR] BẮT ĐẦU ĐÁNH GIÁ RỦI RO CHO {_projectsToDelete.Count} DỰ ÁN...");
            LogMessage("================================================================================");

            var result = await aiClient.AnalyzeCleanupSafetyAsync(settings.Ai, _projectsToDelete, allProjects);

            LogMessage($"MỨC ĐỘ RỦI RO: [{result.RiskLevel.ToString().ToUpperInvariant()}]");
            LogMessage($"TỔNG QUAN: {result.Summary}");

            if (result.Warnings.Count > 0)
            {
                LogMessage("CÁC CẢNH BÁO PHÁT HIỆN:");
                foreach (var w in result.Warnings)
                {
                    LogMessage($"  [!] {w}");
                }
            }

            if (result.Recommendations.Count > 0)
            {
                LogMessage("KHUYẾN NGHỊ BẢO VỆ DỮ LIỆU:");
                foreach (var r in result.Recommendations)
                {
                    LogMessage($"  [*] {r}");
                }
            }
            LogMessage("================================================================================\n");

            var icon = result.RiskLevel switch
            {
                DatabaseRiskLevel.High => MessageBoxImage.Error,
                DatabaseRiskLevel.Medium => MessageBoxImage.Warning,
                _ => MessageBoxImage.Information
            };

            string title = result.RiskLevel switch
            {
                DatabaseRiskLevel.High => "CẢNH BÁO RỦI RO CAO - DATABASE",
                DatabaseRiskLevel.Medium => "CẢNH BÁO CẦN LƯU Ý - DATABASE",
                _ => "ĐÁNH GIÁ AN TOÀN - DATABASE"
            };

            string msg = $"{result.Summary}\n\n" +
                         (result.Warnings.Count > 0 ? $"Cảnh báo ({result.Warnings.Count}):\n- {string.Join("\n- ", result.Warnings.Take(3))}\n\n" : "") +
                         $"Xem chi tiết trong Nhật Ký Thực Thi bên dưới.";

            MessageBox.Show(this, msg, title, MessageBoxButton.OK, icon);
        }
        catch (Exception ex)
        {
            LogMessage($"[AI SAFETY ERROR] Lỗi khi phân tích an toàn: {ex.Message}");
            MessageBox.Show(this, $"Lỗi: {ex.Message}", "Lỗi Phân Tích AI", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnAiSafetyCheck.IsEnabled = true;
            AiSafetyBusyBar.Visibility = Visibility.Collapsed;
            TxtAiSafetyLabel.Text = "AI Cố Vấn An Toàn";
        }
    }
}
