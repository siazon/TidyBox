using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace PowerTools.Core.Recycling;

/// <param name="TotalBytes">Real size currently sitting in the Recycle Bin (every drive).</param>
/// <param name="ItemCount">Number of items currently in the Recycle Bin.</param>
public record RecycleBinUsage(long TotalBytes, int ItemCount);

public interface IRecycleBin
{
    void Delete(string folderPath);

    /// <summary>Empties the Recycle Bin on every drive, so space moved there by a previous Delete is actually freed.</summary>
    void Empty();

    /// <summary>How much the Recycle Bin (every drive) currently holds, so the user can decide whether to empty it.</summary>
    RecycleBinUsage GetUsage();
}

/// <summary>
/// Moves a folder to the Recycle Bin so the user can still restore it, and can report on or empty it on request.
/// Not unit-tested against the real Recycle Bin (that would actually empty/depend on the developer's or CI
/// machine's Recycle Bin); callers are tested against a fake <see cref="IRecycleBin"/> instead.
/// </summary>
public sealed class ShellRecycleBin : IRecycleBin
{
    private const uint SherbNoconfirmation = 0x00000001;
    private const uint SherbNoprogressui = 0x00000002;
    private const uint SherbNosound = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

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

    public RecycleBinUsage GetUsage()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var hr = SHQueryRecycleBinW(null, ref info);

        // An empty Recycle Bin is reported as a failure (E_FAIL) on some Windows versions rather than zero sizes.
        if (hr != 0) return new RecycleBinUsage(0, 0);

        return new RecycleBinUsage(info.i64Size, (int)info.i64NumItems);
    }
}
