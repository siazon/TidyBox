using TidyBox.Modules.Downloads.Rules;
using TidyBox.Modules.Downloads.Scanning;
using TidyBox.Modules.Downloads.Tidy;

namespace TidyBox.Modules.Downloads.Tests.Tidy;

public class ProposalBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());

    public ProposalBuilderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Existing(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private DownloadFile Top(string name, string content)
    {
        var info = new FileInfo(Existing(name, content));
        return new DownloadFile(info.FullName, info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc, null);
    }

    private IReadOnlyList<Proposal> Build(params DownloadFile[] files) => new ProposalBuilder(new CategoryNames()).Build(_root, files);

    private string Dest(string folder, string name) => Path.Combine(_root, folder, name);

    [Fact]
    public void Pdf_NoConflict_IsMoveToDocumentsFolder_DefaultChecked()
    {
        var proposal = Assert.Single(Build(Top("a.pdf", "x")));

        Assert.Equal((ProposalKind.Move, "documents", Dest("文档", "a.pdf"), true, false),
            (proposal.Kind, proposal.CategoryId, proposal.DestinationPath, proposal.DefaultChecked, proposal.IsSuspicious));
    }

    [Fact]
    public void UnknownExtension_IsUntouched_NotChecked_NoDestination()
    {
        var proposal = Assert.Single(Build(Top("notes.md", "x")));

        Assert.Equal((ProposalKind.Untouched, null, null, false), (proposal.Kind, proposal.CategoryId, proposal.DestinationPath, proposal.DefaultChecked));
        Assert.False(string.IsNullOrWhiteSpace(proposal.Reason));
    }

    [Fact]
    public void DoubleExtension_IsMove_ButNotChecked_AndFlaggedSuspicious()
    {
        var proposal = Assert.Single(Build(Top("invoice.pdf.exe", "x")));

        Assert.Equal((ProposalKind.Move, "installers", false, true), (proposal.Kind, proposal.CategoryId, proposal.DefaultChecked, proposal.IsSuspicious));
    }

    [Fact]
    public void SameNameDifferentContent_GetsSuffix_OnlyOnce()
    {
        Existing(Path.Combine("文档", "a.pdf"), "old");

        var proposal = Assert.Single(Build(Top("a.pdf", "new!")));

        Assert.Equal((ProposalKind.Move, Dest("文档", "a (1).pdf"), true), (proposal.Kind, proposal.DestinationPath, proposal.DefaultChecked));
    }

    [Fact]
    public void SourceAlreadyNumbered_DoesNotDoubleSuffix()
    {
        Existing(Path.Combine("文档", "Bookings (1).xlsx"), "old");

        var proposal = Assert.Single(Build(Top("Bookings (1).xlsx", "new!")));

        Assert.Equal(Dest("文档", "Bookings (2).xlsx"), proposal.DestinationPath);
    }

    [Fact]
    public void SameNameSameContent_IsRecycleDuplicate_NotChecked()
    {
        var existing = Existing(Path.Combine("文档", "a.pdf"), "same");

        var proposal = Assert.Single(Build(Top("a.pdf", "same")));

        Assert.Equal((ProposalKind.RecycleDuplicate, existing, false), (proposal.Kind, proposal.DestinationPath, proposal.DefaultChecked));
    }

    [Fact]
    public void SameNameSameSizeButDifferentBytes_IsNotDuplicate()
    {
        Existing(Path.Combine("文档", "a.pdf"), "aaaa");

        var proposal = Assert.Single(Build(Top("a.pdf", "bbbb")));

        Assert.Equal((ProposalKind.Move, Dest("文档", "a (1).pdf")), (proposal.Kind, proposal.DestinationPath));
    }

    [Fact]
    public void TwoSourcesResolvingToSameSuffixedName_GetDistinctDestinations()
    {
        Existing(Path.Combine("文档", "a.pdf"), "old");

        // "a.pdf" will take "a (1).pdf" in the destination, which is exactly the name the second file wants.
        var proposals = Build(Top("a.pdf", "first"), Top("a (1).pdf", "second"));

        Assert.Equal(2, proposals.Select(p => p.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void NeverProposesOverwritingAnExistingFile()
    {
        Existing(Path.Combine("文档", "a.pdf"), "old");
        Existing(Path.Combine("图片", "p.png"), "old");

        var proposals = Build(Top("a.pdf", "new1"), Top("p.png", "new22"), Top("c.pdf", "c"));

        Assert.All(proposals.Where(p => p.Kind == ProposalKind.Move), p => Assert.False(File.Exists(p.DestinationPath)));
    }

    [Fact]
    public void FilesAlreadyInCategoryFolders_AreIgnored()
    {
        var inside = new DownloadFile(Existing(Path.Combine("文档", "a.pdf"), "x"), 1, DateTime.UtcNow, DateTime.UtcNow, "文档");

        Assert.Empty(Build(inside));
    }
}
