namespace TidyBox.Core.Modules;

/// <summary>A self-contained feature module (disk analyzer, context menu manager, uninstaller...) hosted by the shell.</summary>
public interface IToolModule
{
    /// <summary>Stable identifier used as the registry key. Compared case-sensitively.</summary>
    string ModuleId { get; }

    string DisplayName { get; }

    /// <summary>Called once by the shell before the module is shown. If it throws, the shell shows the module as failed instead of crashing.</summary>
    Task InitializeAsync();
}
