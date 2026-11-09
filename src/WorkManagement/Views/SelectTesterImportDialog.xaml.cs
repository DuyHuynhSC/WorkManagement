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

public class TesterOptionItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public string RawTester { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public int VmCount { get; set; }
    public string CountBadgeText => $"{VmCount} máy";

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

public partial class SelectTesterImportDialog : Window
{
    private readonly ObservableCollection<TesterOptionItem> _testerItems = new();
    private readonly ICollectionView _testerView;
    private string _searchQuery = string.Empty;

    public HashSet<string> SelectedRawTesters { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public SelectTesterImportDialog(IEnumerable<VirtualMachineEntry> entries, IEnumerable<string>? previouslySelected = null)
    {
        InitializeComponent();

        var entryList = entries?.ToList() ?? new List<VirtualMachineEntry>();

        // Nhóm các máy ảo theo người kiểm tra
        var grouped = entryList
            .GroupBy(e => (e.Tester ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key);

        var prevSet = previouslySelected != null
            ? new HashSet<string>(previouslySelected, StringComparer.OrdinalIgnoreCase)
            : null;

        int totalTesters = 0;
        foreach (var group in grouped)
        {
            totalTesters++;
            string rawKey = group.Key;
            string displayName = string.IsNullOrEmpty(rawKey) ? "(Chưa phân công)" : rawKey;
            string subtitle = string.IsNullOrEmpty(rawKey) ? "Các máy không có thông tin người kiểm tra" : $"Người kiểm tra: {rawKey}";

            bool isSelected = prevSet == null || prevSet.Contains(rawKey);

            _testerItems.Add(new TesterOptionItem
            {
                RawTester = rawKey,
                DisplayName = displayName,
                Subtitle = subtitle,
                VmCount = group.Count(),
                IsSelected = isSelected
            });
        }

        _testerView = CollectionViewSource.GetDefaultView(_testerItems);
        _testerView.Filter = FilterTester;
        ListTesters.ItemsSource = _testerView;

        TxtHeaderBadge.Text = $"{totalTesters} người ({entryList.Count} máy)";
        UpdateSelectionStatus();
    }

    private bool FilterTester(object obj)
    {
        if (obj is not TesterOptionItem item) return false;
        if (string.IsNullOrWhiteSpace(_searchQuery)) return true;

        return item.DisplayName.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
               item.RawTester.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase);
    }

    private void TxtSearchTester_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = TxtSearchTester.Text?.Trim() ?? string.Empty;
        BtnClearSearch.Visibility = string.IsNullOrEmpty(_searchQuery) ? Visibility.Collapsed : Visibility.Visible;
        _testerView.Refresh();
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtSearchTester.Clear();
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _testerView.Cast<TesterOptionItem>())
        {
            item.IsSelected = true;
        }
        UpdateSelectionStatus();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _testerView.Cast<TesterOptionItem>())
        {
            item.IsSelected = false;
        }
        UpdateSelectionStatus();
    }

    private void TesterCheckBox_Click(object sender, RoutedEventArgs e)
    {
        UpdateSelectionStatus();
    }

    private void UpdateSelectionStatus()
    {
        int total = _testerItems.Count;
        int selected = _testerItems.Count(t => t.IsSelected);
        int selectedVms = _testerItems.Where(t => t.IsSelected).Sum(t => t.VmCount);

        TxtSelectionStatus.Text = $"Đã chọn: {selected}/{total} ({selectedVms} máy)";
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        var selected = _testerItems.Where(t => t.IsSelected).Select(t => t.RawTester).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Vui lòng chọn ít nhất một người kiểm tra để import.", "Chưa chọn người kiểm tra", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedRawTesters = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
