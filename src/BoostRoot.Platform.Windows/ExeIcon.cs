using System.Runtime.InteropServices;

namespace BoostRoot.Platform.Windows;

/// <summary>Reads the largest icon embedded in a game's .exe (the user's own install) as 32-bit BGRA pixels.</summary>
public static partial class ExeIcon
{
    public sealed record Pixels(int Width, int Height, byte[] Bgra);

    public static unsafe Pixels? Extract(string exePath, int size = 256)
    {
        if (!File.Exists(exePath)) return null;
        nint icon = 0;
        try
        {
            if (PrivateExtractIconsW(exePath, 0, size, size, &icon, null, 1, 0) == 0 || icon == 0) return null;
            if (!GetIconInfo(icon, out var info)) return null;
            try
            {
                if (info.hbmColor == 0) return null;
                var bi = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
                var buf = new byte[size * size * 4];
                var dc = GetDC(0);
                try
                {
                    fixed (byte* p = buf)
                        if (GetDIBits(dc, info.hbmColor, 0, (uint)size, p, &bi, 0) == 0) return null;
                }
                finally { ReleaseDC(0, dc); }
                // Old icons have no alpha channel: treat as opaque.
                var anyAlpha = false;
                for (var i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { anyAlpha = true; break; }
                if (!anyAlpha) for (var i = 3; i < buf.Length; i += 4) buf[i] = 255;
                return new Pixels(size, size, buf);
            }
            finally
            {
                if (info.hbmColor != 0) DeleteObject(info.hbmColor);
                if (info.hbmMask != 0) DeleteObject(info.hbmMask);
            }
        }
        catch (Exception) { return null; }
        finally { if (icon != 0) DestroyIcon(icon); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO { public int fIcon; public int xHotspot, yHotspot; public nint hbmMask, hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, nint* icons, uint* ids, uint count, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetIconInfo(nint icon, out ICONINFO info);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint dc);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint obj);
    [LibraryImport("gdi32.dll")] private static unsafe partial int GetDIBits(nint dc, nint bmp, uint start, uint lines, void* bits, BITMAPINFOHEADER* info, uint usage);
}
