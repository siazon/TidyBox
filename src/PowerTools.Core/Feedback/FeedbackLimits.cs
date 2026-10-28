using System.Text;

namespace PowerTools.Core.Feedback;

/// <summary>The same limits the server enforces, so the client can stay inside them instead of being rejected.</summary>
public static class FeedbackLimits
{
    public const int MaxMessageChars = 4000;
    public const int MaxContactChars = 200;
    public const int MaxLogBytes = 65536;

    /// <summary>
    /// Keeps the end of the log (the most recent lines) so that it fits in <see cref="MaxLogBytes"/> UTF-8 bytes. It starts
    /// at the beginning of a line and never splits a multi-byte character. When a single line is longer than the limit
    /// there is no line to start at, and it is cut at a character boundary instead.
    /// </summary>
    public static string TrimLogs(string logs)
    {
        var bytes = Encoding.UTF8.GetBytes(logs);
        if (bytes.Length <= MaxLogBytes) return logs;

        var start = bytes.Length - MaxLogBytes;
        while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80) start++; // skip the rest of a split character

        if (bytes[start - 1] != (byte)'\n')
        {
            var lineBreak = Array.IndexOf(bytes, (byte)'\n', start);
            // A break that is the very last byte would leave nothing, so only a break with text after it counts.
            if (lineBreak >= 0 && lineBreak + 1 < bytes.Length) start = lineBreak + 1;
        }

        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }
}
