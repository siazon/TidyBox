using System.Text.Json;

namespace PowerTools.Modules.DiskAnalyzer.Steam;

/// <summary>
/// Folder names under steamapps/common that Valve or a shared runtime puts there, which are not themselves a
/// game and must never be flagged as an orphan install. Maintained offline (seeded/reviewed with LLM assistance)
/// and shipped as an embedded resource, not fetched at runtime.
/// </summary>
public static class KnownSharedFolders
{
    // Only case is folded, nothing is stripped: a folder a user copied (Explorer appends " - 副本" / " - Copy" /
    // "(2)") must end up as a *different* string from the original, or the copy would be silently treated as the
    // legitimate shared folder and never show up as an orphan.
    public static string Normalize(string? text) => (text ?? "").Trim().ToLowerInvariant();

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
