using BoostRoot.Core.Updates;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class UpdateCenterTests
{
    private const string WingetOut = "   - \\ | \n\b\bName                          Id                        Version       Available     Source\n" +
        "------------------------------------------------------------------------------------------------\n" +
        "Google Chrome                 Google.Chrome             130.0.6723.70 131.0.6778.86 winget\n" +
        "Discord                       Discord.Discord           1.0.9168      1.0.9171      winget\n" +
        "7-Zip 23.01 (x64)             7zip.7zip                 23.01         24.08         winget\n" +
        "3 upgrades available.\n";

    [Fact]
    public void Winget_ParsesTable()
    {
        var apps = WingetTable.Parse(WingetOut);
        Assert.Equal(3, apps.Count);
        Assert.Equal(new AppUpdate("Google Chrome", "Google.Chrome", "130.0.6723.70", "131.0.6778.86", "winget"), apps[0]);
        Assert.Equal("7zip.7zip", apps[2].Id);
        Assert.Empty(WingetTable.Parse("No installed package found matching input criteria."));
    }

    [Fact]
    public void Winget_ParsesTurkishHeaders()
    {
        var tr = "Ad                Kimlik            Sürüm   Kullanılabilir Kaynak\n-------------------------------------------------------------------\nSteam             Valve.Steam       2.10    2.10.91.91     winget\n1 yükseltme var.\n";
        var a = Assert.Single(WingetTable.Parse(tr));
        Assert.Equal("Valve.Steam", a.Id);
        Assert.Equal("2.10.91.91", a.Available);
    }

    [Fact]
    public void WindowsUpdate_ParsesJsonAndInstallResult()
    {
        var json = """[{"Id":"6b8f2c3a-1111-2222-3333-444455556666","Title":"AMD - Display","Type":2,"Auto":false,"Size":1048576,"Kb":""},{"Id":"x","Title":"2026-11 Cumulative Update Preview","Type":1,"Auto":false,"Size":5,"Kb":"5000001"}]""";
        var u = WindowsUpdateJson.Parse(json);
        Assert.True(u[0].IsDriver);
        Assert.True(u[1].LooksUnneeded);
        Assert.Single(WindowsUpdateJson.Parse("""{"Id":"a","Title":"t","Type":1,"Auto":true,"Size":0,"Kb":"1"}"""));
        Assert.Empty(WindowsUpdateJson.Parse("garbage"));
        Assert.Equal((true, true), WindowsUpdateJson.ParseInstall("x\nRESULT:2;REBOOT:True\n"));
        Assert.Equal((false, false), WindowsUpdateJson.ParseInstall("RESULT:4;REBOOT:False"));
        Assert.DoesNotContain("'evil'", WindowsUpdateJson.InstallScript(["evil'; rm", "6b8f2c3a-1111-2222-3333-444455556666"]));
    }

    [Fact]
    public void Bloat_MatchesKnownAndProtectsXbox()
    {
        var json = """[{"Name":"king.com.CandyCrushSaga","PackageFullName":"king.com.CandyCrushSaga_1_x64__k"},{"Name":"Microsoft.XboxGamingOverlay","PackageFullName":"x"},{"Name":"Microsoft.BingNews","PackageFullName":"Microsoft.BingNews_4_x64__8"},{"Name":"Microsoft.WindowsStore","PackageFullName":"s"}]""";
        var b = Bloatware.Match(json, true);
        Assert.Equal(["king.com.CandyCrushSaga", "Microsoft.BingNews"], b.Select(x => x.Name));
        Assert.Equal("'BAD'", Bloatware.RemoveScript("x'; Remove-Item C:\\"));
    }

    [Fact]
    public async Task ViewModel_ScanUpdateInstallRemove()
    {
        var svc = ViewModelTests.Services();
        var vm = new UpdatesViewModel(svc);
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Apps.Count);
        Assert.Equal(2, vm.Windows.Count);
        Assert.True(vm.Windows[0].U.IsDriver);
        Assert.False(vm.Windows.Single(w => w.Unneeded).IsChecked);
        Assert.Contains("4", vm.Summary);
        await vm.UpgradeAllAppsCommand.ExecuteAsync(null);
        Assert.Equal(["Google.Chrome", "Discord.Discord"], svc.Upgraded);
        await vm.InstallWindowsCommand.ExecuteAsync(null);
        Assert.Single(svc.InstalledWin);
        Assert.Contains("yeniden başlat", vm.Status);
        await vm.RemoveBloatCommand.ExecuteAsync(vm.Bloat[0]);
        Assert.Empty(vm.Bloat);
        new MainViewModel(svc).ReportBug();
        Assert.NotNull(svc.BugContext);
    }
}
