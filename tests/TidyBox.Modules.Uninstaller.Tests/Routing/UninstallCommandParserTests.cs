using TidyBox.Modules.Uninstaller.Routing;

namespace TidyBox.Modules.Uninstaller.Tests.Routing;

public sealed class UninstallCommandParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TidyBoxTests", Guid.NewGuid().ToString("N"));

    public UninstallCommandParserTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Parses_a_quoted_path_followed_by_arguments()
    {
        var cmd = UninstallCommandParser.Parse("\"C:\\Program Files\\X\\unins000.exe\" /SILENT /NORESTART");

        Assert.Equal(new UninstallCommand(@"C:\Program Files\X\unins000.exe", "/SILENT /NORESTART"), cmd);
    }

    [Fact]
    public void Parses_an_unquoted_path_with_arguments()
    {
        var cmd = UninstallCommandParser.Parse(@"C:\a\b.exe /S");

        Assert.Equal(new UninstallCommand(@"C:\a\b.exe", "/S"), cmd);
    }

    [Fact]
    public void Finds_an_unquoted_path_that_contains_spaces_by_checking_which_prefix_exists()
    {
        var folder = Path.Combine(_dir, "Program Files", "X");
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "u.exe");
        File.WriteAllText(exe, "");

        var cmd = UninstallCommandParser.Parse($"{exe} /S");

        Assert.Equal(new UninstallCommand(exe, "/S"), cmd);
    }

    [Fact]
    public void Turns_an_msiexec_install_command_into_an_uninstall()
    {
        var install = UninstallCommandParser.Parse("MsiExec.exe /I{12345678-1234-1234-1234-123456789ABC}");
        var uninstall = UninstallCommandParser.Parse("MsiExec.exe /X{12345678-1234-1234-1234-123456789ABC}");

        Assert.Equal("/X{12345678-1234-1234-1234-123456789ABC}", install!.Arguments);
        Assert.Equal("/X{12345678-1234-1234-1234-123456789ABC}", uninstall!.Arguments);
    }

    [Fact]
    public void Expands_environment_variables()
    {
        var cmd = UninstallCommandParser.Parse("\"%windir%\\system32\\x.exe\" /u");

        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("windir")!, "system32", "x.exe"), cmd!.FileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"unterminated")]
    public void Returns_null_when_there_is_nothing_to_run(string text)
    {
        Assert.Null(UninstallCommandParser.Parse(text));
    }
}
