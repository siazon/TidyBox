using System.Security;
using PowerTools.Core.Modules;
using PowerTools.Modules.ContextMenu.Analyze;
using PowerTools.Modules.ContextMenu.Catalog;
using PowerTools.Modules.ContextMenu.Classic;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Model;
using PowerTools.Modules.ContextMenu.Naming;
using PowerTools.Modules.ContextMenu.Toggle;

namespace PowerTools.Modules.ContextMenu;

/// <param name="Restored">How many switches were turned back on (a handler listed under several places counts once).</param>
/// <param name="Failures">One line per entry that could not be restored; the others were still restored.</param>
public record RestoreAllResult(int Restored, IReadOnlyList<string> Failures, bool NeedsExplorerRestart);

public sealed class ContextMenuModule : IToolModule
{
    private readonly ContextMenuRegistryRoots _roots;
    private readonly ContextMenuCatalog _catalog;
    private readonly MenuToggler _toggler;
    private readonly ClassicMenuSwitch _classicMenu;
    private readonly AnalyzeFolderMenu _analyzeMenu;
    private readonly IExplorerRestarter _restarter;

    /// <param name="analyzeExecutable">What the "analyze this folder" menu entries start; defaults to the app's execution alias.</param>
    /// <param name="analyzeIcon">An .ico file shown next to those entries.</param>
    public ContextMenuModule(ContextMenuRegistryRoots? roots = null, IMenuNameResolver? names = null, IExplorerRestarter? restarter = null,
        string? analyzeExecutable = null, string? analyzeIcon = null)
    {
        _roots = roots ?? ContextMenuRegistryRoots.Real();
        _catalog = new ContextMenuCatalog(_roots, names ?? MenuNameResolver.CreateDefault());
        _toggler = new MenuToggler(_roots);
        _classicMenu = new ClassicMenuSwitch(_roots);
        _analyzeMenu = new AnalyzeFolderMenu(_roots, analyzeExecutable ?? DefaultAnalyzeExecutable, icon: analyzeIcon);
        _restarter = restarter ?? new ExplorerRestarter();
    }

    /// <summary>
    /// The alias declared in the package manifest. Unlike the install folder, which has the version in its name, it is the
    /// same path after every update.
    /// </summary>
    public static string DefaultAnalyzeExecutable { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "PowerTools.exe");

    public string ModuleId => "context-menu";

    public string DisplayName => "右键菜单管理";

    public bool IsClassicMenuEnabled => _classicMenu.IsEnabled;

    public Task InitializeAsync()
    {
        // The only thing that can really be wrong up front: the registry classes roots cannot be opened at all.
        try
        {
            using var machine = _roots.Machine.OpenSubKey(ContextMenuRegistryRoots.ClassesPath);
            using var user = _roots.User.OpenSubKey(ContextMenuRegistryRoots.ClassesPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException or ObjectDisposedException)
        {
            throw new InvalidOperationException("无法读取注册表中的右键菜单信息，右键菜单管理不可用。", ex);
        }

        // After an update the icon sits in a new folder; an added "analyze this folder" menu must follow it.
        _analyzeMenu.RefreshIcon();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MenuEntry>> LoadEntriesAsync() => Task.Run(_catalog.LoadAll);

    public ToggleResult SetEntryEnabled(MenuEntry entry, bool enabled) => _toggler.SetEnabled(entry, enabled);

    /// <summary>
    /// Turns back on every entry that is disabled for the current user. Entries disabled at machine level are not ours to
    /// change, and locked ones are never touched. One failing entry does not stop the others.
    /// </summary>
    public RestoreAllResult RestoreAll(IReadOnlyList<MenuEntry> entries)
    {
        var restored = 0;
        var needsRestart = false;
        var failures = new List<string>();

        // A handler listed under several places shares one switch, so it is restored (and counted) once.
        var disabled = entries.Where(e => e.State == MenuEntryState.Disabled && e.LockedReason is null)
            .DistinctBy(e => e.Kind == MenuEntryKind.Handler && e.Clsid is not null ? "clsid:" + e.Clsid.ToUpperInvariant() : e.Id);
        foreach (var entry in disabled)
        {
            var result = _toggler.SetEnabled(entry, true);
            if (result.Success)
            {
                restored++;
                needsRestart |= result.NeedsExplorerRestart;
            }
            else
            {
                failures.Add($"{entry.DisplayName}：{result.Error}");
            }
        }

        return new RestoreAllResult(restored, failures, needsRestart);
    }

    public ToggleResult SetClassicMenu(bool enabled) => _classicMenu.SetEnabled(enabled);

    public bool IsAnalyzeMenuEnabled => _analyzeMenu.IsEnabled;

    public ToggleResult SetAnalyzeMenu(bool enabled) => _analyzeMenu.SetEnabled(enabled);

    public Task RestartExplorerAsync(CancellationToken ct = default) => _restarter.RestartAsync(ct);
}
