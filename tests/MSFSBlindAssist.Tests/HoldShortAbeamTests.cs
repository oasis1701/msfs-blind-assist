// Tests for MSFSBlindAssist.Navigation.HoldShortAbeam: a hold-short the aircraft passes beside
// its node, outside the 25 m capture circle.
//
// OMDB, 2026-11-24, live, a route to Runway 30R over K4 with its hold short of runway 12R. The
// hold segment runs 031 degrees to the hold node at 25.2520256, 55.3638573. Rolled past 35 m to
// the side at 8 kt, the hold never latched: one "Hold short runway 12R at K4. Stop.", then the
// status read "continue onto taxiway M7B" and Continue did nothing. With a route the pilot had
// chosen to keep, following the tone back took the aircraft from the hold (123 m from 12R's
// centreline) to 48 m from it.

using MSFSBlindAssist.Navigation;

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
}
