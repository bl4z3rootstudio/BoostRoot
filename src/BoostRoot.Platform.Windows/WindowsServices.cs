using System.Diagnostics;
using System.Runtime.InteropServices;
using BoostRoot.Core.Platform;

namespace BoostRoot.Platform.Windows;

public sealed class WindowsProcessManager : IProcessManager
{
    public IReadOnlyList<ProcessInfo> List()
    {
        var list = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                string? path = null;
                try { path = p.MainModule?.FileName; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
                list.Add(new ProcessInfo(p.Id, p.ProcessName + ".exe", path));
            }
        }
        return list;
    }

    public bool Close(int processId, TimeSpan gracefulTimeout)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            if (p.CloseMainWindow() && p.WaitForExit(gracefulTimeout)) return true;
            if (p.HasExited) return true;
            p.Kill(entireProcessTree: false);
            return p.WaitForExit(TimeSpan.FromSeconds(3));
        }
        catch (ArgumentException) { return true; } // already gone
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static bool WithHandle(int id, Func<nint, int> action)
    {
        try
        {
            using var p = Process.GetProcessById(id);
            return action(p.Handle) == 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public bool Suspend(int processId) => WithHandle(processId, NativeMethods.NtSuspendProcess);
    public bool Resume(int processId) => WithHandle(processId, NativeMethods.NtResumeProcess);

    public bool SetHighPriority(int processId)
    {
        try
        {
            using var p = Process.GetProcessById(processId);
            p.PriorityClass = ProcessPriorityClass.High;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public void Start(string fileName, string? arguments, bool useShellExecute) =>
        Process.Start(new ProcessStartInfo(fileName, arguments ?? "") { UseShellExecute = useShellExecute })?.Dispose();
}

public sealed class WindowsPowerPlans : IPowerPlans
{
    public Guid? GetActive()
    {
        if (NativeMethods.PowerGetActiveScheme(0, out var ptr) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { NativeMethods.LocalFree(ptr); }
    }

    public bool SetActive(Guid scheme) => NativeMethods.PowerSetActiveScheme(0, scheme) == 0;
}

public static class RestorePoint
{
    /// <summary>Creates a "Modify settings" restore point. Returns false if System Protection is off or creation failed.</summary>
    public static bool Create(string description)
    {
        var info = new NativeMethods.RESTOREPOINTINFOW
        {
            dwEventType = 100, // BEGIN_SYSTEM_CHANGE
            dwRestorePtType = 12, // MODIFY_SETTINGS
            llSequenceNumber = 0,
            szDescription = description.Length > 255 ? description[..255] : description,
        };
        if (!NativeMethods.SRSetRestorePointW(ref info, out var status)) return false;
        info.dwEventType = 101; // END_SYSTEM_CHANGE
        info.llSequenceNumber = status.llSequenceNumber;
        return NativeMethods.SRSetRestorePointW(ref info, out _);
    }
}

public static class WindowsEnvironment
{
    public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);

    public static IReadOnlyList<string> FixedDriveRoots() =>
        [.. DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name.TrimEnd('\\'))];

    public static Games.LocatorEnvironment Locators(Core.IO.IFileSystem fs, IRegistry registry) => new(
        fs, registry,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FixedDriveRoots());
}
