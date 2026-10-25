using System.Security;
using System.Text.Json;
using PowerTools.Core.Elevation;

namespace PowerTools.Modules.Uninstaller.Residue;

public record RemovalResult(ResidueItem Item, bool Success, string? Error);

/// <summary>
/// Removes leftovers recoverably. Folders go to the Recycle Bin; registry keys are first exported to a .reg file
/// (double-click it to restore) and only then deleted. Anything that needs administrator rights is handed to the
/// elevated helper, which checks the request again.
/// </summary>
public sealed class ResidueRemover
{
    public const string ElevatedOperation = "uninstall.removeResidual";

    private readonly IRecycleBin _recycleBin;
    private readonly IElevationClient _elevation;
    private readonly ResidueRoots _roots;
    private readonly IRegTool _regTool;
    private int _backupCounter;

    public ResidueRemover(IRecycleBin recycleBin, IElevationClient elevation, ResidueRoots roots, IRegTool? regTool = null, string? backupDirectory = null)
    {
        _recycleBin = recycleBin;
        _elevation = elevation;
        _roots = roots;
        _regTool = regTool ?? new ProcessRegTool();
        BackupDirectory = backupDirectory ?? DefaultBackupDirectory();
    }

    /// <summary>Where the .reg backups of removed registry keys are written.</summary>
    public string BackupDirectory { get; }

    // Documents rather than AppData: a packaged app's AppData writes are redirected into the package, where nobody would look.
    private static string DefaultBackupDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PowerTools", "RegistryBackups");

    /// <returns>One result per input item, in the same order.</returns>
    public async Task<IReadOnlyList<RemovalResult>> RemoveAsync(IReadOnlyList<ResidueItem> items, CancellationToken ct = default)
    {
        var results = new RemovalResult?[items.Count];
        var elevated = new List<int>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var problem = Check(item);
            if (problem is not null)
            {
                results[i] = Fail(item, problem);
            }
            else if (item.NeedsElevation)
            {
                elevated.Add(i);
            }
            else if (item.Kind == ResidueKind.Folder)
            {
                results[i] = MoveToRecycleBin(item);
            }
            else
            {
                results[i] = await RemoveRegistryKeyAsync(item, ct);
            }
        }

        if (elevated.Count > 0) await RemoveElevatedAsync(items, elevated, results, ct);

        return results!;
    }

    private string? Check(ResidueItem item)
    {
        if (!item.Removable) return "无法在这里删除，请手动处理。";

        if (item.Kind == ResidueKind.Folder)
        {
            if (_roots.IsProtectedFolder(item.Path)) return "这是受保护的位置，不会被删除。";
            return Directory.Exists(item.Path) ? null : "路径已不存在。";
        }

        return _roots.IsRemovableRegistryKey(item.Path) ? null : "这是受保护的注册表位置，不会被删除。";
    }

    private RemovalResult MoveToRecycleBin(ResidueItem item)
    {
        try
        {
            _recycleBin.Delete(item.Path);
            return new RemovalResult(item, true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            return Fail(item, ex.Message);
        }
    }

    private async Task<RemovalResult> RemoveRegistryKeyAsync(ResidueItem item, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var backup = Path.Combine(BackupDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Interlocked.Increment(ref _backupCounter)}-{FileSafe(item.Path)}.reg");

            // The backup comes first and must really exist: with no way back, the key is not deleted.
            var exported = await _regTool.RunAsync(new[] { "export", item.Path, backup, "/y" }, ct);
            if (exported != 0 || !File.Exists(backup) || new FileInfo(backup).Length == 0)
            {
                return Fail(item, "无法备份这个注册表项（可能已经不存在），已放弃删除。");
            }

            var deleted = await _regTool.RunAsync(new[] { "delete", item.Path, "/f" }, ct);
            return deleted == 0 ? new RemovalResult(item, true, null) : Fail(item, $"删除注册表项失败（reg.exe 返回 {deleted}）。备份仍保留在 {backup}。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Fail(item, ex.Message);
        }
    }

    private static string FileSafe(string registryPath)
    {
        var chars = registryPath.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars).Substring(0, Math.Min(chars.Length, 80));
    }

    private async Task RemoveElevatedAsync(IReadOnlyList<ResidueItem> items, List<int> indexes, RemovalResult?[] results, CancellationToken ct)
    {
        var folders = indexes.Where(i => items[i].Kind == ResidueKind.Folder).Select(i => items[i].Path).ToArray();
        var keys = indexes.Where(i => items[i].Kind == ResidueKind.RegistryKey).Select(i => items[i].Path).ToArray();

        ElevationResponse response;
        try
        {
            response = await _elevation.ExecuteAsync(new ElevationRequest(ElevatedOperation, JsonSerializer.Serialize(new { folders, registryKeys = keys })), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var i in indexes) results[i] = Fail(items[i], $"无法获得管理员权限：{ex.Message}");
            return;
        }

        var perItem = ParsePerItem(response.ResultJson);
        foreach (var i in indexes)
        {
            if (perItem.TryGetValue(items[i].Path, out var outcome))
            {
                results[i] = outcome.Success ? new RemovalResult(items[i], true, null) : Fail(items[i], outcome.Error ?? "删除失败。");
            }
            else
            {
                results[i] = Fail(items[i], response.ErrorMessage ?? "管理员帮手没有返回这一项的结果。");
            }
        }
    }

    private static Dictionary<string, (bool Success, string? Error)> ParsePerItem(string? json)
    {
        var map = new Dictionary<string, (bool, string?)>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(json)) return map;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return map;

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.TryGetProperty("path", out var path) && path.GetString() is { } p)
                {
                    var success = entry.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                    var error = entry.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    map[p] = (success, error);
                }
            }
        }
        catch (JsonException)
        {
            // An unreadable answer counts as "no answer"; the caller reports the items as failed.
        }

        return map;
    }

    private static RemovalResult Fail(ResidueItem item, string error) => new(item, false, error);
}
