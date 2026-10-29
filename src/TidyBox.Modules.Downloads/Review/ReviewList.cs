using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Review;

/// <param name="Stale">Not touched for more than 90 days and not kept; largest first.</param>
/// <param name="Kept">The files currently on disk that the user chose to keep.</param>
public sealed record ReviewResult(IReadOnlyList<DownloadFile> Stale, IReadOnlyList<DownloadFile> Kept);

public static class ReviewList
{
    public static ReviewResult Build(IEnumerable<DownloadFile> files, KeptStore kept, DateTime nowUtc)
    {
        var all = files.ToList();
        var keptFiles = all.Where(kept.IsKept).ToList();
        var stale = all.Where(file => !kept.IsKept(file) && FileAge.IsStale(file, nowUtc)).OrderByDescending(file => file.SizeBytes).ToList();
        return new ReviewResult(stale, keptFiles);
    }
}
