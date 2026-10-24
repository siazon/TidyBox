using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerTools.Modules.DiskAnalyzer;

namespace PowerTools.App.Views;

// ToString is what screen readers announce for a list item, so keep it human-readable.
public record ResultRow(string Name, string Detail)
{
    public override string ToString() => $"{Name}, {Detail}";
}

public record GameRow(string AppId, string Name, string Detail)
{
    public override string ToString() => $"{Name}, {Detail}";
}

public sealed partial class DiskAnalyzerView : Page
{
    private DiskAnalyzerModule _module = null!;
    private DiskScanResult? _lastResult;

    public DiskAnalyzerView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _module = (DiskAnalyzerModule)e.Parameter;
        _ = LoadGamesAsync();
    }

    private async Task LoadGamesAsync()
    {
        try
        {
            var games = await Task.Run(_module.GetSteamGames);
            GamesHeader.Text = _module.IsSteamAvailable ? $"Steam 游戏库（{games.Count} 个游戏）" : "未检测到 Steam";
            GamesList.ItemsSource = games
                .Select(g => new GameRow(g.AppId, g.Name, $"{ByteSizeFormatter.Format(g.SizeBytes)} · 最后更新 {g.LastUpdatedUtc.ToLocalTime():yyyy-MM-dd}"))
                .ToList();
        }
        catch (Exception ex)
        {
            GamesHeader.Text = $"读取 Steam 游戏库失败：{ex.Message}";
        }
    }

    private async void OnScanClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        SetBusy(true);
        StatusText.Text = $"正在扫描 {folder.Path} …";
        try
        {
            _lastResult = await _module.ScanAsync(folder.Path);

            CompareToggle.IsChecked = false;
            CompareToggle.IsEnabled = _lastResult.Previous is not null;
            ShowSizes();

            var total = _lastResult.Current.Entries.Sum(x => x.SizeBytes);
            var history = _lastResult.Previous is null
                ? "这是第一次扫描，下次扫描后可以与它对比。"
                : $"上次扫描：{_lastResult.Previous.ScannedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            StatusText.Text = $"{folder.Path}：{_lastResult.Current.Entries.Count} 个文件夹，共 {ByteSizeFormatter.Format(total)}。{history}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"扫描失败：{ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnCompareChecked(object sender, RoutedEventArgs e) => ShowChanges();

    private void OnCompareUnchecked(object sender, RoutedEventArgs e) => ShowSizes();

    private void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string appId }) return;

        try
        {
            _module.UninstallSteamGame(appId);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"无法启动 Steam 卸载：{ex.Message}";
        }
    }

    private void ShowSizes()
    {
        ResultList.ItemsSource = _lastResult?.Current.Entries
            .Select(entry => new ResultRow(Path.GetFileName(entry.Path), ByteSizeFormatter.Format(entry.SizeBytes)))
            .ToList();
    }

    private void ShowChanges()
    {
        ResultList.ItemsSource = _lastResult?.Changes
            .Where(delta => delta.SizeDeltaBytes != 0)
            .Select(delta => new ResultRow(
                Path.GetFileName(delta.Path),
                $"{ByteSizeFormatter.FormatSigned(delta.SizeDeltaBytes)}（{ByteSizeFormatter.Format(delta.PreviousSizeBytes)} → {ByteSizeFormatter.Format(delta.CurrentSizeBytes)}）"))
            .ToList();
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        ScanButton.IsEnabled = !busy;
    }
}
