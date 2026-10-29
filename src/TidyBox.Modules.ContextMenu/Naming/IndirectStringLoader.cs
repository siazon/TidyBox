using System.Runtime.InteropServices;
using System.Text;

namespace TidyBox.Modules.ContextMenu.Naming;

public interface IIndirectStringLoader
{
    /// <summary>Expands <c>@dll,-id</c> style references; plain text is returned unchanged; null if it cannot be loaded.</summary>
    string? Load(string indirect);
}

public sealed class IndirectStringLoader : IIndirectStringLoader
{
    public string? Load(string indirect)
    {
        if (!indirect.StartsWith('@')) return indirect;

        var buffer = new StringBuilder(512);
        return SHLoadIndirectString(indirect, buffer, buffer.Capacity, IntPtr.Zero) == 0 ? buffer.ToString() : null;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);
}
