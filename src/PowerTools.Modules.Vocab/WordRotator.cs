namespace PowerTools.Modules.Vocab;

/// <summary>Hands out the words one after another in file order and starts over after the last.</summary>
public sealed class WordRotator
{
    private readonly IReadOnlyList<WordCard> _cards;
    private int _next;

    public WordRotator(IReadOnlyList<WordCard> cards) => _cards = cards;

    /// <returns>The next word, or null when the list is empty.</returns>
    public WordCard? Next()
    {
        if (_cards.Count == 0) return null;

        var card = _cards[_next];
        _next = (_next + 1) % _cards.Count;
        return card;
    }
}
