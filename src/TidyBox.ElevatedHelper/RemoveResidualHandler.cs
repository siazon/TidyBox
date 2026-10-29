using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TidyBox.Core.Elevation;

namespace TidyBox.ElevatedHelper;

/// <summary>
/// Removes leftover program folders with administrator rights. The request comes from the app, but this process does
/// not trust it: every path is re-checked against an allowlist, and nothing is deleted — folders are moved to a
/// quarantine folder so the user can still get them back.
/// </summary>
public sealed class RemoveResidualHandler : IElevatedOperationHandler
{
    // Names under Program Files / ProgramData that belong to Windows or are shared by many programs.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.Ordinal)
    {
        "microsoft", "windows", "windowsnt", "windowsapps", "commonfiles", "packages", "packagecache",
        "microsoftshared", "internetexplorer", "windowspowershell", "dotnet", "modifiablewindowsapps",
        // Registry only: keys under HKLM\SOFTWARE that Windows or many programs share.
        "classes", "policies", "wow6432node", "clients", "registeredapplications", "odbc",
    };

    private readonly IReadOnlyList<string> _allowedRoots;
    private readonly string _quarantineRoot;
    private readonly IRegistryTool _registryTool;
    private readonly QuarantineTrust _trust;
    private readonly Func<DateTime> _clock;
    private int _backupCounter;
    private bool _quarantineChecked;
    private string? _quarantineError;

    /// <param name="allowedRoots">Folders whose subfolders may be removed; defaults to Program Files (both) and ProgramData.</param>
    /// <param name="quarantineRoot">Where removed folders are moved; defaults to ProgramData\TidyBox\Quarantine.</param>
    /// <param name="trust">Who may control the quarantine folders; defaults to Administrators and SYSTEM.</param>
    /// <param name="clock">Names the per-request quarantine folder; defaults to the local time.</param>
    public RemoveResidualHandler(IReadOnlyList<string>? allowedRoots = null, string? quarantineRoot = null, IRegistryTool? registryTool = null,
        QuarantineTrust? trust = null, Func<DateTime>? clock = null)
    {
        _trust = trust ?? QuarantineTrust.Machine;
        _clock = clock ?? (() => DateTime.Now);
        _registryTool = registryTool ?? new ProcessRegistryTool();
        _allowedRoots = (allowedRoots ?? DefaultAllowedRoots())
            .Where(r => r.Length > 0)
            .Select(Full)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _quarantineRoot = Full(quarantineRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TidyBox", "Quarantine"));
    }

    // This helper is a 32-bit process on 64-bit Windows, where SpecialFolder.ProgramFiles is "Program Files (x86)";
    // ProgramW6432 is the native one.
    public static IReadOnlyList<string> DefaultAllowedRoots(Func<string, string?>? environment = null)
    {
        var env = environment ?? Environment.GetEnvironmentVariable;
        return new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)", "ProgramData" }
            .Select(name => env(name) ?? "")
            .ToList();
    }

    public string OperationType => "uninstall.removeResidual";

    public Task<ElevationResponse> HandleAsync(string payloadJson)
    {
        List<string?> folders, keys;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            folders = Strings(doc.RootElement, "folders");
            keys = Strings(doc.RootElement, "registryKeys");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Task.FromResult(new ElevationResponse(false, "invalid request payload", null));
        }

