using MSFSBlindAssist.FirstOfficer;
using Xunit;
using Action = MSFSBlindAssist.FirstOfficer.CenterFuelPumpAutomation.Action;

namespace MSFSBlindAssist.Tests;

public class CenterFuelPumpAutomationTests
{
    private static CenterFuelPumpAutomation Make() => new();

    // enabled, onGround, centerQty, centerPumpsOn, lowPressRaw, wingPumpsOn
    private static Action Tick(CenterFuelPumpAutomation a, bool onGround, double qty,
        bool pumpsOn, bool lowPress, bool wingPumps, bool enabled = true)
        => a.Update(enabled, onGround, qty, pumpsOn, lowPress, wingPumps);

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
        // Pumps already on (no prior arm → no settle window), tank dry, low-press lit.
        for (int i = 0; i < CenterFuelPumpAutomation.LowPressConfirmTicks - 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: true, wingPumps: false));
        Assert.Equal(Action.TurnOff, Tick(a, false, 100, true, true, false));
    }

    [Fact]
    public void FlickeringLowPress_DoesNotTurnOff()
    {
        var a = Make();
        foreach (bool lit in new[] { true, true, false, true, true, false })
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: lit, wingPumps: false));
    }

    [Fact]
    public void SettleWindow_SuppressesImmediateOffAfterArming()
    {
        var a = Make();
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, false, false, true));   // arms, starts settle
        // Pumps now on; even with low-press lit, no off until the settle window drains.
        for (int i = 0; i < CenterFuelPumpAutomation.SettleTicksAfterOn - 1; i++)
            Assert.Equal(Action.None, Tick(a, false, 100, pumpsOn: true, lowPress: true, wingPumps: false));
        Assert.Equal(Action.TurnOff, Tick(a, false, 100, true, true, false));
    }

    [Fact]
    public void OnceOff_StaysOffUntilRefuelOnGround()
    {
        var a = Make();
        // Drive to a dry TurnOff (pumps on, no settle since no prior arm).
        for (int i = 0; i < CenterFuelPumpAutomation.LowPressConfirmTicks - 1; i++)
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
        for (int i = 0; i < CenterFuelPumpAutomation.LowPressConfirmTicks; i++)
            Tick(a, false, 100, true, true, false);   // dry-off latch set
        a.Reset();
        // After reset, a fresh ground arm works again immediately.
        Assert.Equal(Action.TurnOn, Tick(a, true, 5000, false, false, true));
    }
}
