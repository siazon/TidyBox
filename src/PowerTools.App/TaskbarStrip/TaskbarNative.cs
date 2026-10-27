using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PowerTools.App.TaskbarStrip;

/// <summary>
/// The Win32 side of showing a window on the taskbar: find the taskbar and the notification area, make our window a
/// child of the taskbar, and place it with native pixel coordinates.
/// </summary>
/// <remarks>
/// Embedding a window in Shell_TrayWnd follows the approach of ANYNC/TaskbarLyrics (MIT). The position is set only
/// with SetWindowPos in taskbar-client physical pixels, never through the Window's own position properties.
/// </remarks>
internal static class TaskbarNative
{
    /// <summary>What the strip needs to know about the taskbar right now.</summary>
    internal readonly record struct Geometry(IntPtr Taskbar, int HeightPx, double TrayLeftPx, double Scale);

    private const int GwlStyle = -16, GwlExStyle = -20;
    private const long WsPopup = 0x80000000L, WsChild = 0x40000000L, WsVisible = 0x10000000L;
    private const long WsCaption = 0xC00000L, WsThickFrame = 0x40000L, WsSysMenu = 0x80000L, WsMinMax = 0x30000L;
    private const long WsExNoActivate = 0x08000000L, WsExToolWindow = 0x80L;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20;
    private const uint GaParent = 1;

    /// <returns>Null when the taskbar or its notification area is not there (Explorer is starting or restarting).</returns>
    public static Geometry? Measure()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return null;

        var tray = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(taskbar, out var bar) || !GetWindowRect(tray, out var notify)) return null;

        var dpi = GetDpiForWindow(taskbar);
        return new Geometry(taskbar, bar.Bottom - bar.Top, notify.Left - bar.Left, dpi > 0 ? dpi / 96.0 : 1.0);
    }

    public static bool IsAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

    /// <summary>
    /// GetParent is not used for the check: while the window still has the popup style it reports the owner, not the
    /// parent, and would wrongly say the embedding failed.
    /// </summary>
    public static bool IsEmbeddedIn(IntPtr hwnd, IntPtr taskbar) => GetAncestor(hwnd, GaParent) == taskbar;

    /// <returns>True when the window is now a child of the taskbar.</returns>
    public static bool Embed(IntPtr hwnd, IntPtr taskbar)
    {
        SetParent(hwnd, taskbar);

        var style = GetLong(hwnd, GwlStyle);
        SetLong(hwnd, GwlStyle, (style & ~(WsPopup | WsCaption | WsThickFrame | WsSysMenu | WsMinMax)) | WsChild | WsVisible);
        var extended = GetLong(hwnd, GwlExStyle);
        SetLong(hwnd, GwlExStyle, (extended & ~WsExToolWindow) | WsExNoActivate);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        return IsEmbeddedIn(hwnd, taskbar);
    }

    /// <param name="slot">Left and width in physical pixels, or null to shrink the window to nothing so it covers no button.</param>
    public static void Place(IntPtr hwnd, int heightPx, (int Left, int Width)? slot)
    {
        if (slot is not { } s)
        {
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoZOrder | SwpNoActivate);
            return;
        }

        SetWindowPos(hwnd, IntPtr.Zero, s.Left, 0, s.Width, heightPx, SwpNoZOrder | SwpNoActivate);
    }

    public static bool TaskbarUsesLightTheme() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    // GetWindowLongPtrW only exists in 64-bit user32.dll; on x86 the same call is GetWindowLongW.
    private static long GetLong(IntPtr hwnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, index).ToInt64() : unchecked((uint)GetWindowLong32(hwnd, index));

    private static void SetLong(IntPtr hwnd, int index, long value)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr(hwnd, index, new IntPtr(value));
        else SetWindowLong32(hwnd, index, unchecked((int)(uint)value));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? name);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
}
