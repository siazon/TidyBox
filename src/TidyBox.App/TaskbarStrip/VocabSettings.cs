using Windows.Storage;

namespace TidyBox.App.TaskbarStrip;

/// <summary>What the vocab page remembers between runs, in the same place as the theme setting.</summary>
internal static class VocabSettings
{
    private const string PathKey = "Vocab.Path";
    private const string EnabledKey = "Vocab.Enabled";

    private static ApplicationDataContainer Values => ApplicationData.Current.LocalSettings;

    /// <summary>The word-list file the user picked; only a file that loaded is stored. The file itself is not copied.</summary>
    public static string? Path
    {
        get => Values.Values[PathKey] as string;
        set => Values.Values[PathKey] = value;
    }

    /// <summary>Whether the strip was showing when the app last ran.</summary>
    public static bool Enabled
    {
        get => Values.Values[EnabledKey] is true;
        set => Values.Values[EnabledKey] = value;
    }
}
