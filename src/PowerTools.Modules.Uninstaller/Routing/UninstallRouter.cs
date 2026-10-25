using System.ComponentModel;
using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Routing;

public enum UninstallStatus { Completed, Failed, MaybeStillRunning, NotAvailable }

public record UninstallOutcome(UninstallStatus Status, int? ExitCode, string? Message);

/// <summary>One "uninstall" entry point; the app's source decides which of the three mechanisms does the work.</summary>
public sealed class UninstallRouter
{
    private const int ErrorCancelled = 1223; // the user declined the UAC prompt

    // Success codes of Windows Installer that still mean "done": reboot initiated / reboot required.
    private static readonly int[] RebootRequired = { 1641, 3010 };

    // These start the real work in another process and return at once, so "exited" does not mean "finished".
    private static readonly string[] HandOverLaunchers = { "msiexec.exe", "msiexec", "update.exe" };

    private readonly IProcessRunner _runner;
    private readonly IStoreRemover _store;
    private readonly IUrlLauncher _urls;

    public UninstallRouter(IProcessRunner runner, IStoreRemover store, IUrlLauncher urls)
    {
        _runner = runner;
        _store = store;
        _urls = urls;
    }

    public Task<UninstallOutcome> UninstallAsync(InstalledApp app, CancellationToken ct = default) => app.Source switch
    {
        AppSource.Steam => Task.FromResult(UninstallSteam(app)),
        AppSource.Store => UninstallStoreAsync(app, ct),
        _ => UninstallWin32Async(app, ct),
    };

    // The id ends up in a ShellExecute call and came from the registry, so accept digits only.
    private UninstallOutcome UninstallSteam(InstalledApp app)
    {
        var id = app.SteamAppId;
        if (string.IsNullOrEmpty(id) || !id.All(char.IsAsciiDigit))
        {
            return new UninstallOutcome(UninstallStatus.NotAvailable, null, "无法识别这个 Steam 游戏的编号。");
        }

        _urls.Open(new Uri("steam://uninstall/" + id));
        return new UninstallOutcome(UninstallStatus.Completed, null, "已交给 Steam 处理，请在 Steam 的窗口中确认。");
    }

    private async Task<UninstallOutcome> UninstallStoreAsync(InstalledApp app, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(app.PackageFullName))
        {
            return new UninstallOutcome(UninstallStatus.NotAvailable, null, "缺少应用包名称。");
        }

        try
        {
            await _store.RemoveAsync(app.PackageFullName, ct);
            return new UninstallOutcome(UninstallStatus.Completed, null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UninstallOutcome(UninstallStatus.Failed, null, $"卸载失败：{ex.Message}");
        }
    }

    private async Task<UninstallOutcome> UninstallWin32Async(InstalledApp app, CancellationToken ct)
    {
        var command = UninstallCommandParser.Parse(app.UninstallString);
        if (command is null)
        {
            return new UninstallOutcome(UninstallStatus.NotAvailable, null, "这个程序没有登记卸载命令。");
        }

        int exitCode;
        try
        {
            exitCode = await _runner.RunAndWaitAsync(command.FileName, command.Arguments, ct);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new UninstallOutcome(UninstallStatus.Failed, null, "已取消（未授予管理员权限）。");
        }
        catch (Win32Exception ex)
        {
            return new UninstallOutcome(UninstallStatus.Failed, null, $"无法启动卸载程序：{ex.Message}");
        }

        if (exitCode != 0 && !RebootRequired.Contains(exitCode))
        {
            return new UninstallOutcome(UninstallStatus.Failed, exitCode, $"卸载程序返回了错误代码 {exitCode}。");
        }

        var launcher = Path.GetFileName(command.FileName);
        return HandOverLaunchers.Contains(launcher, StringComparer.OrdinalIgnoreCase)
            ? new UninstallOutcome(UninstallStatus.MaybeStillRunning, exitCode, "卸载可能还在后台进行，稍后请刷新列表确认。")
            : new UninstallOutcome(UninstallStatus.Completed, exitCode, null);
    }
}
