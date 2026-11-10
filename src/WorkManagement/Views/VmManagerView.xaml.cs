using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkManagement.Models;
using WorkManagement.Services;
using Microsoft.Win32;

namespace WorkManagement.Views;

public partial class VmManagerView : UserControl
{
    private readonly VmManagerService _vmService;
    private readonly ExcelImportService _excelService;
    private readonly RemoteDesktopService _rdpService;
    private readonly List<VirtualMachineEntry> _allEntries = new();
    private readonly ObservableCollection<VirtualMachineEntry> _displayEntries = new();
    private readonly ICollectionView _collectionView;
    private readonly DispatcherTimer _searchDebounceTimer;
    private bool _isUpdatingSelectAllHeader;
    private string _currentSearchQuery = string.Empty;

    // EXCEL-STYLE COLUMN FILTER STATE
    private readonly Dictionary<string, HashSet<string>> _activeColumnFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _columnFilterButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ColumnFilterItem> _filterItems = new();
    private readonly ICollectionView _filterItemsView;
    private string _currentFilterColumnKey = string.Empty;
    private bool _isUpdatingFilterSelectAll;

    public VmManagerView()
    {
        InitializeComponent();
        _vmService = new VmManagerService();
        _excelService = new ExcelImportService();
        _rdpService = new RemoteDesktopService();

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _searchDebounceTimer.Tick += (s, e) =>
        {
            _searchDebounceTimer.Stop();
            ApplyFilter();
        };

        _collectionView = CollectionViewSource.GetDefaultView(_displayEntries);
        _collectionView.Filter = FilterVm;
        GridVms.ItemsSource = _collectionView;

        _filterItemsView = CollectionViewSource.GetDefaultView(_filterItems);
        ListFilterItems.ItemsSource = _filterItemsView;

        if (BtnFilter_IsFavorite != null)
        {
            _columnFilterButtons["IsFavorite"] = BtnFilter_IsFavorite;
        }

        LoadData();
    }

    public void LoadData()
    {
        _allEntries.Clear();
        var loaded = _vmService.LoadAll();
        _allEntries.AddRange(loaded);

        _displayEntries.Clear();
        foreach (var item in _allEntries)
        {
            _displayEntries.Add(item);
        }

        ApplyFilter();
        _ = Dispatcher.BeginInvoke(new Action(AutoFitColumns), DispatcherPriority.Loaded);
    }

    private void ApplyFilter()
    {
        _currentSearchQuery = TxtSearch?.Text?.Trim() ?? string.Empty;
        if (BtnClearSearch != null)
        {
            BtnClearSearch.Visibility = string.IsNullOrEmpty(_currentSearchQuery) ? Visibility.Collapsed : Visibility.Visible;
        }

        bool isFavFiltered = _activeColumnFilters.TryGetValue("IsFavorite", out var favSet) &&
                             favSet.Count == 1 && favSet.Contains("Yêu thích (★)");
        UpdateToolbarFavoriteButton(isFavFiltered);

        _collectionView?.Refresh();
        UpdateSummary();
        UpdateSelectAllState();
    }

