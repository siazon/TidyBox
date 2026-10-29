using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Catalog;

/// <summary>Microsoft Store (MSIX) apps; frameworks, resource packs and system packages are not something a user uninstalls.</summary>
public sealed class StoreAppCatalog : IAppCatalog
{
    private readonly IStorePackageSource _source;

    public StoreAppCatalog(IStorePackageSource source) => _source = source;

    public IReadOnlyList<InstalledApp> LoadAll()
        => _source.FindForCurrentUser()
            .Where(p => !p.IsFramework && !p.IsResource && !p.IsSystem)
            .Select(p => new InstalledApp(
                Id: p.FullName,
                DisplayName: p.DisplayName,
                Publisher: p.Publisher,
                Version: p.Version,
                EstimatedSizeBytes: null,
                InstalledOn: p.InstalledOn,
                Source: AppSource.Store,
                InstallLocation: p.InstallLocation,
                UninstallString: null,
                SteamAppId: null,
                PackageFullName: p.FullName))
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
