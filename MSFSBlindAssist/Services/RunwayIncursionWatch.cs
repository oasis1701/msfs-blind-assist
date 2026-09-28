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
    /// <para>Accepted residual: a 90° turn onto a SHORT straight connector drawn as one segment
    /// (hold line within about 20 m of the junction, no fillet bends) is seen only once the nose
    /// is within 20° of the connector, a few metres before the hold line. Earlier than that the
    /// aircraft is indistinguishable, in one frame, from one taxiing straight past a rapid-exit
    /// junction; telling them apart needs heading history, which this pure test does not have.
    /// The proximity rule warned from 40 m there, and at every abeam node everywhere.</para>
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

            double corridor = Math.Max(PathCorridorMinHalfWidthM,
                seg.WidthFeet * MetresPerFoot / 2.0 + PathCorridorMarginM);

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

    /// <summary>Signed difference bearing − heading, in (−180, 180].</summary>
    private static double RelativeBearingDeg(double bearingDeg, double headingDeg)
    {
        double rel = ((bearingDeg - headingDeg) % 360.0 + 540.0) % 360.0 - 180.0;
        return rel <= -180.0 ? rel + 360.0 : rel;
    }
}
