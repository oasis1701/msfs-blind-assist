using System;
using System.Globalization;
using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

/// <summary>
/// Pure composition helpers for the iFly 737 MAX8 First Officer state evaluator
/// (a later task). Composes pressurization altitude LED windows from their five
/// digit cells, reads the center fuel tank quantity in pounds, and tests the
/// "is this lamp lit" encoding used by the aircraft's 0-5 switch+light composite
/// fields. No instance state — everything here is a pure function of a captured
/// <see cref="IFlySdkSnapshot"/> (or a raw value for <see cref="Lit"/>).
/// </summary>
public static class IFly737FoComposition
{
    /// <summary>Metric (kg) to U.S. (lb) conversion factor used by the SDK's
    /// per-tank UNITstyle flag (0:Metric, 1:U.S.System — see IFlySdkOffsets.UNITstyle).</summary>
    public const double KgToLb = 2.20462;

    /// <summary>
    /// Composes a five-digit LED altitude window (e.g. the Flight or Landing
    /// Altitude Indicator) into feet. Each offset is read as a single digit
    /// cell (0-9); any cell greater than 9 means the window is blanked/unpowered
    /// (the generated header documents 10:'-' and/or 11:blank depending on the
    /// field) and the whole reading is indeterminate.
    /// </summary>
    public static double ComposeAltWindow(
        IFlySdkSnapshot snap,
        int tenThousandsOffset,
        int thousandsOffset,
        int hundredsOffset,
        int tensOffset,
        int onesOffset)
    {
        byte tenThousands = snap.ByteAt(tenThousandsOffset);
        byte thousands = snap.ByteAt(thousandsOffset);
        byte hundreds = snap.ByteAt(hundredsOffset);
        byte tens = snap.ByteAt(tensOffset);
        byte ones = snap.ByteAt(onesOffset);

        if (tenThousands > 9 || thousands > 9 || hundreds > 9 || tens > 9 || ones > 9)
            return double.NaN;

        return 10000 * tenThousands + 1000 * thousands + 100 * hundreds + 10 * tens + ones;
    }

    /// <summary>
    /// Reads the center tank (index 2) fuel quantity gauge and returns pounds.
    /// The gauge is a per-digit text display (<see cref="IFlySdkSnapshot.FuelQuantityText"/>);
    /// when blanked/unpowered it renders as an empty (or otherwise non-numeric)
    /// string, which parses to NaN rather than a fabricated zero — a zero would
    /// read as "center tank empty" and could switch fuel pumps off. When the
    /// aircraft's UNITstyle is Metric (0) the gauge reads kilograms and is
    /// converted via <see cref="KgToLb"/>; U.S. (1) already reads pounds.
    /// </summary>
    public static double CenterQuantityLbs(IFlySdkSnapshot snap)
    {
        string text = snap.FuelQuantityText(2).Trim();
        if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            return double.NaN;

        return snap.ByteAt(IFlySdkOffsets.UNITstyle) == 0 ? value * KgToLb : value;
    }

    /// <summary>
    /// Tests the "is this lamp lit" encoding used by the aircraft's 0-5
    /// switch+light composite fields: the raw value's rounded integer, taken
    /// mod 3, is nonzero when lit. NaN (indeterminate — not yet read, or a
    /// blanked source) is never lit.
    /// </summary>
    public static bool Lit(double raw) => !double.IsNaN(raw) && ((int)Math.Round(raw)) % 3 > 0;
}
