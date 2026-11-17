namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The VOR and ILS picture's rows and flags (plan 28), from <see cref="Bbs757EhsiReading.Approach"/>: what BlueBird draws in
/// VOR and ILS mode, in screen order, BlueBird's words (the layout's), every number culture-invariant. The screen prints no
/// course number: the course pointer's row gives the course, as the rose shows it, and where it points against the
/// heading number. A value that is not a number draws nothing of its own (never a throw).
/// </summary>
public static partial class Bbs757EhsiText
{
    /// <summary>Under this many dots a deviation reads "centered" (the screen's one decimal would print 0.0).</summary>
    private const double CenteredDots = 0.05;

    private const string LocFlagKey = "loc", GlideslopeFlagKey = "glideslope";

    /// <summary>
    /// The rows of VOR and ILS mode after the mode and the top line's heading (<see cref="Rows"/>): the DME at the top left
    /// (or the part's not-read row), plan 26's ETA at the top right (expanded only: the full rose draws none), TERR's rows
    /// (or the map extras' not-read row), the TCAS block, the rose's rows (<see cref="ApproachRows"/>), the bottom rows by
    /// their place (the wind at 0.895 H on the left, then <see cref="ApproachBottomRows"/>), the bearing pointers, then
    /// plan 26's heading bug.
    /// </summary>
    private static void VorIlsRows(List<string> rows, Bbs757EhsiReading r, double? distance)
    {
        if (r.Approach is { } a) rows.Add(DmeRow(r, a));
        else NotRead(rows, ApproachPart, r.ApproachMissing);
        if (!r.Centered) rows.Add(EtaRow(r, distance));
        TerrRows(rows, r);
        NotRead(rows, MapExtrasPart, r.MapExtrasMissing);
        TcasRows(rows, r);
        if (r.Approach is { } rose) ApproachRows(rows, r, rose);
        WindRow(rows, r);
        if (r.Approach is { } bottom) ApproachBottomRows(rows, r, bottom);
        if (r.Approach is { } pointers) BearingPointerRows(rows, r, pointers);
        if (r.Valid) rows.Add(HeadingBugRow(r));
    }

    /// <summary>The top line's left field in VOR and ILS: "Top left: DME 12.3" (one decimal under 100, the integer from
    /// 100, as drawn) or the screen's dashes, "Top left: DME ---". ILS mode shows the ILS DME, VOR mode the VOR's.</summary>
    private static string DmeRow(Bbs757EhsiReading r, Bbs757EhsiApproachReading a)
    {
        bool ils = Is(r, Bbs757EhsiMode.Ils);
        bool valid = ils ? a.IlsDmeValid : a.VorDmeValid && double.IsFinite(a.VorDme) && a.VorDme >= 0;
        double nm = ils ? a.IlsDme : a.VorDme;
        return "Top left: " + Word(a.Words.Dme) + " " + (valid ? Bbs757EhsiGeometry.TopLineText(nm) : Word(a.Words.NoDme).Trim());
    }

    /// <summary>
    /// The rose's rows, top to bottom: the course pointer ("Center: course pointer 187, 68° right"), the deviation
    /// ("Center: deviation 0.5 dots right", "centered", "at full scale, right"; "Center: no deviation bar" while the bar is
    /// not drawn), ILS without its dots "Center: no deviation scale" as a row of its own (with them, during the ILS
    /// self-test, "Center: deviation scale amber"), in ILS while the glideslope scale shows "Right side: glideslope 1.2
    /// dots above" (or "no glideslope pointer"; during its test "Right side: glideslope scale amber" after it), and ILS's
    /// G/S flag in place ("Right side:
    /// G/S flag", above the scale's center). The bottom rows are <see cref="ApproachBottomRows"/>'.
    /// </summary>
    private static void ApproachRows(List<string> rows, Bbs757EhsiReading r, Bbs757EhsiApproachReading a)
    {
        var w = a.Words;
        bool ils = Is(r, Bbs757EhsiMode.Ils);
        double course = ils ? a.IlsCourse : a.VorCourse;
        rows.Add("Center: course pointer " + ThreeDigits(course) + ", " + Relative(course, r.Number));

        bool bar = ils ? a.LocalizerBar : a.VorBar;
        double deviation = ils ? a.LocalizerDeviation : a.VorDeviation;
        // The bar follows its own byte alone; ILS's dots are a byte of their own, their absence a row of its own.
        if (bar && double.IsFinite(deviation))
            rows.Add("Center: deviation " + Deflection(deviation, a.DeviationFullScale, a.DotsPerUnit, "right", "left"));
        else rows.Add("Center: no deviation bar");
        if (ils && !a.IlsDots) rows.Add("Center: no deviation scale");
        // The ILS self-test colors the dots amber: no other row names the scale while it is drawn.
        else if (ils && a.IlsTest) rows.Add("Center: deviation scale amber");

        if (ils)
        {
            if (a.GlideslopeScale)
            {
                rows.Add(a.GlideslopePointer && double.IsFinite(a.GlideslopeDeviation)
                    ? "Right side: glideslope " + Deflection(a.GlideslopeDeviation, a.GlideslopePin, 1, "above", "below")
                    : "Right side: no glideslope pointer");
                if (a.GlideslopeTest) rows.Add("Right side: glideslope scale amber");
            }
            if (a.GlideslopeFlag) rows.Add("Right side: " + Word(w.Glideslope) + " flag");
        }
    }

