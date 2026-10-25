using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PowerTools.Core.Elevation;

namespace PowerTools.ElevatedHelper;

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
    };

    private readonly IReadOnlyList<string> _allowedRoots;
    private readonly string _quarantineRoot;

    /// <param name="allowedRoots">Folders whose subfolders may be removed; defaults to Program Files (both) and ProgramData.</param>
    /// <param name="quarantineRoot">Where removed folders are moved; defaults to ProgramData\PowerTools\Quarantine.</param>
    public RemoveResidualHandler(IReadOnlyList<string>? allowedRoots = null, string? quarantineRoot = null)
    {
        _allowedRoots = (allowedRoots ?? new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            })
            .Where(r => r.Length > 0)
            .Select(Full)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _quarantineRoot = Full(quarantineRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PowerTools", "Quarantine"));
    }

    public string OperationType => "uninstall.removeResidual";

    public Task<ElevationResponse> HandleAsync(string payloadJson)
    {
        List<string?> folders;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            folders = doc.RootElement.GetProperty("folders").EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Task.FromResult(new ElevationResponse(false, "invalid request payload", null));
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var results = folders.Select(f => Remove(f, stamp)).ToList();
        var json = JsonSerializer.Serialize(results.Select(r => new { path = r.Path, success = r.Error is null, error = r.Error }));
        var allOk = results.All(r => r.Error is null);
        return Task.FromResult(new ElevationResponse(allOk, allOk ? null : "some folders were rejected or could not be removed", json));
    }

    private (string Path, string? Error) Remove(string? requested, string stamp)
    {
        var path = requested ?? "";
        var error = Validate(path, out var full);
        if (error is not null) return (path, error);

        try
        {
            var destinationFolder = Directory.CreateDirectory(Path.Combine(_quarantineRoot, stamp)).FullName;
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
