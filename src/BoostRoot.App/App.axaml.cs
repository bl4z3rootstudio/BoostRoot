using Avalonia.Input.Platform;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BoostRoot.ViewModels;

namespace BoostRoot.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = WindowsAppServices.Create();
            var vm = new MainViewModel(services);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            services.SessionStarter = (entry, settings) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var session = new GameSession(entry, settings, services.Sensors, services.IsGameRunning,
                    names => new BoostRoot.Platform.Windows.PresentMonSource(names), services.CreateClipRecorder(settings));
                session.RecordPlaytime = (id, name, a, b) => services.Playtime.Add(new BoostRoot.Core.Fps.PlaySession(id, name, a, b));
                session.RaisePriority = services.SetGameHighPriority;
                session.Ended += () => { window.WindowState = Avalonia.Controls.WindowState.Normal; window.Activate(); };
            });
            desktop.Exit += (_, _) => GameSession.Current?.Dispose();
            services.ClipboardSetter = text => _ = window.Clipboard?.SetTextAsync(text);
            services.ExitForUpdate = () => Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
            Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = vm.CheckUpdateAsync(), TimeSpan.FromSeconds(4));
            _ = vm.Scan.ScanCommand.ExecuteAsync(null); // score drives level recommendations
            _ = Task.Run(async () =>
            {
                if (await services.PrefetchCoversAsync(services.LoadCatalog(), default) > 0)
                    Avalonia.Threading.Dispatcher.UIThread.Post(vm.Home.Reload);
            });
        }
        base.OnFrameworkInitializationCompleted();
    }
}
