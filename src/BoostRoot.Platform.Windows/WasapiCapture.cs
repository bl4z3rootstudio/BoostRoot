using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Runtime.InteropServices.Marshalling;
using BoostRoot.Core.Clips;

namespace BoostRoot.Platform.Windows;

/// <summary>Captures system audio (loopback of the default speakers) or the default microphone with WASAPI.
/// Source-generated COM → NativeAOT friendly. Gaps (silence = no packets) are filled so audio stays in sync.</summary>
public sealed partial class WasapiCapture : IDisposable
{
    private readonly Thread _thread;
    private volatile bool _stop;
    public AudioRing? Ring { get; private set; }
    public string? Error { get; private set; }
    private readonly ManualResetEventSlim _ready = new();

    /// <param name="clock">Session clock (seconds) shared with the video segments.</param>
    public WasapiCapture(bool loopback, double keepSeconds, Func<double> clock)
    {
        _thread = new Thread(() => Run(loopback, keepSeconds, clock)) { IsBackground = true, Name = "BoostRoot audio" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    private unsafe void Run(bool loopback, double keepSeconds, Func<double> clock)
    {
        try
        {
            var cw = new StrategyBasedComWrappers();
            var clsid = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
            var iid = typeof(IMMDeviceEnumerator).GUID;
            Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, 1, iid, out var pEnum));
            var en = (IMMDeviceEnumerator)cw.GetOrCreateObjectForComInstance(pEnum, CreateObjectFlags.None);
            Marshal.Release(pEnum);
            en.GetDefaultAudioEndpoint(loopback ? 0 : 1, loopback ? 0 : 2, out var dev); // eRender/eConsole or eCapture/eCommunications
            var clientIid = typeof(IAudioClient).GUID;
            dev.Activate(clientIid, 1, 0, out var pClient);
            var client = (IAudioClient)cw.GetOrCreateObjectForComInstance(pClient, CreateObjectFlags.None);
            Marshal.Release(pClient);
            client.GetMixFormat(out var fmt);
            var channels = *(ushort*)(fmt + 2);
            var rate = (int)*(uint*)(fmt + 4);
            var bits = *(ushort*)(fmt + 14);
            client.Initialize(0, loopback ? 0x00020000 : 0, 10_000_000, 0, fmt, 0);
            Marshal.FreeCoTaskMem(fmt);
            var capIid = typeof(IAudioCaptureClient).GUID;
            client.GetService(capIid, out var pCap);
            var cap = (IAudioCaptureClient)cw.GetOrCreateObjectForComInstance(pCap, CreateObjectFlags.None);
            Marshal.Release(pCap);
            var ring = new AudioRing(rate, channels, keepSeconds) { StartTime = clock() };
            Ring = ring;
            client.Start();
            _ready.Set();
            var tmp = new float[rate * channels];
            while (!_stop)
            {
                Thread.Sleep(10);
                cap.GetNextPacketSize(out var packet);
                if (packet == 0)
                {
                    var missing = (int)((clock() - ring.StartTime) * rate) - (int)(ring.Seconds * rate);
                    if (missing > rate / 20) ring.WriteSilence(missing);
                    continue;
                }
                while (packet > 0)
                {
                    cap.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    var n = (int)frames * channels;
                    if (n > tmp.Length) tmp = new float[n];
                    if ((flags & 2) != 0 || bits != 32) Array.Clear(tmp, 0, n);
                    else new ReadOnlySpan<float>((void*)data, n).CopyTo(tmp);
                    ring.Write(tmp.AsSpan(0, n));
                    cap.ReleaseBuffer(frames);
                    cap.GetNextPacketSize(out packet);
                }
            }
            client.Stop();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ExternalException)
        {
            Error = ex.Message;
        }
        finally { _ready.Set(); }
    }

