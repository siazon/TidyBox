using System.ComponentModel;
using System.Diagnostics;

namespace PowerTools.Modules.ContextMenu.Classic;

public interface IExplorerRestarter
{
    Task RestartAsync(CancellationToken ct = default);
}

internal sealed record ExplorerProcess(int Id, int SessionId);

/// <summary>The few process operations the restarter needs, so its rules can be tested without ending a real Explorer.</summary>
internal interface IExplorerProcessApi
{
    int CurrentSessionId { get; }

    IReadOnlyList<ExplorerProcess> GetExplorerProcesses();

    void Kill(int id);

    void StartExplorer();
}

/// <summary>
/// Ends this session's explorer.exe so Windows brings the shell back, and only starts one itself if Windows has not
/// done so within a generous wait (starting a second one would open a stray window or leave two shells).
/// </summary>
public sealed class ExplorerRestarter : IExplorerRestarter
{
    private readonly IExplorerProcessApi _processes;
    private readonly TimeSpan _shellWait;
    private readonly TimeSpan _pollInterval;

    public ExplorerRestarter() : this(new SystemProcesses(), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250))
    {
    }

    internal ExplorerRestarter(IExplorerProcessApi processes, TimeSpan shellWait, TimeSpan pollInterval)
    {
        _processes = processes;
        _shellWait = shellWait;
        _pollInterval = pollInterval;
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        var session = _processes.CurrentSessionId;
        var ended = new HashSet<int>();
        foreach (var process in _processes.GetExplorerProcesses().Where(p => p.SessionId == session))
        {
            _processes.Kill(process.Id);
            ended.Add(process.Id);
        }

        var deadline = DateTime.UtcNow + _shellWait;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(_pollInterval, ct);

            // A process we just ended may still be listed for a moment; only a new one in our session is the shell.
            if (_processes.GetExplorerProcesses().Any(p => p.SessionId == session && !ended.Contains(p.Id))) return;
        }

        _processes.StartExplorer();
    }

    private sealed class SystemProcesses : IExplorerProcessApi
    {
        public int CurrentSessionId
        {
            get
            {
                using var current = Process.GetCurrentProcess();
                return current.SessionId;
            }
        }

        public IReadOnlyList<ExplorerProcess> GetExplorerProcesses()
        {
            var result = new List<ExplorerProcess>();
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    result.Add(new ExplorerProcess(process.Id, process.SessionId));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Exited between listing and inspecting.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return result;
        }

        public void Kill(int id)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                process.Kill();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
            {
                // Already gone, or not ours to end.
            }
        }

        public void StartExplorer() => Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
    }
}
