using System.Globalization;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Utils;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What the captain's ND status (58) and the cross-track error (52) say the ND draws: the track line (58.1), the
/// heading bug (58.2), the route solid (58.3; dashed when left out) and the cross-track error in nautical miles, negative
/// left (null when not sent).
/// </summary>
internal sealed record FenixNdStatus(bool TrackLine, bool HeadingBug, bool SolidRoute, double? CrossTrack);

/// <summary>
/// The ND's route layer: the captain's route as the ND draws it, in route order from the active leg (its waypoints with
/// their constraints, the markers, holds, where the line leaves the screen and where it ends and starts again), the
/// temporary plan, the heading bug, the track line, the cross-track error, the range rings and FIX INFO; and the
/// bottom right label, a layer of its own. Only what the ND draws is listed (spec 2.4), each look from
/// <see cref="FenixChannelNdLook"/> or the channel's color member. Measured on 3.0.0.7613 (evidence
/// 03-nd.md, sitting 1 Part B, sitting 2 and sitting 3). The missed approach is listed only while the ND draws it
/// (44.1 = 1); the alternate and the go-around marker are not listed (Ruling 43: never seen drawn in flight).
/// </summary>
internal static class FenixChannelNdRoute
{
    /// <summary>The name the route layer's status row and problems carry.</summary>
    public const string LayerName = "Route";

    /// <summary>The name the missed approach's status row and problem carry (field 44 in another shape).</summary>
    public const string MissedApproachName = "Missed approach";

    /// <summary>The name the bottom right label's layer carries.</summary>
    public const string LabelsLayerName = "Terrain and weather";

    // The ND declutters at 160 and above (sitting 1, Step 14).
    private const double DeclutterRangeNm = 160;

    // A marker lies along a leg when it is within this distance across it (spec row 56).
    private const double AlongRouteNm = 0.5;

    // Two points are one within this distance: a constraint and its waypoint, 2.11 and its symbol, a waypoint of two lists.
    private const double SamePointNm = 0.1;

    // The cross-track error is drawn from this size: CHOSEN between 0.05 NM (not drawn) and 0.18 NM (drawn), sitting 2.
    private const double CrossTrackDrawnNm = 0.1;

    private const double GrowPx = 10;     // the window grown by this much bounds every walk along the line
    private const double StepPx = 1;      // a walk along the line looks at every pixel

    /// <summary>The look items of the route layer, each a <see cref="FenixChannelNdLook"/> entry.</summary>
    internal static class LookItem
    {
        public const string ToWaypoint = "TO waypoint";
        public const string Waypoint = "route waypoint";
        public const string Vor = "route VOR";
        public const string ToVor = "TO VOR";
        public const string Runway = "runway";
        public const string RouteLine = "route line";
        public const string DashedRouteLine = "route line in HDG";
        public const string MissedApproachLine = "missed approach line";
        public const string MissedApproachHold = "missed approach hold";
        public const string TemporaryLine = "temporary flight plan line";
        public const string ConstraintRing = "constraint ring";
        public const string ConstraintLabel = "constraint label";
        public const string Tod = "top of descent";
        public const string Toc = "top of climb";
        public const string Eod = "end of descent";
        public const string Decel = "deceleration point";
        public const string Cross = "cross marker";
        public const string SpeedLimit = "speed limit";
        public const string SpeedLimitRose = "speed limit in ROSE NAV";
        public const string Etp = "(ETP)";
        public const string HeadingBug = "heading bug";
        public const string TrackLine = "track line";
        public const string CrossTrack = "cross-track error";
        public const string RangeRing = "range ring";
        public const string CompassRing = "compass ring";
        public const string FixInfoFix = "FIX INFO fix";
        public const string FixInfoLines = "FIX INFO rings and radials";
        public const string Terr = "TERR label";
        public const string WxThH = "WX+T+H label";
        public const string PwsScan = "PWS SCAN label";
        public const string WxrOff = "WXR OFF label";
    }

    // ---- The layers Compose runs.

    /// <summary>
    /// The route layer for a snapshot. The route is drawn in ARC, ROSE NAV and PLAN; ROSE LS and ROSE VOR draw none of
    /// this layer. No flight plan ("no route data" while the next waypoint reads PPOS, or a route with no legs) is no
    /// fault: the route lists nothing. A route or ND status that cannot be read, a missing active leg or one that matches
    /// no waypoint gives one status row in the route's place ("Route: not available, field 2.4.3 has the wrong shape"),
    /// and the layer's problem for the log; the rings are listed whatever the route.
    /// </summary>
    public static FenixNdLayer Layer(FenixChannelSnapshot s, FenixNdData header)
    {
        if (s.Push is null || header.Mode is not ("ARC" or "NAV" or "PLAN")) return FenixNdLayer.Of([]);

        FenixNdFrame? frame = FenixNdFrame.From(header);
        FenixChannelSection<FenixNdStatus> status = ReadStatus(s.Push);
        FenixChannelSection<FenixRoute> read = FenixChannelRoute.Decode(s.Push);

        string? problem = null;
        FenixRoute? route = null;
        if (!status.IsOk) problem = status.Problem;
        else if (read.IsOk) route = read.Value!.Legs.Count > 0 ? read.Value : null;
        else if (!IsNoFlightPlan(read.Problem, header)) problem = read.Problem;

        if (problem is null && route is not null)
        {
            if (route.ActiveLeg is not { } active) problem = "no active leg";
            else if (frame is null) problem = "the ND's view is not known";
            else if (ToIndex(route, active) is null) problem = ActiveLegUnmatched;
        }

        var rows = new List<string>();
        if (problem is not null) rows.Add($"{LayerName}: not available, {problem}");
        else if (route is not null) rows.AddRange(Rows(frame!, route, header, status.Value!).Rows);

        // Field 44 in another shape: the route reads as if it were absent, and one row says so in the missed approach's place.
        string? missedProblem = problem is null ? route?.MissedApproachProblem : null;
        if (missedProblem is not null) rows.Add($"{MissedApproachName}: not available, {missedProblem}");

        if (status.IsOk) rows.AddRange(StatusRows(header, status.Value!));
        rows.AddRange(RingRows(header, FenixChannelNdTraffic.TcasRingDrawn(s.Push, header)));
        if (problem is null && route is not null && frame is not null && header.Mode is "ARC" or "NAV")
        {
            rows.AddRange(FixInfoRows(frame, route));
        }

        string? layerProblem = problem is not null ? $"{LayerName}: {problem}"
            : missedProblem is not null ? $"{MissedApproachName}: {missedProblem}" : null;
        return new FenixNdLayer(rows, layerProblem, null);
    }

