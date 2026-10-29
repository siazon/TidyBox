using TidyBox.Modules.Uninstaller.Residue;

namespace TidyBox.Modules.Uninstaller.Tests.Residue;

public class LeftoverWatcherTests
{
    private sealed class FakeClock
    {
        public List<TimeSpan> Delays = new();
        public TimeSpan Elapsed => TimeSpan.FromTicks(Delays.Sum(d => d.Ticks));
        public Action<int>? OnTick;

        public Task Delay(TimeSpan span, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Delays.Add(span);
            OnTick?.Invoke(Delays.Count);
            return Task.CompletedTask;
        }
    }

    private static ResidueItem Item(string path)
        => new(ResidueKind.Folder, path, ResidueConfidence.Medium, "test", false, true);

    private static readonly WatchOptions Options = new(
        Initial: TimeSpan.FromSeconds(2), Max: TimeSpan.FromSeconds(10), Backoff: 1.5,
        MaxDuration: TimeSpan.FromSeconds(60), StableFor: TimeSpan.FromSeconds(20));

    [Fact]
    public async Task Reports_the_shrinking_list_and_stops_once_nothing_is_left()
    {
        var clock = new FakeClock();
        var gone = new HashSet<string>();
        clock.OnTick = tick =>
        {
            if (tick == 1) gone.Add("a");
            if (tick == 3) gone.Add("b");
        };
        var reported = new List<string[]>();

        await LeftoverWatcher.WatchAsync(new[] { Item("a"), Item("b") }, item => !gone.Contains(item.Path),
            remaining => reported.Add(remaining.Select(i => i.Path).ToArray()), Options, clock.Delay, CancellationToken.None);

        Assert.Equal(new[] { new[] { "b" }, Array.Empty<string>() }, reported);
        Assert.Equal(3, clock.Delays.Count);
    }

    [Fact]
    public async Task Says_nothing_and_gives_up_when_the_list_stays_the_same()
    {
        var clock = new FakeClock();
        var calls = 0;

        await LeftoverWatcher.WatchAsync(new[] { Item("a") }, _ => true, _ => calls++, Options, clock.Delay, CancellationToken.None);

        Assert.Equal(0, calls);
        // 2 + 3 + 4.5 + 6.75 + 10 = 26.25 s: the first tick at which 20 s have passed without a change.
        Assert.Equal(TimeSpan.FromSeconds(26.25), clock.Elapsed);
    }

    [Fact]
    public async Task The_wait_between_checks_grows_and_is_capped()
    {
        var clock = new FakeClock();
        clock.OnTick = tick => { };
        var options = Options with { StableFor = TimeSpan.FromSeconds(55) };

        await LeftoverWatcher.WatchAsync(new[] { Item("a") }, _ => true, _ => { }, options, clock.Delay, CancellationToken.None);

        Assert.Equal(new[] { 2, 3, 4.5, 6.75, 10, 10, 10, 10 }, clock.Delays.Select(d => d.TotalSeconds));
    }

    [Fact]
    public async Task Never_runs_longer_than_the_maximum_even_when_things_keep_changing()
    {
        var clock = new FakeClock();
        var items = Enumerable.Range(0, 500).Select(i => Item("item" + i)).ToArray();
        var gone = new HashSet<string>();
        clock.OnTick = tick => gone.Add("item" + tick);

        await LeftoverWatcher.WatchAsync(items, item => !gone.Contains(item.Path), _ => { }, Options, clock.Delay, CancellationToken.None);

        Assert.InRange(clock.Elapsed.TotalSeconds, 60, 70);
        Assert.InRange(clock.Delays.Count, 1, 30);
    }

    [Fact]
    public async Task Each_check_looks_at_every_remaining_item_exactly_once()
    {
        var clock = new FakeClock();
        var checks = 0;

        await LeftoverWatcher.WatchAsync(new[] { Item("a"), Item("b"), Item("c") }, _ => { checks++; return true; }, _ => { }, Options, clock.Delay, CancellationToken.None);

        Assert.Equal(3 * clock.Delays.Count, checks);
    }

    [Fact]
    public async Task Cancelling_ends_the_watch_quietly()
    {
        var clock = new FakeClock();
        using var cts = new CancellationTokenSource();
        clock.OnTick = tick => { if (tick == 2) cts.Cancel(); };
        var calls = 0;

        await LeftoverWatcher.WatchAsync(new[] { Item("a") }, _ => true, _ => calls++, Options, clock.Delay, cts.Token);

        Assert.Equal(2, clock.Delays.Count);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task WaitUntil_returns_true_as_soon_as_the_condition_holds()
    {
        var clock = new FakeClock();

        var met = await LeftoverWatcher.WaitUntilAsync(() => clock.Delays.Count >= 3, Options, clock.Delay, CancellationToken.None);

        Assert.True(met);
        Assert.Equal(3, clock.Delays.Count);
    }

    [Fact]
    public async Task WaitUntil_gives_up_at_the_maximum_duration()
    {
        var clock = new FakeClock();

        var met = await LeftoverWatcher.WaitUntilAsync(() => false, Options, clock.Delay, CancellationToken.None);

        Assert.False(met);
        Assert.InRange(clock.Elapsed.TotalSeconds, 60, 70);
    }

    [Fact]
    public async Task WaitUntil_returns_false_when_cancelled()
    {
        var clock = new FakeClock();
        using var cts = new CancellationTokenSource();
        clock.OnTick = tick => { if (tick == 1) cts.Cancel(); };

        Assert.False(await LeftoverWatcher.WaitUntilAsync(() => false, Options, clock.Delay, cts.Token));
    }
}
