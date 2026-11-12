using System.Runtime.InteropServices;
using BoostRoot.Core.Monitoring;

namespace BoostRoot.Platform.Windows.Sensors;

/// <summary>CPU + RAM from Windows, GPU from NVML (NVIDIA) or ADL (AMD). Every source is optional:
/// a missing driver DLL just means that value stays null. No kernel driver is ever loaded.</summary>
public sealed class WindowsSensors : ISensors
{
    private readonly CpuSensor _cpu = new();
    private readonly NvmlSensor? _nv = NvmlSensor.TryCreate();
    private readonly AdlSensor? _amd;
    public Func<double?>? FpsSource { get; set; }

    public WindowsSensors() { if (_nv is null) _amd = AdlSensor.TryCreate(); }

    public string GpuBackend => _nv is not null ? "NVML" : _amd is not null ? "ADL" : "none";

    public SensorSnapshot Read()
    {
        var s = new SensorSnapshot(DateTime.UtcNow);
        try { s = _cpu.Fill(s); } catch (Exception) { }
        try { s = _nv?.Fill(s) ?? _amd?.Fill(s) ?? s; } catch (Exception) { }
        try { s = s with { RamUsedPct = RamPct() }; } catch (Exception) { }
        if (FpsSource is not null) s = s with { Fps = FpsSource() };
        return s;
    }

    private static double? RamPct()
    {
        var m = new HardwareScanner.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<HardwareScanner.MEMORYSTATUSEX>() };
        return HardwareScanner.GlobalMemoryStatusEx(ref m) ? m.dwMemoryLoad : null;
    }

    public void Dispose() { _nv?.Dispose(); _amd?.Dispose(); }
}

internal sealed partial class CpuSensor
{
    private long _idle, _kernel, _user;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

    [LibraryImport("powrprof.dll")]
    private static partial uint CallNtPowerInformation(int level, nint input, uint inputLen, nint output, uint outputLen);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION { public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState; }

    public CpuSensor() => GetSystemTimes(out _idle, out _kernel, out _user);

    public unsafe SensorSnapshot Fill(SensorSnapshot s)
    {
        double? load = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel - _kernel + (user - _user); // kernel time includes idle
            var busy = total - (idle - _idle);
            if (total > 0) load = Math.Clamp(busy * 100.0 / total, 0, 100);
            (_idle, _kernel, _user) = (idle, kernel, user);
        }
        var n = Environment.ProcessorCount;
        var buf = stackalloc PROCESSOR_POWER_INFORMATION[n];
        double? mhz = null;
        var throttled = false;
        if (CallNtPowerInformation(11, 0, 0, (nint)buf, (uint)(sizeof(PROCESSOR_POWER_INFORMATION) * n)) == 0)
        {
            double sum = 0;
            for (var i = 0; i < n; i++) { sum += buf[i].CurrentMhz; throttled |= buf[i].MhzLimit > 0 && buf[i].MhzLimit < buf[i].MaxMhz; }
            mhz = sum / n;
        }
        return s with { CpuLoad = load, CpuMhz = mhz, CpuThrottled = throttled };
    }
}

/// <summary>NVIDIA Management Library — ships with every NVIDIA driver (System32\nvml.dll).</summary>
internal sealed unsafe class NvmlSensor : IDisposable
{
    private readonly nint _lib, _dev;
    private readonly delegate* unmanaged<nint, int, uint*, int> _temp;
    private readonly delegate* unmanaged<nint, uint*, int> _util;
    private readonly delegate* unmanaged<nint, int, uint*, int> _clock;
    private readonly delegate* unmanaged<nint, uint*, int> _power;
    private readonly delegate* unmanaged<nint, ulong*, int> _mem;
    private readonly delegate* unmanaged<int> _shutdown;

