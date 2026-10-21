// Characterization tests for TaxiGraph.DepartureLineupPoint (2026-09-24): the ONE place that turns a
// runway end into the departure lineup point (PickFullLengthStart + SnapStartToRunwayCenterline),
// plus the rule that a point snapped BEHIND the runway_end pavement edge is kept only where the taxi
// network reaches back to it.
//
// Background: Taxi2Gate's converted LFPG puts 09L's start row 136 m to the side of the runway and
// 43 m behind its pavement edge, with nothing behind the edge. Snapped, the lineup point sat on the
// grass, the Z1 hold node became the nearest node — i.e. the route destination — and the lineup tone
// cut across the grass instead of following Z1 onto the runway. A starter extension (EGKK 26L 406 m
// back, iniBuilds EGLL 09L from AB13) is the legitimate version of the same geometry and must keep
// its point.
//
// Fixture idiom (shared with RunwayReachEndedShortTests): an east-west runway on the equator, where
// along-track metres = degrees of longitude x 111132.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class DepartureLineupPointTests
{
    private const double M_PER_DEG = 111132.0;
    private const double FarLon = 0.027;           // ~3000 m runway, pavement edge at lon 0

    private static Runway Rwy() => new Runway
    {
        RunwayID = "09", Heading = 90, Width = 150,
        StartLat = 0, StartLon = 0, EndLat = 0, EndLon = FarLon,
    };

    private static StartPosition Start(double latM, double alongM) => new StartPosition
    {
        RunwayName = "09", Latitude = latM / M_PER_DEG, Longitude = alongM / M_PER_DEG,
    };

    // A connector meeting the runway 60 m in, plus (optionally) taxi pavement behind the edge.
    private static TaxiGraph Graph(bool pavementBehind)
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { Name = "Z1", StartLat = 0.001, StartLon = 60 / M_PER_DEG, EndLat = 0, EndLon = 60 / M_PER_DEG },
            new TaxiPath { Name = "",   StartLat = 0,     StartLon = 60 / M_PER_DEG, EndLat = 0, EndLon = 200 / M_PER_DEG },
        };
        if (pavementBehind)
            paths.Add(new TaxiPath { Name = "X", StartLat = 0, StartLon = -80 / M_PER_DEG, EndLat = 0, EndLon = 60 / M_PER_DEG });
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static double AlongM((double Lat, double Lon) p) => p.Lon * M_PER_DEG;

    [Fact]
    public void A_start_row_behind_the_edge_with_nothing_behind_it_moves_to_the_edge()
    {
        // LFPG 09L shape: 136 m to the side, 43 m behind the pavement edge.
        var p = Graph(pavementBehind: false).DepartureLineupPoint(new[] { Start(136, -43) }, Rwy());
        Assert.Equal(0.0, AlongM(p), 1);
        Assert.Equal(0.0, p.Lat, 9);
    }

    [Fact]
    public void A_start_row_behind_the_edge_on_a_starter_extension_is_kept()
    {
        // EGKK 26L / EGLL 09L shape: taxi pavement runs back past the lineup point.
        var p = Graph(pavementBehind: true).DepartureLineupPoint(new[] { Start(110, -43) }, Rwy());
        Assert.Equal(-43.0, AlongM(p), 0);
        Assert.Equal(0.0, p.Lat, 9);   // the lateral snap still applies
    }

    [Fact]
    public void A_start_row_just_behind_the_edge_is_rounding_and_is_left_alone()
    {
        var p = Graph(pavementBehind: false).DepartureLineupPoint(new[] { Start(0, -8) }, Rwy());
        Assert.Equal(-8.0, AlongM(p), 0);
    }

    [Fact]
    public void A_start_row_on_the_runway_is_unchanged()
    {
        var p = Graph(pavementBehind: false).DepartureLineupPoint(new[] { Start(0, 45) }, Rwy());
        Assert.Equal(45.0, AlongM(p), 0);
    }

    [Fact]
    public void No_start_row_falls_back_to_the_pavement_edge()
    {
        var p = Graph(pavementBehind: false).DepartureLineupPoint(null, Rwy());
        Assert.Equal((0.0, 0.0), p);
    }
}
