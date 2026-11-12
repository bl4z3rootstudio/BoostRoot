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
                _ => "",
            });
        }
        _text.Text = string.Join("\n", lines.Where(l => l.Length > 0));
    }
}

/// <summary>Custom crosshair drawn above the game (click-through, excluded from capture).</summary>
public sealed class CrosshairOverlay : OverlayWindowBase
{
    public CrosshairOverlay(CrosshairSettings c) => Content = new CrosshairShape(c);

    protected override void Place()
    {
        var s = Screen0;
        Position = new PixelPoint(s.X + (s.Width - (int)(Bounds.Width * RenderScaling)) / 2, s.Y + (s.Height - (int)(Bounds.Height * RenderScaling)) / 2);
    }

    private sealed class CrosshairShape(CrosshairSettings c) : Control
    {
        private readonly int _extent = (c.Size + c.Gap + 2) * 2 + c.Thickness;
        protected override Size MeasureOverride(Size availableSize) => new(_extent, _extent);

        public override void Render(DrawingContext ctx)
        {
            var color = Color.TryParse(c.Color, out var col) ? col : Colors.Lime;
            var mid = _extent / 2.0;
            var outline = new Pen(Brushes.Black, c.Thickness + 2);
            var pen = new Pen(new SolidColorBrush(color), c.Thickness);
            (Point, Point)[] arms =
            [
                (new(mid, mid - c.Gap), new(mid, mid - c.Gap - c.Size)), (new(mid, mid + c.Gap), new(mid, mid + c.Gap + c.Size)),
                (new(mid - c.Gap, mid), new(mid - c.Gap - c.Size, mid)), (new(mid + c.Gap, mid), new(mid + c.Gap + c.Size, mid)),
            ];
            if (c.Size > 0)
            {
                if (c.Outline) foreach (var (a, b) in arms) ctx.DrawLine(outline, a, b);
                foreach (var (a, b) in arms) ctx.DrawLine(pen, a, b);
            }
            if (c.Dot)
            {
                var r = Math.Max(1, c.Thickness);
                if (c.Outline) ctx.DrawEllipse(Brushes.Black, null, new Point(mid, mid), r + 1, r + 1);
                ctx.DrawEllipse(new SolidColorBrush(color), null, new Point(mid, mid), r, r);
            }
        }
    }
}