    /// <summary>
    /// The bottom rows after the wind (which sits at the bottom left of the same height as TO/FROM, so before it), top to
    /// bottom then left to right as the evidence measures them: VOR's "Bottom right: TO" or "FROM" (0.895 H), ILS's LOC
    /// flag ("Bottom: LOC flag", 0.9 H), the receiver's label ("Bottom left: VOR L", "VOR R", "ILS") and ILS's frequency
    /// ("Bottom right: 110.30", or the dashes), both 31 px lower.
    /// </summary>
    private static void ApproachBottomRows(List<string> rows, Bbs757EhsiReading r, Bbs757EhsiApproachReading a)
    {
        var w = a.Words;
        bool ils = Is(r, Bbs757EhsiMode.Ils);
        if (ils)
        {
            if (a.LocFlag) rows.Add("Bottom: " + Word(w.Loc) + " flag");
        }
        else if (a.ToFrom != Bbs757EhsiToFrom.None)
            rows.Add("Bottom right: " + Word(a.ToFrom == Bbs757EhsiToFrom.To ? w.To : w.From));

        rows.Add("Bottom left: " + Word(ils ? w.Ils : a.CaptainSide ? w.VorLeft : w.VorRight));
        if (ils)
            rows.Add("Bottom right: " + (a.IlsFrequency is { } f ? f.ToString("F2", Inv) : Word(w.NoFrequency)));
    }

    /// <summary>A deviation in words: "at full scale, right" at or past <paramref name="fullScale"/>, else its dots
    /// (<paramref name="value"/> × <paramref name="dotsPerUnit"/>) to one decimal ("0.5 dots right"), "centered" under
    /// <see cref="CenteredDots"/>. A positive value is <paramref name="positive"/>.</summary>
    private static string Deflection(double value, double fullScale, double dotsPerUnit, string positive, string negative)
    {
        string side = value < 0 ? negative : positive;
        double abs = Math.Abs(value);
        if (abs >= fullScale) return "at full scale, " + side;
        double dots = abs * dotsPerUnit;
        if (dots < CenteredDots) return "centered";
        return dots.ToString("F1", Inv) + " dots " + side;
    }

    /// <summary>
    /// The bearing pointers, every mode but PLAN while the IRS is valid: "Bearing pointer 1: VOR L, SEA, 74° right" (or
    /// "ahead", "behind": the angle only, as the screen draws an arrow and no distance; the station's bearing from the
    /// map's center against its rotation, unclipped: the pointer points at it wherever it is), "Bearing pointer 2: ADF R"
    /// for an ADF the layout gives no position for. No row for a pointer with no station. The expanded view clips the
    /// pointer to the area above the aircraft (rect 0, 0, W, the center's y); the row is listed regardless, as the pointer
    /// runs through the center and its head or its tail always shows there.
    /// </summary>
    private static void BearingPointerRows(List<string> rows, Bbs757EhsiReading r, Bbs757EhsiApproachReading a)
    {
        if (Is(r, Bbs757EhsiMode.Plan) || !r.Valid) return;
        AddPointer(rows, r, 1, a.Pointer1);
        AddPointer(rows, r, 2, a.Pointer2);
    }

    private static void AddPointer(List<string> rows, Bbs757EhsiReading r, int number, Bbs757EhsiBearingPointer? pointer)
    {
        if (pointer is not { } p) return;
        string text = "Bearing pointer " + number.ToString(Inv) + ": " + p.Source;
        if (!string.IsNullOrEmpty(p.Ident)) text += ", " + p.Ident;
        if (p.Latitude is { } lat && p.Longitude is { } lon)
        {
            var (_, _, _, bearing) = Bbs757EhsiGeometry.Project(r.Map, 1, 1, lat, lon);
            text += ", " + Bbs757EhsiGeometry.Angle(Bbs757EhsiGeometry.OffTop(bearing, r.Map.Rotation));
        }
        rows.Add(text);
    }

    /// <summary>ILS mode's flags: LOC and G/S, by the screen's words.</summary>
    private static void ApproachFlags(List<Item> flags, Bbs757EhsiReading r)
    {
        if (!Is(r, Bbs757EhsiMode.Ils) || r.Approach is not { } a) return;
        if (a.LocFlag) flags.Add(new Item(LocFlagKey, Word(a.Words.Loc)));
        if (a.GlideslopeFlag) flags.Add(new Item(GlideslopeFlagKey, Word(a.Words.Glideslope)));
    }
}
