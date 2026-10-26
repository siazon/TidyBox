using PowerTools.Modules.DiskAnalyzer.Steam;

namespace PowerTools.Modules.DiskAnalyzer.Tests.Steam;

public class SteamAppCacheTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "pt-cache-" + Guid.NewGuid() + ".json");

    public void Dispose()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }

    [Fact]
    public void TryGetName_UnknownAppId_ReturnsNull()
    {
        var cache = new SteamAppCache(_file);

        Assert.Null(cache.TryGetName("999"));
    }

    [Fact]
    public void Upsert_ThenTryGetName_ReturnsTheStoredName()
    {
        var cache = new SteamAppCache(_file);

        cache.Upsert("413150", "Stardew Valley");

        Assert.Equal("Stardew Valley", cache.TryGetName("413150"));
    }

    [Fact]
    public void Upsert_PersistsAcrossInstances()
    {
        new SteamAppCache(_file).Upsert("413150", "Stardew Valley");

        var reloaded = new SteamAppCache(_file);

        Assert.Equal("Stardew Valley", reloaded.TryGetName("413150"));
    }

    [Fact]
    public void Upsert_SameAppIdAgain_OverwritesTheName()
    {
        var cache = new SteamAppCache(_file);
        cache.Upsert("413150", "Old Name");

        cache.Upsert("413150", "New Name");

        Assert.Equal("New Name", cache.TryGetName("413150"));
    }

    [Fact]
    public void Upsert_DifferentAppIds_BothPersist()
    {
        var cache = new SteamAppCache(_file);
        cache.Upsert("1", "Game One");
        cache.Upsert("2", "Game Two");

        var reloaded = new SteamAppCache(_file);

        Assert.Equal("Game One", reloaded.TryGetName("1"));
        Assert.Equal("Game Two", reloaded.TryGetName("2"));
    }

    [Fact]
    public void Constructor_MissingFile_DoesNotThrow()
    {
        Assert.Null(new SteamAppCache(_file).TryGetName("1"));
    }

    [Fact]
    public void Constructor_CorruptFile_IsTreatedAsEmpty()
    {
        File.WriteAllText(_file, "not json {{{");

        Assert.Null(new SteamAppCache(_file).TryGetName("1"));
    }
}
