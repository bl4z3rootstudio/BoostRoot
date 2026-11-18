using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoostRoot.Core.Boost;
using BoostRoot.Core.Hardware;
using BoostRoot.Core.Net;
using BoostRoot.Core.Tools;
using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Stress;
using BoostRoot.Core.Fps;
using BoostRoot.Core.Clips;
using BoostRoot.Core.Tweaks;
using BoostRoot.Core.Updates;
using BoostRoot.Platform.Windows.Sensors;
using BoostRoot.ViewModels.Localization;
using BoostRoot.Core.IO;
using BoostRoot.Core.Profiles;
using BoostRoot.Core.Safety;
using BoostRoot.Games;
using BoostRoot.Platform.Windows;
using BoostRoot.ViewModels;

namespace BoostRoot.App;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<BoostRoot.Core.Crosshair.CrosshairDesign>))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

/// <summary>Composition root: wires Core/Games/Platform together for the real Windows machine.</summary>
public sealed class WindowsAppServices : IAppServices
{
    /// <summary>Safe tweaks applied with every boost (spec 6.6). Advanced tweaks are opt-in on their own screen.</summary>
    public static IReadOnlyList<string> SafeTweaks { get; } = ["power-high", "gamedvr-off", "gamemode-on", "windowed-opt"];

    private readonly IFileSystem _fs = PhysicalFileSystem.Instance;
    private readonly WindowsRegistry _registry = new();
    private readonly WindowsProcessManager _processes = new();
    private readonly Journal _journal;
    private readonly UndoService _undo;
    private readonly BoostOrchestrator _orchestrator;
    private readonly string _settingsPath;
    private readonly string _profilesDir;
    private readonly string _coversDir;
    private IReadOnlyList<GameProfile>? _profiles;
    public static string UserDataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoostRoot");
    private readonly RegistryChanger _registryChanger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private HardwareInfo? _lastHardware;
    private readonly ProfileApplier _applier;
    private readonly SafeFileEditor _editor;
    private readonly WindowsPowerPlans _powerPlans = new();
    private readonly Core.Gpu.GpuProfileChanger _gpu;
    /// <summary>Set by the app: starts the in-game session (overlay, hotkeys, clips, 4:3) after a boost.</summary>
    public Action<CatalogEntry, AppSettings>? SessionStarter { get; set; }
    public Action<string>? SessionStartedFor { get; set; }

    private WindowsAppServices()
    {
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BoostRoot");
        var local = UserDataDir;
        _settingsPath = Path.Combine(local, "settings.json");
        _coversDir = Path.Combine(local, "covers");
        _profilesDir = Path.Combine(AppContext.BaseDirectory, "profiles");

        _journal = new Journal(_fs, Path.Combine(shared, "journal"));
        var editor = _editor = new SafeFileEditor(_fs, _journal, Path.Combine(shared, "backup"));
        var applier = _applier = new ProfileApplier(new PathResolver(_fs, p => WindowsEnvironment.Expand(ExpandGameVars(p))), editor);
        var registryChanger = _registryChanger = new RegistryChanger(_registry, _journal);
        var powerChanger = new PowerPlanChanger(_powerPlans, _journal);
        _gpu = new Core.Gpu.GpuProfileChanger(new NvidiaDrs(), _journal);
        _undo = new UndoService(_journal, [new FileUndoHandler(_fs), registryChanger, powerChanger, _gpu]);
        _orchestrator = new BoostOrchestrator(applier, _processes, _registry, registryChanger, powerChanger, _undo, []);
    }

    public static WindowsAppServices Create() => new();

    private IReadOnlyList<GameProfile> Profiles => _profiles ??= LoadProfiles();

    private IReadOnlyList<GameProfile> LoadProfiles()
    {
        if (!_fs.DirectoryExists(_profilesDir)) return [];
        var json = ProfileLoader.LoadDirectory(_fs, _profilesDir);
        var lib = Path.Combine(_profilesDir, "library.txt");
        return _fs.FileExists(lib) ? [.. json, .. GameLibrary.Parse(TextFile.Decode(_fs.ReadAllBytes(lib)).Text, json)] : json;
    }

    /// <summary>Process names for a profile; "dir:&lt;folder&gt;" resolves to whatever runs from the game's folder.</summary>
    private string[] ResolveProcesses(IEnumerable<string> names)
    {
        var list = new List<string>();
        foreach (var n in names)
        {
            if (!n.StartsWith("dir:", StringComparison.Ordinal)) { list.Add(n); continue; }
            var dir = n[4..].Replace('/', '\\').TrimEnd('\\') + "\\";
            list.AddRange(_processes.List().Where(p => p.Path?.StartsWith(dir, StringComparison.OrdinalIgnoreCase) == true).Select(p => Path.GetFileName(p.Path!)));
        }
        return [.. list.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<CatalogEntry> LoadCatalog()
    {
        var env = WindowsEnvironment.Locators(_fs, _registry);
        IGameLocator[] locators = [new SteamLocator(env), new EpicLocator(env), new RiotLocator(env), new XboxLocator(env), new MinecraftLocator(env), new RobloxLocator(env)];
        var manual = (LoadSettings().ManualGames ?? []).Where(m => File.Exists(m.ExePath))
            .Select(m => new InstalledGame("manual", "manual-" + GameLibrary.Slug(Path.GetFileNameWithoutExtension(m.ExePath)), m.Name, Path.GetDirectoryName(m.ExePath)!,
                new Dictionary<string, string> { ["exe"] = m.ExePath }));
        return GameCatalog.Match(Profiles, locators.SelectMany(l => l.Locate()).Concat(manual));
    }

    /// <summary>Neutral mid score until the hardware scanner (PC Karnesi) has run.</summary>
    public int PcScore { get; private set; } = 60;
    public GpuVendor GpuVendor
    {
        get
        {
            if (_lastHardware is null && !_scanTried) { _scanTried = true; try { _lastHardware = HardwareScanner.Scan(_registry); } catch (Exception) { } }
            return _lastHardware?.MainGpu?.Vendor ?? GpuVendor.Unknown;
        }
    }
    private bool _scanTried;
    public int MonitorHz
    {
        get
        {
            if (_monitorHz < 0) { try { _monitorHz = HardwareScanner.Displays().Select(d => Math.Max(d.CurrentHz, d.MaxHz)).DefaultIfEmpty(0).First(); } catch (Exception) { _monitorHz = 0; } }
            return _monitorHz;
        }
    }
    private int _monitorHz = -1;
    public string? SaveWeeklyCard(BoostRoot.Core.Session.WeeklyReport week) => WeeklyCardRenderer.Save(week);
    public string? LastSessionReport => GameSession.LastSummary?.Text(BoostRoot.ViewModels.Localization.Loc.I.Pick);

    /// <summary>Game being boosted: gives profiles {install}, {steam}, {gameDir}, {documents}.</summary>
    private InstalledGame? _currentGame;

    internal string ExpandGameVars(string path)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments).Replace('\\', '/');
        path = path.Replace("{documents}", docs, StringComparison.Ordinal);
        if (_currentGame is { } g)
        {
            var install = g.InstallPath.Replace('\\', '/').TrimEnd('/');
            path = path.Replace("{install}", install, StringComparison.Ordinal);
            var i = install.IndexOf("/steamapps/", StringComparison.OrdinalIgnoreCase);
            if (i > 0) path = path.Replace("{steam}", install[..i], StringComparison.Ordinal);
            if (g.Extra.TryGetValue("gameDir", out var gd)) path = path.Replace("{gameDir}", gd.Replace('\\', '/'), StringComparison.Ordinal);
        }
        return path; // unresolved {vars} simply match nothing → "settings file not found"
    }

