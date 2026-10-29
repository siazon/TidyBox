using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace TidyBox.Core.Launch;

/// <summary>
/// The running app listens here for "analyze this folder" requests. A second copy started by the right-click menu hands its
/// request over and exits instead of opening another window.
/// </summary>
public sealed class AnalyzeRequestListener : IDisposable
{
    // A folder path is far shorter; anything bigger is not a request.
    private const int MaxMessageBytes = 32 * 1024;

    private readonly string _pipeName;
    private readonly Action<string> _onRequest;
    private readonly CancellationTokenSource _stop = new();

    public AnalyzeRequestListener(string pipeName, Action<string> onRequest)
    {
        _pipeName = pipeName;
        _onRequest = onRequest;
    }

    /// <summary>One name per Windows user, so one person's app never answers another's menu.</summary>
    public static string DefaultPipeName => "TidyBox.AnalyzeRequests." + WindowsIdentity.GetCurrent().User!.Value;

    /// <summary>Starts listening. The pipe exists before this returns, so a request sent right after is not lost.</summary>
    public void Start()
    {
        var first = NewServer();
        _ = Task.Run(() => RunAsync(first));
    }

    public void Dispose() => _stop.Cancel();

    private NamedPipeServerStream NewServer()
        => new(_pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private async Task RunAsync(NamedPipeServerStream server)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await server.WaitForConnectionAsync(_stop.Token);
                var message = await ReadAsync(server, _stop.Token);
                server.Dispose();
                server = NewServer();
                if (message is not null) Deliver(message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or UnauthorizedAccessException)
            {
                // A client that vanished mid-message must not stop the listener; start from a fresh pipe.
                server.Dispose();
                if (_stop.IsCancellationRequested) return;
                server = NewServer();
            }
        }

        server.Dispose();
    }

    // The handler is the app's own code. If it throws, only that request is lost, and the pipe, which may already be
    // talking to the next client, is left alone.
    private void Deliver(string message)
    {
        try
        {
            _onRequest(message);
        }
        catch (Exception)
        {
        }
    }

    /// <returns>The text sent, or null when it was empty or too large to be a request.</returns>
    private static async Task<string?> ReadAsync(Stream pipe, CancellationToken ct)
    {
        var buffer = new byte[MaxMessageBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await pipe.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0) break;
            length += read;
        }

        if (length > MaxMessageBytes) return null;

        var text = Encoding.UTF8.GetString(buffer, 0, length).Trim();
        return text.Length == 0 || text.Contains('\0') ? null : text;
    }
}

public static class AnalyzeRequestSender
{
    /// <returns>True when a running app took the request; false when none is listening (within <paramref name="timeout"/>).</returns>
    public static bool TrySend(string pipeName, string path, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect((int)timeout.TotalMilliseconds);
            client.Write(Encoding.UTF8.GetBytes(path));
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
