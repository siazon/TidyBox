using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using PowerTools.Core.Launch;
using PowerTools.Core.Logging;
using PowerTools.Core.Modules;
using PowerTools.Modules.ContextMenu;
using PowerTools.Modules.DiskAnalyzer;
using PowerTools.Modules.Uninstaller;
using PowerTools.Modules.Vocab;
using PowerTools.App.TaskbarStrip;

namespace PowerTools.App;

public partial class App : Application
{
    private const int AsfwAny = -1; // AllowSetForegroundWindow: any process may take the foreground

    private Window? _window;
    private AnalyzeRequestListener? _analyzeRequests;
    private SingleInstanceGuard? _singleInstance; // held for the life of the process: it is what makes this copy "the" running one

    public static ModuleRegistry Modules { get; } = new();

    /// <summary>The vocab module and the strip that shows it live here, not on the page: the page is rebuilt on every visit.</summary>
    public static VocabModule Vocab { get; } = new();

    internal static TaskbarStripHost VocabStrip { get; } = new(Vocab);

    /// <summary>The shell window; pages need it for picker dialogs.</summary>
    public static Window? CurrentWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        // Only recorded, never marked handled: the app still crashes as before, but the log says why.
        UnhandledException += (_, e) => AppLog.Default.Error("unhandled", "UnhandledException: " + e.Message, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Default.Error("unhandled", "AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => AppLog.Default.Error("unhandled", "UnobservedTaskException", e.Exception);

        Modules.Register(new DiskAnalyzerModule());

        // The icon sits in the install folder, which has the version in its name; the module repoints the menu after updates.
        Modules.Register(new ContextMenuModule(analyzeIcon: Path.Combine(AppContext.BaseDirectory, "Assets", "PowerTools.ico")));
        Modules.Register(new UninstallerModule());
        Modules.Register(Vocab);
    }

    private static string? _pendingAnalyzePath;

    /// <summary>The folder the right-click menu asked to analyze; handed out once, to the page that scans it.</summary>
    public static string? TakePendingAnalyzePath()
    {
        var path = _pendingAnalyzePath;
        _pendingAnalyzePath = null;
        return path;
    }

    public static void SetPendingAnalyzePath(string path) => _pendingAnalyzePath = path;

    public static bool HasPendingAnalyzePath => _pendingAnalyzePath is not null;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);

    private const uint MbOk = 0x0, MbIconWarning = 0x30;

    /// <summary>For a copy that found another one already running.</summary>
    /// <returns>True when this copy should carry on as the running one; false when it should exit.</returns>
    private bool HandOverOrTakeOver()
    {
        // This copy was started by the user, so it may let the running one come to the front. The running copy may
        // still be starting up and not listening yet; sending waits for its pipe for a few seconds.
        AllowSetForegroundWindow(AsfwAny);
        var handedOver = AnalyzeRequestSender.TrySend(
            AnalyzeRequestListener.DefaultPipeName, _pendingAnalyzePath ?? LaunchRequest.ActivateMessage, TimeSpan.FromSeconds(3));

        // Let go of our own handle first: while any handle is open the name stays taken, even if its owner is gone.
        _singleInstance!.Dispose();
        if (handedOver) return false;

        // Nobody answered. The running copy may just have closed (its name outlives its pipe for a moment), in which case
        // the name is free now and this copy takes over, analyze request and all. If it is still taken, that copy is
        // stuck; say so instead of vanishing without a trace.
        _singleInstance = new SingleInstanceGuard(SingleInstanceGuard.DefaultName);
        if (_singleInstance.IsPrimary) return true;

        // A copy running as administrator cannot be reached from a normal one (it made the name and the pipe with
        // administrator-only access), so the reason differs from "stuck".
        var message = _singleInstance.IsAccessDenied
            ? "PowerTools 已在以管理员身份运行，无法从这里打开它。请先关闭那个窗口，再重新打开。"
            : "PowerTools 已在运行，但没有响应。请先在任务管理器里结束它，再重新打开。";
        _singleInstance.Dispose();
        MessageBoxW(IntPtr.Zero, message, "PowerTools", MbOk | MbIconWarning);
        return false;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _pendingAnalyzePath = LaunchRequest.GetAnalyzePath(Environment.GetCommandLineArgs());

        // Decided before anything else is created, so a second copy never opens a window or starts the vocab strip.
        _singleInstance = new SingleInstanceGuard(SingleInstanceGuard.DefaultName);
        if (!_singleInstance.IsPrimary && !HandOverOrTakeOver())
        {
            Exit();
            return;
        }

        AppLog.Default.Info("app", $"PowerTools {AppVersion.Current} started");

        var main = new MainWindow(Modules);
        _window = main;
        CurrentWindow = main;

        // Apply the saved theme before the window is shown, so it never starts in one theme and flips to another later.
        if (main.Content is FrameworkElement root)
        {
            root.RequestedTheme = (Windows.Storage.ApplicationData.Current.LocalSettings.Values["Theme"] as string) switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }

        _analyzeRequests = new AnalyzeRequestListener(AnalyzeRequestListener.DefaultPipeName, message =>
        {
            if (LaunchRequest.IsActivate(message)) main.DispatcherQueue.TryEnqueue(main.BringToFront);
            else if (Path.IsPathFullyQualified(message)) main.DispatcherQueue.TryEnqueue(() => main.OpenInDiskAnalyzer(message));
        });
        _analyzeRequests.Start();

        // The strip is a window of its own, so it would keep the process alive after the main window is gone.
        main.Closed += (_, _) =>
        {
            VocabStrip.Hide();
            _analyzeRequests?.Dispose();
            _singleInstance?.Dispose();
            Exit();
        };

        main.Activate();
        RestoreVocabStrip();    }

    // Brings the strip back if it was showing when the app last ran. A word list that has gone missing is left as saved:
    // nothing is shown this time, and the Vocab page says why when it is opened.
    private static void RestoreVocabStrip()
    {
        if (!VocabSettings.Enabled || VocabSettings.Path is not { Length: > 0 } path) return;

        if (Vocab.Load(path) && Vocab.Start() && !VocabStrip.Show()) Vocab.Stop();
    }
}
