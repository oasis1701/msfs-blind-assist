// Characterization tests for the PMDG 777 emergency-exit light guard/switch
// sequencing (FirstOfficer/EmerExitLightSequence.cs).
//
// The 777 emergency-exit light switch is GUARDED. The polarity and ordering
// pinned here were user-verified against the real switch via MobiFlight on
// 2026-08-01 and are encoded in PMDG777Definition.HandleUIVariableSet — this
// file is the single place that keeps the First Officer's copy of that model
// honest, because the sim-facing dispatch around it cannot be unit-tested.
//
// The contracts pinned here:
// - guard polarity is 0 = CLOSED, 1 = OPEN. The First Officer previously had
//   this exactly inverted (a method named CloseEmerExitLightGuard sent 1, which
//   OPENS the guard), so the polarity gets its own explicit test;
// - ARMED is the normal, guard-closed position. OFF and ON sit OUTSIDE the
//   guard, so the guard must be lifted BEFORE the switch will move there;
// - returning to ARMED is the mirror: move the switch first, then close the
//   guard over it;
// - ordering matters because the guard and the switch travel on DIFFERENT
//   transports (guard = CDA, switch = TransmitClientEvent) and are not
//   guaranteed to arrive in issue order without a settle gap between them.

using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests;

public class EmerExitLightSequenceTests
{
    // The hardware-verified polarity. If this test ever needs "fixing", the fix
    // is wrong — re-read PMDG777Definition.HandleUIVariableSet first.
    [Fact]
    public void GuardPolarity_IsZeroClosedOneOpen()
    {
        Assert.Equal(0, EmerExitLightSequence.GuardClosed);
        Assert.Equal(1, EmerExitLightSequence.GuardOpen);
    }

    [Fact]
    public void Plan_IsEmpty_WhenAlreadyAtTarget()
    {
        var steps = EmerExitLightSequence.Plan(
            current: EmerExitLightSequence.Armed,
            target: EmerExitLightSequence.Armed,
            haveGuard: true);

        Assert.Empty(steps);
    }

    [Fact]
    public void Plan_LiftsGuardThenMovesSwitch_WhenLeavingArmedToOff()
    {
        var steps = EmerExitLightSequence.Plan(
            current: EmerExitLightSequence.Armed,
            target: EmerExitLightSequence.Off,
            haveGuard: true);

        Assert.Equal(2, steps.Count);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Guard, EmerExitLightSequence.GuardOpen), steps[0]);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Switch, EmerExitLightSequence.Off), steps[1]);
    }

    [Fact]
    public void Plan_LiftsGuardThenMovesSwitch_WhenLeavingArmedToOn()
    {
        var steps = EmerExitLightSequence.Plan(
            current: EmerExitLightSequence.Armed,
            target: EmerExitLightSequence.On,
            haveGuard: true);

        Assert.Equal(2, steps.Count);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Guard, EmerExitLightSequence.GuardOpen), steps[0]);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Switch, EmerExitLightSequence.On), steps[1]);
    }

    [Fact]
    public void Plan_MovesSwitchThenClosesGuard_WhenReturningToArmedFromOff()
    {
        var steps = EmerExitLightSequence.Plan(
            current: EmerExitLightSequence.Off,
            target: EmerExitLightSequence.Armed,
            haveGuard: true);

        Assert.Equal(2, steps.Count);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Switch, EmerExitLightSequence.Armed), steps[0]);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Guard, EmerExitLightSequence.GuardClosed), steps[1]);
    }

    [Fact]
    public void Plan_MovesSwitchThenClosesGuard_WhenReturningToArmedFromOn()
    {
        var steps = EmerExitLightSequence.Plan(
            current: EmerExitLightSequence.On,
            target: EmerExitLightSequence.Armed,
            haveGuard: true);

        Assert.Equal(2, steps.Count);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Switch, EmerExitLightSequence.Armed), steps[0]);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Guard, EmerExitLightSequence.GuardClosed), steps[1]);
    }

    // Both OFF and ON sit outside the guard, so moving between them still needs
    // the guard lifted first — the def branches on the TARGET, not the origin.
    [Fact]
    public void Plan_LiftsGuardThenMovesSwitch_WhenMovingBetweenTwoUnguardedPositions()
    {
        var steps = EmerExitLightSequence.Plan(
            current: EmerExitLightSequence.Off,
            target: EmerExitLightSequence.On,
            haveGuard: true);

        Assert.Equal(2, steps.Count);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Guard, EmerExitLightSequence.GuardOpen), steps[0]);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Switch, EmerExitLightSequence.On), steps[1]);
    }

    // Degrade gracefully: with no guard event resolved, move the switch alone
    // rather than doing nothing (mirrors the def's !haveGuard branch).
    [Fact]
    public void Plan_MovesSwitchAlone_WhenGuardEventUnavailable()
    {
        var steps = EmerExitLightSequence.Plan(
            current: EmerExitLightSequence.Armed,
            target: EmerExitLightSequence.Off,
            haveGuard: false);

        Assert.Single(steps);
        Assert.Equal(new EmerExitStep(EmerExitStepKind.Switch, EmerExitLightSequence.Off), steps[0]);
    }

    [Fact]
    public void Plan_IsEmpty_WhenAlreadyAtTarget_EvenWithoutGuard()
    {
        var steps = EmerExitLightSequence.Plan(
            current: EmerExitLightSequence.Off,
            target: EmerExitLightSequence.Off,
            haveGuard: false);

        Assert.Empty(steps);
    }
}
