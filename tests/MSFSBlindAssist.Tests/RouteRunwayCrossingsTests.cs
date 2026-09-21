// Characterization tests for the designator and label helpers of
// MSFSBlindAssist.Navigation.RouteRunwayCrossings: normalization/padding, the W (water-runway)
// suffix, reciprocals, label parsing, the crossing-label composition policy, the destination
// countdown-rail rule and prefix stripping. Hold placement is pinned in RunwayHoldPlacementTests,
// the spoken clause in RunwayEventDescriptionTests, classification in RunwayRouteClassifierTests.
//
// This is characterization, not spec verification: if a literal ever disagrees with actual output,
// the test must be corrected to match real output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RouteRunwayCrossingsTests
{
    // --- NormalizeDesignator ---------------------------------------------------

    [Theory]
    [InlineData("9", "09")]
    [InlineData("9l", "09L")]
    [InlineData("28R", "28R")]
    [InlineData("NE", "NE")]
    public void NormalizeDesignator_pads_and_uppercases(string input, string expected)
    {
        Assert.Equal(expected, RouteRunwayCrossings.NormalizeDesignator(input));
    }

    // --- Reciprocal --------------------------------------------------------

    [Theory]
    [InlineData("18W", "36W")]
    [InlineData("36W", "18W")]
    [InlineData("9", "27")]
    [InlineData("10L", "28R")]
    [InlineData("28R", "10L")]
    public void Reciprocal_adds_18_and_swaps_LR_suffix(string input, string expected)
    {
        Assert.Equal(expected, RouteRunwayCrossings.Reciprocal(input));
    }

    // --- ExtractRunwayDesignator ---------------------------------------------

    [Fact]
    public void ExtractRunwayDesignator_normalizes_an_unpadded_label()
    {
        Assert.Equal("09", RouteRunwayCrossings.ExtractRunwayDesignator("runway 9 at Q"));
    }

    [Fact]
    public void ExtractRunwayDesignator_returns_null_for_a_non_runway_label()
    {
        Assert.Null(RouteRunwayCrossings.ExtractRunwayDesignator("end of taxiway B"));
        Assert.Null(RouteRunwayCrossings.ExtractRunwayDesignator("A5"));
        Assert.Null(RouteRunwayCrossings.ExtractRunwayDesignator(null));
    }

    // --- ComposeCrossingLabel ---------------------------------------------

    [Fact]
    public void ComposeCrossingLabel_empty_label_becomes_runway_designator()
    {
        Assert.Equal("runway 10L", RouteRunwayCrossings.ComposeCrossingLabel(null, "10L"));
    }

    [Fact]
    public void ComposeCrossingLabel_upgrades_a_bare_holding_point_name()
    {
        Assert.Equal("runway 10L at A5", RouteRunwayCrossings.ComposeCrossingLabel("A5", "10L"));
    }

    [Fact]
    public void ComposeCrossingLabel_preserves_a_user_end_of_taxiway_hold()
    {
        Assert.Null(RouteRunwayCrossings.ComposeCrossingLabel("end of taxiway B", "10L"));
    }

    [Fact]
    public void ComposeCrossingLabel_preserves_a_label_naming_the_reciprocal_pavement()
    {
        Assert.Null(RouteRunwayCrossings.ComposeCrossingLabel("runway 10R", "28L"));
    }

    [Fact]
    public void ComposeCrossingLabel_preserves_a_correct_DB_name()
    {
        Assert.Null(RouteRunwayCrossings.ComposeCrossingLabel("runway 28L at Q", "28L"));
    }

    [Fact]
    public void ComposeCrossingLabel_corrects_a_DB_name_for_a_different_pavement()
    {
        Assert.Equal("runway 28L", RouteRunwayCrossings.ComposeCrossingLabel("runway 28R at Q", "28L"));
    }

    // --- ShouldExcludeFinalHold ---------------------------------------------------------
    //
    // A runway destination's final hold-short is TruncateToHoldShort's countdown rail, not a hold
    // point to count; a gate route's final hold-short is real.

    private static List<TaxiRouteSegment> Route(int count)
    {
        var list = new List<TaxiRouteSegment>();
        for (int i = 0; i < count; i++)
            list.Add(new TaxiRouteSegment
            {
                FromNode = new TaxiNode { NodeId = 1, Latitude = i * 0.001 },
                ToNode = new TaxiNode { NodeId = 2, Latitude = i * 0.001 + 0.001 },
                DistanceMeters = 100.0,
            });
        return list;
    }

    [Fact]
    public void ShouldExcludeFinalHold_ExcludesARunwayRoutesOwnTaggedFinalSegment()
    {
        var segs = Route(3);
        segs[^1].IsHoldShortPoint = true;
        Assert.True(RouteRunwayCrossings.ShouldExcludeFinalHold(segs, isRunwayDestination: true));
    }

    [Fact]
    public void ShouldExcludeFinalHold_KeepsAGateRoutesTaggedFinalSegment()
    {
        var segs = Route(3);
        segs[^1].IsHoldShortPoint = true;
        Assert.False(RouteRunwayCrossings.ShouldExcludeFinalHold(segs, isRunwayDestination: false));
    }

    [Fact]
    public void ShouldExcludeFinalHold_IsFalseWhenTheFinalSegmentCarriesNoHold()
        => Assert.False(RouteRunwayCrossings.ShouldExcludeFinalHold(Route(3), isRunwayDestination: true));

    [Fact]
    public void ShouldExcludeFinalHold_IsFalseForAnEmptyRoute()
        => Assert.False(RouteRunwayCrossings.ShouldExcludeFinalHold(
            Array.Empty<TaxiRouteSegment>(), isRunwayDestination: true));

    // --- StripRunwayPrefix -------------------------------------------------------

    [Theory]
    [InlineData("Runway 33L", "33L")]
    [InlineData("runway 09", "09")]
    [InlineData("RUNWAY 4R", "4R")]
    [InlineData("A9 - Gate Medium", "A9 - Gate Medium")]
    [InlineData("  Runway 22L  ", "22L")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void StripRunwayPrefix_drops_only_the_spoken_prefix(string? input, string expected)
    {
        Assert.Equal(expected, RouteRunwayCrossings.StripRunwayPrefix(input));
    }
}
