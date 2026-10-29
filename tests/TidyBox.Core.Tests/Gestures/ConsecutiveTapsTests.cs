using TidyBox.Core.Gestures;

namespace TidyBox.Core.Tests.Gestures;

public class ConsecutiveTapsTests
{
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(3);
    private DateTime _now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private ConsecutiveTaps NewTaps(int required = 5) => new(required, Gap, () => _now);

    private bool TapAfter(ConsecutiveTaps taps, TimeSpan wait)
    {
        _now += wait;
        return taps.Tap();
    }

    [Fact]
    public void FiveQuickTaps_CompleteOnTheFifth_AndOnlyThen()
    {
        var taps = NewTaps();

        var results = Enumerable.Range(0, 5).Select(_ => TapAfter(taps, TimeSpan.FromMilliseconds(200))).ToList();

        Assert.Equal(new[] { false, false, false, false, true }, results);
    }

    [Fact]
    public void Remaining_CountsDown_AndStartsOverAfterACompletedSequence()
    {
        var taps = NewTaps();
        Assert.Equal(5, taps.Remaining);

        TapAfter(taps, TimeSpan.Zero);
        TapAfter(taps, TimeSpan.Zero);
        Assert.Equal(3, taps.Remaining);

        TapAfter(taps, TimeSpan.Zero);
        TapAfter(taps, TimeSpan.Zero);
        Assert.True(TapAfter(taps, TimeSpan.Zero));
        Assert.Equal(5, taps.Remaining);
    }

    [Fact]
    public void ALongPause_StartsTheCountOver()
    {
        var taps = NewTaps();
        TapAfter(taps, TimeSpan.Zero);
        TapAfter(taps, TimeSpan.Zero);
        TapAfter(taps, TimeSpan.Zero);

        // A fourth and fifth tap after a pause only count as the first and second.
        Assert.False(TapAfter(taps, Gap + TimeSpan.FromMilliseconds(1)));
        Assert.False(TapAfter(taps, TimeSpan.Zero));
        Assert.Equal(3, taps.Remaining);
    }

    [Fact]
    public void AGapExactlyAtTheLimit_DoesNotBreakTheSequence()
    {
        var taps = NewTaps(required: 2);
        TapAfter(taps, TimeSpan.Zero);

        Assert.True(TapAfter(taps, Gap));
    }

    [Fact]
    public void AfterCompleting_TheNextFiveTapsCompleteAgain()
    {
        var taps = NewTaps(required: 3);
        for (var i = 0; i < 3; i++) TapAfter(taps, TimeSpan.Zero);

        Assert.Equal(new[] { false, false, true }, Enumerable.Range(0, 3).Select(_ => TapAfter(taps, TimeSpan.Zero)).ToList());
    }

    [Fact]
    public void RequiredBelowOne_Throws() => Assert.Throws<ArgumentOutOfRangeException>(() => NewTaps(required: 0));
}
