using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerTools.Modules.DiskAnalyzer.Scanning;

namespace PowerTools.Modules.DiskAnalyzer.History;

/// <summary>Keeps the latest scan snapshot per root path as a JSON file.</summary>
public class SnapshotStore
{
    private readonly string _storageDirectory;

    public SnapshotStore(string storageDirectory)
    {
        _storageDirectory = storageDirectory;
    }

    public async Task SaveAsync(DiskScanSnapshot snapshot)
    {
        Directory.CreateDirectory(_storageDirectory);
        await using var stream = File.Create(FileFor(snapshot.RootPath));
        await JsonSerializer.SerializeAsync(stream, snapshot);
    }

    /// <summary>Returns null when there is no snapshot for the root, or the stored file is unreadable.</summary>
    public async Task<DiskScanSnapshot?> LoadLatestAsync(string rootPath)
    {
        var file = FileFor(rootPath);
        if (!File.Exists(file)) return null;

        try
        {
            await using var stream = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync<DiskScanSnapshot>(stream);
        }
        catch (JsonException)
        {
            // History is a convenience; a truncated or corrupt file just means "no previous scan".
            return null;
        }
    }

    // Windows paths are case-insensitive and may or may not end with a separator; hash a normalized form so the
    // file name is also free of characters that are illegal in file names.
    private string FileFor(string rootPath)
    {
        var key = Path.TrimEndingDirectorySeparator(rootPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(_storageDirectory, hash + ".json");
    }
}
