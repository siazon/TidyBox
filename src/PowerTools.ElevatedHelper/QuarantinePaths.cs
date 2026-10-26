namespace PowerTools.ElevatedHelper;

/// <summary>Shared default location for the quarantine folder, used by the inspect/purge operations.</summary>
internal static class QuarantinePaths
{
    public static string DefaultRoot()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PowerTools", "Quarantine");
}
