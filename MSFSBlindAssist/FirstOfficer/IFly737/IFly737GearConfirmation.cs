using System;
using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

/// <summary>
/// The landing gear confirmed the way a crew confirms it — never from the lever alone (owner
/// decisions 2026-09-22): UP is "gear up, lights out", DOWN is "three green". Pure logic;
/// <see cref="IFly737StateEvaluator"/> feeds it the live SDK fields and publishes the verdicts
/// as the synthetic state fields <see cref="UpField"/> and <see cref="DownField"/>.
///
/// A port of <see cref="MSFSBlindAssist.FirstOfficer.PMDG737.GearConfirmation"/> onto this
/// airframe's own SDK field names and its two-position lever (0 Up / 1 Down — no OFF detent,
/// see <c>AT_GEAR_OFF</c>/<c>ATKO_GEAR_OFF</c> in <see cref="IFly737FlowDefinitions"/> and
/// <see cref="IFly737ChecklistDefinitions"/>). The actual "up"/"down" tests live in the shared
/// <see cref="GearLightRules"/> so this profile and the PMDG 737's <c>GearConfirmation</c>
/// (and Fenix's <c>FenixGearConfirmation</c>) apply the identical rule; only which fields play
/// the lever/green/red roles differs per aircraft.
/// </summary>
public static class IFly737GearConfirmation
{
    /// <summary>Synthetic state field: 1 = gear confirmed up, 0 = not, NaN = no data yet.</summary>
    public const string UpField = "FO_GEAR_UP";

    /// <summary>Synthetic state field: 1 = gear confirmed down, 0 = not, NaN = no data yet.</summary>
    public const string DownField = "FO_GEAR_DOWN";

    /// <summary>SDK gear lever field: 0 Up, 1 Down — this airframe has no OFF detent
    /// (RegisterLandingGear, Aircraft/IFly737MAXDefinition.ForwardPedestal.cs).</summary>
    public const string LeverField = "Gear_Lever_Status";

    /// <summary>Main-panel green DOWN-AND-LOCKED light, one per gear (left, nose, right).</summary>
    public static IReadOnlyList<string> GreenFields { get; } = new[]
        { "LEFT_GEAR_GreenLight_Status", "NOSE_GEAR_GreenLight_Status", "RIGHT_GEAR_GreenLight_Status" };

    /// <summary>Main-panel red IN-TRANSIT / DISAGREE light, one per gear.</summary>
    public static IReadOnlyList<string> RedFields { get; } = new[]
        { "LEFT_GEAR_RedLight_Status", "NOSE_GEAR_RedLight_Status", "RIGHT_GEAR_RedLight_Status" };

    /// <summary>Aft-overhead green gear lights (the alternate DOWN-AND-LOCKED indication).</summary>
    public static IReadOnlyList<string> OverheadGreenFields { get; } = new[]
        { "SYS2_LEFT_GEAR_GreenLight_Status", "SYS2_NOSE_GEAR_GreenLight_Status", "SYS2_RIGHT_GEAR_GreenLight_Status" };

    /// <summary>Every gear indication light: greens, reds, then the overhead greens.</summary>
    public static IReadOnlyList<string> AllLightFields { get; } =
        GreenFields.Concat(RedFields).Concat(OverheadGreenFields).ToArray();

    /// <summary>SDK light bytes are 0 OFF, 1 ON (DIM), 2 ON (BRT): dim counts as on.</summary>
    private static bool On(double v) => v > 0.5;

    /// <summary>
    /// The UP verdict composed from a live field reader — exactly what
    /// <see cref="IFly737StateEvaluator"/> publishes as <see cref="UpField"/>: the lever from
    /// <see cref="LeverField"/> (below the Up/Down midpoint = Up) and every field in
    /// <see cref="AllLightFields"/>. NaN (via <see cref="GearLightRules.AsField"/>) when the
    /// lever or any light hasn't been read yet — ChecklistManager then neither ticks nor
    /// reverts, and the After Takeoff flow's closing wait keeps waiting.
    /// </summary>
    public static double UpValue(Func<string, double> read)
    {
        double lever = read(LeverField);
        var lights = AllLightFields.Select(read).ToList();
        return GearLightRules.AsField(lights.Prepend(lever),
            GearLightRules.IsUp(lever < 0.5, lights.Select(On)));
    }

    /// <summary>
    /// The DOWN verdict composed from a live field reader — exactly what
    /// <see cref="IFly737StateEvaluator"/> publishes as <see cref="DownField"/>: the lever from
    /// <see cref="LeverField"/> (above the Up/Down midpoint = Down), every
    /// <see cref="GreenFields"/> on and no <see cref="RedFields"/> on. The overhead greens are
    /// deliberately not read here — see <see cref="GearLightRules.IsDown"/>. NaN when the lever
    /// or any green/red hasn't been read yet.
    /// </summary>
    public static double DownValue(Func<string, double> read)
    {
        double lever = read(LeverField);
        var greens = GreenFields.Select(read).ToList();
        var reds = RedFields.Select(read).ToList();
        return GearLightRules.AsField(greens.Concat(reds).Prepend(lever),
            GearLightRules.IsDown(lever > 0.5, greens.Select(On), reds.Select(On)));
    }
}
