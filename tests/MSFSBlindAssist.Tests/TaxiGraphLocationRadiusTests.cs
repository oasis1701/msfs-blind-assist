using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// GC-5 (PR #230 review), as the owner ruled on it. <see cref="TaxiGraph.DescribeLocation"/> gathered
/// its node candidates from a FIXED ±30-cell ring of the 1e-5° node hash — ±33 m north-south but only
/// ±33·cos(latitude) m east-west: ±20 m at Schiphol's 52°N, ±7 m at Longyearbyen (ENSB, 78°N) — under
/// a comment claiming "~= 330 m". At 52°N a stand more than about 20 m east or west of the aircraft
/// was never a candidate, so Where-Am-I (Alt+Y, and the first line of Alt+L) named the taxiway, or
/// nothing, instead of the gate.
///
/// <para>The ruling gives the three node answers different reaches, and each is pinned here. The
/// STAND (40 m) is found in every direction at every latitude. The "near a runway start" answer
/// (50 m) keeps EXACTLY the old ring: it outranks the taxiway you are on, and Build names the entry
/// junction or hold line "Runway X", so a wider reach would change what is said at a hold line. The
/// FALLBACK (60 m) is found in every direction and takes the nearest node that HAS a taxiway name,
/// so a nearer unnamed node can no longer silence it.</para>
///
/// <para>Offsets are true metres in TaxiGraph.FastDistanceMeters' own model (a due-east offset uses
/// cos of the shared latitude). Each fixture keeps what it does not test out of reach: named nodes
/// beyond 60 m and taxi edges beyond their tolerance, unless the test is about them.</para>
/// </summary>
public class TaxiGraphLocationRadiusTests
{
    private const double MetresPerDegLat = 111132.0;

    private static double North(double lat, double metres) => lat + metres / MetresPerDegLat;

    private static double East(double lat, double lon, double metres)
        => lon + metres / (MetresPerDegLat * Math.Cos(lat * Math.PI / 180.0));

    private static TaxiPath Path(string name, string type, string startType, string endType,
                                 double lat1, double lon1, double lat2, double lon2, double widthFt) => new()
    {
        Name = name, Type = type, Width = widthFt, StartType = startType, EndType = endType,
        StartLat = lat1, StartLon = lon1, EndLat = lat2, EndLon = lon2,
    };

    private static TaxiGraph Graph(List<TaxiPath> paths, List<ParkingSpot>? stands = null,
                                   List<StartPosition>? starts = null)
        => TaxiGraph.Build(paths, stands ?? new List<ParkingSpot>(), starts ?? new List<StartPosition>(),
                           new List<Runway>());

    // ---- The stand: 40 m in every direction, at every latitude ----

    /// <summary>
    /// Stand "A 5" at <paramref name="standLat"/>/<paramref name="standLon"/> on an unnamed lead-in from
    /// a connector <paramref name="connectorNorthMetres"/> north and <paramref name="connectorEastMetres"/>
    /// east of the stand, and named taxiway K running 300 m east from that connector. The stand's own
    /// node carries no taxiway name; the connector is the nearest NAMED node.
    /// </summary>
    private static TaxiGraph StandOnLeadIn(double standLat, double standLon,
                                           double connectorNorthMetres, double connectorEastMetres)
    {
        double cLat = North(standLat, connectorNorthMetres);
        double cLon = East(standLat, standLon, connectorEastMetres);
        var paths = new List<TaxiPath>
        {
            Path("", "P", "N", "P", cLat, cLon, standLat, standLon, 60.0),
            Path("K", "T", "N", "N", cLat, cLon, cLat, East(cLat, cLon, 300.0), 75.0),
        };
        var stand = new ParkingSpot { Name = "A", Number = 5, Latitude = standLat, Longitude = standLon };
        return Graph(paths, stands: new List<ParkingSpot> { stand });
    }

    [Theory]
    [InlineData(52.0, 4.0, 30.0)]       // Schiphol's latitude: the old ring reached 20.5 m east-west
    [InlineData(52.0, 4.0, -30.0)]      // due WEST, same reach
    [InlineData(52.0, 4.0, 39.0)]       // just inside PARKING_RADIUS_M (40 m)
    [InlineData(78.246, 15.466, 35.0)]  // ENSB: the old ring reached 6.8 m east-west
    public void A_stand_due_east_or_west_within_40_m_is_named(double lat, double lon, double metres)
    {
        var g = StandOnLeadIn(lat, East(lat, lon, metres), connectorNorthMetres: 60.0, connectorEastMetres: 0.0);

        Assert.Equal("Gate A 5", g.DescribeLocation(lat, lon));
    }