    /// <summary>
    /// The bottom right label (sitting 1 Part B, sitting 2 "The header in flight"), in ARC and the ROSE modes (PLAN prints
    /// none): "TERR" cyan with the captain's TERR ON ND (53), "WX+T+H" green with the radar on (191.4), "PWS SCAN" green
    /// while the predictive windshear scans (191.8), "WXR OFF" white with none of them and no predictive windshear
    /// enabled (191.9); nothing with 191.9 alone. Two of 53, 191.4 and 191.8 at once were never measured: nothing is said.
    /// The terrain's two boxed numbers are not sent.
    /// </summary>
    public static FenixNdLayer LabelsLayer(FenixChannelSnapshot s, FenixNdData header)
    {
        if (s.Push is null || header.Mode is not ("ARC" or "NAV" or "LS" or "VOR")) return FenixNdLayer.Of([]);

        FenixChannelPush push = s.Push;
        bool? terrain = Flag(push, Remote.TerrainOnNdCapt);
        if (terrain is null) return FenixNdLayer.Failed(LabelsLayerName, Wrong("53"));

        bool radarOn = false, scanning = false, enabled = false;
        if (push.TryGet(Remote.WeatherRadarData, out FenixChannelField field))
        {
            if (field.AsMessage() is not { } radar) return FenixNdLayer.Failed(LabelsLayerName, Wrong("191"));

            bool? on = Flag(radar, WeatherRadarState.SysOn), scan = Flag(radar, WeatherRadarState.PredWindshearScan),
                armed = Flag(radar, WeatherRadarState.PredWindshearEnabled);
            if (on is null) return FenixNdLayer.Failed(LabelsLayerName, Wrong("191.4"));
            if (scan is null) return FenixNdLayer.Failed(LabelsLayerName, Wrong("191.8"));
            if (armed is null) return FenixNdLayer.Failed(LabelsLayerName, Wrong("191.9"));
            (radarOn, scanning, enabled) = (on.Value, scan.Value, armed.Value);
        }

        int printing = (terrain.Value ? 1 : 0) + (radarOn ? 1 : 0) + (scanning ? 1 : 0);
        string? item = printing switch
        {
            0 => enabled ? null : LookItem.WxrOff,
            1 => terrain.Value ? LookItem.Terr : radarOn ? LookItem.WxThH : LookItem.PwsScan,
            _ => null
        };
        if (item is null) return FenixNdLayer.Of([]);

        return FenixNdLayer.Of([$"Bottom right: {Words(item)}"]);
    }

    // ---- The route's own rows.

