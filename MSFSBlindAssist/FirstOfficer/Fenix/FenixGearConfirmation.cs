using System;
using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// The Fenix A320's landing gear confirmed the way a crew confirms it — never from the
/// lever alone (owner decision 2026-09-22): UP is "gear up, lights out". Pure logic;
/// <see cref="FenixStateEvaluator"/> feeds it the live L:var cache and publishes the
/// verdict as the synthetic state field <see cref="UpField"/>.
///
/// NO down rule, and deliberately so. The seven LDG GEAR indicator L:vars in
/// <see cref="LightFields"/> — the three wheels' upper/lower legends plus the lever's own
/// red arrow — are plain off/on: unlike the PMDG 737's separate green DOWN-AND-LOCKED and
/// red IN-TRANSIT fields, nothing here distinguishes a green triangle from a red bar, so
/// "three green, no red" (<see cref="GearLightRules.IsDown"/>) cannot be composed from
/// them. And the Fenix First Officer profile has no Landing flow, so its checklist's
/// "Landing gear: DOWN" line (<c>LDC_GEAR</c>) is never latched by a flow finishing — it
/// stays the plain lever mirror it always was, with nothing here to change.
/// </summary>
public static class FenixGearConfirmation
{
    /// <summary>Synthetic state field: 1 = gear confirmed up, 0 = not, NaN = no data yet.</summary>
    public const string UpField = "FO_GEAR_UP";

    /// <summary>Gear lever L:var: 0 UP, 1 DOWN.</summary>
    public const string LeverField = "S_MIP_GEAR";

    /// <summary>The seven LDG GEAR indicator lights: each wheel's upper/lower legend plus
    /// the main-panel lever's own red arrow. Plain off/on — none of them says green or red.</summary>
    public static IReadOnlyList<string> LightFields { get; } = new[]
    {
        "I_MIP_GEAR_1_U", "I_MIP_GEAR_1_L",
        "I_MIP_GEAR_2_U", "I_MIP_GEAR_2_L",
        "I_MIP_GEAR_3_U", "I_MIP_GEAR_3_L",
        "I_MIP_GEAR_RED",
    };

    /// <summary>
    /// UP — "gear up, lights out": the lever reads UP (0) AND every LDG GEAR indicator
    /// light is out. NaN (via <see cref="GearLightRules.AsField"/>) whenever the lever or
    /// any light is not yet cached, so an unwritten field can never read "confirmed up".
    /// </summary>
    public static double UpValue(Func<string, double> read)
    {
        double lever = read(LeverField);
        List<double> lights = LightFields.Select(read).ToList();
        return GearLightRules.AsField(
            lights.Prepend(lever),
            GearLightRules.IsUp(lever < 0.5, lights.Select(v => v > 0.5)));
    }
}
