using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The rows of the Fenix PFD window: one snapshot of the display channel turned into what the captain's PFD draws,
/// worded for a screen reader. <see cref="Compose"/> is the only place that decides the order of the rows; the builders
/// below it each make one row or one part of one.
/// </summary>
/// <remarks>
/// <para>The rows run: the FMA by column, the airspeed and the speed target, the tape's speeds, the Mach, the altitude,
/// the vertical speed, the attitude, the flight director, the heading, the radio altitude and the DH message, then the LS
/// information and the two scales (only while the LS button is on), the approach labels, the marker and the LS flag. A
/// row whose condition fails, or whose value is not a number, is left out. The exception is a primary value (airspeed,
/// altitude, vertical speed, pitch and bank, heading) of an instrument the PFD draws: it reads "not available" in its
/// place. An instrument the PFD does not draw reads as its flag, in the PFD's own words, where its rows would be.</para>
/// <para>A row speaks no color unless it comes from channel text through <see cref="FenixChannelSpeech"/>. Numbers are
/// written with the invariant culture and an ASCII minus whatever the PC's culture is, whole numbers round away from zero,
/// a figure with a decimal place drops a ".0", and a value that rounds to 0 never reads with a side or a minus.</para>
/// <para>The constants below are what the sim measured on the Fenix build 3.0.0.7613, and each sign is used where it is
/// read, so a different build that flips one changes one line.</para>
/// </remarks>
internal static class FenixChannelPfdRows
{
    /// <summary>What <see cref="FenixChannelMonitor.ReportFeatureProblem"/> logs the window under.</summary>
    public const string Feature = "PFD";

    /// <summary>The speed tape stops here: under it the PFD draws no airspeed figure.</summary>
    internal const double TapeFloorKnots = 30;

    /// <summary>The trend arrow is drawn from this size on, in knots.</summary>
    internal const double TrendShownKnots = 2;

    /// <summary>The vertical speed (28.6) is in feet per minute already.</summary>
    internal const double VerticalSpeedToFeetPerMinute = 1;

    internal const bool BankPositiveIsRight = true;
    internal const bool LocDotsPositiveIsRight = true;
    internal const bool GsDotsPositiveIsAbove = true;

    /// <summary>
    /// The localizer and glideslope scales hold this many dots on each side of center. A deviation at or past it draws
    /// the half diamond at the scale's end (2.35 and 4.0 both did), so the row gives no figure from here on.
    /// </summary>
    internal const double FullScaleDots = 2;

    /// <summary>The metric altitude is drawn to the nearest multiple of this, in meters.</summary>
    internal const double MetricStepMeters = 10;

    /// <summary>The unit the flight director's bars are measured in, plural.</summary>
    internal const string FdUnitWord = "degrees";

    internal const bool FdPitchPositiveIsUp = true;
    internal const bool FdRollPositiveIsRight = true;
    internal const bool FdYawPositiveIsRight = true;

    /// <summary>What the PFD draws in place of an instrument it has lost, in its words and with no color.</summary>
    internal const string SpeedFlag = "SPD flag";

    internal const string AltitudeFlag = "ALT flag";
    internal const string AttitudeFlag = "ATT flag";
    internal const string HeadingFlag = "HDG flag";
    internal const string VerticalSpeedFlag = "V/S flag";

    /// <summary>The FMA slot of the Engagement column's flight director row (row 2).</summary>
    internal const int FdSlot = 9;

    private const string AttitudeUnit = "degrees";
    private const double MetersPerFoot = 0.3048;

    // The flight director row of the Engagement column arrives as a format letter, a digit or a dash, a run of spaces and
    // a digit or a dash ("w1   2"): the PFD draws the letters FD between the two itself.
    private static readonly Regex FdPair = new(@"^w([12-]) +([12-])\z", RegexOptions.CultureInvariant);

    // ---- The window ---------------------------------------------------------------------------

