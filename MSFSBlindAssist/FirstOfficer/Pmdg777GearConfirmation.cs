using System;
using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The PMDG 777's landing gear confirmed from where the gear physically IS, not the lever
/// alone — the 777 counterpart of the 737's "gear up, lights out" / "three green"
/// (<see cref="MSFSBlindAssist.FirstOfficer.PMDG737.GearConfirmation"/>). The 777 SDK exposes
/// no gear-indication lights, so the physical position comes from the stock
/// <c>GEAR LEFT/CENTER/RIGHT POSITION</c> SimVars (percent), pushed into
/// <see cref="AircraftStateEvaluator"/> by the First Officer's 1 Hz timer
/// (<c>SimConnectManager.RequestFOGearPositions</c>) and served as <see cref="PositionFields"/>.
/// The evaluator publishes the verdicts as the synthetic fields <see cref="UpField"/> and
/// <see cref="DownField"/>. Pure logic; the rule itself is the shared <see cref="GearLightRules"/>
/// with each leg standing in for its light.
///
/// Tolerances are the System Display Gear page's own (<c>PMDG777Definition.SystemDisplay</c>
/// <c>GearPos</c>: ≥ 99 % "down", ≤ 1 % "up"), so the checklist can never call gear "down"
/// that the Gear page calls "in transit", or the reverse. One percent absorbs float noise at the
/// end stops while still rejecting a leg that is visibly travelling.
/// </summary>
public static class Pmdg777GearConfirmation
{
    /// <summary>Synthetic state field: 1 = gear confirmed up, 0 = not, NaN = no data yet.</summary>
    public const string UpField = "FO_GEAR_UP";

    /// <summary>Synthetic state field: 1 = gear confirmed down, 0 = not, NaN = no data yet.</summary>
    public const string DownField = "FO_GEAR_DOWN";

    /// <summary>PMDG 777 CDA gear lever: 0 Up, 1 Down.</summary>
    public const string LeverField = "GEAR_Lever";

    /// <summary>Stock gear-leg positions (percent extended: 0 retracted, 100 down): left main,
    /// nose, right main — synthetic fields the evaluator serves from its SimConnect pushes.</summary>
    public static IReadOnlyList<string> PositionFields { get; } = new[]
        { "FO_GEAR_LEFT_POS", "FO_GEAR_CENTER_POS", "FO_GEAR_RIGHT_POS" };

    /// <summary>A leg at or below this is fully retracted (System Display: "up").</summary>
    public const double RetractedMaxPercent = 1.0;

    /// <summary>A leg at or above this is fully extended (System Display: "down").</summary>
    public const double ExtendedMinPercent = 99.0;

    /// <summary>
    /// UP: the lever UP AND every leg fully retracted. The lever half stops a leg read of 0 on
    /// a lever just selected DOWN (gear not yet moving) reading "up"; the legs half stops a lever
    /// at UP reading "up" while the gear is still travelling or has hung. NaN when the lever or
    /// any leg has not been read yet — ChecklistManager then neither ticks nor reverts, and the
    /// After Takeoff flow's closing wait keeps waiting.
    /// </summary>
    public static double UpValue(Func<string, double> read)
    {
        double lever = read(LeverField);
        var legs = PositionFields.Select(read).ToList();
        return GearLightRules.AsField(legs.Prepend(lever),
            GearLightRules.IsUp(lever < 0.5, legs.Select(p => p > RetractedMaxPercent)));
    }

    /// <summary>
    /// DOWN: the lever DOWN AND every leg fully extended — the 777's "three green". NaN when the
    /// lever or any leg has not been read yet.
    /// </summary>
    public static double DownValue(Func<string, double> read)
    {
        double lever = read(LeverField);
        var legs = PositionFields.Select(read).ToList();
        return GearLightRules.AsField(legs.Prepend(lever),
            GearLightRules.IsDown(lever > 0.5, legs.Select(p => p >= ExtendedMinPercent),
                Enumerable.Empty<bool>()));
    }
}
