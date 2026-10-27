using PowerTools.Core.Launch;

namespace PowerTools.Core.Tests;

public sealed class SingleInstanceGuardTests : IDisposable
{
    // A name of our own per test, so these never touch (or are touched by) a real running PowerTools.
    private readonly string _name = "PowerTools.Tests." + Guid.NewGuid();
    private readonly List<SingleInstanceGuard> _guards = new();

    public void Dispose()
    {
        foreach (var guard in _guards) guard.Dispose();
    }

    private SingleInstanceGuard Guard(string? name = null)
    {
        var guard = new SingleInstanceGuard(name ?? _name);
        _guards.Add(guard);
        return guard;
    }

    [Fact]
    public void The_first_guard_is_primary()
    {
        Assert.True(Guard().IsPrimary);
    }

    [Fact]
    public void A_second_guard_with_the_same_name_is_not_primary()
    {
        Guard();

        Assert.False(Guard().IsPrimary);
    }

    [Fact]
    public void Once_all_guards_are_disposed_a_new_guard_is_primary_again()
    {
        var first = Guard();
        var second = Guard();
        first.Dispose();
        second.Dispose();

        Assert.True(Guard().IsPrimary);
    }

    [Fact]
    public void Guards_with_different_names_are_independent()
    {
        Guard();

        Assert.True(Guard("PowerTools.Tests." + Guid.NewGuid()).IsPrimary);
    }
}
