using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace BoostRoot.App;

/// <summary>Draws a 600x900 cover for games without store art: a per-game color gradient with the game's own .exe icon,
/// or big initials when there is no icon. Nothing is downloaded and no third-party artwork is copied.</summary>
internal static class CoverPoster
{
    public static void Save(string name, BoostRoot.Platform.Windows.ExeIcon.Pixels? icon, string path)
    {
        const int w = 600, h = 900;
        var hue = (uint)name.Aggregate(17, (a, c) => a * 31 + c) % 360;
        Color Hsl(double l, double s = 0.55) => HslColor.ToRgb(hue, s, l, 1);
        Control centre;
        if (icon is not null)
        {
            var bmp = new WriteableBitmap(new PixelSize(icon.Width, icon.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = bmp.Lock())
                System.Runtime.InteropServices.Marshal.Copy(icon.Bgra, 0, fb.Address, icon.Bgra.Length);
            centre = new Border
            {
                Width = 300, Height = 300, CornerRadius = new CornerRadius(56), Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 60, Spread = 4, Color = Color.FromArgb(120, 0, 0, 0), OffsetY = 18 }),
                Child = new Image { Source = bmp, Width = 220, Height = 220, Stretch = Stretch.Uniform },
            };
        }
        else
        {
            var words = name.Split([' ', '-', ':', '.'], StringSplitOptions.RemoveEmptyEntries).Where(x => char.IsLetterOrDigit(x[0])).ToArray();
            var initials = words.Length switch { 0 => "?", 1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(), _ => string.Concat(words.Take(3).Select(x => char.ToUpperInvariant(x[0]))) };
            centre = new TextBlock { Text = initials, FontSize = 170, FontWeight = FontWeight.Black, Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)) };
        }
        centre.HorizontalAlignment = HorizontalAlignment.Center;
        centre.VerticalAlignment = VerticalAlignment.Center;
        var root = new Grid
        {
            Width = w, Height = h,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Hsl(0.42), 0), new GradientStop(Hsl(0.22), 0.55), new GradientStop(Color.Parse("#0B0912"), 1) },
            },
            Children =
            {
                // soft light spot behind the icon
                new Border { Width = 520, Height = 520, CornerRadius = new CornerRadius(260), Margin = new Thickness(0, -120, 0, 0),
                    Background = new RadialGradientBrush { GradientStops = { new GradientStop(Color.FromArgb(70, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) } } },
                new Border { Child = centre, Margin = new Thickness(0, -120, 0, 0) },
            },
        };
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        using var rt = new RenderTargetBitmap(new PixelSize(w, h));
        rt.Render(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
#pragma warning disable CS0618
        rt.Save(path);
#pragma warning restore CS0618
    }
}
