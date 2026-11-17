using Avalonia.Controls;

namespace BoostRoot.App.Views;

public sealed partial class StatsView : UserControl
{
    public StatsView() => InitializeComponent();
}

/// <summary>Bar height (0..1) → pixels inside the 150 px chart area.</summary>
public sealed class BarHeight : Avalonia.Data.Converters.IValueConverter
{
    public static BarHeight Instance { get; } = new();
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is double d ? Math.Round(d * 150) : 0d;
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
