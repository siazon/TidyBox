using PowerTools.Modules.Uninstaller.Catalog;

namespace PowerTools.Modules.Uninstaller.Tests.Catalog;

public sealed class ShortcutResolverTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));

    public ShortcutResolverTests() => Directory.CreateDirectory(_base);

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private string MakeShortcut(string name, string target)
    {
        var path = Path.Combine(_base, name + ".lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.Save();
        return path;
    }

    [Fact]
    public void A_shortcut_resolves_to_its_target()
    {
        var target = Path.Combine(_base, "real.exe");
        File.WriteAllText(target, "");

        Assert.Equal(target, ShortcutResolver.Resolve(MakeShortcut("real", target)));
    }

    [Fact]
    public void A_non_shortcut_path_is_returned_unchanged()
    {
        var file = Path.Combine(_base, "a.exe");

        Assert.Equal(file, ShortcutResolver.Resolve(file));
    }

    [Fact]
    public void A_shortcut_that_cannot_be_read_gives_null()
    {
        var broken = Path.Combine(_base, "broken.lnk");
        File.WriteAllText(broken, "not a shortcut");

        Assert.Null(ShortcutResolver.Resolve(broken));
    }
}
