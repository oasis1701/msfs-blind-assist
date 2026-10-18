namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The altimeter setting as the B key and the altimeter callout speak it, for an aircraft that
/// reads its setting in inches: "Altimeter standard" for a reading within 0.005 inHg of 29.92,
/// otherwise both units, hPa first ("Altimeter: 1018, 30.05"). The numbers are formatted in the
/// invariant culture: in the PC's culture the inches read "30,05" under a German or Swedish
/// Windows. One rule for every aircraft: the PMDG 777 speaks it and the MD-11's inches readings
/// (<see cref="MD11.Md11Fcp"/>) use its standard check, factor and number format.
/// </summary>
internal static class AltimeterPhrase
{
    public const double StandardInHg = 29.92;

    /// <summary>hPa per inHg, so a 30.12 reads 1020 on every aircraft that uses this.</summary>
    public const double HpaPerInHg = 33.8639;

    public static bool IsStandardInHg(double inHg) => Math.Abs(inHg - StandardInHg) < 0.005;

    /// <summary>"1018, 30.05": hPa, then inches to two decimals, invariant culture.</summary>
    public static string BothUnits(int hpa, double inHg) => FormattableString.Invariant($"{hpa}, {inHg:0.00}");

    public static string Sentence(double inHg)
    {
        if (IsStandardInHg(inHg)) return "Altimeter standard";
        return $"Altimeter: {BothUnits((int)Math.Round(inHg * HpaPerInHg), inHg)}";
    }
}
