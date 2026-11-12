using BoostRoot.Core.Platform;
using BoostRoot.Core.Profiles;
using BoostRoot.Core.Safety;

namespace BoostRoot.Core.Tweaks;

public enum TweakTier { Safe, Advanced }

public sealed record RegistryWrite(RegistryHive Hive, string Key, string Name, RegistryValue Value);

/// <param name="RequiresRestart">Windows must restart for the change to take effect.</param>
public sealed record WindowsTweak(string Id, TweakTier Tier, Localized Name, Localized Description, Localized? Warning, bool RequiresRestart, IReadOnlyList<RegistryWrite> Writes, bool AppliesPowerPlan = false);

/// <summary>The fixed, reviewed list of Windows tweaks. Nothing here touches networking, DNS, Defender, Update or the page file.</summary>
public static class WindowsTweaks
{
    private const string DxKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string DxGlobalName = "DirectXUserGlobalSettings";

    public static IReadOnlyList<WindowsTweak> All { get; } =
    [
        new("power-high", TweakTier.Safe,
            new("Yüksek performans güç planı", "High performance power plan"),
            new("İşlemcinin oyunda yavaşlamasını önler.", "Keeps the CPU from down-clocking while gaming."),
            null, false, [], AppliesPowerPlan: true),
        new("gamedvr-off", TweakTier.Safe,
            new("Xbox arka plan kaydı kapalı", "Xbox background recording off"),
            new("Game DVR'ın sürekli kayıt için harcadığı kaynağı geri verir.", "Frees the resources Game DVR uses for background capture."),
            null, false,
            [
                new(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", RegistryValue.Dword(0)),
                new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", RegistryValue.Dword(0)),
            ]),
        new("gamemode-on", TweakTier.Safe,
            new("Windows Oyun Modu açık", "Windows Game Mode on"),
            new("Windows'un oyuna öncelik vermesini sağlar.", "Lets Windows prioritise the game."),
            null, false, [new(RegistryHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", RegistryValue.Dword(1))]),
        new("windowed-opt", TweakTier.Safe,
            new("Pencereli oyun iyileştirmeleri", "Optimizations for windowed games"),
            new("Kenarlıksız modda gecikmeyi düşürür (Windows 11).", "Lowers latency in borderless mode (Windows 11)."),
            null, false, [new(RegistryHive.CurrentUser, DxKey, DxGlobalName, RegistryValue.Str("SwapEffectUpgradeEnable=1;"))]),
        new("transparency-off", TweakTier.Advanced,
            new("Saydamlık efektleri kapalı", "Transparency effects off"),
            new("Masaüstü efektlerinin GPU kullanımını azaltır.", "Reduces desktop effects' GPU usage."),
            null, false, [new(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", RegistryValue.Dword(0))]),
        new("timer-global", TweakTier.Advanced,
            new("Zamanlayıcı çözünürlüğü (Win11)", "Global timer resolution (Win11)"),
            new("Oyunların istediği hassas zamanlayıcının sistem genelinde geçerli olmasını sağlar.", "Makes the precise timer games request apply system-wide."),
            null, true, [new(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", RegistryValue.Dword(1))]),
        new("hags-on", TweakTier.Advanced,
            new("Donanım hızlandırmalı GPU zamanlaması", "Hardware-accelerated GPU scheduling"),
            new("Destekleyen kartlarda gecikmeyi biraz düşürebilir.", "Can slightly lower latency on supported GPUs."),
            new("Bazı eski kartlarda takılmaya yol açabilir; sorun olursa geri al.", "May stutter on some older GPUs; revert if so."),
            true, [new(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", RegistryValue.Dword(2))]),
        new("hvci-off", TweakTier.Advanced,
            new("Bellek bütünlüğünü kapat", "Turn off Memory Integrity"),
            new("Bazı sistemlerde birkaç FPS kazandırır.", "Gains a few FPS on some systems."),
            new("Bilgisayarın kötü amaçlı sürücülere karşı korumasını azaltır. Sadece riski anlıyorsan aç.", "Weakens protection against malicious drivers. Only enable if you understand the risk."),
            true, [new(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", RegistryValue.Dword(0))]),
    ];

    public static WindowsTweak Get(string id) => All.FirstOrDefault(t => t.Id == id) ?? throw new ArgumentException($"Unknown tweak '{id}'.", nameof(id));

    /// <summary>Applies a tweak with journaling. DirectX global settings are merged, not overwritten.</summary>
    public static int Apply(WindowsTweak tweak, string operationId, IRegistry registry, RegistryChanger changer, PowerPlanChanger? power)
    {
        var changed = 0;
        if (tweak.AppliesPowerPlan && power is not null && power.SetActive(operationId, PowerSchemes.HighPerformance)) changed++;
        foreach (var w in tweak.Writes)
        {
            var value = w.Value;
            if (w.Name == DxGlobalName && w.Key == DxKey)
                value = RegistryValue.Str(MergeDx(registry.GetValue(w.Hive, w.Key, w.Name)?.Data, w.Value.Data));
            if (changer.Set(operationId, w.Hive, w.Key, w.Name, value, tweak.Id)) changed++;
        }
        return changed;
    }

    /// <summary>Sets the per-game "High performance" GPU preference.</summary>
    public static bool SetGpuPreference(string exePath, string operationId, IRegistry registry, RegistryChanger changer) =>
        changer.Set(operationId, RegistryHive.CurrentUser, DxKey, exePath,
            RegistryValue.Str(MergeDx(registry.GetValue(RegistryHive.CurrentUser, DxKey, exePath)?.Data, "GpuPreference=2;")), "gpu-preference");

    /// <summary>Merges "k=v;" pairs, keeping existing keys that the update does not mention.</summary>
    public static string MergeDx(string? existing, string update)
    {
        var map = new List<KeyValuePair<string, string>>();
        void Add(string? s)
        {
            foreach (var part in (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var k = part[..eq];
                var idx = map.FindIndex(p => p.Key.Equals(k, StringComparison.OrdinalIgnoreCase));
                var kv = new KeyValuePair<string, string>(k, part[(eq + 1)..]);
                if (idx >= 0) map[idx] = kv; else map.Add(kv);
            }
        }
        Add(existing);
        Add(update);
        return string.Concat(map.Select(p => p.Key + "=" + p.Value + ";"));
    }
}
