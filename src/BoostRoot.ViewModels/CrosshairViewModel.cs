using System.Collections.ObjectModel;
using System.ComponentModel;
using BoostRoot.Core.Crosshair;
using BoostRoot.ViewModels.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BoostRoot.ViewModels;

public sealed record CrosshairTargetRow(CrosshairTarget Target, string How);

/// <summary>Crosshair Studio: design once, export to VALORANT / CS2 / any game (overlay).</summary>
public sealed partial class CrosshairViewModel : ObservableObject
{
    private readonly IAppServices _services;
    private readonly Action<CrosshairDesign> _useAsOverlay;
    private bool _loading;

    public IReadOnlyList<CrosshairDesign> Presets => CrosshairDesign.Presets;
    public ObservableCollection<CrosshairDesign> Saved { get; } = [];
    public IReadOnlyList<CrosshairTargetRow> Targets { get; }
    public IReadOnlyList<string> Swatches { get; } = ["#FFFFFF", "#00FF00", "#00FF66", "#7FFF00", "#FFFF00", "#00FFFF", "#FF00FF", "#FF5CF0", "#FF0000", "#FF8800"];

    [ObservableProperty] public partial string Name { get; set; } = "Benim nişangâhım";
    [ObservableProperty] public partial string Color { get; set; } = "#00FF66";
    [ObservableProperty] public partial bool Outline { get; set; } = true;
    [ObservableProperty] public partial int OutlineThickness { get; set; } = 1;
    [ObservableProperty] public partial double OutlineOpacity { get; set; } = 0.5;
    [ObservableProperty] public partial bool Dot { get; set; }
    [ObservableProperty] public partial int DotThickness { get; set; } = 2;
    [ObservableProperty] public partial bool InnerShow { get; set; } = true;
    [ObservableProperty] public partial int InnerThickness { get; set; } = 2;
    [ObservableProperty] public partial int InnerLength { get; set; } = 6;
    [ObservableProperty] public partial int InnerOffset { get; set; } = 3;
    [ObservableProperty] public partial double InnerOpacity { get; set; } = 1;
    [ObservableProperty] public partial bool OuterShow { get; set; }
    [ObservableProperty] public partial int OuterThickness { get; set; } = 2;
    [ObservableProperty] public partial int OuterLength { get; set; } = 2;
    [ObservableProperty] public partial int OuterOffset { get; set; } = 10;
    [ObservableProperty] public partial double OuterOpacity { get; set; } = 0.35;
    [ObservableProperty] public partial CrosshairDesign Design { get; set; } = new();
    [ObservableProperty] public partial string? ImportCode { get; set; }
    [ObservableProperty] public partial string? Status { get; set; }
    [ObservableProperty] public partial string? LastExport { get; set; }

    public CrosshairViewModel(IAppServices services, Action<CrosshairDesign> useAsOverlay)
    {
        _services = services;
        _useAsOverlay = useAsOverlay;
        var tr = Loc.I.Language == "tr";
        Targets = [.. CrosshairTargets.All.Select(t => new CrosshairTargetRow(t, tr ? t.HowTr : t.HowEn))];
        foreach (var d in services.LoadCrosshairs()) Saved.Add(d);
        Rebuild();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is not (nameof(Design) or nameof(ImportCode) or nameof(Status) or nameof(LastExport))) Rebuild();
    }

    private void Rebuild() => Design = new CrosshairDesign(Name, Color, Outline, OutlineThickness, OutlineOpacity, Dot, DotThickness, 1,
        new CrosshairLines(InnerShow, InnerThickness, InnerLength, InnerOffset, InnerOpacity),
        new CrosshairLines(OuterShow, OuterThickness, OuterLength, OuterOffset, OuterOpacity));

    [RelayCommand]
    public void Load(CrosshairDesign d)
    {
        _loading = true;
        (Name, Color, Outline, OutlineThickness, OutlineOpacity, Dot, DotThickness) = (d.Name, d.Color, d.Outline, d.OutlineThickness, d.OutlineOpacity, d.Dot, d.DotThickness);
        var i = d.InnerLines; (InnerShow, InnerThickness, InnerLength, InnerOffset, InnerOpacity) = (i.Show, i.Thickness, i.Length, i.Offset, i.Opacity);
        var o = d.OuterLines; (OuterShow, OuterThickness, OuterLength, OuterOffset, OuterOpacity) = (o.Show, o.Thickness, o.Length, o.Offset, o.Opacity);
        _loading = false;
        Rebuild();
    }

    [RelayCommand] public void Swatch(string hex) => Color = hex;

    [RelayCommand]
    public void Import()
    {
        if (ImportCode is { Length: > 0 } c && CrosshairDesign.FromValorantCode(c) is { } d) { Load(d); Status = Loc.I["ch.imported"]; }
        else Status = Loc.I["ch.badCode"];
    }

    [RelayCommand]
    public void Export(CrosshairTargetRow row)
    {
        if (row.Target.Export is { } f)
        {
            LastExport = f(Design);
            _services.CopyToClipboard(LastExport);
            Status = Loc.I.Format("ch.copied", row.Target.Game);
        }
        else
        {
            _useAsOverlay(Design);
            Status = Loc.I["ch.overlaySet"];
        }
    }

    [RelayCommand]
    public void Save()
    {
        var existing = Saved.FirstOrDefault(x => x.Name == Design.Name);
        if (existing is not null) Saved[Saved.IndexOf(existing)] = Design; else Saved.Add(Design);
        _services.SaveCrosshairs([.. Saved]);
        Status = Loc.I["ch.saved"];
    }

    [RelayCommand]
    public void Delete(CrosshairDesign d) { Saved.Remove(d); _services.SaveCrosshairs([.. Saved]); }
}
