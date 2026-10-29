using System.Text.Json;

namespace TidyBox.Modules.Downloads.Journal;

/// <summary>One JSON file per batch. A damaged file is skipped, so the rest of the history still shows.</summary>
public sealed class JournalStore
{
    private readonly string _directory;

    public JournalStore(string directory)
    {
        _directory = directory;
    }

    public void Save(Batch batch)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FileFor(batch.BatchId), JsonSerializer.Serialize(batch));
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Batch> LoadAll()
    {
        if (!Directory.Exists(_directory)) return Array.Empty<Batch>();

        var batches = new List<Batch>();
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<Batch>(File.ReadAllText(file)) is { BatchId.Length: > 0, Entries: not null } batch) batches.Add(batch);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // History is a convenience; skip what cannot be read.
            }
        }

        return batches.OrderByDescending(batch => batch.CreatedUtc).ToList();
    }

    public void Delete(string batchId)
    {
        var file = FileFor(batchId);
        if (File.Exists(file)) File.Delete(file);
    }

    // The id names a file inside the journal folder; anything that could point elsewhere is refused.
    private string FileFor(string batchId)
    {
        if (batchId.Length == 0 || batchId != Path.GetFileName(batchId)) throw new ArgumentException($"'{batchId}' is not a valid batch id.", nameof(batchId));
        return Path.Combine(_directory, batchId + ".json");
    }
}
