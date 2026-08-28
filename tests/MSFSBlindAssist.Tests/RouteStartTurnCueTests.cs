// The route-start turn cue's wording, lifted verbatim out of TaxiGuidanceManager so it has
// exactly ONE owner and can be spoken from two places without the two ever disagreeing.
//
// Live KATL 2026-08-27: the SayIntentions import spoke its summary (destination, applied
// taxiways, hold short of runway 08L), and 50 ms later the first position frame fired this
// cue as an INTERRUPTING announcement, cutting the summary off mid-word. The pilot got a
// fragment of a sentence and then a bare "Make a U-turn to the left" -- taxiway-less,
// because _lastAnnouncedTaxiway is reset by LoadRoute and still empty on that frame.

using MSFSBlindAssist.Navigation;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class RouteStartTurnCueTests
{
    [Fact]
    public void No_cue_below_the_sharp_turn_threshold()
    {
        Assert.Null(RouteStartTurnCue.Compose(99.9, "H"));
        Assert.Null(RouteStartTurnCue.Compose(-99.9, "H"));
        Assert.Null(RouteStartTurnCue.Compose(0.0, "H"));
    }

    [Fact]
    public void A_sharp_turn_between_100_and_135_names_the_taxiway()
    {
        Assert.Equal("Sharp turn right onto taxiway H.", RouteStartTurnCue.Compose(120.0, "H"));
        Assert.Equal("Sharp turn left onto taxiway H.", RouteStartTurnCue.Compose(-120.0, "H"));
    }

    [Fact]
    public void At_or_above_135_it_is_a_turnaround()
    {
        Assert.Equal("Taxiway H is behind you. Turn left to come around.",
            RouteStartTurnCue.Compose(-135.0, "H"));
    }

    [Fact]
    public void The_live_KATL_value_is_a_left_turnaround_naming_the_taxiway()
    {
        // raw = -175.78 on the first frame of the imported route.
        Assert.Equal("Taxiway H is behind you. Turn left to come around.",
            RouteStartTurnCue.Compose(-175.78, "H"));
    }

    [Fact]
    public void Both_boundaries_are_inclusive()
    {
        Assert.Equal("Sharp turn right onto taxiway H.", RouteStartTurnCue.Compose(100.0, "H"));
        Assert.Equal("Taxiway H is behind you. Turn right to come around.",
            RouteStartTurnCue.Compose(135.0, "H"));
    }

    [Fact]
    public void Without_a_taxiway_name_it_falls_back_in_both_bands()
    {
        Assert.Equal("Sharp turn left.", RouteStartTurnCue.Compose(-110.0, null));
        Assert.Equal("Sharp turn left.", RouteStartTurnCue.Compose(-110.0, "  "));
        Assert.Equal("Make a U-turn to the left.", RouteStartTurnCue.Compose(-175.78, null));
    }
}
