using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>How another aircraft is moving relative to the own aircraft's heading.</summary>
public enum TrafficMotion
{
    Stopped,
    SameDirection,
    HeadOn,          // opposite direction AND ahead of us: coming toward us
    OppositeDirection,
    CrossingLeftToRight,
    CrossingRightToLeft,
}

/// <summary>Where an aircraft is relative to one runway.</summary>
internal enum RunwayTrafficKind { None, OnRunway, OnFinal }

internal readonly record struct RunwayTrafficFix(
    RunwayTrafficKind Kind,
    string Designator,      // for OnFinal: the runway end it is landing on
    double DistanceNm);     // for OnFinal: distance to that threshold

/// <summary>A point on the route ahead, with its cumulative route distance.</summary>
public readonly record struct GroundTrafficRoutePoint(double Lat, double Lon, string Taxiway, double RouteMetres);

/// <summary>Where a traffic aircraft sits relative to the route ahead.</summary>
internal readonly record struct RouteProjection(double LateralMetres, double RouteMetres, string Taxiway);

/// <summary>
/// Pure geometry and phrasing for <see cref="GroundTrafficMonitor"/>: closest point of approach,
/// relative motion, runway occupancy/final classification, route projection, spoken names.
/// No simulator state — everything is pinned by <c>GroundTrafficLogicTests</c>.
/// </summary>
internal static class GroundTrafficLogic
{
    private const double MetresPerDegLat = 110_540.0;
    private const double MetresPerDegLonEquator = 111_320.0;
    private const double KtsToMps = 0.514444;
    public const double MetresPerNm = 1852.0;

    // ── Departure queue: has the aircraft we pulled up behind left? ──────────
    /// <summary>At or below this ground speed an aircraft ahead counts as stopped in the queue.</summary>
    public const double QueueStoppedGs = 1.5;
    /// <summary>Speed-edge trigger: rolling this fast in the sample after a stopped one.</summary>
    public const double QueueMovingGs = 2.0;
    /// <summary>Gap trigger: the target must be moving at least this fast to have opened the gap itself.</summary>
    public const double QueueCreepGs = 1.0;
    /// <summary>Gap trigger: growth over the stopped-gap baseline that counts as departed.</summary>
    public const double QueueGapOpenedFt = 60.0;

    /// <summary>
    /// Has the aircraft ahead pulled away? TWO triggers, because one is not enough at a real
    /// holding point.
    ///
    /// The SPEED EDGE (stopped in the previous sample, rolling in this one) is the fast one, but a
    /// queue CREEPS: an aircraft easing forward to 2 kt and holding there shows one edge at most,
    /// and once <paramref name="previousGs"/> is above the stopped gate the edge can never fire
    /// again — the pilot then sits behind an opening gap in silence. The GAP trigger catches it:
    /// the distance has grown by <see cref="QueueGapOpenedFt"/> over the closest we were while it
    /// sat stopped ahead of us (<paramref name="stoppedGapFt"/>, NaN until it has been seen
    /// stopped). The target's OWN motion is required there — our own pushback opens the gap just
    /// as well, and must not be reported as the queue moving.
    /// </summary>
    /// <summary>
    /// Largest gap between two aircraft that still counts as ONE departure queue, and the
    /// largest gap between the pilot and the queue's tail that still counts as joining it.
    /// <para>
    /// A queue is a contiguous line. Aircraft hold roughly 50-100 m apart, so 250 m is well
    /// clear of normal spacing while being far smaller than the distance between two
    /// SEPARATE queues — the line at an intermediate holding point and the line at the
    /// runway hold beyond it.
    /// </para>
    /// </summary>
    public const double QueueLinkMaxGapM = 250.0;

    /// <summary>
    /// How many aircraft are in the departure queue the pilot is actually in, given every
    /// slow aircraft on the route ahead, as along-route distances in metres.
    /// <para>
    /// Counting everything on the route ahead — which is what the scan window alone does —
    /// MERGES separate queues. At a busy field the 1,500 m of route in front of a stopped
    /// aircraft can hold the line it has just joined at a holding point AND a second line
    /// at the runway hold several hundred metres further on, so the pilot is told they are
    /// seventh when they are third in the queue they are in and the other four are a
    /// different queue entirely.
    /// </para>
    /// <para>
    /// So walk outward from the pilot and stop at the first gap wider than
    /// <see cref="QueueLinkMaxGapM"/>. That yields the queue being joined WHEREVER it is —
    /// which is the point: a queue at a holding point well back from the runway is still
    /// the pilot's queue, and may well be the longer of the two.
    /// </para>
    /// </summary>
    /// <param name="aheadMetres">Along-route distance to each qualifying aircraft ahead.
    /// Order does not matter; the method sorts.</param>
    public static int QueueAheadCount(IEnumerable<double> aheadMetres)
        => QueueAheadOf(aheadMetres).Count;

