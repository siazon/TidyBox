namespace PowerTools.Modules.Vocab.Tests;

public class WordCardTests
{
    [Fact]
    public void Title_has_the_phonetic_in_slashes_after_two_spaces()
    {
        var card = new WordCard("remain", new List<string> { "vi.留下" }, "rɪˈmen");

        Assert.Equal("remain  /rɪˈmen/", card.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Title_is_just_the_word_without_a_phonetic(string? phonetic)
    {
        var card = new WordCard("remain", new List<string> { "vi.留下" }, phonetic);

        Assert.Equal("remain", card.Title);
    }

    [Fact]
    public void Meaning_joins_the_entries_with_a_full_width_semicolon()
    {
        var card = new WordCard("x", new List<string> { "a", "b" }, null);

        Assert.Equal("a；b", card.Meaning);
    }
}
