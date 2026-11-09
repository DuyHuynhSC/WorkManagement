using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WorkManagement.Models;

namespace WorkManagement.Views;

public class PersonInChargeOptionItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public string RawPerson { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public int ProjectCount { get; set; }
    public string CountBadgeText => $"{ProjectCount} dự án";

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}

public partial class SelectPersonInChargeImportDialog : Window
{
    private readonly ObservableCollection<PersonInChargeOptionItem> _personItems = new();
    private readonly ICollectionView _personView;
    private string _searchQuery = string.Empty;

    public HashSet<string> SelectedRawPersons { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public SelectPersonInChargeImportDialog(IEnumerable<ProjectEntry> entries, IEnumerable<string>? previouslySelected = null)
    {
        InitializeComponent();

        var entryList = entries?.ToList() ?? new List<ProjectEntry>();

        // Nhóm các dự án theo người phụ trách
        var grouped = entryList
            .GroupBy(p => (p.PersonInCharge ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key);

        var prevSet = previouslySelected != null
            ? new HashSet<string>(previouslySelected, StringComparer.OrdinalIgnoreCase)
            : null;

        int totalPersons = 0;
        foreach (var group in grouped)
        {
            totalPersons++;
            string rawKey = group.Key;
            string displayName = string.IsNullOrEmpty(rawKey) ? "(Chưa phân công)" : rawKey;
            string subtitle = string.IsNullOrEmpty(rawKey) ? "Các dự án chưa có thông tin người phụ trách" : $"Người phụ trách: {rawKey}";

            bool isSelected = prevSet == null || prevSet.Contains(rawKey);

            _personItems.Add(new PersonInChargeOptionItem
            {
                RawPerson = rawKey,
                DisplayName = displayName,
                Subtitle = subtitle,
                ProjectCount = group.Count(),
                IsSelected = isSelected
            });
        }

        _personView = CollectionViewSource.GetDefaultView(_personItems);
        _personView.Filter = FilterPerson;
        ListPersons.ItemsSource = _personView;

        TxtHeaderBadge.Text = $"{totalPersons} người ({entryList.Count} dự án)";
        UpdateSelectionStatus();
    }

    private bool FilterPerson(object obj)
    {
        if (obj is not PersonInChargeOptionItem item) return false;
        if (string.IsNullOrWhiteSpace(_searchQuery)) return true;

        return item.DisplayName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
               item.RawPerson.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase);
    }

    private void TxtSearchPerson_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = TxtSearchPerson.Text?.Trim() ?? string.Empty;
        BtnClearSearch.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Collapsed : Visibility.Visible;
        _personView.Refresh();
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtSearchPerson.Clear();
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _personView.Cast<PersonInChargeOptionItem>())
        {
            item.IsSelected = true;
        }
        UpdateSelectionStatus();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _personView.Cast<PersonInChargeOptionItem>())
        {
            item.IsSelected = false;
        }
        UpdateSelectionStatus();
    }

    private void PersonCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateSelectionStatus();
    }

    private void UpdateSelectionStatus()
    {
        int total = _personItems.Count;
        int selected = _personItems.Count(t => t.IsSelected);
        int selectedProjects = _personItems.Where(t => t.IsSelected).Sum(t => t.ProjectCount);

        TxtSelectionStatus.Text = $"Đã chọn: {selected}/{total} ({selectedProjects} dự án)";
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        var selected = _personItems.Where(t => t.IsSelected).Select(t => t.RawPerson).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Vui lòng chọn ít nhất một người phụ trách để import.", "Chưa chọn người phụ trách", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedRawPersons = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
