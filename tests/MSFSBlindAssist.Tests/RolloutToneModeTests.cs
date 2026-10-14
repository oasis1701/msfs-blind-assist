// Characterization tests for RolloutExitGate.SelectToneMode — which of the three
// rollout steering-tone behaviours applies on a given frame.
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The rollout tone is silent until the
// aircraft is within 300 ft of the selected exit. At 2,232 ft to go the pilot had no
// cue at all while drifting 15° off the centreline, and the tone's FIRST utterance
// after the handoff was a 79° hard pan. DriftCorrection fills that silent gap.
//
// A second regression, same incident write-up: within RolloutExitGate.TurnWindowFeet
// of the exit, a heading deviation toward a KNOWN exit side must go Silent rather than
// DriftCorrection — a pilot legitimately turning off between 3° and 15° of deviation
// must not get a tone fighting the turn IsExitTurnBegun is about to accept.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RolloutToneModeTests
{
    // Above 50 kt the tone stays silent regardless of distance: the existing comment
    // in TaxiGuidanceManager.Rollout.cs warns that autopilot crab / crosswind
    // alignment produces confusing pan during the high-speed phase.
    [Fact]
    public void AboveFiftyKnots_IsSilent()
    {
        Assert.Equal(RolloutToneMode.Silent, RolloutExitGate.SelectToneMode(50.1, 2232.0, 0.0, 0.0, RolloutExitGate.TurnWindowFeet));
        Assert.Equal(RolloutToneMode.Silent, RolloutExitGate.SelectToneMode(149.1, 100.0, 0.0, 0.0, RolloutExitGate.TurnWindowFeet));
    }

    // At or below 50 kt and inside the 300 ft arm distance the exit-bearing tone owns
    // the frame — today's behaviour, unchanged.
    [Fact]
    public void InsideArmDistance_IsExitBearing()
    {
        Assert.Equal(RolloutToneMode.ExitBearing, RolloutExitGate.SelectToneMode(50.0, 300.0, 0.0, 0.0, RolloutExitGate.TurnWindowFeet));
        Assert.Equal(RolloutToneMode.ExitBearing, RolloutExitGate.SelectToneMode(19.7, 12.0, 0.0, 0.0, RolloutExitGate.TurnWindowFeet));
    }

    // The gap this fix exists to fill: slowed down, but the exit is still far away, and
    // heading is dead-on the runway (no deviation to test the turn-window exception).
    [Fact]
    public void BelowFiftyKnotsAndOutsideArmDistance_IsDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(29.7, 2349.0, 0.0, 0.0, RolloutExitGate.TurnWindowFeet));
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(50.0, 300.1, 0.0, 0.0, RolloutExitGate.TurnWindowFeet));
    }

    // KSEA regression: 19.7 kt, 2,232 ft to go — the exact frame the old handoff fired
    // on. The pilot must have had a drift-correction tone here, not silence.
    [Fact]
    public void Ksea34L_AtTheOldHandoffFrame_IsDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(19.7, 2232.0, 0.0, 0.0, RolloutExitGate.TurnWindowFeet));
    }

    // A turn TOWARD the exit, inside TurnWindowFeet, with a known exit side: the tone
    // must not fight a turn IsExitTurnBegun is about to accept. Same-sign heading delta
    // and exit relative bearing (both RIGHT).
    [Fact]
    public void TurnTowardExit_InsideWindow_IsSilent()
    {
        Assert.Equal(RolloutToneMode.Silent,
            RolloutExitGate.SelectToneMode(20.0, 900.0, 8.0, 13.6, RolloutExitGate.TurnWindowFeet));
    }

    // The identical turn-toward-exit heading geometry, but OUTSIDE TurnWindowFeet — too
    // far from the exit for this to plausibly be the exit turn yet, so DriftCorrection
    // must still own the frame.
    [Fact]
    public void TurnTowardExit_OutsideWindow_StaysDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(20.0, RolloutExitGate.TurnWindowFeet + 1.0, 8.0, 13.6, RolloutExitGate.TurnWindowFeet));
    }

    // The KSEA 34L incident geometry itself: 15.1° LEFT drift with the exit 13.6° to the
    // RIGHT — a turn AWAY from the known exit side, inside the window. Must stay
    // DriftCorrection; this is the case the fix must not regress.
    [Fact]
    public void TurnAwayFromExit_InsideWindow_StaysDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(19.7, 900.0, -15.1, 13.6, RolloutExitGate.TurnWindowFeet));
    }

    // Same heading deviation and window as the toward-exit case, but the exit's side is
    // UNKNOWN. The 0.0 relative bearing is what RolloutExitGate.ExitRelativeBearingDeg
    // MAPS the ExitBearingTrue == 0.0 sentinel to — not something normalisation lands on
    // by itself (see ExitRelativeBearingTests, which disproves that). A drift and an exit
    // turn are indistinguishable here, so the drift tone must keep working rather than
    // going silent.
    [Fact]
    public void UnknownExitSide_InsideWindow_StaysDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(20.0, 900.0, 8.0, 0.0, RolloutExitGate.TurnWindowFeet));
    }

    // A sub-deadband deviation (below DriftToneSilentDeg, where the drift tone is already
    // silent-in-effect) toward a known exit side, inside the window: still DriftCorrection
    // — the turn-window exception only applies once the deviation clears the same 2.0°
    // floor the drift tone itself uses.
    [Fact]
    public void SubDeadbandDeviation_InsideWindow_StaysDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(20.0, 900.0, 1.5, 13.6, RolloutExitGate.TurnWindowFeet));
    }

    // ---- With the landing assist's centerline cue engaged (KSEA 16R, 2026-10-15) ----
    //
    // An A300 autoland held the centerline to 96 kt, then yawed right at 2-3°/s and left the
    // pavement at 70 kt, 1,303 ft short of exit Q, with no cue: the tone was Silent above 50 kt.
    // While the cue is engaged, exit guidance yields its silent band and the drift band beyond
    // the exit's own turn window to it (Centerline), and keeps the tone for the exit itself.
    // KSEA 16R's exits lie to the left of the runway heading: relative bearing -16.2°.

    private static RolloutToneMode WithCue(double gs, double dist, double hdg = 0.0, double exitRel = -16.2,
        double window = 294.0, bool tooFast = false)
        => RolloutExitGate.SelectToneMode(gs, dist, hdg, exitRel, window, tooFast, centerlineCueEngaged: true);

    // The KSEA frame: 86 kt, 1,920 ft from Q, 3.5° right of the runway heading, away from Q.
    [Fact]
    public void WithCue_TheKseaVeer_IsCenterline()
    {
        Assert.Equal(RolloutToneMode.Centerline, WithCue(86.0, 1920.0, 3.5));
    }

    // Above 50 kt exit guidance has no tone of its own, so the cue keeps it even near the exit.
    [Fact]
    public void WithCue_AboveFiftyKnots_NearTheExit_IsCenterline()
    {
        Assert.Equal(RolloutToneMode.Centerline, WithCue(55.0, 200.0));
    }

    // ...except for a turn toward the exit inside the exit's own window: never fight it.
    [Fact]
    public void WithCue_AboveFiftyKnots_TurningTowardTheExitInsideItsWindow_IsSilent()
    {
        Assert.Equal(RolloutToneMode.Silent, WithCue(55.0, 250.0, -3.0));
    }

    [Fact]
    public void WithCue_AboveFiftyKnots_TurningTowardTheExitBeyondItsWindow_IsCenterline()
    {
        Assert.Equal(RolloutToneMode.Centerline, WithCue(55.0, 600.0, -3.0));
    }

    // Too fast for the exit: there is no turn to protect, so the cue holds the centerline.
    [Fact]
    public void WithCue_TooFastForTheExit_IsCenterlineEverywhere()
    {
        Assert.Equal(RolloutToneMode.Centerline, WithCue(45.0, 200.0, -3.0, tooFast: true));
        Assert.Equal(RolloutToneMode.Centerline, WithCue(70.0, 250.0, -3.0, tooFast: true));
    }

    // At or below 50 kt, the exit zone keeps today's tones.
    [Fact]
    public void WithCue_AtOrBelowFiftyKnots_TheExitZoneIsUnchanged()
    {
        Assert.Equal(RolloutToneMode.ExitBearing, WithCue(50.0, 300.0));
        Assert.Equal(RolloutToneMode.Silent, WithCue(40.0, 900.0, -3.0, window: 1000.0));
        Assert.Equal(RolloutToneMode.DriftCorrection, WithCue(40.0, 900.0, 3.0, window: 1000.0));
    }

    [Fact]
    public void WithCue_AtOrBelowFiftyKnots_BeyondTheWindow_IsCenterline()
    {
        Assert.Equal(RolloutToneMode.Centerline, WithCue(40.0, 1200.0, 3.0, window: 1000.0));
    }

    // Without the cue, Centerline is never selected: every case above this section is unchanged.
    [Fact]
    public void WithoutTheCue_CenterlineNeverAppears()
    {
        foreach (double gs in new[] { 10.0, 50.0, 90.0, 140.0 })
            foreach (double dist in new[] { 100.0, 600.0, 3000.0 })
                foreach (bool tooFast in new[] { false, true })
                    Assert.NotEqual(RolloutToneMode.Centerline,
                        RolloutExitGate.SelectToneMode(gs, dist, 5.0, -16.2, 294.0, tooFast));
    }

    // Exit guidance owns the tone only in a rollout with an exit, outside its Centerline mode: the
    // cue plays with no exit guidance, in the runway-end countdown and in Centerline.
    [Theory]
    [InlineData(false, false, RolloutToneMode.Silent, false)]
    [InlineData(true, true, RolloutToneMode.ExitBearing, false)]
    [InlineData(true, false, RolloutToneMode.Centerline, false)]
    [InlineData(true, false, RolloutToneMode.Silent, true)]
    [InlineData(true, false, RolloutToneMode.DriftCorrection, true)]
    [InlineData(true, false, RolloutToneMode.ExitBearing, true)]
    public void ExitGuidanceOwnsTheTone_OnlyInAnExitRolloutOutsideCenterline(
        bool inLandingRollout, bool noExitMode, RolloutToneMode lastToneMode, bool expected)
    {
        Assert.Equal(expected, RolloutExitGate.ExitGuidanceOwnsTone(inLandingRollout, noExitMode, lastToneMode));
    }
}
