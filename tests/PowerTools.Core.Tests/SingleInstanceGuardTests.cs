using System.Security.AccessControl;
using System.Security.Principal;
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

    // What an elevated PowerTools leaves behind for a normal one: a name the current user is not allowed to open.
    private Mutex HoldAnUnopenableName()
    {
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(WindowsIdentity.GetCurrent().User!, MutexRights.FullControl, AccessControlType.Deny));
        return MutexAcl.Create(initiallyOwned: false, _name, out _, security);
    }

    [Fact]
    public void The_first_guard_is_primary()
    {
        Assert.True(Guard().IsPrimary);
    }

    [Fact]
    public void A_name_we_are_not_allowed_to_open_gives_a_guard_that_is_not_primary_instead_of_throwing()
    {
        using var holder = HoldAnUnopenableName();

        Assert.False(Guard().IsPrimary);
    }

    [Fact]
    public void A_guard_that_was_refused_the_name_says_so()
    {
        using var holder = HoldAnUnopenableName();

        Assert.True(Guard().IsAccessDenied);
    }

    [Fact]
    public void A_guard_that_could_open_the_name_is_never_access_denied()
    {
        Assert.False(Guard().IsAccessDenied);
        Assert.False(Guard().IsAccessDenied); // the second one is not primary, but it was allowed in
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
