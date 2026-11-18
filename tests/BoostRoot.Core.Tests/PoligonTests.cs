using BoostRoot.Core.Poligon;
using Xunit;

namespace BoostRoot.Core.Tests;

public class PoligonTests
{
    private const string Json = """
    [{"drill":"duel","game":"valorant","sens":0.4,"score":820,"kills":7,"shots":9,"hits":8,"heads":7,"acc":0.889,"hs":0.875,"avg_react":412,"died":true,"ts":1763440000},
     {"drill":"duel","game":"valorant","sens":0.4,"score":1020,"kills":9,"shots":10,"acc":0.9,"hs":0.9,"avg_react":388,"died":true,"ts":1763441000},
     {"drill":"flick","game":"valorant","score":3100,"kills":31,"shots":40,"acc":0.775,"hs":1,"avg_react":610,"ts":1763442000},
     {"drill":"free","game":"valorant","score":100,"ts":1763443000},
     {"bad":true}, 5, {"drill":7}]
    """;

    [Fact]
    public void Parses_runs_and_skips_garbage()
    {
        var runs = PoligonRuns.Parse(Json);
        Assert.Equal(4, runs.Count);
        Assert.True(runs[0].Died);
        Assert.Equal(412, runs[0].AvgReactionMs);
        Assert.Equal(0.4, runs[0].Sens);
    }

    [Fact]
    public void Summary_per_drill_without_free_range()
    {
        var s = PoligonRuns.Summary(PoligonRuns.Parse(Json), "valorant");
        Assert.Equal(["duel", "flick"], s.Select(x => x.Drill));
        var duel = s[0];
        Assert.Equal(1020, duel.Best);
        Assert.Equal(1020, duel.Last);
        Assert.Equal(2, duel.Runs);
        Assert.Equal(400, duel.AvgReact);
    }

    [Fact]
    public void Broken_file_gives_empty_list()
    {
        Assert.Empty(PoligonRuns.Parse("{not json"));
        Assert.Empty(PoligonRuns.Parse("{}"));
    }
}

public class ValorantConfigTests
{
    [Fact]
    public void Reads_sensitivity()
    {
        Assert.Equal(0.312, ValorantConfig.Sensitivity("[Settings]\r\nEAresFloatSettingName::MouseSensitivity=0.312000\r\nEAresFloatSettingName::MouseSensitivityADS=1.0\r\n"));
        Assert.Null(ValorantConfig.Sensitivity("[Settings]\nfoo=1"));
    }

    [Fact]
    public void Detects_stretched()
    {
        Assert.True(ValorantConfig.Stretched("ResolutionSizeX=1440\nResolutionSizeY=1080\nbShouldLetterbox=False\n"));
        Assert.Null(ValorantConfig.Stretched("ResolutionSizeX=1440\nResolutionSizeY=1080\nbShouldLetterbox=True\n"));
        Assert.False(ValorantConfig.Stretched("ResolutionSizeX=1920\nResolutionSizeY=1080\n"));
        Assert.Null(ValorantConfig.Stretched("nothing"));
    }
}

public class ValorantCrosshairConfigTests
{
    private const string Ini = "[Settings]\r\n" + "EAresStringSettingName::SavedCrosshairProfileData=\"{\\\"currentProfile\\\":1,\\\"profiles\\\":[{\\\"primary\\\":{}},{\\\"primary\\\":{\\\"color\\\":{\\\"r\\\":0,\\\"g\\\":255,\\\"b\\\":255,\\\"a\\\":255},\\\"colorCustom\\\":{\\\"r\\\":1,\\\"g\\\":2,\\\"b\\\":3,\\\"a\\\":255},\\\"bUseCustomColor\\\":false,\\\"bHasOutline\\\":true,\\\"outlineThickness\\\":1,\\\"outlineOpacity\\\":0,\\\"centerDotSize\\\":2,\\\"centerDotOpacity\\\":1,\\\"bDisplayCenterDot\\\":false,\\\"innerLines\\\":{\\\"lineThickness\\\":1,\\\"lineLength\\\":2,\\\"lineLengthVertical\\\":2,\\\"lineOffset\\\":0,\\\"bAllowVertScaling\\\":false,\\\"bShowMovementError\\\":false,\\\"bShowShootingError\\\":false,\\\"opacity\\\":1,\\\"bShowLines\\\":true},\\\"outerLines\\\":{\\\"lineThickness\\\":2,\\\"lineLength\\\":2,\\\"lineLengthVertical\\\":2,\\\"lineOffset\\\":10,\\\"bShowMovementError\\\":true,\\\"bShowShootingError\\\":true,\\\"opacity\\\":0.35,\\\"bShowLines\\\":false}}}]}\"" + "\r\nother=1\r\n";

    [Fact]
    public void Reads_current_profile_and_round_trips_to_a_code()
    {
        var d = ValorantCrosshairConfig.Read(Ini);
        Assert.NotNull(d);
        Assert.Equal("#00FFFF", d!.Color);
        Assert.Equal(0, d.OutlineOpacity);
        Assert.Equal(2, d.InnerLines.Length);
        Assert.Equal(0, d.InnerLines.Offset);
        Assert.False(d.OuterLines.Show);
        var back = BoostRoot.Core.Crosshair.CrosshairDesign.FromValorantCode(d.ToValorantCode())!;
        Assert.Equal(d.InnerLines.Length, back.InnerLines.Length);
        Assert.Equal(d.Color, back.Color);
    }

    [Fact]
    public void Missing_or_broken_gives_null()
    {
        Assert.Null(ValorantCrosshairConfig.Read("foo=1"));
        Assert.Null(ValorantCrosshairConfig.Read("EAresStringSettingName::SavedCrosshairProfileData=\"{bad\""));
    }
}
