using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class AiSettingsView : UserControl
{
    private ConfigService _configService = new();
    private AppLogger? _logger;
    private FtpSettings _fullSettings = new();
    private bool _isApiKeyRevealed;

    public event EventHandler? SettingsSaved;

    public AiSettingsView()
    {
        InitializeComponent();
    }

    public void Initialize(ConfigService configService, AppLogger? logger = null)
    {
        _configService = configService;
        _logger = logger;
        _fullSettings = _configService.Load();
        LoadSettingsToUi(_fullSettings.Ai ?? new AiSettings());
    }

    public void LoadSettingsToUi(AiSettings s)
    {
        ChkAiEnabled.IsChecked = s.IsEnabled;
        TxtBaseUrl.Text = string.IsNullOrWhiteSpace(s.BaseUrl) ? "http://localhost:11434/v1" : s.BaseUrl;
        TxtApiKey.Password = s.ApiKey;
        TxtApiKeyRevealed.Text = s.ApiKey;
        ChkBypassProxy.IsChecked = s.BypassProxy;

        // Điền model name hiện tại vào ComboBox
        string currentModel = string.IsNullOrWhiteSpace(s.ModelName) ? "default" : s.ModelName;
        if (!CboModelName.Items.Contains(currentModel))
        {
            CboModelName.Items.Add(currentModel);
        }
        CboModelName.Text = currentModel;
        CboModelName.SelectedItem = currentModel;

        TxtTimeout.Text = s.TimeoutSeconds > 0 ? s.TimeoutSeconds.ToString() : "30";
        TxtTemperature.Text = s.Temperature.ToString("0.#");
    }

    public AiSettings ReadSettingsFromUi()
    {
        int.TryParse(TxtTimeout.Text, out int timeout);
        if (timeout <= 0) timeout = 30;

        double.TryParse(TxtTemperature.Text, out double temp);
        if (temp < 0 || temp > 2.0) temp = 0.1;

        string apiKey = _isApiKeyRevealed ? TxtApiKeyRevealed.Text : TxtApiKey.Password;
        string modelName = CboModelName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(modelName)) modelName = "default";

        return new AiSettings
        {
            IsEnabled = ChkAiEnabled.IsChecked == true,
            BaseUrl = string.IsNullOrWhiteSpace(TxtBaseUrl.Text) ? "http://localhost:11434/v1" : TxtBaseUrl.Text.Trim(),
            ApiKey = apiKey.Trim(),
            ModelName = modelName,
            BypassProxy = ChkBypassProxy.IsChecked == true,
            TimeoutSeconds = timeout,
            Temperature = temp
        };
    }

    private void BtnToggleApiKey_Click(object sender, RoutedEventArgs e)
    {
        _isApiKeyRevealed = !_isApiKeyRevealed;
        if (_isApiKeyRevealed)
        {
            TxtApiKeyRevealed.Text = TxtApiKey.Password;
            TxtApiKey.Visibility = Visibility.Collapsed;
            TxtApiKeyRevealed.Visibility = Visibility.Visible;
            IconApiKeyEye.Kind = PackIconKind.EyeOffOutline;
        }
        else
        {
            TxtApiKey.Password = TxtApiKeyRevealed.Text;
            TxtApiKeyRevealed.Visibility = Visibility.Collapsed;
            TxtApiKey.Visibility = Visibility.Visible;
            IconApiKeyEye.Kind = PackIconKind.EyeOutline;
        }
    }

    private async void BtnTestAndLoadModels_Click(object sender, RoutedEventArgs e)
    {
        var tempSettings = ReadSettingsFromUi();

        if (string.IsNullOrWhiteSpace(tempSettings.BaseUrl))
        {
            MessageBox.Show(Window.GetWindow(this), "Vui lòng nhập Endpoint URL trước khi kiểm tra.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnTestAndLoadModels.IsEnabled = false;
        BusyBarModels.Visibility = Visibility.Visible;
        TxtTestBtnLabel.Text = "Đang tải...";
        TxtStatusMessage.Text = "Đang kết nối server và truy vấn models...";

        try
        {
            var aiService = new OpenAiClientService();
            _logger?.Info($"Bắt đầu kiểm tra và tải models từ: {tempSettings.BaseUrl} (BypassProxy: {tempSettings.BypassProxy})...");

            var (success, models, message) = await aiService.GetAvailableModelsAsync(tempSettings);

            if (success && models.Count > 0)
            {
                string oldSelection = CboModelName.Text;
                CboModelName.ItemsSource = models;

                // Nếu model cũ có trong danh sách mới thì giữ nguyên, ngược lại chọn model đầu tiên
                if (models.Contains(oldSelection, StringComparer.OrdinalIgnoreCase))
                {
                    CboModelName.SelectedItem = models.First(m => string.Equals(m, oldSelection, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    CboModelName.SelectedItem = models[0];
                }

                TxtStatusMessage.Text = $"Thành công! Đã tìm thấy {models.Count} model trên server.";
                _logger?.Info($"Đã nạp {models.Count} models: {string.Join(", ", models)}");

                MessageBox.Show(Window.GetWindow(this),
                    $"Kết nối thành công!\nĐã tải được {models.Count} model từ máy chủ AI nội bộ.\nVui lòng chọn model mong muốn trong danh sách.",
                    "Kiểm Tra Thành Công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (success)
            {
                TxtStatusMessage.Text = "Kết nối thành công nhưng server trả về danh sách rỗng.";
                MessageBox.Show(Window.GetWindow(this), message, "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TxtStatusMessage.Text = "Kết nối thất bại. Vui lòng kiểm tra lại URL hoặc mạng nội bộ.";
                _logger?.Error($"Lỗi khi kiểm tra AI: {message}");
                MessageBox.Show(Window.GetWindow(this), message, "Kết Nối Thất Bại", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtStatusMessage.Text = $"Lỗi: {ex.Message}";
            _logger?.Error($"Exception khi kiểm tra AI: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this), $"Lỗi ngoại lệ: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnTestAndLoadModels.IsEnabled = true;
            BusyBarModels.Visibility = Visibility.Collapsed;
            TxtTestBtnLabel.Text = "Kiểm Tra & Tải Model";
        }
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var updatedAi = ReadSettingsFromUi();
            _fullSettings = _configService.Load();
            _fullSettings.Ai = updatedAi;
            _configService.Save(_fullSettings);

            _logger?.Info($"Đã lưu cấu hình AI Model: {updatedAi.BaseUrl} (Model: {updatedAi.ModelName}, BypassProxy: {updatedAi.BypassProxy}).");
            MessageBox.Show(Window.GetWindow(this),
                "Cấu hình AI Model nội bộ đã được lưu thành công!\nCác tính năng AI Schema Mapping, Cố Vấn Database và SQL Generator sẽ sử dụng cấu hình mới này.",
                "Lưu Thành Công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Lỗi khi lưu cấu hình AI: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this), $"Lỗi khi lưu cấu hình: {ex.Message}", "Lỗi Lưu Cấu Hình", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
