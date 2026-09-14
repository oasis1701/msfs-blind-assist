// Exact-string tests for RouteReachabilityMessages — the only new speech the route reachability
// change adds. A blind pilot hears these once, so the wording is pinned letter for letter.
// Distances are formatted by the caller's formatter (the manager passes its own ground-distance
// formatter); these tests pass explicit metres and feet formatters so no global unit setting is
// touched.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RouteReachabilityMessagesTests
{
    private static readonly Func<double, string> Metres = m => $"{Math.Round(m)} metres";
    private static readonly Func<double, string> Feet = m => $"{Math.Round(m * 3.28084)} feet";

    [Fact]
    public void The_destination_refusal_names_the_destination()
    {
        Assert.Equal(
            "No taxi route to Parking 40. It isn't connected to the taxiway network you're on.",
            RouteReachabilityMessages.DestinationNotConnected("Parking 40"));
    }

    [Fact]
    public void The_runway_refusal_names_the_runway()
    {
        Assert.Equal(
            "No taxi route from here. You aren't on the connected taxiway network, and the way onto it crosses runway 13.",
            RouteReachabilityMessages.FirstLegCrossesRunway("13"));
    }

    [Fact]
    public void The_unmapped_leg_warning_names_the_first_taxiway_in_metres()
    {
        Assert.Equal(
            "Your position isn't connected to the taxiway network. The first 230 metres to taxiway B aren't mapped.",
            RouteReachabilityMessages.UnmappedFirstLeg(230, Metres, "B"));
    }

    [Fact]
    public void The_unmapped_leg_warning_uses_the_callers_unit()
    {
        Assert.Equal(
            "Your position isn't connected to the taxiway network. The first 755 feet to taxiway B aren't mapped.",
            RouteReachabilityMessages.UnmappedFirstLeg(230, Feet, "B"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_a_named_taxiway_the_warning_speaks_of_the_route(string? name)
    {
        Assert.Equal(
            "Your position isn't connected to the taxiway network. The first 230 metres of the route aren't mapped.",
            RouteReachabilityMessages.UnmappedFirstLeg(230, Metres, name));
    }

    [Fact]
    public void The_recalculation_refusals_lead_with_off_route()
    {
        Assert.Equal(
            "Off route. Unable to recalculate. Parking 40 isn't connected to the taxiway network you're on.",
            RouteReachabilityMessages.RecalculationRefusedDestination("Parking 40"));
        Assert.Equal(
            "Off route. Unable to recalculate. The way onto the connected taxiway network crosses runway 13.",
            RouteReachabilityMessages.RecalculationRefusedRunway("13"));
    }

    [Fact]
    public void Start_speech_puts_the_warning_before_the_turn_cue_in_one_utterance()
    {
        Assert.Equal("W. C.", RouteReachabilityMessages.JoinStartSpeech("W.", "C."));
        Assert.Equal("W.", RouteReachabilityMessages.JoinStartSpeech("W.", null));
        Assert.Equal("C.", RouteReachabilityMessages.JoinStartSpeech(null, "C."));
        Assert.Equal("C.", RouteReachabilityMessages.JoinStartSpeech("", "C."));
        Assert.Null(RouteReachabilityMessages.JoinStartSpeech(null, ""));
    }

    [Fact]
    public void The_destination_runway_refusal_names_the_destination_and_the_runway()
    {
        Assert.Equal(
            "No taxi route to Parking 40. It isn't connected to the taxiway network you're on, and the way to it crosses runway 13.",
            RouteReachabilityMessages.DestinationLegCrossesRunway("Parking 40", "13"));
    }

    [Fact]
    public void The_unmapped_leg_to_a_destination_names_the_destination_in_the_callers_unit()
    {
        Assert.Equal(
            "C 51L isn't connected to the taxiway network you're on. The first 35 metres of the route aren't mapped.",
            RouteReachabilityMessages.UnmappedLegToDestination("C 51L", 35, Metres));
        Assert.Equal(
            "C 51L isn't connected to the taxiway network you're on. The first 115 feet of the route aren't mapped.",
            RouteReachabilityMessages.UnmappedLegToDestination("C 51L", 35, Feet));
    }

    [Fact]
    public void The_recalculation_refusal_for_a_destination_across_a_runway_leads_with_off_route()
    {
        Assert.Equal(
            "Off route. Unable to recalculate. The way to Parking 40 crosses runway 13.",
            RouteReachabilityMessages.RecalculationRefusedDestinationRunway("Parking 40", "13"));
    }

    [Theory]
    [InlineData("B 18R - Gate Heavy, Jetway", "B 18R")]
    [InlineData("A 24A - Gate Medium, also A24 (online)", "A 24A")]
    [InlineData("A-9", "A-9")]
    [InlineData("Runway 27L", "Runway 27L")]
    public void A_stand_is_named_by_its_identifier_not_its_whole_label(string label, string spoken)
    {
        Assert.Equal(spoken, RouteReachabilityMessages.SpokenDestinationName(label));
    }

    [Fact]
    public void Every_destination_sentence_speaks_the_identifier_only()
    {
        const string label = "B 18R - Gate Heavy, Jetway";
        Assert.Equal("No taxi route to B 18R. It isn't connected to the taxiway network you're on.",
            RouteReachabilityMessages.DestinationNotConnected(label));
        Assert.Equal("No taxi route to B 18R. It isn't connected to the taxiway network you're on, and the way to it crosses runway 13.",
            RouteReachabilityMessages.DestinationLegCrossesRunway(label, "13"));
        Assert.Equal("B 18R isn't connected to the taxiway network you're on. The first 35 metres of the route aren't mapped.",
            RouteReachabilityMessages.UnmappedLegToDestination(label, 35, Metres));
        Assert.Equal("Off route. Unable to recalculate. B 18R isn't connected to the taxiway network you're on.",
            RouteReachabilityMessages.RecalculationRefusedDestination(label));
        Assert.Equal("Off route. Unable to recalculate. The way to B 18R crosses runway 13.",
            RouteReachabilityMessages.RecalculationRefusedDestinationRunway(label, "13"));
    }
}
