using BoostRoot.Core.Hardware;
using BoostRoot.Core.Net;
using BoostRoot.Core.Platform;
using BoostRoot.Core.Safety;
using BoostRoot.Core.Tools;
using BoostRoot.Testing;

namespace BoostRoot.Core.Tests;

public class NetToolsTests
{
    private sealed class SeqPinger(params double?[] values) : IPinger
    {
        private int _i;
        public Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct) => Task.FromResult(values[_i++ % values.Length]);
    }

    [Fact]
    public async Task Ping_Summary_LossAndJitter()
    {
        var s = await NetAnalysis.MeasureAsync(new SeqPinger(10, 20, null, 10), "x", 4, CancellationToken.None);
        Assert.Equal((4, 3, 13.3, 10.0), (s.Sent, s.Received, s.AvgMs, s.JitterMs));
        Assert.Equal(25, s.LossPercent);
    }

    [Fact] public void Ping_AllLost() => Assert.Equal(100, NetAnalysis.Summarize("x", 5, []).LossPercent);

    [Fact]
    public void Advice_GoodConnection_SaysGood()
    {
        var a = NetAnalysis.Advice([new NetInfo("eth", false, 1000)], [NetAnalysis.Summarize("x", 5, [12, 13, 12, 14, 12])], new SpeedResult(100, 20, 12, 30));
        Assert.Single(a);
        Assert.Contains("iyi", a[0].Tr);
    }

    [Fact]
    public void Advice_WifiLossJitterBufferbloat()
    {
        var a = NetAnalysis.Advice([new NetInfo("wifi", true, 300)], [NetAnalysis.Summarize("x", 10, [10, 60, 10, 70, 10, 65, 10, 60])], new SpeedResult(50, 5, 15, 120));
        Assert.Equal(4, a.Count);
    }

    [Fact]
    public void Junk_SkipsShaderCaches_Downloads_AndRecentFiles()
    {
        var now = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
        var fs = new InMemoryFileSystem()
            .Put("C:/T/old.tmp", "x", now.AddDays(-3))
            .Put("C:/T/new.tmp", "x", now.AddHours(-1))
            .Put("C:/T/NVIDIA/DXCache/a.bin", "x", now.AddDays(-30))
            .Put("C:/T/Downloads/keep.zip", "x", now.AddDays(-30));
        var cleaner = new JunkCleaner(fs, _ => 100);
        var cat = JunkCleaner.Categories("C:/T", "C:/W/Temp", "C:/L", "C:/PD", "C:/W").First(c => c.Id == "user-temp");
        var scan = cleaner.Scan(cat, now);
        Assert.Equal(["C:/T/old.tmp"], scan.Files);
        Assert.Equal(100, cleaner.Clean(scan));
        Assert.False(fs.FileExists("C:/T/old.tmp"));
        Assert.True(fs.FileExists("C:/T/NVIDIA/DXCache/a.bin"));
    }

    [Fact]
    public void Junk_NeverIncludesRecycleBinOrShaderCacheCategories()
    {
        var cats = JunkCleaner.Categories("t", "w", "l", "p", "win");
        Assert.DoesNotContain(cats, c => c.Roots.Any(r => r.Contains("Recycle", StringComparison.OrdinalIgnoreCase)));
        // Shader caches live only in their own opt-in category (unchecked by default).
        Assert.All(cats.Where(c => c.Roots.Any(r => r.Contains("DXCache", StringComparison.OrdinalIgnoreCase))), c => { Assert.Equal(JunkCleaner.ShaderCacheId, c.Id); Assert.True(c.RequiresConfirm); });
    }

    [Fact]
    public void Startup_Disable_Then_Undo()
    {
        var reg = new FakeRegistry();
        reg.SetValue(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Spotify", RegistryValue.Str("spotify.exe /min"));
        var j = new Journal(new InMemoryFileSystem(), "C:/j");
        var ch = new RegistryChanger(reg, j);
        var sm = new StartupManager(reg, ch);
        var item = sm.List(h => h == RegistryHive.CurrentUser ? ["Spotify"] : []).Single();
        Assert.True(item.Enabled);
        sm.SetEnabled("op", item, false);
        Assert.False(sm.List(h => h == RegistryHive.CurrentUser ? ["Spotify"] : []).Single().Enabled);
        new UndoService(j, [ch]).RevertOperation("op");
        Assert.True(sm.List(h => h == RegistryHive.CurrentUser ? ["Spotify"] : []).Single().Enabled);
    }

    [Fact]
    public void Driver_OldAmd_PointsToAmd()
    {
        var hw = new HardwareInfo("cpu", 6, [new GpuInfo("Radeon RX 580", GpuVendor.Amd, 4096, "1", new DateOnly(2026, 1, 1))], 16, [], [], 50, null, [], 1);
        var s = DriverCheck.Check(hw, new DateOnly(2026, 11, 12))!;
        Assert.True(s.IsOld);
        Assert.Contains("amd.com", s.DownloadUrl);
    }
}
