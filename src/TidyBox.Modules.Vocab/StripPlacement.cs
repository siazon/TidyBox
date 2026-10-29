namespace TidyBox.Modules.Vocab;

/// <summary>Where the taskbar strip goes: just left of the notification area, as wide as fits.</summary>
public static class StripPlacement
{
    private const double DesiredWidthDip = 560;
    private const double MinWidthDip = 100;
    private const double GapDip = 8;

    /// <param name="trayLeftPx">Left edge of the notification area, in physical pixels from the taskbar's left edge.</param>
    /// <param name="scale">DPI scale, 1.75 for 175 %.</param>
    /// <returns>Left edge and width in physical pixels, or null when there is not enough room (or the input is unusable).</returns>
    public static (int Left, int Width)? Calculate(double trayLeftPx, double scale)
    {
        if (!double.IsFinite(trayLeftPx) || !double.IsFinite(scale) || trayLeftPx < 0 || scale <= 0) return null;

        var right = trayLeftPx - GapDip * scale;
        var width = Math.Min(DesiredWidthDip * scale, right);
        if (width < MinWidthDip * scale) return null;

        return ((int)Math.Round(right - width, MidpointRounding.AwayFromZero), (int)Math.Round(width, MidpointRounding.AwayFromZero));
    }
}
