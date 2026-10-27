using PowerTools.Core.Launch;

namespace PowerTools.Core.Tests;

public class LaunchRequestTests
{
    [Fact]
    public void The_folder_after_the_analyze_switch_is_returned()
    {
        var path = LaunchRequest.GetAnalyzePath(new[] { "PowerTools.exe", "--analyze", @"C:\Users\me\Documents" });

        Assert.Equal(@"C:\Users\me\Documents", path);
    }

    [Fact]
    public void The_switch_is_not_case_sensitive()
    {
        Assert.Equal(@"C:\Data", LaunchRequest.GetAnalyzePath(new[] { "x", "--ANALYZE", @"C:\Data" }));
    }

    // The right-click command appends "\." so a drive root such as D:\ does not end in a backslash before the closing
    // quote, which the command line parser would read as an escaped quote.
    [Theory]
    [InlineData(@"C:\Users\me\Documents\.", @"C:\Users\me\Documents")]
    [InlineData(@"D:\\.", @"D:\")]
    [InlineData(@"C:\Program Files\.", @"C:\Program Files")]
    public void The_trailing_dot_added_by_the_menu_command_is_normalised_away(string given, string expected)
    {
        Assert.Equal(expected, LaunchRequest.GetAnalyzePath(new[] { "x", "--analyze", given }));
    }

    [Theory]
    [InlineData]
    [InlineData("x")]
    [InlineData("x", "--analyze")]
    [InlineData("x", "--analyze", "")]
    [InlineData("x", "--analyze", "   ")]
    [InlineData("x", "--analyze", "--other")]
    [InlineData("x", "--analyze", "relative\\folder")]
    [InlineData("x", @"C:\Data")]
    public void Anything_that_is_not_a_full_path_after_the_switch_gives_null(params string[] args)
    {
        Assert.Null(LaunchRequest.GetAnalyzePath(args));
    }

    [Theory]
    [InlineData("--activate")]
    [InlineData(" --ACTIVATE \n")]
    public void IsActivate_is_true_for_the_token_ignoring_case_and_whitespace(string message)
    {
        Assert.True(LaunchRequest.IsActivate(message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Users\x")]
    [InlineData("--analyze")]
    public void IsActivate_is_false_for_paths_and_empty_input(string? message)
    {
        Assert.False(LaunchRequest.IsActivate(message));
    }
}
