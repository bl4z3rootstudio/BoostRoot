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
