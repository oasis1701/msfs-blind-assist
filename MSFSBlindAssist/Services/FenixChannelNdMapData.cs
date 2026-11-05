using System.Globalization;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One point of the GetMapData reply: <see cref="Kind"/> is the channel's kind number (1 airport, 2, 3 or 4 a navaid, 5 a
/// fix, 7 an NDB); <see cref="Tuned"/> says the point is the tuned navaid (member 4 = 1).
/// </summary>
internal sealed record FenixNdMapPoint(int Kind, string Ident, FenixLatLon Point, bool Tuned = false);

/// <summary>
/// The GetMapData reply. <paramref name="Range"/> is the ND's reach the reply covers (0 when left out), <paramref name="Mode"/>
/// the reply's mode number (2 ROSE NAV, 3 ARC, 4 PLAN; null when left out), <paramref name="MapOption"/> the option's name
/// (null for none) and <paramref name="Partial"/> says only a patch near the aircraft was sent.
/// </summary>
internal sealed record FenixNdMapPoints(
    IReadOnlyList<FenixNdMapPoint> Points, FenixLatLon Center, double TrueTrack, int Range, int? Mode,
    string? MapOption, long Version, bool Partial);

/// <summary>
/// The ND's map points (navaids, fixes, airports and NDBs) from the GetMapData reply: the decoder, the check that the
/// reply is the one the ND draws from, and the rows. Measured on 3.0.0.7613 at the KATL gate
/// (evidence03-nd.md, Part C). The reply is used only under <see cref="Check"/> ([FNX-22]).
/// </summary>
internal static class FenixChannelNdMapData
{
    /// <summary>A reply confirmed current longer ago than this is too old.</summary>
    public const long MaxAgeMs = 3000;

    /// <summary>
    /// The reply renews after the aircraft moves this fraction of the reply's range (sitting 2: steps of 2.00 to 2.08 NM
    /// at 20 and 16.01 to 16.14 NM at 160), so its center trails the frame's by up to that much (2.01 NM at ARC 20 and
    /// 16.04 NM at ARC 160 before a renewal).
    /// </summary>
    public const double CenterToleranceRangeFraction = 0.1;

    /// <summary>
    /// Added to the range fraction: the version is polled once a second, and the center is 0.11 to 0.14 NM from the
    /// frame's at a renewal (sitting 2, GetMapData in flight).
    /// </summary>
    public const double CenterToleranceMarginNm = 0.5;

    /// <summary>A route waypoint and a map point with the same ident are one point within this distance.</summary>
    private const double SamePointNm = 1.0;

    private const int ModeNav = 2, ModeArc = 3, ModePlan = 4;

    /// <summary>The look items of the map points, by kind.</summary>
    internal static class LookItem
    {
        public const string VorDme = "VOR/DME map point";
        public const string Dme = "DME map point";
        public const string Ndb = "NDB map point";
        public const string Airport = "airport map point";
        public const string Fix = "fix map point";
        public const string Vor = "VOR map point";
        public const string TunedVorDme = "tuned VOR/DME map point";
        public const string TunedVor = "tuned VOR map point";
        public const string TunedDme = "tuned DME map point";
    }

