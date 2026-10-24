using System.IO.Pipes;

namespace PowerTools.Core.Elevation;

public class ElevationClient : IElevationClient
{
    private readonly string _pipeName;
    private readonly IHelperProcessLauncher _launcher;
    private readonly TimeSpan _connectTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ElevationClient(string pipeName, IHelperProcessLauncher launcher, TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName;
        _launcher = launcher;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5);
    }

    // A fixed pipe name could be squatted by another process before the helper starts; use a random one per app session.
    public static string NewSessionPipeName() => "PowerTools." + Guid.NewGuid().ToString("N");

    // Serialized so concurrent callers cannot each decide the helper is not running and each show a UAC prompt.
    public async Task<ElevationResponse> ExecuteAsync(ElevationRequest request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await ExecuteCoreAsync(request, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ElevationResponse> ExecuteCoreAsync(ElevationRequest request, CancellationToken ct)
    {
        if (!_launcher.IsHelperRunning(_pipeName) && !TryLaunch(out var launchError))
        {
            return new ElevationResponse(false, launchError, null);
        }

        NamedPipeClientStream? pipe;
        try
        {
            pipe = await TryConnectAsync(ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            // The pipe exists but its ACL does not allow us; relaunching would not help.
            return new ElevationResponse(false, ex.Message, null);
        }

        if (pipe is null)
        {
            // The helper may have exited after its idle timeout; treat it as not running and relaunch once.
            if (!TryLaunch(out var relaunchError))
            {
                return new ElevationResponse(false, relaunchError, null);
            }

            try
            {
                pipe = await TryConnectAsync(ct);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new ElevationResponse(false, ex.Message, null);
            }

            if (pipe is null)
            {
                return new ElevationResponse(false, "could not connect to elevated helper", null);
            }
        }

        using (pipe)
        {
            try
            {
                await PipeMessageFraming.WriteAsync(pipe, request, ct);
                return await PipeMessageFraming.ReadAsync<ElevationResponse>(pipe, ct)
                    ?? new ElevationResponse(false, "empty response from elevated helper", null);
            }
            catch (IOException ex)
            {
                return new ElevationResponse(false, ex.Message, null);
            }
        }
    }

    private bool TryLaunch(out string? error)
    {
        try
        {
            _launcher.LaunchElevated(_pipeName);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private async Task<NamedPipeClientStream?> TryConnectAsync(CancellationToken ct)
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)_connectTimeout.TotalMilliseconds, ct);
            return pipe;
        }
        catch (TimeoutException)
        {
            pipe.Dispose();
            return null;
        }
    }
}
