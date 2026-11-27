// Tests for MSFSBlindAssist.Navigation.HoldShortAbeam: a hold-short the aircraft passes beside
// its node, outside the 25 m capture circle.
//
// OMDB, 2026-11-24, live, a route to Runway 30R over K4 with its hold short of runway 12R. The
// hold segment runs 031 degrees to the hold node at 25.2520256, 55.3638573. Rolled past 35 m to
// the side at 8 kt, the hold never latched: one "Hold short runway 12R at K4. Stop.", then the
// status read "continue onto taxiway M7B" and Continue did nothing. With a route the pilot had
// chosen to keep, following the tone back took the aircraft from the hold (123 m from 12R's
// centreline) to 48 m from it.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class HoldShortAbeamTests
{
    [Fact]
    public void A_pass_beside_the_node_is_at_the_hold_once_level_with_it()
    {
        // The live pass: 35 m to the side, heading 032 on the 031 segment.
        Assert.False(HoldShortAbeam.IsReached(alongRemainingMetres: 12, crossMetres: -35, headingOffDegrees: 1));
        Assert.True(HoldShortAbeam.IsReached(alongRemainingMetres: 0, crossMetres: -35, headingOffDegrees: 1));
        Assert.True(HoldShortAbeam.IsReached(alongRemainingMetres: -6, crossMetres: 35, headingOffDegrees: 1));
    }

    [Fact]
    public void Coming_back_to_a_kept_route_past_the_node_is_at_the_hold()
    {
        // Where the tone had led the aircraft back to: 26 m past the node, 36 m to the side,
        // heading 335 against the segment's 031.
        Assert.True(HoldShortAbeam.IsReached(alongRemainingMetres: -26, crossMetres: -36, headingOffDegrees: -56));
    }

    [Fact]
    public void The_node_still_ahead_is_never_at_the_hold()
    {
        Assert.False(HoldShortAbeam.IsReached(alongRemainingMetres: 0.5, crossMetres: 0, headingOffDegrees: 0));
        Assert.False(HoldShortAbeam.IsReached(alongRemainingMetres: 60, crossMetres: 30, headingOffDegrees: 0));
    }

    // ---- TryMeasure: where the aircraft is, along and across the hold segment ----

    private const double HoldLat = 25.2520256, HoldLon = 55.3638573;   // OMDB K4, hold short of 12R
    private const double MetresPerDegree = 111132.0;

    // A point `north` and `east` metres from the hold node.
    private static (double Lat, double Lon) From(double north, double east)
        => (HoldLat + north / MetresPerDegree,
            HoldLon + east / (MetresPerDegree * Math.Cos(HoldLat * Math.PI / 180.0)));

    // A point `along` metres down the 031 degree hold segment from the node (negative: before it)
    // and `right` metres to its right.
    private static (double Lat, double Lon) OnSegment(double along, double right)
    {
        double brg = 31.0 * Math.PI / 180.0;
        return From(along * Math.Cos(brg) - right * Math.Sin(brg),
                    along * Math.Sin(brg) + right * Math.Cos(brg));
    }

    [Theory]
    [InlineData(-12.0, 35.0)]    // 12 m short of the line, 35 m to the right: the live pass
    [InlineData(0.0, -35.0)]
    [InlineData(26.0, 36.0)]     // past the line, coming back to a kept route
    public void The_offsets_are_measured_along_and_across_the_hold_segment(double along, double right)
    {
        var from = OnSegment(-60, 0);
        var aircraft = OnSegment(along, right);

        Assert.True(HoldShortAbeam.TryMeasure(
            aircraft.Lat, aircraft.Lon, from.Lat, from.Lon, HoldLat, HoldLon, HoldLat, HoldLon,
            out double alongRemaining, out double cross, out double bearing));

        Assert.Equal(-along, alongRemaining, 1);
        Assert.Equal(Math.Abs(right), Math.Abs(cross), 1);
        Assert.Equal(31.0, bearing, 1);
    }

    // A sub-metre hold segment has no direction. Projected onto, it read "level with the node, on
    // the line" from anywhere, and the index is parked on a hold segment the aircraft is still far
    // from (AdvanceToNearestSegment), so a hold 90 m away was "reached".
    [Fact]
    public void A_sub_metre_segment_measures_nothing()
    {
        var stubStart = OnSegment(-0.4, 0);
        var aircraft = OnSegment(-90, 20);

        Assert.False(HoldShortAbeam.TryMeasure(
            aircraft.Lat, aircraft.Lon, stubStart.Lat, stubStart.Lon, HoldLat, HoldLon, HoldLat, HoldLon,
            out double alongRemaining, out double cross, out _));
        Assert.Equal(0.0, alongRemaining);
        Assert.Equal(0.0, cross);
    }

    // The manager then measures along the segment before the stub, still to the hold node itself.
    [Fact]
    public void The_segment_before_a_stub_measures_to_the_hold_node()
    {
        var before = OnSegment(-45, 0);
        var stubStart = OnSegment(-0.4, 0);
        var far = OnSegment(-90, 20);
        var abeam = OnSegment(2, 35);

        Assert.True(HoldShortAbeam.TryMeasure(
            far.Lat, far.Lon, before.Lat, before.Lon, stubStart.Lat, stubStart.Lon, HoldLat, HoldLon,
            out double farAlong, out double farCross, out double bearing));
        Assert.False(HoldShortAbeam.IsReached(farAlong, farCross, 0));
        Assert.Equal(90.0, farAlong, 1);

        Assert.True(HoldShortAbeam.TryMeasure(
            abeam.Lat, abeam.Lon, before.Lat, before.Lon, stubStart.Lat, stubStart.Lon, HoldLat, HoldLon,
            out double abeamAlong, out double abeamCross, out _));
        Assert.True(HoldShortAbeam.IsReached(abeamAlong, abeamCross, 32.0 - bearing));
    }

    [Theory]
    [InlineData(75.0, true)]
    [InlineData(-75.0, true)]
    [InlineData(76.0, false)]
    [InlineData(-130.0, false)] // the next connector along, OMDB K5 from K4
    public void Only_within_reach_beside_the_node(double crossMetres, bool reached)
        => Assert.Equal(reached, HoldShortAbeam.IsReached(-1, crossMetres, 0));

    [Theory]
    [InlineData(-40.0, true)]
    [InlineData(-41.0, false)] // "Stop. Hold short" is not said well beyond the line
    public void Only_while_still_at_the_line(double alongRemainingMetres, bool reached)
        => Assert.Equal(reached, HoldShortAbeam.IsReached(alongRemainingMetres, 30, 0));

    [Theory]
    [InlineData(60.0, true)]
    [InlineData(-60.0, true)]
    // Still rolling along the taxiway before a 90 degree turn onto a short hold segment, already
    // level with that segment's end: not at the hold until turned toward it.
    [InlineData(90.0, false)]
    [InlineData(-90.0, false)]
    [InlineData(180.0, false)]
    public void Only_travelling_the_way_the_hold_segment_runs(double headingOffDegrees, bool reached)
        => Assert.Equal(reached, HoldShortAbeam.IsReached(-2, 50, headingOffDegrees));

    // ---- IsRollingOnBehindTheLine: the missed turn onto the hold's connector ----
    //
    // VirtualPilot missturn, 2026-11-27, fs2024, the 100 busiest airports: the pilot misses the turn
    // onto a connector that leaves a parallel taxiway at 30 to 60 degrees and rolls on along the
    // taxiway. Level with the hold node it is inside every bound of IsReached (KDTW W to runway 27R:
    // 38 m from the node, heading 43 degrees off the segment) and was stopped there, on the wrong
    // taxiway, in 61 of 1,502 flights. In every one the aircraft was 26 to 53 m farther from the
    // runway than the hold node, with the tone at full pan.

    [Theory]
    [InlineData(27.0, 90.0)]    // KDTW W to runway 27R
    [InlineData(27.0, -90.0)]
    [InlineData(26.0, 90.0)]    // KORD R to runway 09R: the least behind of them
    [InlineData(53.0, 90.0)]    // OEKM, runway 14: the most
    public void A_missed_turn_rolling_on_behind_the_line_is_not_at_the_hold(double behindLineMetres, double steeringErrorDegrees)
    {
        Assert.True(HoldShortAbeam.IsReached(alongRemainingMetres: 0, crossMetres: 38, headingOffDegrees: 43));
        Assert.True(HoldShortAbeam.IsRollingOnBehindTheLine(behindLineMetres, steeringErrorDegrees));
    }

    // The stops the capture is for. The first three are the ones it added to 7,617 ordinary
    // VirtualPilot flights that sit nearest the missed turns; the last two are the live OMDB passes,
    // on a connector square to its runway, where level with the node is level with the line.
    [Theory]
    [InlineData(-45.0, 129.0)]  // KCLE, a hold short of 06L/24R: the tone at full pan, but 45 m NEARER the runway than the node
    [InlineData(21.0, 0.0)]     // VHHH J5 to 07R: 21 m behind on a 180 degree turn, the tone centred
    [InlineData(35.0, 12.0)]    // KSFO, a hold short of 01R: 35 m behind, the tone a quarter panned
    [InlineData(0.0, 80.0)]     // OMDB K4: 42 m to the side, heading 40 degrees off the segment
    [InlineData(-26.0, 56.0)]   // OMDB K4: coming back to a kept route, 26 m past the node
    public void A_pass_at_the_line_or_steered_onto_the_route_is_still_at_the_hold(double behindLineMetres, double steeringErrorDegrees)
        => Assert.False(HoldShortAbeam.IsRollingOnBehindTheLine(behindLineMetres, steeringErrorDegrees));

    [Theory]
    [InlineData(10.0, 90.0, false)]   // as near the runway as the node, give or take 10 m: at the line
    [InlineData(10.5, 90.0, true)]
    [InlineData(40.0, 29.0, false)]   // the tone short of full pan: being steered onto the route
    [InlineData(40.0, 30.0, true)]
    [InlineData(40.0, -30.0, true)]
    public void It_takes_both_behind_the_line_and_the_tone_at_full_pan(double behindLineMetres, double steeringErrorDegrees, bool rollingOn)
        => Assert.Equal(rollingOn, HoldShortAbeam.IsRollingOnBehindTheLine(behindLineMetres, steeringErrorDegrees));

    // A stop the route placed itself, a named holding point, a node far from any centreline.
    [Fact]
    public void A_hold_that_guards_no_known_runway_is_taken_as_before()
        => Assert.False(HoldShortAbeam.IsRollingOnBehindTheLine(null, 120));

    [Theory]
    [InlineData(90.0, 120.0, 30.0)]     // the node 90 m from the centreline, the aircraft 120 m out on the same side
    [InlineData(-90.0, -120.0, 30.0)]   // the same on the runway's other side
    [InlineData(90.0, 64.0, -26.0)]     // past the line
    [InlineData(90.0, -20.0, -110.0)]   // across the centreline
    public void Behind_the_line_is_measured_on_the_nodes_side_of_the_runway(double nodeLateralMetres, double aircraftLateralMetres, double behindLineMetres)
        => Assert.Equal(behindLineMetres, HoldShortAbeam.MetresBehindLine(nodeLateralMetres, aircraftLateralMetres), 6);

    // ---- TaxiGuidanceManager.TryMeasureHoldAbeam: which segment gives the hold its direction ----

    private static TaxiNode Node((double Lat, double Lon) p) => new() { Latitude = p.Lat, Longitude = p.Lon };

    // A route along the 031 degree line through the given stations (metres along it, the hold node
    // at 0), one segment per pair. The last segment ends at the hold node.
    private static List<TaxiRouteSegment> RouteThrough(params double[] alongStations)
    {
        var segments = new List<TaxiRouteSegment>();
        for (int i = 0; i + 1 < alongStations.Length; i++)
            segments.Add(new TaxiRouteSegment
            {
                FromNode = Node(OnSegment(alongStations[i], 0)),
                ToNode = Node(OnSegment(alongStations[i + 1], 0)),
            });
        return segments;
    }

    [Theory]
    [InlineData(new[] { -60.0, 0.0 })]                 // the hold segment has a direction of its own
    [InlineData(new[] { -45.0, -0.4, 0.0 })]           // one stub: the segment before it
    [InlineData(new[] { -45.0, -0.8, -0.4, 0.0 })]     // two stubs in a row: the second segment before
    public void The_hold_segment_or_one_of_the_two_before_it_gives_the_direction(double[] stations)
    {
        var route = RouteThrough(stations);
        var aircraft = OnSegment(-90, 20);

        Assert.True(TaxiGuidanceManager.TryMeasureHoldAbeam(
            route, route.Count - 1, aircraft.Lat, aircraft.Lon,
            out double alongRemaining, out double cross, out double bearing));

        Assert.Equal(90.0, alongRemaining, 1);   // to the hold node, whichever segment gave the direction
        Assert.Equal(20.0, Math.Abs(cross), 1);
        Assert.Equal(31.0, bearing, 1);
    }

    [Theory]
    [InlineData(new[] { -45.0, -1.2, -0.8, -0.4, 0.0 })]   // three stubs: the look-back stops at two before
    [InlineData(new[] { -0.4, 0.0 })]                      // a stub hold on segment 0: nothing before it
    [InlineData(new[] { -0.8, -0.4, 0.0 })]                // stubs back to the route's start
    public void No_direction_within_the_look_back_measures_nothing(double[] stations)
    {
        var route = RouteThrough(stations);
        var aircraft = OnSegment(2, 35);

        Assert.False(TaxiGuidanceManager.TryMeasureHoldAbeam(
            route, route.Count - 1, aircraft.Lat, aircraft.Lon,
            out double alongRemaining, out double cross, out double bearing));
        Assert.Equal(0.0, alongRemaining);
        Assert.Equal(0.0, cross);
        Assert.Equal(0.0, bearing);
    }

    // The direction is the earlier segment's own, and the offsets are still to the hold node: a
    // segment running due east into the stub, not along the 031 line.
    [Fact]
    public void The_direction_comes_from_the_earlier_segment_and_the_offsets_from_the_hold_node()
    {
        var route = new List<TaxiRouteSegment>
        {
            new() { FromNode = Node(From(north: 0, east: -50)), ToNode = Node(OnSegment(-0.4, 0)) },
            new() { FromNode = Node(OnSegment(-0.4, 0)), ToNode = Node((HoldLat, HoldLon)) },
        };
        var aircraft = From(north: 20, east: -30);

        Assert.True(TaxiGuidanceManager.TryMeasureHoldAbeam(
            route, 1, aircraft.Lat, aircraft.Lon,
            out double alongRemaining, out double cross, out double bearing));

        Assert.InRange(bearing, 89.0, 91.0);
        Assert.InRange(alongRemaining, 29.0, 31.0);
        Assert.InRange(Math.Abs(cross), 19.0, 21.0);
    }
}
