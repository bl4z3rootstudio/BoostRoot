using BoostRoot.Core.Boost;
using BoostRoot.Core.Platform;
using BoostRoot.Core.Profiles;
using BoostRoot.Core.Safety;
using BoostRoot.Core.Tweaks;
using BoostRoot.Games;
using BoostRoot.Testing;

namespace BoostRoot.Core.Tests;

public class RegistryAndUndoTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _reg = new();
    private Journal J => new(_fs, "C:/BR/j");

    [Fact]
    public void Registry_Set_Then_Revert_RestoresPreviousAndAbsent()
    {
        _reg.SetValue(RegistryHive.CurrentUser, "K", "A", RegistryValue.Dword(1));
        var j = J;
        var ch = new RegistryChanger(_reg, j);
        Assert.True(ch.Set("op", RegistryHive.CurrentUser, "K", "A", RegistryValue.Dword(0)));
        Assert.True(ch.Set("op", RegistryHive.CurrentUser, "K", "B", RegistryValue.Str("x")));
        Assert.False(ch.Set("op", RegistryHive.CurrentUser, "K", "B", RegistryValue.Str("x")));
        new UndoService(j, [ch]).RevertOperation("op");
        Assert.Equal(RegistryValue.Dword(1), _reg.GetValue(RegistryHive.CurrentUser, "K", "A"));
        Assert.Null(_reg.GetValue(RegistryHive.CurrentUser, "K", "B"));
        Assert.All(j.ReadAll(), e => Assert.True(e.Reverted));
    }

    [Fact]
    public void Undo_ReverseOrder_SameValueTwice()
    {
        var j = J;
        var ch = new RegistryChanger(_reg, j);
        ch.Set("op", RegistryHive.CurrentUser, "K", "A", RegistryValue.Dword(1));
        ch.Set("op", RegistryHive.CurrentUser, "K", "A", RegistryValue.Dword(2));
        new UndoService(j, [ch]).RevertOperation("op");
        Assert.Null(_reg.GetValue(RegistryHive.CurrentUser, "K", "A"));
    }

    [Fact]
    public void PowerPlan_Set_And_Revert()
    {
        var plans = new FakePowerPlans(PowerSchemes.Balanced);
        var j = J;
        var pc = new PowerPlanChanger(plans, j);
        Assert.True(pc.SetActive("op", PowerSchemes.HighPerformance));
        new UndoService(j, [pc]).RevertOperation("op");
        Assert.Equal(PowerSchemes.Balanced, plans.Active);
    }

    [Fact]
    public void Undo_UnknownKind_Throws_ButRevertsOthers()
    {
        var j = J;
        var ch = new RegistryChanger(_reg, j);
        ch.Set("op", RegistryHive.CurrentUser, "K", "A", RegistryValue.Dword(1));
        j.Record("op", "mystery", "x", null, false, null);
        Assert.Throws<AggregateException>(() => new UndoService(j, [ch]).RevertOperation("op"));
        Assert.Null(_reg.GetValue(RegistryHive.CurrentUser, "K", "A"));
    }

    [Theory]
    [InlineData(null, "SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable=1;")]
    [InlineData("VRROptimizeEnable=0;", "SwapEffectUpgradeEnable=1;", "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;")]
    [InlineData("SwapEffectUpgradeEnable=0;AutoHDREnable=1;", "SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable=1;AutoHDREnable=1;")]
    public void MergeDx_KeepsOtherFields(string? existing, string update, string expected) =>
        Assert.Equal(expected, WindowsTweaks.MergeDx(existing, update));

    [Fact]
    public void Tweak_GameDvr_Apply_And_Undo()
    {
        var j = J;
        var ch = new RegistryChanger(_reg, j);
        var n = WindowsTweaks.Apply(WindowsTweaks.Get("gamedvr-off"), "op", _reg, ch, null);
        Assert.Equal(2, n);
        Assert.Equal(RegistryValue.Dword(0), _reg.GetValue(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled"));
        new UndoService(j, [ch]).RevertOperation("op");
        Assert.Equal(0, _reg.Count);
    }

    [Fact]
    public void Tweaks_NeverTouchNetworkOrDefender()
    {
        foreach (var w in WindowsTweaks.All.SelectMany(t => t.Writes))
        {
            Assert.DoesNotContain("Tcpip", w.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Dnscache", w.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Defender", w.Key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WindowsUpdate", w.Key, StringComparison.OrdinalIgnoreCase);
        }
        Assert.All(WindowsTweaks.All.Where(t => t.Id == "hvci-off"), t => { Assert.Equal(TweakTier.Advanced, t.Tier); Assert.NotNull(t.Warning); });
    }
}

public class OrchestratorTests
{
    private const string Gus = "C:/L/VALORANT/Saved/Config/acc/WindowsClient/GameUserSettings.ini";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _reg = new();
    private readonly FakeProcessManager _proc = new();
    private readonly FakePowerPlans _plans = new(PowerSchemes.Balanced);
    private readonly GameProfile _p = ProfileLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "_example.json")));

    private (BoostOrchestrator O, Journal J, UndoService U) Build(params IBoostStep[] extra)
    {
        var j = new Journal(_fs, "C:/BR/j");
        var editor = new SafeFileEditor(_fs, j, "C:/BR/b");
        var applier = new ProfileApplier(new PathResolver(_fs, p => p.Replace("%LOCALAPPDATA%", "C:/L")), editor);
        var rc = new RegistryChanger(_reg, j);
        var pc = new PowerPlanChanger(_plans, j);
        var undo = new UndoService(j, [new FileUndoHandler(_fs), rc, pc]);
        return (new BoostOrchestrator(applier, _proc, _reg, rc, pc, undo, extra, () => "op1"), j, undo);
    }

    private BoostRequest Req(params string[] close) =>
        new(_p, BoostLevel.Competitive, close, ["power-high", "gamedvr-off"], "C:/Riot Games/VALORANT/live/VALORANT.exe", ["VALORANT-Win64-Shipping.exe", "VALORANT.exe"]);

    [Fact]
    public void Boost_RunsAllSteps_InOrder_AndUndoRestoresEverything()
    {
        _fs.Put(Gus, "[ScalabilityGroups]\nsg.ShadowQuality=3\n");
        _proc.Running.AddRange([new(10, "chrome.exe", null), new(11, "Spotify.exe", null), new(12, "Discord.exe", null)]);
        var (o, _, undo) = Build();
        var r = o.Run(Req("chrome", "spotify.exe"));
        Assert.True(r.Success);
        Assert.Equal(["game-settings", "close-apps", "windows"], r.Steps.Select(s => s.StepId));
        Assert.Equal(["chrome.exe", "Spotify.exe"], _proc.Closed);
        Assert.Equal(PowerSchemes.HighPerformance, _plans.Active);
        Assert.Contains("ShadowQuality=0", _fs.Text(Gus));
        undo.RevertOperation(r.OperationId);
        Assert.Equal("[ScalabilityGroups]\nsg.ShadowQuality=3\n", _fs.Text(Gus));
        Assert.Equal(PowerSchemes.Balanced, _plans.Active);
        Assert.Equal(0, _reg.Count);
    }

    [Fact]
    public void Boost_GameRunning_ReturnsError_WritesNothing()
    {
        _fs.Put(Gus, "[ScalabilityGroups]\nsg.ShadowQuality=3\n");
        _proc.Running.Add(new(5, "VALORANT-Win64-Shipping.exe", null));
        var (o, j, _) = Build();
        var r = o.Run(Req());
        Assert.False(r.Success);
        Assert.Equal(BoostOrchestrator.GameRunningError, r.Error);
        Assert.Empty(j.ReadAll());
        Assert.Contains("=3", _fs.Text(Gus));
    }

    [Fact]
    public void Boost_GameSettingsFail_RollsBack()
    {
        _fs.Put(Gus, "[ScalabilityGroups]\nsg.ShadowQuality=3\n");
        _fs.SetReadOnly(Gus, true);
        var (o, _, _) = Build();
        var r = o.Run(Req("chrome"));
        Assert.False(r.Success);
        Assert.Equal("GameSettingsFailed", r.Error);
        Assert.Equal(PowerSchemes.Balanced, _plans.Active);
        Assert.Empty(_proc.Closed);
    }

    [Fact]
    public void CloseList_FiltersProtectedProcesses()
    {
        var list = BoostOrchestrator.FilterCloseList(["chrome.exe", "vgc.exe", "audiodg", "SplitWire.exe", "winws.exe", "explorer.exe", "Spotify"]);
        Assert.Equal(["chrome", "spotify"], list);
    }

    private sealed class ThrowingStep : IBoostStep
    {
        public string Id => "gpu";
        public StepResult Run(BoostContext context) => throw new InvalidOperationException("driver missing");
    }

    [Fact]
    public void Boost_ExtraStepFailure_IsReported_NotFatal()
    {
        _fs.Put(Gus, "[ScalabilityGroups]\nsg.ShadowQuality=3\n");
        var (o, _, _) = Build(new ThrowingStep());
        var r = o.Run(Req());
        Assert.True(r.Success);
        Assert.Equal(StepStatus.Failed, r.Steps.Single(s => s.StepId == "gpu").Status);
    }
}

