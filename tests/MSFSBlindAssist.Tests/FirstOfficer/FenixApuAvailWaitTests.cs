using System;
using System.Linq;
using Xunit;

using FenixChecklist = MSFSBlindAssist.FirstOfficer.Fenix.FenixChecklistDefinitions;
using FenixFlows = MSFSBlindAssist.FirstOfficer.Fenix.FenixFlowDefinitions;
using FenixExec = MSFSBlindAssist.FirstOfficer.Fenix.FenixActionExecutor;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Fenix A320 — "Unable to complete: APU: ON and available" on a healthy APU start.
/// StartApuAsync returned at the START pulse while BS_APU detects on the AVAIL lamp,
/// which lights ~45 s later; the checklist's revert grace is ~10 s past the action.
/// </summary>
public class FenixApuAvailWaitTests
{

    // ==================================================================
    // Fenix — the APU wait
    // ==================================================================

    /// <summary>The executor waits on the same lamp BS_APU/AL_APU detect on. If these ever
    /// diverge the wait would end on one condition while the checklist judged another — the
    /// exact shape of the bug being fixed.</summary>
    [Theory]
    [InlineData("BEFORE_START", "BS_APU")]
    [InlineData("AFTER_LANDING", "AL_APU")]
    public void Fenix_apu_items_detect_on_the_lamp_the_executor_waits_for(string groupId, string itemId)
    {
        var item = FenixChecklist.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

        Assert.Equal(FenixExec.ApuAvailField, item.StateFieldName);
        Assert.True(item.EvaluateState(1));
        Assert.False(item.EvaluateState(0));
    }

    /// <summary>The lamp test the executor polls agrees with the checklist condition, and an
    /// unread (null) cache keeps waiting rather than reading as available.</summary>
    [Fact]
    public void Apu_available_test_matches_the_checklist_condition()
    {
        Assert.True(FenixExec.IsApuAvailable(1));
        Assert.False(FenixExec.IsApuAvailable(0));
        Assert.False(FenixExec.IsApuAvailable(null));
        Assert.False(FenixExec.IsApuAvailable(double.NaN));
    }

    /// <summary>The wait budget has to outlast a real A320 APU start (~45 s) by a wide margin,
    /// and matches the Before Start flow's own 180 s WaitForField so both paths give up
    /// together. Anything near the ChecklistManager's 10 s ManualTickGrace reproduces the bug.</summary>
    [Fact]
    public void Apu_wait_budget_matches_the_flow_and_dwarfs_the_revert_grace()
    {
        Assert.Equal(180_000, FenixExec.ApuAvailTimeoutMs);

        var wait = FenixFlows.Build()
            .Single(f => f.Id == "BEFORE_START").Steps
            .Single(s => s.Id == "BS_APU_AVAIL");
        Assert.Equal(FenixExec.ApuAvailField, wait.ConditionFieldName);
        Assert.Equal(FenixExec.ApuAvailTimeoutMs / 1000, wait.TimeoutSeconds);
    }
}
