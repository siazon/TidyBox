using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerTools.Core.Modules;

namespace PowerTools.App;

public sealed partial class MainWindow : Window
{
    private readonly ModuleRegistry _registry;

    public MainWindow(ModuleRegistry registry)
    {
        _registry = registry;
        InitializeComponent();
        Nav.SelectionChanged += OnSelectionChanged;
        Nav.Loaded += async (_, _) => await LoadModulesAsync();
    }

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
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            ContentText.Text = item.Content?.ToString() ?? string.Empty;
        }
    }
}
