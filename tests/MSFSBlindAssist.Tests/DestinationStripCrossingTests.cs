// Characterization tests for how a crossing of the DESTINATION runway's own strip is handled
// by TaxiGuidanceManager.InsertRunwayCrossingHoldShorts (audit finding, 2026-08-24).
//
// `WhichRunwayCrossedByEdge` names a crossing after whichever runway END is nearer the
// crossing point, not after the end the pilot selected. The old rule skipped any crossing
// whose name equalled the destination's, which was wrong in both directions:
//
//   - a route to 04L crossing the 04L/22R strip nearer the 22R end reported "22R", slipped
//     past the exclusion, and announced a hold-short of a runway the pilot never named;
//   - the same crossing nearer the 04L end reported "04L" and was DROPPED ENTIRELY — no
//     hold-short before crossing the active runway, which is the runway-incursion direction
//     (FAA AIM 4-3-18 / ICAO Doc 4444), and the one CLAUDE.md forbids disabling.
//
// The rule is now: skip ONLY the route's own arrival (the FINAL segment, which
// TruncateToHoldShort already truncated to and tagged), reciprocal-aware; every other
// crossing of that strip is tagged, and labelled with the designator the pilot selected.
//
// These pin the two ingredients the fix rests on. The composition itself
// (`sameStripAsDestination && i >= route.Segments.Count - 1`) needs a live graph and route,
// so it is covered by the in-sim plan rather than here.

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class DestinationStripCrossingTests
{
    // --- "is this the destination's own strip?" -----------------------------------------

    [Theory]
    [InlineData("22R", "04L")]   // the reported crossing is the reciprocal of the destination
    [InlineData("04L", "04L")]   // ...or the destination itself, depending which end is nearer
    [InlineData("27", "09")]
    [InlineData("27R", "09L")]
    [InlineData("36C", "18C")]   // centre keeps its letter across the reciprocal
    public void A_crossing_of_the_destination_strip_is_recognised_from_either_end(
        string crossedRwy, string destination)
    {
        Assert.True(TaxiGuidanceManager.RunwayDesignatorsMatch(crossedRwy, destination));
    }

    [Theory]
    [InlineData("04R", "04L")]   // the PARALLEL runway is a different strip and must be tagged
    [InlineData("22L", "04L")]
    [InlineData("08L", "04L")]
    public void A_crossing_of_a_different_runway_is_not_the_destination_strip(
        string crossedRwy, string destination)
    {
        Assert.False(TaxiGuidanceManager.RunwayDesignatorsMatch(crossedRwy, destination));
    }

    // --- what the pilot now hears --------------------------------------------------------

    [Fact]
    public void The_crossing_is_labelled_with_the_designator_the_pilot_selected()
    {
        // Geometry reports "22R" (the nearer end); the pilot asked for 04L. The label is
        // composed from the DESTINATION designator, so the callout names the runway on their
        // clearance — and keeps the hold point, which is what distinguishes this crossing
        // callout from the destination's own "Stop. Hold short of Runway 04L".
        Assert.Equal("runway 04L at D5", RouteRunwayCrossings.ComposeCrossingLabel("D5", "04L"));
        Assert.Equal("runway 04L", RouteRunwayCrossings.ComposeCrossingLabel(null, "04L"));
    }

    [Fact]
    public void A_hold_node_already_naming_this_strip_keeps_its_own_label()
    {
        // The scenery's own name for the line wins when it already names this pavement, from
        // either end — ComposeCrossingLabel's existing reciprocal tolerance, which the
        // designator swap above must not disturb.
        Assert.Null(RouteRunwayCrossings.ComposeCrossingLabel("runway 22R", "04L"));
        Assert.Null(RouteRunwayCrossings.ComposeCrossingLabel("runway 04L", "04L"));
    }

    [Fact]
    public void A_user_end_of_taxiway_terminator_is_still_never_overwritten()
    {
        Assert.Null(RouteRunwayCrossings.ComposeCrossingLabel("end of taxiway B", "04L"));
    }
}
