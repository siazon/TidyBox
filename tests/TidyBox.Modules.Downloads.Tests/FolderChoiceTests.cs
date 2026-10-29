namespace TidyBox.Modules.Downloads.Tests;

public class FolderChoiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tb-downloads-" + Guid.NewGuid());

    public FolderChoiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void OrdinaryFolder_IsAccepted() => Assert.Null(FolderChoice.Validate(_dir));

    [Fact]
    public void FolderWithTrailingSeparator_IsAccepted() => Assert.Null(FolderChoice.Validate(_dir + Path.DirectorySeparatorChar));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\folder")]
    public void EmptyOrRelativePath_IsRefused(string path) => Assert.NotNull(FolderChoice.Validate(path));

    [Fact]
    public void MissingFolder_IsRefused() => Assert.NotNull(FolderChoice.Validate(Path.Combine(_dir, "nope")));

    [Fact]
    public void FileInsteadOfFolder_IsRefused()
    {
        var file = Path.Combine(_dir, "a.txt");
        File.WriteAllText(file, "x");

        Assert.NotNull(FolderChoice.Validate(file));
    }

    [Fact]
    public void DriveRoot_IsRefused() => Assert.NotNull(FolderChoice.Validate(Path.GetPathRoot(_dir)!));

    [Theory]
    [InlineData(Environment.SpecialFolder.Windows)]
    [InlineData(Environment.SpecialFolder.ProgramFiles)]
    [InlineData(Environment.SpecialFolder.ProgramFilesX86)]
    [InlineData(Environment.SpecialFolder.CommonApplicationData)]
    public void SystemFolder_IsRefused(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);

        Assert.NotNull(FolderChoice.Validate(path));
        Assert.NotNull(FolderChoice.Validate(path + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void FolderInsideASystemFolder_IsRefused()
    {
        Assert.NotNull(FolderChoice.Validate(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32")));
    }

    [Fact]
    public void UserProfileRoot_IsRefused() => Assert.NotNull(FolderChoice.Validate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
}
