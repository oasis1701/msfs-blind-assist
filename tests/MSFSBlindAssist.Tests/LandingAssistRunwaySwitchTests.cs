// Characterization tests for LandingAssistRunwaySwitch — when the manual landing (flare/rollout)
// assist re-points its tones at the runway actually being landed on, and when it says so
// (PR #236 review, finding F12: armed for 12L and landing on 12R, both tones panned hard toward 12L).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingAssistRunwaySwitchTests
{
    private static Runway Rwy(string id, double sLat, double sLon) => new()
    {
        RunwayID = id, Heading = 121.4, Length = 12000, Width = 197,
        StartLat = sLat, StartLon = sLon, EndLat = sLat - 0.02, EndLon = sLon + 0.03,
    };

    private static readonly Runway TwelveL = Rwy("12L", 25.266695, 55.346588);
    private static readonly Runway TwelveR = Rwy("12R", 25.252785, 55.364380);

    private static LandingRunwayResult Different(Runway r) => new(LandingRunwayVerdict.DifferentRunway, r);
    private static readonly LandingRunwayResult Matches = new(LandingRunwayVerdict.Matches, null);
    private static readonly LandingRunwayResult Unknown = new(LandingRunwayVerdict.Unknown, null);

    [Fact]
    public void Flare_engage_on_another_runway_switches_and_speaks_when_audible()
    {
        var d = LandingAssistRunwaySwitch.AtFlareEngage(Different(TwelveR), flareSilent: false);

        Assert.Same(TwelveR, d.SwitchTo);
        Assert.True(d.SpeakCorrection);
    }

    [Fact]
    public void A_silent_flare_switches_without_speaking()
    {
        var d = LandingAssistRunwaySwitch.AtFlareEngage(Different(TwelveR), flareSilent: true);

        Assert.Same(TwelveR, d.SwitchTo);
        Assert.False(d.SpeakCorrection);
    }

    [Fact]
    public void Matches_or_unknown_at_flare_engage_keeps_the_armed_runway()
    {
        Assert.Equal(new LandingAssistRunwayDecision(null, false), LandingAssistRunwaySwitch.AtFlareEngage(Matches, false));
        Assert.Equal(new LandingAssistRunwayDecision(null, false), LandingAssistRunwaySwitch.AtFlareEngage(Unknown, false));
    }

    [Fact]
    public void Touchdown_on_another_runway_switches_and_speaks_once()
    {
        var d = LandingAssistRunwaySwitch.AtTouchdown(Different(TwelveR), active: TwelveL, armed: TwelveL, correctionSpoken: false, exitPlanWillSayIt: false);

        Assert.Same(TwelveR, d.SwitchTo);
        Assert.True(d.SpeakCorrection);
    }

    [Fact]
    public void Touchdown_confirming_an_announced_flare_switch_says_nothing_new()
    {
        var d = LandingAssistRunwaySwitch.AtTouchdown(Matches, active: TwelveR, armed: TwelveL, correctionSpoken: true, exitPlanWillSayIt: false);

        Assert.Null(d.SwitchTo);
        Assert.False(d.SpeakCorrection);
    }

    [Fact]
    public void Touchdown_after_a_silent_flare_switch_speaks_the_correction()
    {
        var d = LandingAssistRunwaySwitch.AtTouchdown(Matches, active: TwelveR, armed: TwelveL, correctionSpoken: false, exitPlanWillSayIt: false);

        Assert.Null(d.SwitchTo);
        Assert.True(d.SpeakCorrection);
    }

    [Fact]
    public void Touchdown_back_on_the_armed_runway_switches_back_without_a_correction()
    {
        var d = LandingAssistRunwaySwitch.AtTouchdown(Different(TwelveL), active: TwelveR, armed: TwelveL, correctionSpoken: true, exitPlanWillSayIt: false);

        Assert.Same(TwelveL, d.SwitchTo);
        Assert.False(d.SpeakCorrection);
    }

    // ---------------------------------------------------------------------------------
    // PR #236 follow-up: one voice for "you are not on the runway you planned for".
    //
    // Both the manual landing assist and the landing-exit planner now notice a runway change and
    // both say so, and at touchdown they say it a few hundredths of a second apart. The assist runs
    // on the simulator's own frame rate and speaks as the wheels touch; the planner asks the
    // simulator for a fresh position first and speaks just after, interrupting. So the assist's
    // sentence was cut off mid-word — and on an approach flown with visual guidance, where the
    // assist stays silent through the flare, that cut-off sentence was the ONLY time the pilot
    // would have been told before the planner's own, longer sentence.
    //
    // The planner's sentence names the runway AND what to do about it, so when it is going to
    // speak, the assist leaves the telling to it and just re-points its tones.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void The_assist_leaves_the_telling_to_the_exit_planner_when_it_is_going_to_speak()
    {
        var d = LandingAssistRunwaySwitch.AtTouchdown(
            Different(TwelveR), active: TwelveL, armed: TwelveL,
            correctionSpoken: false, exitPlanWillSayIt: true);

        Assert.Same(TwelveR, d.SwitchTo);       // the tones still move to the right runway
        Assert.False(d.SpeakCorrection);        // but the pilot hears it once, from the planner
    }

    [Fact]
    public void With_no_exit_plan_running_the_assist_still_says_it_itself()
    {
        var d = LandingAssistRunwaySwitch.AtTouchdown(
            Different(TwelveR), active: TwelveL, armed: TwelveL,
            correctionSpoken: false, exitPlanWillSayIt: false);

        Assert.True(d.SpeakCorrection);
    }

    [Fact]
    public void The_flare_correction_is_unaffected_because_it_comes_long_before_touchdown()
    {
        // Spoken at about fifty feet, seconds before the planner says anything — no collision.
        var d = LandingAssistRunwaySwitch.AtFlareEngage(Different(TwelveR), flareSilent: false);

        Assert.True(d.SpeakCorrection);
    }

    [Fact]
    public void Phrases_name_the_runway_only_with_a_correction()
    {
        Assert.Equal("Flare guidance, runway 12R, not 12L.", LandingAssistRunwaySwitch.FlareGuidancePhrase("12R", "12L", true));
        Assert.Equal("Flare guidance", LandingAssistRunwaySwitch.FlareGuidancePhrase("12L", "12L", false));
        Assert.Equal("Rollout guidance, runway 12R, not 12L.", LandingAssistRunwaySwitch.RolloutGuidancePhrase("12R", "12L", true));
        Assert.Equal("Rollout guidance", LandingAssistRunwaySwitch.RolloutGuidancePhrase("12L", "12L", false));
    }
}
