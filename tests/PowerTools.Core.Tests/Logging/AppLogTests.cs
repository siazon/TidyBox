using System.Text;
using System.Text.RegularExpressions;
using PowerTools.Core.Logging;

namespace PowerTools.Core.Tests.Logging;

public sealed class AppLogTests : IDisposable
{
    private const string Stamp = @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+(?:Z|[+-]\d{2}:\d{2})";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pt-log-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Current => Path.Combine(_dir, "powertools.log");
    private string Old => Path.Combine(_dir, "powertools.1.log");

    private static string[] LinesOf(string path) => File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Info_writes_one_timestamped_line()
    {
        new AppLog(_dir).Info("app", "hello");

        var line = Assert.Single(LinesOf(Current));
        Assert.Matches($@"^{Stamp} \[INFO\] app: hello$", line);
    }

    [Fact]
    public void Error_appends_the_exception_on_indented_lines()
    {
        Exception thrown;
        try { throw new InvalidOperationException("boom"); }
        catch (Exception ex) { thrown = ex; }

        new AppLog(_dir).Error("module", "vocab failed to load", thrown);

        var lines = LinesOf(Current);
        Assert.Matches($@"^{Stamp} \[ERROR\] module: vocab failed to load$", lines[0]);
        Assert.Contains("InvalidOperationException: boom", lines[1]);
        Assert.All(lines.Skip(1), l => Assert.StartsWith("  ", l));
    }

    [Fact]
    public void A_multi_line_message_stays_inside_its_own_entry()
    {
        new AppLog(_dir).Info("app", "first\nsecond");

        var lines = LinesOf(Current);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("  second", lines[1]);
    }

    [Fact]
    public void A_full_file_is_rolled_into_an_old_one()
    {
        var log = new AppLog(_dir, maxFileBytes: 1000);

        for (var i = 0; i < 30; i++) log.Info("t", $"entry {i:D3} " + new string('x', 60));

        Assert.True(File.Exists(Old));
        Assert.True(new FileInfo(Current).Length < 1000 + 200);
        Assert.Contains("entry 029", File.ReadAllText(Current));
    }

    [Fact]
    public void Only_one_old_file_is_kept()
    {
        var log = new AppLog(_dir, maxFileBytes: 500);

        for (var i = 0; i < 100; i++) log.Info("t", $"entry {i:D3} " + new string('x', 60));

        Assert.Equal(new[] { "powertools.1.log", "powertools.log" }, Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(n => n));
    }

    [Fact]
    public void ReadRecent_gives_the_newest_text_from_both_files_starting_at_a_whole_line()
    {
        var log = new AppLog(_dir, maxFileBytes: 1000);
        for (var i = 0; i < 40; i++) log.Info("t", $"entry {i:D3} " + new string('x', 60));

        var recent = log.ReadRecent(1500);

        Assert.True(Encoding.UTF8.GetByteCount(recent) <= 1500);
        Assert.Contains("entry 039", recent);
        Assert.All(recent.Split('\n', StringSplitOptions.RemoveEmptyEntries), l => Assert.Matches($"^{Stamp} ", l));
        Assert.Contains("entry 030", recent); // reaches back into the rolled-over file
    }

    [Fact]
    public void ReadRecent_without_a_log_directory_returns_an_empty_string()
    {
        Assert.Equal(string.Empty, new AppLog(_dir).ReadRecent(1000));
    }

    [Fact]
    public void A_log_that_cannot_be_written_never_throws()
    {
        var file = Path.Combine(Path.GetTempPath(), "pt-log-file-" + Guid.NewGuid());
        File.WriteAllText(file, "I am a file, not a directory");
        try
        {
            var log = new AppLog(Path.Combine(file, "under-a-file"));

            log.Info("app", "x");
            log.Error("app", "y", new Exception("z"));
            Assert.Equal(string.Empty, log.ReadRecent(100));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Entries_written_from_many_threads_stay_whole()
    {
        var log = new AppLog(_dir);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++) log.Info("t", $"thread {t} item {i}");
        })));

        var lines = LinesOf(Current);
        Assert.Equal(400, lines.Length);
        Assert.All(lines, l => Assert.Matches($@"^{Stamp} \[INFO\] t: thread \d item \d+$", l));
        Assert.Equal(400, lines.Select(l => Regex.Match(l, @"thread \d item \d+").Value).Distinct().Count());
    }

    [Fact]
    public void The_default_log_lives_under_the_local_app_data_folder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerTools", "logs");

        Assert.Equal(expected, AppLog.Default.LogDirectory);
    }
}
