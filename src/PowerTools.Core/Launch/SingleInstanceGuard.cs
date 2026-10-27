namespace PowerTools.Core.Launch;

/// <summary>
/// Decides which copy of the app is "the" running one. The first to create the named mutex is primary; later copies
/// are not and should hand their request to the primary and exit.
/// </summary>
/// <remarks>
/// The mutex is never waited on or released: it only serves as a name that exists while the primary lives. Mutex
/// ownership is tied to a thread, and the thread that starts the app need not be the one that closes it. Closing the
/// handle is enough; the system removes the name once the last handle is gone.
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;

    /// <summary>One per Windows session, so each signed-in user has their own.</summary>
    public static string DefaultName => @"Local\PowerTools.SingleInstance";

    public SingleInstanceGuard(string name)
    {
        _mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void Dispose() => _mutex.Dispose();
}
