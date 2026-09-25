using System;
using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>
/// The Fenix A320's landing gear confirmed the way a crew confirms it — never from the
/// lever alone (owner decisions 2026-09-22): UP is "gear up, lights out", DOWN is "three
/// green". Pure logic; <see cref="FenixStateEvaluator"/> feeds it the live L:var cache and
/// publishes the verdicts as the synthetic state fields <see cref="UpField"/> and
/// <see cref="DownField"/>.
///
/// Each of the three wheels carries TWO legend L:vars — <c>_U</c> ("Upper") and <c>_L</c>
/// ("Lower") — plus the lever's own red arrow <c>I_MIP_GEAR_RED</c>. Which legend is the green
/// DOWN-AND-LOCKED one was MEASURED live on 2026-09-25 (Fenix A320 CFM, parked at KDFW,
/// aircraft powered — main bus 28.5 V — lever <c>S_MIP_GEAR</c> = 1 DOWN, gear down and
/// locked on the ground):
/// <list type="bullet">
/// <item>Annunciator switch <c>S_OH_IN_LT_ANN_LT</c> = 1 (Bright): <c>I_MIP_GEAR_1_L</c>,
/// <c>_2_L</c>, <c>_3_L</c> = 1; <c>I_MIP_GEAR_1_U</c>, <c>_2_U</c>, <c>_3_U</c> = 0;
/// <c>I_MIP_GEAR_RED</c> = 0.</item>
/// <item>Annunciator light TEST (<c>S_OH_IN_LT_ANN_LT</c> = 2): all three <c>_U</c> = 1 and
/// <c>I_MIP_GEAR_RED</c> = 1; back to Bright returned the <c>_U</c> legends to 0. That proves
/// the <c>_U</c> vars are live lights, not a nonexistent L:var reading 0.</item>
/// <item>Annunciator switch DIM (<c>S_OH_IN_LT_ANN_LT</c> = 0): the <c>_L</c> legends still read 1 —
/// plain on/off, not brightness-scaled, so the <c>&gt; 0.5</c> test holds at either setting.</item>
/// </list>
/// So with the gear down and locked only the LOWER legend of each wheel is lit: <c>_L</c> is
/// the green (<see cref="GreenFields"/>). <c>_U</c> is the wheel's other legend — dark when
/// down-locked, lit only in the light test. Its colour in transit (red UNLK on the real jet)
/// was NOT observed, and nothing here depends on it: it and the arrow are simply lights that
/// must be OUT for "three green, no red" (<see cref="RedFields"/>). Because the light test
/// lights them too, a light test can never read as "down" — exactly
/// <see cref="GearLightRules.IsDown"/>'s contract.
///
/// This happens to match the real A320's LDG GEAR panel (red UNLK above, green triangle
/// below), but that is NOT why it is right: a Fenix legend's meaning must never be inferred
/// from its suffix, from the real aircraft, or from a sibling pushbutton — the APU START
/// pushbutton's <c>_U</c>/<c>_L</c> are REVERSED against the real jet (see
/// <c>FoPr160ProcedureFixTests</c>). It is right because it was read in the sim.
///
/// The Fenix First Officer profile has no Landing flow, so the Landing Checklist's "Landing
/// gear: DOWN" line (<c>LDC_GEAR</c>) is ticked only by its own state condition on
/// <see cref="DownField"/> — never latched by a flow finishing.
/// </summary>
public static class FenixGearConfirmation
{
    /// <summary>Synthetic state field: 1 = gear confirmed up, 0 = not, NaN = no data yet.</summary>
    public const string UpField = "FO_GEAR_UP";

    /// <summary>Synthetic state field: 1 = gear confirmed down (three green, no red), 0 = not,
    /// NaN = no data yet.</summary>
    public const string DownField = "FO_GEAR_DOWN";

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

    /// <summary>The green DOWN-AND-LOCKED legend of each wheel — the LOWER one, as measured
    /// 2026-09-25 (lit, and the only legend lit, with the gear down and locked).</summary>
    public static IReadOnlyList<string> GreenFields { get; } = new[]
        { "I_MIP_GEAR_1_L", "I_MIP_GEAR_2_L", "I_MIP_GEAR_3_L" };

    /// <summary>The lights that must be OUT for "three green": each wheel's UPPER legend
    /// (dark when down-locked, measured 2026-09-25) and the lever's red arrow. All four light
    /// in the annunciator test, so a test can never read "down".</summary>
    public static IReadOnlyList<string> RedFields { get; } = new[]
        { "I_MIP_GEAR_1_U", "I_MIP_GEAR_2_U", "I_MIP_GEAR_3_U", "I_MIP_GEAR_RED" };

    private static bool On(double v) => v > 0.5;

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
            GearLightRules.IsUp(lever < 0.5, lights.Select(On)));
    }

    /// <summary>
    /// DOWN — "three green": the lever reads DOWN (1) AND every <see cref="GreenFields"/>
    /// legend is lit AND none of <see cref="RedFields"/> is. NaN whenever the lever or any
    /// of those lights is not yet cached, so an unwritten field can never read "confirmed down".
    /// </summary>
    public static double DownValue(Func<string, double> read)
    {
        double lever = read(LeverField);
        List<double> greens = GreenFields.Select(read).ToList();
        List<double> reds = RedFields.Select(read).ToList();
        return GearLightRules.AsField(
            greens.Concat(reds).Prepend(lever),
            GearLightRules.IsDown(lever > 0.5, greens.Select(On), reds.Select(On)));
    }
}
