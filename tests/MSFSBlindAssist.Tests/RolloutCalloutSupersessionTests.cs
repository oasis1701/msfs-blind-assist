// "Will this rollout callout come due while a one-shot sentence is still being spoken?" — the rule
// shared by the crossing-decline utterance and the touchdown runway correction. When it says yes,
// the caller marks the callout as already made and folds whatever it uniquely adds into the
// sentence, so the callout cannot cut the sentence off.
//
// PR #236 follow-up. The rule converted the sentence's length into a distance at a CONSTANT ground
// speed, on the one phase of flight that decelerates hardest. At touchdown speed that overstates
// how far the aircraft travels by hundreds of feet, so callouts were written off that the pilot
// only reached well after the sentence had finished — and because each approach callout only fires
// inside its own distance band, a callout written off early is never heard at all. Worst case on a
// wrong-runway landing: the exit is 2,600 ft ahead at 140 kt, all three approach calls are written
// off, and the pilot gets one distance at touchdown and then silence until "turn now".
//
// The reach now decelerates at the same comfortable rate the exit re-plan assumes.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RolloutCalloutSupersessionTests
{
    private const double TouchdownKts = 140.0;
    private const double CorrectionLeadSec = 9.0;   // ROLLOUT_TOUCHDOWN_CORRECTION_LEAD_SEC

    [Fact]
    public void A_callout_the_aircraft_is_already_inside_is_always_superseded()
    {
        Assert.True(RolloutCalloutSupersession.Supersedes(500.0, 500.0, TouchdownKts, CorrectionLeadSec));
        Assert.True(RolloutCalloutSupersession.Supersedes(120.0, 500.0, 0.0, CorrectionLeadSec));
    }

    [Fact]
    public void A_stopped_aircraft_supersedes_nothing_ahead_of_it()
        => Assert.False(RolloutCalloutSupersession.Supersedes(900.0, 500.0, 0.0, CorrectionLeadSec));

    [Fact]
    public void A_sentence_with_no_length_supersedes_nothing_ahead_of_it()
        => Assert.False(RolloutCalloutSupersession.Supersedes(900.0, 500.0, TouchdownKts, 0.0));

    [Fact]
    public void The_reach_allows_for_braking_rather_than_holding_touchdown_speed()
    {
        // 140 kt is 236.3 ft/s. Held flat for 9 s that is 2,127 ft; slowing at the comfortable
        // 2.0 m/s the aircraft actually covers about 1,861 ft.
        double reach = RolloutCalloutSupersession.ReachFeet(TouchdownKts, CorrectionLeadSec);

        Assert.InRange(reach, 1830.0, 1890.0);
        Assert.True(reach < TouchdownKts * 1.6878 * CorrectionLeadSec);
    }

    [Fact]
    public void An_aircraft_already_at_taxi_speed_is_not_treated_as_braking()
    {
        // The crossing-decline sentence is spoken anywhere from a standstill to 90 kt. At 22 kt
        // the aircraft is taxiing, not shedding energy, so its reach is simply speed times time —
        // assuming heavy braking here would understate it and write off callouts too readily.
        double reach = RolloutCalloutSupersession.ReachFeet(22.0, 4.0);

        Assert.Equal(22.0 * 1.6878 * 4.0, reach, 1.0);
    }

    [Fact]
    public void Braking_stops_at_taxi_speed_rather_than_running_down_to_a_standstill()
    {
        // 60 kt for 9 s: the aircraft slows to 30 kt after about 7.7 s and then holds it, so it
        // covers roughly 650 ft — not the 911 ft of holding 60 kt, nor the 646 ft of braking all
        // the way to a stop.
        double reach = RolloutCalloutSupersession.ReachFeet(60.0, 9.0);

        Assert.InRange(reach, 630.0, 680.0);
    }

    [Fact]
    public void The_reach_grows_with_speed_and_with_the_length_of_the_sentence()
    {
        Assert.True(RolloutCalloutSupersession.ReachFeet(140.0, 9.0)
                  > RolloutCalloutSupersession.ReachFeet(60.0, 9.0));
        Assert.True(RolloutCalloutSupersession.ReachFeet(140.0, 9.0)
                  > RolloutCalloutSupersession.ReachFeet(140.0, 4.0));
    }

    // The case that motivated the change: a re-planned exit 2,600 ft ahead at touchdown speed.
    [Fact]
    public void The_500_foot_call_survives_a_wrong_runway_touchdown_2600_feet_from_the_exit()
    {
        const double ToExit = 2600.0;

        Assert.True(RolloutCalloutSupersession.Supersedes(ToExit, 1500.0, TouchdownKts, CorrectionLeadSec));
        Assert.True(RolloutCalloutSupersession.Supersedes(ToExit, 900.0, TouchdownKts, CorrectionLeadSec));
        Assert.False(RolloutCalloutSupersession.Supersedes(ToExit, 500.0, TouchdownKts, CorrectionLeadSec));
    }
}
