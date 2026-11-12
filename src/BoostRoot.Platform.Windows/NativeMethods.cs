using System.Runtime.InteropServices;

namespace BoostRoot.Platform.Windows;

internal static partial class NativeMethods
{
    [LibraryImport("ntdll.dll")]
    internal static partial int NtSuspendProcess(nint processHandle);

    [LibraryImport("ntdll.dll")]
    internal static partial int NtResumeProcess(nint processHandle);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetActiveScheme(nint userRootPowerKey, out nint activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerSetActiveScheme(nint userRootPowerKey, in Guid schemeGuid);

    [LibraryImport("kernel32.dll")]
    internal static partial nint LocalFree(nint hMem);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RESTOREPOINTINFOW
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STATEMGRSTATUS
    {
        public int nStatus;
        public long llSequenceNumber;
    }

#pragma warning disable SYSLIB1054 // struct with ByValTStr needs classic marshalling
    [DllImport("srclient.dll", CharSet = CharSet.Unicode)]
    internal static extern bool SRSetRestorePointW(ref RESTOREPOINTINFOW restorePtSpec, out STATEMGRSTATUS stateMgrStatus);
#pragma warning restore SYSLIB1054
}
