using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using PowerTools.ElevatedHelper;

namespace PowerTools.ElevatedHelper.Tests;

/// <summary>The tests run without elevation, so "the people allowed to control the quarantine" is the current user here.</summary>
public static class TestTrust
{
    public static SecurityIdentifier Me => WindowsIdentity.GetCurrent().User!;

    public static QuarantineTrust CurrentUser => new(Me, new[] { Me });
}

public sealed class QuarantineSecurityTests : IDisposable
{
    private sealed class FakeRegistryTool : IRegistryTool
    {
        public List<string[]> Calls = new();

        public int Run(IReadOnlyList<string> args)
        {
            Calls.Add(args.ToArray());
            return 0;
        }
    }

    private static readonly DateTime Moment = new(2026, 1, 2, 3, 4, 5);
    private const string Stamp = "20260102-030405";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "PowerToolsTests", Guid.NewGuid().ToString("N"));
    private readonly string _allowed;
    private readonly string _outside;
    private readonly string _parent;
    private readonly string _quarantine;
    private readonly FakeRegistryTool _tool = new();

    public QuarantineSecurityTests()
    {
        _allowed = Directory.CreateDirectory(Path.Combine(_base, "Program Files")).FullName;
        _outside = Directory.CreateDirectory(Path.Combine(_base, "Users", "me", "Documents")).FullName;
        _parent = Path.Combine(_base, "PowerTools");
        _quarantine = Path.Combine(_parent, "Quarantine");
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

    private RemoveResidualHandler Handler(QuarantineTrust? trust = null)
        => new(new[] { _allowed }, _quarantine, _tool, trust ?? TestTrust.CurrentUser, () => Moment);

    private static string FolderPayload(params string[] folders) => JsonSerializer.Serialize(new { folders });

    private string NewFolder(string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_allowed, name)).FullName;
        File.WriteAllText(Path.Combine(folder, "data.txt"), "hello");
        return folder;
    }

    private static void Junction(string link, string target)
    {
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
    }

    private static List<string> Principals(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected, $"{path} still inherits permissions from its parent");
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Select(r => r.IdentityReference.Value).Distinct().ToList();
    }

    [Fact]
    public async Task A_new_quarantine_and_its_parent_are_open_only_to_the_trusted_principals()
    {
        var response = await Handler().HandleAsync(FolderPayload(NewFolder("Foo")));

        Assert.True(response.Success);
        Assert.Equal(new[] { TestTrust.Me.Value }, Principals(_quarantine));
        Assert.Equal(new[] { TestTrust.Me.Value }, Principals(_parent));
    }

    [Fact]
    public async Task An_existing_quarantine_that_other_users_can_write_to_is_locked_down_again()
    {
        var existing = Directory.CreateDirectory(_quarantine);
        var security = existing.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        existing.SetAccessControl(security);

        var response = await Handler().HandleAsync(FolderPayload(NewFolder("Foo")));

        Assert.True(response.Success);
        Assert.Equal(new[] { TestTrust.Me.Value }, Principals(_quarantine));
    }

    [Fact]
    public async Task A_quarantine_owned_by_someone_untrusted_is_refused_and_nothing_is_moved()
    {
        Directory.CreateDirectory(_quarantine);
        var folder = NewFolder("Foo");
        var systemOnly = new QuarantineTrust(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new[] { new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) });

        var response = await Handler(systemOnly).HandleAsync(FolderPayload(folder));

        Assert.False(response.Success);
        Assert.True(File.Exists(Path.Combine(folder, "data.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_quarantine));
    }

    [Fact]
    public async Task A_link_planted_where_this_request_would_go_is_refused_and_the_folder_stays()
    {
        Directory.CreateDirectory(_quarantine);
        Junction(Path.Combine(_quarantine, Stamp), _outside);
        var folder = NewFolder("Foo");

        var response = await Handler().HandleAsync(FolderPayload(folder));

        Assert.False(response.Success);
        Assert.True(File.Exists(Path.Combine(folder, "data.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_outside));
    }

    [Fact]
    public async Task A_link_planted_where_the_registry_backup_would_go_stops_the_export_and_the_delete()
    {
        Directory.CreateDirectory(Path.Combine(_quarantine, Stamp));
        Junction(Path.Combine(_quarantine, Stamp, "registry"), _outside);

        var response = await Handler().HandleAsync(JsonSerializer.Serialize(new { folders = Array.Empty<string>(), registryKeys = new[] { @"HKLM\SOFTWARE\Foo" } }));

        Assert.False(response.Success);
        Assert.Empty(_tool.Calls);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_outside));
    }
}
