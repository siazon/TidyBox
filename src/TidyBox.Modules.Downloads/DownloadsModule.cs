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
    // Applying, recycling, undoing and (un)keeping all write the journal and the kept list, which are not safe to touch from two threads at once.
    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;
    private readonly CategoryNames _names = new();
    private readonly DownloadScanner _scanner = new();
    private readonly ProposalBuilder _proposals;
    private readonly JournalStore _journal;
    private readonly KeptStore _kept;
    private readonly IFileRecycler _recycler;
    private TidyExecutor _executor;
    private TidyUndo _undo;

    /// <param name="downloadsFolder">The folder to tidy. The Downloads folder when null; the user can change it later with <see cref="SetFolder"/>.</param>
    /// <param name="options">The thresholds the user set; <see cref="TidyOptions.Default"/> when null.</param>
    public DownloadsModule(
        string? downloadsFolder = null, string? storageDirectory = null, IFileRecycler? recycler = null, Func<DateTime>? clock = null, TidyOptions? options = null)
    {
        DownloadsFolderPath = downloadsFolder ?? DownloadsFolder.Resolve();
        Options = options ?? TidyOptions.Default;
        StorageDirectory = storageDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TidyBox", "Downloads");
        _clock = clock ?? (() => DateTime.UtcNow);
        _recycler = recycler ?? new ShellFileRecycler();

        _proposals = new ProposalBuilder(_names);
        _journal = new JournalStore(Path.Combine(StorageDirectory, "journal"));
        _kept = new KeptStore(Path.Combine(StorageDirectory, "kept.json"));
        _executor = new TidyExecutor(DownloadsFolderPath, _journal, _kept, _recycler, _clock, Options.RecentWindow);
        _undo = new TidyUndo(DownloadsFolderPath, _journal, _kept);
    }

    public string ModuleId => "downloads";

    public string DisplayName => "文件整理";

    /// <summary>The folder being tidied: the Downloads folder unless the user chose another.</summary>
    public string DownloadsFolderPath { get; private set; }

    /// <summary>Switches to another folder. Everything the module does afterwards, and every path it accepts, is about that folder only.</summary>
    /// <exception cref="ArgumentException">The folder is not one TidyBox may tidy (see <see cref="FolderChoice"/>); the current folder stays.</exception>
    public void SetFolder(string path)
    {
        if (FolderChoice.Validate(path) is { } problem) throw new ArgumentException(problem, nameof(path));

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        lock (_gate)
        {
            DownloadsFolderPath = folder;
            _executor = new TidyExecutor(folder, _journal, _kept, _recycler, _clock, Options.RecentWindow);
            _undo = new TidyUndo(folder, _journal, _kept);
        }
    }

    /// <summary>The thresholds in force: how old a file must be to show up in the review, and how recently written a file is left alone.</summary>
    public TidyOptions Options { get; private set; }

    /// <summary>Applies new thresholds. They take effect at once: the next scan, and any recycle that is requested from now on, use them.</summary>
    public void SetOptions(TidyOptions options)
    {
        lock (_gate)
        {
            Options = options;
            _executor = new TidyExecutor(DownloadsFolderPath, _journal, _kept, _recycler, _clock, options.RecentWindow);
        }
    }

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
        var folder = DownloadsFolderPath; // one folder and one set of thresholds for the whole scan, even if the user changes them meanwhile
        var options = Options;
        var files = _scanner.Scan(folder, _names.AllFolderNames, now, options.RecentWindow);
        ct.ThrowIfCancellationRequested();

        var proposals = _proposals.Build(folder, files);
        ReviewResult review;
        lock (_gate) review = ReviewList.Build(files, _kept, now, options.StaleAfter); // reads the kept list, which an operation may be changing

        var domains = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in proposals.Select(p => p.File).Concat(review.Stale).Concat(review.Kept).DistinctBy(f => f.FullPath))
        {
            ct.ThrowIfCancellationRequested();
            if (SourceReader.TryGetDomain(file.FullPath) is { } domain) domains[file.FullPath] = domain;
        }

        return new DownloadsReport(
            folder, files.Sum(f => f.SizeBytes), review.Stale.Sum(f => f.SizeBytes), proposals, review.Stale, review.Kept, domains);
    }, ct);

    /// <exception cref="ArgumentException">A proposal points outside the downloads folder; nothing was changed.</exception>
    public Task<ExecutionResult> ApplyAsync(IReadOnlyList<Proposal> selected, CancellationToken ct = default)
        => Task.Run(() => { lock (_gate) return _executor.Execute(selected); }, ct);

    /// <exception cref="ArgumentException">A file is outside the downloads folder; nothing was changed.</exception>
    public Task<ExecutionResult> RecycleAsync(IReadOnlyList<DownloadFile> files, CancellationToken ct = default)
        => Task.Run(() => { lock (_gate) return _executor.Recycle(files); }, ct);

    public void Keep(DownloadFile file)
    {
        lock (_gate) _kept.Keep(file);
    }

    public bool Unkeep(string fullPath)
    {
        lock (_gate) return _kept.Unkeep(fullPath);
    }

    /// <summary>The batches that were about the current folder, newest first. Those of another folder come back when the user switches to it.</summary>
    public IReadOnlyList<Batch> GetHistory()
    {
        var folder = DownloadsFolderPath;
        return _journal.LoadAll().Where(batch => batch.Entries.All(entry => PathGuard.IsInside(folder, entry.From))).ToList();
    }

    public UndoResult Undo(string batchId)
    {
        lock (_gate) return _undo.Undo(batchId);
    }
}
