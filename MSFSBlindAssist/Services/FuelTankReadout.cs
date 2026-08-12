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
    /// Builds one list row: <c>"Centre  12,345 lb (5,600 kg)"</c>, or for a symmetric pair
    /// <c>"Outer  left 8,818 lb (4,000 kg), right 8,818 lb (4,000 kg)"</c>.
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
    /// </summary>
    public static IReadOnlyList<string> BuildLines(IReadOnlyList<FuelTankReading> readings)
    {
        var lines = readings.Select(FormatRow).ToList();
        double totalLbs = readings.Sum(r => r.Values.Sum(v => v.Lbs));
        lines.Add($"Total  {Both(totalLbs)}");
        return lines;
    }

    /// <summary>"12,345 lb (5,600 kg)" — both units, rounded to the pound/kilogram.</summary>
    private static string Both(double lbs)
    {
        long roundedLbs = (long)Math.Round(lbs);
        long roundedKg = (long)Math.Round(lbs * LbsToKg);
        return $"{roundedLbs.ToString("N0", CultureInfo.InvariantCulture)} lb "
             + $"({roundedKg.ToString("N0", CultureInfo.InvariantCulture)} kg)";
    }

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
