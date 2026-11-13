using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The rows of the Fenix ISIS window: the decoded ISIS (<see cref="FenixChannelIsis"/>) worded for a screen reader, in the
/// ISIS's own layout, the speed column, the attitude and LS scales, then the altitude column. <see cref="Rows"/> is the
/// only place that decides the order of the rows; the builders below it each make one row.
/// </summary>
/// <remarks>
/// <para>What the ISIS also shares with the PFD reads in plan 02's PFD words (<see cref="FenixChannelPfdRows"/>): "Airspeed
/// 251 knots", "Pitch 2.6 degrees up, bank 1 degree left", "Altitude 31012 feet, STD", "QNH 1016", "Localizer diamond 0.4
/// dots right". What the ISIS alone draws has its own words: its bugs, "INIT 90 seconds" and "ATT 10 seconds".</para>
/// <para>Measured on 3.0.0.7613 (2026-11-04, evidence07-isis.md). While initializing (182.2) the ISIS
/// draws no tapes and no attitude, only the SPD, ATT and ALT flags, "INIT 90s", the baro and, on the LS scales, the LOC and
/// G/S flags. A quick realignment (182.17 with 182.2 clear) draws "ATT 10s" in place of the attitude. The figures of 182.16
/// and 182.17 are the lengths of their phases, never a countdown, so they are read as drawn. The bugs page (182.5)
/// replaces the whole display. The Mach is on the wire exactly while the ISIS draws it (from 0.50 climbing until 0.45
/// descending), so it needs no threshold here. The QNH (182.18) is always hPa, and the ISIS shows it in inches to two
/// places while 182.6 is clear. The slip has no row: its side and the size at which the index leaves center are not yet
/// measured, and the app never speaks a guess.</para>
/// <para>Numbers are written with the invariant culture, whole numbers round away from zero, a figure with a decimal place
/// drops a ".0", and a value that rounds to 0 never reads with a side or a minus.</para>
/// </remarks>
internal static class FenixChannelIsisRows
{
    /// <summary>What <see cref="FenixChannelMonitor.ReportFeatureProblem"/> logs the window under.</summary>
    public const string Feature = "ISIS";

    /// <summary>The status row's reason on a Fenix with the round standby gauges.</summary>
    public const string NoIsisProblem = "this Fenix has no ISIS";

    /// <summary>The speed scale's lowest figure: the pointer rests there at rest (measured, as the PFD's).</summary>
    internal const double TapeFloorKnots = 30;

    /// <summary>The LS scales hold two dots either side of center; the wire pins a deviation past them at 4.</summary>
    internal const double FullScaleDots = 2;

    internal const bool PitchPositiveIsUp = true;
    internal const bool BankPositiveIsRight = true;
    internal const bool LocDotsPositiveIsRight = true;
    internal const bool GsDotsPositiveIsAbove = true;

    /// <summary>The bugs in the order the channel sends them: this many speed bugs, then the altitude bugs.</summary>
    internal const int SpeedBugCount = 4;

    private const string AttitudeUnit = "degrees";

    /// <summary>
    /// The window's rows for <paramref name="snapshot"/>, or its one status row: "this Fenix has no ISIS" on a round-gauge
    /// Fenix, else the decoder's problem, which also goes to <paramref name="reportProblem"/>. The caller has judged the
    /// snapshot's verdict.
    /// </summary>
    public static FenixChannelRows Compose(FenixChannelSnapshot snapshot, Aircraft.FenixVariant variant,
        Action<string> reportProblem)
    {
        if (variant.HasIsis == false) return FenixChannelRows.NotAvailable(NoIsisProblem);

        FenixChannelSection<FenixIsisState> section = snapshot.Push is { } push
            ? FenixChannelIsis.Decode(push)
            : FenixChannelSection<FenixIsisState>.Missing("ISIS data");

        if (!section.IsOk)
        {
            string problem = section.Problem ?? "no ISIS data";
            reportProblem(problem);
            return FenixChannelRows.NotAvailable(problem);
        }

        return FenixChannelRows.Of(Rows(section.Value!));
    }

