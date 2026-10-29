namespace TidyBox.Modules.Downloads.Scanning;

/// <param name="CategoryFolder">Null for a file directly in the downloads folder; otherwise the name of the category folder it sits in.</param>
public sealed record DownloadFile(string FullPath, long SizeBytes, DateTime CreatedUtc, DateTime ModifiedUtc, string? CategoryFolder)
{
    public string Name => Path.GetFileName(FullPath);
}
