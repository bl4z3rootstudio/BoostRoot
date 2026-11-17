using System.Diagnostics;

namespace BoostRoot.Platform.Windows;

/// <summary>Pauses Windows Update / BITS / Delivery Optimization while a game runs, then starts again only what was running.
/// Windows starts these services on demand anyway, so nothing stays broken if BoostRoot is killed.</summary>
public static class BackgroundDownloads
{
    private static readonly string[] Services = ["wuauserv", "BITS", "DoSvc"];

    public static IReadOnlyList<string> Pause()
    {
        var stopped = new List<string>();
        foreach (var s in Services)
            if (Run("query", s).Contains("RUNNING", StringComparison.Ordinal) && Run("stop", s).Length >= 0) stopped.Add(s);
        return stopped;
    }

    public static void Resume(IEnumerable<string> services)
    {
        foreach (var s in services) Run("start", s);
    }

    private static string Run(string verb, string service)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("sc.exe", $"{verb} {service}") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            if (p is null) return "";
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o;
        }
        catch (Exception) { return ""; }
    }
}
