using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The COWS DA40 Series POH's own performance tables, transcribed.
///
/// ⚠️ THESE ARE CHARTS, WHICH IS EXACTLY WHY THEY BELONG HERE. A sighted pilot opens the
/// manual, finds their altitude row and reads across; a blind pilot could not open it at
/// all. MSFSBA already reads the live manifold pressure, RPM, fuel flow and load - the
/// numbers a cruise table is written IN - and had nothing to compare them against.
///
/// Two things live here, one per airframe, and they answer different questions:
///
///  • The XLS is a Lycoming with a mixture: the POH gives MANIFOLD PRESSURE against
///    pressure altitude for each power setting, plus the fuel flow for best economy and
///    best power. Section II, "Table to set engine performance", 45-55 % and 65-75 %.
///  • The NG is a FADEC diesel with one lever: there is no mixture to set, so the table is
///    a CHECK instead - the MINIMUM LOAD the engine must show at full power for the
///    conditions. The POH's own words: "The pilot must confirm proper engine operation
///    before departure by setting max power and comparing the load displayed to the tables
///    shown below." A blind pilot had no way to make that check.
///
/// ⚠️ THE SHADED "RECOMMENDED BANDS" ARE NOT TRANSCRIBED, AND THEY DO NOT NEED TO BE. The
/// POH shades some cells light blue to mark the recommended band for each RPM, and those
/// edges cannot be read off the page with enough confidence to state as fact - a wrong band
/// is worse than no band. But the aeroplane spells the same thing out in TEXT: the XLS's own
/// native checklist ("Weights/Speeds/Power table") lists, per power setting and per 1000 ft
/// from sea level to 17,000, the recommended RPM, manifold pressure and fuel flow - and
/// MSFSBA reads that file, so the pilot already has it. This class answers the other
/// question, which the checklist cannot: what the engine is making RIGHT NOW.
///
/// That checklist also CONFIRMS this transcription independently. Every cell the two
/// documents share agrees exactly - 45 % / 1800 at sea level 22.7 in, 55 % / 2000 at
/// 9,000 ft 21.1 in, 55 % / 2400 at 13,000 ft 17.6 in, 65 % / 2400 at 9,000 ft 20.7 in
/// and 9.8 gph, 75 % / 2400 at 5,000 ft 24.1 in - which is a stronger check on a figure
/// read off a scan than any amount of re-reading the same image.
///
/// ⚠️ ALTITUDE IS PRESSURE ALTITUDE and the temperature column of the XLS tables is ISA for
/// that altitude, not the OAT - the POH prints it only so the pilot can see how far from
/// standard they are. Its correction is stated separately and is applied by
/// <see cref="IsaCorrection"/>: at ISA + 15 °C the power actually delivered falls about
/// 3 % of the figure selected, and at ISA - 15 °C it rises about 3 %.
/// </summary>
public static class DA40PerformanceTables
{
    // ==================================================================================
    // DA40-NG — minimum load at full power
    // ==================================================================================

    /// <summary>The table's altitude rows, in feet.</summary>
    public static readonly int[] NgAltitudesFt = { 0, 2000, 4000, 6000, 8000, 10000 };

    /// <summary>The table's temperature columns, in celsius.</summary>
    public static readonly int[] NgTemperaturesC = { -35, -20, -10, 0, 10, 20, 30, 40, 50 };

    /// <summary>
    /// Minimum load percent by [altitude row][temperature column]. A null is a cell the
    /// POH leaves blank - the aeroplane is not expected to make book power there, and
    /// saying "no figure" is the honest answer rather than extrapolating one.
    /// </summary>
    private static readonly double?[][] NgMinimumLoad =
    {
        //            -35   -20   -10     0    10    20    30    40    50
        new double?[] { 94,   94,   94,   96,   96,   96,   95,   92,   90 },   //     0 ft
        new double?[] { 94,   94,   94,   96,   96,   96,   95,   92, null },   //  2000
        new double?[] { 96,   96,   96,   96,   96,   96,   95,   92, null },   //  4000
        new double?[] { 96,   96,   96,   96,   96,   96,   95,   92, null },   //  6000
        new double?[] { 96,   96,   96,   96,   96,   95,   94,   91, null },   //  8000
        new double?[] { 96,   96,   96,   94,   93,   91,   88, null, null }    // 10000
    };

    /// <summary>
    /// The load the NG must reach at full power for this altitude and outside air
    /// temperature, or null where the table gives no figure.
    ///
    /// Both axes are taken to the NEAREST tabulated value rather than interpolated: the
    /// table is a pass/fail check quoted in whole percent, and inventing a figure between
    /// two of its cells would present arithmetic as though it came from the manual.
    /// </summary>
    public static double? NgMinimumLoadPercent(double pressureAltitudeFt, double oatC)
    {
        int row = NearestIndex(NgAltitudesFt, pressureAltitudeFt);
        int col = NearestIndex(NgTemperaturesC, oatC);
        return NgMinimumLoad[row][col];
    }

