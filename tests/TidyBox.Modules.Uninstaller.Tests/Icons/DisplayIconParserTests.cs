using TidyBox.Modules.Uninstaller.Icons;

namespace TidyBox.Modules.Uninstaller.Tests.Icons;

public class DisplayIconParserTests
{
    [Theory]
    [InlineData("\"C:\\a\\b.exe\",0", @"C:\a\b.exe", 0)]
    [InlineData("C:\\a\\b.exe,2", @"C:\a\b.exe", 2)]
    [InlineData("C:\\a\\b.exe", @"C:\a\b.exe", 0)]
    [InlineData("C:\\a\\b.exe,-101", @"C:\a\b.exe", -101)]
    [InlineData("\"C:\\Program Files\\x\\y.ico\"", @"C:\Program Files\x\y.ico", 0)]
    [InlineData("  \"C:\\a\\b.exe\" , 1 ", @"C:\a\b.exe", 1)]
    public void Parses_the_forms_found_in_the_registry(string text, string file, int index)
    {
        Assert.Equal(new IconLocation(file, index), DisplayIconParser.Parse(text));
    }

    [Fact]
    public void Expands_environment_variables()
    {
        var location = DisplayIconParser.Parse("%SystemRoot%\\system32\\shell32.dll,3");

        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "system32", "shell32.dll"), location!.File);
        Assert.Equal(3, location.Index);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",1")]
    [InlineData("\"unterminated,0")]
    public void Returns_null_when_there_is_no_file(string? text)
    {
        Assert.Null(DisplayIconParser.Parse(text));
    }
}