public class GameLocatorTests
{
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeRegistry _reg = new();
    private LocatorEnvironment Env => new(_fs, _reg, "C:/ProgramData", "C:/Users/u/AppData/Roaming", "C:/Users/u/AppData/Local", ["C:", "D:"]);

    [Fact]
    public void Vdf_Parses_Nested_And_Escapes()
    {
        var n = VdfReader.Parse("\"a\" { \"b\" \"x\\\\y\" // c\n \"c\" { \"d\" \"1\" } }");
        Assert.Equal("x\\y", n.Child("a")!.Str("b"));
        Assert.Equal("1", n.Child("a")!.Child("c")!.Str("d"));
    }

    [Fact]
    public void Steam_FindsGames_InAllLibraries()
    {
        _reg.SetValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", RegistryValue.Str("c:/program files (x86)/steam"));
        _fs.Put("C:/Program Files (x86)/Steam/steamapps/libraryfolders.vdf", "\"libraryfolders\"\n{\n \"0\" { \"path\" \"C:\\\\Program Files (x86)\\\\Steam\" }\n \"1\" { \"path\" \"D:\\\\SteamLibrary\" }\n}\n");
        _fs.Put("C:/Program Files (x86)/Steam/steamapps/appmanifest_730.acf", "\"AppState\" { \"appid\" \"730\" \"name\" \"Counter-Strike 2\" \"installdir\" \"Counter-Strike Global Offensive\" }");
        _fs.Put("D:/SteamLibrary/steamapps/appmanifest_892970.acf", "\"AppState\" { \"appid\" \"892970\" \"name\" \"Valheim\" \"installdir\" \"Valheim\" }");
        _fs.Put("D:/SteamLibrary/steamapps/appmanifest_1.acf", "bozuk");
        var g = new SteamLocator(Env).Locate();
        Assert.Equal(["730", "892970"], g.Select(x => x.SourceId).Order());
        Assert.EndsWith("common/Valheim", g.Single(x => x.SourceId == "892970").InstallPath);
    }

