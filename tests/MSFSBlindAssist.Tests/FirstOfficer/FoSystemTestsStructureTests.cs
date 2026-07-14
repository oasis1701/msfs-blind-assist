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
    public void B737_TcasStaysEarly_GpwsAndWxrConcludeFlow()
    {
        // The three test audios must not overlap: TCAS stays with the warning tests
        // (its "TEST PASS" plays ~8 s later, over the following preflight steps), while
        // GPWS + WXR are moved to the very end so nothing else's test audio collides.
        var steps = B737.PMDG737FlowDefinitions.Build()
            .First(f => f.Id == "PREFLIGHT").Steps.Select(s => s.Id).ToList();
        int ovspd2 = steps.IndexOf("PF_OVSPD_TEST2");
        int tcas = steps.IndexOf("PF_TCAS_TEST");
        int gpws = steps.IndexOf("PF_GPWS_TEST");
        int wxr = steps.IndexOf("PF_WXR_TEST");
        Assert.True(ovspd2 >= 0 && tcas >= 0 && gpws >= 0 && wxr >= 0);
        Assert.Equal(ovspd2 + 1, tcas);                       // TCAS stays with warning tests
        Assert.True(gpws > tcas + 1, "GPWS must be well separated from TCAS");
        Assert.Equal(steps.Count - 2, gpws);                  // GPWS second-to-last
        Assert.Equal(steps.Count - 1, wxr);                   // WXR concludes the flow
    }

    [Fact]
    public void B777_TcasStaysEarly_WxrConcludesFlow()
    {
        var steps = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions.Build()
            .SelectMany(f => f.Steps).Select(s => s.Id).ToList();
        int fire = steps.IndexOf("CP_FIRE_TEST");
        int tcas = steps.IndexOf("CP_TCAS_TEST");
        int wxr = steps.IndexOf("CP_WXR_TEST");
        Assert.True(fire >= 0 && tcas >= 0 && wxr >= 0);
        Assert.Equal(fire + 1, tcas);                         // TCAS stays with the fire test
        Assert.True(wxr > tcas + 1, "WXR must be well separated from TCAS");
    }

    [Theory]
    [InlineData("CP_TCAS_TEST", "TCAS_TEST", "PF_TCAS_TEST")]
    [InlineData("CP_WXR_TEST", "WXR_TEST", "PF_WXR_TEST")]
    public void B777_CockpitPrepFlow_HasSystemTestStep(string stepId, string pseudoKey, string itemId)
    {
        var flows = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions.Build();
        var step = flows.SelectMany(f => f.Steps).FirstOrDefault(s => s.Id == stepId);
        Assert.NotNull(step);
        Assert.Equal(FlowStepActionType.SetSwitch, step!.ActionType);
        Assert.Equal(pseudoKey, step.EventName);
        Assert.Equal(itemId, step.CompletesChecklistItemId);
    }

    [Theory]
    [InlineData("PF_TCAS_TEST")]
    [InlineData("PF_WXR_TEST")]
    public void B777_PreflightChecklist_HasManualTestItem(string itemId)
    {
        var group = MSFSBlindAssist.FirstOfficer.PMDG777ChecklistDefinitions.Build()
            .First(g => g.Id == "PREFLIGHT");
        var item = group.Items.FirstOrDefault(i => i.Id == itemId);
        Assert.NotNull(item);
        Assert.Equal(ChecklistItemType.Actionable, item!.Type);
        Assert.True(item.ManualCompletionAllowed);
        Assert.NotNull(item.CheckAction);
    }

    [Fact]
    public void B777_HasNoGpwsTest()   // the 777 SDK has no GPWS self-test button
    {
        var allStepEvents = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions.Build()
            .SelectMany(f => f.Steps).Select(s => s.EventName).ToList();
        Assert.DoesNotContain("GPWS_TEST", allStepEvents);
    }
}
