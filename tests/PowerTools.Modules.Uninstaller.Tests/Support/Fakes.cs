using PowerTools.Modules.Uninstaller.Routing;

namespace PowerTools.Modules.Uninstaller.Tests.Support;

public sealed class FakeRunner : IProcessRunner
{
    public (string FileName, string Arguments)? Started;
    public bool StartedAsAdministrator;
    public Func<int> Result = () => 0;

    public Task<int> RunAndWaitAsync(string fileName, string arguments, CancellationToken ct, bool asAdministrator = false)
    {
        Started = (fileName, arguments);
        StartedAsAdministrator = asAdministrator;
        return Task.FromResult(Result());
    }
}

public sealed class FakeStore : IStoreRemover
{
    public string? Removed;
    public Exception? Throw;

    public Task RemoveAsync(string packageFullName, CancellationToken ct)
    {
        Removed = packageFullName;
        return Throw is null ? Task.CompletedTask : Task.FromException(Throw);
    }
}

public sealed class FakeUrls : IUrlLauncher
{
    public Uri? Opened;

    public void Open(Uri uri) => Opened = uri;
}
