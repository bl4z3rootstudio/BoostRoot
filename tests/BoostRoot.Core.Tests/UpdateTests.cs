using BoostRoot.Core.Updates;
using BoostRoot.Core.Versioning;
using BoostRoot.ViewModels;

namespace BoostRoot.Core.Tests;

public class UpdateTests
{
    private const string Json = """
        {"tag_name":"v26.12.1","name":"BoostRoot 26.12.1","body":"Yenilikler","draft":false,"prerelease":false,
         "assets":[{"name":"BoostRoot-Setup-26.12.1.exe","browser_download_url":"https://github.com/x/setup.exe","size":1234},
                   {"name":"SHA256SUMS.txt","browser_download_url":"https://github.com/x/sums","size":10}]}
        """;

    [Fact]
    public void Evaluate_FindsNewerVerifiedRelease()
    {
        var r = Updater.ParseRelease(Json)!;
        var sums = "abcdef0123  BoostRoot-Setup-26.12.1.exe\n999  other.zip\n";
        var u = Updater.Evaluate(r, AppVersion.Parse("26.11.2"), sums)!;
        Assert.Equal(AppVersion.Parse("26.12.1"), u.Version);
        Assert.Equal("abcdef0123", u.Sha256);
        Assert.Equal("https://github.com/x/sums", Updater.SumsUrl(r));
        Assert.Null(Updater.Evaluate(r, AppVersion.Parse("26.12.1"), sums));
        Assert.Null(Updater.Evaluate(r with { Prerelease = true }, AppVersion.Parse("26.11.1"), sums));
        Assert.Null(Updater.Evaluate(r, AppVersion.Parse("26.11.1"), null)!.Sha256);
        Assert.Null(Updater.ParseRelease("not json"));
    }

    [Fact]
    public async Task Main_ShowsUpdate_AndInstalls()
    {
        var svc = ViewModelTests.Services();
        var r = Updater.ParseRelease(Json)!;
        svc.NextUpdate = Updater.Evaluate(r, AppVersion.Parse("26.11.1"), "aa  BoostRoot-Setup-26.12.1.exe");
        var main = new MainViewModel(svc);
        await main.CheckUpdateAsync();
        Assert.Contains("26.12.1", main.UpdateText);
        svc.UpdateOk = false;
        await main.InstallUpdateCommand.ExecuteAsync(null);
        Assert.NotNull(main.UpdateError);
        main.Donate();
        Assert.Contains(MainViewModel.SponsorsUrl, svc.Opened);
    }
}
