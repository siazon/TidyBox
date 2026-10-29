using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Catalog;

/// <summary>A source of installed apps. The module merges all of them into one list.</summary>
public interface IAppCatalog
{
    IReadOnlyList<InstalledApp> LoadAll();

    /// <summary>Whether the app is still registered as installed, or null when this catalog cannot tell.</summary>
    bool? IsInstalled(InstalledApp app) => null;
}
