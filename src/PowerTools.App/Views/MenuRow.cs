using System.ComponentModel;
using PowerTools.Modules.ContextMenu.Model;

namespace PowerTools.App.Views;

/// <summary>One line of the context menu list: either a location header or a menu entry with a toggle.</summary>
public sealed class MenuRow : INotifyPropertyChanged
{
    private MenuEntry? _entry;

    private MenuRow(MenuEntry? entry, string title, string groupKey = "", bool isExpanded = true)
    {
        _entry = entry;
        Title = title;
        GroupKey = groupKey;
        IsExpanded = isExpanded;
    }

    public MenuRow(MenuEntry entry) : this(entry, entry.DisplayName)
    {
    }

    /// <param name="groupKey">Stable identifier (not the display text) the view uses to remember collapsed state.</param>
    public static MenuRow Header(string title, string groupKey, bool isExpanded) => new(null, title, groupKey, isExpanded);

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsHeader => _entry is null;

    public string Title { get; }

    /// <summary>Only meaningful for a header row; which category it toggles.</summary>
    public string GroupKey { get; }

    /// <summary>Only meaningful for a header row; whether this category's entries are currently shown.</summary>
    public bool IsExpanded { get; }

    public string ExpanderGlyph => IsExpanded ? "" : ""; // ChevronDown / ChevronRight

    public MenuEntry? Entry => _entry;

    public string DisplayName => Title;

    /// <summary>The switch position: on means the entry shows up in the menu.</summary>
    public bool IsOn => _entry?.State == MenuEntryState.Enabled;

    public bool CanToggle => _entry is { LockedReason: null };

    public string KindText => _entry?.Kind == MenuEntryKind.Handler ? "扩展" : "命令";

    /// <summary>Why the entry is read-only, else what it is and any caveat — all visible, not only in a tooltip.</summary>
    public string Detail
    {
        get
        {
            if (_entry is null) return "";

            var what = _entry.LockedReason
                ?? _entry.Problem
                ?? (_entry.Kind == MenuEntryKind.Verb
                    ? $"命令名：{_entry.KeyName.Trim()}"
                    : string.Join(" · ", new[] { _entry.Publisher, _entry.ModulePath }.Where(part => !string.IsNullOrWhiteSpace(part))));

            return _entry.Warning is null ? what : $"{what}　⚠ {_entry.Warning}";
        }
    }

    public string ToggleTooltip => _entry switch
    {
        null => "",
        { LockedReason: { } reason } => reason,
        { Warning: { } warning } => warning,
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
