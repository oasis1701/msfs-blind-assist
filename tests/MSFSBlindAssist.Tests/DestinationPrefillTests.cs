// Tests for MSFSBlindAssist.Navigation.DestinationPrefill: what the Select Destination
// Runway window opens with.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class DestinationPrefillTests
{
    [Fact]
    public void Nothing_known_leaves_the_box_empty()
    {
        var p = DestinationPrefill.Choose(null, null, "", "  ");
        Assert.Null(p.Icao);
        Assert.Null(p.RunwayId);
    }

    [Fact]
    public void Flight_plan_arrival_fills_the_airport()
    {
        var p = DestinationPrefill.Choose("kjfk ", "", null, null);
        Assert.Equal("KJFK", p.Icao);
        Assert.Null(p.RunwayId);
    }

    [Fact]
    public void Existing_destination_is_used_when_there_is_no_flight_plan()
    {
        var p = DestinationPrefill.Choose("", "", "EGLL", "27L");
        Assert.Equal("EGLL", p.Icao);
        Assert.Equal("27L", p.RunwayId);
    }

    [Fact]
    public void Flight_plan_beats_a_destination_left_over_from_the_previous_leg()
    {
        var p = DestinationPrefill.Choose("KBOS", "22L", "KJFK", "31R");
        Assert.Equal("KBOS", p.Icao);
        Assert.Equal("22L", p.RunwayId);
    }

    [Fact]
    public void A_runway_from_another_airport_is_never_carried_over()
    {
        var p = DestinationPrefill.Choose("KBOS", "", "KJFK", "31R");
        Assert.Equal("KBOS", p.Icao);
        Assert.Null(p.RunwayId);
    }

    [Fact]
    public void Same_airport_prefers_the_runway_already_picked()
    {
        var p = DestinationPrefill.Choose("KJFK", "22L", "KJFK", "31R");
        Assert.Equal("KJFK", p.Icao);
        Assert.Equal("31R", p.RunwayId);
    }

    [Theory]
    [InlineData("06L", "6L", true)]
    [InlineData("RW06L", "06L", true)]
    [InlineData("27l", "27L", true)]
    [InlineData("27L", "27R", false)]
    [InlineData("1", "10", false)]
    [InlineData("27L", null, false)]
    public void Runway_match_ignores_padding_case_and_prefix(string prefill, string? listed, bool expected)
    {
        var p = new DestinationPrefill("KJFK", prefill);
        Assert.Equal(expected, p.MatchesRunway(listed));
    }

    [Fact]
    public void No_prefill_runway_matches_nothing()
    {
        Assert.False(new DestinationPrefill("KJFK", null).MatchesRunway("27L"));
    }
}
