using BoostRoot.Core.Input;
using Xunit;

namespace BoostRoot.Core.Tests;

public class InputTests
{
    [Fact]
    public void Reaction_measures_time_after_green()
    {
        var t = new ReactionTest();
        t.Start(0, 2000);
        Assert.False(t.Tick(1999));
        Assert.True(t.Tick(2000));
        Assert.Equal(ReactionState.Result, t.Click(2210));
        Assert.Equal(210, t.Last);
        Assert.Equal(210, t.Average);
    }

    [Fact]
    public void Reaction_click_before_green_is_too_early_and_not_counted()
    {
        var t = new ReactionTest();
        t.Start(0, 2000);
        Assert.Equal(ReactionState.TooEarly, t.Click(1500));
        Assert.Empty(t.Results);
    }

    [Fact]
    public void Reaction_click_after_delay_without_tick_still_counts()
    {
        var t = new ReactionTest();
        t.Start(100, 1000);
        Assert.Equal(ReactionState.Result, t.Click(1350));
        Assert.Equal(250, t.Last);
    }

    [Fact]
    public void Reaction_keeps_last_five()
    {
        var t = new ReactionTest();
        for (var i = 0; i < 7; i++) { t.Start(i * 10_000, 1000); t.Click(i * 10_000 + 1000 + 200 + i); }
        Assert.Equal(5, t.Results.Count);
        Assert.Equal(202, t.Best);
    }

    [Theory]
    [InlineData(150, "pro")] [InlineData(200, "good")] [InlineData(260, "avg")] [InlineData(400, "slow")]
    public void Reaction_rating(double ms, string key) => Assert.Equal(key, ReactionTest.Rating(ms));

    [Fact]
    public void Chatter_flags_presses_too_close_together()
    {
        var c = new ClickChatter();
        Assert.False(c.Press(0));
        Assert.True(c.Press(20));   // switch bounce
        Assert.False(c.Press(200)); // normal double click speed
        Assert.False(c.Press(290));
        Assert.Equal(4, c.Presses);
        Assert.Equal(1, c.Chatters);
        c.Reset();
        Assert.Equal(0, c.Chatters);
    }

    [Fact]
    public void Sensitivity_valorant_to_cs2_uses_known_ratio()
    {
        // Community rule: CS sens = Valorant sens × 3.18
        Assert.Equal(1.0 * 0.07 / 0.022, Sensitivity.Convert(1.0, "valorant", "cs2"), 6);
        Assert.Equal(0.4, Sensitivity.Convert(Sensitivity.Convert(0.4, "valorant", "ow2"), "ow2", "valorant"), 9);
    }

    [Fact]
    public void Sensitivity_cm360_cs2()
    {
        // CS2 sens 2 at 800 DPI ≈ 25.98 cm/360 (well-known reference value)
        Assert.Equal(25.98, Sensitivity.CmPer360(2, 800, "cs2"), 1);
        Assert.Equal(0, Sensitivity.CmPer360(0, 800, "cs2"));
        Assert.Equal(1600, Sensitivity.Edpi(2, 800));
    }
}

public class MinecraftRamTests
{
    [Theory]
    [InlineData("-Xmx2G -XX:+UseG1GC", 2048)]
    [InlineData("-Xms1G -Xmx4096M", 4096)]
    [InlineData("-Xmx6g", 6144)]
    [InlineData("-XX:+UseG1GC", null)]
    [InlineData("", null)]
    public void Parses_xmx(string args, int? mb) => Assert.Equal(mb, BoostRoot.Core.Tools.MinecraftRam.ParseXmxMb(args));

    [Fact]
    public void Sets_xmx_keeping_other_flags()
    {
        Assert.Equal("-Xmx6G -XX:+UseG1GC", BoostRoot.Core.Tools.MinecraftRam.WithXmx("-Xmx2G -XX:+UseG1GC", 6));
        Assert.Equal("-Xmx4G -XX:+UseG1GC", BoostRoot.Core.Tools.MinecraftRam.WithXmx("-XX:+UseG1GC", 4));
        Assert.StartsWith("-Xmx4G -XX:+UnlockExperimentalVMOptions", BoostRoot.Core.Tools.MinecraftRam.WithXmx(null, 4));
    }

    [Theory]
    [InlineData(2048, 16, "low")]
    [InlineData(2048, 4, null)]
    [InlineData(6144, 16, null)]
    [InlineData(12288, 16, "high")]
    public void Verdict(int xmx, double total, string? v) => Assert.Equal(v, BoostRoot.Core.Tools.MinecraftRam.Verdict(xmx, total));

    [Fact]
    public void Recommended() { Assert.Equal(4, BoostRoot.Core.Tools.MinecraftRam.RecommendedGb(8)); Assert.Equal(6, BoostRoot.Core.Tools.MinecraftRam.RecommendedGb(16)); Assert.Equal(8, BoostRoot.Core.Tools.MinecraftRam.RecommendedGb(32)); }
}

public class DoctorMinecraftTests
{
    private static BoostRoot.Core.Tools.DoctorFacts F(int? xmx, double ram, string? launcher) =>
        new("Minecraft", "minecraft", true, true, true, false, 100, 100, true, null, false, false, false, false, 10, false, xmx, ram, launcher);

    [Fact]
    public void Low_ram_is_fixable_warning()
    {
        var f = BoostRoot.Core.Tools.GameDoctor.Evaluate(F(2048, 16, "official"));
        var mc = Assert.Single(f, x => x.Id == "mc-ram");
        Assert.True(mc.CanAutoFix);
        Assert.Contains("6 GB", mc.Detail.Tr);
    }

    [Fact]
    public void Fine_ram_gives_no_finding() =>
        Assert.DoesNotContain(BoostRoot.Core.Tools.GameDoctor.Evaluate(F(6144, 16, "official")), x => x.Id.StartsWith("mc-"));

    [Fact]
    public void Modrinth_gets_manual_tip() =>
        Assert.Contains(BoostRoot.Core.Tools.GameDoctor.Evaluate(F(null, 16, "modrinth")), x => x.Id == "mc-ram-manual" && !x.CanAutoFix);
}
