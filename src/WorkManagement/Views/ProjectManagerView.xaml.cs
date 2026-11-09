using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class ProjectManagerView : UserControl
{
    private readonly ProjectManagerService _projectService;
    private readonly ProjectExcelService _excelService;
    private readonly VmManagerService _vmService;
    private readonly RemoteDesktopService _rdpService;
    private readonly List<ProjectEntry> _allEntries = new();
    private readonly ObservableCollection<ProjectEntry> _displayEntries = new();
    private readonly ICollectionView _collectionView;
    private readonly DispatcherTimer _searchDebounceTimer;
    private bool _isUpdatingSelectAllHeader;
    private string _currentSearchQuery = string.Empty;

    // EXCEL-STYLE COLUMN FILTER
    private readonly Dictionary<string, HashSet<string>> _activeColumnFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _columnFilterButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ColumnFilterItem> _filterItems = new();
    private readonly ICollectionView _filterItemsView;
    private string _currentFilterColumnKey = string.Empty;
    private bool _isUpdatingFilterSelectAll;

    public ProjectManagerView()
    {
        InitializeComponent();
        _projectService = new ProjectManagerService();
        _excelService = new ProjectExcelService();
        _vmService = new VmManagerService();
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
        _collectionView.Filter = FilterProject;
        GridProjects.ItemsSource = _collectionView;

        _filterItemsView = CollectionViewSource.GetDefaultView(_filterItems);
        ListFilterItems.ItemsSource = _filterItemsView;

        LoadData();
    }

    public void LoadData()
    {
        _allEntries.Clear();
        var loaded = _projectService.LoadAll();
        _allEntries.AddRange(loaded);

        ResolveDbaFromVm(_allEntries);

        _displayEntries.Clear();
        foreach (var item in _allEntries)
        {
            _displayEntries.Add(item);
        }

        ApplyFilter();
        _ = Dispatcher.BeginInvoke(new Action(AutoFitColumns), DispatcherPriority.Loaded);
    }

    private void ResolveDbaFromVm(IEnumerable<ProjectEntry> projects)
    {
        try
        {
            var vms = _vmService.LoadAll();
            if (vms.Count == 0) return;

            foreach (var p in projects)
            {
                string host = p.HostServer?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(host)) continue;

                var matchedVm = vms.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.Ip) && v.Ip.Trim().Equals(host, StringComparison.OrdinalIgnoreCase))
                    ?? vms.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.MachineName) && v.MachineName.Trim().Equals(host, StringComparison.OrdinalIgnoreCase))
                    ?? vms.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.Ip) && (v.Ip.Contains(host) || host.Contains(v.Ip)));

                if (matchedVm != null)
                {
                    if (!string.IsNullOrWhiteSpace(matchedVm.DbaUser))
                    {
                        p.ResolvedDbaUser = matchedVm.DbaUser;
                    }
                    if (!string.IsNullOrWhiteSpace(matchedVm.DbaPassword))
                    {
                        p.ResolvedDbaPassword = matchedVm.DbaPassword;
                    }
                }
            }
        }
        catch { }
    }

    private void ApplyFilter()
    {
        _currentSearchQuery = TxtSearch?.Text?.Trim() ?? string.Empty;
        if (BtnClearSearch != null)
        {
            BtnClearSearch.Visibility = string.IsNullOrEmpty(_currentSearchQuery) ? Visibility.Collapsed : Visibility.Visible;
        }

        _collectionView?.Refresh();
        UpdateSummary();
        UpdateSelectAllState();
    }

    private bool FilterProject(object obj)
    {
        if (obj is not ProjectEntry p) return false;

        // 1. Excel-style column filters
        foreach (var kvp in _activeColumnFilters)
        {
            string val = GetColumnValue(p, kvp.Key);
            if (!kvp.Value.Contains(val))
            {
                return false;
            }
        }

        // 2. Global search query
        if (string.IsNullOrWhiteSpace(_currentSearchQuery)) return true;

        var terms = _currentSearchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            bool matches =
                (p.No != null && p.No.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.ProjectName != null && p.ProjectName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.Description != null && p.Description.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.SourceFolder != null && p.SourceFolder.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.HostServer != null && p.HostServer.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.ServiceName != null && p.ServiceName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.Port != null && p.Port.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.TnsName != null && p.TnsName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.DbUsers != null && p.DbUsers.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.Tablespace != null && p.Tablespace.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.PersonInCharge != null && p.PersonInCharge.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.ResolvedDbaUser != null && p.ResolvedDbaUser.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.Notes != null && p.Notes.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (p.Notes2 != null && p.Notes2.Contains(term, StringComparison.OrdinalIgnoreCase));

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

        TxtSummary.Text = $"Tổng cộng: {total} dự án (Đang hiển thị: {visible}){filterInfo}";

        if (BtnClearAllFilters != null)
        {
            BtnClearAllFilters.Visibility = _activeColumnFilters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void AutoFitColumns()
    {
        if (GridProjects == null || GridProjects.Columns.Count == 0) return;

        for (int i = 0; i < GridProjects.Columns.Count; i++)
        {
            var col = GridProjects.Columns[i];
            string header = GetColumnHeaderString(col);

            if (i == 0 || col.Header is CheckBox)
            {
                col.Width = new DataGridLength(60);
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

    #region Selection & Multi-Delete

    private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelectAllHeader || ChkSelectAll == null) return;

        var visibleItems = (_collectionView?.Cast<ProjectEntry>() ?? _displayEntries).ToList();
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
            if (obj is ProjectEntry p)
            {
                p.IsSelected = isSelected;
            }
        }
    }

    private void UpdateSelectAllState()
    {
        if (ChkSelectAll == null || _collectionView == null) return;

        var visibleItems = _collectionView.Cast<ProjectEntry>().ToList();
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
            ChkSelectAll.IsChecked = null;
        }
        _isUpdatingSelectAllHeader = false;
    }

    private void BtnDeleteSelectedRows_Click(object sender, RoutedEventArgs e)
    {
        var selectedProjects = _allEntries.Where(x => x.IsSelected).ToList();
        if (selectedProjects.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Vui lòng tích chọn ít nhất một dự án trong danh sách để xóa khỏi grid.",
                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parentWindow = Window.GetWindow(this);
        var confirm = MessageBox.Show(parentWindow,
            $"Bạn có chắc chắn muốn xóa {selectedProjects.Count} dòng dự án đang chọn ra khỏi grid không?\n\n(Lưu ý: Thao tác này chỉ xóa dòng thông tin trong bảng danh sách, không xóa source folder hay database trên máy chủ).",
            "Xác nhận xóa dòng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            var selectedIds = new HashSet<string>(selectedProjects.Select(x => x.Id));
            _allEntries.RemoveAll(x => selectedIds.Contains(x.Id));

            for (int i = _displayEntries.Count - 1; i >= 0; i--)
            {
                if (selectedIds.Contains(_displayEntries[i].Id))
                {
                    _displayEntries.RemoveAt(i);
                }
            }

            _projectService.SaveAll(_allEntries);
            _collectionView?.Refresh();
            UpdateSummary();
            UpdateSelectAllState();
        }
    }

    private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedProjects = _allEntries.Where(x => x.IsSelected).ToList();
        if (selectedProjects.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Vui lòng tích chọn ít nhất một dự án trong danh sách để xóa.",
                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenDeleteDialog(selectedProjects);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry entry)
        {
            OpenDeleteDialog(new List<ProjectEntry> { entry });
        }
    }

    private void OpenDeleteDialog(List<ProjectEntry> projects)
    {
        var parent = Window.GetWindow(this);
        var dialog = new ProjectDeleteExecutionDialog(projects, _projectService) { Owner = parent };

        if (dialog.ShowDialog() == true || dialog.HasDeletedProjects)
        {
            // Xóa các dự án đã thực thi khỏi danh sách lưu trữ
            var deletedIds = new HashSet<string>(projects.Select(x => x.Id));
            _allEntries.RemoveAll(x => deletedIds.Contains(x.Id));

            for (int i = _displayEntries.Count - 1; i >= 0; i--)
            {
                if (deletedIds.Contains(_displayEntries[i].Id))
                {
                    _displayEntries.RemoveAt(i);
                }
            }

            _projectService.SaveAll(_allEntries);
            _collectionView?.Refresh();
            UpdateSummary();
            UpdateSelectAllState();
        }
    }

    private void BtnDeleteAll_Click(object sender, RoutedEventArgs e)
    {
        if (_allEntries.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Danh sách dự án hiện đang trống.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var parent = Window.GetWindow(this);
        var confirm = MessageBox.Show(parent,
            $"Bạn có muốn mở hộp thoại XÓA TOÀN BỘ {_allEntries.Count} dự án hiện tại không?\n\nThao tác này sẽ dọn dẹp Source Folder, DB Users và Tablespace của toàn bộ danh sách!",
            "Xác nhận dọn dẹp tất cả",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            OpenDeleteDialog(_allEntries.ToList());
        }
    }

    #endregion

    #region Add & Edit Project

    private void BtnAddProject_Click(object sender, RoutedEventArgs e)
    {
        var parent = Window.GetWindow(this);
        var dialog = new EditProjectDialog { Owner = parent };
        if (dialog.ShowDialog() == true)
        {
            _allEntries.Add(dialog.Entry);
            _displayEntries.Add(dialog.Entry);
            _projectService.SaveAll(_allEntries);
            _collectionView?.Refresh();
            UpdateSummary();
            UpdateSelectAllState();
        }
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry entry)
        {
            OpenEditDialog(entry);
        }
    }

    private void GridProjects_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridProjects.SelectedItem is ProjectEntry entry)
        {
            OpenEditDialog(entry);
        }
    }

    private void OpenEditDialog(ProjectEntry entry)
    {
        var parent = Window.GetWindow(this);
        var dialog = new EditProjectDialog(entry) { Owner = parent };
        if (dialog.ShowDialog() == true)
        {
            int idxAll = _allEntries.FindIndex(x => x.Id == entry.Id);
            if (idxAll >= 0) _allEntries[idxAll] = dialog.Entry;

            int idxDisp = _displayEntries.IndexOf(entry);
            if (idxDisp >= 0) _displayEntries[idxDisp] = dialog.Entry;

            _projectService.SaveAll(_allEntries);
            _collectionView?.Refresh();
            UpdateSummary();
        }
    }

    #endregion

    #region Excel-Style Column Filter

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

        var groups = _allEntries
            .GroupBy(p => GetColumnValue(p, colKey))
            .OrderBy(g => string.IsNullOrWhiteSpace(g.Key) ? 1 : 0)
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
            ChkFilterSelectAll.IsChecked = null;
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
            _activeColumnFilters.Remove(_currentFilterColumnKey);
            UpdateColumnButtonVisual(_currentFilterColumnKey, false);
        }
        else
        {
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
                icon.Kind = isActive ? MaterialDesignThemes.Wpf.PackIconKind.Filter : MaterialDesignThemes.Wpf.PackIconKind.FilterOutline;
                icon.Foreground = isActive
                    ? (Brush)FindResource("AppPrimaryButtonBackground")
                    : (Brush)FindResource("AppSecondaryTextBrush");
            }
            string name = GetColumnDisplayName(colKey);
            btn.ToolTip = isActive
                ? $"Đang lọc cột '{name}'. Nhấn để thay đổi hoặc bỏ lọc."
                : $"Lọc cột '{name}'";
        }
    }

    private static string GetColumnValue(ProjectEntry p, string colKey)
    {
        return colKey switch
        {
            "No" => p.No ?? string.Empty,
            "ProjectName" => p.ProjectName ?? string.Empty,
            "Description" => p.Description ?? string.Empty,
            "SourceFolder" => p.SourceFolder ?? string.Empty,
            "HostServer" => p.HostServer ?? string.Empty,
            "ServiceName" => p.ServiceName ?? string.Empty,
            "Port" => p.Port ?? string.Empty,
            "TnsName" => p.TnsName ?? string.Empty,
            "DbUsers" => p.DbUsers ?? string.Empty,
            "Tablespace" => p.Tablespace ?? string.Empty,
            "PersonInCharge" => p.PersonInCharge ?? string.Empty,
            "DbaUser" => p.ResolvedDbaUser ?? string.Empty,
            "Notes" => p.Notes ?? string.Empty,
            _ => string.Empty
        };
    }

    private static string GetColumnDisplayName(string colKey) => colKey switch
    {
        "No" => "No",
        "ProjectName" => "Project",
        "Description" => "Giải thích thêm",
        "SourceFolder" => "Source Folder",
        "HostServer" => "Host Server",
        "ServiceName" => "Service Name",
        "TnsName" => "tnsname",
        "DbUsers" => "User (DB)",
        "Tablespace" => "Tablespace Cần Xóa",
        "PersonInCharge" => "Người phụ trách",
        "DbaUser" => "DBA User",
        "Notes" => "Ghi chú",
        _ => colKey
    };

    #endregion

    #region Excel Import & Template

    private async void BtnImportExcel_Click(object sender, RoutedEventArgs e)
    {
        var parent = Window.GetWindow(this);
        var ofd = new OpenFileDialog
        {
            Title = "Chọn file danh sách dự án Excel (.xlsx / .xls)",
            Filter = "File Excel (*.xlsx;*.xls)|*.xlsx;*.xls|Tất cả tệp (*.*)|*.*"
        };

        if (ofd.ShowDialog(parent) != true) return;

        try
        {
            var imported = await _excelService.ReadProjectsFromExcelAsync(ofd.FileName);
            if (imported.Count == 0)
            {
                MessageBox.Show(parent, "Không tìm thấy dữ liệu hợp lệ trong file Excel đã chọn.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int added = 0;
            int updated = 0;

            foreach (var item in imported)
            {
                int existingIdx = _allEntries.FindIndex(x =>
                    (!string.IsNullOrEmpty(item.ProjectName) && x.ProjectName.Equals(item.ProjectName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(item.No) && x.No.Equals(item.No, StringComparison.OrdinalIgnoreCase)));

                if (existingIdx >= 0)
                {
                    item.Id = _allEntries[existingIdx].Id;
                    _allEntries[existingIdx] = item;
                    updated++;
                }
                else
                {
                    _allEntries.Add(item);
                    added++;
                }
            }

            _projectService.SaveAll(_allEntries);
            _displayEntries.Clear();
            foreach (var item in _allEntries)
            {
                _displayEntries.Add(item);
            }

            _collectionView?.Refresh();
            UpdateSummary();
            UpdateSelectAllState();
            _ = Dispatcher.BeginInvoke(new Action(AutoFitColumns), DispatcherPriority.Loaded);

            MessageBox.Show(parent,
                $"Import thành công!\n- Thêm mới: {added} dự án\n- Cập nhật (ghi đè): {updated} dự án\n- Tổng số dự án hiện tại: {_allEntries.Count}",
                "Kết quả Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(parent, $"Lỗi khi đọc file Excel: {ex.Message}", "Lỗi Import", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDownloadTemplate_Click(object sender, RoutedEventArgs e)
    {
        var parent = Window.GetWindow(this);
        var sfd = new SaveFileDialog
        {
            Title = "Lưu file mẫu Excel danh sách dự án",
            Filter = "File Excel (*.xlsx)|*.xlsx",
            FileName = "DanhSachDuAn_Template.xlsx"
        };

        if (sfd.ShowDialog(parent) != true) return;

        try
        {
            await _excelService.ExportTemplateAsync(sfd.FileName);
            MessageBox.Show(parent, $"Đã xuất file mẫu thành công tại:\n{sfd.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(parent, $"Không thể xuất file mẫu: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnExportExcel_Click(object sender, RoutedEventArgs e)
    {
        var parent = Window.GetWindow(this);
        var visibleItems = (_collectionView?.Cast<ProjectEntry>() ?? _displayEntries).ToList();
        if (visibleItems.Count == 0)
        {
            MessageBox.Show(parent, "Không có dữ liệu dự án nào đang hiển thị để xuất Excel.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedItems = visibleItems.Where(x => x.IsSelected).ToList();
        var itemsToExport = selectedItems.Count > 0 ? selectedItems : visibleItems;

        var sfd = new SaveFileDialog
        {
            Title = $"Xuất {itemsToExport.Count} dự án ra file Excel",
            Filter = "File Excel (*.xlsx)|*.xlsx",
            FileName = $"DanhSachDuAn_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };

        if (sfd.ShowDialog(parent) != true) return;

        try
        {
            await _excelService.ExportProjectsToExcelAsync(sfd.FileName, itemsToExport);
            MessageBox.Show(parent,
                $"Đã xuất thành công {itemsToExport.Count} dự án ra file Excel:\n{sfd.FileName}",
                "Xuất Excel Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(parent, $"Lỗi khi xuất file Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Copy & Quick Actions

    private void BtnCopyFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry p && !string.IsNullOrEmpty(p.SourceFolder))
        {
            try { Clipboard.SetText(p.SourceFolder); } catch { }
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry p && !string.IsNullOrEmpty(p.SourceFolder))
        {
            try
            {
                if (Directory.Exists(p.SourceFolder))
                {
                    Process.Start("explorer.exe", p.SourceFolder);
                }
                else
                {
                    MessageBox.Show(Window.GetWindow(this), $"Thư mục không tồn tại hoặc không thể kết nối tới ổ mạng:\n{p.SourceFolder}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), $"Không thể mở thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnPlayHostRdp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry p)
        {
            string host = p.HostServer?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show(Window.GetWindow(this), "Dự án này chưa có thông tin Host Server để kết nối.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var vmList = _vmService.LoadAll();
                var matchedVm = vmList.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.Ip) && v.Ip.Trim().Equals(host, StringComparison.OrdinalIgnoreCase))
                    ?? vmList.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.MachineName) && v.MachineName.Trim().Equals(host, StringComparison.OrdinalIgnoreCase))
                    ?? vmList.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.Ip) && (v.Ip.Contains(host) || host.Contains(v.Ip)))
                    ?? vmList.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.MachineName) && (v.MachineName.IndexOf(host, StringComparison.OrdinalIgnoreCase) >= 0 || host.IndexOf(v.MachineName, StringComparison.OrdinalIgnoreCase) >= 0));

                if (matchedVm != null)
                {
                    bool success = await _rdpService.ConnectAsync(matchedVm);
                    if (!success)
                    {
                        MessageBox.Show(Window.GetWindow(this), $"Không thể mở Remote Desktop tới máy ảo {matchedVm.Ip} ({matchedVm.MachineName}).", "Lỗi RDP", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    var confirm = MessageBox.Show(Window.GetWindow(this),
                        $"Không tìm thấy máy chủ '{host}' trong Quản lý danh sách máy ảo.\n\nBạn có muốn mở Remote Desktop trực tiếp tới địa chỉ '{host}' không?",
                        "Xác nhận kết nối RDP",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirm == MessageBoxResult.Yes)
                    {
                        var directVm = new VirtualMachineEntry { Ip = host };
                        bool success = await _rdpService.ConnectAsync(directVm);
                        if (!success)
                        {
                            MessageBox.Show(Window.GetWindow(this), $"Không thể mở Remote Desktop tới {host}.", "Lỗi RDP", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), $"Lỗi khi kết nối Remote Desktop: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnCopyHost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry p && !string.IsNullOrEmpty(p.HostServer))
        {
            try { Clipboard.SetText(p.HostServer); } catch { }
        }
    }

    private void BtnCopyUsers_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry p && !string.IsNullOrEmpty(p.DbUsers))
        {
            try { Clipboard.SetText(p.DbUsers); } catch { }
        }
    }

    private void BtnToggleDbaPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry p)
        {
            p.IsDbaPasswordMasked = !p.IsDbaPasswordMasked;
        }
    }

    private void BtnCopyDbaPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProjectEntry p && !string.IsNullOrEmpty(p.ResolvedDbaPassword))
        {
            try { Clipboard.SetText(p.ResolvedDbaPassword); } catch { }
        }
    }

    #endregion
}
