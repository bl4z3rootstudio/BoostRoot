using BoostRoot.Core.Monitoring;
using BoostRoot.Core.Stress;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class StressTests
{
    [Fact]
    public void CpuKernel_IsDeterministic() => Assert.Equal(CpuStress.Kernel(1.2345), CpuStress.Kernel(1.2345));

    [Fact]
    public void Ram_FillVerify_DetectsCorruption()
    {
        var b = new ulong[1000];
        RamStress.Fill(b, 3);
        Assert.Equal(0, RamStress.Verify(b, 3));
        b[500] ^= 1;
        Assert.Equal(1, RamStress.Verify(b, 3));
        Assert.Equal(1000, RamStress.Verify(b, 4));
    }

    [Fact]
    public void Limits_StopOnHeat()
    {
        var l = StressLimits.Default;
        Assert.Null(l.Check(new SensorSnapshot(DateTime.UtcNow, GpuTempC: 80)));
        Assert.Equal("stress.stop.gpuTemp", l.Check(new SensorSnapshot(DateTime.UtcNow, GpuTempC: 91)));
        Assert.Equal("stress.stop.gpuHotspot", l.Check(new SensorSnapshot(DateTime.UtcNow, GpuTempC: 80, GpuHotspotC: 106)));
    }

    [Fact]
    public void CpuDrop_MeasuresThrottle()
    {
        Assert.Equal(0, StressRunner.CpuDrop([100, 100, 100]));
        Assert.Equal(30, StressRunner.CpuDrop([100, 100, 100, 100, 100, 100, 100, 100, 70, 70]));
    }

    private sealed class Fake(long errorsAfter) : IStressWorkload
    {
        public StressKind Kind => StressKind.Cpu;
        public long Work { get; private set; }
        public long Errors => Work >= errorsAfter ? 1 : 0;
        public async Task RunAsync(CancellationToken ct) { while (!ct.IsCancellationRequested) { Work++; await Task.Delay(1); } }
    }

    [Fact]
    public async Task Runner_CompletesOrStopsOnErrorOrHeat()
    {
        var sensors = new FakeSensors();
        var ok = await new StressRunner(sensors, StressLimits.Default).RunAsync([new Fake(long.MaxValue)], TimeSpan.FromMilliseconds(120), TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(20));
        Assert.True(ok.Passed);
        var bad = await new StressRunner(sensors, StressLimits.Default).RunAsync([new Fake(1)], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(20));
        Assert.Equal("stress.stop.errors", bad.StopReason);
        sensors.GpuTemp = 95;
        var hot = await new StressRunner(sensors, StressLimits.Default).RunAsync([new Fake(long.MaxValue)], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(20));
        Assert.Equal("stress.stop.gpuTemp", hot.StopReason);
        Assert.Equal(95, hot.MaxGpuTemp);
    }

    [Fact]
    public async Task ViewModel_RunsRamTest_AndShowsPass()
    {
        var vm = new StressViewModel(new FakeAppServices());
        vm.ChooseKind(vm.Kinds.Single(k => k.Value == StressKind.Ram));
        await vm.RunAsync(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50));
        Assert.True(vm.ResultOk);
        Assert.NotEmpty(vm.TempSeries);
        Assert.False(vm.IsRunning);
    }
}
