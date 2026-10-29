using TidyBox.Modules.Downloads.Scanning;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.Modules.Downloads;

/// <param name="TotalBytes">The size of the files this scan considered (top-level files and those inside the category folders, minus the ones the safety filter held back); folders are not counted.</param>
/// <param name="StaleBytes">The size of <paramref name="Stale"/>.</param>
/// <param name="Proposals">One per top-level file: what tidying would do with it.</param>
/// <param name="Stale">Untouched for more than 90 days and not kept; largest first.</param>
/// <param name="Kept">Files the user chose to keep.</param>
/// <param name="SourceDomains">Where the files shown came from, by full path; only files with a known domain appear.</param>
public sealed record DownloadsReport(
    string Folder,
    long TotalBytes,
    long StaleBytes,
    IReadOnlyList<Proposal> Proposals,
    IReadOnlyList<DownloadFile> Stale,
    IReadOnlyList<DownloadFile> Kept,
    IReadOnlyDictionary<string, string> SourceDomains);