    /// <summary>
    /// The window's rows for <paramref name="snapshot"/>, or the one status row when the PFD cannot be read: the
    /// section's problem, which also goes to <paramref name="reportProblem"/>.
    /// </summary>
    public static FenixChannelRows Compose(FenixChannelSnapshot snapshot, Action<string> reportProblem)
    {
        FenixChannelSection<FenixChannelPfd> section = FenixChannelPfd.From(snapshot);
        if (!section.IsOk)
        {
            string problem = section.Problem ?? "no data";
            reportProblem(problem);
            return FenixChannelRows.NotAvailable(problem);
        }

        FenixChannelPfd pfd = section.Value!;
        FenixChannelFrame frame = snapshot.Frame!;
        var rows = new List<string>(FmaRows(frame.Fma));

        if (pfd.SpeedShown)
        {
            rows.Add(AirspeedRow(pfd));
            Add(rows, SpeedTargetRow(pfd));
            rows.AddRange(TapeSpeeds(pfd).Select(speed => SpeedWords(speed.Name, speed.Knots)));
            Add(rows, MachRow(pfd));
        }
        else
        {
            rows.Add(SpeedFlag);
        }

        rows.Add(pfd.AltitudeShown ? AltitudeRow(pfd) : AltitudeFlag);
        rows.Add(pfd.VerticalSpeedShown ? VerticalSpeedRow(pfd) : VerticalSpeedFlag);
        rows.Add(pfd.AttitudeShown ? AttitudeRow(pfd) : AttitudeFlag);
        // The yaw bar sits at its commanded heading minus the heading, and the frame's heading can be a stale number while
        // the PFD draws the HDG flag, so the bar waits for the heading.
        Add(rows, FlightDirectorRow(frame.Fd, pfd.AttitudeShown, pfd.Pitch, pfd.Bank,
            pfd.HeadingShown ? pfd.MagHeading : double.NaN));
        rows.Add(pfd.HeadingShown ? HeadingRow(pfd) : HeadingFlag);

        if (FenixChannelSpeech.Line(pfd.RadioAltitudeText) is { } radioAltitude) rows.Add($"Radio altitude {radioAltitude}");
        if (pfd.DhMessage) rows.Add("DH");

        if (pfd.Ls)
        {
            Add(rows, Spoken("LS information", pfd.LsIdent, pfd.LsFrequency, pfd.LsDme));
            Add(rows, DeviationRow("Localizer", pfd.LocDots, LocDotsPositiveIsRight, "right", "left"));
            Add(rows, DeviationRow("Glideslope", pfd.GsDots, GsDotsPositiveIsAbove, "above", "below"));
        }

        // The labels, the marker and the LS flag are shown whenever the channel sends their text, whatever the LS button
        // says.
        Add(rows, Spoken("Approach labels", pfd.ApproachCenter, pfd.ApproachLoc, pfd.ApproachGs));
        Add(rows, Spoken("Marker beacon", pfd.MarkerText));
        Add(rows, Spoken("LS flag", pfd.LsFlagText));

        return FenixChannelRows.Of(rows);
    }

    // ---- The FMA ------------------------------------------------------------------------------

    /// <summary>
    /// The FMA's rows: one for each column with something to say, in column order and worded by
    /// <see cref="FenixChannelFmaLayout.ColumnPhrase"/> ("Thrust: MACH, green"), then one for each message slot, by
    /// column and then by slot ("FMA message: LVR CLB, white"). The Engagement column's flight director row reads as
    /// the PFD draws it ("1 FD 2"). No Lateral row while the PFD draws LAND, FLARE or ROLL OUT across both columns: the
    /// Fenix keeps sending a lateral mode underneath that the glass does not show.
    /// </summary>
    internal static IReadOnlyList<string> FmaRows(IReadOnlyList<FenixFmaSlot> fma)
    {
        IReadOnlyList<FenixFmaSlot> slots = WithFdWords(fma);
        FenixFmaColumn[] columns = Enum.GetValues<FenixFmaColumn>().Where(column => column != FenixFmaColumn.Unknown).ToArray();
        bool commonMode = slots.Count == FenixChannelFields.FmaEntry.SlotCount && FenixChannelFmaSpeech.ShowsCommonMode(slots);
        var rows = new List<string>();

        foreach (FenixFmaColumn column in columns)
        {
            if (commonMode && column == FenixFmaColumn.Lateral) continue;

            if (FenixChannelFmaLayout.ColumnPhrase(slots, column) is { } phrase)
            {
                rows.Add($"{FenixChannelFmaLayout.NameOf(column)}: {phrase}");
            }
        }

        foreach (FenixFmaColumn column in columns)
        {
            foreach (int slot in Enumerable.Range(0, slots.Count))
            {
                if (FenixChannelFmaLayout.Of(slot).Column != column || !FenixChannelFmaLayout.IsMessage(slot)) continue;

                if (FenixChannelSpeech.Line(slots[slot].Text) is { } message) rows.Add($"FMA message: {message}");
            }
        }

        return rows;
    }

