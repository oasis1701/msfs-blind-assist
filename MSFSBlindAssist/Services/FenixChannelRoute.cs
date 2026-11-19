using System.Globalization;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>A latitude and longitude in degrees, as the channel sends them.</summary>
internal readonly record struct FenixLatLon(double Latitude, double Longitude);

/// <summary>
/// A hold on a route element: the inbound course (14: true degrees on the entry arc, magnetic after it, as sent) and
/// the side of the turns. No leg length or time was seen.
/// </summary>
internal sealed record FenixRouteHold(double InboundCourse, bool RightTurns)
{
    /// <summary>The hold kind (12); 4 seen; 0 when absent.</summary>
    public int Kind { get; init; }
}

/// <summary>
/// One element of a route list. <see cref="Kind"/> is a <see cref="RouteLegKind"/> constant (0 when absent): kinds 0
/// to 3 are vertices of the drawn line and 4 is a waypoint symbol. The other members read as the channel leaves them:
/// null for a turn member that was not sent.
/// </summary>
internal readonly record struct FenixRouteLeg(FenixLatLon Point, int Kind, string Ident)
{
    /// <summary>The element is on the missed approach (4).</summary>
    public bool MissedApproach { get; init; }

    /// <summary>The waypoint symbol (7), on a kind 4 element; 0 when absent.</summary>
    public int Symbol { get; init; }

    /// <summary>The side of a turn (8), "L" or "R"; null when absent.</summary>
    public string? TurnSide { get; init; }

    /// <summary>The turn radius (9), in nautical miles (inferred); null when absent.</summary>
    public double? TurnRadius { get; init; }

    /// <summary>The course in (10), true degrees; null when absent.</summary>
    public double? CourseIn { get; init; }

    /// <summary>The course out (11), true degrees; null when absent.</summary>
    public double? CourseOut { get; init; }

    /// <summary>The hold, set when the element sent any of 12, 13 or 14; null otherwise.</summary>
    public FenixRouteHold? Hold { get; init; }

    /// <summary>Why a kind 3 element ends the line (16), a <see cref="RouteEndReason"/>; 0 when absent.</summary>
    public int EndReason { get; init; }

    /// <summary>The element is the destination runway's point, set by the decoder (see <see cref="FenixChannelRoute.IsDestination"/>).</summary>
    public bool Destination { get; init; }

    /// <summary>The element's symbol, label and constraint are removed when the ND declutters (5).</summary>
    public bool RemoveOnDeclutter { get; init; }
}

/// <summary>
/// A FIX INFO entry (2.13): its fix (a point in the shape of a GetMapData point: kind, position, ident), the variation
/// at the fix, the radials (magnetic degrees, as entered) and the ring radii (nautical miles).
/// </summary>
internal sealed record FenixFixInfo(
    string Ident, int Kind, FenixLatLon Point, double MagneticVariation, IReadOnlyList<double> Radials,
    IReadOnlyList<double> Rings);

/// <summary>A map marker (2.12): a constraint, a top of descent or another marker the ND draws at a point.</summary>
internal readonly record struct FenixRouteMarker(FenixLatLon Point, int Type, bool IsGoAround)
{
    /// <summary>The label as the ND prints it, "\n" between two printed lines; "" when absent.</summary>
    public string Label { get; init; } = "";

    /// <summary>The color (FMCColorEnum); null when the marker has none.</summary>
    public int? Color { get; init; }

    /// <summary>The marker is removed when the ND declutters.</summary>
    public bool RemoveOnDeclutter { get; init; }
}

/// <summary>A runway of the route (2.1): the departure's first, the arrival's second.</summary>
internal sealed record FenixRouteRunway(string Airport, string Runway, FenixLatLon Threshold)
{
    /// <summary>The true course (3); null when absent.</summary>
    public double? TrueCourse { get; init; }

    /// <summary>The length in feet (5); null when absent.</summary>
    public int? LengthFt { get; init; }
}

