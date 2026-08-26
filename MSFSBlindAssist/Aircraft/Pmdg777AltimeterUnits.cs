namespace MSFSBlindAssist.Aircraft;

using System.Globalization;

/// <summary>
/// Phrases the PMDG 777 baro readout — the output mode + B hotkey and the background
/// announcement when the setting changes, which must never disagree.
///
/// <para>
/// Spoken the way it is read on the frequency: <c>QNH 1013</c> or <c>Altimeter 29.92</c>.
/// The label follows the unit because a hectopascal setting IS a QNH and an inches setting
/// IS an altimeter setting; no unit word and no colon, so the phrase matches the shape of
/// an ATIS or a METAR rather than sounding like a field being read out of a form.
/// </para>
/// <para>
/// One number, not two. The 777 SDK publishes the baro UNIT per side
/// (<c>EFIS_BaroSelHPA[2]</c>) but no per-side pressure — <c>EFIS_BaroKnob[2]</c> is a 0..99
/// detent counter, not a setting — and PMDG drives only one stock Kohlsman value
/// (measured in the sim, 2026-08-26: setting the two knobs apart still produced a single
/// number). So there is nothing to say about the two sides separately, and a readout that
/// named them would be inventing a distinction the aeroplane does not publish.
/// </para>
/// <para>
/// Both selectors are still consulted, because the UNITS genuinely are independent. Only an
/// agreeing, known pair picks a single unit; a split pair speaks both, since neither pilot
/// may be handed the other's unit. So does a pair that is not known yet: every PMDG field
/// reads 0.0 before the first CDA snapshot and 0 means INCHES, so a defaulted read would
/// announce an inches setting on an aeroplane set to hectopascals.
/// </para>
/// </summary>
public static class Pmdg777AltimeterUnits
{
    /// <summary>Standard pressure, inches of mercury.</summary>
    public const double StandardInHg = 29.92;

    /// <summary>How close to <see cref="StandardInHg"/> still counts as STD.</summary>
    public const double StandardToleranceInHg = 0.005;

    /// <summary>Inches of mercury to hectopascals.</summary>
    public const double InHgToHpa = 33.8639;

    /// <summary>
    /// The phrase to speak. <paramref name="captainHpa"/> / <paramref name="firstOfficerHpa"/>
    /// are each side's EFIS BARO selector (true = HPA, false = IN, null = not known yet).
    /// </summary>
    public static string Describe(double inHg, bool? captainHpa, bool? firstOfficerHpa)
    {
        if (System.Math.Abs(inHg - StandardInHg) < StandardToleranceInHg)
            return "Altimeter standard";

        string hpa = ((int)System.Math.Round(inHg * InHgToHpa)).ToString(CultureInfo.InvariantCulture);
        string inches = inHg.ToString("0.00", CultureInfo.InvariantCulture);

        if (captainHpa is bool c && firstOfficerHpa is bool f && c == f)
            return c ? $"QNH {hpa}" : $"Altimeter {inches}";

        return $"QNH {hpa}, Altimeter {inches}";
    }
}
