using System.Text;
using PowerTools.Core.Feedback;

namespace PowerTools.Core.Tests.Feedback;

public class FeedbackLimitsTests
{
    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    private static string Lines(int count, string body = "some log text to give each line a little weight")
        => string.Join("\n", Enumerable.Range(1, count).Select(i => $"line {i:D5} {body}"));

    [Fact]
    public void Short_text_is_returned_unchanged()
    {
        Assert.Equal("a\nb", FeedbackLimits.TrimLogs("a\nb"));
    }

    [Fact]
    public void Long_text_keeps_the_end_starting_at_a_whole_line()
    {
        var text = Lines(1500);
        Assert.True(Bytes(text) > FeedbackLimits.MaxLogBytes);

        var trimmed = FeedbackLimits.TrimLogs(text);

        Assert.True(Bytes(trimmed) <= FeedbackLimits.MaxLogBytes);
        Assert.EndsWith("line 01500 some log text to give each line a little weight", trimmed);
        var cut = text.Length - trimmed.Length;
        Assert.Equal('\n', text[cut - 1]); // what was dropped ended exactly at a line break
    }

    [Fact]
    public void Multibyte_characters_are_never_split()
    {
        var text = string.Join("\n", Enumerable.Range(0, 4000).Select(_ => "中文日志内容中文日志内容"));

        var trimmed = FeedbackLimits.TrimLogs(text);

        Assert.True(Bytes(trimmed) <= FeedbackLimits.MaxLogBytes);
        Assert.DoesNotContain('�', trimmed);
        Assert.Equal(trimmed, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(trimmed)));
    }

    [Fact]
    public void A_long_single_line_of_multibyte_characters_is_not_split_mid_character()
    {
        // With no line break to start at, the cut lands wherever the byte limit falls, often inside a 3-byte character.
        var trimmed = FeedbackLimits.TrimLogs(new string('中', 30000)); // 90000 bytes

        Assert.True(Bytes(trimmed) <= FeedbackLimits.MaxLogBytes);
        Assert.DoesNotContain('�', trimmed);
        Assert.All(trimmed, c => Assert.Equal('中', c));
    }

    [Fact]
    public void A_single_line_longer_than_the_limit_is_cut_to_fit()
    {
        var trimmed = FeedbackLimits.TrimLogs(new string('a', FeedbackLimits.MaxLogBytes + 4000));

        Assert.Equal(FeedbackLimits.MaxLogBytes, Bytes(trimmed));
        Assert.All(trimmed, c => Assert.Equal('a', c));
    }

    [Fact]
    public void A_last_line_longer_than_the_limit_is_not_dropped_entirely()
    {
        var trimmed = FeedbackLimits.TrimLogs("first\n" + new string('b', FeedbackLimits.MaxLogBytes + 10) + "\n");

        Assert.NotEmpty(trimmed);
        Assert.True(Bytes(trimmed) <= FeedbackLimits.MaxLogBytes);
    }
}
