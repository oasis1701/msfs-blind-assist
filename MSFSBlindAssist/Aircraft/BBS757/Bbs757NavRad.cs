using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One VOR receiver as the CDU's NAV RADIO page shows it. <paramref name="Mhz"/> is null when it is not
/// tuned (the page shows <c>------</c>). <paramref name="Mode"/> is the letter next to the frequency: 'A'
/// auto (the FMC tuned it), 'R' remote (tuned from the CDU), 'M' manual (on the VOR/DME panel), any other
/// letter manual; null when the page shows none. <paramref name="Ident"/> is the station's ident once
/// received, else null.
/// </summary>
public sealed record Bbs757VorTuning(double? Mhz, char? Mode, string? Ident);

/// <summary>
/// The NAV RADIO page's two VORs. A side is null when its text could not be read (a half-drawn row, a
/// layout not seen): that is not "not tuned", which is a <see cref="Bbs757VorTuning"/> with no frequency.
/// </summary>
public sealed record Bbs757NavRadPage(Bbs757VorTuning? Left, Bbs757VorTuning? Right);

/// <summary>
/// The runway ILS the FMC holds, as the first officer's CDU shows it on APPROACH REF. <paramref name="Runway"/>
/// is the ILS line's runway ("09L"); <paramref name="Airport"/> the airport the page names for that runway
/// (null when the page does not, or names another runway); <paramref name="FrontCourse"/> the front
/// course in degrees, null when the page shows none.
/// </summary>
public sealed record Bbs757RunwayIls(string Runway, double Mhz, string Ident, int? FrontCourse, string? Airport = null);

/// <summary>
/// The 757's own nav radios, for output N and the Ctrl+N window. BlueBird's VOR and ILS receivers are its
/// own and leave the stock NAV variables alone (measured, [B757-5]), so the stock NAV
/// radios are never read here. The courses are BlueBird's L:vars; the VOR frequencies are on the CDU's NAV
/// RADIO page, and the FMC's runway ILS on APPROACH REF, both read on the first officer's CDU
/// (<see cref="Bbs757CduPages"/>, ruling R52). The tuned ILS frequency is on no page: the
/// readout says what the ILS control panel's display shows (BlueBird's <c>L:BBS_757_ILS_FREQ</c>, -1 parked;
/// <see cref="Bbs757IlsPanel.ReadingFrom"/>), or, when it cannot be
/// read, the app's count of the panel (<see cref="Bbs757IlsPanel"/>), saying it is a count ("ILS 110.50 by
/// count."). The members here read screens and build sentences; they press no key.
///
/// The measured NAV RADIO page (CDU 2, 2026-10-26): row 0 the title <c>NAV RADIO</c>, row 1 the labels
/// <c>VOR L … VOR R</c>, row 2 the tunings, row 3 <c>CRS RADIAL CRS</c>, row 4 the courses. The two sides
/// are mirrored. VOR L is <c>freq[mode][ident]</c> flush left (<c>116.90AGSU</c>, <c>113.90R</c>,
/// <c>117.40M</c>); VOR R is <c>[ident][mode]freq</c> flush right (<c>GSUR116.90</c>). The mode is A auto (the
/// FMC tuned it), R tuned from the CDU, M manual on the VOR/DME panel. <c>------</c> is not tuned. Each side
/// is read on its own, so one that cannot be read leaves the other readable.
///
/// A VOR's CRS field exists only while that VOR is tuned by hand: in AUTO the CDU blanks it and ignores a
/// course entry (measured). So a readout speaks a VOR's course only then.
/// </summary>
public static class Bbs757NavRad
{
    /// <summary>The CDU key that brings up the NAV RADIO page (<see cref="Bbs757CduKeys.NavRad"/>).</summary>
    public const string PageKey = Bbs757CduKeys.NavRad;

    /// <summary>The variable keys the readout reads fresh: BlueBird's course L:vars, integer degrees.</summary>
    public const string VorLCourseKey = "BBS757_VOR_L_COURSE";
    public const string VorRCourseKey = "BBS757_VOR_R_COURSE";
    public const string IlsCourseKey = "BBS757_ILS_COURSE";

    /// <summary>The ILS course L:var the Ctrl+N window writes (<see cref="CourseCode"/>).</summary>
    public const string IlsCourseLVar = "BBS_ILS_COURSE";

