using BoostRoot.Core.Hardware;
using BoostRoot.Core.Platform;
using BoostRoot.Testing;

namespace BoostRoot.Core.Tests;

public class HardwareTests
{
    [Theory]
    [InlineData("Radeon RX 580 Series", 40)]
    [InlineData("NVIDIA GeForce RTX 4060 Laptop GPU", 74)]
    [InlineData("NVIDIA GeForce RTX 4090", 96)]
    [InlineData("AMD Radeon RX 7800 XT", 82)]
    [InlineData("Intel(R) UHD Graphics 630", 12)]
    [InlineData("Some Future GPU", 50)]
    [InlineData(null, 50)]
    public void GpuScore(string? name, int expected) => Assert.Equal(expected, HardwareTables.GpuScore(name));

    [Theory]
    [InlineData("Intel(R) Core(TM) i5-9400F CPU @ 2.90GHz", 48)]
    [InlineData("AMD Ryzen 7 7800X3D 8-Core Processor", 100)]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", 68)]
    [InlineData("12th Gen Intel(R) Core(TM) i5-12400F", 68)]
    public void CpuScore(string name, int expected) => Assert.Equal(expected, HardwareTables.CpuScore(name));

    private static byte[] Type17(ushort sizeMb, ushort speed, ushort configured)
    {
        var s = new byte[0x22];
        s[0] = 17; s[1] = 0x22;
        BitConverter.GetBytes(sizeMb).CopyTo(s, 0x0C);
        BitConverter.GetBytes(speed).CopyTo(s, 0x15);
        BitConverter.GetBytes(configured).CopyTo(s, 0x20);
        return [.. s, 0, 0];
    }

    [Fact]
    public void Smbios_ParsesMemoryDevices_SkipsEmptySlots()
    {
        byte[] other = [4, 4, 0, 0, (byte)'C', (byte)'P', (byte)'U', 0, 0];
        byte[] table = [.. other, .. Type17(16384, 2666, 2133), .. Type17(0, 0, 0), .. Type17(16384, 2666, 2133), 127, 4, 0, 0, 0, 0];
        byte[] rsmb = [0, 3, 3, 0, .. BitConverter.GetBytes(table.Length), .. table];
        var mods = SmbiosMemory.Parse(rsmb);
        Assert.Equal(2, mods.Count);
        Assert.All(mods, m => Assert.Equal((16384, 2666, 2133), (m.SizeMb, m.SpeedMts, m.ConfiguredMts)));
    }

    [Fact] public void Smbios_Garbage_ReturnsEmpty() => Assert.Empty(SmbiosMemory.Parse([1, 2, 3]));

    private static HardwareInfo Pc(double ramRatioConfigured = 2133, int curHz = 240, int maxHz = 240, long net = 1000, bool wifi = false, double free = 13, DateOnly? driver = null) =>
        new("Intel(R) Core(TM) i5-9400F", 6,
            [new GpuInfo("Radeon RX 580 Series", GpuVendor.Amd, 4096, "32.0", driver ?? new DateOnly(2026, 5, 20))],
            32, [new RamModule(16384, 2666, (int)ramRatioConfigured)], [new DisplayInfo("Legion", 1440, 1080, curHz, maxHz)], free, null,
            [new NetInfo("Realtek PCIe GbE", wifi, net)], 26300);

    [Fact]
    public void Health_UserPc_FlagsXmp_Only()
    {
        var issues = HealthChecks.Evaluate(Pc(), new DateOnly(2026, 11, 12));
        Assert.Equal(["ram-xmp"], issues.Select(i => i.Id));
        Assert.Contains("2133", issues[0].Title.Tr);
    }

    [Fact]
    public void Health_AllProblems()
    {
        var ids = HealthChecks.Evaluate(Pc(2666, curHz: 60, maxHz: 144, net: 100, free: 5, driver: new DateOnly(2025, 1, 1)), new DateOnly(2026, 11, 12)).Select(i => i.Id);
        Assert.Equal(["display-hz", "gpu-driver-old", "net-100mbps", "disk-full"], ids);
    }

    [Fact] public void Health_Wifi_IsInfo() =>
        Assert.Equal(IssueSeverity.Info, HealthChecks.Evaluate(Pc(2666, wifi: true, net: 866), new DateOnly(2026, 11, 12)).Single().Severity);

    [Fact]
    public void Score_UserPc()
    {
        var hw = Pc();
        var s = HealthChecks.Score(hw, HealthChecks.Evaluate(hw, new DateOnly(2026, 11, 12)));
        // gpu 40, cpu 48, ram 85 (xmp off), disk 100, health 80 → 18+12+12.75+5+8 = 55.75
        Assert.Equal(56, s.Total);
    }

    [Fact]
    public void RegistryReader_ReadsGpuAndCpu()
    {
        var r = new FakeRegistry();
        r.SetValue(RegistryHive.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", RegistryValue.Str("Intel(R) Core(TM) i5-9400F CPU @ 2.90GHz  "));
        Assert.Equal("Intel(R) Core(TM) i5-9400F CPU @ 2.90GHz", RegistryHardwareReader.CpuName(r));
    }
}

public class GpuRegistryTests
{
    [Fact]
    public void RegistryReader_ReadsGpus_SkipsBasicDisplay()
    {
        var r = new FakeRegistry();
        var k = RegistryHardwareReader.DisplayClass;
        r.SetValue(RegistryHive.LocalMachine, k + @"\0000", "DriverDesc", RegistryValue.Str("Radeon RX 580 Series"));
        r.SetValue(RegistryHive.LocalMachine, k + @"\0000", "HardwareInformation.qwMemorySize", new RegistryValue(RegistryValueKind.QWord, (4096L * 1024 * 1024).ToString()));
        r.SetValue(RegistryHive.LocalMachine, k + @"\0000", "DriverDate", RegistryValue.Str("5-20-2026"));
        r.SetValue(RegistryHive.LocalMachine, k + @"\0001", "DriverDesc", RegistryValue.Str("Microsoft Basic Display Adapter"));
        r.SetValue(RegistryHive.LocalMachine, k + @"\Properties", "x", RegistryValue.Str("y"));
        var g = Assert.Single(RegistryHardwareReader.Gpus(r));
        Assert.Equal((GpuVendor.Amd, 4096L, new DateOnly(2026, 5, 20)), (g.Vendor, g.VramMb, g.DriverDate!.Value));
    }
}
