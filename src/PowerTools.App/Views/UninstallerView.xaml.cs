using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerTools.Modules.Uninstaller;
using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Routing;

namespace PowerTools.App.Views;

public sealed partial class UninstallerView : Page
{
    private UninstallerModule _module = null!;
    private IReadOnlyList<InstalledApp> _all = Array.Empty<InstalledApp>();

    public UninstallerView()
    {
        InitializeComponent();
    }

    // Used from the row template (x:Bind functions).
    public static string Subtitle(string? publisher, string? version)
        => string.Join(" · ", new[] { publisher, version }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public static string SourceText(AppSource source) => source switch
    {
        AppSource.Store => "商店应用",
        AppSource.Steam => "Steam 游戏",
        _ => "桌面程序",
    };

    public static string SizeText(long? bytes) => bytes switch
    {
        null => "",
        >= 1L << 30 => $"{bytes.Value / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes.Value / (double)(1L << 20):0.#} MB",
        _ => $"{Math.Max(1, bytes.Value >> 10)} KB",
    };

    public static string DateText(DateTime? date) => date?.ToString("yyyy-MM-dd") ?? "";

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _module = (UninstallerModule)e.Parameter;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        StatusText.Text = "正在读取已安装的程序…";
        try
        {
            _all = await _module.LoadAppsAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"读取已安装的程序失败：{ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var source = (SourceFilter.SelectedItem as ComboBoxItem)?.Tag as string;

        var shown = _all.Where(app =>
            (source is null || app.Source.ToString() == source)
            && (query.Length == 0
                || app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (app.Publisher?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();

        AppList.ItemsSource = shown;
        StatusText.Text = shown.Count == _all.Count ? $"共 {_all.Count} 项" : $"共 {_all.Count} 项，当前显示 {shown.Count} 项";
    }

    private void OnFilterChanged(object sender, object e)
    {
        // The combo box raises this while the page is still being built.
        if (AppList is null || !IsLoaded && _all.Count == 0) return;
        ApplyFilter();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UninstallButton.IsEnabled = AppList.SelectedItem is InstalledApp;

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is not InstalledApp app) return;

        var confirm = new ContentDialog
        {
            Title = $"卸载“{app.DisplayName}”？",
            Content = app.Source switch
            {
                AppSource.Steam => "会打开 Steam 的卸载确认窗口。",
                AppSource.Store => "这个商店应用会被卸载，之后可以在 Microsoft Store 重新安装。",
                _ => "会运行这个程序自带的卸载程序，完成后检查是否有残留。",
            },
            PrimaryButtonText = "卸载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        ResiduePanel.Visibility = Visibility.Collapsed;
        UninstallButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        StatusText.Text = $"正在卸载“{app.DisplayName}”…";
        try
        {
            var result = await _module.UninstallAsync(app);
            var outcome = result.Outcome;
            StatusText.Text = outcome.Status switch
            {
                UninstallStatus.Completed => outcome.Message ?? $"已卸载“{app.DisplayName}”。",
                _ => outcome.Message ?? "卸载没有完成。",
            };

            if (outcome.Status != UninstallStatus.NotAvailable) await LoadAsync();
            if (result.Leftovers.Count > 0) ShowResidue(app, result.Leftovers);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"卸载失败：{ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            UninstallButton.IsEnabled = AppList.SelectedItem is InstalledApp;
        }
    }

    private void ShowResidue(InstalledApp app, IReadOnlyList<PowerTools.Modules.Uninstaller.Residue.ResidueItem> leftovers)
    {
        ResidueTitle.Text = $"“{app.DisplayName}”卸载后可能还留下 {leftovers.Count} 项，请检查后再决定是否删除";
        ResidueList.ItemsSource = leftovers.Select(item => new ResidueRow(item)).ToList();
        ResiduePanel.Visibility = Visibility.Visible;
    }

    private async void OnRemoveResidueClick(object sender, RoutedEventArgs e)
    {
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

            StatusText.Text = failed.Count == 0
                ? $"已移除 {results.Count} 项，可在回收站或 Quarantine 目录中找回。"
                : $"已移除 {results.Count - failed.Count} 项，{failed.Count} 项失败：{failed[0].Item.Path} —— {failed[0].Error}";
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

    private void OnDismissResidueClick(object sender, RoutedEventArgs e) => ResiduePanel.Visibility = Visibility.Collapsed;
}
