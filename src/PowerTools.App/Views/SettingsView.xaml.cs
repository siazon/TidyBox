using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerTools.Core.Modules;
using PowerTools.Modules.DiskAnalyzer;
using PowerTools.Modules.Uninstaller;
using Windows.Storage;

namespace PowerTools.App.Views;

public sealed partial class SettingsView : Page
{
    private UninstallerModule _uninstaller = null!;

    public SettingsView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var registry = (ModuleRegistry)e.Parameter;
        _uninstaller = (UninstallerModule)registry.GetById("uninstaller")!;

        RegistryBackupPathText.Text = _uninstaller.RegistryBackupDirectory;
        VersionText.Text = $"PowerTools {AppVersionText()}";

        ThemeCombo.SelectedIndex = ApplicationData.Current.LocalSettings.Values["Theme"] switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };

        _ = LoadQuarantineUsageAsync();
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

    private async Task LoadQuarantineUsageAsync()
    {
        try
        {
            var summary = await _uninstaller.InspectQuarantineAsync();
            QuarantineUsageText.Text = summary.BatchCount == 0
                ? "隔离区是空的"
                : $"隔离区当前占用 {ByteSizeFormatter.Format(summary.TotalBytes)}（{summary.BatchCount} 次清理）";
        }
        catch (Exception)
        {
            QuarantineUsageText.Text = ""; // not critical; the button still works without this hint
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

    private async void OnPurgeQuarantineClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "清空隔离区？",
            Content = "隔离区里是之前清理残留时移进来、需要管理员权限才能删除的文件。清空后无法恢复，确定要清空吗？",
            PrimaryButtonText = "清空隔离区",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        PurgeQuarantineButton.IsEnabled = false;
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

        await LoadQuarantineUsageAsync();
    }

    private async void OnEmptyRecycleBinClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "清空回收站？",
            Content = "这会清空整台机器的回收站，不止 PowerTools 清理过的内容，且无法撤销。",
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

        if (App.CurrentWindow?.Content is FrameworkElement root) root.RequestedTheme = theme;
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
