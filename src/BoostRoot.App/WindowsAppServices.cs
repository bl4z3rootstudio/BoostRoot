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
        return GameCatalog.Match(Profiles, locators.SelectMany(l => l.Locate()));
    }

    /// <summary>Neutral mid score until the hardware scanner (PC Karnesi) has run.</summary>
    public int PcScore { get; private set; } = 60;

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
                var n = exeNames.Sum(exeName => _gpu.Apply(result.OperationId, exeName, prefs));
                if (n > 0) result = result with { Steps = [.. result.Steps, new StepResult("gpu-profile", StepStatus.Done, "NVIDIA", n)] };
            }
            catch (Exception ex) when (ex is InvalidOperationException or EntryPointNotFoundException or SEHException or AccessViolationException)
            {
                result = result with { Steps = [.. result.Steps, new StepResult("gpu-profile", StepStatus.Skipped, ex.Message)] };
            }
        }
        SessionStarter?.Invoke(entry, settings);
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
        _processes.Start(cmd.FileName, cmd.Arguments, cmd.UseShellExecute);
        return true;
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (_fs.FileExists(_settingsPath))
                return JsonSerializer.Deserialize(TextFile.Decode(_fs.ReadAllBytes(_settingsPath)).Text, AppJsonContext.Default.AppSettings) ?? AppSettings.Default;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return AppSettings.Default;
    }

    public void SaveSettings(AppSettings settings) =>
        _fs.WriteAllBytesAtomic(_settingsPath, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings)));

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
        var path = Path.Combine(_coversDir, id + ".jpg");
        return File.Exists(path) ? path : null;
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

    public Task InstallFfmpegAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (LoadSettings().Offline) throw new HttpRequestException("Offline mode");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BoostRoot/" + Core.Versioning.AppVersion.Current);
        return InstallCore(http, progress, ct);
        static async Task InstallCore(HttpClient h, IProgress<double>? p, CancellationToken c) => await Ffmpeg.InstallAsync(h, p, c);
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
        if (CoverPath(e) is { } have) return have;
        var id = e.Profile?.Id ?? e.Game?.SourceId;
        if (id is null) return null;
        Directory.CreateDirectory(_coversDir);
        var target = Path.Combine(_coversDir, id + ".jpg");
        var miss = Path.Combine(_coversDir, id + ".none");
        if (File.Exists(miss) && File.GetLastWriteTimeUtc(miss) > DateTime.UtcNow.AddDays(-7)) return null;
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
            if (LoadSettings().Offline) return null;
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
            await File.WriteAllTextAsync(miss, appId ?? "", ct);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException or JsonException) { return null; }
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

    public async Task<bool> InstallUpdateAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct)
    {
        if (update.Sha256 is null) return false; // never run an unverified installer
        var path = Path.Combine(Path.GetTempPath(), update.Setup.Name);
        using (var resp = await _http.GetAsync(update.Setup.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buf = new byte[1 << 16];
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (update.Setup.Size > 0) progress?.Report(done / (double)update.Setup.Size);
            }
        }
        await using (var f = File.OpenRead(path))
        {
            var hash = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(f, ct));
            if (hash != update.Sha256) { File.Delete(path); return false; }
        }
        _processes.Start(path, "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS", true);
        ExitForUpdate?.Invoke();
        return true;
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

    private static readonly HashSet<string> SystemProcs = new(StringComparer.OrdinalIgnoreCase)
    { "explorer", "BoostRoot", "ApplicationFrameHost", "SystemSettings", "TextInputHost", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "LockApp", "dwm", "csrss", "winlogon" };

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
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); if (p.ProcessName is "explorer" or "BoostRoot" or "csrss" or "winlogon" or "dwm") return false; p.CloseMainWindow(); if (!p.WaitForExit(1500)) p.Kill(); return true; }
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
        if (CheckDriver() is { } d && d.AgeDays >= 0) items.Add(new("pf.driver", !d.IsOld, $"{d.AgeDays} gün / days", d.IsOld ? "driver" : ""));
        try
        {
            var c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            var freeGb = c.AvailableFreeSpace / 1073741824.0;
            items.Add(new("pf.disk", freeGb >= 20 && c.AvailableFreeSpace * 10 >= c.TotalSize, $"{freeGb:0} GB"));
        }
        catch (IOException) { }
        var dvr = _registry.GetValue(Core.Platform.RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled")?.Data;
        var dvrOn = dvr == "1";
        items.Add(new("pf.gamebar", !dvrOn, dvrOn ? Loc.I["pf.gamebarFix"] : "✓"));
        return items;
    }
}