    [Fact]
    public void A_stand_due_north_within_40_m_is_named()
    {
        // The old ring reached 33.3 m north-south at any latitude.
        var g = StandOnLeadIn(North(52.0, 38.0), 4.0, connectorNorthMetres: 0.0, connectorEastMetres: 60.0);

        Assert.Equal("Gate A 5", g.DescribeLocation(52.0, 4.0));
    }

    [Fact]
    public void A_stand_45_m_away_is_not_the_gate_you_are_at()
    {
        // Honouring the radius must not widen it: PARKING_RADIUS_M is 40 m. The stand's own node is
        // unnamed and the named connector is 75 m out, beyond the 60 m fallback, so nothing answers.
        var g = StandOnLeadIn(52.0, East(52.0, 4.0, 45.0), connectorNorthMetres: 60.0, connectorEastMetres: 0.0);

        Assert.Equal("", g.DescribeLocation(52.0, 4.0));
    }

    // ---- "Near a runway start": EXACTLY the old ring ----

    /// <summary>
    /// Named taxiway A from 200 m beyond the aircraft at (<paramref name="lat"/>, <paramref name="lon"/>),
    /// THROUGH it, to its last node R <paramref name="northMetres"/> north and <paramref name="eastMetres"/>
    /// east of the aircraft. A runway-09 start row sits on R, so Build names R "Runway 09" (a lone start
    /// row pairs with nothing, so no runway centerline answers first). The aircraft is ON taxiway A: the
    /// answer is "Runway 09" when the runway-start answer reaches R and "Taxiway A" when it does not.
    /// </summary>
    private static TaxiGraph TaxiwayToRunwayStart(double lat, double lon, double northMetres, double eastMetres)
    {
        double length = Math.Sqrt(northMetres * northMetres + eastMetres * eastMetres);
        double rLat = North(lat, northMetres), rLon = East(lat, lon, eastMetres);
        double fLat = North(lat, -200.0 * northMetres / length);
        double fLon = East(lat, lon, -200.0 * eastMetres / length);
        var start = new StartPosition { RunwayName = "09", Latitude = rLat, Longitude = rLon, Heading = 90.0 };
        return Graph(new List<TaxiPath> { Path("A", "T", "N", "N", fLat, fLon, rLat, rLon, 75.0) },
                     starts: new List<StartPosition> { start });
    }

    [Fact]
    public void A_runway_start_node_40_m_due_east_does_not_outrank_the_taxiway_you_are_on()
    {
        // At 52°N the old ring reached 20.5 m east-west, and this answer keeps that reach: 40 m west of
        // the runway entry you are still on taxiway A. (A 50 m reach in every direction — the option
        // the ruling rejected — would say "Runway 09" here.)
        var g = TaxiwayToRunwayStart(52.0, 4.0, northMetres: 0.0, eastMetres: 40.0);

        Assert.Equal("Taxiway A", g.DescribeLocation(52.0, 4.0));
    }

    [Fact]
    public void A_runway_start_node_20_m_due_north_still_outranks_the_taxiway_you_are_on()
    {
        // Inside the old ring (33.3 m north-south), so today's answer stands.
        var g = TaxiwayToRunwayStart(52.0, 4.0, northMetres: 20.0, eastMetres: 0.0);

        Assert.Equal("Runway 09", g.DescribeLocation(52.0, 4.0));
    }