/// <summary>
/// The captain's route and the aircraft's position, read from the RemoteData message: the active route (2.4), the
/// active leg (2.11), the markers (2.12), the present position (192) and what the ND needs to draw them.
/// </summary>
internal sealed record FenixRoute(
    FenixLatLon Position, IReadOnlyList<FenixRouteLeg> Legs, FenixRouteLeg? ActiveLeg,
    IReadOnlyList<FenixRouteMarker> Markers)
{
    /// <summary>The temporary flight plan (2.5); empty when none is pending.</summary>
    public IReadOnlyList<FenixRouteLeg> TemporaryLegs { get; init; } = [];

    /// <summary>The alternate (2.7).</summary>
    public IReadOnlyList<FenixRouteLeg> AlternateLegs { get; init; } = [];

    /// <summary>The temporary alternate (2.8).</summary>
    public IReadOnlyList<FenixRouteLeg> TemporaryAlternateLegs { get; init; } = [];

    /// <summary>The departure and arrival runways (2.1).</summary>
    public IReadOnlyList<FenixRouteRunway> Runways { get; init; } = [];

    /// <summary>The position the ND draws (56); null when not sent.</summary>
    public FenixLatLon? NdPosition { get; init; }

    /// <summary>The PLAN centre (84); null when not sent.</summary>
    public FenixLatLon? PlanCenter { get; init; }

    /// <summary>
    /// The ND draws the missed approach (44.1 = 1, <see cref="RouteOptionsData.GoAroundVisible"/>): its elements (member 4)
    /// are sent all flight and drawn only while this holds. False with 44 empty or not sent.
    /// </summary>
    public bool MissedApproachDrawn { get; init; }

    /// <summary>Why field 44 could not be read ("field 44.1 has the wrong shape"); null when it was. Never fails the route.</summary>
    public string? MissedApproachProblem { get; init; }

    /// <summary>The FIX INFO entries that hold a fix (2.13); an empty entry is none.</summary>
    public IReadOnlyList<FenixFixInfo> FixInfos { get; init; } = [];
}

