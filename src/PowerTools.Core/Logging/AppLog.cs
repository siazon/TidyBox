using System.Security;
using System.Text;

namespace PowerTools.Core.Logging;

/// <summary>
/// A small log file for the app's own trouble (start-up, unhandled exceptions, modules that failed). It is what the
/// feedback form attaches to an error report, so it only ever holds what the app chose to write.
/// </summary>
/// <remarks>
/// Logging must never be the reason something fails: every file problem is swallowed. Each entry is written straight to
/// the file, so it is there even if the app dies a moment later.
/// </remarks>
public sealed class AppLog
{
    private const string FileName = "powertools.log";
    private const string OldFileName = "powertools.1.log";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly long _maxFileBytes;

    public AppLog(string directory, long maxFileBytes = 512 * 1024)
    {
        LogDirectory = directory;
        _maxFileBytes = maxFileBytes;
    }

    /// <summary>%LOCALAPPDATA%\PowerTools\logs</summary>
    public static AppLog Default { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerTools", "logs"));

    public string LogDirectory { get; }

    public void Info(string area, string message) => Write("INFO", area, message, null);

    public void Error(string area, string message, Exception? exception = null) => Write("ERROR", area, message, exception);

    /// <returns>
    /// The newest log text, the rolled-over file before the current one, at most <paramref name="maxBytes"/> UTF-8 bytes
    /// and starting at a whole line. Empty when there is no log.
    /// </returns>
    public string ReadRecent(int maxBytes)
    {
        lock (_gate)
        {
            try
            {
                var all = new StringBuilder();
                foreach (var name in new[] { OldFileName, FileName })
                {
                    var path = Path.Combine(LogDirectory, name);
                    if (File.Exists(path)) all.Append(File.ReadAllText(path, Utf8));
                }

                return TextTail.Of(all.ToString(), maxBytes);
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                return string.Empty;
            }
        }
    }

    private void Write(string level, string area, string message, Exception? exception)
    {
        // Lines after the first are indented, so every entry starts at a line beginning with its timestamp.
        var entry = new StringBuilder($"{DateTimeOffset.Now:o} [{level}] {area}: {Indent(message, first: false)}\n");
        if (exception is not null) entry.Append(Indent(exception.ToString(), first: true)).Append('\n');

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var path = Path.Combine(LogDirectory, FileName);
                if (File.Exists(path) && new FileInfo(path).Length >= _maxFileBytes)
                {
                    File.Move(path, Path.Combine(LogDirectory, OldFileName), overwrite: true);
                }

                File.AppendAllText(path, entry.ToString(), Utf8);
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                // Nowhere to write it; losing the line is better than failing the caller.
            }
        }
    }

    private static string Indent(string text, bool first)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Select((line, i) => i == 0 && !first ? line : "  " + line));
    }

    private static bool IsFileProblem(Exception ex)
        => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;
}
