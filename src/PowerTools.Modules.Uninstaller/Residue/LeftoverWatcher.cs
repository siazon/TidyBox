namespace PowerTools.Modules.Uninstaller.Residue;

/// <param name="Initial">Wait before the first check.</param>
/// <param name="Max">The wait between checks grows by <paramref name="Backoff"/> each time, up to this.</param>
/// <param name="MaxDuration">Hard limit for the whole watch, whatever happens.</param>
/// <param name="StableFor">Give up when nothing has changed for this long.</param>
public record WatchOptions(TimeSpan Initial, TimeSpan Max, double Backoff, TimeSpan MaxDuration, TimeSpan StableFor)
{
    /// <summary>About ten checks in a minute, each only asking "does this path still exist?" for the few items on the list.</summary>
    public static WatchOptions Default { get; } = new(
        Initial: TimeSpan.FromSeconds(2), Max: TimeSpan.FromSeconds(10), Backoff: 1.5,
        MaxDuration: TimeSpan.FromSeconds(60), StableFor: TimeSpan.FromSeconds(20));
}

/// <summary>
/// Keeps a leftover list honest after the uninstaller said "done" but is still cleaning up in the background.
/// Deliberately small: it never rescans the disk, it only re-asks whether the listed items still exist, with a
/// growing wait between asks and a hard stop, so it costs next to nothing.
/// </summary>
public static class LeftoverWatcher
{
    /// <summary>Calls <paramref name="onShrunk"/> with what is left each time items have disappeared. Ends when the list is empty, stays unchanged for a while, the time limit passes, or it is cancelled.</summary>
    public static async Task WatchAsync(
        IReadOnlyList<ResidueItem> items,
        Func<ResidueItem, bool> exists,
        Action<IReadOnlyList<ResidueItem>> onShrunk,
        WatchOptions options,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct)
    {
        var remaining = items.ToList();
        var interval = options.Initial;
        var elapsed = TimeSpan.Zero;
        var stable = TimeSpan.Zero;

        while (remaining.Count > 0)
        {
            try
            {
                await delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (ct.IsCancellationRequested) return;
            elapsed += interval;

            var still = remaining.Where(exists).ToList();
            if (still.Count != remaining.Count)
            {
                remaining = still;
                stable = TimeSpan.Zero;
                onShrunk(remaining);
            }
            else
            {
                stable += interval;
            }

            if (elapsed >= options.MaxDuration || stable >= options.StableFor) return;
            interval = Next(interval, options);
        }
    }

    /// <returns>True when the condition became true in time; false at the time limit or on cancellation.</returns>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, WatchOptions options, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        var interval = options.Initial;
        var elapsed = TimeSpan.Zero;

        while (!condition())
        {
            try
            {
                await delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            if (ct.IsCancellationRequested) return false;
            elapsed += interval;
            if (elapsed >= options.MaxDuration) return condition();
            interval = Next(interval, options);
        }

        return true;
    }

    private static TimeSpan Next(TimeSpan interval, WatchOptions options)
    {
        var grown = TimeSpan.FromTicks((long)(interval.Ticks * options.Backoff));
        return grown > options.Max ? options.Max : grown;
    }
}