    /// <summary>The course L:vars the keys read, with the names the variables are shown under.</summary>
    public static readonly IReadOnlyList<(string Key, string LVar, string Label)> CourseVariables = new[]
    {
        (VorLCourseKey, "BBS_VOR_L_COURSE", "VOR L Course"),
        (VorRCourseKey, "BBS_VOR_R_COURSE", "VOR R Course"),
        (IlsCourseKey, IlsCourseLVar, "ILS Course"),
    };

    /// <summary>
    /// The keys of BlueBird's tuning variables (added by its 2026-10-31 update, not in its public table;
    /// read in cruise 2026-11-05, docs/b757.md, "Radios"): the ILS panel's frequency (-1 while parked), the two VOR receivers'
    /// frequencies and whether each is tuned automatically.
    /// </summary>
    public const string IlsFrequencyKey = "BBS757_ILS_FREQ";
    public const string VorLFrequencyKey = "BBS757_VOR_L_FREQ";
    public const string VorRFrequencyKey = "BBS757_VOR_R_FREQ";
    public const string VorLAutoKey = "BBS757_VOR_L_AUTO";
    public const string VorRAutoKey = "BBS757_VOR_R_AUTO";

    /// <summary>The tuning L:vars the keys read, with the names the variables are shown under.</summary>
    public static readonly IReadOnlyList<(string Key, string LVar, string Label)> NavVariables = new[]
    {
        (IlsFrequencyKey, "BBS_757_ILS_FREQ", "ILS Frequency"),
        (VorLFrequencyKey, "BBS_757_VOR_L_FREQ", "VOR L Frequency"),
        (VorRFrequencyKey, "BBS_757_VOR_R_FREQ", "VOR R Frequency"),
        (VorLAutoKey, "B757_MCP_VOR1_MODE_AUTO", "VOR L Auto Tuning"),
        (VorRAutoKey, "B757_MCP_VOR2_MODE_AUTO", "VOR R Auto Tuning"),
    };

    /// <summary>What an empty ILS course box says.</summary>
    public const string CourseEmptyMessage = "Type a course first.";

    /// <summary>What a typed course that is not a whole number of degrees from 0 to 360 says.</summary>
    public const string CourseInvalidMessage = "Course must be 0 to 360.";

    private const string PageTitle = "NAV RADIO";
    private const string ApproachRefTitle = "APPROACH REF";
    private const string InitRefIndexTitle = "INIT/REF INDEX";

    // The tuning row, and the column the two receivers' texts divide at (24 columns: VOR L is flush
    // left and at most 11 characters, VOR R flush right).
    private const int TuningRow = 2;
    private const int SplitColumn = 12;

    // The CDU's row of the 6L key: "<INDEX" there leads to INIT/REF INDEX.
    private const int SixLeftRow = 12;

    // VOR L: frequency, optional mode letter, optional ident ("116.90AGSU", "113.90R", "108.00").
    private static readonly Regex LeftPattern = new(
        @"^(?<freq>\d{3}\.\d{2})(?<mode>[A-Z])?(?<ident>[A-Z0-9]*)$", RegexOptions.CultureInvariant);

    // VOR R, mirrored: an ident always comes with its mode letter ("GSUR116.90"); a mode letter may stand
    // alone ("R113.90"); neither ("108.00").
    private static readonly Regex RightPattern = new(
        @"^(?:(?<ident>[A-Z0-9]+)(?<mode>[A-Z])|(?<mode>[A-Z]))?(?<freq>\d{3}\.\d{2})$", RegexOptions.CultureInvariant);

    // APPROACH REF's ILS is two rows (measured, route KATL-KMIA): a label row " ILS 09L       FRONT CRS"
    // and under it "110.50 IHZK         095°" (frequency and ident from column 0, the front course at the
    // right). The page names the airport and runway on a row above: " KATL09L      FLAP/SPEED".
    private static readonly Regex IlsLabelPattern = new(
        @"^\s*ILS\s+(?<rwy>\d{2}[LRC]?)(?:\s|$)", RegexOptions.CultureInvariant);

