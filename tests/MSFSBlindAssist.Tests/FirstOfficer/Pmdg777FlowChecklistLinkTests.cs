using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Audit: every PMDG 777 First Officer flow step that WRITES a switch is tied
/// (<c>FlowStep.LinkedChecklistItemIds</c>) to every checklist line it sets in the groups the
/// flow latches — the PMDG 737 / iFly 737 audits' rule, shared through
/// <see cref="FlowChecklistLinkAudit"/>. When a flow finishes, FirstOfficerForm calls
/// <c>ChecklistManager.MarkGroupComplete</c> for each of the flow's completion groups
/// (<see cref="FlowDefinition{TState}.CompletionGroupIds"/>), ticking and latching every line
/// EXCEPT those a SKIPPED step links — so an unlinked write step had its line latched complete
/// even when the write failed (the Electrical Power Up flow's verified nav-lights and ADIRU
/// writes were the reviewer's examples).
///
/// The 777-specific data: the CDA field(s) each written event drives, the stateless events, the
/// action-only lines with the write sets that deliver them (one set per step where the line's
/// CheckAction spans switches the flow writes as separate steps), and the target-less TOGGLE
/// presses the flows fire toward OFF. One rule on top of the shared one: a line a READ-ONLY step
/// of the same flow completes belongs to that check, not to a write (the gear/flaps shape —
/// write → the action group's line, read-only check → the checklist's).
/// </summary>
public class Pmdg777FlowChecklistLinkTests
{
    // PMDG 777 event → the CDA field(s) it drives, and the field value a written target
    // produces (identity unless noted). EXHAUSTIVE for events the flows write: an event in
    // neither this map nor NoStateEvents fails the audit, so a new step cannot slip past it.
    private static readonly Dictionary<string, (string Field, Func<int, double> Value)[]> EventFields = BuildEventFields();

