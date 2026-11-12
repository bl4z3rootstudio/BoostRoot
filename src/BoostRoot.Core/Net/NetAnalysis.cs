using System.Diagnostics;
using System.Net.NetworkInformation;
using BoostRoot.Core.Hardware;
using BoostRoot.Core.Profiles;

namespace BoostRoot.Core.Net;

public sealed record PingStats(string Target, int Sent, int Received, double AvgMs, double MinMs, double MaxMs, double JitterMs)
{
    public double LossPercent => Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent;
}

public sealed record SpeedResult(double DownloadMbps, double UploadMbps, double IdleLatencyMs, double LoadedLatencyMs);

public sealed record GameServer(string Game, string Region, string Host);

public interface IPinger
{
    /// <summary>Round-trip time in ms, or null on timeout/failure.</summary>
    Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct);
}

public sealed class IcmpPinger : IPinger
{
    public async Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var p = new Ping();
            var r = await p.SendPingAsync(host, timeout, cancellationToken: ct);
            return r.Status == IPStatus.Success ? r.RoundtripTime : null;
        }
        catch (PingException) { return null; }
    }
}

public static class NetAnalysis
{
    /// <summary>Public, well-known endpoints near Turkish players. Measurement only — BoostRoot never changes network settings.</summary>
    public static IReadOnlyList<GameServer> DefaultServers { get; } =
    [
        new("Cloudflare", "Global", "1.1.1.1"),
        new("Google", "Global", "8.8.8.8"),
        new("AWS", "Frankfurt", "dynamodb.eu-central-1.amazonaws.com"),
        new("AWS", "Stockholm", "dynamodb.eu-north-1.amazonaws.com"),
        new("AWS", "Paris", "dynamodb.eu-west-3.amazonaws.com"),
        new("AWS", "Milano", "dynamodb.eu-south-1.amazonaws.com"),
    ];

    public static async Task<PingStats> MeasureAsync(IPinger pinger, string host, int count, CancellationToken ct)
    {
        var rtts = new List<double>();
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await pinger.PingAsync(host, TimeSpan.FromSeconds(2), ct) is { } ms) rtts.Add(ms);
            if (i < count - 1) await Task.Delay(200, ct);
        }
        return Summarize(host, count, rtts);
    }

    public static PingStats Summarize(string host, int sent, IReadOnlyList<double> rtts)
    {
        if (rtts.Count == 0) return new(host, sent, 0, 0, 0, 0, 0);
        var jitter = rtts.Count < 2 ? 0 : rtts.Zip(rtts.Skip(1), (a, b) => Math.Abs(b - a)).Average();
        return new(host, sent, rtts.Count, Math.Round(rtts.Average(), 1), rtts.Min(), rtts.Max(), Math.Round(jitter, 1));
    }

    /// <summary>Advice only; never applies changes.</summary>
    public static IReadOnlyList<Localized> Advice(IReadOnlyList<NetInfo> adapters, IReadOnlyList<PingStats> pings, SpeedResult? speed)
    {
        var a = new List<Localized>();
        var active = adapters.Where(n => n.SpeedMbps > 0).ToList();
        if (active.Count > 0 && active.All(n => n.IsWireless))
            a.Add(new("Wi-Fi kullanıyorsun. Ethernet kablosu pingi düşürür ve dalgalanmayı azaltır.", "You're on Wi-Fi. An ethernet cable lowers ping and jitter."));
        if (active.Any(n => !n.IsWireless && n.SpeedMbps <= 100))
            a.Add(new("Kablolu bağlantın 100 Mbps'te. Kabloyu/portu değiştir; 1 Gbps olmalı.", "Your wired link is 100 Mbps. Swap the cable/port; it should be 1 Gbps."));
        if (pings.Any(p => p.LossPercent >= 2))
            a.Add(new("Paket kaybı var. Modemi yeniden başlat; devam ederse internet sağlayıcını ara.", "Packet loss detected. Restart the router; if it persists, call your ISP."));
        if (pings.Any(p => p.JitterMs >= 15))
            a.Add(new("Ping dalgalanıyor. Oyun sırasında indirme/yayın yapan cihaz veya program olabilir.", "Ping is unstable. Something may be downloading or streaming while you play."));
        if (speed is { } s && s.LoadedLatencyMs - s.IdleLatencyMs > 60)
            a.Add(new("İnternet dolunca ping çok artıyor (bufferbloat). Modemde SQM/QoS varsa aç.", "Ping rises a lot under load (bufferbloat). Enable SQM/QoS on the router if available."));
        if (a.Count == 0) a.Add(new("Bağlantın oyun için iyi görünüyor.", "Your connection looks good for gaming."));
        return a;
    }
}

/// <summary>Cloudflare speed test endpoints (speed.cloudflare.com). Free, no key.</summary>
public sealed class CloudflareSpeedTest(HttpClient http)
{
    private const string Base = "https://speed.cloudflare.com";

    public async Task<SpeedResult> RunAsync(IPinger pinger, IProgress<double>? progress, CancellationToken ct)
    {
        var idle = await LatencyAsync(ct);
        progress?.Report(0.1);
        var loadedLatency = new List<double>();
        var downTask = TransferAsync(download: true, TimeSpan.FromSeconds(8), ct);
        while (!downTask.IsCompleted) { if (await LatencyAsync(ct) is var l and > 0) loadedLatency.Add(l); await Task.Delay(400, ct); }
        var down = await downTask;
        progress?.Report(0.6);
        var up = await TransferAsync(download: false, TimeSpan.FromSeconds(6), ct);
        progress?.Report(1);
        return new(Math.Round(down, 1), Math.Round(up, 1), Math.Round(idle, 1), Math.Round(loadedLatency.Count > 0 ? loadedLatency.Average() : idle, 1));
    }

    private async Task<double> LatencyAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try { using var r = await http.GetAsync($"{Base}/__down?bytes=0", ct); }
        catch (HttpRequestException) { return 0; }
        return sw.Elapsed.TotalMilliseconds;
    }

    private async Task<double> TransferAsync(bool download, TimeSpan duration, CancellationToken ct)
    {
        long bytes = 0;
        var sw = Stopwatch.StartNew();
        var buffer = new byte[1 << 16];
        var payload = new byte[4 << 20];
        while (sw.Elapsed < duration)
        {
            try
            {
                if (download)
                {
                    using var r = await http.GetAsync($"{Base}/__down?bytes={25_000_000}", HttpCompletionOption.ResponseHeadersRead, ct);
                    await using var s = await r.Content.ReadAsStreamAsync(ct);
                    int n;
                    while ((n = await s.ReadAsync(buffer, ct)) > 0) { bytes += n; if (sw.Elapsed >= duration) break; }
                }
                else
                {
                    using var c = new ByteArrayContent(payload);
                    using var r = await http.PostAsync($"{Base}/__up", c, ct);
                    if (r.IsSuccessStatusCode) bytes += payload.Length;
                }
            }
            catch (HttpRequestException) { break; }
        }
        return sw.Elapsed.TotalSeconds <= 0 ? 0 : bytes * 8 / sw.Elapsed.TotalSeconds / 1_000_000;
    }
}
