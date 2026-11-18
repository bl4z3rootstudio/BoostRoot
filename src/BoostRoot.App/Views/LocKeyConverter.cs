using System.Globalization;
using Avalonia.Data.Converters;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.App.Views;

/// <summary>Shows a localized label for a stored code ("tl" → "Sol üst").</summary>
public sealed class LocKeyConverter(string prefix) : IValueConverter
{
    public static LocKeyConverter Corner { get; } = new("corner.");
    public static LocKeyConverter Audio { get; } = new("audio.");
    public static LocKeyConverter Plain { get; } = new("");
    /// <summary>0 → "Off", otherwise "144 FPS".</summary>
    public static LocKeyConverter FpsCap { get; } = new("fpscap");
    public static LocKeyConverter Theme { get; } = new("theme.");
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int n when prefix == "fpscap" => n == 0 ? Loc.I["settings.fpsCapOff"] : $"{n} FPS",
        string s => Loc.I[prefix + s],
        _ => value,
    };
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
