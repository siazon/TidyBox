using TidyBox.Modules.ContextMenu.Model;

namespace TidyBox.Modules.ContextMenu.Catalog;

public static class MenuEntryFilter
{
    /// <summary>
    /// True when every whitespace-separated word of <paramref name="query"/> occurs (case-insensitively) in the entry's
    /// name, key name, publisher, module path, CLSID or location name. An empty query matches everything.
    /// </summary>
    public static bool Matches(MenuEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        var fields = new[]
        {
            entry.DisplayName, entry.KeyName, entry.Publisher, entry.ModulePath, entry.Clsid, entry.Location.DisplayName(),
        };

        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => fields.Any(field => field?.Contains(word, StringComparison.OrdinalIgnoreCase) == true));
    }
}
