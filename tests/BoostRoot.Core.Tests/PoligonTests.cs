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
    public void Reads_fps_cap_only_when_enabled()
    {
        Assert.Equal(180, ValorantConfig.FpsCap("EAresFloatSettingName::MaxFramerateAlways=180\nEAresBoolSettingName::LimitFramerateAlways=True\n"));
        Assert.Null(ValorantConfig.FpsCap("EAresFloatSettingName::MaxFramerateAlways=180\nEAresBoolSettingName::LimitFramerateAlways=False\n"));
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

public class Cs2ConfigTests
{
    private const string Vcfg = """
    "config"
    {
        "convars"
        {
            "sensitivity"		"1.250000"
            "cl_crosshairsize"		"2"
            "cl_crosshairthickness"		"0.5"
            "cl_crosshairgap"		"-2"
            "cl_crosshaircolor"		"5"
            "cl_crosshaircolor_r"		"0"
            "cl_crosshaircolor_g"		"255"
            "cl_crosshaircolor_b"		"255"
            "cl_crosshairdot"		"0"
            "cl_crosshair_drawoutline"		"1"
        }
    }
    """;

    [Fact]
    public void Reads_sens_and_crosshair()
    {
        Assert.Equal(1.25, Cs2Config.Sensitivity(Vcfg));
        var d = Cs2Config.Crosshair(Vcfg)!;
        Assert.Equal("#00FFFF", d.Color);
        Assert.Equal(4, d.InnerLines.Length);
        Assert.Equal(2, d.InnerLines.Offset);
        Assert.True(d.Outline);
        Assert.NotNull(BoostRoot.Core.Crosshair.CrosshairDesign.FromValorantCode(d.ToValorantCode()));
    }

    [Fact]
    public void Round_trips_our_own_export()
    {
        var design = new BoostRoot.Core.Crosshair.CrosshairDesign("t", "#FF00FF", Inner: new(true, 2, 6, 3, 1));
        var cfg = string.Join("\n", design.ToSourceConvars().Select(kv => $"\"{kv.Key}\" \"{kv.Value}\""));
        var back = Cs2Config.Crosshair(cfg)!;
        Assert.Equal(design.Color, back.Color);
        Assert.Equal(design.InnerLines.Length, back.InnerLines.Length);
        Assert.Equal(design.InnerLines.Offset, back.InnerLines.Offset);
        Assert.Equal(design.InnerLines.Thickness, back.InnerLines.Thickness);
    }
}

public class HotspotFilterTests
{
    [Fact]
    public void Mirrored_edge_value_is_never_shown()
    {
        var f = new BoostRoot.Core.Monitoring.HotspotFilter();
        Assert.Null(f.Apply(70, 70));
        Assert.Null(f.Apply(71, 71));
        Assert.Null(f.Apply(50, 49.8));
    }

    [Fact]
    public void Real_sensor_shows_after_it_is_clearly_hotter()
    {
        var f = new BoostRoot.Core.Monitoring.HotspotFilter();
        Assert.Null(f.Apply(40, 40));
        Assert.Equal(86, f.Apply(70, 86));
        Assert.Equal(41, f.Apply(41, 41));   // idle can be equal once it is known to be real
        Assert.Null(f.Apply(60, 30));        // junk below edge
        Assert.Null(f.Apply(60, null));
        Assert.Null(f.Apply(60, 400));
    }
}
