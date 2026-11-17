using System.Collections.ObjectModel;
using BoostRoot.Core.Tools;
using BoostRoot.Games;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed record DoctorGameChoice(CatalogEntry Entry)
{
    public string Name => Entry.DisplayName;
    public override string ToString() => Name;
}

public sealed partial class DoctorFindingViewModel(DoctorFinding f) : ObservableObject
{
    public DoctorFinding Finding { get; } = f;
    public string Title => Loc.I.Pick(Finding.Title);
    public string Detail => Loc.I.Pick(Finding.Detail);
    public string? Manual => Finding.Manual is { } m ? Loc.I.Pick(m) : null;
    public bool HasManual => Finding.Manual is not null;
    public bool IsProblem => Finding.Severity is DoctorSeverity.Problem or DoctorSeverity.Warning;
    public bool IsOk => Finding.Severity == DoctorSeverity.Ok;
    public bool IsInfo => Finding.Severity == DoctorSeverity.Info;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowFix))] public partial bool Fixed { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowFix))] public partial bool Busy { get; set; }
    [ObservableProperty] public partial string? Result { get; set; }
    public bool ShowFix => Finding.CanAutoFix && !Fixed && !Busy;
}

/// <summary>"Oyun Doktoru": pick a game, find what stops it from starting or running well, fix what can be fixed, explain the rest.</summary>
public sealed partial class DoctorViewModel(IAppServices services) : ObservableObject
{
    public ObservableCollection<DoctorGameChoice> Games { get; } = [];
    public ObservableCollection<DoctorFindingViewModel> Findings { get; } = [];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ScanCommand))] public partial DoctorGameChoice? Selected { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(FixAllCommand))] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? Summary { get; set; }
    [ObservableProperty] public partial bool HasFixable { get; set; }

    public void LoadGames(string? preferId = null)
    {
        var keep = Selected?.Entry.Profile?.Id ?? preferId;
        Games.Clear();
        try { foreach (var e in services.LoadCatalog().Where(e => e.IsInstalled).OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)) Games.Add(new(e)); }
        catch (Exception) { }
        Selected = Games.FirstOrDefault(g => g.Entry.Profile?.Id == keep) ?? Games.FirstOrDefault();
    }

    private bool CanScan => Selected is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanAsync()
    {
        if (Selected is not { } g) return;
        IsBusy = true;
        Summary = Loc.I["doc.scanning"];
        Findings.Clear();
        try
        {
            foreach (var f in await services.DiagnoseGameAsync(g.Entry)) Findings.Add(new(f));
            var problems = Findings.Count(f => f.IsProblem);
            Summary = problems == 0 ? Loc.I["doc.clean"] : Loc.I.Format("doc.found", problems);
        }
        catch (Exception) { Summary = Loc.I["doc.failed"]; }
        finally { IsBusy = false; HasFixable = Findings.Any(f => f.ShowFix && f.IsProblem); }
    }

    [RelayCommand]
    public async Task FixAsync(DoctorFindingViewModel row)
    {
        if (Selected is not { } g || row.Busy) return;
        row.Busy = true;
        try
        {
            var ok = await services.FixGameAsync(g.Entry, row.Finding.Id);
            row.Fixed = ok;
            row.Result = ok ? Loc.I["doc.fixed"] : Loc.I["doc.notFixed"];
        }
        catch (Exception) { row.Result = Loc.I["doc.notFixed"]; }
        finally { row.Busy = false; HasFixable = Findings.Any(f => f.ShowFix && f.IsProblem); }
    }

    private bool CanFixAll => !IsBusy;

    /// <summary>Fixes every real problem that has a one-click fix (not the optional "info" actions like file verification).</summary>
    [RelayCommand(CanExecute = nameof(CanFixAll))]
    public async Task FixAllAsync()
    {
        IsBusy = true;
        try { foreach (var row in Findings.Where(f => f.ShowFix && f.IsProblem).ToList()) await FixAsync(row); }
        finally { IsBusy = false; }
    }
}
