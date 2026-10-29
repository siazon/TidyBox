using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;

namespace TidyBox.Core.Recycling;

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

    // SHQUERYRBINFO's native size is bitness-dependent: 24 bytes on x64 (the DWORD is padded so the following
    // __int64 lands on an 8-byte boundary) but 20 bytes on x86 (no padding). Marshalling it with a single
    // always-24-byte layout works on x64 but silently returns garbage/zeroed values on a 32-bit process
    // (confirmed empirically: HRESULT still came back S_OK, with i64NumItems=0 and a bogus i64Size, even though
    // the Recycle Bin had real content) — this is why TidyBox.App, which is a 32-bit (x86) process, reported
    // "empty" on a Recycle Bin that was not.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct SHQUERYRBINFO_X86
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO_X64
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
    private static extern int SHQueryRecycleBinW_X86(string? pszRootPath, ref SHQUERYRBINFO_X86 pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
    private static extern int SHQueryRecycleBinW_X64(string? pszRootPath, ref SHQUERYRBINFO_X64 pSHQueryRBInfo);

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
        if (Environment.Is64BitProcess)
        {
            var info = new SHQUERYRBINFO_X64 { cbSize = Marshal.SizeOf<SHQUERYRBINFO_X64>() };
            var hr = SHQueryRecycleBinW_X64(null, ref info);
            return hr == 0 ? new RecycleBinUsage(info.i64Size, (int)info.i64NumItems) : new RecycleBinUsage(0, 0);
        }
        else
        {
            var info = new SHQUERYRBINFO_X86 { cbSize = Marshal.SizeOf<SHQUERYRBINFO_X86>() };
            var hr = SHQueryRecycleBinW_X86(null, ref info);
            return hr == 0 ? new RecycleBinUsage(info.i64Size, (int)info.i64NumItems) : new RecycleBinUsage(0, 0);
        }
    }
}
