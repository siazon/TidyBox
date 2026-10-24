namespace PowerTools.Modules.ContextMenu.Model;

public enum MenuEntryKind
{
    /// <summary>A shell extension registered under shellex\ContextMenuHandlers.</summary>
    Handler,

    /// <summary>A static command registered under shell\&lt;verb&gt;.</summary>
    Verb,
}

public enum MenuEntryState
{
    Enabled,

    /// <summary>Disabled in the current user's hive; reversible from here.</summary>
    Disabled,

    /// <summary>Disabled only at machine level; this module cannot undo that.</summary>
    DisabledBySystem,
}

/// <param name="Id">Always <c>"{Location}|{Kind}|{KeyName}"</c>; <paramref name="KeyName"/> is kept verbatim (it may have leading spaces).</param>
/// <param name="Problem">Non-null when the entry is unrecognised or stale; the view shows it read-only with this explanation.</param>
public record MenuEntry(
    string Id,
    MenuLocation Location,
    MenuEntryKind Kind,
    string KeyName,
    string DisplayName,
    string? Publisher,
    string? ModulePath,
    string? Clsid,
    bool InMachineHive,
    bool InUserHive,
    MenuEntryState State,
    string? Problem);
