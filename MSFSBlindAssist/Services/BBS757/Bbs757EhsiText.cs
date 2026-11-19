using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// What the 757 EHSI window lists, what the reader says by itself and the Ctrl+W line, from one reading (plan 26). Only
/// what the screen draws (measured on BlueBird's draw; docs/invariants/b757-ehsi.md): a map item only inside BlueBird's own clip
/// (<see cref="Bbs757EhsiGeometry.OnScreen"/>), rows top to bottom, BlueBird's printed words and formats ("NM", the
/// top line's "z", the DATA ETA's "Z", "HDG", "TRU", "EXCESS DATA"), every number culture-invariant. Pure, and never throws
/// for any reading a sampler returns, a corrupt one included. The map's constants and BlueBird's words come with the
/// reading (<see cref="Bbs757EhsiReading.Constants"/>, <see cref="Bbs757EhsiReading.Words"/>, the layout's); what the
/// screen does in each mode follows BlueBird's mode value (<see cref="Bbs757EhsiReading.ModeValue"/>), and the mode is
/// shown by the layout's word (<see cref="Bbs757EhsiReading.Mode"/>).
/// </summary>
public static partial class Bbs757EhsiText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Not a layout word: the "---" of a heading not valid while aligning is the top line's own dashes.
    private const string NoNumber = "---";

    private const string Unavailable = "EHSI waypoint unavailable: ";
    private const string NoReadingYet = "no reading yet";
    private const string DisplayDark = "display dark";
    private const string NoActiveWaypoint = "No active waypoint";
    private const string DiscontinuityName = "discontinuity";
    private const string UnnamedWaypoint = "unnamed waypoint";

    /// <summary>The one map row of a light reading (no route read) where the map rows would be.</summary>
    private const string MapReading = "Map: reading";

    /// <summary>Half a degree: within it of straight ahead (or behind) the bug reads "ahead" (or "behind"), as a place does.</summary>
    private const double AlignedDegrees = 0.5;

    /// <summary>Under this distance from the map's center a PLAN leg names the center (the screen's one decimal rounds it
    /// to 0.0).</summary>
    private const double CenterNm = 0.05;

    // ---- the rows ----

    /// <summary>
    /// The window's rows, top to bottom, each only when the screen draws it:
    /// <list type="number">
    /// <item>Not powered: "EHSI: dark", alone.</item>
    /// <item>"EHSI: MAP, range 160" (", centered" when centered); centered VOR and ILS, whose full rose draws no range
    /// label, "EHSI: VOR, centered".</item>
    /// <item>Valid: "Top: HDG 119 M" (TRK with track up, TRU for a true reference). Not valid: "Top: ---" while aligning,
    /// then "Flag: HDG" (or TRK), and in MAP "Flag: MAP". While the range changes, nothing more.</item>
    /// <item>VOR and ILS (plan 28's approach part, <see cref="VorIlsRows"/>): "Top left: DME 0.5", "Top right: 1533.3z"
    /// (or "no ETA"; expanded only), the TCAS block (<see cref="TcasRows"/>), the rose's rows, the bearing pointers and,
    /// while valid, the heading bug, and nothing more. MAP and PLAN: "Top left: 37.5 NM" (or "no distance") and "Top
    /// right: 1533.3z" (or "no ETA"), then the TCAS block.</item>
    /// <item>Plan 28's map extras (<see cref="TerrRows"/>, <see cref="WindRow"/>): TERR's rows after the top line, before
    /// the TCAS block; the wind in VOR and ILS among the bottom rows by its place (before TO/FROM, the LOC flag, the
    /// receiver's label and the ILS frequency), and in MAP after the map's rows and the bearing pointers; the runways and
    /// the deceleration points among the map's items. The map guides
    /// (<see cref="AddMapGuides"/>) among the map's items too, and in MAP the VNAV path deviation right after them.</item>
    /// <item>A part of plan 28's not read, with a reason: "TCAS: not read: not in this layout file", one row where the
    /// part's rows would start, only in the modes where it shows something.</item>
    /// <item>In MAP while valid or in PLAN, a light reading (no route read): "Map: reading", alone. A full reading lists
    /// its map rows (none when nothing is on the map).</item>
    /// <item>Map rows, in a full reading, in MAP while valid or in PLAN, by their place on the screen (top to bottom, left to
    /// right): each leg with a symbol ("Map: KOLM, active, 37.5 NM, 87° right"; an inactive route's "Map, inactive route: ",
    /// a pending modification's "Map, modification: "; with DATA on its constraint and ETA as drawn), and the points
    /// within range + the margin ("Map: T/C, …", "Map: KSEA airport, …"). In PLAN the place is from the leg at the map's
    /// center ("13.4 NM north-east of KOLM"), else "the center". A discontinuity draws nothing. Plan 28's map options among
    /// them (the tuned stations and the items BlueBird's loop draws), and in MAP the TCAS traffic; then the VOR course
    /// lines.</item>
    /// <item>MAP while valid: the bearing pointers.</item>
    /// <item>Valid: "Heading bug: 147, 28° right".</item>
    /// <item>"Message: EXCESS DATA" at the layout's count, else "Messages: none".</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> Rows(Bbs757EhsiReading r)
    {
        if (!r.Powered) return new[] { "EHSI: dark" };
        var w = r.Words;
        // The full VOR and ILS rose draws no range label (MAP's half-range label is drawn in every other view): no range.
        bool roseOnly = r.Centered && (Is(r, Bbs757EhsiMode.Vor) || Is(r, Bbs757EhsiMode.Ils));
        var rows = new List<string>
        {
            "EHSI: " + ModeWord(r) + (roseOnly ? "" : ", range " + Number(r.Range)) + (r.Centered ? ", centered" : ""),
        };
        if (r.Valid)
            rows.Add("Top: " + HeadingWord(r) + " " + ThreeDigits(r.Number) + " " + Word(r.TrueReference ? w.True : w.Mag));
        else
        {
            if (r.Aligning) rows.Add("Top: " + NoNumber);
            rows.Add("Flag: " + HeadingWord(r));
            if (Is(r, Bbs757EhsiMode.Map)) rows.Add("Flag: " + Word(w.Map));
        }
        if (r.RangeChanging) return rows;
        double? distance = Distance(r);
        if (Is(r, Bbs757EhsiMode.Vor) || Is(r, Bbs757EhsiMode.Ils))
        {
            VorIlsRows(rows, r, distance);
            return rows;
        }

        rows.Add("Top left: " + (distance is { } nm ? Bbs757EhsiGeometry.TopLineText(nm) + " " + Word(w.Nm) : "no distance"));
        rows.Add(EtaRow(r, distance));
        TerrRows(rows, r);
        NotRead(rows, MapExtrasPart, r.MapExtrasMissing);
        TcasRows(rows, r);
        bool mapDrawn = MapDrawn(r);
        if (mapDrawn) NotRead(rows, MapOptionsPart, r.MapOptionsMissing);
        if (mapDrawn) NotRead(rows, MapGuidesPart, r.MapGuidesMissing);
        rows.AddRange(MapRows(r));
        VnavDeviationRow(rows, r);
        // The bearing pointers are drawn in MAP while the IRS is valid (never in PLAN).
        if (Is(r, Bbs757EhsiMode.Map) && r.Valid)
        {
            if (r.Approach is { } a) BearingPointerRows(rows, r, a);
            else NotRead(rows, ApproachPart, r.ApproachMissing);
        }
        WindRow(rows, r);
        if (r.Valid) rows.Add(HeadingBugRow(r));
        rows.Add(Excess(r) ? "Message: " + Word(w.Excess) : "Messages: none");
        return rows;
    }

    /// <summary>The top line's ETA: "Top right: 1533.3z", or "Top right: no ETA" where it draws its dashes (under the
    /// minimum ground speed, or no distance).</summary>
    private static string EtaRow(Bbs757EhsiReading r, double? distance) =>
        "Top right: " + (Eta(r, distance) is { } eta ? eta + Word(r.Words.EtaSuffix) : "no ETA");

    private static string HeadingBugRow(Bbs757EhsiReading r) =>
        "Heading bug: " + ThreeDigits(r.HeadingBug) + ", " + Relative(r.HeadingBug, r.Number);

    /// <summary>A layout word, empty when the reading carries none (a default reading).</summary>
    private static string Word(string? word) => word ?? "";

    private static bool Is(Bbs757EhsiReading r, Bbs757EhsiMode mode) => r.ModeValue == (int)mode;

    private static string ModeWord(Bbs757EhsiReading r) => r.Mode ?? Bbs757WordTable.Unrecognised;

    private static string HeadingWord(Bbs757EhsiReading r) => Word(r.TrackUp ? r.Words.Trk : r.Words.Hdg);

    /// <summary>BlueBird's <c>round % 360</c> in three digits ("119"; 360 → "000"); not a number: "---".</summary>
    private static string ThreeDigits(double value)
    {
        if (!double.IsFinite(value)) return NoNumber;
        double whole = Math.Round(value, MidpointRounding.AwayFromZero) % 360.0;
        if (whole < 0) whole += 360.0;
        return ((int)whole).ToString("D3", Inv);
    }

    private static string Number(double value) => value.ToString("0.##", Inv);

    /// <summary>The top line's distance to the active leg, or null where it draws dashes (no leg, or a VM or FM leg). A
    /// discontinuity is at 0° N 0° E and the screen prints the distance there: so does this.</summary>
    private static double? Distance(Bbs757EhsiReading r)
    {
        if (r.Active is not { } a || a.Manual) return null;
        double nm = Bbs757EhsiGeometry.TopLineDistance(r.Latitude, r.Longitude, a.Latitude, a.Longitude);
        return double.IsFinite(nm) ? nm : null;
    }

    private static string? Eta(Bbs757EhsiReading r, double? distance) =>
        distance is { } nm ? Bbs757EhsiGeometry.Eta(r.Clock, nm, r.GroundSpeed, r.Constants.MinEtaGroundSpeed) : null;

    /// <summary>"28° right" from <paramref name="from"/> to <paramref name="to"/> folded to ±180, "ahead" or "behind" within
    /// half a degree; "unknown" for a value that is not a number.</summary>
    private static string Relative(double to, double from)
    {
        if (!double.IsFinite(to) || !double.IsFinite(from)) return "unknown";
        double off = Bbs757EhsiGeometry.OffTop(to, from);
        double abs = Math.Abs(off);
        if (abs <= AlignedDegrees) return "ahead";
        if (abs >= 180.0 - AlignedDegrees) return "behind";
        return Math.Round(abs, MidpointRounding.AwayFromZero).ToString("F0", Inv) + "° " + (off < 0 ? "left" : "right");
    }

    private static bool MapOrPlan(Bbs757EhsiReading r) => Is(r, Bbs757EhsiMode.Map) || Is(r, Bbs757EhsiMode.Plan);

    /// <summary>EXCESS DATA: drawn in MAP and PLAN when the frame's count of background items reaches the layout's limit.</summary>
    private static bool Excess(Bbs757EhsiReading r) =>
        r.Powered && MapOrPlan(r) && r.ExcessCount >= r.Constants.ExcessLimit;

    // ---- the map ----

    private readonly record struct MapItem(double Y, double X, string Text);

    private static IEnumerable<string> MapRows(Bbs757EhsiReading r)
    {
        bool plan = Is(r, Bbs757EhsiMode.Plan);
        if (!MapDrawn(r)) return Array.Empty<string>();
        // A light reading holds no route: the map is drawn but not read yet (the window's first second).
        if (r.Route is null) return new[] { MapReading };

        var items = new List<MapItem>();
        string? center = plan ? CenterName(r) : null;

        // The route's legs: "active" marks the route's own leg 0 (by its index: a flown copy of it is not active) while the
        // route is active, a modification pending or not: the screen keeps the active route magenta with its leg 0
        // magenta and draws a pending modification white dashed beside it (measured). The modification's legs are never
        // marked active.
        for (int i = 0; i < r.Route.Count; i++)
        {
            bool active = r.RouteActive && i == r.ActiveIndex;
            AddLeg(items, r, r.Route[i], r.RouteActive ? "Map: " : "Map, inactive route: ", active, center);
        }
        foreach (var leg in r.Modification ?? Array.Empty<Bbs757EhsiLeg>())
            AddLeg(items, r, leg, "Map, modification: ", false, center);

        foreach (var point in r.Points ?? Array.Empty<Bbs757EhsiPoint>())
        {
            if (Where(r, point.Latitude, point.Longitude, center) is not { } at) continue;
            if (!(at.Nm <= r.Range + r.Constants.MarkerMargin)) continue;
            string name = point.Airport ? point.Word + " airport" : point.Word;
            items.Add(new MapItem(at.Y, at.X, "Map: " + name + ", " + at.Place));
        }

        // Plan 28's map options and traffic (Bbs757EhsiText.MapOptions.cs, .Tcas.cs), among the map's items by their place.
        AddMapOptions(items, r, center);
        AddMapExtras(items, r, center);
        AddMapGuides(items, r, center);
        AddTraffic(items, r);

        // Top to bottom, then left to right; OrderBy is stable, so one spot keeps the draw's order. The course lines, which
        // cross the screen, after them, then a fix the reader cannot place.
        return items.OrderBy(i => i.Y).ThenBy(i => i.X).Select(i => i.Text).Concat(CourseLineRows(r))
            .Concat(UnplacedGuideRows(r)).ToArray();
    }

    private static void AddLeg(List<MapItem> items, Bbs757EhsiReading r, Bbs757EhsiLeg leg, string prefix, bool active, string? center)
    {
        if (!leg.Symbol || leg.Discontinuity) return;
        if (Where(r, leg.Latitude, leg.Longitude, center) is not { } at) return;
        string text = prefix + LegName(leg) + (active ? ", active" : "") + ", " + at.Place;
        if (r.DataSwitch)
        {
            if (Constraint(leg) is { } constraint) text += ", " + constraint;
            if (DataEta(leg.Eta) is { } eta) text += ", " + eta;
        }
        items.Add(new MapItem(at.Y, at.X, text));
    }

    private static string LegName(Bbs757EhsiLeg leg) => leg.Label.Length > 0 ? leg.Label : UnnamedWaypoint;

    /// <summary>Where an item is: its pixel, its distance from the map's center and its place in words; null when the clip
    /// cuts it.</summary>
    private static (double X, double Y, double Nm, string Place)? Where(Bbs757EhsiReading r, double lat, double lon, string? center)
    {
        var c = r.Constants;
        var (x, y, nm, bearing) = Bbs757EhsiGeometry.Project(r.Map, r.Range, c.RadiusFactor, lat, lon);
        if (!Bbs757EhsiGeometry.OnScreen(r.Map, r.Centered, x, y, c.RadiusFactor, c.AircraftYFactor, c.ClipInset)) return null;
        string place = center is not null
            ? Bbs757EhsiGeometry.PlanPlace(nm, bearing, center)
            : Bbs757EhsiGeometry.Place(nm, Bbs757EhsiGeometry.OffTop(bearing, r.Map.Rotation));
        return (x, y, nm, place);
    }

    /// <summary>PLAN's center by name: the first leg (the route's, then the modification's) at the map's center, else
    /// "the center".</summary>
    private static string CenterName(Bbs757EhsiReading r)
    {
        foreach (var leg in (r.Route ?? Array.Empty<Bbs757EhsiLeg>()).Concat(r.Modification ?? Array.Empty<Bbs757EhsiLeg>()))
        {
            if (leg.Discontinuity || leg.Label.Length == 0) continue;
            var (_, _, nm, _) = Bbs757EhsiGeometry.Project(r.Map, 1, 1, leg.Latitude, leg.Longitude);
            if (nm < CenterNm) return leg.Label;
        }
        return "the center";
    }

    // The DATA switch's constraint and ETA texts: Bbs757EhsiText.Data.cs.

    // ---- what is spoken ----

    /// <summary>One flag or message on the screen: <see cref="Key"/> tells it apart (the heading flag is one flag whether
    /// it says HDG or TRK), <see cref="Word"/> is what the screen prints.</summary>
    private readonly record struct Item(string Key, string Word);

    private const string HeadingFlag = "heading", MapFlag = "map", ExcessMessage = "excess";

    /// <summary>The flags drawn, only while powered: the heading (or track) flag while not valid, and in MAP the MAP flag;
    /// then plan 28's ILS flags (LOC, G/S).</summary>
    private static List<Item> Flags(Bbs757EhsiReading r)
    {
        var flags = new List<Item>(4);
        if (!r.Powered) return flags;
        if (!r.Valid)
        {
            flags.Add(new Item(HeadingFlag, HeadingWord(r)));
            if (Is(r, Bbs757EhsiMode.Map)) flags.Add(new Item(MapFlag, Word(r.Words.Map)));
        }
        ApproachFlags(flags, r);
        return flags;
    }

    /// <summary>The messages drawn: EXCESS DATA, then plan 28's TCAS messages.</summary>
    private static List<Item> Messages(Bbs757EhsiReading r)
    {
        var messages = Excess(r) ? new List<Item> { new(ExcessMessage, Word(r.Words.Excess)) } : new List<Item>();
        TcasMessages(messages, r);
        return messages;
    }

    /// <summary>
    /// What changed from <paramref name="heard"/> to <paramref name="now"/>, all on the one Ctrl+M row
    /// (<see cref="Bbs757EhsiDisplay.MuteKey"/>), in this order; the words are the same with or without
    /// <paramref name="withPrefix"/> ("EHSI" names the display):
    /// <list type="number">
    /// <item>"EHSI message: EXCESS DATA", "EHSI message gone: EXCESS DATA"; plan 28's TCAS FAIL, TA ONLY, TCAS OFF and
    /// TRAFFIC the same way.</item>
    /// <item>"EHSI flag: HDG, MAP", "EHSI flag gone: HDG, MAP" (each change of one settle joined); plan 28's LOC and G/S
    /// the same way.</item>
    /// <item>The active waypoint's label changing, while both are powered: "EHSI: KOLM, 37 NM" (whole NM, truncated;
    /// without the distance where the top line draws dashes, and in VOR and ILS, where it shows DME), or
    /// "EHSI: no active waypoint".</item>
    /// </list>
    /// A flag or message that comes or goes only because the mode changed (the MAP flag, EXCESS DATA, LOC and G/S, which
    /// some modes do not draw; TCAS FAIL and TCAS OFF, which the nd's gate holds back in some, and a failed TCAS's TA ONLY,
    /// which shows where the gate holds FAIL back) is not spoken: the pilot
    /// turned the selector, and the window shows it. TRAFFIC is not compared while either reading has the TCAS self-test
    /// on (the test sets the RA byte, so red TRAFFIC shows for its ten seconds). A part's flags or messages are compared only
    /// while that part is read in both readings: one going null at its tear limit, or coming back, says nothing (the TCAS
    /// words with <see cref="Bbs757EhsiReading.Tcas"/>, LOC and G/S with <see cref="Bbs757EhsiReading.Approach"/>).
    /// Nothing else is ever spoken: no mode, range, CTR, DATA, heading, bug,
    /// route edit, map item, traffic, TFC or TCAS TEST. Going dark or lit is the monitor's.
    /// </summary>
    public static IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757EhsiReading heard, Bbs757EhsiReading now, bool withPrefix)
    {
        var lines = new List<Bbs757SpokenLine>();
        bool sameMode = heard.ModeValue == now.ModeValue;
        // The TCAS messages the nd's gate holds back come and go with the EFIS mode alone: compared only while the gate
        // is the same in both.
        bool sameGate = heard.Tcas?.MessagesAllowed == now.Tcas?.MessagesAllowed;
        // A failed TCAS shows TA ONLY only while the gate is clear (FAIL hides it): then TA ONLY comes and goes with the
        // gate too.
        bool eitherFailed = (heard.Tcas is { } h && Failed(h)) || (now.Tcas is { } n && Failed(n));
        // The TCAS self-test sets the RA byte for its ten seconds (red TRAFFIC): the test is the pilot's own, not spoken.
        bool eitherTesting = heard.Tcas?.Test == true || now.Tcas?.Test == true;
        // A part not read in either reading (at its tear limit, missing, or not read in that mode) says nothing of its
        // flags or messages: the screen did not change, the reader lost sight of it. When it reads again its state is the
        // new baseline, silently (the monitor advances its baseline when nothing is spoken).
        bool tcasInBoth = heard.Tcas is not null && now.Tcas is not null;
        bool approachInBoth = heard.Approach is not null && now.Approach is not null;
        bool Comparable(Item item) => item.Key switch
        {
            LocFlagKey or GlideslopeFlagKey => sameMode && approachInBoth,
            MapFlag => sameMode,
            ExcessMessage => sameMode || (MapOrPlan(heard) && MapOrPlan(now)),
            TcasFailKey or TcasOffKey => sameGate && tcasInBoth,
            TaOnlyKey => (sameGate || !eitherFailed) && tcasInBoth,
            TrafficKey => !eitherTesting && tcasInBoth,
            _ => true,
        };
        Diff(lines, Messages(heard), Messages(now), "EHSI message: ", "EHSI message gone: ", Comparable);
        Diff(lines, Flags(heard), Flags(now), "EHSI flag: ", "EHSI flag gone: ", Comparable);

        if (heard.Powered && now.Powered
            && !string.Equals(heard.Active?.Label, now.Active?.Label, StringComparison.Ordinal))
            lines.Add(Spoken("EHSI: " + WaypointChange(now)));
        return lines;
    }

    private static void Diff(List<Bbs757SpokenLine> lines, List<Item> before, List<Item> after, string newLine, string goneLine,
        Func<Item, bool> comparable)
    {
        var added = after.Where(i => comparable(i) && !before.Any(b => b.Key == i.Key)).Select(i => i.Word).ToList();
        var gone = before.Where(i => comparable(i) && !after.Any(a => a.Key == i.Key)).Select(i => i.Word).ToList();
        if (added.Count > 0) lines.Add(Spoken(newLine + string.Join(", ", added)));
        if (gone.Count > 0) lines.Add(Spoken(goneLine + string.Join(", ", gone)));
    }

    private static Bbs757SpokenLine Spoken(string text) => new(Bbs757EhsiDisplay.MuteKey, text);

    private static string WaypointChange(Bbs757EhsiReading r)
    {
        if (r.Active is not { } a) return "no active waypoint";
        string name = WaypointName(a);
        if (!MapOrPlan(r) || Distance(r) is not { } nm) return name;
        return name + ", " + Math.Truncate(nm).ToString("F0", Inv) + " " + Word(r.Words.Nm);
    }

    /// <summary>The active leg's name as the pilot hears it: its label, "discontinuity" for a discontinuity or an empty
    /// label.</summary>
    private static string WaypointName(Bbs757EhsiLeg a) =>
        a.Discontinuity || a.Label.Length == 0 ? DiscontinuityName : a.Label;

    // The Ctrl+W line: Bbs757EhsiText.WaypointLine.cs.
}
