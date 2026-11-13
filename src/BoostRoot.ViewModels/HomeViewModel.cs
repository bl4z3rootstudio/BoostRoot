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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCover))] public partial string? Cover { get; set; } = cover;
    public bool HasCover => Cover is not null;
    /// <summary>Generated cover text when no art exists: "Counter-Strike 2" → "CS2", "Elden Ring" → "ER".</summary>
    public string Initials { get; } = MakeInitials(entry.DisplayName);
    public string Genre => Entry.Profile?.Competitive == true ? "⚔" : "🎮";

    private static string MakeInitials(string name)
    {
        var words = name.Split([' ', '-', ':', '.'], StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToArray();
        if (words.Length == 0) return "?";
        if (words.Length == 1) return words[0].Length <= 4 ? words[0].ToUpperInvariant() : words[0][..2].ToUpperInvariant();
        return string.Concat(words.Take(3).Select(w => char.IsDigit(w[0]) ? w : char.ToUpperInvariant(w[0]).ToString()));
    }
    /// <summary>Stable tint per game for the cover placeholder.</summary>
    public string Tint { get; } = Palette[Math.Abs(StableHash(entry.DisplayName)) % Palette.Length];
    [ObservableProperty] public partial bool IsSelected { get; set; }
    [ObservableProperty] public partial bool IsFavorite { get; set; }

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

public sealed record ChangeRow(string Text, string Impact, bool High);

/// <summary>One line of the boost result: what actually happened in each step.</summary>
public sealed record StepRow(string Icon, string Text, bool Ok);

public sealed partial class HomeViewModel : ObservableObject
{
    public ObservableCollection<ChangeRow> Changes { get; } = [];
    public ObservableCollection<StepRow> ResultSteps { get; } = [];

    /// <summary>Turns step results into plain sentences, so "0 settings changed" is never the whole story.</summary>
    public static IEnumerable<StepRow> Describe(BoostRoot.Core.Boost.BoostResult r, BoostRoot.Core.Profiles.GameProfile p)
    {
        foreach (var st in r.Steps)
        {
            var ok = st.Status != BoostRoot.Core.Boost.StepStatus.Failed;
            yield return st.StepId switch
            {
                "game-settings" when p.Group == "B" || p.Configs.Count == 0 => new StepRow("🎮", Loc.I["step.gameGuide"], true),
                "game-settings" when !ok => new StepRow("⚠", Loc.I["step.gameFailed"], false),
                "game-settings" when st.Changes == 0 => new StepRow("✓", Loc.I["step.gameAlready"], true),
                "game-settings" => new StepRow("✓", Loc.I.Format("step.game", st.Changes), true),
                "close-apps" when st.Changes > 0 => new StepRow("✓", Loc.I.Format("step.closed", st.Changes), true),
                "close-apps" => new StepRow("✓", Loc.I["step.closedNone"], true),
                "windows" when st.Changes > 0 => new StepRow("✓", Loc.I.Format("step.windows", st.Changes), true),
                "windows" => new StepRow("✓", Loc.I["step.windowsAlready"], true),
                "gpu-profile" when ok && st.Status == BoostRoot.Core.Boost.StepStatus.Done => new StepRow("✓", Loc.I.Format("step.gpu", st.Changes), true),
                "gpu-profile" => new StepRow("ℹ", Loc.I["step.gpuSkipped"], true),
                "stretch" => new StepRow(ok ? "✓" : "⚠", ok ? Loc.I.Format("step.stretch", st.Detail ?? "") : Loc.I["step.stretchFailed"], ok),
                _ when !ok => new StepRow("⚠", st.StepId + ": " + st.Detail, false),
                _ when st.Changes > 0 => new StepRow("✓", Loc.I.Format("step.other", st.Changes), true),
                _ => new StepRow("✓", Loc.I["step.otherAlready"], true),
            };
        }
    }
    [ObservableProperty] public partial string? Tips { get; set; }
    [ObservableProperty] public partial string? PlaytimeText { get; set; }
    [ObservableProperty] public partial bool NoFileChanges { get; set; }

    /// <summary>"What will change?" list for the selected game + level (from the profile's notes).</summary>
    private void RefreshChanges()
    {
        Changes.Clear();
        Tips = null;
        if (Selected?.Entry.Profile is not { } p) return;
        var tr = Loc.I.Language == "tr";
        Tips = p.Tips is { } t ? (tr ? t.Tr : t.En) : null;
        var total = _services.Playtime.Total(p.Id);
        PlaytimeText = total > TimeSpan.Zero ? Loc.I.Format("home.playtime", BoostRoot.Core.Fps.PlaytimeLog.Format(total)) : null;
        foreach (var key in p.Levels[SelectedLevel].Set.Keys)
        {
            if (!p.Notes.TryGetValue(key, out var n)) continue;
            Changes.Add(new ChangeRow(tr ? n.Text.Tr : n.Text.En, Loc.I["impact." + n.FpsImpact], n.FpsImpact == "high"));
        }
        NoFileChanges = p.Levels[SelectedLevel].Set.Count == 0;
        Changes.Add(new ChangeRow(Loc.I["change.windows"], Loc.I["impact.medium"], false));
        if (p.Levels[SelectedLevel].Gpu is not null) Changes.Add(new ChangeRow(Loc.I["change.gpu"], Loc.I["impact.medium"], false));
    }
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
    public string GamesTitle => Loc.I.Format("home.games", _all.Count(g => g.IsInstalled));
    public string LibraryText => Loc.I.Format("home.library", _all.Count);
    private List<GameCardViewModel> _all = [];
    [ObservableProperty] public partial string? SearchText { get; set; }
    [ObservableProperty] public partial bool ShowAll { get; set; }
    partial void OnSearchTextChanged(string? value) => ApplyFilter();
    partial void OnShowAllChanged(bool value) => ApplyFilter();

    /// <summary>Installed games + hand-tuned profiles by default; the full library (hundreds) via search or "show all".</summary>
    private void ApplyFilter()
    {
        var q = BoostRoot.Core.Profiles.GameLibrary.Normalize(SearchText ?? "");
        Games.Clear();
        foreach (var g in _all) g.IsFavorite = g.Entry.Profile is { } pf && _settings.IsFavorite(pf.Id);
        // Favorites first, then installed, then name.
        foreach (var g in _all.Where(g => Visible(g, q)).OrderByDescending(g => g.IsFavorite).ThenByDescending(g => g.IsInstalled).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase))
            Games.Add(g);
        OnPropertyChanged(nameof(GamesTitle));
        OnPropertyChanged(nameof(LibraryText));
        _ = LoadCoversAsync([.. Games.Where(g => g.Cover is null)]);
    }

    private bool Visible(GameCardViewModel g, string q)
    {
        var id = g.Entry.Profile?.Id ?? "";
        return q.Length > 0 ? BoostRoot.Core.Profiles.GameLibrary.Normalize(g.Name).Contains(q, StringComparison.Ordinal)
            : ShowAll || g.IsInstalled || !(id.StartsWith("lib-", StringComparison.Ordinal) || id.StartsWith("auto-", StringComparison.Ordinal));
    }

    /// <summary>(profileId, favorite) — the shell stores it in settings.</summary>
    public Action<string, bool>? FavoriteChanged { get; set; }

    [RelayCommand]
    public void ToggleFavorite(GameCardViewModel g)
    {
        if (g.Entry.Profile is not { } p) return;
        g.IsFavorite = !g.IsFavorite;
        FavoriteChanged?.Invoke(p.Id, g.IsFavorite);
    }

    [ObservableProperty] public partial string? ShareInput { get; set; }
    [ObservableProperty] public partial string? ShareStatus { get; set; }
    public Action<string>? CopyText { get; set; }
    public Action<string>? ApplySharedCrosshair { get; set; }
    public Func<string?>? CurrentCrosshairCode { get; set; }

    [RelayCommand]
    public void Share()
    {
        if (Selected?.Entry.Profile is not { } p) return;
        var code = BoostRoot.Core.Profiles.ShareCode.Encode(new(p.Id, SelectedLevel, IsStretched, CurrentCrosshairCode?.Invoke()));
        CopyText?.Invoke(code);
        ShareStatus = Loc.I["share.copied"];
    }

    [RelayCommand]
    public void ImportShare()
    {
        if (BoostRoot.Core.Profiles.ShareCode.Decode(ShareInput ?? "") is not { } s) { ShareStatus = Loc.I["share.bad"]; return; }
        var card = _all.FirstOrDefault(g => g.Entry.Profile?.Id == s.Game);
        if (card is null || !card.IsInstalled) { ShareStatus = Loc.I["share.notInstalled"]; return; }
        Select(card);
        ChooseLevel(Levels.First(l => l.Level == s.Level));
        if (CanStretch) IsStretched = s.Stretched;
        if (s.Crosshair is { } c) ApplySharedCrosshair?.Invoke(c);
        ShareStatus = Loc.I["share.applied"];
        ShareInput = null;
    }

    private CancellationTokenSource? _coverCts;

    private async Task LoadCoversAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        _coverCts?.Cancel();
        var cts = _coverCts = new CancellationTokenSource();
        var ui = SynchronizationContext.Current;
        await Parallel.ForEachAsync(cards.Take(120), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token }, async (card, ct) =>
        {
            string? path;
            try { path = await _services.FetchCoverAsync(card.Entry, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            if (path is null) return;
            if (ui is null) card.Cover = path; else ui.Post(_ => card.Cover = path, null);
        }).ContinueWith(_ => { }, TaskScheduler.Default);
    }
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
        _all = [.. _services.LoadCatalog().Select(e => new GameCardViewModel(e, _services.CoverPath(e)))];
        ApplyFilter();
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
        ResultSteps.Clear();
        if (game?.Entry.Profile is { } p)
        {
            var rec = Recommended(p);
            foreach (var l in Levels) { l.IsRecommended = l.Level == rec; l.IsSelected = l.Level == rec; }
            LastOperation = _services.LastBoostOperation(p.Id);
            RefreshChanges();
            _loadingStretch = true;
            IsStretched = _settings.IsStretched(p.Id);
            _loadingStretch = false;
        }
    }

    [RelayCommand]
    public void ChooseLevel(LevelOptionViewModel level)
    {
        foreach (var l in Levels) l.IsSelected = l == level;
        RefreshChanges();
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
            ResultSteps.Clear();
            if (entry.Profile is { } prof) foreach (var row in Describe(result, prof)) ResultSteps.Add(row);
            if (result.Success)
            {
                LastOperation = result.OperationId;
                var notFound = result.GameSettings?.Targets.Count > 0 && result.GameSettings.Targets.All(t => t.Status == TargetStatus.NotFound);
                Status = notFound ? Loc.I["boost.notFound"] : Loc.I["boost.done"] + " ✓";
                StatusIsError = notFound;
                AskLaunch = !notFound;
                if (!notFound && _settings.Sounds) _services.PlayBoostSound();
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
