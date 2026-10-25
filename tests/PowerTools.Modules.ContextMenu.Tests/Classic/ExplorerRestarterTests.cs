using PowerTools.Modules.ContextMenu.Classic;

namespace PowerTools.Modules.ContextMenu.Tests.Classic;

public class ExplorerRestarterTests
{
    private sealed class FakeProcesses : IExplorerProcessApi
    {
        public int CurrentSessionId { get; set; } = 1;
        public List<ExplorerProcess> Running { get; } = new();
        public List<int> Killed { get; } = new();
        public int Started { get; private set; }
        public bool KilledProcessesLinger { get; set; }
        public Action<int>? OnPoll { get; set; }
        private int _polls;

        public IReadOnlyList<ExplorerProcess> GetExplorerProcesses()
        {
            OnPoll?.Invoke(++_polls);
            return Running.ToList();
        }

        public void Kill(int id)
        {
            Killed.Add(id);
            if (!KilledProcessesLinger) Running.RemoveAll(p => p.Id == id);
        }

        public void StartExplorer() => Started++;
    }

    private static ExplorerRestarter Restarter(FakeProcesses api, int shellWaitMs = 200)
        => new(api, TimeSpan.FromMilliseconds(shellWaitMs), TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task RestartAsync_EndsOnlyTheExplorerProcessesOfTheCurrentSession()
    {
        var api = new FakeProcesses();
        api.Running.Add(new ExplorerProcess(10, SessionId: 1));
        api.Running.Add(new ExplorerProcess(11, SessionId: 2)); // another logged-in user
        api.OnPoll = _ => { if (api.Killed.Count > 0 && api.Running.All(p => p.Id != 99)) api.Running.Add(new ExplorerProcess(99, 1)); }; // the shell comes back after the kill

        await Restarter(api).RestartAsync();

        Assert.Equal(new[] { 10 }, api.Killed);
    }

    [Fact]
    public async Task RestartAsync_WindowsBringsTheShellBack_WeDoNotStartAnotherOne()
    {
        var api = new FakeProcesses();
        api.Running.Add(new ExplorerProcess(10, 1));
        api.OnPoll = poll => { if (poll == 3) api.Running.Add(new ExplorerProcess(99, 1)); }; // Winlogon restarts it a moment later

        await Restarter(api).RestartAsync();

        Assert.Equal(0, api.Started);
    }

    [Fact]
    public async Task RestartAsync_NoShellComesBackInTime_StartsOne()
    {
        var api = new FakeProcesses();
        api.Running.Add(new ExplorerProcess(10, 1));

        await Restarter(api, shellWaitMs: 80).RestartAsync();

        Assert.Equal(1, api.Started);
    }

    [Fact]
    public async Task RestartAsync_AProcessWeEndedButIsStillListed_IsNotMistakenForTheNewShell()
    {
        var api = new FakeProcesses { KilledProcessesLinger = true };
        api.Running.Add(new ExplorerProcess(10, 1));

        await Restarter(api, shellWaitMs: 80).RestartAsync();

        Assert.Equal(1, api.Started);
    }

    [Fact]
    public async Task RestartAsync_AnotherSessionsExplorerIsNotOurShellComingBack()
    {
        var api = new FakeProcesses();
        api.Running.Add(new ExplorerProcess(10, 1));
        api.Running.Add(new ExplorerProcess(50, 2));

        await Restarter(api, shellWaitMs: 80).RestartAsync();

        Assert.Equal(1, api.Started);
    }

    [Fact]
    public async Task RestartAsync_Cancelled_StopsWaitingAndStartsNothing()
    {
        var api = new FakeProcesses();
        api.Running.Add(new ExplorerProcess(10, 1));
        using var cts = new CancellationTokenSource();
        api.OnPoll = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Restarter(api).RestartAsync(cts.Token));

        Assert.Equal(0, api.Started);
    }
}
