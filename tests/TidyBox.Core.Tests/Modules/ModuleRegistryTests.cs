using TidyBox.Core.Modules;

namespace TidyBox.Core.Tests.Modules;

public class ModuleRegistryTests
{
    private class FakeModule : IToolModule
    {
        public string ModuleId { get; }
        public string DisplayName { get; }
        public FakeModule(string id, string name) { ModuleId = id; DisplayName = name; }
        public Task InitializeAsync() => Task.CompletedTask;
    }

    [Fact]
    public void Register_TwoModules_GetAllReturnsBoth()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeModule("disk", "磁盘分析"));
        registry.Register(new FakeModule("context-menu", "右键菜单管理"));

        Assert.Equal(2, registry.GetAll().Count);
    }

    [Fact]
    public void GetAll_ReturnsModulesInRegistrationOrder_WhichIsTheOrderOfTheNavigationMenu()
    {
        var registry = new ModuleRegistry();
        foreach (var id in new[] { "disk", "tidy", "uninstall", "context-menu", "vocab" }) registry.Register(new FakeModule(id, id));

        Assert.Equal(new[] { "disk", "tidy", "uninstall", "context-menu", "vocab" }, registry.GetAll().Select(m => m.ModuleId));
    }

    [Fact]
    public void Register_SameIdTwice_KeepsTheEarlierPosition()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeModule("a", "old"));
        registry.Register(new FakeModule("b", "b"));
        registry.Register(new FakeModule("a", "new"));

        Assert.Equal(new[] { "new", "b" }, registry.GetAll().Select(m => m.DisplayName));
    }

    [Fact]
    public void GetById_KnownId_ReturnsModule()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeModule("disk", "磁盘分析"));

        var found = registry.GetById("disk");

        Assert.NotNull(found);
        Assert.Equal("磁盘分析", found!.DisplayName);
    }

    [Fact]
    public void Register_SameIdTwice_ReplacesEarlierModule()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeModule("disk", "old"));
        registry.Register(new FakeModule("disk", "new"));

        Assert.Single(registry.GetAll());
        Assert.Equal("new", registry.GetById("disk")!.DisplayName);
    }

    [Fact]
    public void GetById_IsCaseSensitive()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeModule("disk", "Disk"));

        Assert.Null(registry.GetById("DISK"));
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        var registry = new ModuleRegistry();
        Assert.Null(registry.GetById("missing"));
    }
}
