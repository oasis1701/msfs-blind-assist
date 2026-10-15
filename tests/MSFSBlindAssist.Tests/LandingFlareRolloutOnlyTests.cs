using System.Globalization;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The landing assist's rollout-only cue (armed by a destination runway or a landing exit, no flare
/// phase): when a touchdown engages it (LandingFlareAssistManager.ShouldEngageRolloutOnly), and the
/// landing_assist.log trace line it writes (FormatTraceLine). KSEA 16R, 2026-10-15: an A300 autoland
/// left the runway at 70 kt with no centerline cue, because none was armed for an autoland.
/// </summary>
public class LandingFlareRolloutOnlyTests
{
    [Theory]
    [InlineData(LandingRunwayVerdict.Matches)]
    [InlineData(LandingRunwayVerdict.ReciprocalEnd)]
    [InlineData(LandingRunwayVerdict.DifferentRunway)]
    public void A_real_touchdown_at_the_armed_airport_engages(LandingRunwayVerdict verdict)
        => Assert.True(LandingFlareAssistManager.ShouldEngageRolloutOnly(true, 40.0, verdict));

    // A stale destination (armed for KSEA, landed at another airport) must never pan the pilot toward
    // a centerline that is not under the wheels.
    [Fact]
    public void A_touchdown_off_the_armed_airport_never_engages()
        => Assert.False(LandingFlareAssistManager.ShouldEngageRolloutOnly(true, 137.0, LandingRunwayVerdict.Unknown));

    // A fast taxi or a rejected takeoff never left the ground: not a landing.
    [Fact]
    public void Without_flying_first_nothing_engages()
        => Assert.False(LandingFlareAssistManager.ShouldEngageRolloutOnly(false, 137.0, LandingRunwayVerdict.Matches));

    // The landing-exit planner's real-landing floor: below it, a teleport or a taxi onto the ground.
    [Fact]
    public void Below_40_kt_nothing_engages()
        => Assert.False(LandingFlareAssistManager.ShouldEngageRolloutOnly(true, 39.9, LandingRunwayVerdict.Matches));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_trace_line_is_invariant_whatever_the_culture(string culture)
    {
        Assert.Equal("47.459977,-122.317925,86.0,3.50,-10.3,-0.23,-3.73,-0.75,1,cue",
            UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceLine(
                47.459977, -122.317925, 86.04, 3.5, -10.26, -0.23, -3.73, -0.75f, true, false)));
        Assert.EndsWith(",0,exit", UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceLine(
            0, 0, 0, 0, 0, 0, 0, 0f, false, true)));
    }

    // The "Rollout guidance" sentence at touchdown: the manual landing assist's flare mode speaks it at once
    // as before; the rollout-only cue stays silent when the planner starts exit guidance at this touchdown
    // (its sentence is the one utterance), queues it after any planner sentence when a plan exists that will
    // not start here (another airport, or a plan already used this session), and speaks it at once otherwise.
    [Fact]
    public void The_rollout_guidance_sentence_follows_what_the_planner_will_say()
    {
        var immediate = LandingFlareAssistManager.RolloutGuidanceSpeech.Immediate;
        var queued = LandingFlareAssistManager.RolloutGuidanceSpeech.Queued;
        var none = LandingFlareAssistManager.RolloutGuidanceSpeech.None;
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(true, false, false));
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(true, true, true));
        Assert.Equal(none, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(false, true, true));
        Assert.Equal(queued, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(false, false, true));
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(false, false, false));
    }

    // A database switch disarms whatever was armed and says which: the checkbox arming as before, and the
    // rollout-only cue too, so its silence at the next touchdown is never mistaken for an on-track cue.
    [Fact]
    public void A_database_switch_names_what_it_switched_off()
    {
        Assert.Equal("Manual landing assist off", LandingFlareAssistManager.DisarmAnnouncement(true, true));
        Assert.Equal("Rollout centerline tone off", LandingFlareAssistManager.DisarmAnnouncement(true, false));
        Assert.Null(LandingFlareAssistManager.DisarmAnnouncement(false, true));
        Assert.Null(LandingFlareAssistManager.DisarmAnnouncement(false, false));
    }

    // Only flying above 80 ft (the flare band's own latch height) arms the touchdown decision, so settling
    // back onto the runway right after liftoff never starts the cue on the takeoff roll.
    [Theory]
    [InlineData(false, 80.1, true)]
    [InlineData(false, 80.0, false)]
    [InlineData(false, 30.0, false)]
    [InlineData(true, 200.0, false)]
    public void Only_flying_above_80_ft_arms_the_touchdown(bool onGround, double gearAglFt, bool expected)
        => Assert.Equal(expected, LandingFlareAssistManager.ArmsRolloutOnlyTouchdown(onGround, gearAglFt));

    // Why the cue ended, or why a touchdown did not engage it, goes to landing_assist.log beside the trace:
    // debug.log rotates within minutes, and the in-sim test plan reads this file.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_trace_event_line_is_invariant_whatever_the_culture(string culture)
    {
        Assert.Equal("=== Rollout end: stopped gs=2.5 ===",
            UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceEvent("Rollout end: stopped", 2.5)));
    }

    private static string UnderCulture(string name, Func<string> render)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