    /// <summary>
    /// "Book minimum 95 percent at 4000 feet, 30 degrees" - or why there is no figure.
    /// Reports; it does not judge the engine, which is the pilot's call against the load
    /// they can already read.
    /// </summary>
    public static string DescribeNgMinimumLoad(double pressureAltitudeFt, double oatC)
    {
        int row = NearestIndex(NgAltitudesFt, pressureAltitudeFt);
        int col = NearestIndex(NgTemperaturesC, oatC);
        string at = $"{NgAltitudesFt[row]} feet, {NgTemperaturesC[col]} degrees";

        double? load = NgMinimumLoad[row][col];
        return load is null
            ? $"Book gives no minimum load at {at}"
            : $"Book minimum load {load.Value:F0} percent at {at}";
    }

    // ==================================================================================
    // DA40-XLS — manifold pressure and fuel flow
    // ==================================================================================

    /// <summary>One column of the XLS tables: a power setting at an RPM.</summary>
    public sealed record XlsCruiseSetting(
        int PowerPercent,
        int Rpm,
        double BestEconomyGph,
        double? BestPowerGph,
        IReadOnlyDictionary<int, double> ManifoldByAltitudeFt)
    {
        /// <summary>The manifold pressure for this setting at an altitude, or null above the table.</summary>
        public double? ManifoldAt(double pressureAltitudeFt)
        {
            if (ManifoldByAltitudeFt.Count == 0) return null;
            var alts = ManifoldByAltitudeFt.Keys.ToArray();
            int nearest = alts[NearestIndex(alts, pressureAltitudeFt)];

            // Above the last altitude the column publishes, the setting is simply not
            // available - the engine cannot make that power up there, which is what the
            // POH's blank cells mean. Below the first, the nearest row stands.
            return pressureAltitudeFt > alts.Max() + 500 ? null : ManifoldByAltitudeFt[nearest];
        }
    }

    private static IReadOnlyDictionary<int, double> Mp(params (int Alt, double InHg)[] rows)
        => rows.ToDictionary(r => r.Alt, r => r.InHg);

    /// <summary>
    /// Every column of both XLS tables, lowest power first. The 45 % and 55 % settings come
    /// from the [45 - 55] table and the 65 % and 75 % from [65 - 75].
    /// </summary>
    public static readonly IReadOnlyList<XlsCruiseSetting> XlsCruise = new[]
    {
        new XlsCruiseSetting(45, 1800, 5.8, null, Mp(
            (0, 22.7), (1000, 22.4), (2000, 22.1), (3000, 21.8), (4000, 21.5), (5000, 21.2),
            (6000, 20.9), (7000, 20.5), (8000, 20.2), (9000, 19.9), (10000, 19.6),
            (11000, 19.3))),

        new XlsCruiseSetting(45, 2000, 6.0, null, Mp(
            (0, 21.3), (1000, 21.0), (2000, 20.7), (3000, 20.4), (4000, 20.2), (5000, 19.9),
            (6000, 19.6), (7000, 19.3), (8000, 19.0), (9000, 18.7), (10000, 18.4),
            (11000, 18.2), (12000, 17.9), (13000, 17.6))),

        new XlsCruiseSetting(45, 2200, 6.3, 7.3, Mp(
            (0, 20.2), (1000, 19.9), (2000, 19.6), (3000, 19.3), (4000, 19.0), (5000, 18.7),
            (6000, 18.4), (7000, 18.2), (8000, 17.9), (9000, 17.6), (10000, 17.3),
            (11000, 17.0), (12000, 16.7), (13000, 16.4), (14000, 16.1), (15000, 15.8),
            (16000, 15.5))),

        new XlsCruiseSetting(45, 2400, 6.6, 7.7, Mp(
            (0, 19.0), (1000, 18.7), (2000, 18.4), (3000, 18.2), (4000, 17.9), (5000, 17.6),
            (6000, 17.4), (7000, 17.1), (8000, 16.9), (9000, 16.6), (10000, 16.3),
            (11000, 16.1), (12000, 15.8), (13000, 15.5), (14000, 15.3), (15000, 15.0),
            (16000, 14.7), (17000, 14.5))),

        new XlsCruiseSetting(55, 2000, 7.0, null, Mp(
            (0, 23.9), (1000, 23.6), (2000, 23.3), (3000, 23.0), (4000, 22.7), (5000, 22.3),
            (6000, 22.0), (7000, 21.7), (8000, 21.3), (9000, 21.1))),

        new XlsCruiseSetting(55, 2200, 7.2, 8.5, Mp(
            (0, 22.4), (1000, 22.2), (2000, 21.9), (3000, 21.6), (4000, 21.2), (5000, 20.9),
            (6000, 20.6), (7000, 20.3), (8000, 20.0), (9000, 19.7), (10000, 19.4),
            (11000, 19.1))),

        new XlsCruiseSetting(55, 2400, 7.5, 8.7, Mp(
            (0, 21.2), (1000, 21.0), (2000, 20.7), (3000, 20.4), (4000, 20.1), (5000, 19.8),
            (6000, 19.5), (7000, 19.3), (8000, 19.0), (9000, 18.7), (10000, 18.4),
            (11000, 18.1), (12000, 17.8), (13000, 17.6))),

        new XlsCruiseSetting(65, 2000, 7.9, null, Mp(
            (0, 26.8), (1000, 26.4), (2000, 26.0), (3000, 25.7), (4000, 25.4))),

        new XlsCruiseSetting(65, 2200, 8.2, 9.5, Mp(
            (0, 24.9), (1000, 24.5), (2000, 24.2), (3000, 23.8), (4000, 23.5), (5000, 23.1),
            (6000, 22.8), (7000, 22.4))),

        new XlsCruiseSetting(65, 2400, 8.5, 9.8, Mp(
            (0, 23.4), (1000, 23.2), (2000, 22.9), (3000, 22.6), (4000, 22.3), (5000, 22.0),
            (6000, 21.7), (7000, 21.4), (8000, 21.0), (9000, 20.7))),

        new XlsCruiseSetting(75, 2200, 9.2, 10.7, Mp(
            (0, 27.3), (1000, 26.8), (2000, 26.5), (3000, 26.1))),

        new XlsCruiseSetting(75, 2400, 9.5, 11.0, Mp(
            (0, 25.8), (1000, 25.5), (2000, 25.2), (3000, 24.8), (4000, 24.5), (5000, 24.1)))
    };

