using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The words the PMDG 777 baro readout uses — shared by the output mode + B hotkey and the
/// background announcement that follows a change, which must never word the same fact
/// differently.
///
/// <para>
/// Both units are still spoken. That is the readout's real virtue: it states both numbers
/// and lets the pilot take the one they are working in, so it cannot be wrong. Only the
/// wording changes, from a field being read out of a form to the way the value is actually
/// said on the frequency — a hectopascal setting IS a QNH and an inches setting IS an
/// altimeter setting, so each number is labelled as what it is, with no colon:
/// </para>
/// <code>
/// was:  Altimeter: 1013, 29.92
/// now:  QNH 1013, Altimeter 29.92
/// </code>
/// <para>
/// No unit word is needed — 1013 and 29.92 cannot be mistaken for one another.
/// </para>
/// </summary>
public static class Pmdg777AltimeterPhrase
{
    /// <summary>Standard pressure, inches of mercury.</summary>
    public const double StandardInHg = 29.92;

    /// <summary>
    /// How close to <see cref="StandardInHg"/> still counts as STD.
    ///
    /// <para>
    /// Sized against hectopascals, not inches, because that is the tight side. The nearest
    /// inches setting a controller can issue is 0.0100 away, but a pilot working in
    /// hectopascals sets whole units, and <b>QNH 1013 is only 0.0061 away</b> (29.9139 inHg)
    /// while true standard, 1013.25 hPa, is 0.0012 away (29.9212 inHg). The band has to
    /// separate those two, and 0.005 did not do it with any margin -- it sat 1.2x below a
    /// real QNH 1013, so any rounding in the PMDG-to-SimConnect path would have announced a
    /// set pressure as "Altimeter standard": a number replaced by a state, which is the one
    /// kind of wrong a pilot cannot hear happening.
    /// </para>
    /// <para>
    /// 0.003 sits 2.4x above the true-standard case and 2.0x below QNH 1013. Do not widen it
    /// back toward 0.005, and do not narrow it below 0.0012 or genuine STD stops reading.
    /// </para>
    /// </summary>
    public const double StandardToleranceInHg = 0.003;

    /// <summary>Inches of mercury to hectopascals.</summary>
    public const double InHgToHpa = 33.8639;

    /// <summary>The phrase to speak for an altimeter setting in inches of mercury.</summary>
    public static string Describe(double inHg)
    {
        if (Math.Abs(inHg - StandardInHg) < StandardToleranceInHg)
            return "Altimeter standard";

        string hpa = ((int)Math.Round(inHg * InHgToHpa)).ToString(CultureInfo.InvariantCulture);
        return $"QNH {hpa}, Altimeter {inHg.ToString("0.00", CultureInfo.InvariantCulture)}";
    }
}
