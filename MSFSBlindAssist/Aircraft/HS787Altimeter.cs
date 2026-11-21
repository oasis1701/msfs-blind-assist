using System.Globalization;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The WT-B78x 787 Ctrl+B altimeter entry (HorizonSim 787-9, Kuro 787-8, stock Asobo 787-10).
///
/// <c>K:KOHLSMAN_SET</c> takes MILLIBARS x 16. The dialog used to send inches x 16
/// (1013 hPa = 29.91 inHg → 479 → 29.9 mb), which the sim clamps to its floor, so every entry
/// set 948 hPa / 27.99 inHg, and the STD button sent the same wrong number. Measured live
/// 2026-11-19: <c>KOHLSMAN_SET 16208</c> sets 1013 hPa, 16320 sets 1020.
///
/// The pilot may type either unit; the ranges cannot overlap:
///   hectopascals  900 to 1100     ("1013", "1013.2")
///   inches        26.00 to 32.00  ("29.92", "29,92")
///   inches x 100  2600 to 3200    ("2992", as it is often read off a METAR's A2992)
/// </summary>
public static class HS787Altimeter
{
    public const double InHgToHpa = 33.8639;

    /// <summary>The validation message for an entry <see cref="TryParseHpa"/> refuses.</summary>
    public const string RangeText = "hectopascals 900 to 1100, or inches 26.00 to 32.00";

    /// <summary>Parse an entry in either unit to hectopascals; false when it is in neither range.</summary>
    public static bool TryParseHpa(string? input, out double hpa)
    {
        hpa = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        string s = input.Trim().Replace(',', '.');
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return false;

        if (v is >= 900 and <= 1100) { hpa = v; return true; }
        if (v is >= 26.0 and <= 32.0) { hpa = v * InHgToHpa; return true; }
        if (v is >= 2600 and <= 3200 && !s.Contains('.')) { hpa = v / 100.0 * InHgToHpa; return true; }
        return false;
    }

    /// <summary>The <c>KOHLSMAN_SET</c> parameter, millibars x 16 rounded, for a pressure in hPa.</summary>
    public static uint KohlsmanSetParameter(double hpa) => (uint)Math.Round(hpa * 16.0, MidpointRounding.AwayFromZero);
}
