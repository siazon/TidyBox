using TidyBox.Modules.Uninstaller.Catalog;
using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Tests.Catalog;

public sealed class AppSizerTests : IDisposable
{
    private const string FullName = "Contoso.Weather_4.54.63045.0_x64__8wekyb3d8bbwe";
    private const string Family = "Contoso.Weather_8wekyb3d8bbwe";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "TidyBoxTests", Guid.NewGuid().ToString("N"));
    private readonly string _install;
    private readonly string _dataRoot;

    public AppSizerTests()
    {
        _install = Directory.CreateDirectory(Path.Combine(_base, "WindowsApps", FullName)).FullName;
        _dataRoot = Directory.CreateDirectory(Path.Combine(_base, "Packages")).FullName;
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private static void Write(string folder, string name, int bytes)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), new byte[bytes]);
    }

    private InstalledApp Store(string? packageFullName = FullName, string? installLocation = null)
        => new(packageFullName ?? "x", "Weather", null, null, null, null, AppSource.Store, installLocation ?? _install, null, null, packageFullName);

    private InstalledApp Win32() => new("id", "Tool", null, null, null, null, AppSource.Win32, _install, "unins.exe", null, null);

    [Fact]
    public void A_store_app_counts_its_install_folder_and_its_data_folder()
    {
        Write(_install, "app.dll", 1000);
        Write(Path.Combine(_install, "Assets"), "logo.png", 200);
        Write(Path.Combine(_dataRoot, Family, "LocalState"), "cache.db", 50);

        Assert.Equal(1250, AppSizer.Measure(Store(), CancellationToken.None, _dataRoot));
    }

    [Fact]
    public void A_store_app_without_a_data_folder_is_just_its_install_folder()
    {
        Write(_install, "app.dll", 1000);

        Assert.Equal(1000, AppSizer.Measure(Store(), CancellationToken.None, _dataRoot));
    }

    [Fact]
    public void Data_folders_of_other_packages_are_not_counted()
    {
        Write(_install, "app.dll", 1000);
        Write(Path.Combine(_dataRoot, "Other.App_8wekyb3d8bbwe"), "big.bin", 5000);

        Assert.Equal(1000, AppSizer.Measure(Store(), CancellationToken.None, _dataRoot));
    }

    [Fact]
    public void A_package_that_is_not_installed_for_this_user_but_has_data_counts_the_data()
    {
        Write(Path.Combine(_dataRoot, Family), "left.bin", 70);

        Assert.Equal(70, AppSizer.Measure(Store(installLocation: Path.Combine(_base, "gone")), CancellationToken.None, _dataRoot));
    }

    [Fact]
    public void Nothing_found_at_all_gives_null_rather_than_zero()
    {
        Assert.Null(AppSizer.Measure(Store(installLocation: Path.Combine(_base, "gone")), CancellationToken.None, _dataRoot));
    }

    // An install folder the user may not read would otherwise show up as an app that takes 1 KB.
    [Fact]
    public void A_store_app_whose_folders_hold_nothing_readable_gives_null()
    {
        Assert.Null(AppSizer.Measure(Store(), CancellationToken.None, _dataRoot));
    }

    [Theory]
    [InlineData("NoUnderscoresAtAll")]
    [InlineData("Only_Two_Parts")]
    public void A_package_name_that_cannot_be_split_ignores_the_data_folder_instead_of_guessing(string name)
    {
        Write(_install, "app.dll", 1000);
        Write(Path.Combine(_dataRoot, name), "x.bin", 9999);

        Assert.Equal(1000, AppSizer.Measure(Store(name), CancellationToken.None, _dataRoot));
    }

    [Fact]
    public void A_desktop_program_is_only_its_install_folder()
    {
        Write(_install, "app.exe", 400);
        Write(Path.Combine(_dataRoot, Family), "x.bin", 9999);

        Assert.Equal(400, AppSizer.Measure(Win32(), CancellationToken.None, _dataRoot));
    }

    [Fact]
    public void An_empty_desktop_program_folder_is_zero_not_unknown()
    {
        Assert.Equal(0, AppSizer.Measure(Win32(), CancellationToken.None, _dataRoot));
    }

    [Fact]
    public void A_desktop_program_with_no_folder_is_unknown()
    {
        var app = Win32() with { InstallLocation = Path.Combine(_base, "gone"), UninstallString = null };

        Assert.Null(AppSizer.Measure(app, CancellationToken.None, _dataRoot));
    }

    // Zero Install keeps programs in its own content-addressed cache and registers no install location.
    [Fact]
    public void A_zero_install_program_says_where_the_size_went()
    {
        var app = Win32() with
        {
            InstallLocation = null,
            UninstallString = "\"C:\\Users\\me\\AppData\\Roaming\\Programs\\Zero Install\\0install-win.exe\" remove https://appdownload.deepl.com/windows/0install/deepl.xml",
        };

        Assert.Contains("Zero Install", AppSizer.UnknownReason(app));
    }

    [Fact]
    public void A_desktop_program_without_an_install_location_says_so()
    {
        var app = Win32() with { InstallLocation = null, UninstallString = "\"C:\\x\\unins000.exe\"" };

        var reason = AppSizer.UnknownReason(app);

        Assert.Contains("没有登记安装位置", reason);
        Assert.DoesNotContain("Zero Install", reason);
    }

    [Fact]
    public void A_store_app_says_its_folder_could_not_be_read()
    {
        Assert.Contains("读不到", AppSizer.UnknownReason(Store()));
    }

    [Fact]
    public void Cancelling_stops_the_measurement()
    {
        Write(_install, "app.dll", 10);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => AppSizer.Measure(Store(), cts.Token, _dataRoot));
    }
}
