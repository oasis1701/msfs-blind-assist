using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure formatting for the per-tank fuel readout (the Fuel Tanks window, output Alt+U).
/// Kept side-effect free so the wording is covered by characterization tests.
/// </summary>
public static class FuelTankReadout
{
    // Same lbs→kg factor the PMDG per-tank kg readout uses (PMDG737/777Definition).
    private const double LbsToKg = 0.453592;

    /// <summary>
    /// Builds one list row: <c>"Center  12,345 pounds (5,600 kilograms)"</c>, or for a
    /// symmetric pair <c>"Outer tanks  left 8,818 pounds (4,000 kilograms), right …"</c>.
    ///
    /// The units are SPELLED OUT because this line is read aloud: a screen reader says
    /// "ell bee" for lb and "kay gee" for kg. Same rule the SayIntentions altimeter line
    /// follows ("inches", never "inHg"), and the one the existing fuel readouts already
    /// keep ("Fuel on board 12724 kilograms").
    ///
    /// BOTH units on every row, pounds first with kilograms in brackets. The app has users
    /// on each, and this is a lookup surface rather than a spoken sentence — a unit toggle
    /// would mean opening the window, discovering it is in the wrong unit and going to
    /// settings, where showing both costs a few characters the reader passes over.
    ///
    /// The LABEL LEADS THE LINE and that is load-bearing, not cosmetic: <c>DisplayListBox</c>
    /// deliberately leaves the native ListBox incremental type-ahead on, so typing "c" (or
    /// "ce" quickly) jumps to Centre. Put a number or a unit first and that navigation —
    /// the whole reason a window can replace nine hotkey chords — stops working.
    ///
    /// Thousands separators are InvariantCulture so the readout does not change shape with
    /// the machine's locale (matching the aviation-number rule used elsewhere); they earn
    /// their place in braille, where an undelimited 123456 has to be counted by hand.
    /// </summary>
    public static string FormatRow(FuelTankReading reading)
    {
        if (reading.Values.Count == 1)
            return $"{reading.Label}  {Both(reading.Values[0].Lbs)}";

        var parts = reading.Values.Select(v => $"{v.Side} {Both(v.Lbs)}");
        return $"{reading.Label}  {string.Join(", ", parts)}";
    }

    /// <summary>
    /// The whole window body: every row plus a Total. Total is the sum of the rows as
    /// resolved, NOT a separate simvar read — so it can never disagree with the numbers
    /// printed above it (a total that does not match the visible rows reads as a bug even
    /// when both values are individually right).
    ///
    /// It sums the ROUNDED figures each row actually prints, in each unit separately.
    /// Summing raw pounds and converting once instead let the Total's kilograms miss the
    /// sum of the printed kilograms (the A380's own live capture is off by 1), and the
    /// pounds too whenever the sim reports fractional pounds — which in flight it always
    /// does. A pilot adding the rows up must land exactly on the Total.
    /// </summary>
    public static IReadOnlyList<string> BuildLines(IReadOnlyList<FuelTankReading> readings)
    {
        var lines = readings.Select(FormatRow).ToList();
        long totalLbs = readings.Sum(r => r.Values.Sum(v => RoundLbs(v.Lbs)));
        long totalKg = readings.Sum(r => r.Values.Sum(v => RoundKg(v.Lbs)));
        lines.Add($"Total  {Format(totalLbs, totalKg)}");
        return lines;
    }

    /// <summary>"12,345 pounds (5,600 kilograms)" — both units, rounded to the whole unit.</summary>
    private static string Both(double lbs) => Format(RoundLbs(lbs), RoundKg(lbs));

    private static long RoundLbs(double lbs) => (long)Math.Round(lbs);

    private static long RoundKg(double lbs) => (long)Math.Round(lbs * LbsToKg);

    private static string Format(long lbs, long kg)
        => $"{lbs.ToString("N0", CultureInfo.InvariantCulture)} pounds "
         + $"({kg.ToString("N0", CultureInfo.InvariantCulture)} kilograms)";

    /// <summary>
    /// Resolves a static slot table against the 16-wide FUELSYSTEM tank-weight array
    /// (pounds, 0-based index = tank index - 1) into rows the window can print.
    /// </summary>
    public static IReadOnlyList<FuelTankReading> Resolve(
        IReadOnlyList<FuelTankSlot> slots, double[] weightsLbs)
        => slots.Select(s => new FuelTankReading(
                s.Label,
                s.Tanks.Select(t => (t.Side, Lbs(t.TankIndex, weightsLbs))).ToList()))
            .ToList();

    private static double Lbs(int tankIndex, double[] weightsLbs)
        => tankIndex >= 1 && tankIndex <= weightsLbs.Length ? weightsLbs[tankIndex - 1] : 0;
}
