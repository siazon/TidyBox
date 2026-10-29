namespace TidyBox.Modules.Downloads.Scanning;

/// <summary>How long a file has sat untouched. Last-access time is not used: it is unreliable, and Windows may not update it.</summary>
public static class FileAge
{
    /// <summary>The later of created and modified: a file extracted from an archive keeps the old modified time inside it, but it only arrived now.</summary>
    public static DateTime Basis(DownloadFile file) => file.CreatedUtc > file.ModifiedUtc ? file.CreatedUtc : file.ModifiedUtc;

    public static bool IsStale(DownloadFile file, DateTime nowUtc, TimeSpan staleAfter) => nowUtc - Basis(file) > staleAfter;

    public static int AgeDays(DownloadFile file, DateTime nowUtc) => (int)(nowUtc - Basis(file)).TotalDays;
}
