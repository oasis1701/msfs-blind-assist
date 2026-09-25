// The FCU callouts are judged at the END of a continuous batch (FcuValueAnnouncer's batch-end release):
// a staged value change is released or dropped against the FCU health var as it stands when that batch
// finishes. That is only sound when the health var arrives in the SAME batch message as the values it
// judges — a health var in another batch would be one delivery stale, so an FCU powering down could
// have its zeroed values released as knob turns. Batch membership is computed by the production
// layout (ContinuousBatchLayout, the one StartContinuousMonitoring uses): batch-covered vars sorted by
// full name ("L:" prefix for L:vars), 300 per batch. Adding variables moves the boundaries, so this
// pins the property rather than any index.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FcuHealthBatchMembershipTests
{
    private static void AssertSameBatch(IAircraftDefinition def, string healthKey, params string[] valueKeys)
    {
        var batches = ContinuousBatchLayout.BatchNumbers(def.GetVariables());
        Assert.True(batches.TryGetValue(healthKey, out int healthBatch), $"{healthKey} is not batch-covered");
        foreach (string key in valueKeys)
        {
            Assert.True(batches.TryGetValue(key, out int batch), $"{key} is not batch-covered");
            Assert.True(batch == healthBatch,
                $"{def.AircraftName}: {key} rides batch {batch} but the health var {healthKey} rides batch {healthBatch}");
        }
    }

    [Fact]
    public void The_a380_fcu_health_var_shares_a_batch_with_the_heading_speed_and_altitude_sources()
    {
        AssertSameBatch(new FlyByWireA380Definition(), "A32NX_FCU_AFS_CP_ACTIVE",
            "A32NX_AUTOPILOT_HEADING_SELECTED", "A32NX_AUTOPILOT_SPEED_SELECTED", "FCU_ALT_VALUE");
    }

    [Fact]
    public void The_a32nx_fcu_health_var_shares_a_batch_with_the_heading_and_speed_shims()
    {
        AssertSameBatch(new FlyByWireA320Definition(), "A32NX_FCU_HEALTHY",
            "A32NX_AUTOPILOT_HEADING_SELECTED", "A32NX_AUTOPILOT_SPEED_SELECTED");
    }

    [Fact]
    public void The_a330_fcu_health_var_shares_a_batch_with_the_heading_and_speed_shims()
    {
        AssertSameBatch(new HeadwindA330Definition(), "A32NX_FCU_HEALTHY",
            "A32NX_AUTOPILOT_HEADING_SELECTED", "A32NX_AUTOPILOT_SPEED_SELECTED");
    }

    [Fact]
    public void Batches_are_300_vars_in_ordinal_full_name_order_with_the_L_prefix()
    {
        // The layout rule itself, on a synthetic set: an L:var sorts under "L:", so it lands after a
        // stock name beginning with 'A' and before one beginning with 'Z'; ExcludeFromBatch, OnRequest,
        // unannounced and PMDG vars never ride a batch.
        var vars = new Dictionary<string, SimVarDefinition>();
        for (int i = 0; i < 300; i++)
            vars[$"A{i:000}"] = new SimVarDefinition { Name = $"AAA {i:000}", Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true };
        vars["lvar"] = new SimVarDefinition { Name = "AAB", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true };
        vars["zed"] = new SimVarDefinition { Name = "ZED", Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true };
        vars["own"] = new SimVarDefinition { Name = "AAA 000X", Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromBatch = true };
        vars["req"] = new SimVarDefinition { Name = "AAA 000Y", Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = true };
        vars["quiet"] = new SimVarDefinition { Name = "AAA 000Z", Type = SimVarType.SimVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = false };

        var batches = ContinuousBatchLayout.BatchNumbers(vars);

        Assert.Equal(302, batches.Count);
        Assert.Equal(1, batches["A000"]);
        Assert.Equal(1, batches["A299"]);
        Assert.Equal(2, batches["lvar"]);   // "L:AAB" sorts after every "AAA …"
        Assert.Equal(2, batches["zed"]);
        Assert.False(batches.ContainsKey("own"));
        Assert.False(batches.ContainsKey("req"));
        Assert.False(batches.ContainsKey("quiet"));
    }
}
