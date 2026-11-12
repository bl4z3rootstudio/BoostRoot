using System.Diagnostics;
using BoostRoot.Core.Monitoring;

namespace BoostRoot.Core.Stress;

[Flags]
public enum StressKind { None = 0, Cpu = 1, Gpu = 2, Ram = 4, All = Cpu | Gpu | Ram }

/// <summary>Something that produces load and counts work + detected errors.</summary>
public interface IStressWorkload
{
    StressKind Kind { get; }
    /// <summary>Work units finished so far (iterations, frames, bytes verified…).</summary>
    long Work { get; }
    long Errors { get; }
    Task RunAsync(CancellationToken ct);
}

/// <summary>Every CPU thread runs a deterministic floating-point kernel and compares the result
/// with a reference value. A mismatch means the CPU computed wrong — unstable overclock/undervolt or heat.</summary>
public sealed class CpuStress(int? threads = null) : IStressWorkload
{
    private long _work, _errors;
    public StressKind Kind => StressKind.Cpu;
    public long Work => Interlocked.Read(ref _work);
    public long Errors => Interlocked.Read(ref _errors);
    public int Threads { get; } = Math.Max(1, threads ?? Environment.ProcessorCount);

    public static double Kernel(double seed)
    {
        double a = seed, b = 1.0000001, c = 0.9999999, acc = 0;
        for (var i = 0; i < 20_000; i++)
        {
            a = Math.FusedMultiplyAdd(a, b, c) * 0.5;
            b = Math.FusedMultiplyAdd(b, c, 1e-9);
            c = Math.Sqrt(c * c + 1e-12);
            acc += a * 1e-6 + Math.Sin(i * 1e-3);
        }
        return acc + a + b + c;
    }

