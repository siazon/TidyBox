using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TidyBox.App.TaskbarStrip;
using TidyBox.Modules.Vocab;

namespace TidyBox.App.Views;

/// <summary>
/// Picks the word list and switches the strip on and off. The strip itself belongs to <see cref="App.VocabStrip"/>, not to
/// this page: the page is rebuilt every time the user navigates here, and the strip must outlive that.
/// </summary>
public sealed partial class VocabView : Page
{
    private VocabModule _module = null!;
    private bool _updating; // set while the page changes the switch itself, so Toggled does not act on it

    public VocabView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _module = (VocabModule)e.Parameter;
        Refresh();
    }

    private void Refresh()
    {
        _updating = true;
        PathText.Text = _module.Path ?? "尚未选择词表";
        StatusText.Text = _module.LoadError ?? (_module.WordCount > 0 ? $"已加载 {_module.WordCount} 个单词" : string.Empty);
        ShowSwitch.IsEnabled = _module.WordCount > 0;
        ShowSwitch.IsOn = App.VocabStrip.IsShowing;
        _updating = false;
    }

    private async void OnPickClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        if (_module.Load(file.Path))
        {
            VocabSettings.Path = file.Path;
        }
        else
        {
            // A bad list also stopped the module; take the strip down with it.
            App.VocabStrip.Hide();
            VocabSettings.Enabled = false;
        }

        Refresh();
    }

    private void OnShowToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;

        if (!ShowSwitch.IsOn)
        {
            App.VocabStrip.Hide();
            _module.Stop();
            VocabSettings.Enabled = false;
            return;
        }

        if (_module.Start() && App.VocabStrip.Show())
        {
            VocabSettings.Enabled = true;
            return;
        }

        var reason = _module.WordCount == 0 ? _module.LoadError ?? "请先选择词表" : App.VocabStrip.Error;
        _module.Stop();
        _updating = true;
        ShowSwitch.IsOn = false;
        _updating = false;
        StatusText.Text = reason ?? "无法显示在任务栏";
    }
}