    /// <summary>The pilot’s own queue, and whether anything is holding beyond it.</summary>
    /// <param name="Count">Aircraft in the contiguous line the pilot is in.</param>
    /// <param name="MoreBeyond">At least one more aircraft sits past the gap that ended
    /// that line — a separate group further along the route.</param>
    public readonly record struct QueueCluster(int Count, bool MoreBeyond);

    /// <summary>
    /// As <see cref="QueueAheadCount"/>, and also reports whether the gap that ended the
    /// pilot’s queue had anything beyond it. Knowing you are third in YOUR line is only
    /// half the picture when another line is holding between you and the runway.
    /// </summary>
    public static QueueCluster QueueAheadOf(IEnumerable<double> aheadMetres)
    {
        var sorted = aheadMetres.Where(d => d >= 0).OrderBy(d => d).ToList();
        int count = 0;
        double previous = 0.0;          // the pilot
        foreach (double d in sorted)
        {
            if (d - previous > QueueLinkMaxGapM) break;
            count++;
            previous = d;
        }
        return new QueueCluster(count, sorted.Count > count);
    }

    public static bool QueueDeparted(double previousGs, double gs, double distFt, double stoppedGapFt)
    {
        bool speedEdge = previousGs <= QueueStoppedGs && gs >= QueueMovingGs;
        bool gapOpened = gs >= QueueCreepGs
                         && !double.IsNaN(stoppedGapFt)
                         && distFt - stoppedGapFt >= QueueGapOpenedFt;
        return speedEdge || gapOpened;
    }

    // Motion classification bands (degrees between the two headings)
    private const double SameDirectionDeg = 35.0;
    private const double OppositeDirectionDeg = 145.0;
    private const double StoppedKts = 2.0;

    // Final-approach classification
    public const double FinalMaxNm = 6.0;
    private const double FinalHeadingToleranceDeg = 30.0;
    private const double FinalLateralBaseM = 300.0;     // cone half-width at the threshold
    private const double FinalLateralSlope = 0.12;       // + per metre out (≈ 7°)
    private const double FinalMaxHeightPerNmFt = 500.0;  // well above a 3° path (318 ft/nm)
    private const double FinalMaxHeightBaseFt = 800.0;

    /// <summary>Flat-earth metres of (lat, lon) from the reference point: x east, y north.</summary>
    public static (double X, double Y) ToLocal(double refLat, double refLon, double lat, double lon)
    {
        double x = (lon - refLon) * MetresPerDegLonEquator * Math.Cos(refLat * Math.PI / 180.0);
        double y = (lat - refLat) * MetresPerDegLat;
        return (x, y);
    }

    /// <summary>Velocity in metres per second (x east, y north) from a true heading and ground speed.</summary>
    public static (double Vx, double Vy) Velocity(double headingTrueDeg, double groundSpeedKts)
    {
        double r = headingTrueDeg * Math.PI / 180.0;
        double v = Math.Max(0.0, groundSpeedKts) * KtsToMps;
        return (v * Math.Sin(r), v * Math.Cos(r));
    }

    /// <summary>
    /// Closest point of approach of two straight-line tracks. Inputs are the TRAFFIC's position and
    /// velocity RELATIVE to the own aircraft. Returns the time until closest approach (0 when the
    /// two are already opening or not moving relative to each other) and the distance then.
    /// </summary>
    public static (double TimeSec, double DistanceM) ClosestApproach(double rx, double ry, double rvx, double rvy)
    {
        double v2 = rvx * rvx + rvy * rvy;
        double t = v2 < 1e-6 ? 0.0 : Math.Max(0.0, -(rx * rvx + ry * rvy) / v2);
        double cx = rx + rvx * t, cy = ry + rvy * t;
        return (t, Math.Sqrt(cx * cx + cy * cy));
    }

    /// <summary>Signed smallest angle a − b, in (−180, 180].</summary>
    public static double AngleDiff(double a, double b)
    {
        double d = ((a - b) % 360.0 + 540.0) % 360.0 - 180.0;
        return d == -180.0 ? 180.0 : d;
    }

    /// <summary>
    /// How the traffic is moving relative to us. <paramref name="relBearingDeg"/> is where it is
    /// (0 = dead ahead, clockwise) — it separates "coming toward us" from merely opposite.
    /// </summary>
    public static TrafficMotion ClassifyMotion(double ownHeadingTrue, double trafficHeadingTrue,
                                               double trafficGsKts, double relBearingDeg)
    {
        if (trafficGsKts < StoppedKts) return TrafficMotion.Stopped;
        double d = AngleDiff(trafficHeadingTrue, ownHeadingTrue);
        double abs = Math.Abs(d);
        if (abs <= SameDirectionDeg) return TrafficMotion.SameDirection;
        if (abs >= OppositeDirectionDeg)
        {
            double relAbs = Math.Abs(AngleDiff(relBearingDeg, 0.0));
            return relAbs <= 60.0 ? TrafficMotion.HeadOn : TrafficMotion.OppositeDirection;
        }
        // Heading to our right of our own direction = moving from our left toward our right.
        return d > 0 ? TrafficMotion.CrossingLeftToRight : TrafficMotion.CrossingRightToLeft;
    }