    private static readonly Regex IlsValuePattern = new(
        @"^\s*(?<freq>\d{3}\.\d{2})(?:\s+(?<ident>[A-Z][A-Z0-9]*))?(?:\s+(?<crs>\d{3})\s*°?)?\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex AirportRunwayPattern = new(
        @"^\s*(?<apt>[A-Z][A-Z0-9]{3})(?<rwy>\d{2}[LRC]?)(?:\s|$)", RegexOptions.CultureInvariant);

    private static readonly Bbs757VorTuning NotTuned = new(null, null, null);

    /// <summary>Whether the screen is the NAV RADIO page.</summary>
    public static bool IsNavRadio(Bbs757CduScreen s)
        => s.Lines.Count > 0 && string.Equals(Bbs757CduTitle.PageName(s.Lines[0]), PageTitle, StringComparison.Ordinal);

    /// <summary>
    /// The two VOR tunings of a NAV RADIO screen; null only when the screen is another page. Each side is
    /// read on its own: a text that is neither a tuning nor dashes (a half-drawn row, a layout not seen)
    /// leaves that side null, logged, and the other side is still read. The readout then says that side's
    /// frequency is unavailable, never "not tuned" for a text it could not read.
    /// </summary>
    public static Bbs757NavRadPage? Parse(Bbs757CduScreen s)
    {
        if (!IsNavRadio(s) || s.Lines.Count <= TuningRow) return null;
        string row = s.Lines[TuningRow].PadRight(Bbs757CduFrame.Columns);
        return new Bbs757NavRadPage(
            ParseSide(row[..SplitColumn], right: false), ParseSide(row[SplitColumn..], right: true));
    }

    private static Bbs757VorTuning? ParseSide(string text, bool right)
    {
        text = text.Trim();
        if (text.Length > 0 && text.All(c => c == '-')) return NotTuned;   // ------: not tuned
        var m = (right ? RightPattern : LeftPattern).Match(text);
        if (m.Success && NumberParsing.TryParseExternal(m.Groups["freq"].Value, out double mhz))
        {
            char? mode = m.Groups["mode"].Success ? m.Groups["mode"].Value[0] : null;
            string ident = m.Groups["ident"].Value;
            return new Bbs757VorTuning(mhz, mode, ident.Length == 0 ? null : ident);
        }
        Log.Debug("BBS757", $"NAV RADIO: the {(right ? "VOR R" : "VOR L")} text is not a tuning: \"{text}\"");
        return null;
    }

    /// <summary>
    /// A course L:var's value as whole degrees; null when it was not read or is not a course (NaN,
    /// below 0 or above 360).
    /// </summary>
    public static int? Course(double? raw)
        => raw is double v && double.IsFinite(v) && v >= 0 && v <= 360 ? (int)Math.Round(v) : null;

    /// <summary>
    /// A typed course: a whole number of degrees from 0 to 360, read on any Windows culture
    /// (<see cref="NumberParsing.TryParseTyped"/>); 360 is 0. An empty box says <see cref="CourseEmptyMessage"/>,
    /// anything else that is not a course <see cref="CourseInvalidMessage"/>.
    /// </summary>
    public static bool TryParseCourse(string? text, out int degrees, out string error)
    {
        degrees = 0;
        error = "";
        string typed = (text ?? "").Trim();
        if (typed.Length == 0)
        {
            error = CourseEmptyMessage;
            return false;
        }
        if (!NumberParsing.TryParseTyped(typed, out double value) || value != Math.Floor(value) || value < 0 || value > 360)
        {
            error = CourseInvalidMessage;
            return false;
        }
        degrees = (int)value % 360;
        return true;
    }

    /// <summary>
    /// The calculator string that sets a course L:var: "95 (>L:BBS_ILS_COURSE)". A whole number of degrees,
    /// written with no culture in it.
    /// </summary>
    public static string CourseCode(string lvar, int degrees)
        => $"{degrees.ToString(CultureInfo.InvariantCulture)} (>L:{lvar})";

