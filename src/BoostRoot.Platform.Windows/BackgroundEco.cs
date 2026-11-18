using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BoostRoot.Platform.Windows;

/// <summary>
/// Task Manager's "Efficiency mode" for known background apps while a game runs: Idle priority + EcoQoS.
/// Nothing is closed; everything is put back when the game ends. Voice/music apps and the game itself are never touched.
/// </summary>
public static partial class BackgroundEco
{
    /// <summary>Process names (without .exe) that are safe to slow down during a match.</summary>
    public static readonly string[] Targets =
    [
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi",
        "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper", "Battle.net",
        "OneDrive", "Dropbox", "GoogleDriveFS",
    ];

    public sealed record Changed(int Pid, ProcessPriorityClass Before);

    public static IReadOnlyList<Changed> Apply(IReadOnlyList<string> gameNames)
    {
        var skip = new HashSet<string>(gameNames.Select(n => Path.GetFileNameWithoutExtension(n)), StringComparer.OrdinalIgnoreCase);
        var done = new List<Changed>();
        foreach (var name in Targets)
        {
            if (skip.Contains(name)) continue;
            foreach (var p in Process.GetProcessesByName(name))
                using (p)
                    try
                    {
                        var before = p.PriorityClass;
                        p.PriorityClass = ProcessPriorityClass.Idle;
                        SetEco(p.Handle, true);
                        done.Add(new(p.Id, before));
                    }
                    catch (Exception) { } // access denied / exited: skip
        }
        return done;
    }

    public static void Restore(IEnumerable<Changed> changed)
    {
        foreach (var c in changed)
            try
            {
                using var p = Process.GetProcessById(c.Pid);
                p.PriorityClass = c.Before;
                SetEco(p.Handle, false);
            }
            catch (Exception) { } // closed meanwhile
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, uint size);

    private static void SetEco(IntPtr handle, bool on)
    {
        // ProcessPowerThrottling = 4; EXECUTION_SPEED = 1. Off = hand control back to Windows (mask 0).
        var s = new PowerThrottlingState { Version = 1, ControlMask = on ? 1u : 0u, StateMask = on ? 1u : 0u };
        SetProcessInformation(handle, 4, ref s, (uint)Marshal.SizeOf<PowerThrottlingState>());
    }
}