    /// <summary>
    /// The runway-start answer reaches exactly the nodes the OLD ring could see, sample by sample,
    /// checked against a verbatim copy of that ring (<see cref="OldNodeHashRingSees"/>).
    /// <paramref name="insideOldRing"/> records which side of the ring's edge each sample sits on, so
    /// no row rests on a guess. It passes before this change and after it; that is the point.
    /// </summary>
    [Theory]
    // 52°N: 30 cells is 33.3 m north-south but only 20.5 m east-west, and the ring is a RECTANGLE of
    // cells, not a circle.
    [InlineData(52.0, 4.0, 31.0, 0.0, true)]
    [InlineData(52.0, 4.0, -36.0, 0.0, false)]
    [InlineData(52.0, 4.0, 0.0, 18.0, true)]
    [InlineData(52.0, 4.0, 0.0, -23.0, false)]
    [InlineData(52.0, 4.0, 0.0, 20.7, true)]
    [InlineData(52.0, 4.0, 0.0, 21.0, false)]
    [InlineData(52.0, 4.0, 33.0, 20.0, true)]       // a corner: 38.6 m away, yet inside
    // ENSB, 78°N: 30 cells is 6.8 m east-west.
    [InlineData(78.246, 15.466, -32.0, 0.0, true)]
    [InlineData(78.246, 15.466, -35.0, 0.0, false)]
    [InlineData(78.246, 15.466, 0.0, -6.0, true)]
    [InlineData(78.246, 15.466, 0.0, -7.5, false)]
    // On the prime meridian. The old key is a STRING, so "0" and "-0" are different buckets, and from
    // an aircraft at 0.000001°E the ring listed "0" but not "-0": a node 0.14 m east (in "0") was seen,
    // one 0.14 m west (in "-0") never was. Pinned only to prove the replication is exact — comparing
    // rounded doubles, where 0.0 == -0.0, would call both inside.
    [InlineData(51.5, 0.000001, 20.0, 0.14, true)]
    [InlineData(51.5, 0.000001, 20.0, -0.14, false)]
    public void The_runway_start_reach_is_exactly_the_old_node_hash_ring(
        double lat, double lon, double northMetres, double eastMetres, bool insideOldRing)
    {
        var g = TaxiwayToRunwayStart(lat, lon, northMetres, eastMetres);
        var r = g.Nodes.Values.Single(n => n.ParkingName == "Runway 09");

        Assert.Equal(insideOldRing, OldNodeHashRingSees(lat, lon, r.Latitude, r.Longitude));
        Assert.Equal(insideOldRing ? "Runway 09" : "Taxiway A", g.DescribeLocation(lat, lon));
    }

    /// <summary>
    /// A verbatim copy of the ring this replaced: DescribeLocation's Pass 1 loop as it stood at
    /// aa04a1cf, over TaxiGraph.GetSpatialHashKey — the key under which Build files every node, so a
    /// node was seen exactly when one of the 61 x 61 keys the loop formed equals its own. Kept as
    /// STRINGS on purpose: that is what the production replication must match.
    /// </summary>
    private static bool OldNodeHashRingSees(double lat, double lon, double nodeLat, double nodeLon)
    {
        static string GetSpatialHashKey(double la, double lo) => $"{Math.Round(la, 5)},{Math.Round(lo, 5)}";

        double step = Math.Pow(10, -5);
        string nodeKey = GetSpatialHashKey(nodeLat, nodeLon);
        for (int dlat = -30; dlat <= 30; dlat++)
            for (int dlon = -30; dlon <= 30; dlon++)
                if (GetSpatialHashKey(lat + dlat * step, lon + dlon * step) == nodeKey)
                    return true;
        return false;
    }

    // ---- The fallback: the nearest NAMED node within 60 m, in every direction ----

    /// <summary>
    /// Named taxiway K running 300 m east from a node <paramref name="eastMetres"/> due east of
    /// (<paramref name="lat"/>, <paramref name="lon"/>). K's nearest point to the aircraft is that node.
    /// </summary>
    private static TaxiGraph TaxiwayStartingEastOf(double lat, double lon, double eastMetres)
    {
        double nLon = East(lat, lon, eastMetres);
        return Graph(new List<TaxiPath> { Path("K", "T", "N", "N", lat, nLon, lat, East(lat, nLon, 300.0), 75.0) });
    }

    [Theory]
    [InlineData(52.0, 4.0, 58.0, "Near taxiway K")]
    [InlineData(78.246, 15.466, 58.0, "Near taxiway K")]
    [InlineData(52.0, 4.0, 65.0, "")]   // NODE_FALLBACK_RADIUS_M is 60 m
    public void The_nearest_named_node_is_the_fallback_out_to_60_m(double lat, double lon, double metres, string expected)
    {
        var g = TaxiwayStartingEastOf(lat, lon, metres);

        Assert.Equal(expected, g.DescribeLocation(lat, lon));
    }

    [Fact]
    public void A_named_node_50_m_away_beats_a_nearer_unnamed_node_for_the_fallback()
    {
        // An unnamed taxilane ending 20 m due north — the old ring saw that node, took it as THE
        // nearest node, found no name on it and said nothing — and named taxiway K starting 50 m due
        // east. Both run on AWAY from the aircraft, so no edge is within its tolerance.
        var paths = new List<TaxiPath>
        {
            Path("", "T", "N", "N", North(52.0, 20.0), 4.0, North(52.0, 320.0), 4.0, 75.0),
            Path("K", "T", "N", "N", 52.0, East(52.0, 4.0, 50.0), 52.0, East(52.0, 4.0, 350.0), 75.0),
        };

        Assert.Equal("Near taxiway K", Graph(paths).DescribeLocation(52.0, 4.0));
    }
}
