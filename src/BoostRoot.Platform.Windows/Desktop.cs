using System.Runtime.InteropServices;

namespace BoostRoot.Platform.Windows;

/// <summary>Global hotkeys on a private message-only window thread (RegisterHotKey — no keyboard hook,
/// nothing touches the game's input).</summary>
public sealed partial class HotkeyService : IDisposable
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, NoRepeat = 0x4000;
    private readonly Thread _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _ready = new();
    public event Action<int>? Pressed;

    /// <param name="keys">(id, modifiers, virtual-key)</param>
    public HotkeyService(IReadOnlyList<(int Id, uint Mods, uint Vk)> keys)
    {
        _thread = new Thread(() => Loop(keys)) { IsBackground = true, Name = "BoostRoot hotkeys" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    private void Loop(IReadOnlyList<(int Id, uint Mods, uint Vk)> keys)
    {
        _threadId = GetCurrentThreadId();
        foreach (var k in keys) RegisterHotKey(0, k.Id, k.Mods | NoRepeat, k.Vk);
        _ready.Set();
        while (GetMessageW(out var msg, 0, 0, 0) > 0)
            if (msg.message == 0x0312) Pressed?.Invoke((int)msg.wParam); // WM_HOTKEY
        foreach (var k in keys) UnregisterHotKey(0, k.Id);
    }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, 0x0012, 0, 0); // WM_QUIT
        _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool RegisterHotKey(nint hWnd, int id, uint mods, uint vk);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool UnregisterHotKey(nint hWnd, int id);
    [LibraryImport("user32.dll")] private static partial int GetMessageW(out MSG msg, nint hWnd, uint min, uint max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostThreadMessageW(uint thread, uint msg, nint w, nint l);
    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
}

public static partial class WindowStyles
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;

    /// <summary>Makes an overlay window ignore the mouse, never take focus and stay out of Alt+Tab.</summary>
    public static void MakeOverlay(nint hwnd)
    {
        var ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, ex | (nint)(WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST));
        SetWindowPos(hwnd, -1, 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x20); // HWND_TOPMOST, NOSIZE|NOMOVE|NOACTIVATE|FRAMECHANGED
        SetWindowDisplayAffinity(hwnd, 0x11); // WDA_EXCLUDEFROMCAPTURE: overlay never shows in clips
    }

    [LibraryImport("user32.dll")] private static partial nint GetWindowLongPtrW(nint hWnd, int index);
    [LibraryImport("user32.dll")] private static partial nint SetWindowLongPtrW(nint hWnd, int index, nint value);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowDisplayAffinity(nint hWnd, uint affinity);
}

/// <summary>Temporary desktop mode for 4:3 stretched. CDS_FULLSCREEN = not saved to the registry:
/// Windows restores the normal resolution when we call Restore() or when BoostRoot exits/crashes.</summary>
public static partial class DisplayChanger
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DEVMODEW
    {
        public fixed char dmDeviceName[32];
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        public fixed char dmFormName[32];
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    private const uint DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000, DM_DISPLAYFIXEDOUTPUT = 0x20000000;
    private const uint CDS_FULLSCREEN = 0x4, DMDFO_STRETCH = 1;

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettings(string? device, int mode, ref DEVMODEW dm);

    [LibraryImport("user32.dll", EntryPoint = "ChangeDisplaySettingsW")]
    private static partial int ChangeDisplaySettings(ref DEVMODEW dm, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "ChangeDisplaySettingsW")]
    private static partial int ChangeDisplaySettingsReset(nint dm, uint flags);

    public static (int Width, int Height, int Hz)? Current()
    {
        var dm = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
        return EnumDisplaySettings(null, -1, ref dm) ? ((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency) : null;
    }

    /// <summary>Switches the primary display to width×height at the current refresh rate, stretched to full panel.</summary>
    public static bool SetStretched(int width, int height)
    {
        var dm = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
        if (!EnumDisplaySettings(null, -1, ref dm)) return false;
        dm.dmPelsWidth = (uint)width;
        dm.dmPelsHeight = (uint)height;
        dm.dmDisplayFixedOutput = DMDFO_STRETCH;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_DISPLAYFIXEDOUTPUT;
        if (ChangeDisplaySettings(ref dm, CDS_FULLSCREEN) == 0) return true;
        dm.dmFields &= ~DM_DISPLAYFIXEDOUTPUT; // some drivers reject the scaling flag; GPU scaling setting then decides
        return ChangeDisplaySettings(ref dm, CDS_FULLSCREEN) == 0;
    }

    public static void Restore() => ChangeDisplaySettingsReset(0, 0);
}
