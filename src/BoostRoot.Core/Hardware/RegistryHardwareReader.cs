using System.Globalization;
using BoostRoot.Core.Platform;

namespace BoostRoot.Core.Hardware;

/// <summary>Reads CPU and GPU details from the registry (no WMI, AOT-safe, testable with a fake registry).</summary>
public static class RegistryHardwareReader
{
    public const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public static string CpuName(IRegistry r) =>
        r.GetValue(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString")?.Data.Trim() ?? "";

    public static IReadOnlyList<GpuInfo> Gpus(IRegistry r)
    {
        var list = new List<GpuInfo>();
        foreach (var sub in r.GetSubKeyNames(RegistryHive.LocalMachine, DisplayClass).Where(s => s.Length == 4 && s.All(char.IsDigit)))
        {
            var key = DisplayClass + "\\" + sub;
            var name = r.GetValue(RegistryHive.LocalMachine, key, "DriverDesc")?.Data;
            if (string.IsNullOrWhiteSpace(name) || name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || name.Contains("Remote", StringComparison.OrdinalIgnoreCase)) continue;
            long vram = 0;
            if (r.GetValue(RegistryHive.LocalMachine, key, "HardwareInformation.qwMemorySize") is { } q)
                vram = q.Kind == RegistryValueKind.QWord ? long.Parse(q.Data, CultureInfo.InvariantCulture) : FromBinary(q);
            else if (r.GetValue(RegistryHive.LocalMachine, key, "HardwareInformation.MemorySize") is { } m)
                vram = m.Kind == RegistryValueKind.DWord ? (uint)int.Parse(m.Data, CultureInfo.InvariantCulture) : FromBinary(m);
            var ver = r.GetValue(RegistryHive.LocalMachine, key, "DriverVersion")?.Data;
            DateOnly? date = DateOnly.TryParseExact(r.GetValue(RegistryHive.LocalMachine, key, "DriverDate")?.Data, ["M-d-yyyy", "MM-dd-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            list.Add(new GpuInfo(name.Trim(), HardwareTables.VendorOf(name), vram / (1024 * 1024), ver, date));
        }
        return list;
    }

    private static long FromBinary(RegistryValue v)
    {
        if (v.Kind != RegistryValueKind.Binary) return long.TryParse(v.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var b = Convert.FromBase64String(v.Data);
        return b.Length >= 8 ? BitConverter.ToInt64(b) : b.Length >= 4 ? BitConverter.ToUInt32(b) : 0;
    }
}
