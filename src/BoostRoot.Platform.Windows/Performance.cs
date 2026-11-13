using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BoostRoot.Platform.Windows;

/// <summary>Frees RAM: trims other processes' working sets and purges the standby list (what "RAM cleaner" tools do).
/// Nothing is closed; Windows simply pages back in what a process still needs.</summary>
public static partial class RamCleaner
{
    [LibraryImport("psapi.dll", EntryPoint = "EmptyWorkingSet")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(nint process);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetSystemInformation(int infoClass, ref int info, int length);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? system, string name, out long luid);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attributes; }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TOKEN_PRIVILEGES state, int len, nint prev, nint retLen);

    [LibraryImport("kernel32.dll")] private static partial nint GetCurrentProcess();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint h);

    private static void Enable(string privilege)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out var token)) return; // ADJUST_PRIVILEGES | QUERY
        if (LookupPrivilegeValue(null, privilege, out var luid))
        {
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };
            AdjustTokenPrivileges(token, false, ref tp, 0, 0, 0);
        }
        CloseHandle(token);
    }

    /// <param name="keep">Process names (without .exe) not to trim, e.g. the running game.</param>
    /// <returns>Bytes of available RAM gained.</returns>
    public static long Clean(IReadOnlyCollection<string> keep)
    {
        var before = (long)HardwareScanner.AvailableRamBytes();
        Enable("SeProfileSingleProcessPrivilege");
        Enable("SeIncreaseQuotaPrivilege");
        foreach (var p in Process.GetProcesses())
        {
            try { if (!keep.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase) && p.Id > 4) EmptyWorkingSet(p.Handle); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            finally { p.Dispose(); }
        }
        var purge = 4; // MemoryPurgeStandbyList
        NtSetSystemInformation(80, ref purge, sizeof(int)); // SystemMemoryListInformation
        return Math.Max(0, (long)HardwareScanner.AvailableRamBytes() - before);
    }
}

/// <summary>0.5 ms system timer while the game runs (smoother frame pacing on many games); released on exit.</summary>
public static partial class TimerResolution
{
    [LibraryImport("ntdll.dll")]
    private static partial int NtSetTimerResolution(uint desired, [MarshalAs(UnmanagedType.U1)] bool set, out uint current);

    public static bool Set() => NtSetTimerResolution(5000, true, out _) == 0;
    public static void Release() => NtSetTimerResolution(5000, false, out _);
}
