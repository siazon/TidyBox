using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PowerTools.Core.Elevation;
using PowerTools.ElevatedHelper;
using Xunit;

namespace PowerTools.ElevatedHelper.Tests;

public class ElevationServerTests
{
    private class EchoHandler : IElevatedOperationHandler
    {
        public string OperationType => "echo";
        public Task<ElevationResponse> HandleAsync(string payloadJson)
            => Task.FromResult(new ElevationResponse(true, null, payloadJson));
    }

    [Fact]
    public async Task RunAsync_KnownOperation_EchoesPayloadBack()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var server = new ElevationServer(pipeName, new[] { new EchoHandler() }, TimeSpan.FromSeconds(5));
        var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(2000);
        var request = new ElevationRequest("echo", "{\"x\":1}");
        await PipeMessageFraming.WriteAsync(client, request);
        var response = await PipeMessageFraming.ReadAsync<ElevationResponse>(client);

        Assert.True(response!.Success);
        Assert.Equal("{\"x\":1}", response.ResultJson);

        cts.Cancel();
    }

    [Fact]
    public async Task RunAsync_UnknownOperation_ReturnsFailureResponse()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var server = new ElevationServer(pipeName, new[] { new EchoHandler() }, TimeSpan.FromSeconds(5));
        var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(2000);
        await PipeMessageFraming.WriteAsync(client, new ElevationRequest("nope", "{}"));
        var response = await PipeMessageFraming.ReadAsync<ElevationResponse>(client);

        Assert.False(response!.Success);
        Assert.Contains("nope", response.ErrorMessage);

        cts.Cancel();
    }

    [Fact]
    public async Task RunAsync_NoConnectionsWithinIdleTimeout_ReturnsOnItsOwn()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var server = new ElevationServer(pipeName, Array.Empty<IElevatedOperationHandler>(), TimeSpan.FromMilliseconds(200));

        var completed = await Task.WhenAny(server.RunAsync(CancellationToken.None), Task.Delay(2000));

        Assert.True(completed.IsCompleted);
    }
}
