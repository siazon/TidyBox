using Windows.Storage;

namespace TidyBox.App;

/// <summary>
/// Whether the developer-only features (for now, the vocab strip) are on. Off by default; the user switches it by tapping the version
/// number on the Settings page, and the choice is remembered between runs in the same place as the theme setting.
/// </summary>
internal static class DeveloperMode
{
    private const string Key = "DeveloperMode";

    private static ApplicationDataContainer Values => ApplicationData.Current.LocalSettings;

    /// <summary>Raised after <see cref="IsEnabled"/> changes, on the thread that changed it (the UI thread).</summary>
    public static event Action? Changed;

    public static bool IsEnabled
    {
        get => Values.Values[Key] is true;
        set
        {
            Values.Values[Key] = value;
            Changed?.Invoke();
        }
    }
}
