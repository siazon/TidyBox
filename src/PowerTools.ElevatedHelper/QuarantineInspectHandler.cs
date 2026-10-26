using System.Text.Json;
using PowerTools.Core.Elevation;

namespace PowerTools.ElevatedHelper;

public record QuarantineSummary(long TotalBytes, int BatchCount);

/// <summary>
/// Reports how much the quarantine folder is holding, so the app can show "X GB from past cleanups, want to
/// clear it?" without guessing. Read-only: never deletes or moves anything.
/// </summary>
public sealed class QuarantineInspectHandler : IElevatedOperationHandler
{
    private readonly string _quarantineRoot;

    public QuarantineInspectHandler(string? quarantineRoot = null) => _quarantineRoot = quarantineRoot ?? QuarantinePaths.DefaultRoot();

    public string OperationType => "quarantine.inspect";

    public Task<ElevationResponse> HandleAsync(string payloadJson)
    {
        if (!Directory.Exists(_quarantineRoot))
        {
            return Task.FromResult(Ok(new QuarantineSummary(0, 0)));
        }

        long total = 0;
        var batches = 0;
        foreach (var batch in SafeDirectories(_quarantineRoot))
        {
            batches++;
            total += MeasureSize(batch);
        }

        return Task.FromResult(Ok(new QuarantineSummary(total, batches)));
    }

    private static ElevationResponse Ok(QuarantineSummary summary) => new(true, null, JsonSerializer.Serialize(summary));

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try
        {
            return Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
    }

    private static long MeasureSize(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        long total = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
        {
            try
            {
                total += file.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return total;
    }
}