    /// <summary>The rows of a decoded ISIS, top to bottom by column.</summary>
    public static IReadOnlyList<string> Rows(FenixIsisState s)
    {
        if (s.InBugsPage) return BugsPage(s);

        var rows = new List<string>();

        if (s.Initializing)
        {
            rows.Add(FenixChannelPfdRows.SpeedFlag);
            rows.Add(FenixChannelPfdRows.AttitudeFlag);
            rows.Add(s.AlignSeconds is { } length ? $"INIT {Seconds(length)}" : "INIT");
            AddLs(rows, s);
            rows.Add($"{FenixChannelPfdRows.AltitudeFlag}, {Baro(s)}");
            return rows;
        }

        rows.Add(AirspeedRow(s.Airspeed));
        rows.AddRange(EnabledBugs(s, speed: true).Select(bug => $"Speed bug {Whole(bug.Value)}"));
        if (IsNumber(s.Mach) && s.Mach > 0) rows.Add($"Mach {Mach(s.Mach)}");

        rows.Add(s.RealignSeconds is { } realign
            ? $"ATT {Seconds(realign)}"
            : $"{PitchWords(s.Pitch)}, {BankWords(s.Bank)}");
        AddLs(rows, s);

        string altitude = IsNumber(s.Altitude) ? $"Altitude {Whole(s.Altitude)} feet" : "Altitude not available";
        rows.Add($"{altitude}, {Baro(s)}");
        rows.AddRange(EnabledBugs(s, speed: false).Select(bug => $"Altitude bug {Whole(bug.Value)}"));

        return rows;
    }

    // ---- The parts ----------------------------------------------------------------------------

    private static string AirspeedRow(double airspeed)
    {
        if (!IsNumber(airspeed)) return "Airspeed not available";

        return airspeed < TapeFloorKnots
            ? $"Airspeed below {Whole(TapeFloorKnots)} knots"
            : $"Airspeed {Whole(airspeed)} knots";
    }

    // ".75": two decimals and no leading zero, as the ISIS draws it under the speed scale.
    private static string Mach(double mach)
        => Rounded(mach, 2).ToString("0.00", CultureInfo.InvariantCulture).TrimStart('0');

    private static string PitchWords(double pitch)
    {
        if (!IsNumber(pitch)) return "Pitch not available";

        double rounded = Rounded(pitch, 1);
        if (rounded == 0) return $"Pitch 0 {AttitudeUnit}";

        return $"Pitch {Quantity(Math.Abs(rounded), AttitudeUnit)} {SideOf(rounded, PitchPositiveIsUp, "up", "down")}";
    }

    private static string BankWords(double bank)
    {
        if (!IsNumber(bank)) return "bank not available";

        double rounded = Rounded(bank, 0);
        if (rounded == 0) return "wings level";

        return $"bank {Quantity(Math.Abs(rounded), AttitudeUnit)} {SideOf(rounded, BankPositiveIsRight, "right", "left")}";
    }

    // "STD" while at standard, whatever QNH the ISIS keeps; else the QNH in the unit the ISIS shows: hPa while 182.6,
    // else inches converted from the hPa it is sent in. "baro not read" for a QNH that is not a number.
    private static string Baro(FenixIsisState s)
    {
        if (s.Standard) return "STD";
        if (!IsNumber(s.Qnh)) return "baro not read";

        if (s.HectoPascal) return $"QNH {Whole(s.Qnh)}";

        double inches = Rounded(s.Qnh / Aircraft.AltimeterPhrase.HpaPerInHg, 2);
        return $"QNH {inches.ToString("0.00", CultureInfo.InvariantCulture)}";
    }

