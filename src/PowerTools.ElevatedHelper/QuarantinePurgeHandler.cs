using PowerTools.Core.Elevation;

namespace PowerTools.ElevatedHelper;

/// <summary>
/// Really deletes everything in the quarantine folder (not another move — this is the user's explicit "empty it"
/// confirmation). Never silent/automatic: only runs when the app asks for it, after the user confirms in the UI.
/// </summary>
public sealed class QuarantinePurgeHandler : IElevatedOperationHandler
{
    private readonly string _quarantineRoot;
    private readonly QuarantineTrust _trust;

    public QuarantinePurgeHandler(string? quarantineRoot = null, QuarantineTrust? trust = null)
    {
        _quarantineRoot = quarantineRoot ?? QuarantinePaths.DefaultRoot();
        _trust = trust ?? QuarantineTrust.Machine;
    }

    public string OperationType => "quarantine.purge";

    public Task<ElevationResponse> HandleAsync(string payloadJson)
    {
        if (!Directory.Exists(_quarantineRoot))
        {
            return Task.FromResult(new ElevationResponse(true, null, null));
        }

        // Same ownership check as removing a residual: whoever controls this folder could have swapped a batch
        // for a link pointing somewhere else, and a recursive delete would follow it there.
        var problem = QuarantineFolder.Secure(_quarantineRoot, _trust);
        if (problem is not null)
        {
            return Task.FromResult(new ElevationResponse(false, problem, null));
        }

        var errors = new List<string>();
        foreach (var batch in SafeDirectories(_quarantineRoot))
        {
            try
            {
                if (new DirectoryInfo(batch).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Directory.Delete(batch); // a link: remove the link itself, never follow it
                }
                else
                {
                    Directory.Delete(batch, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{batch}: {ex.Message}");
            }
        }

        return Task.FromResult(errors.Count == 0
            ? new ElevationResponse(true, null, null)
            : new ElevationResponse(false, string.Join("; ", errors), null));
    }

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
}
