using MSFSBlindAssist.FirstOfficer;
using Xunit;
using Action = MSFSBlindAssist.FirstOfficer.CenterFuelPumpAutomation.Action;

namespace MSFSBlindAssist.Tests;

public class CenterFuelPumpAutomationTests
{
    // A nominal tick spacing that mirrors the real ~1 Hz AircraftPositionReceived feed the
    // constants (LowPressConfirmSeconds / SettleSecondsAfterOn) were tuned against.
    private const double NominalTickMs = 1000;

    private static CenterFuelPumpAutomation Make() => new();

    // enabled, onGround, centerQty, centerPumpsOn, lowPressRaw, wingPumpsOn, elapsedMs
    private static Action Tick(CenterFuelPumpAutomation a, bool onGround, double qty,
        bool pumpsOn, bool lowPress, bool wingPumps, bool enabled = true,
        double elapsedMs = NominalTickMs)
        => a.Update(enabled, onGround, qty, pumpsOn, lowPress, wingPumps, elapsedMs);

    // Drives enough post-edge decay ticks (pumps already observed on, low-press quiet) to
    // fully exhaust the post-switch-on settle window, so a subsequent test can exercise the
    // low-press debounce in isolation. Must be called AFTER a tick that established the
    // pumps-on rising edge (i.e. _prevPumpsOn is already true), or this itself would
    // re-trigger a fresh edge and reset the window instead of draining it.
    private static void DrainSettleWindow(CenterFuelPumpAutomation a, double qty = 100)
    {
        double remaining = CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000;
        while (remaining > 0)
        {
            double step = System.Math.Min(remaining, 2000); // respect the policy's own clamp
            a.Update(true, false, qty, true, false, false, step);
            remaining -= step;
        }
    }

