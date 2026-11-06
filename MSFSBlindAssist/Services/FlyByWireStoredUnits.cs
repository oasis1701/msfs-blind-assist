namespace MSFSBlindAssist.Services;

/// <summary>
/// Converts an FBW L:var from the unit the sim STORES it in back to the unit FBW's
/// script wrote it in. A script write such as SetSimVarValue("L:X", "knots", 250) is
/// stored in the base unit (m/s), and the app reads an on-request L:var as "number"
/// whatever its registered Units (SimConnectManager registers individual L:var defs
/// as "number"), so it gets the stored value. Read live on the A380, MSFS 2020:
/// cross-track 264.51 stored = 0.1428 NM (2026-10-09); preselected speed -1 kt
/// stored as -0.5144 (2026-11-04).
/// </summary>
internal static class FlyByWireStoredUnits
{
    public const double MetresPerNauticalMile = 1852.0;
    public const double MetresPerFoot = 0.3048;
    public const double MetresPerSecondPerKnot = MetresPerNauticalMile / 3600.0;

    public static double NauticalMiles(double storedMetres) => storedMetres / MetresPerNauticalMile;
    public static double Feet(double storedMetres) => storedMetres / MetresPerFoot;
    public static double Knots(double storedMetresPerSecond) => storedMetresPerSecond / MetresPerSecondPerKnot;
}
