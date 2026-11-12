using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace BoostRoot.App.Views;

/// <summary>Loads a local image file as a small decoded bitmap (thumbnails); null when missing.</summary>
public sealed class FileImageConverter : IValueConverter
{
    public static FileImageConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try { using var s = File.OpenRead(path); return Bitmap.DecodeToWidth(s, 440); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { return null; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
