namespace PowerTools.Core.Launch;

/// <summary>What the command line asks the app to do when it starts.</summary>
public static class LaunchRequest
{
    public const string AnalyzeSwitch = "--analyze";

    /// <param name="args">As returned by <see cref="Environment.GetCommandLineArgs"/>: the program itself comes first.</param>
    /// <returns>The folder to analyze, as a clean full path, or null when there is none.</returns>
    public static string? GetAnalyzePath(IReadOnlyList<string> args)
    {
        for (var i = 1; i < args.Count - 1; i++)
        {
            if (!args[i].Equals(AnalyzeSwitch, StringComparison.OrdinalIgnoreCase)) continue;

            var value = args[i + 1];
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)) return null;

            try
            {
                return Path.GetFullPath(value);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        return null;
    }
}
