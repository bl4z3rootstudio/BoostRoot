using BoostRoot.Core.Profiles;
using BoostRoot.Core.Safety;

namespace BoostRoot.Core.Gpu;

/// <summary>Per-game driver profile writer (NVIDIA DRS on Windows).</summary>
public interface IGpuDriverProfiles
{
    bool Supported { get; }
    /// <summary>Writes the settings for the exe. Returns (changes, backup) — backup is opaque text for Revert.</summary>
    (int Changes, string Backup) Apply(string exeName, IReadOnlyDictionary<uint, uint> settings);
    void Revert(string exeName, string backup);
}

/// <summary>NVIDIA driver setting ids/values (NvApiDriverSettings.h) chosen from a profile's GPU preferences.</summary>
public static class NvidiaSettings
{
    public const uint PreferredPState = 0x1057EB71, PreferMaxPerformance = 1;
    public const uint PreRenderLimit = 0x007BA09E;
    public const uint VSyncMode = 0x00A879CF, VSyncForceOff = 0x08416747;
    public const uint QualityEnhancements = 0x00CE2691, HighPerformance = 20, Performance = 10;

    public static IReadOnlyDictionary<uint, uint> From(GpuPrefs p)
    {
        var d = new Dictionary<uint, uint> { [PreferredPState] = PreferMaxPerformance };
        if (p.LowLatency == true) d[PreRenderLimit] = 1;
        if (p.VSync == false) d[VSyncMode] = VSyncForceOff;
        if (p.TextureFiltering == "performance") d[QualityEnhancements] = HighPerformance;
        return d;
    }

    /// <summary>Backup format: "created=0;0x1057EB71=1;0x007BA09E=-" ("-" = setting was not set before).</summary>
    public static string Encode(bool created, IReadOnlyDictionary<uint, uint?> previous) =>
        "created=" + (created ? "1" : "0") + string.Concat(previous.Select(kv => $";0x{kv.Key:X8}=" + (kv.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")));

    public static (bool Created, IReadOnlyDictionary<uint, uint?> Previous) Decode(string backup)
    {
        var created = false;
        var prev = new Dictionary<uint, uint?>();
        foreach (var part in backup.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "created") { created = kv[1] == "1"; continue; }
            var id = Convert.ToUInt32(kv[0], 16);
            prev[id] = kv[1] == "-" ? null : uint.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        return (created, prev);
    }
}

/// <summary>Journaled GPU driver profile changes (kind "gpuprofile").</summary>
public sealed class GpuProfileChanger(IGpuDriverProfiles driver, Journal journal) : IUndoHandler
{
    public string Kind => JournalKinds.GpuProfile;

    public int Apply(string operationId, string exeName, GpuPrefs prefs)
    {
        if (!driver.Supported) return 0;
        var (changes, backup) = driver.Apply(exeName, NvidiaSettings.From(prefs));
        if (changes > 0) journal.Record(operationId, Kind, exeName, backup, true, "NVIDIA driver profile");
        return changes;
    }

    public void Revert(JournalEntry entry) => driver.Revert(entry.Target, entry.BackupRef ?? "created=0");
}
