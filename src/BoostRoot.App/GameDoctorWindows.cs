using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BoostRoot.Core.Platform;
using BoostRoot.Core.Tools;
using BoostRoot.Games;

namespace BoostRoot.App;

/// <summary>Collects <see cref="DoctorFacts"/> for one game and applies the one-click fixes. Read-only until a fix is clicked.</summary>
internal sealed class GameDoctorWindows(BoostRoot.Platform.Windows.WindowsRegistry registry, Func<CatalogEntry, string?> gameExe, Func<string, string?> lastBoost, Action<string> undo,
    Func<IReadOnlyList<JunkScan>> scanJunk, Func<IEnumerable<JunkScan>, long> cleanJunk, Func<DateOnly?> driverDate)
{
    private const string CompatKey = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    public DoctorFacts Collect(CatalogEntry e)
    {
        var g = e.Game;
        var source = g?.Source ?? "";
        var folder = g is not null && Directory.Exists(g.InstallPath);
        var exe = gameExe(e);
        return new DoctorFacts(
            e.DisplayName, source, folder, exe is not null, LauncherInstalled(g), StuckProcesses(e).Count > 0,
            FreeGb(Path.GetPathRoot(Environment.SystemDirectory)), folder ? FreeGb(Path.GetPathRoot(g!.InstallPath)) : null,
            VcRedist(), IsRiot(e) ? VanguardOk() : null, IsRiot(e) && RiotClientBroken(),
            exe is not null && CompatExes(e, exe).Count > 0, e.Profile is { } p && lastBoost(p.Id) is not null,
            PendingReboot(), driverDate() is { } d ? DateOnly.FromDateTime(DateTime.Today).DayNumber - d.DayNumber : 0,
            ShaderCacheBytes() > 2L * 1024 * 1024 * 1024);
    }

    public async Task<bool> FixAsync(CatalogEntry e, string id)
    {
        try
        {
            switch (id)
            {
                case "steam-verify" or "exe-missing" when e.Game is { Source: "steam" } sg:
                    Process.Start(new ProcessStartInfo($"steam://validate/{sg.SourceId}") { UseShellExecute = true })?.Dispose();
                    return true;
                case "stuck-process":
                    foreach (var p in StuckProcesses(e)) using (p) try { p.Kill(true); } catch (Exception) { }
                    return StuckProcesses(e).Count == 0;
                case "riot-client":
                    return FixRiotClient();
                case "vanguard":
                    await Sc("config vgc start= demand");
                    await Sc("start vgc");
                    return VanguardOk();
                case "vcredist":
                    var (code, _) = await BoostRoot.Platform.Windows.PowerShellRunner.WingetAsync(
                        ["install", "--id", "Microsoft.VCRedist.2015+.x64", "-e", "--silent", "--accept-package-agreements", "--accept-source-agreements"], CancellationToken.None);
                    return code == 0 || VcRedist();
                case "disk-space":
                    return await Task.Run(() => cleanJunk(scanJunk().Where(s => !s.Category.RequiresConfirm)) >= 0);
                case "shader-cache":
                    return await Task.Run(() => cleanJunk(scanJunk().Where(s => s.Category.Id == JunkCleaner.ShaderCacheId)) >= 0);
                case "compat-flags" when gameExe(e) is { } exe:
                    foreach (var (hive, name) in CompatExes(e, exe)) registry.DeleteValue(hive, CompatKey, name);
                    return CompatExes(e, exe).Count == 0;
                case "boost-active" when e.Profile is { } pr && lastBoost(pr.Id) is { } op:
                    undo(op);
                    return true;
            }
        }
        catch (Exception) { }
        return false;
    }

    private static bool IsRiot(CatalogEntry e) => e.Game?.Source == "riot" || e.Profile?.Detect.ContainsKey("riot") == true;

    private bool LauncherInstalled(InstalledGame? g) => g?.Source switch
    {
        "steam" => registry.GetValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath") is not null,
        "epic" => Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher")),
        "riot" => g.Extra.TryGetValue("client", out var c) && File.Exists(c.Replace('/', '\\')),
        _ => true,
    };

    /// <summary>Game processes that run for over a minute without any window (a start that hung).</summary>
    private static List<Process> StuckProcesses(CatalogEntry e)
    {
        var names = (e.Profile?.Detect.TryGetValue("process", out var p) == true ? p : "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(n => !n.StartsWith("dir:", StringComparison.Ordinal))
            .Select(n => Path.GetFileNameWithoutExtension(n));
        var list = new List<Process>();
        foreach (var n in names)
            foreach (var proc in Process.GetProcessesByName(n))
            {
                try { if (proc.MainWindowHandle == 0 && DateTime.Now - proc.StartTime > TimeSpan.FromMinutes(1)) { list.Add(proc); continue; } } catch (Exception) { }
                proc.Dispose();
            }
        return list;
    }

    private static double FreeGb(string? root)
    {
        try { var d = new DriveInfo(root ?? "C:\\"); return d.IsReady ? d.AvailableFreeSpace / 1_073_741_824.0 : 999; } catch (Exception) { return 999; }
    }

    private bool VcRedist() =>
        registry.GetValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64", "Installed")?.Data == "1"
        || registry.GetValue(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\X64", "Installed")?.Data == "1";

    /// <summary>Vanguard's service must exist and not be disabled (it starts with the game).</summary>
    private bool VanguardOk()
    {
        var start = registry.GetValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\vgc", "Start")?.Data;
        return start is not null && start != "4";
    }

    private static string RiotAsar => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Riot Games", "Riot Client", "RiotClientElectron", "resources", "app.asar");

    /// <summary>Reads dist/main/main.js out of the Riot Client's app.asar and checks it is JavaScript (a known corruption puts protobuf text there).</summary>
    internal static bool RiotClientBroken(string? asarPath = null)
    {
        var path = asarPath ?? RiotAsar;
        if (!File.Exists(path)) return false;
        try { using var fs = File.OpenRead(path); return AsarProbe.MainJsLooksBroken(fs); }
        catch (Exception) { return false; }
    }

    /// <summary>Closes the Riot Client and deletes app.asar; the client downloads a fresh copy on its next start.</summary>
    private static bool FixRiotClient()
    {
        foreach (var name in new[] { "Riot Client", "RiotClientServices", "RiotClientUx", "RiotClientUxRender", "RiotClientCrashHandler" })
            foreach (var p in Process.GetProcessesByName(name)) using (p) try { p.Kill(true); p.WaitForExit(3000); } catch (Exception) { }
        try { File.Delete(RiotAsar); } catch (Exception) { return false; }
        var services = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(RiotAsar)!)!)!, "RiotClientServices.exe");
        if (File.Exists(services)) Process.Start(new ProcessStartInfo(services) { UseShellExecute = true })?.Dispose();
        return !File.Exists(RiotAsar);
    }

    /// <summary>"Run in compatibility mode / as XP / as admin" entries Windows keeps for the game's exes.</summary>
    private List<(RegistryHive Hive, string Name)> CompatExes(CatalogEntry e, string exe)
    {
        var dir = e.Game?.InstallPath ?? Path.GetDirectoryName(exe) ?? "";
        var hits = new List<(RegistryHive, string)>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            try
            {
                foreach (var name in registry.GetValueNames(hive, CompatKey))
                    if (name.Equals(exe, StringComparison.OrdinalIgnoreCase) || (dir.Length > 3 && name.StartsWith(dir, StringComparison.OrdinalIgnoreCase)))
                        hits.Add((hive, name));
            }
            catch (Exception) { }
        return hits;
    }

    private bool PendingReboot() =>
        registry.GetSubKeyNames(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update").Contains("RebootRequired", StringComparer.OrdinalIgnoreCase)
        || registry.GetSubKeyNames(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing").Contains("RebootPending", StringComparer.OrdinalIgnoreCase);

    private static long ShaderCacheBytes()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        long total = 0;
        foreach (var d in new[] { Path.Combine(local, "D3DSCache"), Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(local, "AMD", "DxCache"), Path.Combine(local, "AMD", "DxcCache") })
            try
            {
                if (Directory.Exists(d)) total += new DirectoryInfo(d).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Sum(f => f.Length);
            }
            catch (Exception) { }
        return total;
    }

    private static async Task Sc(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", args) { CreateNoWindow = true, UseShellExecute = false });
        if (p is not null) await p.WaitForExitAsync();
    }
}
