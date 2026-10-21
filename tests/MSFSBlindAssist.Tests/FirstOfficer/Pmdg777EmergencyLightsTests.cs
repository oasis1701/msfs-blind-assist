// The PMDG 777 emergency lights are armed in Cockpit Prep, under a Preflight line (owner report,
// 2026-10-19). PMDG's Preflight Procedure (First Officer) in B777_Checklist.xml reads "EMERGENCY
// LIGHTS switch: Guard closed" after the backup generators and the L wiper, before the passenger
// oxygen and the window heat; its Secure Procedure turns the switch OFF. The First Officer armed
// it only in the Electrical Power Up flow, which PMDG's Electrical Power Up page does not list,
// and no checklist line named it, so a flight started from a powered panel never re-armed it.
// The Boris Audio Works soundpack plays passenger chatter only with the switch at ARMED or ON,
// so the cabin stayed silent in the 777-300ER.

using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.Tests;

public class Pmdg777EmergencyLightsTests
{
    private static System.Collections.Generic.List<string> FlowStepIds(string flowId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Select(s => s.Id).ToList();

    private static System.Collections.Generic.List<string> ItemIds(string groupId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Select(i => i.Id).ToList();

    private static MSFSBlindAssist.FirstOfficer.Models.FlowStep<AircraftStateEvaluator> Step(string flowId, string stepId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    private static MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> Item(
        string groupId, string itemId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    [Fact]
    public void Preflight_lists_the_emergency_lights_right_after_the_backup_generators()
    {
        var items = ItemIds("PREFLIGHT");
        Assert.Equal(items.IndexOf("PF_BACKUP_GENS") + 1, items.IndexOf("PF_EMER_LIGHTS"));
        Assert.True(items.IndexOf("PF_EMER_LIGHTS") < items.IndexOf("PF_WINDOW_HEAT"));
    }

    // ARMED is the guard-closed position (EmerExitLightSequence): OFF and ON both sit outside the
    // guard, so neither is "Guard closed", and an unread selector is neither.
    [Theory]
    [InlineData(EmerExitLightSequence.Off, false)]
    [InlineData(EmerExitLightSequence.Armed, true)]
    [InlineData(EmerExitLightSequence.On, false)]
    [InlineData(double.NaN, false)]
    public void Preflight_line_reads_complete_only_at_ARMED(double selector, bool expected)
    {
        var item = Item("PREFLIGHT", "PF_EMER_LIGHTS");
        Assert.Equal("LTS_EmerLightsSelector", item.StateFieldName);
        Assert.Equal(expected, item.EvaluateState(selector));
    }

    [Fact]
    public void Preflight_line_arms_the_lights_when_ticked()
    {
        var item = Item("PREFLIGHT", "PF_EMER_LIGHTS");
        Assert.True(item.IsAutoDetectable);
        Assert.NotNull(item.CheckAction);
        Assert.Contains("ARMED", item.Label);
    }

    [Fact]
    public void Cockpit_prep_arms_the_lights_where_PMDGs_preflight_lists_them()
    {
        var cp = FlowStepIds("COCKPIT_PREP");
        Assert.Equal(cp.IndexOf("CP_BACKUP_GENS") + 1, cp.IndexOf("CP_EMER_LIGHTS"));
        Assert.True(cp.IndexOf("CP_EMER_LIGHTS") < cp.IndexOf("CP_WINDOW_HEAT_1"));

        var step = Step("COCKPIT_PREP", "CP_EMER_LIGHTS");
        Assert.Equal("EMER_EXIT_LIGHTS", step.EventName);
        Assert.Equal(EmerExitLightSequence.Armed, step.TargetValue);
        Assert.Equal(new[] { "PF_EMER_LIGHTS" }, step.LinkedChecklistItemIds.ToArray());
    }

    // A write that does not take is heard skipped and keeps the line out of the latch (FO-6).
    [Fact]
    public void Cockpit_prep_verifies_the_lights_reached_ARMED()
    {
        var step = Step("COCKPIT_PREP", "CP_EMER_LIGHTS");
        Assert.Equal("LTS_EmerLightsSelector", step.VerifyFieldName);
        Assert.NotNull(step.VerifyCondition);
        Assert.True(step.VerifyCondition!(EmerExitLightSequence.Armed));
        Assert.False(step.VerifyCondition!(EmerExitLightSequence.Off));
        Assert.False(step.VerifyCondition!(EmerExitLightSequence.On));
        Assert.False(step.VerifyCondition!(double.NaN));
    }

    [Fact]
    public void Cockpit_prep_never_calls_an_unread_selector_already_set()
    {
        var step = Step("COCKPIT_PREP", "CP_EMER_LIGHTS");
        Assert.NotNull(step.SkipCondition);
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    // PMDG's Electrical Power Up page has no emergency-lights line: the arming moved to Cockpit
    // Prep, so the departure flows arm the switch exactly once.
    [Fact]
    public void Only_cockpit_prep_arms_the_lights_before_departure()
    {
        var arming = PMDG777FlowDefinitions.Build()
            .SelectMany(f => f.Steps.Select(s => (Flow: f.Id, Step: s)))
            .Where(x => x.Step.EventName == "EMER_EXIT_LIGHTS"
                        && x.Step.TargetValue == EmerExitLightSequence.Armed)
            .Select(x => $"{x.Flow}/{x.Step.Id}")
            .ToList();
        Assert.Equal(new[] { "COCKPIT_PREP/CP_EMER_LIGHTS" }, arming);
    }
}
