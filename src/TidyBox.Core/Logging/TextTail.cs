using System.Text;

namespace TidyBox.Core.Logging;

internal static class TextTail
{
    /// <summary>
    /// Keeps the end of <paramref name="text"/> so that it fits in <paramref name="maxBytes"/> UTF-8 bytes. It starts at the
    /// beginning of a line and never splits a multi-byte character. When a single line is longer than the limit there is
    /// no line to start at, and it is cut at a character boundary instead.
    /// </summary>
    public static string Of(string text, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= maxBytes) return text;

        var start = bytes.Length - maxBytes;
        while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80) start++; // skip the rest of a split character

        if (start > 0 && bytes[start - 1] != (byte)'\n')
        {
            var lineBreak = Array.IndexOf(bytes, (byte)'\n', start);
            // A break that is the very last byte would leave nothing, so only a break with text after it counts.
            if (lineBreak >= 0 && lineBreak + 1 < bytes.Length) start = lineBreak + 1;
        }

        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }
}
