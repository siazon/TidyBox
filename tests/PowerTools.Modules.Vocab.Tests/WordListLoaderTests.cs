using System.Text;

namespace PowerTools.Modules.Vocab.Tests;

public sealed class WordListLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pt-vocab-" + Guid.NewGuid());

    public WordListLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File_(string json)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Qwerty_format_loads_in_file_order()
    {
        var path = File_("""
            [
              { "name": "remain", "trans": ["vi.留下,保持"], "usphone": "rɪˈmen", "ukphone": "rɪˈmeɪn" },
              { "name": "among",  "trans": ["prep.在…之内"],  "usphone": "əˈmʌŋ" }
            ]
            """);

        var result = WordListLoader.Load(path);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "remain", "among" }, result.Cards.Select(c => c.Name));
        Assert.Equal("rɪˈmen", result.Cards[0].Usphone);
        Assert.Equal("vi.留下,保持", result.Cards[0].Trans[0]);
    }

    [Fact]
    public void A_missing_file_reports_not_found()
    {
        var result = WordListLoader.Load(Path.Combine(_dir, "nope.json"));

        Assert.False(result.Succeeded);
        Assert.Contains("找不到文件", result.Error);
        Assert.Empty(result.Cards);
    }

    [Fact]
    public void A_directory_instead_of_a_file_reports_unreadable()
    {
        var result = WordListLoader.Load(_dir);

        Assert.Contains("无法读取文件", result.Error);
    }

    [Fact]
    public void An_empty_path_reports_unreadable_instead_of_throwing()
    {
        var result = WordListLoader.Load("");

        Assert.Contains("无法读取文件", result.Error);
    }

    [Fact]
    public void Malformed_json_reports_a_format_error()
    {
        var result = WordListLoader.Load(File_("{ not json"));

        Assert.Contains("词表格式不正确", result.Error);
        Assert.Empty(result.Cards);
    }

    [Fact]
    public void A_json_object_instead_of_an_array_reports_a_format_error()
    {
        var result = WordListLoader.Load(File_("""{ "name": "x", "trans": ["y"] }"""));

        Assert.Contains("词表格式不正确", result.Error);
    }

    [Fact]
    public void Entries_without_a_usable_translation_or_name_are_skipped()
    {
        var path = File_("""
            [
              { "name": "ok",      "trans": ["好"], "usphone": "oʊk" },
              { "name": "empty",   "trans": [] },
              { "name": "missing" },
              { "name": "blank",   "trans": ["", " "] },
              { "name": "",        "trans": ["x"] },
              null,
              { "name": "ok2",     "trans": ["二"] }
            ]
            """);

        var result = WordListLoader.Load(path);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "ok", "ok2" }, result.Cards.Select(c => c.Name));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[ { "name": "a", "trans": [] }, { "name": "b" } ]""")]
    public void A_list_with_nothing_usable_reports_empty(string json)
    {
        var result = WordListLoader.Load(File_(json));

        Assert.False(result.Succeeded);
        Assert.Equal("词表里没有可用的单词", result.Error);
    }

    [Fact]
    public void A_utf8_file_with_a_byte_order_mark_loads()
    {
        var path = Path.Combine(_dir, "bom.json");
        File.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("""[ { "name": "a", "trans": ["啊"] } ]""")).ToArray());

        var result = WordListLoader.Load(path);

        Assert.True(result.Succeeded);
        Assert.Equal("a", Assert.Single(result.Cards).Name);
    }
}