    private bool FilterVm(object obj)
    {
        if (obj is not VirtualMachineEntry vm) return false;

        // 1. Excel-style column filters
        foreach (var kvp in _activeColumnFilters)
        {
            string val = GetColumnValue(vm, kvp.Key);
            if (!kvp.Value.Contains(val))
            {
                return false;
            }
        }

        // 2. Global search box query
        if (string.IsNullOrWhiteSpace(_currentSearchQuery)) return true;

        var terms = _currentSearchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            bool matches =
                (vm.IsFavorite && (term.Equals("fav", StringComparison.OrdinalIgnoreCase) ||
                                   term.Equals("@fav", StringComparison.OrdinalIgnoreCase) ||
                                   term.Equals("star", StringComparison.OrdinalIgnoreCase) ||
                                   "yêu thích".Contains(term, StringComparison.OrdinalIgnoreCase))) ||
                (vm.Ip != null && vm.Ip.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.MachineName != null && vm.MachineName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.DomainOrWorkgroup != null && vm.DomainOrWorkgroup.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.Os != null && vm.Os.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.Username != null && vm.Username.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.Tns != null && vm.Tns.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.DatabaseName != null && vm.DatabaseName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.OracleVersion != null && vm.OracleVersion.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.Tester != null && vm.Tester.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.Notes != null && vm.Notes.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.ExpirationDate != null && vm.ExpirationDate.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.DbaUser != null && vm.DbaUser.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (vm.No != null && vm.No.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (!matches) return false;
        }

        return true;
    }

    private void UpdateSummary()
    {
        if (TxtSummary == null) return;
        int total = _allEntries.Count;
        int visible = _collectionView?.Cast<object>().Count() ?? _displayEntries.Count;

        string filterInfo = _activeColumnFilters.Count > 0
            ? $" (Đang lọc {_activeColumnFilters.Count} cột)"
            : string.Empty;

        TxtSummary.Text = $"Tổng cộng: {total} máy ảo (Đang hiển thị: {visible}){filterInfo}";

        if (BtnClearAllFilters != null)
        {
            BtnClearAllFilters.Visibility = _activeColumnFilters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void AutoFitColumns()
    {
        if (GridVms == null || GridVms.Columns.Count == 0) return;

        for (int i = 0; i < GridVms.Columns.Count; i++)
        {
            var col = GridVms.Columns[i];
            string header = GetColumnHeaderString(col);

            if (i == 0 || col.Header is CheckBox)
            {
                col.Width = new DataGridLength(60);
            }
            else if (col.Header is MaterialDesignThemes.Wpf.PackIcon)
            {
                col.Width = new DataGridLength(48);
            }
            else if (header == "Play")
            {
                col.Width = new DataGridLength(85);
            }
            else if (header == "Thao Tác")
            {
                col.Width = new DataGridLength(85);
            }
            else
            {
                col.Width = new DataGridLength(1.0, DataGridLengthUnitType.Auto);
            }
        }
    }

    private static string GetColumnHeaderString(DataGridColumn col)
    {
        if (col.Header is string str) return str;
        if (col.Header is Panel panel)
        {
            var tb = panel.Children.OfType<TextBlock>().FirstOrDefault();
            if (tb != null) return tb.Text;
        }
        return col.Header?.ToString() ?? string.Empty;
    }

    private void BtnAutoFitColumns_Click(object sender, RoutedEventArgs e)
    {
        AutoFitColumns();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = TxtSearch?.Text?.Trim() ?? string.Empty;
        if (BtnClearSearch != null)
        {
            BtnClearSearch.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _searchDebounceTimer.Stop();
            ApplyFilter();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _searchDebounceTimer.Stop();
            TxtSearch.Text = string.Empty;
            ApplyFilter();
            e.Handled = true;
        }
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        TxtSearch.Text = string.Empty;
        ApplyFilter();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadData();
    }

    #region Selection & Multi-Delete Logic

    private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelectAllHeader || ChkSelectAll == null) return;

        var visibleItems = (_collectionView?.Cast<VirtualMachineEntry>() ?? _displayEntries).ToList();
        bool allSelected = visibleItems.Count > 0 && visibleItems.All(x => x.IsSelected);
        bool newState = !allSelected;

        SetAllSelected(newState);

        _isUpdatingSelectAllHeader = true;
        ChkSelectAll.IsChecked = newState;
        _isUpdatingSelectAllHeader = false;
    }

    private void RowCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateSelectAllState();
    }

    private void SetAllSelected(bool isSelected)
    {
        if (_collectionView == null) return;
        foreach (var obj in _collectionView)
        {
            if (obj is VirtualMachineEntry vm)
            {
                vm.IsSelected = isSelected;
            }
        }
    }

    private void UpdateSelectAllState()
    {
        if (ChkSelectAll == null || _collectionView == null) return;

        var visibleItems = _collectionView.Cast<VirtualMachineEntry>().ToList();
        if (visibleItems.Count == 0)
        {
            _isUpdatingSelectAllHeader = true;
            ChkSelectAll.IsChecked = false;
            _isUpdatingSelectAllHeader = false;
            return;
        }

        int selectedCount = visibleItems.Count(x => x.IsSelected);

        _isUpdatingSelectAllHeader = true;
        if (selectedCount == 0)
        {
            ChkSelectAll.IsChecked = false;
        }
        else if (selectedCount == visibleItems.Count)
        {
            ChkSelectAll.IsChecked = true;
        }
        else
        {
            ChkSelectAll.IsChecked = null; // Indeterminate state (dash)
        }
        _isUpdatingSelectAllHeader = false;
    }

