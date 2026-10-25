using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Management.Deployment;
using Windows.Storage.Streams;

namespace PowerTools.Modules.Uninstaller.Icons;

/// <summary>Reads icons through the Windows shell, so the result is the same one Explorer and the Start menu show.</summary>
public sealed class ShellIconExtractor : IIconExtractor
{
    private const int SiigbfDefault = 0;
    private const int SiigbfIconOnly = 4;

    public async Task<byte[]?> ExtractPngAsync(IconTarget target, int size, CancellationToken ct)
    {
        try
        {
            var bitmap = target.PackageFullName is not null
                ? await StoreAppBitmapAsync(target.PackageFullName, size, ct)
                : await Task.Run(() => FileBitmap(target.File!, target.Index, size), ct);
            return bitmap is null ? null : await EncodePngAsync(bitmap.Value, ct);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // A Store app's icon must be asked for without SIIGBF_ICONONLY, which answers with a generic blank-page icon instead.
    private static async Task<PixelBitmap?> StoreAppBitmapAsync(string packageFullName, int size, CancellationToken ct)
    {
        var package = new PackageManager().FindPackageForUser("", packageFullName);
        if (package is null) return null;

        var entries = await package.GetAppListEntriesAsync().AsTask(ct);
        var aumid = entries.FirstOrDefault()?.AppUserModelId;
        return aumid is null ? null : await Task.Run(() => ShellBitmap(@"shell:AppsFolder\" + aumid, size, SiigbfDefault), ct);
    }

    private static PixelBitmap? FileBitmap(string file, int index, int size)
    {
        if (!File.Exists(file)) return null;

        var extension = Path.GetExtension(file);
        var plainFile = index == 0 && (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase));
        return plainFile ? ShellBitmap(file, size, SiigbfIconOnly) : IndexedIconBitmap(file, index, size);
    }

    private static PixelBitmap? ShellBitmap(string parsingName, int bitmapSize, int flags)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory) != 0) return null;

        try
        {
            factory.GetImage(new Size { Cx = bitmapSize, Cy = bitmapSize }, flags, out var bitmap);
            return ReadAndFree(bitmap, hasAlphaGuarantee: true);
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    private static PixelBitmap? IndexedIconBitmap(string file, int index, int size)
    {
        var icons = new IntPtr[1];
        if (PrivateExtractIcons(file, index, size, size, icons, null, 1, 0) == 0 || icons[0] == IntPtr.Zero) return null;

        try
        {
            if (!GetIconInfo(icons[0], out var info)) return null;
            if (info.Mask != IntPtr.Zero) DeleteObject(info.Mask);
            return info.Color == IntPtr.Zero ? null : ReadAndFree(info.Color, hasAlphaGuarantee: false);
        }
        finally
        {
            DestroyIcon(icons[0]);
        }
    }

    private static PixelBitmap? ReadAndFree(IntPtr bitmap, bool hasAlphaGuarantee)
    {
        if (bitmap == IntPtr.Zero) return null;

        try
        {
            if (GetObject(bitmap, Marshal.SizeOf<Bitmap>(), out var info) == 0) return null;

            var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = info.Width, Height = -info.Height, Planes = 1, BitCount = 32 };
            var pixels = new byte[info.Width * info.Height * 4];
            var dc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(dc, bitmap, 0, (uint)info.Height, pixels, ref header, 0) == 0) return null;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }

            // An old-style icon (colour plus mask) comes back without any alpha; show whatever is drawn instead of nothing.
            if (!hasAlphaGuarantee && !HasAlpha(pixels))
            {
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0) pixels[i + 3] = 255;
                }
            }

            return new PixelBitmap(info.Width, info.Height, pixels);
        }
        finally
        {
            DeleteObject(bitmap);
        }
    }

    private static bool HasAlpha(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0) return true;
        }

        return false;
    }

    private static async Task<byte[]> EncodePngAsync(PixelBitmap bitmap, CancellationToken ct)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(ct);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.Width, (uint)bitmap.Height, 96, 96, bitmap.Bgra);
        await encoder.FlushAsync().AsTask(ct);

        var png = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(png.AsBuffer(), (uint)stream.Size, InputStreamOptions.None).AsTask(ct);
        return png;
    }

    private readonly record struct PixelBitmap(int Width, int Height, byte[] Bgra);

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        void GetImage(Size size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int Cx; public int Cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap { public int Type; public int Width; public int Height; public int WidthBytes; public short Planes; public short BitsPixel; public IntPtr Bits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size; public int Width; public int Height; public short Planes; public short BitCount;
        public int Compression; public int SizeImage; public int XPelsPerMeter; public int YPelsPerMeter; public int ClrUsed; public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo { public bool IsIcon; public int XHotspot; public int YHotspot; public IntPtr Mask; public IntPtr Color; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItemImageFactory factory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, uint[]? ids, uint count, uint flags);

    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr obj, int size, out Bitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
}
