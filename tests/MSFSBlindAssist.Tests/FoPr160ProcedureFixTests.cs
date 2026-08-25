using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

using A320Flows = MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320FlowDefinitions;
using A320Checklist = MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320ChecklistDefinitions;
using FenixFlows = MSFSBlindAssist.FirstOfficer.Fenix.FenixFlowDefinitions;
using FenixChecklist = MSFSBlindAssist.FirstOfficer.Fenix.FenixChecklistDefinitions;
using Pmdg777Flows = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions;
using Pmdg777Checklist = MSFSBlindAssist.FirstOfficer.PMDG777ChecklistDefinitions;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using Pmdg737Checklist = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Guardrails for the four owner-reported First Officer defects fixed under PR #160
/// (design: docs/superpowers/specs/2026-08-25-pr160-fo-procedure-fixes-design.md):
///   1. A320 descent preparation wording — no EFB, no "top of descent".
///   2. PMDG 777 secondary ground power skipping itself at Electrical Power Up.
///   3. PMDG 777 speedbrake arming during Approach instead of Landing.
///   4. PMDG 737 speedbrake arming with no verification.
/// Pure-logic only — every fact walks the public Build() accessors the app enumerates.
/// No SimConnect, no executor invocation.
/// </summary>
public class FoPr160ProcedureFixTests
{
    // -- helpers ----------------------------------------------------------

    private static IEnumerable<string> FlowStepIds<TState>(
        IEnumerable<FlowDefinition<TState>> flows, string flowId)
        where TState : IFoStateEvaluator =>
        flows.Single(f => f.Id == flowId).Steps.Select(s => s.Id);

    private static IEnumerable<string> ChecklistItemIds<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator =>
        groups.Single(g => g.Id == groupId).Items.Select(i => i.Id);

    private static string FlowStepLabel<TState>(
        IEnumerable<FlowDefinition<TState>> flows, string flowId, string stepId)
        where TState : IFoStateEvaluator =>
        flows.Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId).Label;

    private static string ChecklistItemLabel<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId, string itemId)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator =>
        groups.Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId).Label;

    // -- 1. A320 descent preparation wording -------------------------------

    // The EFB has no landing-performance answer on the A320 (VAPP comes off the MCDU
    // PERF APPR page), and neither A320 profile has a CRUISE group — the Descent group
    // IS the pre-TOD preparation group, so "before top of descent" contradicted where
    // the item lives. The two reminders were one job split across two lines.

    [Fact]
    public void Fenix_DescentPrep_IsOneItem_WithNoEfbAndNoTopOfDescent()
    {
        var groups = FenixChecklist.Build();
        var ids = ChecklistItemIds(groups, "DESCENT").ToList();

        Assert.DoesNotContain("DC_ARRPERF", ids);
        Assert.Contains("DC_MCDU", ids);

        string label = ChecklistItemLabel(groups, "DESCENT", "DC_MCDU");
        Assert.DoesNotContain("EFB", label);
        Assert.DoesNotContain("top of descent", label);
        Assert.Contains("PERF APPR", label);
    }

    [Fact]
    public void Fenix_DescentFlow_IsOneItem_WithNoEfbAndNoTopOfDescent()
    {
        var flows = FenixFlows.Build();
        var ids = FlowStepIds(flows, "DESCENT").ToList();

        Assert.DoesNotContain("DC_ARRPERF", ids);
        Assert.Contains("DC_MCDU", ids);

        string label = FlowStepLabel(flows, "DESCENT", "DC_MCDU");
        Assert.DoesNotContain("EFB", label);
        Assert.DoesNotContain("top of descent", label);
        Assert.Contains("PERF APPR", label);
    }

    [Fact]
    public void A32nx_DescentPrep_IsOneItem_WithNoEfbAndNoTopOfDescent()
    {
        var groups = A320Checklist.Build();
        var ids = ChecklistItemIds(groups, "DESCENT").ToList();

        Assert.DoesNotContain("DC_ARRPERF", ids);
        Assert.Contains("DC_MCDU", ids);

        string label = ChecklistItemLabel(groups, "DESCENT", "DC_MCDU");
        Assert.DoesNotContain("EFB", label);
        Assert.DoesNotContain("top of descent", label);
        Assert.Contains("PERF APPR", label);
    }

    [Fact]
    public void A32nx_DescentFlow_IsOneItem_WithNoEfbAndNoTopOfDescent()
    {
        var flows = A320Flows.Build();
        var ids = FlowStepIds(flows, "DESCENT").ToList();

        Assert.DoesNotContain("DC_ARRPERF", ids);
        Assert.Contains("DC_MCDU", ids);

        string label = FlowStepLabel(flows, "DESCENT", "DC_MCDU");
        Assert.DoesNotContain("EFB", label);
        Assert.DoesNotContain("top of descent", label);
        Assert.Contains("PERF APPR", label);
    }

    // The two A320 profiles were written as copies; the wording must not drift apart.
    [Fact]
    public void BothA320Profiles_UseTheSameDescentPrepWording()
    {
        string fenix = ChecklistItemLabel(FenixChecklist.Build(), "DESCENT", "DC_MCDU");
        string a32nx = ChecklistItemLabel(A320Checklist.Build(), "DESCENT", "DC_MCDU");
        Assert.Equal(fenix, a32nx);
    }

    // -- 2. PMDG 777 ground power ------------------------------------------

    // Both GPU steps must survive at Electrical Power Up. The defect was not a missing
    // step but a shared skip predicate that made the second one skip itself once the
    // first had connected; the predicates themselves are not directly testable (the 777
    // state evaluator wraps a concrete PMDG777DataManager), which is why the rule lives
    // in GroundPowerGate — see GroundPowerGateTests.
    [Fact]
    public void Pmdg777_ElectricalPowerUp_StillDrivesBothGroundPowerSides()
    {
        var ids = FlowStepIds(Pmdg777Flows.Build(), "ELECTRICAL_POWER_UP").ToList();
        Assert.Contains("EPU_GND_PWR_PRIM", ids);
        Assert.Contains("EPU_GND_PWR_SEC", ids);
    }

    [Fact]
    public void Pmdg777_Secure_StillDisconnectsBothGroundPowerSides()
    {
        var ids = FlowStepIds(Pmdg777Flows.Build(), "SECURE").ToList();
        Assert.Contains("SEC_GND_PWR_PRIM", ids);
        Assert.Contains("SEC_GND_PWR_SEC", ids);
    }
}
