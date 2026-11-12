using System.Text.Json;
using BoostRoot.Core.IO;
using BoostRoot.Core.Platform;

namespace BoostRoot.Games;

internal static class Util
{
    public static string J(params string[] parts) => string.Join('/', parts.Select((p, i) => i == 0 ? p.Replace('\\', '/').TrimEnd('/') : p.Replace('\\', '/').Trim('/')));
    public static string Read(IFileSystem fs, string path) => TextFile.Decode(fs.ReadAllBytes(path)).Text;
    public static string Leaf(string p) => p.Replace('\\', '/').TrimEnd('/')[(p.Replace('\\', '/').TrimEnd('/').LastIndexOf('/') + 1)..];
    public static IReadOnlyDictionary<string, string> NoExtra { get; } = new Dictionary<string, string>();

    /// <summary>Runs a locator body; any parse/IO problem yields an empty list instead of crashing the scan.</summary>
    public static IReadOnlyList<InstalledGame> Safe(Func<IEnumerable<InstalledGame>> body)
    {
        try { return body().ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException) { return []; }
    }
}

public sealed class SteamLocator(LocatorEnvironment env) : IGameLocator
{
    public IReadOnlyList<InstalledGame> Locate() => Util.Safe(() =>
    {
        var steam = env.Registry.GetValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")?.Data
                    ?? Util.J(env.DriveRoots.FirstOrDefault() ?? "C:", "Program Files (x86)", "Steam");
        var libs = new List<string> { steam };
        var lf = Util.J(steam, "steamapps", "libraryfolders.vdf");
        if (env.Fs.FileExists(lf))
        {
            var root = VdfReader.Parse(Util.Read(env.Fs, lf)).Child("libraryfolders");
            if (root is not null)
                foreach (var v in root.Values.OfType<VdfReader.Node>())
                    if (v.Str("path") is { } p) libs.Add(p);
        }
        var games = new List<InstalledGame>();
        foreach (var lib in libs.Select(l => l.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Util.J(lib, "steamapps");
            foreach (var acf in env.Fs.EnumerateFiles(apps, "appmanifest_*.acf", false))
            {
                try
                {
                    var st = VdfReader.Parse(Util.Read(env.Fs, acf)).Child("AppState");
                    if (st?.Str("appid") is not { } id || st.Str("installdir") is not { } dir) continue;
                    games.Add(new InstalledGame("steam", id, st.Str("name") ?? dir, Util.J(apps, "common", dir), Util.NoExtra));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return games.DistinctBy(g => g.SourceId);
    });
}

public sealed class EpicLocator(LocatorEnvironment env) : IGameLocator
{
    public IReadOnlyList<InstalledGame> Locate() => Util.Safe(() =>
    {
        var dir = Util.J(env.ProgramData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
        var games = new List<InstalledGame>();
        foreach (var f in env.Fs.EnumerateFiles(dir, "*.item", false))
        {
            try
            {
                using var doc = JsonDocument.Parse(Util.Read(env.Fs, f));
                var r = doc.RootElement;
                string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                if (S("AppName") is not { Length: > 0 } app) continue;
                games.Add(new InstalledGame("epic", app, S("DisplayName"), S("InstallLocation").Replace('\\', '/'),
                    new Dictionary<string, string>
                    {
                        ["namespace"] = S("CatalogNamespace"),
                        ["itemId"] = S("CatalogItemId"),
                        ["exe"] = S("LaunchExecutable"),
                    }));
            }
            catch (JsonException) { }
        }
        return games;
    });
}

public sealed class RiotLocator(LocatorEnvironment env) : IGameLocator
{
    private static readonly (string Product, string Name)[] Products = [("valorant", "VALORANT"), ("league_of_legends", "League of Legends")];

    public IReadOnlyList<InstalledGame> Locate() => Util.Safe(() =>
    {
        var games = new List<InstalledGame>();
        var client = "";
        var installs = Util.J(env.ProgramData, "Riot Games", "RiotClientInstalls.json");
        if (env.Fs.FileExists(installs))
        {
            using var d = JsonDocument.Parse(Util.Read(env.Fs, installs));
            if (d.RootElement.TryGetProperty("rc_default", out var rc)) client = (rc.GetString() ?? "").Replace('\\', '/');
        }
        foreach (var (product, name) in Products)
        {
            var yaml = Util.J(env.ProgramData, "Riot Games", "Metadata", $"{product}.live", $"{product}.live.product_settings.yaml");
            if (!env.Fs.FileExists(yaml)) continue;
            var line = Util.Read(env.Fs, yaml).Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("product_install_full_path:", StringComparison.Ordinal));
            var path = line is null ? "" : line[(line.IndexOf(':') + 1)..].Trim().Trim('"').Replace('\\', '/');
            games.Add(new InstalledGame("riot", product, name, path, new Dictionary<string, string> { ["client"] = client }));
        }
        return games;
    });
}

public sealed class XboxLocator(LocatorEnvironment env) : IGameLocator
{
    public IReadOnlyList<InstalledGame> Locate() => Util.Safe(() =>
        env.DriveRoots.SelectMany(root => env.Fs.EnumerateDirectories(Util.J(root, "XboxGames")))
            .Select(d => d.Replace('\\', '/'))
            .Where(d => !Util.Leaf(d).Equals("GameSave", StringComparison.OrdinalIgnoreCase) && !Guid.TryParse(Util.Leaf(d), out _))
            .Select(d => new InstalledGame("xbox", Util.Leaf(d), Util.Leaf(d), d, Util.NoExtra)));
}

public sealed class MinecraftLocator(LocatorEnvironment env) : IGameLocator
{
    public IReadOnlyList<InstalledGame> Locate() => Util.Safe(() =>
    {
        var list = new List<InstalledGame>();
        void Add(string kind, string name, string gameDir)
        {
            if (env.Fs.DirectoryExists(gameDir))
                list.Add(new InstalledGame("minecraft", "java", name, gameDir, new Dictionary<string, string> { ["launcher"] = kind, ["gameDir"] = gameDir }));
        }
        Add("official", "Minecraft (Java)", Util.J(env.AppData, ".minecraft"));
        foreach (var p in env.Fs.EnumerateDirectories(Util.J(env.AppData, "ModrinthApp", "profiles")))
            Add("modrinth", "Minecraft · " + Util.Leaf(p), p.Replace('\\', '/'));
        foreach (var p in env.Fs.EnumerateDirectories(Util.J(env.AppData, "PrismLauncher", "instances")))
        {
            var inner = Util.J(p, "minecraft");
            Add("prism", "Minecraft · " + Util.Leaf(p), env.Fs.DirectoryExists(inner) ? inner : Util.J(p, ".minecraft"));
        }
        return list;
    });
}

public sealed class RobloxLocator(LocatorEnvironment env) : IGameLocator
{
    public IReadOnlyList<InstalledGame> Locate() => Util.Safe(() =>
    {
        var dir = Util.J(env.LocalAppData, "Roblox");
        return env.Fs.DirectoryExists(dir) ? [new InstalledGame("roblox", "player", "Roblox", dir, Util.NoExtra)] : [];
    });
}
