using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Catalog;

/// <summary>A source of installed apps. The module merges all of them into one list.</summary>
public interface IAppCatalog
{
    IReadOnlyList<InstalledApp> LoadAll();
}
