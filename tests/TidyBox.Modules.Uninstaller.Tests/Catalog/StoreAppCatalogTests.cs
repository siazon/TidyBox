using TidyBox.Modules.Uninstaller.Catalog;
using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Tests.Catalog;

public class StoreAppCatalogTests
{
    private sealed class FakeSource(params StorePackage[] packages) : IStorePackageSource
    {
        public IReadOnlyList<StorePackage> FindForCurrentUser() => packages;
    }

    private static StorePackage Package(string name, bool framework = false, bool resource = false, bool system = false)
        => new($"{name}_1.0_x64__abc", name, "Pub", "1.0", new DateTime(2024, 1, 2), @"C:\Program Files\WindowsApps\" + name, framework, resource, system);

    [Fact]
    public void Filters_out_framework_resource_and_system_packages()
    {
        var catalog = new StoreAppCatalog(new FakeSource(
            Package("Zeta"), Package("Alpha"), Package("Fw", framework: true), Package("Res", resource: true), Package("Sys", system: true)));

        var apps = catalog.LoadAll();

        Assert.Equal(new[] { "Alpha", "Zeta" }, apps.Select(a => a.DisplayName));
    }

    [Fact]
    public void Maps_package_fields_to_an_installed_app()
    {
        var app = new StoreAppCatalog(new FakeSource(Package("Alpha"))).LoadAll().Single();

        Assert.Equal(AppSource.Store, app.Source);
        Assert.Equal("Alpha_1.0_x64__abc", app.Id);
        Assert.Equal("Alpha_1.0_x64__abc", app.PackageFullName);
        Assert.Equal("Pub", app.Publisher);
        Assert.Equal("1.0", app.Version);
        Assert.Equal(new DateTime(2024, 1, 2), app.InstalledOn);
        Assert.Null(app.UninstallString);
    }
}
