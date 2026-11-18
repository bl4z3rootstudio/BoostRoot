namespace BoostRoot.Core.Input;

/// <summary>Mouse sensitivity between games, via each game's degrees-per-count ("yaw").</summary>
public static class Sensitivity
{
    /// <summary>Yaw (degrees turned per mouse count at sensitivity 1). Well known community values.</summary>
    public static IReadOnlyList<(string Id, string Name, double Yaw)> Games { get; } =
    [
        ("valorant", "Valorant", 0.07),
        ("cs2", "Counter-Strike 2", 0.022),
        ("apex", "Apex Legends", 0.022),
        ("tf2", "Team Fortress 2", 0.022),
        ("ow2", "Overwatch 2", 0.0066),
        ("cod", "Call of Duty (MW/Warzone)", 0.0066),
        ("rust", "Rust", 0.1125),
        ("r6", "Rainbow Six Siege (ADS 50)", 0.00572958),
    ];

    public static double Yaw(string id) => Games.First(g => g.Id == id).Yaw;

    /// <summary>Converts a sensitivity from one game to another with the same physical feel (same cm/360 at the same DPI).</summary>
    public static double Convert(double sens, string from, string to) => sens * Yaw(from) / Yaw(to);

    /// <summary>Centimetres of mouse travel for a full 360° turn.</summary>
    public static double CmPer360(double sens, int dpi, string game) =>
        sens <= 0 || dpi <= 0 ? 0 : 360.0 / (sens * Yaw(game) * dpi) * 2.54;

    public static double Edpi(double sens, int dpi) => sens * dpi;
}