    // A copy of the slots with the flight director row's run of spaces replaced by " FD ", when that slot has the shape the
    // Fenix sends. Any other text, and every other slot, is as it came.
    private static IReadOnlyList<FenixFmaSlot> WithFdWords(IReadOnlyList<FenixFmaSlot> fma)
    {
        if (fma.Count <= FdSlot) return fma;

        Match pair = FdPair.Match(fma[FdSlot].Text ?? "");
        if (!pair.Success) return fma;

        FenixFmaSlot[] copy = fma.ToArray();
        copy[FdSlot] = fma[FdSlot] with { Text = $"w{pair.Groups[1].Value} FD {pair.Groups[2].Value}" };
        return copy;
    }

    // ---- Speed --------------------------------------------------------------------------------

    private static string AirspeedRow(FenixChannelPfd pfd)
    {
        if (!IsNumber(pfd.Airspeed)) return "Airspeed not available";

        string row = pfd.Airspeed < TapeFloorKnots
            ? $"Airspeed below {Whole(TapeFloorKnots)} knots"
            : $"Airspeed {Whole(pfd.Airspeed)} knots";

        // The arrow ends at the airspeed plus the trend. The tape ends at the floor, so the PFD draws no arrow from an
        // airspeed under it and none to a tip under it.
        if (IsNumber(pfd.SpeedTrend) && Math.Abs(pfd.SpeedTrend) >= TrendShownKnots)
        {
            double tip = pfd.Airspeed + pfd.SpeedTrend;
            if (pfd.Airspeed >= TapeFloorKnots && tip >= TapeFloorKnots) row += $", trend arrow to {Whole(tip)}";
        }

        return row;
    }

    private static string? SpeedTargetRow(FenixChannelPfd pfd)
        => pfd.BugsVisible && IsNumber(pfd.TargetIas)
            ? $"Speed target {Whole(pfd.TargetIas)} knots, {(pfd.SpeedManaged ? "managed" : "selected")}"
            : null;

    private static string? MachRow(FenixChannelPfd pfd)
        => FenixChannelSpeech.Line(pfd.MachText) is { } mach ? $"Mach {mach}" : null;

    /// <summary>
    /// The speeds the tape draws as marks and limits, highest first, equal speeds in the order they are listed here: VMAX,
    /// VFE next, F, S, green dot, VLS, VSW (in alternate law, else alpha prot), V1, VR, V2. Numbers only, and none while
    /// the PFD shows the speed flag.
    /// </summary>
    internal static IReadOnlyList<(string Name, double Knots)> TapeSpeeds(FenixChannelPfd pfd)
    {
        if (!pfd.SpeedShown) return [];

        (string Name, double Knots)[] speeds =
        [
            ("VMAX", pfd.VMax), ("VFE next", pfd.VfeNext), ("F speed", pfd.F), ("S speed", pfd.S),
            ("Green dot", pfd.GreenDot), ("VLS", pfd.Vls),
            IsNumber(pfd.Vsw) ? ("VSW", pfd.Vsw) : ("Alpha prot", pfd.AlphaProt),
            ("V1", pfd.V1), ("VR", pfd.Vr), ("V2", pfd.V2)
        ];

        return speeds.Where(speed => IsNumber(speed.Knots)).OrderByDescending(speed => speed.Knots).ToList();
    }

    /// <summary>One speed of the tape in whole knots: "VLS 232 knots".</summary>
    internal static string SpeedWords(string name, double knots) => $"{name} {Whole(knots)} knots";

    // ---- Altitude and vertical speed ----------------------------------------------------------

