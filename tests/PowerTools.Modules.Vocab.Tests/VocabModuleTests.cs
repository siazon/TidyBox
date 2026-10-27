namespace PowerTools.Modules.Vocab.Tests;

public sealed class VocabModuleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pt-vocab-module-" + Guid.NewGuid());

    public VocabModuleTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string List(params string[] names)
    {
        var path = Path.Combine(_dir, Guid.NewGuid() + ".json");
        var entries = names.Select(n => $$"""{ "name": "{{n}}", "trans": ["释义"] }""");
        File.WriteAllText(path, "[" + string.Join(",", entries) + "]");
        return path;
    }

    private string Missing() => Path.Combine(_dir, "missing.json");

    [Fact]
    public void Id_and_display_name_are_fixed()
    {
        var module = new VocabModule();

        Assert.Equal("vocab", module.ModuleId);
        Assert.Equal("背单词", module.DisplayName);
    }

    [Fact]
    public void Load_of_a_valid_file_sets_path_count_and_clears_the_error()
    {
        var module = new VocabModule();
        module.Load(Missing());
        var path = List("a", "b", "c");

        Assert.True(module.Load(path));

        Assert.Equal(path, module.Path);
        Assert.Equal(3, module.WordCount);
        Assert.Null(module.LoadError);
    }

    [Fact]
    public void Next_after_load_starts_at_the_first_word()
    {
        var module = new VocabModule();
        module.Load(List("a", "b"));

        Assert.Equal("a", module.Next()!.Name);
        Assert.Equal("b", module.Next()!.Name);
    }

    [Fact]
    public void Next_before_anything_is_loaded_returns_null()
    {
        Assert.Null(new VocabModule().Next());
    }

    [Fact]
    public void Load_of_a_missing_file_returns_false_and_leaves_no_words()
    {
        var module = new VocabModule();
        module.Load(List("a"));

        Assert.False(module.Load(Missing()));

        Assert.Equal(0, module.WordCount);
        Assert.Equal(Missing(), module.Path);
        Assert.Contains("找不到文件", module.LoadError);
        Assert.Null(module.Next());
    }

    [Fact]
    public void Start_without_words_returns_false_and_does_not_run()
    {
        var module = new VocabModule();

        Assert.False(module.Start());

        Assert.False(module.IsRunning);
    }

    [Fact]
    public void Start_twice_is_harmless_and_does_not_restart_the_rotation()
    {
        var module = new VocabModule();
        module.Load(List("a", "b"));
        Assert.True(module.Start());
        Assert.Equal("a", module.Next()!.Name);

        Assert.True(module.Start());

        Assert.True(module.IsRunning);
        Assert.Equal("b", module.Next()!.Name);
    }

    [Fact]
    public void Stop_when_not_running_does_nothing()
    {
        var module = new VocabModule();
        module.Load(List("a"));

        module.Stop();

        Assert.False(module.IsRunning);
        Assert.Equal(1, module.WordCount);
    }

    [Fact]
    public void Stop_after_start_ends_the_run_but_keeps_the_words()
    {
        var module = new VocabModule();
        module.Load(List("a"));
        module.Start();

        module.Stop();

        Assert.False(module.IsRunning);
        Assert.Equal(1, module.WordCount);
    }

    [Fact]
    public void A_failed_load_while_running_stops_it()
    {
        var module = new VocabModule();
        module.Load(List("a"));
        module.Start();

        module.Load(Missing());

        Assert.False(module.IsRunning);
    }

    [Fact]
    public void A_successful_load_while_running_keeps_running_and_restarts_from_the_first_word()
    {
        var module = new VocabModule();
        module.Load(List("a", "b"));
        module.Start();
        module.Next();
        module.Next();

        Assert.True(module.Load(List("x", "y")));

        Assert.True(module.IsRunning);
        Assert.Equal("x", module.Next()!.Name);
    }
}
