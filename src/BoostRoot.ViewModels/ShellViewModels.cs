using BoostRoot.Core.Versioning;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed partial class NavItemViewModel(string key, string icon, ObservableObject page) : ObservableObject
{
    public string Key { get; } = key;
    public string Title => Loc.I["nav." + Key];
    /// <summary>SVG path data for the icon.</summary>
    public string Icon { get; } = icon;
    public ObservableObject Page { get; } = page;
    [ObservableProperty] public partial bool IsActive { get; set; }
}

/// <summary>Shown for sections built in later plans.</summary>
public sealed class PlaceholderViewModel(string key) : ObservableObject
{
    public string Title => Loc.I["nav." + key];
    public string Message => Loc.I["common.soon"];
}

public sealed partial class MetricToggleViewModel(string key, bool on, Action changed) : ObservableObject
{
    public string Key { get; } = key;
    public string Title => Loc.I["metric." + Key];
    [ObservableProperty] public partial bool IsOn { get; set; } = on;
    partial void OnIsOnChanged(bool value) => changed();
}

public sealed partial class AppChoiceViewModel(string process, string name, bool running, bool chosen, Action changed) : ObservableObject
{
    public string Process { get; } = process;
    public string Name { get; } = name;
    public bool IsRunning { get; } = running;
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    [ObservableProperty] public partial bool IsChecked { get; set; } = chosen;
    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>Apps people usually want closed while gaming (process name, display name).</summary>
public static class AppCatalog
{
    public static IReadOnlyList<(string Process, string Name)> Known { get; } =
    [
        ("chrome", "Google Chrome"), ("msedge", "Microsoft Edge"), ("opera", "Opera / Opera GX"), ("firefox", "Firefox"), ("brave", "Brave"),
        ("spotify", "Spotify"), ("Discord", "Discord"), ("steamwebhelper", "Steam (tarayıcı parçası)"), ("EpicGamesLauncher", "Epic Games Launcher"),
        ("EADesktop", "EA app"), ("upc", "Ubisoft Connect"), ("Battle.net", "Battle.net"), ("GalaxyClient", "GOG Galaxy"), ("OneDrive", "OneDrive"),
        ("ms-teams", "Microsoft Teams"), ("WhatsApp", "WhatsApp"), ("Telegram", "Telegram"), ("Zoom", "Zoom"), ("Slack", "Slack"),
        ("Creative Cloud", "Adobe Creative Cloud"), ("Dropbox", "Dropbox"), ("GoogleDriveFS", "Google Drive"), ("qbittorrent", "qBittorrent"),
        ("uTorrent", "µTorrent"), ("wallpaper64", "Wallpaper Engine"), ("Notion", "Notion"), ("Figma", "Figma"), ("obs64", "OBS Studio"),
    ];

    public static string Friendly(string process) => Known.FirstOrDefault(k => k.Process.Equals(process, StringComparison.OrdinalIgnoreCase)).Name ?? process;
}

public sealed partial class AccentChoiceViewModel(string key, string hex, bool selected) : ObservableObject
{
    public string Key { get; } = key;
    public string Hex { get; } = hex;
    public string Name => Loc.I["accent." + Key];
    [ObservableProperty] public partial bool IsSelected { get; set; } = selected;
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppServices _services;
    private readonly Action<AppSettings> _onChanged;
    private AppSettings _base;
    private readonly bool _ready;

    public IReadOnlyList<string> Languages => Loc.Languages;
    public IReadOnlyList<string> Corners { get; } = ["tl", "tr", "bl", "br"];
    public IReadOnlyList<string> AudioModes { get; } = ["none", "system", "system+mic"];
    public IReadOnlyList<int> ClipHeights { get; } = [720, 1080, 1440];
    public IReadOnlyList<int> ClipFpsOptions { get; } = [30, 60];
    public IReadOnlyList<int> ReplayLengths { get; } = [15, 30, 60, 120];
    public string VersionText => Loc.I.Format("settings.version", AppVersion.Current);
    public IReadOnlyList<MetricToggleViewModel> Metrics { get; }

