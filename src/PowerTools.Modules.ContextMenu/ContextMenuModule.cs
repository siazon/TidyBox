using System.Security;
using PowerTools.Core.Modules;
using PowerTools.Modules.ContextMenu.Catalog;
using PowerTools.Modules.ContextMenu.Classic;
using PowerTools.Modules.ContextMenu.Hives;
using PowerTools.Modules.ContextMenu.Model;
using PowerTools.Modules.ContextMenu.Naming;
using PowerTools.Modules.ContextMenu.Toggle;

namespace PowerTools.Modules.ContextMenu;

public sealed class ContextMenuModule : IToolModule
{
    private readonly ContextMenuRegistryRoots _roots;
    private readonly ContextMenuCatalog _catalog;
    private readonly MenuToggler _toggler;
    private readonly ClassicMenuSwitch _classicMenu;
    private readonly IExplorerRestarter _restarter;

    public ContextMenuModule(ContextMenuRegistryRoots? roots = null, IMenuNameResolver? names = null, IExplorerRestarter? restarter = null)
    {
        _roots = roots ?? ContextMenuRegistryRoots.Real();
        _catalog = new ContextMenuCatalog(_roots, names ?? MenuNameResolver.CreateDefault());
        _toggler = new MenuToggler(_roots);
        _classicMenu = new ClassicMenuSwitch(_roots);
        _restarter = restarter ?? new ExplorerRestarter();
    }

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

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MenuEntry>> LoadEntriesAsync() => Task.Run(_catalog.LoadAll);

    public ToggleResult SetEntryEnabled(MenuEntry entry, bool enabled) => _toggler.SetEnabled(entry, enabled);

    public ToggleResult SetClassicMenu(bool enabled) => _classicMenu.SetEnabled(enabled);

    public Task RestartExplorerAsync(CancellationToken ct = default) => _restarter.RestartAsync(ct);
}
