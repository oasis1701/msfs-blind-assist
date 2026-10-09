using System.Collections.Generic;
using System.Linq;
using Xunit;

using A320Flows = MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320FlowDefinitions;
using A320Checklist = MSFSBlindAssist.FirstOfficer.FBWA320.FbwA320ChecklistDefinitions;
using FenixFlows = MSFSBlindAssist.FirstOfficer.Fenix.FenixFlowDefinitions;
using FenixChecklist = MSFSBlindAssist.FirstOfficer.Fenix.FenixChecklistDefinitions;
using Pmdg777Flows = MSFSBlindAssist.FirstOfficer.PMDG777FlowDefinitions;
using Pmdg777Checklist = MSFSBlindAssist.FirstOfficer.PMDG777ChecklistDefinitions;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using Pmdg737Checklist = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;
using A380Flows = MSFSBlindAssist.FirstOfficer.FBWA380.FbwA380FlowDefinitions;
using A380Checklist = MSFSBlindAssist.FirstOfficer.FBWA380.FbwA380ChecklistDefinitions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Guardrail tests for the 2026-07-13 FO shutdown/secure tightening (Tasks 1-5 of the
/// plan): transponder→standby deferred to the Shutdown flow, Airbus LS pushbuttons off
/// at Shutdown, and Secure powering down ground power + APU (the A380's moved from
/// Parking to Secure on 2026-08-15). Each fact asserts
/// the exact step ids from the corresponding task's "Produces" block, walking the same
/// public <c>Build()</c> accessors the app itself enumerates. Pure-logic only — no
/// SimConnect, no executor invocation.
/// </summary>
public class FoShutdownSecureTighteningTests
{
    // -- helpers ----------------------------------------------------------

    private static IEnumerable<string> FlowStepIds<TState>(
        IEnumerable<MSFSBlindAssist.FirstOfficer.Models.FlowDefinition<TState>> flows, string flowId)
        where TState : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator =>
        flows.Single(f => f.Id == flowId).Steps.Select(s => s.Id);

    private static IEnumerable<string> ChecklistItemIds<TExec, TState>(
        IEnumerable<MSFSBlindAssist.FirstOfficer.Models.ChecklistGroup<TExec, TState>> groups, string groupId)
        where TExec : MSFSBlindAssist.FirstOfficer.IFoActionExecutor
        where TState : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator =>
        groups.Single(g => g.Id == groupId).Items.Select(i => i.Id);

    private static IEnumerable<string> AllChecklistItemIds<TExec, TState>(
        IEnumerable<MSFSBlindAssist.FirstOfficer.Models.ChecklistGroup<TExec, TState>> groups)
        where TExec : MSFSBlindAssist.FirstOfficer.IFoActionExecutor
        where TState : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator =>
        groups.SelectMany(g => g.Items).Select(i => i.Id);

    // -- Task 1: FBW A320 -------------------------------------------------

    [Fact]
    public void A320_Xpdr_Tcas_Ls_MovedToShutdown()
    {
        var flows = A320Flows.Build();
        var al = FlowStepIds(flows, "AFTER_LANDING").ToList();
        var sd = FlowStepIds(flows, "SHUTDOWN").ToList();

        Assert.DoesNotContain("AL_XPDR_STBY", al);
        Assert.DoesNotContain("AL_TCAS_STBY", al);
        Assert.Contains("SD_XPDR_STBY", sd);
        Assert.Contains("SD_TCAS_STBY", sd);
        Assert.Contains("SD_LS1", sd);
        Assert.Contains("SD_LS2", sd);

        var groups = A320Checklist.Build();
        var alItems = ChecklistItemIds(groups, "AFTER_LANDING").ToList();
        var sdItems = ChecklistItemIds(groups, "SHUTDOWN").ToList();
        Assert.DoesNotContain("AL_XPDR_STBY", alItems);
        Assert.Contains("SD_XPDR_STBY", sdItems);
        Assert.Contains("SD_LS1", sdItems);
        Assert.Contains("SD_LS2", sdItems);
    }

    // -- Task 2: Fenix A320 -----------------------------------------------

    [Fact]
    public void Fenix_Xpdr_Tcas_Ls_MovedToShutdown()
    {
        var flows = FenixFlows.Build();
        var al = FlowStepIds(flows, "AFTER_LANDING").ToList();
        var sd = FlowStepIds(flows, "SHUTDOWN").ToList();

        Assert.DoesNotContain("AL_XPDR_STBY", al);
        Assert.DoesNotContain("AL_TCAS_STBY", al);
        Assert.Contains("SD_XPDR_STBY", sd);
        Assert.Contains("SD_TCAS_STBY", sd);
        Assert.Contains("SD_LS1", sd);
        Assert.Contains("SD_LS2", sd);

        var groups = FenixChecklist.Build();
        var alItems = ChecklistItemIds(groups, "AFTER_LANDING").ToList();
        var sdItems = ChecklistItemIds(groups, "SHUTDOWN").ToList();
        Assert.DoesNotContain("AL_XPDR_STBY", alItems);
        Assert.Contains("SD_XPDR_STBY", sdItems);
        Assert.Contains("SD_LS1", sdItems);
        Assert.Contains("SD_LS2", sdItems);
    }

    // -- Task 3: PMDG 777 -------------------------------------------------

    [Fact]
    public void Pmdg777_AfterLandingXpndrDropped_SecureDropsGroundPower()
    {
        var flows = Pmdg777Flows.Build();
        Assert.DoesNotContain("AL_XPNDR_STBY", FlowStepIds(flows, "AFTER_LANDING"));
        var sec = FlowStepIds(flows, "SECURE").ToList();
        Assert.Contains("SEC_GND_PWR_PRIM", sec);
        Assert.Contains("SEC_GND_PWR_SEC", sec);
        // Shutdown transponder step is untouched.
        Assert.Contains("SD_XPNDR_STBY", FlowStepIds(flows, "SHUTDOWN"));

        var groups = Pmdg777Checklist.Build();
        Assert.DoesNotContain("AL_TRANSPONDER", ChecklistItemIds(groups, "AFTER_LANDING"));
        Assert.Contains("SEC_GND_PWR_OFF", ChecklistItemIds(groups, "SECURE"));
    }

    // -- Task 4: PMDG 737 -------------------------------------------------

    [Fact]
    public void Pmdg737_SecureTurnsOffApuAndGroundPower_NoEpdPwrReminder()
    {
        var flows = Pmdg737Flows.Build();
        var sec = FlowStepIds(flows, "SECURE").ToList();
        Assert.Contains("SE_APU_OFF", sec);
        Assert.Contains("SE_GND_PWR_OFF", sec);

        var groups = Pmdg737Checklist.Build();
        var secItems = ChecklistItemIds(groups, "SECURE").ToList();
        Assert.Contains("SE_APU_OFF", secItems);
        Assert.Contains("SE_GND_PWR_OFF", secItems);
        // The redundant power-down reminder is gone from every checklist group.
        Assert.DoesNotContain("EPD_PWR", AllChecklistItemIds(groups));
    }

    // -- Task 5: FBW A380 -------------------------------------------------
    // SUPERSEDED 2026-08-15. The original fact pinned PK_EXTPWR_OFF / PK_APU_OFF *in*
    // PARKING, because at the time the A380 had no Secure flow and Parking was the only
    // place a power-down could live. It now has one, so the power-down moved there and
    // Parking leaves the aircraft powered at the gate (it selects the APU generators and
    // APU bleed ON — killing external power and the APU master two steps later left the
    // jet on batteries). This asserts the move, not a relaxation.

    [Fact]
    public void A380_AfterLandingTcasDropped_SecurePowersDown_ParkingDoesNot()
    {
        var flows = A380Flows.Build();
        Assert.DoesNotContain("AL_TCAS", FlowStepIds(flows, "AFTER_LANDING"));

        var pk = FlowStepIds(flows, "PARKING").ToList();
        Assert.DoesNotContain("PK_EXTPWR_OFF", pk);
        Assert.DoesNotContain("PK_APU_OFF", pk);

        var sec = FlowStepIds(flows, "SECURE").ToList();
        Assert.Contains("SC_EXTPWR_OFF", sec);
        Assert.Contains("SC_APU_OFF", sec);

        var groups = A380Checklist.Build();
        Assert.DoesNotContain("AL_TCAS", ChecklistItemIds(groups, "AFTER_LANDING"));

        var pkItems = ChecklistItemIds(groups, "PARKING").ToList();
        Assert.DoesNotContain("PK_EXTPWR_OFF", pkItems);
        Assert.DoesNotContain("PK_APU_OFF", pkItems);

        var secItems = ChecklistItemIds(groups, "SECURE").ToList();
        Assert.Contains("SC_EXTPWR_OFF", secItems);
        Assert.Contains("SC_APU_OFF", secItems);
    }
}
