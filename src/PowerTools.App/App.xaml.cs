using Microsoft.UI.Xaml;
using PowerTools.Core.Modules;
using PowerTools.Modules.ContextMenu;
using PowerTools.Modules.DiskAnalyzer;

namespace PowerTools.App;

public partial class App : Application
{
    private Window? _window;

    public static ModuleRegistry Modules { get; } = new();

    /// <summary>The shell window; pages need it for picker dialogs.</summary>
    public static Window? CurrentWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        Modules.Register(new DiskAnalyzerModule());
        Modules.Register(new ContextMenuModule());
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(Modules);
        CurrentWindow = _window;
        _window.Activate();
    }
}
