namespace PowerTools.Modules.Vocab.Tests;

public class WordRotatorTests
{
    private static WordCard Card(string name) => new(name, new List<string> { "释义" }, null);

    [Fact]
    public void Next_returns_the_cards_in_order_and_wraps_around()
    {
        var rotator = new WordRotator(new[] { Card("a"), Card("b"), Card("c") });

        var names = Enumerable.Range(0, 7).Select(_ => rotator.Next()!.Name);

        Assert.Equal(new[] { "a", "b", "c", "a", "b", "c", "a" }, names);
    }

    [Fact]
    public void Next_on_an_empty_list_returns_null()
    {
        Assert.Null(new WordRotator(Array.Empty<WordCard>()).Next());
    }
}
