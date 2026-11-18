using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.ViewModels;

public sealed record PoligonDrillRow(string Name, string Best, string Last, string Runs, string Reaction, string Accuracy);

/// <summary>Launcher + history for the Poligon aim trainer (a separate Godot app, downloaded on first use).</summary>
public sealed partial class PoligonViewModel(IAppServices services) : ObservableObject
{
    public IReadOnlyList<string> Games { get; } = ["VALORANT"];
    [ObservableProperty] public partial int GameIndex { get; set; }
    [ObservableProperty] public partial string Sens { get; set; } = "";
    [ObservableProperty] public partial string Dpi { get; set; } = "800";
    [ObservableProperty] public partial string CrosshairCode { get; set; } = "";
    [ObservableProperty] public partial bool Stretched { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string? Installed { get; set; }
    [ObservableProperty] public partial string? Cm360 { get; set; }
    [ObservableProperty] public partial string? Detected { get; set; }
    public ObservableCollection<PoligonDrillRow> Drills { get; } = [];
    [ObservableProperty] public partial bool HasRuns { get; set; }

    private static string GameId(int i) => i switch { _ => "valorant" };

    partial void OnSensChanged(string value) => UpdateCm();
    partial void OnDpiChanged(string value) => UpdateCm();

    private bool TryNum(string s, out double v) =>
        double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0;

    private void UpdateCm()
    {
        Cm360 = TryNum(Sens, out var s) && int.TryParse(Dpi, out var dpi) && dpi > 0
            ? Loc.I.Format("pol.cm", BoostRoot.Core.Input.Sensitivity.CmPer360(s, dpi, GameId(GameIndex)).ToString("0.0", CultureInfo.InvariantCulture), (s * dpi).ToString("0", CultureInfo.InvariantCulture))
            : null;
    }

    private bool _detected;

    public void Refresh()
    {
        if (!_detected)
        {
            _detected = true;
            var (sens, stretched, code) = services.ValorantSettings();
            if (code is not null && string.IsNullOrWhiteSpace(CrosshairCode)) CrosshairCode = code;
            if (sens is { } s && string.IsNullOrWhiteSpace(Sens)) { Sens = s.ToString("0.###", CultureInfo.InvariantCulture); Detected = Loc.I["pol.detected"]; }
            if (stretched is { } st) Stretched = st;
        }
        Installed = services.PoligonVersion();
        Drills.Clear();
        foreach (var d in BoostRoot.Core.Poligon.PoligonRuns.Summary(services.PoligonRuns(), GameId(GameIndex)))
        {
            var name = BoostRoot.Core.Poligon.PoligonRuns.Names.TryGetValue(d.Drill, out var n) ? Loc.I.Text(n.Tr, n.En) : d.Drill;
            Drills.Add(new(name, d.Best.ToString("0"), d.Last.ToString("0"), d.Runs.ToString(CultureInfo.InvariantCulture),
                d.AvgReact > 0 ? $"{d.AvgReact:0} ms" : "—", $"%{d.AvgAcc * 100:0}"));
        }
        HasRuns = Drills.Count > 0;
        UpdateCm();
    }

    [RelayCommand]
    public async Task PlayAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var latest = await services.PoligonLatestAsync(CancellationToken.None);
            var installed = services.PoligonVersion();
            if (installed is null || (latest is not null && latest != installed))
            {
                Status = Loc.I["pol.downloading"];
                Progress = 0;
                var ok = await services.InstallPoligonAsync(new Progress<double>(p => Progress = p * 100), CancellationToken.None);
                if (!ok && services.PoligonVersion() is null) { Status = Loc.I["pol.downloadFail"]; return; }
            }
            Installed = services.PoligonVersion();
            var sens = TryNum(Sens, out var s) ? s : 0;
            Status = services.LaunchPoligon(GameId(GameIndex), sens, int.TryParse(Dpi, out var dpi) ? dpi : 0, CrosshairCode, Stretched)
                ? Loc.I["pol.started"] : Loc.I["pol.launchFail"];
        }
        finally { Busy = false; }
    }
}
