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

/// <summary>Afterburner-style stats panel. Alt+O toggles it. Icon + value rows; color, opacity and size come from settings.</summary>
public sealed class StatsOverlay : OverlayWindowBase
{
    private readonly ISensors _sensors;
    private readonly RollingFps _fps;
    private readonly IReadOnlyList<string> _metrics;
    private readonly string _corner;
    private readonly bool _icons;
    private readonly IBrush _fg;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly Dictionary<string, (TextBlock Value, PathIcon? Icon)> _cells = [];
    private readonly DispatcherTimer _timer;
    private readonly Queue<double> _lowWindow = new();
    private double? _ping;
    private int _tick;
    private readonly BoostRoot.Core.Net.IcmpPinger _pinger = new();

    // Small 24x24 path icons (same style as the app's navigation icons).
    private static readonly Dictionary<string, string> IconData = new()
    {
        ["fps"] = "M3 12h4l3 8 4-16 3 8h4",
        ["low1"] = "M12 4v14M6 12l6 6 6-6",
        ["cpu"] = "M6 6h12v12H6zM9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4",
        ["cputemp"] = "M14 14.8V4a2 2 0 0 0-4 0v10.8a4 4 0 1 0 4 0z",
        ["gpu"] = "M2 7h20v10H2zM6 17v3M18 17v3M7 12a2 2 0 1 0 4 0 2 2 0 1 0-4 0",
        ["gputemp"] = "M14 14.8V4a2 2 0 0 0-4 0v10.8a4 4 0 1 0 4 0z",
        ["vram"] = "M3 8h18v8H3zM7 8v8M11 8v8M15 8v8",
        ["ram"] = "M3 8h18v8H3zM7 8v8M11 8v8M15 8v8",
        ["clock"] = "M12 3a9 9 0 1 0 0 18 9 9 0 1 0 0-18zM12 7v5l3 3",
        ["ping"] = "M2 9a15 15 0 0 1 20 0M5 13a10 10 0 0 1 14 0M8.5 16.5a5 5 0 0 1 7 0M12 20h.01",
        ["time"] = "M12 3a9 9 0 1 0 0 18 9 9 0 1 0 0-18zM12 7v5h4",
    };

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["fps"] = "FPS", ["low1"] = "LOW", ["cpu"] = "CPU", ["cputemp"] = "CPU°", ["gpu"] = "GPU", ["gputemp"] = "GPU°",
        ["vram"] = "VRAM", ["ram"] = "RAM", ["clock"] = "CLK", ["ping"] = "PING", ["time"] = "TIME",
    };

    public StatsOverlay(ISensors sensors, RollingFps fps, AppSettings settings)
    {
        _sensors = sensors; _fps = fps; _metrics = settings.Metrics; _corner = settings.OverlayCorner; _icons = settings.OverlayIcons;
        _fg = Color.TryParse(settings.OverlayColor, out var c) ? new SolidColorBrush(c) : Brushes.White;
        var scale = Math.Clamp(settings.OverlayScale, 60, 200) / 100.0;
        var alpha = (byte)Math.Round(Math.Clamp(settings.OverlayOpacity, 0, 100) * 2.55);
        foreach (var m in _metrics)
        {
            if (!Labels.ContainsKey(m)) continue;
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 * scale };
            PathIcon? icon = null;
            if (_icons && IconData.TryGetValue(m, out var data))
            {
                icon = new PathIcon { Data = Geometry.Parse(data), Width = 13 * scale, Height = 13 * scale, Foreground = _fg };
                row.Children.Add(icon);
            }
            else row.Children.Add(new TextBlock { Text = Labels[m], Width = 42 * scale, FontFamily = new FontFamily("Consolas"), FontSize = 12 * scale, Foreground = _fg, Opacity = 0.75 });
            var value = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 14 * scale, FontWeight = FontWeight.SemiBold, Foreground = _fg, MinWidth = 58 * scale };
            row.Children.Add(value);
            _rows.Children.Add(row);
            _cells[m] = (value, icon);
        }
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(alpha, 10, 8, 18)), CornerRadius = new CornerRadius(8 * scale), Padding = new Thickness(10 * scale, 6 * scale), Child = _rows };
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
        foreach (var (m, (value, icon)) in _cells)
        {
            var label = _icons && icon is not null ? Labels[m] + " " : "";
            value.Text = label + m switch
            {
                "fps" => N(fps, ""),
                "low1" => _lowWindow.Count > 0 ? $"{_lowWindow.Min():0}" : "—",
                "cpu" => N(x.CpuLoad, "%"),
                "cputemp" => N(x.CpuTempC, "°C"),
                "gpu" => N(x.GpuLoad, "%"),
                "gputemp" => N(x.GpuTempC, "°C"),
                "vram" => N(x.VramUsedMb, " MB"),
                "ram" => N(x.RamUsedPct, "%"),
                "clock" => N(x.GpuClockMhz, " MHz"),
                "ping" => N(_ping, " ms"),
                "time" => DateTime.Now.ToString("HH:mm"),
                _ => "",
            };
            // Warn colors only on the values that are actually too hot.
            var hot = m switch { "gputemp" => x.GpuTempC >= 85, "cputemp" => x.CpuTempC >= 90, "ping" => _ping >= 120, _ => false };
            value.Foreground = hot ? Brushes.OrangeRed : _fg;
            if (icon is not null) icon.Foreground = value.Foreground;
        }
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

    public static void Show(string text, double seconds = 2.5)
    {
        var t = new ToastOverlay(text);
        t.Show();
        DispatcherTimer.RunOnce(t.Close, TimeSpan.FromSeconds(seconds));
    }
}
