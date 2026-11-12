using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BoostRoot.App.Controls;

/// <summary>Tiny allocation-free live line chart (0–Max range) for sensor graphs.</summary>
public sealed class LineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty = AvaloniaProperty.Register<LineChart, IReadOnlyList<double>?>(nameof(Values));
    public static readonly StyledProperty<IBrush> StrokeProperty = AvaloniaProperty.Register<LineChart, IBrush>(nameof(Stroke), Brushes.MediumPurple);
    public static readonly StyledProperty<double> MaxProperty = AvaloniaProperty.Register<LineChart, double>(nameof(Max), 100);
    public static readonly StyledProperty<int> CapacityProperty = AvaloniaProperty.Register<LineChart, int>(nameof(Capacity), 300);

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Max { get => GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public int Capacity { get => GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }

    private static readonly IPen Grid = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValuesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged o) o.CollectionChanged -= OnChanged;
            if (change.NewValue is INotifyCollectionChanged n) n.CollectionChanged += OnChanged;
            InvalidateVisual();
        }
    }

    private void OnChanged(object? s, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        for (var i = 1; i < 4; i++) ctx.DrawLine(Grid, new Point(0, b.Height * i / 4), new Point(b.Width, b.Height * i / 4));
        var v = Values;
        if (v is null || v.Count < 2) return;
        var max = Math.Max(Max, v.Max());
        var step = b.Width / Math.Max(1, Capacity - 1);
        var x0 = b.Width - (v.Count - 1) * step;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(x0, b.Height - v[0] / max * b.Height), false);
            for (var i = 1; i < v.Count; i++) g.LineTo(new Point(x0 + i * step, b.Height - v[i] / max * b.Height));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(Stroke, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
    }
}
