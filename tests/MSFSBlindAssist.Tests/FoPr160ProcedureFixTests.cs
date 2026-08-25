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
using MSFSBlindAssist.FirstOfficer.PMDG737;

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

    // -- 3. PMDG 777 speedbrake: Approach -> Landing ------------------------

    [Fact]
    public void Pmdg777_ApproachSetupFlow_NoLongerArmsTheSpeedbrake()
    {
        var ids = FlowStepIds(Pmdg777Flows.Build(), "APPROACH_SETUP").ToList();
        Assert.DoesNotContain("APP_SPEEDBRAKE_ARM", ids);
        Assert.Contains("APP_ALTIMETERS", ids);
    }

    [Fact]
    public void Pmdg777_ApproachGroup_NoLongerArmsTheSpeedbrake()
    {
        var ids = ChecklistItemIds(Pmdg777Checklist.Build(), "APPROACH").ToList();
        Assert.DoesNotContain("APPA_SPEEDBRAKE", ids);
        Assert.Contains("APPA_ALTIMETERS", ids);
    }

    [Fact]
    public void Pmdg777_HasALandingFlow_ThatArmsTheSpeedbrake()
    {
        var flows = Pmdg777Flows.Build();
        var landing = flows.Single(f => f.Id == "LANDING");

        Assert.Equal(new[] { "LD_SPEEDBRAKE_ARM", "LD_MISSED" },
                     landing.Steps.Select(s => s.Id).ToArray());
        Assert.Contains("LANDING_CL", landing.RelatedChecklistGroupIds);

        var arm = landing.Steps.Single(s => s.Id == "LD_SPEEDBRAKE_ARM");
        Assert.Equal("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", arm.EventName);
        Assert.Equal("FCTL_Speedbrake_Lever", arm.VerifyFieldName);
        Assert.Equal("LDG_SPEEDBRAKE", arm.CompletesChecklistItemId);
    }

    // The Landing flow must run AFTER Approach Setup and BEFORE After Landing, so the
    // FO window lists it in the order a pilot flies it.
    [Fact]
    public void Pmdg777_LandingFlow_SitsBetweenApproachSetupAndAfterLanding()
    {
        var ids = Pmdg777Flows.Build().Select(f => f.Id).ToList();
        Assert.Equal(ids.IndexOf("APPROACH_SETUP") + 1, ids.IndexOf("LANDING"));
        Assert.Equal(ids.IndexOf("LANDING") + 1, ids.IndexOf("AFTER_LANDING"));
    }

    // Ticking "Speedbrake: ARMED" on the Landing checklist must actually arm it; the
    // item verified but never actuated (action: null).
    [Fact]
    public void Pmdg777_LandingChecklistSpeedbrake_ActuallyArms()
    {
        var item = Pmdg777Checklist.Build()
            .Single(g => g.Id == "LANDING_CL").Items
            .Single(i => i.Id == "LDG_SPEEDBRAKE");
        Assert.NotNull(item.CheckAction);
        Assert.Equal("FCTL_Speedbrake_Lever", item.StateFieldName);
    }

    // -- 4. PMDG 737 speedbrake: verified arm -------------------------------

    // All three sites used to be unverified, resting on a comment claiming no
    // lever state field exists in the NG3 CDA struct. MAIN_annunSPEEDBRAKE_ARMED
    // does exist (PMDGNG3DataStruct.cs, and PMDG_NG3_SDK.h) - so a failed arm was
    // reported as success and the pilot landed with the lever down.

    [Fact]
    public void Pmdg737_LandingGroupSpeedbrake_AutoDetectsFromTheArmedAnnunciator()
    {
        var item = Pmdg737Checklist.Build()
            .Single(g => g.Id == "LANDING").Items
            .Single(i => i.Id == "LDA_SPDBRK");

        Assert.Equal(ChecklistItemType.AutoDetectable, item.Type);
        Assert.Equal("MAIN_annunSPEEDBRAKE_ARMED", item.StateFieldName);
        Assert.NotNull(item.CheckAction);
    }

    [Fact]
    public void Pmdg737_LandingChecklistSpeedbrake_VerifiesButDoesNotActuate()
    {
        var item = Pmdg737Checklist.Build()
            .Single(g => g.Id == "LANDING_CL").Items
            .Single(i => i.Id == "LDC_SPDBRK");

        Assert.Equal(ChecklistItemType.AutoDetectable, item.Type);
        Assert.Equal("MAIN_annunSPEEDBRAKE_ARMED", item.StateFieldName);
        Assert.Null(item.CheckAction);
    }

    [Fact]
    public void Pmdg737_LandingFlowSpeedbrake_GoesThroughTheVerifiedPseudoKey()
    {
        var step = Pmdg737Flows.Build()
            .Single(f => f.Id == "LANDING").Steps
            .Single(s => s.Id == "LD_SPDBRK");

        Assert.Equal(SpeedbrakeArmLadder.PseudoKey, step.EventName);
        Assert.Equal(SpeedbrakeArmLadder.ArmedField, step.VerifyFieldName);
        Assert.Equal("LDC_SPDBRK", step.CompletesChecklistItemId);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
    }

    // The pseudo-key is intercepted before the dispatch table is consulted, so it must
    // never collide with a real PMDG event name.
    [Fact]
    public void Pmdg737_SpeedbrakePseudoKey_IsNotARealPmdgEvent()
    {
        Assert.False(MSFSBlindAssist.Aircraft.PMDG737Definition.EventIds
            .ContainsKey(SpeedbrakeArmLadder.PseudoKey));
    }
}
