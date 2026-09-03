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

    // --- ResolveCrossingHoldSegment ----------------------------------------
    //
    // Placement of a runway-crossing hold-short. The crossing is detected as the
    // edge straddling the runway CENTERLINE, so the node before it is routinely
    // ON the pavement — the resolver walks back to the scenery's own hold node.
    // Shape below is LEBL D5 over 24R (2026-08): 250 m → 105 m HSND → 51 m →
    // 21 m → centerline, with the hold node named after the nearer end (06L).

    private static TaxiRouteSegment Leg(
        double distanceM,
        TaxiNodeType endType = TaxiNodeType.Normal,
        string? endHoldName = null,
        bool alreadyHold = false) => new TaxiRouteSegment
        {
            FromNode = new TaxiNode(),
            ToNode = new TaxiNode { Type = endType, HoldShortName = endHoldName },
            DistanceMeters = distanceM,
            IsHoldShortPoint = alreadyHold,
        };

    [Fact]
    public void ResolveCrossingHoldSegment_walks_back_to_the_scenery_hold_node()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Leg(112),                                                            // 0: → 105 m out
            Leg(54, TaxiNodeType.HoldShort, "runway 06L at D5"),                  // 1: → HSND
            Leg(29),                                                             // 2: → 51 m out
            Leg(23),                                                             // 3: → 21 m out (old pick)
            Leg(23),                                                             // 4: crossing edge
        };

        // Either designator of the crossed pavement resolves to the same node.
        Assert.Equal(1, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 4, "24R"));
        Assert.Equal(1, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 4, "06L"));
    }

    [Fact]
    public void ResolveCrossingHoldSegment_accepts_an_ils_hold_node()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Leg(40, TaxiNodeType.ILSHoldShort, "runway 02 at S"),
            Leg(30),
            Leg(25),
        };

        Assert.Equal(0, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 2, "02"));
    }

    [Fact]
    public void ResolveCrossingHoldSegment_accepts_an_unnamed_hold_node()
    {
        var segs = new List<TaxiRouteSegment> { Leg(40, TaxiNodeType.HoldShort), Leg(30), Leg(25) };

        Assert.Equal(0, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 2, "24R"));
    }

    [Fact]
    public void ResolveCrossingHoldSegment_falls_back_when_there_is_no_hold_node()
    {
        var segs = new List<TaxiRouteSegment> { Leg(60), Leg(29), Leg(23), Leg(23) };

        Assert.Equal(2, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 3, "24R"));
    }

    [Fact]
    public void ResolveCrossingHoldSegment_falls_back_when_the_hold_node_is_beyond_the_lookback()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Leg(50, TaxiNodeType.HoldShort, "runway 24R"),   // 0: too far back
            Leg(200),                                        // 1: blows the 150 m budget
            Leg(23),                                         // 2
            Leg(23),                                         // 3: crossing edge
        };

        Assert.Equal(2, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 3, "24R"));
    }

    [Fact]
    public void ResolveCrossingHoldSegment_stops_at_a_hold_node_guarding_another_runway()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Leg(30, TaxiNodeType.HoldShort, "runway 24R at D5"),  // 0: the one we want…
            Leg(20, TaxiNodeType.HoldShort, "runway 02 at D5"),   // 1: …behind another runway's line
            Leg(23),                                             // 2
            Leg(23),                                             // 3: crossing edge
        };

        Assert.Equal(2, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 3, "24R"));
    }

    [Fact]
    public void ResolveCrossingHoldSegment_never_walks_through_an_existing_hold_short()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Leg(30, TaxiNodeType.HoldShort, "runway 24R"),
            Leg(20, alreadyHold: true),                           // another crossing's stop point
            Leg(23),
            Leg(23),                                             // crossing edge
        };

        Assert.Equal(2, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 3, "24R"));
    }

    [Fact]
    public void ResolveCrossingHoldSegment_always_returns_an_indexable_segment()
    {
        var segs = new List<TaxiRouteSegment> { Leg(23), Leg(23) };

        // Crossing on the first segment — there is nothing before it.
        Assert.Equal(0, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 0, "24R"));
        // Out-of-range index clamps into the route rather than handing the
        // caller an index that would throw on Segments[...].
        Assert.Equal(1, RouteRunwayCrossings.ResolveCrossingHoldSegment(segs, 99, "24R"));
        Assert.Equal(0, RouteRunwayCrossings.ResolveCrossingHoldSegment(
            new List<TaxiRouteSegment>(), 3, "24R"));
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

    // --- the extracted crossing/tagging loop -------------------------------------------

    private static TaxiRouteSegment Seg(double lat = 0.0) => new()
    {
        FromNode = new TaxiNode { NodeId = 1, Latitude = lat, Longitude = 0.0 },
        ToNode   = new TaxiNode { NodeId = 2, Latitude = lat + 0.001, Longitude = 0.0 },
        DistanceMeters = 100.0,
    };

    private static List<TaxiRouteSegment> Route(int count)
    {
        var list = new List<TaxiRouteSegment>();
        for (int i = 0; i < count; i++) list.Add(Seg(i * 0.001));
        return list;
    }

    // Probe that reports a crossing on the given segment indices only.
    private static RouteRunwayCrossings.EdgeRunwayProbe ProbeOn(
        IReadOnlyList<TaxiRouteSegment> segs, params (int Index, string Runway)[] hits)
        => (aLat, aLon, bLat, bLon) =>
        {
            for (int i = 0; i < segs.Count; i++)
                if (Math.Abs(segs[i].FromNode.Latitude - aLat) < 1e-12)
                    foreach (var h in hits)
                        if (h.Index == i) return h.Runway;
            return "";
        };

    private static bool NeverMatches(string a, string b) => false;

    [Fact]
    public void InsertCrossingHoldShorts_TagsTheSegmentBeforeEachCrossing()
    {
        var segs = Route(10);
        var crossed = RouteRunwayCrossings.InsertCrossingHoldShorts(
            segs, destinationName: "",
            ProbeOn(segs, (3, "26R"), (6, "04L")), NeverMatches);

        Assert.Equal(new[] { "26R", "04L" }, crossed);
        Assert.True(segs[2].IsHoldShortPoint, "hold must land BEFORE the crossing edge");
        Assert.True(segs[5].IsHoldShortPoint);
        Assert.False(segs[3].IsHoldShortPoint, "the crossing edge itself is not the hold");
    }

    [Fact]
    public void InsertCrossingHoldShorts_LabelsTheHoldWithTheRunwayCrossed()
    {
        var segs = Route(6);
        RouteRunwayCrossings.InsertCrossingHoldShorts(
            segs, destinationName: "", ProbeOn(segs, (3, "26R")), NeverMatches);

        Assert.Equal("runway 26R", segs[2].HoldShortRunway);
    }

    [Fact]
    public void InsertCrossingHoldShorts_DoesNotRetagTheSameRunwayOnConsecutiveEdges()
    {
        var segs = Route(8);
        var crossed = RouteRunwayCrossings.InsertCrossingHoldShorts(
            segs, destinationName: "",
            ProbeOn(segs, (3, "26R"), (4, "26R")), NeverMatches);

        Assert.Equal(new[] { "26R" }, crossed);
    }

    // The destination's own strip: only the route's ARRIVAL at it is skipped (TruncateToHoldShort
    // already tagged the final segment). Every earlier crossing of that same pavement is tagged —
    // dropping it is the runway-incursion direction.
    [Fact]
    public void InsertCrossingHoldShorts_SkipsOnlyTheFinalSegmentOnTheDestinationStrip()
    {
        var segs = Route(6);
        var crossed = RouteRunwayCrossings.InsertCrossingHoldShorts(
            segs, destinationName: "Runway 04R",
            ProbeOn(segs, (5, "22L")),                       // final segment, reciprocal name
            designatorsMatch: (a, b) => true);

        Assert.Empty(crossed);
        Assert.False(segs[4].IsHoldShortPoint);
    }

    [Fact]
    public void InsertCrossingHoldShorts_TagsAnEarlierCrossingOfTheDestinationStrip()
    {
        var segs = Route(8);
        var crossed = RouteRunwayCrossings.InsertCrossingHoldShorts(
            segs, destinationName: "Runway 04R",
            ProbeOn(segs, (3, "22L")),                       // mid-route, reciprocal name
            designatorsMatch: (a, b) => true);

        Assert.Equal(new[] { "22L" }, crossed);
        // Announced under the designator the PILOT chose, not the reciprocal geometry reported.
        Assert.Equal("runway 04R", segs[2].HoldShortRunway);
    }

    [Fact]
    public void InsertCrossingHoldShorts_IsANoOpForARouteTooShortToCross()
    {
        var segs = Route(1);
        Assert.Empty(RouteRunwayCrossings.InsertCrossingHoldShorts(
            segs, "", ProbeOn(segs, (0, "26R")), NeverMatches));
    }

    // A null delegate must FAIL LOUDLY, not degrade to "no crossings found". This pass is the
    // FAA AIM 4-3-18 / ICAO Doc 4444 hold-short before every crossed runway; an empty result
    // is indistinguishable from a route that genuinely crosses nothing, so a silent return
    // would present a programming error as a safe route. `segments` is deliberately NOT in
    // this rule — an empty route legitimately crosses nothing.
    [Fact]
    public void InsertCrossingHoldShorts_ThrowsRatherThanReportNoCrossings_WhenTheProbeIsNull()
    {
        var segs = Route(4);
        Assert.Throws<ArgumentNullException>(() =>
            RouteRunwayCrossings.InsertCrossingHoldShorts(segs, "", null!, NeverMatches));
    }

    [Fact]
    public void InsertCrossingHoldShorts_ThrowsRatherThanReportNoCrossings_WhenTheMatchIsNull()
    {
        var segs = Route(4);
        Assert.Throws<ArgumentNullException>(() =>
            RouteRunwayCrossings.InsertCrossingHoldShorts(
                segs, "", ProbeOn(segs, (2, "26R")), null!));
    }

    // The lenient contract for segments is unchanged: a too-short or absent route is not an
    // error, it simply crosses nothing.
    [Fact]
    public void InsertCrossingHoldShorts_StillToleratesANullRouteWithoutThrowing()
        => Assert.Empty(RouteRunwayCrossings.InsertCrossingHoldShorts(
            null!, "", (a, b, c, d) => "", NeverMatches));

    // --- ShouldExcludeFinalHold ---------------------------------------------------------
    //
    // Both describers of a route — LoadRoute's summary and the recalc's "Route changed"
    // callout — must agree on whether the FINAL segment's hold is a real crossing or just the
    // destination's own countdown rail. The rule was spelled out twice, once in each; one
    // owner here is what makes "the two paths cannot drift" true rather than merely intended.

    [Fact]
    public void ShouldExcludeFinalHold_ExcludesARunwayRoutesOwnTaggedFinalSegment()
    {
        var segs = Route(3);
        segs[^1].IsHoldShortPoint = true;
        Assert.True(RouteRunwayCrossings.ShouldExcludeFinalHold(segs, isRunwayDestination: true));
    }

    // A gate route never runs TruncateToHoldShort, so a hold-short on its last segment is a
    // genuine crossing and must be described.
    [Fact]
    public void ShouldExcludeFinalHold_KeepsAGateRoutesTaggedFinalSegment()
    {
        var segs = Route(3);
        segs[^1].IsHoldShortPoint = true;
        Assert.False(RouteRunwayCrossings.ShouldExcludeFinalHold(segs, isRunwayDestination: false));
    }

    [Fact]
    public void ShouldExcludeFinalHold_IsFalseWhenTheFinalSegmentCarriesNoHold()
        => Assert.False(RouteRunwayCrossings.ShouldExcludeFinalHold(
            Route(3), isRunwayDestination: true));

    [Fact]
    public void ShouldExcludeFinalHold_IsFalseForAnEmptyRoute()
        => Assert.False(RouteRunwayCrossings.ShouldExcludeFinalHold(
            Array.Empty<TaxiRouteSegment>(), isRunwayDestination: true));
}