    private static Dictionary<string, (string Field, Func<int, double> Value)[]> BuildEventFields()
    {
        var m = new Dictionary<string, (string Field, Func<int, double> Value)[]>(StringComparer.Ordinal);
        void Id(string ev, params string[] fields) =>
            m[ev] = fields.Select(f => (f, (Func<int, double>)(t => t))).ToArray();

        Id("EVT_OH_ELEC_BATTERY_SWITCH", "ELEC_Battery_Sw_ON");
        Id("EVT_OH_LIGHTS_STORM", "LTS_Storm_Sw_ON");
        Id("EVT_OH_HYD_ELEC1", "HYD_PrimaryElecPump_Sw_ON_0");
        Id("EVT_OH_HYD_ELEC2", "HYD_PrimaryElecPump_Sw_ON_1");
        Id("EVT_OH_HYD_ENG1", "HYD_PrimaryEngPump_Sw_ON_0");
        Id("EVT_OH_HYD_ENG2", "HYD_PrimaryEngPump_Sw_ON_1");
        Id("EVT_OH_HYD_DEMAND_ELEC1", "HYD_DemandElecPump_Selector_0");
        Id("EVT_OH_HYD_DEMAND_ELEC2", "HYD_DemandElecPump_Selector_1");
        Id("EVT_OH_HYD_AIR1", "HYD_DemandAirPump_Selector_0");
        Id("EVT_OH_HYD_AIR2", "HYD_DemandAirPump_Selector_1");
        Id("EVT_GEAR_LEVER", "GEAR_Lever");      // the LEVER only — never the confirmed-gear synthetics
        Id("EVT_OH_ELEC_BUS_TIE1_SWITCH", "ELEC_BusTie_Sw_AUTO_0");
        Id("EVT_OH_ELEC_BUS_TIE2_SWITCH", "ELEC_BusTie_Sw_AUTO_1");
        // Ground power pushes are TOGGLES: which way a push goes is the step's intent (connect
        // reads 1; the disconnect steps are declared 0 in StepWrites). Either receptacle feeds
        // the "any external power" synthetic.
        Id(GroundPowerGate.EventForAnnunciatorIndex(0), "FO_ANY_GPU_ON");
        Id(GroundPowerGate.EventForAnnunciatorIndex(1), "FO_ANY_GPU_ON");
        Id("EVT_CONTROL_STAND_PARK_BRAKE_LEVER", "BRAKES_ParkingBrakeLeverOn");
        Id("EVT_OH_LIGHTS_NAV", "LTS_NAV_Sw_ON");
        Id("EVT_OH_LIGHTS_LOGO", "LTS_Logo_Sw_ON");
        Id("EVT_OH_ADIRU_SWITCH", "ADIRU_Sw_On");
        Id("EMER_EXIT_LIGHTS", "LTS_EmerLightsSelector");   // guarded pseudo-key; target = selector position
        Id("EVT_OH_THRUST_ASYM_COMP", "FCTL_ThrustAsymComp_Sw_AUTO");
        Id("EVT_OH_ELEC_IFE", "ELEC_IFEPassSeatsSw");
        Id("EVT_OH_ELEC_CAB_UTIL", "ELEC_CabUtilSw");
        Id("EVT_OH_ELEC_APU_GEN_SWITCH", "ELEC_APUGen_Sw_ON");
        Id("EVT_OH_ELEC_GEN1_SWITCH", "ELEC_Gen_Sw_ON_0");
        Id("EVT_OH_ELEC_GEN2_SWITCH", "ELEC_Gen_Sw_ON_1");
        Id("EVT_OH_ELEC_BACKUP_GEN1_SWITCH", "ELEC_BackupGen_Sw_ON_0");
        Id("EVT_OH_ELEC_BACKUP_GEN2_SWITCH", "ELEC_BackupGen_Sw_ON_1");
        Id("EVT_OH_ELEC_APU_SEL_SWITCH", "ELEC_APU_Selector");
        for (int i = 1; i <= 4; i++) Id($"EVT_OH_ICE_WINDOW_HEAT_{i}", $"ICE_WindowHeat_Sw_ON_{i - 1}");
        Id("EVT_OH_ICE_WING_ANTIICE", "ICE_WingAntiIceSw");
        Id("EVT_OH_ICE_ENGINE_ANTIICE_1", "ICE_EngAntiIceSw_0");
        Id("EVT_OH_ICE_ENGINE_ANTIICE_2", "ICE_EngAntiIceSw_1");
        Id("EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", "SIGNS_SeatBeltsSelector");
        Id("EVT_OH_NO_SMOKING_LIGHT_SWITCH", "SIGNS_NoSmokingSelector");
        Id("EVT_OH_FIRE_CARGO_ARM_FWD", "FIRE_CargoFire_Sw_Arm_0");
        Id("EVT_OH_FIRE_CARGO_ARM_AFT", "FIRE_CargoFire_Sw_Arm_1");
        Id("EVT_OH_EEC_L_SWITCH", "ENG_EECMode_Sw_NORM_0");
        Id("EVT_OH_EEC_R_SWITCH", "ENG_EECMode_Sw_NORM_1");
        Id("EVT_OH_ENGINE_AUTOSTART", "ENG_Autostart_Sw_ON");
        Id("EVT_OH_LIGHTS_BEACON", "LTS_Beacon_Sw_ON");
        Id("EVT_OH_LIGHTS_WING", "LTS_Wing_Sw_ON");
        Id("EVT_OH_LIGHTS_TAXI", "LTS_Taxi_Sw_ON");
        Id("EVT_OH_LIGHTS_STROBE", "LTS_Strobe_Sw_ON");
        Id("EVT_OH_LIGHTS_LR_TURNOFF", "LTS_RunwayTurnoff_Sw_ON_0", "LTS_RunwayTurnoff_Sw_ON_1"); // ganged L+R
        Id("EVT_OH_LIGHTS_LANDING_L", "LTS_LandingLights_Sw_ON_0");
        Id("EVT_OH_LIGHTS_LANDING_R", "LTS_LandingLights_Sw_ON_1");
        Id("EVT_OH_LIGHTS_LANDING_NOSE", "LTS_LandingLights_Sw_ON_2");
        Id("EVT_OH_AIRCOND_EQUIP_COOLING_SWITCH", "AIR_EquipCooling_Sw_AUTO");
        Id("EVT_OH_AIRCOND_GASPER_SWITCH", "AIR_Gasper_Sw_On");
        Id("EVT_OH_AIRCOND_RECIRC_FANS_SWITCH", "AIR_RecircFan_Sw_On_0", "AIR_RecircFan_Sw_On_1"); // one switch, both fans
        Id("EVT_OH_AIRCOND_PACK_SWITCH_L", "AIR_Pack_Sw_AUTO_0");
        Id("EVT_OH_AIRCOND_PACK_SWITCH_R", "AIR_Pack_Sw_AUTO_1");
        Id("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_L", "AIR_TrimAir_Sw_On_0");
        Id("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_R", "AIR_TrimAir_Sw_On_1");
        Id("EVT_OH_BLEED_ENG_1_SWITCH", "AIR_EngBleedAir_Sw_AUTO_0");
        Id("EVT_OH_BLEED_ENG_2_SWITCH", "AIR_EngBleedAir_Sw_AUTO_1");
        Id("EVT_OH_BLEED_APU_SWITCH", "AIR_APUBleedAir_Sw_AUTO");
        Id("EVT_OH_PRESS_VALVE_SWITCH_1", "AIR_OutflowValve_Sw_AUTO_0");
        Id("EVT_OH_PRESS_VALVE_SWITCH_2", "AIR_OutflowValve_Sw_AUTO_1");
        Id("EVT_EFIS_CPT_MODE", "EFIS_ModeSel_0");
        Id("EVT_EFIS_FO_MODE", "EFIS_ModeSel_1");
        Id("EVT_EFIS_CPT_RANGE", "EFIS_RangeSel_0");
        Id("EVT_EFIS_FO_RANGE", "EFIS_RangeSel_1");
        Id("EVT_MCP_FD_SWITCH_L", "MCP_FD_Sw_On_0");          // mouse-flag toggles; see StepWrites
        Id("EVT_MCP_FD_SWITCH_R", "MCP_FD_Sw_On_1");
        Id("EVT_MCP_AT_ARM_SWITCH_L", "MCP_ATArm_Sw_On_0");
        Id("EVT_MCP_AT_ARM_SWITCH_R", "MCP_ATArm_Sw_On_1");
        Id("EVT_MCP_LNAV_SWITCH", "MCP_annunLNAV");           // toggle pushed only toward ARMED
        Id("EVT_MCP_VNAV_SWITCH", "MCP_annunVNAV");
        Id("EVT_ABS_AUTOBRAKE_SELECTOR", "BRAKES_AutobrakeSelector");
        Id("EVT_TCAS_MODE", "XPDR_ModeSel");
        Id("EVT_OH_ENGINE_L_START", "ENG_Start_Selector_0");
        Id("EVT_OH_ENGINE_R_START", "ENG_Start_Selector_1");
        // Every pump also feeds the Before Start synthetic (all ON → 1; the center pumps'
        // empty-tank suppression is what that synthetic accepts).
        foreach (var (ev, field) in new[]
        {
            ("EVT_OH_FUEL_PUMP_1_FORWARD", "FUEL_PumpFwd_Sw_0"), ("EVT_OH_FUEL_PUMP_2_FORWARD", "FUEL_PumpFwd_Sw_1"),
            ("EVT_OH_FUEL_PUMP_1_AFT", "FUEL_PumpAft_Sw_0"),     ("EVT_OH_FUEL_PUMP_2_AFT", "FUEL_PumpAft_Sw_1"),
            ("EVT_OH_FUEL_PUMP_L_CENTER", "FUEL_PumpCtr_Sw_0"),  ("EVT_OH_FUEL_PUMP_R_CENTER", "FUEL_PumpCtr_Sw_1"),
        })
            Id(ev, field, "FO_FUEL_PUMPS_BS_OK");
        // Fuel control switches: PMDG's parameter is INVERTED — 1 = CUTOFF, 0 = RUN.
        m["EVT_CONTROL_STAND_ENG1_START_LEVER"] = new (string, Func<int, double>)[]
        { ("ENG_FuelControl_Sw_RUN_0", t => t == 0 ? 1 : 0) };
        m["EVT_CONTROL_STAND_ENG2_START_LEVER"] = new (string, Func<int, double>)[]
        { ("ENG_FuelControl_Sw_RUN_1", t => t == 0 ? 1 : 0) };
        // Per-detent flap lever click events: the detent they select.
        m["EVT_CONTROL_STAND_FLAPS_LEVER_0"] = new (string, Func<int, double>)[] { ("FCTL_Flaps_Lever", _ => 0) };
        m["EVT_CONTROL_STAND_FLAPS_LEVER_1"] = new (string, Func<int, double>)[] { ("FCTL_Flaps_Lever", _ => 1) };
        // The 777 table is keyed by DEGREES: _5 is lever detent 2, _15 detent 3, _20 detent 4,
        // _25 detent 5 (Pmdg777TakeoffFlapsTests).
        m["EVT_CONTROL_STAND_FLAPS_LEVER_5"] = new (string, Func<int, double>)[] { ("FCTL_Flaps_Lever", _ => 2) };
        m["EVT_CONTROL_STAND_FLAPS_LEVER_15"] = new (string, Func<int, double>)[] { ("FCTL_Flaps_Lever", _ => 3) };
        m["EVT_CONTROL_STAND_FLAPS_LEVER_20"] = new (string, Func<int, double>)[] { ("FCTL_Flaps_Lever", _ => 4) };
        m["EVT_CONTROL_STAND_FLAPS_LEVER_25"] = new (string, Func<int, double>)[] { ("FCTL_Flaps_Lever", _ => 5) };
        // Speed brake detent click events (the lever is an analog 0-100 position).
        m["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM"] = new (string, Func<int, double>)[]
        { ("FCTL_Speedbrake_Lever", _ => Pmdg777SpeedbrakeLever.ArmedValue) };
        m["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN"] = new (string, Func<int, double>)[]
        { ("FCTL_Speedbrake_Lever", _ => Pmdg777SpeedbrakeLever.DownValue) };
        return m;
    }

