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

    public SelectUsersToDeleteDialog(List<ProjectEntry> projects, Dictionary<string, List<string>>? preselected = null)
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
            HashSet<string>? selectedSet = null;
            if (preselected != null && preselected.TryGetValue(proj.Id, out var list))
            {
                selectedSet = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var u in parsedUsers)
            {
                bool isSelected = selectedSet != null ? selectedSet.Contains(u) : true;
                var item = new UserToDeleteItem
                {
                    IsSelected = isSelected,
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
        if (e.PropertyName == nameof(UserToDeleteItem.IsSelected))
        {
            UpdateSelectionCount();
        }
    }

    private void UpdateSelectionCount()
    {
        int selected = UserItems.Count(x => x.IsSelected);
        TxtSelectionCount.Text = $"Đã chọn: {selected} / {UserItems.Count} user";
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
}
