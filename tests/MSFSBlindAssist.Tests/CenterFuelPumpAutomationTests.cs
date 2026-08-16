using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer;
using Xunit;
using Action = MSFSBlindAssist.FirstOfficer.CenterFuelPumpAutomation.Action;

namespace MSFSBlindAssist.Tests;

public class CenterFuelPumpAutomationTests
{
    private const double NominalTickMs = 1000;

    private static CenterFuelPumpAutomation Make() => new();

    // Args map: (enabled, dataReady, onGround, qty, pumpsOn, wingPumps, elapsedMs) — matches the
    // new 7-parameter Update signature (the dry/credible annunciator params are gone).
    private static Action Tick(CenterFuelPumpAutomation a, bool onGround, double qty,
        bool pumpsOn, bool wingPumps, bool enabled = true,
        bool dataReady = true, double elapsedMs = NominalTickMs)
        => a.Update(enabled, dataReady, onGround, qty, pumpsOn, wingPumps, elapsedMs);

    // ---- Preserved / signature-updated ----

    [Fact]
    public void Disabled_NeverActuates()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 5000, false, true, enabled: false));
    }

    [Fact]
    public void ArmsOn_WhenGroundFuelPresentWingPumpsOnPumpsOff()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, false, true));
    }

    [Fact]
    public void DoesNotArm_WhenWingPumpsOff()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 5000, false, wingPumps: false));
    }

    [Fact]
    public void DoesNotArm_WhenAirborne()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, onGround: false, 5000, false, true));
    }

    [Fact]
    public void DoesNotArm_WhenNoCenterFuel()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 100, false, true));
    }

    [Fact]
    public void DoesNotArm_WhenPumpsAlreadyOn()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: true, wingPumps: true));
    }

    [Fact]
    public void ArmThreshold_ExactBoundary_DoesNotArm()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, CenterFuelPumpAutomation.ArmThresholdLbs,
            pumpsOn: false, wingPumps: true));
    }

    [Fact]
    public void ArmThreshold_JustAboveBoundary_Arms()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, CenterFuelPumpAutomation.ArmThresholdLbs + 0.01,
            pumpsOn: false, wingPumps: true));
    }

    // ---- Core new quantity-based OFF cases (verbatim from the design brief) ----

    [Fact]
    public void Off_Fires_After_Continuous_Confirm_Below_Threshold()
    {
        var a = new CenterFuelPumpAutomation();
        // pumps already on, healthy quantity
        a.Update(true, true, false, 5000, true, true, 0);
        Assert.Equal(CenterFuelPumpAutomation.Action.None, a.Update(true, true, false, 1200, true, true, 1000));
        // crosses below 1000: needs 2 s continuous
        Assert.Equal(CenterFuelPumpAutomation.Action.None, a.Update(true, true, false, 950, true, true, 1000));
        Assert.Equal(CenterFuelPumpAutomation.Action.TurnOff, a.Update(true, true, false, 900, true, true, 1000));
    }

    [Fact]
    public void Off_Confirm_Resets_When_Quantity_Rises_Back_Above()
    {
        var a = new CenterFuelPumpAutomation();
        a.Update(true, true, false, 5000, true, true, 0);
        a.Update(true, true, false, 950, true, true, 1000);
        a.Update(true, true, false, 1100, true, true, 1000);   // back above: reset
        Assert.Equal(CenterFuelPumpAutomation.Action.None, a.Update(true, true, false, 950, true, true, 1000));
        Assert.Equal(CenterFuelPumpAutomation.Action.TurnOff, a.Update(true, true, false, 900, true, true, 1000));
    }

    [Fact]
    public void Replays_20260816_Log_Arm_Refused_At_922_And_Depletion_Fires_Off()
    {
        var a = new CenterFuelPumpAutomation();
        // Ground, wing pumps on, 922 lbs — the morning's arm must NOT happen (>1500 required).
        Assert.Equal(CenterFuelPumpAutomation.Action.None, a.Update(true, true, true, 922, false, true, 0));
        Assert.Equal(CenterFuelPumpAutomation.Action.None, a.Update(true, true, true, 922, false, true, 1000));
        // Rich tank arms.
        Assert.Equal(CenterFuelPumpAutomation.Action.TurnOn, a.Update(true, true, true, 5000, false, true, 1000));
        a.Update(true, true, true, 5000, true, true, 1000);    // readback lands
        // Airborne depletion 922 → 304 → 0 at ~1 Hz: OFF within ~2 s of crossing 1000.
        a.Update(true, true, false, 922, true, true, 1000);
        var second = a.Update(true, true, false, 304, true, true, 1000);
        var third  = a.Update(true, true, false, 0,   true, true, 1000);
        Assert.True(second == CenterFuelPumpAutomation.Action.TurnOff
                 || third  == CenterFuelPumpAutomation.Action.TurnOff);
    }

    [Fact]
    public void Arm_Requires_Above_ArmThreshold_Not_Merely_OffThreshold()
    {
        var a = new CenterFuelPumpAutomation();
        Assert.Equal(CenterFuelPumpAutomation.Action.None, a.Update(true, true, true, 1400, false, true, 0));
        Assert.Equal(CenterFuelPumpAutomation.Action.TurnOn, a.Update(true, true, true, 1600, false, true, 1000));
    }

    [Fact]
    public void Off_Not_Retriggered_While_Pending_And_Latch_Suppresses_Rearm()
    {
        var a = new CenterFuelPumpAutomation();
        a.Update(true, true, true, 900, true, true, 0);
        a.Update(true, true, true, 900, true, true, 1000);
        Assert.Equal(CenterFuelPumpAutomation.Action.TurnOff, a.Update(true, true, true, 900, true, true, 1000));
        // pending Off + readback not yet landed: no reissue
        Assert.Equal(CenterFuelPumpAutomation.Action.None, a.Update(true, true, true, 900, true, true, 1000));
        // readback lands (pumps off); dry-off latch suppresses re-arm — IN FLIGHT, because on the
        // ground 2000 lbs would legitimately clear the latch as a refuel (900 floor + 250 margin).
        Assert.Equal(CenterFuelPumpAutomation.Action.None, a.Update(true, true, false, 2000, false, true, 1000));
        // and the ground-refuel clear still works: same reading ON the ground re-arms.
        Assert.Equal(CenterFuelPumpAutomation.Action.TurnOn, a.Update(true, true, true, 2000, false, true, 1000));
    }

    // Invalid quantity (NaN/negative/Infinity) must reset the confirm and never fire OFF on the
    // invalid tick itself — only a fresh run of continuous VALID below-threshold ticks can confirm.
    [Fact]
    public void InvalidQuantity_ResetsConfirm_NeverFiresOffOnInvalidTick()
    {
        var a = new CenterFuelPumpAutomation();
        // rising edge, first below-threshold tick (0 elapsed ms → no accrual yet)
        Assert.Equal(Action.None, a.Update(true, true, false, 900, true, true, 0));
        // an invalid reading must never fire OFF, and must reset the confirm window
        Assert.Equal(Action.None, a.Update(true, true, false, double.NaN, true, true, 1000));
        // fresh accrual: one valid below-threshold tick is not yet 2 s of continuous evidence
        Assert.Equal(Action.None, a.Update(true, true, false, 900, true, true, 1000));
        // second consecutive valid below-threshold tick completes the fresh 2 s confirm
        Assert.Equal(Action.TurnOff, a.Update(true, true, false, 900, true, true, 1000));
    }

    // ---- Refuel floor ratchet + margin (manual-off latch path — quantities well above the arm
    //      threshold so the assertions observe the clear/arm directly, matching the pre-existing
    //      pinned values). ----

    [Fact]
    public void LatchAtFrozenReference_NotClearedWithinMargin()
    {
        var a = Make();
        Tick(a, true, 5000, pumpsOn: true, wingPumps: true);    // rising edge
        Tick(a, true, 5000, pumpsOn: false, wingPumps: true);   // falling → manualOffLatch, floor=5000
        Assert.Equal(Action.None, Tick(a, true, 5000, false, wingPumps: true));   // unchanged
        Assert.Equal(Action.None, Tick(a, true, 5200, false, wingPumps: true));   // within margin
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, false, wingPumps: true)); // genuine uplift
    }

    [Fact]
    public void RatchetLowersTheRefuelReference()
    {
        var a = Make();
        Tick(a, true, 5000, pumpsOn: true, wingPumps: true);
        Tick(a, true, 5000, pumpsOn: false, wingPumps: true);   // floor=5000
        Assert.Equal(Action.None, Tick(a, true, 4000, false, wingPumps: true));   // ratchet → floor 4000
        Assert.Equal(Action.None, Tick(a, true, 4200, false, wingPumps: true));   // within new margin
        Assert.Equal(Action.TurnOn, Tick(a, true, 4300, false, wingPumps: true)); // > 4250 → clear
    }

    [Fact]
    public void RatchetAndClear_AreMutuallyExclusive()
    {
        var a = Make();
        Tick(a, true, 5000, pumpsOn: true, wingPumps: true);
        Tick(a, true, 5000, pumpsOn: false, wingPumps: true);   // floor=5000
        Assert.Equal(Action.None, Tick(a, true, 5000, false, wingPumps: true));   // == floor: neither
        Assert.Equal(Action.None, Tick(a, true, 5250, false, wingPumps: true));   // == floor+margin: NOT '>'
        Assert.Equal(Action.TurnOn, Tick(a, true, 5251, false, wingPumps: true)); // strictly greater: clears
    }

    // Quantity-triggered OFF also seeds the floor correctly (SeedFloor is shared with the manual
    // path). Must stay under OffThresholdLbs to actually confirm OFF.
    [Fact]
    public void OnceOff_StaysOffUntilGenuineRefuelClearsLatchAndArms()
    {
        var a = Make();
        DriveToDryOff(a, 900);
        Tick(a, true, 900, pumpsOn: false, wingPumps: true);   // readback lands; floor stays 900
        Assert.Equal(Action.None, Tick(a, true, 1140, pumpsOn: false, wingPumps: true));  // within margin
        Assert.Equal(Action.TurnOn, Tick(a, true, 2000, pumpsOn: false, wingPumps: true)); // clears AND arms
    }

    [Fact]
    public void Reset_ClearsPolicyLatch_ReArmsWithoutMarginRequirement()
    {
        var a = Make();
        DriveToDryOff(a, 900);
        a.Reset();
        Assert.Equal(Action.TurnOn, Tick(a, true, 1600, pumpsOn: false, wingPumps: true));
    }

    // ---- Pending-command latch ----

    [Fact]
    public void TurnOn_NotRepeated_WhilePendingReadback()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, pumpsOn: false, wingPumps: true));
        for (int i = 0; i < 5; i++)   // readback never lands (pumps stay off)
            Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: false, wingPumps: true));
    }

    [Fact]
    public void TurnOff_NotRepeated_WhilePendingReadback()
    {
        var a = Make();
        DriveToDryOff(a, 900);
        // Pumps still read on (write not landed); still within 30 s confirm.
        for (int i = 0; i < 10; i++)
            Assert.Equal(Action.None, Tick(a, false, 900, pumpsOn: true, wingPumps: false));
    }

    // THE TRAP — a confirmed dry-off (latch set) then a MANUAL re-arm must still TurnOff on a
    // fresh confirm. Fails if OFF is gated on _switchedOffThisLeg.
    [Fact]
    public void ConfirmedOff_ThenManualRearm_StillTurnsOffOnFreshConfirm()
    {
        var a = Make();
        DriveToDryOff(a, 900);                                   // TurnOff; pending Off; switchedOffThisLeg
        Tick(a, false, 900, pumpsOn: false, wingPumps: false);    // readback lands: falling, our own off
        Tick(a, false, 900, pumpsOn: true, wingPumps: false);     // manual re-arm: rising edge
        // The rising tick itself already contributes 1 s below-threshold (qty was already below
        // OffThresholdLbs at the moment of the edge), so only ONE more tick reaches the 2 s confirm.
        Assert.Equal(Action.TurnOff, Tick(a, false, 900, pumpsOn: true, wingPumps: false));
    }

    // ---- Manual-off latch + falling-edge attribution ----

    [Fact]
    public void ManualOff_WithWingPumpsOn_SuppressesRearm()
    {
        var a = Make();
        Tick(a, true, 6000, pumpsOn: true, wingPumps: true);   // rising edge
        Tick(a, true, 6000, pumpsOn: false, wingPumps: true);  // falling → _manualOffLatch
        Assert.Equal(Action.None, Tick(a, true, 6000, pumpsOn: false, wingPumps: true));
    }

    [Fact]
    public void ManualOff_WithWingPumpsOff_DoesNotLatch()
    {
        var a = Make();
        Tick(a, true, 6000, pumpsOn: true, wingPumps: false);  // rising edge
        Tick(a, true, 6000, pumpsOn: false, wingPumps: false); // falling, wing off → no latch
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, wingPumps: true));
    }

    // Our own OFF must NOT set the intent (manual-off) latch even after the pending confirm
    // times out — a write landing late still carries _lastCommandedOff. Observed via Diagnostics
    // since a genuine refuel would clear both latches identically.
    [Fact]
    public void OurOwnOff_DoesNotSetIntentLatch_EvenAfterConfirmTimeout()
    {
        var a = Make();
        DriveToDryOff(a, 200);                        // TurnOff; _lastCommandedOff=true; pending Off
        // Pumps stay on but quantity is healthy again → no re-OFF; let pending time out (~30 s).
        for (int i = 0; i < 20; i++)
            a.Update(true, true, true, 5000, true, false, 2000);   // 40 s > CommandConfirmSeconds
        // The write finally lands: falling edge, _lastCommandedOff still true → NO _manualOffLatch.
        a.Update(true, true, true, 5000, false, false, NominalTickMs);
        Assert.Contains("manualOffLatch=0", a.Diagnostics);
        // A genuine turnaround refuel clears _switchedOffThisLeg and arms — proving no intent latch stuck.
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, wingPumps: true));
    }

    // ---- Enable-edge interaction with the manual-off latch ----

    [Fact]
    public void DisabledManualOff_ClearedByEnableEdge_ArmPossible()
    {
        var a = Make();
        a.Update(false, true, true, 5000, true, true, NominalTickMs);   // disabled, pumps ON (rising)
        a.Update(false, true, true, 5000, false, true, NominalTickMs);  // disabled, falling → latch may set
        a.Update(false, true, true, 5000, false, true, NominalTickMs);  // disabled, no edge
        // Enable tick, NO edge: step 1 clears all policy latches → arm fires this tick.
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, pumpsOn: false, wingPumps: true));
    }

    [Fact]
    public void EnableTickFallingEdge_LatchesManualOff()
    {
        var a = Make();
        for (int i = 0; i < 3; i++)
            a.Update(false, true, true, 5000, true, true, NominalTickMs); // disabled, pumps ON
        // Single tick that is BOTH the enable edge AND the falling edge → latch set post-clear.
        Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: false, wingPumps: true));
        for (int i = 0; i < 5; i++)
            Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: false, wingPumps: true));
        // A genuine ground refuel uplift (> floor 5000 + RefuelMarginLbs) clears the latch → arm.
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, wingPumps: true));
    }

    // ---- !dataReady inertness ----

    [Fact]
    public void DataNotReady_TouchesNothing_ThenForcesFreshConfirm()
    {
        var a = Make();
        for (int i = 0; i < 5; i++)
            Assert.Equal(Action.None,
                a.Update(true, false, false, 900, true, false, NominalTickMs)); // dataReady:false
        // Data returns with pumps on + low qty → fresh confirm needs 2 continuous ticks.
        Assert.Equal(Action.None, Tick(a, false, 900, pumpsOn: true, wingPumps: false));
        Assert.Equal(Action.TurnOff, Tick(a, false, 900, pumpsOn: true, wingPumps: false));
    }

    // A data gap must NOT burn the pending-confirm window (I2): _pendingMs is not accrued while
    // !dataReady.
    [Fact]
    public void DataGap_DoesNotBurnPendingConfirmWindow()
    {
        var a = Make();
        DriveToDryOff(a, 900);                        // TurnOff; pending Off
        for (int i = 0; i < 20; i++)                  // 40 s of gap — would time out the 30 s confirm IF accrued
            a.Update(true, false, false, 900, true, false, 2000);   // dataReady:false
        var actions = new List<Action>();
        for (int i = 0; i < 15; i++)
            actions.Add(a.Update(true, true, false, 900, true, false, NominalTickMs));
        Assert.DoesNotContain(Action.TurnOff, actions);
    }

    // ---- Other invariants ----

    [Fact]
    public void ConstantsInvariants()
    {
        Assert.True(CenterFuelPumpAutomation.ArmThresholdLbs > CenterFuelPumpAutomation.OffThresholdLbs);
        Assert.True(CenterFuelPumpAutomation.OffThresholdLbs > CenterFuelPumpAutomation.RefuelMarginLbs);
    }

    [Fact]
    public void InFlightArming_Impossible()
    {
        var a = Make();
        for (int i = 0; i < 60; i++)
            Assert.Equal(Action.None, Tick(a, onGround: false, 5000, pumpsOn: false, wingPumps: true));
    }

    [Fact]
    public void ManualOff_ThenGroundRefuel_ReArms()
    {
        var a = Make();
        Tick(a, true, 8000, pumpsOn: true, wingPumps: true);   // rising edge
        Tick(a, true, 8000, pumpsOn: false, wingPumps: true);  // falling → latch, seed 8000
        Assert.Equal(Action.None, Tick(a, true, 8000, pumpsOn: false, wingPumps: true));
        Assert.Equal(Action.TurnOn, Tick(a, true, 8300, pumpsOn: false, wingPumps: true)); // > 8250
    }

    [Fact]
    public void ManualOff_AfterEarlierLowerReading_DoesNotClearImmediately()
    {
        var a = Make();
        Tick(a, true, 5000, pumpsOn: true, wingPumps: true);   // observe 5000 (no latch)
        Tick(a, true, 6000, pumpsOn: true, wingPumps: true);   // rise to 6000 (no latch)
        Tick(a, true, 6000, pumpsOn: false, wingPumps: true);  // falling → latch, seed 6000
        Assert.Equal(Action.None, Tick(a, true, 6000, pumpsOn: false, wingPumps: true)); // 6000 < 6250
    }

    [Fact]
    public void ManualOff_ThenRisingEdge_ClearsLatch()
    {
        var a = Make();
        Tick(a, true, 8000, pumpsOn: true, wingPumps: true);   // rising edge
        Tick(a, true, 8000, pumpsOn: false, wingPumps: true);  // falling → latch
        Tick(a, true, 8000, pumpsOn: true, wingPumps: true);   // rising → _manualOffLatch cleared
        Tick(a, true, 8000, pumpsOn: false, wingPumps: false); // falling, wing off → no NEW latch
        Assert.Equal(Action.TurnOn, Tick(a, true, 8000, pumpsOn: false, wingPumps: true));
    }

    // Re-enabling mid below-threshold confirm must emit exactly one TurnOff — the confirm window
    // is not gated on `enabled`, only the decision is.
    [Fact]
    public void ReEnableMidBelowThresholdConfirm_EmitsExactlyOneTurnOff()
    {
        var a = Make();
        bool pumpsOn = true;
        a.Update(false, true, true, 900, pumpsOn, true, NominalTickMs); // disabled, rising edge, 1 s below threshold
        var actions = new List<Action>();
        for (int i = 0; i < 10; i++)
        {
            Action r = a.Update(true, true, true, 900, pumpsOn, true, NominalTickMs);
            actions.Add(r);
            if (r == Action.TurnOff) pumpsOn = false;
            else if (r == Action.TurnOn) pumpsOn = true;
        }
        Assert.Single(actions.FindAll(x => x == Action.TurnOff));
    }

    // ---- Helper: drive a rising edge + two continuous below-threshold ticks to a confirmed TurnOff.
    //      dryOffQty MUST be < OffThresholdLbs. ----
    private static void DriveToDryOff(CenterFuelPumpAutomation a, double dryOffQty)
    {
        a.Update(true, true, false, dryOffQty, true, false, NominalTickMs); // rising edge; 1st below-threshold tick
        Action last = a.Update(true, true, false, dryOffQty, true, false, NominalTickMs); // 2nd tick → confirm
        Assert.Equal(Action.TurnOff, last);
    }
}
