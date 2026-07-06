// Characterization tests for MSFSBlindAssist.Navigation.RouteRunwayCrossings.
//
// Ports the RouteRunwayCrossings-relevant golden cases from
// tools/ProgressiveTaxiProbe/Program.cs (sections #7, #9(f), #10, #11): the KSFO
// same-runway-twice incident, the KBOS three-distinct-runways summary, label-shape
// parsing, destination-truncation exclusion, designator normalization/padding, the W
// (water-runway) suffix, reciprocal merging, and the crossing-label composition policy.
//
// This is characterization, not spec verification: values are taken from the probe /
// derived by reasoning about the source and confirmed by running the tests; if a
// literal ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RouteRunwayCrossingsTests
{
    private static TaxiRouteSegment Seg(bool hold, string? label) => new TaxiRouteSegment
    {
        FromNode = new TaxiNode(),
        ToNode = new TaxiNode(),
        IsHoldShortPoint = hold,
        HoldShortRunway = label,
    };

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

    // --- Describe: KSFO 2026-07-01 incident shape --------------------------

    [Fact]
    public void Describe_same_runway_crossed_twice_reports_twice()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Seg(false, null), Seg(true, "runway 10L"), Seg(false, null), Seg(true, "runway 10L"),
        };

        var (clause, nonRunway) = RouteRunwayCrossings.Describe(segs, excludeLastSegment: false);

        Assert.Equal("crossing runway 10L twice", clause);
        Assert.Equal(0, nonRunway);
    }

    [Fact]
    public void Describe_three_distinct_runways_preserves_taxi_order()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Seg(true, "runway 04L"), Seg(true, "runway 04R at C"), Seg(true, "runway 27"),
        };

        var (clause, nonRunway) = RouteRunwayCrossings.Describe(segs, excludeLastSegment: false);

        Assert.Equal("crossing runways 04L, 04R and 27", clause);
        Assert.Equal(0, nonRunway);
    }

    [Fact]
    public void Describe_mixed_label_shapes_and_non_runway_holds()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Seg(true, "runway 15R at N"), Seg(true, "D5, Runway 22R"),
            Seg(true, "end of taxiway B"), Seg(true, "A5"),
        };

        var (clause, nonRunway) = RouteRunwayCrossings.Describe(segs, excludeLastSegment: false);

        Assert.Equal("crossing runways 15R and 22R", clause);
        Assert.Equal(2, nonRunway);
    }

    [Fact]
    public void Describe_excludes_the_destination_truncation_tag_on_the_last_segment()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Seg(true, "runway 04L"), Seg(false, null), Seg(true, "Runway 33L"),
        };

        var (clause, nonRunway) = RouteRunwayCrossings.Describe(segs, excludeLastSegment: true);

        Assert.Equal("crossing runway 04L", clause);
        Assert.Equal(0, nonRunway);
    }

    [Fact]
    public void Describe_no_hold_shorts_yields_empty_clause()
    {
        var segs = new List<TaxiRouteSegment> { Seg(false, null), Seg(false, null) };

        var (clause, nonRunway) = RouteRunwayCrossings.Describe(segs, excludeLastSegment: false);

        Assert.Equal("", clause);
        Assert.Equal(0, nonRunway);
    }

    [Fact]
    public void Describe_merges_reciprocal_designators_as_one_pavement_speaking_both_names()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Seg(true, "runway 10L"), Seg(false, null), Seg(true, "runway 28R"),
        };

        var (clause, nonRunway) = RouteRunwayCrossings.Describe(segs, excludeLastSegment: false);

        Assert.Equal("crossing runway 10L/28R twice", clause);
        Assert.Equal(0, nonRunway);
    }

    [Fact]
    public void Describe_same_designator_crossings_keep_a_single_name()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Seg(true, "runway 10L"), Seg(false, null), Seg(true, "runway 10L"),
        };

        var (clause, nonRunway) = RouteRunwayCrossings.Describe(segs, excludeLastSegment: false);

        Assert.Equal("crossing runway 10L twice", clause);
        Assert.Equal(0, nonRunway);
    }

    [Fact]
    public void Describe_merges_unpadded_reciprocal_labels_with_the_padded_form()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Seg(true, "runway 9"), Seg(false, null), Seg(true, "runway 27"),
        };

        var (clause, nonRunway) = RouteRunwayCrossings.Describe(segs, excludeLastSegment: false);

        Assert.Equal("crossing runway 09/27 twice", clause);
        Assert.Equal(0, nonRunway);
    }
}
