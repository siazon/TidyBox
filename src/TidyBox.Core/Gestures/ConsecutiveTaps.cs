namespace TidyBox.Core.Gestures;

/// <summary>Counts taps that follow one another closely, like the "tap the version number five times" gesture. A pause longer than the allowed gap starts the count over.</summary>
public sealed class ConsecutiveTaps
{
    private readonly int _required;
    private readonly TimeSpan _maxGap;
    private readonly Func<DateTime> _clock;
    private int _count;
    private DateTime _lastTap;

    public ConsecutiveTaps(int required, TimeSpan maxGap, Func<DateTime> clock)
    {
        if (required < 1) throw new ArgumentOutOfRangeException(nameof(required), required, "At least one tap is needed.");

        _required = required;
        _maxGap = maxGap;
        _clock = clock;
    }

    /// <summary>How many more taps would complete the sequence, counting from the current state.</summary>
    public int Remaining => _required - _count;

    /// <returns>True when this tap completed the sequence; the count then starts over.</returns>
    public bool Tap()
    {
        var now = _clock();
        if (_count > 0 && now - _lastTap > _maxGap) _count = 0;

        _lastTap = now;
        if (++_count < _required) return false;

        _count = 0;
        return true;
    }
}
