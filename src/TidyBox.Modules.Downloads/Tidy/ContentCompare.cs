using System.Security.Cryptography;

namespace TidyBox.Modules.Downloads.Tidy;

internal static class ContentCompare
{
    /// <summary>
    /// True when both files exist and hold the same bytes. Sizes are compared first and the files are only hashed when they match.
    /// A file that cannot be read counts as different: that errs towards a suffix or a refusal, never towards a recycle.
    /// </summary>
    public static bool AreSame(string pathA, string pathB)
    {
        try
        {
            var a = new FileInfo(pathA);
            var b = new FileInfo(pathB);
            if (!a.Exists || !b.Exists || a.Length != b.Length) return false;

            using var first = a.OpenRead();
            using var second = b.OpenRead();
            return SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
