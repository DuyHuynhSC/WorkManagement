using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class ExcelImportPreviewDialog : Window
{
    private readonly string _filePath;
    private readonly List<ExcelSheetData> _sheets;
    private List<VirtualMachineEntry> _rawCurrentSheetEntries = new();
    private HashSet<string>? _selectedTesters;

    public List<VirtualMachineEntry> ImportedList { get; private set; } = new();
    public ExcelImportConflictAction SelectedAction { get; private set; } = ExcelImportConflictAction.OverwriteExisting;

    public ExcelImportPreviewDialog(string filePath, List<ExcelSheetData> sheets, HashSet<string>? initialSelectedTesters = null)
    {
        InitializeComponent();
        _filePath = filePath;
        _sheets = sheets ?? new List<ExcelSheetData>();
        _selectedTesters = initialSelectedTesters;

        TxtFilePathSubtitle.Text = $"File: {Path.GetFileName(filePath)} ({filePath})";

        CboSheets.ItemsSource = _sheets;
        if (_sheets.Count > 0)
        {
            CboSheets.SelectedIndex = 0;
            UpdatePreviewWithSheet(_sheets[0]);
        }
    }

    public ExcelImportPreviewDialog(string filePath, List<VirtualMachineEntry> entries, HashSet<string>? initialSelectedTesters = null)
        : this(filePath, new List<ExcelSheetData>
        {
            new ExcelSheetData
            {
                SheetName = "Mặc định",
                Entries = entries,
                IsRecommended = true
            }
        }, initialSelectedTesters)
    {
    }

    private void CboSheets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboSheets.SelectedItem is ExcelSheetData selectedSheet)
        {
            // Khi đổi sheet, nếu chưa có bộ lọc thì giữ nguyên, hoặc reset về dữ liệu sheet mới
            UpdatePreviewWithSheet(selectedSheet);
        }
    }

    private void UpdatePreviewWithSheet(ExcelSheetData sheet)
    {
        _rawCurrentSheetEntries = sheet.Entries ?? new List<VirtualMachineEntry>();
        ApplyTesterFilter();
    }

    private void ApplyTesterFilter()
    {
        if (_selectedTesters != null && _selectedTesters.Count > 0)
        {
            ImportedList = _rawCurrentSheetEntries
                .Where(e => _selectedTesters.Contains((e.Tester ?? string.Empty).Trim()))
                .ToList();
            TxtTesterFilterButton.Text = $"Người KT ({_selectedTesters.Count})";
        }
        else
        {
            ImportedList = new List<VirtualMachineEntry>(_rawCurrentSheetEntries);
            TxtTesterFilterButton.Text = "Lọc Người KT (Tất cả)";
        }

        TxtCountBadge.Text = $"{ImportedList.Count}/{_rawCurrentSheetEntries.Count} máy";
        GridPreview.ItemsSource = null;
        GridPreview.ItemsSource = ImportedList;
    }

    private void BtnFilterTester_Click(object sender, RoutedEventArgs e)
    {
        if (_rawCurrentSheetEntries.Count == 0)
        {
            MessageBox.Show(this, "Sheet hiện tại không có dữ liệu máy ảo để lọc.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SelectTesterImportDialog(_rawCurrentSheetEntries, _selectedTesters)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedTesters = dialog.SelectedRawTesters;
            ApplyTesterFilter();
        }
    }

    private async void BtnAiMapColumns_Click(object sender, RoutedEventArgs e)
    {
        if (CboSheets.SelectedItem is not ExcelSheetData selectedSheet)
        {
            MessageBox.Show(this, "Vui lòng chọn sheet cần AI phân tích.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var configService = new ConfigService();
        var settings = configService.Load();
        if (settings.Ai == null || !settings.Ai.IsEnabled || string.IsNullOrWhiteSpace(settings.Ai.BaseUrl))
        {
            MessageBox.Show(this, "Chưa bật hoặc chưa cấu hình AI Model nội bộ.\nVui lòng vào mục Cài Đặt Kết Nối để thiết lập AI Endpoint và kiểm tra kết nối.", "Chưa Cấu Hình AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnAiMapColumns.IsEnabled = false;
        AiMappingBusyBar.Visibility = Visibility.Visible;
        TxtAiMapButton.Text = "Đang map...";

        try
        {
            var importService = new ExcelImportService();
            var (remappedSheet, aiResult) = await importService.RemapSheetWithAiAsync(
                _filePath,
                selectedSheet.SheetName,
                settings.Ai);

            if (aiResult.Success && remappedSheet.Entries.Count > 0)
            {
                selectedSheet.Entries = remappedSheet.Entries;
                UpdatePreviewWithSheet(selectedSheet);

                string detail = string.Join(", ", aiResult.ColumnMapping.Select(kv => $"[{kv.Key}]->{kv.Value}"));
                MessageBox.Show(this, $"AI đã nhận diện thành công {aiResult.ColumnMapping.Count} cột:\n{detail}\n\nTổng số máy: {remappedSheet.Entries.Count}\n{aiResult.Explanation}", "AI Schema Mapping Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string msg = !string.IsNullOrEmpty(aiResult.ErrorMessage)
                    ? aiResult.ErrorMessage
                    : "AI không thể ánh xạ được các cột phù hợp.";
                MessageBox.Show(this, msg, "AI Schema Mapping Thất Bại", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Lỗi trong quá trình gọi AI: {ex.Message}", "Lỗi AI", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnAiMapColumns.IsEnabled = true;
            AiMappingBusyBar.Visibility = Visibility.Collapsed;
            TxtAiMapButton.Text = "AI Map Cột";
        }
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (ImportedList.Count == 0)
        {
            MessageBox.Show(this, "Danh sách máy ảo đang trống hoặc các người kiểm tra được chọn không có máy nào để import.", "Không có dữ liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (RbSkip.IsChecked == true)
        {
            SelectedAction = ExcelImportConflictAction.SkipExisting;
        }
        else if (RbAppend.IsChecked == true)
        {
            SelectedAction = ExcelImportConflictAction.AppendAll;
        }
        else
        {
            SelectedAction = ExcelImportConflictAction.OverwriteExisting;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
