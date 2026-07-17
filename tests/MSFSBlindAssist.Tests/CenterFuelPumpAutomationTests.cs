using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer;
using Xunit;
using Action = MSFSBlindAssist.FirstOfficer.CenterFuelPumpAutomation.Action;

namespace MSFSBlindAssist.Tests;

public class CenterFuelPumpAutomationTests
{
    private const double NominalTickMs = 1000;

    private static CenterFuelPumpAutomation Make() => new();

    // Args map: (enabled, dataReady, onGround, qty, pumpsOn, dry, credible, wingPumps, elapsedMs).
    // The test surface renames the old lowPress→dry and adds dataReady/credible with safe defaults.
    private static Action Tick(CenterFuelPumpAutomation a, bool onGround, double qty,
        bool pumpsOn, bool dry, bool wingPumps, bool enabled = true, bool credible = true,
        bool dataReady = true, double elapsedMs = NominalTickMs)
        => a.Update(enabled, dataReady, onGround, qty, pumpsOn, dry, credible, wingPumps, elapsedMs);

    // Drain the post-switch-on settle window (pumps already observed on, not dry). Must be
    // called AFTER a rising-edge tick.
    private static void DrainSettleWindow(CenterFuelPumpAutomation a, double qty = 100)
    {
        double remaining = CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000;
        while (remaining > 0)
        {
            double step = System.Math.Min(remaining, 2000);
            a.Update(true, true, false, qty, true, false, true, false, step);
            remaining -= step;
        }
    }

    // ---- Preserved / signature-updated ----

