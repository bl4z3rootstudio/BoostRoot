using BoostRoot.Core.Versioning;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed partial class NavItemViewModel : ObservableObject
{
    public NavItemViewModel(string key, string icon, ObservableObject page)
    {
        Key = key; Icon = icon; Page = page;
        Loc.I.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Title)); // live language switch
    }

    public string Key { get; }
    public string Title => Loc.I["nav." + Key];
    /// <summary>SVG path data for the icon.</summary>
    public string Icon { get; }
    public ObservableObject Page { get; }
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
    [ObservableProperty] public partial bool AutoBoost { get; set; }
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool SessionReport { get; set; }
    [ObservableProperty] public partial bool StutterDetect { get; set; }
    [ObservableProperty] public partial bool PacketLossAlert { get; set; }
    [ObservableProperty] public partial bool PauseDownloads { get; set; }
    [ObservableProperty] public partial bool Notifications { get; set; }
    [ObservableProperty] public partial bool Hotkeys { get; set; }
    [ObservableProperty] public partial bool OverlayIcons { get; set; }
    [ObservableProperty] public partial string OverlayColor { get; set; }
    [ObservableProperty] public partial int OverlayOpacity { get; set; }
    [ObservableProperty] public partial int OverlayScale { get; set; }
    [ObservableProperty] public partial int FpsCap { get; set; }
    [ObservableProperty] public partial string? ShareSettingsCode { get; set; }
    [ObservableProperty] public partial string? ImportCode { get; set; }
    [ObservableProperty] public partial string? ShareSettingsStatus { get; set; }
    public IReadOnlyList<string> OverlayColors { get; } = ["#FFFFFF", "#00FF66", "#22D3EE", "#FACC15", "#F472B6", "#A855F7"];
    public IReadOnlyList<int> FpsCaps { get; } = [0, 60, 75, 120, 144, 165, 240, 300];
    /// <summary>Monitor-based suggestion shown next to the cap picker.</summary>
    public string FpsCapHint => _services.MonitorHz is > 0 and var hz
        ? Loc.I.Format("settings.fpsCapHint", hz, BoostRoot.Core.Session.GameStats.SuggestedCap(hz)) : "";

    [RelayCommand] public void UseSuggestedCap() { if (_services.MonitorHz > 0) FpsCap = BoostRoot.Core.Session.GameStats.SuggestedCap(_services.MonitorHz); }
    [RelayCommand] public void SetOverlayColor(string c) => OverlayColor = c;
    [RelayCommand] public void SetCrosshairColor(string c) => CrosshairColor = c;

    /// <summary>Everything except personal paths/webhooks, as a short code to send to a friend.</summary>
    [RelayCommand]
    public void ExportSettings()
    {
        ShareSettingsCode = SettingsCode.Encode(Current);
        _services.CopyToClipboard(ShareSettingsCode);
        ShareSettingsStatus = Loc.I["settings.codeCopied"];
    }

    [RelayCommand]
    public void ImportSettings()
    {
        if (SettingsCode.Decode(ImportCode ?? "", Current) is not { } s) { ShareSettingsStatus = Loc.I["settings.codeBad"]; return; }
        _base = s;
        _loading = true;
        Load(s);
        _loading = false;
        Save();
        ShareSettingsStatus = Loc.I["settings.codeApplied"];
    }
    private bool _loading = true;

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_loading && !_services.SetStartWithWindows(value) && value) StartWithWindows = false; // could not register → show off
    }
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
        Load(s);
        Accents = [.. AccentPalette.All.Select(a => new AccentChoiceViewModel(a.Key, a.Hex, a.Key == Accent))];
        _appsLoadPending = true;
        _loading = false;
        Metrics = [.. AppSettings.AllMetrics.Select(m => new MetricToggleViewModel(m, s.Metrics.Contains(m), Save))];
        Loc.I.SetLanguage(s.Language);
        _ready = true;
    }

    private void Load(AppSettings s)
    {
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
        AutoBoost = s.AutoBoost;
        StartWithWindows = s.StartWithWindows;
        (SessionReport, StutterDetect, PacketLossAlert, PauseDownloads, Notifications, Hotkeys) = (s.SessionReport, s.StutterDetect, s.PacketLossAlert, s.PauseDownloads, s.Notifications, s.Hotkeys);
        (OverlayIcons, OverlayColor, OverlayOpacity, OverlayScale, FpsCap) = (s.OverlayIcons, s.OverlayColor, s.OverlayOpacity, s.OverlayScale, s.FpsCap);
        if (Metrics is not null) foreach (var m in Metrics) m.IsOn = s.Metrics.Contains(m.Key);
    }

    partial void OnLanguageChanged(string value) { Loc.I.SetLanguage(value); Save(); }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Language) or nameof(ShareSettingsCode) or nameof(ImportCode) or nameof(ShareSettingsStatus)) && !_loading) Save();
    }

    public AppSettings Current => _base with
    {
        AutoUndo = AutoUndo, Accent = Accent ?? "purple", TrayIcon = TrayIcon, AutoBoost = AutoBoost, StartWithWindows = StartWithWindows,
        SessionReport = SessionReport, StutterDetect = StutterDetect, PacketLossAlert = PacketLossAlert, PauseDownloads = PauseDownloads,
        Notifications = Notifications, Hotkeys = Hotkeys, OverlayIcons = OverlayIcons, OverlayColor = OverlayColor ?? "#FFFFFF",
        OverlayOpacity = Math.Clamp(OverlayOpacity, 0, 100), OverlayScale = Math.Clamp(OverlayScale, 60, 200), FpsCap = Math.Clamp(FpsCap, 0, 1000),
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
    public TestsViewModel Tests { get; }
    public StatsViewModel Stats { get; }
    public DoctorViewModel Doctor { get; }
    /// <summary>Shown under the sidebar so everyone can see which version they run.</summary>
    public string VersionLabel => "BoostRoot v" + AppVersion.Current;
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
        Home.ManualGamesChanged = change => Settings.Update(s => s with { ManualGames = change(s.ManualGames ?? []) });
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
        Tests = new TestsViewModel();
        Stats = new StatsViewModel(services);
        Doctor = new DoctorViewModel(services);
        Scan.OpenFpsTest = () => Navigate(Nav!.First(n => n.Page == Fps));
        Home.CurrentCrosshairCode = () => Crosshair.Design.ToValorantCode();
        Home.ApplySharedCrosshair = code => { if (BoostRoot.Core.Crosshair.CrosshairDesign.FromValorantCode(code) is { } d) Crosshair.Load(d); };
        Nav =
        [
            new("boost", "M13 2 4 14h7l-1 8 9-12h-7z", Home),
            new("scan", "M2 3h20v14H13v2h4v2H7v-2h4v-2H2zm2 2v10h16V5zm2 7h3v2H6zm5-3h3v5h-3zm5-2h2v7h-2z", Scan),
            new("fps", "M2 13h4l2.5-6 4 12 3.5-9 1.5 3H22v2h-5.8L15 12.5 11.6 21 7.6 9.5 7.3 10.3 7 15H2z", Fps),
            new("stats", "M11 2.05V13h10.95A10 10 0 1 1 11 2.05zM13 2.05A10 10 0 0 1 21.95 11H13z", Stats),
            new("doctor", "M10 2h4v6h6v4h-6v6h-4v-6H4V8h6z", Doctor),
            new("stress", "M13 2c.5 3-1.5 4.5-3 6.5-1.3 1.8-2 3.3-2 5.5a6 6 0 0 0 12 0c0-3-1.6-5.2-3-6.6 0 1.8-.8 3-2 3.6.4-3.5-.4-6.6-2-9zM12 22a3 3 0 0 1-3-3c0-1.6 1.2-2.6 2-3.5.3 1.2 1 1.8 1.8 2.2.4-.6.6-1.2.6-2 1 .9 1.6 2 1.6 3.3a3 3 0 0 1-3 3z", Stress),
            new("net", "M12 4c4 0 7.6 1.6 10.3 4.2l-1.5 1.5A12.5 12.5 0 0 0 12 6a12.5 12.5 0 0 0-8.8 3.7L1.7 8.2A14.5 14.5 0 0 1 12 4zm0 5c2.6 0 5 1 6.7 2.8l-1.5 1.5A7.4 7.4 0 0 0 12 11a7.4 7.4 0 0 0-5.2 2.3l-1.5-1.5A9.4 9.4 0 0 1 12 9zm0 5c1.3 0 2.5.5 3.4 1.4L12 19l-3.4-3.6c.9-.9 2.1-1.4 3.4-1.4z", Net),
            new("clips", "M4 6h12v12H4zM16 10l5-3v10l-5-3", Clips),
            new("updates", "M11 3h2v10l3.5-3.5 1.4 1.4L12 16.8 6.1 10.9l1.4-1.4L11 13zM4 19h16v2H4z", Updates),
            new("monitor", "M3 21V10h4v11zm7 0V3h4v18zm7 0v-7h4v7z", Monitor),
            new("tests", "M11 2.1V9H5.1A7 7 0 0 1 11 2.1zm2 0A7 7 0 0 1 18.9 9H13zM5 11h14v4a7 7 0 0 1-14 0z", Tests),
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
        if (item.Page == Stats) Stats.Refresh();
        if (item.Page == Scan) Scan.RefreshGains();
        if (item.Page == Doctor) Doctor.LoadGames(Home.Selected?.Entry.Profile?.Id);
    }
}
