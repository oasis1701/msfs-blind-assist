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
using B757 = MSFSBlindAssist.FirstOfficer.BBS757;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;
using IFly737 = MSFSBlindAssist.FirstOfficer.IFly737;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Cross-profile structural invariants for the checklist refinements: the uniform Before Start tail
/// (ACARS → pushback/start clearance), and the Airbus read-backs: the A320 family's card (FOA-8) and the
/// FBW A380's own ECL (FOA-9), with no line markers and no Before/After Takeoff list.
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

    [Fact] public void B757_BeforeStartTail()
    { AssertBeforeStartTail(B757.Bbs757FoChecklistDefinitions.Build());
      AssertBeforeStartFlowTail(B757.Bbs757FoFlowDefinitions.Build()); }

    // ---- A320 family follows the Nov 2021 Airbus card: no line markers, no Before/After Takeoff read-backs ----
    [Fact] public void A320_Readbacks_FollowTheAirbusCard()
    {
        foreach (var groups in new[]
                 {
                     A320.FbwA320ChecklistDefinitions.Build().Select(g => (g.Id, Items: g.Items.Select(i => i.Type))),
                     HwA330.HwA330ChecklistDefinitions.Build().Select(g => (g.Id, Items: g.Items.Select(i => i.Type))),
                     Fenix.FenixChecklistDefinitions.Build().Select(g => (g.Id, Items: g.Items.Select(i => i.Type))),
                 })
        {
            var list = groups.ToList();
            Assert.DoesNotContain(list, g => g.Id is "BEFORE_TAKEOFF_CL" or "AFTER_TAKEOFF_CL" or "DEPARTURE_CHANGE_CL");
            Assert.All(list.Where(g => g.Id.EndsWith("_CL")),
                g => Assert.DoesNotContain(MSFSBlindAssist.FirstOfficer.Models.ChecklistItemType.Informational, g.Items));
        }
    }

    // ---- the FBW A380 follows its own ECL (FOA-9): no Before/After Takeoff, After Landing or Departure
    //      Change read-back, and no line markers ----
    [Fact] public void A380_Readbacks_FollowTheEcl()
    {
        var groups = A380.FbwA380ChecklistDefinitions.Build();
        Assert.DoesNotContain(groups, g => g.Id is "BEFORE_TAKEOFF_CL" or "AFTER_TAKEOFF_CL"
            or "AFTER_LANDING_CL" or "DEPARTURE_CHANGE_CL");
        Assert.All(groups.Where(g => g.Id.EndsWith("_CL")),
            g => Assert.DoesNotContain(g.Items, i => i.Type == ChecklistItemType.Informational));
    }

    // ---- guardrail: the Airbus *_CL read-back groups stay action-free (FO-3) ----
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
