using Microsoft.UI.Xaml;
using PowerTools.Core.Modules;

namespace PowerTools.App;

public partial class App : Application
{
    private Window? _window;

    public static ModuleRegistry Modules { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(Modules);
        _window.Activate();
    }
}