    public BoostResult Boost(CatalogEntry entry, BoostLevel level, AppSettings settings)
    {
        _currentGame = entry.Game;
        EnsureRestorePoint();
        var profile = entry.Profile ?? throw new InvalidOperationException("No profile for this game.");
        var rawNames = profile.Detect.TryGetValue("process", out var p) ? p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
        var processNames = ResolveProcesses(rawNames);
        string? exe = null;
        if (entry.Game is { } g && profile.Detect.TryGetValue("exe", out var exeName))
            exe = FindExe(g.InstallPath, exeName);
        var request = new BoostRequest(profile, level, settings.CloseList, SafeTweaks, exe, processNames);
        var result = _orchestrator.Run(request);
        if (!result.Success) return result;
        if (settings.IsStretched(profile.Id) && DisplayChanger.Current() is { } cur && profile.StretchKeys is { Count: > 0 } keys)
        {
            var (w, h) = Core.Session.StretchPlan.For(cur.Height);
            var guard = new NeverTouchGuard(profile.NeverTouch).WithExemptions(keys);
            StepResult step;
            try
            {
                var rep = _applier.Apply(profile, level, result.OperationId, Core.Session.StretchPlan.Values(profile, w, h), guard);
                step = new StepResult("stretch", rep.Targets.All(t => t.Status != TargetStatus.Failed) ? StepStatus.Done : StepStatus.Failed, $"{w}x{h}", rep.Targets.Sum(t => t.ChangedKeys));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProtectedSettingException) { step = new StepResult("stretch", StepStatus.Failed, ex.Message); }
            result = result with { Steps = [.. result.Steps, step] };
        }
        var exeNames = rawNames.Where(n => !n.StartsWith("dir:", StringComparison.Ordinal)).ToArray();
        if (profile.Levels[level].Gpu is { } prefs && exeNames.Length > 0)
        {
            try
            {
                var capped = prefs with { FpsCap = settings.FpsCap > 0 ? settings.FpsCap : null };
                var n = exeNames.Sum(exeName => _gpu.Apply(result.OperationId, exeName, capped));
                if (n > 0) result = result with { Steps = [.. result.Steps, new StepResult("gpu-profile", StepStatus.Done, "NVIDIA", n)] };
            }
            catch (Exception ex) when (ex is InvalidOperationException or EntryPointNotFoundException or SEHException or AccessViolationException)
            {
                result = result with { Steps = [.. result.Steps, new StepResult("gpu-profile", StepStatus.Skipped, ex.Message)] };
            }
        }
        SessionStarter?.Invoke(entry, settings);
        if (entry.Profile is { } sp) SessionStartedFor?.Invoke(sp.Id);
        return result;
    }

