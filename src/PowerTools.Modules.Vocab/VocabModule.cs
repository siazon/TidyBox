using PowerTools.Core.Modules;

namespace PowerTools.Modules.Vocab;

/// <summary>
/// The word list and whether it is being shown. Pure state: showing the words on the taskbar and remembering the
/// settings belong to the app, which keeps this module free of any window or storage dependency.
/// </summary>
public sealed class VocabModule : IToolModule
{
    private WordRotator _rotator = new(Array.Empty<WordCard>());

    public string ModuleId => "vocab";

    public string DisplayName => "背单词";

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>The last path given to <see cref="Load"/>, whether or not it could be read.</summary>
    public string? Path { get; private set; }

    public int WordCount { get; private set; }

    /// <summary>Why the last <see cref="Load"/> failed; null after a successful one.</summary>
    public string? LoadError { get; private set; }

    public bool IsRunning { get; private set; }

    /// <returns>True when the file gave at least one usable word.</returns>
    public bool Load(string path)
    {
        Path = path;
        var result = WordListLoader.Load(path);
        if (!result.Succeeded)
        {
            _rotator = new WordRotator(Array.Empty<WordCard>());
            WordCount = 0;
            LoadError = result.Error;
            IsRunning = false;
            return false;
        }

        _rotator = new WordRotator(result.Cards);
        WordCount = result.Cards.Count;
        LoadError = null;
        return true;
    }

    /// <returns>False when there are no words to show.</returns>
    public bool Start()
    {
        if (WordCount == 0) return false;

        IsRunning = true;
        return true;
    }

    public void Stop() => IsRunning = false;

    /// <returns>The next word in file order, or null when nothing is loaded.</returns>
    public WordCard? Next() => _rotator.Next();
}