    [ObservableProperty] public partial string Language { get; set; }
    [ObservableProperty] public partial bool Sounds { get; set; }
    [ObservableProperty] public partial bool Animations { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }
    [ObservableProperty] public partial string CloseList { get; set; }
    [ObservableProperty] public partial string OverlayCorner { get; set; }
    [ObservableProperty] public partial bool CrosshairEnabled { get; set; }
    [ObservableProperty] public partial string CrosshairColor { get; set; }
    [ObservableProperty] public partial int CrosshairSize { get; set; }
    [ObservableProperty] public partial int CrosshairThickness { get; set; }
    [ObservableProperty] public partial int CrosshairGap { get; set; }
    [ObservableProperty] public partial bool CrosshairDot { get; set; }
    [ObservableProperty] public partial bool CrosshairOutline { get; set; }
    [ObservableProperty] public partial bool ReplayEnabled { get; set; }
    [ObservableProperty] public partial int ReplaySeconds { get; set; }
    [ObservableProperty] public partial int ClipHeight { get; set; }
    [ObservableProperty] public partial int ClipFps { get; set; }
    [ObservableProperty] public partial string ClipAudio { get; set; }
    [ObservableProperty] public partial string ClipFolder { get; set; }
    [ObservableProperty] public partial string DiscordWebhook { get; set; }
    [ObservableProperty] public partial bool AutoUndo { get; set; }
    [ObservableProperty] public partial string Accent { get; set; }
    [ObservableProperty] public partial bool TrayIcon { get; set; }
    public IReadOnlyList<AccentChoiceViewModel> Accents { get; }

    [RelayCommand]
    public void SetAccent(string key)
    {
        Accent = AccentPalette.Get(key).Key;
        foreach (var a in Accents) a.IsSelected = a.Key == Accent;
        AccentPalette.Raise(Accent);
    }
    private bool _appsLoadPending;
    /// <summary>Called when the settings page opens.</summary>
    public void EnsureAppsLoaded() { if (_appsLoadPending) { _appsLoadPending = false; LoadRunningApps(); } }
    public System.Collections.ObjectModel.ObservableCollection<string> RunningApps { get; } = [];
    public System.Collections.ObjectModel.ObservableCollection<AppChoiceViewModel> AppChoices { get; } = [];

    /// <summary>Known launchers/apps + whatever is open right now, each with a checkbox (no typing).</summary>
    [RelayCommand]
    public void LoadRunningApps()
    {
        var chosen = (CloseList ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var running = _services.RunningApps().ToHashSet(StringComparer.OrdinalIgnoreCase);
        AppChoices.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (proc, name) in AppCatalog.Known.Concat(running.Select(r => (r, AppCatalog.Friendly(r)))).Concat(chosen.Select(c => (c, AppCatalog.Friendly(c)))))
        {
            if (!seen.Add(proc)) continue;
            AppChoices.Add(new AppChoiceViewModel(proc, name, running.Contains(proc), chosen.Contains(proc), SyncCloseList));
        }
        var ordered = AppChoices.OrderByDescending(a => a.IsRunning).ThenByDescending(a => a.IsChecked).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        AppChoices.Clear();
        foreach (var a in ordered) AppChoices.Add(a);
    }

    private void SyncCloseList() => CloseList = string.Join(", ", AppChoices.Where(a => a.IsChecked).Select(a => a.Process));

    [RelayCommand]
    public void AddToCloseList(string app)
    {
        CloseList = string.IsNullOrWhiteSpace(CloseList) ? app : CloseList + ", " + app;
        RunningApps.Remove(app);
    }

    public SettingsViewModel(IAppServices services, Action<AppSettings> onChanged)
    {
        _services = services;
        _onChanged = onChanged;
        var s = _base = services.LoadSettings();
        Language = s.Language;
        Sounds = s.Sounds;
        Animations = s.Animations;
        Offline = s.Offline;
        CloseList = string.Join(", ", s.CloseList);
        OverlayCorner = s.OverlayCorner;
        var c = s.CrosshairOrDefault;
        (CrosshairEnabled, CrosshairColor, CrosshairSize, CrosshairThickness, CrosshairGap, CrosshairDot, CrosshairOutline) = (c.Enabled, c.Color, c.Size, c.Thickness, c.Gap, c.Dot, c.Outline);
        var k = s.ClipsOrDefault;
        (ReplayEnabled, ReplaySeconds, ClipHeight, ClipFps, ClipAudio, ClipFolder, DiscordWebhook) = (k.ReplayEnabled, k.ReplaySeconds, k.Height, k.Fps, k.Audio, k.Folder ?? "", k.DiscordWebhook ?? "");
        AutoUndo = s.AutoUndo;
        Accent = AccentPalette.Get(s.Accent).Key;
        TrayIcon = s.TrayIcon;
        Accents = [.. AccentPalette.All.Select(a => new AccentChoiceViewModel(a.Key, a.Hex, a.Key == Accent))];
        _appsLoadPending = true;
        Metrics = [.. AppSettings.AllMetrics.Select(m => new MetricToggleViewModel(m, s.Metrics.Contains(m), Save))];
        Loc.I.SetLanguage(s.Language);
        _ready = true;
    }

