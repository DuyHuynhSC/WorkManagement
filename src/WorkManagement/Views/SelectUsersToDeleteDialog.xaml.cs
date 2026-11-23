using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using WorkManagement.Models;
using WorkManagement.Services;

namespace WorkManagement.Views;

public class UserToDeleteItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private bool _isBackupSelected = true;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBackupSelected
    {
        get => _isBackupSelected;
        set
        {
            if (_isBackupSelected != value)
            {
                _isBackupSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string UserName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string HostServer { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public partial class SelectUsersToDeleteDialog : Window
{
    public ObservableCollection<UserToDeleteItem> UserItems { get; } = new();

    public SelectUsersToDeleteDialog(
        List<ProjectEntry> projects,
        Dictionary<string, List<string>>? preselectedToDelete = null,
        Dictionary<string, List<string>>? preselectedToBackup = null,
        bool defaultBackup = true)
    {
        InitializeComponent();

        var projectNames = new List<string>();
        var hosts = new HashSet<string>();

        foreach (var proj in projects)
        {
            if (!string.IsNullOrWhiteSpace(proj.ProjectName))
            {
                projectNames.Add(proj.ProjectName);
            }
            if (!string.IsNullOrWhiteSpace(proj.HostServer))
            {
                hosts.Add($"{proj.HostServer}:{proj.Port}/{proj.ServiceName}");
            }

            var parsedUsers = ProjectCleanupService.ParseUserList(proj.DbUsers);

            HashSet<string>? deleteSet = null;
            if (preselectedToDelete != null && preselectedToDelete.TryGetValue(proj.Id, out var dList))
            {
                deleteSet = new HashSet<string>(dList, StringComparer.OrdinalIgnoreCase);
            }

            HashSet<string>? backupSet = null;
            if (preselectedToBackup != null && preselectedToBackup.TryGetValue(proj.Id, out var bList))
            {
                backupSet = new HashSet<string>(bList, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var u in parsedUsers)
            {
                bool isDelete = deleteSet != null ? deleteSet.Contains(u) : true;
                bool isBackup = backupSet != null ? backupSet.Contains(u) : defaultBackup;

                var item = new UserToDeleteItem
                {
                    IsSelected = isDelete,
                    IsBackupSelected = isBackup,
                    UserName = u,
                    ProjectName = proj.ProjectName,
                    ProjectId = proj.Id,
                    HostServer = $"{proj.HostServer}:{proj.Port}/{proj.ServiceName}"
                };
                item.PropertyChanged += Item_PropertyChanged;
                UserItems.Add(item);
            }
        }

        TxtDbSummary.Text = $"Dự án: {string.Join(", ", projectNames)} | DB: {string.Join("; ", hosts)}";
        GridUsers.ItemsSource = UserItems;

        UpdateSelectionCount();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserToDeleteItem.IsSelected) ||
            e.PropertyName == nameof(UserToDeleteItem.IsBackupSelected))
        {
            UpdateSelectionCount();
        }
    }

    private void UpdateSelectionCount()
    {
        int selected = UserItems.Count(x => x.IsSelected);
        int backup = UserItems.Count(x => x.IsBackupSelected);
        TxtSelectionCount.Text = $"Xóa: {selected}/{UserItems.Count} | Sao lưu: {backup}/{UserItems.Count}";
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in UserItems)
        {
            item.IsSelected = true;
        }
        UpdateSelectionCount();
    }

    private void BtnUnselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in UserItems)
        {
            item.IsSelected = false;
        }
        UpdateSelectionCount();
    }

    private void BtnBackupAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in UserItems)
        {
            item.IsBackupSelected = true;
        }
        UpdateSelectionCount();
    }

    private void BtnUnbackupAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in UserItems)
        {
            item.IsBackupSelected = false;
        }
        UpdateSelectionCount();
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        var selected = UserItems.Where(x => x.IsSelected).ToList();

        if (selected.Count == 0)
        {
            var res = MessageBox.Show(this,
                "Bạn chưa chọn user nào để xóa.\nBạn có muốn bỏ qua việc xóa DB User không?",
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                DialogResult = true;
                Close();
            }
            return;
        }

        string userNames = string.Join(", ", selected.Select(x => x.UserName));
        var confirm = MessageBox.Show(this,
            $"CẢNH BÁO QUAN TRỌNG:\nBạn đã chọn xóa {selected.Count} Oracle User:\n\n{userNames}\n\nThao tác này sẽ DROP CASCADE và làm mất toàn bộ dữ liệu của các user này.\nBạn có chắc chắn muốn xóa?",
            "Xác nhận xóa Oracle User",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            DialogResult = true;
            Close();
        }
    }

    /// <summary>
    /// Lấy danh sách tên user được chọn theo từng Project Id
    /// </summary>
    public Dictionary<string, List<string>> GetSelectedUsersByProjectId()
    {
        return UserItems
            .Where(x => x.IsSelected)
            .GroupBy(x => x.ProjectId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserName).Distinct().ToList());
    }

    /// <summary>
    /// Lấy toàn bộ danh sách user được chọn
    /// </summary>
    public List<string> GetSelectedUsers()
    {
        return UserItems
            .Where(x => x.IsSelected)
            .Select(x => x.UserName)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Lấy danh sách user được chọn sao lưu theo từng Project Id
    /// </summary>
    public Dictionary<string, List<string>> GetSelectedBackupUsersByProjectId()
    {
        return UserItems
            .Where(x => x.IsBackupSelected)
            .GroupBy(x => x.ProjectId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserName).Distinct().ToList());
    }

    /// <summary>
    /// Lấy toàn bộ danh sách user được chọn sao lưu
    /// </summary>
    public List<string> GetSelectedBackupUsers()
    {
        return UserItems
            .Where(x => x.IsBackupSelected)
            .Select(x => x.UserName)
            .Distinct()
            .ToList();
    }
}
