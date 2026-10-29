namespace TidyBox.Modules.Downloads;

/// <summary>The two thresholds the user can set. <see cref="Default"/> is 90 days and 5 minutes.</summary>
public sealed class TidyOptions
{
    public const int MaxStaleDays = 3650;
    public const int MaxRecentMinutes = 1440;

    private TidyOptions(int staleDays, int recentMinutes)
    {
        StaleDays = staleDays;
        RecentMinutes = recentMinutes;
    }

    public static TidyOptions Default { get; } = new(90, 5);

    /// <summary>A file untouched for longer than this many days shows up in the review.</summary>
    public int StaleDays { get; }

    /// <summary>A file written within this many minutes is left alone, so a download that is still arriving is not touched. Zero turns the wait off; a file in use is still skipped.</summary>
    public int RecentMinutes { get; }

    public TimeSpan StaleAfter => TimeSpan.FromDays(StaleDays);

    public TimeSpan RecentWindow => TimeSpan.FromMinutes(RecentMinutes);

    /// <exception cref="ArgumentOutOfRangeException">Days outside 1 to 3650, or minutes outside 0 to 1440.</exception>
    public static TidyOptions Create(int staleDays, int recentMinutes)
    {
        if (staleDays is < 1 or > MaxStaleDays) throw new ArgumentOutOfRangeException(nameof(staleDays), staleDays, $"Days must be between 1 and {MaxStaleDays}.");
        if (recentMinutes is < 0 or > MaxRecentMinutes) throw new ArgumentOutOfRangeException(nameof(recentMinutes), recentMinutes, $"Minutes must be between 0 and {MaxRecentMinutes}.");
        return new TidyOptions(staleDays, recentMinutes);
    }
}
