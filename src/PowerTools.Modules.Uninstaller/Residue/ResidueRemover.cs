using System.Security;
using System.Text.Json;
using PowerTools.Core.Elevation;

namespace PowerTools.Modules.Uninstaller.Residue;

public record RemovalResult(ResidueItem Item, bool Success, string? Error);

/// <summary>Removes folder leftovers recoverably: user-level folders go to the Recycle Bin, protected ones are quarantined by the elevated helper.</summary>
public sealed class ResidueRemover
{
    public const string ElevatedOperation = "uninstall.removeResidual";

    private readonly IRecycleBin _recycleBin;
    private readonly IElevationClient _elevation;
    private readonly ResidueRoots _roots;

    public ResidueRemover(IRecycleBin recycleBin, IElevationClient elevation, ResidueRoots roots)
    {
        _recycleBin = recycleBin;
        _elevation = elevation;
        _roots = roots;
    }

    /// <returns>One result per input item, in the same order.</returns>
    public async Task<IReadOnlyList<RemovalResult>> RemoveAsync(IReadOnlyList<ResidueItem> items, CancellationToken ct = default)
    {
        var results = new RemovalResult?[items.Count];
        var elevated = new List<int>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Kind != ResidueKind.Folder || !item.Removable)
            {
                results[i] = Fail(item, "无法在这里删除，请手动处理。");
            }
            else if (_roots.IsProtectedFolder(item.Path))
            {
                results[i] = Fail(item, "这是受保护的位置，不会被删除。");
            }
            else if (!Directory.Exists(item.Path))
            {
                results[i] = Fail(item, "路径已不存在。");
            }
            else if (item.NeedsElevation)
            {
                elevated.Add(i);
            }
            else
            {
                results[i] = MoveToRecycleBin(item);
            }
        }

        if (elevated.Count > 0) await RemoveElevatedAsync(items, elevated, results, ct);

        return results!;
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

    private async Task RemoveElevatedAsync(IReadOnlyList<ResidueItem> items, List<int> indexes, RemovalResult?[] results, CancellationToken ct)
    {
        var paths = indexes.Select(i => items[i].Path).ToArray();
        ElevationResponse response;
        try
        {
            response = await _elevation.ExecuteAsync(new ElevationRequest(ElevatedOperation, JsonSerializer.Serialize(new { folders = paths })), ct);
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
