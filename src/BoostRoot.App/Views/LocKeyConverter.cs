using System.Globalization;
using Avalonia.Data.Converters;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.App.Views;

/// <summary>Shows a localized label for a stored code ("tl" → "Sol üst").</summary>
public sealed class LocKeyConverter(string prefix) : IValueConverter
{
    public static LocKeyConverter Corner { get; } = new("corner.");
    public static LocKeyConverter Audio { get; } = new("audio.");
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string s ? Loc.I[prefix + s] : value;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