    private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedVms = _allEntries.Where(x => x.IsSelected).ToList();
        if (selectedVms.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Vui lòng tích chọn ít nhất một máy ảo trong danh sách để xóa.",
                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parentWindow = Window.GetWindow(this);
        var confirm = MessageBox.Show(parentWindow,
            $"Bạn có chắc chắn muốn xóa {selectedVms.Count} máy ảo đã chọn không?",
            "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            var selectedIds = new HashSet<string>(selectedVms.Select(x => x.Id));
            _allEntries.RemoveAll(x => selectedIds.Contains(x.Id));

            for (int i = _displayEntries.Count - 1; i >= 0; i--)
            {
                if (selectedIds.Contains(_displayEntries[i].Id))
                {
                    _displayEntries.RemoveAt(i);
                }
            }

            _vmService.SaveAll(_allEntries);
            _collectionView?.Refresh();
            UpdateSummary();
            UpdateSelectAllState();

            MessageBox.Show(parentWindow,
                $"Đã xóa thành công {selectedVms.Count} máy ảo.",
                "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion

    #region Excel-Style Column Filter Logic

    private void BtnColumnFilter_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button btn || btn.Tag is not string colKey) return;

        OpenFilterPopup(btn, colKey);
    }

    private void OpenFilterPopup(Button anchorButton, string colKey)
    {
        _currentFilterColumnKey = colKey;
        _columnFilterButtons[colKey] = anchorButton;

        TxtFilterColumnTitle.Text = $"Lọc Cột: {GetColumnDisplayName(colKey)}";

        // Group values and counts from _allEntries
        var groups = _allEntries
            .GroupBy(vm => GetColumnValue(vm, colKey))
            .OrderBy(g => string.IsNullOrWhiteSpace(g.Key) ? 1 : 0) // blank items at the end
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        bool hasFilter = _activeColumnFilters.TryGetValue(colKey, out var allowedSet);

        _filterItems.Clear();
        foreach (var g in groups)
        {
            string raw = g.Key;
            string display = string.IsNullOrWhiteSpace(raw)
                ? $"(Trống / Blanks) ({g.Count()})"
                : $"{raw} ({g.Count()})";

            bool isChecked = !hasFilter || (allowedSet != null && allowedSet.Contains(raw));

            _filterItems.Add(new ColumnFilterItem
            {
                RawValue = raw,
                DisplayText = display,
                IsChecked = isChecked
            });
        }

        TxtFilterSearch.Text = string.Empty;
        UpdateFilterSelectAllState();
        UpdateFilterItemCount();

        FilterPopup.PlacementTarget = anchorButton;
        FilterPopup.IsOpen = true;
        TxtFilterSearch.Focus();
    }

    private void TxtFilterSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = TxtFilterSearch.Text.Trim();
        BtnClearFilterSearch.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;

        _filterItemsView.Filter = obj =>
        {
            if (string.IsNullOrEmpty(q)) return true;
            if (obj is ColumnFilterItem item)
            {
                return item.DisplayText.Contains(q, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        };
        _filterItemsView.Refresh();

        UpdateFilterSelectAllState();
        UpdateFilterItemCount();
    }

    private void BtnClearFilterSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtFilterSearch.Text = string.Empty;
    }

    private void ChkFilterSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingFilterSelectAll || ChkFilterSelectAll == null) return;

        var visibleItems = _filterItemsView.Cast<ColumnFilterItem>().ToList();
        bool allChecked = visibleItems.Count > 0 && visibleItems.All(x => x.IsChecked);
        bool newState = !allChecked;

        foreach (var item in visibleItems)
        {
            item.IsChecked = newState;
        }

