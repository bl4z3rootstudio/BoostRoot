using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BoostRoot.Core.Fps;
using BoostRoot.Core.Session;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.App;

/// <summary>Square "my gaming week" picture for sharing (saved to Pictures\BoostRoot).</summary>
internal static class WeeklyCardRenderer
{
    private static TextBlock T(string text, double size, string color = "#FFFFFF", FontWeight w = FontWeight.Normal) =>
        new() { Text = text, FontSize = size, Foreground = Brush.Parse(color), FontWeight = w, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private static Control Stat(string label, string value) => new Border
    {
        Background = Brush.Parse("#1F1638"), BorderBrush = Brush.Parse("#3B2A66"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(28),
        Padding = new Thickness(24, 20), Margin = new Thickness(12),
        Child = new StackPanel { Spacing = 4, Children = { T(value, 64, "#C084FC", FontWeight.Bold), T(label, 26, "#A19DB5") } },
    };

    public static string Save(WeeklyReport w)
    {
        const int size = 1080;
        var logo = new Image { Width = 120, Height = 120, Source = new Bitmap(AssetLoader.Open(new Uri("avares://BoostRoot/Assets/logo-256.png"))) };
        var root = new Border
        {
            Width = size, Height = size, Padding = new Thickness(60),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#0B0912"), 0), new GradientStop(Color.Parse("#1A0F33"), 0.6), new GradientStop(Color.Parse("#3B1F66"), 1) },
            },
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Spacing = 16,
                Children =
                {
                    logo,
                    T(Loc.I["week.title"], 56, "#FFFFFF", FontWeight.Bold),
                    new UniformGrid { Columns = 2, Children =
                    {
                        Stat(Loc.I["stats.total"], PlaytimeLog.Format(w.Played)), Stat(Loc.I["stats.sessions"], w.Sessions.ToString(System.Globalization.CultureInfo.CurrentCulture)),
                        Stat(Loc.I["stats.avgFps"], w.AvgFps is { } f ? $"{f:0}" : "—"), Stat(Loc.I["week.topShort"], w.TopGame ?? "—"),
                    } },
                    T("BoostRoot · github.com/bl4z3rootstudio/BoostRoot", 26, "#C084FC"),
                },
            },
        };
        root.Measure(new Size(size, size));
        root.Arrange(new Rect(0, 0, size, size));
        using var bmp = new RenderTargetBitmap(new PixelSize(size, size));
        bmp.Render(root);
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BoostRoot");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"hafta-{DateTime.Now:yyyyMMdd-HHmmss}.png");
#pragma warning disable CS0618
        bmp.Save(path);
#pragma warning restore CS0618
        return path;
    }
}
