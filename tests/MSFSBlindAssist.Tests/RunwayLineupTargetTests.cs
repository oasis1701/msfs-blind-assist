using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

// Characterization of the lineup-entry maths lifted out of TaxiAssistForm.PopulateDestinations:
// start row → snapped onto the centerline → FindRunwayLineupEntryNode. Runway 09 runs east along
// north=0 from east 0 to 1,000 m.
public class RunwayLineupTargetTests
{
    private static double Lat(double n) => RunwayFixture.Lat(n);
    private static double Lon(double e) => RunwayFixture.Lon(e);

    // Taxiway A1 meets the runway at east 200 and sits exactly ON the runway axis (north=0).
    // That makes A1's own node BOTH the plain-nearest node to every lineup point used below AND
    // within FindRunwayLineupEntryNode's own accept-immediately radius (perpendicular 0 m, well
    // under RUNWAY_REACH_MAX_CROSS_M's 120 m) — so `if (plainPerp <= maxAcceptableCrossM) return
    // plain;` fires before the method ever reaches its entrance-search loop. Taxiway "A" (from
    // A1's far end, away from the runway) exists only so the graph has more than one node; it is
    // never the nearer candidate for any lineup point tested here. These fixtures exercise
    // PickFullLengthStart + SnapStartToRunwayCenterline and the "no usable row" pavement-start
    // fallback — never FindRunwayLineupEntryNode's own entrance search, which needs the plain
    // nearest node to be MORE than 120 m off the centerline before the search loop ever runs.
    // See EntranceSearchGraph() below for a fixture built to reach it.
    private static TaxiGraph Graph() => TaxiGraph.Build(
        new List<TaxiPath>
        {
            new() { Name = "A1", Type = "T", Width = 98, StartType = "HS", EndType = "N",
                    StartLat = Lat(0), StartLon = Lon(200), EndLat = Lat(100), EndLon = Lon(200) },
            new() { Name = "A", Type = "T", Width = 98, StartType = "N", EndType = "N",
                    StartLat = Lat(100), StartLon = Lon(200), EndLat = Lat(100), EndLon = Lon(400) },
        },
        new List<ParkingSpot>(), new List<StartPosition>());

    // Shaped like LPPT 20: the lineup point's plain-nearest node is off to the side, on an
    // off-runway taxiway (node X, 150 m off axis) — far enough (> RUNWAY_REACH_MAX_CROSS_M,
    // 120 m) that FindRunwayLineupEntryNode's early "close enough" return does NOT fire, so it
    // must run its entrance-search loop. That loop must reject X itself (150 m off axis, well
    // outside any plausible runway corridor) and instead find node Y — a real entrance 15 m off
    // the axis, further back up the runway (east 100 vs the lineup point's east 300), linked to
    // X's off-runway taxiway — even though Y is nearer to nothing in particular and X is the
    // globally-nearest node to the lineup point (150 m vs Y's ~200.6 m). Whether Y clears the
    // search's perpendicular corridor (`halfWidthMeters + 5`) depends entirely on the runway
    // width fallback: at the shipped 150 ft fallback the corridor is ~27.9 m wide and Y (15 m
    // off axis) is inside it; a runway with genuinely no width data would resolve nothing at all
    // without that fallback.
    private static TaxiGraph EntranceSearchGraph() => TaxiGraph.Build(
        new List<TaxiPath>
        {
            new() { Name = "B", Type = "T", Width = 98, StartType = "N", EndType = "N",
                    StartLat = Lat(15), StartLon = Lon(100), EndLat = Lat(150), EndLon = Lon(300) },
        },
        new List<ParkingSpot>(), new List<StartPosition>());

    private static Runway Runway09() => new()
    {
        RunwayID = "09", StartLat = Lat(0), StartLon = Lon(0), EndLat = Lat(0), EndLon = Lon(1000),
        Heading = 90, Length = 1000 / 0.3048, Width = 150,
    };

    [Fact]
    public void Lineup_point_is_the_start_row_snapped_onto_the_centerline()
    {
        // The row sits 8 m north of the axis at east 60; the snap pulls it back onto north = 0.
        var rows = new[] { new StartPosition { RunwayName = "09", Latitude = Lat(8), Longitude = Lon(60), Heading = 90 } };

        var r = RunwayLineupTarget.Resolve(Graph(), Runway09(), rows, null, null);

        Assert.Equal(Lat(0), r.LineupLat, 9);
        Assert.Equal(Lon(60), r.LineupLon, 9);
        Assert.NotNull(r.EntryNode);
        Assert.Equal(Lat(0), r.EntryNode!.Latitude, 9);      // A1's runway-side node
        Assert.Equal(Lon(200), r.EntryNode!.Longitude, 9);
    }

    [Fact]
    public void Without_a_start_row_the_pavement_start_is_the_lineup_point()
    {
        var r = RunwayLineupTarget.Resolve(Graph(), Runway09(), null, null, null);

        Assert.Equal(Lat(0), r.LineupLat, 9);
        Assert.Equal(Lon(0), r.LineupLon, 9);
        Assert.NotNull(r.EntryNode);
        Assert.Equal(Lat(0), r.EntryNode!.Latitude, 9);
        Assert.Equal(Lon(200), r.EntryNode!.Longitude, 9);
    }

    [Fact]
    public void Rows_that_all_fail_the_40_percent_rule_fall_back_to_the_pavement_start()
    {
        // A single row at east 500 on this 1,000 m runway sits at 50% — past PickFullLengthStart's
        // 40% bar — so it is not a usable departure point at all and Resolve must fall back to the
        // physical pavement start exactly as if no row had been supplied.
        var rows = new[] { new StartPosition { RunwayName = "09", Latitude = Lat(0), Longitude = Lon(500), Heading = 90 } };

        var r = RunwayLineupTarget.Resolve(Graph(), Runway09(), rows, null, null);

        Assert.Equal(Lat(0), r.LineupLat, 9);
        Assert.Equal(Lon(0), r.LineupLon, 9);
    }

    [Fact]
    public void Half_width_falls_back_to_150ft_and_the_search_finds_the_real_entrance()
    {
        // Start row on the axis, 30% down the runway (east 300) — kept by the 40% rule.
        var rows = new[] { new StartPosition { RunwayName = "09", Latitude = Lat(0), Longitude = Lon(300), Heading = 90 } };
        var rwy = Runway09();
        rwy.Width = 0; // forces the 150 ft fallback: half-width = 150 * 0.3048 / 2 = 22.86 m.

        var r = RunwayLineupTarget.Resolve(EntranceSearchGraph(), rwy, rows, null, null);

        Assert.Equal(Lat(0), r.LineupLat, 9);
        Assert.Equal(Lon(300), r.LineupLon, 9);
        Assert.NotNull(r.EntryNode);
        // Node Y (15 m off axis, east 100) — NOT node X (150 m off axis, east 300), which is the
        // plain-nearest node to the lineup point and would be returned by a bare FindNearestNode
        // or by the search if the width fallback (and so the search corridor) were dropped.
        Assert.Equal(Lat(15), r.EntryNode!.Latitude, 9);
        Assert.Equal(Lon(100), r.EntryNode!.Longitude, 9);
    }
}