    // The LS scales, only while the ISIS shows them: each scale's flag while its source has failed, else its diamond.
    private static void AddLs(List<string> rows, FenixIsisState s)
    {
        if (!s.LsShown) return;

        rows.Add(s.LocFailed ? "LOC flag" : DeviationRow("Localizer", s.LocDots, LocDotsPositiveIsRight, "right", "left"));
        rows.Add(s.GsFailed ? "G/S flag" : DeviationRow("Glideslope", s.GsDots, GsDotsPositiveIsAbove, "above", "below"));
    }

    // An LS deviation worded for the diamond, never the aircraft's side, in plan 02's words: "<scale> diamond 0.4 dots
    // right", "<scale> centered" when it rounds to 0, "<scale> diamond at full scale, right" from FullScaleDots on. A
    // scale with no number draws no diamond.
    private static string DeviationRow(string scale, double dots, bool positiveIsFirst, string first, string second)
    {
        if (!IsNumber(dots)) return $"{scale} scale, no diamond";

        if (Math.Abs(dots) >= FullScaleDots)
        {
            return $"{scale} diamond at full scale, {SideOf(dots, positiveIsFirst, first, second)}";
        }

        double rounded = Rounded(dots, 1);
        if (rounded == 0) return $"{scale} centered";

        return $"{scale} diamond {Quantity(Math.Abs(rounded), "dots")} {SideOf(rounded, positiveIsFirst, first, second)}";
    }

    // The enabled bugs of one column, in the order sent, leaving out a value the ISIS could not draw.
    private static IEnumerable<FenixIsisBug> EnabledBugs(FenixIsisState s, bool speed)
        => s.Bugs.Select((bug, index) => (bug, index))
            .Where(entry => (entry.index < SpeedBugCount) == speed && entry.bug.Enabled && IsNumber(entry.bug.Value))
            .Select(entry => entry.bug);

    // The bugs page: every bug, numbered top to bottom in its column as the page lays them out, its value, on or off,
    // and ", selected" on the one 182.21 names (0-based over all the bugs).
    private static IReadOnlyList<string> BugsPage(FenixIsisState s)
    {
        var rows = new List<string> { "Bugs page" };
        for (int index = 0; index < s.Bugs.Count; index++)
        {
            FenixIsisBug bug = s.Bugs[index];
            bool speed = index < SpeedBugCount;
            int number = speed ? index + 1 : index - SpeedBugCount + 1;
            string value = IsNumber(bug.Value) ? Whole(bug.Value) : "not available";
            string selected = index == s.SelectedBug ? ", selected" : "";
            rows.Add($"{(speed ? "Speed" : "Altitude")} bug {number}: {value}, {(bug.Enabled ? "on" : "off")}{selected}");
        }

        return rows;
    }

    // ---- Helpers ------------------------------------------------------------------------------

    // "90 seconds": the length the ISIS draws as "90s", spelled out so a screen reader does not say "nineties".
    private static string Seconds(int seconds) => seconds == 1 ? "1 second" : $"{seconds.ToString(CultureInfo.InvariantCulture)} seconds";

    // A figure is a finite number: NaN is a value the ISIS does not draw, and infinity is no figure either.
    private static bool IsNumber(double value) => double.IsFinite(value);

    // Rounded away from zero to a number of places, and never a negative zero.
    private static double Rounded(double value, int places)
    {
        double rounded = Math.Round(value, places, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    private static string Whole(double value) => Rounded(value, 0).ToString("0", CultureInfo.InvariantCulture);

    // An amount already rounded and without its sign: "2.5 degrees", "4 degrees", "1 degree".
    private static string Quantity(double amount, string pluralUnit)
        => amount == 1
            ? $"1 {(pluralUnit.EndsWith('s') ? pluralUnit[..^1] : pluralUnit)}"
            : $"{amount.ToString("0.#", CultureInfo.InvariantCulture)} {pluralUnit}";

    // Which of two sides an amount is on, given the side a positive amount is on. The caller has judged it is not zero.
    private static string SideOf(double amount, bool positiveIsFirst, string first, string second)
        => (amount > 0) == positiveIsFirst ? first : second;
}
