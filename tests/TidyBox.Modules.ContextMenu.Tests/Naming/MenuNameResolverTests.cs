using TidyBox.Modules.ContextMenu.Model;
using TidyBox.Modules.ContextMenu.Naming;

namespace TidyBox.Modules.ContextMenu.Tests.Naming;

public class MenuNameResolverTests
{
    private const string SharingClsid = "{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}";
    private const string SomeClsid = "{AAAAAAAA-0000-0000-0000-000000000009}";

    private sealed class FakeLoader : IIndirectStringLoader
    {
        public Dictionary<string, string?> Strings { get; } = new();
        public string? Load(string indirect) => Strings.TryGetValue(indirect, out var value) ? value : indirect;
    }

    private static MenuNameResolver Resolver(FakeLoader? loader = null, Func<string, ModuleInfo?>? readModule = null)
        => new(loader ?? new FakeLoader(), readModule ?? (_ => null));

    [Fact]
    public void Resolve_KnownClsid_ReturnsChineseNameEvenWhenKeyNameDiffers()
    {
        var info = Resolver().Resolve(MenuEntryKind.Handler, "SomethingElse", SharingClsid.ToUpperInvariant(), null, "Shell extensions for sharing", null);

        Assert.Equal("共享", info.DisplayName);
    }

    [Theory]
    [InlineData(MenuEntryKind.Handler, "Sharing", "共享")]
    [InlineData(MenuEntryKind.Handler, "Open With", "打开方式")]
    [InlineData(MenuEntryKind.Handler, "SendTo", "发送到")]
    [InlineData(MenuEntryKind.Handler, "CopyAsPathMenu", "复制文件地址")]
    [InlineData(MenuEntryKind.Verb, "cmd", "在此处打开命令窗口")]
    [InlineData(MenuEntryKind.Verb, "find", "搜索")]
    public void Resolve_KnownKeyName_ReturnsChineseName(MenuEntryKind kind, string keyName, string expected)
    {
        Assert.Equal(expected, Resolver().Resolve(kind, keyName, null, null, null, null).DisplayName);
    }

    [Fact]
    public void Resolve_SameKeyNameButDifferentKind_DoesNotUseTheOtherKindsName()
    {
        // "cmd" is a known verb, not a known handler.
        Assert.Equal("cmd", Resolver().Resolve(MenuEntryKind.Handler, "cmd", null, null, null, null).DisplayName);
    }

    [Fact]
    public void Resolve_UnknownClsidWithRegisteredName_UsesRegisteredName()
    {
        var info = Resolver().Resolve(MenuEntryKind.Handler, "Foo", SomeClsid, null, "Foo Context Menu", null);

        Assert.Equal("Foo Context Menu", info.DisplayName);
    }

    [Fact]
    public void Resolve_EmptyRegisteredName_FallsBackToTheDllFileDescriptionAndTakesThePublisherFromIt()
    {
        var resolver = Resolver(readModule: path => path == @"C:\x\shellext.dll" ? new ModuleInfo("Antimalware Shell Extension", "Microsoft Corporation") : null);

        var info = resolver.Resolve(MenuEntryKind.Handler, "EPP", SomeClsid, @"C:\x\shellext.dll", "", null);

        Assert.Equal("Antimalware Shell Extension", info.DisplayName);
        Assert.Equal("Microsoft Corporation", info.Publisher);
    }

    [Fact]
    public void Resolve_PublisherComesFromTheDllEvenWhenTheNameCameFromTheTable()
    {
        var resolver = Resolver(readModule: _ => new ModuleInfo("Whatever", "Some Company"));

        var info = resolver.Resolve(MenuEntryKind.Handler, "Sharing", SharingClsid, @"C:\x\ntshrui.dll", null, null);

        Assert.Equal("共享", info.DisplayName);
        Assert.Equal("Some Company", info.Publisher);
    }

    [Fact]
    public void Resolve_NothingKnown_FallsBackToTheTrimmedKeyName()
    {
        var info = Resolver().Resolve(MenuEntryKind.Handler, " FileSyncEx", null, null, null, null);

        Assert.Equal("FileSyncEx", info.DisplayName);
        Assert.Null(info.Publisher);
    }

    [Fact]
    public void Resolve_MuiVerbIndirect_IsExpandedThroughTheLoader()
    {
        var loader = new FakeLoader();
        loader.Strings["@shell32.dll,-8506"] = "在此处打开";

        var info = Resolver(loader).Resolve(MenuEntryKind.Verb, "AnyCode", null, null, "ignored default", "@shell32.dll,-8506");

        Assert.Equal("在此处打开", info.DisplayName);
    }

    [Fact]
    public void Resolve_MuiVerbThatCannotBeLoaded_FallsBackToTheRegisteredName()
    {
        var loader = new FakeLoader();
        loader.Strings["@missing.dll,-1"] = null;

        var info = Resolver(loader).Resolve(MenuEntryKind.Verb, "AnyCode", null, null, "Open with Code", "@missing.dll,-1");

        Assert.Equal("Open with Code", info.DisplayName);
    }

    [Fact]
    public void Resolve_StripsAcceleratorAmpersands()
    {
        var info = Resolver().Resolve(MenuEntryKind.Verb, "x", null, null, "打开(&O)", null);

        Assert.Equal("打开(O)", info.DisplayName);
    }

    [Fact]
    public void Resolve_ModuleReaderThrows_FallsBackWithoutThrowing()
    {
        var resolver = Resolver(readModule: _ => throw new IOException("locked"));

        var info = resolver.Resolve(MenuEntryKind.Handler, "Foo", SomeClsid, @"C:\x\y.dll", "", null);

        Assert.Equal("Foo", info.DisplayName);
        Assert.Null(info.Publisher);
    }

    [Fact]
    public void IndirectStringLoader_PlainText_PassesThrough()
    {
        Assert.Equal("Just text", new IndirectStringLoader().Load("Just text"));
    }

    [Fact]
    public void IndirectStringLoader_UnresolvableReference_ReturnsNull()
    {
        Assert.Null(new IndirectStringLoader().Load("@this-dll-does-not-exist.dll,-1"));
    }

    [Fact]
    public void IndirectStringLoader_RealShellResource_IsResolved()
    {
        var text = new IndirectStringLoader().Load(@"@%SystemRoot%\system32\shell32.dll,-21770");

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("@", text);
    }

    [Fact]
    public void ModuleInfoReader_RealSystemDll_HasADescriptionAndACompany()
    {
        var info = ModuleInfoReader.Read(Path.Combine(Environment.SystemDirectory, "shell32.dll"));

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info!.Description));
        Assert.False(string.IsNullOrWhiteSpace(info.Company));
    }

    [Fact]
    public void ModuleInfoReader_MissingFile_ReturnsNull()
    {
        Assert.Null(ModuleInfoReader.Read(@"C:\definitely\not\here.dll"));
    }
}
