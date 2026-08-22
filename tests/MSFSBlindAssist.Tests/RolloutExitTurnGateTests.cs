// Characterization tests for RolloutExitGate.IsExitTurnBegun — "has the pilot begun the
// turn onto the selected exit?".
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The old gate was
// `hdgDeltaAbs >= 15 && gs < 90` — an ABSOLUTE heading deviation with no reference to
// where the exit is or how far away it lies. A 15.1° LEFT drift at 19.7 kt, 2,232 ft
// short of an exit that lies 13.6° to the RIGHT, satisfied it.
//
// Sign convention: headingDelta and exitRelativeBearing are both signed relative to the
// runway heading, POSITIVE = right. So KSEA's drift is -15.1 and exit Z is +13.6.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RolloutExitTurnGateTests
{
    // THE regression. Wrong side AND far outside the turn window — either alone is
    // disqualifying, and this case has both.
    [Fact]
    public void Ksea34L_LeftDriftTowardsARightHandExit_IsNotATurn()
    {
        Assert.False(RolloutExitGate.IsExitTurnBegun(
            headingDeltaSignedDeg: -15.1,
            groundSpeedKts: 19.7,
            distToExitFeet: 2232.0,
            pastExit: false,
            exitRelativeBearingDeg: 13.6));
    }

    // The ordinary case this gate exists for: a hard turn onto a right-hand exit,
    // at the exit, at taxi speed.
    [Fact]
    public void RightTurnAtARightHandExit_IsATurn()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(
            headingDeltaSignedDeg: 16.0,
            groundSpeedKts: 20.0,
            distToExitFeet: 150.0,
            pastExit: false,
            exitRelativeBearingDeg: 13.6));
    }

    // Same turn, same exit, but 2,232 ft short of it. You cannot be turning onto an
    // exit that is still 2,232 ft away — this is what the window rejects.
    [Fact]
    public void RightTurnFarShortOfTheExit_IsNotATurn()
    {
        Assert.False(RolloutExitGate.IsExitTurnBegun(
            headingDeltaSignedDeg: 16.0,
            groundSpeedKts: 20.0,
            distToExitFeet: 2232.0,
            pastExit: false,
            exitRelativeBearingDeg: 13.6));
    }

    // Window boundary: 1,000 ft is derived from a 558 ft worst-case exit-node
    // displacement plus the app's own 450 ft "at the exit" range. Inclusive.
    [Fact]
    public void TurnWindowBoundaryIsInclusiveAtOneThousandFeet()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 1000.0, false, 13.6));
        Assert.False(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 1000.1, false, 13.6));
    }

    // pastExit bypasses the window entirely: an overshooting aircraft is beyond the
    // exit, so distance-to-exit is growing and would fail the window forever.
    [Fact]
    public void PastExit_BypassesTheDistanceWindow()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 5000.0, true, 13.6));
    }

    // Left-hand exits are the mirror image; nothing here is right-hand-specific.
    [Fact]
    public void LeftTurnAtALeftHandExit_IsATurn()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(-16.0, 20.0, 150.0, false, -30.0));
        Assert.False(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 150.0, false, -30.0));
    }

    // The existing 15° and 90 kt gates are unchanged.
    [Fact]
    public void BelowFifteenDegreesOrAboveNinetyKnots_IsNotATurn()
    {
        Assert.False(RolloutExitGate.IsExitTurnBegun(14.9, 20.0, 150.0, false, 13.6));
        Assert.True(RolloutExitGate.IsExitTurnBegun(15.0, 20.0, 150.0, false, 13.6));
        Assert.False(RolloutExitGate.IsExitTurnBegun(16.0, 90.0, 150.0, false, 13.6));
        Assert.True(RolloutExitGate.IsExitTurnBegun(16.0, 89.9, 150.0, false, 13.6));
    }

    // ExitBearingTrue == 0.0 is the rollout code's "unknown bearing" sentinel and
    // normalises into the sub-3° band. Unknown side must NOT block the handoff —
    // degrade to the old direction-blind behaviour rather than stranding the pilot.
    [Fact]
    public void UnknownExitSide_SkipsTheDirectionTest()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(-16.0, 20.0, 150.0, false, 0.0));
        Assert.True(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 150.0, false, 2.9));
        // At 3.0° the exit has a side again and the wrong-way turn is rejected.
        Assert.False(RolloutExitGate.IsExitTurnBegun(-16.0, 20.0, 150.0, false, 3.0));
    }

    // IsTurnTowardExit is exposed separately for the post-handoff overshoot monitor,
    // which needs the direction test WITHOUT the distance window.
    [Fact]
    public void IsTurnTowardExit_IsDirectionOnly()
    {
        Assert.True(RolloutExitGate.IsTurnTowardExit(20.0, 13.6));
        Assert.False(RolloutExitGate.IsTurnTowardExit(-20.0, 13.6));
        Assert.True(RolloutExitGate.IsTurnTowardExit(-20.0, -13.6));
        Assert.True(RolloutExitGate.IsTurnTowardExit(-20.0, 0.0));
    }
}