    public void Dispose() { _stop = true; _thread.Join(500); _ready.Dispose(); }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint clsCtx, in Guid iid, out nint ppv);

    [GeneratedComInterface, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal partial interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [GeneratedComInterface, Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal partial interface IMMDevice
    {
        void Activate(in Guid iid, uint clsCtx, nint activationParams, out nint ppInterface);
    }

    [GeneratedComInterface, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    internal partial interface IAudioClient
    {
        void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);
        void GetBufferSize(out uint frames);
        void GetStreamLatency(out long latency);
        void GetCurrentPadding(out uint padding);
        void IsFormatSupported(int shareMode, nint format, out nint closest);
        void GetMixFormat(out nint format);
        void GetDevicePeriod(out long def, out long min);
        void Start();
        void Stop();
        void Reset();
        void SetEventHandle(nint handle);
        void GetService(in Guid iid, out nint ppv);
    }

    [GeneratedComInterface, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    internal partial interface IAudioCaptureClient
    {
        void GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        void ReleaseBuffer(uint frames);
        void GetNextPacketSize(out uint frames);
    }
}

/// <summary>Runs ffmpeg (downloaded on demand to %LOCALAPPDATA%\BoostRoot\tools).</summary>
public static class Ffmpeg
{
    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoostRoot", "tools", "ffmpeg");
    public static string Exe => Path.Combine(Dir, "ffmpeg.exe");
    public static bool Installed => File.Exists(Exe);

    public static Process Start(IReadOnlyList<string> args, bool stdin = false)
    {
        var psi = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = stdin };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
        p.PriorityClass = ProcessPriorityClass.BelowNormal; // never steal CPU from the game
        return p;
    }

    /// <summary>Runs to completion; returns (exit code, stderr).</summary>
    public static async Task<(int Code, string Err)> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        using var p = Start(args);
        var err = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return (p.ExitCode, await err.ConfigureAwait(false));
    }

    public static async Task<double?> DurationAsync(string file)
    {
        var (_, err) = await RunAsync(["-hide_banner", "-i", file]).ConfigureAwait(false);
        return BoostRoot.Core.Clips.FfmpegArgs.ParseDuration(err);
    }

    /// <summary>Candidate builds (GPL, static, with ddagrab + nvenc/amf/qsv) from BtbN/FFmpeg-Builds.</summary>
    public static readonly string[] Builds = ["ffmpeg-n8.0-latest-win64-gpl-8.0.zip", "ffmpeg-n7.1-latest-win64-gpl-7.1.zip", "ffmpeg-master-latest-win64-gpl.zip"];
    private const string Base = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";

    /// <summary>Downloads, verifies SHA-256 against the release's checksums file, extracts ffmpeg.exe.</summary>
    public static async Task InstallAsync(HttpClient http, IProgress<double>? progress, CancellationToken ct)
    {
        var sums = await http.GetStringAsync(Base + "checksums.sha256", ct).ConfigureAwait(false);
        foreach (var name in Builds)
        {
            var line = sums.Split('\n').FirstOrDefault(l => l.TrimEnd().EndsWith(name, StringComparison.Ordinal));
            if (line is null) continue;
            var expected = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim().ToLowerInvariant();
            Directory.CreateDirectory(Dir);
            var zip = Path.Combine(Dir, "download.zip");
            using (var resp = await http.GetAsync(Base + name, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(zip);
                var buf = new byte[1 << 16];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report(done / (double)total);
                }
            }
            await using (var f = File.OpenRead(zip))
            {
                var hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(f, ct).ConfigureAwait(false));
                if (hash != expected) { File.Delete(zip); throw new InvalidDataException("ffmpeg checksum mismatch"); }
            }
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.First(e => e.FullName.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                entry.ExtractToFile(Exe, true);
                var license = archive.Entries.FirstOrDefault(e => e.Name.Equals("LICENSE.txt", StringComparison.OrdinalIgnoreCase));
                license?.ExtractToFile(Path.Combine(Dir, "LICENSE.txt"), true);
            }
            File.Delete(zip);
            return;
        }
        throw new FileNotFoundException("No ffmpeg build found in the release");
    }
}
