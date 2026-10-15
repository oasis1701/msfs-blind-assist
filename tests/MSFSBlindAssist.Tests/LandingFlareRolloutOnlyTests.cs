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
        => Assert.True(LandingFlareAssistManager.ShouldEngageRolloutOnly(true, 50.0, verdict));

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

    // A light aircraft touching down at 44 kt would hear "Rollout guidance" and then, at the 40 kt end a second
    // later, an interrupting "Rollout guidance complete": the floor stays 10 kt above the end.
    [Theory]
    [InlineData(44.0)]
    [InlineData(49.9)]
    public void A_touchdown_too_close_to_the_40_kt_end_never_engages(double groundSpeedKts)
        => Assert.False(LandingFlareAssistManager.ShouldEngageRolloutOnly(true, groundSpeedKts, LandingRunwayVerdict.Matches));

    // With no exit guidance steering at an exit, a turn off the runway at exit speed mutes the cue instead of
    // panning against it: the runway-end countdown's turn onto the end taxiway, a turnaround, a rapid exit taken
    // with only a destination set. Above the fastest exit speed (60 kt) a heading swing is a veer (KSEA 16R left
    // the pavement at 70 kt) and the cue keeps steering; in an exit rollout exit guidance owns the turn window.
    [Theory]
    [InlineData(false, 12.0, 90.0, true)]     // countdown: turning onto the end taxiway
    [InlineData(false, 50.0, -30.0, true)]    // destination only: a 30° rapid exit at 50 kt
    [InlineData(false, 60.0, 15.0, true)]
    [InlineData(false, 60.1, 30.0, false)]    // faster than any exit: a veer
    [InlineData(false, 40.0, 14.9, false)]    // not yet a turn
    [InlineData(true, 20.0, 90.0, false)]     // exit rollout: exit guidance owns the turn window
    public void A_turn_off_the_runway_mutes_the_cue_only_where_nobody_owns_the_turn(
        bool inExitRollout, double groundSpeedKts, double headingOffRunwayDeg, bool expected)
        => Assert.Equal(expected,
            LandingFlareAssistManager.IsTurningOffRunway(inExitRollout, groundSpeedKts, headingOffRunwayDeg));

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
        Assert.EndsWith(",0,turn", UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceLine(
            0, 0, 0, 0, 0, 0, 0, 0f, false, LandingFlareAssistManager.RolloutToneOwner.TurnOff)));
    }

    // Visual guidance switches itself off only on the 1 Hz SIM_ON_GROUND touchdown edge, up to a second after
    // the cue's first ground frame. Until then it keeps the tone, so its two tones and the cue never play
    // together; then exit guidance holds it for the exit itself ([ROL-32]), then nobody during a turn off the
    // runway, and the cue everywhere else.
    [Fact]
    public void Visual_guidance_keeps_the_tone_until_it_switches_off()
    {
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.VisualGuidance,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: true, exitGuidanceOwnsTone: false,
                turningOffRunway: true));
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.VisualGuidance,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: true, exitGuidanceOwnsTone: true,
                turningOffRunway: false));
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.ExitGuidance,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: false, exitGuidanceOwnsTone: true,
                turningOffRunway: true));
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.TurnOff,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: false, exitGuidanceOwnsTone: false,
                turningOffRunway: true));
        Assert.Equal(LandingFlareAssistManager.RolloutToneOwner.Cue,
            LandingFlareAssistManager.DecideRolloutToneOwner(visualGuidanceActive: false, exitGuidanceOwnsTone: false,
                turningOffRunway: false));
    }

    // The "Rollout guidance" sentence at touchdown, with visual guidance off: spoken at once with no exit plan,
    // and in flare mode when the plan starts exit guidance here (the planner then says the runway correction).
    // Deferred for the rollout-only cue with any plan, and in flare mode for a plan that will not start guidance
    // here (made for another airport, or already used): the planner hears the touchdown up to a second later
    // and may then say "Runway not identified", which would cut an immediate sentence off.
    [Fact]
    public void The_rollout_guidance_sentence_waits_for_the_planner_when_a_plan_exists()
    {
        var immediate = LandingFlareAssistManager.RolloutGuidanceSpeech.Immediate;
        var deferred = LandingFlareAssistManager.RolloutGuidanceSpeech.Deferred;
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: true, exitPlanExists: false, exitGuidanceStartsHere: false, visualGuidanceActive: false));
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: true, exitPlanExists: true, exitGuidanceStartsHere: true, visualGuidanceActive: false));
        Assert.Equal(deferred, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: true, exitPlanExists: true, exitGuidanceStartsHere: false, visualGuidanceActive: false));
        Assert.Equal(immediate, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: false, exitPlanExists: false, exitGuidanceStartsHere: false, visualGuidanceActive: false));
        Assert.Equal(deferred, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: false, exitPlanExists: true, exitGuidanceStartsHere: true, visualGuidanceActive: false));
        Assert.Equal(deferred, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
            flareEnabled: false, exitPlanExists: true, exitGuidanceStartsHere: false, visualGuidanceActive: false));
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
                foreach (bool startsHere in new[] { false, true })
                    Assert.Equal(deferred, LandingFlareAssistManager.DecideRolloutGuidanceSpeech(
                        flareEnabled, exitPlanExists, startsHere, visualGuidanceActive: true));
    }

    // A deferred sentence waits while the planner still has to decide the touchdown, however long its position
    // read and route take (no timer standing in for that). Once it has: dropped if exit guidance started (its
    // touchdown sentence said so), otherwise (a plan for another airport, a plan already used) spoken, without
    // interrupting whatever is being said.
    [Fact]
    public void A_deferred_rollout_guidance_sentence_is_spoken_only_if_exit_guidance_did_not_start()
    {
        var wait = LandingFlareAssistManager.DeferredSpeech.Wait;
        var speak = LandingFlareAssistManager.DeferredSpeech.Speak;
        var drop = LandingFlareAssistManager.DeferredSpeech.Drop;
        Assert.Equal(wait, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            1.5, plannerAwaitingTouchdown: true, exitGuidanceRunning: false, visualGuidanceActive: false));
        Assert.Equal(wait, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            4.9, plannerAwaitingTouchdown: true, exitGuidanceRunning: false, visualGuidanceActive: false));
        Assert.Equal(speak, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            0.1, plannerAwaitingTouchdown: false, exitGuidanceRunning: false, visualGuidanceActive: false));
        Assert.Equal(drop, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            0.1, plannerAwaitingTouchdown: false, exitGuidanceRunning: true, visualGuidanceActive: false));
        Assert.Equal(speak, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            30.0, plannerAwaitingTouchdown: false, exitGuidanceRunning: false, visualGuidanceActive: false));
    }

    // A touchdown the planner never takes (it never got the ground sample, or refused it as too slow) still
    // gets its sentence: the wait for the planner is bounded at 5 s.
    [Fact]
    public void A_planner_that_never_decides_holds_the_sentence_at_most_5_seconds()
    {
        Assert.Equal(LandingFlareAssistManager.DeferredSpeech.Speak, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            5.0, plannerAwaitingTouchdown: true, exitGuidanceRunning: false, visualGuidanceActive: false));
    }

    // While visual guidance is still on, the sentence waits however long that takes, then is decided as above.
    [Fact]
    public void A_deferred_rollout_guidance_sentence_waits_while_visual_guidance_is_on()
    {
        var wait = LandingFlareAssistManager.DeferredSpeech.Wait;
        Assert.Equal(wait, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            30.0, plannerAwaitingTouchdown: false, exitGuidanceRunning: false, visualGuidanceActive: true));
        Assert.Equal(wait, LandingFlareAssistManager.ResolveDeferredRolloutGuidance(
            30.0, plannerAwaitingTouchdown: false, exitGuidanceRunning: true, visualGuidanceActive: true));
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