    partial void OnLanguageChanged(string value) { Loc.I.SetLanguage(value); Save(); }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not nameof(Language)) Save();
    }

    public AppSettings Current => _base with
    {
        AutoUndo = AutoUndo, Accent = Accent ?? "purple", TrayIcon = TrayIcon,
        Language = Language ?? "tr", Sounds = Sounds, Animations = Animations, Offline = Offline,
        CloseList = [.. (CloseList ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
        OverlayMetrics = [.. Metrics.Where(m => m.IsOn).Select(m => m.Key)],
        OverlayCorner = OverlayCorner ?? "tl",
        CrosshairDesign = _base.CrosshairDesign is { } cd && cd.Color == CrosshairColor && cd.Dot == CrosshairDot && cd.Outline == CrosshairOutline ? cd : null,
        Crosshair = new CrosshairSettings(CrosshairEnabled, CrosshairColor ?? "#00FF66", Math.Clamp(CrosshairSize, 0, 40), Math.Clamp(CrosshairThickness, 1, 10), Math.Clamp(CrosshairGap, 0, 30), CrosshairDot, CrosshairOutline),
        Clips = new ClipSettings(ReplayEnabled, ReplaySeconds, ClipHeight, ClipFps, string.IsNullOrWhiteSpace(ClipFolder) ? null : ClipFolder, ClipAudio ?? "system", string.IsNullOrWhiteSpace(DiscordWebhook) ? null : DiscordWebhook.Trim()),
    };

    public void UseCrosshair(BoostRoot.Core.Crosshair.CrosshairDesign d)
    {
        var o = d.ToOverlay();
        (CrosshairColor, CrosshairSize, CrosshairThickness, CrosshairGap, CrosshairDot, CrosshairOutline) = (o.Color, o.Size, o.Thickness, o.Gap, o.Dot, o.Outline);
        CrosshairEnabled = true;
        Update(s => s with { CrosshairDesign = d });
    }

    /// <summary>Called by other screens (e.g. 4:3 toggle on Home) so all settings stay in one place.</summary>
    public void Update(Func<AppSettings, AppSettings> change) { _base = change(Current); Save(); }

    private void Save()
    {
        if (!_ready) return;
        var s = _base = Current;
        _services.SaveSettings(s);
        _onChanged(s);
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    public IReadOnlyList<NavItemViewModel> Nav { get; }
    public HomeViewModel Home { get; }
    public SettingsViewModel Settings { get; }
    public ScanViewModel Scan { get; }
    public NetViewModel Net { get; }
    public ToolsViewModel Tools { get; }
    public StressViewModel Stress { get; }
    public FpsViewModel Fps { get; }
    public ClipsViewModel Clips { get; }
    public CrosshairViewModel Crosshair { get; }
    public MonitorViewModel Monitor { get; }
    public UpdatesViewModel Updates { get; }

    [ObservableProperty] public partial ObservableObject CurrentPage { get; set; }
    [ObservableProperty] public partial bool ShowWelcome { get; set; }
    [ObservableProperty] public partial BoostRoot.Core.Updates.UpdateInfo? Update { get; set; }
    [ObservableProperty] public partial double UpdateProgress { get; set; }
    [ObservableProperty] public partial bool IsUpdating { get; set; }
    [ObservableProperty] public partial string? UpdateError { get; set; }
    public string UpdateText => Update is null ? "" : Loc.I.Format("update.available", Update.Version);
    partial void OnUpdateChanged(BoostRoot.Core.Updates.UpdateInfo? value) => OnPropertyChanged(nameof(UpdateText));
    private readonly IAppServices _services;

    public const string SponsorsUrl = "https://github.com/bl4z3rootstudio/BoostRoot#destek-ol--support";
    [RelayCommand] public void Donate() => _services.OpenUrl(SponsorsUrl);
    [RelayCommand] public void ReportBug() => _services.ReportBug(CurrentPage.GetType().Name);

    public async Task CheckUpdateAsync()
    {
        try { Update = await _services.CheckUpdateAsync(CancellationToken.None); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { }
    }

    [RelayCommand]
    public async Task InstallUpdateAsync()
    {
        if (Update is null || IsUpdating) return;
        IsUpdating = true;
        UpdateError = null;
        try
        {
            if (!await _services.InstallUpdateAsync(Update, new Progress<double>(p => UpdateProgress = p), CancellationToken.None))
                UpdateError = Loc.I["update.failed"];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            UpdateError = Loc.I["update.failed"];
        }
        finally { IsUpdating = false; }
    }

    [RelayCommand]
    public void FinishWelcome()
    {
        ShowWelcome = false;
        Settings.Update(s => s with { FirstRunDone = true });
    }

    public MainViewModel(IAppServices services)
    {
        _services = services;
        Home = new HomeViewModel(services);
        Settings = new SettingsViewModel(services, s => Home.ApplySettings(s));
        Home.FavoriteChanged = (id, on) => Settings.Update(s => s with
        {
            Favorites = on ? [.. (s.Favorites ?? []).Append(id).Distinct()] : [.. (s.Favorites ?? []).Where(x => x != id)],
        });
        Home.CopyText = services.CopyToClipboard;
        Home.StretchChanged = (id, on) => Settings.Update(s => s with
        {
            StretchGames = on ? [.. (s.StretchGames ?? []).Append(id).Distinct()] : [.. (s.StretchGames ?? []).Where(x => x != id)],
        });
        Scan = new ScanViewModel(services, r => { Home.RefreshRecommendation(); Fps!.Cpu = r.Info.CpuName; Fps.Gpu = r.Info.Gpus.FirstOrDefault()?.Name ?? ""; });
        Net = new NetViewModel(services);
        Tools = new ToolsViewModel(services);
        Stress = new StressViewModel(services);
        Fps = new FpsViewModel(services);
        Clips = new ClipsViewModel(services, () => Settings!.Current);
        ShowWelcome = !services.LoadSettings().FirstRunDone;
        Crosshair = new CrosshairViewModel(services, d => Settings.UseCrosshair(d));
        Monitor = new MonitorViewModel(services, () => Settings.Current);
        Updates = new UpdatesViewModel(services);
        Home.CurrentCrosshairCode = () => Crosshair.Design.ToValorantCode();
        Home.ApplySharedCrosshair = code => { if (BoostRoot.Core.Crosshair.CrosshairDesign.FromValorantCode(code) is { } d) Crosshair.Load(d); };
        Nav =
        [
            new("boost", "M13 2 4 14h7l-1 8 9-12h-7z", Home),
            new("scan", "M3 3h18v14H3zM8 21h8M12 17v4", Scan),
            new("fps", "M3 12h4l3 8 4-16 3 8h4", Fps),
            new("stress", "M12 2a7 7 0 0 0-4 12.7V18h8v-3.3A7 7 0 0 0 12 2zM9 22h6", Stress),
            new("net", "M2 9a15 15 0 0 1 20 0M5 13a10 10 0 0 1 14 0M8.5 16.5a5 5 0 0 1 7 0M12 20h.01", Net),
            new("clips", "M4 6h12v12H4zM16 10l5-3v10l-5-3", Clips),
            new("updates", "M11 3h2v10l3.5-3.5 1.4 1.4L12 16.8 6.1 10.9l1.4-1.4L11 13zM4 19h16v2H4z", Updates),
            new("monitor", "M3 21V10h4v11zm7 0V3h4v18zm7 0v-7h4v7z", Monitor),
            new("crosshair", "M11 2h2v7h-2zM11 15h2v7h-2zM2 11h7v2H2zM15 11h7v2h-7zM11 11h2v2h-2z", Crosshair),
            new("tools", "M14.7 6.3a4 4 0 0 0-5.4 5.4L3 18l3 3 6.3-6.3a4 4 0 0 0 5.4-5.4l-2.6 2.6-2.4-.6-.6-2.4z", Tools),
            new("settings", "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z", Settings),
        ];
        CurrentPage = Home;
        Nav[0].IsActive = true;
    }

    [RelayCommand]
    public void Navigate(NavItemViewModel item)
    {
        foreach (var n in Nav) n.IsActive = n == item;
        CurrentPage = item.Page;
        if (item.Page == Tools && Tools.Junk.Count == 0) _ = Tools.LoadCommand.ExecuteAsync(null);
        if (item.Page == Clips) Clips.Refresh();
        if (item.Page == Settings) Settings.EnsureAppsLoaded();
        if (item.Page == Monitor) { Monitor.RunChecks(); Monitor.RefreshProcesses(); }
        if (item.Page == Fps) Fps.Reload();
    }
}