    [Fact]
    public void Disabled_NeverActuates()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, onGround: true, qty: 5000, pumpsOn: false,
            lowPress: false, wingPumps: true, enabled: false));
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
        Assert.Equal(Action.None, Tick(a, true, qty: 100, false, false, true));
    }

    [Fact]
    public void DoesNotArm_WhenPumpsAlreadyOn()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, 5000, pumpsOn: true, false, true));
    }

    [Fact]
    public void TurnsOff_AfterSustainedLowPressWhilePumpsOn()
    {
        var a = Make();
        // Establish the pumps-on rising edge, then drain the settle window so this test
        // exercises ONLY the low-press debounce, matching "pumps were already running" (no
        // fresh switch-on protection in play).
        Tick(a, false, 100, pumpsOn: true, lowPress: false, wingPumps: false);
        DrainSettleWindow(a);
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks - 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: true, wingPumps: false));
        Assert.Equal(Action.TurnOff, Tick(a, false, 100, true, true, false));
    }

    [Fact]
    public void FlickeringLowPress_DoesNotTurnOff()
    {
        var a = Make();
        Tick(a, false, 100, pumpsOn: true, lowPress: false, wingPumps: false);
        DrainSettleWindow(a);
        foreach (bool lit in new[] { true, true, false, true, true, false })
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: lit, wingPumps: false));
    }

    [Fact]
    public void SettleWindow_SuppressesImmediateOffAfterArming()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, false, false, true));   // arms; no settle set here (FIX 1)
        // Readback tick: centerPumpsOn flips true → THIS is the rising edge that starts the
        // settle window (not the TurnOn call above).
        Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: true, wingPumps: false));
        int settleTicks = (int)(CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000 / NominalTickMs);
        // 9 more ticks (decay ticks 1-9 of the 10-tick window) — still inside the settle
        // window the whole time, even though low-press latches well before the window ends.
        for (int i = 0; i < settleTicks - 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: true, wingPumps: false));
        // Decay tick 10: settle window exhausted, low-press still latched → OFF fires now.
        Assert.Equal(Action.TurnOff, Tick(a, false, 100, true, true, false));
    }

    [Fact]
    public void OnceOff_StaysOffUntilRefuelOnGround()
    {
        var a = Make();
        // Drive to a dry TurnOff (pumps already on, settle drained → no settle protection).
        Tick(a, false, 100, pumpsOn: true, lowPress: false, wingPumps: false);
        DrainSettleWindow(a);
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks - 1; i++)
            Tick(a, false, 100, true, true, false);
        Assert.Equal(Action.TurnOff, Tick(a, false, 100, true, true, false));
        // Back on ground, still dry, pumps off, wing pumps on → must NOT re-arm.
        Assert.Equal(Action.None, Tick(a, true, 100, pumpsOn: false, lowPress: true, wingPumps: true));
        // Turnaround refuel on the ground clears the latch → re-arms.
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, lowPress: false, wingPumps: true));
    }

    [Fact]
    public void Reset_ClearsLatches()
    {
        var a = Make();
        Tick(a, false, 100, pumpsOn: true, lowPress: false, wingPumps: false);
        DrainSettleWindow(a);
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks; i++)
            Tick(a, false, 100, true, true, false);   // dry-off latch set
        a.Reset();
        // After reset, a fresh ground arm works again immediately.
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, false, false, true));
    }

    // --- FIX 1 regression -------------------------------------------------------------
    // The settle window must protect ANY observed center-pump switch-on, not just one the
    // automation itself performed. Reachable failure (pre-fix): pilot switches the center
    // pumps on with a FULL tank while wingPumpsOn is false the whole time (so the automation
    // never arms this switch-on itself, and the old code only ever started the settle window
    // inside its own TurnOn branch) → the low-press spin-up transient gets NO settle
    // protection and falsely fires TurnOff on a full 8000 lb tank.
    //
    // Against the pre-fix code (settle only set in TurnOn branch, tick-counted, no edge
    // tracking) this scenario never sets a settle window at all, so the 3rd consecutive
    // low-press tick (reached partway through the loop below) would return TurnOff where
    // this test asserts None — the test would FAIL against the old code.
    [Fact]
    public void ManualSwitchOn_FullTank_GetsSettleProtection_NeverFalselyTurnsOff()
    {
        var a = Make();
        // wingPumpsOn stays false throughout → the automation never arms this switch-on.
        Assert.Equal(Action.None, Tick(a, true, 8000, pumpsOn: false, lowPress: false, wingPumps: false));
        // Manual switch-on observed here: pumps go false -> true. This is the rising edge.
        Assert.Equal(Action.None, Tick(a, true, 8000, pumpsOn: true, lowPress: true, wingPumps: false));
        int settleTicks = (int)(CenterFuelPumpAutomation.SettleSecondsAfterOn * 1000 / NominalTickMs);
        // Sustained low-press through the rest of the settle window on a FULL tank must
        // never fire TurnOff.
        for (int i = 0; i < settleTicks - 1; i++)
            Assert.Equal(Action.None, Tick(a, true, 8000, pumpsOn: true, lowPress: true, wingPumps: false));
    }

    // --- FIX 2 regression -------------------------------------------------------------
    // A dry-off latch set while quantity never dropped below ArmThresholdLbs must still be
    // clearable by a later ground refuel above the threshold (declaring the tank dry via the
    // low-press annunciator IS the "below arm" observation).
    //
    // Against the pre-fix code (TurnOff only sets _switchedOffThisLeg, never _belowArmSeen),
    // since qty here never dips below the threshold, _belowArmSeen would stay false forever,
    // the refuel branch's `_belowArmSeen &&` guard would never pass, and the final assertion
    // below (TurnOn after refuel) would FAIL — the latch would be permanently stuck, asserting
    // None instead.
    [Fact]
    public void LatchSetAboveThreshold_StillClearableByGroundRefuel()
    {
        var a = Make();
        // Quantity stays >= ArmThresholdLbs throughout — dry-off is annunciator-driven only.
        Tick(a, false, 5000, pumpsOn: true, lowPress: false, wingPumps: false);
        DrainSettleWindow(a, qty: 5000);
        int confirmTicks = (int)(CenterFuelPumpAutomation.LowPressConfirmSeconds * 1000 / NominalTickMs);
        for (int i = 0; i < confirmTicks - 1; i++)
            Tick(a, false, 5000, true, true, false);
        Assert.Equal(Action.TurnOff, Tick(a, false, 5000, true, true, false));
        // Ground refuel above the threshold must re-arm even though qty never dropped below it.
        Assert.Equal(Action.TurnOn, Tick(a, true, 6000, pumpsOn: false, lowPress: false, wingPumps: true));
    }

    // --- FIX 3 clamp --------------------------------------------------------------------
    // An absurd elapsed value (sim-pause / first-call / hitch) must not instantly satisfy a
    // window beyond the 2000 ms per-call clamp.
    [Fact]
    public void AbsurdElapsed_IsClampedAndDoesNotInstantlySatisfyWindow()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: false, lowPress: false, wingPumps: false, elapsedMs: 1));
        // Rising edge with a huge elapsed spike — clamped to 2000 ms, so low-press (needs
        // 3000 ms) cannot latch in this single tick.
        Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: true, wingPumps: false, elapsedMs: 60000));
        // A second huge spike (clamped to 2000 ms) does latch low-press (4000 ms >= 3000 ms)
        // but the settle window (also only decayed by the clamped 2000 ms, now at 8000 ms
        // remaining of 10000) still suppresses OFF — the absurd elapsed value could not blow
        // through either window in one shot.
        Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: true, wingPumps: false, elapsedMs: 60000));
    }

    // --- Exact ArmThresholdLbs boundary (the condition is strictly >, so qty == threshold
    // must NOT arm).
    [Fact]
    public void ArmThreshold_ExactBoundary_DoesNotArm()
    {
        var a = Make();
        Assert.Equal(Action.None, Tick(a, true, qty: CenterFuelPumpAutomation.ArmThresholdLbs,
            pumpsOn: false, lowPress: false, wingPumps: true));
    }

    [Fact]
    public void ArmThreshold_JustAboveBoundary_Arms()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, qty: CenterFuelPumpAutomation.ArmThresholdLbs + 0.01,
            pumpsOn: false, lowPress: false, wingPumps: true));
    }
}