    /// <summary>
    /// One receiver as spoken: "VOR L 116.90 auto, GSU", "VOR L 113.90 remote", "VOR R not tuned", "VOR R
    /// frequency unavailable" (<paramref name="t"/> null: the text could not be read). The mode letter is a
    /// word (A auto, R remote, any other manual; none, no word), the ident follows a comma, the frequency has
    /// two decimals and a dot whatever the PC's culture.
    /// </summary>
    public static string TuningPhrase(string side, Bbs757VorTuning? t)
    {
        if (t == null) return $"{side} frequency unavailable";
        if (t.Mhz is not double mhz) return $"{side} not tuned";
        string said = $"{side} {mhz.ToString("0.00", CultureInfo.InvariantCulture)}";
        if (t.Mode is char mode) said += mode switch { 'A' => " auto", 'R' => " remote", _ => " manual" };
        if (!string.IsNullOrEmpty(t.Ident)) said += $", {t.Ident}";
        return said;
    }

    /// <summary>
    /// What N says. With the page, each VOR's tuning ("VOR L 116.90 auto, GSU."), its course only while it is
    /// tuned by hand (a frequency and a mode other than auto: "VOR L 113.90 remote, course 095."), then "ILS
    /// course 095.", then the ILS control panel: what its display shows (<paramref name="ilsRead"/>, through
    /// <see cref="Bbs757IlsPanel.ReadPhrase"/>: "ILS 110.50.", "ILS parked.") or, when it could not be read, as
    /// the app counts it, said as a count so it never sounds like a reading (<paramref name="ilsDial"/>, through
    /// <see cref="Bbs757IlsPanel.CountPhrase"/>: "ILS 110.50 by count.", "ILS parked by count.", or "ILS tuning
    /// unknown." with no count). Without the page (the first officer's CDU busy or unreachable): "VOR L course
    /// 095, VOR R course 000, ILS course 095. ILS 110.50. VOR frequencies unavailable." A course that was not read
    /// is "course unavailable".
    /// </summary>
    public static string Readout(Bbs757NavRadPage? page, int? vorL, int? vorR, int? ils, Bbs757IlsDial? ilsDial,
        Bbs757IlsReading? ilsRead = null, Bbs757VorTuning? vorLVar = null, Bbs757VorTuning? vorRVar = null)
    {
        string panel = $"{(ilsRead != null ? Bbs757IlsPanel.ReadPhrase(ilsRead) : Bbs757IlsPanel.CountPhrase(ilsDial))}.";
        if (page == null && (vorLVar != null || vorRVar != null))
            return $"{VorPhrase("VOR L", vorLVar, vorL)} {VorPhrase("VOR R", vorRVar, vorR)} ILS course {CourseWords(ils)}. {panel}";
        if (page == null)
            return $"VOR L course {CourseWords(vorL)}, VOR R course {CourseWords(vorR)}, ILS course {CourseWords(ils)}. {panel} VOR frequencies unavailable.";
        return $"{VorPhrase("VOR L", page.Left, vorL)} {VorPhrase("VOR R", page.Right, vorR)} ILS course {CourseWords(ils)}. {panel}";
    }

    /// <summary>
    /// One VOR's tuning from BlueBird's variables, for N when the CDU page cannot be read: null when the
    /// frequency was not read, is not a number or reads 0 (within 0.0005: a BlueBird build without the
    /// 2026-10-31 variables reads an absent L:var as 0, so 0 is unread, never "not tuned"); otherwise the
    /// frequency rounded to 0.01, with <c>Mhz</c> null ("not tuned") when that is outside 108.00-117.95 (a
    /// not-tuned receiver's reading is unmeasured); <c>Mode</c> 'A' for an auto flag of 1, 'M' for 0, null
    /// when unread. No ident.
    /// </summary>
    public static Bbs757VorTuning? TuningFromVariables(double? mhz, double? auto)
    {
        if (mhz is not double f || !double.IsFinite(f) || Math.Abs(f) < 0.0005) return null;
        double rounded = Math.Round(f, 2);
        double? tuned = rounded >= 108.00 && rounded <= 117.95 ? rounded : null;
        char? mode = auto is double a && double.IsFinite(a) ? (a >= 0.5 ? 'A' : 'M') : null;
        return new Bbs757VorTuning(tuned, mode, null);
    }

    /// <summary>One VOR's sentence: the tuning, and its course only when it is tuned by hand.</summary>
    private static string VorPhrase(string side, Bbs757VorTuning? t, int? course)
    {
        string said = TuningPhrase(side, t);
        bool byHand = t is { Mhz: not null } && t.Mode != 'A';
        return byHand ? $"{said}, course {CourseWords(course)}." : $"{said}.";
    }

