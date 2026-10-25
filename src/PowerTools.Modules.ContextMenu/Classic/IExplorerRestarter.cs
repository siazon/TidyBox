using System.ComponentModel;
using System.Diagnostics;

namespace PowerTools.Modules.ContextMenu.Classic;

public interface IExplorerRestarter
{
    Task RestartAsync(CancellationToken ct = default);
}

/// <summary>Ends every explorer.exe of this session so Windows brings the shell back; starts it if Windows does not.</summary>
public sealed class ExplorerRestarter : IExplorerRestarter
{
    public async Task RestartAsync(CancellationToken ct = default)
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // Already gone, or not ours to end.
            }
            finally
            {
                process.Dispose();
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        var running = Process.GetProcessesByName("explorer");
        var anyRunning = running.Length > 0;
        foreach (var process in running) process.Dispose();
        if (!anyRunning) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
    }
}
