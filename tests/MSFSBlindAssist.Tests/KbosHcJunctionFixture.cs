using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The H / connector / C junction at the south-west end of a Boston runway 04R/22L, the taxi
/// network from real navdata (two measured legs are asserted in OffChartLegTests).
/// </summary>
internal static class KbosHcJunctionFixture
{
    public static readonly (double Lat, double Lon)
        HNorth = (42.363495, -71.006866), X = (42.361935, -71.006577), F1 = (42.361790, -71.006760),
        N261 = (42.361572, -71.007164), N260 = (42.361465, -71.007599), K = (42.361439, -71.007957),
        C1 = (42.361237, -71.007263), Z = (42.361153, -71.006950), Y = (42.361069, -71.006638),
        C4 = (42.360893, -71.005959), CEast = (42.360600, -71.004753), CWest = (42.362236, -71.010376);
    public static readonly (double Lat, double Lon) StopPosition = (42.3616609, -71.0070903);   // heading 234.5 true
    /// Beyond CEast with eastGap: 400 m with no taxiway name (GapMid halfway), then C again to CFarEast.
    public static readonly (double Lat, double Lon)
        GapMid = (42.360014, -71.002450), GapEnd = (42.359428, -71.000147), CFarEast = (42.359135, -70.998996);

    /// connectorName "C" reproduces the graph with online names loaded; "" is raw navdata.
    /// eastGap adds what the real taxiway C has 900 m east of the junction: a stretch the navdata
    /// leaves unnamed (nodes 339 to 348, 400 m), after which C resumes.
    public static TaxiGraph Build(string connectorName = "C", bool eastGap = false)
    {
        var paths = new List<TaxiPath>();
        void Add(string name, (double Lat, double Lon) a, (double Lat, double Lon) b)
            => paths.Add(new TaxiPath
            {
                StartLat = a.Lat, StartLon = a.Lon, EndLat = b.Lat, EndLon = b.Lon,
                Name = name, Type = "PT", StartType = "N", EndType = "N", Width = 98.0,
            });

        var h1 = (42.362633, -71.006668);
        var h2 = (42.362206, -71.006577);
        Add("H", HNorth, h1);
        Add("H", h1, h2);
        Add("H", h2, X);

        Add(connectorName, X, F1);
        Add(connectorName, F1, N261);
        Add(connectorName, N261, N260);
        Add(connectorName, N260, K);

        var c0 = (42.361469, -71.008057);
        var c5 = (42.360870, -71.005875);
        Add("C", CWest, c0);
        Add("C", c0, K);
        Add("C", K, C1);
        Add("C", C1, Z);
        Add("C", Z, Y);
        Add("C", Y, C4);
        Add("C", C4, c5);
        if (!eastGap) Add("C", c5, CEast);
        else
        {
            // Twelve nodes on the way to CEast, as the real C has: the router tries its ten nearest
            // nodes of a taxiway as entries, and here, as at the airport, none of them lies past the gap.
            var from = c5;
            for (int i = 1; i <= 12; i++)
            {
                var to = i == 12 ? CEast : (c5.Item1 + (CEast.Lat - c5.Item1) * i / 12.0, c5.Item2 + (CEast.Lon - c5.Item2) * i / 12.0);
                Add("C", from, to);
                from = to;
            }
            Add("", CEast, GapMid);
            Add("", GapMid, GapEnd);
            Add("C", GapEnd, CFarEast);
        }

        var runways = new List<Runway>
        {
            new Runway
            {
                RunwayID = "04R",
                StartLat = 42.350777, StartLon = -71.011986,
                EndLat = 42.376953, EndLon = -70.999298,
                Heading = 19.7, Width = 164.0, Length = 10138,
            },
        };
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "04R", Latitude = 42.350777, Longitude = -71.011986, Heading = 19.7 },
            new StartPosition { RunwayName = "22L", Latitude = 42.376953, Longitude = -70.999298, Heading = 199.7 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, runways);
    }

    /// Nearest node, asserting it is within 2 m.
    public static int Node(TaxiGraph g, (double Lat, double Lon) p)
    {
        int best = -1;
        double bestM = double.MaxValue;
        foreach (var n in g.Nodes.Values)
        {
            double d = TaxiGraph.FastDistanceMeters(p.Lat, p.Lon, n.Latitude, n.Longitude);
            if (d < bestM) { bestM = d; best = n.NodeId; }
        }
        if (best < 0 || bestM > 2.0)
            throw new InvalidOperationException($"no node within 2 m of {p.Lat}, {p.Lon} (nearest {bestM:F1} m)");
        return best;
    }
}
