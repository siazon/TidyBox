using TidyBox.Core.Logging;

namespace TidyBox.Core.Feedback;

/// <summary>The same limits the server enforces, so the client can stay inside them instead of being rejected.</summary>
public static class FeedbackLimits
{
    public const int MaxMessageChars = 4000;
    public const int MaxContactChars = 200;
    public const int MaxLogBytes = 65536;

    /// <summary>The most recent part of the log that fits in <see cref="MaxLogBytes"/>, cut at a line start where there is one.</summary>
    public static string TrimLogs(string logs) => TextTail.Of(logs, MaxLogBytes);
}
