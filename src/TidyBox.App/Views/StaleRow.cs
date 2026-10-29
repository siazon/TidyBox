using System.ComponentModel;
using TidyBox.Modules.DiskAnalyzer;
using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.App.Views;

/// <summary>One line of the review list (and of the kept list): unchecked by default, and the page can check a whole filtered set at once.</summary>
public sealed class StaleRow : INotifyPropertyChanged
{
    private bool _isChecked;

    public StaleRow(DownloadFile file, int ageDays, string? sourceDomain, string categoryLabel)
    {
        File = file;
        AgeDays = ageDays;
        SourceDomain = sourceDomain;
        CategoryLabel = categoryLabel;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DownloadFile File { get; }

    public int AgeDays { get; }

    public string? SourceDomain { get; }

    /// <summary>What the type filter matches on.</summary>
    public string CategoryLabel { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public string Name => File.Name;

    public string Detail
    {
        get
        {
            var parts = new List<string> { ByteSizeFormatter.Format(File.SizeBytes), $"{AgeDays} 天没动" };
            if (File.CategoryFolder is not null) parts.Add($"在「{File.CategoryFolder}」");
            if (SourceDomain is not null) parts.Add($"来源 {SourceDomain}");
            return string.Join(" · ", parts);
        }
    }
}
