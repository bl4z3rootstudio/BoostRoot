using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BoostRoot.Core.Net;

public sealed record DnsServer(string Name, string Address);
public sealed record DnsResult(DnsServer Server, double? MedianMs, int Failures);

/// <summary>Measures how fast DNS servers answer (UDP A queries). Measurement only — BoostRoot never changes DNS.</summary>
public static class DnsBenchmark
{
    public static IReadOnlyList<DnsServer> Public { get; } =
    [
        new("Cloudflare", "1.1.1.1"), new("Google", "8.8.8.8"), new("Quad9", "9.9.9.9"),
        new("OpenDNS", "208.67.222.222"), new("AdGuard", "94.140.14.14"), new("Yandex", "77.88.8.8"),
    ];

    public static readonly string[] Domains = ["www.google.com", "valorant.secure.dyn.riotcdn.net", "store.steampowered.com", "www.epicgames.com", "discord.com", "www.youtube.com"];

    /// <summary>The DNS server(s) Windows currently uses.</summary>
    public static IReadOnlyList<DnsServer> System()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().DnsAddresses)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString()).Distinct().Take(2)
                .Select(a => new DnsServer("Şu anki / Current", a))];
        }
        catch (NetworkInformationException) { return []; }
    }

    /// <summary>Standard DNS query packet for an A record.</summary>
    public static byte[] Query(string domain, ushort id)
    {
        var bytes = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in domain.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(global::System.Text.Encoding.ASCII.GetBytes(label));
        }
        bytes.AddRange([0, 0, 1, 0, 1]);
        return [.. bytes];
    }

    public static bool IsAnswer(ReadOnlySpan<byte> resp, ushort id) => resp.Length >= 12 && resp[0] == (byte)(id >> 8) && resp[1] == (byte)id && (resp[2] & 0x80) != 0;

    public static async Task<DnsResult> MeasureAsync(DnsServer server, CancellationToken ct)
    {
        var times = new List<double>();
        var fails = 0;
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var ep = new IPEndPoint(IPAddress.Parse(server.Address), 53);
        ushort id = (ushort)Random.Shared.Next(1, 65000);
        foreach (var d in Domains)
        {
            id++;
            var q = Query(d, id);
            var sw = Stopwatch.StartNew();
            try
            {
                await udp.SendAsync(q, ep, ct).ConfigureAwait(false);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(1500);
                while (true)
                {
                    var r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    if (IsAnswer(r.Buffer, id)) { times.Add(sw.Elapsed.TotalMilliseconds); break; }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException) { if (ct.IsCancellationRequested) throw; fails++; }
        }
        times.Sort();
        return new DnsResult(server, times.Count == 0 ? null : Math.Round(times[times.Count / 2], 1), fails);
    }
}