    // Written events with no readable CDA state behind them. Lines they deliver are
    // action-only, matched through ActionItems below.
    private static readonly HashSet<string> NoStateEvents = new(StringComparer.Ordinal)
    {
        "EVT_OH_WIPER_LEFT_SWITCH", "EVT_OH_WIPER_RIGHT_SWITCH",     // no wiper field in the CDA struct
        "EVT_ALTN_FLAPS_POS",                                       // no reliable alternate-flaps field
        "EVT_OH_CVR_TEST",                                          // momentary test
        "EVT_OH_LIGHTS_IND_LTS_SWITCH",                             // no indicator-lights-master field
        "EVT_OH_FUEL_JETTISON_NOZZLE_L", "EVT_OH_FUEL_JETTISON_NOZZLE_R", "EVT_OH_FUEL_JETTISON_ARM",
        "EVT_OH_FUEL_CROSSFEED_FORWARD", "EVT_OH_FUEL_CROSSFEED_AFT",
        "EVT_DSP_CANC_RCL_SWITCH",                                  // momentary
        // Held self-completing test pseudo-keys (no persistent "test performed" state).
        "OXY_TEST_CAPT", "OXY_TEST_FO", "FIRE_OVHT_TEST", "TCAS_TEST", "WXR_TEST",
    };

