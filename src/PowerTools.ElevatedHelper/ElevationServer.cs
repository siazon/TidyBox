using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using PowerTools.Core.Elevation;

namespace PowerTools.ElevatedHelper;

public class ElevationServer
{
    private readonly string _pipeName;
    private readonly IReadOnlyDictionary<string, IElevatedOperationHandler> _handlers;
    private readonly TimeSpan _idleTimeout;
    private readonly PipeSecurity _pipeSecurity;

    /// <param name="allowedClient">
    /// The only identity allowed to connect. It is passed in because the helper
    /// may run as a different admin account than the app that launched it; defaults to this process's user.
    /// </param>
    public ElevationServer(string pipeName, IEnumerable<IElevatedOperationHandler> handlers, TimeSpan idleTimeout, SecurityIdentifier? allowedClient = null)
    {
        _pipeName = pipeName;
        _handlers = handlers.ToDictionary(h => h.OperationType);
        _idleTimeout = idleTimeout;

        // Default pipe ACLs let other local users/processes talk to an elevated process; only the allowed client may read/write.
        _pipeSecurity = new PipeSecurity();
        var self = WindowsIdentity.GetCurrent().User!;
        _pipeSecurity.AddAccessRule(new PipeAccessRule(self, PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        _pipeSecurity.AddAccessRule(new PipeAccessRule(allowedClient ?? self, PipeAccessRights.ReadWrite, AccessControlType.Allow));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Always keep one instance listening: the next one is created before the current connection is handled, so
        // the pipe name never disappears between requests (clients use its existence to decide whether to launch us).
        // FirstPipeInstance makes a second helper on the same pipe name fail fast instead of sharing it.
        var listener = CreatePipe(PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Link the idle timeout into the token handed to WaitForConnectionAsync so the pending accept is
                // actually cancelled (via CancelIoEx) when it elapses, instead of losing a Task.WhenAny race.
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idleCts.CancelAfter(_idleTimeout);

                try
                {
                    await listener.WaitForConnectionAsync(idleCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Either the caller cancelled RunAsync, or no client connected
                    // within the idle timeout. Either way, exit the accept loop.
                    return;
                }
                catch (IOException)
                {
                    // A client connected and vanished before we accepted it (e.g. a probe or a crashed client).
                    // Replace the broken instance and keep listening instead of taking the helper down.
                    var broken = listener;
                    listener = CreatePipe(PipeOptions.Asynchronous);
                    broken.Dispose();
                    continue;
                }

                var connected = listener;
                listener = CreatePipe(PipeOptions.Asynchronous);
                using (connected)
                {
                    await HandleConnectionAsync(connected, ct);
                }
            }
        }
        finally
        {
            listener.Dispose();
        }
    }

    private NamedPipeServerStream CreatePipe(PipeOptions options)
        => NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            2,
            PipeTransmissionMode.Byte,
            options,
            0,
            0,
            _pipeSecurity);

    private async Task HandleConnectionAsync(NamedPipeServerStream pipeServer, CancellationToken ct)
    {
        try
        {
            var request = await PipeMessageFraming.ReadAsync<ElevationRequest>(pipeServer, ct);
            if (request is null)
            {
                return;
            }

            ElevationResponse response;
            if (_handlers.TryGetValue(request.OperationType, out var handler))
            {
                try
                {
                    response = await handler.HandleAsync(request.PayloadJson);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    response = new ElevationResponse(false, ex.Message, null);
                }
            }
            else
            {
                response = new ElevationResponse(false, $"unknown operation: {request.OperationType}", null);
            }

            await PipeMessageFraming.WriteAsync(pipeServer, response, ct);
            pipeServer.WaitForPipeDrain();
        }
        catch (OperationCanceledException)
        {
            // Server is shutting down; drop the in-flight connection.
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            // Client disconnected or sent a malformed frame; drop this connection and move on.
        }
        finally
        {
            if (pipeServer.IsConnected)
            {
                pipeServer.Disconnect();
            }
        }
    }
}