    [Fact]
    public void Disabled_NeverActuates()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 5000, false, false, true, enabled: false));
    }

    [Fact]
    public void ArmsOn_WhenGroundFuelPresentWingPumpsOnPumpsOff()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, false, false, true));
    }

    [Fact]
    public void DoesNotArm_WhenWingPumpsOff()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 5000, false, false, wingPumps: false));
    }

    [Fact]
    public void DoesNotArm_WhenAirborne()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, onGround: false, 5000, false, false, true));
    }

    [Fact]
    public void DoesNotArm_WhenNoCenterFuel()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 100, false, false, true));
    }

    [Fact]
    public void DoesNotArm_WhenPumpsAlreadyOn()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: true, false, true));
    }

    [Fact]
    public void TurnsOff_AfterSustainedDryWhilePumpsOn()
    {
        var a = Make();
        Tick(a, false, 100, pumpsOn: true, dry: false, wingPumps: false);   // rising edge
        DrainSettleWindow(a);
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks - 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, dry: true, wingPumps: false));
        Assert.Equal(Action.TurnOff, Tick(a, false, 100, true, true, false));
    }

    [Fact]
    public void FlickeringDry_DoesNotTurnOff()
    {
        var a = Make();
        Tick(a, false, 100, pumpsOn: true, dry: false, wingPumps: false);
        DrainSettleWindow(a);
        foreach (bool dry in new[] { true, true, false, true, true, false })
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, dry: dry, wingPumps: false));
    }

    [Fact]
    public void SettleWindow_SuppressesImmediateOffAfterArming()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, false, false, true));
        Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, dry: true, wingPumps: false)); // rising edge starts settle
        int settleTicks = (int)(CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000 / NominalTickMs);
        for (int i = 0; i < settleTicks - 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, dry: true, wingPumps: false));
        Assert.Equal(Action.TurnOff, Tick(a, false, 100, true, true, false));
    }

    // FIX 1 regression: any observed switch-on gets settle protection, on a FULL tank.
    [Fact]
    public void ManualSwitchOn_FullTank_GetsSettleProtection_NeverFalselyTurnsOff()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 8000, pumpsOn: false, dry: false, wingPumps: false));
        Assert.Equal(Action.None, Tick(a, true, 8000, pumpsOn: true, dry: true, wingPumps: false)); // rising edge
        int settleTicks = (int)(CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000 / NominalTickMs);
        for (int i = 0; i < settleTicks - 1; i++)
            Assert.Equal(Action.None, Tick(a, true, 8000, pumpsOn: true, dry: true, wingPumps: false));
    }

    // Bug-#1 pin: sustained dry on the ground must fire OFF exactly once, never oscillate.
    [Fact]
    public void SustainedDry_OnGround_DoesNotOscillate()
    {
        var a = Make();
        bool pumpsOn = true;
        a.Update(true, true, true, 5000, pumpsOn, false, true, true, NominalTickMs);
        double remaining = CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000;
        while (remaining > 0)
        {
            double step = System.Math.Min(remaining, 2000);
            a.Update(true, true, true, 5000, pumpsOn, false, true, true, step);
            remaining -= step;
        }
        var actions = new List<Action>();
        for (int i = 0; i < 60; i++)
        {
            Action r = a.Update(true, true, true, 5000, pumpsOn, true, true, true, NominalTickMs);
            actions.Add(r);
            if (r == Action.TurnOff) pumpsOn = false;
            else if (r == Action.TurnOn) pumpsOn = true;
        }
        Assert.Equal(1, actions.FindAll(x => x == Action.TurnOff).Count);
        Assert.Equal(0, actions.FindAll(x => x == Action.TurnOn).Count);
    }

    // T-13b (was AbsurdElapsed): now proven via the GAP mechanism (R-M4), not "low-press never
    // reaches 3000 ms". 60000 ms > ObservationGapMs (5000) → gap → phantom rising edge each spike
    // → settle re-armed to 10 s → OFF cannot fire.
    [Fact]
    public void AbsurdElapsed_GapReArmsSettle_DoesNotInstantlyTurnOff()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: false, dry: false, wingPumps: false, elapsedMs: 1));
        Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, dry: true, wingPumps: false, elapsedMs: 60000));
        Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, dry: true, wingPumps: false, elapsedMs: 60000));
    }

    [Fact]
    public void ArmThreshold_ExactBoundary_DoesNotArm()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, CenterFuelPumpAutomation.ArmThresholdLbs,
            pumpsOn: false, dry: false, wingPumps: true));
    }

    [Fact]
    public void ArmThreshold_JustAboveBoundary_Arms()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, CenterFuelPumpAutomation.ArmThresholdLbs + 0.01,
            pumpsOn: false, dry: false, wingPumps: true));
    }

    // ---- Changed: refuel-clear semantics ----

    // T-6 (no-drop): a FROZEN reference (dry-off at 5000, never dropped) must not be vacuously
    // cleared. 5200 (< 5250) holds the latch; only 6000 (> 5250) clears. Fails against a machine
    // that clears at qty>threshold.
    [Fact]
    public void LatchAtFrozenReference_NotClearedWithinMargin()
    {
        var a = Make();
        DriveToDryOff(a, 5000);
        Assert.Equal(Action.None, Tick(a, true, 5000, false, false, wingPumps: true));   // unchanged
        Assert.Equal(Action.None, Tick(a, true, 5200, false, false, wingPumps: true));   // within margin
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, false, false, wingPumps: true)); // genuine uplift
    }

    // T-7 (new): the ratchet lowers the refuel reference. Drop to 4000 → floor 4000; 4200 (< 4250)
    // holds; 4300 (> 4250) clears. Fails if the ratchet is deleted (floor stays 5000; 4300 < 5250).
    [Fact]
    public void RatchetLowersTheRefuelReference()
    {
        var a = Make();
        DriveToDryOff(a, 5000);
        Assert.Equal(Action.None, Tick(a, true, 4000, false, false, wingPumps: true));   // ratchet → floor 4000
        Assert.Equal(Action.None, Tick(a, true, 4200, false, false, wingPumps: true));   // within new margin
        Assert.Equal(Action.TurnOn, Tick(a, true, 4300, false, false, wingPumps: true)); // > 4250 → clear
    }

    [Fact]
    public void OnceOff_StaysOffUntilRefuelOnGround()
    {
        var a = Make();
        DriveToDryOff(a, 600);
        Assert.Equal(Action.None, Tick(a, true, 700, pumpsOn: false, dry: true, wingPumps: true)); // <850, latch holds
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-9: Reset() clears the POLICY latch (behavioral). Without Reset, 5100 (< 5250) → None.
    [Fact]
    public void Reset_ClearsPolicyLatch_ReArmsBelowRefuelMargin()
    {
        var a = Make();
        DriveToDryOff(a, 5000);
        a.Reset();
        Assert.Equal(Action.TurnOn, Tick(a, true, 5100, false, false, wingPumps: true));
    }

    // T-10: Reset() clears the OBSERVATION group too — a fresh rising edge re-arms the full settle,
    // so a dry run right after Reset is suppressed for the settle window.
    [Fact]
    public void Reset_ClearsObservationSettle()
    {
        var a = Make();
        Tick(a, false, 100, pumpsOn: true, dry: false, wingPumps: false);
        DrainSettleWindow(a);
        a.Reset();
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks + 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, dry: true, wingPumps: false));
    }

    // ---- New discriminating tests (each states the mutation it catches) ----

    // T-1: pending-command latch — TurnOn is not repeated while the readback has not landed.
    [Fact]
    public void TurnOn_NotRepeated_WhilePendingReadback()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, pumpsOn: false, dry: false, wingPumps: true));
        for (int i = 0; i < 5; i++)   // readback never lands (pumps stay off)
            Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-2: pending-command latch — TurnOff is not repeated within the confirm window.
    [Fact]
    public void TurnOff_NotRepeated_WhilePendingReadback()
    {
        var a = Make();
        DriveToDryOff(a, 5000);
        // Pumps still read on (write not landed); dry sustained; still within 30 s confirm.
        for (int i = 0; i < 10; i++)
            Assert.Equal(Action.None, Tick(a, false, 5000, pumpsOn: true, dry: true, wingPumps: false));
    }

    // T-4: THE TRAP — a confirmed dry-off (latch set) then a MANUAL re-arm must still TurnOff on a
    // fresh dry run. Fails if OFF is gated on _switchedOffThisLeg or a give-up latch.
    [Fact]
    public void ConfirmedOff_ThenManualRearm_StillTurnsOff()
    {
        var a = Make();
        DriveToDryOff(a, 5000);
        // Readback: pumps go off → clears the pending Off, falling edge attributed to us.
        Tick(a, false, 5000, pumpsOn: false, dry: false, wingPumps: false);
        // Manual re-arm: pumps on → rising edge (settle re-armed); _switchedOffThisLeg still set.
        Tick(a, false, 5000, pumpsOn: true, dry: false, wingPumps: false);
        DrainSettleWindow(a, qty: 5000);
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks - 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 5000, pumpsOn: true, dry: true, wingPumps: false));
        Assert.Equal(Action.TurnOff, Tick(a, false, 5000, pumpsOn: true, dry: true, wingPumps: false));
    }

    // T-5: credibility gate — dry but NOT credible never turns off (F3 poison block).
    [Fact]
    public void SystemNotCredible_NeverTurnsOff()
    {
        var a = Make();
        Tick(a, false, 5000, pumpsOn: true, dry: false, wingPumps: false);
        DrainSettleWindow(a, qty: 5000);
        for (int i = 0; i < 60; i++)
            Assert.Equal(Action.None,
                a.Update(true, true, false, 5000, true, true, false, false, NominalTickMs)); // credible:false
    }

    // T-8a: manual-off with wing pumps on suppresses re-arm.
    [Fact]
    public void ManualOff_WithWingPumpsOn_SuppressesRearm()
    {
        var a = Make();
        Tick(a, true, 6000, pumpsOn: true, dry: false, wingPumps: true);   // rising edge
        Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: true);  // falling → _manualOffLatch
        Assert.Equal(Action.None, Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-8b: manual-off with wing pumps OFF does NOT latch (shutdown vs intent) — turnaround arms.
    [Fact]
    public void ManualOff_WithWingPumpsOff_DoesNotLatch()
    {
        var a = Make();
        Tick(a, true, 6000, pumpsOn: true, dry: false, wingPumps: false);  // rising edge
        Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: false); // falling, wing off → no latch
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-8c (M3): our own OFF must NOT set the intent latch even after the confirm timeout — a write
    // landing late still carries _lastCommandedOff. Fails against a time-limited hadOffPending flag.
    [Fact]
    public void OurOwnOff_DoesNotSetIntentLatch_EvenAfterConfirmTimeout()
    {
        var a = Make();
        DriveToDryOff(a, 200);                        // TurnOff; _lastCommandedOff=true; pending Off
        // Pumps stay on but the tank is no longer dry → no re-OFF; let pending time out (~30 s).
        for (int i = 0; i < 20; i++)
            a.Update(true, true, true, 200, true, false, true, false, 2000);   // 40 s > CommandConfirmSeconds
        // The write finally lands: falling edge, _lastCommandedOff still true → NO _manualOffLatch.
        a.Update(true, true, true, 200, false, false, true, false, NominalTickMs);
        // A genuine turnaround refuel clears _switchedOffThisLeg and arms — proving no intent latch stuck.
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-11: disabled tracks settle + edges; re-enable mid-spin-up must NOT false-OFF on a full tank.
    // Also fails if the enable edge calls full Reset() (which would wipe _settleMs).
    [Fact]
    public void Disabled_TracksSettleAndEdges_ReEnableMidSpinUp_NoFalseOff()
    {
        var a = Make();
        // Disabled: pilot switches pumps on (rising edge tracked), full tank, transient dry.
        a.Update(false, true, false, 8000, false, false, true, false, NominalTickMs);
        a.Update(false, true, false, 8000, true, true, true, false, NominalTickMs);   // rising while disabled
        a.Update(false, true, false, 8000, true, true, true, false, 2000);            // 2 s of settle burned
        // Re-enable: 8 s of settle remain → sustained dry on a FULL tank must not fire OFF for 7 s.
        for (int i = 0; i < 7; i++)
            Assert.Equal(Action.None, a.Update(true, true, false, 8000, true, true, true, false, NominalTickMs));
    }

    // T-12 (adjudicated 2026-07-16): a manual-off latch set WHILE DISABLED is cleared by the
    // enable-edge (step 1) — "re-enabling is a fresh start" (OQ-2). The falling edge must land
    // strictly BEFORE the enable tick; step 8 is deliberately NOT enabled-gated (R-9).
    // Mutation it catches: the enable-edge clear being dropped or moved after the decision gate.
    [Fact]
    public void DisabledManualOff_ClearedByEnableEdge_ArmPossible()
    {
        var a = Make();
        a.Update(false, true, true, 5000, true, false, true, true, NominalTickMs);   // disabled, pumps ON (rising)
        a.Update(false, true, true, 5000, false, false, true, true, NominalTickMs);  // disabled, falling → latch may set
        a.Update(false, true, true, 5000, false, false, true, true, NominalTickMs);  // disabled, no edge
        // Enable tick, NO edge: step 1 clears all policy latches → arm fires this tick.
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-12b (adjudicated 2026-07-16): a falling edge ON the enable tick itself is a LIVE manual-off
    // on an enabled tick and must latch (step 1's clear runs before step 8's set — correct order;
    // "don't fight the pilot"). Mutation it catches: reordering step 1 after step 8, or gating
    // step 8 on enabled.
    [Fact]
    public void EnableTickFallingEdge_LatchesManualOff()
    {
        var a = Make();
        for (int i = 0; i < 3; i++)
            a.Update(false, true, true, 5000, true, false, true, true, NominalTickMs); // disabled, pumps ON
        // Single tick that is BOTH the enable edge AND the falling edge → latch set post-clear.
        Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: false, dry: false, wingPumps: true));
        // Latch holds: no arm on subsequent enabled ticks despite armable conditions.
        for (int i = 0; i < 5; i++)
            Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: false, dry: false, wingPumps: true));
        // A genuine ground refuel uplift (> floor 5000 + RefuelMarginLbs) clears the latch → arm.
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-13: dataReady=false touches nothing and forces a fresh settle when data returns.
    [Fact]
    public void DataNotReady_TouchesNothing_ThenForcesFreshSettle()
    {
        var a = Make();
        for (int i = 0; i < 5; i++)
            Assert.Equal(Action.None,
                a.Update(true, false, false, 5000, true, true, true, false, NominalTickMs)); // dataReady:false
        // Data returns with pumps on + dry → phantom rising edge → fresh 10 s settle suppresses OFF.
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks + 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 5000, pumpsOn: true, dry: true, wingPumps: false));
    }

    // T-14: the CI-pinnable constant invariant. NOTE: there is deliberately NO "Settle > Confirm"
    // assertion (M1: that relation is unsound; SettleSecondsAfterOn=10 < CommandConfirmSeconds=30
    // and that is correct). Fails if a tuner breaks "any armable quantity also clears".
    [Fact]
    public void ConstantsInvariants()
    {
        Assert.True(CenterFuelPumpAutomation.ArmThresholdLbs > CenterFuelPumpAutomation.RefuelMarginLbs);
    }

    // T-15: in-flight arming is impossible (F1).
    [Fact]
    public void InFlightArming_Impossible()
    {
        var a = Make();
        for (int i = 0; i < 60; i++)
            Assert.Equal(Action.None, Tick(a, onGround: false, 5000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-16 (C-A): manual-off (no dry-off) then a ground refuel > seed+250 re-arms.
    [Fact]
    public void ManualOff_ThenGroundRefuel_ReArms()
    {
        var a = Make();
        Tick(a, true, 8000, pumpsOn: true, dry: false, wingPumps: true);   // rising edge
        Tick(a, true, 8000, pumpsOn: false, dry: false, wingPumps: true);  // falling → latch, seed 8000
        Assert.Equal(Action.None, Tick(a, true, 8000, pumpsOn: false, dry: false, wingPumps: true));
        Assert.Equal(Action.TurnOn, Tick(a, true, 8300, pumpsOn: false, dry: false, wingPumps: true)); // > 8250
    }

    // T-16b: a lower reading BEFORE the latch is invisible to the latch (seed-at-latch, not
    // session-min). Catches the running-minimum proposal.
    [Fact]
    public void ManualOff_AfterEarlierLowerReading_DoesNotClearImmediately()
    {
        var a = Make();
        Tick(a, true, 5000, pumpsOn: true, dry: false, wingPumps: true);   // observe 5000 (no latch)
        Tick(a, true, 6000, pumpsOn: true, dry: false, wingPumps: true);   // rise to 6000 (no latch)
        Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: true);  // falling → latch, seed 6000
        Assert.Equal(Action.None, Tick(a, true, 6000, pumpsOn: false, dry: false, wingPumps: true)); // 6000 < 6250
    }

    // T-16c (C-A rising-edge clear): manual-off latch, then pumps rise → latch clears → un-latched.
    [Fact]
    public void ManualOff_ThenRisingEdge_ClearsLatch()
    {
        var a = Make();
        Tick(a, true, 8000, pumpsOn: true, dry: false, wingPumps: true);   // rising edge
        Tick(a, true, 8000, pumpsOn: false, dry: false, wingPumps: true);  // falling → latch
        Tick(a, true, 8000, pumpsOn: true, dry: false, wingPumps: true);   // rising → _manualOffLatch cleared
        Tick(a, true, 8000, pumpsOn: false, dry: false, wingPumps: false); // falling, wing off → no NEW latch
        Assert.Equal(Action.TurnOn, Tick(a, true, 8000, pumpsOn: false, dry: false, wingPumps: true));
    }

    // T-18 (I1): re-enable mid dry-run emits EXACTLY ONE TurnOff (readback tracks the last action).
    // Fails against a clear-after-decision ordering (TurnOff→TurnOn→TurnOff over ~13 s).
    [Fact]
    public void ReEnableMidDryRun_EmitsExactlyOneTurnOff()
    {
        var a = Make();
        bool pumpsOn = true;
        // Disabled prelude: pumps on, settle drained, tank dry, on ground.
        a.Update(false, true, true, 5000, pumpsOn, false, true, true, NominalTickMs);
        double remaining = CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000;
        while (remaining > 0)
        {
            double step = System.Math.Min(remaining, 2000);
            a.Update(false, true, true, 5000, pumpsOn, false, true, true, step);
            remaining -= step;
        }
        var actions = new List<Action>();
        for (int i = 0; i < 60; i++)   // now enabled; drive 60 s
        {
            Action r = a.Update(true, true, true, 5000, pumpsOn, true, true, true, NominalTickMs);
            actions.Add(r);
            if (r == Action.TurnOff) pumpsOn = false;
            else if (r == Action.TurnOn) pumpsOn = true;
        }
        Assert.Equal(1, actions.FindAll(x => x == Action.TurnOff).Count);
    }

    // T-19 (I2): a data gap must NOT burn the confirm window. Fails if _pendingMs accrues above the
    // !dataReady return (then pending times out during the gap and a second TurnOff fires on restore).
    [Fact]
    public void DataGap_DoesNotBurnConfirmWindow()
    {
        var a = Make();
        DriveToDryOff(a, 5000);                       // TurnOff; pending Off
        for (int i = 0; i < 20; i++)                  // 40 s of gap — would time out the 30 s confirm IF accrued
            a.Update(true, false, false, 5000, true, true, true, false, 2000);   // dataReady:false
        // Restore: pending Off must still suppress (real elapsed ~15 s < 30 s). No 2nd TurnOff.
        var actions = new List<Action>();
        for (int i = 0; i < 15; i++)
            actions.Add(a.Update(true, true, false, 5000, true, true, true, false, NominalTickMs));
        Assert.DoesNotContain(Action.TurnOff, actions);
    }

    // T-20 (R-M4 starvation): a sustained slow feed (> MaxElapsedMs, < ObservationGapMs) still turns
    // off. Fails if the gap test is keyed on MaxElapsedMs (every tick a phantom gap → settle never drains).
    [Fact]
    public void SustainedSlowFeed_StillTurnsOff()
    {
        var a = Make();
        a.Update(true, true, false, 5000, true, false, true, false, 2500);   // rising edge, 2500 ms
        var actions = new List<Action>();
        for (int i = 0; i < 30; i++)
            actions.Add(a.Update(true, true, false, 5000, true, true, true, false, 2500));
        Assert.Contains(Action.TurnOff, actions);
    }

    // T-21 (R-3b property 4): the refuel clear is strict '>' and the margin is > 0, so ratchet and
    // clear are mutually exclusive and the clear cannot fire on the boundary. Fails if the clear is '>='.
    [Fact]
    public void RatchetAndClear_AreMutuallyExclusive()
    {
        var a = Make();
        DriveToDryOff(a, 5000);   // seed 5000
        Assert.Equal(Action.None, Tick(a, true, 5000, false, false, wingPumps: true));  // == floor: neither
        Assert.Equal(Action.None, Tick(a, true, 5250, false, false, wingPumps: true));  // == floor+margin: NOT '>'
        Assert.Equal(Action.TurnOn, Tick(a, true, 5251, false, false, wingPumps: true));// strictly greater: clears
    }

    // ---- Helper: drive a full arm-drain-dryoff to a TurnOff at the given quantity ----
    private static void DriveToDryOff(CenterFuelPumpAutomation a, double dryOffQty)
    {
        a.Update(true, true, false, dryOffQty, true, false, true, false, NominalTickMs); // rising edge
        double remaining = CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000;
        while (remaining > 0)
        {
            double step = System.Math.Min(remaining, 2000);
            a.Update(true, true, false, dryOffQty, true, false, true, false, step);
            remaining -= step;
        }
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks - 1; i++)
            a.Update(true, true, false, dryOffQty, true, true, true, false, NominalTickMs);
        Action last = a.Update(true, true, false, dryOffQty, true, true, true, false, NominalTickMs);
        Assert.Equal(Action.TurnOff, last);
    }
}