    private static (string Event, int Target)[] W(params (string Event, int Target)[] writes) => writes;

    // Action-only lines (no auto-detect field) → the write sets their CheckAction fires. A step
    // making every write of ANY one set delivers the line; a line whose CheckAction spans two
    // switches the flow writes as two steps lists one set per step.
    private static readonly Dictionary<string, (string Event, int Target)[][]> ActionItems = new(StringComparer.Ordinal)
    {
        ["EPU_WIPERS_OFF"] = new[] { W(("EVT_OH_WIPER_LEFT_SWITCH", 0)), W(("EVT_OH_WIPER_RIGHT_SWITCH", 0)) },
        ["EPU_ALT_FLAPS_OFF"] = new[] { W(("EVT_ALTN_FLAPS_POS", 0)) },
        ["PF_OXY_TEST_CAPT"] = new[] { W(("OXY_TEST_CAPT", 1)) },
        ["PF_OXY_TEST_FO"] = new[] { W(("OXY_TEST_FO", 1)) },
        ["PF_FIRE_TEST"] = new[] { W(("FIRE_OVHT_TEST", 1)) },
        ["PF_TCAS_TEST"] = new[] { W(("TCAS_TEST", 1)) },
        ["PF_WXR_TEST"] = new[] { W(("WXR_TEST", 1)) },
        ["PF_MASTER_LIGHTS"] = new[] { W(("EVT_OH_LIGHTS_IND_LTS_SWITCH", 1)) },
        ["PF_JETTISON_OFF"] = new[]
        {
            W(("EVT_OH_FUEL_JETTISON_NOZZLE_L", 0), ("EVT_OH_FUEL_JETTISON_NOZZLE_R", 0)),
            W(("EVT_OH_FUEL_JETTISON_ARM", 0)),
        },
        ["PF_CROSSFEED_OFF"] = new[] { W(("EVT_OH_FUEL_CROSSFEED_FORWARD", 0), ("EVT_OH_FUEL_CROSSFEED_AFT", 0)) },
        ["PF_EFIS_SET"] = new[]
        {
            W(("EVT_EFIS_CPT_MODE", 2)), W(("EVT_EFIS_FO_MODE", 2)),
            W(("EVT_EFIS_CPT_RANGE", 2)), W(("EVT_EFIS_FO_RANGE", 2)),
        },
        ["BS_CANCEL_RECALL"] = new[] { W(("EVT_DSP_CANC_RCL_SWITCH", 1)) },
        ["BT_RECALL"] = new[] { W(("EVT_DSP_CANC_RCL_SWITCH", 1)) },
        ["DSCA_RECALL"] = new[] { W(("EVT_DSP_CANC_RCL_SWITCH", 1)) },
        ["SEC_EMER_EXIT_OFF"] = new[] { W(("EMER_EXIT_LIGHTS", EmerExitLightSequence.Off)) },
        // The Secure Checklist's read-back of that same switch (a Manual line with no state).
        ["SECCL_EMER_LIGHTS"] = new[] { W(("EMER_EXIT_LIGHTS", EmerExitLightSequence.Off)) },
        ["SEC_GND_PWR_OFF"] = new[]
        {
            W((GroundPowerGate.EventForAnnunciatorIndex(0), 0)), W((GroundPowerGate.EventForAnnunciatorIndex(1), 0)),
        },
        ["EPD_APU_GND_OFF"] = new[]
        {
            W(("EVT_OH_ELEC_APU_SEL_SWITCH", 0)),
            W((GroundPowerGate.EventForAnnunciatorIndex(0), 0)), W((GroundPowerGate.EventForAnnunciatorIndex(1), 0)),
        },
    };

