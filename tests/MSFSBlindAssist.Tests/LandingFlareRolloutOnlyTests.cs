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
                47.459977, -122.317925, 86.04, 3.5, -10.26, -0.23, -3.73, -0.75f, true,
                LandingFlareAssistManager.RolloutToneOwner.Cue)));
        Assert.EndsWith(",0,exit", UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceLine(
            0, 0, 0, 0, 0, 0, 0, 0f, false, LandingFlareAssistManager.RolloutToneOwner.ExitGuidance)));
        Assert.EndsWith(",0,vg", UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceLine(
            0, 0, 0, 0, 0, 0, 0, 0f, false, LandingFlareAssistManager.RolloutToneOwner.VisualGuidance)));
    }

    // Visual guidance switches itself off only on the 1 Hz SIM_ON_GROUND touchdown edge, up to a second after
    // the cue's first ground frame. Until then it keeps the tone, so its two tones and the cue never play
    // together; then exit guidance holds it for the exit itself ([ROL-32]), and the cue everywhere else.
    [Fact]
    public void Visual_guidance_keeps_the_tone_until_it_switches_off()
    {
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.VisualGuidance,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: true, exitGuidanceOwnsTone: false));
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.VisualGuidance,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: true, exitGuidanceOwnsTone: true));
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.ExitGuidance,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: false, exitGuidanceOwnsTone: true));
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.Cue,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: false, exitGuidanceOwnsTone: false));
    }

    // The "Rollout guidance" sentence at touchdown, with visual guidance off: the manual landing assist's flare
    // mode speaks it at once as before, and so does the rollout-only cue with no exit plan; with a plan the cue
    // defers it, because the planner hears the touchdown up to a second later (the 1 Hz SIM_ON_GROUND sample
    // plus a position read).
    [Fact]
    public void The_rollout_guidance_sentence_waits_for_the_planner_when_a_plan_exists()
    {
        var immediate = LandingFlareAssistManager.RolloutGuidanceSpeech.Immediate;
        var deferred = LandingFlareAssistManager.RolloutGuidanceSpeech.Deferred;
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: true, exitPlanExists: false, visualGuidanceActive: false));
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: true, exitPlanExists: true, visualGuidanceActive: false));
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: false, exitPlanExists: false, visualGuidanceActive: false));
        Assert.Equal(deferred, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: false, exitPlanExists: true, visualGuidanceActive: false));
    }

    // Visual guidance still on at touchdown holds the sentence in every mode, the manual landing assist's flare
    // mode included: said at once it would cut off visual guidance's "Touchdown", and it belongs after
    // "Visual guidance off".
    [Fact]
    public void The_rollout_guidance_sentence_waits_for_visual_guidance_to_switch_off()
    {
        var deferred = LandingFlareAssistManager.RolloutGuidanceSpeech.Deferred;
        foreach (bool flareEnabled in new[] { false, true })
            foreach (bool exitPlanExists in new[] { false, true })
                Assert.Equal(deferred, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
                    flareEnabled, exitPlanExists, visualGuidanceActive: true));
    }

    // A deferred sentence waits 1.5 s. If exit guidance has started by then, the planner's touchdown sentence
    // said guidance had started, so it is dropped; otherwise (a plan for another airport, a plan already used,
    // a touchdown the planner did not take) it is spoken, without interrupting whatever is being said.
    [Fact]
    public void A_deferred_rollout_guidance_sentence_is_spoken_only_if_exit_guidance_did_not_start()
    {
        var wait = LandingFlareAssistManager.DeferredSpeech.Wait;
        var speak = LandingFlareAssistManager.DeferredSpeech.Speak;
        var drop = LandingFlareAssistManager.DeferredSpeech.Drop;
        Assert.Equal(wait, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            1.49, exitGuidanceRunning: false, visualGuidanceActive: false));
        Assert.Equal(wait, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            0.2, exitGuidanceRunning: true, visualGuidanceActive: false));
        Assert.Equal(speak, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            1.5, exitGuidanceRunning: false, visualGuidanceActive: false));
        Assert.Equal(drop, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            1.5, exitGuidanceRunning: true, visualGuidanceActive: false));
        Assert.Equal(speak, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            30.0, exitGuidanceRunning: false, visualGuidanceActive: false));
    }

    // While visual guidance is still on, the sentence waits however long that takes, then is decided as above.
    [Fact]
    public void A_deferred_rollout_guidance_sentence_waits_while_visual_guidance_is_on()
    {
        var wait = LandingFlareAssistManager.DeferredSpeech.Wait;
        Assert.Equal(wait, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            30.0, exitGuidanceRunning: false, visualGuidanceActive: true));
        Assert.Equal(wait, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            30.0, exitGuidanceRunning: true, visualGuidanceActive: true));
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
