using TidyBox.Core.Modules;
using TidyBox.Core.Recycling;
using TidyBox.Modules.Downloads.Journal;
using TidyBox.Modules.Downloads.Review;
using TidyBox.Modules.Downloads.Rules;
using TidyBox.Modules.Downloads.Scanning;
using TidyBox.Modules.Downloads.Source;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.Modules.Downloads;

public sealed class DownloadsModule : IToolModule
{
    private readonly Func<DateTime> _clock;
    private readonly CategoryNames _names = new();
    private readonly DownloadScanner _scanner = new();
    private readonly ProposalBuilder _proposals;
    private readonly JournalStore _journal;
    private readonly KeptStore _kept;
    private readonly TidyExecutor _executor;
    private readonly TidyUndo _undo;

    public DownloadsModule(string? downloadsFolder = null, string? storageDirectory = null, IFileRecycler? recycler = null, Func<DateTime>? clock = null)
    {
        DownloadsFolderPath = downloadsFolder ?? DownloadsFolder.Resolve();
        StorageDirectory = storageDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TidyBox", "Downloads");
        _clock = clock ?? (() => DateTime.UtcNow);

        _proposals = new ProposalBuilder(_names);
        _journal = new JournalStore(Path.Combine(StorageDirectory, "journal"));
        _kept = new KeptStore(Path.Combine(StorageDirectory, "kept.json"));
        _executor = new TidyExecutor(DownloadsFolderPath, _journal, _kept, recycler ?? new ShellFileRecycler(), _clock);
        _undo = new TidyUndo(DownloadsFolderPath, _journal, _kept);
    }

    public string ModuleId => "downloads";

    public string DisplayName => "下载文件夹";

    public string DownloadsFolderPath { get; }

    public string StorageDirectory { get; }

    public Task InitializeAsync()
    {
        // Without a writable storage folder neither the journal nor the kept list can work, so the shell should show the module as failed.
        try
        {
            Directory.CreateDirectory(Path.Combine(StorageDirectory, "journal"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot create the downloads storage folder '{StorageDirectory}'.", ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>Looks at the downloads folder and reports what tidying and reviewing would show. Reads only: nothing is moved, written or deleted.</summary>
    public Task<DownloadsReport> ScanAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var now = _clock();
        var files = _scanner.Scan(DownloadsFolderPath, _names.AllFolderNames, now);
        ct.ThrowIfCancellationRequested();

        var proposals = _proposals.Build(DownloadsFolderPath, files);
        var review = ReviewList.Build(files, _kept, now);

        var domains = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in proposals.Select(p => p.File).Concat(review.Stale).Concat(review.Kept).DistinctBy(f => f.FullPath))
        {
            ct.ThrowIfCancellationRequested();
            if (SourceReader.TryGetDomain(file.FullPath) is { } domain) domains[file.FullPath] = domain;
        }

        return new DownloadsReport(
            DownloadsFolderPath, files.Sum(f => f.SizeBytes), review.Stale.Sum(f => f.SizeBytes), proposals, review.Stale, review.Kept, domains);
    }, ct);

    /// <exception cref="ArgumentException">A proposal points outside the downloads folder; nothing was changed.</exception>
    public Task<ExecutionResult> ApplyAsync(IReadOnlyList<Proposal> selected, CancellationToken ct = default)
        => Task.Run(() => _executor.Execute(selected), ct);

    /// <exception cref="ArgumentException">A file is outside the downloads folder; nothing was changed.</exception>
    public Task<ExecutionResult> RecycleAsync(IReadOnlyList<DownloadFile> files, CancellationToken ct = default)
        => Task.Run(() => _executor.Recycle(files), ct);

    public void Keep(DownloadFile file) => _kept.Keep(file);

    public bool Unkeep(string fullPath) => _kept.Unkeep(fullPath);

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Batch> GetHistory() => _journal.LoadAll();

    public UndoResult Undo(string batchId) => _undo.Undo(batchId);
}
