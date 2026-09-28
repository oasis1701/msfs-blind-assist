using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Whether the runway-incursion warning still runs on the frames where taxi guidance has NO route
/// to follow.
///
/// <para><c>TaxiGuidanceManager.UpdatePosition</c> returns early without a route, and the warning
/// used to be rescued from that early return for one state only: <c>Arrived</c>, the normal end of
/// a landing-exit route, so the pilot still hears "Runway crossing ahead. Hold short." on the way
/// to the stand (runway 16/34 at EIDW lies east of the S6 exit).</para>
///
/// <para>There are now three more ways to finish on the airfield with no route and keep taxiing:
/// the runway-end countdown's "Runway vacated" close-out, and both backtrack endings. All three
/// land in <c>Taxiing</c>. Which of those states the pilot happens to be in should not decide
/// whether they are warned about a runway ahead of them — having the airport's map should.
/// Pure — <c>RunwayIncursionWatchTests</c>.</para>
///
/// <para>The same class owns what "approaching" a hold-short node MEANS for the off-route half of
/// that guard (<see cref="IsApproaching"/>, <see cref="IsApproachingAlongAPath"/>) and which of
/// several in-range nodes the guard speaks about (<see cref="Pick"/>).</para>
/// </summary>
public static class RunwayIncursionWatch
{
    public static bool RunsWithoutARoute(TaxiGuidanceState state, bool hasGraph)
        => hasGraph
           && (state == TaxiGuidanceState.Arrived || state == TaxiGuidanceState.Taxiing);

    /// <summary>
    /// Half-width of the corridor ahead of the aircraft, along its own heading, inside which a
    /// hold-short node counts as being approached by <see cref="IsApproaching"/>. A node the
    /// aircraft is taxiing straight at sits on the centreline of the connector it is on, a few
    /// metres off at most for an aircraft riding the paint imperfectly; a node on a neighbouring
    /// taxiway's hold line is tens of metres off the heading line whatever the range.
    /// </summary>
    public const double ApproachCorridorHalfWidthM = 12.0;

    /// <summary>
    /// Floor for the half-width of the corridor around a path segment into the hold-short node
    /// inside which the aircraft counts as being on that segment (<see cref="IsApproachingAlongAPath"/>).
    /// The segment's own pavement half-width plus <see cref="PathCorridorMarginM"/> is used when
    /// it is wider — an aircraft rolling straight at a runway on a 60 m taxiway may be 25 m off
    /// its centreline and is still on it. Wider than the heading corridor because an aircraft
    /// part-way through a turn onto a connector is beside the connector's centreline, not on it.
    /// </summary>
    public const double PathCorridorMinHalfWidthM = 12.0;

    /// <summary>Added to a segment's pavement half-width for its corridor.</summary>
    public const double PathCorridorMarginM = 5.0;

    /// <summary>
    /// How far the aircraft's heading may differ from a path segment's direction toward the
    /// hold-short node for the aircraft to count as taxiing ALONG that segment. 20°: the
    /// ICAO-standard rapid-exit connector leaves its parallel taxiway at 30°, so at 30° or more an
    /// aircraft taxiing straight past the junction would read as turning onto it, and the
    /// judgement is a strict "less than" for the same reason.
    /// </summary>
    public const double PathHeadingToleranceDeg = 20.0;

    /// <summary>
    /// How far BEFORE a segment's far end the aircraft may still count as on that segment — an
    /// aircraft beginning its turn is a few metres short of the junction node.
    /// </summary>
    public const double PathAlongTrackSlackM = 10.0;

    /// <summary>
    /// How far back from the hold-short node the path test looks along each chain of
    /// degree-two nodes leading into it. Navdata draws a 90° fillet as several short
    /// micro-bend segments; only the last of them is incident to the node, and an aircraft
    /// part-way round the turn is on an earlier one.
    /// </summary>
    public const double PathLookBackMetres = 40.0;

