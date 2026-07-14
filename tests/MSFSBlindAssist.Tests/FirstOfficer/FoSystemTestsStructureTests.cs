using System.Linq;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural invariants for the 2026-07-13 PMDG system tests (TCAS / WXR / GPWS):
/// each preflight flow test step uses its executor pseudo-key, completes a checklist
/// item that exists in the PREFLIGHT group, and that item is an Actionable manual tick
/// (never Auto — no persistent "test performed" state exists in either PMDG SDK).
/// </summary>
public class FoSystemTestsStructureTests
{
    [Theory]
    [InlineData("PF_GPWS_TEST", "GPWS_TEST")]
    [InlineData("PF_TCAS_TEST", "TCAS_TEST")]
    [InlineData("PF_WXR_TEST", "WXR_TEST")]
    public void B737_PreflightFlow_HasSystemTestStep(string stepId, string pseudoKey)
    {
        var flow = B737.PMDG737FlowDefinitions.Build().First(f => f.Id == "PREFLIGHT");
        var step = flow.Steps.FirstOrDefault(s => s.Id == stepId);
        Assert.NotNull(step);
        Assert.Equal(FlowStepActionType.SetSwitch, step!.ActionType);
        Assert.Equal(pseudoKey, step.EventName);
        Assert.Equal(stepId, step.CompletesChecklistItemId);
    }

    [Theory]
    [InlineData("PF_GPWS_TEST")]
    [InlineData("PF_TCAS_TEST")]
    [InlineData("PF_WXR_TEST")]
    public void B737_PreflightChecklist_HasManualTestItem(string itemId)
    {
        var group = B737.PMDG737ChecklistDefinitions.Build().First(g => g.Id == "PREFLIGHT");
        var item = group.Items.FirstOrDefault(i => i.Id == itemId);
        Assert.NotNull(item);
        Assert.Equal(ChecklistItemType.Actionable, item!.Type);
        Assert.True(item.ManualCompletionAllowed);
        Assert.NotNull(item.CheckAction);
        Assert.Null(item.StateFieldName);   // never Auto — no readable test state
    }

    [Fact]
    public void B737_TestsFollowOverspeedTests()
    {
        var steps = B737.PMDG737FlowDefinitions.Build()
            .First(f => f.Id == "PREFLIGHT").Steps.Select(s => s.Id).ToList();
        int ovspd2 = steps.IndexOf("PF_OVSPD_TEST2");
        Assert.True(ovspd2 >= 0);
        Assert.Equal(ovspd2 + 1, steps.IndexOf("PF_GPWS_TEST"));
        Assert.Equal(ovspd2 + 2, steps.IndexOf("PF_TCAS_TEST"));
        Assert.Equal(ovspd2 + 3, steps.IndexOf("PF_WXR_TEST"));
    }
}