    public static string DescribeMotion(TrafficMotion m) => m switch
    {
        TrafficMotion.Stopped => "stopped",
        TrafficMotion.SameDirection => "same direction",
        TrafficMotion.HeadOn => "head-on",
        TrafficMotion.OppositeDirection => "opposite direction",
        TrafficMotion.CrossingLeftToRight => "crossing left to right",
        TrafficMotion.CrossingRightToLeft => "crossing right to left",
        _ => "",
    };

    /// <summary>Plain-language relative direction: 0 = dead ahead, clockwise.</summary>
    public static string DescribeDirection(double relBearing)
    {
        relBearing = ((relBearing % 360.0) + 360.0) % 360.0;
        bool right = relBearing < 180.0;
        double abs = right ? relBearing : (360.0 - relBearing);

        if (abs <= 20.0) return "ahead";
        if (abs <= 70.0) return right ? "ahead and to the right" : "ahead and to the left";
        if (abs <= 110.0) return right ? "to the right" : "to the left";
        if (abs <= 160.0) return right ? "behind and to the right" : "behind and to the left";
        return "behind";
    }

    /// <summary>"from the left" / "from the right" / "from ahead" / "from behind".</summary>
    public static string DescribeSide(double relBearing)
    {
        double d = AngleDiff(relBearing, 0.0);
        double abs = Math.Abs(d);
        if (abs <= 25.0) return "from ahead";
        if (abs >= 155.0) return "from behind";
        return d > 0 ? "from the right" : "from the left";
    }

    // ── Runway occupancy / final ─────────────────────────────────────────────

    /// <summary>
    /// Classifies one aircraft against one runway: on the pavement, on final to either end
    /// (airborne, inside the approach cone, pointing at the runway, not too high), or neither.
    /// <paramref name="heightAboveFieldFt"/> is the traffic altitude minus the field elevation.
    /// </summary>
    public static RunwayTrafficFix ClassifyAgainstRunway(
        RunwayShape shape, double lat, double lon, bool onGround,
        double headingTrue, double heightAboveFieldFt)
    {
        var none = new RunwayTrafficFix(RunwayTrafficKind.None, "", 0);
        if (shape.IsDegenerate) return none;

        var (along, lateral) = shape.Project(lat, lon);
        if (onGround)
        {
            return shape.ContainsAlongLateral(along, lateral, 0.0)
                ? new RunwayTrafficFix(RunwayTrafficKind.OnRunway, "", 0)
                : none;
        }

        double axisHdg = NavigationCalculator.CalculateBearing(shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2);

        // Short of end 1, flying toward end 2 → landing on Name1.
        if (along < shape.ExtentMinMeters)
        {
            double outM = shape.ExtentMinMeters - along;
            if (IsOnFinal(outM, lateral, headingTrue, axisHdg, heightAboveFieldFt))
                return new RunwayTrafficFix(RunwayTrafficKind.OnFinal, shape.Name1, outM / MetresPerNm);
        }
        // Beyond end 2, flying toward end 1 → landing on Name2.
        else if (along > shape.ExtentMaxMeters)
        {
            double outM = along - shape.ExtentMaxMeters;
            if (IsOnFinal(outM, lateral, headingTrue, (axisHdg + 180.0) % 360.0, heightAboveFieldFt))
                return new RunwayTrafficFix(RunwayTrafficKind.OnFinal, shape.Name2, outM / MetresPerNm);
        }
        return none;
    }

    private static bool IsOnFinal(double outM, double lateral, double headingTrue, double landingHdg,
                                  double heightFt)
    {
        double nm = outM / MetresPerNm;
        if (nm > FinalMaxNm) return false;
        if (Math.Abs(lateral) > FinalLateralBaseM + FinalLateralSlope * outM) return false;
        if (Math.Abs(AngleDiff(headingTrue, landingHdg)) > FinalHeadingToleranceDeg) return false;
        if (heightFt > FinalMaxHeightBaseFt + FinalMaxHeightPerNmFt * nm) return false;
        return true;
    }

    // ── Route projection ─────────────────────────────────────────────────────

