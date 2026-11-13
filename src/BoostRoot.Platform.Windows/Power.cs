using System.Runtime.InteropServices;
using BoostRoot.Core.Ui;

namespace BoostRoot.Platform.Windows;

public static partial class PowerAndSound
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(nint sound, nint module, uint flags);

    [LibraryImport("kernel32.dll")] private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint h);
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(nint h, uint flags, char* buf, ref int size);

    /// <summary>Exe path of a process via a limited-query handle (cheap; works without reading the module list).</summary>
    public static unsafe string? ProcessPath(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == 0) return null;
        try
        {
            var buf = stackalloc char[1024];
            var size = 1024;
            return QueryFullProcessImageName(h, 0, buf, ref size) ? new string(buf, 0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndMemory = 0x0004;
    private static nint _wav;

    public static BatteryState Battery() =>
        GetSystemPowerStatus(out var s) ? BatteryState.FromWin32(s.ACLineStatus, s.BatteryFlag, s.BatteryLifePercent) : new(false, true, null);

    /// <summary>Plays the boost chime asynchronously; the buffer stays pinned for the process lifetime (async play reads it later).</summary>
    public static void PlayBoost()
    {
        try
        {
            if (_wav == 0)
            {
                var bytes = BoostSound.Wav();
                var p = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, p, bytes.Length);
                _wav = p;
            }
            PlaySound(_wav, 0, SndAsync | SndNoDefault | SndMemory);
        }
        catch (Exception) { }
    }
}
