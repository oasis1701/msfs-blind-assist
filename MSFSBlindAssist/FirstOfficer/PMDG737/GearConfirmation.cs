using System;
using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

/// <summary>
/// The landing gear confirmed the way a crew confirms it — never from the lever alone
/// (owner decisions 2026-09-22): UP is "gear up, lights out", DOWN is "three green".
/// Pure logic; <see cref="AircraftStateEvaluator"/> feeds it the live CDA fields and
/// publishes the verdicts as the synthetic state fields <see cref="UpField"/> and
/// <see cref="DownField"/>.
/// </summary>
public static class GearConfirmation
{
    /// <summary>Synthetic state field: 1 = gear confirmed up, 0 = not, NaN = no data yet.</summary>
    public const string UpField = "FO_GEAR_UP";

    /// <summary>Synthetic state field: 1 = gear confirmed down, 0 = not, NaN = no data yet.</summary>
    public const string DownField = "FO_GEAR_DOWN";

    /// <summary>PMDGNG3DataStruct lever field: 0 UP, 1 OFF, 2 DOWN.</summary>
    public const string LeverField = "MAIN_GearLever";

    /// <summary>Main-panel green DOWN-AND-LOCKED light, one per gear (left, nose, right).</summary>
    public static IReadOnlyList<string> GreenFields { get; } = new[]
        { "MAIN_annunGEAR_locked_0", "MAIN_annunGEAR_locked_1", "MAIN_annunGEAR_locked_2" };

    /// <summary>Main-panel red IN-TRANSIT / DISAGREE light, one per gear.</summary>
    public static IReadOnlyList<string> RedFields { get; } = new[]
        { "MAIN_annunGEAR_transit_0", "MAIN_annunGEAR_transit_1", "MAIN_annunGEAR_transit_2" };

    /// <summary>Aft-overhead green gear lights (the alternate DOWN-AND-LOCKED indication).</summary>
    public static IReadOnlyList<string> OverheadGreenFields { get; } = new[]
        { "GEAR_annunOvhdLEFT", "GEAR_annunOvhdNOSE", "GEAR_annunOvhdRIGHT" };

    /// <summary>Every gear indication light: greens, reds, then the overhead greens.</summary>
    public static IReadOnlyList<string> AllLightFields { get; } =
        GreenFields.Concat(RedFields).Concat(OverheadGreenFields).ToArray();

    /// <summary>
    /// UP — "gear up, lights out": the lever is UP or OFF (not DOWN) AND every gear light
    /// is out. The lever half stops a cold-and-dark aircraft — every light dark for want of
    /// power — reading "gear up" on the ground; the lights half stops a lever already at UP
    /// reading "gear up" while the gear is still travelling or has hung. A light TEST lights
    /// all of them and so reads "not up" — the safe direction. A NaN lever reads "not up".
    /// </summary>
    public static bool IsConfirmedUp(double lever, IEnumerable<bool> allLightsOn)
        => lever < 1.5 && !allLightsOn.Any(on => on);

    /// <summary>
    /// The UP verdict composed from a CDA field reader — exactly what
    /// <see cref="AircraftStateEvaluator"/> publishes as <see cref="UpField"/>, kept here so the
    /// wiring (which field plays which role) is testable: the lever from
    /// <see cref="LeverField"/>, and a light counts as on when its field reads above 0.5.
    /// </summary>
    public static bool IsConfirmedUp(Func<string, double> readField)
        => IsConfirmedUp(readField(LeverField), AllLightFields.Select(f => readField(f) > 0.5));

    /// <summary>
    /// DOWN — "three green": the lever is DOWN AND every main-panel green DOWN-AND-LOCKED
    /// light is on AND no red is on. A red means a gear is in transit or disagrees with the
    /// lever, and a light TEST lights the reds too, so neither can read as "down". The
    /// overhead greens are the alternate indication and are deliberately not required, so
    /// they can never hold a real "down" hostage. A NaN lever, or no greens supplied,
    /// reads "not down".
    /// </summary>
    public static bool IsConfirmedDown(double lever, IEnumerable<bool> greensOn, IEnumerable<bool> redsOn)
    {
        var greens = greensOn.ToList();
        return lever > 1.5 && greens.Count > 0 && greens.All(on => on) && !redsOn.Any(on => on);
    }

    /// <summary>The DOWN verdict composed from a CDA field reader — see the UP overload.</summary>
    public static bool IsConfirmedDown(Func<string, double> readField)
        => IsConfirmedDown(readField(LeverField),
               GreenFields.Select(f => readField(f) > 0.5),
               RedFields.Select(f => readField(f) > 0.5));
}
