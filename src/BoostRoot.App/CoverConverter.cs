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
