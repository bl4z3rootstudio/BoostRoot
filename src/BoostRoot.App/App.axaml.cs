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
                session.Ended += () =>
                {
                    if (!services.LoadSettings().AutoUndo || entry.Profile is null || services.LastBoostOperation(entry.Profile.Id) is not { } op) return;
                    try { services.Undo(op); vm.Home.Select(vm.Home.Selected); } catch (Exception) { }
                };
                session.Ended += () => { window.WindowState = Avalonia.Controls.WindowState.Normal; window.Activate(); };
            });
            desktop.Exit += (_, _) => GameSession.Current?.Dispose();
            services.ClipboardSetter = text => _ = window.Clipboard?.SetTextAsync(text);
            services.ExitForUpdate = () => Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
            Avalonia.Threading.DispatcherTimer.RunOnce(() => _ = vm.CheckUpdateAsync(), TimeSpan.FromSeconds(4));
            _ = vm.Scan.ScanCommand.ExecuteAsync(null); // score drives level recommendations

            ApplyAccent(AccentPalette.Get(services.LoadSettings().Accent));
            AccentPalette.Changed += a => Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyAccent(a));
            SetupTray(desktop, window, services);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Recolours the shared accent brushes in place, so every {StaticResource Accent} user updates live.</summary>
    private void ApplyAccent(Accent a)
    {
        void Set(string key, string hex)
        {
            if (this.TryGetResource(key, null, out var r) && r is Avalonia.Media.SolidColorBrush b && Avalonia.Media.Color.TryParse(hex, out var c)) b.Color = c;
        }
        Set("Accent", a.Hex);
        Set("AccentLight", a.LightHex);
        Set("NavActive", a.NavHex);
        if (Avalonia.Media.Color.TryParse(a.Hex, out var ac1)) Resources["AccentColor"] = ac1;       // gradients use DynamicResource
        if (Avalonia.Media.Color.TryParse(a.LightHex, out var al1)) Resources["AccentLightColor"] = al1;
        if (Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().FirstOrDefault() is { } fluent
            && fluent.Palettes.TryGetValue(Avalonia.Styling.ThemeVariant.Dark, out var pal) && Avalonia.Media.Color.TryParse(a.Hex, out var ac))
            pal.Accent = ac;
    }

    private bool _exiting;

    /// <summary>Tray icon: click opens the window; with the setting on, closing the window hides it to the tray.</summary>
    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, WindowsAppServices services)
    {
        var L = BoostRoot.ViewModels.Localization.Loc.I;
        void Show() { window.Show(); window.WindowState = Avalonia.Controls.WindowState.Normal; window.Activate(); }
        var open = new Avalonia.Controls.NativeMenuItem(L["tray.show"]);
        open.Click += (_, _) => Show();
        var exit = new Avalonia.Controls.NativeMenuItem(L["tray.exit"]);
        exit.Click += (_, _) => { _exiting = true; desktop.Shutdown(); };
        var tray = new Avalonia.Controls.TrayIcon
        {
            Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://BoostRoot/Assets/boostroot.ico"))),
            ToolTipText = L["tray.tip"],
            Menu = [open, new Avalonia.Controls.NativeMenuItemSeparator(), exit],
            IsVisible = services.LoadSettings().TrayIcon,
        };
        tray.Clicked += (_, _) => Show();
        Avalonia.Controls.TrayIcon.SetIcons(this, [tray]);
        window.Closing += (_, e) =>
        {
            if (_exiting || e.CloseReason != Avalonia.Controls.WindowCloseReason.WindowClosing || !services.LoadSettings().TrayIcon) return;
            e.Cancel = true;
            window.Hide();
        };
        services.SettingsChanged += s => Avalonia.Threading.Dispatcher.UIThread.Post(() => tray.IsVisible = s.TrayIcon);
        desktop.ShutdownRequested += (_, _) => _exiting = true;
    }
}
