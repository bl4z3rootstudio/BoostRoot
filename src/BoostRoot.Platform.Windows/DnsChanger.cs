using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BoostRoot.Platform.Windows;

/// <summary>Switches the IPv4 DNS of connected adapters, keeping a backup so "Geri al" restores exactly what was there (DHCP or the old list).
/// Only well-known public resolvers are offered; nothing else on the adapters is touched.</summary>
public static class DnsChanger
{
    public static readonly Dictionary<string, string> Secondary = new() { ["1.1.1.1"] = "1.0.0.1", ["8.8.8.8"] = "8.8.4.4", ["9.9.9.9"] = "149.112.112.112" };

    private sealed record Backup(string Adapter, bool Dhcp, string[] Servers);

    private static string BackupFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoostRoot", "dns-backup.txt");

    public static bool HasBackup => File.Exists(BackupFile);

    private static IEnumerable<NetworkInterface> Active() => NetworkInterface.GetAllNetworkInterfaces().Where(n =>
        n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));

    public static bool Set(string primary)
    {
        if (!Secondary.TryGetValue(primary, out var second)) return false;
        var backups = new List<Backup>();
        var ok = false;
        foreach (var n in Active())
        {
            var dhcp = IsDhcpDns(n.Id);
            var old = n.GetIPProperties().DnsAddresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray();
            backups.Add(new(n.Name, dhcp, old));
            ok |= Netsh($"interface ipv4 set dnsservers name=\"{n.Name}\" static {primary} primary validate=no") == 0;
            Netsh($"interface ipv4 add dnsservers name=\"{n.Name}\" address={second} index=2 validate=no");
        }
        if (backups.Count > 0 && !HasBackup) // keep the very first backup: that is the user's real setting
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!);
            File.WriteAllLines(BackupFile, backups.Select(b => b.Adapter + "\t" + (b.Dhcp ? "dhcp" : string.Join(",", b.Servers))));
        }
        Netsh("interface ip delete arpcache");
        Run("ipconfig.exe", "/flushdns");
        return ok;
    }

    public static bool Restore()
    {
        if (!HasBackup) return false;
        try
        {
            var rows = File.ReadAllLines(BackupFile).Select(l => l.Split('\t')).ToArray();
            foreach (var r in rows.Where(r => r.Length == 2))
            {
                if (r[1] == "dhcp" || r[1].Length == 0) Netsh($"interface ipv4 set dnsservers name=\"{r[0]}\" source=dhcp");
                else
                {
                    var list = r[1].Split(',');
                    Netsh($"interface ipv4 set dnsservers name=\"{r[0]}\" static {list[0]} primary validate=no");
                    for (var i = 1; i < list.Length; i++) Netsh($"interface ipv4 add dnsservers name=\"{r[0]}\" address={list[i]} index={i + 1} validate=no");
                }
            }
            File.Delete(BackupFile);
            Run("ipconfig.exe", "/flushdns");
            return true;
        }
        catch (Exception) { return false; }
    }

    public static void FlushDns() => Run("ipconfig.exe", "/flushdns");

    /// <summary>Empty "NameServer" in the adapter's registry key = DNS comes from the router (DHCP).</summary>
    private static bool IsDhcpDns(string adapterId)
    {
        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{adapterId}");
        return string.IsNullOrWhiteSpace(k?.GetValue("NameServer") as string);
    }

    private static int Netsh(string args) => Run("netsh.exe", args);

    private static int Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
            if (p is null) return -1;
            p.WaitForExit(15000);
            return p.ExitCode;
        }
        catch (Exception) { return -1; }
    }
}
