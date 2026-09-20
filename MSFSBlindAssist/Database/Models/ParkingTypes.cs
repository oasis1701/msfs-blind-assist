namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Names for the navdata parking-type integers, so no caller spells the sets out again. The
/// numbering is navdata's (LittleNavMapProvider.MapParkingType); GSX stands arrive already
/// translated to it (GsxGateMapper, which exists because 14 and 15 are SWAPPED between the two).
/// </summary>
public static class ParkingTypes
{
    public static bool IsGate(int type) => type is 9 or 10 or 11 or 13 or 14;
    public static bool IsGaRamp(int type) => type is 2 or 3 or 4 or 5 or 15;
    public static bool IsCargo(int type) => type is 6 or 7;
    public static bool IsDock(int type) => type == 12;
    public static bool IsFuel(int type) => type == 16;
    public static bool IsVehicle(int type) => type == 17;
}