    public Task RunAsync(CancellationToken ct)
    {
        var expected = Kernel(1.2345);
        var tasks = Enumerable.Range(0, Threads).Select(_ => Task.Factory.StartNew(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; // keep the PC responsive
            while (!ct.IsCancellationRequested)
            {
                if (Kernel(1.2345) != expected) Interlocked.Increment(ref _errors);
                Interlocked.Increment(ref _work);
            }
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        return Task.WhenAll(tasks).ContinueWith(_ => { }, TaskScheduler.Default);
    }
}

/// <summary>Fills a slice of free RAM with address-dependent patterns and verifies it, pass after pass.</summary>
public sealed class RamStress(long bytes) : IStressWorkload
{
    private long _work, _errors;
    public StressKind Kind => StressKind.Ram;
    public long Work => Interlocked.Read(ref _work);
    public long Errors => Interlocked.Read(ref _errors);
    public long Bytes { get; } = Math.Max(1 << 20, bytes);
    private const int BlockLongs = 8 << 20; // 64 MB blocks

    /// <summary>How much to test: half of free RAM, at most 8 GB, leaving the OS room.</summary>
    public static long Recommended(ulong availableBytes) => (long)Math.Min(availableBytes / 2, 8UL << 30);

    public Task RunAsync(CancellationToken ct) => Task.Factory.StartNew(() =>
    {
        var blocks = new List<ulong[]>();
        try
        {
            for (long left = Bytes; left > 0 && !ct.IsCancellationRequested; left -= (long)BlockLongs * 8)
                blocks.Add(GC.AllocateUninitializedArray<ulong>((int)Math.Min(BlockLongs, left / 8)));
        }
        catch (OutOfMemoryException) { /* test what we got */ }
        ulong pass = 0;
        while (!ct.IsCancellationRequested)
        {
            pass++;
            foreach (var b in blocks) Fill(b, pass);
            foreach (var b in blocks)
            {
                if (ct.IsCancellationRequested) break;
                Interlocked.Add(ref _errors, Verify(b, pass));
                Interlocked.Add(ref _work, (long)b.Length * 8);
            }
        }
        blocks.Clear();
        GC.Collect();
    }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(_ => { }, TaskScheduler.Default);

    internal static ulong Pattern(int i, ulong pass) => (ulong)i * 0x9E3779B97F4A7C15UL ^ pass * 0xD1B54A32D192ED03UL ^ (pass % 2 == 0 ? ulong.MaxValue : 0);

    internal static void Fill(ulong[] b, ulong pass) { for (var i = 0; i < b.Length; i++) b[i] = Pattern(i, pass); }

    internal static long Verify(ulong[] b, ulong pass)
    {
        long bad = 0;
        for (var i = 0; i < b.Length; i++) if (b[i] != Pattern(i, pass)) bad++;
        return bad;
    }
}

public sealed record StressSample(TimeSpan T, SensorSnapshot Sensors, double CpuRate, double GpuRate, double RamRate);

public sealed record StressResult(
    StressKind Kinds,
    TimeSpan Planned,
    TimeSpan Ran,
    bool Completed,
    string? StopReason,
    long CpuErrors,
    long GpuErrors,
    long RamErrors,
    bool ThrottleSeen,
    double? MaxCpuTemp,
    double? MaxGpuTemp,
    double? MaxGpuHotspot,
    double CpuDropPercent)
{
    public bool Passed => Completed && CpuErrors + GpuErrors + RamErrors == 0;
}

/// <summary>Runs the workloads, samples sensors every second and stops on errors or heat.</summary>
public sealed class StressRunner(ISensors sensors, StressLimits limits)
{
    public event Action<StressSample>? Sample;

    public async Task<StressResult> RunAsync(IReadOnlyList<IStressWorkload> loads, TimeSpan duration, CancellationToken ct, TimeSpan? tick = null)
    {
        var kinds = loads.Aggregate(StressKind.None, (k, l) => k | l.Kind);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var running = loads.Select(l => l.RunAsync(stop.Token)).ToArray();
        var sw = Stopwatch.StartNew();
        var interval = tick ?? TimeSpan.FromSeconds(1);
        string? reason = null;
        bool throttle = false;
        double? maxCpu = null, maxGpu = null, maxHot = null;
        var cpuRates = new List<double>();
        var last = loads.ToDictionary(l => l, l => l.Work);
        var lastT = sw.Elapsed;
        try
        {
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
                var now = sw.Elapsed;
                var dt = Math.Max(0.001, (now - lastT).TotalSeconds);
                lastT = now;
                double Rate(StressKind k)
                {
                    double r = 0;
                    foreach (var l in loads.Where(l => l.Kind == k)) { var w = l.Work; r += (w - last[l]) / dt; last[l] = w; }
                    return r;
                }
                var s = sensors.Read();
                var sample = new StressSample(now, s, Rate(StressKind.Cpu), Rate(StressKind.Gpu), Rate(StressKind.Ram));
                if (kinds.HasFlag(StressKind.Cpu)) cpuRates.Add(sample.CpuRate);
                throttle |= s.CpuThrottled;
                maxCpu = Max(maxCpu, s.CpuTempC); maxGpu = Max(maxGpu, s.GpuTempC); maxHot = Max(maxHot, s.GpuHotspotC);
                Sample?.Invoke(sample);
                reason = limits.Check(s);
                if (reason is null && loads.Any(l => l.Errors > 0)) reason = "stress.stop.errors";
                if (reason is not null) break;
            }
        }
        catch (OperationCanceledException) { reason ??= "stress.stop.user"; }
        stop.Cancel();
        await Task.WhenAll(running).ConfigureAwait(false);
        if (reason is null && ct.IsCancellationRequested) reason = "stress.stop.user";
        long Err(StressKind k) => loads.Where(l => l.Kind == k).Sum(l => l.Errors);
        return new StressResult(kinds, duration, sw.Elapsed, reason is null, reason,
            Err(StressKind.Cpu), Err(StressKind.Gpu), Err(StressKind.Ram), throttle, maxCpu, maxGpu, maxHot, CpuDrop(cpuRates));
    }

    private static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    /// <summary>How much slower the CPU got from the first to the last part of the run (heat throttling).</summary>
    public static double CpuDrop(IReadOnlyList<double> rates)
    {
        if (rates.Count < 6) return 0;
        var n = Math.Max(2, rates.Count / 5);
        var first = rates.Skip(1).Take(n).Average();
        var lastAvg = rates.Skip(rates.Count - n).Average();
        return first <= 0 ? 0 : Math.Max(0, Math.Round((first - lastAvg) / first * 100, 1));
    }
}
