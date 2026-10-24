using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PowerTools.Modules.DiskAnalyzer;
using PowerTools.Modules.DiskAnalyzer.Scanning;
using PowerTools.Modules.DiskAnalyzer.Tree;

namespace PowerTools.App.Views;

// ToString is what screen readers announce for a list item, so keep it human-readable.
public record GameRow(string AppId, string Name, string Detail)
{
    public override string ToString() => $"{Name}, {Detail}";
}

public sealed partial class DiskAnalyzerView : Page
{
    private static readonly SolidColorBrush GrowthBrush = new(ColorHelper.FromArgb(255, 209, 52, 56));
    private static readonly SolidColorBrush ShrinkBrush = new(ColorHelper.FromArgb(255, 16, 124, 16));

    private DiskAnalyzerModule _module = null!;
    private DiskTreeModel? _tree;

    public DiskAnalyzerView()
    {
        InitializeComponent();
        UpdateHeaders();
    }

    // Used from the row template (x:Bind functions).
    public static GridLength BarStar(double percent) => new(Math.Clamp(percent, 0, 100), GridUnitType.Star);

    public static GridLength RestStar(double percent) => new(100 - Math.Clamp(percent, 0, 100), GridUnitType.Star);

    public static Thickness IndentFor(int depth) => new(depth * 18, 0, 0, 0);

    public static string ExpanderGlyph(bool isExpanded, bool hasChildren)
        => !hasChildren ? "" : isExpanded ? "" : ""; // ChevronDown / ChevronRight

    public static string IconGlyph(DiskNodeKind kind) => kind == DiskNodeKind.FilesGroup ? "" : ""; // Document / Folder

    public static Brush ChangeBrush(string changeText)
        => changeText.StartsWith('+') ? GrowthBrush : changeText.StartsWith('-') ? ShrinkBrush : new SolidColorBrush(Colors.Gray);

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
            SteamTab.Header = _module.IsSteamAvailable ? $"Steam 游戏（{games.Count}）" : "Steam 游戏（未检测到 Steam）";
            GamesList.ItemsSource = games
                .Select(g => new GameRow(g.AppId, g.Name, $"{ByteSizeFormatter.Format(g.SizeBytes)} · 最后更新 {g.LastUpdatedUtc.ToLocalTime():yyyy-MM-dd}"))
                .ToList();
        }
        catch (Exception ex)
        {
            SteamTab.Header = "Steam 游戏（读取失败）";
            StatusText.Text = $"读取 Steam 游戏库失败：{ex.Message}";
        }
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        PathBox.Text = folder.Path;
        await ScanAsync(folder.Path);
    }

    private async void OnScanClick(object sender, RoutedEventArgs e) => await ScanAsync(PathBox.Text.Trim());

    private async void OnPathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await ScanAsync(PathBox.Text.Trim());
    }

    private async Task ScanAsync(string path)
    {
        if (path.Length == 0 || !Directory.Exists(path))
        {
            StatusText.Text = $"找不到文件夹：{path}";
            return;
        }

        SetBusy(true);
        StatusText.Text = $"正在扫描 {path} …";
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _module.ScanAsync(path);
            stopwatch.Stop();

            _tree = result.CreateTreeModel();
            TreeList.ItemsSource = _tree.Rows;
            UpdateHeaders();
            StatusText.Text = Summarize(result, stopwatch.Elapsed);
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

    private static string Summarize(DiskScanResult result, TimeSpan elapsed)
    {
        var root = result.Root;
        var text = $"{root.FullPath} · {ByteSizeFormatter.Format(root.SizeBytes)} · {root.FileCount:N0} 个文件 · {root.FolderCount:N0} 个文件夹 · 耗时 {elapsed.TotalSeconds:0.0} 秒";

        if (result.Previous is null) return text + " · 第一次扫描，下次扫描后可看到“较上次”的变化";

        var totalChange = result.Current.Entries.Sum(x => x.SizeBytes) - result.Previous.Entries.Sum(x => x.SizeBytes);
        return text + $" · 上次扫描 {result.Previous.ScannedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}，总量较上次 {ByteSizeFormatter.FormatSigned(totalChange)}";
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (_tree is null || sender is not Button { Tag: string column }) return;

        _tree.SortBy(Enum.Parse<DiskTreeColumn>(column));
        UpdateHeaders();
    }

    private void OnExpanderClick(object sender, RoutedEventArgs e)
    {
        if (_tree is not null && (sender as FrameworkElement)?.DataContext is DiskTreeRow row) _tree.Toggle(row);
    }

    private void OnRowDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_tree is not null && TreeList.SelectedItem is DiskTreeRow row) _tree.Toggle(row);
    }

    private void UpdateHeaders()
    {
        void Set(Button button, string title, DiskTreeColumn column)
        {
            var arrow = _tree is not null && _tree.SortColumn == column ? (_tree.SortDescending ? " ▼" : " ▲") : "";
            button.Content = title + arrow;
        }

        Set(NameHeader, "名称", DiskTreeColumn.Name);
        Set(SizeHeader, "大小", DiskTreeColumn.Size);
        Set(FilesHeader, "文件", DiskTreeColumn.Files);
        Set(FoldersHeader, "文件夹", DiskTreeColumn.Folders);
        Set(PercentHeader, "占上一层 %", DiskTreeColumn.Percent);
        Set(ModifiedHeader, "上次修改", DiskTreeColumn.Modified);
    }

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

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        ScanButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
    }
}