    /// <summary>The reply, or the field that has the wrong shape ("field 2.5 has the wrong shape").</summary>
    public static FenixChannelSection<FenixNdMapPoints> Decode(byte[] reply)
    {
        FenixChannelPush? push = FenixChannelPush.From(reply);
        if (push is null) return new(default, "the reply is not valid protobuf");

        string? wrong = null;
        void Wrong(string path) => wrong ??= path;

        var points = new List<FenixNdMapPoint>();
        foreach (int group in new[] { MapData.Airports, MapData.Navaids, MapData.Fixes, MapData.Ndbs })
        {
            string path = group.ToString(CultureInfo.InvariantCulture);
            foreach (FenixChannelField entry in push.All(group))
            {
                FenixChannelPush? message = entry.AsMessage();
                if (message is null) { Wrong(path); continue; }

                double latitude = ReadDouble(message, MapPoint.Latitude, $"{path}.{MapPoint.Latitude}", Wrong);
                double longitude = ReadDouble(message, MapPoint.Longitude, $"{path}.{MapPoint.Longitude}", Wrong);
                int kind = (int)ReadVarint(message, MapPoint.Kind, $"{path}.{MapPoint.Kind}", Wrong, 0)!.Value;
                string ident = "";
                if (message.TryGet(MapPoint.Ident, out FenixChannelField identField))
                {
                    string? text = identField.AsString();
                    if (text is null) Wrong($"{path}.{MapPoint.Ident}");
                    else ident = text;
                }

                ulong tuned = ReadVarint(message, MapPoint.Tuned, $"{path}.{MapPoint.Tuned}", Wrong, 0)!.Value;

                points.Add(new FenixNdMapPoint(kind, ident, new FenixLatLon(latitude, longitude), tuned == 1));
            }
        }

        var center = new FenixLatLon(
            ReadDouble(push, MapData.CenterLatitude, "5", Wrong), ReadDouble(push, MapData.CenterLongitude, "6", Wrong));
        double track = ReadDouble(push, MapData.TrueTrack, "7", Wrong);
        ulong range = ReadVarint(push, MapData.Range, "8", Wrong, 0)!.Value;
        ulong? mode = ReadVarint(push, MapData.Mode, "9", Wrong, null);
        ulong version = ReadVarint(push, MapData.Version, "10", Wrong, 0)!.Value;
        ulong partial = ReadVarint(push, MapData.Partial, "11", Wrong, 0)!.Value;
        ulong? option = ReadVarint(push, MapData.MapOption, "12", Wrong, null);

        if (wrong is not null) return FenixChannelSection<FenixNdMapPoints>.WrongShape(wrong);

        return FenixChannelSection<FenixNdMapPoints>.Ok(new FenixNdMapPoints(
            points, center, track, (int)Math.Min(range, int.MaxValue), mode is null ? null : (int)Math.Min(mode.Value, int.MaxValue),
            FenixChannelNd.MapOptionName(option), (long)Math.Min(version, long.MaxValue), partial != 0));
    }

    private static double ReadDouble(FenixChannelPush message, int number, string path, Action<string> wrong)
    {
        if (!message.TryGet(number, out FenixChannelField field)) return 0;
        if (field.Type == FenixWireType.Fixed64) return field.AsDouble();

        wrong(path);
        return 0;
    }

    private static ulong? ReadVarint(FenixChannelPush message, int number, string path, Action<string> wrong, ulong? whenAbsent)
    {
        if (!message.TryGet(number, out FenixChannelField field)) return whenAbsent;
        if (field.Type == FenixWireType.Varint) return field.Varint;

        wrong(path);
        return whenAbsent;
    }

    /// <summary>
    /// Null when the reply is the one the ND draws from, else why not (the first failing reason): "map mode differs",
    /// "map option differs", "map range below the view", "map center differs", "map data too old".
    /// </summary>
    public static string? Check(FenixNdMapPoints p, FenixNdData header, FenixNdFrame frame, long receivedAtMs, long nowMs)
    {
        int? headerMode = header.Mode switch { "NAV" => ModeNav, "ARC" => ModeArc, "PLAN" => ModePlan, _ => null };
        if (headerMode is null || p.Mode != headerMode) return "map mode differs";
        if (!string.Equals(p.MapOption, header.MapOption, StringComparison.Ordinal)) return "map option differs";

        // The reply's range is the ND's reach: the range in ARC, half the range in ROSE NAV and PLAN. A reply that
        // reaches further still covers the view.
        double reach = frame.Mode == "ARC" ? frame.RangeNm : frame.RangeNm / 2;
        if (p.Range < reach) return "map range below the view";

        double away = NavigationCalculator.CalculateDistance(
            frame.Center.Latitude, frame.Center.Longitude, p.Center.Latitude, p.Center.Longitude);
        // The frame's center stays the reference (192 in ARC and ROSE NAV, 84 in PLAN); the reply's trails it by up to a
        // tenth of its own range before it renews.
        if (!(away <= p.Range * CenterToleranceRangeFraction + CenterToleranceMarginNm)) return "map center differs";

        if (nowMs - receivedAtMs > MaxAgeMs) return "map data too old";
        return null;
    }

