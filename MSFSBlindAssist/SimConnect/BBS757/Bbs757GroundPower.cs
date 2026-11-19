namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// BlueBird's ground power, as the 757 EFB code knows it, and what the definition's ground power row says when it changes. Pure.
/// BlueBird's Ground Services page has one GPU tile and it TOGGLES <see cref="ConnectedVar"/> (measured 2026-10-25, session 8,
/// and again 2026-10-26). Pressing the tile is the tablet press pipeline's like any other tile (plan 18): nothing here judges or
/// refuses it. Connecting only makes the ground power available: the EXT PWR button on the Electrical panel puts it on line.
/// </summary>
public static class Bbs757GroundPower
{
    /// <summary>BlueBird's ground power connection (0 not connected, 1 connected, measured): the GPU tile toggles it.</summary>
    public const string ConnectedVar = "BBS_757_GPU_AC_PWR_CONNECTED";

    /// <summary>
    /// From just before the window's taps, the longest a change of the ground power row counts as the window's
    /// own run: longer than any run can take, so it matters only for a run that never settles. A run that
    /// ends ends the echo at once (the definition's <c>SettleGroundPowerEcho</c>).
    /// </summary>
    public const int WindowRunMaxMs = 60_000;

    /// <summary>
    /// What the ground power row says when <see cref="ConnectedVar"/> changes to <paramref name="v"/>:
    /// "Ground power connected" or "Ground power disconnected"; null when it is not finite (NaN or infinity).
    /// </summary>
    public static string? ChangePhrase(double v) => Flag(v) switch
    {
        true => "Ground power connected",
        false => "Ground power disconnected",
        null => null,
    };

    /// <summary>A value as a flag: true for 1, false for 0, null when it was not read or is not finite.</summary>
    private static bool? Flag(double? value) => value is double v && double.IsFinite(v) ? v > 0.5 : null;
}