    /// <summary>
    /// What the book asks for at this power setting, RPM and altitude, or null when the
    /// table has no column for that pair.
    /// </summary>
    public static XlsCruiseSetting? XlsSetting(int powerPercent, int rpm)
        => XlsCruise.FirstOrDefault(s => s.PowerPercent == powerPercent && s.Rpm == rpm);

    /// <summary>
    /// ⚠️ READ THE TABLE BACKWARDS, WHICH IS WHAT A PILOT IN THE CRUISE ACTUALLY NEEDS.
    /// The chart is written to be entered with a power setting; airborne, the pilot has
    /// the RPM and manifold pressure in front of them and wants to know what they are
    /// MAKING. This takes the columns at the nearest tabulated RPM and names the power
    /// setting whose manifold pressure at this altitude is closest - reporting the figure,
    /// never recommending one.
    /// </summary>
    public static string DescribeXlsCruise(double pressureAltitudeFt, double rpm, double manifoldInHg)
    {
        if (rpm < 1200 || manifoldInHg <= 0) return "Cruise table needs the engine running";

        int[] rpms = XlsCruise.Select(s => s.Rpm).Distinct().OrderBy(r => r).ToArray();
        int nearestRpm = rpms[NearestIndex(rpms, rpm)];

        var candidates = XlsCruise
            .Where(s => s.Rpm == nearestRpm)
            .Select(s => (Setting: s, Mp: s.ManifoldAt(pressureAltitudeFt)))
            .Where(x => x.Mp is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            return $"Book has no {nearestRpm} RPM column at this altitude";
        }

        var best = candidates
            .OrderBy(x => Math.Abs(x.Mp!.Value - manifoldInHg))
            .First();

        string economy = best.Setting.BestEconomyGph.ToString("0.0", CultureInfo.InvariantCulture);
        string power = best.Setting.BestPowerGph is null
            ? ""
            : $", {best.Setting.BestPowerGph.Value.ToString("0.0", CultureInfo.InvariantCulture)} best power";

        return $"Book: nearest is {best.Setting.PowerPercent} percent at {nearestRpm} RPM, "
             + $"{best.Mp!.Value.ToString("0.0", CultureInfo.InvariantCulture)} inches, "
             + $"{economy} gallons per hour best economy{power}";
    }

    // ==================================================================================
    // Shared
    // ==================================================================================

    /// <summary>
    /// The POH's temperature correction, as a sentence, or empty when the day is close
    /// enough to standard for it to say nothing. ISA is 15 °C at sea level falling 2 °C
    /// per thousand feet, which is the lapse the tables' own °C column is printed on.
    /// </summary>
    public static string IsaCorrection(double pressureAltitudeFt, double oatC)
    {
        double isa = 15.0 - 2.0 * (pressureAltitudeFt / 1000.0);
        double delta = oatC - isa;
        if (Math.Abs(delta) < 7.5) return "";

        // The POH quotes 3 % of the selected power per 15 degrees, in each direction.
        double percent = 3.0 * Math.Abs(delta) / 15.0;
        string direction = delta > 0 ? "less" : "more";
        return $"ISA {(delta > 0 ? "plus" : "minus")} {Math.Abs(delta):F0}, "
             + $"so about {percent:F0} percent {direction} power than the table";
    }

    private static int NearestIndex(IReadOnlyList<int> values, double target)
    {
        int best = 0;
        double bestGap = Math.Abs(values[0] - target);
        for (int i = 1; i < values.Count; i++)
        {
            double gap = Math.Abs(values[i] - target);
            if (gap < bestGap) { bestGap = gap; best = i; }
        }
        return best;
    }
}