    private static string AltitudeRow(FenixChannelPfd pfd)
    {
        string altitude = IsNumber(pfd.Altitude)
            ? $"Altitude {Whole(pfd.Altitude)} feet{(pfd.AltitudeAmber ? ", amber" : "")}"
            : "Altitude not available";
        string row = $"{altitude}, {BaroReference(pfd.Baro) ?? "baro not read"}";

        if (pfd.MetricAltitude && IsNumber(pfd.Altitude))
        {
            row += $", {Whole(Rounded(pfd.Altitude * MetersPerFoot / MetricStepMeters, 0) * MetricStepMeters)} meters";
        }

        return row;
    }

    /// <summary>
    /// What the baro box shows, from the captain's EFIS and never from the baro text: "STD", "QNH 1029" or, in inches of
    /// mercury, "QNH 29.92". Null for QFE, and for a QNH that is not a number in the unit the box shows.
    /// </summary>
    internal static string? BaroReference(FenixPfdBaro baro)
    {
        if (baro.Std) return "STD";
        if (baro.Qfe) return null;

        if (baro.Inches) return IsNumber(baro.QnhInHg) ? $"QNH {Rounded(baro.QnhInHg, 2).ToString("0.00", CultureInfo.InvariantCulture)}" : null;

        return IsNumber(baro.QnhHpa) ? $"QNH {Whole(baro.QnhHpa)}" : null;
    }

    private static string VerticalSpeedRow(FenixChannelPfd pfd)
    {
        if (!IsNumber(pfd.VerticalSpeed)) return "Vertical speed not available";

        double rate = Rounded(pfd.VerticalSpeed * VerticalSpeedToFeetPerMinute / 100, 0) * 100;
        if (rate == 0) return "Vertical speed 0 feet per minute";

        return $"Vertical speed {Whole(Math.Abs(rate))} feet per minute {(rate > 0 ? "up" : "down")}";
    }

    // ---- Attitude and the flight director -----------------------------------------------------

    private static string AttitudeRow(FenixChannelPfd pfd) => $"{PitchWords(pfd.Pitch)}, {BankWords(pfd.Bank)}";

    private static string PitchWords(double pitch)
    {
        if (!IsNumber(pitch)) return "Pitch not available";

        double rounded = Rounded(pitch, 1);
        if (rounded == 0) return $"Pitch 0 {AttitudeUnit}";

        return $"Pitch {Quantity(Math.Abs(rounded), AttitudeUnit)} {(rounded > 0 ? "up" : "down")}";
    }

    private static string BankWords(double bank)
    {
        if (!IsNumber(bank)) return "bank not available";

        double rounded = Rounded(bank, 0);
        if (rounded == 0) return "wings level";

        return $"bank {Quantity(Math.Abs(rounded), AttitudeUnit)} {SideOf(rounded, BankPositiveIsRight, "right", "left")}";
    }

    /// <summary>
    /// The flight director as the captain's PFD draws it. The fields hold the commanded attitude, so each bar is read
    /// where it sits: the commanded value minus the attitude ("pitch bar 2.5 degrees up, roll bar 4 degrees left"). The
    /// yaw bar sits at its commanded heading minus the magnetic heading, and is drawn only while roll guidance is set; a
    /// <paramref name="magHeading"/> that is not a number (the window passes NaN while the HDG flag shows) leaves it
    /// out. Null when no bar is drawn: the captain's FD is off, the attitude is not shown, an inhibit covers the bars, the PFD
    /// draws the flight path director instead, or no bar is a number. While the bars flash the row ends ", flashing".
    /// </summary>
    internal static string? FlightDirectorRow(FenixFdState fd, bool attitudeShown, double pitch, double bank,
        double magHeading)
    {
        if (!fd.FdOnCapt || !attitudeShown || fd.FdInhibit || fd.TrkFpa) return null;

        var bars = new List<string>();
        if (!fd.InhibitPitch)
        {
            Add(bars, Offset("pitch bar", fd.Pitch - pitch, FdUnitWord, FdPitchPositiveIsUp, "up", "down"));
        }

        if (!fd.InhibitRoll)
        {
            Add(bars, Offset("roll bar", fd.Roll - bank, FdUnitWord, FdRollPositiveIsRight, "right", "left"));
        }

        if (fd.RollGuidance)
        {
            Add(bars, Offset("yaw bar", Wrapped(fd.Yawbar - magHeading), FdUnitWord, FdYawPositiveIsRight, "right", "left"));
        }

        if (bars.Count == 0) return null;

        return $"Flight director: {string.Join(", ", bars)}{(fd.FdFlash ? ", flashing" : "")}";
    }

