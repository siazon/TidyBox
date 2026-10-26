using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerTools.Modules.DiskAnalyzer;
using PowerTools.Modules.Uninstaller;
using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Residue;
using PowerTools.Modules.Uninstaller.Routing;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace PowerTools.App.Views;

public sealed partial class UninstallerView : Page
{
    private UninstallerModule _module = null!;
    private IReadOnlyList<AppRow> _all = Array.Empty<AppRow>();
    private CancellationTokenSource? _measuring;
    private CancellationTokenSource? _loadingIcons;
    private CancellationTokenSource? _watching;

    public UninstallerView()
    {
        InitializeComponent();
    }

    // Used from the row template (x:Bind functions).
    public static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _module = (UninstallerModule)e.Parameter;
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _measuring?.Cancel();
        _loadingIcons?.Cancel();
        _watching?.Cancel();
    }

    private async Task LoadAsync()
    {
        _measuring?.Cancel();
        StatusText.Text = "正在读取已安装的程序…";
        try
        {
            var apps = await _module.LoadAppsAsync();
            _all = await Task.Run(() => apps.Select(app => new AppRow(app)).ToList());
            BatchUninstallButton.IsEnabled = false; // fresh rows are all unchecked after a reload
            ApplyFilter();
            _ = MeasureMissingSizesAsync();
            _ = LoadIconsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"读取已安装的程序失败：{ex.Message}";
        }
    }

    // The registry often has no size for a program; measure its folder in the background so the column (and sorting by size) is useful.
    private async Task MeasureMissingSizesAsync()
    {
        var cts = _measuring = new CancellationTokenSource();
        var pending = _all.Where(row => row.SizeBytes is null).ToList();
        foreach (var row in pending) row.Measuring = true;

        using var gate = new SemaphoreSlim(3);
        try
        {
            await Task.WhenAll(pending.Select(async row =>
            {
                await gate.WaitAsync(cts.Token);
                try
                {
                    row.SizeBytes = await _module.MeasureSizeAsync(row.App, cts.Token);
                    row.SizeChecked = true;
                }
                finally
                {
                    row.Measuring = false;
                    gate.Release();
                }
            }));

            if (SortKey == "size") ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // Leaving the page or refreshing: the rows that were not reached just stay without a size.
            foreach (var row in pending) row.Measuring = false;
        }
    }

    // Icons come from the shell one by one in the background; a row keeps its placeholder until (or unless) its icon arrives.
    private async Task LoadIconsAsync()
    {
        _loadingIcons?.Cancel();
        var cts = _loadingIcons = new CancellationTokenSource();
        using var gate = new SemaphoreSlim(4);
        try
        {
            await Task.WhenAll(_all.Select(async row =>
            {
                await gate.WaitAsync(cts.Token);
                try
                {
                    var png = await _module.GetIconPngAsync(row.App, cts.Token);
                    if (png is null) return;

                    using var stream = new InMemoryRandomAccessStream();
                    await stream.WriteAsync(png.AsBuffer());
                    stream.Seek(0);
                    var image = new BitmapImage { DecodePixelWidth = 48 };
                    await image.SetSourceAsync(stream);
                    row.Icon = image;
                }
                finally
                {
                    gate.Release();
                }
            }));
        }
        catch (OperationCanceledException)
        {
            // Leaving the page or refreshing: rows not reached keep the placeholder.
        }
    }

    private string? SortKey => (SortBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var source = (SourceFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        var selected = AppList.SelectedItem as AppRow;

        IEnumerable<AppRow> shown = _all.Where(row =>
            (source is null || row.App.Source.ToString() == source)
            && (query.Length == 0
                || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (row.App.Publisher?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));

        shown = SortKey switch
        {
            "size" => shown.OrderByDescending(row => row.SizeBytes ?? -1),
            "date" => shown.OrderByDescending(row => row.App.InstalledOn ?? DateTime.MinValue),
            _ => shown.OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase),
        };

        var list = shown.ToList();
        AppList.ItemsSource = list;
        if (selected is not null && list.Contains(selected)) AppList.SelectedItem = selected;
        StatusText.Text = list.Count == _all.Count ? $"共 {_all.Count} 项" : $"共 {_all.Count} 项，当前显示 {list.Count} 项";
    }

    private void OnFilterChanged(object sender, object e)
    {
        // The combo boxes raise this while the page is still being built.
        if (AppList is null || SortBox is null || SourceFilter is null) return;
        ApplyFilter();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UninstallButton.IsEnabled = AppList.SelectedItem is AppRow;

    private void OnRowCheckChanged(object sender, RoutedEventArgs e) => BatchUninstallButton.IsEnabled = _all.Any(row => row.IsChecked);

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
            e.DragUIOverride.Caption = "找到这个文件对应的软件";
        }
    }

    // A shortcut or program file dropped here is traced back to the installed program it belongs to, so a program
    // you cannot name (a pop-up, a tray icon) can still be found and uninstalled.
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var path = items.FirstOrDefault()?.Path;
        if (string.IsNullOrEmpty(path)) return;

        var found = _module.FindAppByPath(_all.Select(row => row.App), path);
        var row = found is null ? null : _all.FirstOrDefault(r => r.App == found);
        if (row is null)
        {
            StatusText.Text = $"没有找到“{System.IO.Path.GetFileName(path)}”对应的已安装程序。";
            return;
        }

        SearchBox.Text = "";
        SourceFilter.SelectedIndex = 0;
        ApplyFilter();
        AppList.SelectedItem = row;
        AppList.ScrollIntoView(row);
        StatusText.Text = $"已定位到“{row.DisplayName}”。";
    }

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not AppRow selected) return;
        var app = selected.App;

        var confirm = new ContentDialog
        {
            Title = $"卸载“{app.DisplayName}”？",
            Content = (app.Source switch
            {
                AppSource.Steam => "会打开 Steam 的卸载确认窗口。",
                AppSource.Store => "这个商店应用会被卸载，之后可以在 Microsoft Store 重新安装。",
                _ => "会运行这个程序自带的卸载程序，完成后检查是否有残留。",
            }) + (selected.Health is null ? "" : $"\n\n⚠ {selected.Health}，卸载可能无法完成。"),
            PrimaryButtonText = "卸载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        _watching?.Cancel();
        ResiduePanel.Visibility = Visibility.Collapsed;
        UninstallButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        StatusText.Text = $"正在卸载“{app.DisplayName}”…";
        Func<CancellationToken, Task<IReadOnlyList<ResidueItem>>>? deferred = null;
        try
        {
            var result = await _module.UninstallAsync(app);
            if (result.Outcome.NeedsAdministrator && await AskToRetryAsAdministratorAsync(app))
            {
                StatusText.Text = $"正在以管理员身份卸载“{app.DisplayName}”…";
                result = await _module.UninstallAsync(app, asAdministrator: true);
            }

            var outcome = result.Outcome;

            // Reloading rewrites the status line, so the outcome is written after it or a failure would vanish at once.
            if (outcome.Status != UninstallStatus.NotAvailable) await LoadAsync();
            StatusText.Text = outcome.Status switch
            {
                UninstallStatus.Completed => outcome.Message ?? $"已卸载“{app.DisplayName}”。",
                _ => outcome.Message ?? "卸载没有完成。",
            };

            if (result.Leftovers.Count > 0) ShowResidue(app, result.Leftovers);
            deferred = result.DeferredScan;
            if (deferred is not null) StatusText.Text = "卸载程序还在后台进行，完成后会自动检查残留（最多等 2 分钟）。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"卸载失败：{ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            UninstallButton.IsEnabled = AppList.SelectedItem is AppRow;
        }

        if (deferred is not null) await ScanLaterAsync(app, deferred);
    }

    private async Task<bool> AskToRetryAsAdministratorAsync(InstalledApp app)
    {
        var ask = new ContentDialog
        {
            Title = "需要管理员权限",
            Content = $"“{app.DisplayName}”的卸载程序不允许当前用户运行。\n\n是否以管理员身份重试？会弹出 Windows 的权限确认（UAC），确认窗口里显示的是“Windows 命令处理程序”（cmd.exe），由它来启动这个卸载程序。",
            PrimaryButtonText = "以管理员身份重试",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await ask.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void OnBatchUninstallClick(object sender, RoutedEventArgs e)
    {
        var chosen = _all.Where(row => row.IsChecked).ToList();
        if (chosen.Count == 0)
        {
            StatusText.Text = "没有勾选任何要卸载的程序。";
            return;
        }

        var knownTotal = chosen.Sum(row => row.SizeBytes ?? 0);
        var unknownCount = chosen.Count(row => row.SizeBytes is null);
        var sizeNote = unknownCount > 0
            ? $"预计释放 {ByteSizeFormatter.Format(knownTotal)}（另有 {unknownCount} 项大小未知，不计入此数字）"
            : $"预计释放 {ByteSizeFormatter.Format(knownTotal)}";

        var confirm = new ContentDialog
        {
            Title = $"批量卸载 {chosen.Count} 个程序？",
            Content = $"{string.Join("、", chosen.Take(5).Select(r => r.DisplayName))}{(chosen.Count > 5 ? " 等" : "")}\n\n{sizeNote}。\n\n会依次运行每个程序自带的卸载程序；需要管理员权限的会被跳过，之后可以单独用“以管理员身份重试”卸载。",
            PrimaryButtonText = "批量卸载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        _watching?.Cancel();
        ResiduePanel.Visibility = Visibility.Collapsed;
        UninstallButton.IsEnabled = false;
        BatchUninstallButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;

        var succeeded = new List<string>();
        var needsAdmin = new List<string>();
        var failed = new List<string>();
        var allLeftovers = new List<ResidueItem>();

        foreach (var row in chosen)
        {
            StatusText.Text = $"正在卸载“{row.DisplayName}”…（{succeeded.Count + needsAdmin.Count + failed.Count + 1}/{chosen.Count}）";
            try
            {
                var result = await _module.UninstallAsync(row.App);
                if (result.Outcome.NeedsAdministrator)
                {
                    needsAdmin.Add(row.DisplayName);
                }
                else if (result.Outcome.Status == UninstallStatus.Completed)
                {
                    succeeded.Add(row.DisplayName);
                    allLeftovers.AddRange(result.Leftovers);
                }
                else
                {
                    failed.Add($"{row.DisplayName}（{result.Outcome.Message}）");
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{row.DisplayName}（{ex.Message}）");
            }
        }

        await LoadAsync();

        var parts = new List<string>();
        if (succeeded.Count > 0) parts.Add($"成功卸载 {succeeded.Count} 个");
        if (needsAdmin.Count > 0) parts.Add($"{needsAdmin.Count} 个需要管理员权限，已跳过：{string.Join("、", needsAdmin)}");
        if (failed.Count > 0) parts.Add($"{failed.Count} 个失败：{string.Join("；", failed)}");
        StatusText.Text = string.Join("；", parts) + "。";

        if (allLeftovers.Count > 0) ShowBatchResidue(allLeftovers, succeeded.Count);

        RefreshButton.IsEnabled = true;
        UninstallButton.IsEnabled = AppList.SelectedItem is AppRow;
    }

    // Like ShowResidue, but for several apps at once: no single app to watch "still cleaning up" against, so the
    // live-shrinking behavior is skipped here.
    private void ShowBatchResidue(IReadOnlyList<ResidueItem> leftovers, int appCount)
    {
        ResidueTitle.Text = $"批量卸载的 {appCount} 个程序一共可能还留下 {leftovers.Count} 项，请检查后再决定是否删除";
        ResidueList.ItemsSource = leftovers.Select(item => new ResidueRow(item)).ToList();
        ResidueResult.Visibility = Visibility.Collapsed;
        ResiduePanel.Visibility = Visibility.Visible;
    }

    // The uninstaller handed over to a background process: wait (bounded, and without blocking the buttons) until the
    // program has really disappeared, then look for leftovers once.
    private async Task ScanLaterAsync(InstalledApp app, Func<CancellationToken, Task<IReadOnlyList<ResidueItem>>> scan)
    {
        _watching?.Cancel();
        var cts = _watching = new CancellationTokenSource();
        try
        {
            var leftovers = await scan(cts.Token);
            if (cts.IsCancellationRequested) return;

            await LoadAsync();
            if (leftovers.Count > 0)
            {
                ShowResidue(app, leftovers);
                StatusText.Text = $"“{app.DisplayName}”卸载完成，发现 {leftovers.Count} 项可能的残留。";
            }
            else
            {
                StatusText.Text = $"“{app.DisplayName}”卸载完成，没有发现残留。";
            }
        }
        catch (OperationCanceledException)
        {
            // Left the page or started something else.
        }
        catch (Exception ex)
        {
            StatusText.Text = $"检查残留失败：{ex.Message}";
        }
    }

    private void ShowResidue(InstalledApp app, IReadOnlyList<ResidueItem> leftovers)
    {
        ResidueTitle.Text = $"“{app.DisplayName}”卸载后可能还留下 {leftovers.Count} 项，请检查后再决定是否删除";
        ResidueList.ItemsSource = leftovers.Select(item => new ResidueRow(item)).ToList();
        ResidueResult.Visibility = Visibility.Collapsed;
        ResiduePanel.Visibility = Visibility.Visible;

        // Some uninstallers keep deleting after they say "done": the list drops what disappears on its own (a minute at most).
        _watching?.Cancel();
        var cts = _watching = new CancellationTokenSource();
        _ = _module.WatchLeftoversAsync(leftovers, remaining => DispatcherQueue.TryEnqueue(() => OnLeftoversShrunk(app, remaining)), cts.Token);
    }

    private void OnLeftoversShrunk(InstalledApp app, IReadOnlyList<ResidueItem> remaining)
    {
        if (ResidueList.ItemsSource is not IEnumerable<ResidueRow> shown) return;

        var keep = remaining.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = shown.Where(row => keep.Contains(row.Item.Path)).ToList();
        var dropped = shown.Count() - rows.Count;
        if (dropped == 0) return;

        ResidueList.ItemsSource = rows;
        if (rows.Count == 0)
        {
            ResiduePanel.Visibility = Visibility.Collapsed;
            StatusText.Text = $"“{app.DisplayName}”的卸载程序已自行清理完毕，没有残留。";
            return;
        }

        ResidueTitle.Text = $"“{app.DisplayName}”卸载后可能还留下 {rows.Count} 项，请检查后再决定是否删除";
        ResidueResult.Text = $"有 {dropped} 项已被卸载程序自己清理掉，列表已更新。";
        ResidueResult.Visibility = Visibility.Visible;
    }

    private async void OnRemoveResidueClick(object sender, RoutedEventArgs e)
    {
        _watching?.Cancel(); // no point polling while the user is removing things
        var rows = ((IEnumerable<ResidueRow>)ResidueList.ItemsSource).ToList();
        var chosen = rows.Where(r => r.CanSelect && r.IsChecked == true).ToList();
        if (chosen.Count == 0)
        {
            StatusText.Text = "没有勾选任何可删除的项目。";
            return;
        }

        RemoveButton.IsEnabled = false;
        try
        {
            var results = await _module.RemoveResidueAsync(chosen.Select(r => r.Item).ToList());
            var failed = results.Where(r => !r.Success).ToList();
            var removedPaths = results.Where(r => r.Success).Select(r => r.Item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var remaining = rows.Where(r => !removedPaths.Contains(r.Item.Path)).ToList();
            ResidueList.ItemsSource = remaining;
            if (remaining.Count == 0) ResiduePanel.Visibility = Visibility.Collapsed;

            var removed = results.Count(r => r.Success && !r.AlreadyGone);
            var gone = results.Count(r => r.AlreadyGone);
            var parts = new List<string>();
            if (removed > 0) parts.Add($"已移除 {removed} 项，文件夹可在回收站或 Quarantine 目录中找回");
            if (gone > 0) parts.Add($"{gone} 项已经不在了（卸载程序自己清理掉了）");
            if (failed.Count > 0) parts.Add($"{failed.Count} 项失败：{failed[0].Item.Path} —— {failed[0].Error}");
            if (results.Any(r => r.Success && !r.AlreadyGone && r.Item.Kind == ResidueKind.RegistryKey))
            {
                parts.Add($"注册表备份在 {_module.RegistryBackupDirectory}（需要管理员权限的备份在 Quarantine 目录里）");
            }

            // Shown in the panel as well as in the status line at the top, which is out of sight when the list is long.
            StatusText.Text = ResidueResult.Text = string.Join("；", parts) + "。";
            ResidueResult.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"删除残留失败：{ex.Message}";
        }
        finally
        {
            RemoveButton.IsEnabled = true;
        }
    }

    private void OnDismissResidueClick(object sender, RoutedEventArgs e)
    {
        _watching?.Cancel();
        ResiduePanel.Visibility = Visibility.Collapsed;
    }

    private async void OnEmptyRecycleBinClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "清空回收站？",
            Content = "这会清空整台机器的回收站，不止这次删除的内容，且无法撤销。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _module.EmptyRecycleBinAsync();
            StatusText.Text = "回收站已清空。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"清空回收站失败：{ex.Message}";
        }
    }

    private async void OnManageQuarantineClick(object sender, RoutedEventArgs e)
    {
        QuarantineSummary summary;
        try
        {
            summary = await _module.InspectQuarantineAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"查看隔离区失败：{ex.Message}";
            return;
        }

        if (summary.BatchCount == 0)
        {
            StatusText.Text = "隔离区是空的。";
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "管理隔离区",
            Content = $"隔离区里有 {summary.BatchCount} 次清理留下的内容，共占用 {ByteSizeFormatter.Format(summary.TotalBytes)}。\n\n清空后无法恢复，确定要清空吗？",
            PrimaryButtonText = "清空隔离区",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var success = await _module.PurgeQuarantineAsync();
            StatusText.Text = success ? "隔离区已清空。" : "清空隔离区失败。";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"清空隔离区失败：{ex.Message}";
        }
    }
}
