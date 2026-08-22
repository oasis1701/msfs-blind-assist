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
    /// Altitude Indicator) into feet, cell order 10000s..1s (left to right on
    /// the physical display). Each cell reads 0-9 (a digit), 10 ('-', a minus
    /// sign) or 11 (blank) per the generated header.
    ///
    /// The window is a right-aligned five-cell LED counter, so a value that
    /// doesn't need all five digits (e.g. a 450 ft LAND ALT, or any value
    /// under 10,000 ft) is padded on the LEFT with blanks, and a negative
    /// value (e.g. a below-sea-level LAND ALT such as Schiphol's -11 ft) is
    /// padded with blanks then a single leading minus. Composition rule:
    ///   - Scan cells left to right. Leading cells may be blank (11); the one
    ///     cell immediately before the first digit may instead be a minus (10).
    ///   - Once the first digit (0-9) appears, every remaining cell to the
    ///     right MUST be a digit — a blank or minus at or after that point
    ///     means the window is malformed and the whole reading is NaN.
    ///   - All five cells blank -> NaN. An unpowered/blank display must never
    ///     read as 0 feet (the safety-critical direction: a blank window must
    ///     never be treated as "reached the target altitude").
    ///   - A leading minus negates the composed magnitude.
    /// </summary>
    public static double ComposeAltWindow(
        IFlySdkSnapshot snap,
        int tenThousandsOffset,
        int thousandsOffset,
        int hundredsOffset,
        int tensOffset,
        int onesOffset)
    {
        Span<byte> cells = stackalloc byte[5]
        {
            snap.ByteAt(tenThousandsOffset),
            snap.ByteAt(thousandsOffset),
            snap.ByteAt(hundredsOffset),
            snap.ByteAt(tensOffset),
            snap.ByteAt(onesOffset),
        };

        bool negative = false;
        bool sawMinus = false;
        bool sawDigit = false;
        long magnitude = 0;

        for (int i = 0; i < cells.Length; i++)
        {
            byte cell = cells[i];
            if (cell <= 9)
            {
                sawDigit = true;
                sawMinus = false; // consumed
                magnitude = magnitude * 10 + cell;
            }
            else if (cell == 10) // minus sign
            {
                // Only valid as the single cell immediately before the first digit — a digit
                // already seen, or a minus not immediately followed by a digit, is malformed.
                if (sawDigit || sawMinus) return double.NaN;
                negative = true;
                sawMinus = true;
            }
            else if (cell == 11) // blank
            {
                // Only valid before the first digit, and never right after a minus (a minus
                // must sit immediately before the first digit) — either is malformed.
                if (sawDigit || sawMinus) return double.NaN;
            }
            else
            {
                return double.NaN; // unrecognized cell value
            }
        }

        if (sawMinus) return double.NaN; // trailing minus with no digit after it — malformed

        if (!sawDigit) return double.NaN; // all blank (and/or a lone minus) — nothing to read

        return negative ? -magnitude : magnitude;
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
