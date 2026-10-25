using System.Text.Json;
using PowerTools.Core.Elevation;
using PowerTools.Modules.Uninstaller.Residue;

namespace PowerTools.Modules.Uninstaller.Tests.Residue;

public sealed class ResidueRemoverTests : IDisposable
{
    private sealed class FakeRecycleBin : IRecycleBin
    {
        public List<string> Deleted = new();
        public Dictionary<string, Exception> Failures = new(StringComparer.OrdinalIgnoreCase);

        public void Delete(string path)
        {
            if (Failures.TryGetValue(path, out var failure)) throw failure;
            Deleted.Add(path);
        }
    }

    private sealed class FakeElevation : IElevationClient
    {
        public List<ElevationRequest> Requests = new();
        public Func<ElevationRequest, ElevationResponse> Respond = _ => new ElevationResponse(true, null, "[]");

        public Task<ElevationResponse> ExecuteAsync(ElevationRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private readonly string _programFiles;
    private readonly string _appData;
    private readonly FakeRecycleBin _bin = new();
    private readonly FakeElevation _elevation = new();

    public ResidueRemoverTests()
    {
        _programFiles = Directory.CreateDirectory(Path.Combine(_base, "Program Files")).FullName;
        _appData = Directory.CreateDirectory(Path.Combine(_base, "AppData")).FullName;
    }

    public void Dispose() => Directory.Delete(_base, recursive: true);

    private ResidueRemover Remover() => new(_bin, _elevation, new ResidueRoots(
        new[] { _programFiles, _appData }, new[] { _programFiles }, Array.Empty<RegistryRoot>()));

    private string Folder(string root, string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;

    private static ResidueItem Item(string path, bool elevated = false, bool removable = true, ResidueKind kind = ResidueKind.Folder)
        => new(kind, path, ResidueConfidence.High, "test", elevated, removable);

    [Fact]
    public async Task Folders_go_to_the_recycle_bin_not_deleted_directly()
    {
        var folder = Folder(_appData, "Foo");

        var results = await Remover().RemoveAsync(new[] { Item(folder) });

        Assert.True(Assert.Single(results).Success);
        Assert.Equal(new[] { folder }, _bin.Deleted);
        Assert.True(Directory.Exists(folder)); // the fake bin does not delete; the real one moves it to the Recycle Bin
        Assert.Empty(_elevation.Requests);
    }

    [Fact]
    public async Task Registry_items_are_refused_and_never_touched()
    {
        var results = await Remover().RemoveAsync(new[] { Item(@"HKCU\Software\Foo", kind: ResidueKind.RegistryKey, removable: false) });

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Contains("手动", result.Error);
        Assert.Empty(_bin.Deleted);
        Assert.Empty(_elevation.Requests);
    }

    [Fact]
    public async Task One_failing_item_does_not_stop_the_others()
    {
        var locked = Folder(_appData, "Locked");
        var denied = Folder(_appData, "Denied");
        var ok = Folder(_appData, "Ok");
        var gone = Path.Combine(_appData, "Gone");
        _bin.Failures[locked] = new IOException("in use");
        _bin.Failures[denied] = new UnauthorizedAccessException("denied");

        var results = await Remover().RemoveAsync(new[] { Item(locked), Item(denied), Item(gone), Item(ok) });

        Assert.Equal(new[] { false, false, false, true }, results.Select(r => r.Success));
        Assert.Contains("in use", results[0].Error);
        Assert.Contains("denied", results[1].Error);
        Assert.Contains("不存在", results[2].Error);
        Assert.Equal(new[] { locked, denied, gone, ok }, results.Select(r => r.Item.Path));
    }

    [Fact]
    public async Task Items_needing_elevation_are_sent_to_the_helper_and_not_touched_locally()
    {
        var elevated = Folder(_programFiles, "Foo");
        var plain = Folder(_appData, "Foo");
        _elevation.Respond = _ => new ElevationResponse(true, null, JsonSerializer.Serialize(new[] { new { path = elevated, success = true, error = (string?)null } }));

        var results = await Remover().RemoveAsync(new[] { Item(elevated, elevated: true), Item(plain) });

        var request = Assert.Single(_elevation.Requests);
        Assert.Equal("uninstall.removeResidual", request.OperationType);
        using var payload = JsonDocument.Parse(request.PayloadJson);
        Assert.Equal(new[] { elevated }, payload.RootElement.GetProperty("folders").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(new[] { plain }, _bin.Deleted);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task Per_item_results_from_the_helper_are_mapped_back()
    {
        var a = Folder(_programFiles, "A");
        var b = Folder(_programFiles, "B");
        _elevation.Respond = _ => new ElevationResponse(false, "partly failed", JsonSerializer.Serialize(new object[]
        {
            new { path = a, success = true, error = (string?)null },
            new { path = b, success = false, error = "path is not allowed" },
        }));

        var results = await Remover().RemoveAsync(new[] { Item(a, elevated: true), Item(b, elevated: true) });

        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.Contains("not allowed", results[1].Error);
    }

    [Fact]
    public async Task A_helper_failure_without_details_fails_all_elevated_items_with_its_message()
    {
        var a = Folder(_programFiles, "A");
        _elevation.Respond = _ => new ElevationResponse(false, "UAC was declined", null);

        var results = await Remover().RemoveAsync(new[] { Item(a, elevated: true) });

        Assert.False(results[0].Success);
        Assert.Contains("UAC was declined", results[0].Error);
    }

    [Fact]
    public async Task Protected_paths_are_refused_before_anything_is_sent()
    {
        var windows = Folder(_programFiles, "Windows");

        var results = await Remover().RemoveAsync(new[] { Item(_programFiles, elevated: true), Item(windows, elevated: true), Item(_appData) });

        Assert.All(results, r => Assert.False(r.Success));
        Assert.Empty(_bin.Deleted);
        Assert.Empty(_elevation.Requests);
    }
}
