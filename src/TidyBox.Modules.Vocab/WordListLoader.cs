using System.Text.Json;

namespace TidyBox.Modules.Vocab;

/// <param name="Cards">The usable entries, in file order. Empty when <paramref name="Error"/> is set.</param>
/// <param name="Error">Why the file could not be used, worded for the settings page; null on success.</param>
public sealed record WordListLoadResult(IReadOnlyList<WordCard> Cards, string? Error)
{
    public bool Succeeded => Error is null;
}

public static class WordListLoader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Reads a dictionary file. Never throws for a bad file; the reason comes back in the result.</summary>
    public static WordListLoadResult Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fail($"找不到文件：{path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fail($"无法读取文件：{ex.Message}");
        }

        List<WordCard?>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<WordCard?>>(text, Options);
        }
        catch (JsonException ex)
        {
            return Fail($"词表格式不正确：{ex.Message}");
        }

        // The record binds a missing "trans" to null at run time even though the type says it cannot be.
        var cards = (entries ?? new List<WordCard?>())
            .Where(card => card is { Name.Length: > 0 } && card.Trans?.Any(t => !string.IsNullOrWhiteSpace(t)) == true)
            .Select(card => card!)
            .ToList();

        return cards.Count == 0 ? Fail("词表里没有可用的单词") : new WordListLoadResult(cards, null);
    }

    private static WordListLoadResult Fail(string error) => new(Array.Empty<WordCard>(), error);
}
