namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// The navdata parking-type families (LittleNavMapProvider.MapParkingType numbering; GSX stands
/// arrive translated by GsxGateMapper). Each type belongs to at most one family (pinned).
/// </summary>
public static class ParkingTypes
{
    public static bool IsGate(int type) => type is 9 or 10 or 11 or 13 or 14;
    public static bool IsGaRamp(int type) => type is 2 or 3 or 4 or 5 or 15;

    /// <summary>Civil cargo only (6). Counting military cargo made "Cargo ramp" of 605 military
    /// stands at 64 fs2024 airports.</summary>
    public static bool IsCargo(int type) => type == 6;

    /// <summary>7 RAMP_MIL_CARGO and 8 RAMP_MIL_COMBAT.</summary>
    public static bool IsMilitary(int type) => type is 7 or 8;

    public static bool IsDock(int type) => type == 12;
    public static bool IsFuel(int type) => type == 16;
    public static bool IsVehicle(int type) => type == 17;
}
