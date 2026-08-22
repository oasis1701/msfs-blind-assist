// Characterization tests for MSFSBlindAssist.Services.GateSearchFilter.
//
// No dedicated probe exists; cases derived by reading Normalize/NormalizeIdentity/
// NormalizeGateName/Matches/Filter (identity = Name+Number+Suffix with whitespace
// stripped, StandTypeWords dropped from online names, and alias matching) and confirmed
// by running the tests. This is characterization, not spec verification: if a literal
// ever disagrees with actual output, the test must be corrected to match real output,
// not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class GateSearchFilterTests
{
    private static ParkingSpot Spot(string name, int number, string suffix = "", params string[] aliases)
        => new ParkingSpot { Name = name, Number = number, Suffix = suffix, Aliases = aliases.ToList() };

    // --- Normalize ------------------------------------------------------------------

    [Fact]
    public void Normalize_strips_whitespace_and_uppercases()
    {
        Assert.Equal("ABC12", GateSearchFilter.Normalize(" a b c 1 2 "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Normalize_null_or_empty_yields_empty(string? raw)
    {
        Assert.Equal("", GateSearchFilter.Normalize(raw));
    }

    // --- NormalizeIdentity ------------------------------------------------------------

    [Fact]
    public void NormalizeIdentity_concatenates_name_number_and_suffix()
    {
        Assert.Equal("C18L", GateSearchFilter.NormalizeIdentity(Spot("C", 18, "L")));
    }

    [Fact]
    public void NormalizeIdentity_omits_number_when_zero()
    {
        Assert.Equal("C", GateSearchFilter.NormalizeIdentity(Spot("C", 0)));
    }

    // --- NormalizeGateName / StandTypeWords ---------------------------------------

    [Fact]
    public void NormalizeGateName_strips_a_type_qualifier_word()
    {
        Assert.Equal("H2", GateSearchFilter.NormalizeGateName("Ramp H2"));
    }

    [Fact]
    public void NormalizeGateName_strips_multiple_type_qualifier_words()
    {
        Assert.Equal("12", GateSearchFilter.NormalizeGateName("Stand 12 Apron"));
    }

    [Fact]
    public void NormalizeGateName_preserves_GA_as_a_real_concourse_designator()
    {
        // Deliberately NOT in StandTypeWords -- some airports have a real GA-apron concourse.
        Assert.Equal("GA5", GateSearchFilter.NormalizeGateName("GA 5"));
    }

    [Fact]
    public void NormalizeGateName_of_a_bare_type_word_alone_is_empty()
    {
        Assert.Equal("", GateSearchFilter.NormalizeGateName("Ramp"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeGateName_blank_input_yields_empty(string? raw)
    {
        Assert.Equal("", GateSearchFilter.NormalizeGateName(raw));
    }

    // --- Matches ------------------------------------------------------------------

    [Fact]
    public void Matches_empty_query_matches_everything()
    {
        Assert.True(GateSearchFilter.Matches(Spot("C", 18), ""));
        Assert.True(GateSearchFilter.Matches(Spot("C", 18), null));
    }

    [Fact]
    public void Matches_a_substring_of_the_identity()
    {
        Assert.True(GateSearchFilter.Matches(Spot("C", 18, "L"), "18"));
    }

    [Fact]
    public void Matches_is_case_insensitive_and_whitespace_insensitive()
    {
        Assert.True(GateSearchFilter.Matches(Spot("C", 18), " c 1 "));
    }

    [Fact]
    public void Matches_falls_through_to_an_alias()
    {
        // Real ATC gate "B04" aliased onto navdata "B 6" -- typing the alias must find it.
        var spot = Spot("B", 6, "", "B04");
        Assert.True(GateSearchFilter.Matches(spot, "B04"));
    }

    [Fact]
    public void Matches_returns_false_when_neither_identity_nor_aliases_contain_the_query()
    {
        var spot = Spot("B", 6, "", "B04");
        Assert.False(GateSearchFilter.Matches(spot, "ZZZ"));
    }

    [Fact]
    public void Matches_handles_a_null_alias_list_without_throwing()
    {
        var spot = Spot("B", 6);
        spot.Aliases = null!;
        Assert.False(GateSearchFilter.Matches(spot, "ZZZ"));
    }

    // --- Filter ---------------------------------------------------------------------

    [Fact]
    public void Filter_returns_every_spot_for_an_empty_query()
    {
        var spots = new List<ParkingSpot> { Spot("A", 1), Spot("B", 2) };
        Assert.Equal(2, GateSearchFilter.Filter(spots, "").Count);
    }

    [Fact]
    public void Filter_keeps_only_matching_spots()
    {
        var spots = new List<ParkingSpot> { Spot("A", 1), Spot("B", 2) };
        var result = GateSearchFilter.Filter(spots, "A1");
        var spot = Assert.Single(result);
        Assert.Equal("A", spot.Name);
    }
}
