// Characterization tests for MSFSBlindAssist.Navigation.TaxiLeadIn.
//
// This is characterization, not spec verification: values are derived by
// reasoning about the source and confirmed by running the tests; if a literal
// ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiLeadInTests
{
    private static TaxiRouteSegment Seg(string taxiway, double distM) => new TaxiRouteSegment
    {
        FromNode = new TaxiNode(),
        ToNode = new TaxiNode(),
        TaxiwayName = taxiway,
        DistanceMeters = distM,
    };

    // --- Extract ---------------------------------------------------------------

    [Fact]
    public void Extract_reports_no_lead_in_when_the_route_starts_on_the_cleared_taxiway()
    {
        var route = new TaxiRoute { Segments = { Seg("A", 100), Seg("A", 50) } };

        var info = TaxiLeadIn.Extract(route, "A");

        Assert.False(info.HasLeadIn);
        Assert.Equal(0, info.DistanceMeters);
        Assert.Empty(info.Taxiways);
    }

    [Fact]
    public void Extract_collects_the_leading_run_of_segments_before_the_cleared_taxiway()
    {
        var route = new TaxiRoute { Segments = { Seg("", 20), Seg("B", 30), Seg("A", 100) } };

        var info = TaxiLeadIn.Extract(route, "A");

        Assert.True(info.HasLeadIn);
        Assert.Equal(50, info.DistanceMeters);
        Assert.Equal(new[] { "B" }, info.Taxiways);
    }

    [Fact]
    public void Extract_dedups_consecutive_same_named_segments_but_keeps_repeats_after_a_break()
    {
        var route = new TaxiRoute
        {
            Segments =
            {
                Seg("B", 10), Seg("B", 10), Seg("C", 10), Seg("B", 10), Seg("A", 5),
            },
        };

        var info = TaxiLeadIn.Extract(route, "A");

        Assert.Equal(new[] { "B", "C", "B" }, info.Taxiways);
    }

    [Fact]
    public void Extract_treats_the_whole_route_as_lead_in_when_the_cleared_taxiway_never_appears()
    {
        var route = new TaxiRoute { Segments = { Seg("B", 10), Seg("C", 20) } };

        var info = TaxiLeadIn.Extract(route, "A");

        Assert.True(info.HasLeadIn);
        Assert.Equal(30, info.DistanceMeters);
        Assert.Equal(new[] { "B", "C" }, info.Taxiways);
    }

    // --- IsAcceptable ------------------------------------------------------------

    [Fact]
    public void IsAcceptable_rejects_when_a_fallback_reason_is_present()
    {
        Assert.False(TaxiLeadIn.IsAcceptable(50, 100, "no nodes found on taxiway"));
    }

    [Fact]
    public void IsAcceptable_allows_up_to_the_ratio_plus_pad_budget()
    {
        // gap=10 -> allowed = 10*2.5 + 300 = 325
        Assert.True(TaxiLeadIn.IsAcceptable(325.0, 10, null));
        Assert.False(TaxiLeadIn.IsAcceptable(325.1, 10, null));
    }

    // --- Clause --------------------------------------------------------------------

    [Fact]
    public void Clause_is_empty_when_there_is_no_lead_in()
    {
        var info = new TaxiLeadIn.LeadInInfo { HasLeadIn = false };
        Assert.Equal("", TaxiLeadIn.Clause(info, "A"));
    }

    [Fact]
    public void Clause_names_the_taxiway_directly_when_no_intermediate_taxiways_are_named()
    {
        var info = new TaxiLeadIn.LeadInInfo { HasLeadIn = true, Taxiways = Array.Empty<string>() };
        Assert.Equal(" First taxi onto A.", TaxiLeadIn.Clause(info, "A"));
    }

    [Fact]
    public void Clause_names_a_single_intermediate_taxiway()
    {
        var info = new TaxiLeadIn.LeadInInfo { HasLeadIn = true, Taxiways = new[] { "B" } };
        Assert.Equal(" First taxi via B to reach A.", TaxiLeadIn.Clause(info, "A"));
    }

    [Fact]
    public void Clause_lists_multiple_intermediate_taxiways_with_an_oxford_and()
    {
        var info = new TaxiLeadIn.LeadInInfo { HasLeadIn = true, Taxiways = new[] { "B", "C" } };
        Assert.Equal(" First taxi via B and C to reach A.", TaxiLeadIn.Clause(info, "A"));
    }
}
