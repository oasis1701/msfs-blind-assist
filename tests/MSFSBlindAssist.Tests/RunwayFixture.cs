// Shared synthetic runway geometry for the classifier, hold-placement, guard and membership tests.
// Coordinates are metres east / north of a base point just north of the equator (never (0, 0),
// which RunwayShape treats as an unset pavement end).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

internal static class RunwayFixture
{
    public const double M = 111132.0;
    public const double BaseLat = 0.01;
    public const double BaseLon = 0.01;

    private static readonly double MetresPerDegLon = M * Math.Cos(BaseLat * Math.PI / 180.0);

    public static double Lat(double northM) => BaseLat + northM / M;
    public static double Lon(double eastM) => BaseLon + eastM / MetresPerDegLon;

    /// <summary>East-west runway: <paramref name="name1"/> at the west end (east = 0), pavement and start rows on the same line.</summary>
    public static TaxiGraph.RunwayCenterline EastWest(
        string name1 = "09", string name2 = "27", double lengthM = 3000.0, double halfWidthM = 30.0, double northM = 0.0)
        => Line(name1, name2, 0.0, northM, lengthM, northM, halfWidthM);

    /// <summary>North-south runway at <paramref name="eastM"/>: <paramref name="name1"/> at <paramref name="fromNorthM"/>, <paramref name="name2"/> at <paramref name="toNorthM"/>.</summary>
    public static TaxiGraph.RunwayCenterline NorthSouth(
        string name1, string name2, double eastM, double fromNorthM, double toNorthM, double halfWidthM = 30.0)
        => Line(name1, name2, eastM, fromNorthM, eastM, toNorthM, halfWidthM);

    private static TaxiGraph.RunwayCenterline Line(
        string name1, string name2, double e1, double n1, double e2, double n2, double halfWidthM) => new()
    {
        Name1 = name1, Name2 = name2,
        Lat1 = Lat(n1), Lon1 = Lon(e1), Lat2 = Lat(n2), Lon2 = Lon(e2),
        HalfWidthMeters = RunwayShape.DefaultHalfWidthMeters,
        PavementLat1 = Lat(n1), PavementLon1 = Lon(e1), PavementLat2 = Lat(n2), PavementLon2 = Lon(e2),
        PavementHalfWidthMeters = halfWidthM,
    };

    public static TaxiNode Node(int id, double eastM, double northM,
        TaxiNodeType type = TaxiNodeType.Normal, string? holdShortName = null) => new()
    {
        NodeId = id, Latitude = Lat(northM), Longitude = Lon(eastM), Type = type, HoldShortName = holdShortName,
    };

    /// <summary>Segments joining consecutive nodes; each segment's HoldShortRunway carries its end node's DB name, as TaxiRouter does.</summary>
    public static List<TaxiRouteSegment> Route(params TaxiNode[] nodes)
    {
        var segments = new List<TaxiRouteSegment>();
        for (int i = 1; i < nodes.Length; i++)
        {
            segments.Add(new TaxiRouteSegment
            {
                FromNode = nodes[i - 1],
                ToNode = nodes[i],
                DistanceMeters = TaxiGraph.FastDistanceMeters(
                    nodes[i - 1].Latitude, nodes[i - 1].Longitude, nodes[i].Latitude, nodes[i].Longitude),
                HoldShortRunway = nodes[i].HoldShortName,
            });
        }
        return segments;
    }

    public static TaxiNode?[] Nodes(params (double East, double North)[] points)
    {
        var nodes = new TaxiNode?[points.Length];
        for (int i = 0; i < points.Length; i++) nodes[i] = Node(i + 1, points[i].East, points[i].North);
        return nodes;
    }
}
