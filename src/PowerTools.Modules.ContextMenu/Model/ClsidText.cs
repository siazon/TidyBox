namespace PowerTools.Modules.ContextMenu.Model;

internal static class ClsidText
{
    /// <summary>The CLSID in the canonical <c>{UPPERCASE-GUID}</c> form, or null when the text is not a braced GUID.</summary>
    public static string? Normalize(string? raw)
        => raw is not null && Guid.TryParseExact(raw.Trim(), "B", out var guid)
            ? "{" + guid.ToString("D").ToUpperInvariant() + "}"
            : null;
}