    private static string CourseWords(int? course)
        => course is int c ? c.ToString("000", CultureInfo.InvariantCulture) : "unavailable";

    // ---- APPROACH REF -----------------------------------------------------------------------------

    /// <summary>Whether the screen is the APPROACH REF page (whatever state its title shows).</summary>
    public static bool IsApproachRef(Bbs757CduScreen s)
        => s.Lines.Count > 0 && string.Equals(Bbs757CduTitle.Identity(s.Lines[0]), ApproachRefTitle, StringComparison.Ordinal);

    /// <summary>Whether the screen is the INIT/REF INDEX page.</summary>
    public static bool IsInitRefIndex(Bbs757CduScreen s)
        => s.Lines.Count > 0 && string.Equals(Bbs757CduTitle.Identity(s.Lines[0]), InitRefIndexTitle, StringComparison.Ordinal);

    /// <summary>
    /// Whether the page offers <c>&lt;INDEX</c> at 6L, which opens INIT/REF INDEX: POS INIT and PERF INIT do
    /// (measured), and so does APPROACH REF itself.
    /// </summary>
    public static bool HasIndexKey(Bbs757CduScreen s)
        => s.Lines.Count > SixLeftRow && s.Lines[SixLeftRow].StartsWith("<INDEX", StringComparison.Ordinal);

    /// <summary>
    /// The FMC's runway ILS from an APPROACH REF screen: the label row <c>ILS 09L … FRONT CRS</c> and the row
    /// under it, <c>110.50 IHZK … 095°</c> (measured). Null when the screen has no ILS rows, or the row under
    /// the label holds no frequency. The front course is null when the row shows none; the airport is the one
    /// named with the same runway on a row above (<c> KATL09L</c>), else null.
    /// </summary>
    public static Bbs757RunwayIls? ParseApproachRefIls(Bbs757CduScreen s)
    {
        var lines = s.Lines;
        for (int i = 1; i < lines.Count - 1; i++)
        {
            var label = IlsLabelPattern.Match(lines[i]);
            if (!label.Success) continue;
            var value = IlsValuePattern.Match(lines[i + 1]);
            if (!value.Success || !NumberParsing.TryParseExternal(value.Groups["freq"].Value, out double mhz))
            {
                Log.Debug("BBS757", $"APPROACH REF: the row under the ILS label is not a frequency: \"{lines[i + 1].TrimEnd()}\"");
                return null;
            }

            string runway = label.Groups["rwy"].Value;
            int? course = null;
            if (value.Groups["crs"].Success && NumberParsing.TryParseExternal(value.Groups["crs"].Value, out double crs))
                course = Course(crs);
            return new Bbs757RunwayIls(runway, mhz, value.Groups["ident"].Value, course, AirportFor(lines, i, runway));
        }
        return null;
    }

    /// <summary>The airport named with <paramref name="runway"/> on a row above the ILS label, nearest first; null when none.</summary>
    private static string? AirportFor(IReadOnlyList<string> lines, int labelRow, string runway)
    {
        for (int i = labelRow - 1; i >= 1; i--)
        {
            var m = AirportRunwayPattern.Match(lines[i]);
            if (m.Success && m.Groups["rwy"].Value == runway) return m.Groups["apt"].Value;
        }
        return null;
    }

    /// <summary>
    /// The FMC's runway ILS as spoken: "FMC runway ILS KATL 09L, 110.50, IHZK, front course 095" (without the
    /// airport when none was read, without the course when the page shows none), or "FMC runway ILS
    /// unavailable" when <paramref name="ils"/> is null. The frequency has two decimals and a dot whatever the
    /// PC's culture.
    /// </summary>
    public static string RunwayIlsPhrase(Bbs757RunwayIls? ils)
    {
        if (ils == null) return "FMC runway ILS unavailable";
        string runway = string.IsNullOrEmpty(ils.Airport) ? ils.Runway : $"{ils.Airport} {ils.Runway}";
        string said = $"FMC runway ILS {runway}, {ils.Mhz.ToString("0.00", CultureInfo.InvariantCulture)}";
        if (!string.IsNullOrEmpty(ils.Ident)) said += $", {ils.Ident}";
        if (ils.FrontCourse is int course) said += $", front course {course.ToString("000", CultureInfo.InvariantCulture)}";
        return said;
    }
}
