namespace TidyBox.Modules.Downloads.Source;

public static class SourceReader
{
    /// <summary>The site the file was downloaded from, or null when it has no Zone.Identifier stream, the stream has no usable address, or it cannot be read.</summary>
    public static string? TryGetDomain(string filePath)
    {
        try
        {
            return ZoneIdentifierParser.ParseDomain(File.ReadAllText(filePath + ":Zone.Identifier"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // No such stream (the usual case), or a drive that has no streams.
            return null;
        }
    }
}
