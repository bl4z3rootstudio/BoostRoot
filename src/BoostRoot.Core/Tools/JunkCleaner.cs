using BoostRoot.Core.IO;
using BoostRoot.Core.Profiles;

namespace BoostRoot.Core.Tools;

/// <param name="RequiresConfirm">Shown unchecked; user must opt in (e.g. Recycle Bin).</param>
public sealed record JunkCategory(string Id, Localized Name, IReadOnlyList<string> Roots, string Pattern, bool RequiresConfirm = false, TimeSpan? MinAge = null);

public sealed record JunkScan(JunkCategory Category, IReadOnlyList<string> Files, long Bytes);

/// <summary>Deletes only well-known temporary locations. Shader caches, game caches, Downloads are never touched.</summary>
public sealed class JunkCleaner(IFileSystem fs, Func<string, long> sizeOf)
{
    public static IReadOnlyList<JunkCategory> Categories(string temp, string winTemp, string localAppData, string programData, string windir) =>
    [
        new("user-temp", new("Kullanıcı geçici dosyaları", "User temp files"), [temp], "*", MinAge: TimeSpan.FromHours(24)),
        new("win-temp", new("Windows geçici dosyaları", "Windows temp files"), [winTemp], "*", MinAge: TimeSpan.FromHours(24)),
        new("thumbs", new("Küçük resim önbelleği", "Thumbnail cache"), [Path.Combine(localAppData, "Microsoft", "Windows", "Explorer")], "thumbcache_*.db"),
        new("wer", new("Hata raporları", "Error reports"), [Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"), Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue"), Path.Combine(localAppData, "CrashDumps")], "*"),
        new("do-cache", new("Teslim İyileştirme önbelleği", "Delivery Optimization cache"), [Path.Combine(windir, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache")], "*"),
        new("wu-download", new("Eski Windows Update indirmeleri", "Old Windows Update downloads"), [Path.Combine(windir, "SoftwareDistribution", "Download")], "*", MinAge: TimeSpan.FromDays(10)),
        // Opt-in: fixes stutter/crashes after a driver update or a corrupted cache; games rebuild it on first launch (a few minutes of extra loading).
        new(ShaderCacheId, new("Ekran kartı gölgelendirici önbelleği (sürücü güncellemesinden sonra takılma yaşıyorsan)", "GPU shader cache (if you stutter after a driver update)"),
            [Path.Combine(localAppData, "D3DSCache"), Path.Combine(localAppData, "NVIDIA", "DXCache"), Path.Combine(localAppData, "NVIDIA", "GLCache"),
             Path.Combine(localAppData, "AMD", "DxCache"), Path.Combine(localAppData, "AMD", "DxcCache"), Path.Combine(localAppData, "AMD", "VkCache"), Path.Combine(localAppData, "AMD", "GLCache")],
            "*", RequiresConfirm: true),
    ];

    public const string ShaderCacheId = "shader-cache";

    /// <summary>Folders that must never be cleaned even if a category root points near them.</summary>
    public static IReadOnlyList<string> NeverClean { get; } = ["DXCache", "D3DSCache", "NVIDIA", "AMD", "ShaderCache", "GLCache", "Downloads", "Steam", "Riot Games"];

    public JunkScan Scan(JunkCategory c, DateTime nowUtc)
    {
        var files = new List<string>();
        long bytes = 0;
        foreach (var root in c.Roots)
        {
            IEnumerable<string> found;
            try { found = fs.EnumerateFiles(root, c.Pattern, true).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in found)
            {
                var parts = f.Replace('\\', '/').Split('/');
                if (c.Id != ShaderCacheId && parts.Any(p => NeverClean.Contains(p, StringComparer.OrdinalIgnoreCase))) continue;
                try
                {
                    if (c.MinAge is { } age && nowUtc - fs.GetLastWriteTimeUtc(f) < age) continue;
                    bytes += sizeOf(f);
                    files.Add(f);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return new(c, files, bytes);
    }

    /// <summary>Best effort: locked files are skipped. Returns bytes freed.</summary>
    public long Clean(JunkScan scan)
    {
        long freed = 0;
        foreach (var f in scan.Files)
        {
            try
            {
                var size = sizeOf(f);
                fs.Delete(f);
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return freed;
    }
}
