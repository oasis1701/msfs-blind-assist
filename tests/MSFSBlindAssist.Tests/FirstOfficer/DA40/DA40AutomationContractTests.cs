using System;
using System.Collections.Generic;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.Automation;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.DA40;

/// <summary>
/// The DA40 against the universal auto-gear and auto-AP (FO-17, FO-18, FO-27): fixed gear that the
/// simulator reports retractable, the GFC 700's 800 ft limitation, and an engaged state that is null
/// until read.
/// </summary>
public class DA40AutomationContractTests
{
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void The_DA40_has_no_gear_to_move_and_a_floor_of_800_feet(DA40Variant v)
    {
        var def = new CowsDA40Definition(v);
        Assert.False(def.HasRetractableGear);
        Assert.Equal(800, def.MinimumAutopilotEngageAltitudeAgl);
        Assert.False(def.AutopilotEngageWaitsForState);   // AUTOPILOT_ON is ON, never a toggle
        Assert.True(def.AutopilotEngageRetryIntervalMs > 1000, "a retry must outlast the 1 Hz batch (DA40S-2)");
    }

    [Fact]
    public void The_engaged_state_is_null_until_read()
    {
        var def = new CowsDA40Definition();
        Assert.Null(def.IsAutopilotEngaged(SeededSimConnectCache.With()));
        Assert.False(def.IsAutopilotEngaged(SeededSimConnectCache.With(("DA40_AP_MASTER", 0))));
        Assert.True(def.IsAutopilotEngaged(SeededSimConnectCache.With(("DA40_AP_MASTER", 1))));
    }

    [Fact]
    public void Without_gear_neither_gear_call_acts_or_speaks()
    {
        var events = new List<string>();
        var spoken = new List<string>();
        var s = new UniversalAutomationService(events.Add, spoken.Add, isGearHandleDown: () => false)
        {
            AutoGearUpEnabled = true, AutoGearDownEnabled = true, HasRetractableGear = false,
        };
        s.Update(0, 0, 0, onGround: true);
        s.Update(500, 800, 300, onGround: false);    // a climb: gear up would fire
        s.Update(5000, 800, 5000, onGround: false);
        s.Update(3000, -700, 1500, onGround: false);  // the descent band: gear down would fire
        Assert.Empty(events);
        Assert.Empty(spoken);
    }

    [Fact]
    public void Every_other_aircraft_keeps_its_gear()
        => Assert.True(new MSFSBlindAssist.Aircraft.YourAircraftDefinition().HasRetractableGear);
}
