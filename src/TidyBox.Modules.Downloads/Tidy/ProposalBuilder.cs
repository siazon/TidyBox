using System.Text.RegularExpressions;
using TidyBox.Modules.Downloads.Rules;
using TidyBox.Modules.Downloads.Scanning;

namespace TidyBox.Modules.Downloads.Tidy;

public sealed class ProposalBuilder
{
    private static readonly Regex TrailingNumber = new(@"^(?<stem>.*) \(\d+\)$", RegexOptions.Compiled);

    private readonly CategoryNames _names;

    public ProposalBuilder(CategoryNames names)
    {
        _names = names;
    }

    /// <summary>One proposal per file directly in the downloads folder. Files already inside a category folder are not sorted again. Never proposes overwriting anything.</summary>
    public IReadOnlyList<Proposal> Build(string downloadsRoot, IReadOnlyList<DownloadFile> files)
    {
        // Destinations handed out earlier in this batch, which do not exist on disk yet.
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return files.Where(file => file.CategoryFolder is null).Select(file => ProposeFor(downloadsRoot, file, reserved)).ToList();
    }

    private Proposal ProposeFor(string downloadsRoot, DownloadFile file, HashSet<string> reserved)
    {
        var category = Categories.ForFile(file.Name);
        if (category is null) return new Proposal(file, ProposalKind.Untouched, null, null, "没有匹配的规则，留在原处", false, false);

        var suspicious = Suspicion.HasDeceptiveDoubleExtension(file.Name);
        var folderName = _names.FolderFor(category.Id);
        var folder = Path.Combine(downloadsRoot, folderName);
        var desired = Path.Combine(folder, file.Name);

        if (Path.Exists(desired) && ContentCompare.AreSame(file.FullPath, desired))
        {
            return new Proposal(file, ProposalKind.RecycleDuplicate, category.Id, desired, "目标已有内容相同的文件", false, suspicious);
        }

        var taken = Path.Exists(desired) || reserved.Contains(desired);
        var destination = taken ? NextFreeName(folder, file.Name, reserved) : desired;
        reserved.Add(destination);

        var reason = suspicious ? "文件名有双扩展名，请确认" : taken ? "目标已有同名文件，内容不同，已加后缀" : $"移动到「{folderName}」";
        return new Proposal(file, ProposalKind.Move, category.Id, destination, reason, DefaultChecked: !suspicious, suspicious);
    }

    // "name (n).ext" with the smallest free n. A source that is already "name (k).ext" loses its own " (k)" first, so the result is never "name (1) (1).ext".
    private static string NextFreeName(string folder, string fileName, HashSet<string> reserved)
    {
        var extension = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (TrailingNumber.Match(stem) is { Success: true } numbered) stem = numbered.Groups["stem"].Value;

        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({n}){extension}");
            if (!Path.Exists(candidate) && !reserved.Contains(candidate)) return candidate;
        }
    }
}
