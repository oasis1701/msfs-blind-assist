using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural tests over the FlyByWire A380 Securing phase (2026-08-15). The A380 was the
/// only First-Officer aircraft with no Secure flow or checklist — there was nowhere for the
/// power-down to happen, which is why the Parking flow had grown one (see
/// FoShutdownSecureTighteningTests for that supersession).
/// </summary>
public class FoA380SecureFlowTests
{
    // The APU master goes off before external power, with the APU flap wait between them: FlyByWire's
    // own order, chosen by the owner for every Airbus on 2026-10-14 (FOA-10). The A380's shipped
    // reference text (Checklists/FBW_A380_Checklist.txt, retired 2026-10-17, in git history) lists
    // external power first; this was the
    // order until then (SC_EXTPWR_OFF, SC_APU_OFF).
    private static readonly string[] ExpectedSecureIds =
    {
        "SC_OXY", "SC_EFB", "SC_ADIRS", "SC_EMEREXIT", "SC_NOSMOKE",
        "SC_EXTLT_OFF", "SC_APUBLEED_OFF", "SC_APU_OFF", "SC_APU_WAIT", "SC_EXTPWR_OFF", "SC_BAT_OFF",
    };

    [Fact]
    public void SecureFlowCarriesTheFullPowerDownInOrder()
    {
        var flow = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE");
        Assert.Equal(ExpectedSecureIds, flow.Steps.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void SecureFlowPointsAtTheSecuringReadbackGroup()
    {
        var flow = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE");
        Assert.Contains("SECURING_CL", flow.RelatedChecklistGroupIds ?? System.Array.Empty<string>());
    }

    // The Securing action list has one line per step, except for the read-only APU flap wait (SC_APU_WAIT,
    // owner, 2026-10-14; FOA-10): a pacing wait links no line, so it has none, in the order the flow runs.
    [Fact]
    public void SecureChecklistGroupMirrorsTheFlowOneToOne()
    {
        var flowIds = FbwA380FlowDefinitions.Build()
            .Single(f => f.Id == "SECURE").Steps.Where(s => s.Id != "SC_APU_WAIT").Select(s => s.Id).ToList();
        var itemIds = FbwA380ChecklistDefinitions.Build()
            .Single(g => g.Id == "SECURE").Items.Select(i => i.Id).ToList();
        Assert.Equal(flowIds, itemIds);
    }

    [Fact]
    public void TheApuFlapWaitLinksNoLine()
    {
        var wait = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE").Steps.Single(s => s.Id == "SC_APU_WAIT");
        Assert.Empty(wait.LinkedChecklistItemIds);
        Assert.DoesNotContain(FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items), i => i.Id == "SC_APU_WAIT");
    }

    [Fact]
    public void SecuringReadbackGroupExistsAndIsActionFree()
    {
        var group = FbwA380ChecklistDefinitions.Build().Single(g => g.Id == "SECURING_CL");
        Assert.NotEmpty(group.Items);
        foreach (var item in group.Items)
        {
            Assert.Null(item.CheckAction);
            Assert.NotEqual(MSFSBlindAssist.FirstOfficer.Models.ChecklistItemType.Actionable, item.Type);
        }
    }

    [Fact]
    public void BatteriesAreTheLastThingSecureTurnsOff()
    {
        var steps = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE").Steps;
        Assert.Equal("SC_BAT_OFF", steps[^1].Id);
    }

    [Fact]
    public void A380ChecklistItemIdsStayUnique()
    {
        var ids = FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryA380FlowRelatedGroupIdResolvesToAChecklistGroup()
    {
        var groupIds = FbwA380ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet();
        foreach (var flow in FbwA380FlowDefinitions.Build())
            foreach (var gid in flow.RelatedChecklistGroupIds ?? System.Array.Empty<string>())
                Assert.Contains(gid, groupIds);
    }

    // -----------------------------------------------------------------------
    // Payload pinning — the SECURE steps' exact (variable, value) writes (the read-only APU
    // flap wait, SC_APU_WAIT, writes nothing and is pinned in AirbusSecuringPowerOffTests). Structural
    // tests above cover ids/order/mirroring but not a single variable name or target value,
    // which is exactly where the safety lives: an ordinal, case-sensitive lookup dictionary
    // means a wrong casing (e.g. "..._POSITION" vs "..._Position") writes a variable nothing
    // reads and never ticks the checklist — silently in both directions. Values below are
    // written out literally, transcribed from BuildSecure() by hand, not derived from it.
    // -----------------------------------------------------------------------

    private static readonly (string Id, FlowStepActionType ActionType, string? EventName, int? TargetValue,
        (string EventName, int TargetValue)[] MultiActions)[] ExpectedSecurePayloads =
    {
        // Crew oxygen is INVERTED: 1 = Off (0 = Auto/on).
        ("SC_OXY", FlowStepActionType.SetSwitch, "PUSH_OVHD_OXYGEN_CREW", 1, Array.Empty<(string, int)>()),

        // Captain reminder — no automation, writes nothing.
        ("SC_EFB", FlowStepActionType.CaptainReminder, null, null, Array.Empty<(string, int)>()),

        ("SC_ADIRS", FlowStepActionType.SetSwitchMultiple, null, null, new[]
        {
            ("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 0),
            ("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 0),
            ("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 0),
        }),

        // 3-position sign switch, 2 = Off.
        ("SC_EMEREXIT", FlowStepActionType.SetSwitch, "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", 2,
            Array.Empty<(string, int)>()),
        ("SC_NOSMOKE", FlowStepActionType.SetSwitch, "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", 2,
            Array.Empty<(string, int)>()),

        // The runway turn-off lights too: Lineup switches them on and only After Takeoff off, a flow a
        // pilot can skip, so "Exterior lights: OFF" was spoken over them (PR #160 second review).
        ("SC_EXTLT_OFF", FlowStepActionType.SetSwitchMultiple, null, null, new[]
        {
            ("LIGHT_NAV", 0), ("LIGHT_LOGO", 0), ("LIGHT_RWY_TURNOFF", 0),
        }),

        ("SC_APUBLEED_OFF", FlowStepActionType.SetSwitch, "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0,
            Array.Empty<(string, int)>()),

        ("SC_APU_OFF", FlowStepActionType.SetSwitch, "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0,
            Array.Empty<(string, int)>()),

        ("SC_EXTPWR_OFF", FlowStepActionType.SetSwitchMultiple, null, null, new[]
        {
            ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0),
            ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0),
        }),

        ("SC_BAT_OFF", FlowStepActionType.SetSwitchMultiple, null, null, new[]
        {
            ("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 0), ("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 0),
            ("A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", 0), ("A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO", 0),
        }),
    };

    [Fact]
    public void SecureFlowStepsWriteThePinnedVariablesAndValues()
    {
        var steps = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE").Steps
            .ToDictionary(s => s.Id);

        foreach (var expected in ExpectedSecurePayloads)
        {
            var step = steps[expected.Id];
            Assert.Equal(expected.ActionType, step.ActionType);
            Assert.Equal(expected.EventName, step.EventName);
            Assert.Equal(expected.TargetValue, step.TargetValue);
            Assert.Equal(
                expected.MultiActions.Select(a => (a.EventName, (int?)a.TargetValue)).ToArray(),
                step.MultiActions.ToArray());
        }
    }
}
