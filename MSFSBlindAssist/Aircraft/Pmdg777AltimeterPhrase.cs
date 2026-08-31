namespace MSFSBlindAssist.Aircraft;

using System.Globalization;

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

    /// <summary>How close to <see cref="StandardInHg"/> still counts as STD.</summary>
    public const double StandardToleranceInHg = 0.005;

    /// <summary>Inches of mercury to hectopascals.</summary>
    public const double InHgToHpa = 33.8639;

    /// <summary>The phrase to speak for an altimeter setting in inches of mercury.</summary>
    public static string Describe(double inHg)
    {
        if (System.Math.Abs(inHg - StandardInHg) < StandardToleranceInHg)
            return "Altimeter standard";

        string hpa = ((int)System.Math.Round(inHg * InHgToHpa)).ToString(CultureInfo.InvariantCulture);
        return $"QNH {hpa}, Altimeter {inHg.ToString("0.00", CultureInfo.InvariantCulture)}";
    }
}
