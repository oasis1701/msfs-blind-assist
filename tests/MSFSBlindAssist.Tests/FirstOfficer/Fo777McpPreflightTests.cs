using System;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural invariants for the 2026-07-17 777 MCP preflight tightening:
/// Cockpit Preparation must set the flight directors ON and arm BOTH autothrottle
/// switches (Boeing preflight procedure) — the old flow set them OFF as a "cold
/// preflight state", contradicting the PREFLIGHT checklist's FD ON item. All four
/// are mouse-flag TOGGLES, so each step needs a skip predicate keyed on the target
/// state (skip when already there) or a re-run would flip the switch the wrong way.
/// </summary>
public class Fo777McpPreflightTests
{
    private static FlowDefinition<AircraftStateEvaluator> CockpitPrep() =>
        PMDG777FlowDefinitions.Build().First(f => f.Id == "COCKPIT_PREP");

    [Theory]
    [InlineData("CP_FD_L",     "EVT_MCP_FD_SWITCH_L")]
    [InlineData("CP_AT_ARM_L", "EVT_MCP_AT_ARM_SWITCH_L")]
    [InlineData("CP_AT_ARM_R", "EVT_MCP_AT_ARM_SWITCH_R")]
    [InlineData("CP_FD_R",     "EVT_MCP_FD_SWITCH_R")]
    public void CockpitPrep_HasMouseFlagToggleStep(string stepId, string eventName)
    {
        var step = CockpitPrep().Steps.FirstOrDefault(s => s.Id == stepId);
        Assert.NotNull(step);
        Assert.Equal(eventName, step!.EventName);
        Assert.True(step.UsesMouseFlag);
        Assert.NotNull(step.SkipCondition);   // toggle — must skip when already in target state
        Assert.Null(step.TargetValue);        // no absolute position exists for these switches
    }

    [Fact]
    public void CockpitPrep_McpStepsFollowPhysicalPanelOrder()
    {
        var ids = CockpitPrep().Steps.Select(s => s.Id).ToList();
        int fdL = ids.IndexOf("CP_FD_L");
        int atL = ids.IndexOf("CP_AT_ARM_L");
        int atR = ids.IndexOf("CP_AT_ARM_R");
        int fdR = ids.IndexOf("CP_FD_R");
        Assert.True(fdL >= 0 && fdL < atL && atL < atR && atR < fdR,
            "MCP steps must run left-to-right: L FD, L A/T ARM, R A/T ARM, R FD");
    }

    [Theory]
    [InlineData("PF_FD_ON",  "MCP_FD_Sw_On_0",    "MCP_FD_Sw_On_1")]
    [InlineData("PF_AT_ARM", "MCP_ATArm_Sw_On_0", "MCP_ATArm_Sw_On_1")]
    public void PreflightChecklist_FdAndAtArm_AutoDetectBothSides(
        string itemId, string field, string additionalField)
    {
        var group = PMDG777ChecklistDefinitions.Build().First(g => g.Id == "PREFLIGHT");
        var item = group.Items.FirstOrDefault(i => i.Id == itemId);
        Assert.NotNull(item);
        Assert.Equal(ChecklistItemType.AutoDetectable, item!.Type);
        Assert.Equal(RevertBehavior.RevertToState, item.RevertBehavior);
        Assert.Equal(field, item.StateFieldName);
        Assert.Contains(additionalField, item.AdditionalStateFields);
        Assert.NotNull(item.CheckAction);
        Assert.True(item.StateCondition!(1.0));   // switch on ⇒ satisfied
        Assert.False(item.StateCondition(0.0));   // switch off ⇒ pending
    }

    [Fact]
    public void ShutdownStillTurnsFlightDirectorsOff()
    {
        // The preflight flip to FD ON must not have taken the shutdown OFF steps with it.
        var shutdown = PMDG777FlowDefinitions.Build().First(f => f.Id == "SHUTDOWN");
        Assert.Contains(shutdown.Steps, s => s.Id == "SD_FD_L");
        Assert.Contains(shutdown.Steps, s => s.Id == "SD_FD_R");
    }
}
