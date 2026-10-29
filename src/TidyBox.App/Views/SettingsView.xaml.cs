using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using TidyBox.App.Feedback;
using TidyBox.Core.Feedback;
using TidyBox.Core.Gestures;
using TidyBox.Core.Modules;
using TidyBox.Modules.DiskAnalyzer;
using TidyBox.Modules.Downloads;
using TidyBox.Modules.Uninstaller;
using Windows.Storage;

namespace TidyBox.App.Views;

public sealed partial class SettingsView : Page
{
    private UninstallerModule _uninstaller = null!;
    private DownloadsModule _tidy = null!;
    private readonly ConsecutiveTaps _versionTaps = new(required: 5, maxGap: TimeSpan.FromSeconds(3), clock: () => DateTime.UtcNow);
    private bool _showingTidyOptions; // set while the page fills the boxes itself, so ValueChanged does not act on it

    public SettingsView()
    {
        InitializeComponent();
    }

    // Tapping the version number five times in a row switches developer mode (see DeveloperMode). The page says how many taps are left once
    // it is clearly on purpose, and says what happened when the fifth lands.
    private void OnVersionTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_versionTaps.Tap())
        {
            DeveloperMode.IsEnabled = !DeveloperMode.IsEnabled;
            ShowDeveloperState(DeveloperMode.IsEnabled ? "已开启开发者模式（连续点击版本号 5 次可关闭）" : "已关闭开发者模式");
        }
        else if (_versionTaps.Remaining <= 3)
        {
            ShowDeveloperState($"再点击 {_versionTaps.Remaining} 次{(DeveloperMode.IsEnabled ? "关闭" : "开启")}开发者模式");
        }
    }

    private void ShowDeveloperState(string text)
    {
        DeveloperHint.Text = text;
        DeveloperHint.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowTidyOptions()
    {
        _showingTidyOptions = true;
        StaleDaysBox.Value = _tidy.Options.StaleDays;
        RecentMinutesBox.Value = _tidy.Options.RecentMinutes;
        _showingTidyOptions = false;
    }

    // Applied and saved as soon as a value changes; the next scan on the tidy page uses it. A box that is empty or mid-edit is ignored until it has a number.
    private void OnTidyOptionChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_showingTidyOptions || double.IsNaN(StaleDaysBox.Value) || double.IsNaN(RecentMinutesBox.Value)) return;

        try
        {
            var options = TidyOptions.Create((int)StaleDaysBox.Value, (int)RecentMinutesBox.Value);
            _tidy.SetOptions(options);
            TidyOptionsSettings.Save(options);
        }
        catch (ArgumentOutOfRangeException)
        {
            ShowTidyOptions(); // the boxes keep to the range; this is only a guard, and it puts back what is in force
        }
    }

    private void OnResetTidyOptionsClick(object sender, RoutedEventArgs e)
    {
        _tidy.SetOptions(TidyOptions.Default);
        TidyOptionsSettings.Save(TidyOptions.Default);
        ShowTidyOptions();
    }

    private bool _feedbackDialogOpen;

    private async void OnSendFeedbackClick(object sender, RoutedEventArgs e)
    {
        // A fast double-click queues a second click before the first dialog's overlay exists, and ShowAsync throws when a
        // ContentDialog is already open — from an async void handler, which takes the whole app down.
        if (_feedbackDialogOpen || FeedbackConfig.ResolveEndpoint() is not { } endpoint) return;

        _feedbackDialogOpen = true;
        try
        {
            var dialog = new FeedbackDialog(FeedbackClient.Create(endpoint)) { XamlRoot = XamlRoot };
            await dialog.ShowAsync();
            FeedbackStatusText.Text = dialog.WasSent ? "已发送，谢谢你的反馈。" : string.Empty;
        }
        finally
        {
            _feedbackDialogOpen = false;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var registry = (ModuleRegistry)e.Parameter;
        _uninstaller = (UninstallerModule)registry.GetById("uninstaller")!;
        _tidy = (DownloadsModule)registry.GetById("downloads")!;
        ShowTidyOptions();

        RegistryBackupPathText.Text = _uninstaller.RegistryBackupDirectory;
        VersionText.Text = $"TidyBox {AppVersionText()}";
        ShowDeveloperState(DeveloperMode.IsEnabled ? "开发者模式已开启（连续点击版本号 5 次可关闭）" : string.Empty);

        // With a feedback server configured the app can send something, but only when the user presses "发送"; the
        // privacy sentence must say so. Without one it stays as it was, because it is still true.
        if (FeedbackConfig.ResolveEndpoint() is not null)
        {
            FeedbackSection.Visibility = Visibility.Visible;
            PrivacyText.Text = "TidyBox 不会自动联网，也不会收集使用数据。只有在你点击“发送反馈”里的“发送”时，才会把你写的内容发送出去。";
        }

        ThemeCombo.SelectedIndex = ApplicationData.Current.LocalSettings.Values["Theme"] switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };

        // Querying the quarantine folder's usage requires admin rights (it is locked to Administrators/SYSTEM), so
        // it is not loaded automatically here — that would mean a UAC prompt just from opening Settings. It is only
        // queried when the user clicks "清空隔离区" below. The Recycle Bin needs no elevation, so that one does load.
        QuarantineUsageText.Text = "点击“清空隔离区”查看占用并清空（需要管理员权限）";
        _ = LoadRecycleBinUsageAsync();
    }

    private static string AppVersionText()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch (InvalidOperationException)
        {
            // Not running with a package identity (e.g. `dotnet run` during development).
            var v = typeof(SettingsView).Assembly.GetName().Version;
            return v is null ? "开发版" : $"{v.Major}.{v.Minor}.{v.Build}（开发版）";
        }
    }

    private async Task LoadRecycleBinUsageAsync()
    {
        try
        {
            var usage = await _uninstaller.GetRecycleBinUsageAsync();
            RecycleBinUsageText.Text = usage.ItemCount == 0
                ? "回收站是空的"
                : $"回收站当前占用 {ByteSizeFormatter.Format(usage.TotalBytes)}（{usage.ItemCount} 项）";
        }
        catch (Exception)
        {
            RecycleBinUsageText.Text = "";
        }
    }

    // Inspecting the quarantine folder needs admin rights, so the only UAC prompt for this whole flow happens
    // right here, after the user explicitly asked to manage it — never automatically on page load.
    private async void OnPurgeQuarantineClick(object sender, RoutedEventArgs e)
    {
        PurgeQuarantineButton.IsEnabled = false;
        QuarantineSummary summary;
        try
        {
            summary = await _uninstaller.InspectQuarantineAsync();
        }
        catch (Exception ex)
        {
            QuarantineUsageText.Text = $"查看隔离区失败：{ex.Message}";
            PurgeQuarantineButton.IsEnabled = true;
            return;
        }

        if (summary.BatchCount == 0)
        {
            QuarantineUsageText.Text = "隔离区是空的。";
            PurgeQuarantineButton.IsEnabled = true;
            return;
        }

        QuarantineUsageText.Text = $"隔离区当前占用 {ByteSizeFormatter.Format(summary.TotalBytes)}（{summary.BatchCount} 次清理）";

        var confirm = new ContentDialog
        {
            Title = "清空隔离区？",
            Content = $"隔离区里有 {summary.BatchCount} 次清理留下的内容，共占用 {ByteSizeFormatter.Format(summary.TotalBytes)}。\n\n清空后无法恢复，确定要清空吗？",
            PrimaryButtonText = "清空隔离区",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            PurgeQuarantineButton.IsEnabled = true;
            return;
        }

        try
        {
            var success = await _uninstaller.PurgeQuarantineAsync();
            QuarantineUsageText.Text = success ? "隔离区已清空。" : "清空隔离区失败。";
        }
        catch (Exception ex)
        {
            QuarantineUsageText.Text = $"清空隔离区失败：{ex.Message}";
        }
        finally
        {
            PurgeQuarantineButton.IsEnabled = true;
        }
    }

    private async void OnEmptyRecycleBinClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "清空回收站？",
            Content = "这会清空整台机器的回收站，不止 TidyBox 清理过的内容，且无法撤销。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _uninstaller.EmptyRecycleBinAsync();
            RecycleBinUsageText.Text = "回收站已清空。";
        }
        catch (Exception ex)
        {
            RecycleBinUsageText.Text = $"清空回收站失败：{ex.Message}";
        }

        await LoadRecycleBinUsageAsync();
    }

    private void OnOpenRegistryBackupFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_uninstaller.RegistryBackupDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { _uninstaller.RegistryBackupDirectory }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            RegistryBackupPathText.Text = $"{_uninstaller.RegistryBackupDirectory}（打开失败：{ex.Message}）";
        }
    }

    private void OnGoToContextMenuClick(object sender, RoutedEventArgs e)
    {
        if (App.CurrentWindow is MainWindow window) window.NavigateToModule("context-menu");
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;

        var theme = tag switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (App.CurrentWindow is { Content: FrameworkElement root } window)
        {
            root.RequestedTheme = theme;
            TitleBarTheme.Apply(window, theme);
        }

        ApplicationData.Current.LocalSettings.Values["Theme"] = tag;
    }

    private async void OnShowThirdPartyNoticesClick(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = $"无法读取第三方声明文件：{ex.Message}";
        }

        var dialog = new ContentDialog
        {
            Title = "第三方声明",
            Content = new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, MaxHeight = 420 },
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
