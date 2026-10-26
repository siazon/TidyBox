using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using PowerTools.Core.Launch;
using PowerTools.Core.Modules;
using PowerTools.Modules.ContextMenu;
using PowerTools.Modules.DiskAnalyzer;
using PowerTools.Modules.Uninstaller;

namespace PowerTools.App;

public partial class App : Application
{
    private const int AsfwAny = -1; // AllowSetForegroundWindow: any process may take the foreground

    private Window? _window;
    private AnalyzeRequestListener? _analyzeRequests;

    public static ModuleRegistry Modules { get; } = new();

    /// <summary>The shell window; pages need it for picker dialogs.</summary>
    public static Window? CurrentWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        Modules.Register(new DiskAnalyzerModule());

        // The icon sits in the install folder, which has the version in its name; the module repoints the menu after updates.
        Modules.Register(new ContextMenuModule(analyzeIcon: Path.Combine(AppContext.BaseDirectory, "Assets", "PowerTools.ico")));
        Modules.Register(new UninstallerModule());
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _pendingAnalyzePath = LaunchRequest.GetAnalyzePath(Environment.GetCommandLineArgs());

        // Started by the right-click menu while the app is already open: hand the folder to that window and leave.
        if (_pendingAnalyzePath is { } requested)
        {
            // This copy was started by the user's click, so it may let the running one come to the front.
            AllowSetForegroundWindow(AsfwAny);
            if (AnalyzeRequestSender.TrySend(AnalyzeRequestListener.DefaultPipeName, requested, TimeSpan.FromMilliseconds(250)))
            {
                Exit();
                return;
            }
        }

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

        _analyzeRequests = new AnalyzeRequestListener(AnalyzeRequestListener.DefaultPipeName, path =>
        {
            if (Path.IsPathFullyQualified(path)) main.DispatcherQueue.TryEnqueue(() => main.OpenInDiskAnalyzer(path));
        });
        _analyzeRequests.Start();

        main.Activate();
    }
}
