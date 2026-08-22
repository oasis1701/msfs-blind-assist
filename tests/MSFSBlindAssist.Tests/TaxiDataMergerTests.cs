// Characterization tests for MSFSBlindAssist.Services.TaxiAugment.TaxiDataMerger.
//
// No dedicated probe exists; cases derived by reading MergeNamesOntoNavData/BestMatchName
// (the anti-grass invariants: navdata names are NEVER overwritten, online data only fills
// UNNAMED segments, online-only geometry with no navdata match is IGNORED, and the
// ambiguity guard refuses a guess when two differently-named online segments are both
// within tolerance) and confirmed by running the tests. This is characterization, not
// spec verification: if a literal ever disagrees with actual output, the test must be
// corrected to match real output, not the other way around.

using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class TaxiDataMergerTests
{
    private static MergeOptions DefaultOpt() => new();

    private static AirportTaxiData Source(string kind, params (string name, double lat1, double lon1, double lat2, double lon2)[] segs)
    {
        var data = new AirportTaxiData { Source = kind };
        foreach (var s in segs)
            data.Taxiways.Add(new NamedTaxiSegment { Name = s.name, Lat1 = s.lat1, Lon1 = s.lon1, Lat2 = s.lat2, Lon2 = s.lon2 });
        return data;
    }

    // --- NormalizeTaxiwayName -----------------------------------------------------

    [Theory]
    [InlineData("twy k 2", "K2")]
    [InlineData("TAXIWAY K2", "K2")]
    [InlineData("K 2", "K2")]
    [InlineData("  k2  ", "K2")]
    public void NormalizeTaxiwayName_strips_prefix_spaces_and_uppercases(string raw, string expected)
    {
        Assert.Equal(expected, TaxiDataMerger.NormalizeTaxiwayName(raw));
    }

    [Fact]
    public void NormalizeTaxiwayName_does_not_collapse_a_bare_TWY_named_taxiway_to_empty()
    {
        // A taxiway literally named "TWY" must not become "" -- that would compare equal to
        // every other empty/prefix-only name and defeat the ambiguity guard.
        Assert.Equal("TWY", TaxiDataMerger.NormalizeTaxiwayName("TWY"));
        Assert.Equal("TAXIWAY", TaxiDataMerger.NormalizeTaxiwayName("TAXIWAY"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeTaxiwayName_blank_input_yields_empty(string? raw)
    {
        Assert.Equal("", TaxiDataMerger.NormalizeTaxiwayName(raw!));
    }

    // --- Anti-grass invariant: navdata names are NEVER overwritten ----------------

    [Fact]
    public void An_already_named_navdata_segment_keeps_its_name_even_when_an_online_segment_disagrees()
    {
        // Navdata segment named "K2" runs along the same line as an online segment named
        // "KILO2" -- the navdata name must survive unchanged; "KILO2" becomes an alias, not
        // a replacement name.
        var nav = new List<NavSegment> { new NavSegment("K2", 0, 0, 0, 0.001) };
        var aptdat = Source("aptdat", ("KILO2", 0, 0, 0, 0.001));

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { aptdat }, DefaultOpt(), "TEST", out var coverage);

        var seg = Assert.Single(result);
        Assert.Equal("K2", seg.Name);
        Assert.Contains("KILO2", seg.Aliases);
        Assert.Equal(1, coverage.AliasesAdded);
    }

    [Fact]
    public void A_named_segment_with_no_matching_online_alias_gets_no_alias()
    {
        var nav = new List<NavSegment> { new NavSegment("K2", 0, 0, 0, 0.001) };
        var aptdat = Source("aptdat"); // no segments at all

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { aptdat }, DefaultOpt(), "TEST", out var coverage);

        var seg = Assert.Single(result);
        Assert.Equal("K2", seg.Name);
        Assert.Empty(seg.Aliases);
        Assert.Equal(0, coverage.AliasesAdded);
    }

    [Fact]
    public void An_online_name_that_normalizes_the_same_as_navdata_is_not_recorded_as_an_alias()
    {
        // "twy k2" normalizes to "K2", same as the navdata canonical name -- not a useful alias.
        var nav = new List<NavSegment> { new NavSegment("K2", 0, 0, 0, 0.001) };
        var aptdat = Source("aptdat", ("twy k2", 0, 0, 0, 0.001));

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { aptdat }, DefaultOpt(), "TEST", out var coverage);

        var seg = Assert.Single(result);
        Assert.Empty(seg.Aliases);
        Assert.Equal(0, coverage.AliasesAdded);
    }

    // --- Anti-grass invariant: online data only fills UNNAMED segments ------------

    [Fact]
    public void An_unnamed_navdata_segment_adopts_a_matching_online_name()
    {
        var nav = new List<NavSegment> { new NavSegment("", 0, 0, 0, 0.001) };
        var aptdat = Source("aptdat", ("HAWKER", 0, 0, 0, 0.001));

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { aptdat }, DefaultOpt(), "TEST", out var coverage);

        var seg = Assert.Single(result);
        Assert.Equal("HAWKER", seg.Name);
        Assert.Equal(1, coverage.NamesAdoptedFromAptDat);
        Assert.Equal(1, coverage.NavUnnamedSegments);
    }

    [Fact]
    public void An_unnamed_segment_with_no_matching_online_geometry_stays_unnamed()
    {
        // Online-only taxiways with no matching navdata geometry are IGNORED: a far-away
        // online segment must never be adopted.
        var nav = new List<NavSegment> { new NavSegment("", 0, 0, 0, 0.0001) };
        var aptdat = Source("aptdat", ("FARAWAY", 10, 10, 10, 10.0001)); // nowhere near

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { aptdat }, DefaultOpt(), "TEST", out var coverage);

        var seg = Assert.Single(result);
        Assert.Equal("", seg.Name);
        Assert.Equal(0, coverage.NamesAdoptedFromAptDat);
    }

    [Fact]
    public void Unnamed_segment_prefers_aptdat_over_osm_on_disagreement()
    {
        var nav = new List<NavSegment> { new NavSegment("", 0, 0, 0, 0.001) };
        var osm = Source("osm", ("OSMNAME", 0, 0, 0, 0.001));
        var aptdat = Source("aptdat", ("APTNAME", 0, 0, 0, 0.001));

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { osm, aptdat }, DefaultOpt(), "TEST", out var coverage);

        var seg = Assert.Single(result);
        Assert.Equal("APTNAME", seg.Name);
        Assert.Equal(1, coverage.OsmAptDatDisagreements);
        Assert.Equal(1, coverage.NamesAdoptedFromAptDat);
        Assert.Equal(0, coverage.NamesAdoptedFromOsm);
    }

    [Fact]
    public void Unnamed_segment_adopts_the_agreed_name_and_counts_it_as_OSM_when_both_sources_agree()
    {
        var nav = new List<NavSegment> { new NavSegment("", 0, 0, 0, 0.001) };
        var osm = Source("osm", ("SAMENAME", 0, 0, 0, 0.001));
        var aptdat = Source("aptdat", ("SAMENAME", 0, 0, 0, 0.001));

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { osm, aptdat }, DefaultOpt(), "TEST", out var coverage);

        var seg = Assert.Single(result);
        Assert.Equal("SAMENAME", seg.Name);
        Assert.Equal(1, coverage.NamesAdoptedFromOsm);
        Assert.Equal(0, coverage.OsmAptDatDisagreements);
    }

    [Fact]
    public void Ambiguity_guard_refuses_to_adopt_when_two_differently_named_candidates_are_both_in_tolerance()
    {
        // Two parallel taxiways with different names both close to the navdata midpoint ->
        // a miss is safer than a wrong name, so nothing is adopted.
        var nav = new List<NavSegment> { new NavSegment("", 0, 0, 0, 0.001) };
        var aptdat = Source("aptdat",
            ("ALPHA", 0, 0, 0, 0.001),
            ("BRAVO", 0.00001, 0, 0.00001, 0.001)); // near-identical distance, different name

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { aptdat }, DefaultOpt(), "TEST", out var coverage);

        var seg = Assert.Single(result);
        Assert.Equal("", seg.Name);
        Assert.Equal(0, coverage.NamesAdoptedFromAptDat);
    }

    [Fact]
    public void Bearing_gate_rejects_a_geometrically_close_but_wrongly_oriented_online_segment()
    {
        // Navdata segment runs east-west; the online segment at the SAME location runs
        // north-south (90 deg off) -- must be rejected by the bearing gate even though it's
        // geometrically coincident.
        var nav = new List<NavSegment> { new NavSegment("", 0, 0, 0, 0.001) }; // east-west
        var aptdat = Source("aptdat", ("CROSS", -0.0005, 0.0005, 0.0005, 0.0005)); // north-south

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { aptdat }, DefaultOpt(), "TEST", out _);

        var seg = Assert.Single(result);
        Assert.Equal("", seg.Name);
    }

    [Fact]
    public void MergeNamesOntoNavData_never_mutates_the_input_list_length_or_order()
    {
        var nav = new List<NavSegment>
        {
            new NavSegment("A", 0, 0, 0, 0.001),
            new NavSegment("", 1, 1, 1, 1.001),
        };
        var aptdat = Source("aptdat", ("B", 1, 1, 1, 1.001));

        var result = TaxiDataMerger.MergeNamesOntoNavData(
            nav, new[] { aptdat }, DefaultOpt(), "TEST", out _);

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0].Name);
        Assert.Equal("B", result[1].Name);
    }
}
