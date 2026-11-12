using BoostRoot.Core.Platform;
using BoostRoot.Core.Safety;
using BoostRoot.Platform.Windows;
using BoostRoot.Testing;

namespace BoostRoot.Windows.Tests;

/// <summary>Runs only on Windows (CI windows-latest). Uses a throwaway HKCU key.</summary>
public class WindowsPlatformTests
{
    private const string Key = @"Software\BoostRoot.Tests";

    [Fact]
    public void Registry_RoundTrip_And_JournaledUndo()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        var reg = new WindowsRegistry();
        reg.DeleteValue(RegistryHive.CurrentUser, Key, "Dw");
        reg.SetValue(RegistryHive.CurrentUser, Key, "Str", RegistryValue.Str("önce"));
        var j = new Journal(new InMemoryFileSystem(), "C:/j");
        var ch = new RegistryChanger(reg, j);
        ch.Set("op", RegistryHive.CurrentUser, Key, "Dw", RegistryValue.Dword(7));
        ch.Set("op", RegistryHive.CurrentUser, Key, "Str", RegistryValue.Str("sonra"));
        Assert.Equal(RegistryValue.Dword(7), reg.GetValue(RegistryHive.CurrentUser, Key, "Dw"));
        new UndoService(j, [ch]).RevertOperation("op");
        Assert.Null(reg.GetValue(RegistryHive.CurrentUser, Key, "Dw"));
        Assert.Equal(RegistryValue.Str("önce"), reg.GetValue(RegistryHive.CurrentUser, Key, "Str"));
        reg.DeleteValue(RegistryHive.CurrentUser, Key, "Str");
    }

    [Fact]
    public void PowerPlan_ReadsActive()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        Assert.NotNull(new WindowsPowerPlans().GetActive());
    }

    [Fact]
    public void Processes_ListIncludesSelf()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        Assert.Contains(new WindowsProcessManager().List(), p => p.Id == Environment.ProcessId);
    }
}
