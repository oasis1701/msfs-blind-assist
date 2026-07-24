using MSFSBlindAssist.Automation;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class UniversalAutomationServiceTests
{
    private readonly List<string> events = new();
    private readonly List<string> spoken = new();
    private UniversalAutomationService Make() => new(events.Add, spoken.Add);

    [Fact]
    public void GearUp_FiresOnceOnPositiveRateAboveFiftyFeet()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.Update(altitudeMsl: 500, verticalSpeedFpm: 800, altitudeAgl: 100);
        s.Update(altitudeMsl: 700, verticalSpeedFpm: 800, altitudeAgl: 300);
        Assert.Equal(new[] { "GEAR_UP" }, events);
    }

    [Fact]
    public void GearUp_DoesNotFireBelowFiftyFeetOrWhenLevel()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.Update(200, 800, 40);    // too low
        s.Update(200, 0,   400);   // not climbing
        Assert.Empty(events);
    }

    [Fact]
    public void GearDown_FiresOnceDescendingThroughTwoThousandAgl()
    {
        var s = Make();
        s.AutoGearDownEnabled = true;
        s.Update(5000, 800, 5000);   // airborne, clear on-ground latch
        s.Update(3000, -700, 1500);  // descending through the window
        s.Update(2800, -700, 1200);
        Assert.Equal(new[] { "GEAR_DOWN" }, events);
    }

    [Fact]
    public void GearDown_RearmsAboveThreeThousandForGoAround()
    {
        var s = Make();
        s.AutoGearDownEnabled = true;
        s.Update(5000, 800, 5000);
        s.Update(3000, -700, 1500);  // GEAR_DOWN #1
        s.Update(6000, 1200, 3500);  // go-around: above 3000 AGL re-arms
        s.Update(3000, -700, 1500);  // GEAR_DOWN #2
        Assert.Equal(new[] { "GEAR_DOWN", "GEAR_DOWN" }, events);
    }

    [Fact]
    public void Ap_EngagesOnceAtConfiguredAgl()
    {
        var s = Make();
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 350;
        s.Update(300, 900, 300);   // below engage height
        s.Update(500, 900, 400);   // through 350
        s.Update(700, 900, 600);
        Assert.Equal(new[] { "AUTOPILOT_ON" }, events);
        Assert.Contains("350 feet. Autopilot engaged.", spoken);
    }

    // When an aircraft-routed AP-engage delegate is supplied (PMDG CMD A / A/P L), it is
    // used instead of the stock AUTOPILOT_ON event.
    [Fact]
    public void Ap_UsesInjectedEngageDelegateWhenProvided()
    {
        int engaged = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add, () => engaged++);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 350;
        s.Update(300, 900, 300);
        s.Update(500, 900, 400);   // through 350
        s.Update(700, 900, 600);
        Assert.Equal(1, engaged);
        Assert.DoesNotContain("AUTOPILOT_ON", events);   // stock event NOT used
        Assert.Contains("350 feet. Autopilot engaged.", spoken);
    }

    [Fact]
    public void Touchdown_ResetsGearUpAndApLatches()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.Update(500, 800, 400);   // GEAR_UP #1
        s.Update(0, 0, 5);         // on ground (AGL<20) after being airborne -> reset
        s.Update(500, 800, 400);   // GEAR_UP #2
        Assert.Equal(new[] { "GEAR_UP", "GEAR_UP" }, events);
    }

    [Fact]
    public void Disabled_NeverActuates()
    {
        var s = Make();
        s.Update(500, 800, 400);
        Assert.Empty(events);
        Assert.False(s.AnyEnabled);
    }
}
