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

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppServices _services;
    private readonly Action<AppSettings> _onChanged;

    public IReadOnlyList<string> Languages => Loc.Languages;
    public string VersionText => Loc.I.Format("settings.version", AppVersion.Current);

    [ObservableProperty] public partial string Language { get; set; }
    [ObservableProperty] public partial bool Sounds { get; set; }
    [ObservableProperty] public partial bool Animations { get; set; }
    [ObservableProperty] public partial bool Offline { get; set; }
    [ObservableProperty] public partial string CloseList { get; set; }

    public SettingsViewModel(IAppServices services, Action<AppSettings> onChanged)
    {
        _services = services;
        _onChanged = onChanged;
        var s = services.LoadSettings();
        Language = s.Language;
        Sounds = s.Sounds;
        Animations = s.Animations;
        Offline = s.Offline;
        CloseList = string.Join(", ", s.CloseList);
        Loc.I.SetLanguage(s.Language);
    }

    partial void OnLanguageChanged(string value) { Loc.I.SetLanguage(value); Save(); }
    partial void OnSoundsChanged(bool value) => Save();
    partial void OnAnimationsChanged(bool value) => Save();
    partial void OnOfflineChanged(bool value) => Save();
    partial void OnCloseListChanged(string value) => Save();

    public AppSettings Current => new(Language ?? "tr", Sounds, Animations, Offline,
        [.. (CloseList ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], false);

    private void Save()
    {
        if (CloseList is null) return; // still constructing
        var s = Current;
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

    [ObservableProperty] public partial ObservableObject CurrentPage { get; set; }

    public MainViewModel(IAppServices services)
    {
        Home = new HomeViewModel(services);
        Settings = new SettingsViewModel(services, s => Home.ApplySettings(s));
        Scan = new ScanViewModel(services, r => { Home.RefreshRecommendation(); Fps!.Cpu = r.Info.CpuName; Fps.Gpu = r.Info.Gpus.FirstOrDefault()?.Name ?? ""; });
        Net = new NetViewModel(services);
        Tools = new ToolsViewModel(services);
        Stress = new StressViewModel(services);
        Fps = new FpsViewModel(services);
        Nav =
        [
            new("boost", "M13 2 4 14h7l-1 8 9-12h-7z", Home),
            new("scan", "M3 3h18v14H3zM8 21h8M12 17v4", Scan),
            new("fps", "M3 12h4l3 8 4-16 3 8h4", Fps),
            new("stress", "M12 2a7 7 0 0 0-4 12.7V18h8v-3.3A7 7 0 0 0 12 2zM9 22h6", Stress),
            new("net", "M2 9a15 15 0 0 1 20 0M5 13a10 10 0 0 1 14 0M8.5 16.5a5 5 0 0 1 7 0M12 20h.01", Net),
            new("clips", "M4 6h12v12H4zM16 10l5-3v10l-5-3", new PlaceholderViewModel("clips")),
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
    }
}
