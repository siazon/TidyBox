using System.ComponentModel;
using PowerTools.Modules.Uninstaller.Model;

namespace PowerTools.Modules.Uninstaller.Routing;

public enum UninstallStatus { Completed, Failed, MaybeStillRunning, NotAvailable }

/// <param name="NeedsAdministrator">The uninstaller refused to start for this user; running it as administrator may work.</param>
public record UninstallOutcome(UninstallStatus Status, int? ExitCode, string? Message, bool NeedsAdministrator = false);

/// <summary>One "uninstall" entry point; the app's source decides which of the three mechanisms does the work.</summary>
public sealed class UninstallRouter
{
    private const int ErrorCancelled = 1223; // the user declined the UAC prompt
    private const int ErrorAccessDenied = 5; // the file is there, but this user may not run it

    private static readonly char[] ShellOperators = { '&', '|', '<', '>', '^', '%', '\r', '\n' };

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

    /// <param name="asAdministrator">Only for desktop programs: retry through an elevated command prompt, for uninstallers the current user may not run.</param>
    public Task<UninstallOutcome> UninstallAsync(InstalledApp app, bool asAdministrator = false, CancellationToken ct = default) => app.Source switch
    {
        AppSource.Steam => Task.FromResult(UninstallSteam(app)),
        AppSource.Store => UninstallStoreAsync(app, ct),
        _ => UninstallWin32Async(app, asAdministrator, ct),
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
        // Opening the URL only hands off to Steam's own confirmation window; the actual removal happens in the
        // background on Steam's own schedule, so this is "maybe still running", not "done" (same as MSI/Squirrel).
        return new UninstallOutcome(UninstallStatus.MaybeStillRunning, null, "已交给 Steam 处理，请在 Steam 的窗口中确认；完成后列表会自动刷新。");
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

    private async Task<UninstallOutcome> UninstallWin32Async(InstalledApp app, bool asAdministrator, CancellationToken ct)
    {
        var command = UninstallCommandParser.Parse(app.UninstallString);
        if (command is null)
        {
            return new UninstallOutcome(UninstallStatus.NotAvailable, null, "这个程序没有登记卸载命令。");
        }

        var (file, arguments) = (command.FileName, command.Arguments);
        if (asAdministrator)
        {
            // cmd would act on these in a command line that came from the registry, now with administrator rights.
            if (file.IndexOfAny(ShellOperators) >= 0 || arguments.IndexOfAny(ShellOperators) >= 0)
            {
                return new UninstallOutcome(UninstallStatus.Failed, null, "这个程序的卸载命令含有特殊字符，无法安全地以管理员身份运行。");
            }

            // The outer quotes are the ones cmd /c strips, so a path with spaces keeps its own.
            var line = $"\"{command.FileName}\" {command.Arguments}".TrimEnd();
            (file, arguments) = ("cmd.exe", $"/c \"{line}\"");
        }

        int exitCode;
        try
        {
            exitCode = await _runner.RunAndWaitAsync(file, arguments, ct, asAdministrator);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new UninstallOutcome(UninstallStatus.Failed, null, "已取消（未授予管理员权限）。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorAccessDenied && !asAdministrator)
        {
            return new UninstallOutcome(UninstallStatus.Failed, null, "当前用户没有权限运行这个程序的卸载程序（通常只有管理员能运行）。", NeedsAdministrator: true);
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
