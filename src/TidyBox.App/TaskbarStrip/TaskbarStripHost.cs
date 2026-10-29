using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using TidyBox.Core.Logging;
using TidyBox.Modules.Vocab;

namespace TidyBox.App.TaskbarStrip;

/// <summary>
/// Shows the vocab strip on the taskbar and keeps it there. Lives for the whole app run, independent of any page:
/// pages come and go as the user navigates, the strip must not.
/// </summary>
/// <remarks>Must be used from the UI thread.</remarks>
internal sealed class TaskbarStripHost
{
    private static readonly TimeSpan WordInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(2);
    private const int MaxFailedReopens = 3;

    private readonly VocabModule _module;
    private TaskbarStripWindow? _window;
    private IntPtr _hwnd;
    private WordCard? _current;
    private bool _active;
    private int _failedReopens;
    private bool? _lightTaskbar;
    private (int Height, (int Left, int Width)? Slot)? _lastPlacement;
    private DispatcherQueueTimer? _wordTimer;
    private DispatcherQueueTimer? _maintenanceTimer;

    public TaskbarStripHost(VocabModule module) => _module = module;

    public bool IsShowing => _active;

    /// <summary>Why the last <see cref="Show"/> failed; null otherwise.</summary>
    public string? Error { get; private set; }

    /// <returns>False (with <see cref="Error"/> set and no window left behind) when the strip could not be shown.</returns>
    public bool Show()
    {
        if (_active) return true;

        Error = null;
        _failedReopens = 0;
        _current = _module.Next();
        if (_current is null)
        {
            Error = "没有可显示的单词";
            return false;
        }

        if (!Open()) return false;

        _active = true;
        var queue = DispatcherQueue.GetForCurrentThread();
        _wordTimer = queue.CreateTimer();
        _wordTimer.Interval = WordInterval;
        _wordTimer.Tick += (_, _) => ShowNextWord();
        _wordTimer.Start();
        _maintenanceTimer = queue.CreateTimer();
        _maintenanceTimer.Interval = MaintenanceInterval;
        _maintenanceTimer.Tick += (_, _) => Maintain();
        _maintenanceTimer.Start();
        return true;
    }

    public void Hide()
    {
        _active = false;
        _wordTimer?.Stop();
        _maintenanceTimer?.Stop();
        _wordTimer = null;
        _maintenanceTimer = null;
        CloseWindow();
    }

    private void ShowNextWord()
    {
        if (_module.Next() is not { } card) return;

        _current = card;
        _window?.SetCard(card);
    }

    // Creates the window and embeds it. On failure nothing is left behind.
    private bool Open()
    {
        // Creating and embedding the window leaves the focus with it; remember who had it to hand it back.
        var previousForeground = TaskbarNative.Foreground();
        var window = new TaskbarStripWindow();
        window.SetCard(_current!);
        window.AppWindow.Show(activateWindow: false);

        var geometry = TaskbarNative.Measure();
        var hwnd = window.Hwnd;
        var embedded = geometry is not null && TaskbarNative.Embed(hwnd, geometry.Value.Taskbar);
        TaskbarNative.RestoreForeground(previousForeground);
        if (!embedded)
        {
            Error = "无法嵌入任务栏";
            AppLog.Default.Error("vocab-strip", "could not embed the strip in the taskbar");
            CloseWindow(window);
            return false;
        }

        _window = window;
        _hwnd = hwnd;
        _lightTaskbar = null;
        _lastPlacement = null;
        Layout(geometry!.Value); // not null: embedding only succeeds with a measured taskbar
        return true;
    }

    // Icons and the notification area change, the DPI can change, and Explorer can restart (which destroys the taskbar
    // and with it our child window). Nothing here throws: if the taskbar is not there yet, the next tick tries again.
    private void Maintain()
    {
        if (!_active) return;

        if (_window is null || !TaskbarNative.IsAlive(_hwnd))
        {
            CloseWindow();
            if (TaskbarNative.Measure() is null) return; // Explorer is not back yet: wait, this is not a failure

            if (Open()) _failedReopens = 0;
            else if (++_failedReopens >= MaxFailedReopens) Hide(); // stop for good instead of retrying forever; Error says why
            return;
        }

        if (TaskbarNative.Measure() is not { } geometry)
        {
            TaskbarNative.Place(_hwnd, 0, null);
            _lastPlacement = null;
            return;
        }

        if (!TaskbarNative.IsEmbeddedIn(_hwnd, geometry.Taskbar) && !TaskbarNative.Embed(_hwnd, geometry.Taskbar)) return;

        Layout(geometry);
    }

    private void Layout(TaskbarNative.Geometry geometry)
    {
        var slot = StripPlacement.Calculate(geometry.TrayLeftPx, geometry.Scale);
        if (_lastPlacement != (geometry.HeightPx, slot))
        {
            TaskbarNative.Place(_hwnd, geometry.HeightPx, slot);
            _lastPlacement = (geometry.HeightPx, slot);
        }

        var light = TaskbarNative.TaskbarUsesLightTheme();
        if (_lightTaskbar != light)
        {
            _window?.SetTextColor(light);
            _lightTaskbar = light;
        }
    }

    private void CloseWindow() => CloseWindow(_window);

    private void CloseWindow(TaskbarStripWindow? window)
    {
        if (ReferenceEquals(window, _window))
        {
            _window = null;
            _hwnd = IntPtr.Zero;
        }

        try
        {
            window?.Close();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // Already gone with the taskbar that was its parent.
        }
    }
}
