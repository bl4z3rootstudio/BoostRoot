using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace BoostRoot.App;

/// <summary>Loads a cached cover image from disk; null (placeholder tint shows) when missing.</summary>
public sealed class CoverConverter : IValueConverter
{
    public static CoverConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try { return new Bitmap(path); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { return null; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>"#RRGGBB" → brush (accent swatches).</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public static HexBrushConverter Instance { get; } = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Avalonia.Media.Color.TryParse(s, out var c) ? new Avalonia.Media.SolidColorBrush(c) : null;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Language code → its own name, so the picker reads "Deutsch" instead of "de".</summary>
public sealed class LanguageNameConverter : IValueConverter
{
    public static LanguageNameConverter Instance { get; } = new();
    private static readonly Dictionary<string, string> Names = new()
    {
        ["tr"] = "Türkçe", ["en"] = "English", ["az"] = "Azərbaycanca", ["de"] = "Deutsch",
        ["ru"] = "Русский", ["es"] = "Español", ["pt"] = "Português", ["fr"] = "Français", ["ar"] = "العربية",
    };
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Names.TryGetValue(s, out var n) ? n : value;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class SelectedBorderConverter : IValueConverter
{
    public static SelectedBorderConverter Instance { get; } = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => new Avalonia.Thickness(value is true ? 3 : 0);
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
