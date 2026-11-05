using System.Globalization;
using G = MSFSBlindAssist.Services.PMDG.Pmdg737NavDisplayGeometry;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The Captain's navigation display as list rows, in screen order (header, top left, top centre, top right,
/// left side, map, right side, bottom, bottom corners, messages), each starting with where it sits and
/// carrying the ND's own printed text. A row exists only where PMDG's ND draws it at
/// this mode, format and CTR (2026-10-25), and the map lists only what falls inside PMDG's clip.
/// Formatting and gating of the raw sample happen here; numbers print in the invariant culture. Pure.
/// </summary>
public static partial class Pmdg737NavDisplayText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The bottom corners' VOR/ADF switch positions PMDG tests (the SDK's
    /// EFIS_VORADFSel order), and the VOR state from which it prints no DME distance (an ILS tuned).</summary>
    private const int VorSwitch = 0, AdfSwitch = 2, DmeShownBelowState = 3;

    /// <summary>The app's words for the traffic's four threat levels (PMDG draws shapes, no words).</summary>
    private static readonly string[] ThreatWords = { "other traffic", "proximate traffic", "traffic advisory", "resolution advisory" };

    /// <summary>Every row, in screen order.</summary>
    public static IReadOnlyList<string> Lines(NavDisplaySample s, Pmdg737NavDisplayLayout l) => Build(s, l).Rows;

    /// <summary>The ND's messages as it prints them, in list order (empty while the ND is off or not shown).</summary>
    public static IReadOnlyList<string> Messages(NavDisplaySample s, Pmdg737NavDisplayLayout l) => Build(s, l).Messages;

    /// <summary>Whether a lit display unit draws the Captain's ND.</summary>
    public static bool Shown(NavDisplaySample s, Pmdg737NavDisplayLayout l) =>
        Pmdg737NavDisplayCrossCheck.DisplaysLit(s, l) && CaptainNd(s, l).Shown;

    /// <summary>The top-right corner as printed (name, ETA, distance to go), also Output Ctrl+W's answer; null
    /// with no TO waypoint (PMDG then prints only "------Z" and "--- NM").</summary>
    public static string? TopRight(NavDisplaySample s, Pmdg737NavDisplayLayout l)
    {
        if (s.Active is not { } w) return null;
        bool data = w.LegType != l.Route.NoDataLegType;
        return $"{w.Name}, {Eta(data ? w.Eta : null, l.Route.Clock)}, {(data ? Distance(w.Distance) : "---")} NM";
    }

    /// <summary>A distance as the ND prints it: tenths below 99.95 NM, whole miles above.</summary>
    public static string Distance(double nm) => nm < 99.95 ? nm.ToString("F1", Inv) : ((int)(nm + 0.5)).ToString(Inv);

    /// <summary>An ETA as the top-right corner prints it: "HHMM.m" (the minute's tenths) and "Z"; "------Z" with none.</summary>
    public static string Eta(NdClock? c, NdClockFields fields)
    {
        if (c is not { } t) return "------Z";
        // PMDG rounds s / 60 to one decimal, half up, and prints its cap while the tenths round above it (57-59 s).
        int tenths = Math.Min((int)Math.Round(Math.Round(t.Seconds / 60.0, 1, MidpointRounding.AwayFromZero) * 10), fields.TenthsAtMost);
        return Invariant($"{t.Hours:00}{t.Minutes:00}.{tenths}Z");
    }

    /// <summary>Whole degrees off the top of the map: "20° right", "104° left", "ahead".</summary>
    public static string Direction(double relativeDeg)
    {
        int d = (int)Math.Round(relativeDeg, MidpointRounding.AwayFromZero);
        return d == 0 ? "ahead" : d > 0 ? Invariant($"{d}° right") : Invariant($"{-d}° left");
    }

    // ---- the whole list ----

    private sealed record Built(IReadOnlyList<string> Rows, IReadOnlyList<string> Messages);

    /// <summary>What every row needs: the reading, the layout, whether the Captain's ND is in its full format,
    /// the view and the selected range.</summary>
    private sealed class Context(NavDisplaySample s, Pmdg737NavDisplayLayout l, bool full)
    {
        public NavDisplaySample S { get; } = s;
        public Pmdg737NavDisplayLayout L { get; } = l;
        public bool Full { get; } = full;
        public NdView View { get; } = G.ViewOf(s.Panel, l.Geometry.Modes);
        public bool Plan => View == NdView.Plan;
        public bool Map => S.Panel.Mode == L.Geometry.Modes.Map;
        public bool MapOrPlan => Map || Plan;
        public double RangeNm { get; } = G.SelectedRangeNm(s.Panel, l.Geometry);
        public NdViewport? Viewport => G.Viewport(View, L.Geometry);
    }

    private static Built Build(NavDisplaySample s, Pmdg737NavDisplayLayout l)
    {
        if (!Pmdg737NavDisplayCrossCheck.DisplaysLit(s, l)) return new(new[] { "Captain ND: off" }, Array.Empty<string>());
        var (shown, full) = CaptainNd(s, l);
        if (!shown) return new(new[] { "Captain ND: not on any display" }, Array.Empty<string>());

        var c = new Context(s, l, full);
        var modes = l.Geometry.Modes;
        var rows = new List<string> { Header(c), "Top left: " + TopLeft(c) };
        if (!c.Plan && TopCentre(c) is { } centre) rows.Add("Top centre: " + centre);
        // PLN: the compass letter PMDG draws in the readout's place, its arrow's lines written as ↑.
        if (c.Plan) rows.Add("Top centre: " + l.Orientation.PlanCompass.North + "↑");
        if (c.MapOrPlan)
        {
            rows.Add("Top right: " + (TopRight(s, l) ?? "------Z, --- NM"));
            var labels = Holding(l.Messages.MapOptionLabels, c);
            // PMDG's terrain block returns before the radar lines, so none is listed once it is entered, numbers or not.
            if (TerrainLines(c, labels) is { } terrain) labels.AddRange(terrain);
            else if (labels.Contains(l.Messages.Wxr.Label)) labels.AddRange(WxrLines(c));
            if (labels.Count > 0) rows.Add("Left side: " + string.Join(", ", labels));
        }
        // PLN: PMDG's other compass letters, at the screen's left and right edges at the centre's height
        // and at its bottom centre.
        var compass = l.Orientation.PlanCompass;
        if (c.Plan) rows.Add("Left side: " + compass.West);
        bool approach = s.Panel.Mode == modes.App || s.Panel.Mode == modes.Vor;
        if (approach) rows.AddRange(ApproachRows(c));
        var map = c.MapOrPlan ? MapRows(c) : approach ? ApproachTraffic(c) : new MapResult(Array.Empty<string>(), false, false);
        rows.AddRange(map.Rows);
        if (Xtk(c) is { } xtk) rows.Add("Below the aircraft: XTK " + xtk);
        if (c.Plan) rows.Add("Right side: " + compass.East);
        if (c.Map && Vdev(c) is { } vdev) rows.Add("Right side: " + vdev);
        // PMDG's failure flag text in the VDEV scale's place (VTK, amber, its gate MAP's), listed there.
        var vdevFlag = l.Messages.Vdev.Flag is { } f && Holding(new[] { f }, c).Count > 0 ? f.Text : null;
        if (vdevFlag is not null) rows.Add("Right side: " + FailedLabel(vdevFlag, l.Messages.Vdev.FlagBoxed));
        if (c.Map && s.RnpShown) rows.Add("Bottom: " + Rnp(c));
        // PMDG's label at the bottom left (MAP and PLN, not drawn while the map is invalid): the FMC
        // the ND shows.
        if (c.MapOrPlan && s.MapValid) rows.Add("Bottom left: " + l.Messages.FmcLabel.For(s.Fmc));
        if (full && !c.Plan) rows.AddRange(Corners(c));
        if (c.Plan) rows.Add("Bottom centre: " + compass.South);
        var messages = MessageTexts(c, map);
        rows.AddRange(messages.Count == 0 ? new[] { "Messages: none" } : messages.Select(m => "Message: " + m));
        // A flag PMDG draws at its own place (the glideslope's, VTK) is listed there and spoken like a message.
        var placed = approach ? PlacedFlags(c) : vdevFlag is not null ? new[] { vdevFlag } : Enumerable.Empty<string>();
        return new(rows, messages.Concat(placed).ToList());
    }

    /// <summary>Whether a display unit shows the Captain's ND (PMDG's display unit rule, from the layout), and
    /// whether on a full ND page (else an inboard map page, the half format).</summary>
    private static (bool Shown, bool Full) CaptainNd(NavDisplaySample s, Pmdg737NavDisplayLayout l)
    {
        var shown = Pmdg737NavDisplayCrossCheck.CaptainUnitsShown(s.UnitPages, s.LowerSelector, l.Units).ToList();
        return (shown.Count > 0, shown.Any(u => u.Full));
    }

    private static string Header(Context c)
    {
        var p = c.S.Panel;
        var m = c.L.Geometry.Modes;
        string mode = p.Mode == m.App ? "APP" : p.Mode == m.Vor ? "VOR" : p.Mode == m.Map ? "MAP" : p.Mode == m.Plan ? "PLN" : p.Mode.ToString(Inv);
        string header = Invariant($"Captain ND: {mode}, range {c.RangeNm}");
        if (c.Plan) return header + ", centred on " + CentreName(c);
        return header + (p.Vsd ? ", VSD on" : p.Centered ? ", centred" : "");
    }

    private static string CentreName(Context c) => c.S.PlnCentre is not null && c.S.PlnCentreName is { } name ? name : "the aircraft";

    // ---- the corners and the readouts ----

    /// <summary>PMDG's own up direction: the track or heading as the glass data holds them
    /// (magnetic), the variation added only with the true-north reference.</summary>
    private static double PmdgUp(NavDisplaySample s, Pmdg737NavDisplayLayout l)
    {
        double raw = G.IsTrackUp(s, l) ? s.TrackDeg : s.HeadingDeg;
        double up = (raw + (s.MagneticReference ? 0 : s.MagneticVariationDeg)) % 360;
        return up < 0 ? up + 360 : up;
    }

    /// <summary>The top-left corner: GS in the IRU's NAV condition, TAS from its
    /// threshold, the wind once the TAS is above its threshold (dashes below, or with no track), nothing
    /// while calm, with its arrow outside PLN.</summary>
    private static string TopLeft(Context c)
    {
        var s = c.S;
        var k = c.L.Corners;
        string gs = s.IruCondition == k.GroundSpeedNavCondition ? Whole(s.GroundSpeed) : "---";
        string tas = s.TrueAirspeed >= k.TasShownFrom ? Whole(s.TrueAirspeed) : "---";
        string row = $"GS {gs}, TAS {tas}";
        bool trackValid = s.TrackDeg > c.L.Orientation.TrackInvalidAtOrBelow;
        bool tasHigh = s.TrueAirspeed > k.WindTasAbove;
        bool windShown = s.WindSpeed >= k.WindHiddenBelow;
        if (tasHigh && !windShown && trackValid) return row;
        if (!tasHigh || !trackValid) return row + ", wind " + k.WindDashes;
        row += $", wind {((int)(s.WindDirection + 0.5)).ToString("000", Inv)}{k.WindSeparator}{Whole(s.WindSpeed)}";
        return c.Plan ? row : row + ", from " + Direction(G.RelativeDeg(s.WindDirection, PmdgUp(s, c.L)));
    }

    /// <summary>The orientation readout: TRK (HDG when heading-up), the up direction "%03d" or dashes while the
    /// readout is invalid, then TRU, MAG or (half format) M; nothing while failed.</summary>
    private static string? TopCentre(Context c)
    {
        var s = c.S;
        var o = c.L.Orientation;
        if (s.ReadoutFailed) return null;
        bool trackUp = G.IsTrackUp(s, c.L);
        double raw = trackUp ? s.TrackDeg : s.HeadingDeg;
        string number = s.ReadoutValid && raw > o.TrackInvalidAtOrBelow ? ((int)(PmdgUp(s, c.L) + 0.5) % 360).ToString("000", Inv) : "---";
        string suffix = !s.MagneticReference ? o.TrueLabel : c.Full ? o.MagneticLabel : o.MapMagneticLabel;
        return $"{(trackUp ? o.TrackLabel : o.HeadingLabel)} {number} {suffix}";
    }

    /// <summary>The VDEV scale and pointer: while PMDG gives a deviation (not blanked by the phase or the
    /// perf info) and the scale's gates hold (the FMC's VTK flag clear), its absolute value from the layout's
    /// threshold, and the side its pointer shows: drawn up the scale, the path is above the aircraft, which is
    /// below it (live, 2026-10-28: -1090 ft after starting down early, the pointer at the top); nearer than the
    /// threshold PMDG prints no number: on the path.</summary>
    private static string? Vdev(Context c)
    {
        var s = c.S;
        var r = c.L.Route;
        var v = c.L.Messages.Vdev;
        double d = s.VnavDeviationFt;
        if (d <= r.VnavInvalidAtOrBelow || s.VnavPhase < r.VnavPhaseBelow || !s.PerfPath || !v.DrawnWhile.All(k => Holds(k, c))) return null;
        double a = Math.Abs(d);
        bool pointerUp = v.PointerDownWhenPositive ? d < 0 : d > 0;
        string row = a < v.NumberFrom ? "VDEV, on the path" : "VDEV " + Whole(a) + (pointerUp ? ", below the path" : ", above the path");
        return row + VerticalRnp(c);
    }

    /// <summary>The scale's vertical RNP and ANP under the bottom RNP's option, as printed ("---" and "-.--" while not
    /// valid), after the app's word "vertical" for the RNP.</summary>
    private static string VerticalRnp(Context c)
    {
        var r = c.L.Messages.Vdev.VerticalRnp;
        if (!c.S.RnpShown || c.S.Details is not { } d) return "";
        string Value(int v, string dashes) => v > r.InvalidAtOrBelow ? Printf(r.Format, v) : dashes;
        return $", vertical {r.RnpLabel} {Value(d.VerticalRnp, r.RnpDashes)}, {r.AnpLabel} {Value(d.VerticalAnp, r.AnpDashes)}";
    }

    /// <summary>PMDG's radar lines, listed with the WXR label: the tilt as printed, after the app's word "tilt"
    /// ("tilt +2A", "tilt 0M"), and the variable-gain text ("VAR"; "VAR/" in the half format) while its byte says so.</summary>
    private static IEnumerable<string> WxrLines(Context c)
    {
        var w = c.L.Messages.Wxr;
        if (c.S.Details is not { } d) yield break;
        char letter = d.WxrAuto ? w.AutoLetter : w.ManualLetter;
        yield return "tilt " + (d.WxrTilt != 0 ? Printf(w.TiltFormat, d.WxrTilt, letter) : Printf(w.ZeroFormat, letter));
        if (d.WxrVariable == w.VarWhileSet) yield return c.Full ? w.VarFull : w.VarHalf;
    }

    /// <summary>PMDG's terrain numbers, listed with the TERR label while PMDG enters their block (the label shown
    /// with the panel's TERR on, PMDG's option set, the GPWS test clear): the highest as printed, then the lowest while
    /// the highest's colour code is not 0, each with its colour ("highest 045 (amber)"); empty when the block is
    /// entered but neither number is valid; null unless PMDG enters the terrain block.</summary>
    private static List<string>? TerrainLines(Context c, List<string> labels)
    {
        var t = c.L.Messages.Terrain;
        var s = c.S;
        if (!labels.Contains(t.Label) || !s.Panel.Terr || s.Details is not { } d || !d.TerrainOption || d.GpwsTest) return null;
        string Number(int feet, int colour) =>
            Printf(t.Format, (int)Math.Floor(feet * t.Scale + 0.5)) + (colour == t.RedCode ? " (red)" : colour == t.AmberCode ? " (amber)" : " (green)");
        var lines = new List<string>();
        if (d.TerrainHighest > t.InvalidAtOrBelow) lines.Add("highest " + Number(d.TerrainHighest, d.TerrainHighestColour));
        if (d.TerrainHighestColour != 0 && d.TerrainLowest > t.InvalidAtOrBelow) lines.Add("lowest " + Number(d.TerrainLowest, d.TerrainLowestColour));
        return lines;
    }

    /// <summary>The cross-track error under the aircraft, as printed: below the layout's limit, a zero text
    /// near 0, else the magnitude and its side's letter, two decimals at the close ranges ("XTK" is the app's word:
    /// PMDG prints the number alone).</summary>
    private static string? Xtk(Context c)
    {
        var x = c.L.Xtk;
        if (!c.Map || c.S.Xtk is not { } v || !(Math.Abs(v) < x.ShownBelow)) return null;
        bool close = c.S.Panel.Range <= x.CloseAtMost;
        double a = Math.Abs(v);
        if (a < (close ? x.CloseZeroBelow : x.FarZeroBelow)) return close ? x.CloseZero : x.FarZero;
        return Printf(close ? x.CloseFormat : x.FarFormat, a, v > 0 ? x.Right : x.Left).Trim();
    }

    private static string Rnp(Context c)
    {
        var r = c.L.Messages.Rnp;
        string Value(double v) => v > r.InvalidAtOrBelow ? Printf(r.Format, v).Trim() : r.Dashes;
        return $"{r.RnpLabel} {Value(c.S.Rnp)}, {r.AnpLabel} {Value(c.S.Anp)}";
    }

    /// <summary>The bottom corners: per VOR/ADF switch, the receiver's label, its ident (or frequency)
    /// while PMDG shows one, and the DME; a failed receiver's label in amber with no value, boxed when PMDG's
    /// failed arm draws a rectangle around it.</summary>
    private static IEnumerable<string> Corners(Context c)
    {
        var rx = c.L.Receivers;
        for (int k = 0; k < c.S.Receivers.Count; k++)
        {
            var r = c.S.Receivers[k];
            string corner = k == 0 ? "Bottom left" : "Bottom right";
            if (r.Switch == VorSwitch)
            {
                string label = Invariant($"VOR {k + 1}");
                string vor = r.VorFailed ? FailedLabel(label, rx.VorFailedBoxed)
                    : label + ", " + (r.Powered && rx.VorShownStates.Contains(r.VorState) ? Ident(r.VorIdent, r.VorFrequency) : "------");
                string dme = r.DmeFailed ? FailedLabel("DME", rx.DmeFailedBoxed)
                    : "DME " + (r.DmeNm >= 0 && r.VorState < DmeShownBelowState ? DmeText(r.DmeNm, rx.DmeTenthsBelow) : "---");
                yield return $"{corner}: {vor}, {dme}";
            }
            else if (r.Switch == AdfSwitch)
            {
                string label = Invariant($"ADF {k + 1}");
                yield return $"{corner}: " + (r.AdfFailed ? FailedLabel(label, rx.AdfFailedBoxed) : label + ", " + (r.AdfValid > 0 ? Ident(r.AdfIdent, r.AdfFrequency) : "------"));
            }
        }
    }

    private static string Ident(string ident, string frequency) => ident.Length > 0 ? ident : frequency.Trim();

    /// <summary>A failed receiver's label or a flag (G/S, VTK) as PMDG draws it: amber, boxed when its arm draws a rectangle.</summary>
    private static string FailedLabel(string label, bool boxed) => label + (boxed ? " (amber, boxed)" : " (amber)");

    private static string DmeText(double nm, double tenthsBelow)
    {
        int whole = (int)(nm + 0.5);
        return whole < tenthsBelow ? nm.ToString("F1", Inv) : whole.ToString(Inv);
    }

    // ---- the map ----

    private sealed record MapResult(IReadOnlyList<string> Rows, bool ExcessData, bool Offscale);

    /// <summary>Distances and directions from the map's centre (the aircraft; PLN: the stepped-to waypoint).</summary>
    private sealed class Placer(Context c, GeoPoint centre, double up)
    {
        public (double Distance, double Bearing, double Relative, bool OnMap) At(GeoPoint p)
        {
            var (d, b) = G.Measure(centre, p);
            double r = G.RelativeDeg(b, up);
            return (d, b, r, G.OnMap(c.View, G.Project(c.View, d, r, c.RangeNm, c.L.Geometry), c.L.Geometry));
        }

        public (double X, double Y) Point(GeoPoint p)
        {
            var (d, b) = G.Measure(centre, p);
            return G.Project(c.View, d, G.RelativeDeg(b, up), c.RangeNm, c.L.Geometry);
        }

        /// <summary>"23.0 NM, 20° right"; PLN: "23.0 NM north-west of EMPAD", or "at the centre".</summary>
        public string Where((double Distance, double Bearing, double Relative, bool OnMap) at)
        {
            if (!c.Plan) return $"{Distance(at.Distance)} NM, {Direction(at.Relative)}";
            return at.Distance < 0.05 ? "at the centre" : $"{Distance(at.Distance)} NM {G.CompassWord(at.Bearing)} of {CentreName(c)}";
        }
    }

    private static MapResult MapRows(Context c)
    {
        var s = c.S;
        var l = c.L;
        var rows = new List<string>();
        if (c.Map && c.Full)
        {
            var labels = l.Panel.RangeLabels;
            int index = Math.Clamp(s.Panel.Range + (s.Panel.Centered ? l.Panel.CentredLabelShift : l.Panel.ExpandedLabelShift), 0, labels.Count - 1);
            rows.Add("Map: half-range ring " + labels[index]);
        }
        // The track line, drawn with the compass before the map.
        if (c.Map && TrackLine(c) is { } track) rows.Add("Map: " + track);
        // PLN's two range rings, each labelled above and below the centre.
        if (c.Plan)
        {
            var labels = l.Panel.RangeLabels;
            var rings = l.Orientation.PlanCompass.RingLabelShifts.Select(k => labels[Math.Clamp(s.Panel.Range + k, 0, labels.Count - 1)]);
            rows.Add("Map: range rings " + string.Join(" and ", rings));
        }
        var centre = c.Plan ? s.PlnCentre ?? s.Aircraft : s.Aircraft;
        if (centre is null || G.UpDirectionDeg(s, l) is not { } up) return new(rows, false, false);
        var place = new Placer(c, centre.Value, up);

        // PLN's aircraft symbol: in PLN centred on a waypoint (centred on the aircraft, the header says so), the
        // aircraft at the FMS position while the map is valid and its latitude within the file's limit, where the map
        // shows it.
        if (c.Plan && s.PlnCentre is not null && s.MapValid && s.Aircraft is { } aircraft
            && Math.Abs(aircraft.LatitudeRad * 180 / Math.PI) <= l.Symbols.PlanAircraftLatitudeLimit)
        {
            // Drawn while PMDG's cull passes it; its 21 points reach about 38 from its point whatever the heading:
            // the square inside that reach stands for it when the circle's edge cuts it.
            var on = place.At(aircraft);
            var point = place.Point(aircraft);
            if (on.OnMap) rows.Add("Map: aircraft, " + place.Where(on));
            else if (G.Drawn(c.View, point, l.Geometry) && ShowsAtEdge(c.View, l.Geometry, point, new EdgeBox(-27, -27, 27, 27)))
                rows.Add("Map: aircraft, " + place.Where(on) + ", at the edge");
        }

        // PMDG draws no route, fixes, stations, airports, tuned stations or CDU fixes while the map is invalid
        // (its map-invalid flag: no FMS position or the readout invalid), the route but in PLN; the heading bug
        // tests the readout alone.
        if (c.Plan || s.MapValid)
        {
            // PMDG draws the markers only while the route's perf info has its VNAV path byte set.
            rows.AddRange(RouteRows(place, c, s.RefRoute, s.Route, s.RoutePath, s.RouteAirports, s.PerfPath ? s.Markers : Array.Empty<NdMarker>(), ""));
            if (s.ModifiedRoute is { } modified)
            {
                var airports = (s.ModifiedRouteAirports ?? Array.Empty<NdRouteAirport>()).Where(a => !s.RouteAirports.Contains(a)).ToList();
                rows.AddRange(RouteRows(place, c, s.RefRoute + l.Route.ModOffset, modified, s.ModifiedRoutePath ?? Array.Empty<NdPathPoint>(), airports,
                    Array.Empty<NdMarker>(), "modified route: "));
            }
        }
        if (!c.Plan && s.ReadoutValid && HeadingBug(c) is { } bug) rows.Add("Map: heading bug, " + bug);
        if (!c.Plan && s.ReadoutValid && HeadingLine(c) is { } line) rows.Add("Map: dashed line to the heading bug, " + line);
        rows.AddRange(PointerRows(c, "Map: "));
        rows.AddRange(PosRows(place, c));
        if (c.Map && AltitudeArc(c) is { } arc) rows.Add($"Map: altitude arc, {arc} NM ahead");
        if (c.Map && TrendVector(c) is { } trend) rows.Add("Map: trend vector, " + trend);
        bool excess = false;
        if (s.MapValid)
        {
            rows.AddRange(TunedRows(place, c));
            rows.AddRange(FixPageRows(place, c));
            var (objects, excessData) = ObjectRows(place, c);
            rows.AddRange(objects);
            excess = excessData;
        }
        // The ND draws the traffic on the expanded map, and centred in its one centred mode (MAP).
        bool trafficDrawn = c.View == NdView.ExpandedMap || c.View == NdView.CentredMap && s.Panel.Mode == l.Traffic.CentredMode;
        // The TCAS range ring, drawn just before the traffic under the same gate.
        if (trafficDrawn && TcasRing(c) is { } ring) rows.Add("Map: " + ring);
        var (traffic, offscale) = trafficDrawn ? TrafficRows(c, c.View, "Map: ") : (new List<string>(), false);
        rows.AddRange(traffic);
        return new(rows, excess, offscale);
    }

    /// <summary>The TCAS range ring of dots round the aircraft: while the traffic list exists, TFC or the TCAS
    /// self-test is on, the list's value is one the ring is drawn for (not the TCAS test's), the range is at most the
    /// layout's and PMDG's option is set. The caller checks the ND draws the traffic here.</summary>
    private static string? TcasRing(Context c)
    {
        if (c.L.Details?.TcasRing is not { } r || c.S.Details is not { } d) return null;
        var st = c.S.TrafficState;
        bool drawn = d.TrafficList && (c.S.Panel.Tfc || st.SelfTest) && r.ListValues.Contains(st.TestValue) && c.RangeNm <= r.UpToRangeNm && d.TcasRingOption;
        return drawn ? "TCAS ring, " + r.RadiusNm.ToString("0.##", Inv) + " NM" : null;
    }

    /// <summary>A route (flight plan <paramref name="plan"/>) in flying order: its departure airport, each drawn
    /// waypoint on the map (with DATA's text), a row where its line leaves the map naming the waypoint the line
    /// heads for, the markers nearest first, the arrival airport.</summary>
    private static List<string> RouteRows(Placer place, Context c, int plan, IReadOnlyList<NdWaypoint> route, IReadOnlyList<NdPathPoint> path,
        IReadOnlyList<NdRouteAirport> airports, IReadOnlyList<NdMarker> markers, string prefix)
    {
        var rows = new List<string>();
        foreach (var a in airports.Where(a => a.Departure)) if (AirportRow(place, c, a, prefix) is { } row) rows.Add(row);

        char pseudo = (char)c.L.Route.PseudoPrefix;
        // Where PMDG's route line leaves the map, each named by the waypoint it heads for,
        // listed just before that waypoint (after the route when it heads for no waypoint of the map's route).
        var pieces = PathPieces(path);
        var edgeRows = G.PieceEdges(pieces.Select(p => (place.Point(p.From), place.Point(p.To))).ToList(), c.View, c.L.Geometry)
            .Select(e => (pieces[e.AfterIndex].Toward, Row: EdgeRow(place, prefix, e.Edge, pieces[e.AfterIndex].Toward)))
            .ToList();
        void AddEdges(Func<NdPathToward?, bool> which)
        {
            foreach (var (_, row) in edgeRows.Where(e => which(e.Toward)))
                if (rows.Count == 0 || rows[^1] != row) rows.Add(row);
        }
        for (int i = 0; i < route.Count; i++)
        {
            int index = i;
            AddEdges(t => t?.RouteIndex == index);
            var w = route[i];
            if (w.IsHold)
            {
                if (HoldRow(place, c, w, prefix) is { } hold) rows.Add(hold);
                continue;
            }
            if (w.Position is not { } position || (w.Name.Length > 0 && w.Name[0] == pseudo)) continue;
            var at = place.At(position);
            if (at.OnMap) rows.Add($"Map: {prefix}{w.MapName}, {place.Where(at)}{Data(w, c, plan)}");
            else if (ShowsOnScreenAtEdge(c, place.Point(position), WaypointBoxes(w.MapName.Length, DataLines(w, c, plan))))
                rows.Add($"Map: {prefix}{w.MapName}, {place.Where(at)}{Data(w, c, plan)}, at the edge");
        }
        AddEdges(t => t?.RouteIndex is not { } index || index >= route.Count);
        rows.AddRange(markers.Select(m => (m.Label, At: place.At(m.Position), Point: place.Point(m.Position)))
            .Select(m => (m.Label, m.At, Edge: !m.At.OnMap && ShowsOnScreenAtEdge(c, m.Point, MarkerBoxes(m.Label.Length))))
            .Where(m => m.At.OnMap || m.Edge).OrderBy(m => m.At.Distance)
            .Select(m => $"Map: {prefix}{m.Label}, {place.Where(m.At)}{(m.Edge ? ", at the edge" : "")}"));

        foreach (var a in airports.Where(a => !a.Departure)) if (AirportRow(place, c, a, prefix) is { } row) rows.Add(row);
        return rows;
    }

    /// <summary>PMDG's holding pattern at a hold node (PMDG's copy of its fix, right after the fix in the walk),
    /// listed while its fix is on the map: the HOLD page's inbound course (the true course less the node's variation,
    /// rounded and "%03d" as the page prints it), the turn, and the legs, the distance while PMDG has one, else the time,
    /// each in the HOLD page's format.</summary>
    private static string? HoldRow(Placer place, Context c, NdWaypoint w, string prefix)
    {
        if (c.L.Details?.Hold is not { } h || w.Hold is not { } hold || w.Position is not { } position || !place.At(position).OnMap) return null;
        double inbound = ((hold.InboundTrueDeg - w.MagneticVariationDeg) % 360 + 360) % 360;
        bool Valid(double v) => double.IsFinite(v) && v > h.LegInvalidAtOrBelow;
        string legs = Valid(hold.LegNm) ? $", legs {Printf(h.LegDistanceFormat, hold.LegNm).Trim()} NM"
            : Valid(hold.LegMinutes) ? $", {Printf(h.LegTimeFormat, hold.LegMinutes).Trim()} minute legs" : "";
        return $"Map: {prefix}hold at {w.MapName}, inbound {Printf("%03d", (int)Math.Floor(inbound + 0.5))}°, {(hold.Turn == h.TurnChar ? "left" : "right")} turns{legs}";
    }

    /// <summary>The route line's pieces in drawing order, each with the waypoint it heads for: each point's drawn
    /// lines, and a turn drawn since the last line joining that line's end to the next line's start, heading where
    /// that line heads (the arc's own geometry is not read). PMDG puts a turn on a point of its own,
    /// with no line, and the line after it on the next point. A point drawing neither breaks the line, as a gap
    /// PMDG leaves (a discontinuity) does.</summary>
    private static List<(GeoPoint From, GeoPoint To, NdPathToward? Toward)> PathPieces(IReadOnlyList<NdPathPoint> path)
    {
        var pieces = new List<(GeoPoint From, GeoPoint To, NdPathToward? Toward)>();
        GeoPoint? last = null;
        bool turned = false;
        foreach (var point in path)
        {
            var lines = new[] { point.FirstLine, point.SecondLine }.OfType<NdPathLine>().ToList();
            turned |= point.Turn;
            if (lines.Count == 0)
            {
                if (!point.Turn) (last, turned) = (null, false);
                continue;
            }
            if (turned && last is { } end) pieces.Add((end, lines[0].From, point.Toward));
            foreach (var line in lines) pieces.Add((line.From, line.To, point.Toward));
            (last, turned) = (lines[^1].To, false);
        }
        return pieces;
    }

    private static string EdgeRow(Placer place, string prefix, string edge, NdPathToward? toward) =>
        $"Map: {prefix}route goes off the {edge} edge"
        + (toward is null ? "" : $", toward {toward.Name}" + (toward.Position is { } at ? ", " + place.Where(place.At(at)) : ""));

    /// <summary>A route airport: at or below the close-up range index with a runway, its outline and name alone;
    /// otherwise the circle and the airport's name, with the runway's name when one is set.</summary>
    private static string? AirportRow(Placer place, Context c, NdRouteAirport a, string prefix)
    {
        if (a.Position is not { } position) return null;
        var at = place.At(position);
        if (!at.OnMap) return null;
        bool closeUp = a.Runway is not null && c.S.Panel.Range <= c.L.Route.Airports.RunwayUpToRange;
        string what = closeUp ? "runway " + a.Runway : "airport " + a.Name + (a.Runway is { } runway ? ", runway " + runway : "");
        return $"Map: {prefix}{what}, {place.Where(at)}" + (closeUp && Centreline(c, a, position) is { } line ? "; " + line : "");
    }

    /// <summary>The runway's dashed centreline, close up with a runway: the layout's NM beyond each end of the runway
    /// (drawn from its position along its heading for its length), its direction on the screen as the line runs (within
    /// 90° of the top), and, while the aircraft is beside the line's extent, how far it is from the line on the screen
    /// ("on it" within the tenth PMDG's distances round to).</summary>
    private static string? Centreline(Context c, NdRouteAirport a, GeoPoint threshold)
    {
        var r = c.L.Route.Airports;
        if (G.UpDirectionDeg(c.S, c.L) is not { } up) return null;
        // The heading is a course: PMDG turns it true by the variation, as the course pointer's.
        double heading = a.RunwayHeadingDeg + c.S.MagneticVariationDeg;
        double rel = G.RelativeDeg(heading, up);
        double screen = rel > 90 ? rel - 180 : rel <= -90 ? rel + 180 : rel;
        int d = (int)Math.Round(screen, MidpointRounding.AwayFromZero);
        string running = d == 0 ? "running to the top" : Invariant($"running {Math.Abs(d)}° {(d > 0 ? "right" : "left")} of the top");
        string text = $"centreline {Distance(r.CentrelineNm)} NM each way, {running}";
        if (c.S.Aircraft is not { } aircraft) return text;
        var (distance, bearing) = G.Measure(threshold, aircraft);
        double along = distance * Math.Cos((bearing - heading) * Math.PI / 180);
        if (along < -r.CentrelineNm || along > a.RunwayLengthFt / r.FeetPerNm + r.CentrelineNm) return text;
        double side = distance * Math.Sin((bearing - (up + screen)) * Math.PI / 180);
        string off = Distance(Math.Abs(side));
        return text + (off == Distance(0) ? ", you are on it" : $", you are {off} NM {(side > 0 ? "right" : "left")} of it");
    }

    /// <summary>PMDG's DATA text under a waypoint: its constraint lines, the third
    /// line, and with DATA on its ETA ("HHMMZ", unless the node hides it); with DATA off the constraint and the
    /// third line of the active waypoint alone, while PMDG's ND_ActWptData option is set; none on a plan
    /// PMDG prints no DATA text for (the MODs).</summary>
    private static string Data(NdWaypoint w, Context c, int plan) => DataLines(w, c, plan) is { Count: > 0 } parts ? "; " + string.Join(", ", parts) : "";

    /// <summary>The DATA text's lines under a waypoint's name, top to bottom, as <see cref="Data"/> lists them.</summary>
    private static List<string> DataLines(NdWaypoint w, Context c, int plan)
    {
        var d = c.L.Route.DataText;
        bool data = c.S.Panel.Data;
        if (!(data || c.S.ActiveWaypointData && w.IsActiveWaypoint) || !d.DrawsDataFor(plan)) return new List<string>();
        var k = w.Constraint;
        var parts = new List<string>();
        bool Valid(int altitude) => altitude > d.InvalidAtOrBelow;
        int transition = k.Phase < d.ClimbPhasesBelow ? c.S.TransitionAltitude : c.S.TransitionLevel;
        string Alt(int altitude, char suffix) =>
            (altitude >= transition ? Printf(d.FlightLevelFormat, altitude / 100, suffix) : Printf(d.AltitudeFormat, altitude, suffix)).Trim();
        if (k.Type == d.WindowType)
        {
            if (Valid(k.Upper) && Valid(k.Lower))
            {
                parts.Add(Alt(k.Upper, d.BelowType));
                parts.Add(Alt(k.Lower, d.AboveType));
            }
        }
        else if ((k.Type == d.AboveType || k.Type == d.BelowType) && Valid(k.At)) parts.Add(Alt(k.At, k.Type));
        else if (k.Type == d.AtType && Valid(k.At)) parts.Add(Alt(k.At, ' '));
        if (k.ThirdLine != 0) parts.Add(Printf(d.ThirdLineFormat, k.ThirdLine).Trim());
        if (data && !w.EtaHidden) parts.Add(w.Eta is { } e ? Invariant($"{e.Hours:00}{e.Minutes:00}Z") : "------Z");
        return parts;
    }

    /// <summary>The heading bug: on the compass ring at the selected heading off PMDG's up direction;
    /// PMDG parks it nowhere, so beyond the screen it is not on the map. <paramref name="viewport"/> is the
    /// compass's when the view has no map of its own (APP and VOR).</summary>
    private static string? HeadingBug(Context c, NdViewport? viewport = null)
    {
        var s = c.S;
        if ((viewport ?? c.Viewport) is not { } v) return null;
        double raw = G.IsTrackUp(s, c.L) ? s.TrackDeg : s.HeadingDeg;
        if (raw <= c.L.Orientation.TrackInvalidAtOrBelow) return null;
        double rel = G.RelativeDeg(s.SelectedHeadingDeg, PmdgUp(s, c.L)), a = rel * Math.PI / 180;
        var point = (v.CentreX + v.CompassRadius * Math.Sin(a), v.CentreY - v.CompassRadius * Math.Cos(a));
        return G.OnScreen(point, c.L.Geometry) ? Direction(rel) : null;
    }

    /// <summary>The track line, in every mode but PLN: centred in the layout's modes (APP, VOR) on the half format, the
    /// track pointer at the compass while the readout is valid; otherwise the line while the track is valid, and its
    /// marks while PMDG draws them, at their fractions of the NM the compass spans (the view's by CTR, as APP and VOR
    /// share MAP's). The line leans by the track less the heading while heading-up with the readout valid, else it
    /// points straight up: "straight ahead".</summary>
    private static string? TrackLine(Context c)
    {
        var s = c.S;
        if (c.Plan || c.L.Details?.TrackLine is not { } t || s.Details is not { } d) return null;
        bool trackValid = s.TrackDeg > c.L.Orientation.TrackInvalidAtOrBelow;
        string direction = !G.IsTrackUp(s, c.L) && s.ReadoutValid && trackValid ? Direction(G.RelativeDeg(s.TrackDeg, s.HeadingDeg)) : "straight ahead";
        if (s.Panel.Centered && !c.Full && t.PointerCentredIn.Contains(s.Panel.Mode)) return s.ReadoutValid ? "track pointer, " + direction : null;
        // PMDG's two flags: the marks while the track is valid or a picture or the traffic is on, unless the range
        // arcs replace them (the option, not centred, and a picture or the traffic on).
        bool shown = s.Panel.Wxr || s.Panel.Terr || s.Panel.Tfc || s.TrafficState.SelfTest;
        bool marks = (trackValid || shown) && !(d.RangeArcsOption && !s.Panel.Centered && shown);
        var v = G.Viewport(s.Panel.Centered ? NdView.CentredMap : NdView.ExpandedMap, c.L.Geometry)!;
        var at = t.MarkFractions.Select(f => (f * c.RangeNm * v.RangeFactor).ToString("0.##", Inv)).ToList();
        string text = marks ? "marks at " + string.Join(", ", at.Take(at.Count - 1)) + " and " + at[^1] + " NM" : "";
        if (trackValid) return "track line, " + direction + (marks ? ", " + text : "");
        return marks ? "track line " + text : null;
    }

    /// <summary>The heading bug's dashed line from the aircraft toward the bug, drawn to the compass whether the
    /// bug is on the screen or not (the screen clip alone): never centred in the layout's modes (VOR and APP), and while
    /// PMDG's option is set and the FMA roll mode is a timed one (LNAV, VOR/LOC), only within the layout's seconds
    /// of the selected heading's last change on the sim's clock. Its direction is the bug's.</summary>
    private static string? HeadingLine(Context c)
    {
        var s = c.S;
        var h = c.L.Symbols.HeadingLine;
        double raw = G.IsTrackUp(s, c.L) ? s.TrackDeg : s.HeadingDeg;
        if (raw <= c.L.Orientation.TrackInvalidAtOrBelow || s.Details is not { } d) return null;
        if (s.Panel.Centered && h.HiddenCentredIn.Contains(s.Panel.Mode)) return null;
        if (d.HeadingLineOption && h.TimedRollModes.Contains(d.RollMode) && d.SimTime - d.HeadingChangedAt > h.ShownForSeconds) return null;
        return Direction(G.RelativeDeg(s.SelectedHeadingDeg, PmdgUp(s, c.L)));
    }

    /// <summary>The altitude range arc: where the MCP altitude is reached at this vertical and ground speed,
    /// within its share of the range radius.</summary>
    private static string? AltitudeArc(Context c)
    {
        var s = c.S;
        var sym = c.L.Symbols;
        if (c.Viewport is not { } v || Math.Abs(s.VerticalSpeedFpm) <= sym.ArcVsAbove) return null;
        double minutes = (s.McpAltitudeFt - s.AltitudeFt) / s.VerticalSpeedFpm;
        if (minutes <= 0) return null;
        double nm = s.GroundSpeed * minutes / sym.MinutesPerHour;
        return nm < sym.ArcWithinRadius * c.RangeNm * v.RangeFactor ? Distance(nm) : null;
    }

    /// <summary>The trend vector, in MAP while the map is valid and the ground speed is above the layout's limit:
    /// its segments (by the range selector) end at segments × (the gap and the drawn seconds), "in 90 seconds"; while
    /// the turn rate is at least the layout's, the degrees turned by then and the arcs' radius, else the distance flown
    /// straight up the map by then. Listed whole, whatever part the arc or the clip cuts.</summary>
    private static string? TrendVector(Context c)
    {
        var s = c.S;
        if (c.L.Details?.TrendVector is not { } v || s.Details is not { } d || !s.MapValid || !(s.GroundSpeed > v.GroundSpeedAbove)) return null;
        if (s.Panel.Range < 0 || s.Panel.Range >= v.SegmentsByRange.Count) return null;
        double seconds = v.SegmentsByRange[s.Panel.Range] * (v.DrawnSeconds + v.GapSeconds);
        double perSecond = s.GroundSpeed / v.SecondsPerHour;
        double rate = d.TurnRate / v.RadiansPerDegree; // degrees a second, as PMDG smooths it
        if (!(Math.Abs(rate) >= v.TurnFrom)) return Invariant($"straight ahead, {Distance(perSecond * seconds)} NM in {seconds} seconds");
        string side = rate > 0 == v.RightWhenPositive ? "right" : "left";
        int degrees = (int)Math.Round(Math.Abs(rate) * seconds, MidpointRounding.AwayFromZero);
        return Invariant($"curving {side} through {degrees}° in {seconds} seconds, {Distance(perSecond / (Math.Abs(rate) * v.RadiansPerDegree))} NM radius");
    }

    /// <summary>The tuned stations: a tuned VOR on the map (PMDG's tuned flag: the receiver powered, the
    /// layout checks the corners read the same byte, and in the tuned state), with its course while PMDG draws
    /// the course line.</summary>
    private static IEnumerable<string> TunedRows(Placer place, Context c)
    {
        var tu = c.L.Tuned;
        for (int k = 0; k < c.S.Tuned.Count; k++)
        {
            var t = c.S.Tuned[k];
            bool powered = k < c.S.Receivers.Count && c.S.Receivers[k].Powered;
            if (!powered || t.State != tu.TunedState || t.Position is not { } position) continue;
            var at = place.At(position);
            // PMDG's cull drops the station only while its course-line option is clear; its symbol is the DME
            // shape while the DME has a distance, else the VOR's hexagon.
            var point = place.Point(position);
            bool edge = !at.OnMap && (c.S.CourseLineOption || G.Drawn(c.View, point, c.L.Geometry))
                && ShowsAtEdge(c.View, c.L.Geometry, point, SymbolBox(NdObjectKind.Navaid, t.DmeNm >= 0 ? c.L.Lists.DmeFlag : 0, c.L.Lists), TextBox(t.Ident.Length));
            if (!at.OnMap && !edge) continue;
            bool line = c.S.CourseLineOption && k < c.S.Receivers.Count && c.S.Receivers[k].Switch == VorSwitch;
            string course = line ? ", course " + (((int)Math.Round(t.CourseDeg) % 360 + 360) % 360).ToString("000", Inv) : "";
            yield return Invariant($"Map: VOR {k + 1} {t.Ident}, {place.Where(at)}{course}{(edge ? ", at the edge" : "")}");
        }
    }

    /// <summary>The CDU FIX page on the map: each FIX page entry, with its RAD/DIS lines as PMDG labels them ("R090", ring
    /// "20" or "12.5").</summary>
    private static IEnumerable<string> FixPageRows(Placer place, Context c)
    {
        var f = c.L.FixPage;
        foreach (var e in c.S.FixPage)
        {
            var at = place.At(e.Position);
            var lines = new List<string>();
            for (int j = 0; j < Math.Max(e.RadialsDeg.Count, e.RingsNm.Count); j++)
            {
                var parts = new List<string>();
                if (j < e.RadialsDeg.Count && e.RadialsDeg[j] > f.InvalidAtOrBelow)
                {
                    int radial = ((int)Math.Round(e.RadialsDeg[j]) % 360 + 360) % 360;
                    parts.Add("R" + (radial == 0 ? 360 : radial).ToString("000", Inv));
                }
                if (j < e.RingsNm.Count && e.RingsNm[j] > f.InvalidAtOrBelow)
                {
                    double ring = e.RingsNm[j];
                    parts.Add("ring " + (Math.Abs(ring - Math.Round(ring)) < 0.05 ? ((int)Math.Round(ring)).ToString(Inv) : ring.ToString("F1", Inv)));
                }
                if (parts.Count > 0) lines.Add(string.Join(" ", parts));
            }
            // The line to the abeam point on the route, drawn while the point is valid, wherever it falls on the screen.
            string abeam = e.Abeam is { } point ? "; abeam line to the route at " + place.Where(place.At(point)) : "";
            yield return $"Map: FIX {e.Name}, {place.Where(at)}{(at.OnMap ? "" : ", off the map")}{(lines.Count > 0 ? ": " + string.Join(", ", lines) : "")}{abeam}";
        }
    }

    /// <summary>
    /// The nearby stations, fixes and airports: each list in vector order, an element drawn while PMDG's
    /// cull passes it, and more than MaxElements drawn at the top of an element sets EXCESS DATA and
    /// stops. A drawn element is a row while on the map, and "at the edge" while the clip cuts it but part of it
    /// shows. Fixes only up to their range, navaids above HighClassAbove only of HighClass. Rows nearest first per kind.
    /// </summary>
    private static (List<string> Rows, bool Excess) ObjectRows(Placer place, Context c)
    {
        var s = c.S;
        var li = c.L.Lists;
        var rows = new List<string>();
        bool excess = false;
        var tunedIdents = s.Tuned.Where(t => t.State == c.L.Tuned.TunedState).Select(t => t.Ident).ToHashSet(StringComparer.Ordinal);
        // PMDG's ARPT list tests no route, so it draws the route's airports too. The route's own airport, drawn
        // after it, puts the same circle and name over one only without a runway: with one, its symbol sits at the runway (close
        // up the runway alone), and the ARPT circle and name at the airport still show.
        var routeAirports = s.RouteAirports.Concat(s.ModifiedRoute is null ? Array.Empty<NdRouteAirport>() : s.ModifiedRouteAirports ?? Array.Empty<NdRouteAirport>())
            .Where(a => a.Runway is null && a.Position is { } p && place.At(p).OnMap).Select(a => a.Name).ToHashSet(StringComparer.Ordinal);

        // pmdgDraws: PMDG's own skips (not counted); listed: drawn and counted, but already a row elsewhere.
        void Draw(NdObjectKind kind, Func<NdMapObject, bool> pmdgDraws, Func<NdMapObject, bool> listed, Func<NdMapObject, string> what)
        {
            var drawn = new List<(string Row, double Distance)>();
            int count = 0;
            foreach (var o in s.Objects.Where(o => o.Kind == kind))
            {
                if (count > li.MaxElements)
                {
                    excess = true;
                    break;
                }
                if (!pmdgDraws(o)) continue;
                var point = place.Point(o.Position);
                if (!G.Drawn(c.View, point, c.L.Geometry)) continue;
                count++;
                if (!listed(o)) continue;
                var at = place.At(o.Position);
                if (at.OnMap) drawn.Add(($"Map: {what(o)}, {place.Where(at)}", at.Distance));
                else if (ShowsAtEdge(c.View, c.L.Geometry, point, SymbolBox(o.Kind, o.Flags, li), TextBox(o.Name.Length)))
                    drawn.Add(($"Map: {what(o)}, {place.Where(at)}, at the edge", at.Distance));
            }
            rows.AddRange(drawn.OrderBy(d => d.Distance).Select(d => d.Row));
        }

        if (s.Panel.Sta)
        {
            bool highOnly = c.RangeNm > li.HighClassAbove;
            Draw(NdObjectKind.Navaid, o => li.DrawsStation(o.Flags) && (!highOnly || o.Class == li.HighClass) && !tunedIdents.Contains(o.Name), _ => true,
                o => NavaidWord(o.Flags, li) + " " + o.Name);
        }
        if (s.Panel.Wpt && c.RangeNm <= li.FixesUpTo) Draw(NdObjectKind.Fix, _ => true, _ => true, o => "fix " + o.Name);
        if (s.Panel.Arpt) Draw(NdObjectKind.Airport, _ => true, o => !routeAirports.Contains(o.Name), o => "airport " + o.Name);
        return (rows, excess);
    }

    /// <summary>A part of a drawn symbol (its shape or a text), as a box around the symbol's point (screen units, y down).</summary>
    private readonly record struct EdgeBox(double Left, double Top, double Right, double Bottom);

    /// <summary>A symbol's extent around its point, as PMDG draws it on the -800: a fix's triangle, an airport's
    /// 32-unit circle, a station's DME 12-point shape, else the VOR's hexagon.</summary>
    private static EdgeBox SymbolBox(NdObjectKind kind, int flags, NdListFields li) => kind switch
    {
        NdObjectKind.Fix => new(-18.45, -21.33, 18.45, 10.67),
        NdObjectKind.Airport => new(-16, -16, 16, 16),
        _ => (flags & li.DmeFlag) != 0 ? new(-23, -16.9, 23, 23) : new(-12.4, -10.7, 12.4, 10.7),
    };

    /// <summary>PMDG's size-38 name font on the ND: a text's letters from about 8 to 34 below its line's top, about
    /// 18 a letter. PMDG raises the line by 15.2 for vertical alignment 2 and by 26.6 for 3 (centred). A name is
    /// printed from (+22, +14) of its symbol's point (fixes, airports, stations and route waypoints). Measured on
    /// the -800 and estimated from the font's size, not in the layout.</summary>
    private const double NameX = 22, NameY = 14, LetterTop = 8, LetterBottom = 34, NameLetter = 18, RaisedLeft = 15.2, RaisedCentred = 26.6;

    /// <summary>A text printed left-aligned from (x, y) of a symbol's point with vertical alignment 2.</summary>
    private static EdgeBox TextBox(int letters, double x = NameX, double y = NameY) =>
        new(x, y - RaisedLeft + LetterTop, x + NameLetter * letters, y - RaisedLeft + LetterBottom);

    /// <summary>A text printed centred on (x, y) of a symbol's point (alignment 3, 3).</summary>
    private static EdgeBox CentredTextBox(int letters, double x, double y) =>
        new(x - NameLetter * letters / 2, y - RaisedCentred + LetterTop, x + NameLetter * letters / 2, y - RaisedCentred + LetterBottom);

    /// <summary>Less than this far into the map (about 3 pixels), a cut symbol or text does not count as showing.</summary>
    private const double EdgeSliver = 4;

    /// <summary>Whether part of a drawn symbol whose point is off the map shows on it: one of its boxes more than a
    /// sliver inside the view's clip and the screen.</summary>
    private static bool ShowsAtEdge(NdView view, NdGeometry g, (double X, double Y) p, params EdgeBox[] boxes)
    {
        foreach (var b in boxes)
        {
            double left = p.X + b.Left + EdgeSliver, top = p.Y + b.Top + EdgeSliver, right = p.X + b.Right - EdgeSliver, bottom = p.Y + b.Bottom - EdgeSliver;
            if (left > right || top > bottom) continue;
            for (int i = 0; i <= 8; i++)
                for (int j = 0; j <= 4; j++)
                    if (G.OnMap(view, (left + (right - left) * i / 8, top + (bottom - top) * j / 4), g)) return true;
        }
        return false;
    }

    /// <summary>The kind of a navaid PMDG draws (the VOR or DME bit set, the ILS bit clear), named by
    /// the symbol PMDG draws: VOR/DME with the DME bit (its DME shape), else VOR (the hexagon's bit).
    /// NDBs (4), ILS stations (1) and TACANs alone (16) are never drawn, so they have no word; a TACAN's bit
    /// beside a drawn bit (a VORTAC) changes no symbol.</summary>
    private static string NavaidWord(int flags, NdListFields li) =>
        (flags & li.DmeFlag) != 0 ? "VOR/DME"
        : (flags & li.VorFlag) != 0 ? "VOR"
        : "navaid";

    /// <summary>The TCAS traffic (expanded MAP, and APP and VOR on the same picture): nothing unless the list is shown and
    /// TCAS powered, nor in the self-test's first moments or its test pattern; other traffic only inside the
    /// filter unless all is shown; other and proximate traffic only with TFC (or the self-test); an RA shown as a
    /// TA in TA ONLY; a TA or RA beyond OffscaleBeyond of the range makes OFFSCALE. In the TCAS test no filter,
    /// and each target at the test's place for its index in the list (its own past them). Rows nearest first, each
    /// starting with <paramref name="prefix"/>, placed on <paramref name="view"/>.</summary>
    private static (List<string> Rows, bool Offscale) TrafficRows(Context c, NdView view, string prefix)
    {
        var s = c.S;
        var t = c.L.Traffic;
        var st = s.TrafficState;
        var rows = new List<(string Row, double Range)>();
        bool offscale = false;
        if (!st.ListShown || !st.Powered || (st.SelfTest && !st.SelfTestShown)) return (new List<string>(), false);
        bool test = st.TestValue == t.TestValue;
        foreach (var x in s.Traffic)
        {
            int threat = x.Threat == 3 && st.TaOnly ? 2 : x.Threat;
            if (threat == 0 && !test && !st.ShowAllTraffic && (x.RangeNm > st.FilterRangeNm || Math.Abs(x.RelativeAltitudeFt) > st.FilterSeparationFt)) continue;
            if (threat <= t.TfcThreatsUpTo && !(s.Panel.Tfc || st.SelfTest)) continue;
            var (rangeNm, bearingRad) = test && x.Index < t.TestPositions.Count
                ? (t.TestPositions[x.Index].RangeNm, t.TestPositions[x.Index].BearingRad)
                : (x.RangeNm, x.BearingRad);
            double rel = G.TrafficRelativeDeg(bearingRad, s, c.L);
            var point = G.Project(view, rangeNm, rel, c.RangeNm, c.L.Geometry);
            // PMDG's cull: a culled TA or RA is OFFSCALE too, and nothing of a culled target is drawn.
            bool drawn = G.Drawn(view, point, c.L.Geometry);
            if (threat >= 2 && (rangeNm > t.OffscaleBeyond * c.RangeNm || t.OffscaleWhenCulled && !drawn)) offscale = true;
            if (!drawn) continue;
            int hundreds = Math.Clamp((int)Math.Round(x.RelativeAltitudeFt / 100, MidpointRounding.AwayFromZero), -t.HundredsLimit, t.HundredsLimit);
            string altitude = (hundreds < 0 ? "-" : "+") + Math.Abs(hundreds).ToString("00", Inv);
            bool onMap = G.OnMap(view, point, c.L.Geometry);
            if (!onMap && !ShowsAtEdge(view, c.L.Geometry, point, TrafficBoxes(hundreds, altitude.Length, x.Trend))) continue;
            string trend = x.Trend > 0 ? " climbing" : x.Trend < 0 ? " descending" : "";
            string word = threat >= 0 && threat < ThreatWords.Length ? ThreatWords[threat] : "traffic";
            rows.Add(($"{prefix}{word}, {Distance(rangeNm)} NM, {Direction(rel)}, {altitude}{trend}{(onMap ? "" : ", at the edge")}", rangeNm));
        }
        return (rows.OrderBy(r => r.Range).Select(r => r.Row).ToList(), offscale);
    }

    /// <summary>PMDG draws a route waypoint or a marker while it is on the screen square; the clip then cuts it.</summary>
    private static bool ShowsOnScreenAtEdge(Context c, (double X, double Y) p, EdgeBox[] boxes) =>
        G.OnScreen(p, c.L.Geometry) && ShowsAtEdge(c.View, c.L.Geometry, p, boxes);

    /// <summary>A route waypoint's parts: the star 20 each way, the name from (+22, +14), each DATA
    /// line 30 below the one above.</summary>
    private static EdgeBox[] WaypointBoxes(int letters, IReadOnlyList<string> dataLines) =>
        new[] { new EdgeBox(-20, -20, 20, 20), TextBox(letters) }
            .Concat(dataLines.Select((line, k) => TextBox(line.Length, NameX, NameY + 30 * (k + 1)))).ToArray();

    /// <summary>A VNAV marker's parts: the 16-unit circle, the label from (+18, -8).</summary>
    private static EdgeBox[] MarkerBoxes(int letters) => new[] { new EdgeBox(-8, -8, 8, 8), TextBox(letters, 18, -8) };

    /// <summary>A traffic target's parts around its point: its symbol (a diamond 9 by 13, a
    /// circle or square 13), the relative altitude centred 32.5 above (0 and up) or below, the trend arrow 32.5 to
    /// the right.</summary>
    private static EdgeBox[] TrafficBoxes(int hundreds, int letters, int trend)
    {
        var boxes = new List<EdgeBox> { new(-13, -13, 13, 13), CentredTextBox(letters, 0, hundreds >= 0 ? -32.5 : 32.5) };
        if (trend != 0) boxes.Add(new(24.5, -13, 40.5, 16.9));
        return boxes.ToArray();
    }

    // ---- the messages ----

    /// <summary>The layout's messages that hold, then TRAFFIC (a TA, or an RA outside TA ONLY) and OFFSCALE
    /// (under the TFC message's gate), both quiet while WINDSHEAR shows and in the self-test's first moments,
    /// then EXCESS DATA.</summary>
    private static IReadOnlyList<string> MessageTexts(Context c, MapResult map)
    {
        var s = c.S;
        var m = c.L.Messages;
        var texts = Holding(m.Messages, c);
        texts.AddRange(ApproachMessages(c));
        var st = s.TrafficState;
        bool windshear = texts.Contains("WINDSHEAR");
        if (!windshear && !(st.SelfTest && !st.SelfTestShown))
        {
            // PMDG's TA count, and its RA count while not TA ONLY (in TA ONLY an RA shows as a TA).
            if (s.Traffic.Any(x => x.Threat >= 2)) texts.Add(m.Traffic);
            // OFFSCALE under PMDG's TFC gate: the TFC button, and MAP or VOR/APP not centred.
            var modes = c.L.Geometry.Modes;
            bool gate = s.Panel.Tfc && (s.Panel.Mode == modes.Map || (s.Panel.Mode == modes.App || s.Panel.Mode == modes.Vor) && !s.Panel.Centered);
            if (map.Offscale && gate) texts.Add(m.Offscale);
        }
        if (map.ExcessData) texts.Add(m.ExcessData);
        return texts;
    }

    /// <summary>The texts whose conditions hold in some alternative, in the layout's order.</summary>
    private static List<string> Holding(IReadOnlyList<NdMessage> messages, Context c) =>
        messages.Where(msg => msg.AnyOf.Any(alternative => alternative.All(k => Holds(k, c)))).Select(msg => msg.Text).ToList();

    private static bool Holds(NdCondition k, Context c)
    {
        var s = c.S;
        long value = k.Source.Kind switch
        {
            NdSourceKind.Object or NdSourceKind.Panel or NdSourceKind.ByFmc => s.ConditionValues.TryGetValue(k.Source, out long v) ? v : 0,
            NdSourceKind.FullFormat => c.Full ? 1 : 0,
            NdSourceKind.Centred => s.Panel.Centered ? 1 : 0,
            NdSourceKind.Mode => s.Panel.Mode == k.Source.Mode ? 1 : 0,
            NdSourceKind.Valid => s.ReadoutValid ? 1 : 0,
            NdSourceKind.MapValid => s.MapValid ? 1 : 0,
            _ => 0,
        };
        long constant = k.Constant ?? 0;
        uint uv = unchecked((uint)value), uk = unchecked((uint)constant);
        bool test = k.CompareOpcode switch
        {
            0 => value != 0,
            0x46 => value == constant,
            0x47 => value != constant,
            0x48 => value < constant,
            0x49 => uv < uk,
            0x4A => value > constant,
            0x4B => uv > uk,
            0x4C => value <= constant,
            0x4D => uv <= uk,
            0x4E => value >= constant,
            0x4F => uv >= uk,
            _ => false,
        };
        return test != k.Negated;
    }

    // ---- printing ----

    private static string Whole(double v) => ((int)(v + 0.5)).ToString(Inv);

    private static string Invariant(FormattableString s) => s.ToString(Inv);

    private static string Printf(string format, params object[] args) => PmdgPrintf.Format(format, args);
}
