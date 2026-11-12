using BoostRoot.Core.Profiles;
using BoostRoot.Core.Scoring;

namespace BoostRoot.Core.Hardware;

/// <summary>Turns a hardware snapshot into actionable issues (spec 6.2) and a PC score.</summary>
public static class HealthChecks
{
    public static IReadOnlyList<HealthIssue> Evaluate(HardwareInfo hw, DateOnly today)
    {
        var list = new List<HealthIssue>();
        if (hw.RamSpeedRatio < 0.95)
        {
            var m = hw.RamModules.First(r => r.ConfiguredMts > 0 && r.SpeedMts > 0);
            list.Add(new("ram-xmp", IssueSeverity.Warning,
                new($"RAM {m.ConfiguredMts} MHz'de çalışıyor, desteklenen hız {m.SpeedMts} MHz", $"RAM runs at {m.ConfiguredMts} MHz, rated {m.SpeedMts} MHz"),
                new("BIOS'a gir (açılışta DEL/F2) ve XMP/EXPO/DOCP'yi etkinleştir. Ücretsiz FPS kazancı.", "Enter BIOS (DEL/F2 at boot) and enable XMP/EXPO/DOCP. Free FPS.")));
        }
        foreach (var d in hw.Displays.Where(d => d.MaxHz > d.CurrentHz + 1))
            list.Add(new("display-hz", IssueSeverity.Warning,
                new($"Monitör {d.CurrentHz} Hz'de, {d.MaxHz} Hz destekliyor", $"Monitor runs at {d.CurrentHz} Hz but supports {d.MaxHz} Hz"),
                new("Ayarlar > Sistem > Ekran > Gelişmiş ekran > Yenileme hızı: en yüksek değeri seç.", "Settings > System > Display > Advanced display > Refresh rate: pick the highest.")));
        if (hw.MainGpu is { DriverDate: { } date } g && date.AddMonths(6) < today)
            list.Add(new("gpu-driver-old", IssueSeverity.Warning,
                new($"Ekran kartı sürücüsü eski ({date:yyyy-MM-dd})", $"Graphics driver is old ({date:yyyy-MM-dd})"),
                new(g.Vendor == GpuVendor.Nvidia ? "nvidia.com/drivers adresinden güncel sürücüyü kur." : g.Vendor == GpuVendor.Amd ? "amd.com/support adresinden güncel Adrenalin sürücüsünü kur." : "Üreticinin sitesinden güncel sürücüyü kur.",
                    g.Vendor == GpuVendor.Nvidia ? "Install the latest driver from nvidia.com/drivers." : g.Vendor == GpuVendor.Amd ? "Install the latest Adrenalin driver from amd.com/support." : "Install the latest driver from the vendor's site.")));
        var active = hw.Networks.Where(n => n.SpeedMbps > 0).ToList();
        if (active.Count > 0 && active.All(n => n.IsWireless))
            list.Add(new("net-wifi", IssueSeverity.Info,
                new("Wi-Fi ile bağlısın", "You're on Wi-Fi"),
                new("Mümkünse ethernet kablosu kullan: ping daha düşük ve sabit olur.", "Use an ethernet cable if you can: lower and steadier ping.")));
        foreach (var n in active.Where(n => !n.IsWireless && n.SpeedMbps is > 0 and <= 100))
            list.Add(new("net-100mbps", IssueSeverity.Warning,
                new($"Kablolu bağlantı {n.SpeedMbps} Mbps'e düşmüş", $"Wired link is only {n.SpeedMbps} Mbps"),
                new("Kabloyu ve modemdeki girişi kontrol et; Cat5e/Cat6 kablo 1 Gbps verir.", "Check the cable and router port; a Cat5e/Cat6 cable gives 1 Gbps.")));
        if (hw.SystemDiskFreePercent < 10)
            list.Add(new("disk-full", IssueSeverity.Warning,
                new($"Sistem diski dolu (%{100 - hw.SystemDiskFreePercent:0} dolu)", $"System disk is almost full ({100 - hw.SystemDiskFreePercent:0}% used)"),
                new("Araçlar > Çöp temizleyici ile yer aç.", "Free space with Tools > Junk cleaner.")));
        return list;
    }

    public static PcScore Score(HardwareInfo hw, IReadOnlyList<HealthIssue> issues) =>
        PcScorer.Score(new PcSpecs(
            HardwareTables.GpuScore(hw.MainGpu?.Name), HardwareTables.CpuScore(hw.CpuName), hw.RamGb, hw.RamSpeedRatio,
            hw.SystemDiskIsSsd ?? true, hw.SystemDiskFreePercent, issues.Count(i => i.Severity == IssueSeverity.Warning)));
}
