using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using BoostRoot.App;
using BoostRoot.ViewModels;

// Renders every page of the real UI with fake services → PNGs for visual QA (Linux, no GPU).
var outDir = args.Length > 0 ? args[0] : "/tmp/shots";
Directory.CreateDirectory(outDir);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var svc = new BoostRoot.Core.Tests.FakeAppServices();
var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "profiles");
var json = BoostRoot.Core.Profiles.ProfileLoader.LoadDirectory(BoostRoot.Core.IO.PhysicalFileSystem.Instance, dir).Where(p => p.Id != "example").ToList();
var all = json.Concat(BoostRoot.Core.Profiles.GameLibrary.Parse(File.ReadAllText(Path.Combine(dir, "library.txt")), json)).ToList();
string[] installed = ["valorant", "minecraft", "cs2", "lib-overwatch-2", "rocket-league", "lib-forza-horizon-4", "valheim", "lib-sea-of-thieves"];
foreach (var p in all) svc.Catalog.Add(new BoostRoot.Games.CatalogEntry(p, installed.Contains(p.Id) ? new BoostRoot.Games.InstalledGame("steam", p.Id, p.Name, "C:/g/" + p.Id, new Dictionary<string, string>()) : null));
if (Environment.GetEnvironmentVariable("SHOTLANG") is { } sl) BoostRoot.ViewModels.Localization.Loc.I.SetLanguage(sl);
var vm = new MainViewModel(svc);
vm.FinishWelcome();
var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
w.Show();
void Pump(int ms = 600) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(16); } }
vm.Updates.ScanCommand.Execute(null);
foreach (var nav in vm.Nav)
{
    vm.Navigate(nav);
    Pump(900);
    w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, nav.Key + ".png"));
}
foreach (var lang in new[] { "de", "ar", "ru" })
{
    BoostRoot.ViewModels.Localization.Loc.I.SetLanguage(lang);
    w.FlowDirection = lang == "ar" ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;
    Console.WriteLine(lang + ": " + BoostRoot.ViewModels.Localization.Loc.I.Text("x", "RAM runs at 2133 MHz, rated 3200 MHz"));
    BoostRoot.App.App.Relayout(w);
    foreach (var key in new[] { "boost", "scan", "tests" })
    {
        vm.Navigate(vm.Nav.First(n => n.Key == key));
        Pump(900);
        w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, $"{lang}-{key}.png"));
    }
}
Console.WriteLine("ok");
