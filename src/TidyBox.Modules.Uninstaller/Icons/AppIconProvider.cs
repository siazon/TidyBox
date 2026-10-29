using System.Collections.Concurrent;
using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.Modules.Uninstaller.Icons;

public interface IIconExtractor
{
    /// <returns>A PNG of the icon, or null when it cannot be read.</returns>
    Task<byte[]?> ExtractPngAsync(IconTarget target, int size, CancellationToken ct);
}

public interface IAppIconProvider
{
    Task<byte[]?> GetPngAsync(InstalledApp app, CancellationToken ct = default);
}

/// <summary>Finds an app's icon as PNG bytes; the same icon is only read once.</summary>
public sealed class AppIconProvider : IAppIconProvider
{
    private readonly IIconExtractor _extractor;
    private readonly int _size;
    private readonly ConcurrentDictionary<IconTarget, byte[]> _cache = new();

    public AppIconProvider(IIconExtractor extractor, int size = 64)
    {
        _extractor = extractor;
        _size = size;
    }

    public async Task<byte[]?> GetPngAsync(InstalledApp app, CancellationToken ct = default)
    {
        var target = await Task.Run(() => IconTargetResolver.Resolve(app), ct);
        if (target is null) return null;
        if (_cache.TryGetValue(target, out var cached)) return cached;

        var png = await _extractor.ExtractPngAsync(target, _size, ct);
        // A failure is not remembered: the file may appear later (an install finishing), and a retry is cheap.
        if (png is not null) _cache[target] = png;
        return png;
    }
}
