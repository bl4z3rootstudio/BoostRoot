using System.Collections.ObjectModel;
using BoostRoot.Core.Boost;
using BoostRoot.Core.Profiles;
using BoostRoot.Games;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed partial class GameCardViewModel(CatalogEntry entry, string? cover) : ObservableObject
{
    public CatalogEntry Entry { get; } = entry;
    public string Name => Entry.DisplayName;
    public bool IsInstalled => Entry.IsInstalled;
    public string? Cover { get; } = cover;
    /// <summary>Stable tint per game for the cover placeholder.</summary>
    public string Tint { get; } = Palette[Math.Abs(StableHash(entry.DisplayName)) % Palette.Length];
    [ObservableProperty] public partial bool IsSelected { get; set; }

    private static readonly string[] Palette = ["#7A1F2B", "#3A3420", "#2E4A22", "#4A3A18", "#2A2F45", "#1F3440", "#23361F", "#3A2A44", "#4A2418", "#1F3A4A"];
    private static int StableHash(string s) { unchecked { var h = 17; foreach (var c in s) h = h * 31 + c; return h; } }
}

public sealed partial class LevelOptionViewModel(BoostLevel level) : ObservableObject
{
    public BoostLevel Level { get; } = level;
    public string Key { get; } = level switch { BoostLevel.Quality => "quality", BoostLevel.Balanced => "balanced", BoostLevel.MaxFps => "maxfps", _ => "competitive" };
    public string Name => Loc.I["level." + Key];
    public string Description => Loc.I["level." + Key + ".desc"];
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsRecommended { get; set; }
}

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IAppServices _services;
    private AppSettings _settings;

    public ObservableCollection<GameCardViewModel> Games { get; } = [];
    public ObservableCollection<LevelOptionViewModel> Levels { get; } = [.. Enum.GetValues<BoostLevel>().Select(l => new LevelOptionViewModel(l))];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection), nameof(RecommendedText), nameof(CanStretch))] [NotifyCanExecuteChangedFor(nameof(BoostCommand), nameof(UndoCommand))]
    public partial GameCardViewModel? Selected { get; set; }

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(BoostCommand))] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool StatusIsError { get; set; }
    [ObservableProperty] public partial bool AskLaunch { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(UndoCommand))] public partial string? LastOperation { get; set; }
    [ObservableProperty] public partial BoostResult? LastResult { get; set; }

    public bool HasSelection => Selected is not null;
    public bool CanStretch => Selected?.Entry.Profile?.StretchKeys is { Count: > 0 };
    [ObservableProperty] public partial bool IsStretched { get; set; }
    private bool _loadingStretch;
    /// <summary>(profileId, enabled) — the shell stores it in settings.</summary>
    public Action<string, bool>? StretchChanged { get; set; }
    partial void OnIsStretchedChanged(bool value) { if (!_loadingStretch && Selected?.Entry.Profile is { } p) StretchChanged?.Invoke(p.Id, value); }
    public string GamesTitle => Loc.I.Format("home.games", Games.Count(g => g.IsInstalled));
    public string RecommendedText => Selected?.Entry.Profile is { } p ? Loc.I.Format("home.recommended", Loc.I["level." + Levels.First(l => l.Level == Recommended(p)).Key]) : "";

    public HomeViewModel(IAppServices services)
    {
        _services = services;
        _settings = services.LoadSettings();
        Reload();
    }

    public BoostLevel SelectedLevel => Levels.FirstOrDefault(l => l.IsSelected)?.Level ?? BoostLevel.Balanced;

    private BoostLevel Recommended(GameProfile p) => Recommender.Recommend(p, _services.PcScore);

    public void Reload()
    {
        Games.Clear();
        foreach (var e in _services.LoadCatalog()) Games.Add(new GameCardViewModel(e, _services.CoverPath(e)));
        OnPropertyChanged(nameof(GamesTitle));
        Select(Games.FirstOrDefault(g => g.IsInstalled));
    }

    [RelayCommand]
    public void Select(GameCardViewModel? game)
    {
        if (game is not null && !game.IsInstalled) return;
        foreach (var g in Games) g.IsSelected = g == game;
        Selected = game;
        Status = null;
        AskLaunch = false;
        if (game?.Entry.Profile is { } p)
        {
            var rec = Recommended(p);
            foreach (var l in Levels) { l.IsRecommended = l.Level == rec; l.IsSelected = l.Level == rec; }
            LastOperation = _services.LastBoostOperation(p.Id);
            _loadingStretch = true;
            IsStretched = _settings.IsStretched(p.Id);
            _loadingStretch = false;
        }
    }

    [RelayCommand]
    public void ChooseLevel(LevelOptionViewModel level)
    {
        foreach (var l in Levels) l.IsSelected = l == level;
    }

    private bool CanBoost() => Selected?.Entry.Profile is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBoost))]
    public async Task BoostAsync()
    {
        var entry = Selected!.Entry;
        IsBusy = true;
        Status = Loc.I["boost.running"];
        StatusIsError = false;
        AskLaunch = false;
        try
        {
            var level = SelectedLevel;
            var result = await Task.Run(() => _services.Boost(entry, level, _settings));
            LastResult = result;
            if (result.Success)
            {
                LastOperation = result.OperationId;
                var notFound = result.GameSettings?.Targets.Count > 0 && result.GameSettings.Targets.All(t => t.Status == TargetStatus.NotFound);
                Status = notFound ? Loc.I["boost.notFound"] : Loc.I["boost.done"] + " · " + Loc.I.Format("boost.changes", result.Steps.Sum(s => s.Changes));
                StatusIsError = notFound;
                AskLaunch = !notFound;
            }
            else
            {
                StatusIsError = true;
                Status = result.Error switch
                {
                    BoostOrchestrator.GameRunningError => Loc.I["boost.gameRunning"],
                    _ => Loc.I["boost.settingsFailed"],
                };
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public void Launch()
    {
        AskLaunch = false;
        if (Selected is not null) _services.Launch(Selected.Entry);
    }

    [RelayCommand]
    public void DismissLaunch() => AskLaunch = false;

    private bool CanUndo() => LastOperation is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        _services.Undo(LastOperation!);
        LastOperation = Selected?.Entry.Profile is { } p ? _services.LastBoostOperation(p.Id) : null;
        Status = Loc.I["boost.undone"];
        StatusIsError = false;
    }

    public void ApplySettings(AppSettings settings) => _settings = settings;

    /// <summary>Re-evaluates the recommended level after the PC score changes.</summary>
    public void RefreshRecommendation()
    {
        if (Selected?.Entry.Profile is not { } p) return;
        var rec = Recommended(p);
        foreach (var l in Levels) l.IsRecommended = l.Level == rec;
        OnPropertyChanged(nameof(RecommendedText));
    }
}