    private string? FindExe(string root, string exeName)
    {
        try { return _fs.EnumerateFiles(root, exeName, true).FirstOrDefault(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Undo(string operationId) => _undo.RevertOperation(operationId);

    public bool Launch(CatalogEntry entry)
    {
        if (entry.Game is null || LaunchCommands.For(entry.Game) is not { } cmd) return false;
        if (entry.Game.Source == "manual")
        {
            // Many games only find their data when started from their own folder.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cmd.FileName) { WorkingDirectory = Path.GetDirectoryName(cmd.FileName)!, UseShellExecute = true })?.Dispose();
            return true;
        }
        _processes.Start(cmd.FileName, cmd.Arguments, cmd.UseShellExecute);
        return true;
    }

    // Settings are only written by this process, so the parsed copy is reused (the auto-boost check asks every 5 s).
    private volatile AppSettings? _settingsCache;

    public AppSettings LoadSettings()
    {
        if (_settingsCache is { } cached) return cached;
        try
        {
            if (_fs.FileExists(_settingsPath))
                return _settingsCache = JsonSerializer.Deserialize(TextFile.Decode(_fs.ReadAllBytes(_settingsPath)).Text, AppJsonContext.Default.AppSettings) ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return AppSettings.Default with { Language = Loc.SystemLanguage() };
    }

    public event Action<AppSettings>? SettingsChanged;

    public void SaveSettings(AppSettings settings)
    {
        _fs.WriteAllBytesAtomic(_settingsPath, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings)));
        _settingsCache = settings;
        SettingsChanged?.Invoke(settings);
    }

    public string? LastBoostOperation(string profileId) =>
        _journal.ReadAll().Where(e => !e.Reverted && e.OperationId.StartsWith($"boost-{profileId}-", StringComparison.Ordinal))
            .OrderByDescending(e => e.At).Select(e => e.OperationId).FirstOrDefault();

    public HardwareReport ScanHardware()
    {
        var info = _lastHardware = HardwareScanner.Scan(_registry);
        var issues = HealthChecks.Evaluate(info, DateOnly.FromDateTime(DateTime.Now));
        var score = HealthChecks.Score(info, issues);
        PcScore = score.Total;
        return new HardwareReport(info, issues, score);
    }

    public string? CoverPath(CatalogEntry entry)
    {
        var id = entry.Profile?.Id ?? entry.Game?.SourceId;
        if (id is null) return null;
        // Custom picture first, then store art, then the generated poster.
        foreach (var name in new[] { id + ".custom.png", id + ".jpg", id + ".gen2.png" })
        {
            var path = Path.Combine(_coversDir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private GameDoctorWindows Doctor => _doctor ??= new GameDoctorWindows(_registry, GameExe, LastBoostOperation, Undo, ScanJunk, CleanJunk,
        () => { _ = GpuVendor; return _lastHardware?.MainGpu?.DriverDate; });
    private GameDoctorWindows? _doctor;

    public async Task<IReadOnlyList<DoctorFinding>> DiagnoseGameAsync(CatalogEntry entry) =>
        GameDoctor.Evaluate(await Task.Run(() => Doctor.Collect(entry)));

    public Task<bool> FixGameAsync(CatalogEntry entry, string findingId) => Doctor.FixAsync(entry, findingId);

    /// <summary>Uses a picture the user chose as the game's cover (copied, so the original can move).</summary>
    public string? SetCustomCover(CatalogEntry e, string imagePath)
    {
        var id = e.Profile?.Id ?? e.Game?.SourceId;
        if (id is null || !File.Exists(imagePath)) return null;
        Directory.CreateDirectory(_coversDir);
        var target = Path.Combine(_coversDir, id + ".custom.png");
        File.Copy(imagePath, target, true);
        return target;
    }

    /// <summary>Poster with the game's own .exe icon (or initials) for games without store art.</summary>
    private async Task<string?> GeneratedCoverAsync(CatalogEntry e, string id)
    {
        var target = Path.Combine(_coversDir, id + ".gen2.png");
        if (File.Exists(target)) return target;
        var icon = await Task.Run(() => GameExe(e) is { } exe ? ExeIcon.Extract(exe) : null);
        try { await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => CoverPoster.Save(e.DisplayName, icon, target)); }
        catch (Exception) { return null; }
        return File.Exists(target) ? target : null;
    }

    private async Task<bool> StoreArtAsync(CatalogEntry e, string target, CancellationToken ct)
    {
        string? url = null;
        try
        {
            if (e.Game is { Source: "epic" } g && g.Extra.TryGetValue("itemId", out var item) && item.Length > 0)
                url = EpicCatalogArt(item);
            else if (e.Game is { Source: "xbox" or "riot" or "minecraft" or "roblox" } || e.Profile?.Detect.ContainsKey("xbox") == true
                     || e.Profile?.Detect.ContainsKey("riot") == true)
                url = await MsStorePosterAsync(e.DisplayName, ct);
            if (url is null) return false;
            var bytes = await _http.GetByteArrayAsync(url, ct);
            if (bytes.Length < 2048) return false;
            await File.WriteAllBytesAsync(target, bytes, ct);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException) { return false; }
    }

    /// <summary>
    /// Poster art from Microsoft's public store catalog (no key needed). Has VALORANT, League of Legends, Minecraft,
    /// Roblox and most PC games, so it is also the fallback when Steam has nothing.
    /// </summary>
    private async Task<string?> MsStorePosterAsync(string name, CancellationToken ct)
    {
        // The plain "Minecraft" search lists the spin-offs first; ask for the real game by its store title.
        if (name.Contains("minecraft", StringComparison.OrdinalIgnoreCase) && !name.Contains("legends", StringComparison.OrdinalIgnoreCase)
            && !name.Contains("dungeons", StringComparison.OrdinalIgnoreCase))
            name = "Minecraft: Java & Bedrock Edition for PC";
        var q = Uri.EscapeDataString(name);
        var json = await _http.GetStringAsync($"https://displaycatalog.mp.microsoft.com/v7.0/productFamilies/autosuggest?market=US&languages=en-US&query={q}&productFamilyNames=Games,Apps", ct);
        using var doc = JsonDocument.Parse(json);
        string Norm(string s) => new([.. s.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
        var want = Norm(name);
        string? best = null;
        var bestScore = 0;
        foreach (var fam in doc.RootElement.GetProperty("Results").EnumerateArray())
            foreach (var p in fam.GetProperty("Products").EnumerateArray())
            {
                var title = p.TryGetProperty("Title", out var t) ? t.GetString() ?? "" : "";
                var n = Norm(title);
                // Skip currency packs, guides and other look-alikes.
                if (n.Length == 0 || title.Contains("Robux", StringComparison.OrdinalIgnoreCase) || n.Contains("guide") || n.Contains("tips") || n.Contains("codes")) continue;
                var score = n == want ? 3 : n.StartsWith(want, StringComparison.Ordinal) ? 2 : want.StartsWith(n, StringComparison.Ordinal) && n.Length >= 4 ? 1 : 0;
                if (score > bestScore && p.TryGetProperty("ProductId", out var pid)) { bestScore = score; best = pid.GetString(); }
            }
        if (best is null) return null;
        var prod = await _http.GetStringAsync($"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={best}&market=US&languages=en-US", ct);
        using var pd = JsonDocument.Parse(prod);
        string? poster = null, box = null;
        foreach (var product in pd.RootElement.GetProperty("Products").EnumerateArray())
            foreach (var lp in product.GetProperty("LocalizedProperties").EnumerateArray())
                if (lp.TryGetProperty("Images", out var imgs))
                    foreach (var img in imgs.EnumerateArray())
                    {
                        var purpose = img.TryGetProperty("ImagePurpose", out var ip) ? ip.GetString() : null;
                        var uri = img.TryGetProperty("Uri", out var u) ? u.GetString() : null;
                        if (uri is null) continue;
                        if (uri.StartsWith("//", StringComparison.Ordinal)) uri = "https:" + uri;
                        if (purpose == "Poster") poster ??= uri + "?w=400&h=600&q=90";
                        else if (purpose == "BoxArt") box ??= uri + "?w=600&h=600&q=90";
                    }
        return poster ?? box;
    }

    /// <summary>Epic keeps its catalog (with box art links) in a base64 JSON file next to the launcher's data.</summary>
    private static string? EpicCatalogArt(string catalogItemId)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Catalog", "catcache.bin");
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(Convert.FromBase64String(File.ReadAllText(path)));
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id) || id.GetString() != catalogItemId || !item.TryGetProperty("keyImages", out var imgs)) continue;
            string? any = null;
            foreach (var img in imgs.EnumerateArray())
            {
                var type = img.TryGetProperty("type", out var t) ? t.GetString() : null;
                var u = img.TryGetProperty("url", out var uu) ? uu.GetString() : null;
                if (u is null) continue;
                if (type is "DieselGameBoxTall" or "OfferImageTall") return u;
                any ??= u;
            }
            return any;
        }
        return null;
    }

    /// <summary>The game's main .exe: manual path, the profile's exe name, else the biggest .exe near the install folder.</summary>
    private static string? GameExe(CatalogEntry e)
    {
        if (e.Game is not { } g) return null;
        if (g.Extra.TryGetValue("exe", out var manual) && File.Exists(manual)) return manual;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        // Riot ships each game's own icon next to its metadata.
        if (g.Source == "riot" && Path.Combine(programData, "Riot Games", "Metadata", $"{g.SourceId}.live", $"{g.SourceId}.live.ico") is var ico && File.Exists(ico)) return ico;
        if (g.Source == "riot" && Path.Combine(g.InstallPath, g.SourceId == "league_of_legends" ? "LeagueClient.exe" : "VALORANT.exe") is var rexe && File.Exists(rexe)) return rexe;
        if (g.Source == "minecraft")
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] launchers = g.Extra.GetValueOrDefault("launcher") switch
            {
                "modrinth" => [Path.Combine(local, "Modrinth App", "Modrinth App.exe"), Path.Combine(local, "Programs", "Modrinth App", "Modrinth App.exe"), @"C:\Program Files\Modrinth App\Modrinth App.exe"],
                "prism" => [Path.Combine(local, "Programs", "PrismLauncher", "prismlauncher.exe")],
                _ => [@"C:\XboxGames\Minecraft Launcher\Content\Minecraft.exe", @"C:\Program Files (x86)\Minecraft Launcher\MinecraftLauncher.exe"],
            };
            return launchers.FirstOrDefault(File.Exists);
        }
        if (!Directory.Exists(g.InstallPath)) return null;
        try
        {
            var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true };
            var exes = Directory.EnumerateFiles(g.InstallPath, "*.exe", opts).Take(400).ToList();
            var names = new List<string>();
            if (e.Profile?.Detect.TryGetValue("exe", out var x) == true) names.Add(x);
            if (e.Profile?.Detect.TryGetValue("process", out var p) == true) names.AddRange(p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(n => !n.StartsWith("dir:", StringComparison.Ordinal)));
            // Prefer the small launcher exe (it carries the nice icon), e.g. VALORANT.exe over VALORANT-Win64-Shipping.exe.
            foreach (var n in names.OrderBy(n => n.Length))
                if (exes.FirstOrDefault(f => Path.GetFileName(f).Equals(n, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
            string[] skip = ["unins", "crash", "redist", "setup", "vc_", "dxsetup", "helper", "report", "updater", "launcher_", "uninstall"];
            return exes.Where(f => !skip.Any(k => Path.GetFileName(f).Contains(k, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public IReadOnlyList<NetInfo> NetworkAdapters() => _lastHardware?.Networks ?? HardwareScanner.Scan(_registry).Networks;

    public Task<PingStats> PingAsync(string host, CancellationToken ct) => NetAnalysis.MeasureAsync(new IcmpPinger(), host, 10, ct);

    public Task<SpeedResult> SpeedTestAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (LoadSettings().Offline) throw new HttpRequestException("Offline mode");
        return new CloudflareSpeedTest(_http).RunAsync(new IcmpPinger(), progress, ct);
    }

    private static JunkCleaner Cleaner() => new(PhysicalFileSystem.Instance, f => new FileInfo(f).Length);

    public IReadOnlyList<JunkScan> ScanJunk()
    {
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var cats = JunkCleaner.Categories(Path.GetTempPath(), Path.Combine(windir, "Temp"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), windir);
        var c = Cleaner();
        return [.. cats.Select(x => c.Scan(x, DateTime.UtcNow))];
    }

    public long CleanJunk(IEnumerable<JunkScan> scans) { var c = Cleaner(); return scans.Sum(c.Clean); }

    private StartupManager Startup() => new(_registry, _registryChanger);

    public IReadOnlyList<StartupItem> StartupItems() =>
        Startup().List(h => _registry.GetValueNames(h, @"Software\Microsoft\Windows\CurrentVersion\Run"));

    public void SetStartupEnabled(StartupItem item, bool enabled) =>
        Startup().SetEnabled($"startup-{DateTime.UtcNow:yyyyMMddHHmmss}", item, enabled);

    public DriverStatus? CheckDriver() => DriverCheck.Check(_lastHardware ?? HardwareScanner.Scan(_registry), DateOnly.FromDateTime(DateTime.Now));

    public void OpenUrl(string url) => _processes.Start(url, null, true);

    private WindowsSensors? _sensors;
    public ISensors Sensors => _sensors ??= new WindowsSensors();

    public IStressWorkload? CreateGpuStress() => new BoostRoot.App.Stress.GpuStressWorkload(Loc.I["stress.gpuWindow"]);

    public long RamStressBytes() => RamStress.Recommended(HardwareScanner.AvailableRamBytes());

    public IFrameSource StartFrames(CatalogEntry entry)
    {
        var names = entry.Profile?.Detect.TryGetValue("process", out var p) == true ? p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
        return new PresentMonSource(ResolveProcesses(names));
    }

    private FpsHistory? _fpsHistory;
    public FpsHistory FpsHistory => _fpsHistory ??= new FpsHistory(PhysicalFileSystem.Instance, Path.Combine(UserDataDir, "fps-history.jsonl"));

    public string? SaveShareCard(ShareCard card, bool story)
    {
        try { return ShareCardRenderer.Save(card, story); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public bool IsGameRunning(IReadOnlyList<string> processNames)
    {
        processNames = ResolveProcesses(processNames);
        var wanted = processNames.Select(n => Path.GetFileNameWithoutExtension(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _processes.List().Any(p => wanted.Contains(Path.GetFileNameWithoutExtension(p.Name)));
    }


    public IClipRecorder? CreateClipRecorder(AppSettings settings) =>
        new ClipRecorder(settings.ClipsOrDefault, (_lastHardware?.Gpus.FirstOrDefault(g => g.Vendor != GpuVendor.Unknown) ?? _lastHardware?.Gpus.FirstOrDefault())?.Vendor ?? GpuVendor.Unknown);

    public bool FfmpegInstalled => Ffmpeg.Installed;

    public async Task InstallFfmpegAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (LoadSettings().Offline) throw new HttpRequestException("Offline mode");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BoostRoot/" + Core.Versioning.AppVersion.Current);
        await Ffmpeg.InstallAsync(http, progress, ct);
    }

    public string ClipsFolder => ClipRecorder.Root(LoadSettings().ClipsOrDefault);

    public IReadOnlyList<ClipFile> ListClips()
    {
        var root = ClipsFolder;
        if (!Directory.Exists(root)) return [];
        try
        {
            return [.. Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".thumbs" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Select(f => new FileInfo(f))
                .Select(fi => new ClipFile(fi.FullName, Path.GetRelativePath(root, fi.DirectoryName!) is var g && g != "." ? g : "—", fi.CreationTimeUtc, fi.Length))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public async Task<string?> ThumbnailAsync(ClipFile clip)
    {
        var dir = Path.Combine(Path.GetDirectoryName(clip.Path)!, ".thumbs");
        var thumb = Path.Combine(dir, Path.GetFileNameWithoutExtension(clip.Path) + ".jpg");
        if (File.Exists(thumb)) return thumb;
        try
        {
            Directory.CreateDirectory(dir);
            File.SetAttributes(dir, FileAttributes.Hidden | FileAttributes.Directory);
            var (code, _) = await Ffmpeg.RunAsync(FfmpegArgs.Thumbnail(clip.Path, thumb));
            return code == 0 && File.Exists(thumb) ? thumb : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return null; }
    }

    public async Task<double?> ClipDurationAsync(string path)
    {
        try { return Ffmpeg.Installed ? await Ffmpeg.DurationAsync(path) : null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private static async Task<string?> Derive(string path, string suffix, Func<string, IReadOnlyList<string>> args)
    {
        var output = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + " " + suffix + ".mp4");
        try { var (code, _) = await Ffmpeg.RunAsync(args(output)); return code == 0 ? output : null; }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { return null; }
    }

    public Task<string?> TrimClipAsync(string path, double start, double end) => Derive(path, "kesit", o => FfmpegArgs.Trim(path, start, end, o));

    public Task<string?> VerticalClipAsync(string path) => Derive(path, "dikey", o => FfmpegArgs.Vertical(path, o));

    public async Task<bool> SendToDiscordAsync(string webhook, string path, CancellationToken ct)
    {
        if (LoadSettings().Offline) return false;
        if (!Uri.TryCreate(webhook, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !(uri.Host is "discord.com" or "discordapp.com" or "canary.discord.com" or "ptb.discord.com") || !uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal))
            return false;
        const long Limit = 10L * 1024 * 1024;
        var file = path;
        string? temp = null;
        try
        {
            if (new FileInfo(path).Length > Limit && path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                var dur = await Ffmpeg.DurationAsync(path) ?? 30;
                temp = Path.Combine(Path.GetTempPath(), "BoostRoot-discord-" + Guid.NewGuid().ToString("N")[..8] + ".mp4");
                var (code, _) = await Ffmpeg.RunAsync(FfmpegArgs.Shrink(path, dur, Limit - 300_000, temp), ct);
                if (code != 0) return false;
                file = temp;
            }
            using var form = new MultipartFormDataContent();
            var json = "{\"content\":\"" + System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(Path.GetFileNameWithoutExtension(path) + " · BoostRoot") + "\"}";
            form.Add(new StringContent(json, System.Text.Encoding.UTF8, "application/json"), "payload_json");
            await using var fs = File.OpenRead(file);
            form.Add(new StreamContent(fs), "files[0]", Path.GetFileName(path));
            using var resp = await _http.PostAsync(uri, form, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.ComponentModel.Win32Exception) { return false; }
        finally { if (temp is not null) try { File.Delete(temp); } catch (IOException) { } }
    }

    private void EnsureRestorePoint()
    {
        var marker = Path.Combine(UserDataDir, "restore-point.done");
        if (File.Exists(marker)) return;
        var ok = RestorePoint.Create("BoostRoot - ilk Boost oncesi / before first boost");
        Directory.CreateDirectory(UserDataDir);
        File.WriteAllText(marker, ok ? "created " + DateTime.Now.ToString("O") : "unavailable (System Protection off) " + DateTime.Now.ToString("O"));
    }

    private readonly SemaphoreSlim _coverGate = new(4);

    /// <summary>Finds cover art: Steam's local cache → Steam CDN by app id → Steam store search by name.
    /// Misses are remembered for a week so we don't hammer the store.</summary>
    public async Task<string?> FetchCoverAsync(CatalogEntry e, CancellationToken ct)
    {
        if (CoverPath(e) is { } have && !have.EndsWith(".gen2.png", StringComparison.OrdinalIgnoreCase)) return have;
        var id = e.Profile?.Id ?? e.Game?.SourceId;
        if (id is null) return null;
        Directory.CreateDirectory(_coversDir);
        var target = Path.Combine(_coversDir, id + ".jpg");
        var miss = Path.Combine(_coversDir, id + ".none");
        if (File.Exists(miss) && File.GetLastWriteTimeUtc(miss) > DateTime.UtcNow.AddDays(-7)) return await GeneratedCoverAsync(e, id);
        await _coverGate.WaitAsync(ct);
        try
        {
            var appId = e.Profile?.Detect.TryGetValue("steam", out var a) == true ? a
                : e.Game?.Source == "steam" ? e.Game.SourceId
                : e.Profile?.Detect.TryGetValue(GameLibrary.CoverKey, out var c) == true ? c : null;
            if (appId is not null && appId.All(char.IsAsciiDigit) && LocalSteamCover(appId) is { } local)
            {
                File.Copy(local, target, true);
                return target;
            }
            // Epic and Xbox games: their own store's box art (Epic from the launcher's local catalog, Xbox from Microsoft's public catalog).
            if (!LoadSettings().Offline && await StoreArtAsync(e, target, ct)) return target;
            // Games that are never on Steam would only match a wrong store page.
            var notOnSteam = e.Game?.Source is "riot" or "minecraft" or "roblox" or "manual" || e.Profile?.Detect.ContainsKey("riot") == true;
            if (LoadSettings().Offline) return await GeneratedCoverAsync(e, id);
            if (notOnSteam) return await GeneratedCoverAsync(e, id);
            if (appId is null || !appId.All(char.IsAsciiDigit)) appId = await SteamSearchAsync(e.DisplayName, ct);
            if (appId is not null)
            {
                using var resp = await _http.GetAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg", ct);
                if (resp.IsSuccessStatusCode && await resp.Content.ReadAsByteArrayAsync(ct) is { Length: > 2048 } bytes)
                {
                    await File.WriteAllBytesAsync(target, bytes, ct);
                    return target;
                }
            }
            // Not on Steam: try Microsoft's store catalog before falling back to a generated poster.
            if (await MsStorePosterAsync(e.DisplayName, ct) is { } ms && await _http.GetByteArrayAsync(ms, ct) is { Length: > 2048 } msBytes)
            {
                await File.WriteAllBytesAsync(target, msBytes, ct);
                return target;
            }
            await File.WriteAllTextAsync(miss, appId ?? "", ct);
            return await GeneratedCoverAsync(e, id);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException or JsonException) { return await GeneratedCoverAsync(e, id); }
        finally { _coverGate.Release(); }
    }

    private string? LocalSteamCover(string appId)
    {
        if (_registry.GetValue(Core.Platform.RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")?.Data is not string root) return null;
        var cache = Path.Combine(root.Replace('/', '\\'), "appcache", "librarycache");
        var flat = Path.Combine(cache, appId + "_library_600x900.jpg");
        if (File.Exists(flat)) return flat;
        var dir = Path.Combine(cache, appId);
        try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "library_600x900*.jpg", SearchOption.AllDirectories).FirstOrDefault() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private async Task<string?> SteamSearchAsync(string name, CancellationToken ct)
    {
        var json = await _http.GetStringAsync("https://store.steampowered.com/api/storesearch/?l=english&cc=US&term=" + Uri.EscapeDataString(name), ct);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return null;
        var want = GameLibrary.Normalize(name);
        string? first = null;
        foreach (var it in items.EnumerateArray())
        {
            var id = it.GetProperty("id").ToString();
            var n = it.TryGetProperty("name", out var nn) ? nn.GetString() ?? "" : "";
            first ??= id;
            if (GameLibrary.Normalize(n) == want) return id;
        }
        // Only trust the first hit when it clearly starts with the same words.
        var firstName = items.GetArrayLength() > 0 && items[0].TryGetProperty("name", out var fn) ? GameLibrary.Normalize(fn.GetString() ?? "") : "";
        return firstName.StartsWith(want, StringComparison.Ordinal) || want.StartsWith(firstName, StringComparison.Ordinal) && firstName.Length > 3 ? first : null;
    }

    public IReadOnlyList<WindowsTweak> AdvancedTweaks => [.. WindowsTweaks.All.Where(t => t.Tier == TweakTier.Advanced)];

    private string TweakFile => Path.Combine(UserDataDir, "tweaks.json");

    public IReadOnlyDictionary<string, string> AppliedTweaks()
    {
        try { return File.Exists(TweakFile) ? JsonSerializer.Deserialize(File.ReadAllText(TweakFile), AppJsonContext.Default.DictionaryStringString) ?? [] : []; }
        catch (Exception ex) when (ex is JsonException or IOException) { return new Dictionary<string, string>(); }
    }

    /// <summary>26.11.17 repair: undo the MMCSS "games-priority" tweak and the SystemResponsiveness change from 26.11.12–16,
    /// which caused system-wide lag on some PCs. Runs once; only resets values that still hold exactly what BoostRoot wrote.</summary>
    public void RepairLagTweaks()
    {
        var marker = Path.Combine(UserDataDir, "repair-26.11.17.done");
        if (File.Exists(marker)) return;
        try
        {
            var map = new Dictionary<string, string>(AppliedTweaks());
            foreach (var id in new[] { "games-priority", "net-throttle-off" })
                if (map.Remove(id, out var op)) try { _undo.RevertOperation(op); } catch (Exception) { }
            Directory.CreateDirectory(UserDataDir);
            File.WriteAllText(TweakFile, JsonSerializer.Serialize(map, AppJsonContext.Default.DictionaryStringString));
            const string mm = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            const string games = mm + @"\Tasks\Games";
            var R = Core.Platform.RegistryHive.LocalMachine;
            // Windows defaults; written only where our values are still present.
            if (_registry.GetValue(R, games, "Scheduling Category")?.Data == "High") _registry.SetValue(R, games, "Scheduling Category", Core.Platform.RegistryValue.Str("Medium"));
            if (_registry.GetValue(R, games, "SFIO Priority")?.Data == "High") _registry.SetValue(R, games, "SFIO Priority", Core.Platform.RegistryValue.Str("Normal"));
            if (_registry.GetValue(R, games, "Priority")?.Data == "6") _registry.SetValue(R, games, "Priority", Core.Platform.RegistryValue.Dword(2));
            if (_registry.GetValue(R, mm, "SystemResponsiveness")?.Data == "10") _registry.SetValue(R, mm, "SystemResponsiveness", Core.Platform.RegistryValue.Dword(20));
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception) { }
    }

    public void SetTweak(string id, bool on)
    {
        var map = new Dictionary<string, string>(AppliedTweaks());
        if (on && !map.ContainsKey(id))
        {
            var op = $"tweak-{id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            WindowsTweaks.Apply(WindowsTweaks.Get(id), op, _registry, _registryChanger, null);
            map[id] = op;
        }
        else if (!on && map.Remove(id, out var op)) _undo.RevertOperation(op);
        Directory.CreateDirectory(UserDataDir);
        File.WriteAllText(TweakFile, JsonSerializer.Serialize(map, AppJsonContext.Default.DictionaryStringString));
    }

    public async Task<UpdateInfo?> CheckUpdateAsync(CancellationToken ct)
    {
        if (LoadSettings().Offline) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, Updater.LatestUrl);
        req.Headers.UserAgent.ParseAdd("BoostRoot/" + Core.Versioning.AppVersion.Current);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        if (Updater.ParseRelease(await resp.Content.ReadAsStringAsync(ct)) is not { } rel) return null;
        var sums = Updater.SumsUrl(rel) is { } u ? await _http.GetStringAsync(u, ct) : null;
        return Updater.Evaluate(rel, Core.Versioning.AppVersion.Current, sums);
    }

    /// <summary>Called after the installer starts so the app can close (the installer replaces it).</summary>
    public Action? ExitForUpdate { get; set; }

    /// <summary>The installer leaves unins000.exe next to the app; the portable zip does not.</summary>
    private static bool IsPortable => !File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public async Task<bool> InstallUpdateAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct)
    {
        // Portable copy: running the installer would put the new version somewhere else and the user would keep
        // opening the old one (update offered again forever), so replace the files in place instead.
        if (IsPortable && update.Portable is { } zip && update.PortableSha256 is { } zipSha)
            return await UpdatePortableAsync(zip, zipSha, progress, ct);
        if (update.Sha256 is null) return false; // never run an unverified installer
        var path = Path.Combine(Path.GetTempPath(), update.Setup.Name);
        if (!await DownloadVerifiedAsync(update.Setup, update.Sha256, path, progress, ct)) return false;
        _processes.Start(path, "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS", true);
        ExitForUpdate?.Invoke();
        return true;
    }

    private async Task<bool> UpdatePortableAsync(ReleaseAsset zip, string sha, IProgress<double>? progress, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "BoostRoot-update");
        try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { }
        Directory.CreateDirectory(work);
        var zipPath = Path.Combine(work, zip.Name);
        if (!await DownloadVerifiedAsync(zip, sha, zipPath, progress, ct)) return false;
        var files = Path.Combine(work, "files");
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, files, true);
        var dir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var exe = Environment.ProcessPath ?? Path.Combine(dir, "BoostRoot.exe");
        var script = Path.Combine(work, "apply.cmd");
        // Wait for this process to exit, copy the new files over the old ones, start the app again.
        File.WriteAllText(script, string.Join("\r\n",
            "@echo off",
            ":wait",
            $"tasklist /FI \"PID eq {Environment.ProcessId}\" | find \"{Environment.ProcessId}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)",
            $"robocopy \"{files}\" \"{dir}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS >nul",
            $"start \"\" \"{exe}\"",
            ""));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
        ExitForUpdate?.Invoke();
        return true;
    }

    /// <summary>Update downloads get their own client: the shared one has a 30 s timeout, which also covered reading the body,
    /// so big updates (with ffmpeg) were cut off at ~70 MB on slower connections and the update silently failed.</summary>
    private static readonly HttpClient UpdateHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    private async Task<bool> DownloadVerifiedAsync(ReleaseAsset asset, string sha, string path, IProgress<double>? progress, CancellationToken ct)
    {
        if (File.Exists(path)) File.Delete(path);
        long done = 0;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, asset.Url);
                req.Headers.UserAgent.ParseAdd("BoostRoot/" + Core.Versioning.AppVersion.Current);
                if (done > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(done, null); // resume
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(TimeSpan.FromSeconds(60));
                using var resp = await UpdateHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                resp.EnsureSuccessStatusCode();
                if (done > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent) done = 0; // server ignored the range
                await using var src = await resp.Content.ReadAsStreamAsync(stall.Token);
                await using var dst = new FileStream(path, done > 0 ? FileMode.Append : FileMode.Create);
                var buf = new byte[1 << 16];
                int n;
                while ((n = await src.ReadAsync(buf, stall.Token)) > 0)
                {
                    stall.CancelAfter(TimeSpan.FromSeconds(60)); // only a 60 s stall aborts, never a slow but moving download
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (asset.Size > 0) progress?.Report(done / (double)asset.Size);
                }
                if (asset.Size <= 0 || done >= asset.Size) break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                UpdateLog($"download attempt {attempt + 1} failed at {done} bytes: {ex.GetType().Name} {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
            }
        }
        await using (var f = File.OpenRead(path))
        {
            var hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(f, ct));
            if (hash != sha) { UpdateLog($"hash mismatch for {asset.Name} ({done} bytes)"); f.Close(); File.Delete(path); return false; }
        }
        return true;
    }

    private static void UpdateLog(string line)
    {
        try { File.AppendAllText(Path.Combine(UserDataDir, "update.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); } catch (Exception) { }
    }

    /// <summary>Uninstaller: reverts every change BoostRoot ever made (journal), including advanced tweaks.</summary>
    public void RevertAll()
    {
        _undo.RevertAll();
        try { File.Delete(TweakFile); } catch (IOException) { }
    }

    public bool FpsToolInstalled => PresentMonSource.Installed;

    public Task InstallFpsToolAsync(CancellationToken ct) =>
        LoadSettings().Offline ? Task.FromException(new HttpRequestException("Offline mode")) : PresentMonSource.InstallAsync(_http, ct);

    /// <summary>Set by the app (needs the window's clipboard).</summary>
    public Action<string>? ClipboardSetter { get; set; }
    public void CopyToClipboard(string text) => ClipboardSetter?.Invoke(text);

    private static string CrosshairFile => Path.Combine(UserDataDir, "crosshairs.json");

    public IReadOnlyList<BoostRoot.Core.Crosshair.CrosshairDesign> LoadCrosshairs()
    {
        try { return File.Exists(CrosshairFile) ? JsonSerializer.Deserialize(File.ReadAllText(CrosshairFile), AppJsonContext.Default.ListCrosshairDesign) ?? [] : []; }
        catch (Exception ex) when (ex is JsonException or IOException) { return []; }
    }

    public void SaveCrosshairs(IReadOnlyList<BoostRoot.Core.Crosshair.CrosshairDesign> designs)
    {
        Directory.CreateDirectory(UserDataDir);
        File.WriteAllText(CrosshairFile, JsonSerializer.Serialize(designs.ToList(), AppJsonContext.Default.ListCrosshairDesign));
    }

    private PlaytimeLog? _playtime;
    public PlaytimeLog Playtime => _playtime ??= new PlaytimeLog(PhysicalFileSystem.Instance, Path.Combine(UserDataDir, "playtime.jsonl"));

    public long CleanRam() => RamCleaner.Clean([]);

    public bool SetGameHighPriority(IReadOnlyList<string> processNames)
    {
        var wanted = ResolveProcesses(processNames).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ok = false;
        foreach (var p in _processes.List().Where(p => wanted.Contains(Path.GetFileNameWithoutExtension(p.Name)))) ok |= _processes.SetHighPriority(p.Id);
        return ok;
    }

    public IReadOnlyList<DnsServer> SystemDns() => DnsBenchmark.System();
    public Task<DnsResult> MeasureDnsAsync(DnsServer server, CancellationToken ct) => DnsBenchmark.MeasureAsync(server, ct);
    public bool SetDns(string address) => DnsChanger.Set(address);

    // ---------------- Poligon ----------------
    private static string PoligonDir => Path.Combine(UserDataDir, "poligon");
    private static string PoligonExe => Path.Combine(PoligonDir, "app", "BoostRootPoligon.exe");
    private const string PoligonRelease = "https://api.github.com/repos/" + Core.Updates.Updater.Repo + "/releases/tags/poligon";

    public string? PoligonVersion()
    {
        try { return File.Exists(PoligonExe) ? File.ReadAllText(Path.Combine(PoligonDir, "app", "version.txt")).Trim() : null; }
        catch (IOException) { return "?"; }
    }

    private async Task<Core.Updates.GitHubRelease?> PoligonReleaseAsync(CancellationToken ct)
    {
        if (LoadSettings().Offline) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, PoligonRelease);
        req.Headers.UserAgent.ParseAdd("BoostRoot/" + Core.Versioning.AppVersion.Current);
        using var resp = await _http.SendAsync(req, ct);
        return resp.IsSuccessStatusCode ? Core.Updates.Updater.ParseRelease(await resp.Content.ReadAsStringAsync(ct)) : null;
    }

    private async Task<string?> SmallAssetAsync(Core.Updates.GitHubRelease r, string name, CancellationToken ct)
    {
        var a = r.Assets.FirstOrDefault(x => x.Name == name);
        if (a is null) return null;
        using var req = new HttpRequestMessage(HttpMethod.Get, a.Url);
        req.Headers.UserAgent.ParseAdd("BoostRoot/" + Core.Versioning.AppVersion.Current);
        using var resp = await UpdateHttp.SendAsync(req, ct);
        return resp.IsSuccessStatusCode ? (await resp.Content.ReadAsStringAsync(ct)).Trim() : null;
    }

    public async Task<string?> PoligonLatestAsync(CancellationToken ct)
    {
        try { return await PoligonReleaseAsync(ct) is { } r ? await SmallAssetAsync(r, "poligon-version.txt", ct) : null; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    public async Task<bool> InstallPoligonAsync(IProgress<double>? progress, CancellationToken ct)
    {
        try
        {
            if (await PoligonReleaseAsync(ct) is not { } r || r.Assets.FirstOrDefault(a => a.Name == "BoostRoot-Poligon.zip") is not { } zip
                || await SmallAssetAsync(r, "BoostRoot-Poligon.sha256", ct) is not { Length: 64 } sha) return false;
            Directory.CreateDirectory(PoligonDir);
            var zipPath = Path.Combine(PoligonDir, "download.zip");
            if (!await DownloadVerifiedAsync(zip, sha.ToLowerInvariant(), zipPath, progress, ct)) return false;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("BoostRootPoligon")) using (p) try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
            var app = Path.Combine(PoligonDir, "app");
            if (Directory.Exists(app)) Directory.Delete(app, true);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, app);
            File.Delete(zipPath);
            return File.Exists(PoligonExe);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException)
        {
            UpdateLog("poligon install failed: " + ex.Message);
            return false;
        }
    }

    public int? GameFpsCap(string game)
    {
        if (game != "valorant") return null;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VALORANT", "Saved", "Config");
            if (!Directory.Exists(root)) return null;
            var f = Directory.EnumerateFiles(root, "RiotUserSettings.ini", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
            return f is null ? null : Core.Poligon.ValorantConfig.FpsCap(File.ReadAllText(f.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public bool LaunchPoligon(string game, double sens, int dpi, string? crosshairCode, bool stretched, int fpsCap = 0)
    {
        if (!File.Exists(PoligonExe)) return false;
        var data = Path.Combine(PoligonDir, "data");
        Directory.CreateDirectory(data);
        var psi = new System.Diagnostics.ProcessStartInfo(PoligonExe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(PoligonExe)! };
        // Everything after "--" goes to the game's own argument parser.
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--data=" + data);
        psi.ArgumentList.Add("--game=" + game);
        if (sens > 0) psi.ArgumentList.Add("--sens=" + sens.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (dpi > 0) psi.ArgumentList.Add("--dpi=" + dpi);
        if (!string.IsNullOrWhiteSpace(crosshairCode)) psi.ArgumentList.Add("--xhair=" + crosshairCode.Trim());
        psi.ArgumentList.Add("--stretched=" + (stretched ? "1" : "0"));
        if (fpsCap > 0) psi.ArgumentList.Add("--fps=" + fpsCap);
        try { System.Diagnostics.Process.Start(psi)?.Dispose(); return true; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public (double? Sens, bool? Stretched, string? CrosshairCode) AimSettings(string game) => game == "cs2" ? Cs2Settings() : ValorantSettings();

    private (double? Sens, bool? Stretched, string? CrosshairCode) Cs2Settings()
    {
        try
        {
            if (_registry.GetValue(Core.Platform.RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")?.Data is not string root) return (null, null, null);
            var userdata = Path.Combine(root.Replace('/', '\\'), "userdata");
            if (!Directory.Exists(userdata)) return (null, null, null);
            var newest = Directory.EnumerateDirectories(userdata)
                .Select(a => Path.Combine(a, "730", "local", "cfg")).Where(Directory.Exists)
                .SelectMany(c => Directory.EnumerateFiles(c, "cs2_user_convars_*.vcfg"))
                .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest is null) return (null, null, null);
            var text = File.ReadAllText(newest.FullName);
            return (Core.Poligon.Cs2Config.Sensitivity(text), null, Core.Poligon.Cs2Config.Crosshair(text)?.ToValorantCode());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, null, null); }
    }

    private (double? Sens, bool? Stretched, string? CrosshairCode) ValorantSettings()
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VALORANT", "Saved", "Config");
            if (!Directory.Exists(root)) return (null, null, null);
            FileInfo? Newest(string name) => Directory.EnumerateFiles(root, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            var user = Newest("RiotUserSettings.ini") is { } u ? File.ReadAllText(u.FullName) : "";
            var sens = Core.Poligon.ValorantConfig.Sensitivity(user);
            var code = Core.Poligon.ValorantCrosshairConfig.Read(user)?.ToValorantCode();
            var str = Newest("GameUserSettings.ini") is { } g ? Core.Poligon.ValorantConfig.Stretched(File.ReadAllText(g.FullName)) : null;
            return (sens, str, code);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, null, null); }
    }

    public IReadOnlyList<Core.Poligon.PoligonRun> PoligonRuns()
    {
        try
        {
            var f = Path.Combine(PoligonDir, "data", "results.json");
            return File.Exists(f) ? Core.Poligon.PoligonRuns.Parse(File.ReadAllText(f)) : [];
        }
        catch (IOException) { return []; }
    }

    public string? BackupSaves()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] sources = [Path.Combine(home, "Saved Games"), Path.Combine(docs, "My Games")];
        var dir = Path.Combine(docs, "BoostRoot", "Yedekler");
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, $"oyun-kayitlari-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
        var count = 0;
        foreach (var src in sources.Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(src, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try
                {
                    var info = new FileInfo(f);
                    if (info.Length > 200L * 1024 * 1024) continue; // skip huge caches/videos
                    System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, f, Path.Combine(Path.GetFileName(src), Path.GetRelativePath(src, f)), System.IO.Compression.CompressionLevel.Fastest);
                    count++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        return count > 0 ? zipPath : null;
    }

    public void OpenGpuApp()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates = GpuVendor == GpuVendor.Amd
            ? [Path.Combine(pf, "AMD", "CNext", "CNext", "RadeonSoftware.exe")]
            : [Path.Combine(pf, "NVIDIA Corporation", "NVIDIA App", "CEF", "NVIDIA App.exe"), Path.Combine(pf, "NVIDIA Corporation", "Control Panel Client", "nvcplui.exe")];
        var exe = candidates.FirstOrDefault(File.Exists);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe ?? (GpuVendor == GpuVendor.Amd
                ? "https://www.amd.com/en/products/software/adrenalin.html" : "https://www.nvidia.com/en-us/software/nvidia-app/")) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception) { }
    }
    public bool RestoreDns() => DnsChanger.Restore();
    public bool DnsChangedByBoostRoot => DnsChanger.HasBackup;
    public void FlushDns() => DnsChanger.FlushDns();

    private static readonly HashSet<string> SystemProcs = new(StringComparer.OrdinalIgnoreCase)
    { "explorer", "BoostRoot", "ApplicationFrameHost", "SystemSettings", "TextInputHost", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "LockApp", "dwm", "csrss", "winlogon" };

    /// <summary>A logon task with highest privileges: BoostRoot needs admin, and a Run-key entry would be blocked by UAC.</summary>
    public bool SetStartWithWindows(bool on)
    {
        try
        {
            var exe = Environment.ProcessPath ?? "";
            if (on && !File.Exists(exe)) return false;
            var args = on
                ? $"/Create /TN \"BoostRoot\" /TR \"\\\"{exe}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F"
                : "/Delete /TN \"BoostRoot\" /F";
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return false;
            p.WaitForExit(10_000);
            return p.ExitCode == 0 || !on; // deleting a task that doesn't exist is fine
        }
        catch (Exception) { return false; }
    }

    public IReadOnlyList<string> RunningProcessPaths()
    {
        var list = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
            using (p)
                try { if (p.SessionId != 0 && BoostRoot.Platform.Windows.PowerAndSound.ProcessPath(p.Id) is { } f) list.Add(f); } catch (Exception) { }
        return list;
    }

    public IReadOnlyList<string> RunningApps()
    {
        var list = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try { if (p.MainWindowHandle != 0 && !SystemProcs.Contains(p.ProcessName) && !BoostOrchestrator.NeverClose.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) list.Add(p.ProcessName); }
            catch (InvalidOperationException) { }
            finally { p.Dispose(); }
        }
        return [.. list.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    public Task<IReadOnlyList<(string Name, string Path, long Bytes)>> GameSizesAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken ct) => Task.Run<IReadOnlyList<(string, string, long)>>(() =>
    {
        var res = new List<(string, string, long)>();
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.Game?.InstallPath is not { Length: > 3 } path || !Directory.Exists(path)) continue;
            long size = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                    try { size += new FileInfo(f).Length; } catch (IOException) { }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            res.Add((e.DisplayName, path, size));
        }
        return res;
    }, ct);

    private static string BenchFile => Path.Combine(UserDataDir, "benchmark.txt");
    public int? LastBenchmark
    {
        get => File.Exists(BenchFile) && int.TryParse(File.ReadAllText(BenchFile), out var v) ? v : null;
        set { Directory.CreateDirectory(UserDataDir); File.WriteAllText(BenchFile, value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""); }
    }

    public int ApplyCs2Crosshair(BoostRoot.Core.Crosshair.CrosshairDesign design)
    {
        if (IsGameRunning(["cs2.exe"])) throw new InvalidOperationException(Loc.I["boost.gameRunning"]);
        if (_registry.GetValue(Core.Platform.RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath")?.Data is not string root) return 0;
        var userdata = Path.Combine(root.Replace('/', '\\'), "userdata");
        if (!Directory.Exists(userdata)) return 0;
        var op = $"crosshair-cs2-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var convars = design.ToSourceConvars();
        var n = 0;
        foreach (var acc in Directory.EnumerateDirectories(userdata))
        {
            var cfg = Path.Combine(acc, "730", "local", "cfg");
            if (!Directory.Exists(cfg)) continue;
            foreach (var file in Directory.EnumerateFiles(cfg, "cs2_user_convars_*.vcfg"))
            {
                try
                {
                    var r = _editor.Apply(op, file, text =>
                    {
                        var doc = Core.Config.ConfigFormats.Get("keyvalues").Parse(text);
                        foreach (var (k, v) in convars) doc.Set("config/convars/" + k, v);
                        return doc.Serialize();
                    }, "CS2 crosshair");
                    if (r.Success) n++;
                }
                catch (ArgumentException) { }
            }
        }
        return n;
    }

    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _cpuSnap = [];

    public IReadOnlyList<ProcessUsage> TopProcesses(int count)
    {
        var now = DateTime.UtcNow;
        var list = new List<ProcessUsage>();
        var seen = new HashSet<int>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4) continue;
                var cpu = p.TotalProcessorTime;
                seen.Add(p.Id);
                double pct = 0;
                if (_cpuSnap.TryGetValue(p.Id, out var prev) && (now - prev.At).TotalMilliseconds > 100)
                    pct = (cpu - prev.Cpu).TotalMilliseconds / (now - prev.At).TotalMilliseconds / Environment.ProcessorCount * 100;
                _cpuSnap[p.Id] = (cpu, now);
                list.Add(new ProcessUsage(p.Id, p.ProcessName, Math.Max(0, pct), p.WorkingSet64 / 1048576.0));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            finally { p.Dispose(); }
        }
        foreach (var dead in _cpuSnap.Keys.Where(k => !seen.Contains(k)).ToList()) _cpuSnap.Remove(dead);
        return [.. list.OrderByDescending(x => x.CpuPercent).ThenByDescending(x => x.RamMb).Take(count)];
    }

    public bool KillProcess(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); if (p.ProcessName is "explorer" or "BoostRoot" or "csrss" or "winlogon" or "dwm" || BoostRoot.Core.Boost.BoostOrchestrator.NeverClose.Contains(p.ProcessName.ToLowerInvariant())) return false; p.CloseMainWindow(); if (!p.WaitForExit(1500)) p.Kill(); return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public IReadOnlyList<PreflightItem> Preflight(AppSettings settings)
    {
        var items = new List<PreflightItem>();
        var running = RunningApps().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var open = settings.CloseList.Where(running.Contains).ToList();
        items.Add(new("pf.apps", open.Count == 0, open.Count == 0 ? "✓" : string.Join(", ", open.Select(AppCatalog.Friendly)), "close-apps"));
        var avail = HardwareScanner.AvailableRamBytes();
        var total = (ulong)((_lastHardware?.RamGb ?? 0) * 1073741824.0);
        var pct = total > 0 ? avail * 100.0 / total : 50;
        items.Add(new("pf.ram", pct >= 25, $"{avail / 1073741824.0:0.0} GB (%{pct:0})", "ram"));
        var plan = _powerPlans.GetActive();
        var high = plan == Core.Platform.PowerSchemes.HighPerformance || plan == new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");
        items.Add(new("pf.power", high, high ? "✓" : Loc.I["pf.powerFix"]));
        var s = Sensors.Read();
        if (s.GpuTempC is { } t) items.Add(new("pf.gpuTemp", t < 70, $"{t:0}°C"));
        if (CheckDriver() is { } d && d.AgeDays >= 0) items.Add(new("pf.driver", !d.IsOld, Loc.I.Format("pf.days", d.AgeDays), d.IsOld ? "driver" : ""));
        try
        {
            var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            var freeGb = c.AvailableFreeSpace / 1073741824.0;
            items.Add(new("pf.disk", freeGb >= 20 && c.AvailableFreeSpace * 10 >= c.TotalSize, $"{freeGb:0} GB"));
        }
        catch (IOException) { }
        if (BoostRoot.Platform.Windows.DisplayChanger.Current() is { } cur && BoostRoot.Platform.Windows.DisplayChanger.MaxRefresh() is var maxHz && maxHz > 0)
            items.Add(new("pf.hz", cur.Hz + 1 >= maxHz, cur.Hz + 1 >= maxHz ? $"{cur.Hz} Hz ✓" : Loc.I.Format("pf.hzFix", cur.Hz, maxHz), cur.Hz + 1 >= maxHz ? "" : "hz"));
        var dvr = _registry.GetValue(Core.Platform.RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled")?.Data;
        var dvrOn = dvr == "1";
        items.Add(new("pf.gamebar", !dvrOn, dvrOn ? Loc.I["pf.gamebarFix"] : "✓"));
        var bat = BoostRoot.Platform.Windows.PowerAndSound.Battery();
        if (bat.HasBattery) items.Add(new("pf.battery", !bat.HurtsFps, bat.HurtsFps ? Loc.I.Format("pf.batteryFix", bat.Percent ?? 0) : Loc.I["pf.batteryOk"]));
        return items;
    }

    public bool SetMaxRefresh() => BoostRoot.Platform.Windows.DisplayChanger.SetMaxRefresh();

    public void PlayBoostSound() => BoostRoot.Platform.Windows.PowerAndSound.PlayBoost();

    public async Task<IReadOnlyList<AppUpdate>> ScanAppUpdatesAsync(CancellationToken ct)
    {
        var (_, output) = await PowerShellRunner.WingetAsync(["upgrade", "--include-unknown", "--accept-source-agreements", "--disable-interactivity"], ct);
        return WingetTable.Parse(output);
    }

    public async Task<bool> UpgradeAppAsync(AppUpdate app, CancellationToken ct)
    {
        var (code, _) = await PowerShellRunner.WingetAsync(["upgrade", "--id", app.Id, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"], ct);
        return code == 0;
    }

    public async Task<IReadOnlyList<WinUpdate>> ScanWindowsUpdatesAsync(CancellationToken ct) =>
        WindowsUpdateJson.Parse(await PowerShellRunner.RunAsync(WindowsUpdateJson.SearchScript, ct, TimeSpan.FromMinutes(10)));

    public async Task<(bool Ok, bool Reboot)> InstallWindowsUpdatesAsync(IReadOnlyList<WinUpdate> updates, CancellationToken ct) =>
        WindowsUpdateJson.ParseInstall(await PowerShellRunner.RunAsync(WindowsUpdateJson.InstallScript(updates.Select(u => u.Id)), ct, TimeSpan.FromHours(2)));

    public Task HideWindowsUpdateAsync(WinUpdate update, CancellationToken ct) => PowerShellRunner.RunAsync(WindowsUpdateJson.HideScript(update.Id), ct, TimeSpan.FromMinutes(5));

    public async Task<IReadOnlyList<BloatApp>> ScanBloatAsync(CancellationToken ct) =>
        Bloatware.Match(await PowerShellRunner.RunAsync(Bloatware.ListScript, ct, TimeSpan.FromMinutes(2)), (tr, en) => Loc.I.Text(tr, en));

    public async Task<bool> RemoveBloatAsync(BloatApp app, CancellationToken ct) =>
        (await PowerShellRunner.RunAsync(Bloatware.RemoveScript(app.PackageFullName), ct, TimeSpan.FromMinutes(3))).Contains("OK", StringComparison.Ordinal);

    public void ReportBug(string context)
    {
        var hw = _lastHardware;
        var body = $"""
            **Ne oldu? / What happened?**


            **Ne bekliyordun? / What did you expect?**


            ---
            BoostRoot {Core.Versioning.AppVersion.Current} · {Environment.OSVersion.VersionString} · {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}
            CPU: {hw?.CpuName ?? "?"} · GPU: {string.Join(", ", hw?.Gpus.Select(g => g.Name) ?? [])} · RAM: {hw?.RamGb ?? 0:0} GB
            Ekran / Page: {context}
            """;
        var url = "https://github.com/bl4z3rootstudio/BoostRoot/issues/new?title=" + Uri.EscapeDataString("[Hata] ") + "&body=" + Uri.EscapeDataString(body);
        OpenUrl(url);
    }
}
