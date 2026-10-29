using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace TidyBox.App;

/// <summary>
/// The caption bar is drawn by Windows and follows the system theme, not the theme chosen inside the app, so a dark app on a light
/// system keeps a white bar. When the user picks Light or Dark the colors are set by hand; "follow system" gives them back to Windows.
/// </summary>
internal static class TitleBarTheme
{
    public static void Apply(Window window, ElementTheme theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var bar = window.AppWindow.TitleBar;
        if (theme == ElementTheme.Default)
        {
            bar.BackgroundColor = bar.ForegroundColor = bar.InactiveBackgroundColor = bar.InactiveForegroundColor = null;
            bar.ButtonBackgroundColor = bar.ButtonForegroundColor = bar.ButtonInactiveBackgroundColor = bar.ButtonInactiveForegroundColor = null;
            bar.ButtonHoverBackgroundColor = bar.ButtonHoverForegroundColor = bar.ButtonPressedBackgroundColor = bar.ButtonPressedForegroundColor = null;
            return;
        }

        // The page background of each theme, so the bar and the content read as one window.
        var dark = theme == ElementTheme.Dark;
        var background = dark ? Rgb(0x20, 0x20, 0x20) : Rgb(0xF3, 0xF3, 0xF3);
        var foreground = dark ? Rgb(0xFF, 0xFF, 0xFF) : Rgb(0x00, 0x00, 0x00);
        var dimmed = dark ? Rgb(0x9D, 0x9D, 0x9D) : Rgb(0x7A, 0x7A, 0x7A);
        var hover = dark ? Rgb(0x33, 0x33, 0x33) : Rgb(0xE5, 0xE5, 0xE5);
        var pressed = dark ? Rgb(0x3D, 0x3D, 0x3D) : Rgb(0xCC, 0xCC, 0xCC);

        bar.BackgroundColor = bar.InactiveBackgroundColor = bar.ButtonBackgroundColor = bar.ButtonInactiveBackgroundColor = background;
        bar.ForegroundColor = bar.ButtonForegroundColor = foreground;
        bar.InactiveForegroundColor = bar.ButtonInactiveForegroundColor = dimmed;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = foreground;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);
}
