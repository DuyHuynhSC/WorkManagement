using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public partial class DeletedProjectManagerView : UserControl
{
    private readonly ProjectManagerService _projectService;
    private List<ProjectEntry> _allEntries = new();
    private readonly ObservableCollection<ProjectEntry> _displayEntries = new();
    private ICollectionView? _collectionView;
    private bool _isUpdatingSelectAllHeader;

    public event EventHandler? ProjectsRestored;

    public DeletedProjectManagerView()
    {
        InitializeComponent();
        _projectService = new ProjectManagerService();
        InitView();
    }

    public void Initialize(ProjectManagerService projectService)
    {
        LoadData();
    }

    private void InitView()
    {
        _collectionView = CollectionViewSource.GetDefaultView(_displayEntries);
        _collectionView.Filter = FilterProject;
        GridDeletedProjects.ItemsSource = _collectionView;

        LoadData();
    }

    public void LoadData()
    {
        _allEntries = _projectService.LoadAllDeleted();

        _displayEntries.Clear();
        foreach (var p in _allEntries)
        {
            p.IsSelected = false;
            _displayEntries.Add(p);
        }

        _collectionView?.Refresh();
        UpdateSummary();
        UpdateSelectAllState();
    }

    private bool FilterProject(object item)
    {
        if (item is not ProjectEntry project) return false;

        string query = TxtSearch.Text.Trim();
        if (string.IsNullOrEmpty(query)) return true;

        return (project.ProjectName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (project.No?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (project.SourceFolder?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (project.HostServer?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (project.DbUsers?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (project.Tablespace?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (project.PersonInCharge?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (project.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (project.Notes2?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        BtnClearSearch.Visibility = string.IsNullOrEmpty(TxtSearch.Text) ? Visibility.Collapsed : Visibility.Visible;
        _collectionView?.Refresh();
        UpdateSummary();
        UpdateSelectAllState();
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = string.Empty;
    }

    private void UpdateSummary()
    {
        int total = _allEntries.Count;
        int selected = _allEntries.Count(x => x.IsSelected);
        int displayed = _collectionView?.Cast<object>().Count() ?? 0;

        if (displayed < total)
        {
            TxtSummary.Text = $"Hiển thị: {displayed}/{total} dự án đã xóa | Đã chọn: {selected} dự án";
        }
        else
        {
            TxtSummary.Text = $"Tổng số: {total} dự án đã xóa | Đã chọn: {selected} dự án";
        }
    }

    private void UpdateSelectAllState()
    {
        if (_isUpdatingSelectAllHeader) return;
        _isUpdatingSelectAllHeader = true;

        var visible = _collectionView?.Cast<ProjectEntry>().ToList() ?? new List<ProjectEntry>();
        if (visible.Count == 0)
        {
            ChkSelectAll.IsChecked = false;
        }
        else if (visible.All(x => x.IsSelected))
        {
            ChkSelectAll.IsChecked = true;
        }
        else if (visible.Any(x => x.IsSelected))
        {
            ChkSelectAll.IsChecked = null;
        }
        else
        {
            ChkSelectAll.IsChecked = false;
        }

        _isUpdatingSelectAllHeader = false;
    }

    private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelectAllHeader) return;

        bool targetState = ChkSelectAll.IsChecked == true;
        var visible = _collectionView?.Cast<ProjectEntry>().ToList() ?? new List<ProjectEntry>();

        foreach (var p in visible)
        {
            p.IsSelected = targetState;
        }

        UpdateSummary();
    }

    private void RowCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateSummary();
        UpdateSelectAllState();
    }

    private ProjectEntry? GetEntryFromMenuItem(object sender)
    {
        if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
        {
            return (contextMenu.PlacementTarget as FrameworkElement)?.DataContext as ProjectEntry
                ?? GridDeletedProjects.SelectedItem as ProjectEntry;
        }
        return GridDeletedProjects.SelectedItem as ProjectEntry;
    }

    #region Hành Động (Actions)

    private void BtnRestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allEntries.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Vui lòng tích chọn ít nhất một dự án để khôi phục.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"Bạn có chắc chắn muốn khôi phục {selected.Count} dự án đã chọn trở lại danh sách Quản Lý Dự Án không?",
            "Xác nhận khôi phục dự án",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        _projectService.RestoreProjects(selected);

        var restoredIds = new HashSet<string>(selected.Select(x => x.Id));
        _allEntries.RemoveAll(x => restoredIds.Contains(x.Id));

        for (int i = _displayEntries.Count - 1; i >= 0; i--)
        {
            if (restoredIds.Contains(_displayEntries[i].Id))
            {
                _displayEntries.RemoveAt(i);
            }
        }

        _collectionView?.Refresh();
        UpdateSummary();
        UpdateSelectAllState();

        ProjectsRestored?.Invoke(this, EventArgs.Empty);

        MessageBox.Show(Window.GetWindow(this),
            $"Đã khôi phục thành công {selected.Count} dự án về màn hình Quản Lý Dự Án!",
            "Khôi phục thành công",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnDeleteSelectedForever_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allEntries.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Vui lòng tích chọn ít nhất một dự án để xóa vĩnh viễn.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"CẢNH BÁO QUAN TRỌNG:\nBạn có chắc chắn muốn XÓA VĨNH VIỄN {selected.Count} dòng dự án đã chọn khỏi thùng rác không?\n\nThông tin quản lý các dự án này sẽ mất vĩnh viễn và không thể khôi phục lại!",
            "Xác nhận xóa vĩnh viễn",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var selectedIds = new HashSet<string>(selected.Select(x => x.Id));
        _projectService.PermanentlyDelete(selectedIds);

        _allEntries.RemoveAll(x => selectedIds.Contains(x.Id));
        for (int i = _displayEntries.Count - 1; i >= 0; i--)
        {
            if (selectedIds.Contains(_displayEntries[i].Id))
            {
                _displayEntries.RemoveAt(i);
            }
        }

        _collectionView?.Refresh();
        UpdateSummary();
        UpdateSelectAllState();
    }

    private void BtnEmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        if (_allEntries.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Thùng rác hiện đang trống.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"CẢNH BÁO:\nBạn có chắc chắn muốn DỌN SẠCH toàn bộ {_allEntries.Count} dự án trong thùng rác không?\n\nThao tác này sẽ xóa vĩnh viễn tất cả thông tin dự án đã lưu trong đây!",
            "Xác nhận xóa sạch thùng rác",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _projectService.ClearAllDeleted();
        LoadData();
    }

    private void BtnCleanupSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _allEntries.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Vui lòng tích chọn ít nhất một dự án để dọn dẹp.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenCleanupDialog(selected);
    }

    private void OpenCleanupDialog(List<ProjectEntry> projects)
    {
        var parent = Window.GetWindow(this);
        var dialog = new ProjectDeleteExecutionDialog(projects, _projectService) { Owner = parent };
        dialog.ShowDialog();

        _collectionView?.Refresh();
        UpdateSummary();
        UpdateSelectAllState();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadData();
    }

    private void MenuItemRestore_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntryFromMenuItem(sender);
        if (entry != null)
        {
            var confirm = MessageBox.Show(Window.GetWindow(this),
                $"Bạn có chắc chắn muốn khôi phục dự án '{entry.ProjectName}' về danh sách Quản Lý Dự Án không?",
                "Xác nhận khôi phục",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                _projectService.RestoreProjects(new List<ProjectEntry> { entry });
                _allEntries.Remove(entry);
                _displayEntries.Remove(entry);
                _collectionView?.Refresh();
                UpdateSummary();
                UpdateSelectAllState();

                ProjectsRestored?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void MenuItemCleanup_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntryFromMenuItem(sender);
        if (entry != null)
        {
            OpenCleanupDialog(new List<ProjectEntry> { entry });
        }
    }

    private void MenuItemCopyFolder_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntryFromMenuItem(sender);
        if (entry != null && !string.IsNullOrWhiteSpace(entry.SourceFolder))
        {
            try
            {
                Clipboard.SetText(entry.SourceFolder);
                MessageBox.Show(Window.GetWindow(this), $"Đã sao chép đường dẫn: {entry.SourceFolder}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), $"Không thể sao chép: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void MenuItemDeleteForever_Click(object sender, RoutedEventArgs e)
    {
        var entry = GetEntryFromMenuItem(sender);
        if (entry != null)
        {
            var confirm = MessageBox.Show(Window.GetWindow(this),
                $"CẢNH BÁO: Bạn có chắc chắn muốn xóa vĩnh viễn dự án '{entry.ProjectName}' khỏi thùng rác không?",
                "Xác nhận xóa vĩnh viễn",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm == MessageBoxResult.Yes)
            {
                _projectService.PermanentlyDelete(new List<string> { entry.Id });
                _allEntries.Remove(entry);
                _displayEntries.Remove(entry);
                _collectionView?.Refresh();
                UpdateSummary();
                UpdateSelectAllState();
            }
        }
    }

    #endregion
}