        _quarantineChecked = false;
        var stamp = _clock().ToString("yyyyMMdd-HHmmss");
        var results = folders.Select(f => Remove(f, stamp)).Concat(keys.Select(k => RemoveKey(k, stamp))).ToList();
        var json = JsonSerializer.Serialize(results.Select(r => new { path = r.Path, success = r.Error is null, error = r.Error }));
        var allOk = results.All(r => r.Error is null);
        return Task.FromResult(new ElevationResponse(allOk, allOk ? null : "some folders were rejected or could not be removed", json));
    }

    // "folders" is required, "registryKeys" is optional (older callers only send folders).
    private static List<string?> Strings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var array))
        {
            if (name == "folders") throw new KeyNotFoundException(name);
            return new List<string?>();
        }

        return array.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null).ToList();
    }

    private (string Path, string? Error) RemoveKey(string? requested, string stamp)
    {
        var key = requested ?? "";
        var error = ValidateKey(key);
        if (error is not null) return (key, error);

        var unsafeQuarantine = QuarantineProblem();
        if (unsafeQuarantine is not null) return (key, unsafeQuarantine);

        try
        {
            // The backup comes first and must really exist: with no way back, the key is not deleted.
            var stampFolder = Path.Combine(_quarantineRoot, stamp);
            var backupFolder = Path.Combine(stampFolder, "registry");
            var link = QuarantineFolder.CreateInside(stampFolder) ?? QuarantineFolder.CreateInside(backupFolder);
            if (link is not null) return (key, link);

            var safeName = new string(key.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            var backup = Path.Combine(backupFolder, $"{++_backupCounter}-{safeName.Substring(0, Math.Min(safeName.Length, 80))}.reg");

            if (_registryTool.Run(new[] { "export", key, backup, "/y", "/reg:64" }) != 0 || !File.Exists(backup) || new FileInfo(backup).Length == 0)
            {
                return (key, "could not back up the key (it may no longer exist); not deleted");
            }

            var exit = _registryTool.Run(new[] { "delete", key, "/f", "/reg:64" });
            return exit == 0 ? (key, null) : (key, $"reg.exe returned {exit}; the backup is kept in {backup}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (key, ex.Message);
        }
    }

    // Only HKLM\SOFTWARE\<vendor-or-app>[\<app>] (also under WOW6432Node), and never a name Windows or many programs share.
    private static string? ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "key is empty";
        if (key.IndexOfAny(new[] { '"', '\r', '\n', '*', '?' }) >= 0) return "key contains characters that are not allowed";

        var segments = key.Split('\\');
        if (segments.Length < 3
            || !segments[0].Equals("HKLM", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase))
        {
            return "key is not under HKLM\\SOFTWARE";
        }

        var rest = segments.Skip(2).ToArray();
        if (rest[0].Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase)) rest = rest.Skip(1).ToArray();

        if (rest.Length is < 1 or > 2) return "key is not a vendor or app key";
        if (rest.Any(s => s.Length == 0 || s == "." || s == "..")) return "key has an invalid path part";
        if (ProtectedNames.Contains(Normalize(rest[0]))) return "key is in a protected area";

        return null;
    }

    private (string Path, string? Error) Remove(string? requested, string stamp)
    {
        var path = requested ?? "";
        var error = Validate(path, out var full);
        if (error is not null) return (path, error);

        var unsafeQuarantine = QuarantineProblem();
        if (unsafeQuarantine is not null) return (path, unsafeQuarantine);

        try
        {
            var destinationFolder = Path.Combine(_quarantineRoot, stamp);
            var link = QuarantineFolder.CreateInside(destinationFolder);
            if (link is not null) return (path, link);

            var destination = Path.Combine(destinationFolder, Path.GetFileName(full));
            for (var n = 2; Directory.Exists(destination); n++)
            {
                destination = Path.Combine(destinationFolder, $"{Path.GetFileName(full)}-{n}");
            }

            // A rename on the same volume: nothing is copied or deleted, and a failure leaves the folder where it was.
            Directory.Move(full, destination);
            return (path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (path, ex.Message);
        }
    }

    // Once per request, and only when something is really about to be moved or backed up. The parent is secured too:
    // whoever controls it could replace the quarantine folder itself.
    private string? QuarantineProblem()
    {
        if (!_quarantineChecked)
        {
            _quarantineChecked = true;
            var parent = Path.GetDirectoryName(_quarantineRoot);
            _quarantineError = (parent is null ? null : QuarantineFolder.Secure(parent, _trust)) ?? QuarantineFolder.Secure(_quarantineRoot, _trust);
        }

        return _quarantineError;
    }

    private string? Validate(string path, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(path)) return "path is empty";
        if (!Path.IsPathFullyQualified(path)) return "path is not absolute";
        if (path.Split('\\', '/').Contains("..")) return "path traversal is not allowed";

        full = Full(path);
        if (IsUnder(full, _quarantineRoot) || string.Equals(full, _quarantineRoot, StringComparison.OrdinalIgnoreCase))
        {
            return "path is inside the quarantine folder";
        }

        var candidate = full;
        var root = _allowedRoots.FirstOrDefault(r => IsUnder(candidate, r));
        if (root is null) return "path is not inside an allowed folder";

        var segments = full.Substring(root.Length + 1).Split('\\');
        if (ProtectedNames.Contains(Normalize(segments[0]))) return "path is in a protected folder";

        // A junction or symlink anywhere on the way could make this path point somewhere else entirely.
        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) && new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return "path goes through a link";
            }
        }

        return Directory.Exists(full) ? null : "folder no longer exists";
    }

    private static string Full(string path) => Path.GetFullPath(path).TrimEnd('\\');

    private static bool IsUnder(string path, string root) => path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
