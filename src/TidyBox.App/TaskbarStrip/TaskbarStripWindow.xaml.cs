using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TidyBox.Modules.Vocab;
using WinRT.Interop;

namespace TidyBox.App.TaskbarStrip;

/// <summary>The two lines of text. It knows nothing about the taskbar; <see cref="TaskbarStripHost"/> embeds and places it.</summary>
public sealed partial class TaskbarStripWindow : Window
{
    public TaskbarStripWindow()
    {
        InitializeComponent();
        SystemBackdrop = new TransparentBackdrop();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
        }
        AppWindow.IsShownInSwitchers = false;
    }

    internal IntPtr Hwnd => WindowNative.GetWindowHandle(this);

    internal void SetCard(WordCard card)
    {
        WordText.Text = card.Title;
        MeaningText.Text = card.Meaning;
    }

    /// <summary>The text sits straight on the taskbar, so it must contrast with the taskbar's theme, not the app's.</summary>
    internal void SetTextColor(bool lightTaskbar)
    {
        var brush = new SolidColorBrush(lightTaskbar ? Colors.Black : Colors.White);
        WordText.Foreground = brush;
        MeaningText.Foreground = brush;
    }
}