    /// <summary>
    /// The rows of the route itself, in route order from the active leg: the line's look, then each waypoint the ND
    /// draws (only the FROM of the passed ones, green; the TO white; the later ones green) with its constraint ring and,
    /// with CSTR, the constraint as printed, the runways, the markers where each lies along the route, holds after their
    /// fix, where the line leaves the screen and where it ends and starts again; then the markers that lie along no leg,
    /// nearest first; then the temporary plan while one is pending. Nothing the frame clips; at 160 and above nothing
    /// that declutters. A route with no active leg (2.11), or one whose active leg matches no waypoint
    /// (<see cref="ToIndex"/>), gives one status row.
    /// </summary>
    public static FenixNdLayer Rows(FenixNdFrame f, FenixRoute r, FenixNdData header, FenixNdStatus status)
    {
        if (r.ActiveLeg is not { } active) return FenixNdLayer.Failed(LayerName, "no active leg");

        string? centerIdent = f.Mode == "PLAN" ? FenixChannelNdRows.PlanCenterIdent(f, r) : null;
        bool declutter = f.RangeNm >= DeclutterRangeNm;
        bool cstr = header.MapOption == "CSTR";
        var rows = new List<string>();

        // The active route's symbols (the TO and FROM logic runs on these, the same list ToIndex counts in) and, while the
        // ND draws it (44.1), the missed approach's, listed after the route's in the plain route waypoint look: the look
        // of an active missed approach's TO is unmeasured, so no TO color is claimed for it.
        List<FenixRouteLeg> listedSymbols = [.. ListedSymbols(r.Legs, r.MissedApproachDrawn)];
        List<FenixRouteLeg> symbols = [.. listedSymbols.Where(leg => !leg.MissedApproach)];
        if (ToIndex(r, active) is not { } to) return FenixNdLayer.Failed(LayerName, ActiveLegUnmatched);

        List<Vertex> path = Path(r.Legs);
        var items = new List<Item>();
        LineRows(f, path, centerIdent, items, out bool seen, Words(status.SolidRoute ? LookItem.RouteLine : LookItem.DashedRouteLine),
            out string lineRow, "route", "route line");
        if (seen) rows.Add(lineRow);

        // One waypoint symbol's row: its look, its constraint ring and, with CSTR, the constraint as printed. The
        // missed approach's constraints are the go-around markers (2.12 member 6 = 1), the route's are not.
        string? SymbolRow(FenixRouteLeg symbol, bool isTo)
        {
            if (declutter && symbol.RemoveOnDeclutter) return null;
            if (FenixChannelNdGeometry.PlaceRoute(f, symbol.Point) is not { } place) return null;

            string look = symbol.Symbol switch
            {
                RouteSymbol.Waypoint => Words(isTo ? LookItem.ToWaypoint : LookItem.Waypoint),
                RouteSymbol.Vor => Words(isTo ? LookItem.ToVor : LookItem.Vor),
                _ => "waypoint"
            };

            string constraint = "";
            FenixRouteMarker? ring = r.Markers
                .Where(m => m.Type == MapMarkerKind.AltCstr && m.IsGoAround == symbol.MissedApproach
                    && !(declutter && m.RemoveOnDeclutter) && Distance(m.Point, symbol.Point) <= SamePointNm)
                .Select(m => (FenixRouteMarker?)m)
                .FirstOrDefault();
            if (ring is { } marker)
            {
                string? color = MarkerColor(marker.Color);
                string ringWord = FenixChannelNdLook.For(LookItem.ConstraintRing)!.Symbol;
                look += color is null ? $" in a {ringWord}" : $" in {(color == "amber" ? "an" : "a")} {color} {ringWord}";
                if (cstr && Printed(marker.Label) is { Length: > 0 } text)
                {
                    constraint = $"; {text}";
                }
            }

            return $"Map: {symbol.Ident}, {look}, {FenixChannelNdGeometry.Where(f, place, centerIdent)}{constraint}";
        }

        // The waypoints: the FROM (the last passed), the TO and every later one.
        for (int i = Math.Max(0, to - 1); i < symbols.Count; i++)
        {
            if (SymbolRow(symbols[i], i == to) is { } row)
            {
                items.Add(new Item(Project(path, symbols[i].Point).Key, Order.Waypoint, row));
            }
        }

        // The departure and arrival runways (2.1), where they are drawn.
        foreach (FenixRouteRunway runway in r.Runways.Take(2))
        {
            if (FenixChannelNdGeometry.PlaceRoute(f, runway.Threshold) is not { } place) continue;

            string name = string.Join(" ", new[] { runway.Airport.Trim(), runway.Runway.Trim() }.Where(part => part.Length > 0));
            items.Add(new Item(Project(path, runway.Threshold).Key, Order.Runway,
                $"Map: {name}, {Words(LookItem.Runway)}, {FenixChannelNdGeometry.Where(f, place, centerIdent)}"));
        }

        // The markers the ND draws, between the waypoints they lie between; those along no leg after the route.
        var aside = new List<(double Distance, string Row)>();
        foreach (FenixRouteMarker marker in r.Markers)
        {
            if (marker.IsGoAround || (declutter && marker.RemoveOnDeclutter)) continue;
            if (MarkerLook(marker, f.Mode) is not { } look) continue;
            if (FenixChannelNdGeometry.PlaceRoute(f, marker.Point) is not { } place) continue;

            string row = $"Map: {look.Name}, {look.Words}, {FenixChannelNdGeometry.Where(f, place, centerIdent)}";
            (double key, double across) = Project(path, marker.Point);
            if (across <= AlongRouteNm) items.Add(new Item(key, Order.Marker, row));
            else aside.Add((place.DistanceNm, row));
        }

        // Holds after their fix: each run of elements with hold members, while any of it is on the screen.
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].Leg.Hold is not { } hold) continue;

            int last = i;
            while (last + 1 < path.Count && path[last + 1].Leg.Hold is not null) last++;
            bool drawn = Enumerable.Range(i, last - i + 1)
                .Any(k => FenixChannelNdGeometry.PlaceRoute(f, path[k].Leg.Point) is not null);
            if (drawn)
            {
                string inbound = FenixChannelNdRows.CompassText(path[last].Leg.Hold!.InboundCourse);
                items.Add(new Item(path[i].Key, Order.Hold,
                    $"Map: hold at {path[i].Leg.Ident}, inbound {inbound}, {(hold.RightTurns ? "right" : "left")} turns"));
            }

            i = last;
        }

        rows.AddRange(Sorted(items));
        rows.AddRange(aside.OrderBy(entry => entry.Distance).Select(entry => entry.Row));
        if (r.MissedApproachDrawn)
        {
            rows.AddRange(MissedApproachRows(f, r, listedSymbols.Where(leg => leg.MissedApproach), SymbolRow, centerIdent));
        }

        rows.AddRange(TemporaryRows(f, r, symbols, declutter, centerIdent));
        return FenixNdLayer.Of(rows);
    }

    // The route's problem when 2.11 names no route waypoint, no missed approach waypoint and not the arrival runway.
    private const string ActiveLegUnmatched = "the active leg matches no waypoint";

    /// <summary>
    /// The TO waypoint's index among the active route's symbols (the active list's <see cref="ListedSymbols"/> without
    /// the missed approach's), the one answer the layer and its rows both use: the symbol with the active leg's (2.11)
    /// ident within 0.1 NM of it. With none, 2.11 may lie past the route, and then every route symbol is passed (their
    /// count, so the last is the FROM): on one of the active list's missed approach symbols (same ident within 0.1 NM,
    /// whatever 44.1 says; Ruling 99: the landing roll, a go-around), or on the arrival runway, which has no symbol of its
    /// own (sitting 2: 2.11 read "KCVG18L" from 21:51:20 on the approach). Otherwise null: the layer says so in its status
    /// row and never guesses a TO waypoint.
    /// </summary>
    private static int? ToIndex(FenixRoute r, FenixRouteLeg active)
    {
        bool Matches(FenixRouteLeg symbol)
            => symbol.Ident == active.Ident && Distance(symbol.Point, active.Point) <= SamePointNm;

        List<FenixRouteLeg> symbols = [.. ListedSymbols(r.Legs, false)];
        int to = symbols.FindIndex(Matches);
        if (to >= 0) return to;

        bool onMissedApproach = r.Legs.Any(leg => leg.Kind == RouteLegKind.Symbol && leg.MissedApproach && Matches(leg));
        string arrival = FenixChannelRoute.ArrivalIdent(r.Runways);
        bool onArrivalRunway = arrival.Length > 0 && active.Ident == arrival;
        return onMissedApproach || onArrivalRunway ? symbols.Count : null;
    }

    /// <summary>
    /// The waypoint symbols of one route list that the ND draws as route waypoints: the missed approach's only while the
    /// ND draws it (<paramref name="missedApproachDrawn"/>, 44.1; sitting 3). The route layer lists these for the active
    /// and the temporary list only, and the map points layer asks the same two lists through this helper, so a map point
    /// is the route's exactly when this layer lists it (Ruling 53).
    /// </summary>
    internal static IEnumerable<FenixRouteLeg> ListedSymbols(IEnumerable<FenixRouteLeg> legs, bool missedApproachDrawn)
        => legs.Where(leg => leg.Kind == RouteLegKind.Symbol && (missedApproachDrawn || !leg.MissedApproach));

    // The missed approach while the ND draws it (44.1 = 1; Ruling 91, sitting 3): one row for its line, cyan and solid,
    // from the runway threshold through its waypoints; its waypoints and constraints; a hold as one row for the arc at
    // its fix (not its legs). The hold's legs are no part of the line: they were not drawn.
    private static IEnumerable<string> MissedApproachRows(
        FenixNdFrame f, FenixRoute r, IEnumerable<FenixRouteLeg> symbols, Func<FenixRouteLeg, bool, string?> symbolRow,
        string? centerIdent)
    {
        List<FenixRouteLeg> legs = [.. r.Legs.Where(FenixChannelRoute.IsPath)];
        int first = legs.FindIndex(leg => leg.MissedApproach);
        if (first < 0) yield break;

        // The line starts at the last element of the route before it (the runway threshold), unless that one breaks it.
        var line = new List<FenixRouteLeg>();
        if (first > 0 && !FenixChannelRoute.BreaksPath(legs[first - 1])) line.Add(legs[first - 1]);
        line.AddRange(legs.Where(leg => leg.MissedApproach && leg.Hold is null));
        List<Vertex> path = BuildPath(line);

        var items = new List<Item>();
        LineRows(f, path, centerIdent, items, out bool seen, Words(LookItem.MissedApproachLine), out string lineRow,
            "missed approach", "missed approach line");
        if (seen) yield return lineRow;

        foreach (FenixRouteLeg symbol in symbols)
        {
            if (symbolRow(symbol, false) is { } row) items.Add(new Item(Project(path, symbol.Point).Key, Order.Waypoint, row));
        }

        // A hold: each run of its elements, one row while its fix is on the screen.
        List<FenixRouteLeg> missed = [.. legs.Where(leg => leg.MissedApproach)];
        for (int i = 0; i < missed.Count; i++)
        {
            if (missed[i].Hold is null) continue;

            if (FenixChannelNdGeometry.PlaceRoute(f, missed[i].Point) is not null)
            {
                items.Add(new Item(Project(path, missed[i].Point).Key, Order.Hold,
                    $"Map: hold at {missed[i].Ident}, {Words(LookItem.MissedApproachHold)}"));
            }

            while (i + 1 < missed.Count && missed[i + 1].Hold is not null) i++;
        }

        foreach (string row in Sorted(items)) yield return row;
    }

    // The temporary plan (2.5) while one is pending: its line from where it starts, the waypoints the active route does
    // not draw at the same place, where its line leaves the screen and its gaps. Its waypoints' look was not measured.
    private static IEnumerable<string> TemporaryRows(
        FenixNdFrame f, FenixRoute r, IReadOnlyList<FenixRouteLeg> activeSymbols, bool declutter, string? centerIdent)
    {
        if (r.TemporaryLegs.Count == 0) yield break;

        List<Vertex> path = Path(r.TemporaryLegs);
        var items = new List<Item>();
        LineRows(f, path, centerIdent, items, out bool seen, Words(LookItem.TemporaryLine), out _, "temporary flight plan",
            "temporary flight plan line");
        if (seen)
        {
            FenixNdPlace? start = path.Count > 0 ? FenixChannelNdGeometry.PlaceRoute(f, path[0].Leg.Point) : null;
            string from = start is { } at ? ", starting " + FenixChannelNdGeometry.Where(f, at with { AtEdge = false }, centerIdent) : "";
            yield return $"Map: temporary flight plan, {Words(LookItem.TemporaryLine)}{from}";
        }

        foreach (FenixRouteLeg symbol in ListedSymbols(r.TemporaryLegs, false))
        {
            if (declutter && symbol.RemoveOnDeclutter) continue;
            if (activeSymbols.Any(leg => leg.Ident == symbol.Ident && Distance(leg.Point, symbol.Point) <= SamePointNm)) continue;
            if (FenixChannelNdGeometry.PlaceRoute(f, symbol.Point) is not { } place) continue;

            items.Add(new Item(Project(path, symbol.Point).Key, Order.Waypoint,
                $"Map: {symbol.Ident}, temporary flight plan waypoint, {FenixChannelNdGeometry.Where(f, place, centerIdent)}"));
        }

        foreach (string row in Sorted(items)) yield return row;
    }

    // The line of one route list: where it leaves the screen and where it ends and starts again inside it, as items;
    // `seen` when any of it is drawn, with the row that says its look.
    private static void LineRows(FenixNdFrame f, List<Vertex> path, string? centerIdent, List<Item> items, out bool seen,
        string look, out string lineRow, string subject, string lineName)
    {
        seen = false;
        lineRow = $"Map: {lineName}, {look}";
        for (int i = 1; i < path.Count; i++)
        {
            Vertex a = path[i - 1], b = path[i];
            if (b.Joined)
            {
                (List<(double Fraction, string Edge, FenixNdPlace Place)> exits, bool drawn) = Walk(f, a.Leg.Point, b.Leg.Point);
                seen |= drawn;
                foreach ((double fraction, string edge, FenixNdPlace place) in exits)
                {
                    string toward = b.Leg.Ident.Length > 0 ? $", toward {b.Leg.Ident}" : "";
                    items.Add(new Item(a.Key + fraction * (b.Key - a.Key), Order.Exit,
                        $"Map: {subject} goes off the {edge} edge{toward}, {FenixChannelNdGeometry.Where(f, place, centerIdent)}"));
                }
            }

            // A discontinuity: the line ends at the last vertex drawn and starts again at the next start, each only
            // where that end lies on the screen. The word "discontinuity" is not drawn, so it is never said.
            if (FenixChannelRoute.BreaksPath(b.Leg) && FenixChannelNdGeometry.PlaceRoute(f, a.Leg.Point) is not null)
            {
                string ident = b.Leg.Ident.Length > 0 ? b.Leg.Ident : a.Leg.Ident;
                items.Add(new Item(a.Key, Order.Ends, $"Map: {lineName} ends at {ident}"));
            }

            if (b.Leg.Kind == RouteLegKind.Start && FenixChannelRoute.BreaksPath(a.Leg)
                && FenixChannelNdGeometry.PlaceRoute(f, b.Leg.Point) is not null)
            {
                items.Add(new Item(b.Key, Order.StartsAgain, $"Map: {lineName} starts again at {b.Leg.Ident}"));
            }
        }
    }

    // ---- The rows that do not need a flight plan.

    // The heading bug, the track line and the cross-track error, in ARC and ROSE NAV (the modes the flight saw them in;
    // the cross-track error in ARC only, the only mode it was seen drawn in).
    private static IEnumerable<string> StatusRows(FenixNdData header, FenixNdStatus status)
    {
        if (header.Mode is not ("ARC" or "NAV") || header.MagneticHeading is not { } heading) yield break;

        // 58.2 draws the bug at 113.2; 113.2 alone never does (it stays after the bug has gone). The ND prints no number
        // by the bug: its place is the compass reading it sits at, as the heading row reads the one under the lubber line.
        if (status.HeadingBug && header.SelectedHeading is { } selected)
        {
            double off = Signed(selected - heading);
            if (FenixChannelNdGeometry.OnCompass(header.Mode, off))
            {
                yield return $"Map: heading bug, {Words(LookItem.HeadingBug)}, at {FenixChannelNdRows.CompassText(selected)} on the compass";
            }
        }

        if (status.TrackLine && header.MagneticTrack is { } track)
        {
            yield return $"Map: track line, {Words(LookItem.TrackLine)}, {Side(Signed(track - heading), "ahead")}";
        }

        if (header.Mode == "ARC" && status.CrossTrack is { } error && Math.Abs(error) >= CrossTrackDrawnNm)
        {
            string figure = Math.Abs(error).ToString("0.0", CultureInfo.InvariantCulture) + (error < 0 ? "L" : "R");
            string place = error < 0 ? "left of the aircraft" : "beside the aircraft";
            yield return $"Map: cross-track error {figure}, {place}";
        }
    }

    // The range rings (sitting 1, Step 12): ARC a ring with no label at a quarter of the range and two labeled at half
    // and three quarters; ROSE NAV and PLAN a dashed ring labeled a quarter of the range and the solid compass ring
    // labeled half. At range 10 the TCAS ring (2.5 NM) hides the dashed ring at 2.5 NM, the quarter of the range: ARC
    // draws nothing there with the TCAS on, and ROSE NAV draws only the ring's "2.5" label, listed as "label only"
    // (sitting 3). Whether the TCAS ring is drawn is FenixChannelNdTraffic.TcasRingDrawn's to say.
    private static IEnumerable<string> RingRows(FenixNdData header, bool tcasRing)
    {
        if (!NumberParsing.TryParseExternal(header.Range, out double range) || !(range > 0) || double.IsInfinity(range))
        {
            yield break;
        }

        static string Figure(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        string dashed = Words(LookItem.RangeRing);
        bool hidden = tcasRing && range == 10;
        if (header.Mode == "ARC")
        {
            if (!hidden) yield return $"Map: range ring with no label, {dashed}";
            yield return $"Map: range ring {Figure(range / 2)}, {dashed}";
            yield return $"Map: range ring {Figure(range * 3 / 4)}, {dashed}";
        }
        else if (header.Mode is "NAV" or "PLAN")
        {
            yield return hidden
                ? $"Map: range ring {Figure(range / 4)}, label only"
                : $"Map: range ring {Figure(range / 4)}, {dashed}";
            yield return $"Map: range ring {Figure(range / 2)}, {Words(LookItem.CompassRing)}";
        }
    }

    // FIX INFO (2.13), in ARC and ROSE NAV: the rings and radials while any of them is on the screen, even with the
    // fix off it, then the fix itself where it is drawn: its own symbol, green.
    private static IEnumerable<string> FixInfoRows(FenixNdFrame f, FenixRoute r)
    {
        foreach (FenixFixInfo fix in r.FixInfos)
        {
            FenixNdPlace? place = FenixChannelNdGeometry.PlaceRoute(f, fix.Point);
            bool lines = fix.Rings.Any(ring => RingSeen(f, fix, ring)) || fix.Radials.Any(radial => RadialSeen(f, fix, radial));
            if (place is null && !lines) continue;

            var parts = new List<string>();
            if (fix.Rings.Count > 0)
            {
                parts.Add($"rings {List(fix.Rings.Select(ring => ring.ToString("0.##", CultureInfo.InvariantCulture)))} NM");
            }

            if (fix.Radials.Count > 0) parts.Add($"radials {List(fix.Radials.Select(radial => FenixChannelNdRows.CompassText(radial)))}");
            string drawn = parts.Count > 0 ? $": {string.Join(", ", parts)}, {Words(LookItem.FixInfoLines)}" : "";
            yield return $"Map: FIX INFO {fix.Ident}{drawn}";

            if (place is { } at)
            {
                string symbol = FixSymbol(fix.Kind) ?? Words(LookItem.FixInfoFix);
                yield return $"Map: {fix.Ident}, {symbol}, {FenixChannelNdGeometry.Where(f, at, null)}";
            }
        }
    }

    // The fix's own symbol: the map point look of its kind (Part C; kind 2, a VOR without DME, the cross sitting 2
    // measured), without that look's magenta.
    private static string? FixSymbol(int kind) => kind switch
    {
        1 => FenixChannelNdLook.For(FenixChannelNdMapData.LookItem.Airport)?.Symbol,
        2 => FenixChannelNdLook.For(FenixChannelNdMapData.LookItem.Vor)?.Symbol,
        3 => FenixChannelNdLook.For(FenixChannelNdMapData.LookItem.VorDme)?.Symbol,
        4 => FenixChannelNdLook.For(FenixChannelNdMapData.LookItem.Dme)?.Symbol,
        5 => FenixChannelNdLook.For(FenixChannelNdMapData.LookItem.Fix)?.Symbol,
        7 => FenixChannelNdLook.For(FenixChannelNdMapData.LookItem.Ndb)?.Symbol,
        _ => null
    };

    private static bool RingSeen(FenixNdFrame f, FenixFixInfo fix, double radiusNm)
    {
        (double cx, double cy) = FenixChannelNdGeometry.Pixel(f, fix.Point);
        double radius = radiusNm * FenixChannelNdGeometry.PixelsPerNm(f);
        for (int degrees = 0; degrees < 360; degrees += 2)
        {
            double rad = degrees * Math.PI / 180;
            if (FenixChannelNdGeometry.RoutePixel(f, cx + Math.Sin(rad) * radius, cy - Math.Cos(rad) * radius).Drawn) return true;
        }

        return false;
    }

    // A radial runs from the fix outward on its magnetic bearing (true = magnetic + the variation at the fix). It is
    // clipped to the window first (Clip), outside which nothing is drawn, so the walk looks only at the pixels the window
    // holds however far the fix is; the steps keep their places along the radial, 2 px apart from the fix.
    private static bool RadialSeen(FenixNdFrame f, FenixFixInfo fix, double radial)
    {
        const double Step = 2;
        (double x, double y) = FenixChannelNdGeometry.Pixel(f, fix.Point);
        double rad = (radial + fix.MagneticVariation - f.UpTrueDegrees) * Math.PI / 180;
        double reach = 2 * (FenixChannelNdGeometry.WindowWidthPx + FenixChannelNdGeometry.WindowHeightPx)
            + Math.Sqrt(x * x + y * y);
        double dx = Math.Sin(rad) * reach, dy = -Math.Cos(rad) * reach;
        if (!Clip(x, y, dx, dy, out double t0, out double t1)) return false;

        for (double along = Math.Ceiling(t0 * reach / Step) * Step; along <= t1 * reach; along += Step)
        {
            if (FenixChannelNdGeometry.RoutePixel(f, x + Math.Sin(rad) * along, y - Math.Cos(rad) * along).Drawn) return true;
        }

        return false;
    }

    // ---- The route's line.

    // One vertex of the drawn line, its distance along the route from the first vertex and whether a line is drawn to
    // it from the one before (a line or an arc; a start and an end draw none).
    private sealed record Vertex(FenixRouteLeg Leg, double Key, bool Joined);

    // The line's vertices in route order: kinds 0 to 3, never the missed approach (Ruling 43).
    private static List<Vertex> Path(IEnumerable<FenixRouteLeg> legs)
        => BuildPath(legs.Where(leg => FenixChannelRoute.IsPath(leg) && !leg.MissedApproach));

    // The vertices of a run of path elements, each with its distance along the run.
    private static List<Vertex> BuildPath(IEnumerable<FenixRouteLeg> legs)
    {
        var path = new List<Vertex>();
        double key = 0;
        FenixRouteLeg? previous = null;
        foreach (FenixRouteLeg leg in legs)
        {
            if (previous is { } p) key += Distance(p.Point, leg.Point);
            bool joined = previous is not null && leg.Kind is RouteLegKind.Line or RouteLegKind.Arc;
            path.Add(new Vertex(leg, key, joined));
            previous = leg;
        }

        return path;
    }

    // Where a point lies along the line: its distance along the route at the nearest point of a drawn leg or a vertex
    // (the earliest in route order of equally near ones) and how far it is from there.
    private static (double Key, double Across) Project(List<Vertex> path, FenixLatLon p)
    {
        (double Key, double Across) best = (0, double.PositiveInfinity);
        for (int i = 0; i < path.Count; i++)
        {
            Vertex b = path[i];
            if (b.Joined && b.Key - path[i - 1].Key > 1e-3)
            {
                Vertex a = path[i - 1];
                (double along, double cross) = FenixChannelNdGeometry.AlongCross(a.Leg.Point, b.Leg.Point, p);
                if (along > 0 && along < b.Key - a.Key && Math.Abs(cross) < best.Across - 1e-9)
                {
                    best = (a.Key + along, Math.Abs(cross));
                }
            }

            double away = Distance(b.Leg.Point, p);
            if (away < best.Across - 1e-9) best = (b.Key, away);
        }

        return best;
    }

    // Walks the line from a to b pixel by pixel through the window: where it leaves the area the ND draws route items
    // in (the fraction of the way, the edge, the place), and whether any of it is drawn.
    private static (List<(double Fraction, string Edge, FenixNdPlace Place)> Exits, bool Drawn) Walk(
        FenixNdFrame f, FenixLatLon a, FenixLatLon b)
    {
        var exits = new List<(double, string, FenixNdPlace)>();
        (double ax, double ay) = FenixChannelNdGeometry.Pixel(f, a);
        (double bx, double by) = FenixChannelNdGeometry.Pixel(f, b);
        double dx = bx - ax, dy = by - ay;
        if (!Clip(ax, ay, dx, dy, out double t0, out double t1)) return (exits, false);

        bool Inside(double t) => FenixChannelNdGeometry.RoutePixel(f, ax + t * dx, ay + t * dy).Drawn;

        int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) * (t1 - t0) / StepPx));
        double before = t0;
        bool wasInside = Inside(t0), drawn = wasInside;
        for (int k = 1; k <= steps; k++)
        {
            double t = t0 + (t1 - t0) * k / steps;
            bool inside = Inside(t);
            if (wasInside && !inside)
            {
                double lo = before, hi = t;
                for (int n = 0; n < 40; n++)
                {
                    double mid = (lo + hi) / 2;
                    if (Inside(mid)) lo = mid;
                    else hi = mid;
                }

                double x = ax + hi * dx, y = ay + hi * dy;
                exits.Add((hi, FenixChannelNdGeometry.RoutePixel(f, x, y).Edge, FenixChannelNdGeometry.PlaceOfPixel(f, x, y)));
            }

            drawn |= inside;
            wasInside = inside;
            before = t;
        }

        return (exits, drawn);
    }

    // The part of the pixel segment (x, y) + t (dx, dy), 0 <= t <= 1, inside the window grown by GrowPx (Liang-Barsky).
    // Everything the ND draws lies inside it, so the walk starts and ends outside the drawn area unless at a or b.
    private static bool Clip(double x, double y, double dx, double dy, out double t0, out double t1)
    {
        t0 = 0;
        t1 = 1;
        double[] p = [-dx, dx, -dy, dy];
        double[] q =
        [
            x + GrowPx, FenixChannelNdGeometry.WindowWidthPx + GrowPx - x,
            y + GrowPx, FenixChannelNdGeometry.WindowHeightPx + GrowPx - y
        ];
        for (int i = 0; i < 4; i++)
        {
            if (p[i] == 0)
            {
                if (q[i] < 0) return false;
                continue;
            }

            double t = q[i] / p[i];
            if (p[i] < 0) t0 = Math.Max(t0, t);
            else t1 = Math.Min(t1, t);
        }

        return t0 <= t1;
    }

    // ---- Words.

    private enum Order { Runway, StartsAgain, Waypoint, Hold, Marker, Exit, Ends }

    private sealed record Item(double Key, Order Order, string Row);

    private static IEnumerable<string> Sorted(List<Item> items)
        => items.Select((item, index) => (item, index))
            .OrderBy(entry => Math.Round(entry.item.Key, 3))
            .ThenBy(entry => entry.item.Order)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.item.Row);

    private static string Words(string item) => FenixChannelNdRows.Words(item);

    // A marker's name and look: the label the ND prints when it prints one, else the contract's name in plain words
    // ("(ETP)" is printed beside its circle); its color the channel's. Null for a kind no capture saw drawn (SOC,
    // TimeMarker, Debug, the speed constraint alone) and for the constraints, which ring their waypoint.
    private static (string Name, string Words)? MarkerLook(FenixRouteMarker marker, string mode)
    {
        string? item = marker.Type switch
        {
            MapMarkerKind.Tod => LookItem.Tod,
            MapMarkerKind.Toc => LookItem.Toc,
            MapMarkerKind.Eod => LookItem.Eod,
            MapMarkerKind.Decel => LookItem.Decel,
            MapMarkerKind.Cross => LookItem.Cross,
            MapMarkerKind.SpeedLimit => mode == "NAV" ? LookItem.SpeedLimitRose : LookItem.SpeedLimit,
            MapMarkerKind.Etp => LookItem.Etp,
            _ => null
        };
        if (item is null) return null;

        string name = Printed(marker.Label) is { Length: > 0 } label ? label
            : item == LookItem.SpeedLimitRose ? LookItem.SpeedLimit : item;
        string symbol = FenixChannelNdLook.For(item)!.Symbol;
        string? color = MarkerColor(marker.Color);
        return (name, color is null ? symbol : $"{color} {symbol}");
    }

    // The color member by the contract's FMCColorEnum (White 0, Amber 1, Green 2, Cyan 3, Compound 4, Yellow 5, Magenta 6,
    // Red 7; spec 2.6), whose 0, 2, 3 and 6 the flights saw drawn at their declared places: absent (0) white. Amber and
    // red, never seen on a marker, are said by their contract names, since the ND says them always (Ruling 98). Compound
    // has no one color and yellow has no spoken name (FenixChannelNdSpeech): no color is said for them.
    private static string? MarkerColor(int? value) => (value ?? 0) switch
    {
        0 => "white",
        1 => "amber",
        2 => "green",
        3 => "cyan",
        6 => "magenta",
        7 => "red",
        _ => null
    };

    // A label as the ND prints it: its lines in order, trimmed; a window (two altitudes) printed by its first line
    // (sitting 1, Part B).
    private static string Printed(string label)
    {
        string[] lines = [.. label.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
        if (lines.Length == 2 && !lines.Any(line => line.EndsWith("KT", StringComparison.Ordinal))) lines = [lines[0]];
        return string.Join(" ", lines);
    }

    private static string List(IEnumerable<string> values)
    {
        string[] all = [.. values];
        return all.Length <= 1 ? string.Concat(all) : $"{string.Join(", ", all[..^1])} and {all[^1]}";
    }

    // Degrees off up, -180 up to and including 180.
    private static double Signed(double degrees)
    {
        double off = ((degrees % 360) + 360) % 360;
        return off > 180 ? off - 360 : off;
    }

    private static string Side(double off, string whenZero)
    {
        double whole = Math.Round(off, MidpointRounding.AwayFromZero);
        if (whole == 0) return whenZero;

        return $"{Math.Abs(whole).ToString("0", CultureInfo.InvariantCulture)}° {(whole < 0 ? "left" : "right")}";
    }

    private static double Distance(FenixLatLon a, FenixLatLon b)
        => NavigationCalculator.CalculateDistance(a.Latitude, a.Longitude, b.Latitude, b.Longitude);

    // ---- The fields.

    private static string Wrong(string path) => $"field {path} has the wrong shape";

    // A varint flag: false when left out, null in another shape.
    private static bool? Flag(FenixChannelPush message, int number)
    {
        if (!message.TryGet(number, out FenixChannelField field)) return false;
        return field.Type == FenixWireType.Varint ? field.Varint != 0 : null;
    }

    // The ND status (58) and the cross-track error (52) from the snapshot's push.
    private static FenixChannelSection<FenixNdStatus> ReadStatus(FenixChannelPush push)
    {
        bool trackLine = false, bug = false, solid = false;
        if (push.TryGet(Remote.NdStatusCapt, out FenixChannelField field))
        {
            if (field.AsMessage() is not { } nd) return FenixChannelSection<FenixNdStatus>.WrongShape("58");

            bool? line = Flag(nd, NdStatusData.ShowTrackLine), heading = Flag(nd, NdStatusData.ShowSelectedHeading),
                track = Flag(nd, NdStatusData.ShowTrackSolid);
            if (line is null) return FenixChannelSection<FenixNdStatus>.WrongShape("58.1");
            if (heading is null) return FenixChannelSection<FenixNdStatus>.WrongShape("58.2");
            if (track is null) return FenixChannelSection<FenixNdStatus>.WrongShape("58.3");
            (trackLine, bug, solid) = (line.Value, heading.Value, track.Value);
        }

        double? crosstrack = null;
        if (push.TryGet(Remote.Crosstrack, out FenixChannelField error))
        {
            if (error.Type != FenixWireType.Fixed64) return FenixChannelSection<FenixNdStatus>.WrongShape("52");
            if (double.IsFinite(error.AsDouble())) crosstrack = error.AsDouble();
        }

        return FenixChannelSection<FenixNdStatus>.Ok(new FenixNdStatus(trackLine, bug, solid, crosstrack));
    }

    // The decoder's problem when field 2 is not sent (plan 10's contract).
    private const string NoRouteData = "no route data";

    // No flight plan: field 2 is not sent while the next waypoint reads PPOS. Any other problem (a missing present
    // position included) is a fault.
    private static bool IsNoFlightPlan(string? problem, FenixNdData header)
        => problem == NoRouteData
            && header.Waypoint is { } waypoint
            && FenixChannelText.TrimmedText(waypoint.Line1) == "PPOS";
}
