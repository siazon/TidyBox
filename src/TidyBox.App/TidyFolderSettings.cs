using Windows.Storage;

namespace TidyBox.App;

/// <summary>The folder the tidy page works on, remembered between runs in the same place as the theme setting. Nothing stored means the Downloads folder.</summary>
internal static class TidyFolderSettings
{
    private const string PathKey = "Tidy.Folder";

    private static ApplicationDataContainer Values => ApplicationData.Current.LocalSettings;

    /// <summary>The folder the user picked, or null when they use the default. The folder itself is not copied.</summary>
    public static string? Path
    {
        get => Values.Values[PathKey] as string;
        set
        {
            if (value is null) Values.Values.Remove(PathKey);
            else Values.Values[PathKey] = value;
        }
    }

    /// <summary>The remembered folder, unless it has gone since: then the default applies again.</summary>
    public static string? ExistingPath => Path is { Length: > 0 } saved && Directory.Exists(saved) ? saved : null;
}
