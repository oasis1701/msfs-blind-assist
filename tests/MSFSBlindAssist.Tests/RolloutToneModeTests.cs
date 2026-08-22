// Characterization tests for RolloutExitGate.SelectToneMode — which of the three
// rollout steering-tone behaviours applies on a given frame.
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The rollout tone is silent until the
// aircraft is within 300 ft of the selected exit. At 2,232 ft to go the pilot had no
// cue at all while drifting 15° off the centreline, and the tone's FIRST utterance
// after the handoff was a 79° hard pan. DriftCorrection fills that silent gap.

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
        Assert.Equal(RolloutToneMode.Silent, RolloutExitGate.SelectToneMode(50.1, 2232.0));
        Assert.Equal(RolloutToneMode.Silent, RolloutExitGate.SelectToneMode(149.1, 100.0));
    }

    // At or below 50 kt and inside the 300 ft arm distance the exit-bearing tone owns
    // the frame — today's behaviour, unchanged.
    [Fact]
    public void InsideArmDistance_IsExitBearing()
    {
        Assert.Equal(RolloutToneMode.ExitBearing, RolloutExitGate.SelectToneMode(50.0, 300.0));
        Assert.Equal(RolloutToneMode.ExitBearing, RolloutExitGate.SelectToneMode(19.7, 12.0));
    }

    // The gap this fix exists to fill: slowed down, but the exit is still far away.
    [Fact]
    public void BelowFiftyKnotsAndOutsideArmDistance_IsDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(29.7, 2349.0));
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(50.0, 300.1));
    }

    // KSEA regression: 19.7 kt, 2,232 ft to go — the exact frame the old handoff fired
    // on. The pilot must have had a drift-correction tone here, not silence.
    [Fact]
    public void Ksea34L_AtTheOldHandoffFrame_IsDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(19.7, 2232.0));
    }
}
