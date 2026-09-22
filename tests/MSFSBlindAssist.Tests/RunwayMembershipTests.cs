// The runway checks outside the crossing pass use the same runway shape: Where-Am-I (5 m margin),
// takeoff-assist detection (strict, no margin), and the END a hold node is named after. Fixture: a
// 3,000 m runway 09/27, 200 ft wide, whose 09 start row sits 600 m inside the pavement (a displaced
// threshold) — built through TaxiGraph.Build with the runway table, as production does.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

public class RunwayMembershipTests
{
    private static TaxiGraph Build()
    {
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "09", Latitude = Lat(0), Longitude = Lon(600), Heading = 90 },
            new StartPosition { RunwayName = "27", Latitude = Lat(0), Longitude = Lon(3000), Heading = 270 },
        };
        var runways = new List<Runway>
        {
            new Runway { RunwayID = "09", Heading = 90, StartLat = Lat(0), StartLon = Lon(0), EndLat = Lat(0), EndLon = Lon(3000), Width = 200.0 },
            new Runway { RunwayID = "27", Heading = 270, StartLat = Lat(0), StartLon = Lon(3000), EndLat = Lat(0), EndLon = Lon(0), Width = 200.0 },
        };
        return TaxiGraph.Build(new List<TaxiPath>(), new List<ParkingSpot>(), starts, runways);
    }

    [Fact]
    public void Where_am_I_reports_the_runway_on_the_displaced_threshold()
    {
        var g = Build();
        Assert.Single(g.RunwayCenterlines);
        Assert.Equal("Runway 09", g.DescribeLocation(Lat(0), Lon(200)));
    }

    [Fact]
    public void Where_am_I_uses_the_real_half_width_plus_five_metres()
    {
        var g = Build();
        Assert.Equal("Runway 09", g.DescribeLocation(Lat(34), Lon(1000)));   // 30.48 + 5 = 35.48
        Assert.Equal("", g.DescribeLocation(Lat(37), Lon(1000)));
    }

    [Fact]
    public void Takeoff_detection_is_strict_on_the_real_half_width()
    {
        var g = Build();

        Assert.True(g.TryGetRunwayAtPosition(Lat(29), Lon(1000), 90.0, out string id, out _, out double thrLon, out _));
        Assert.Equal("09", id);
        Assert.Equal(Lon(600), thrLon, 9);   // the threshold point is still the start row
        Assert.False(g.TryGetRunwayAtPosition(Lat(31), Lon(1000), 90.0, out _, out _, out _, out _));
        Assert.True(g.TryGetRunwayAtPosition(Lat(0), Lon(200), 90.0, out _, out _, out _, out _));
    }

    [Fact]
    public void A_hold_node_is_named_after_the_pavement_end_it_is_nearer()
    {
        // 1,700 m along the pavement is nearer the 27 end; measured between the start rows
        // (600..3000 m) it would have read as nearer 09.
        var g = Build();
        Assert.Equal("27", TaxiGraph.MatchHoldShortRunwayName(Lat(60), Lon(1700), g.RunwayCenterlines, 150.0));
    }
}

