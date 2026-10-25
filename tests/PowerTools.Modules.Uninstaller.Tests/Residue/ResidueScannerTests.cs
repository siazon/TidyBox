using Microsoft.Win32;
using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Residue;
using PowerTools.Modules.Uninstaller.Tests.Support;

namespace PowerTools.Modules.Uninstaller.Tests.Residue;

public sealed class ResidueScannerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private readonly RegistrySandbox _registry = new();
    private readonly string _programFiles;
    private readonly string _appData;

    public ResidueScannerTests()
    {
        _programFiles = Directory.CreateDirectory(Path.Combine(_base, "Program Files")).FullName;
        _appData = Directory.CreateDirectory(Path.Combine(_base, "AppData")).FullName;
    }

    public void Dispose()
    {
        _registry.Dispose();
        Directory.Delete(_base, recursive: true);
    }

    private ResidueScanner Scanner()
    {
        var roots = new ResidueRoots(
            folderRoots: new[] { _programFiles, _appData },
            elevatedFolderRoots: new[] { _programFiles },
            registryRoots: new[]
            {
                new RegistryRoot("HKCU", _registry.User, "Software", NeedsElevation: false),
                new RegistryRoot("HKLM", _registry.Machine64, "SOFTWARE", NeedsElevation: true),
            });
        return new ResidueScanner(roots);
    }

    private string Dir(params string[] parts) => Directory.CreateDirectory(Path.Combine(parts)).FullName;

    private static InstalledApp App(string name, string? publisher = null, string? installLocation = null)
        => new("id", name, publisher, null, null, null, AppSource.Win32, installLocation, null, null, null);

    private static void Remove(string path) => Directory.Delete(path, recursive: true);

    [Fact]
    public void Install_location_that_survives_uninstall_is_high_confidence_residue()
    {
        var install = Dir(_programFiles, "Odd Name Tool");
        var app = App("Whatever", installLocation: install);
        var scanner = Scanner();
        var before = scanner.Capture(app);

        var leftovers = scanner.FindLeftovers(app, before);

        var item = Assert.Single(leftovers);
        Assert.Equal(install, item.Path);
        Assert.Equal(ResidueKind.Folder, item.Kind);
        Assert.Equal(ResidueConfidence.High, item.Confidence);
        Assert.True(item.Removable);
        Assert.True(item.NeedsElevation);
    }

    [Fact]
    public void Folder_named_after_the_app_in_AppData_is_medium_confidence_and_needs_no_elevation()
    {
        var folder = Dir(_appData, "Postman");
        var app = App("Postman x64 12.23.2");
        var scanner = Scanner();
        var before = scanner.Capture(app);

        var item = Assert.Single(scanner.FindLeftovers(app, before));

        Assert.Equal(folder, item.Path);
        Assert.Equal(ResidueConfidence.Medium, item.Confidence);
        Assert.False(item.NeedsElevation);
    }

    [Fact]
    public void Items_created_only_after_the_uninstall_are_ignored()
    {
        var app = App("Postman");
        var scanner = Scanner();
        var before = scanner.Capture(app);
        Dir(_appData, "Postman"); // e.g. a log folder the uninstaller itself created

        Assert.Empty(scanner.FindLeftovers(app, before));
    }

    [Fact]
    public void Items_already_removed_by_the_uninstaller_are_not_reported()
    {
        var folder = Dir(_appData, "Postman");
        var app = App("Postman");
        var scanner = Scanner();
        var before = scanner.Capture(app);
        Remove(folder);

        Assert.Empty(scanner.FindLeftovers(app, before));
    }

    [Theory]
    [InlineData("Git")]
    [InlineData("Setup")]
    [InlineData("Microsoft")]
    public void Short_or_generic_names_never_match_by_name(string name)
    {
        Dir(_appData, name);
        Dir(_programFiles, name);
        var app = App(name);
        var scanner = Scanner();
        var before = scanner.Capture(app);

        Assert.Empty(scanner.FindLeftovers(app, before));
    }

    [Fact]
    public void A_generic_name_still_reports_its_own_install_location()
    {
        var install = Dir(_programFiles, "Git");
        var app = App("Git", installLocation: install);
        var scanner = Scanner();

        var leftovers = scanner.FindLeftovers(app, scanner.Capture(app));

        Assert.Equal(install, Assert.Single(leftovers).Path);
    }

    [Fact]
    public void Roots_and_protected_folders_are_never_candidates()
    {
        var protectedDir = Dir(_programFiles, "Windows");
        var rootApp = App("Anything Here", installLocation: _programFiles);
        var protectedApp = App("Anything Here", installLocation: protectedDir);
        var driveRootApp = App("Anything Here", installLocation: Path.GetPathRoot(_base));
        var scanner = Scanner();

        Assert.Empty(scanner.FindLeftovers(rootApp, scanner.Capture(rootApp)));
        Assert.Empty(scanner.FindLeftovers(protectedApp, scanner.Capture(protectedApp)));
        Assert.Empty(scanner.FindLeftovers(driveRootApp, scanner.Capture(driveRootApp)));
    }

    [Fact]
    public void Folder_shared_with_another_installed_app_is_not_residue_only_this_apps_subfolder()
    {
        var mine = Dir(_appData, "Contoso", "Foo Studio");
        Dir(_appData, "Contoso", "Other App");
        var app = App("Foo Studio", publisher: "Contoso");
        var scanner = Scanner();

        var leftovers = scanner.FindLeftovers(app, scanner.Capture(app));

        Assert.Equal(mine, Assert.Single(leftovers).Path);
    }

    [Fact]
    public void A_publisher_folder_is_never_reported_just_because_the_publisher_matches()
    {
        Dir(_appData, "Contoso", "Other App");
        var app = App("Foo Studio", publisher: "Contoso");
        var scanner = Scanner();

        Assert.Empty(scanner.FindLeftovers(app, scanner.Capture(app)));
    }

    [Fact]
    public void Chinese_names_are_matched_even_when_they_are_only_two_characters()
    {
        var folder = Dir(_appData, "微信");
        var app = App("微信");
        var scanner = Scanner();

        Assert.Equal(folder, Assert.Single(scanner.FindLeftovers(app, scanner.Capture(app))).Path);
    }

    [Fact]
    public void A_folder_that_only_contains_the_app_name_is_not_a_match()
    {
        Dir(_appData, "FooStudioPro");
        var app = App("Foo Studio");
        var scanner = Scanner();

        Assert.Empty(scanner.FindLeftovers(app, scanner.Capture(app)));
    }

    [Fact]
    public void Registry_residue_is_reported_but_never_removable()
    {
        using (var key = _registry.User.CreateSubKey(@"Software\Postman")) { }
        using (var key = _registry.Machine64.CreateSubKey(@"SOFTWARE\Postman")) { }
        var app = App("Postman");
        var scanner = Scanner();

        var leftovers = scanner.FindLeftovers(app, scanner.Capture(app)).OrderBy(i => i.Path).ToList();

        Assert.Equal(new[] { @"HKCU\Software\Postman", @"HKLM\SOFTWARE\Postman" }, leftovers.Select(i => i.Path));
        Assert.All(leftovers, i =>
        {
            Assert.Equal(ResidueKind.RegistryKey, i.Kind);
            Assert.False(i.Removable);
            Assert.Equal(ResidueConfidence.Medium, i.Confidence);
        });
        Assert.False(leftovers[0].NeedsElevation);
        Assert.True(leftovers[1].NeedsElevation);
    }

    [Fact]
    public void Protected_registry_keys_are_never_candidates()
    {
        using (var key = _registry.User.CreateSubKey(@"Software\Windows")) { }
        var app = App("Windows");
        var scanner = Scanner();

        Assert.Empty(scanner.FindLeftovers(app, scanner.Capture(app)));
    }

    [Fact]
    public void Vendor_subkey_matching_the_app_is_found_under_the_publisher_key()
    {
        using (var key = _registry.User.CreateSubKey(@"Software\Contoso\Foo Studio")) { }
        using (var key = _registry.User.CreateSubKey(@"Software\Contoso\Other App")) { }
        var app = App("Foo Studio", publisher: "Contoso");
        var scanner = Scanner();

        var item = Assert.Single(scanner.FindLeftovers(app, scanner.Capture(app)));

        Assert.Equal(@"HKCU\Software\Contoso\Foo Studio", item.Path);
    }
}
