namespace TidyBox.Modules.Downloads.Rules;

/// <param name="Extensions">Lower-case, without the dot. An extension belongs to one category only.</param>
public sealed record Category(string Id, IReadOnlyList<string> Extensions);

/// <summary>The default sort rules, by extension only. A file that matches none stays where it is: there is no catch-all category.</summary>
public static class Categories
{
    public static IReadOnlyList<Category> All { get; } = new[]
    {
        new Category("images", new[] { "jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "ico", "heic", "heif" }),
        new Category("documents", new[] { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "odt" }),
        new Category("archives", new[] { "zip", "rar", "7z", "tar", "gz", "bz2", "xz" }),
        new Category("installers", new[] { "exe", "msi", "msix", "appx" }),
        new Category("music", new[] { "mp3", "wav", "flac", "aac", "ogg", "wma", "m4a" }),
        new Category("videos", new[] { "mp4", "avi", "mkv", "mov", "wmv", "flv", "webm" }),
    };

    public static Category? ForFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.');
        return All.FirstOrDefault(category => category.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }
}
