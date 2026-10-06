using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;          // PMDG777*Definitions
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using A380 = MSFSBlindAssist.FirstOfficer.FBWA380;
using HwA330 = MSFSBlindAssist.FirstOfficer.HWA330;
using Md11 = MSFSBlindAssist.FirstOfficer.MD11;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;
using IFly737 = MSFSBlindAssist.FirstOfficer.IFly737;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Cross-profile structural invariants for the 2026-07-13 checklist refinements:
/// the uniform Before Start tail (ACARS → pushback/start clearance), the A380's
/// before-the-line separator, and the A320 family's Airbus-card read-backs (no line markers,
/// no Before/After Takeoff list).
/// Pure-logic over the data-driven Build() methods — the automated safety net.
/// </summary>
public class FoChecklistRefinementTests
{
    private const string Acars = "Start ACARS";
    private const string Clearance = "Obtain pushback and start clearance";

    // ---- generic projection helpers (work over any profile's typed Build() list) ----
    private static (string Label, ChecklistItemType Type)[] GroupItems<TExec, TState>(
        IEnumerable<ChecklistGroup<TExec, TState>> groups, string groupId)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        groups.First(g => g.Id == groupId).Items
              .Select(i => (i.Label, i.Type)).ToArray();

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

    // ---- Task 3: uniform Before Start tail across the fleet (five PMDG/FBW jets +
    //      the iFly 737 MAX8, added in the iFly First Officer profile task) ----
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

    [Fact] public void IFly737_BeforeStartTail()
    { AssertBeforeStartTail(IFly737.IFly737ChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(IFly737.IFly737FlowDefinitions.Build()); }

    [Fact] public void A330_BeforeStartTail()
    { AssertBeforeStartTail(HwA330.HwA330ChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(HwA330.HwA330FlowDefinitions.Build()); }

    [Fact] public void Md11_BeforeStartTail()
    { AssertBeforeStartTail(Md11.Md11FoChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(Md11.Md11FoFlowDefinitions.Build()); }

    // ---- Task 2: Airbus before/after-the-line separators (the A380 keeps its own card) ----
    [Fact] public void A380_BeforeStartCL_HasLine()
        => AssertHasLineSeparator(A380.FbwA380ChecklistDefinitions.Build(), "BEFORE_START_CL");

    // ---- A320 family follows the Nov 2021 Airbus card: no line markers, no Before/After Takeoff read-backs ----
    [Fact] public void A320_Readbacks_FollowTheAirbusCard()
    {
        foreach (var groups in new[]
                 {
                     A320.FbwA320ChecklistDefinitions.Build().Select(g => (g.Id, Items: g.Items.Select(i => i.Type))),
                     HwA330.HwA330ChecklistDefinitions.Build().Select(g => (g.Id, Items: g.Items.Select(i => i.Type))),
                 })
        {
            var list = groups.ToList();
            Assert.DoesNotContain(list, g => g.Id is "BEFORE_TAKEOFF_CL" or "AFTER_TAKEOFF_CL" or "DEPARTURE_CHANGE_CL");
            Assert.All(list.Where(g => g.Id.EndsWith("_CL")),
                g => Assert.DoesNotContain(MSFSBlindAssist.FirstOfficer.Models.ChecklistItemType.Informational, g.Items));
        }
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
