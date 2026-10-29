namespace TidyBox.Modules.ContextMenu.Model;

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
/// <param name="Problem">Non-null when the registration looks unrecognised or stale (informational; the entry may still be changeable).</param>
/// <param name="Warning">Non-null when changing the entry has a side effect or it behaves unusually; shown next to the entry.</param>
/// <param name="LockedReason">Non-null when the entry must not be changed here; the view shows it read-only with this explanation.</param>
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
    string? Problem,
    string? Warning = null,
    string? LockedReason = null);
