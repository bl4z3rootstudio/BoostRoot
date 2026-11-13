using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BoostRoot.Core.Crosshair;

namespace BoostRoot.App.Controls;

/// <summary>Draws a <see cref="CrosshairDesign"/> at a zoom factor on a game-like background.</summary>
public sealed class CrosshairPreview : Control
{
    public static readonly StyledProperty<CrosshairDesign?> DesignProperty = AvaloniaProperty.Register<CrosshairPreview, CrosshairDesign?>(nameof(Design));
    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<CrosshairPreview, double>(nameof(Zoom), 4);
    public CrosshairDesign? Design { get => GetValue(DesignProperty); set => SetValue(DesignProperty, value); }
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    static CrosshairPreview() => AffectsRender<CrosshairPreview>(DesignProperty, ZoomProperty);

    public static void Draw(DrawingContext ctx, CrosshairDesign d, Point c, double z)
    {
        var col = Color.TryParse(d.Color, out var cc) ? cc : Colors.Lime;
        void Rect(double x, double y, double w, double h, double opacity)
        {
            if (w <= 0 || h <= 0 || opacity <= 0) return;
            var r = new Rect(x, y, w, h);
            if (d.Outline)
            {
                var o = d.OutlineThickness * z;
                ctx.FillRectangle(new SolidColorBrush(Colors.Black, d.OutlineOpacity * opacity), r.Inflate(o));
            }
            ctx.FillRectangle(new SolidColorBrush(col, opacity), r);
        }
        void Lines(CrosshairLines l)
        {
            if (!l.Show || l.Length <= 0) return;
            double t = l.Thickness * z, len = l.Length * z, vlen = l.VLength * z, off = l.Offset * z;
            if (!d.HideTop) Rect(c.X - t / 2, c.Y - off - vlen, t, vlen, l.Opacity);
            Rect(c.X - t / 2, c.Y + off, t, vlen, l.Opacity);
            Rect(c.X - off - len, c.Y - t / 2, len, t, l.Opacity);
            Rect(c.X + off, c.Y - t / 2, len, t, l.Opacity);
        }
        Lines(d.OuterLines);
        Lines(d.InnerLines);
        if (d.Dot)
        {
            var s = d.DotThickness * z;
            if (d.RoundDot)
            {
                if (d.Outline) ctx.DrawEllipse(new SolidColorBrush(Colors.Black, d.OutlineOpacity), null, c, s / 2 + d.OutlineThickness * z, s / 2 + d.OutlineThickness * z);
                ctx.DrawEllipse(new SolidColorBrush(col, d.DotOpacity), null, c, s / 2, s / 2);
            }
            else Rect(c.X - s / 2, c.Y - s / 2, s, s, d.DotOpacity);
        }
    }

    public static readonly StyledProperty<bool> TransparentProperty = AvaloniaProperty.Register<CrosshairPreview, bool>(nameof(Transparent));
    /// <summary>No sky background (used by the in-game overlay).</summary>
    public bool Transparent { get => GetValue(TransparentProperty); set => SetValue(TransparentProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        if (Transparent) { if (Design is { } dd) Draw(ctx, dd, new Point(b.Width / 2, b.Height / 2), Zoom); return; }
        ctx.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#6E86A8"), 0), new GradientStop(Color.Parse("#B9A27A"), 0.55), new GradientStop(Color.Parse("#4A4038"), 1) },
        }, new Rect(b.Size));
        if (Design is { } d) Draw(ctx, d, new Point(b.Width / 2, b.Height / 2), Zoom);
    }
}
