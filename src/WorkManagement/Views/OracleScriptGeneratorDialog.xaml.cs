using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class OracleScriptGeneratorDialog : Window
{
    private readonly List<ProjectEntry> _projects;
    private readonly OpenAiClientService _aiClient;
    private readonly ConfigService _configService;

    public OracleScriptGeneratorDialog(List<ProjectEntry> projects, ProjectEntry? defaultSelected = null)
    {
        InitializeComponent();
        _projects = projects ?? new List<ProjectEntry>();
        _aiClient = new OpenAiClientService();
        _configService = new ConfigService();

        CboProjects.ItemsSource = _projects;
        if (_projects.Count > 0)
        {
            var target = defaultSelected != null && _projects.Any(p => p.Id == defaultSelected.Id)
                ? _projects.First(p => p.Id == defaultSelected.Id)
                : _projects[0];
            CboProjects.SelectedItem = target;
        }

        UpdateContextSummary();
        SqlSyntaxHighlighter.Highlight(RtbGeneratedScript, "-- Nhấn 'Sinh Script Bằng AI' để tạo mã lệnh Oracle tương ứng...", IsCurrentThemeDark());
    }

    private bool IsCurrentThemeDark()
    {
        var settings = _configService.Load();
        var kind = ThemeService.ParseTheme(settings.ThemeName, settings.IsDarkTheme);
        return ThemeService.GetThemeOption(kind).IsDarkBase;
    }

    private string GetCurrentScriptText()
    {
        return SqlSyntaxHighlighter.GetPlainText(RtbGeneratedScript);
    }

    private ProjectEntry? CurrentProject => CboProjects.SelectedItem as ProjectEntry;

    private void CboProjects_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateContextSummary();
    }

    private void UpdateContextSummary()
    {
        var p = CurrentProject;
        if (p == null)
        {
            TxtContextSummary.Text = "Chưa chọn dự án ngữ cảnh.";
            return;
        }

        string host = string.IsNullOrWhiteSpace(p.HostServer) ? "N/A" : p.HostServer;
        string port = string.IsNullOrWhiteSpace(p.Port) ? "1521" : p.Port;
        string service = string.IsNullOrWhiteSpace(p.ServiceName) ? "ORCL" : p.ServiceName;
        string users = string.IsNullOrWhiteSpace(p.DbUsers) ? "N/A" : p.DbUsers;
        string ts = string.IsNullOrWhiteSpace(p.Tablespace) ? "N/A" : p.Tablespace;

        TxtContextSummary.Text = $"Host: {host}:{port} | Service: {service} | DB Users: {users} | Tablespaces: {ts}";
    }

    private void CboPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtCustomPrompt == null) return;

        switch (CboPresets.SelectedIndex)
        {
            case 0:
                TxtCustomPrompt.Text = "Viết script hoàn chỉnh để ngắt các kết nối active (kill session), drop toàn bộ DB Users với CASCADE và drop Tablespace kèm DATAFILES của dự án này.";
                break;
            case 1:
                TxtCustomPrompt.Text = "Viết query SQL kiểm tra dung lượng tổng dung lượng đã cấp (Allocated MB), dung lượng đã sử dụng (Used MB), phần trăm trống (Free %) của các Tablespace trong dự án, kèm chi tiết đường dẫn các Datafiles.";
                break;
            case 2:
                TxtCustomPrompt.Text = "Tạo lệnh xuất dữ liệu bằng công cụ Oracle Data Pump (expdp) để backup toàn bộ các Schemas của dự án ra file .dmp kèm log.";
                break;
            case 3:
                TxtCustomPrompt.Text = "Viết script tìm kiếm toàn bộ các đối tượng bị lỗi (INVALID objects) thuộc schema của dự án và sinh lệnh ALTER ... COMPILE / ALTER INDEX ... REBUILD tự động.";
                break;
            case 4:
                TxtCustomPrompt.Text = string.Empty;
                break;
        }
    }

    private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        var proj = CurrentProject;
        if (proj == null)
        {
            MessageBox.Show(this, "Vui lòng chọn một dự án làm ngữ cảnh.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string prompt = TxtCustomPrompt.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            MessageBox.Show(this, "Vui lòng nhập yêu cầu hoặc chọn một kịch bản mẫu.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = _configService.Load();
        if (settings.Ai == null || !settings.Ai.IsEnabled || string.IsNullOrWhiteSpace(settings.Ai.BaseUrl))
        {
            MessageBox.Show(this, "Chưa cấu hình hoặc chưa bật AI Model nội bộ.\nVui lòng vào mục Cài Đặt Kết Nối để thiết lập AI Endpoint.", "Chưa Cấu Hình AI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BtnGenerate.IsEnabled = false;
        BusyBarGenerate.Visibility = Visibility.Visible;
        TxtGenerateLabel.Text = "Đang sinh script...";

        try
        {
            var result = await _aiClient.GenerateOracleScriptAsync(settings.Ai, proj, prompt);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Script))
            {
                bool isDark = IsCurrentThemeDark();
                SqlSyntaxHighlighter.Highlight(RtbGeneratedScript, result.Script, isDark);
                TxtExplanation.Text = !string.IsNullOrWhiteSpace(result.Explanation)
                    ? result.Explanation
                    : "Đã sinh script Oracle thành công theo ngữ cảnh dự án.";
            }
            else
            {
                string msg = !string.IsNullOrEmpty(result.ErrorMessage)
                    ? result.ErrorMessage
                    : "AI không thể sinh được script cho yêu cầu này.";
                MessageBox.Show(this, msg, "Lỗi Sinh Script", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Lỗi: {ex.Message}", "Lỗi Gọi AI", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnGenerate.IsEnabled = true;
            BusyBarGenerate.Visibility = Visibility.Collapsed;
            TxtGenerateLabel.Text = "Sinh Script Bằng AI";
        }
    }

    private void BtnCopySqlOnly_Click(object sender, RoutedEventArgs e)
    {
        string fullText = GetCurrentScriptText();
        if (string.IsNullOrWhiteSpace(fullText))
        {
            MessageBox.Show(this, "Chưa có nội dung script để sao chép.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string pureSql = SqlSyntaxHighlighter.ExtractPureSql(fullText);
        Clipboard.SetText(pureSql);
        MessageBox.Show(this, "Đã trích xuất và sao chép các câu lệnh SQL vào Clipboard!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnCopyScript_Click(object sender, RoutedEventArgs e)
    {
        string script = GetCurrentScriptText();
        if (string.IsNullOrWhiteSpace(script))
        {
            MessageBox.Show(this, "Chưa có nội dung script để sao chép.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Clipboard.SetText(script);
        MessageBox.Show(this, "Đã sao chép toàn bộ nội dung vào Clipboard!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSaveScript_Click(object sender, RoutedEventArgs e)
    {
        string rawScript = GetCurrentScriptText();
        if (string.IsNullOrWhiteSpace(rawScript))
        {
            MessageBox.Show(this, "Chưa có nội dung script để lưu.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string scriptToSave = SqlSyntaxHighlighter.ExtractPureSql(rawScript);

        string projName = CurrentProject?.ProjectName ?? "OracleScript";
        var sfd = new SaveFileDialog
        {
            Title = "Lưu Script Oracle Database",
            Filter = "SQL Files (*.sql)|*.sql|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = $"{projName}_{DateTime.Now:yyyyMMdd_HHmmss}.sql"
        };

        if (sfd.ShowDialog(this) == true)
        {
            try
            {
                File.WriteAllText(sfd.FileName, scriptToSave, System.Text.Encoding.UTF8);
                MessageBox.Show(this, $"Đã lưu file thành công tại:\n{sfd.FileName}", "Lưu Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Lỗi khi lưu file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