// PR #238 deferred finding §5: Where-Am-I and takeoff assist named OPPOSITE ends at the same point.
//
// DescribeLocation names the end through RunwayShape.NameAt — the PAVEMENT frame.
// TryGetRunwayAtPosition migrated its MEMBERSHIP test to RunwayShape but still picked the END from
// rwy.HeadingDeg1 and rwy.Lat1/Lat2 — the START-ROW frame — and PR #238's own comment says so
// ("The threshold point, heading and end choice below are unchanged"), which is precisely the
// defect: half the method moved.
//
// On a NAME-SWAPPED centreline the two frames are reversed. Measured over 405 centrelines at 300
// fs2024 airports, standing 25% along the runway, five disagreed; four are this defect (AYCH 03/21,
// OIII 11R/29L, URWW 05/23, EDVQ 27C/09R). Verified against the runway table: the `start` row
// labelled "03" physically sits at the 21 threshold CARRYING 21's HEADING. The navdata is
// self-contradictory and TaxiGraph.Build's heading pass exists to rescue exactly these airports.
//
// A blind pilot asking Where-Am-I was told one runway while the takeoff-assist reference seeded at
// the same spot carried the other, along with that end's threshold coordinates.
public class RunwayEndNamingTests
{
    // AYCH in miniature, on the meridian so the axis bearing is 000: the runway TABLE puts 03 at the
    // south end and 21 at the north, while the START row labelled "03" sits at the north (21) end
    // carrying 21's heading, and the row labelled "21" sits at the south (03) end. Both rows are
    // displaced 600 m in, so the start table and the pavement ends are distinguishable.
    private static TaxiGraph NameSwapped()
    {
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "03", Latitude = Lat(2400), Longitude = Lon(0), Heading = 210 },
            new StartPosition { RunwayName = "21", Latitude = Lat(600), Longitude = Lon(0), Heading = 30 },
        };
        var runways = new List<Runway>
        {
            new Runway { RunwayID = "03", Heading = 30, StartLat = Lat(0), StartLon = Lon(0), EndLat = Lat(3000), EndLon = Lon(0), Width = 200.0 },
            new Runway { RunwayID = "21", Heading = 210, StartLat = Lat(3000), StartLon = Lon(0), EndLat = Lat(0), EndLon = Lon(0), Width = 200.0 },
        };
        return TaxiGraph.Build(new List<TaxiPath>(), new List<ParkingSpot>(), starts, runways);
    }

    [Fact]
    public void Where_am_I_and_takeoff_assist_name_the_same_end()
    {
        var g = NameSwapped();
        Assert.Single(g.RunwayCenterlines);

        // 750 m along the pavement from the south end, lined up to depart northbound.
        Assert.Equal("Runway 03", g.DescribeLocation(Lat(750), Lon(0)));
        Assert.True(g.TryGetRunwayAtPosition(Lat(750), Lon(0), 0.0, out string id, out _, out _, out _));
        Assert.Equal("03", id);
    }

    // ... and the threshold that travels with that name is still the START row, never the pavement
    // end: runway-destination lineup anchors on the start table, which is what accounts for
    // displaced thresholds and starter extensions.
    [Fact]
    public void The_threshold_is_the_start_row_nearest_that_end_not_the_pavement_edge()
    {
        var g = NameSwapped();

        Assert.True(g.TryGetRunwayAtPosition(Lat(750), Lon(0), 0.0, out _, out double thrLat, out _, out double hdg));

        // The row nearest the 03 (south) end is the one LABELLED 21 — that is the swap.
        Assert.Equal(Lat(600), thrLat, 9);
        Assert.Equal(0.0, hdg, 3);
    }

    [Fact]
    public void The_other_end_is_named_and_anchored_the_same_way()
    {
        var g = NameSwapped();

        Assert.True(g.TryGetRunwayAtPosition(Lat(2250), Lon(0), 180.0, out string id, out double thrLat, out _, out double hdg));
        Assert.Equal("21", id);
        Assert.Equal(Lat(2400), thrLat, 9);
        Assert.Equal(180.0, hdg, 3);
    }

    // A sound airport is unchanged: the pairing is by POSITION, and on sound data position and name
    // index agree.
    [Fact]
    public void A_sound_runway_is_unchanged()
    {
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "09", Latitude = Lat(0), Longitude = Lon(600), Heading = 90 },
            new StartPosition { RunwayName = "27", Latitude = Lat(0), Longitude = Lon(3000), Heading = 270 },
        };
        var runways = new List<Runway>
        {
            new Runway { RunwayID = "09", Heading = 90, StartLat = Lat(0), StartLon = Lon(0), EndLat = Lat(0), EndLon = Lon(3000), Width = 200.0 },
            new Runway { RunwayID = "27", Heading = 270, StartLat = Lat(0), StartLon = Lon(3000), EndLat = Lat(0), EndLon = Lon(0), Width = 200.0 },
        };
        var g = TaxiGraph.Build(new List<TaxiPath>(), new List<ParkingSpot>(), starts, runways);

        Assert.True(g.TryGetRunwayAtPosition(Lat(0), Lon(1000), 90.0, out string id, out _, out double thrLon, out double hdg));
        Assert.Equal("09", id);
        Assert.Equal(Lon(600), thrLon, 9);
        Assert.Equal(90.0, hdg, 1);
    }

    // The tripwire from the finding: hold-node MATCHING deliberately stays on the start rows (at
    // EGKK the nodes between 26L and 26R are 26L's lines, and a pavement match would rename them),
    // while the END it is NAMED after already comes from the shape. Do not harmonise them, and this
    // finding must not move either half — 700 m along the pavement is nearer the 03 end, and that
    // is the answer before and after.
    [Fact]
    public void The_end_a_hold_node_is_named_after_still_comes_from_the_shape()
    {
        var g = NameSwapped();
        Assert.Equal("03", TaxiGraph.MatchHoldShortRunwayName(Lat(700), Lon(60), g.RunwayCenterlines, 150.0));
    }
}
