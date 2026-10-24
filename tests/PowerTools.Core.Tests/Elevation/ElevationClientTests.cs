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
        public bool Running;
        public int LaunchCount;
        public bool ThrowOnLaunch; // simulates the user clicking "No" on the UAC prompt
        public Action? OnLaunch;

        public bool IsHelperRunning(string pipeName) => Running;
        public void LaunchElevated(string pipeName)
        {
            LaunchCount++;
            if (ThrowOnLaunch) throw new InvalidOperationException("user declined UAC");
            Running = true;
            OnLaunch?.Invoke();
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
}