    // Target-less TOGGLE presses the flows fire toward OFF (their SkipCondition guards the
    // direction). Every other target-less write reads as 1.
    private static readonly Dictionary<string, (string Event, int Target)[]> StepWrites = new(StringComparer.Ordinal)
    {
        ["BS_GND_PWR_1"] = W((GroundPowerGate.EventForAnnunciatorIndex(0), 0)),
        ["BS_GND_PWR_2"] = W((GroundPowerGate.EventForAnnunciatorIndex(1), 0)),
        ["SEC_GND_PWR_PRIM"] = W((GroundPowerGate.EventForAnnunciatorIndex(0), 0)),
        ["SEC_GND_PWR_SEC"] = W((GroundPowerGate.EventForAnnunciatorIndex(1), 0)),
        ["SD_FD_L"] = W(("EVT_MCP_FD_SWITCH_L", 0)),
        ["SD_FD_R"] = W(("EVT_MCP_FD_SWITCH_R", 0)),
    };

    private static List<(string Event, int Target)> Writes(FlowStep<AircraftStateEvaluator> step) =>
        StepWrites.TryGetValue(step.Id, out var declared) ? declared.ToList() : FlowChecklistLinkAudit.Writes(step);

    private static IReadOnlyList<(string Field, Func<int, double> Value)>? FieldsOf(string ev) =>
        EventFields.TryGetValue(ev, out var f) ? f : null;

    private static readonly Dictionary<string, (string Event, int Target)[]> NoActionItems = new();

    // The groups FirstOfficerForm actually latches when each flow finishes.
    private static List<(FlowDefinition<AircraftStateEvaluator> Flow,
        List<ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator>> Items)> FlowsWithItems()
    {
        var groups = PMDG777ChecklistDefinitions.Build();
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return FlowChecklistLinkAudit.FlowsWithItems(PMDG777FlowDefinitions.Build(), groups,
            f => f.CompletionGroupIds(ids.Contains));
    }

