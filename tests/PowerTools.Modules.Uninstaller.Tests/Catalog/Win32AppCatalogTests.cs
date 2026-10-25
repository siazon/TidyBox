using PowerTools.Modules.Uninstaller.Model;
using PowerTools.Modules.Uninstaller.Tests.Support;

namespace PowerTools.Modules.Uninstaller.Tests.Catalog;

public class Win32AppCatalogTests : IDisposable
{
    private readonly RegistrySandbox _sandbox = new();

    public void Dispose() => _sandbox.Dispose();

    [Fact]
    public void Lists_entries_from_all_three_roots()
    {
        RegistrySandbox.AddEntry(_sandbox.Machine64, "A", ("DisplayName", "Alpha"));
        RegistrySandbox.AddEntry(_sandbox.Machine32, "B", ("DisplayName", "Beta"));
        RegistrySandbox.AddEntry(_sandbox.User, "C", ("DisplayName", "Gamma"));

        var apps = _sandbox.Catalog().LoadAll();

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, apps.Select(a => a.DisplayName));
        Assert.Equal(new[] { @"HKLM64\A", @"HKLM32\B", @"HKCU\C" }, apps.Select(a => a.Id));
    }

    [Fact]
    public void Skips_entries_without_display_name_system_components_and_updates()
    {
        RegistrySandbox.AddEntry(_sandbox.Machine64, "NoName", ("Publisher", "x"));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "System", ("DisplayName", "Sys"), ("SystemComponent", 1));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Patch", ("DisplayName", "Patch"), ("ParentKeyName", "OperatingSystem"));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Hotfix", ("DisplayName", "Hotfix"), ("ReleaseType", "Hotfix"));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Upd", ("DisplayName", "Upd"), ("ReleaseType", "Update"));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Sec", ("DisplayName", "Sec"), ("ReleaseType", "Security Update"));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Real", ("DisplayName", "Real"));

        var apps = _sandbox.Catalog().LoadAll();

        Assert.Equal(new[] { "Real" }, apps.Select(a => a.DisplayName));
    }

    [Fact]
    public void Detects_steam_games_from_uninstall_string()
    {
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Steam App 220", ("DisplayName", "Half-Life 2"),
            ("UninstallString", "\"C:\\Steam\\steam.exe\" steam://uninstall/220"));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Fake", ("DisplayName", "Fake"),
            ("UninstallString", "\"C:\\Steam\\steam.exe\" steam://uninstall/22x"));

        var apps = _sandbox.Catalog().LoadAll().ToDictionary(a => a.DisplayName);

        Assert.Equal(AppSource.Steam, apps["Half-Life 2"].Source);
        Assert.Equal("220", apps["Half-Life 2"].SteamAppId);
        Assert.Equal(AppSource.Win32, apps["Fake"].Source);
        Assert.Null(apps["Fake"].SteamAppId);
    }

    [Fact]
    public void Reads_estimated_size_kb_as_bytes_and_tolerates_bad_formats()
    {
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Good", ("DisplayName", "Good"), ("EstimatedSize", 2048),
            ("InstallDate", "20240315"), ("InstallLocation", @"C:\Apps\Good"), ("Publisher", "Pub"), ("DisplayVersion", "1.2"));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Bad", ("DisplayName", "Bad"), ("InstallDate", "not-a-date"));

        var apps = _sandbox.Catalog().LoadAll().ToDictionary(a => a.DisplayName);

        Assert.Equal(2048L * 1024, apps["Good"].EstimatedSizeBytes);
        Assert.Equal(new DateTime(2024, 3, 15), apps["Good"].InstalledOn);
        Assert.Equal(@"C:\Apps\Good", apps["Good"].InstallLocation);
        Assert.Equal("Pub", apps["Good"].Publisher);
        Assert.Equal("1.2", apps["Good"].Version);
        Assert.Null(apps["Bad"].InstalledOn);
        Assert.Null(apps["Bad"].EstimatedSizeBytes);
    }

    [Fact]
    public void One_unreadable_value_does_not_fail_the_whole_list()
    {
        // DisplayName stored as a DWORD instead of a string: that entry is skipped, the rest still load.
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Bad", ("DisplayName", 5));
        RegistrySandbox.AddEntry(_sandbox.Machine64, "Ok", ("DisplayName", "Ok"));

        var apps = _sandbox.Catalog().LoadAll();

        Assert.Equal(new[] { "Ok" }, apps.Select(a => a.DisplayName));
    }
}
