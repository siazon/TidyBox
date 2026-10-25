using System.ComponentModel;
using PowerTools.Modules.ContextMenu.Model;

namespace PowerTools.App.Views;

/// <summary>One line of the context menu list: either a location header or a menu entry with a toggle.</summary>
public sealed class MenuRow : INotifyPropertyChanged
{
    private MenuEntry? _entry;

    private MenuRow(MenuEntry? entry, string title)
    {
        _entry = entry;
        Title = title;
    }

    public MenuRow(MenuEntry entry) : this(entry, entry.DisplayName)
    {
    }

    public static MenuRow Header(string title) => new(null, title);

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsHeader => _entry is null;

    public string Title { get; }

    public MenuEntry? Entry => _entry;

    public string DisplayName => Title;

    /// <summary>The switch position: on means the entry shows up in the menu.</summary>
    public bool IsOn => _entry?.State == MenuEntryState.Enabled;

    public bool CanToggle => _entry is { State: not MenuEntryState.DisabledBySystem } e && !(e.Kind == MenuEntryKind.Handler && e.Clsid is null);

    public string KindText => _entry?.Kind == MenuEntryKind.Handler ? "扩展" : "命令";

    public string Detail
    {
        get
        {
            if (_entry is null) return "";
            if (_entry.Problem is not null) return _entry.Problem;
            if (_entry.Kind == MenuEntryKind.Verb) return $"命令名：{_entry.KeyName.Trim()}";
            return string.Join(" · ", new[] { _entry.Publisher, _entry.ModulePath }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    public string ToggleTooltip => _entry switch
    {
        null => "",
        { State: MenuEntryState.DisabledBySystem } => "系统已在机器级别禁用此项，无法在这里更改",
        { Kind: MenuEntryKind.Handler, Clsid: null } => _entry.Problem ?? "无法识别此项，不能更改",
        _ => IsOn ? "关闭后，右键菜单里不再显示此项" : "打开后，右键菜单里重新显示此项",
    };

    public void SetState(MenuEntryState state)
    {
        _entry = _entry! with { State = state };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleTooltip)));
    }

    // What screen readers announce for the list item.
    public override string ToString() => IsHeader ? Title : $"{DisplayName}, {(IsOn ? "已启用" : "已禁用")}";
}