    /// <summary>
    /// Whether a hold-short node <paramref name="distanceM"/> away on true bearing
    /// <paramref name="bearingToNodeDeg"/> is AHEAD of an aircraft on true heading
    /// <paramref name="headingTrueDeg"/> — in front of it, within
    /// <see cref="ApproachCorridorHalfWidthM"/> of its heading line, and no further off that line
    /// than it is ahead (inside a 45° cone, so a node a few metres abeam is not "ahead" merely
    /// because it is close). One of the two tests that make a node "approached" for the
    /// off-route half of the runway incursion guard; the guard's 40 m radius only says which
    /// nodes are worth judging.
    ///
    /// <para>Why proximity alone was wrong, measured at KMEM (2026-09-28, fs2024 navdata):
    /// taxiway M runs the length of runway 18R/36L with a connector every few hundred metres,
    /// and the hold-short node on every one of M1 to M9 sits 34-38 m from M's own centreline,
    /// inside that 40 m radius. A pilot taxiing N, M, M1 for 36L — exactly the cleared route —
    /// heard "Warning: approaching runway 36L at M8, off route" at M8, then M7, M6 … one every
    /// ten seconds, while never leaving the route. Those nodes are ABEAM: 35 m off the heading
    /// line at every range, so this test never sees them.</para>
    ///
    /// <para>Deliberately judged against the aircraft's own motion and the map, and nothing
    /// else — not the route (a route-less taxi after landing has none, and after an
    /// <c>Arrived</c> the route object outlives the taxi), not the off-route detector (its turn
    /// windows and post-advance grace are tuned to suppress recalculation, and would mute this
    /// guard exactly at junctions). The planned-crossing "Crossing runway X." case is not this
    /// rule's to decide.</para>
    /// </summary>
    public static bool IsApproaching(double distanceM, double bearingToNodeDeg, double headingTrueDeg)
    {
        if (!(distanceM > 0)) return false;
        double relRad = RelativeBearingDeg(bearingToNodeDeg, headingTrueDeg) * Math.PI / 180.0;
        double along = distanceM * Math.Cos(relRad);
        double cross = Math.Abs(distanceM * Math.Sin(relRad));
        return along > 0 && cross <= ApproachCorridorHalfWidthM && along >= cross;
    }

    /// <summary>
    /// One segment of a path that leads INTO a hold-short node: <c>To</c> is the end nearer the
    /// node along the path (the node itself for the last segment), <c>From</c> the end further
    /// away. <c>WidthFeet</c> is the segment's pavement width, 0 when unknown.
    /// </summary>
    public readonly record struct PathSegment(
        double FromLat, double FromLon, double ToLat, double ToLon, double WidthFeet);

