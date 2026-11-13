using BoostRoot.Core.Ui;
using BoostRoot.ViewModels;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.Core.Tests;

public class Release4Tests
{
    [Fact]
    public void BoostSound_IsValidShortWav()
    {
        var w = BoostSound.Wav();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(w, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(w, 8, 4));
        Assert.Equal(w.Length - 8, BitConverter.ToInt32(w, 4));
        Assert.InRange(w.Length, 5_000, 40_000);
    }

    [Theory]
    [InlineData(0, 1, 60, true, false, 60)]   // laptop on battery
    [InlineData(1, 8, 90, true, true, 90)]    // charging
    [InlineData(1, 128, 255, false, true, null)] // desktop, no battery
    public void Battery_MapsWin32(byte ac, byte flag, byte pct, bool has, bool onAc, int? percent)
    {
        var b = BatteryState.FromWin32(ac, flag, pct);
        Assert.Equal((has, onAc, percent), (b.HasBattery, b.OnAc, b.Percent));
        Assert.Equal(has && !onAc, b.HurtsFps);
    }

    [Fact]
    public void Languages_AllLoad_AndFallBackToEnglish()
    {
        var en = Loc.Load("en");
        foreach (var lang in Loc.Languages)
        {
            var t = Loc.Load(lang).Where(kv => !kv.Key.StartsWith('@')).ToDictionary();
            Assert.True(t.Count >= en.Count - 15, $"{lang}: only {t.Count} of {en.Count} keys translated");
            Assert.All(t, kv => Assert.True(en.ContainsKey(kv.Key), $"{lang}:{kv.Key} not in en"));
            Assert.All(t, kv => Assert.Equal(System.Text.RegularExpressions.Regex.Matches(en[kv.Key], @"\{\d\}").Count,
                System.Text.RegularExpressions.Regex.Matches(kv.Value, @"\{\d\}").Count));
        }
        Assert.Equal("Einstellungen", Loc.Load("de")["nav.settings"]);
        Assert.Equal("Stromquelle", Loc.Load("de")["pf.battery"]);
    }

    [Fact]
    public void BuiltInTexts_TranslateIncludingTemplates()
    {
        var de = Loc.Load("de");
        Assert.Equal("Du bist per WLAN verbunden", de["@You're on Wi-Fi"]);
        Assert.True(de.ContainsKey("@RAM runs at {0} MHz, rated {1} MHz"));
        foreach (var lang in Loc.Languages.Except(["tr", "en"]))
            Assert.True(Loc.Load(lang).Count(kv => kv.Key.StartsWith('@')) > 150, lang);
    }

    [Fact]
    public void Settings_AccentAndTray_Persist()
    {
        var svc = new FakeAppServices();
        var main = new MainViewModel(svc);
        Accent? raised = null;
        void H(Accent a) => raised = a;
        AccentPalette.Changed += H;
        try
        {
            main.Settings.SetAccent("green");
            Assert.Equal("green", svc.Saved.Accent);
            Assert.Equal("#22C55E", raised?.Hex);
            Assert.Single(main.Settings.Accents, a => a.IsSelected);
            main.Settings.TrayIcon = false;
            Assert.False(svc.Saved.TrayIcon);
            Assert.Equal("purple", AccentPalette.Get("nope").Key);
        }
        finally { AccentPalette.Changed -= H; }
    }
}
