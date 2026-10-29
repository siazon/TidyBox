using TidyBox.Modules.DiskAnalyzer.Steam;

namespace TidyBox.Modules.DiskAnalyzer.Tests.Steam;

public class SteamLibraryParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pt-steam-" + Guid.NewGuid());

    public SteamLibraryParserTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteSteamApps(string fileName, string content)
    {
        var dir = Path.Combine(_root, "steamapps");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private const string ValidAcf = """
        "AppState"
        {
            "appid"		"123"
            "name"		"Example Game"
            "installdir"		"ExampleGame"
            "SizeOnDisk"		"5368709120"
            "LastUpdated"		"1700000000"
        }
        """;

    [Fact]
    public void ParseLibrary_ValidAcfFile_ReturnsGameInfo()
    {
        WriteSteamApps("appmanifest_123.acf", ValidAcf);

        var game = new SteamLibraryParser().ParseLibrary(_root).Single();

        Assert.Equal("123", game.AppId);
        Assert.Equal("Example Game", game.Name);
        Assert.Equal(5368709120, game.SizeBytes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime, game.LastUpdatedUtc);
        Assert.Equal(Path.Combine(_root, "steamapps", "common", "ExampleGame"), game.InstallPath);
    }

    [Fact]
    public void ParseLibrary_AcfWithoutInstallDir_StillParsesWithEmptyInstallPath()
    {
        WriteSteamApps("appmanifest_5.acf", """
            "AppState"
            {
                "appid"		"5"
                "name"		"No Dir"
                "SizeOnDisk"		"1"
                "LastUpdated"		"1700000000"
            }
            """);

        Assert.Equal("", new SteamLibraryParser().ParseLibrary(_root).Single().InstallPath);
    }

    [Fact]
    public void ParseLibrary_MalformedAcfFile_SkipsItWithoutThrowing()
    {
        WriteSteamApps("appmanifest_999.acf", "not even close to valid vdf {{{");
        WriteSteamApps("appmanifest_123.acf", ValidAcf);

        var games = new SteamLibraryParser().ParseLibrary(_root);

        Assert.Equal("123", games.Single().AppId);
    }

    [Theory]
    [InlineData("\"name\"\t\"X\"\n\"SizeOnDisk\"\t\"1\"\n\"LastUpdated\"\t\"1700000000\"")]   // appid missing
    [InlineData("\"appid\"\t\"7\"\n\"SizeOnDisk\"\t\"1\"\n\"LastUpdated\"\t\"1700000000\"")] // name missing
    [InlineData("\"appid\"\t\"7\"\n\"name\"\t\"X\"\n\"LastUpdated\"\t\"1700000000\"")]        // size missing
    [InlineData("\"appid\"\t\"7\"\n\"name\"\t\"X\"\n\"SizeOnDisk\"\t\"abc\"\n\"LastUpdated\"\t\"1700000000\"")] // size not a number
    public void ParseLibrary_AcfMissingOrInvalidRequiredField_IsSkipped(string appStateBody)
    {
        WriteSteamApps("appmanifest_7.acf", "\"AppState\"\n{\n" + appStateBody + "\n}");
        WriteSteamApps("appmanifest_123.acf", ValidAcf);

        Assert.Equal("123", new SteamLibraryParser().ParseLibrary(_root).Single().AppId);
    }

    [Fact]
    public void ParseLibrary_TruncatedAcfWhileGameIsUpdating_IsSkipped()
    {
        WriteSteamApps("appmanifest_8.acf", "\"AppState\"\n{\n\t\"appid\"\t\"8\"\n\t\"name\"\t\"Half writ");

        Assert.Empty(new SteamLibraryParser().ParseLibrary(_root));
    }

    [Fact]
    public void ParseLibrary_LibraryFolderDoesNotExist_ReturnsEmpty()
    {
        Assert.Empty(new SteamLibraryParser().ParseLibrary(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void FindLibraryFolders_ParsesLibraryFoldersVdf()
    {
        WriteSteamApps("libraryfolders.vdf", """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                    "label"		""
                }
            }
            """);

        var folders = new SteamLibraryParser().FindLibraryFolders(_root);

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, folders);
    }

    [Fact]
    public void FindLibraryFolders_OldFormatWithPathAsValue_IsSupported()
    {
        WriteSteamApps("libraryfolders.vdf", """
            "LibraryFolders"
            {
                "TimeNextStatsReport"		"1700000000"
                "ContentStatsID"		"-123456"
                "1"		"D:\\OldLibrary"
            }
            """);

        Assert.Equal(new[] { @"D:\OldLibrary" }, new SteamLibraryParser().FindLibraryFolders(_root));
    }

    [Fact]
    public void FindLibraryFolders_FileMissing_ReturnsEmpty()
    {
        Assert.Empty(new SteamLibraryParser().FindLibraryFolders(_root));
    }

    [Fact]
    public void FindLibraryFolders_FileCorrupt_ReturnsEmpty()
    {
        WriteSteamApps("libraryfolders.vdf", "\"libraryfolders\" { \"0\" {");

        Assert.Empty(new SteamLibraryParser().FindLibraryFolders(_root));
    }
}
