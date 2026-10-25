using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerTools.Modules.DiskAnalyzer.Steam;

/// <summary>
/// Folder names under steamapps/common that Valve or a shared runtime puts there, which are not themselves a
/// game and must never be flagged as an orphan install. Maintained offline (seeded/reviewed with LLM assistance)
/// and shipped as an embedded resource, not fetched at runtime.
/// </summary>
public static class KnownSharedFolders
{
    private static readonly Regex NonAlphanumeric = new(@"[^a-z0-9]", RegexOptions.Compiled);

    /// <summary>Lower-case letters and digits only, so "SteamVR" and "Steam VR" match the same entry.</summary>
    public static string Normalize(string? text) => NonAlphanumeric.Replace((text ?? "").ToLowerInvariant(), "");

    /// <returns>The normalized names of all known shared/non-game folders.</returns>
    public static IReadOnlySet<string> Load()
    {
        using var stream = typeof(KnownSharedFolders).Assembly
            .GetManifestResourceStream("PowerTools.Modules.DiskAnalyzer.Steam.KnownSharedFolders.json")
            ?? throw new InvalidOperationException("KnownSharedFolders.json is missing as an embedded resource.");

        var names = JsonSerializer.Deserialize<string[]>(stream) ?? Array.Empty<string>();
        return names.Select(Normalize).ToHashSet();
    }
}
