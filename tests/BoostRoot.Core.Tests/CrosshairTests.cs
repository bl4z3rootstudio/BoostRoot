using BoostRoot.Core.Crosshair;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class CrosshairTests
{
    [Fact]
    public void Valorant_DefaultLikeDesign_ProducesShortCode()
    {
        var d = new CrosshairDesign(Color: "#00FFFF", Inner: new(true, 2, 6, 3, 0.8), Outer: new(false));
        Assert.Equal("0;P;c;5;1b;0", d.ToValorantCode());
    }

    [Fact]
    public void Valorant_CustomColorAndDot_RoundTrip()
    {
        var d = new CrosshairDesign(Color: "#FF5CF0", Outline: false, Dot: true, DotThickness: 3, Inner: new(true, 1, 4, 2, 1), Outer: new(false));
        var code = d.ToValorantCode();
        Assert.Contains("c;8;u;FF5CF0FF", code);
        Assert.Contains("h;0", code);
        Assert.Contains("d;1;z;3", code);
        var back = CrosshairDesign.FromValorantCode(code)!;
        Assert.Equal("#FF5CF0", back.Color);
        Assert.False(back.Outline);
        Assert.True(back.Dot);
        Assert.Equal(new CrosshairLines(true, 1, 4, 2, 1), back.InnerLines);
        Assert.False(back.OuterLines.Show);
        Assert.Null(CrosshairDesign.FromValorantCode("hello"));
    }

    [Fact]
    public void Valorant_ParsesRealWorldCode_WithOtherSections()
    {
        var d = CrosshairDesign.FromValorantCode("0;s;1;P;c;1;h;0;0l;4;0o;2;0a;1;0f;0;1b;0;A;c;1;S;c;1")!;
        Assert.Equal("#00FF00", d.Color);
        Assert.Equal(4, d.InnerLines.Length);
        Assert.Equal(2, d.InnerLines.Offset);
        Assert.False(d.OuterLines.Show);
    }

    [Fact]
    public void Source_Commands_ContainColorAndSize()
    {
        var cmd = new CrosshairDesign(Color: "#FF0000", Dot: true, Inner: new(true, 2, 6, 3, 1)).ToSourceCommands();
        Assert.Contains("cl_crosshaircolor_r 255", cmd);
        Assert.Contains("cl_crosshairsize 3", cmd);
        Assert.Contains("cl_crosshairdot 1", cmd);
        Assert.Contains("cl_crosshairgap -1", cmd);
    }

    [Fact]
    public void Studio_EditsExportsSavesAndSetsOverlay()
    {
        var svc = ViewModelTests.Services();
        var main = new MainViewModel(svc);
        var vm = main.Crosshair;
        vm.Swatch("#FF0000");
        Assert.Equal("#FF0000", vm.Design.Color);
        vm.Export(vm.Targets.First(t => t.Target.Id == "valorant"));
        Assert.StartsWith("0;P;c;7", svc.Clipboard);
        vm.Save();
        Assert.Single(svc.Crosshairs);
        vm.Export(vm.Targets.First(t => t.Target.Id == "overlay"));
        Assert.True(svc.Saved.CrosshairOrDefault.Enabled);
        Assert.Equal("#FF0000", svc.Saved.CrosshairOrDefault.Color);
        vm.ImportCode = "0;P;c;5;h;0";
        vm.Import();
        Assert.Equal("#00FFFF", vm.Color);
    }
}

public class LibraryTests
{
    private static string LibraryText => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "profiles", "library.txt"));

    [Fact]
    public void Library_ParsesHundredsOfValidProfiles()
    {
        var json = new[] { RealProfileTests.Valorant };
        var lib = BoostRoot.Core.Profiles.GameLibrary.Parse(LibraryText, json);
        Assert.True(lib.Count >= 450, $"only {lib.Count}");
        Assert.Equal(lib.Count, lib.Select(p => p.Id).Distinct().Count());
        foreach (var p in lib) Assert.Empty(BoostRoot.Core.Profiles.ProfileValidator.Validate(p));
        Assert.Contains(lib, p => p.Name == "Overwatch 2" && p.Competitive && p.Detect["process"] == "Overwatch.exe");
        Assert.DoesNotContain(lib, p => p.Name == "VALORANT");
    }

    [Fact]
    public void Names_MatchAcrossStores()
    {
        Assert.Equal(BoostRoot.Core.Profiles.GameLibrary.Normalize("Tom Clancy's Rainbow Six® Siege"), BoostRoot.Core.Profiles.GameLibrary.Normalize("Tom Clancys Rainbow Six Siege"));
        var p = BoostRoot.Core.Profiles.GameLibrary.Parse("Baldur's Gate 3;rpg;bg3.exe", [])[0];
        Assert.True(BoostRoot.Games.GameCatalog.Matches(p, new BoostRoot.Games.InstalledGame("steam", "1086940", "Baldur’s Gate 3", "C:/g", new Dictionary<string, string>())) ||
                    BoostRoot.Games.GameCatalog.Matches(p, new BoostRoot.Games.InstalledGame("steam", "1086940", "Baldurs Gate 3", "C:/g", new Dictionary<string, string>())));
    }

    [Fact]
    public void Catalog_GivesUnknownInstalledGamesAGenericProfile()
    {
        var games = new[]
        {
            new BoostRoot.Games.InstalledGame("steam", "999", "Some New Indie", "C:/Steam/steamapps/common/SNI", new Dictionary<string, string>()),
            new BoostRoot.Games.InstalledGame("steam", "228980", "Steamworks Common Redistributables", "C:/x", new Dictionary<string, string>()),
        };
        var cat = BoostRoot.Games.GameCatalog.Match([RealProfileTests.Valorant], games);
        var auto = Assert.Single(cat, e => e.Profile?.Id.StartsWith("auto-", StringComparison.Ordinal) == true);
        Assert.Equal("dir:C:/Steam/steamapps/common/SNI", auto.Profile!.Detect["process"]);
        Assert.Empty(BoostRoot.Core.Profiles.ProfileValidator.Validate(auto.Profile));
    }
}
