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
    [InlineData("PF_OXY_TEST_CAPT")]
    [InlineData("PF_OXY_TEST_FO")]
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

    [Theory]
    // One step per side, each ticking its OWN Preflight item, so either mask can be tested
    // alone (user ruling 2026-08-16). The crew's single "Oxygen: TESTED, 100%" readback line
    // is a separate item in PREFLIGHT_CL — see PreflightReadbackKeepsOneOxygenLine below.
    [InlineData("PF_OXY_TEST_CAPT", "OXY_TEST_CAPT", "PF_OXY_TEST_CAPT")]
    [InlineData("PF_OXY_TEST_FO", "OXY_TEST_FO", "PF_OXY_TEST_FO")]
    public void B737_PreflightFlow_HasOxygenTestStep(string stepId, string pseudoKey, string? itemId)
    {
        var flow = B737.PMDG737FlowDefinitions.Build().First(f => f.Id == "PREFLIGHT");
        var step = flow.Steps.FirstOrDefault(s => s.Id == stepId);
        Assert.NotNull(step);
        Assert.Equal(FlowStepActionType.SetSwitch, step!.ActionType);
        Assert.Equal(pseudoKey, step.EventName);
        Assert.Equal(itemId, step.CompletesChecklistItemId);
    }

    [Fact]
    public void B737_OxygenTestsPrecedeFireTest()
    {
        // Quick oxygen-flow blips lead the flow so they never sit under the fire
        // bell / TCAS callouts.
        var steps = B737.PMDG737FlowDefinitions.Build()
            .First(f => f.Id == "PREFLIGHT").Steps.Select(s => s.Id).ToList();
        int oxyCapt = steps.IndexOf("PF_OXY_TEST_CAPT");
        int oxyFo = steps.IndexOf("PF_OXY_TEST_FO");
        int fire = steps.IndexOf("PF_FIRE_TEST");
        Assert.True(oxyCapt >= 0 && oxyFo >= 0 && fire >= 0);
        Assert.Equal(oxyCapt + 1, oxyFo);
        Assert.True(oxyFo < fire, "oxygen tests must precede the fire test");
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
        Assert.True(wxr > tcas + 1, "WXR must be well separated from TCAS");
        // WXR runs before GPWS: WXR is fully awaited (audio done before the next step);
        // GPWS only fires-and-returns (callouts trail after), so GPWS must be last.
        Assert.Equal(steps.Count - 2, wxr);                   // WXR second-to-last
        Assert.Equal(steps.Count - 1, gpws);                  // GPWS concludes the flow
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

    // The two groups are deliberately shaped DIFFERENTLY and this pins the difference (it
    // was mis-collapsed twice): the Preflight ACTION group carries one oxygen item per side
    // so either mask can be tested alone, while the read-aloud Preflight Checklist carries
    // the crew's SINGLE "Oxygen ... TESTED, 100%" line — action-free, like every *_CL item.
    [Fact]
    public void PreflightReadbackKeepsOneOxygenLine()
    {
        // The two aircraft's groups are distinct generic types, so each is asserted directly.
        var b737 = B737.PMDG737ChecklistDefinitions.Build().First(g => g.Id == "PREFLIGHT_CL")
            .Items.Where(i => i.Label.Contains("Oxygen", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(b737);
        Assert.Null(b737[0].CheckAction);         // readback only — never fires the test

        var b777 = MSFSBlindAssist.FirstOfficer.PMDG777ChecklistDefinitions.Build()
            .First(g => g.Id == "PREFLIGHT_CL")
            .Items.Where(i => i.Label.Contains("Oxygen", System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(b777);
        Assert.Null(b777[0].CheckAction);
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
    [InlineData("PF_OXY_TEST_CAPT")]
    [InlineData("PF_OXY_TEST_FO")]
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

    [Theory]
    // One step per side, each ticking its own Preflight item (see the 737 theory above).
    [InlineData("CP_OXY_TEST_CAPT", "OXY_TEST_CAPT", "PF_OXY_TEST_CAPT")]
    [InlineData("CP_OXY_TEST_FO", "OXY_TEST_FO", "PF_OXY_TEST_FO")]
    public void B777_CockpitPrepFlow_HasOxygenTestStep(string stepId, string pseudoKey, string? itemId)
    {
        var flows = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions.Build();
        var step = flows.SelectMany(f => f.Steps).FirstOrDefault(s => s.Id == stepId);
        Assert.NotNull(step);
        Assert.Equal(FlowStepActionType.SetSwitch, step!.ActionType);
        Assert.Equal(pseudoKey, step.EventName);
        Assert.Equal(itemId, step.CompletesChecklistItemId);
    }

    [Fact]
    public void B777_OxygenTestsPrecedeFireTest()
    {
        var steps = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions.Build()
            .SelectMany(f => f.Steps).Select(s => s.Id).ToList();
        int oxyCapt = steps.IndexOf("CP_OXY_TEST_CAPT");
        int oxyFo = steps.IndexOf("CP_OXY_TEST_FO");
        int fire = steps.IndexOf("CP_FIRE_TEST");
        Assert.True(oxyCapt >= 0 && oxyFo >= 0 && fire >= 0);
        Assert.Equal(oxyCapt + 1, oxyFo);
        Assert.True(oxyFo < fire, "oxygen tests must precede the fire test");
    }

    [Fact]
    public void B777_HasNoGpwsTest()   // the 777 SDK has no GPWS self-test button
    {
        var allStepEvents = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions.Build()
            .SelectMany(f => f.Steps).Select(s => s.EventName).ToList();
        Assert.DoesNotContain("GPWS_TEST", allStepEvents);
    }
}
