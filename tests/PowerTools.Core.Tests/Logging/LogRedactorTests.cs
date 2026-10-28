using PowerTools.Core.Logging;

namespace PowerTools.Core.Tests.Logging;

public class LogRedactorTests
{
    private const string Profile = @"C:\Users\Alice";

    [Fact]
    public void The_user_profile_path_becomes_a_variable_ignoring_case()
    {
        var text = @"Failed: C:\Users\Alice\AppData\Local\x.json and c:\users\alice\y";

        var result = LogRedactor.Redact(text, Profile, "Alice", "DESKTOP-ABC1");

        Assert.Equal(@"Failed: %USERPROFILE%\AppData\Local\x.json and %USERPROFILE%\y", result);
    }

    [Fact]
    public void The_profile_is_replaced_before_the_name_so_the_path_is_not_mangled_twice()
    {
        Assert.Equal(@"%USERPROFILE%\x", LogRedactor.Redact(@"C:\Users\Alice\x", Profile, "Alice", null));
    }

    [Fact]
    public void The_user_name_elsewhere_in_the_text_is_replaced()
    {
        Assert.Equal("signed in as <user> (<user>)", LogRedactor.Redact("signed in as Alice (alice)", Profile, "Alice", null));
    }

    [Fact]
    public void A_machine_name_that_contains_the_user_name_is_replaced_whole()
    {
        // Replacing the shorter name first would leave "-PC1" of the machine name behind.
        var result = LogRedactor.Redact("host=alice-PC1 user=Alice", null, "Alice", "alice-PC1");

        Assert.Equal("host=<machine> user=<user>", result);
    }

    [Fact]
    public void The_machine_name_is_replaced()
    {
        Assert.Equal("crash on <machine>", LogRedactor.Redact("crash on DESKTOP-ABC1", null, null, "desktop-abc1"));
    }

    [Fact]
    public void Names_shorter_than_three_characters_are_left_alone()
    {
        const string text = "Al went to Algiers";

        Assert.Equal(text, LogRedactor.Redact(text, null, "Al", "PC"));
    }

    [Fact]
    public void Missing_values_are_skipped_and_other_text_is_untouched()
    {
        const string text = "nothing personal here: module vocab, 12 words";

        Assert.Equal(text, LogRedactor.Redact(text, null, null, null));
        Assert.Equal(text, LogRedactor.Redact(text, Profile, "Alice", "DESKTOP-ABC1"));
    }

    [Fact]
    public void RedactForSending_removes_this_machines_profile_user_and_machine_name()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var text = $@"{profile}\AppData\x user={Environment.UserName} host={Environment.MachineName}";

        var result = LogRedactor.RedactForSending(text);

        Assert.DoesNotContain(profile, result, StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length >= 3) Assert.DoesNotContain(Environment.UserName, result, StringComparison.OrdinalIgnoreCase);
        if (Environment.MachineName.Length >= 3) Assert.DoesNotContain(Environment.MachineName, result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", result);
    }
}
