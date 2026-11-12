using System.Text.RegularExpressions;

namespace BoostRoot.Core.Hardware;

/// <summary>
/// Rough 0–100 gaming-performance estimates by model family, used only for the PC score and level recommendation.
/// Order matters: first matching pattern wins. Unknown hardware gets a neutral 50.
/// </summary>
public static partial class HardwareTables
{
    private static readonly (Regex Pattern, int Score)[] Gpu =
    [
        (R(@"RTX\s*50(90|80)"), 100), (R(@"RTX\s*40(90|80)"), 96), (R(@"RTX\s*5070"), 90), (R(@"RTX\s*4070"), 86), (R(@"RTX\s*30(90|80)"), 84),
        (R(@"RX\s*9070"), 90), (R(@"RX\s*79\d\d"), 92), (R(@"RX\s*78\d\d"), 82), (R(@"RX\s*77\d\d"), 76), (R(@"RX\s*69\d\d|RX\s*68\d\d"), 78),
        (R(@"RTX\s*5060|RTX\s*4060|RTX\s*3070"), 74), (R(@"RX\s*9060|RX\s*76\d\d|RX\s*67\d\d"), 70), (R(@"RTX\s*3060|RTX\s*2080|Arc\s*B5\d\d"), 64),
        (R(@"RX\s*66\d\d|RX\s*5700|RTX\s*2070|Arc\s*A7\d\d"), 60), (R(@"RTX\s*2060|RTX\s*3050|GTX\s*1080"), 54), (R(@"RX\s*5600|RX\s*65\d\d|GTX\s*1070|GTX\s*1660"), 48),
        (R(@"RX\s*5500|GTX\s*1650|RX\s*(580|590)|GTX\s*1060"), 40), (R(@"RX\s*(570|480|470)|GTX\s*970|GTX\s*1050"), 32),
        (R(@"Radeon\s*(780M|890M|880M)"), 30), (R(@"RX\s*(560|550)|GT\s*1030|Radeon\s*(680M|760M|Vega)"), 20), (R(@"Intel.*(Iris|UHD|HD)|Radeon\s*Graphics"), 12),
    ];

    private static readonly (Regex Pattern, int Score)[] Cpu =
    [
        (R(@"Ryzen\s*9\s*9\d{3}X3D|Ryzen\s*7\s*9800X3D|Ryzen\s*7\s*7800X3D"), 100), (R(@"Core.*Ultra\s*9|i9-1[34]\d{3}"), 92), (R(@"Ryzen\s*9\s*[79]\d{3}"), 92),
        (R(@"Core.*Ultra\s*7|i7-1[34]\d{3}|Ryzen\s*7\s*[79]\d{3}"), 86), (R(@"i5-1[34]\d{3}|Ryzen\s*5\s*[79]\d{3}|Core.*Ultra\s*5"), 78),
        (R(@"i[79]-12\d{3}|Ryzen\s*[79]\s*5\d{3}"), 76), (R(@"i5-12\d{3}|Ryzen\s*5\s*5\d{3}"), 68), (R(@"i[579]-1[01]\d{3}|Ryzen\s*[579]\s*3\d{3}"), 60),
        (R(@"i[79]-[89]\d{3}|Ryzen\s*[79]\s*2\d{3}"), 54), (R(@"i5-[89]\d{3}|Ryzen\s*5\s*2\d{3}"), 48), (R(@"i3-1[0-4]\d{3}"), 44),
        (R(@"i[57]-[67]\d{3}|Ryzen\s*[357]\s*1\d{3}"), 36), (R(@"i3-|Pentium|Celeron|Athlon|i[57]-[2-5]\d{3}"), 20),
    ];

    private static Regex R(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static int GpuScore(string? name) => Lookup(Gpu, name);
    public static int CpuScore(string? name) => Lookup(Cpu, name);

    private static int Lookup((Regex Pattern, int Score)[] table, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 50;
        foreach (var (p, s) in table) if (p.IsMatch(name)) return s;
        return 50;
    }

    public static GpuVendor VendorOf(string name) =>
        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
        : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
        : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
        : GpuVendor.Unknown;
}