/// <summary>
/// Reads the captain's route (field 2) and the present position (192), with the nearby positions the ND uses (56, 84).
/// Never the first officer's (3, 57, 85).
/// </summary>
internal static class FenixChannelRoute
{
    /// <summary>
    /// Reads the route from the RemoteData message <paramref name="push"/>. Every member read is checked for its wire
    /// shape and every text for valid UTF-8; the first one in another shape gives a section that names its path
    /// ("2.4.3"), before any missing content. A member that was left out reads its default; a route with no legs
    /// decodes. Members that are not listed are skipped unread.
    /// </summary>
    public static FenixChannelSection<FenixRoute> Decode(FenixChannelPush push)
    {
        var walk = new Walk();

        FenixChannelPush? route = walk.Part(push, Remote.RouteDataCapt, "");
        FenixChannelPush? position = walk.Part(push, Remote.TruePos, "");
        FenixChannelPush? ndPosition = walk.Part(push, Remote.NdPosCapt, "");
        FenixChannelPush? planCenter = walk.Part(push, Remote.CduCenterCapt, "");

        // 44 is only the missed approach's visibility flag: it is read on a walk of its own, so a wrong shape there (44 or
        // 44.1) never fails the route; the flag then reads as absent and the problem is carried for the route layer.
        var optionsWalk = new Walk();
        FenixChannelPush? options = optionsWalk.Part(push, Remote.RouteOptionsCapt, "");
        bool missedApproachDrawn = options is not null
            && optionsWalk.Flag(options, RouteOptionsData.GoAroundVisible, "44");
        string? missedApproachProblem = optionsWalk.Wrong is { } badOptions ? $"field {badOptions} has the wrong shape" : null;
        if (missedApproachProblem is not null) missedApproachDrawn = false;

        FenixLatLon here = walk.Point(position, "192", LatLon.Latitude, LatLon.Longitude);
        FenixLatLon? nd = ndPosition is null ? null : walk.Point(ndPosition, "56", NdPositionData.Lat, NdPositionData.Lon);
        FenixLatLon? center = planCenter is null ? null : walk.Point(planCenter, "84", LatLon.Latitude, LatLon.Longitude);

        List<FenixRouteRunway> runways = [];
        List<FenixRouteLeg> active = [], temporary = [], alternate = [], temporaryAlternate = [];
        List<FenixRouteMarker> markers = [];
        List<FenixFixInfo> fixInfos = [];
        FenixRouteLeg? activeLeg = null;
        if (route is not null)
        {
            const string at = "2";
            foreach ((FenixChannelPush entry, string path) in walk.Entries(route, RouteData.Runways, at))
            {
                runways.Add(walk.Runway(entry, path));
            }

            walk.Legs(route, RouteData.ActRoute, at, active);
            walk.Legs(route, RouteData.ModRoute, at, temporary);
            walk.Legs(route, RouteData.AltRoute, at, alternate);
            walk.Legs(route, RouteData.ModAltRoute, at, temporaryAlternate);

            FenixChannelPush? activeMessage = walk.Part(route, RouteData.ActiveLeg, at);
            if (activeMessage is not null) activeLeg = walk.Leg(activeMessage, $"{at}.{RouteData.ActiveLeg}");

            foreach ((FenixChannelPush entry, string path) in walk.Entries(route, RouteData.MapMarkers, at))
            {
                markers.Add(walk.Marker(entry, path));
            }

            // FIX INFO: an entry with no fix (the four empty entries sent with none entered) is none.
            foreach ((FenixChannelPush entry, string path) in walk.Entries(route, RouteData.FixRadials, at))
            {
                if (walk.FixInfo(entry, path) is { } fixInfo) fixInfos.Add(fixInfo);
            }
        }

        if (walk.Wrong is not null) return FenixChannelSection<FenixRoute>.WrongShape(walk.Wrong);
        if (route is null) return FenixChannelSection<FenixRoute>.Missing("route data");
        if (position is null) return FenixChannelSection<FenixRoute>.Missing("present position");

        // The destination is the arrival runway's point.
        string arrival = ArrivalIdent(runways);
        List<FenixRouteLeg> MarkDestination(List<FenixRouteLeg> legs)
            => arrival.Length == 0
                ? legs
                : legs.Select(leg => leg.Kind is RouteLegKind.Line or RouteLegKind.Arc
                    && !leg.MissedApproach && leg.Ident == arrival ? leg with { Destination = true } : leg).ToList();

        return FenixChannelSection<FenixRoute>.Ok(new FenixRoute(here, MarkDestination(active), activeLeg, markers)
        {
            TemporaryLegs = MarkDestination(temporary),
            AlternateLegs = alternate,
            TemporaryAlternateLegs = temporaryAlternate,
            Runways = runways,
            NdPosition = nd,
            PlanCenter = center,
            FixInfos = fixInfos,
            MissedApproachDrawn = missedApproachDrawn,
            MissedApproachProblem = missedApproachProblem
        });
    }

    /// <summary>
    /// The element is a vertex of the drawn line: kinds 0 (line), 1 (arc), 2 (start) and 3 (end). A symbol (4) and any
    /// kind not measured are not.
    /// </summary>
    public static bool IsPath(FenixRouteLeg leg) => leg.Kind is >= RouteLegKind.Line and <= RouteLegKind.End;

    /// <summary>
    /// The element is the destination: the decoder found it at the arrival runway's point, on the route and not on
    /// the missed approach (<see cref="FenixRouteLeg.Destination"/>).
    /// </summary>
    public static bool IsDestination(FenixRouteLeg leg) => leg.Destination;

