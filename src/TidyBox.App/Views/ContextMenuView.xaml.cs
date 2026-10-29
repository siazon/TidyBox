using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TidyBox.Modules.ContextMenu;
using TidyBox.Modules.ContextMenu.Catalog;
using TidyBox.Modules.ContextMenu.Model;

namespace TidyBox.App.Views;

public sealed partial class ContextMenuView : Page
{
    private ContextMenuModule _module = null!;
    private List<MenuRow> _entryRows = new();
    private readonly HashSet<string> _collapsedGroups = new();

    public ContextMenuView()
    {
        InitializeComponent();
    }

    // Used from the row template (x:Bind functions).
    public static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowNot(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _module = (ContextMenuModule)e.Parameter;
        SetClassicSwitch(_module.IsClassicMenuEnabled);
        SetAnalyzeSwitch(_module.IsAnalyzeMenuEnabled);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var entries = await _module.LoadEntriesAsync();
            _entryRows = entries.Select(entry => new MenuRow(entry)).ToList();
            Rebuild();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"读取右键菜单失败：{ex.Message}";
        }
    }

    private void Rebuild()
    {
        var query = SearchBox.Text;
        var shown = _entryRows.Where(row => MenuEntryFilter.Matches(row.Entry!, query)).ToList();

        var rows = new List<MenuRow>();
        foreach (var group in shown.GroupBy(row => row.Entry!.Location))
        {
            var groupKey = group.Key.ToString();
            var isExpanded = !_collapsedGroups.Contains(groupKey);
            rows.Add(MenuRow.Header($"{group.Key.DisplayName()}（{group.Count()}）", groupKey, isExpanded));
            if (isExpanded) rows.AddRange(group);
        }

        MenuList.ItemsSource = rows;
        UpdateStatus(shown.Count);
    }

    private void OnHeaderRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string groupKey }) return;
        if (!_collapsedGroups.Remove(groupKey)) _collapsedGroups.Add(groupKey);
        Rebuild();
    }

    private void UpdateStatus(int? shownCount = null)
    {
        var disabled = _entryRows.Count(row => !row.IsOn);
        var text = $"共 {_entryRows.Count} 项，已禁用 {disabled} 项";
        var shown = shownCount ?? _entryRows.Count(row => MenuEntryFilter.Matches(row.Entry!, SearchBox.Text));
        StatusText.Text = shown != _entryRows.Count ? $"{text}，当前显示 {shown} 项" : text;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void OnEntryToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.DataContext is not MenuRow { Entry: { } entry } row) return;

        // Our own updates (and reverts) set the switch to what the row already says; nothing to do then.
        if (toggle.IsOn == row.IsOn) return;

        var wantEnabled = toggle.IsOn;
        var result = _module.SetEntryEnabled(entry, wantEnabled);
        if (!result.Success)
        {
            StatusText.Text = $"无法{(wantEnabled ? "启用" : "禁用")}“{entry.DisplayName}”：{result.Error}";
            toggle.IsOn = row.IsOn;
            return;
        }

        // The same handler can be listed under several locations; they all share one switch.
        var state = wantEnabled ? MenuEntryState.Enabled : MenuEntryState.Disabled;
        foreach (var other in _entryRows.Where(r => SameTarget(r.Entry!, entry))) other.SetState(state);

        if (result.NeedsExplorerRestart) RestartHint.IsOpen = true;
        UpdateStatus();
    }

    private static bool SameTarget(MenuEntry a, MenuEntry b)
        => a.Kind == MenuEntryKind.Handler && b.Kind == MenuEntryKind.Handler && a.Clsid is not null
            ? string.Equals(a.Clsid, b.Clsid, StringComparison.OrdinalIgnoreCase)
            : a.Id == b.Id;

    private async void OnRestoreAllClick(object sender, RoutedEventArgs e)
    {
        var entries = _entryRows.Select(row => row.Entry!).ToList();
        var count = entries.Count(entry => entry.State == MenuEntryState.Disabled && entry.LockedReason is null);
        if (count == 0)
        {
            StatusText.Text = "没有已停用的项目需要恢复。";
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "重新启用所有已停用的项目？",
            Content = $"共 {count} 个已停用的条目会重新显示在右键菜单里。系统级禁用的项和被锁定的核心项不会改动。",
            PrimaryButtonText = "全部恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        RestoreAllButton.IsEnabled = false;
        try
        {
            var result = _module.RestoreAll(entries);
            await LoadAsync();

            // Loading rewrites the status line, so the outcome is written after it.
            StatusText.Text = result.Failures.Count == 0
                ? $"已恢复 {result.Restored} 项。"
                : $"已恢复 {result.Restored} 项，{result.Failures.Count} 项失败：{string.Join("；", result.Failures)}";
            if (result.NeedsExplorerRestart) RestartHint.IsOpen = true;
        }
        finally
        {
            RestoreAllButton.IsEnabled = true;
        }
    }

    private bool _settingClassicSwitch;

    private void SetClassicSwitch(bool on)
    {
        _settingClassicSwitch = true;
        ClassicSwitch.IsOn = on;
        _settingClassicSwitch = false;
    }

    private void OnClassicToggled(object sender, RoutedEventArgs e)
    {
        if (_settingClassicSwitch) return;

        var result = _module.SetClassicMenu(ClassicSwitch.IsOn);
        if (!result.Success)
        {
            StatusText.Text = $"无法更改经典右键菜单设置：{result.Error}";
            SetClassicSwitch(_module.IsClassicMenuEnabled);
            return;
        }

        if (result.NeedsExplorerRestart) RestartHint.IsOpen = true;
    }

    private bool _settingAnalyzeSwitch;

    private void SetAnalyzeSwitch(bool on)
    {
        _settingAnalyzeSwitch = true;
        AnalyzeSwitch.IsOn = on;
        _settingAnalyzeSwitch = false;
    }

    private async void OnAnalyzeToggled(object sender, RoutedEventArgs e)
    {
        if (_settingAnalyzeSwitch) return;

        var result = _module.SetAnalyzeMenu(AnalyzeSwitch.IsOn);
        if (!result.Success)
        {
            StatusText.Text = $"无法更改“分析此文件夹”菜单：{result.Error}";
            SetAnalyzeSwitch(_module.IsAnalyzeMenuEnabled);
            return;
        }

        // The entries are verbs like any other, so they appear in (or leave) the list below.
        await LoadAsync();
    }

    private async void OnRestartClick(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "重启资源管理器？",
            Content = "这会关闭所有已打开的文件夹窗口，并重新启动桌面和任务栏。",
            PrimaryButtonText = "重启",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        RestartButton.IsEnabled = false;
        StatusText.Text = "正在重启资源管理器…";
        try
        {
            await _module.RestartExplorerAsync();
            RestartHint.IsOpen = false;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"重启资源管理器失败：{ex.Message}";
        }
        finally
        {
            RestartButton.IsEnabled = true;
        }
    }
}
