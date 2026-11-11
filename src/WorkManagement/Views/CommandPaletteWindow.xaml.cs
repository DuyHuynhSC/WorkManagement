using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using WorkManagement.Models;

namespace WorkManagement.Views;

public class CommandPaletteItem
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string CategoryBadge { get; set; } = string.Empty;
    public PackIconKind IconKind { get; set; } = PackIconKind.FlashOutline;
    public string SearchKeywords { get; set; } = string.Empty;
    public Action? ExecuteAction { get; set; }
    public bool IsAiCommand { get; set; }
}

public partial class CommandPaletteWindow : Window
{
    private readonly List<CommandPaletteItem> _allItems;
    private readonly ObservableCollection<CommandPaletteItem> _filteredItems = new();
    private AiDevOpsActionResult? _lastAiResult;

    public Func<string, Task<AiDevOpsActionResult>>? AiDispatcher { get; set; }
    public CommandPaletteItem? SelectedCommand { get; private set; }

    public CommandPaletteWindow(IEnumerable<CommandPaletteItem> items, Func<string, Task<AiDevOpsActionResult>>? aiDispatcher = null)
    {
        InitializeComponent();
        _allItems = items.ToList();
        AiDispatcher = aiDispatcher;
        LstResults.ItemsSource = _filteredItems;

        ApplyFilter(string.Empty);
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            TxtQuery.Focus();
            Keyboard.Focus(TxtQuery);
        };
    }

    private void ApplyFilter(string query)
    {
        _filteredItems.Clear();
        string q = query.Trim();

        IEnumerable<CommandPaletteItem> matched;
        if (string.IsNullOrEmpty(q))
        {
            matched = _allItems.Take(50);
        }
        else
        {
            var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            matched = _allItems.Where(item =>
                terms.All(t =>
                    item.Title.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    item.Subtitle.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    item.CategoryBadge.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                    item.SearchKeywords.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .Take(50);
        }

        foreach (var item in matched)
        {
            _filteredItems.Add(item);
        }

        if (_filteredItems.Count > 0)
        {
            LstResults.SelectedIndex = 0;
        }

        TxtResultCount.Text = $"{_filteredItems.Count} kết quả";
    }

    private void TxtQuery_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(TxtQuery.Text);
    }

    private void TxtQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = TriggerAiDispatchAsync();
        }
        else if (e.Key == Key.Enter && _filteredItems.Count == 0 && !string.IsNullOrWhiteSpace(TxtQuery.Text))
        {
            e.Handled = true;
            _ = TriggerAiDispatchAsync();
        }
    }

    private void BtnAiDispatch_Click(object sender, RoutedEventArgs e)
    {
        _ = TriggerAiDispatchAsync();
    }

    private async Task TriggerAiDispatchAsync()
    {
        string query = TxtQuery.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        BusyBarAi.Visibility = Visibility.Visible;
        BtnAiDispatch.IsEnabled = false;

        try
        {
            AiDevOpsActionResult? result = null;
            if (AiDispatcher != null)
            {
                result = await AiDispatcher(query);
            }

            if (result != null && result.Success)
            {
                _lastAiResult = result;
                PanelAiCard.Visibility = Visibility.Visible;
                TxtAiCardTitle.Text = string.IsNullOrWhiteSpace(result.DisplayTitle) ? "AI DevOps Phản Hồi" : result.DisplayTitle;
                TxtAiCardContent.Text = string.IsNullOrWhiteSpace(result.DisplayExplanation) ? result.TargetParam : result.DisplayExplanation;

                BtnExecuteAiCard.Visibility = Visibility.Visible;
                if (BtnExecuteAiCard.Content is TextBlock tb)
                {
                    tb.Text = result.ExecutableAction != null ? "Thực thi" : "Sao chép";
                }

                // Chèn vào đầu danh sách gợi ý của Command Palette
                var aiItem = new CommandPaletteItem
                {
                    Title = $"⚡ {result.DisplayTitle}",
                    Subtitle = string.IsNullOrWhiteSpace(result.DisplayExplanation) ? result.TargetParam : result.DisplayExplanation,
                    CategoryBadge = "⚡ AI DEVOPS",
                    IconKind = MapActionToIcon(result.ActionType),
                    IsAiCommand = true,
                    ExecuteAction = result.ExecutableAction ?? (() =>
                    {
                        if (!string.IsNullOrWhiteSpace(result.TargetParam))
                        {
                            try { Clipboard.SetText(result.TargetParam); } catch { }
                        }
                    })
                };

                _filteredItems.Insert(0, aiItem);
                LstResults.SelectedIndex = 0;
                LstResults.ScrollIntoView(aiItem);
                TxtResultCount.Text = $"{_filteredItems.Count} kết quả (Đã bổ sung phản hồi AI)";
            }
        }
        catch (Exception ex)
        {
            PanelAiCard.Visibility = Visibility.Visible;
            TxtAiCardTitle.Text = "Lỗi khi xử lý AI DevOps";
            TxtAiCardContent.Text = ex.Message;
            BtnExecuteAiCard.Visibility = Visibility.Collapsed;
        }
        finally
        {
            BusyBarAi.Visibility = Visibility.Collapsed;
            BtnAiDispatch.IsEnabled = true;
        }
    }

    private static PackIconKind MapActionToIcon(AiDevOpsActionType actionType)
    {
        return actionType switch
        {
            AiDevOpsActionType.ConnectRdp => PackIconKind.RemoteDesktop,
            AiDevOpsActionType.FilterVms => PackIconKind.DesktopTowerMonitor,
            AiDevOpsActionType.NavigateView => PackIconKind.CompassOutline,
            AiDevOpsActionType.FilterProjects => PackIconKind.FolderEyeOutline,
            AiDevOpsActionType.OpenOracleScript => PackIconKind.DatabaseCogOutline,
            AiDevOpsActionType.OpenProjectFolder => PackIconKind.FolderOpenOutline,
            AiDevOpsActionType.DirectAnswer => PackIconKind.Sparkles,
            _ => PackIconKind.RobotOutline
        };
    }

    private void BtnExecuteAiCard_Click(object sender, RoutedEventArgs e)
    {
        if (_lastAiResult?.ExecutableAction != null)
        {
            SelectedCommand = new CommandPaletteItem
            {
                Title = _lastAiResult.DisplayTitle,
                ExecuteAction = _lastAiResult.ExecutableAction
            };
            DialogResult = true;
            Close();
        }
        else if (!string.IsNullOrWhiteSpace(_lastAiResult?.TargetParam))
        {
            try
            {
                Clipboard.SetText(_lastAiResult.TargetParam);
                MessageBox.Show(this, "Đã sao chép phản hồi vào Clipboard!", "AI DevOps", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = TriggerAiDispatchAsync();
        }
        else if (e.Key == Key.Down)
        {
            if (_filteredItems.Count > 0)
            {
                int next = Math.Min(LstResults.SelectedIndex + 1, _filteredItems.Count - 1);
                LstResults.SelectedIndex = next;
                LstResults.ScrollIntoView(LstResults.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            if (_filteredItems.Count > 0)
            {
                int prev = Math.Max(LstResults.SelectedIndex - 1, 0);
                LstResults.SelectedIndex = prev;
                LstResults.ScrollIntoView(LstResults.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (_filteredItems.Count == 0 && !string.IsNullOrWhiteSpace(TxtQuery.Text))
            {
                e.Handled = true;
                _ = TriggerAiDispatchAsync();
            }
            else
            {
                e.Handled = true;
                ExecuteSelected();
            }
        }
    }

    private void LstResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ExecuteSelected();
    }

    private void ExecuteSelected()
    {
        if (LstResults.SelectedItem is CommandPaletteItem item)
        {
            SelectedCommand = item;
            DialogResult = true;
            Close();
        }
    }
}
