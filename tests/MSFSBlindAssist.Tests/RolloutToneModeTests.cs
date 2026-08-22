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
        Assert.Equal(RolloutToneMode.Silent, RolloutExitGate.SelectToneMode(50.1, 2232.0, 0.0, 0.0));
        Assert.Equal(RolloutToneMode.Silent, RolloutExitGate.SelectToneMode(149.1, 100.0, 0.0, 0.0));
    }

    // At or below 50 kt and inside the 300 ft arm distance the exit-bearing tone owns
    // the frame — today's behaviour, unchanged.
    [Fact]
    public void InsideArmDistance_IsExitBearing()
    {
        Assert.Equal(RolloutToneMode.ExitBearing, RolloutExitGate.SelectToneMode(50.0, 300.0, 0.0, 0.0));
        Assert.Equal(RolloutToneMode.ExitBearing, RolloutExitGate.SelectToneMode(19.7, 12.0, 0.0, 0.0));
    }

    // The gap this fix exists to fill: slowed down, but the exit is still far away, and
    // heading is dead-on the runway (no deviation to test the turn-window exception).
    [Fact]
    public void BelowFiftyKnotsAndOutsideArmDistance_IsDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(29.7, 2349.0, 0.0, 0.0));
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(50.0, 300.1, 0.0, 0.0));
    }

    // KSEA regression: 19.7 kt, 2,232 ft to go — the exact frame the old handoff fired
    // on. The pilot must have had a drift-correction tone here, not silence.
    [Fact]
    public void Ksea34L_AtTheOldHandoffFrame_IsDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(19.7, 2232.0, 0.0, 0.0));
    }

    // A turn TOWARD the exit, inside TurnWindowFeet, with a known exit side: the tone
    // must not fight a turn IsExitTurnBegun is about to accept. Same-sign heading delta
    // and exit relative bearing (both RIGHT).
    [Fact]
    public void TurnTowardExit_InsideWindow_IsSilent()
    {
        Assert.Equal(RolloutToneMode.Silent,
            RolloutExitGate.SelectToneMode(20.0, 900.0, 8.0, 13.6));
    }

    // The identical turn-toward-exit heading geometry, but OUTSIDE TurnWindowFeet — too
    // far from the exit for this to plausibly be the exit turn yet, so DriftCorrection
    // must still own the frame.
    [Fact]
    public void TurnTowardExit_OutsideWindow_StaysDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(20.0, RolloutExitGate.TurnWindowFeet + 1.0, 8.0, 13.6));
    }

    // The KSEA 34L incident geometry itself: 15.1° LEFT drift with the exit 13.6° to the
    // RIGHT — a turn AWAY from the known exit side, inside the window. Must stay
    // DriftCorrection; this is the case the fix must not regress.
    [Fact]
    public void TurnAwayFromExit_InsideWindow_StaysDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(19.7, 900.0, -15.1, 13.6));
    }

    // Same heading deviation and window as the toward-exit case, but the exit's side is
    // UNKNOWN (the ExitBearingTrue == 0.0 sentinel normalises to a relative bearing of
    // 0.0). A drift and an exit turn are indistinguishable here, so the drift tone must
    // keep working rather than going silent.
    [Fact]
    public void UnknownExitSide_InsideWindow_StaysDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(20.0, 900.0, 8.0, 0.0));
    }

    // A sub-deadband deviation (below DriftToneSilentDeg, where the drift tone is already
    // silent-in-effect) toward a known exit side, inside the window: still DriftCorrection
    // — the turn-window exception only applies once the deviation clears the same 2.0°
    // floor the drift tone itself uses.
    [Fact]
    public void SubDeadbandDeviation_InsideWindow_StaysDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection,
            RolloutExitGate.SelectToneMode(20.0, 900.0, 1.5, 13.6));
    }
}
