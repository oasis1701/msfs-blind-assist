using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The hand-written rows: the levers the generated map cannot drive (iniBuilds' flaps and speed
/// brake input events only animate the model or fire a raw K: value), written with the stock events
/// the A300's systems take. Each was verified live at a cold-and-dark gate (2026-10-03):
/// <list type="bullet">
/// <item>Flaps: <c>K:FLAPS_SET</c> with index × 4096 moved <c>A:FLAPS HANDLE INDEX</c> 0 → 1 → 0
/// (four detents past up, <c>A:FLAPS NUM HANDLE POSITIONS</c> 4).</item>
/// <item>Ground spoilers: <c>K:SPOILERS_ARM_SET</c> 1/0 moved <c>L:INI_SPOILERS_ARMED</c>; the stock
/// <c>A:SPOILERS ARMED</c> stays 0, so the L:var is the one to read.</item>
/// <item>Speed brake: <c>K:SPOILERS_SET</c> 8192 put <c>L:INI_SPOILERS_HANDLE_POSITION</c> at 0.5.</item>
/// </list>
/// Pure: the definition sends the strings these build.
/// </summary>
public static class A300Levers
{
    public const string FlapsKey = "A300_FLAPS_LEVER";
    public const string SpoilersArmKey = "A300_SPOILERS_ARM";
    public const string SpeedBrakeKey = "A300_SPEEDBRAKE_LEVER";

    /// <summary>The panel the three levers are placed on, the fleet's "flaps and speed brake" pedestal panel.</summary>
    public const string Panel = "Flaps and Speed Brake";

    /// <summary>Flap handle detents, slats/flaps as the A300 marks them.</summary>
    public static readonly IReadOnlyDictionary<double, string> FlapPositions = new Dictionary<double, string>
    {
        [0] = "Up", [1] = "15/0", [2] = "15/15", [3] = "15/20", [4] = "30/40",
    };

    public static readonly IReadOnlyDictionary<double, string> ArmPositions = new Dictionary<double, string>
    {
        [0] = "Disarmed", [1] = "Armed",
    };

    public static string Name(string key) => key switch
    {
        FlapsKey => "Flaps lever",
        SpoilersArmKey => "Ground spoilers",
        SpeedBrakeKey => "Speed brake lever",
        _ => key,
    };

    public static string FlapsRpn(int index) =>
        $"{Math.Clamp(index, 0, 4) * 4096} (>K:FLAPS_SET)";

    public static string SpoilersArmRpn(double value) =>
        $"{(value >= 0.5 ? 1 : 0)} (>K:SPOILERS_ARM_SET)";

    /// <summary><paramref name="fraction"/> is the handle as <c>L:INI_SPOILERS_HANDLE_POSITION</c> reads it, 0 to 1.</summary>
    public static string SpeedBrakeRpn(double fraction) =>
        $"{Math.Round(Math.Clamp(fraction, 0, 1) * 16384).ToString("0", CultureInfo.InvariantCulture)} (>K:SPOILERS_SET)";
}
