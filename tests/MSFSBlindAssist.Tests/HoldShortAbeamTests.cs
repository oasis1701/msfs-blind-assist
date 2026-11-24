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
