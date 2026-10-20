using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Tests.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The C172 against the universal auto-gear and auto-AP (FO-17, FO-18, FO-27): fixed gear, the GFC 700's
/// 800 ft limitation, and an AP key that toggles, so the engage waits for a definite OFF.
/// </summary>
public class CessnaC172AutomationContractTests
{
    [Fact]
    public void The_C172_has_no_gear_to_move_and_a_floor_of_800_feet()
    {
        var def = new CessnaC172G1000Definition();
        Assert.False(def.HasRetractableGear);
        Assert.Equal(800, def.MinimumAutopilotEngageAltitudeAgl);
    }

    [Fact]
    public void The_AP_key_toggles_so_the_engage_waits_and_outlasts_the_batch()
    {
        var def = new CessnaC172G1000Definition();
        Assert.True(def.AutopilotEngageWaitsForState);   // C172-3: a second press disengages
        Assert.True(def.AutopilotEngageRetryIntervalMs > 1000, "a retry must outlast the 1 Hz batch (DA40S-2)");
    }

    [Fact]
    public void The_engaged_state_is_null_until_read()
    {
        var def = new CessnaC172G1000Definition();
        Assert.Null(def.IsAutopilotEngaged(SeededSimConnectCache.With()));
        Assert.False(def.IsAutopilotEngaged(SeededSimConnectCache.With(("C172_AP_MASTER_STATE", 0))));
        Assert.True(def.IsAutopilotEngaged(SeededSimConnectCache.With(("C172_AP_MASTER_STATE", 1))));
    }
}