    /// <summary>
    /// The second "approached" test, for the turn the heading corridor is blind to. A hold line
    /// set back from its parallel taxiway by less than about the aircraft's turn radius is never
    /// AHEAD on the heading line: through the turn the node sits well off the line, and by the
    /// time the heading settles the aircraft is at it. So a node is also approached when the
    /// aircraft is ON a segment of a path that leads into it — within that segment's corridor
    /// (its pavement half-width plus <see cref="PathCorridorMarginM"/>, never less than
    /// <see cref="PathCorridorMinHalfWidthM"/>), not yet past its near end, no more than
    /// <see cref="PathAlongTrackSlackM"/> short of its far end — with its heading within
    /// <see cref="PathHeadingToleranceDeg"/> of the segment's direction toward the node. An
    /// aircraft part-way round a turn onto a connector is on one of the fillet's micro-bend
    /// segments, pointing along it; an aircraft taxiing straight past the junction on the
    /// parallel taxiway sits at the connector's far end but points 90° (30° for a rapid exit)
    /// away from it. The same corridor is what keeps an aircraft rolling straight at a runway
    /// crossing on a wide taxiway, well off its centreline, inside the warning.
    ///
    /// <para><paramref name="pathSegments"/> are every segment of every chain the caller walked
    /// back from the node (<see cref="PathLookBackMetres"/>) on the TAXIWAY side — the caller
    /// leaves out a chain that runs onto runway pavement, because an aircraft vacating the
    /// runway through the hold line is leaving it, not approaching it. A node with none falls
    /// back to the heading corridor alone. <see cref="PathAlongTrackSlackM"/> is applied per
    /// segment, not only at the chain's junction end: a fillet's bends are a few metres apart,
    /// so an aircraft can qualify on the next bend while a few metres short of it, which is the
    /// safe direction. The frame is a local equirectangular projection about the node, exact
    /// enough for the tens of metres involved.</para>
    ///
    /// <para>This test alone sees a 90° turn onto a SHORT straight connector drawn as one segment
    /// (hold line within about 20 m of the junction, no fillet bends) only once the nose is
    /// within 20° of the connector, a few metres before the hold line: in one frame the aircraft
    /// is indistinguishable from one taxiing straight past a rapid-exit junction. Heading
    /// history tells them apart — <see cref="IsApproachingWhileTurning"/> judges a turning
    /// aircraft at the pose its turn is taking it to.
    /// The proximity rule warned from 40 m there, and at every abeam node everywhere. Measured
    /// on fs2024 navdata for this test and the heading corridor ALONE (PR #255 review, every
    /// taxiway-side path up to 150 m into a hold node, driven along its centreline with the turn
    /// taken instantly at each node): of 516,026 approaches, 14,162 were first warned at 10-20 m
    /// and 262 inside 10 m (KDTW 22L at Y10, KGSO 23R at G) — at 15 kt, 20 m is about 2.6 s,
    /// less than the warning takes to say — while passes that do not lead to the node warned on
    /// 106,277 of the 455,803 the proximity rule warned on (15,025 of 199,353 straight). What the
    /// turning test buys back is measured on <see cref="IsApproachingWhileTurning"/>.</para>
    /// </summary>
    public static bool IsApproachingAlongAPath(
        double aircraftLat, double aircraftLon, double headingTrueDeg,
        double nodeLat, double nodeLon,
        IReadOnlyList<PathSegment> pathSegments)
    {
        const double MetresPerDegLat = 111132.0;
        const double MetresPerFoot = 0.3048;
        double metresPerDegLon = MetresPerDegLat * Math.Cos(nodeLat * Math.PI / 180.0);

        double ax = (aircraftLon - nodeLon) * metresPerDegLon;   // aircraft, node-relative, east
        double ay = (aircraftLat - nodeLat) * MetresPerDegLat;   // north

        // Indexed, not foreach: this runs per in-range node per position frame, and a boxed
        // List enumerator per call would be the allocation the guard's scratch lists exist to avoid.
        for (int i = 0; i < pathSegments.Count; i++)
        {
            var seg = pathSegments[i];
            double fx = (seg.FromLon - nodeLon) * metresPerDegLon;
            double fy = (seg.FromLat - nodeLat) * MetresPerDegLat;
            double tx = (seg.ToLon - nodeLon) * metresPerDegLon;
            double ty = (seg.ToLat - nodeLat) * MetresPerDegLat;
            double dx = tx - fx, dy = ty - fy;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1.0) continue;
            dx /= len; dy /= len;

            double segBearingDeg = Math.Atan2(dx, dy) * 180.0 / Math.PI;
            if (Math.Abs(RelativeBearingDeg(segBearingDeg, headingTrueDeg)) >= PathHeadingToleranceDeg)
                continue;

            // Capped like every other navdata pavement width (fs2024 has path segments into hold
            // nodes over 1,000 ft wide, which would make a corridor over 150 m).
            double corridor = Math.Max(PathCorridorMinHalfWidthM,
                Math.Min(seg.WidthFeet, PavementTolerance.WidthCapFeet) * MetresPerFoot / 2.0 + PathCorridorMarginM);

            // Aircraft relative to the far end, projected onto the segment.
            double rx = ax - fx, ry = ay - fy;
            double along = rx * dx + ry * dy;           // metres from the far end toward the node
            double cross = Math.Abs(rx * dy - ry * dx);  // metres off the segment's line
            if (along < -PathAlongTrackSlackM || along >= len) continue;
            if (cross > corridor) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// How far ahead <see cref="IsApproachingWhileTurning"/> looks along the turn being flown.
    /// At 10 kt that is 15 m of travel — about the distance a hold line close to its junction
    /// was being missed by.
    /// </summary>
    public const double TurnLookaheadSeconds = 3.0;

    /// <summary>
    /// The yaw rate below which the aircraft is holding a line, not turning: the rate at which
    /// taxi guidance's own "Straighten." episode opens. A pilot correcting on a centreline
    /// yaws at a few degrees a second, and a prediction built on that would swing the nose
    /// toward every hold line beside the taxiway.
    /// </summary>
    public const double TurnMinRateDegSec = 4.0;

    /// <summary>The most turn a prediction may assume — a quarter turn, one junction's worth.</summary>
    public const double MaxPredictedTurnDeg = 90.0;

    /// <summary>
    /// Where an aircraft will be, and which way it will point, <paramref name="seconds"/> ahead
    /// if it keeps its ground speed and yaw rate: along the arc it is flying (a straight line
    /// with no yaw). The turn is capped at <see cref="MaxPredictedTurnDeg"/>; the distance
    /// travelled is not. Yaw rate is right-positive, as taxi guidance's own filter keeps it.
    /// </summary>
    public static (double Latitude, double Longitude, double HeadingTrueDeg) PredictPose(
        double lat, double lon, double headingTrueDeg, double groundSpeedMps, double yawRateDegSec, double seconds)
    {
        const double MetresPerDegLat = 111132.0;
        double turnDeg = Math.Clamp(yawRateDegSec * seconds, -MaxPredictedTurnDeg, MaxPredictedTurnDeg);
        double arc = Math.Max(0.0, groundSpeedMps) * seconds;
        double turnRad = turnDeg * Math.PI / 180.0;
        // The chord of the arc: 2R·sin(Δ/2) with R = arc/Δ, leaving at the mean heading.
        double chord = Math.Abs(turnRad) < 1e-9 ? arc : 2.0 * arc * Math.Sin(turnRad / 2.0) / turnRad;
        double chordDirRad = (headingTrueDeg + turnDeg / 2.0) * Math.PI / 180.0;
        double east = chord * Math.Sin(chordDirRad), north = chord * Math.Cos(chordDirRad);
        double metresPerDegLon = MetresPerDegLat * Math.Cos(lat * Math.PI / 180.0);
        double heading = ((headingTrueDeg + turnDeg) % 360.0 + 360.0) % 360.0;
        return (lat + north / MetresPerDegLat, lon + east / metresPerDegLon, heading);
    }

    /// <summary>
    /// The third "approached" test, for the turn both others see late. A hold line close to its
    /// junction is off the heading line and off the connector's heading until the turn onto the
    /// connector is nearly done — the residual <see cref="IsApproachingAlongAPath"/> documents —
    /// but a turn has a yaw rate, and one frame's heading does not. So an aircraft genuinely
    /// turning (at least <see cref="TurnMinRateDegSec"/>) is judged as well at the pose it will
    /// have <see cref="TurnLookaheadSeconds"/> ahead (<see cref="PredictPose"/>), by both of the
    /// other tests — and only when that pose is NEARER the node than the aircraft is now. With
    /// no yaw the prediction is the aircraft's own straight line, so taxiing past a hold line —
    /// KMEM's M8, 35 m abeam of M — gains nothing a straight line would not already have shown.
    ///
    /// <para>Measured on fs2024 (every taxiway-side path up to 150 m into a hold node, driven at
    /// 10 kt with ~25 m fillets, 30 Hz samples and taxi guidance's own yaw filter): approaches
    /// first warned inside 20 m fall from 11,408 to 5,491 of 512,210 (inside 10 m from 39 to 8).
    /// The cost is false warnings on passes through junctions near a hold line that do not
    /// lead to it: 3,556 more on top of the 37,237 the other two tests already give, out of
    /// 266,003 such passes — mostly on turns of 60° or more. Judging the predicted pose by the
    /// path test alone halves that cost (2,241) but leaves 7,790 approaches warned inside
    /// 20 m.</para>
    /// </summary>
    public static bool IsApproachingWhileTurning(
        double aircraftLat, double aircraftLon, double headingTrueDeg,
        double groundSpeedMps, double yawRateDegSec,
        double nodeLat, double nodeLon, IReadOnlyList<PathSegment> pathSegments)
    {
        if (!(Math.Abs(yawRateDegSec) >= TurnMinRateDegSec)) return false;
        var (pLat, pLon, pHdg) = PredictPose(
            aircraftLat, aircraftLon, headingTrueDeg, groundSpeedMps, yawRateDegSec, TurnLookaheadSeconds);

        const double MetresPerDegLat = 111132.0;
        double metresPerDegLon = MetresPerDegLat * Math.Cos(nodeLat * Math.PI / 180.0);
        double east = (nodeLon - pLon) * metresPerDegLon, north = (nodeLat - pLat) * MetresPerDegLat;
        double distance = Math.Sqrt(east * east + north * north);
        // A turn that ends farther from the hold line is taking the aircraft somewhere else —
        // past the connector's junction and on beside it, say — however well the pose it ends
        // in lines up with the connector (fs2024, turning motion model: 279 fewer false
        // warnings on passes near hold lines, no approach warned later).
        double nowEast = (nodeLon - aircraftLon) * metresPerDegLon, nowNorth = (nodeLat - aircraftLat) * MetresPerDegLat;
        if (!(distance < Math.Sqrt(nowEast * nowEast + nowNorth * nowNorth))) return false;
        double bearing = Math.Atan2(east, north) * 180.0 / Math.PI;
        return IsApproaching(distance, bearing, pHdg)
            || IsApproachingAlongAPath(pLat, pLon, pHdg, nodeLat, nodeLon, pathSegments);
    }

    /// <summary>A hold-short node within the guard's radius, with what the guard knows about it.</summary>
    /// <param name="NodeId">The graph node.</param>
    /// <param name="DistanceM">Range from the aircraft.</param>
    /// <param name="OnRoute">On the remaining planned route — a planned crossing, spoken
    /// as "Crossing runway X." whatever the geometry says.</param>
    /// <param name="Approached">Off the route and ahead of the aircraft by either approach test.</param>
    /// <param name="SpokenName">What the guard calls it ("runway 36L at M8"); carried so the
    /// caller need not look the node up again.</param>
    public readonly record struct HoldShortCandidate(
        int NodeId, double DistanceM, bool OnRoute, bool Approached, string SpokenName = "runway");

    /// <summary>
    /// Which in-range hold-short node the guard speaks about: the NEAREST that is either on the
    /// route or approached. Never simply the nearest node — an abeam node a few metres closer
    /// than the one dead ahead would otherwise shadow it, and the warning would wait until the
    /// aircraft was closer to the real hazard than to the bystander.
    /// </summary>
    public static HoldShortCandidate? Pick(IReadOnlyList<HoldShortCandidate> candidates)
    {
        HoldShortCandidate? best = null;
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (!c.OnRoute && !c.Approached) continue;
            if (best == null || c.DistanceM < best.Value.DistanceM) best = c;
        }
        return best;
    }

