using TidyBox.Modules.Downloads;
using Windows.Storage;

namespace TidyBox.App;

/// <summary>The thresholds the user set for the tidy page, remembered between runs in the same place as the theme setting.</summary>
internal static class TidyOptionsSettings
{
    private const string DaysKey = "Tidy.StaleDays";
    private const string MinutesKey = "Tidy.RecentMinutes";

    private static ApplicationDataContainer Values => ApplicationData.Current.LocalSettings;

    /// <summary>What was saved, or the defaults when nothing was or a value is out of range.</summary>
    public static TidyOptions Current
    {
        get
        {
            try
            {
                return TidyOptions.Create(
                    Values.Values[DaysKey] as int? ?? TidyOptions.Default.StaleDays, Values.Values[MinutesKey] as int? ?? TidyOptions.Default.RecentMinutes);
            }
            catch (ArgumentOutOfRangeException)
            {
                return TidyOptions.Default;
            }
        }
    }

    public static void Save(TidyOptions options)
    {
        Values.Values[DaysKey] = options.StaleDays;
        Values.Values[MinutesKey] = options.RecentMinutes;
    }
}
