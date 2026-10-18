namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The PMDG 777 altimeter as the B key and the altimeter callout speak it: "Altimeter standard"
/// for a reading within 0.005 inHg of 29.92, otherwise both units, hPa first
/// ("Altimeter: 1018, 30.05"). The numbers are formatted in the invariant culture: in the PC's
/// culture the inches read "30,05" under a German or Swedish Windows.
/// </summary>
internal static class Pmdg777Altimeter
{
    public static string Sentence(double inHg)
    {
        if (Math.Abs(inHg - 29.92) < 0.005) return "Altimeter standard";
        int hpa = (int)Math.Round(inHg * 33.8639);
        return FormattableString.Invariant($"Altimeter: {hpa}, {inHg:0.00}");
    }
}
