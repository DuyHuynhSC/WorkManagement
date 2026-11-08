using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkManagement.Models;
using WorkManagement.Services;
using WorkManagement.Views;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace WorkManagement;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly AppLogger _logger;
    private readonly FtpCleanupService _ftpService;
    private readonly EmailNotificationService _emailService;
    private readonly ObservableCollection<FtpFileCandidate> _candidates = new();
    private readonly ObservableCollection<FileManagerEntry> _fmEntries = new();
    private readonly ObservableCollection<FileManagerEntry> _fmDisplayEntries = new();
    private string _currentFmRemotePath = "/";
    private bool _fmLoadedOnce;
    private FtpSettings _currentSettings = new();
    private bool _suppressProfileSelectionChanged;
    private bool _suppressThemeChanged;
    private readonly List<string> _selectedScanProfileNames = new();
    private SystemTrayService? _systemTrayService;
    private bool _isExplicitExit;
    private bool _hasShownTrayBalloonOnce;
    private bool _isCommandPaletteOpen;

    public SystemTrayService? TrayService => _systemTrayService;

    public MainWindow() : this(new ConfigService(), new AppLogger())
    {
    }

    private MainWindow(ConfigService configService, AppLogger logger)
        : this(configService, logger, new FtpCleanupService(logger), new EmailNotificationService(logger))
    {
    }

    public MainWindow(ConfigService configService, AppLogger logger, FtpCleanupService ftpService, EmailNotificationService? emailService = null)
    {
        InitializeComponent();
        this.WindowState = System.Windows.WindowState.Maximized;
        _configService = configService;
        _logger = logger;
        _ftpService = ftpService;
        _emailService = emailService ?? new EmailNotificationService(logger);

        GridCandidates.ItemsSource = _candidates;
        GridFileManager.ItemsSource = _fmDisplayEntries;

        _logger.OnLogMessage += line =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtLogs.AppendText(line + Environment.NewLine);
                TxtLogs.ScrollToEnd();
            });
        };

        _currentSettings = _configService.Load();
        _selectedScanProfileNames.Clear();
        if (_currentSettings.SelectedScanProfileNames != null && _currentSettings.SelectedScanProfileNames.Count > 0)
        {
            _selectedScanProfileNames.AddRange(_currentSettings.SelectedScanProfileNames);
        }
        else if (!string.IsNullOrWhiteSpace(_currentSettings.SelectedProfileName))
        {
            _selectedScanProfileNames.Add(_currentSettings.SelectedProfileName);
        }
        CboThemes.ItemsSource = ThemeService.AvailableThemes;
        RefreshProfileComboBox(_currentSettings.SelectedProfileName);
        UpdateScanProfilesButtonLabel();
        var initialTheme = ThemeService.ParseTheme(_currentSettings.ThemeName, _currentSettings.IsDarkTheme);
        ApplyTheme(initialTheme);

        FtpConfigViewControl.Initialize(_currentSettings, _configService, _ftpService, _logger);
        FtpConfigViewControl.SettingsSaved += FtpConfigViewControl_SettingsSaved;

        EmailConfigViewControl.Initialize(_currentSettings, _emailService, () => _candidates);
        EmailConfigViewControl.SettingsSaved += EmailConfigViewControl_SettingsSaved;

        DashboardViewControl.OpenCommandPaletteRequested += (_, _) => OpenCommandPalette();

        _systemTrayService = new SystemTrayService(
            this,
            _configService,
            _currentSettings,
            _logger,
            RestoreFromTray,
            OpenCommandPalette,
            TriggerPreviewScanFromTray,
            ExitApplicationExplicitly);
    }

    public void InitializeSystemTray(bool startMinimized = false)
    {
        _systemTrayService?.Initialize();
        if (startMinimized)
        {
            MinimizeToTray(showNotification: true);
        }
    }

    public void MinimizeToTray(bool showNotification = true)
    {
        _systemTrayService?.Initialize();
        _systemTrayService?.SetTrayGlobalHotkeyEnabled(true);
        ShowInTaskbar = false;
        Hide();

        if (showNotification && !_hasShownTrayBalloonOnce)
        {
            _hasShownTrayBalloonOnce = true;
            _systemTrayService?.ShowBalloonNotification(
                "WorkManagement đang chạy ngầm",
                "Nhấn phím tắt Ctrl + K (hoặc Ctrl + Alt + K) bất cứ lúc nào để mở nhanh Command Palette, hoặc nhấp chuột phải vào biểu tượng Tray.");
        }
    }

    public void RestoreFromTray()
    {
        _systemTrayService?.SetTrayGlobalHotkeyEnabled(false);
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Maximized;
        }
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void ExitApplicationExplicitly()
    {
        _isExplicitExit = true;
        _systemTrayService?.Dispose();
        Close();
        Application.Current?.Shutdown();
    }

    private void TriggerPreviewScanFromTray()
    {
        SwitchToView(AppNavView.FtpCleaner);
        MainTabControl.SelectedIndex = 0;
        BtnPreview_Click(this, new RoutedEventArgs());
    }

    private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToTray(showNotification: true);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit && _currentSettings.MinimizeToTrayOnClose && _systemTrayService is { IsInitialized: true })
        {
            e.Cancel = true;
            MinimizeToTray(showNotification: true);
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _systemTrayService?.Dispose();
    }

    private void FtpConfigViewControl_SettingsSaved(object? sender, EventArgs e)
    {
        RefreshProfileComboBox(_currentSettings.SelectedProfileName);
        _selectedScanProfileNames.RemoveAll(name => !_currentSettings.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));
        if (_selectedScanProfileNames.Count == 0 && !string.IsNullOrWhiteSpace(_currentSettings.SelectedProfileName))
        {
            _selectedScanProfileNames.Add(_currentSettings.SelectedProfileName);
        }
        _currentSettings.SelectedScanProfileNames = new List<string>(_selectedScanProfileNames);
        _configService.Save(_currentSettings);
        UpdateScanProfilesButtonLabel();
        LoadSettingsToUi(_currentSettings);
        _logger.Info($"Đã cập nhật và đồng bộ cấu hình Profile '{_currentSettings.SelectedProfileName}' sang giao diện FTP Cleaner.");
    }

    private void EmailConfigViewControl_SettingsSaved(object? sender, EventArgs e)
    {
        _configService.Save(_currentSettings);
        _logger.Info($"Đã lưu cấu hình Email chung cho tất cả Profile (SMTP: {_currentSettings.SmtpHost}:{_currentSettings.SmtpPort}, Số email nhận: {_currentSettings.RecipientEmails.Count}).");
    }

    public void SetDarkTheme(bool isDark) =>
        SetTheme(isDark ? AppThemeKind.MaterialDark : AppThemeKind.MaterialLight);

    public void SetTheme(AppThemeKind kind)
    {
        _currentSettings.ThemeName = kind.ToString();
        _currentSettings.IsDarkTheme = ThemeService.GetThemeOption(kind).IsDarkBase;
        ApplyTheme(kind);
    }

    private void ApplyTheme(AppThemeKind kind)
    {
        _suppressThemeChanged = true;
        try
        {
            ThemeService.ApplyTheme(kind);
            var option = ThemeService.GetThemeOption(kind);
            CboThemes.SelectedItem = option;

            if (Enum.TryParse<PackIconKind>(option.IconKind, out var parsedIcon))
            {
                IconThemeMode.Kind = parsedIcon;
            }
        }
        finally
        {
            _suppressThemeChanged = false;
        }
    }

    private void CboThemes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressThemeChanged) return;
        if (CboThemes.SelectedItem is not AppThemeOption selectedTheme) return;

        _currentSettings.ThemeName = selectedTheme.Kind.ToString();
        _currentSettings.IsDarkTheme = selectedTheme.IsDarkBase;
        ApplyTheme(selectedTheme.Kind);

        try
        {
            _configService.Save(_currentSettings);
            _logger.Info($"Đã chuyển giao diện sang Theme: '{selectedTheme.DisplayName}'.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Không thể lưu cấu hình Theme: {ex.Message}");
        }
    }
    private void RefreshProfileComboBox(string? selectedProfileName)
    {
        _suppressProfileSelectionChanged = true;
        try
        {
            CboProfiles.ItemsSource = null;
            CboProfiles.ItemsSource = _currentSettings.Profiles;
            var matched = _currentSettings.Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, selectedProfileName, StringComparison.OrdinalIgnoreCase))
                ?? _currentSettings.Profiles.FirstOrDefault();

            if (matched != null)
            {
                CboProfiles.SelectedItem = matched;
                CboProfiles.Text = matched.Name;
            }
            else if (!string.IsNullOrWhiteSpace(selectedProfileName))
            {
                CboProfiles.Text = selectedProfileName;
            }

            _selectedScanProfileNames.RemoveAll(name => !_currentSettings.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));
            if (_selectedScanProfileNames.Count == 0 && !string.IsNullOrWhiteSpace(_currentSettings.SelectedProfileName))
            {
                _selectedScanProfileNames.Add(_currentSettings.SelectedProfileName);
            }
            UpdateScanProfilesButtonLabel();
        }
        finally
        {
            _suppressProfileSelectionChanged = false;
        }
    }

    private void LoadSettingsToUi(FtpSettings s)
    {
        // Connection summary card has been removed; settings are managed via ConnectionSettingsWindow.
    }

    private FtpSettings ReadSettingsFromUi()
    {
        return _currentSettings;
    }

    private void BtnOpenConnectionSettings_Click(object sender, RoutedEventArgs e)
    {
        SwitchToView(AppNavView.FtpConfig);
    }

    private async void CboProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileSelectionChanged) return;
        if (CboProfiles.SelectedItem is FtpSiteProfile selectedProfile)
        {
            selectedProfile.ApplyTo(_currentSettings);
            _logger.Info($"[File Manager] Đã chuyển sang Site Profile: '{selectedProfile.Name}' ({selectedProfile.Username}@{selectedProfile.Host}:{selectedProfile.Port}).");

            if (MainTabControl.SelectedItem == TabFileManager)
            {
                string initialPath = selectedProfile.RemotePaths?.FirstOrDefault() ?? "/";
                if (string.IsNullOrWhiteSpace(initialPath)) initialPath = "/";
                TxtFmRemotePath.Text = initialPath;
                await LoadFileManagerDirectoryAsync(initialPath);
            }
        }
    }


    private void UpdateScanProfilesButtonLabel()
    {
        if (_selectedScanProfileNames.Count == 0 && !string.IsNullOrWhiteSpace(_currentSettings.SelectedProfileName))
        {
            _selectedScanProfileNames.Add(_currentSettings.SelectedProfileName);
        }

        int totalProfiles = _currentSettings.Profiles.Count;
        if (totalProfiles > 1 && _selectedScanProfileNames.Count >= totalProfiles)
        {
            TxtScanProfilesLabel.Text = $"Quét: Tất cả ({totalProfiles})";
        }
        else if (_selectedScanProfileNames.Count == 1)
        {
            TxtScanProfilesLabel.Text = $"Quét: {_selectedScanProfileNames[0]}";
        }
        else
        {
            TxtScanProfilesLabel.Text = $"Quét: {_selectedScanProfileNames.Count} Profiles";
        }
    }

    private void BtnSelectScanProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Views.SelectProfilesWindow(_currentSettings.Profiles, _selectedScanProfileNames)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedScanProfileNames.Clear();
            _selectedScanProfileNames.AddRange(dialog.SelectedProfileNames);
            _currentSettings.SelectedScanProfileNames = new List<string>(_selectedScanProfileNames);
            _configService.Save(_currentSettings);
            UpdateScanProfilesButtonLabel();
            _logger.Info($"Đã chọn {_selectedScanProfileNames.Count} profile để quét và lưu cấu hình: {string.Join(", ", _selectedScanProfileNames)}");
        }
    }

    private List<FtpSiteProfile> GetTargetScanProfiles()
    {
        var result = new List<FtpSiteProfile>();
        var selectedSet = new HashSet<string>(_selectedScanProfileNames, StringComparer.OrdinalIgnoreCase);

        foreach (var prof in _currentSettings.Profiles)
        {
            if (selectedSet.Contains(prof.Name))
            {
                result.Add(prof);
            }
        }

        if (result.Count == 0)
        {
            var matched = _currentSettings.Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, _currentSettings.SelectedProfileName, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                result.Add(matched);
            }
            else
            {
                result.Add(FtpSiteProfile.FromSettings(_currentSettings.SelectedProfileName, _currentSettings));
            }
        }

        return result;
    }

    private void SetUiBusy(bool isBusy, string? busyText = null)
    {
        CboProfiles.IsEnabled = !isBusy;
        BtnOpenConnectionSettings.IsEnabled = !isBusy;
        BtnSelectScanProfiles.IsEnabled = !isBusy;
        BtnPreview.IsEnabled = !isBusy;
        BtnSendPreviewEmail.IsEnabled = !isBusy;
        BtnEmailSettings.IsEnabled = !isBusy;
        BtnExecuteDelete.IsEnabled = !isBusy;
        BusyProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;

        if (isBusy)
        {
            Mouse.OverrideCursor = Cursors.Wait;
        }
        else
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
    {
        _configService.Save(_currentSettings);
        RefreshProfileComboBox(_currentSettings.SelectedProfileName);
        _logger.Info($"Đã lưu cấu hình (Profile '{_currentSettings.SelectedProfileName}') vào '{_configService.DefaultConfigPath}'.");
        MessageBox.Show($"Đã lưu cấu hình cho Profile '{_currentSettings.SelectedProfileName}' thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
    {
        SetUiBusy(true, "Đang kết nối...");
        try
        {
            var settings = ReadSettingsFromUi();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await Task.Run(() => _ftpService.TestConnectionAsync(settings, cts.Token));
            MessageBox.Show("Kết nối tới FTP Server thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            _logger.Error("Kết nối thất bại: Quá thời gian chờ (Timeout 30s). Vui lòng kiểm tra lại Proxy hoặc đường truyền mạng.");
            MessageBox.Show("Kết nối thất bại: Quá thời gian chờ (Timeout 30s). Vui lòng kiểm tra lại cấu hình Proxy hoặc đường truyền mạng.", "Hết thời gian chờ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            string details = ex.InnerException != null ? $"{ex.Message}\nChi tiết: {ex.InnerException.Message}" : ex.Message;
            _logger.Error($"Kết nối thất bại: {details}");
            MessageBox.Show($"Kết nối thất bại:\n{details}", "Lỗi Kết Nối", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetUiBusy(false);
        }
    }



    private async void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        SetUiBusy(true);
        try
        {
            var targetProfiles = GetTargetScanProfiles();
            _candidates.Clear();

            int successfulProfiles = 0;
            var failedProfiles = new List<string>();

            _logger.Info($"Bắt đầu quét xem trước trên {targetProfiles.Count} Site Profile...");

            foreach (var profile in targetProfiles)
            {
                var profSettings = profile.ToSettings(_currentSettings);
                _logger.Info($"--- [Profile: {profile.Name}] Bắt đầu quét ({profile.Host}:{profile.Port}) ---");

                try
                {
                    var list = await _ftpService.ScanCandidatesAsync(profSettings, DateTime.Now);
                    foreach (var item in list)
                    {
                        item.ProfileName = profile.Name;
                        _candidates.Add(item);
                    }
                    successfulProfiles++;
                    _logger.Info($"[Profile: {profile.Name}] Quét thành công! Tìm thấy {list.Count} file thỏa điều kiện.");
                }
                catch (Exception exProf)
                {
                    _logger.Error($"[Profile: {profile.Name}] Quét thất bại: {exProf.Message}");
                    failedProfiles.Add($"{profile.Name}: {exProf.Message}");
                }
            }

            _logger.Info($"Tổng kết Xem Trước: Quét thành công {successfulProfiles}/{targetProfiles.Count} profile, tổng cộng {_candidates.Count} file.");

            if (failedProfiles.Count > 0)
            {
                string failDetails = string.Join("\n- ", failedProfiles);
                MessageBox.Show($"Quét hoàn tất với một số cảnh báo:\n- Thành công: {successfulProfiles}/{targetProfiles.Count} profile (tìm thấy {_candidates.Count} file)\n- Thất bại ({failedProfiles.Count} profile):\n- {failDetails}",
                    "Cảnh báo quét Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (_currentSettings.AutoSendEmailOnPreview && _currentSettings.RecipientEmails.Count > 0)
            {
                await _emailService.SendPreviewReportAsync(_currentSettings, _candidates);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Lỗi khi Xem Trước: {ex.Message}");
            MessageBox.Show($"Lỗi khi quét file hoặc gửi email Xem Trước:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private void BtnEmailSettings_Click(object sender, RoutedEventArgs e)
    {
        SwitchToView(AppNavView.EmailConfig);
    }

    private async void BtnSendPreviewEmail_Click(object sender, RoutedEventArgs e)
    {
        _currentSettings = ReadSettingsFromUi();
        _currentSettings.RecipientEmails ??= new List<string>();

        if (_currentSettings.RecipientEmails.Count == 0)
        {
            var setupDialog = new EmailSettingsWindow(_currentSettings, _emailService, _candidates)
            {
                Owner = this
            };

            if (setupDialog.ShowDialog() != true || _currentSettings.RecipientEmails.Count == 0)
            {
                return;
            }

            _configService.Save(_currentSettings);
        }

        SetUiBusy(true);
        try
        {
            if (_candidates.Count == 0)
            {
                var confirmScan = MessageBox.Show(
                    "Danh sách Xem Trước hiện đang trống. Bạn có muốn quét danh sách file trên FTP Server ngay bây giờ rồi gửi Email không?",
                    "Quét Xem Trước & Gửi Email",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (confirmScan == MessageBoxResult.Cancel) return;

                if (confirmScan == MessageBoxResult.Yes)
                {
                    var targetProfiles = GetTargetScanProfiles();
                    foreach (var profile in targetProfiles)
                    {
                        var profSettings = profile.ToSettings(_currentSettings);
                        try
                        {
                            var list = await _ftpService.ScanCandidatesAsync(profSettings, DateTime.Now);
                            foreach (var item in list)
                            {
                                item.ProfileName = profile.Name;
                                _candidates.Add(item);
                            }
                        }
                        catch (Exception exProf)
                        {
                            _logger.Error($"[Profile: {profile.Name}] Quét thất bại: {exProf.Message}");
                        }
                    }
                }
            }

            await _emailService.SendPreviewReportAsync(_currentSettings, _candidates);
            MessageBox.Show(
                $"Đã gửi kết quả Xem Trước ({_candidates.Count(c => c.IsSelected)} file) thành công tới {_currentSettings.RecipientEmails.Count} địa chỉ email:\n- {string.Join("\n- ", _currentSettings.RecipientEmails)}",
                "Gửi Email Thành Công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"Lỗi khi gửi Email Xem Trước: {ex.Message}");
            MessageBox.Show($"Gửi Email Xem Trước thất bại:\n{ex.Message}", "Lỗi Gửi Email", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private async void BtnExecuteDelete_Click(object sender, RoutedEventArgs e)
    {
        SetUiBusy(true);
        try
        {
            if (_candidates.Count == 0)
            {
                var targetProfiles = GetTargetScanProfiles();
                foreach (var profile in targetProfiles)
                {
                    var profSettings = profile.ToSettings(_currentSettings);
                    try
                    {
                        var scanned = await _ftpService.ScanCandidatesAsync(profSettings, DateTime.Now);
                        foreach (var item in scanned)
                        {
                            item.ProfileName = profile.Name;
                            _candidates.Add(item);
                        }
                    }
                    catch (Exception exProf)
                    {
                        _logger.Error($"[Profile: {profile.Name}] Quét thất bại: {exProf.Message}");
                    }
                }
            }

            var selectedCandidates = _candidates.Where(c => c.IsSelected).ToList();
            if (selectedCandidates.Count == 0)
            {
                MessageBox.Show("Không có file nào thỏa điều kiện hoặc được chọn để xóa.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var groups = selectedCandidates.GroupBy(c => string.IsNullOrWhiteSpace(c.ProfileName) ? _currentSettings.SelectedProfileName : c.ProfileName).ToList();

            string actionDesc = $"thực hiện Sao lưu & Xóa {selectedCandidates.Count} file trên {groups.Count} Site Profile";
            var confirm = MessageBox.Show(
                $"Bạn có chắc chắn muốn {actionDesc} không?\n\nChi tiết các Profile sẽ xử lý:\n" +
                string.Join("\n", groups.Select(g => $"- Profile '{g.Key}': {g.Count()} file")),
                "Xác nhận xóa file đa Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            int totalDeleted = 0;
            int totalFailed = 0;

            foreach (var group in groups)
            {
                var profile = _currentSettings.Profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, group.Key, StringComparison.OrdinalIgnoreCase));
                var profSettings = profile != null ? profile.ToSettings(_currentSettings) : _currentSettings;

                _logger.Info($"=== Bắt đầu xử lý {group.Count()} file cho Profile '{group.Key}' ({profSettings.Host}:{profSettings.Port}) ===");
                var (deleted, failed) = await _ftpService.ExecuteBackupAndDeleteAsync(profSettings, group);
                totalDeleted += deleted;
                totalFailed += failed;
            }

            MessageBox.Show($"Hoàn tất xử lý trên {groups.Count} Profile!\n- Đã xóa thành công: {totalDeleted} file\n- Lỗi: {totalFailed} file",
                "Kết quả Thực thi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"Lỗi khi thực thi xóa: {ex.Message}");
            MessageBox.Show($"Lỗi khi xóa file:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _candidates) item.IsSelected = true;
    }

    private void BtnUnselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _candidates) item.IsSelected = false;
    }

    // =========================================================================
    // SECTION: FTP FILE MANAGER TAB LOGIC (VIEW, UPLOAD, DOWNLOAD, DELETE, ETC.)
    // =========================================================================

    private async void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabControl) return;
        if (MainTabControl.SelectedItem == TabFileManager && !_fmLoadedOnce)
        {
            _fmLoadedOnce = true;
            string initialPath = _currentSettings.RemotePaths?.FirstOrDefault() ?? "/";
            if (string.IsNullOrWhiteSpace(initialPath)) initialPath = "/";
            TxtFmRemotePath.Text = initialPath;
            await LoadFileManagerDirectoryAsync(initialPath);
        }
    }

    private void SetFmBusy(bool isBusy)
    {
        FmProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BtnFmGo.IsEnabled = !isBusy;
        BtnFmUp.IsEnabled = !isBusy;
        BtnFmHome.IsEnabled = !isBusy;
        BtnFmRefresh.IsEnabled = !isBusy;
        BtnFmUploadFile.IsEnabled = !isBusy;
        BtnFmUploadFolder.IsEnabled = !isBusy;
        BtnFmDownload.IsEnabled = !isBusy;
        BtnFmView.IsEnabled = !isBusy;
        BtnFmNewFolder.IsEnabled = !isBusy;
        BtnFmDelete.IsEnabled = !isBusy;
        GridFileManager.IsEnabled = !isBusy;

        Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;
    }

    private async Task LoadFileManagerDirectoryAsync(string remotePath)
    {
        string target = string.IsNullOrWhiteSpace(remotePath) ? "/" : FtpCleanupService.NormalizeSingleRemotePath(remotePath);
        var settings = ReadSettingsFromUi();

        if (string.IsNullOrWhiteSpace(settings.Host))
        {
            MessageBox.Show(this, "Vui lòng nhập địa chỉ FTP Host trước khi duyệt thư mục.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetFmBusy(true);
        try
        {
            _logger.Info($"[File Manager] Đang tải danh sách thư mục: '{target}'...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var entries = await _ftpService.GetDirectoryEntriesAsync(settings, target, cts.Token);

            _currentFmRemotePath = target;
            TxtFmRemotePath.Text = target;

            _fmEntries.Clear();
            foreach (var entry in entries)
            {
                entry.PropertyChanged += (_, propArgs) =>
                {
                    if (propArgs.PropertyName == nameof(FileManagerEntry.IsSelected))
                    {
                        UpdateFmSummary();
                    }
                };
                _fmEntries.Add(entry);
            }

            ApplyFmFilter();
            _logger.Info($"[File Manager] Đã tải xong '{target}' ({entries.Count} mục).");
        }
        catch (OperationCanceledException)
        {
            _logger.Error($"[File Manager] Hết thời gian chờ (Timeout 30s) khi tải '{target}'.");
            MessageBox.Show(this, "Quá thời gian chờ khi kết nối hoặc đọc danh sách file trên FTP Server (Timeout 30s).", "Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            string msg = ex.InnerException != null ? $"{ex.Message} ({ex.InnerException.Message})" : ex.Message;
            _logger.Error($"[File Manager] Lỗi khi tải thư mục '{target}': {msg}");
            MessageBox.Show(this, $"Không thể đọc danh sách file tại '{target}':\n{msg}", "Lỗi FTP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
            UpdateFmSummary();
        }
    }

    private void ApplyFmFilter()
    {
        string filter = TxtFmFilter?.Text?.Trim() ?? string.Empty;
        _fmDisplayEntries.Clear();

        foreach (var entry in _fmEntries)
        {
            if (entry.IsParentEntry)
            {
                _fmDisplayEntries.Add(entry);
                continue;
            }

            if (string.IsNullOrEmpty(filter) || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _fmDisplayEntries.Add(entry);
            }
        }
        UpdateFmSummary();
    }

    private void UpdateFmSummary()
    {
        if (TxtFmSummary == null) return;
        var validEntries = _fmDisplayEntries.Where(e => !e.IsParentEntry).ToList();
        int dirCount = validEntries.Count(e => e.IsDirectory);
        int fileCount = validEntries.Count(e => !e.IsDirectory);
        long totalSize = validEntries.Where(e => !e.IsDirectory).Sum(e => e.Size);
        int selectedCount = validEntries.Count(e => e.IsSelected);

        TxtFmSummary.Text = $"Tổng cộng: {validEntries.Count} mục ({dirCount} thư mục, {fileCount} file - {FileManagerEntry.FormatFileSize(totalSize)}) | Đã chọn: {selectedCount} mục";
    }

    private async void GridFileManager_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridFileManager.SelectedItem is not FileManagerEntry entry) return;
        if (entry.IsParentEntry || entry.IsDirectory)
        {
            await LoadFileManagerDirectoryAsync(entry.FullPath);
        }
        else
        {
            await ViewRemoteFileAsync(entry);
        }
    }

    private async void BtnFmGo_Click(object sender, RoutedEventArgs e)
    {
        await LoadFileManagerDirectoryAsync(TxtFmRemotePath.Text.Trim());
    }

    private async void TxtFmRemotePath_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LoadFileManagerDirectoryAsync(TxtFmRemotePath.Text.Trim());
        }
    }

    private async void BtnFmUp_Click(object sender, RoutedEventArgs e)
    {
        string parent = FtpCleanupService.GetParentRemotePath(_currentFmRemotePath);
        await LoadFileManagerDirectoryAsync(parent);
    }

    private async void BtnFmHome_Click(object sender, RoutedEventArgs e)
    {
        await LoadFileManagerDirectoryAsync("/");
    }

    private async void BtnFmRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadFileManagerDirectoryAsync(_currentFmRemotePath);
    }

    private void TxtFmFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFmFilter();
    }

    private void BtnFmSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _fmDisplayEntries)
        {
            if (!entry.IsParentEntry) entry.IsSelected = true;
        }
    }

    private void BtnFmUnselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _fmDisplayEntries)
        {
            entry.IsSelected = false;
        }
    }

    private List<FileManagerEntry> GetSelectedOrActiveEntries()
    {
        var checkedItems = _fmDisplayEntries.Where(e => e.IsSelected && !e.IsParentEntry).ToList();
        if (checkedItems.Count > 0) return checkedItems;

        if (GridFileManager.SelectedItem is FileManagerEntry active && !active.IsParentEntry)
        {
            return new List<FileManagerEntry> { active };
        }

        return new List<FileManagerEntry>();
    }

    private async Task ViewRemoteFileAsync(FileManagerEntry entry)
    {
        if (entry.IsParentEntry || entry.IsDirectory)
        {
            await LoadFileManagerDirectoryAsync(entry.FullPath);
            return;
        }

        var settings = ReadSettingsFromUi();
        SetFmBusy(true);
        entry.Status = "Đang tải xem...";
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "WorkManagement", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string localFile = Path.Combine(tempDir, entry.Name);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            bool ok = await _ftpService.DownloadSingleFileAsync(settings, entry.FullPath, localFile, cts.Token);
            if (ok && File.Exists(localFile))
            {
                entry.Status = "Đã mở";
                var viewer = new FilePreviewWindow(
                    entry,
                    localFile,
                    async updatedLocalPath =>
                    {
                        using var uploadCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                        return await _ftpService.UploadSingleFileAsync(settings, updatedLocalPath, entry.FullPath, uploadCts.Token);
                    })
                {
                    Owner = this
                };
                viewer.ShowDialog();
                if (viewer.HasSavedToFtp)
                {
                    entry.Status = "Đã cập nhật";
                    await LoadFileManagerDirectoryAsync(_currentFmRemotePath);
                }
            }
            else
            {
                entry.Status = "Lỗi tải file";
                MessageBox.Show(this, $"Không thể tải file '{entry.Name}' để xem trước.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            entry.Status = "Lỗi";
            _logger.Error($"[File Manager] Lỗi khi xem file '{entry.FullPath}': {ex.Message}");
            MessageBox.Show(this, $"Lỗi khi tải xem file:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
        }
    }

    private async void BtnFmView_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedOrActiveEntries();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Vui lòng chọn một file để xem.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ViewRemoteFileAsync(items[0]);
    }

    private void CtxFmView_Click(object sender, RoutedEventArgs e) => BtnFmView_Click(sender, e);

    private async void BtnFmDownload_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedOrActiveEntries();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Vui lòng chọn ít nhất một file hoặc thư mục cần tải về máy tính.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFolderDialog
        {
            Title = $"Chọn thư mục lưu {items.Count} mục tải về từ FTP"
        };

        if (dlg.ShowDialog(this) != true) return;
        string targetDir = dlg.FolderName;

        var settings = ReadSettingsFromUi();
        SetFmBusy(true);
        try
        {
            _logger.Info($"[File Manager] Bắt đầu tải {items.Count} mục về '{targetDir}'...");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var (success, failed) = await _ftpService.DownloadItemsAsync(settings, items, targetDir, cts.Token);

            string msg = $"Đã hoàn tất tải về!\n- Thành công: {success} mục\n- Thất bại: {failed} mục\nĐường dẫn: {targetDir}";
            _logger.Info($"[File Manager] {msg}");
            MessageBox.Show(this, msg, "Kết quả Tải Về", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"[File Manager] Lỗi khi tải về: {ex.Message}");
            MessageBox.Show(this, $"Lỗi trong quá trình tải về:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
        }
    }

    private void CtxFmDownload_Click(object sender, RoutedEventArgs e) => BtnFmDownload_Click(sender, e);

    private async void BtnFmUploadFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = $"Chọn các file tải lên FTP ('{_currentFmRemotePath}')",
            Multiselect = true
        };

        if (dlg.ShowDialog(this) != true || dlg.FileNames.Length == 0) return;

        var uploadEntries = dlg.FileNames.Select(f => new FileManagerEntry
        {
            Name = Path.GetFileName(f),
            FullPath = f,
            IsDirectory = false,
            Size = new FileInfo(f).Length
        }).ToList();

        var settings = ReadSettingsFromUi();
        SetFmBusy(true);
        try
        {
            _logger.Info($"[File Manager] Bắt đầu upload {uploadEntries.Count} file lên '{_currentFmRemotePath}'...");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var (success, failed) = await _ftpService.UploadItemsAsync(settings, uploadEntries, _currentFmRemotePath, cts.Token);

            string msg = $"Hoàn tất tải lên file!\n- Thành công: {success} file\n- Thất bại: {failed} file";
            _logger.Info($"[File Manager] {msg}");
            MessageBox.Show(this, msg, "Kết quả Tải Lên", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"[File Manager] Lỗi khi upload: {ex.Message}");
            MessageBox.Show(this, $"Lỗi trong quá trình tải lên:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
            await LoadFileManagerDirectoryAsync(_currentFmRemotePath);
        }
    }

    private void CtxFmUploadFile_Click(object sender, RoutedEventArgs e) => BtnFmUploadFile_Click(sender, e);

    private async void BtnFmUploadFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = $"Chọn thư mục cần tải lên FTP ('{_currentFmRemotePath}')"
        };

        if (dlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dlg.FolderName)) return;

        string folderPath = dlg.FolderName;
        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(folderName))
        {
            folderName = new DirectoryInfo(folderPath).Name;
        }

        var uploadEntries = new List<FileManagerEntry>
        {
            new FileManagerEntry
            {
                Name = folderName,
                FullPath = folderPath,
                IsDirectory = true
            }
        };

        var settings = ReadSettingsFromUi();
        SetFmBusy(true);
        try
        {
            _logger.Info($"[File Manager] Bắt đầu upload thư mục '{folderPath}' lên '{_currentFmRemotePath}'...");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var (success, failed) = await _ftpService.UploadItemsAsync(settings, uploadEntries, _currentFmRemotePath, cts.Token);

            string msg = $"Hoàn tất tải lên thư mục!\n- Thành công: {success} thư mục\n- Thất bại: {failed} thư mục";
            _logger.Info($"[File Manager] {msg}");
            MessageBox.Show(this, msg, "Kết quả Tải Lên", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"[File Manager] Lỗi khi upload thư mục: {ex.Message}");
            MessageBox.Show(this, $"Lỗi trong quá trình tải lên thư mục:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
            await LoadFileManagerDirectoryAsync(_currentFmRemotePath);
        }
    }

    private void CtxFmUploadFolder_Click(object sender, RoutedEventArgs e) => BtnFmUploadFolder_Click(sender, e);

    private async void BtnFmNewFolder_Click(object sender, RoutedEventArgs e)
    {
        string? folderName = PromptDialogWindow.Show(
            this,
            "Tạo Thư Mục Mới",
            $"Nhập tên thư mục muốn tạo tại '{_currentFmRemotePath}':",
            "ThuMucMoi",
            PackIconKind.FolderPlusOutline);

        if (string.IsNullOrWhiteSpace(folderName)) return;

        var settings = ReadSettingsFromUi();
        string newPath = FtpCleanupService.CombineRemotePath(_currentFmRemotePath, folderName.Trim());

        SetFmBusy(true);
        try
        {
            _logger.Info($"[File Manager] Đang tạo thư mục: '{newPath}'...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _ftpService.CreateRemoteDirectoryAsync(settings, newPath, cts.Token);
            _logger.Info($"[File Manager] Tạo thư mục thành công: '{newPath}'");
        }
        catch (Exception ex)
        {
            _logger.Error($"[File Manager] Lỗi khi tạo thư mục '{newPath}': {ex.Message}");
            MessageBox.Show(this, $"Không thể tạo thư mục:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
            await LoadFileManagerDirectoryAsync(_currentFmRemotePath);
        }
    }

    private void CtxFmNewFolder_Click(object sender, RoutedEventArgs e) => BtnFmNewFolder_Click(sender, e);

    private async void BtnFmDelete_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedOrActiveEntries();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Vui lòng chọn ít nhất một file hoặc thư mục để xóa khỏi FTP.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string desc = items.Count == 1 ? $"'{items[0].Name}'" : $"{items.Count} mục";
        var confirm = MessageBox.Show(
            this,
            $"Bạn có chắc chắn muốn XÓA VĨNH VIỄN {desc} trên FTP Server không?\r\n\r\nĐường dẫn: {_currentFmRemotePath}\r\nCẢNH BÁO: Thao tác này không thể hoàn tác!",
            "Xác nhận xóa trên FTP",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var settings = ReadSettingsFromUi();
        SetFmBusy(true);
        try
        {
            _logger.Info($"[File Manager] Bắt đầu xóa {items.Count} mục trên FTP...");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var (deleted, failed) = await _ftpService.DeleteRemoteEntriesAsync(settings, items, cts.Token);

            string msg = $"Hoàn tất xóa!\n- Đã xóa: {deleted} mục\n- Lỗi: {failed} mục";
            _logger.Info($"[File Manager] {msg}");
            MessageBox.Show(this, msg, "Kết quả Xóa", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"[File Manager] Lỗi khi xóa trên FTP: {ex.Message}");
            MessageBox.Show(this, $"Lỗi trong quá trình xóa:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
            await LoadFileManagerDirectoryAsync(_currentFmRemotePath);
        }
    }

    private void CtxFmDelete_Click(object sender, RoutedEventArgs e) => BtnFmDelete_Click(sender, e);

    private void CtxFmRefresh_Click(object sender, RoutedEventArgs e) => BtnFmRefresh_Click(sender, e);

    private async void CtxFmRename_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedOrActiveEntries();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Vui lòng chọn một file hoặc thư mục để đổi tên.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = items[0];
        string? newName = PromptDialogWindow.Show(
            this,
            "Đổi Tên Trên FTP",
            $"Nhập tên mới cho '{target.Name}':",
            target.Name,
            PackIconKind.FormTextbox);

        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName.Trim(), target.Name, StringComparison.Ordinal))
        {
            return;
        }

        var settings = ReadSettingsFromUi();
        SetFmBusy(true);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _ftpService.RenameRemoteEntryAsync(settings, target, newName.Trim(), cts.Token);
        }
        catch (Exception ex)
        {
            _logger.Error($"[File Manager] Lỗi khi đổi tên '{target.FullPath}': {ex.Message}");
            MessageBox.Show(this, $"Không thể đổi tên:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
            await LoadFileManagerDirectoryAsync(_currentFmRemotePath);
        }
    }

    private void CtxFmCopyPath_Click(object sender, RoutedEventArgs e)
    {
        var items = GetSelectedOrActiveEntries();
        if (items.Count == 0) return;

        try
        {
            string paths = string.Join(Environment.NewLine, items.Select(i => i.FullPath));
            Clipboard.SetText(paths);
            _logger.Info($"[File Manager] Đã sao chép đường dẫn vào Clipboard: {paths}");
        }
        catch { }
    }

    private void GridFileManager_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void GridFileManager_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] droppedPaths || droppedPaths.Length == 0) return;

        var uploadEntries = new List<FileManagerEntry>();
        foreach (string path in droppedPaths)
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                uploadEntries.Add(new FileManagerEntry
                {
                    Name = fi.Name,
                    FullPath = path,
                    IsDirectory = false,
                    Size = fi.Length
                });
            }
            else if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                uploadEntries.Add(new FileManagerEntry
                {
                    Name = di.Name,
                    FullPath = path,
                    IsDirectory = true
                });
            }
        }

        if (uploadEntries.Count == 0) return;

        var confirm = MessageBox.Show(
            this,
            $"Bạn có muốn tải {uploadEntries.Count} mục vừa kéo thả lên thư mục FTP '{_currentFmRemotePath}' không?",
            "Xác nhận Upload Kéo & Thả",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        var settings = ReadSettingsFromUi();
        SetFmBusy(true);
        try
        {
            _logger.Info($"[File Manager] Bắt đầu upload {uploadEntries.Count} mục (Kéo & Thả) lên '{_currentFmRemotePath}'...");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            var (success, failed) = await _ftpService.UploadItemsAsync(settings, uploadEntries, _currentFmRemotePath, cts.Token);

            string msg = $"Hoàn tất tải lên (Drag & Drop)!\n- Thành công: {success} mục\n- Thất bại: {failed} mục";
            _logger.Info($"[File Manager] {msg}");
            MessageBox.Show(this, msg, "Kết quả Tải Lên", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"[File Manager] Lỗi khi upload kéo thả: {ex.Message}");
            MessageBox.Show(this, $"Lỗi khi tải lên:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetFmBusy(false);
            await LoadFileManagerDirectoryAsync(_currentFmRemotePath);
        }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        TxtLogs.Clear();
    }

    #region Navigation Dock Menu Handlers

    public enum AppNavView
    {
        Dashboard,
        FtpCleaner,
        VmManager,
        ProjectManager,
        TaskScheduler,
        FtpConfig,
        EmailConfig
    }

    private void NavBtnDashboard_Click(object sender, RoutedEventArgs e)
    {
        SwitchToView(AppNavView.Dashboard);
    }

    private void NavBtnFtp_Click(object sender, RoutedEventArgs e)
    {
        SwitchToView(AppNavView.FtpCleaner);
    }

    private void NavBtnVm_Click(object sender, RoutedEventArgs e)
    {
        SwitchToView(AppNavView.VmManager);
    }

    private void NavBtnProject_Click(object sender, RoutedEventArgs e)
    {
        SwitchToView(AppNavView.ProjectManager);
    }

    private void NavBtnScheduler_Click(object sender, RoutedEventArgs e)
    {
        SwitchToView(AppNavView.TaskScheduler);
    }

    private void SwitchToView(AppNavView targetView)
    {
        if (ViewContainerDashboard != null)
            ViewContainerDashboard.Visibility = targetView == AppNavView.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        ViewContainerFtp.Visibility = targetView == AppNavView.FtpCleaner ? Visibility.Visible : Visibility.Collapsed;
        ViewContainerVm.Visibility = targetView == AppNavView.VmManager ? Visibility.Visible : Visibility.Collapsed;
        ViewContainerProject.Visibility = targetView == AppNavView.ProjectManager ? Visibility.Visible : Visibility.Collapsed;
        ViewContainerScheduler.Visibility = targetView == AppNavView.TaskScheduler ? Visibility.Visible : Visibility.Collapsed;
        ViewContainerFtpConfig.Visibility = targetView == AppNavView.FtpConfig ? Visibility.Visible : Visibility.Collapsed;
        ViewContainerEmailConfig.Visibility = targetView == AppNavView.EmailConfig ? Visibility.Visible : Visibility.Collapsed;

        if (NavBtnDashboard != null)
            NavBtnDashboard.Style = (Style)FindResource(targetView == AppNavView.Dashboard ? "AppNavButtonActiveStyle" : "AppNavButtonInactiveStyle");
        NavBtnFtp.Style = (Style)FindResource(targetView == AppNavView.FtpCleaner ? "AppNavButtonActiveStyle" : "AppNavButtonInactiveStyle");
        NavBtnVm.Style = (Style)FindResource(targetView == AppNavView.VmManager ? "AppNavButtonActiveStyle" : "AppNavButtonInactiveStyle");
        if (NavBtnProject != null) NavBtnProject.Style = (Style)FindResource(targetView == AppNavView.ProjectManager ? "AppNavButtonActiveStyle" : "AppNavButtonInactiveStyle");
        NavBtnScheduler.Style = (Style)FindResource(targetView == AppNavView.TaskScheduler ? "AppNavButtonActiveStyle" : "AppNavButtonInactiveStyle");
        BtnOpenConnectionSettings.Style = (Style)FindResource(targetView == AppNavView.FtpConfig ? "AppNavButtonActiveStyle" : "AppNavButtonInactiveStyle");
        BtnEmailSettings.Style = (Style)FindResource(targetView == AppNavView.EmailConfig ? "AppNavButtonActiveStyle" : "AppNavButtonInactiveStyle");

        switch (targetView)
        {
            case AppNavView.Dashboard:
                TxtCurrentPageTitle.Text = "Tổng Quan Hệ Thống & Nhật Ký Kiểm Toán (Dashboard)";
                TxtCurrentPageSub.Text = "Theo dõi số liệu máy ảo, dự án Oracle, cảnh báo hạn sử dụng và quản lý dung lượng sao lưu cục bộ";
                IconCurrentPage.Kind = MaterialDesignThemes.Wpf.PackIconKind.ViewDashboardVariantOutline;
                DashboardViewControl?.LoadDashboard();
                break;

            case AppNavView.FtpCleaner:
                TxtCurrentPageTitle.Text = "Quản Lý File & Tự Động Xóa FTP";
                TxtCurrentPageSub.Text = "Quét, sao lưu và dọn dẹp file tự động theo ngày trên máy chủ FTP";
                IconCurrentPage.Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderSyncOutline;
                break;

            case AppNavView.VmManager:
                TxtCurrentPageTitle.Text = "Quản Lý Danh Sách Máy Ảo (VMs & RDP)";
                TxtCurrentPageSub.Text = "Quản lý thông tin máy chủ, tài khoản, kết nối Remote Desktop một chạm và Oracle DB";
                IconCurrentPage.Kind = MaterialDesignThemes.Wpf.PackIconKind.DesktopTowerMonitor;
                VmManagerControl.LoadData();
                break;

            case AppNavView.ProjectManager:
                TxtCurrentPageTitle.Text = "Quản Lý Dự Án & Dọn Dẹp Workspace";
                TxtCurrentPageSub.Text = "Quản lý thông tin source folder, kết nối Oracle DB, tự động backup và xóa tuần tự";
                IconCurrentPage.Kind = MaterialDesignThemes.Wpf.PackIconKind.FolderCogOutline;
                ProjectManagerControl.LoadData();
                break;

            case AppNavView.TaskScheduler:
                TxtCurrentPageTitle.Text = "Cấu Hình Lịch Chạy Windows Task Scheduler";
                TxtCurrentPageSub.Text = "Tự động quét, backup và dọn dẹp file FTP định kỳ ở chế độ Console ngầm";
                IconCurrentPage.Kind = MaterialDesignThemes.Wpf.PackIconKind.CalendarClock;
                SchedulerViewControl.UpdateCommandPreview();
                break;

            case AppNavView.FtpConfig:
                TxtCurrentPageTitle.Text = "Cấu Hình Kết Nối FTP, Proxy & Sao Lưu (Backup)";
                TxtCurrentPageSub.Text = "Thiết lập kết nối máy chủ FTP/FTPS, proxy mạng và thư mục tự động sao lưu dữ liệu";
                IconCurrentPage.Kind = MaterialDesignThemes.Wpf.PackIconKind.TuneVariant;
                FtpConfigViewControl.LoadSettingsToUi(_currentSettings);
                FtpConfigViewControl.RefreshProfileComboBox(_currentSettings.SelectedProfileName);
                break;

            case AppNavView.EmailConfig:
                TxtCurrentPageTitle.Text = "Cấu Hình Gửi Email Báo Cáo Xem Trước";
                TxtCurrentPageSub.Text = "Thiết lập Outgoing Server (SMTP - Port 25/587) và danh sách email thành viên nhận báo cáo";
                IconCurrentPage.Kind = MaterialDesignThemes.Wpf.PackIconKind.EmailFastOutline;
                EmailConfigViewControl.LoadFromSettings();
                break;
        }
    }

    private void BtnHeaderCommandPalette_Click(object sender, RoutedEventArgs e)
    {
        OpenCommandPalette();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            OpenCommandPalette();
        }
    }

    public void OpenCommandPalette()
    {
        if (_isCommandPaletteOpen) return;
        _isCommandPaletteOpen = true;

        try
        {
            var items = BuildCommandPaletteItems();
            var palette = new CommandPaletteWindow(items);

            if (IsVisible && WindowState != WindowState.Minimized)
            {
                palette.Owner = this;
                palette.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                palette.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                palette.Topmost = true;
            }

            if (palette.ShowDialog() == true && palette.SelectedCommand?.ExecuteAction != null)
            {
                palette.SelectedCommand.ExecuteAction.Invoke();
            }
        }
        finally
        {
            _isCommandPaletteOpen = false;
        }
    }

    private void NavigateAndRestore(AppNavView targetView)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            RestoreFromTray();
        }
        SwitchToView(targetView);
    }

    public List<CommandPaletteItem> BuildCommandPaletteItems()
    {
        var items = new List<CommandPaletteItem>
        {
            new()
            {
                Title = "Mở Trang Tổng Quan (Dashboard & Audit Log)",
                Subtitle = "Xem thống kê máy ảo, dự án, dung lượng backup và nhật ký hoạt động",
                CategoryBadge = "ĐIỀU HƯỚNG",
                IconKind = PackIconKind.ViewDashboardVariantOutline,
                SearchKeywords = "dashboard tong quan thong ke log backup",
                ExecuteAction = () => NavigateAndRestore(AppNavView.Dashboard)
            },
            new()
            {
                Title = "Mở Quản Lý File & Tự Động Xóa FTP",
                Subtitle = "Quét file quá hạn và duyệt file trực tiếp trên FTP Server",
                CategoryBadge = "ĐIỀU HƯỚNG",
                IconKind = PackIconKind.FolderSyncOutline,
                SearchKeywords = "ftp cleaner file manager xoa file",
                ExecuteAction = () => NavigateAndRestore(AppNavView.FtpCleaner)
            },
            new()
            {
                Title = "Mở Danh Sách Máy Ảo (VMs & RDP)",
                Subtitle = "Quản lý server, mật khẩu và kết nối Remote Desktop 1 chạm",
                CategoryBadge = "ĐIỀU HƯỚNG",
                IconKind = PackIconKind.DesktopTowerMonitor,
                SearchKeywords = "vm may ao server rdp remote desktop",
                ExecuteAction = () => NavigateAndRestore(AppNavView.VmManager)
            },
            new()
            {
                Title = "Mở Quản Lý Dự Án & Oracle DB Cleanup",
                Subtitle = "Quản lý source folder, DB Users, Tablespace và dọn dẹp dự án",
                CategoryBadge = "ĐIỀU HƯỚNG",
                IconKind = PackIconKind.FolderCogOutline,
                SearchKeywords = "project du an oracle database tablespace",
                ExecuteAction = () => NavigateAndRestore(AppNavView.ProjectManager)
            },
            new()
            {
                Title = "Mở Lịch Chạy Tự Động (Windows Task Scheduler)",
                Subtitle = "Thiết lập chạy ngầm mỗi sáng hoặc theo giờ cố định",
                CategoryBadge = "ĐIỀU HƯỚNG",
                IconKind = PackIconKind.CalendarClock,
                SearchKeywords = "scheduler lich chay tu dong task",
                ExecuteAction = () => NavigateAndRestore(AppNavView.TaskScheduler)
            },
            new()
            {
                Title = "Cấu Hình Kết Nối FTP, Proxy & Bộ Lọc Nâng Cao",
                Subtitle = "Thiết lập Site Profile, Whitelist Exclude, dung lượng tối thiểu và Backup",
                CategoryBadge = "CẤU HÌNH",
                IconKind = PackIconKind.TuneVariant,
                SearchKeywords = "config ftp proxy backup filter",
                ExecuteAction = () => NavigateAndRestore(AppNavView.FtpConfig)
            },
            new()
            {
                Title = "Cấu Hình Gửi Email Báo Cáo (SMTP)",
                Subtitle = "Thiết lập máy chủ gửi mail và danh sách người nhận",
                CategoryBadge = "CẤU HÌNH",
                IconKind = PackIconKind.EmailFastOutline,
                SearchKeywords = "email smtp mail bao cao",
                ExecuteAction = () => NavigateAndRestore(AppNavView.EmailConfig)
            },
            new()
            {
                Title = "Thu Nhỏ Ứng Dụng Chạy Ngầm Dưới Khay Hệ Thống (System Tray)",
                Subtitle = "Ẩn cửa sổ chính xuống góc phải thanh Taskbar Windows",
                CategoryBadge = "HỆ THỐNG",
                IconKind = PackIconKind.TrayArrowDown,
                SearchKeywords = "tray system tray an thu nho chay ngam minimize",
                ExecuteAction = () => MinimizeToTray(showNotification: true)
            }
        };

        // Chỉ thêm các Máy ảo được đánh dấu Yêu thích (Ngôi sao - IsFavorite = true) vào Tìm Nhanh (Ctrl + K)
        try
        {
            var vmService = new VmManagerService();
            var rdpService = new RemoteDesktopService(_logger);
            foreach (var vm in vmService.LoadAll().Where(v => v.IsFavorite))
            {
                if (string.IsNullOrWhiteSpace(vm.Ip)) continue;
                var capturedVm = vm;
                items.Add(new CommandPaletteItem
                {
                    Title = $"★ Kết nối RDP: {capturedVm.MachineName} ({capturedVm.Ip})",
                    Subtitle = $"User: {capturedVm.Username} | OS: {capturedVm.Os} | DB: {capturedVm.DatabaseName} ({capturedVm.Tns}) | KT: {capturedVm.Tester}",
                    CategoryBadge = "MÁY ẢO YÊU THÍCH",
                    IconKind = PackIconKind.RemoteDesktop,
                    SearchKeywords = $"rdp favorite yeu thich {capturedVm.Ip} {capturedVm.MachineName} {capturedVm.DatabaseName} {capturedVm.Tns} {capturedVm.Tester}",
                    ExecuteAction = async () => await rdpService.ConnectAsync(capturedVm)
                });
            }
        }
        catch { }

        // Thêm toàn bộ Dự án để mở nhanh Source Folder hoặc xem dự án
        try
        {
            var projService = new ProjectManagerService();
            foreach (var proj in projService.LoadAll())
            {
                var capturedProj = proj;
                items.Add(new CommandPaletteItem
                {
                    Title = $"Dự án: {capturedProj.ProjectName} (No: {capturedProj.No})",
                    Subtitle = $"Folder: {capturedProj.SourceFolder} | Host DB: {capturedProj.HostServer} ({capturedProj.ServiceName}) | PIC: {capturedProj.PersonInCharge}",
                    CategoryBadge = "DỰ ÁN",
                    IconKind = PackIconKind.FolderOpenOutline,
                    SearchKeywords = $"project du an {capturedProj.ProjectName} {capturedProj.No} {capturedProj.SourceFolder} {capturedProj.HostServer} {capturedProj.DbUsers}",
                    ExecuteAction = () =>
                    {
                        if (!string.IsNullOrWhiteSpace(capturedProj.SourceFolder) && Directory.Exists(capturedProj.SourceFolder))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", capturedProj.SourceFolder);
                        }
                        else
                        {
                            NavigateAndRestore(AppNavView.ProjectManager);
                        }
                    }
                });
            }
        }
        catch { }

        return items;
    }

    private bool _isDockExpanded = true;

    private void BtnToggleDock_Click(object sender, RoutedEventArgs e)
    {
        _isDockExpanded = !_isDockExpanded;
        ColDockSidebar.Width = _isDockExpanded ? new GridLength(230) : new GridLength(0);

        string tip = _isDockExpanded ? "Ẩn / Thu gọn Menu Dock" : "Mở / Hiện Menu Dock";
        if (BtnToggleDock != null) BtnToggleDock.ToolTip = tip;
        if (BtnCollapseDock != null) BtnCollapseDock.ToolTip = tip;
    }

    #endregion
}