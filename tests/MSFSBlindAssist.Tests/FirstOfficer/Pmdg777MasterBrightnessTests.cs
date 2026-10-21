// PMDG's Preflight Procedure (First Officer) in B777_Checklist.xml reads "MASTER BRIGHTNESS switch:
// ON" (switch_2801, the SDK's LTS_MasterBright_Sw_ON, pressed by EVT_OH_MASTER_BRIGHT_PUSH). The
// First Officer's line read "Indicator Lights master: ON" and its Cockpit Prep step "Lights master:
// ON", yet both moved the INDICATOR LIGHTS switch (TEST / BRT / DIM, EVT_OH_LIGHTS_IND_LTS_SWITCH)
// to BRT, a switch PMDG lists "As needed", and nothing set or read the master switch (owner,
// 2026-10-19). The line and the step now name and drive the master switch, a push button pressed
// only over a switch read as off.

using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests;

public class Pmdg777MasterBrightnessTests
{
    private const string Field = "LTS_MasterBright_Sw_ON";

    private static System.Collections.Generic.List<string> FlowStepIds(string flowId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Select(s => s.Id).ToList();

    private static System.Collections.Generic.List<string> ItemIds(string groupId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Select(i => i.Id).ToList();

    private static MSFSBlindAssist.FirstOfficer.Models.FlowStep<AircraftStateEvaluator> Step(string flowId, string stepId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    private static MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> Item(
        string groupId, string itemId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(double.NaN, false)]
    public void Preflight_line_reads_the_master_brightness_switch(double value, bool expected)
    {
        var item = Item("PREFLIGHT", "PF_MASTER_BRIGHT");
        Assert.Equal("Master Brightness switch: ON", item.Label);
        Assert.Equal(Field, item.StateFieldName);
        Assert.NotNull(item.CheckAction);
        Assert.Equal(expected, item.EvaluateState(value));
    }

    [Fact]
    public void Cockpit_prep_presses_the_master_brightness_switch_and_verifies_it()
    {
        var step = Step("COCKPIT_PREP", "CP_MASTER_BRIGHT");
        Assert.Equal("EVT_OH_MASTER_BRIGHT_PUSH", step.EventName);
        Assert.Equal(new[] { "PF_MASTER_BRIGHT" }, step.LinkedChecklistItemIds.ToArray());
        Assert.Equal(Field, step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(1));
        Assert.False(step.VerifyCondition!(0));
        Assert.False(step.VerifyCondition!(double.NaN));
    }

    // A push button flips whatever the switch is: it is pressed only over a READ switch, so an
    // unread one is left alone aloud, never pressed and never "Already set".
    [Fact]
    public void Cockpit_prep_never_presses_an_unread_switch()
    {
        var step = Step("COCKPIT_PREP", "CP_MASTER_BRIGHT");
        var unread = new AircraftStateEvaluator();
        Assert.True(step.LeaveAloneWhen!(unread));
        Assert.False(step.SkipCondition!(unread));
    }

    [Fact]
    public void Line_and_step_keep_their_place_after_the_signs()
    {
        var cp = FlowStepIds("COCKPIT_PREP");
        Assert.Equal(cp.IndexOf("CP_NO_SMOKING") + 1, cp.IndexOf("CP_MASTER_BRIGHT"));
        var items = ItemIds("PREFLIGHT");
        Assert.Equal(items.IndexOf("PF_NO_SMOKING") + 1, items.IndexOf("PF_MASTER_BRIGHT"));
    }

    [Fact]
    public void Nothing_moves_the_indicator_lights_switch_or_names_it_master_any_more()
    {
        Assert.DoesNotContain(PMDG777FlowDefinitions.Build().SelectMany(f => f.Steps),
            s => s.EventName == "EVT_OH_LIGHTS_IND_LTS_SWITCH");
        Assert.DoesNotContain(PMDG777ChecklistDefinitions.Build().SelectMany(g => g.Items),
            i => i.Label.Contains("Indicator Lights", System.StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("PF_MASTER_LIGHTS", ItemIds("PREFLIGHT"));
    }
}