    private NvmlSensor(nint lib, nint dev)
    {
        _lib = lib; _dev = dev;
        _temp = (delegate* unmanaged<nint, int, uint*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetTemperature");
        _util = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetUtilizationRates");
        _clock = (delegate* unmanaged<nint, int, uint*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetClockInfo");
        _power = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetPowerUsage");
        _mem = (delegate* unmanaged<nint, ulong*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetMemoryInfo");
        _shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(lib, "nvmlShutdown");
    }

    public static NvmlSensor? TryCreate()
    {
        if (!NativeLibrary.TryLoad("nvml.dll", out var lib)
            && !NativeLibrary.TryLoad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"), out lib))
            return null;
        try
        {
            var init = (delegate* unmanaged<int>)NativeLibrary.GetExport(lib, "nvmlInit_v2");
            if (init() != 0) return null;
            var byIndex = (delegate* unmanaged<uint, nint*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetHandleByIndex_v2");
            nint dev;
            return byIndex(0, &dev) == 0 ? new NvmlSensor(lib, dev) : null;
        }
        catch (EntryPointNotFoundException) { return null; }
    }

    public SensorSnapshot Fill(SensorSnapshot s)
    {
        uint t, clk, mw;
        var util = stackalloc uint[2];
        var mem = stackalloc ulong[3];
        return s with
        {
            GpuTempC = _temp(_dev, 0, &t) == 0 ? t : null,
            GpuLoad = _util(_dev, util) == 0 ? util[0] : null,
            GpuClockMhz = _clock(_dev, 0, &clk) == 0 ? clk : null,
            GpuPowerW = _power(_dev, &mw) == 0 ? mw / 1000.0 : null,
            VramUsedMb = _mem(_dev, mem) == 0 ? mem[2] / 1048576.0 : null,
        };
    }

    public void Dispose() { try { _shutdown(); } catch (Exception) { } }
}

/// <summary>AMD Display Library — ships with every Radeon driver (atiadlxx.dll). Uses the newest
/// Overdrive API the card supports: OD8 (PMLog, RX 5000+), ODN (RX 400/500/Vega), OD6, OD5.</summary>
internal sealed unsafe class AdlSensor : IDisposable
{
    private readonly nint _ctx;
    private readonly int _adapter, _od;
    private readonly nint _lib;

    [UnmanagedCallersOnly]
    private static nint Alloc(int size) => Marshal.AllocCoTaskMem(size);

    private AdlSensor(nint lib, nint ctx, int adapter, int od) { _lib = lib; _ctx = ctx; _adapter = adapter; _od = od; }

    public static AdlSensor? TryCreate()
    {
        if (!NativeLibrary.TryLoad("atiadlxx.dll", out var lib)) return null;
        try
        {
            var create = (delegate* unmanaged<delegate* unmanaged<int, nint>, int, nint*, int>)NativeLibrary.GetExport(lib, "ADL2_Main_Control_Create");
            nint ctx;
            if (create(&Alloc, 1, &ctx) != 0) return null;
            var count = (delegate* unmanaged<nint, int*, int>)NativeLibrary.GetExport(lib, "ADL2_Adapter_NumberOfAdapters_Get");
            var info = (delegate* unmanaged<nint, nint, int, int>)NativeLibrary.GetExport(lib, "ADL2_Adapter_AdapterInfo_Get");
            var active = (delegate* unmanaged<nint, int, int*, int>)NativeLibrary.GetExport(lib, "ADL2_Adapter_Active_Get");
            var caps = (delegate* unmanaged<nint, int, int*, int*, int*, int>)NativeLibrary.GetExport(lib, "ADL2_Overdrive_Caps");
            int n;
            if (count(ctx, &n) != 0 || n <= 0) return null;
            const int Size = 1572;
            var buf = Marshal.AllocHGlobal(Size * n);
            try
            {
                new Span<byte>((void*)buf, Size * n).Clear();
                for (var i = 0; i < n; i++) *(int*)(buf + i * Size) = Size;
                if (info(ctx, buf, Size * n) != 0) return null;
                for (var i = 0; i < n; i++)
                {
                    var idx = *(int*)(buf + i * Size + 4);
                    var vendor = *(int*)(buf + i * Size + 276);
                    int on = 0, sup = 0, en = 0, ver = 0;
                    if (vendor != 1002 || active(ctx, idx, &on) != 0 || on == 0) continue;
                    if (caps(ctx, idx, &sup, &en, &ver) != 0 || sup == 0) ver = 0;
                    return new AdlSensor(lib, ctx, idx, ver);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return null;
        }
        catch (EntryPointNotFoundException) { return null; }
    }

    public SensorSnapshot Fill(SensorSnapshot s)
    {
        switch (_od)
        {
            case >= 8:
            {
                var data = stackalloc int[1 + 256 * 2];
                data[0] = (1 + 256 * 2) * 4;
                if (((delegate* unmanaged<nint, int, int*, int>)NativeLibrary.GetExport(_lib, "ADL2_New_QueryPMLogData_Get"))(_ctx, _adapter, data) != 0) return s;
                double? V(int sensor) => data[1 + sensor * 2] != 0 ? data[2 + sensor * 2] : null;
                return s with { GpuClockMhz = V(1), GpuTempC = V(8), GpuHotspotC = V(27), GpuLoad = V(19), GpuPowerW = V(23) };
            }
            case 7:
            {
                var temp = (delegate* unmanaged<nint, int, int, int*, int>)NativeLibrary.GetExport(_lib, "ADL2_OverdriveN_Temperature_Get");
                var perf = (delegate* unmanaged<nint, int, int*, int>)NativeLibrary.GetExport(_lib, "ADL2_OverdriveN_PerformanceStatus_Get");
                int t = 0, hot = 0;
                var st = stackalloc int[18];
                var r = s;
                if (temp(_ctx, _adapter, 1, &t) == 0) r = r with { GpuTempC = t / 1000.0 };
                if (temp(_ctx, _adapter, 7, &hot) == 0 && hot > 0) r = r with { GpuHotspotC = hot / 1000.0 };
                if (perf(_ctx, _adapter, st) == 0) r = r with { GpuClockMhz = st[0] / 100.0, GpuLoad = st[6] };
                return r with { GpuPowerW = Od6Power() };
            }
            case 6:
            {
                var temp = (delegate* unmanaged<nint, int, int*, int>)NativeLibrary.GetExport(_lib, "ADL2_Overdrive6_Temperature_Get");
                var status = (delegate* unmanaged<nint, int, int*, int>)NativeLibrary.GetExport(_lib, "ADL2_Overdrive6_CurrentStatus_Get");
                int t = 0;
                var st = stackalloc int[9];
                var r = s;
                if (temp(_ctx, _adapter, &t) == 0) r = r with { GpuTempC = t / 1000.0 };
                if (status(_ctx, _adapter, st) == 0) r = r with { GpuClockMhz = st[0] / 100.0, GpuLoad = st[2] };
                return r with { GpuPowerW = Od6Power() };
            }
            default:
            {
                var temp = (delegate* unmanaged<nint, int, int, int*, int>)NativeLibrary.GetExport(_lib, "ADL2_Overdrive5_Temperature_Get");
                var act = (delegate* unmanaged<nint, int, int*, int>)NativeLibrary.GetExport(_lib, "ADL2_Overdrive5_CurrentActivity_Get");
                var t = stackalloc int[2] { 8, 0 };
                var a = stackalloc int[10];
                a[0] = 40;
                var r = s;
                if (temp(_ctx, _adapter, 0, t) == 0) r = r with { GpuTempC = t[1] / 1000.0 };
                if (act(_ctx, _adapter, a) == 0) r = r with { GpuClockMhz = a[1] / 100.0, GpuLoad = a[4] };
                return r;
            }
        }
    }

    private double? Od6Power()
    {
        if (!NativeLibrary.TryGetExport(_lib, "ADL2_Overdrive6_CurrentPower_Get", out var p)) return null;
        int v = 0;
        return ((delegate* unmanaged<nint, int, int, int*, int>)p)(_ctx, _adapter, 0, &v) == 0 && v > 0 ? v / 256.0 : null;
    }

    public void Dispose()
    {
        try { ((delegate* unmanaged<nint, int>)NativeLibrary.GetExport(_lib, "ADL2_Main_Control_Destroy"))(_ctx); } catch (Exception) { }
    }
}
