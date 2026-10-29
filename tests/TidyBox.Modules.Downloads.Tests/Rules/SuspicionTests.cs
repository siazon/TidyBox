using TidyBox.Modules.Downloads.Rules;

namespace TidyBox.Modules.Downloads.Tests.Rules;

public class SuspicionTests
{
    [Theory]
    [InlineData("invoice.pdf.exe", true)]
    [InlineData("photo.PNG.scr", true)]
    [InlineData("setup.exe", false)]
    [InlineData("a.png.zip", false)]
    [InlineData("report.pdf", false)]
    [InlineData("x.tar.gz", false)]
    public void HasDeceptiveDoubleExtension(string fileName, bool expected)
    {
        Assert.Equal(expected, Suspicion.HasDeceptiveDoubleExtension(fileName));
    }
}
