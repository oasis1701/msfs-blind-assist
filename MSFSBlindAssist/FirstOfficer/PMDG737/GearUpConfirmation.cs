using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// "Landing gear: UP", confirmed the way a crew confirms it after takeoff — gear up,
/// lights out — never from the lever alone (owner decision 2026-09-22). Pure logic;
/// <see cref="AircraftStateEvaluator"/> feeds it the live CDA fields and publishes the
/// verdict as the synthetic state field <see cref="Field"/>.
/// </summary>
public static class GearUpConfirmation
{
    /// <summary>Synthetic state field: 1 = gear confirmed up, 0 = not, NaN = no data yet.</summary>
    public const string Field = "FO_GEAR_UP";

    /// <summary>PMDGNG3DataStruct lever field: 0 UP, 1 OFF, 2 DOWN.</summary>
    public const string LeverField = "MAIN_GearLever";

    /// <summary>Every gear indication light: the main-panel green DOWN-AND-LOCKED and red
    /// IN-TRANSIT lights for each gear (left, nose, right), and the aft-overhead greens.</summary>
    public static IReadOnlyList<string> LightFields { get; } = new[]
    {
        "MAIN_annunGEAR_locked_0", "MAIN_annunGEAR_locked_1", "MAIN_annunGEAR_locked_2",
        "MAIN_annunGEAR_transit_0", "MAIN_annunGEAR_transit_1", "MAIN_annunGEAR_transit_2",
        "GEAR_annunOvhdLEFT", "GEAR_annunOvhdNOSE", "GEAR_annunOvhdRIGHT",
    };

    /// <summary>
    /// True when the lever is UP or OFF (not DOWN) AND every gear light is out.
    /// The lever half stops a cold-and-dark aircraft — every light dark for want of power —
    /// reading "gear up" on the ground. The lights half stops a lever already at UP reading
    /// "gear up" while the gear is still travelling or has hung. A light TEST lights all of
    /// them and so reads "not up" — the safe direction. A NaN lever reads "not up".
    /// </summary>
    public static bool IsConfirmedUp(double lever, IEnumerable<bool> lightsOn)
        => lever < 1.5 && !lightsOn.Any(on => on);
}
