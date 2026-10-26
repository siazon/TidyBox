using System.Text.Json;
using Microsoft.Win32;
using PowerTools.Core.Elevation;
using PowerTools.Core.Recycling;
using PowerTools.Modules.Uninstaller.Residue;

namespace PowerTools.Modules.Uninstaller.Tests.Residue;

public sealed class ResidueRemoverRegistryTests : IDisposable
{
    private sealed class FakeRegTool : IRegTool
    {
        public List<string[]> Calls = new();
        public Func<string[], int> ExitCode = _ => 0;
        public bool CreateBackupFile = true;

        public Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            if (args[0] == "export" && CreateBackupFile) File.WriteAllText(args[2], "Windows Registry Editor Version 5.00");
            return Task.FromResult(ExitCode(args.ToArray()));
        }
    }

    private sealed class FakeBin : IRecycleBin
    {
        public void Delete(string folderPath) { }
        public void Empty() { }
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

    private readonly string _guid = Guid.NewGuid().ToString("N");
    private readonly string _backups = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private readonly FakeRegTool _tool = new();
    private readonly FakeElevation _elevation = new();

    // A real piece of HKCU stands in for "HKCU\Software", so reg.exe can really act on what the tests create.
    private string SandboxSoftware => $@"Software\PowerToolsTests\{_guid}\Software";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\PowerToolsTests\{_guid}", throwOnMissingSubKey: false);
        if (Directory.Exists(_backups)) Directory.Delete(_backups, recursive: true);
    }

    private ResidueRemover Remover(IRegTool? tool = null) => new(
        new FakeBin(),
        _elevation,
        new ResidueRoots(
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[]
            {
                new RegistryRoot("HKCU", Registry.CurrentUser, SandboxSoftware, NeedsElevation: false),
                new RegistryRoot("HKLM", Registry.CurrentUser, MachineSandbox, NeedsElevation: true),
            }),
        tool ?? _tool,
        _backups);

    // HKLM is stood in for by another piece of HKCU, so nothing real is read or changed in the machine hive.
    private string MachineSandbox => $@"Software\PowerToolsTests\{_guid}\Machine";

    // Creates the key for real: a key that does not exist is "already gone" and is never exported or deleted.
    private string Key(string relative)
    {
        using var created = Registry.CurrentUser.CreateSubKey($@"{SandboxSoftware}\{relative}");
        return $@"HKCU\{SandboxSoftware}\{relative}";
    }

    // A path as the scanner would write it, without creating anything.
    private string Raw(string relative) => $@"HKCU\{SandboxSoftware}\{relative}";

    private string MachineKey(string relative)
    {
        using var created = Registry.CurrentUser.CreateSubKey($@"{MachineSandbox}\{relative}");
        return $@"HKLM\{MachineSandbox}\{relative}";
    }

    private static ResidueItem Item(string path, bool elevated = false)
        => new(ResidueKind.RegistryKey, path, ResidueConfidence.Medium, "test", elevated, Removable: true);

    [Fact]
    public async Task A_registry_key_is_exported_to_a_reg_file_and_then_deleted()
    {
        var key = Key("Foo");

        var result = Assert.Single(await Remover().RemoveAsync(new[] { Item(key) }));

        Assert.True(result.Success);
        Assert.Equal(2, _tool.Calls.Count);
        Assert.Equal(new[] { "export", key }, _tool.Calls[0].Take(2));
        Assert.EndsWith(".reg", _tool.Calls[0][2]);
        Assert.StartsWith(_backups, _tool.Calls[0][2]);
        Assert.Equal(new[] { "delete", key, "/f" }, _tool.Calls[1]);
    }

    [Fact]
    public async Task A_failed_export_prevents_the_delete()
    {
        _tool.ExitCode = args => args[0] == "export" ? 1 : 0;

        var result = Assert.Single(await Remover().RemoveAsync(new[] { Item(Key("Foo")) }));

        Assert.False(result.Success);
        Assert.DoesNotContain(_tool.Calls, call => call[0] == "delete");
    }

    [Fact]
    public async Task An_export_that_leaves_no_backup_file_prevents_the_delete()
    {
        _tool.CreateBackupFile = false;

        var result = Assert.Single(await Remover().RemoveAsync(new[] { Item(Key("Foo")) }));

        Assert.False(result.Success);
        Assert.Contains("备份", result.Error);
        Assert.DoesNotContain(_tool.Calls, call => call[0] == "delete");
    }

    [Fact]
    public async Task A_failed_delete_is_reported()
    {
        _tool.ExitCode = args => args[0] == "delete" ? 1 : 0;

        var result = Assert.Single(await Remover().RemoveAsync(new[] { Item(Key("Foo")) }));

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(@"HKCU\Software\SomethingElse\Foo")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Foo")]
    [InlineData(@"HKCR\Foo")]
    [InlineData(@"Foo")]
    public async Task Keys_outside_the_configured_roots_are_refused(string path)
    {
        var result = Assert.Single(await Remover().RemoveAsync(new[] { Item(path) }));

        Assert.False(result.Success);
        Assert.Empty(_tool.Calls);
        Assert.Empty(_elevation.Requests);
    }

    [Fact]
    public async Task The_root_itself_protected_names_and_deep_paths_are_refused()
    {
        var items = new[]
        {
            Item($@"HKCU\{SandboxSoftware}"),
            Item(Key("Microsoft")),
            Item(Key("Classes")),
            Item(Key("Microsoft\\Foo")),
            Item(Key("A\\B\\C")),
            Item(Raw("Foo\\..\\..")),
            Item(Raw("")),
        };

        var results = await Remover().RemoveAsync(items);

        Assert.All(results, r => Assert.False(r.Success));
        Assert.Empty(_tool.Calls);
    }

    [Fact]
    public async Task Machine_keys_go_to_the_helper_together_and_are_not_touched_locally()
    {
        var a = MachineKey("Foo");
        var b = MachineKey(@"Vendor\Foo");
        _elevation.Respond = _ => new ElevationResponse(false, "partly", JsonSerializer.Serialize(new object[]
        {
            new { path = a, success = true, error = (string?)null },
            new { path = b, success = false, error = "refused" },
        }));

        var results = await Remover().RemoveAsync(new[] { Item(a, elevated: true), Item(b, elevated: true) });

        var request = Assert.Single(_elevation.Requests);
        Assert.Equal("uninstall.removeResidual", request.OperationType);
        using var payload = JsonDocument.Parse(request.PayloadJson);
        Assert.Equal(new[] { a, b }, payload.RootElement.GetProperty("registryKeys").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(_tool.Calls);
        Assert.Equal(new[] { true, false }, results.Select(r => r.Success));
        Assert.Contains("refused", results[1].Error);
    }

    [Fact]
    public async Task With_the_real_reg_exe_a_key_is_backed_up_removed_and_can_be_restored_from_the_backup()
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"{SandboxSoftware}\Foo"))
        {
            key.SetValue("Greeting", "hello from the test");
            key.SetValue("Number", 42);
            using var sub = key.CreateSubKey("Child");
            sub.SetValue("Deep", "value");
        }

        var result = Assert.Single(await Remover(new ProcessRegTool()).RemoveAsync(new[] { Item(Key("Foo")) }));

        Assert.True(result.Success, result.Error);
        using (var gone = Registry.CurrentUser.OpenSubKey($@"{SandboxSoftware}\Foo")) Assert.Null(gone);

        var backup = Assert.Single(Directory.GetFiles(_backups, "*.reg"));
        Assert.Contains("hello from the test", File.ReadAllText(backup));

        Assert.Equal(0, await new ProcessRegTool().RunAsync(new[] { "import", backup }, CancellationToken.None));
        using var restored = Registry.CurrentUser.OpenSubKey($@"{SandboxSoftware}\Foo");
        Assert.NotNull(restored);
        Assert.Equal("hello from the test", restored!.GetValue("Greeting"));
        using var child = restored.OpenSubKey("Child");
        Assert.Equal("value", child!.GetValue("Deep"));
    }

    [Fact]
    public async Task A_key_that_is_already_gone_counts_as_done_without_any_backup_or_helper_call()
    {
        var result = Assert.Single(await Remover().RemoveAsync(new[] { Item(Raw("NeverCreated")) }));

        Assert.True(result.Success);
        Assert.True(result.AlreadyGone);
        Assert.Empty(_tool.Calls);
        Assert.Empty(_elevation.Requests);
    }

    [Fact]
    public async Task A_machine_key_that_is_already_gone_is_not_sent_to_the_helper()
    {
        var result = Assert.Single(await Remover().RemoveAsync(new[] { Item($@"HKLM\{MachineSandbox}\NeverCreated", elevated: true) }));

        Assert.True(result.Success);
        Assert.True(result.AlreadyGone);
        Assert.Empty(_elevation.Requests);
    }
}
