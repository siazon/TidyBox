using PowerTools.Core.Elevation;
using PowerTools.ElevatedHelper;

namespace PowerTools.Core.Tests.Elevation;

public class ElevationClientTests
{
    private class EchoHandler : IElevatedOperationHandler
    {
        public string OperationType => "echo";
        public Task<ElevationResponse> HandleAsync(string payloadJson)
            => Task.FromResult(new ElevationResponse(true, null, payloadJson));
    }

    private class FakeLauncher : IHelperProcessLauncher
    {
        public volatile bool Running;
        public int LaunchCount;
        public int LaunchDelayMs; // simulates the time the user takes to answer the UAC prompt
        public bool ThrowOnLaunch; // simulates the user clicking "No" on the UAC prompt
        public Action? OnLaunch;

        public bool IsHelperRunning(string pipeName) => Running;
        public void LaunchElevated(string pipeName)
        {
            Interlocked.Increment(ref LaunchCount);
            if (ThrowOnLaunch) throw new InvalidOperationException("user declined UAC");
            Thread.Sleep(LaunchDelayMs);
            OnLaunch?.Invoke();
            Running = true;
        }
    }

    private static Task StartServer(string pipeName, CancellationToken ct)
        => new ElevationServer(pipeName, new[] { new EchoHandler() }, TimeSpan.FromSeconds(5)).RunAsync(ct);

    [Fact]
    public async Task ExecuteAsync_HelperAlreadyRunning_DoesNotLaunchAgain()
    {
        var pipeName = "pt-client-" + Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var serverTask = StartServer(pipeName, cts.Token);
        var launcher = new FakeLauncher { Running = true };
        var client = new ElevationClient(pipeName, launcher);

        var response = await client.ExecuteAsync(new ElevationRequest("echo", "{\"x\":1}"));

        Assert.True(response.Success);
        Assert.Equal("{\"x\":1}", response.ResultJson);
        Assert.Equal(0, launcher.LaunchCount);
        cts.Cancel();
    }

    [Fact]
    public async Task ExecuteAsync_HelperNotRunning_LaunchesThenConnects()
    {
        var pipeName = "pt-client-" + Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var launcher = new FakeLauncher { Running = false };
        launcher.OnLaunch = () => _ = StartServer(pipeName, cts.Token);
        var client = new ElevationClient(pipeName, launcher);

        var response = await client.ExecuteAsync(new ElevationRequest("echo", "{}"));

        Assert.True(response.Success);
        Assert.Equal(1, launcher.LaunchCount);
        cts.Cancel();
    }

    [Fact]
    public async Task ExecuteAsync_UserDeclinesUac_ReturnsFailureResponseNotThrow()
    {
        var launcher = new FakeLauncher { Running = false, ThrowOnLaunch = true };
        var client = new ElevationClient("pt-client-" + Guid.NewGuid(), launcher);

        var response = await client.ExecuteAsync(new ElevationRequest("echo", "{}"));

        Assert.False(response.Success);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task ExecuteAsync_StalePipeAfterHelperExited_RelaunchesHelper()
    {
        var pipeName = "pt-client-" + Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        // Launcher claims the helper is running, but nobody is listening on the pipe.
        var launcher = new FakeLauncher { Running = true };
        launcher.OnLaunch = () => _ = StartServer(pipeName, cts.Token);
        var client = new ElevationClient(pipeName, launcher, TimeSpan.FromMilliseconds(500));

        var response = await client.ExecuteAsync(new ElevationRequest("echo", "{}"));

        Assert.True(response.Success);
        Assert.Equal(1, launcher.LaunchCount);
        cts.Cancel();
    }

    [Fact]
    public void NewSessionPipeName_IsUnpredictableAndPipeSafe()
    {
        var names = Enumerable.Range(0, 20).Select(_ => ElevationClient.NewSessionPipeName()).ToList();

        Assert.Equal(20, names.Distinct().Count());
        Assert.All(names, n =>
        {
            Assert.StartsWith("PowerTools.", n);
            Assert.Matches("^[A-Za-z0-9.]+$", n);
            Assert.True(n.Length >= "PowerTools.".Length + 32); // 128 bits of randomness
        });
    }

    [Fact]
    public void PipeExists_ReflectsWhetherTheServerIsListening()
    {
        var pipeName = "pt-client-" + Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        _ = StartServer(pipeName, cts.Token);

        Assert.True(ElevatedHelperProcessLauncher.PipeExists(pipeName));
        Assert.False(ElevatedHelperProcessLauncher.PipeExists("pt-client-does-not-exist-" + Guid.NewGuid()));
        cts.Cancel();
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentCallsWhileHelperNotRunning_LaunchesOnlyOnce()
    {
        var pipeName = "pt-client-" + Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var launcher = new FakeLauncher { Running = false, LaunchDelayMs = 200 };
        launcher.OnLaunch = () => _ = StartServer(pipeName, cts.Token);
        var client = new ElevationClient(pipeName, launcher);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => client.ExecuteAsync(new ElevationRequest("echo", "{}")))));

        Assert.All(responses, r => Assert.True(r.Success));
        Assert.Equal(1, launcher.LaunchCount);
        cts.Cancel();
    }

    [Fact]
    public async Task ExecuteAsync_PipeDeniesAccess_ReturnsFailureResponseNotThrow()
    {
        var pipeName = "pt-client-" + Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        var someoneElse = new System.Security.Principal.SecurityIdentifier("S-1-5-21-1111111111-2222222222-3333333333-9999");
        _ = new ElevationServer(pipeName, new[] { new EchoHandler() }, TimeSpan.FromSeconds(5), someoneElse).RunAsync(cts.Token);
        var launcher = new FakeLauncher { Running = true };
        var client = new ElevationClient(pipeName, launcher);

        var response = await client.ExecuteAsync(new ElevationRequest("echo", "{}"));

        Assert.False(response.Success);
        cts.Cancel();
    }
}
