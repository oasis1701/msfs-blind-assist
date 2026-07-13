using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;          // PMDG777*Definitions
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using A380 = MSFSBlindAssist.FirstOfficer.FBWA380;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Cross-profile structural invariants for the 2026-07-13 checklist refinements:
/// the uniform Before Start tail (ACARS → pushback/start clearance), the Airbus
/// before/after-the-line separators, and the removed Fenix After Takeoff baro readback.
/// Pure-logic over the data-driven Build() methods — the automated safety net.
/// </summary>
public class FoChecklistRefinementTests
{
    private const string Acars = "Start ACARS";
    private const string Clearance = "Obtain pushback and start clearance";

    // ---- generic projection helpers (work over any profile's typed Build() list) ----
    private static (string Label, ChecklistItemType Type, bool HasAction)[] GroupItems<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.First(g => g.Id == groupId).Items
              .Select(i => (i.Label, i.Type, i.CheckAction != null)).ToArray();

    private static string[] GroupItemIds<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.First(g => g.Id == groupId).Items.Select(i => i.Id).ToArray();

    private static (string Label, FlowStepActionType Type)[] FlowSteps<TState>(
        IEnumerable<FlowDefinition<TState>> flows, string flowId)
        where TState : IFoStateEvaluator =>
        flows.First(f => f.Id == flowId).Steps.Select(s => (s.Label, s.ActionType)).ToArray();

    private static void AssertBeforeStartTail<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator
    {
        var items = GroupItems(groups, "BEFORE_START");
        Assert.True(items.Length >= 2, "BEFORE_START must have >=2 items");
        Assert.Equal(Acars, items[^2].Label);
        Assert.Equal(ChecklistItemType.CaptainReminder, items[^2].Type);
        Assert.Equal(Clearance, items[^1].Label);
        Assert.Equal(ChecklistItemType.CaptainReminder, items[^1].Type);
    }

    private static void AssertBeforeStartFlowTail<TState>(
        IEnumerable<FlowDefinition<TState>> flows)
        where TState : IFoStateEvaluator
    {
        var steps = FlowSteps(flows, "BEFORE_START");
        Assert.True(steps.Length >= 2, "BEFORE_START flow must have >=2 steps");
        Assert.Equal(Acars, steps[^2].Label);
        Assert.Equal(FlowStepActionType.CaptainReminder, steps[^2].Type);
        Assert.Equal(Clearance, steps[^1].Label);
        Assert.Equal(FlowStepActionType.CaptainReminder, steps[^1].Type);
    }

    private static void AssertHasLineSeparator<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator
    {
        var items = GroupItems(groups, groupId);
        Assert.Contains(items, i => i.Type == ChecklistItemType.Informational
            && i.Label.Contains("line", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Task 3: uniform Before Start tail on all five jets ----
    [Fact] public void Fenix_BeforeStartTail()
    { AssertBeforeStartTail(Fenix.FenixChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(Fenix.FenixFlowDefinitions.Build()); }

    [Fact] public void A320_BeforeStartTail()
    { AssertBeforeStartTail(A320.FbwA320ChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(A320.FbwA320FlowDefinitions.Build()); }

    [Fact] public void A380_BeforeStartTail()
    { AssertBeforeStartTail(A380.FbwA380ChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(A380.FbwA380FlowDefinitions.Build()); }

    [Fact] public void B737_BeforeStartTail()
    { AssertBeforeStartTail(B737.PMDG737ChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(B737.PMDG737FlowDefinitions.Build()); }

    [Fact] public void B777_BeforeStartTail()
    { AssertBeforeStartTail(PMDG777ChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(PMDG777FlowDefinitions.Build()); }

    // ---- Task 2: Airbus before/after-the-line separators ----
    [Fact] public void A320_BeforeStartCL_HasLine()
        => AssertHasLineSeparator(A320.FbwA320ChecklistDefinitions.Build(), "BEFORE_START_CL");
    [Fact] public void A320_BeforeTakeoffCL_HasLine()
        => AssertHasLineSeparator(A320.FbwA320ChecklistDefinitions.Build(), "BEFORE_TAKEOFF_CL");
    [Fact] public void A380_BeforeStartCL_HasLine()
        => AssertHasLineSeparator(A380.FbwA380ChecklistDefinitions.Build(), "BEFORE_START_CL");

    // ---- Task 1 (Fenix baro removal) ----
    [Fact] public void Fenix_AfterTakeoffCL_HasNoBaro()
    {
        var ids = GroupItemIds(Fenix.FenixChecklistDefinitions.Build(), "AFTER_TAKEOFF_CL");
        Assert.DoesNotContain("ATC_BARO", ids);
        Assert.DoesNotContain("ATC_LINE", ids);
    }

    // ---- guardrail: new Info separators keep *_CL groups action-free (Airbus) ----
    [Fact] public void AirbusReadbackGroupsRemainActionFree()
    {
        foreach (var g in A320.FbwA320ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")))
            Assert.All(g.Items, i => Assert.Null(i.CheckAction));
        foreach (var g in A380.FbwA380ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")))
            Assert.All(g.Items, i => Assert.Null(i.CheckAction));
        foreach (var g in Fenix.FenixChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")))
            Assert.All(g.Items, i => Assert.Null(i.CheckAction));
    }
}