        _isUpdatingFilterSelectAll = true;
        ChkFilterSelectAll.IsChecked = newState;
        _isUpdatingFilterSelectAll = false;
        UpdateFilterItemCount();
    }

    private void FilterItemCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateFilterSelectAllState();
        UpdateFilterItemCount();
    }

    private void UpdateFilterSelectAllState()
    {
        if (ChkFilterSelectAll == null || _filterItemsView == null) return;

        var visibleItems = _filterItemsView.Cast<ColumnFilterItem>().ToList();
        if (visibleItems.Count == 0)
        {
            _isUpdatingFilterSelectAll = true;
            ChkFilterSelectAll.IsChecked = false;
            _isUpdatingFilterSelectAll = false;
            return;
        }

        int checkedCount = visibleItems.Count(x => x.IsChecked);
        _isUpdatingFilterSelectAll = true;
        if (checkedCount == 0)
        {
            ChkFilterSelectAll.IsChecked = false;
        }
        else if (checkedCount == visibleItems.Count)
        {
            ChkFilterSelectAll.IsChecked = true;
        }
        else
        {
            ChkFilterSelectAll.IsChecked = null; // Indeterminate
        }
        _isUpdatingFilterSelectAll = false;
    }

    private void UpdateFilterItemCount()
    {
        if (TxtFilterItemCount == null || _filterItemsView == null) return;
        var visibleItems = _filterItemsView.Cast<ColumnFilterItem>().ToList();
        int checkedCount = visibleItems.Count(x => x.IsChecked);
        TxtFilterItemCount.Text = $"{checkedCount} / {visibleItems.Count} mục";
    }

    private void BtnApplyFilter_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilterColumnKey)) return;

        int totalCount = _filterItems.Count;
        int checkedCount = _filterItems.Count(x => x.IsChecked);

        if (checkedCount == totalCount)
        {
            // All selected = remove column filter
            _activeColumnFilters.Remove(_currentFilterColumnKey);
            UpdateColumnButtonVisual(_currentFilterColumnKey, false);
        }
        else
        {
            // Subset selected
            var set = new HashSet<string>(_filterItems.Where(x => x.IsChecked).Select(x => x.RawValue), StringComparer.OrdinalIgnoreCase);
            _activeColumnFilters[_currentFilterColumnKey] = set;
            UpdateColumnButtonVisual(_currentFilterColumnKey, true);
        }

        FilterPopup.IsOpen = false;
        ApplyFilter();
    }

    private void BtnClearColumnFilter_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilterColumnKey)) return;

        _activeColumnFilters.Remove(_currentFilterColumnKey);
        UpdateColumnButtonVisual(_currentFilterColumnKey, false);

        FilterPopup.IsOpen = false;
        ApplyFilter();
    }

    private void BtnCloseFilterPopup_Click(object sender, RoutedEventArgs e)
    {
        FilterPopup.IsOpen = false;
    }

    private void BtnClearAllFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var key in _activeColumnFilters.Keys.ToList())
        {
            UpdateColumnButtonVisual(key, false);
        }
        _activeColumnFilters.Clear();
        UpdateToolbarFavoriteButton(false);
        ApplyFilter();
    }

    private void BtnSortAsc_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilterColumnKey)) return;
        ApplySort(_currentFilterColumnKey, ListSortDirection.Ascending);
        FilterPopup.IsOpen = false;
    }

    private void BtnSortDesc_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilterColumnKey)) return;
        ApplySort(_currentFilterColumnKey, ListSortDirection.Descending);
        FilterPopup.IsOpen = false;
    }

    private void ApplySort(string colKey, ListSortDirection direction)
    {
        if (_collectionView == null) return;
        _collectionView.SortDescriptions.Clear();
        _collectionView.SortDescriptions.Add(new SortDescription(colKey, direction));
    }

    private void UpdateColumnButtonVisual(string colKey, bool isActive)
    {
        if (_columnFilterButtons.TryGetValue(colKey, out var btn))
        {
            if (btn.Content is MaterialDesignThemes.Wpf.PackIcon icon)
            {
                if (colKey.Equals("IsFavorite", StringComparison.OrdinalIgnoreCase))
                {
                    icon.Kind = isActive ? MaterialDesignThemes.Wpf.PackIconKind.Filter : MaterialDesignThemes.Wpf.PackIconKind.Star;
                    icon.Foreground = isActive
                        ? (Brush)FindResource("AppPrimaryButtonBackground")
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                }
                else
                {
                    icon.Kind = isActive ? MaterialDesignThemes.Wpf.PackIconKind.Filter : MaterialDesignThemes.Wpf.PackIconKind.FilterOutline;
                    icon.Foreground = isActive
                        ? (Brush)FindResource("AppPrimaryButtonBackground")
                        : (Brush)FindResource("AppSecondaryTextBrush");
                }
            }
            string name = GetColumnDisplayName(colKey);
            btn.ToolTip = isActive
                ? $"Đang lọc cột '{name}'. Nhấn để thay đổi hoặc bỏ lọc."
                : $"Lọc cột '{name}'";
        }
    }

    private static string GetColumnValue(VirtualMachineEntry vm, string colKey)
    {
        return colKey switch
        {
            "IsFavorite" => vm.IsFavorite ? "Yêu thích (★)" : "Bình thường",
            "Ip" => vm.Ip ?? string.Empty,
            "MachineName" => vm.MachineName ?? string.Empty,
            "DomainOrWorkgroup" => vm.DomainOrWorkgroup ?? string.Empty,
            "Os" => vm.Os ?? string.Empty,
            "Username" => vm.Username ?? string.Empty,
            "Tns" => vm.Tns ?? string.Empty,
            "DatabaseName" => vm.DatabaseName ?? string.Empty,
            "OracleVersion" => vm.OracleVersion ?? string.Empty,
            "Tester" => vm.Tester ?? string.Empty,
            "Notes" => vm.Notes ?? string.Empty,
            "ExpirationDate" => vm.ExpirationDate ?? string.Empty,
            "DbaUser" => vm.DbaUser ?? string.Empty,
            _ => string.Empty
        };
    }

    private static string GetColumnDisplayName(string colKey) => colKey switch
    {
        "IsFavorite" => "Yêu Thích",
        "Ip" => "Địa Chỉ IP",
        "MachineName" => "Tên Máy",
        "DomainOrWorkgroup" => "Domain / Workgroup",
        "Os" => "Hệ Điều Hành",
        "Username" => "User",
        "Tns" => "TNS",
        "DatabaseName" => "Database Name",
        "OracleVersion" => "Oracle Ver",
        "Tester" => "Người KT",
        "Notes" => "Ghi Chú",
        "ExpirationDate" => "Ngày Hết Hạn",
        _ => colKey
    };

    #endregion

    private void BtnAddVm_Click(object sender, RoutedEventArgs e)
    {
        var parentWindow = Window.GetWindow(this);
        var dialog = new EditVmDialog { Owner = parentWindow };
        if (dialog.ShowDialog() == true)
        {
            _allEntries.Add(dialog.Entry);
            _displayEntries.Add(dialog.Entry);
            _vmService.SaveAll(_allEntries);
            _collectionView?.Refresh();
            UpdateSummary();
            UpdateSelectAllState();
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VirtualMachineEntry entry)
        {
            OpenEditDialog(entry);
        }
    }

    private void GridVms_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridVms.SelectedItem is VirtualMachineEntry entry)
        {
            OpenEditDialog(entry);
        }
    }

    private void OpenEditDialog(VirtualMachineEntry entry)
    {
        var parentWindow = Window.GetWindow(this);
        var dialog = new EditVmDialog(entry) { Owner = parentWindow };
        if (dialog.ShowDialog() == true)
        {
            int idx = _allEntries.FindIndex(x => x.Id == entry.Id);
            if (idx >= 0)
            {
                _allEntries[idx] = dialog.Entry;
            }

            int idxDisp = _displayEntries.IndexOf(entry);
            if (idxDisp >= 0)
            {
                _displayEntries[idxDisp] = dialog.Entry;
            }

            _vmService.SaveAll(_allEntries);
            _collectionView?.Refresh();
            UpdateSummary();
        }
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VirtualMachineEntry entry)
        {
            var parentWindow = Window.GetWindow(this);
            var confirm = MessageBox.Show(parentWindow,
                $"Bạn có chắc chắn muốn xóa máy ảo {entry.MachineName} ({entry.Ip})?",
                "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                _allEntries.RemoveAll(x => x.Id == entry.Id);
                _displayEntries.Remove(entry);
                _vmService.SaveAll(_allEntries);
                _collectionView?.Refresh();
                UpdateSummary();
                UpdateSelectAllState();
            }
        }
    }

    private void BtnDeleteAll_Click(object sender, RoutedEventArgs e)
    {
        if (_allEntries.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Danh sách máy ảo hiện đang trống.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parentWindow = Window.GetWindow(this);
        var confirm = MessageBox.Show(parentWindow,
            $"Bạn có chắc chắn muốn XÓA TOÀN BỘ {_allEntries.Count} máy ảo hiện tại không?\n\nThao tác này sẽ dọn sạch danh sách để bạn có thể import lại từ đầu.",
            "Xác nhận xóa tất cả", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            _allEntries.Clear();
            _displayEntries.Clear();
            _activeColumnFilters.Clear();
            foreach (var btn in _columnFilterButtons.Values)
            {
                if (btn.Content is MaterialDesignThemes.Wpf.PackIcon icon)
                {
                    icon.Kind = MaterialDesignThemes.Wpf.PackIconKind.FilterOutline;
                    icon.Foreground = (Brush)FindResource("AppSecondaryTextBrush");
                }
            }
            _vmService.ClearAll();
            _collectionView?.Refresh();
            UpdateSummary();
            UpdateSelectAllState();
            MessageBox.Show(parentWindow, "Đã xóa toàn bộ danh sách máy ảo thành công.", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnToolbarPlay_Click(object sender, RoutedEventArgs e)
    {
        var selected = GridVms.SelectedItem as VirtualMachineEntry ?? _collectionView?.Cast<VirtualMachineEntry>().FirstOrDefault();
        if (selected == null)
        {
            MessageBox.Show(Window.GetWindow(this), "Vui lòng chọn một máy ảo từ danh sách để kết nối Remote Desktop.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await ConnectToVmAsync(selected);
    }

    private async void BtnPlayRdp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VirtualMachineEntry entry)
        {
            await ConnectToVmAsync(entry);
        }
    }

    private void BtnFilterFavoriteOnly_Click(object sender, RoutedEventArgs e)
    {
        bool isFilteringFav = _activeColumnFilters.TryGetValue("IsFavorite", out var set) &&
                              set.Count == 1 && set.Contains("Yêu thích (★)");

        if (isFilteringFav)
        {
            _activeColumnFilters.Remove("IsFavorite");
            UpdateColumnButtonVisual("IsFavorite", false);
            UpdateToolbarFavoriteButton(false);
        }
        else
        {
            _activeColumnFilters["IsFavorite"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Yêu thích (★)" };
            UpdateColumnButtonVisual("IsFavorite", true);
            UpdateToolbarFavoriteButton(true);
        }

        ApplyFilter();
    }

    private void UpdateToolbarFavoriteButton(bool isActive)
    {
        if (BtnFilterFavoriteOnly == null) return;

        if (isActive)
        {
            BtnFilterFavoriteOnly.Style = (Style)FindResource("AppPrimaryButtonStyle");
            if (IconFilterFavorite != null)
            {
                IconFilterFavorite.Kind = MaterialDesignThemes.Wpf.PackIconKind.Star;
                IconFilterFavorite.Foreground = Brushes.White;
            }
            if (TxtFilterFavoriteLabel != null)
            {
                TxtFilterFavoriteLabel.Text = "Đang Lọc: Yêu Thích";
            }
            BtnFilterFavoriteOnly.ToolTip = "Đang chỉ hiển thị máy ảo Yêu thích. Nhấn để hiển thị tất cả.";
        }
        else
        {
            BtnFilterFavoriteOnly.Style = (Style)FindResource("AppOutlinedButtonStyle");
            if (IconFilterFavorite != null)
            {
                IconFilterFavorite.Kind = MaterialDesignThemes.Wpf.PackIconKind.StarOutline;
                IconFilterFavorite.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            }
            if (TxtFilterFavoriteLabel != null)
            {
                TxtFilterFavoriteLabel.Text = "Yêu Thích";
            }
            BtnFilterFavoriteOnly.ToolTip = "Chỉ hiển thị các máy ảo Yêu thích (★)";
        }
    }

    private void BtnToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn && btn.DataContext is VirtualMachineEntry entry)
        {
            entry.IsFavorite = !entry.IsFavorite;
            _vmService.SaveAll(_allEntries);
        }
    }

    private async System.Threading.Tasks.Task ConnectToVmAsync(VirtualMachineEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Ip))
        {
            MessageBox.Show(Window.GetWindow(this), "Địa chỉ IP của máy ảo này đang bị trống.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool success = await _rdpService.ConnectAsync(entry);
        if (!success)
        {
            MessageBox.Show(Window.GetWindow(this), $"Không thể mở Remote Desktop tới {entry.Ip}.", "Lỗi RDP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCopyIp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VirtualMachineEntry entry && !string.IsNullOrEmpty(entry.Ip))
        {
            try
            {
                Clipboard.SetText(entry.Ip);
            }
            catch { }
        }
    }

    private void BtnCopyPass_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VirtualMachineEntry entry && !string.IsNullOrEmpty(entry.Password))
        {
            try
            {
                Clipboard.SetText(entry.Password);
            }
            catch { }
        }
    }

    private void BtnCopyDbaPass_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VirtualMachineEntry entry && !string.IsNullOrEmpty(entry.DbaPassword))
        {
            try
            {
                Clipboard.SetText(entry.DbaPassword);
            }
            catch { }
        }
    }

    private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VirtualMachineEntry entry)
        {
            entry.IsPasswordMasked = !entry.IsPasswordMasked;
        }
    }

    private async void BtnImportExcel_Click(object sender, RoutedEventArgs e)
    {
        var parentWindow = Window.GetWindow(this);
        var ofd = new OpenFileDialog
        {
            Title = "Chọn file danh sách máy ảo Excel",
            Filter = "File Excel (*.xlsx;*.xls)|*.xlsx;*.xls|Tất cả tệp (*.*)|*.*"
        };

        if (ofd.ShowDialog(parentWindow) != true) return;

        try
        {
            var sheets = await _excelService.ReadSheetsFromExcelAsync(ofd.FileName);
            if (sheets.Count == 0 || sheets.All(s => s.Entries.Count == 0))
            {
                MessageBox.Show(parentWindow, "Không tìm thấy dữ liệu hợp lệ trong file Excel đã chọn.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var defaultSheet = sheets.FirstOrDefault(s => s.IsRecommended) ?? sheets[0];
            var testerDialog = new SelectTesterImportDialog(defaultSheet.Entries) { Owner = parentWindow };
            if (testerDialog.ShowDialog() != true) return;

            var selectedTesters = testerDialog.SelectedRawTesters;
            var previewDialog = new ExcelImportPreviewDialog(ofd.FileName, sheets, selectedTesters) { Owner = parentWindow };
            if (previewDialog.ShowDialog() == true && previewDialog.ImportedList.Count > 0)
            {
                var result = _excelService.MergeIntoList(_allEntries, previewDialog.ImportedList, previewDialog.SelectedAction);
                _displayEntries.Clear();
                foreach (var item in _allEntries)
                {
                    _displayEntries.Add(item);
                }

                _vmService.SaveAll(_allEntries);
                _collectionView?.Refresh();
                UpdateSummary();
                UpdateSelectAllState();
                _ = Dispatcher.BeginInvoke(new Action(AutoFitColumns), DispatcherPriority.Loaded);

                MessageBox.Show(parentWindow,
                    $"Import hoàn tất!\n- Tổng số dòng đọc được: {result.TotalRead}\n- Thêm mới: {result.AddedCount}\n- Cập nhật (ghi đè): {result.UpdatedCount}\n- Bỏ qua: {result.SkippedCount}",
                    "Kết quả Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(parentWindow, $"Lỗi khi đọc file Excel: {ex.Message}", "Lỗi Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDownloadTemplate_Click(object sender, RoutedEventArgs e)
    {
        var parentWindow = Window.GetWindow(this);
        var sfd = new SaveFileDialog
        {
            Title = "Lưu file mẫu Excel",
            Filter = "File Excel (*.xlsx)|*.xlsx",
            FileName = "DanhSachMayAo_Template.xlsx"
        };

        if (sfd.ShowDialog(parentWindow) != true) return;

        try
        {
            await _excelService.ExportTemplateAsync(sfd.FileName);
            MessageBox.Show(parentWindow, $"Đã xuất file mẫu thành công tại:\n{sfd.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(parentWindow, $"Không thể xuất file mẫu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var parentWindow = Window.GetWindow(this);
        var visibleItems = (_collectionView?.Cast<VirtualMachineEntry>() ?? _displayEntries).ToList();
        if (visibleItems.Count == 0)
        {
            MessageBox.Show(parentWindow, "Không có dữ liệu máy ảo nào đang hiển thị để xuất Excel.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedItems = visibleItems.Where(x => x.IsSelected).ToList();
        var itemsToExport = selectedItems.Count > 0 ? selectedItems : visibleItems;

        var sfd = new SaveFileDialog
        {
            Title = $"Xuất {itemsToExport.Count} máy ảo ra file Excel",
            Filter = "File Excel (*.xlsx)|*.xlsx",
            FileName = $"DanhSachMayAo_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };

        if (sfd.ShowDialog(parentWindow) != true) return;

        try
        {
            await _excelService.ExportVmListAsync(sfd.FileName, itemsToExport);
            MessageBox.Show(parentWindow,
                $"Đã xuất thành công {itemsToExport.Count} máy ảo ra file Excel:\n{sfd.FileName}",
                "Xuất Excel Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(parentWindow, $"Lỗi khi xuất file Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class ColumnFilterItem : INotifyPropertyChanged
{
    private bool _isChecked = true;

    public string RawValue { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
