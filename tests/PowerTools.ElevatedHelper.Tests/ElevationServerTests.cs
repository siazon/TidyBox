using System;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
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

    private class ThrowingHandler : IElevatedOperationHandler
    {
        public string OperationType => "boom";
        public Task<ElevationResponse> HandleAsync(string payloadJson)
            => throw new InvalidOperationException("handler failed");
    }

    [Fact]
    public async Task RunAsync_HandlerThrows_ReturnsFailureAndKeepsServing()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var server = new ElevationServer(pipeName, new IElevatedOperationHandler[] { new ThrowingHandler(), new EchoHandler() }, TimeSpan.FromSeconds(5));
        var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
        {
            await client.ConnectAsync(2000);
            await PipeMessageFraming.WriteAsync(client, new ElevationRequest("boom", "{}"));
            var response = await PipeMessageFraming.ReadAsync<ElevationResponse>(client);
            Assert.False(response!.Success);
            Assert.Contains("handler failed", response.ErrorMessage);
        }

        using (var client2 = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
        {
            await client2.ConnectAsync(2000);
            await PipeMessageFraming.WriteAsync(client2, new ElevationRequest("echo", "{}"));
            var response2 = await PipeMessageFraming.ReadAsync<ElevationResponse>(client2);
            Assert.True(response2!.Success);
        }

        cts.Cancel();
        Assert.False(serverTask.IsFaulted);
    }

    [Fact]
    public async Task RunAsync_ClientNotInAllowedSid_IsDeniedAccess()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var someoneElse = new SecurityIdentifier("S-1-5-21-1111111111-2222222222-3333333333-9999");
        var server = new ElevationServer(pipeName, new[] { new EchoHandler() }, TimeSpan.FromSeconds(5), someoneElse);
        var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.ConnectAsync(2000));

        cts.Cancel();
    }

    [Fact]
    public async Task RunAsync_DefaultAcl_DoesNotGrantEveryoneOrUsers()
    {
        var pipeName = "pt-test-" + Guid.NewGuid();
        var server = new ElevationServer(pipeName, new[] { new EchoHandler() }, TimeSpan.FromSeconds(5));
        var cts = new CancellationTokenSource();
        var serverTask = server.RunAsync(cts.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(2000);
        var rules = client.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>();
        var broad = new[] { WellKnownSidType.WorldSid, WellKnownSidType.BuiltinUsersSid, WellKnownSidType.AuthenticatedUserSid };

        Assert.DoesNotContain(rules, r => broad.Any(w => r.IdentityReference.Equals(new SecurityIdentifier(w, null)) && r.AccessControlType == AccessControlType.Allow));

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
