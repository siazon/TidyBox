using PowerTools.Core.Modules;

namespace PowerTools.Core.Tests.Modules;

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
    public void GetById_KnownId_ReturnsModule()
    {
        var registry = new ModuleRegistry();
        registry.Register(new FakeModule("disk", "磁盘分析"));

        var found = registry.GetById("disk");

        Assert.NotNull(found);
        Assert.Equal("磁盘分析", found!.DisplayName);
    }

    [Fact]
    public void GetById_UnknownId_ReturnsNull()
    {
        var registry = new ModuleRegistry();
        Assert.Null(registry.GetById("missing"));
    }
}
