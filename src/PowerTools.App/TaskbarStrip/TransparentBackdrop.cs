using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace PowerTools.App.TaskbarStrip;

/// <summary>
/// A WinUI 3 window has no "allow transparency" switch; its default background is opaque. A backdrop that paints a
/// fully transparent colour lets the taskbar show through. The backdrop takes a Windows.UI.Composition brush, so it needs
/// the system Compositor, and that needs a Windows.System DispatcherQueue on the thread (WinUI 3 only has its own
/// Microsoft.UI one). Without that queue creating the Compositor fails and the process dies with 0xC000027B.
/// </summary>
internal sealed partial class TransparentBackdrop : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? _compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        EnsureSystemDispatcherQueue();
        _compositor = new Windows.UI.Composition.Compositor();
        connectedTarget.SystemBackdrop = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        disconnectedTarget.SystemBackdrop = null;
        _compositor?.Dispose();
        _compositor = null;
    }

    private static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;

        var options = new DispatcherQueueOptions { Size = Marshal.SizeOf<DispatcherQueueOptions>(), ThreadType = 2 /* current thread */, ApartmentType = 0 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
}
