using BoostRoot.Core.Fps;
using BoostRoot.Testing;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class FpsTests
{
    [Fact]
    public void Stats_ComputesAvgLowsAndStutters()
    {
        var ft = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(40.0, 10)).ToList();
        var s = FrameStats.From(ft)!;
        Assert.Equal(1000, s.Frames);
        Assert.Equal(1000 / (10.3), s.AvgFps, 0);
        Assert.Equal(25, s.Low1);
        Assert.Equal(10, s.Stutters);
        Assert.Null(FrameStats.From([16, 16]));
        Assert.Equal(20, FrameStats.Gain(s with { AvgFps = 100 }, s with { AvgFps = 120 }));
    }

    [Fact]
    public void Csv_ParsesV1AndV2()
    {
        var v1 = new PresentMonCsv();
        Assert.Null(v1.Parse("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,MsBetweenPresents,MsInPresentAPI"));
        Assert.Equal(6.94, v1.Parse("VALORANT.exe,1,0x1,DXGI,0,0,1,Hardware: Independent Flip,6.94,0.1"));
        var v2 = new PresentMonCsv();
        v2.Parse("Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,FrameType,CPUStartTime,FrameTime,CPUBusy");
        Assert.Equal(4.2, v2.Parse("cs2.exe,2,0x2,DXGI,0,0,1,Hardware Composed: Independent Flip,Application,123.4,4.2,3.1"));
        Assert.Null(v2.Parse("garbage"));
    }

    [Fact]
    public void Rolling_ReportsLastSecond()
    {
        var r = new RollingFps();
        for (var i = 0; i < 300; i++) r.Add(5);
        Assert.Equal(200, r.Fps);
    }

    [Fact]
    public void History_StoresAndCompares()
    {
        var h = new FpsHistory(new InMemoryFileSystem(), "C:/h.jsonl");
        var s = new FrameStats(100, 1, 100, 80, 70, 10, 0);
        h.Add(new FpsRun("val", "VALORANT", new DateTime(2026, 1, 1), FpsHistory.Before, s));
        Assert.Null(h.Comparison("val"));
        h.Add(new FpsRun("val", "VALORANT", new DateTime(2026, 1, 2), FpsHistory.After, s with { AvgFps = 130 }));
        var c = h.Comparison("val")!.Value;
        Assert.Equal(130, c.After.Stats.AvgFps);
        Assert.Equal(2, h.All().Count);
    }

    [Fact]
    public async Task ViewModel_MeasuresBeforeAfter_AndShares()
    {
        var svc = ViewModelTests.Services();
        var vm = new FpsViewModel(svc);
        Assert.NotNull(vm.Selected);
        await vm.RunAsync(FpsHistory.Before, TimeSpan.Zero, TimeSpan.FromMilliseconds(300));
        Assert.NotNull(vm.Before);
        svc.Frames.Ms = 5;
        await vm.RunAsync(FpsHistory.After, TimeSpan.Zero, TimeSpan.FromMilliseconds(300));
        Assert.True(vm.GainPositive);
        Assert.Equal(2, vm.History.Count);
        vm.Share("story");
        Assert.NotNull(svc.LastCard);
        Assert.Equal(["C:/card.png"], svc.Opened);
    }
}
