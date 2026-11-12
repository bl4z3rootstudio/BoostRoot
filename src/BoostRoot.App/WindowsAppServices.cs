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
        var editor = new SafeFileEditor(_fs, _journal, Path.Combine(shared, "backup"));
        var applier = _applier = new ProfileApplier(new PathResolver(_fs, p => WindowsEnvironment.Expand(ExpandGameVars(p))), editor);
        var registryChanger = _registryChanger = new RegistryChanger(_registry, _journal);
        var powerChanger = new PowerPlanChanger(new WindowsPowerPlans(), _journal);
        _undo = new UndoService(_journal, [new FileUndoHandler(_fs), registryChanger, powerChanger]);
        _orchestrator = new BoostOrchestrator(applier, _processes, _registry, registryChanger, powerChanger, _undo, []);
    }

    public static WindowsAppServices Create() => new();

    private IReadOnlyList<GameProfile> Profiles => _profiles ??= _fs.DirectoryExists(_profilesDir) ? ProfileLoader.LoadDirectory(_fs, _profilesDir) : [];

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
        var processNames = profile.Detect.TryGetValue("process", out var p) ? p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
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
        return new PresentMonSource(names);
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

    public async Task<int> PrefetchCoversAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken ct)
    {
        if (LoadSettings().Offline) return 0;
        Directory.CreateDirectory(_coversDir);
        var added = 0;
        foreach (var e in entries)
        {
            if (CoverPath(e) is not null || e.Profile?.Id is not { } id) continue;
            var appId = e.Profile.Detect.TryGetValue("steam", out var a) ? a : e.Game?.Source == "steam" ? e.Game.SourceId : null;
            if (appId is null || !appId.All(char.IsAsciiDigit)) continue;
            try
            {
                var bytes = await _http.GetByteArrayAsync($"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/library_600x900.jpg", ct);
                if (bytes.Length < 1024) continue;
                await File.WriteAllBytesAsync(Path.Combine(_coversDir, id + ".jpg"), bytes, ct);
                added++;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { }
        }
        return added;
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
}
