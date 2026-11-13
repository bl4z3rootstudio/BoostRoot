using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using BoostRoot.Core.Fps;
using BoostRoot.Core.Monitoring;
using BoostRoot.Platform.Windows;
using BoostRoot.ViewModels;

namespace BoostRoot.App.Overlay;

/// <summary>Base for click-through, always-on-top, capture-excluded windows (no injection into the game).</summary>
public abstract class OverlayWindowBase : Window
{
    protected OverlayWindowBase()
    {
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (TryGetPlatformHandle()?.Handle is { } h) WindowStyles.MakeOverlay(h);
        Place();
    }

    protected abstract void Place();

    protected PixelRect Screen0 => Screens.Primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
}

/// <summary>Afterburner-style stats panel. Alt+O toggles it.</summary>
public sealed class StatsOverlay : OverlayWindowBase
{
    private readonly ISensors _sensors;
    private readonly RollingFps _fps;
    private readonly IReadOnlyList<string> _metrics;
    private readonly string _corner;
    private readonly TextBlock _text = new() { FontFamily = new FontFamily("Consolas"), FontSize = 14, Foreground = Brushes.White, LineHeight = 19 };
    private readonly DispatcherTimer _timer;
    private readonly Queue<double> _lowWindow = new();
    private double? _ping;
    private int _tick;
    private readonly BoostRoot.Core.Net.IcmpPinger _pinger = new();

    public StatsOverlay(ISensors sensors, RollingFps fps, AppSettings settings)
    {
        _sensors = sensors; _fps = fps; _metrics = settings.Metrics; _corner = settings.OverlayCorner;
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 10, 8, 18)), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6), Child = _text };
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Refresh());
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    protected override void Place()
    {
        var s = Screen0;
        var w = (int)(Bounds.Width * RenderScaling);
        var h = (int)(Bounds.Height * RenderScaling);
        Position = _corner switch
        {
            "tr" => new PixelPoint(s.Right - w - 12, s.Y + 12),
            "bl" => new PixelPoint(s.X + 12, s.Bottom - h - 12),
            "br" => new PixelPoint(s.Right - w - 12, s.Bottom - h - 12),
            _ => new PixelPoint(s.X + 12, s.Y + 12),
        };
    }

    private void Refresh()
    {
        var x = _sensors.Read();
        var fps = _fps.Fps;
        if (fps is { } f) { _lowWindow.Enqueue(f); while (_lowWindow.Count > 20) _lowWindow.Dequeue(); }
        static string N(double? v, string u) => v is { } d ? $"{d:0}{u}" : "—";
        if (_metrics.Contains("ping") && _tick++ % 4 == 0)
            _ = Task.Run(async () => { var r = await _pinger.PingAsync("1.1.1.1", TimeSpan.FromMilliseconds(900), CancellationToken.None); _ping = r; });
        var hot = x.GpuTempC >= 85 || x.CpuTempC >= 90;
        _text.Foreground = hot ? Brushes.OrangeRed : Brushes.White;
        var lines = new List<string>();
        foreach (var m in _metrics)
        {
            lines.Add(m switch
            {
                "fps" => $"FPS   {N(fps, "")}",
                "low1" => $"LOW   {(_lowWindow.Count > 0 ? $"{_lowWindow.Min():0}" : "—")}",
                "cpu" => $"CPU   {N(x.CpuLoad, "%")}",
                "cputemp" => $"CPU°  {N(x.CpuTempC, "°C")}",
                "gpu" => $"GPU   {N(x.GpuLoad, "%")}",
                "gputemp" => $"GPU°  {N(x.GpuTempC, "°C")}",
                "vram" => $"VRAM  {N(x.VramUsedMb, " MB")}",
                "ram" => $"RAM   {N(x.RamUsedPct, "%")}",
                "clock" => $"CLK   {N(x.GpuClockMhz, " MHz")}",
                "ping" => $"PING  {N(_ping, " ms")}",
                "time" => $"TIME  {DateTime.Now:HH:mm}",
                _ => "",
            });
        }
        _text.Text = string.Join("\n", lines.Where(l => l.Length > 0));
    }
}

/// <summary>Your Crosshair Studio design drawn above any game (click-through, excluded from capture).</summary>
public sealed class CrosshairOverlay : OverlayWindowBase
{
    public CrosshairOverlay(BoostRoot.Core.Crosshair.CrosshairDesign d)
    {
        var extent = (Math.Max(d.InnerLines.Offset + Math.Max(d.InnerLines.Length, d.InnerLines.VLength), d.OuterLines.Show ? d.OuterLines.Offset + Math.Max(d.OuterLines.Length, d.OuterLines.VLength) : 0) + d.OutlineThickness + 4) * 2;
        Content = new BoostRoot.App.Controls.CrosshairPreview { Design = d, Zoom = 1, Transparent = true, Width = extent, Height = extent };
    }

    protected override void Place()
    {
        var s = Screen0;
        Position = new PixelPoint(s.X + (s.Width - (int)(Bounds.Width * RenderScaling)) / 2, s.Y + (s.Height - (int)(Bounds.Height * RenderScaling)) / 2);
    }
}

/// <summary>Short message at the bottom of the screen ("Clip saved").</summary>
public sealed class ToastOverlay : OverlayWindowBase
{
    private ToastOverlay(string text)
    {
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 26, 15, 51)), BorderBrush = new SolidColorBrush(Color.Parse("#A855F7")), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 10),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeight.SemiBold },
        };
    }

    protected override void Place()
    {
        var s = Screen0;
        Position = new PixelPoint(s.X + (s.Width - (int)(Bounds.Width * RenderScaling)) / 2, s.Bottom - (int)(Bounds.Height * RenderScaling) - 80);
    }

    public static void Show(string text)
    {
        var t = new ToastOverlay(text);
        t.Show();
        DispatcherTimer.RunOnce(t.Close, TimeSpan.FromSeconds(2.5));
    }
}