    /// <summary>
    /// The ident the route gives the arrival runway's element: the second runway entry (2.1), airport and runway together
    /// ("KCVG18L"); "" with fewer than two entries. The one rule for the destination: the decoder marks it by this, and
    /// the ND's route layer knows an active leg on the runway by it.
    /// </summary>
    public static string ArrivalIdent(IReadOnlyList<FenixRouteRunway> runways)
        => runways.Count >= 2 ? runways[1].Airport + runways[1].Runway : "";

    /// <summary>
    /// The line is broken after this element: an end (3) because of an F-PLN discontinuity. An end for the final
    /// approach (16 = 2) does not break it: the line goes on from the same point.
    /// </summary>
    public static bool BreaksPath(FenixRouteLeg leg)
        => leg.Kind == RouteLegKind.End && leg.EndReason == RouteEndReason.Discontinuity;

    // Reads messages, remembering the path of the first field found in another wire shape.
    private sealed class Walk
    {
        public string? Wrong { get; private set; }

        private void Fail(string path) => Wrong ??= path;

        private static string PathOf(string parent, int field)
            => parent.Length == 0
                ? field.ToString(CultureInfo.InvariantCulture)
                : $"{parent}.{field.ToString(CultureInfo.InvariantCulture)}";

        // A singular nested part; null when absent or in another shape (then the path is recorded).
        public FenixChannelPush? Part(FenixChannelPush parent, int number, string parentPath)
        {
            if (!parent.TryGet(number, out FenixChannelField field)) return null;

            FenixChannelPush? message = field.AsMessage();
            if (message is null) Fail(PathOf(parentPath, number));
            return message;
        }

        // The entries of a repeated nested field, each with its path ("2.4"). An entry in another shape is recorded and
        // left out.
        public List<(FenixChannelPush Entry, string Path)> Entries(FenixChannelPush parent, int number, string parentPath)
        {
            string path = PathOf(parentPath, number);
            var entries = new List<(FenixChannelPush, string)>();
            foreach (FenixChannelField field in parent.All(number))
            {
                FenixChannelPush? message = field.AsMessage();
                if (message is null) Fail(path);
                else entries.Add((message, path));
            }

            return entries;
        }

        public void Legs(FenixChannelPush parent, int number, string parentPath, List<FenixRouteLeg> into)
        {
            foreach ((FenixChannelPush entry, string path) in Entries(parent, number, parentPath))
            {
                into.Add(Leg(entry, path));
            }
        }

        public FenixLatLon Point(FenixChannelPush? message, string path, int latitude, int longitude)
            => new(Double(message, latitude, path), Double(message, longitude, path));

        public FenixRouteLeg Leg(FenixChannelPush m, string path)
        {
            bool hasHold = m.TryGet(RouteLeg.HoldKind, out _) || m.TryGet(RouteLeg.HoldRightTurns, out _)
                || m.TryGet(RouteLeg.HoldCourse, out _);
            FenixRouteHold? hold = hasHold
                ? new FenixRouteHold(Double(m, RouteLeg.HoldCourse, path), Flag(m, RouteLeg.HoldRightTurns, path))
                {
                    Kind = Int(m, RouteLeg.HoldKind, path)
                }
                : null;

            return new FenixRouteLeg(
                Point(m, path, RouteLeg.Latitude, RouteLeg.Longitude),
                Int(m, RouteLeg.Kind, path),
                Text(m, RouteLeg.Ident, path))
            {
                MissedApproach = Flag(m, RouteLeg.MissedApproach, path),
                Symbol = Int(m, RouteLeg.Symbol, path),
                TurnSide = m.TryGet(RouteLeg.TurnSide, out _) ? Text(m, RouteLeg.TurnSide, path) : null,
                TurnRadius = OptionalSingle(m, RouteLeg.TurnRadius, path),
                CourseIn = OptionalSingle(m, RouteLeg.CourseIn, path),
                CourseOut = OptionalSingle(m, RouteLeg.CourseOut, path),
                Hold = hold,
                EndReason = Int(m, RouteLeg.EndReason, path),
                RemoveOnDeclutter = Flag(m, RouteLeg.RemoveOnDeclutter, path)
            };
        }

