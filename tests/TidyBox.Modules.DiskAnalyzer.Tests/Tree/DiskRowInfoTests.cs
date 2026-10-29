using System.Globalization;
using TidyBox.Modules.DiskAnalyzer;
using TidyBox.Modules.DiskAnalyzer.Scanning;
using TidyBox.Modules.DiskAnalyzer.Tree;

namespace TidyBox.Modules.DiskAnalyzer.Tests.Tree;

public class DiskRowInfoTests
{
    private static readonly DateTime Modified = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static string LocalDate => Modified.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public void A_folder_gives_its_name_path_sizes_counts_and_date_separated_by_tabs()
    {
        var node = new DiskNode("Docs", @"D:\Work\Docs", DiskNodeKind.Folder, 1_572_864, 1234, 56, Modified, Array.Empty<DiskNode>());

        var fields = DiskRowInfo.ToClipboardText(node).Split('\t');

        Assert.Equal(new[] { "Docs", @"D:\Work\Docs", "1572864", ByteSizeFormatter.Format(1_572_864), "1234", "56", LocalDate }, fields);
    }

    [Fact]
    public void The_files_row_names_itself_and_gives_the_folder_its_files_are_in()
    {
        var node = new DiskNode("[3 个文件]", @"D:\Work\Docs", DiskNodeKind.FilesGroup, 900, 3, 0, Modified, Array.Empty<DiskNode>());

        var fields = DiskRowInfo.ToClipboardText(node).Split('\t');

        Assert.Equal("[3 个文件]", fields[0]);
        Assert.Equal(@"D:\Work\Docs", fields[1]);
        Assert.Equal("900", fields[2]);
    }

    [Fact]
    public void Numbers_do_not_pick_up_thousands_separators_so_a_spreadsheet_reads_them_as_numbers()
    {
        var node = new DiskNode("Big", @"D:\Big", DiskNodeKind.Folder, 12_345_678_901, 1_000_000, 2_000, Modified, Array.Empty<DiskNode>());

        var fields = DiskRowInfo.ToClipboardText(node).Split('\t');

        Assert.Equal("12345678901", fields[2]);
        Assert.Equal("1000000", fields[4]);
        Assert.Equal("2000", fields[5]);
    }

    [Fact]
    public void It_is_a_single_line()
    {
        var node = new DiskNode("Docs", @"D:\Work\Docs", DiskNodeKind.Folder, 1, 1, 0, Modified, Array.Empty<DiskNode>());

        Assert.DoesNotContain('\n', DiskRowInfo.ToClipboardText(node));
    }
}