    // ---- Heading ------------------------------------------------------------------------------

    private static string HeadingRow(FenixChannelPfd pfd)
    {
        string row = Compass(pfd.MagHeading) is { } heading ? $"Heading {heading}" : "Heading not available";

        return Compass(pfd.MagneticTrack) is { } track ? $"{row}, track {track}" : row;
    }

    /// <summary>
    /// A heading or track in whole degrees from 001 to 360, three digits, north as 360. Null when it is not a number.
    /// </summary>
    internal static string? Compass(double degrees)
    {
        if (!IsNumber(degrees)) return null;

        double whole = Rounded(degrees, 0) % 360;
        if (whole < 0) whole += 360;
        if (whole == 0) whole = 360;

        return whole.ToString("000", CultureInfo.InvariantCulture);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static void Add(List<string> rows, string? row)
    {
        if (row is not null) rows.Add(row);
    }

    // A figure is a finite number: NaN is a value the PFD does not draw, and infinity is no figure either.
    private static bool IsNumber(double value) => double.IsFinite(value);

    // Rounded away from zero to a number of places, and never a negative zero.
    private static double Rounded(double value, int places)
    {
        double rounded = Math.Round(value, places, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 0 : rounded;
    }

    private static string Whole(double value) => Rounded(value, 0).ToString("0", CultureInfo.InvariantCulture);

    // An amount already rounded to one place and without its sign: "2.5 degrees", "4 degrees", "1 degree".
    private static string Quantity(double amount, string pluralUnit)
        => amount == 1
            ? $"1 {(pluralUnit.EndsWith('s') ? pluralUnit[..^1] : pluralUnit)}"
            : $"{amount.ToString("0.#", CultureInfo.InvariantCulture)} {pluralUnit}";

    // Which of two sides an amount is on, given the side a positive amount is on. The caller has judged it is not zero.
    private static string SideOf(double amount, bool positiveIsFirst, string first, string second)
        => (amount > 0) == positiveIsFirst ? first : second;

    // "<label> 2.5 degrees up", or "<label> centered" when the amount rounds to 0. Null when it is not a number.
    private static string? Offset(string label, double amount, string pluralUnit, bool positiveIsFirst, string first,
        string second)
    {
        if (!IsNumber(amount)) return null;

        double rounded = Rounded(amount, 1);
        if (rounded == 0) return $"{label} centered";

        return $"{label} {Quantity(Math.Abs(rounded), pluralUnit)} {SideOf(rounded, positiveIsFirst, first, second)}";
    }

    // An LS deviation worded for the diamond the PFD draws, never the aircraft's side: "<scale> diamond 0.4 dots right".
    // The figure rounds to a tenth, and one that rounds to 0 is "<scale> centered". From FullScaleDots on the diamond sits
    // at the scale's end whatever the figure, so the row gives none: "<scale> diamond at full scale, right". Null when the
    // deviation is not a number (the scale is not drawn).
    private static string? DeviationRow(string scale, double dots, bool positiveIsFirst, string first, string second)
    {
        if (!IsNumber(dots)) return null;

        if (Math.Abs(dots) >= FullScaleDots)
        {
            return $"{scale} diamond at full scale, {SideOf(dots, positiveIsFirst, first, second)}";
        }

        double rounded = Rounded(dots, 1);
        if (rounded == 0) return $"{scale} centered";

        return $"{scale} diamond {Quantity(Math.Abs(rounded), "dots")} {SideOf(rounded, positiveIsFirst, first, second)}";
    }

    // The difference of two headings, from -180 to 180 degrees.
    private static double Wrapped(double difference)
    {
        double wrapped = difference % 360;
        if (wrapped > 180) wrapped -= 360;
        else if (wrapped <= -180) wrapped += 360;

        return wrapped;
    }

    // "<label>: " and the speech of each text that has any, joined by "; ". Null when no text has.
    private static string? Spoken(string label, params string[] texts)
    {
        string[] lines = texts.Select(text => FenixChannelSpeech.Line(text)).OfType<string>().ToArray();

        return lines.Length == 0 ? null : $"{label}: {string.Join("; ", lines)}";
    }
}
