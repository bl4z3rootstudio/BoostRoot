using BoostRoot.Core.IO;
using BoostRoot.Core.Profiles;
using BoostRoot.Core.Safety;
using BoostRoot.Testing;

namespace BoostRoot.Core.Tests;

/// <summary>Every shipped profile must validate; Valorant is exercised against real (sanitised) config files.</summary>
public class RealProfileTests
{
    private static string Data(params string[] p) => Path.Combine([AppContext.BaseDirectory, "TestData", .. p]);

    [Fact]
    public void AllShippedProfiles_Validate()
    {
        var fs = PhysicalFileSystem.Instance;
        var profiles = ProfileLoader.LoadDirectory(fs, Data("profiles"));
        Assert.Contains(profiles, p => p.Id == "valorant");
    }

    private const string Root = "C:/Users/u/AppData/Local/VALORANT/Saved/Config/acc-eu";

    private (InMemoryFileSystem Fs, ProfileApplier Applier, string Rus, string Gus) Setup()
    {
        var fs = new InMemoryFileSystem();
        var rus = $"{Root}/Windows/RiotUserSettings.ini";
        var gus = $"{Root}/WindowsClient/GameUserSettings.ini";
        fs.PutBytes(rus, File.ReadAllBytes(Data("valorant", "RiotUserSettings.ini")));
        fs.PutBytes(gus, File.ReadAllBytes(Data("valorant", "GameUserSettings.ini")));
        var j = new Journal(fs, "C:/BR/j");
        var applier = new ProfileApplier(new PathResolver(fs, p => p.Replace("%LOCALAPPDATA%", "C:/Users/u/AppData/Local")), new SafeFileEditor(fs, j, "C:/BR/b"));
        return (fs, applier, rus, gus);
    }

    internal static GameProfile Valorant => ProfileLoader.LoadDirectory(PhysicalFileSystem.Instance, Data("profiles")).Single(p => p.Id == "valorant");

    [Theory]
    [InlineData(BoostLevel.Quality)]
    [InlineData(BoostLevel.Balanced)]
    [InlineData(BoostLevel.MaxFps)]
    [InlineData(BoostLevel.Competitive)]
    public void Valorant_EveryLevel_KeepsResolutionKeybindsCrosshairAndCrlf(BoostLevel level)
    {
        var (fs, applier, rus, gus) = Setup();
        var gusBefore = TextFile.Decode(fs.ReadAllBytes(gus));
        var report = applier.Apply(Valorant, level, "op");
        Assert.True(report.Success);
        var gusAfter = TextFile.Decode(fs.ReadAllBytes(gus));
        Assert.Equal("\r\n", gusAfter.Newline);
        foreach (var k in new[] { "ResolutionSizeX=1440", "ResolutionSizeY=1080", "LastConfirmedFullscreenMode=0", "bShouldLetterbox=True" })
            Assert.Contains(k, gusAfter.Text);
        var rusText = TextFile.Decode(fs.ReadAllBytes(rus)).Text;
        var before = TextFile.Decode(File.ReadAllBytes(Data("valorant", "RiotUserSettings.ini"))).Text;
        var protectedLines = before.Split('\n').Where(l => l.Contains("Crosshair") || l.Contains("Sensitivity") || l.Contains("Volume")).ToList();
        Assert.NotEmpty(protectedLines);
        Assert.All(protectedLines, l => Assert.Contains(l, rusText));
        Assert.Equal(gusBefore.Text.Split('\n').Length, gusAfter.Text.Split('\n').Length);
    }

    [Fact]
    public void Valorant_MaxFps_OnUsersAlreadyLowSettings_ChangesOnlyClarity()
    {
        var (fs, applier, rus, _) = Setup();
        var report = applier.Apply(Valorant, BoostLevel.MaxFps, "op");
        Assert.Equal(1, report.Targets.Single(t => t.ConfigId == "rus").ChangedKeys);
        Assert.Contains("EAresBoolSettingName::ImproveClarity=False", TextFile.Decode(fs.ReadAllBytes(rus)).Text);
    }

    [Fact]
    public void Valorant_StretchMode_WritesOnlyResolutionKeys()
    {
        var (fs, applier, _, gus) = Setup();
        var v = Valorant;
        var guard = new NeverTouchGuard(v.NeverTouch).WithExemptions(v.StretchKeys!);
        const string S = "gus:/Script/ShooterGame.ShooterGameUserSettings/";
        var extra = new Dictionary<string, string> { [S + "ResolutionSizeX"] = "1280", [S + "ResolutionSizeY"] = "960", [S + "bShouldLetterbox"] = "False" };
        Assert.True(applier.Apply(v, BoostLevel.Competitive, "op", extra, guard).Success);
        var t = TextFile.Decode(fs.ReadAllBytes(gus)).Text;
        Assert.Contains("ResolutionSizeX=1280", t);
        Assert.Contains("bShouldLetterbox=False", t);
        Assert.Contains("DefaultMonitorIndex=0", t);
    }
}
