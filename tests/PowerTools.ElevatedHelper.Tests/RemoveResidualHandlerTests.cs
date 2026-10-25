using System.Diagnostics;
using System.Text.Json;
using PowerTools.ElevatedHelper;

namespace PowerTools.ElevatedHelper.Tests;

public sealed class RemoveResidualHandlerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private readonly string _allowed;
    private readonly string _quarantine;
    private readonly string _outside;

    public RemoveResidualHandlerTests()
    {
        _allowed = Directory.CreateDirectory(Path.Combine(_base, "Program Files")).FullName;
        _quarantine = Path.Combine(_base, "Quarantine");
        _outside = Directory.CreateDirectory(Path.Combine(_base, "Users", "me", "Documents")).FullName;
    }

    public void Dispose()
    {
        // Junctions created by a test must go first, or recursive delete would follow them.
        foreach (var dir in Directory.EnumerateDirectories(_base, "*", SearchOption.AllDirectories))
        {
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(dir);
        }

        Directory.Delete(_base, recursive: true);
    }

    private RemoveResidualHandler Handler() => new(new[] { _allowed }, _quarantine);

    private static string Payload(params string[] folders) => JsonSerializer.Serialize(new { folders });

    private async Task<(bool Success, List<(string Path, bool Success, string? Error)> Items)> Run(params string[] folders)
    {
        var response = await Handler().HandleAsync(Payload(folders));
        var items = new List<(string, bool, string?)>();
        if (response.ResultJson is not null)
        {
            using var doc = JsonDocument.Parse(response.ResultJson);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                items.Add((e.GetProperty("path").GetString()!, e.GetProperty("success").GetBoolean(),
                    e.GetProperty("error").ValueKind == JsonValueKind.String ? e.GetProperty("error").GetString() : null));
            }
        }

        return (response.Success, items);
    }

    [Fact]
    public async Task Operation_type_is_the_one_the_app_sends()
    {
        Assert.Equal("uninstall.removeResidual", Handler().OperationType);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Rejects_path_outside_allowed_roots()
    {
        var victim = Directory.CreateDirectory(Path.Combine(_outside, "Important")).FullName;

        var (success, items) = await Run(victim);

        Assert.False(success);
        Assert.False(items.Single().Success);
        Assert.True(Directory.Exists(victim));
    }

    [Fact]
    public async Task Rejects_path_traversal()
    {
        var victim = Directory.CreateDirectory(Path.Combine(_outside, "Important")).FullName;
        Directory.CreateDirectory(Path.Combine(_allowed, "Foo"));
        var sneaky = Path.Combine(_allowed, "Foo", "..", "..", "Users", "me", "Documents", "Important");

        var (success, items) = await Run(sneaky);

        Assert.False(success);
        Assert.False(items.Single().Success);
        Assert.True(Directory.Exists(victim));
    }

    [Fact]
    public async Task Rejects_relative_and_empty_paths()
    {
        var (success, items) = await Run("Foo", "");

        Assert.False(success);
        Assert.All(items, i => Assert.False(i.Success));
    }

    [Fact]
    public async Task Rejects_the_roots_themselves_and_protected_vendor_folders()
    {
        var microsoft = Directory.CreateDirectory(Path.Combine(_allowed, "Microsoft")).FullName;
        var windows = Directory.CreateDirectory(Path.Combine(_allowed, "Windows NT", "Accessories")).FullName;

        var (_, items) = await Run(_allowed, microsoft, windows);

        Assert.All(items, i => Assert.False(i.Success));
        Assert.True(Directory.Exists(microsoft));
        Assert.True(Directory.Exists(windows));
    }

    [Fact]
    public async Task Rejects_a_junction_that_escapes_the_allowed_root()
    {
        var victim = Directory.CreateDirectory(Path.Combine(_outside, "Important")).FullName;
        File.WriteAllText(Path.Combine(victim, "keep.txt"), "x");
        var junction = Path.Combine(_allowed, "Evil");
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{victim}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);

        var (success, items) = await Run(junction, Path.Combine(junction, "..", "Evil"));

        Assert.False(success);
        Assert.All(items, i => Assert.False(i.Success));
        Assert.True(File.Exists(Path.Combine(victim, "keep.txt")));
    }

    [Fact]
    public async Task Allowed_folder_is_moved_to_quarantine_not_deleted()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_allowed, "Foo")).FullName;
        File.WriteAllText(Path.Combine(folder, "data.txt"), "hello");

        var (success, items) = await Run(folder);

        Assert.True(success);
        Assert.True(items.Single().Success);
        Assert.False(Directory.Exists(folder));
        var moved = Directory.EnumerateFiles(_quarantine, "data.txt", SearchOption.AllDirectories).Single();
        Assert.Equal("hello", File.ReadAllText(moved));
    }

    [Fact]
    public async Task Two_folders_with_the_same_name_do_not_collide_in_quarantine()
    {
        var a = Directory.CreateDirectory(Path.Combine(_allowed, "Foo")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(_allowed, "Sub", "Foo")).FullName;
        File.WriteAllText(Path.Combine(a, "a.txt"), "a");
        File.WriteAllText(Path.Combine(b, "b.txt"), "b");

        var (success, _) = await Run(a, b);

        Assert.True(success);
        Assert.Single(Directory.EnumerateFiles(_quarantine, "a.txt", SearchOption.AllDirectories));
        Assert.Single(Directory.EnumerateFiles(_quarantine, "b.txt", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Folders_already_in_quarantine_are_rejected()
    {
        Directory.CreateDirectory(_allowed);
        var quarantineInsideAllowed = new RemoveResidualHandler(new[] { _base }, _quarantine);
        var kept = Directory.CreateDirectory(Path.Combine(_quarantine, "20240101-000000", "Foo")).FullName;

        var response = await quarantineInsideAllowed.HandleAsync(Payload(kept));

        Assert.False(response.Success);
        Assert.True(Directory.Exists(kept));
    }

    [Fact]
    public async Task Mixed_request_reports_per_item_results()
    {
        var good = Directory.CreateDirectory(Path.Combine(_allowed, "Good")).FullName;
        var bad = Directory.CreateDirectory(Path.Combine(_outside, "Bad")).FullName;

        var (success, items) = await Run(good, bad);

        Assert.False(success);
        Assert.Equal(new[] { true, false }, items.Select(i => i.Success));
        Assert.Equal(new[] { good, bad }, items.Select(i => i.Path));
    }

    [Fact]
    public async Task A_malformed_payload_is_a_failure_not_a_crash()
    {
        var response = await Handler().HandleAsync("not json");

        Assert.False(response.Success);
    }
}
