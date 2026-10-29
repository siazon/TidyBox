namespace TidyBox.Modules.Vocab;

/// <summary>
/// One entry of a qwerty-learner dictionary file: <c>{ "name", "trans": [...], "usphone" }</c>. The property names
/// match the file's keys (the loader reads them case-insensitively).
/// </summary>
public sealed record WordCard(string Name, List<string> Trans, string? Usphone)
{
    /// <summary>First line of the strip: the word, followed by its American phonetic in slashes when there is one.</summary>
    public string Title => string.IsNullOrEmpty(Usphone) ? Name : $"{Name}  /{Usphone}/";

    /// <summary>Second line of the strip: all translations on one line.</summary>
    public string Meaning => string.Join("；", Trans);
}