    // Lines a READ-ONLY step (a wait or check) of the same flow completes — owned by the check.
    private static bool OwnedByACheck(FlowStep<AircraftStateEvaluator> step, string itemId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Steps.Any(s => s.Id == step.Id)).Steps
            .Where(s => !FlowChecklistLinkAudit.IsWrite(s))
            .Any(s => s.LinkedChecklistItemIds.Contains(itemId));

    private static bool Delivers(FlowStep<AircraftStateEvaluator> step,
        ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> item)
    {
        if (OwnedByACheck(step, item.Id)) return false;
        var writes = Writes(step);
        if (ActionItems.TryGetValue(item.Id, out var alternatives))
            return alternatives.Any(needed => needed.All(writes.Contains));
        return FlowChecklistLinkAudit.Delivers(writes, item, FieldsOf, NoActionItems);
    }

    [Fact]
    public void Every_written_event_is_mapped_to_its_state_or_declared_stateless()
    {
        var unmapped = FlowsWithItems()
            .SelectMany(f => f.Flow.Steps.Where(FlowChecklistLinkAudit.IsWrite).SelectMany(Writes)
                .Where(w => FieldsOf(w.Event) == null && !NoStateEvents.Contains(w.Event))
                .Select(w => $"{f.Flow.Id}: {w.Event}"))
            .Distinct().ToList();
        Assert.True(unmapped.Count == 0, "Unmapped events: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void Toggle_steps_declared_toward_OFF_exist_and_carry_no_target()
    {
        var steps = PMDG777FlowDefinitions.Build().SelectMany(f => f.Steps).ToDictionary(s => s.Id);
        foreach (var (id, writes) in StepWrites)
        {
            Assert.True(steps.ContainsKey(id), $"StepWrites names unknown step {id}");
            var step = steps[id];
            Assert.Null(step.TargetValue);
            Assert.NotNull(step.SkipCondition);   // a toggle is only safe behind its skip guard
            Assert.Equal(step.EventName, Assert.Single(writes).Event);
        }
    }

    [Fact]
    public void Every_write_step_completes_the_line_it_sets()
    {
        var problems = FlowChecklistLinkAudit.LinkProblems(FlowsWithItems(), Delivers);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // CLAUDE.md's one sanctioned StayComplete exception: the engine-start SELECTOR lines latch
    // off the engine's N2 and are never linked — a START write is not a completed start, and
    // a link would tick the line the moment the selector moves.
    [Fact]
    public void No_step_links_a_StayComplete_engine_start_selector_line()
    {
        var stay = PMDG777ChecklistDefinitions.Build().SelectMany(g => g.Items)
            .Where(i => i.RevertBehavior == RevertBehavior.StayComplete && i.IsAutoDetectable)
            .Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ES_ENG1_START_SEL", "ES_ENG2_START_SEL" }, stay);
        foreach (var flow in PMDG777FlowDefinitions.Build())
            foreach (var step in flow.Steps)
                Assert.False(step.LinkedChecklistItemIds.Any(stay.Contains),
                    $"{flow.Id}/{step.Id} links a StayComplete line - a START write is not a completed start");
    }

    [Fact]
    public void Electrical_power_up_verified_writes_complete_their_lines()
    {
        var steps = PMDG777FlowDefinitions.Build().Single(f => f.Id == "ELECTRICAL_POWER_UP").Steps;
        Assert.Equal("EPU_NAV_LIGHTS", steps.Single(s => s.Id == "EPU_NAV_LIGHTS").CompletesChecklistItemId);
        Assert.Equal("EPU_ADIRU", steps.Single(s => s.Id == "EPU_ADIRU").CompletesChecklistItemId);
        Assert.Equal("EPU_STORM_OFF", steps.Single(s => s.Id == "EPU_STORM_OFF").CompletesChecklistItemId);
    }

    // The After Takeoff shape is unchanged: each WRITE completes its own group's line, the
    // read-only checks complete the checklist's, and the gear check stays last.
    [Fact]
    public void After_takeoff_gear_and_flaps_keep_the_write_line_check_line_shape()
    {
        var steps = PMDG777FlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF").Steps;
        Assert.Equal(new[] { "ATKO_GEAR_UP" }, steps.Single(s => s.Id == "ATKOF_GEAR_UP").LinkedChecklistItemIds);
        Assert.Equal(new[] { "ATKO_FLAPS_UP" }, steps.Single(s => s.Id == "ATKOF_FLAPS_UP").LinkedChecklistItemIds);
        Assert.Equal(new[] { "ATKOF_FLAPS" }, steps.Single(s => s.Id == "ATKOF_FLAPS_UP_CHECK").LinkedChecklistItemIds);
        Assert.Equal(new[] { "ATKOF_GEAR" }, steps.Single(s => s.Id == "ATKOF_GEAR_UP_CHECK").LinkedChecklistItemIds);
        Assert.Equal("ATKOF_GEAR_UP_CHECK", steps[^1].Id);

        var landing = PMDG777FlowDefinitions.Build().Single(f => f.Id == "LANDING").Steps;
        Assert.Equal("LD_GEAR_DOWN_CHECK", landing[^1].Id);
    }

    [Fact]
    public void Every_written_event_is_in_the_777_event_table()
    {
        // A write to an event the 777 table does not carry fails at dispatch every time. The
        // Before Taxi flaps steps once named 737-style detent INDICES (_2/_3/_4) where the
        // table is keyed by DEGREES, so flaps 5 / 15 / 20 never moved; none may be missing.
        var pseudoKeys = new HashSet<string>(StringComparer.Ordinal)
        { "OXY_TEST_CAPT", "OXY_TEST_FO", "FIRE_OVHT_TEST", "TCAS_TEST", "WXR_TEST", "EMER_EXIT_LIGHTS" };
        var missing = PMDG777FlowDefinitions.Build()
            .SelectMany(f => f.Steps.Where(FlowChecklistLinkAudit.IsWrite).SelectMany(Writes))
            .Select(w => w.Event)
            .Where(e => !pseudoKeys.Contains(e) && !PMDG777Definition.EventIds.ContainsKey(e))
            .Distinct().OrderBy(e => e, StringComparer.Ordinal).ToArray();
        Assert.Empty(missing);
    }

    // Characterization of what is still latched with no step behind it: every non-reminder line
    // in a latched group that no step of the flow names. Each is a known, reviewed gap — a failed
    // step cannot leave these live. Change this list deliberately.
    [Fact]
    public void Lines_latched_with_no_delivering_step_are_the_known_set()
    {
        Assert.Equal(new[]
        {
            // --- Lines with readable state or a CheckAction, but no step in this flow ---
            // Before Start: seat belts are set ON in Cockpit Prep, not here; the APU start's
            // Stop-policy APU-running wait aborts the flow before any latch if it fails.
            // Before Taxi: autobrake RTO is set in Cockpit Prep; recall has no flow step.
            // Before Taxi "Flap lever: Set for takeoff" (BT_FLAPS): its five plan-gated flap
            // steps are NOT linked — the four not planned skip as "Already set", which would tick
            // the line whatever the planned step does.
            // Before Takeoff "Flaps: Set for takeoff" and Landing "Flaps: Set for landing": the
            // captain sets the flaps; no flow step writes them.
            // Cockpit Prep: battery, ADIRU, demand pumps, nav lights and gear DOWN are set in
            // Electrical Power Up; no step turns the fuel pumps OFF.
            // Descent: recall is a Captain reminder.
            // Engine start selectors: StayComplete on N2 (never linked — see above).
            // Shutdown Checklist "Flaps: UP": the flaps are retracted in After Landing.
            // --- Manual verification lines (no state, no action) the flow still latches ---
            // (trim, CDU, V speeds, MCP, altimeters, anti-ice, flight controls, recall, oil
            // pressure, WX radar, APU) — delivered by the pilot or a Captain reminder step.
            "AFTER_LANDING/AL_WX_RADAR_OFF",
            "APPROACH_SETUP/APP_ALTIMETERS",
            "BEFORE_START/BSCL_CDU_COMPLETE",
            "BEFORE_START/BSCL_MCP",
            "BEFORE_START/BSCL_SIGNS",
            "BEFORE_START/BSCL_TRIM",
            "BEFORE_START/BSCL_V_SPEEDS",
            "BEFORE_START/BS_AIL_TRIM",
            "BEFORE_START/BS_APU_START",
            "BEFORE_START/BS_CDU_COMPLETE",
            "BEFORE_START/BS_INIT_ALT",
            "BEFORE_START/BS_INIT_HDG",
            "BEFORE_START/BS_RUD_TRIM",
            "BEFORE_START/BS_SEAT_BELTS",
            "BEFORE_START/BS_STAB_TRIM",
            "BEFORE_START/BS_V2_SET",
            "BEFORE_TAKEOFF/BTKOF_ALT",
            "BEFORE_TAKEOFF/BTKOF_FLAPS",
            "BEFORE_TAKEOFF/BTKOF_V_SPEEDS",
            "BEFORE_TAXI/BTCL_ANTI_ICE",
            "BEFORE_TAXI/BTCL_AUTOBRAKE",
            "BEFORE_TAXI/BTCL_FCTL",
            "BEFORE_TAXI/BTCL_RECALL",
            "BEFORE_TAXI/BT_ANTI_ICE",
            "BEFORE_TAXI/BT_FCTL_CHECK",
            "BEFORE_TAXI/BT_FLAPS",
            "BEFORE_TAXI/BT_RECALL",
            "COCKPIT_PREP/PF_ADIRU",
            "COCKPIT_PREP/PF_BARO_SET",
            "COCKPIT_PREP/PF_BATTERY",
            "COCKPIT_PREP/PF_CDU_PREFLIGHT",
            "COCKPIT_PREP/PF_DEMAND_PUMPS_OFF",
            "COCKPIT_PREP/PF_FMC_PERF",
            "COCKPIT_PREP/PF_FUEL_PUMPS_OFF",
            "COCKPIT_PREP/PF_GEAR_DOWN",
            "COCKPIT_PREP/PF_NAV_LIGHTS",
            "DESCENT_SETUP/DSCA_RECALL",
            "DESCENT_SETUP/DSC_AUTOBRAKE",
            "DESCENT_SETUP/DSC_LANDING_DATA",
            "DESCENT_SETUP/DSC_RECALL",
            "ENGINE_START/ES_ENG1_OIL_PRESS",
            "ENGINE_START/ES_ENG1_START_SEL",
            "ENGINE_START/ES_ENG2_OIL_PRESS",
            "ENGINE_START/ES_ENG2_START_SEL",
            "LANDING/LDG_FLAPS",
            "SHUTDOWN/SDCL_FLAPS_UP",
            "SHUTDOWN/SDCL_HYD",
            "SHUTDOWN/SDCL_WX_RADAR",
            "SHUTDOWN/SD_APU",
        }, FlowChecklistLinkAudit.UndeliveredLines(FlowsWithItems()));
    }

    // On the 777 no group is named after a flow (or its "_CL"), so a finished flow latches
    // exactly its related groups — the audit above covers every line the form latches.
    [Fact]
    public void Every_777_flow_latches_exactly_its_related_groups()
    {
        var ids = PMDG777ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var flow in PMDG777FlowDefinitions.Build())
            Assert.Equal(flow.RelatedChecklistGroupIds, flow.CompletionGroupIds(ids.Contains).ToArray());
    }

    // The After Landing APU start is confirmed by the read-only APU-running wait, never by the
    // selector writes: a timeout skips aloud and keeps "APU: START" out of the latch.
    [Fact]
    public void After_landing_APU_line_is_completed_by_the_APU_running_wait()
    {
        var steps = PMDG777FlowDefinitions.Build().Single(f => f.Id == "AFTER_LANDING").Steps;
        var wait = steps.Single(s => s.Id == "AL_APU_RUNNING");
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal(FlowStepFailurePolicy.Skip, wait.FailurePolicy);
        Assert.Equal(new[] { "AL_APU" }, wait.LinkedChecklistItemIds);
        Assert.Empty(steps.Single(s => s.Id == "AL_APU_ON").LinkedChecklistItemIds);
        Assert.Empty(steps.Single(s => s.Id == "AL_APU_START").LinkedChecklistItemIds);
    }
}
