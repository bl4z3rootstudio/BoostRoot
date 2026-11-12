using System.Text.Json;
using System.Text.Json.Serialization;
using BoostRoot.Core.Boost;
using BoostRoot.Core.Hardware;
using BoostRoot.Core.IO;
using BoostRoot.Core.Profiles;
using BoostRoot.Core.Safety;
using BoostRoot.Games;
using BoostRoot.Platform.Windows;
using BoostRoot.ViewModels;

namespace BoostRoot.App;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
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

    private WindowsAppServices()
    {
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BoostRoot");
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoostRoot");
        _settingsPath = Path.Combine(local, "settings.json");
        _coversDir = Path.Combine(local, "covers");
        _profilesDir = Path.Combine(AppContext.BaseDirectory, "profiles");

        _journal = new Journal(_fs, Path.Combine(shared, "journal"));
        var editor = new SafeFileEditor(_fs, _journal, Path.Combine(shared, "backup"));
        var applier = new ProfileApplier(new PathResolver(_fs, WindowsEnvironment.Expand), editor);
        var registryChanger = new RegistryChanger(_registry, _journal);
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

    public BoostResult Boost(CatalogEntry entry, BoostLevel level, AppSettings settings)
    {
        var profile = entry.Profile ?? throw new InvalidOperationException("No profile for this game.");
        var processNames = profile.Detect.TryGetValue("process", out var p) ? p.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
        string? exe = null;
        if (entry.Game is { } g && profile.Detect.TryGetValue("exe", out var exeName))
            exe = FindExe(g.InstallPath, exeName);
        var request = new BoostRequest(profile, level, settings.CloseList, SafeTweaks, exe, processNames);
        return _orchestrator.Run(request);
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
        var info = HardwareScanner.Scan(_registry);
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
}
