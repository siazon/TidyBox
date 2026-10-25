using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerTools.App.Views;
using PowerTools.Core.Modules;

namespace PowerTools.App;

public sealed partial class MainWindow : Window
{
    // Which page shows which module. A module without an entry here just shows its name.
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["disk-analyzer"] = typeof(DiskAnalyzerView),
        ["context-menu"] = typeof(ContextMenuView),
        ["uninstaller"] = typeof(UninstallerView),
    };

    private readonly ModuleRegistry _registry;

    public MainWindow(ModuleRegistry registry)
    {
        _registry = registry;
        InitializeComponent();

        // The icon at the top left of the window (and on its taskbar button).
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "PowerTools.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        ContentFrame.Content = new TextBlock { Margin = new Thickness(24), Text = "No modules loaded." };
        Nav.SelectionChanged += OnSelectionChanged;
        Nav.Loaded += async (_, _) => await LoadModulesAsync();
    }

    /// <summary>Shows the folder in the disk analyzer and starts scanning it; used when the right-click menu asks the open window to.</summary>
    public void OpenInDiskAnalyzer(string path)
    {
        var item = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.IsEnabled && i.Tag as string == "disk-analyzer");
        if (item is null) return;

        App.SetPendingAnalyzePath(path);
        if (ContentFrame.Content is DiskAnalyzerView view) view.ScanPendingPath(); // already there: navigating again would do nothing
        else Nav.SelectedItem = item;

        BringToFront();
    }

    private void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private bool _modulesLoaded;

    private async Task LoadModulesAsync()
    {
        // Loaded can fire again (e.g. when the element is re-parented); only populate the nav once.
        if (_modulesLoaded) return;
        _modulesLoaded = true;

        foreach (var module in _registry.GetAll())
        {
            try
            {
                await module.InitializeAsync();
                Nav.MenuItems.Add(new NavigationViewItem { Content = module.DisplayName, Tag = module.ModuleId });
            }
            catch (Exception ex)
            {
                var failed = new NavigationViewItem
                {
                    Content = $"{module.DisplayName} (failed to load)",
                    Tag = ex.Message,
                    IsEnabled = false,
                };
                ToolTipService.SetToolTip(failed, ex.Message);
                Nav.MenuItems.Add(failed);
            }
        }

        // Started from the right-click menu: the folder goes to the disk analyzer.
        if (App.HasPendingAnalyzePath)
        {
            Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => item.IsEnabled && item.Tag as string == "disk-analyzer");
        }

        // Otherwise the user lands on the "No modules loaded." placeholder although modules did load.
        Nav.SelectedItem ??= Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => item.IsEnabled);
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var module = item.Tag is string id ? _registry.GetById(id) : null;
        if (module is not null && Pages.TryGetValue(module.ModuleId, out var pageType))
        {
            ContentFrame.Navigate(pageType, module);
        }
        else
        {
            ContentFrame.Content = new TextBlock { Margin = new Thickness(24), Text = item.Content?.ToString() ?? string.Empty };
        }
    }
}
