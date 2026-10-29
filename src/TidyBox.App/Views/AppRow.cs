using System.ComponentModel;
using TidyBox.Modules.Uninstaller.Catalog;
using TidyBox.Modules.Uninstaller.Model;

namespace TidyBox.App.Views;

/// <summary>One line of the installed-programs list. The size can arrive later, after the program's folder has been measured.</summary>
public sealed class AppRow : INotifyPropertyChanged
{
    private long? _sizeBytes;
    private bool _measuring;
    private bool _sizeChecked;

    private bool _isChecked;

    public AppRow(InstalledApp app)
    {
        App = app;
        _sizeBytes = app.EstimatedSizeBytes;
        Health = AppHealth.Check(app);
    }

    /// <summary>Ticked for batch uninstall; independent of which row is "selected" (single-click) in the list.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public InstalledApp App { get; }

    /// <summary>Why Windows' own uninstall would probably fail for this entry, else null.</summary>
    public string? Health { get; }

    public string DisplayName => App.DisplayName;

    public string Subtitle
    {
        get
        {
            var text = string.Join(" · ", new[] { App.Publisher, App.Version }.Where(part => !string.IsNullOrWhiteSpace(part)));
            return Health is null ? text : $"{text}　⚠ {Health}";
        }
    }

    public string SourceText => App.Source switch
    {
        AppSource.Store => "商店应用",
        AppSource.Steam => "Steam 游戏",
        _ => "桌面程序",
    };

    /// <summary>Installed within the last 30 days. Store apps are left out: their date is the last auto-update, which would flag half the list.</summary>
    public bool IsNew => App.Source != AppSource.Store && App.InstalledOn is { } date && date >= DateTime.Today.AddDays(-30);

    public string DateText => App.InstalledOn?.ToString("yyyy-MM-dd") ?? "";

    public long? SizeBytes
    {
        get => _sizeBytes;
        set => Set(ref _sizeBytes, value);
    }

    public bool Measuring
    {
        get => _measuring;
        set => Set(ref _measuring, value);
    }

    /// <summary>True once the program's folder has been looked at, whether or not that gave a size.</summary>
    public bool SizeChecked
    {
        get => _sizeChecked;
        set => Set(ref _sizeChecked, value);
    }

    /// <summary>Why there is no size, shown on hover; null while there is one or it is still being worked out.</summary>
    public string? SizeTooltip => SizeBytes is null && SizeChecked && !Measuring ? AppSizer.UnknownReason(App) : null;

    private Microsoft.UI.Xaml.Media.ImageSource? _icon;

    /// <summary>Arrives after the list is shown; until then (or when none was found) the placeholder is displayed.</summary>
    public Microsoft.UI.Xaml.Media.ImageSource? Icon
    {
        get => _icon;
        set => Set(ref _icon, value);
    }

    public Microsoft.UI.Xaml.Visibility IconVisibility => _icon is null ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility PlaceholderVisibility => _icon is null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string SizeText => SizeBytes switch
    {
        null => Measuring ? "计算中…" : SizeChecked ? "无法确定" : "",
        >= 1L << 30 => $"{SizeBytes.Value / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{SizeBytes.Value / (double)(1L << 20):0.#} MB",
        _ => $"{Math.Max(1, SizeBytes.Value >> 10)} KB",
    };

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // size text depends on both fields
    }
}
