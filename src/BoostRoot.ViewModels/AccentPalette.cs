namespace BoostRoot.ViewModels;

public sealed record Accent(string Key, string Hex, string LightHex, string NavHex);

/// <summary>Selectable accent colours. The app listens to <see cref="Changed"/> and recolours the shared brushes live.</summary>
public static class AccentPalette
{
    public static IReadOnlyList<Accent> All { get; } =
    [
        new("purple", "#A855F7", "#C084FC", "#241D3D"),
        new("blue", "#3B82F6", "#60A5FA", "#1B2540"),
        new("green", "#22C55E", "#4ADE80", "#17301F"),
        new("red", "#EF4444", "#F87171", "#341A1D"),
        new("orange", "#F97316", "#FB923C", "#35221A"),
        new("pink", "#EC4899", "#F472B6", "#351A2B"),
    ];

    public static Accent Get(string? key) => All.FirstOrDefault(a => a.Key == key) ?? All[0];

    public static event Action<Accent>? Changed;
    internal static void Raise(string key) => Changed?.Invoke(Get(key));
}

/// <param name="Light">Uses the light control theme (dark text).</param>
public sealed record UiTheme(string Key, string Bg, string Surface, string Surface2, string Sidebar, string Hero, string Border, string Text, string Muted, bool Light = false);

/// <summary>Whole-app looks (backgrounds, panels, text) — independent of the accent colour.</summary>
public static class ThemePalette
{
    public static IReadOnlyList<UiTheme> All { get; } =
    [
        new("midnight", "#0B0A12", "#14121F", "#1C1830", "#100E19", "#120E1C", "#2A2540", "#EDEBF5", "#A19DB5"),
        new("oled", "#000000", "#0A0A0C", "#141418", "#000000", "#050507", "#24242A", "#F2F2F5", "#9A9AA5"),
        new("slate", "#0F172A", "#131D35", "#1E293B", "#0B1324", "#111B31", "#334155", "#E5EAF3", "#94A3B8"),
        new("forest", "#0A120E", "#101C16", "#17271F", "#08100C", "#0E1914", "#24382D", "#E6F2EA", "#9DB5A6"),
        new("ember", "#140B0A", "#1E1210", "#2A1915", "#100807", "#1A0F0D", "#3D2620", "#F5ECE9", "#B8A19B"),
        new("light", "#F4F3F8", "#FFFFFF", "#ECEAF3", "#EAE8F2", "#F0EEF7", "#D6D2E3", "#17141F", "#5D5870", true),
    ];

    public static UiTheme Get(string? key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];
    public static event Action<UiTheme>? Changed;
    internal static void Raise(string key) => Changed?.Invoke(Get(key));
}