    /// <summary>
    /// Nearest point of the route polyline to (lat, lon): lateral distance, route distance there
    /// (from the polyline's first point), and the taxiway of that stretch. Null when the route has
    /// fewer than two points.
    /// </summary>
    public static RouteProjection? ProjectOntoRoute(IReadOnlyList<GroundTrafficRoutePoint> route,
                                                    double lat, double lon)
    {
        if (route.Count < 2) return null;
        double refLat = route[0].Lat, refLon = route[0].Lon;
        var (px, py) = ToLocal(refLat, refLon, lat, lon);

        RouteProjection? best = null;
        double bestD = double.MaxValue;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            var (ax, ay) = ToLocal(refLat, refLon, route[i].Lat, route[i].Lon);
            var (bx, by) = ToLocal(refLat, refLon, route[i + 1].Lat, route[i + 1].Lon);
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 < 1e-6 ? 0.0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0.0, 1.0);
            double cx = ax + dx * t, cy = ay + dy * t;
            double d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            if (d < bestD)
            {
                bestD = d;
                double segLen = route[i + 1].RouteMetres - route[i].RouteMetres;
                best = new RouteProjection(d, route[i].RouteMetres + segLen * t, route[i + 1].Taxiway);
            }
        }
        return best;
    }

    // ── Spoken names ─────────────────────────────────────────────────────────

    private static readonly Regex RxCallsign = new(@"^([A-Z]{2,4})(\d{1,5}[A-Z]{0,2})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> SpokenTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A19N"] = "A319", ["A20N"] = "A320", ["A21N"] = "A321",
        ["A332"] = "A330", ["A333"] = "A330", ["A338"] = "A330", ["A339"] = "A330",
        ["A342"] = "A340", ["A343"] = "A340", ["A345"] = "A340", ["A346"] = "A340",
        ["A359"] = "A350", ["A35K"] = "A350", ["A388"] = "A380",
        ["BCS1"] = "A220", ["BCS3"] = "A220",
        ["B37M"] = "737 MAX", ["B38M"] = "737 MAX", ["B39M"] = "737 MAX", ["B3XM"] = "737 MAX",
        ["B736"] = "737", ["B737"] = "737", ["B738"] = "737", ["B739"] = "737",
        ["B744"] = "747", ["B748"] = "747", ["B74F"] = "747",
        ["B752"] = "757", ["B753"] = "757",
        ["B762"] = "767", ["B763"] = "767", ["B764"] = "767",
        ["B772"] = "777", ["B773"] = "777", ["B77L"] = "777", ["B77W"] = "777", ["B778"] = "777", ["B779"] = "777",
        ["B788"] = "787", ["B789"] = "787", ["B78X"] = "787",
        ["MD11"] = "MD-11", ["CRJ7"] = "CRJ", ["CRJ9"] = "CRJ", ["CRJX"] = "CRJ",
        ["E170"] = "Embraer 170", ["E75L"] = "Embraer 175", ["E190"] = "Embraer 190", ["E195"] = "Embraer 195",
        ["AT72"] = "ATR 72", ["AT76"] = "ATR 72", ["DH8D"] = "Dash 8", ["C172"] = "Cessna 172",
    };

    /// <summary>An aircraft type the way a pilot says it ("B77W" → "777").</summary>
    public static string SpokenType(string? rawType)
    {
        string icao = Forms.TcasForm.ShortenAircraftType(rawType ?? "");
        if (string.IsNullOrEmpty(icao)) return "";
        if (SpokenTypes.TryGetValue(icao, out var spoken)) return spoken;
        // A320 / A321 / B747 style: already speakable; drop the Boeing B.
        if (Regex.IsMatch(icao, @"^B7\d7$", RegexOptions.CultureInvariant)) return icao[1..];
        return icao;
    }

    /// <summary>A callsign spaced for speech ("DAL123" → "DAL 123").</summary>
    public static string SpokenCallsign(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();
        var m = RxCallsign.Match(raw.ToUpperInvariant());
        return m.Success ? $"{m.Groups[1].Value} {m.Groups[2].Value}" : raw;
    }

    /// <summary>
    /// How to name an aircraft in a callout, the way ATC would: "Delta A320" when the airline is
    /// known, else "DAL 123, A320", else the type alone, else "traffic".
    /// </summary>
    public static string SpokenName(string? airline, string? callsign, string? rawType)
    {
        string type = SpokenType(rawType);
        string air = (airline ?? "").Trim();
        if (air.Length > 0)
            return type.Length > 0 ? $"{air} {type}" : $"{air} traffic";
        string cs = SpokenCallsign(callsign);
        if (cs.Length > 0)
            return type.Length > 0 ? $"{cs}, {type}" : cs;
        return type.Length > 0 ? type : "traffic";
    }

    /// <summary>A runway status sentence opening, "Runway 27" — designators already bare.</summary>
    public static string RunwayLabel(IEnumerable<string> designators)
        => "Runway " + string.Join(" and ", designators);
}
