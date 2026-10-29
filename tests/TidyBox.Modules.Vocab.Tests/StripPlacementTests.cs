namespace TidyBox.Modules.Vocab.Tests;

public class StripPlacementTests
{
    [Fact]
    public void Roomy_taskbar_places_the_full_width_window_just_left_of_the_tray()
    {
        // Measured by the spike on a 175 % scaled 4K screen: tray starts at 3336 px, window 560 DIP wide, 8 DIP gap.
        Assert.Equal<(int, int)?>((2342, 980), StripPlacement.Calculate(3336, 1.75));
    }

    [Fact]
    public void A_narrow_gap_shrinks_the_window()
    {
        Assert.Equal<(int, int)?>((0, 492), StripPlacement.Calculate(500, 1.0));
    }

    [Fact]
    public void Exactly_the_minimum_width_still_fits()
    {
        Assert.Equal<(int, int)?>((0, 100), StripPlacement.Calculate(108, 1.0));
    }

    [Theory]
    [InlineData(107, 1.0)]
    [InlineData(0, 1.0)]
    public void Less_than_the_minimum_returns_null(double trayLeft, double scale)
    {
        Assert.Null(StripPlacement.Calculate(trayLeft, scale));
    }

    [Theory]
    [InlineData(3336, 0)]
    [InlineData(3336, -1)]
    [InlineData(3336, double.NaN)]
    [InlineData(-5, 1.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void Invalid_input_returns_null(double trayLeft, double scale)
    {
        Assert.Null(StripPlacement.Calculate(trayLeft, scale));
    }
}
