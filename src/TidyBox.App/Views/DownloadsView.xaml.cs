using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TidyBox.Modules.DiskAnalyzer;
using TidyBox.Modules.Downloads;
using TidyBox.Modules.Downloads.Rules;
using TidyBox.Modules.Downloads.Scanning;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.App.Views;

/// <summary>
/// Shows what tidying the downloads folder would do and which files have sat untouched for a long time. The page only displays and
/// confirms; everything that touches a file goes through <see cref="DownloadsModule"/>.
/// </summary>
public sealed partial class DownloadsView : Page
{
    private const string AllTypes = "全部类型";

    private readonly CategoryNames _names = new();
    private DownloadsModule _module = null!;
    private CancellationTokenSource? _scanCts;
    private List<ProposalRow> _proposalRows = new();
    private List<StaleRow> _staleRows = new();
    private bool _rendering; // set while the page fills the filter itself, so SelectionChanged does not act on it

    public DownloadsView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _module = (DownloadsModule)e.Parameter;
        ShowFolder();
        _ = ScanAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => _scanCts?.Cancel();

    private void ShowFolder()
    {
        PathText.Text = _module.DownloadsFolderPath;

        var isDefault = string.Equals(
            Path.TrimEndingDirectorySeparator(_module.DownloadsFolderPath), Path.TrimEndingDirectorySeparator(DownloadsFolder.Resolve()), StringComparison.OrdinalIgnoreCase);
        ResetFolderButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnChangeFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) await ChooseFolderAsync(folder.Path, remember: true);
    }

    private async void OnResetFolderClick(object sender, RoutedEventArgs e) => await ChooseFolderAsync(DownloadsFolder.Resolve(), remember: false);

    // A folder TidyBox may not tidy is refused with the reason and the current folder stays.
    private async Task ChooseFolderAsync(string path, bool remember)
    {
        try
        {
            _module.SetFolder(path);
        }
        catch (ArgumentException ex)
        {
            ResultText.Text = ex.Message;
            return;
        }

        TidyFolderSettings.Path = remember ? _module.DownloadsFolderPath : null;
        ResultText.Text = string.Empty;
        ShowFolder();
        await ScanAsync();
    }

    // Looking is all a scan does: nothing is moved, written or deleted until the user confirms an action below.
    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        var cts = _scanCts = new CancellationTokenSource();
        SetBusy(true, cancellable: true);
        StatusText.Text = "正在扫描…";
        try
        {
            Render(await _module.ScanAsync(cts.Token));
            StatusText.Text = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消扫描。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"扫描失败：{ex.Message}";
        }
        finally
        {
            if (_scanCts == cts) SetBusy(false);
        }
    }

    private void Render(DownloadsReport report)
    {
        SummaryText.Text = $"文件合计 {ByteSizeFormatter.Format(report.TotalBytes)}，超过 90 天没动的 {ByteSizeFormatter.Format(report.StaleBytes)}";

        string? DomainOf(DownloadFile file) => report.SourceDomains.TryGetValue(file.FullPath, out var domain) ? domain : null;

        _proposalRows = report.Proposals.Select(p => new ProposalRow(p, DomainOf(p.File))).ToList();
        RenderProposals();

        _staleRows = report.Stale.Select(f => new StaleRow(f, FileAge.AgeDays(f, DateTime.UtcNow), DomainOf(f), CategoryLabel(f))).ToList();
        StaleTitle.Text = $"② 久未动的（{_staleRows.Count}）· 可释放 {ByteSizeFormatter.Format(report.StaleBytes)}";
        RenderFilter();

        var kept = report.Kept.Select(f => new StaleRow(f, FileAge.AgeDays(f, DateTime.UtcNow), DomainOf(f), CategoryLabel(f))).ToList();
        KeptExpander.Header = $"已保留（{kept.Count}）";
        KeptList.ItemsSource = kept;

        var history = _module.GetHistory().Select(b => new BatchRow(b)).ToList();
        HistoryExpander.Header = $"历史（{history.Count}）";
        HistoryList.ItemsSource = history;
    }

    private void RenderProposals()
    {
        GroupsPanel.Children.Clear();

        var groups = new List<ProposalGroup>();
        foreach (var byCategory in _proposalRows.Where(r => r.Proposal.Kind == ProposalKind.Move).GroupBy(r => r.Proposal.CategoryId!).OrderBy(g => g.Key))
        {
            groups.Add(new ProposalGroup($"将移动到「{_names.FolderFor(byCategory.Key)}」", byCategory.ToList()));
        }

        var duplicates = _proposalRows.Where(r => r.Proposal.Kind == ProposalKind.RecycleDuplicate).ToList();
        if (duplicates.Count > 0) groups.Add(new ProposalGroup("目标已有内容相同的文件，建议把这份移到回收站", duplicates));

        var untouched = _proposalRows.Where(r => r.Proposal.Kind == ProposalKind.Untouched).ToList();
        if (untouched.Count > 0) groups.Add(new ProposalGroup("不会动（没有匹配的规则）", untouched));

        foreach (var group in groups)
        {
            GroupsPanel.Children.Add(new TextBlock { Text = $"{group.Title}（{group.Rows.Count}）", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
            GroupsPanel.Children.Add(new ListView
            {
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 260,
                ItemTemplate = (DataTemplate)Resources["ProposalTemplate"],
                ItemContainerStyle = (Style)Resources["StretchItemStyle"],
                ItemsSource = group.Rows,
            });
        }

        TidyTitle.Text = $"① 可以整理的（{_proposalRows.Count(r => r.CanCheck)}）";
        UpdateTidyButton();
    }

    private void RenderFilter()
    {
        var previous = TypeFilter.SelectedItem as string;
        var labels = new List<string> { AllTypes };
        labels.AddRange(_staleRows.Select(r => r.CategoryLabel).Distinct().OrderBy(l => l));

        _rendering = true;
        TypeFilter.ItemsSource = labels;
        TypeFilter.SelectedItem = previous is not null && labels.Contains(previous) ? previous : AllTypes;
        _rendering = false;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var label = TypeFilter.SelectedItem as string ?? AllTypes;
        StaleList.ItemsSource = _staleRows.Where(r => label == AllTypes || r.CategoryLabel == label).ToList();
        UpdateStaleButtons();
    }

    private string CategoryLabel(DownloadFile file)
        => Categories.ForFile(file.Name) is { } category ? _names.FolderFor(category.Id) : "其他";

    private void UpdateTidyButton()
    {
        var count = _proposalRows.Count(r => r.CanCheck && r.IsChecked);
        TidyButton.Content = $"执行所选操作（{count}）";
        TidyButton.IsEnabled = count > 0 && BusyBar.Visibility == Visibility.Collapsed;
    }

    private void UpdateStaleButtons()
    {
        var count = _staleRows.Count(r => r.IsChecked);
        RecycleButton.Content = $"移到回收站（{count}）";
        KeepButton.Content = $"保留，不再提醒（{count}）";
        var enabled = count > 0 && BusyBar.Visibility == Visibility.Collapsed;
        RecycleButton.IsEnabled = enabled;
        KeepButton.IsEnabled = enabled;
    }

    private void SetBusy(bool busy, bool cancellable = false)
    {
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy && cancellable ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = !busy;
        ChangeFolderButton.IsEnabled = !busy;
        ResetFolderButton.IsEnabled = !busy;
        KeptExpander.IsEnabled = !busy; // "放回" and "撤销" live in these lists: they must not run alongside another operation
        HistoryExpander.IsEnabled = !busy;
        UpdateTidyButton();
        UpdateStaleButtons();
    }

    private void OnProposalCheckClick(object sender, RoutedEventArgs e) => UpdateTidyButton();

    private void OnStaleCheckClick(object sender, RoutedEventArgs e) => UpdateStaleButtons();

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_rendering) ApplyFilter();
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (StaleList.ItemsSource is not IEnumerable<StaleRow> shown) return;

        foreach (var row in shown) row.IsChecked = true;
        UpdateStaleButtons();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ScanAsync();

    private void OnCancelClick(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    private async void OnTidyClick(object sender, RoutedEventArgs e)
    {
        var chosen = _proposalRows.Where(r => r.CanCheck && r.IsChecked).Select(r => r.Proposal).ToList();
        if (chosen.Count == 0) return;

        var moves = chosen.Where(p => p.Kind == ProposalKind.Move).ToList();
        var duplicates = chosen.Where(p => p.Kind == ProposalKind.RecycleDuplicate).ToList();
        var lines = new List<string>();
        if (moves.Count > 0)
        {
            lines.Add($"将移动 {moves.Count} 个文件（共 {ByteSizeFormatter.Format(moves.Sum(p => p.File.SizeBytes))}）到各自的分类文件夹。之后可以在“历史”里撤销。");
        }

        if (duplicates.Count > 0)
        {
            lines.Add($"将把 {duplicates.Count} 个内容重复的文件（共 {ByteSizeFormatter.Format(duplicates.Sum(p => p.File.SizeBytes))}）移到回收站。移到回收站不会立刻释放空间，清空回收站后才真正释放；这部分要在回收站里还原，不能在这里撤销。");
        }

        if (!await ConfirmAsync("整理这些文件？", string.Join("\n\n", lines), "整理")) return;

        await RunAsync(async () => Describe(await _module.ApplyAsync(chosen)));
    }

    private async void OnRecycleClick(object sender, RoutedEventArgs e)
    {
        var chosen = _staleRows.Where(r => r.IsChecked).Select(r => r.File).ToList();
        if (chosen.Count == 0) return;

        var content = $"将把 {chosen.Count} 个文件（共 {ByteSizeFormatter.Format(chosen.Sum(f => f.SizeBytes))}）移到回收站。\n\n"
            + "移到回收站不会立刻释放空间，清空回收站后才真正释放（可在“磁盘空间分析”页清空）。文件可以在回收站里还原。";
        if (!await ConfirmAsync("移到回收站？", content, "移到回收站")) return;

        await RunAsync(async () => Describe(await _module.RecycleAsync(chosen)));
    }

    private async void OnKeepClick(object sender, RoutedEventArgs e)
    {
        var chosen = _staleRows.Where(r => r.IsChecked).Select(r => r.File).ToList();
        if (chosen.Count == 0) return;

        await RunAsync(() => Task.Run(() =>
        {
            foreach (var file in chosen) _module.Keep(file);
            return $"已保留 {chosen.Count} 个文件，它们被修改或换位置之前不会再出现在这里。";
        }));
    }

    private async void OnUnkeepClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not StaleRow row) return;

        await RunAsync(() => Task.Run(() =>
        {
            _module.Unkeep(row.File.FullPath);
            return $"已把 {row.Name} 放回提醒列表。";
        }));
    }

    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BatchRow row) return;

        await RunAsync(() => Task.Run(() =>
        {
            var result = _module.Undo(row.Batch.BatchId);
            var text = $"已恢复 {result.Restored} 个文件。";
            if (result.Skipped.Count > 0)
            {
                var first = result.Skipped[0];
                text += $"{result.Skipped.Count} 个没有恢复：{Path.GetFileName(first.Entry.From)} —— {SkipText(first.Reason)}。";
            }

            return text;
        }));
    }

    // Runs an action, shows what happened, then scans again so the lists match the folder. A refused or failed action is reported, never thrown at the user.
    private async Task RunAsync(Func<Task<string>> action)
    {
        SetBusy(true);
        try
        {
            ResultText.Text = await action();
        }
        catch (ArgumentException ex)
        {
            ResultText.Text = $"操作被拒绝，没有改动任何文件：{ex.Message}";
        }
        catch (Exception ex)
        {
            ResultText.Text = $"操作失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }

        await ScanAsync();
    }

    private static string Describe(ExecutionResult result)
    {
        var parts = new List<string>();
        if (result.MoveBatch is { } moved) parts.Add($"已移动 {moved.Entries.Count} 个文件，可在“历史”里撤销");
        if (result.RecycleBatch is { } recycled) parts.Add($"已把 {recycled.Entries.Count} 个文件移到回收站，要在回收站里还原");
        if (result.Failures.Count > 0) parts.Add($"{result.Failures.Count} 个失败：{Path.GetFileName(result.Failures[0].Path)} —— {result.Failures[0].Message}");

        return parts.Count == 0 ? "没有改动任何文件。" : string.Join("；", parts) + "。";
    }

    private static string SkipText(UndoSkipReason reason) => reason switch
    {
        UndoSkipReason.TargetMissing => "整理后的文件已不在原来放的位置",
        UndoSkipReason.TargetChanged => "整理后文件被修改过，没有动它",
        UndoSkipReason.OriginalOccupied => "原来的位置已有同名文件，没有覆盖",
        _ => "无法移回",
    };

    private async Task<bool> ConfirmAsync(string title, string content, string primaryText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
