using System.Globalization;

namespace TidyBox.Modules.DiskAnalyzer.Tests;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024, "5.0 MB")]
    [InlineData(5368709120, "5.0 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 * 1024, "3.0 TB")]
    public void Format_UsesBinaryUnitsWithOneDecimal(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }

    [Theory]
    [InlineData(2048, "+2.0 KB")]
    [InlineData(-2048, "-2.0 KB")]
    [InlineData(0, "0 B")]
    public void FormatSigned_ShowsAnExplicitSign(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.FormatSigned(bytes));
    }

    [Fact]
    public void Format_IgnoresTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // would print "1,5 KB"
            Assert.Equal("1.5 KB", ByteSizeFormatter.Format(1536));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
