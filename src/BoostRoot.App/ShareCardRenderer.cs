using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BoostRoot.ViewModels;
using BoostRoot.ViewModels.Localization;

namespace BoostRoot.App;

/// <summary>Builds the before/after share image (9:16 story or 1:1 square) and saves it as PNG.</summary>
internal static class ShareCardRenderer
{
    private static readonly IBrush Bg = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#0B0912"), 0), new GradientStop(Color.Parse("#1A0F33"), 0.6), new GradientStop(Color.Parse("#3B1F66"), 1) },
    };

    private static TextBlock T(string text, double size, string color = "#FFFFFF", FontWeight w = FontWeight.Normal) =>
        new() { Text = text, FontSize = size, Foreground = Brush.Parse(color), FontWeight = w, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };

    private static Control Column(string label, double fps, double low, bool accent) => new Border
    {
        Background = Brush.Parse(accent ? "#2A1650" : "#16122A"), BorderBrush = Brush.Parse(accent ? "#A855F7" : "#2E2647"),
        BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(36), Padding = new Thickness(40, 30), Margin = new Thickness(16),
        Child = new StackPanel { Spacing = 6, Children = { T(label, 36, "#A19DB5"), T($"{fps:0}", 120, accent ? "#C084FC" : "#FFFFFF", FontWeight.Bold), T("FPS", 32, "#A19DB5"), T($"1% low {low:0}", 30, "#A19DB5") } },
    };

    public static string Save(ShareCard c, bool story)
    {
        var (w, h) = story ? (1080, 1920) : (1080, 1080);
        var logo = new Image { Width = story ? 200 : 140, Height = story ? 200 : 140, Source = new Bitmap(AssetLoader.Open(new Uri("avares://BoostRoot/Assets/logo-256.png"))) };
        var gain = (c.Gain >= 0 ? "+" : "") + c.Gain.ToString("0.#") + "%";
        var root = new Border
        {
            Width = w, Height = h, Background = Bg, Padding = new Thickness(60),
            Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Spacing = story ? 40 : 18,
                Children =
                {
                    logo,
                    T(c.Game, story ? 72 : 60, "#FFFFFF", FontWeight.Bold),
                    new UniformGrid { Columns = 2, Children = { Column(Loc.I["fps.before"], c.Before.AvgFps, c.Before.Low1, false), Column(Loc.I["fps.after"], c.After.AvgFps, c.After.Low1, true) } },
                    T(gain, story ? 170 : 120, c.Gain >= 0 ? "#86EFAC" : "#FCD34D", FontWeight.Bold),
                    T($"{c.Cpu}\n{c.Gpu}", 28, "#A19DB5"),
                    T("BoostRoot · github.com/bl4z3rootstudio/BoostRoot", 30, "#C084FC"),
                },
            },
        };
        root.Measure(new Size(w, h));
        root.Arrange(new Rect(0, 0, w, h));
        using var bmp = new RenderTargetBitmap(new PixelSize(w, h));
        bmp.Render(root);
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BoostRoot");
        Directory.CreateDirectory(dir);
        var safe = string.Concat(c.Game.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        var path = Path.Combine(dir, $"{safe}-{(story ? "story" : "kare")}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
#pragma warning disable CS0618
        bmp.Save(path);
#pragma warning restore CS0618
        return path;
    }
}