    [Fact]
    public void Epic_ReadsManifests_SkipsBroken()
    {
        _fs.Put("C:/ProgramData/Epic/EpicGamesLauncher/Data/Manifests/a.item", "{\"AppName\":\"Fortnite\",\"DisplayName\":\"Fortnite\",\"InstallLocation\":\"C:\\\\Epic\\\\Fortnite\",\"CatalogNamespace\":\"fn\",\"CatalogItemId\":\"4fe\",\"LaunchExecutable\":\"FortniteGame/Binaries/Win64/FortniteLauncher.exe\"}");
        _fs.Put("C:/ProgramData/Epic/EpicGamesLauncher/Data/Manifests/b.item", "{bozuk");
        var g = Assert.Single(new EpicLocator(Env).Locate());
        Assert.Equal("Fortnite", g.SourceId);
        var cmd = LaunchCommands.For(g)!;
        Assert.Equal("com.epicgames.launcher://apps/fn%3A4fe%3AFortnite?action=launch&silent=true", cmd.FileName);
    }

    [Fact]
    public void Riot_FindsValorant_AndBuildsLaunch()
    {
        _fs.Put("C:/ProgramData/Riot Games/RiotClientInstalls.json", "{\"rc_default\":\"C:/Riot Games/Riot Client/RiotClientServices.exe\"}");
        _fs.Put("C:/ProgramData/Riot Games/Metadata/valorant.live/valorant.live.product_settings.yaml", "product_install_full_path: \"C:/Riot Games/VALORANT/live\"\nother: 1\n");
        var g = Assert.Single(new RiotLocator(Env).Locate());
        Assert.Equal("C:/Riot Games/VALORANT/live", g.InstallPath);
        var cmd = LaunchCommands.For(g)!;
        Assert.Equal("--launch-product=valorant --launch-patchline=live", cmd.Arguments);
        Assert.False(cmd.UseShellExecute);
    }

    [Fact]
    public void Xbox_Minecraft_Roblox()
    {
        _fs.CreateDirectory("C:/XboxGames/Palworld/Content");
        _fs.CreateDirectory("C:/XboxGames/GameSave");
        _fs.CreateDirectory("C:/Users/u/AppData/Roaming/.minecraft");
        _fs.CreateDirectory("C:/Users/u/AppData/Roaming/ModrinthApp/profiles/Azram");
        _fs.CreateDirectory("C:/Users/u/AppData/Local/Roblox/Versions");
        Assert.Equal("Palworld", Assert.Single(new XboxLocator(Env).Locate()).SourceId);
        Assert.Equal(2, new MinecraftLocator(Env).Locate().Count);
        Assert.Single(new RobloxLocator(Env).Locate());
    }

    [Fact]
    public void Locators_EmptySystem_ReturnEmpty()
    {
        IGameLocator[] all = [new SteamLocator(Env), new EpicLocator(Env), new RiotLocator(Env), new XboxLocator(Env), new MinecraftLocator(Env), new RobloxLocator(Env)];
        Assert.All(all, l => Assert.Empty(l.Locate()));
    }

    [Fact]
    public void Catalog_MatchesByDetect_InstalledFirst()
    {
        var p = ProfileLoader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "_example.json")));
        var other = p with { Id = "aaa", Name = "AAA Game", Detect = new Dictionary<string, string> { ["steam"] = "1" } };
        var games = new[] { new InstalledGame("riot", "valorant", "VALORANT", "C:/x", new Dictionary<string, string>()) };
        var entries = GameCatalog.Match([other, p], games);
        Assert.True(entries[0].IsInstalled);
        Assert.Equal(p.Id, entries[0].Profile!.Id);
        Assert.False(entries[1].IsInstalled);
    }
}
