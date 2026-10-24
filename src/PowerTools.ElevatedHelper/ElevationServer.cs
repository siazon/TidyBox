using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PowerTools.Core.Elevation;

namespace PowerTools.ElevatedHelper;

public class ElevationServer
{
    private readonly string _pipeName;
    private readonly IReadOnlyDictionary<string, IElevatedOperationHandler> _handlers;
    private readonly TimeSpan _idleTimeout;

    public ElevationServer(string pipeName, IEnumerable<IElevatedOperationHandler> handlers, TimeSpan idleTimeout)
    {
        _pipeName = pipeName;
        _handlers = handlers.ToDictionary(h => h.OperationType);
        _idleTimeout = idleTimeout;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var pipeServer = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            // Link the idle timeout directly into the token handed to
            // WaitForConnectionAsync so the pending accept is actually
            // cancelled (via CancelIoEx) when the timeout elapses, instead
            // of merely losing a Task.WhenAny race while it keeps running
            // in the background.
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idleCts.CancelAfter(_idleTimeout);

            try
            {
                await pipeServer.WaitForConnectionAsync(idleCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Either the caller cancelled RunAsync, or no client connected
                // within the idle timeout. Either way, exit the accept loop.
                return;
            }

            await HandleConnectionAsync(pipeServer, ct);
        }
    }

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
                response = await handler.HandleAsync(request.PayloadJson);
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
        catch (IOException)
        {
            // Client disconnected unexpectedly; move on to the next connection.
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