        // A FIX INFO entry; null when it holds no fix.
        public FenixFixInfo? FixInfo(FenixChannelPush m, string path)
        {
            FenixChannelPush? fix = Part(m, FixRadial.Fix, path);
            double variation = Double(m, FixRadial.MagVar, path);
            List<double> radials = Doubles(m, FixRadial.Radials, path);
            List<double> rings = Doubles(m, FixRadial.Radius, path);
            if (fix is null) return null;

            string at = PathOf(path, FixRadial.Fix);
            return new FenixFixInfo(Text(fix, MapPoint.Ident, at), Int(fix, MapPoint.Kind, at),
                Point(fix, at, MapPoint.Latitude, MapPoint.Longitude), variation, radials, rings);
        }

        public FenixRouteMarker Marker(FenixChannelPush m, string path)
            => new(Point(m, path, MapMarker.Lat, MapMarker.Lon), Int(m, MapMarker.MapMarkerType, path),
                Flag(m, MapMarker.IsGoAround, path))
            {
                Label = Text(m, MapMarker.Label, path),
                Color = m.TryGet(MapMarker.Color, out _) ? Int(m, MapMarker.Color, path) : null,
                RemoveOnDeclutter = Flag(m, MapMarker.RemoveOnDeclutter, path)
            };

        public FenixRouteRunway Runway(FenixChannelPush m, string path)
            => new(Text(m, RouteRunway.Airport, path), Text(m, RouteRunway.Runway, path),
                Point(m, path, RouteRunway.Latitude, RouteRunway.Longitude))
            {
                TrueCourse = OptionalSingle(m, RouteRunway.TrueCourse, path),
                LengthFt = m.TryGet(RouteRunway.LengthFt, out _) ? Int(m, RouteRunway.LengthFt, path) : null
            };

        // ---- Scalars. Each answers the default a left-out member has, and records a member in another shape.

        private double Double(FenixChannelPush? m, int number, string path)
        {
            if (m is null || !m.TryGet(number, out FenixChannelField field)) return 0;
            if (field.Type != FenixWireType.Fixed64)
            {
                Fail(PathOf(path, number));
                return 0;
            }

            return field.AsDouble();
        }

        // A repeated double, each entry a fixed64 of its own (not packed, as sent).
        private List<double> Doubles(FenixChannelPush m, int number, string path)
        {
            var values = new List<double>();
            foreach (FenixChannelField field in m.All(number))
            {
                if (field.Type != FenixWireType.Fixed64) Fail(PathOf(path, number));
                else values.Add(field.AsDouble());
            }

            return values;
        }

        private double? OptionalSingle(FenixChannelPush m, int number, string path)
        {
            if (!m.TryGet(number, out FenixChannelField field)) return null;
            if (field.Type != FenixWireType.Fixed32)
            {
                Fail(PathOf(path, number));
                return null;
            }

            return field.Fixed32;
        }

        private int Int(FenixChannelPush m, int number, string path)
        {
            if (!m.TryGet(number, out FenixChannelField field)) return 0;
            if (field.Type != FenixWireType.Varint)
            {
                Fail(PathOf(path, number));
                return 0;
            }

            return unchecked((int)field.Varint);
        }

        public bool Flag(FenixChannelPush m, int number, string path)
        {
            if (!m.TryGet(number, out FenixChannelField field)) return false;
            if (field.Type != FenixWireType.Varint)
            {
                Fail(PathOf(path, number));
                return false;
            }

            return field.AsBool();
        }

        private string Text(FenixChannelPush m, int number, string path)
        {
            if (!m.TryGet(number, out FenixChannelField field)) return "";

            string? text = field.AsString();
            if (text is null) Fail(PathOf(path, number));
            return text ?? "";
        }
    }
}
