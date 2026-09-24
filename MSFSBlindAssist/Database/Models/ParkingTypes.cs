namespace MSFSBlindAssist.Database.Models;

/// <summary>
/// Names for the navdata parking-type integers, so no caller spells the sets out again. The
/// numbering is navdata's (LittleNavMapProvider.MapParkingType); GSX stands arrive already
/// translated to it (GsxGateMapper, which exists because 14 and 15 are SWAPPED between the two).
/// Each type belongs to AT MOST ONE family (ParkingTypesTests.No_type_belongs_to_two_families).
/// </summary>
public static class ParkingTypes
{
    public static bool IsGate(int type) => type is 9 or 10 or 11 or 13 or 14;
    public static bool IsGaRamp(int type) => type is 2 or 3 or 4 or 5 or 15;

    /// <summary>CIVIL cargo only: 6, RAMP_CARGO. A military cargo stand is <see cref="IsMilitary"/>;
    /// counted here it made "Cargo ramp" features of 605 military stands at 64
    /// fs2024 airports (PHNL's Hickam ramp among them).</summary>
    public static bool IsCargo(int type) => type == 6;

    /// <summary>7 RAMP_MIL_CARGO and 8 RAMP_MIL_COMBAT — what the taxi form's filter and the TCAS
    /// label have always called "Military".</summary>
    public static bool IsMilitary(int type) => type is 7 or 8;

    public static bool IsDock(int type) => type == 12;
    public static bool IsFuel(int type) => type == 16;
    public static bool IsVehicle(int type) => type == 17;
}