    /// <summary>
    /// The runway a hold-short node guards: the one whose centreline is nearest, within
    /// <see cref="TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M"/>, skipping an unnamed one — the rule
    /// <see cref="TaxiGraph.MatchHoldShortRunwayName"/> names the node by, so the runway this
    /// guard judges "leaving" against is always the runway the warning would name. Null when
    /// none is near enough (the node was named by the threshold fallback, or not at all).
    /// Static per node: callers cache it with the graph.
    /// </summary>
    public static RunwayShape? GuardedRunway(double nodeLat, double nodeLon, IReadOnlyList<RunwayShape> shapes)
    {
        RunwayShape? best = null;
        double bestPerp = double.MaxValue;
        for (int i = 0; i < shapes.Count; i++)
        {
            var shape = shapes[i];
            var cl = shape.Centerline;
            double perp = TaxiGraph.PerpendicularDistanceMetersStatic(
                nodeLat, nodeLon, cl.Lat1, cl.Lon1, cl.Lat2, cl.Lon2);
            if (perp > TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M || perp >= bestPerp) continue;
            if (string.IsNullOrEmpty(shape.NameAt(shape.Project(nodeLat, nodeLon).Along))) continue;
            best = shape;
            bestPerp = perp;
        }
        return best;
    }

    /// <summary>
    /// Whether an aircraft is LEAVING the runway a hold-short node guards, through that hold
    /// line — which is not approaching it, so the off-route warning stays silent for that node.
    /// True only when the aircraft is on <paramref name="guardedRunway"/>'s pavement AND deeper
    /// into it than the node is (the runway side of the hold line).
    ///
    /// <para>Both halves were measured wrong in their first form ("on ANY runway's pavement",
    /// PR #255 review, fs2024 navdata). Any runway: an aircraft rolling along one runway passes
    /// within the guard's 40 m of ANOTHER runway's hold line at 420 airports (704 pairs, 90 of
    /// them dead ahead — EGER on 24/06 toward runway 28's line) and was told nothing. Position
    /// alone: 3,020 of 80,864 hold-short nodes are drawn ON runway pavement, so an aircraft
    /// coming from the taxiway reaches the pavement before the hold line, and 35 approach paths
    /// were never warned at all (EHAM 18C at W5). With this rule 89 of those 90 dead-ahead
    /// pairs warn, and 17 approach paths stay silent — every one examined already on the runway
    /// before the hold line (CYTZ 26 at C: the only hold node is drawn on the FAR side of the
    /// runway it crosses; 2TX6 08 at A: the taxi path runs along the runway centreline), where
    /// the proximity rule too spoke only once the aircraft was on the pavement.</para>
    /// </summary>
    public static bool IsLeavingThroughHoldLine(
        double aircraftLat, double aircraftLon, double nodeLat, double nodeLon, RunwayShape? guardedRunway)
    {
        if (guardedRunway == null || !guardedRunway.Contains(aircraftLat, aircraftLon, 0.0)) return false;
        return MetresOutsidePavement(guardedRunway, aircraftLat, aircraftLon)
             < MetresOutsidePavement(guardedRunway, nodeLat, nodeLon);
    }

    /// <summary>
    /// Signed distance from a runway's pavement edge: positive outside it (to the nearest point
    /// of the pavement rectangle), negative inside (minus the depth to the nearest edge).
    /// </summary>
    private static double MetresOutsidePavement(RunwayShape shape, double lat, double lon)
    {
        var (along, lateral) = shape.Project(lat, lon);
        double outLateral = Math.Abs(lateral) - shape.HalfWidthMeters;
        double outAlong = Math.Max(shape.ExtentMinMeters - along, along - shape.ExtentMaxMeters);
        if (outLateral <= 0 && outAlong <= 0) return Math.Max(outLateral, outAlong);
        double ox = Math.Max(outLateral, 0), oy = Math.Max(outAlong, 0);
        return Math.Sqrt(ox * ox + oy * oy);
    }

    /// <summary>Signed difference bearing − heading, in (−180, 180].</summary>
    private static double RelativeBearingDeg(double bearingDeg, double headingDeg)
    {
        double rel = ((bearingDeg - headingDeg) % 360.0 + 540.0) % 360.0 - 180.0;
        return rel <= -180.0 ? rel + 360.0 : rel;
    }
}
