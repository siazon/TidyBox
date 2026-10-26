using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace PowerTools.Core.Recycling;

public interface IRecycleBin
{
    void Delete(string folderPath);

    /// <summary>Empties the Recycle Bin on every drive, so space moved there by a previous Delete is actually freed.</summary>
    void Empty();
}

/// <summary>
/// Moves a folder to the Recycle Bin so the user can still restore it, and can empty it on request. Not unit-tested
/// against the real Recycle Bin (that would actually empty the developer's or CI machine's Recycle Bin); callers
/// are tested against a fake <see cref="IRecycleBin"/> instead.
/// </summary>
public sealed class ShellRecycleBin : IRecycleBin
{
    private const uint SherbNoconfirmation = 0x00000001;
    private const uint SherbNoprogressui = 0x00000002;
    private const uint SherbNosound = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    public void Delete(string folderPath)
        => FileSystem.DeleteDirectory(folderPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    public void Empty()
    {
        // pszRootPath = null empties the Recycle Bin on every drive, not just the one a particular item came from;
        // the caller is expected to have told the user this is a whole-machine action, not scoped to one cleanup.
        var hr = SHEmptyRecycleBinW(IntPtr.Zero, null, SherbNoconfirmation | SherbNoprogressui | SherbNosound);

        // S_OK (0) and S_FALSE (1, "already empty") both mean nothing is left to do; anything else is a real failure.
        if (hr is not (0 or 1))
        {
            throw new InvalidOperationException($"SHEmptyRecycleBinW failed with HRESULT 0x{hr:X8}.");
        }
    }
}
