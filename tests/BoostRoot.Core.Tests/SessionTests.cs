using BoostRoot.Core.Profiles;
using BoostRoot.Core.Session;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class SessionTests
{
    [Fact]
    public void Stretch_ComputesFourByThree() { Assert.Equal((1440, 1080), StretchPlan.For(1080)); Assert.Equal((1920, 1440), StretchPlan.For(1440)); Assert.Equal((1024, 768), StretchPlan.For(768)); }

    [Fact]
    public void Stretch_ValuesForValorantKeys()
    {
        var p = RealProfileTests.Valorant;
        var v = StretchPlan.Values(p, 1440, 1080);
        Assert.Equal("1440", v["gus:/Script/ShooterGame.ShooterGameUserSettings/ResolutionSizeX"]);
        Assert.Equal("1080", v["gus:/Script/ShooterGame.ShooterGameUserSettings/LastUserConfirmedResolutionSizeY"]);
        Assert.Equal("False", v["gus:/Script/ShooterGame.ShooterGameUserSettings/bShouldLetterbox"]);
        Assert.Equal("0", v["gus:/Script/ShooterGame.ShooterGameUserSettings/PreferredFullscreenMode"]);
        Assert.Equal(p.StretchKeys!.Count, v.Count);
    }

    [Fact]
    public void Watcher_WaitsRunsEndsOrTimesOut()
    {
        var running = false;
        var w = new GameWatcher(_ => running, ["game.exe"], TimeSpan.FromSeconds(10));
        Assert.Equal(GameWatcher.State.Waiting, w.Tick(TimeSpan.FromSeconds(2)));
        running = true;
        Assert.Equal(GameWatcher.State.Running, w.Tick(TimeSpan.FromSeconds(2)));
        running = false;
        Assert.Equal(GameWatcher.State.Ended, w.Tick(TimeSpan.FromSeconds(2)));
        var t = new GameWatcher(_ => false, ["x"], TimeSpan.FromSeconds(3));
        t.Tick(TimeSpan.FromSeconds(2));
        Assert.Equal(GameWatcher.State.TimedOut, t.Tick(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Settings_KeepsAllFields_AndStretchToggleSaves()
    {
        var svc = ViewModelTests.Services();
        var main = new MainViewModel(svc);
        main.Settings.CrosshairEnabled = true;
        main.Settings.Metrics.First(m => m.Key == "vram").IsOn = true;
        main.Settings.ReplaySeconds = 60;
        Assert.True(svc.Saved.CrosshairOrDefault.Enabled);
        Assert.Contains("vram", svc.Saved.Metrics);
        Assert.Equal(60, svc.Saved.ClipsOrDefault.ReplaySeconds);
        main.Settings.Sounds = !main.Settings.Sounds; // unrelated change must not drop the others
        Assert.True(svc.Saved.CrosshairOrDefault.Enabled);
        main.Home.Select(main.Home.Games.First(g => g.IsInstalled));
        main.Home.IsStretched = true;
        Assert.True(svc.Saved.IsStretched(main.Home.Selected!.Entry.Profile!.Id));
        Assert.True(svc.Saved.CrosshairOrDefault.Enabled);
        main.Home.IsStretched = false;
        Assert.False(svc.Saved.IsStretched(main.Home.Selected!.Entry.Profile!.Id));
    }
}

public class ShellExtrasTests
{
    [Fact]
    public void Welcome_ShowsOnce()
    {
        var svc = ViewModelTests.Services();
        var main = new MainViewModel(svc);
        Assert.True(main.ShowWelcome);
        main.FinishWelcome();
        Assert.False(main.ShowWelcome);
        Assert.True(svc.Saved.FirstRunDone);
        Assert.False(new MainViewModel(svc).ShowWelcome);
    }

    [Fact]
    public async Task Tools_AdvancedTweaks_ToggleAndUndo()
    {
        var svc = ViewModelTests.Services();
        var vm = new ToolsViewModel(svc);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Tweaks);
        vm.Tweaks[0].IsOn = true;
        Assert.Single(svc.Tweaks);
        vm.Tweaks[0].IsOn = false;
        Assert.Empty(svc.Tweaks);
    }

    [Fact]
    public void Home_ShowsWhatChanges_ForLevel()
    {
        var vm = new HomeViewModel(ViewModelTests.Services());
        Assert.NotEmpty(vm.Changes);
        var before = vm.Changes.Count;
        vm.ChooseLevel(vm.Levels.First(l => l.Level == BoostRoot.Core.Profiles.BoostLevel.Quality));
        Assert.NotNull(vm.Changes);
        Assert.True(vm.Changes.Count >= 1 && before >= 1);
    }
}

public class GpuProfileTests
{
    private sealed class FakeDrv : BoostRoot.Core.Gpu.IGpuDriverProfiles
    {
        public bool Supported => true;
        public Dictionary<uint, uint> Applied = [];
        public string? Reverted;
        public (int, string) Apply(string exeName, IReadOnlyDictionary<uint, uint> settings) { foreach (var kv in settings) Applied[kv.Key] = kv.Value; return (settings.Count, "created=1"); }
        public void Revert(string exeName, string backup) => Reverted = exeName + "|" + backup;
    }

    [Fact]
    public void Nvidia_SettingsFromPrefs_AndBackupRoundTrip()
    {
        var d = BoostRoot.Core.Gpu.NvidiaSettings.From(new BoostRoot.Core.Profiles.GpuPrefs(LowLatency: true, VSync: false, TextureFiltering: "performance"));
        Assert.Equal(4, d.Count);
        Assert.Equal(BoostRoot.Core.Gpu.NvidiaSettings.VSyncForceOff, d[BoostRoot.Core.Gpu.NvidiaSettings.VSyncMode]);
        var enc = BoostRoot.Core.Gpu.NvidiaSettings.Encode(false, new Dictionary<uint, uint?> { [0x1057EB71] = 0, [0x007BA09E] = null });
        var (created, prev) = BoostRoot.Core.Gpu.NvidiaSettings.Decode(enc);
        Assert.False(created);
        Assert.Equal(0u, prev[0x1057EB71]);
        Assert.Null(prev[0x007BA09E]);
    }

    [Fact]
    public void Changer_JournalsAndUndoes()
    {
        var fs = new BoostRoot.Testing.InMemoryFileSystem();
        var journal = new BoostRoot.Core.Safety.Journal(fs, "C:/j");
        var drv = new FakeDrv();
        var ch = new BoostRoot.Core.Gpu.GpuProfileChanger(drv, journal);
        Assert.Equal(1, ch.Apply("op1", "cs2.exe", new BoostRoot.Core.Profiles.GpuPrefs()));
        new BoostRoot.Core.Safety.UndoService(journal, [ch]).RevertOperation("op1");
        Assert.Equal("cs2.exe|created=1", drv.Reverted);
    }
}
