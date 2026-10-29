namespace TidyBox.Modules.Downloads.Rules;

/// <summary>The one place the category folder names live, so a second language later means changing this, not the logic.</summary>
public sealed class CategoryNames
{
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["images"] = "图片",
        ["documents"] = "文档",
        ["archives"] = "压缩包",
        ["installers"] = "安装包",
        ["music"] = "音乐",
        ["videos"] = "视频",
    };

    private readonly Dictionary<string, string> _names;

    public CategoryNames(IReadOnlyDictionary<string, string>? overrides = null)
    {
        _names = new Dictionary<string, string>(Defaults);
        foreach (var (id, folder) in overrides ?? new Dictionary<string, string>()) _names[id] = folder;
    }

    public IReadOnlyCollection<string> AllFolderNames => _names.Values;

    public string FolderFor(string categoryId)
        => _names.TryGetValue(categoryId, out var folder) ? folder : throw new ArgumentException($"Unknown category '{categoryId}'.", nameof(categoryId));
}