    /// <summary>
    /// The rows of the points the ND draws for the reply's option, nearest first (a tie keeps the reply's order): "Map:
    /// LGC, magenta circle with a cross, 45.2 NM, 103° left". A point on a drawn route list (the same ident within 1
    /// NM) is drawn once, in the route's style, and is the route layer's. With no route (null) every point is listed.
    /// </summary>
    public static FenixNdLayer Rows(FenixNdMapPoints p, FenixNdFrame f, FenixRoute? route)
    {
        string? centerIdent = f.Mode == "PLAN" ? FenixChannelNdRows.PlanCenterIdent(f, route) : null;
        var listed = new List<(FenixNdPlace Place, FenixNdMapPoint Point, string Item)>();
        foreach (FenixNdMapPoint point in p.Points)
        {
            if (LookItemFor(p.MapOption, point) is not { } item) continue;
            if (FenixChannelNdGeometry.Place(f, point.Point) is not { } place) continue;
            if (IsOnRoute(point, route)) continue;

            listed.Add((place, point, item));
        }

        var rows = new List<string>();
        foreach ((FenixNdPlace place, FenixNdMapPoint point, string item) in listed.OrderBy(entry => entry.Place.DistanceNm))
        {
            string ident = point.Ident.Length == 0 ? "" : point.Ident + ", ";
            rows.Add($"Map: {ident}{FenixChannelNdRows.Words(item)}, {FenixChannelNdGeometry.Where(f, place, centerIdent)}");
        }

        return FenixNdLayer.Of(rows);
    }

    // The kinds each option makes the ND draw (Part C's table, kind 2 with VOR.D from sitting 2) and their looks; null
    // for a kind it does not draw. The tuned navaid (kinds 2, 3 and 4, member 4 = 1) is drawn cyan with any option,
    // none included (sitting 2); an NDB flagged tuned was never seen and stays the NDB option's.
    private static string? LookItemFor(string? option, FenixNdMapPoint point)
    {
        if (point.Tuned && point.Kind is 2 or 3 or 4)
        {
            return point.Kind switch { 2 => LookItem.TunedVor, 3 => LookItem.TunedVorDme, _ => LookItem.TunedDme };
        }

        return (option, point.Kind) switch
        {
            ("VOR.D", 2) => LookItem.Vor,
            ("VOR.D", 3) => LookItem.VorDme,
            ("VOR.D", 4) => LookItem.Dme,
            ("NDB", 7) => LookItem.Ndb,
            ("ARPT", 1) => LookItem.Airport,
            ("WPT", 5) => LookItem.Fix,
            _ => null
        };
    }

    // The route layer lists a point (the same ident within 1 NM) when it is a waypoint symbol of the active or the
    // temporary list (FenixChannelNdRoute.ListedSymbols: the missed approach's only while the ND draws it (44.1), never the alternate's: VHP, an
    // alternate waypoint, was drawn as a map point), or the FIX INFO fix (LOZ, tuned, was drawn green once).
    private static bool IsOnRoute(FenixNdMapPoint point, FenixRoute? route)
    {
        if (route is null) return false;

        IEnumerable<FenixRouteLeg> listed = FenixChannelNdRoute.ListedSymbols(route.Legs, route.MissedApproachDrawn)
            .Concat(FenixChannelNdRoute.ListedSymbols(route.TemporaryLegs, false));
        return listed.Any(leg => Same(leg.Ident, leg.Point, point))
            || route.FixInfos.Any(fix => Same(fix.Ident, fix.Point, point));
    }

    private static bool Same(string ident, FenixLatLon at, FenixNdMapPoint point)
        => string.Equals(ident, point.Ident, StringComparison.Ordinal)
            && NavigationCalculator.CalculateDistance(
                at.Latitude, at.Longitude, point.Point.Latitude, point.Point.Longitude) <= SamePointNm;
}
