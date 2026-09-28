using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Audit: every PMDG 737 First Officer flow step that WRITES a switch is tied
/// (<c>CompletesChecklistItemId</c>) to the checklist line it sets, in a group the flow
/// latches. Why it matters: when a flow finishes, <c>ChecklistManager.MarkGroupComplete</c>
/// ticks and latches every line of its related groups EXCEPT those named by a SKIPPED
/// step's <c>CompletesChecklistItemId</c>. A write step with no link therefore has its line
/// latched complete even when the write failed — a switch the checklist then claims is set.
///
/// The step→line match is derived, not hand-listed: each written PMDG event is mapped to the
/// CDA field(s) it drives (below), and a line is a candidate when the step writes one of its
/// auto-detect fields and every such write satisfies the line's own condition — i.e. the
/// step actually achieves the line. Action-only lines (no readable state) are matched by the
/// writes their own CheckAction fires. A write that achieves a line in BOTH its own action
/// group and the phase's read-back (*_CL) checklist completes both — the action-group line as
/// <c>CompletesChecklistItemId</c> (the gear-fix rule), the read-back line through
/// <c>AlsoCompletesChecklistItemIds</c> — so a failed write leaves neither latched.
/// StayComplete lines (the engine-start selector items, CLAUDE.md's one sanctioned
/// exception) are never a candidate: a GRD write is not a completed start, and linking it
/// would latch the item on an aborted start.
/// </summary>
public class Pmdg737FlowChecklistLinkTests
{
    // PMDG 737 event → the CDA field(s) it drives, and the field value a given written target
    // produces (identity unless noted). EXHAUSTIVE for events the flows write: an event in
    // neither this map nor NoStateEvents fails the audit, so a new step cannot slip past it.
    private static readonly Dictionary<string, (string Field, Func<int, double> Value)[]> EventFields = BuildEventFields();

    private static Dictionary<string, (string Field, Func<int, double> Value)[]> BuildEventFields()
    {
        var m = new Dictionary<string, (string Field, Func<int, double> Value)[]>(StringComparer.Ordinal);
        void Id(string ev, params string[] fields) =>
            m[ev] = fields.Select(f => (f, (Func<int, double>)(t => t))).ToArray();

        Id("EVT_OH_ELEC_BATTERY_SWITCH", "ELEC_BatSelector");
        Id("EVT_OH_ELEC_STBY_PWR_SWITCH", "ELEC_StandbyPowerSelector");
        Id("EVT_IRU_MSU_LEFT", "IRS_ModeSelector_0");
        Id("EVT_IRU_MSU_RIGHT", "IRS_ModeSelector_1");
        Id("EVT_OH_YAW_DAMPER", "FCTL_YawDamper_Sw");
        // Every pump also feeds the Before Start synthetic (all ON → 1).
        Id("EVT_OH_FUEL_PUMP_1_FORWARD", "FUEL_PumpFwdSw_0", "FO_FUEL_PUMPS_BS_OK");
        Id("EVT_OH_FUEL_PUMP_2_FORWARD", "FUEL_PumpFwdSw_1", "FO_FUEL_PUMPS_BS_OK");
        Id("EVT_OH_FUEL_PUMP_1_AFT", "FUEL_PumpAftSw_0", "FO_FUEL_PUMPS_BS_OK");
        Id("EVT_OH_FUEL_PUMP_2_AFT", "FUEL_PumpAftSw_1", "FO_FUEL_PUMPS_BS_OK");
        Id("EVT_OH_FUEL_PUMP_L_CENTER", "FUEL_PumpCtrSw_0", "FO_FUEL_PUMPS_BS_OK");
        Id("EVT_OH_FUEL_PUMP_R_CENTER", "FUEL_PumpCtrSw_1", "FO_FUEL_PUMPS_BS_OK");
        Id("EVT_OH_EMER_EXIT_LIGHT_SWITCH", "LTS_EmerExitSelector");
        Id("EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", "COMM_FastenBeltsSelector");
        for (int i = 1; i <= 4; i++) Id($"EVT_OH_ICE_WINDOW_HEAT_{i}", $"ICE_WindowHeatSw_{i - 1}");
        Id("EVT_OH_ICE_PROBE_HEAT_1", "ICE_ProbeHeatSw_0");
        Id("EVT_OH_ICE_PROBE_HEAT_2", "ICE_ProbeHeatSw_1");
        Id("EVT_OH_ICE_WING_ANTIICE", "ICE_WingAntiIceSw");
        Id("EVT_OH_ICE_ENGINE_ANTIICE_1", "ICE_EngAntiIceSw_0");
        Id("EVT_OH_ICE_ENGINE_ANTIICE_2", "ICE_EngAntiIceSw_1");
        Id("EVT_OH_BLEED_RECIRC_FAN_L_SWITCH", "AIR_RecircFanSwitch_0");
        Id("EVT_OH_BLEED_RECIRC_FAN_R_SWITCH", "AIR_RecircFanSwitch_1");
        Id("EVT_OH_BLEED_PACK_L_SWITCH", "AIR_PackSwitch_0");
        Id("EVT_OH_BLEED_PACK_R_SWITCH", "AIR_PackSwitch_1");
        Id("EVT_OH_BLEED_ISOLATION_VALVE_SWITCH", "AIR_IsolationValveSwitch");
        Id("EVT_OH_BLEED_ENG_1_SWITCH", "AIR_BleedAirSwitch_0");
        Id("EVT_OH_BLEED_ENG_2_SWITCH", "AIR_BleedAirSwitch_1");
        Id("EVT_OH_BLEED_APU_SWITCH", "AIR_APUBleedAirSwitch");
        Id("EVT_OH_LIGHTS_LOGO", "LTS_LogoSw");
        Id("EVT_MCP_FD_SWITCH_L", "MCP_FDSw_0");       // mouse-flag toggle, fired only toward ON
        Id("EVT_MCP_FD_SWITCH_R", "MCP_FDSw_1");
        Id("EVT_MCP_AT_ARM_SWITCH", "MCP_ATArmSw");
        Id("EVT_MPM_AUTOBRAKE_SELECTOR", "MAIN_AutobrakeSelector");
        Id("EVT_TCAS_MODE", "XPDR_ModeSel");
        Id("EVT_EFIS_CPT_MODE", "EFIS_ModeSel_0");
        Id("EVT_EFIS_CPT_RANGE", "EFIS_RangeSel_0");
        Id("EVT_OH_LIGHTS_APU_START", "APU_Selector");
        Id("EVT_OH_HYD_ENG1", "HYD_PumpSw_eng_0");
        Id("EVT_OH_HYD_ENG2", "HYD_PumpSw_eng_1");
        Id("EVT_OH_HYD_ELEC1", "HYD_PumpSw_elec_0");
        Id("EVT_OH_HYD_ELEC2", "HYD_PumpSw_elec_1");
        Id("EVT_OH_LIGHTS_ANT_COL", "LTS_AntiCollisionSw");
        Id("EVT_OH_LIGHTS_L_ENGINE_START", "ENG_StartSelector_0");
        Id("EVT_OH_LIGHTS_R_ENGINE_START", "ENG_StartSelector_1");
        Id("EVT_OH_ELEC_GEN1_SWITCH", "ELEC_GenSw_0");
        Id("EVT_OH_ELEC_GEN2_SWITCH", "ELEC_GenSw_1");
        Id("EVT_OH_LIGHTS_TAXI", "LTS_TaxiSw");
        Id("EVT_OH_LIGHTS_L_TURNOFF", "LTS_RunwayTurnoffSw_0");
        Id("EVT_OH_LIGHTS_R_TURNOFF", "LTS_RunwayTurnoffSw_1");
        Id("EVT_OH_LIGHTS_L_RETRACT", "LTS_LandingLtRetractableSw_0");
        Id("EVT_OH_LIGHTS_R_RETRACT", "LTS_LandingLtRetractableSw_1");
        Id("EVT_OH_LIGHTS_L_FIXED", "LTS_LandingLtFixedSw_0");
        Id("EVT_OH_LIGHTS_R_FIXED", "LTS_LandingLtFixedSw_1");
        Id("EVT_OH_LIGHTS_POS_STROBE", "LTS_PositionSw");
        // Start levers: 1 = RUN (ENG_StartLever 1, valve-closed annunciator dark),
        // 0 = CUTOFF (valve-closed annunciator lit).
        m["EVT_CONTROL_STAND_ENG1_START_LEVER"] = new (string, Func<int, double>)[]
        { ("ENG_StartLever_0", t => t), ("FUEL_annunENG_VALVE_CLOSED_0", t => t == 0 ? 1 : 0) };
        m["EVT_CONTROL_STAND_ENG2_START_LEVER"] = new (string, Func<int, double>)[]
        { ("ENG_StartLever_1", t => t), ("FUEL_annunENG_VALVE_CLOSED_1", t => t == 0 ? 1 : 0) };
        // Per-detent flap lever event: flaps UP → TE flap needle 0 degrees.
        m["EVT_CONTROL_STAND_FLAPS_LEVER_0"] = new (string, Func<int, double>)[]
        { ("MAIN_TEFlapsNeedle_0", _ => 0) };
        // Closed-loop verified arm (ArmSpeedbrakeAsync) → the ARMED annunciator.
        m[SpeedbrakeArmLadder.PseudoKey] = new (string, Func<int, double>)[]
        { (SpeedbrakeArmLadder.ArmedField, _ => 1) };
        return m;
    }

    // Written events with no readable CDA state behind them. Lines they deliver are
    // action-only, matched through ActionItems below.
    private static readonly HashSet<string> NoStateEvents = new(StringComparer.Ordinal)
    {
        "EVT_OH_ELEC_GRD_PWR_SWITCH",        // the raw bool lies (no reliable GPU state)
        "EVT_OH_ELEC_APU_GEN1_SWITCH", "EVT_OH_ELEC_APU_GEN2_SWITCH", // stateless push pairs
        "EVT_MPM_FUEL_FLOW_SWITCH",          // momentary reset
        "EVT_DSP_CPT_LOWER_DU_SELECTOR",
        "EVT_OH_ATTND_CALL_SWITCH",          // cabin chime
        // SimBrief-driven DynSW targets. PF_PRESS reads the synthetic FO_PRESS_ALTS_MATCH,
        // which needs BOTH windows and a loaded plan (a null target is a quiet "success").
        "EVT_OH_PRESS_FLT_ALT_SET", "EVT_OH_PRESS_LAND_ALT_SET",
        // Held self-completing test pseudo-keys (no persistent "test performed" state).
        "OXY_TEST_CAPT", "OXY_TEST_FO", "FIRE_TEST", "STALL_TEST_1", "STALL_TEST_2",
        "OVSPD_TEST_1", "OVSPD_TEST_2", "TCAS_TEST", "WXR_TEST", "GPWS_TEST",
    };

    // Action-only lines (no auto-detect field) → the writes their CheckAction fires. A step
    // that makes ALL of those writes delivers the line.
    private static readonly Dictionary<string, (string Event, int Target)[]> ActionItems = new(StringComparer.Ordinal)
    {
        ["EPU_GPU"] = new[] { ("EVT_OH_ELEC_GRD_PWR_SWITCH", 1) },
        ["SE_GND_PWR_OFF"] = new[] { ("EVT_OH_ELEC_GRD_PWR_SWITCH", 0) },
        ["BS_APUGEN"] = new[] { ("EVT_OH_ELEC_APU_GEN1_SWITCH", 1), ("EVT_OH_ELEC_APU_GEN2_SWITCH", 1) },
        ["SD_APUGEN"] = new[] { ("EVT_OH_ELEC_APU_GEN1_SWITCH", 1), ("EVT_OH_ELEC_APU_GEN2_SWITCH", 1) },
        ["BT_LOWERDU"] = new[] { ("EVT_DSP_CPT_LOWER_DU_SELECTOR", 1) },
        ["BTKO_CABIN"] = new[] { ("EVT_OH_ATTND_CALL_SWITCH", 1) },
        ["AP_CABIN"] = new[] { ("EVT_OH_ATTND_CALL_SWITCH", 1) },
        ["PF_OXY_TEST_CAPT"] = new[] { ("OXY_TEST_CAPT", 1) },
        ["PF_OXY_TEST_FO"] = new[] { ("OXY_TEST_FO", 1) },
        ["PF_FIRE_TEST"] = new[] { ("FIRE_TEST", 1) },
        ["PF_STALL_TEST1"] = new[] { ("STALL_TEST_1", 1) },
        ["PF_STALL_TEST2"] = new[] { ("STALL_TEST_2", 1) },
        ["PF_OVSPD_TEST1"] = new[] { ("OVSPD_TEST_1", 1) },
        ["PF_OVSPD_TEST2"] = new[] { ("OVSPD_TEST_2", 1) },
        ["PF_TCAS_TEST"] = new[] { ("TCAS_TEST", 1) },
        ["PF_WXR_TEST"] = new[] { ("WXR_TEST", 1) },
        ["PF_GPWS_TEST"] = new[] { ("GPWS_TEST", 1) },
    };

    private static bool Delivers(FlowStep<AircraftStateEvaluator> step,
        ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> item) =>
        FlowChecklistLinkAudit.Delivers(FlowChecklistLinkAudit.Writes(step), item,
            ev => EventFields.TryGetValue(ev, out var f) ? f : null, ActionItems);

    // Every group a finishing flow latches — FlowDefinition.CompletionGroupIds, the SAME rule
    // FirstOfficerForm uses: the flow's RelatedChecklistGroupIds plus the group named after the
    // flow and its "_CL" read-back. Auditing RelatedChecklistGroupIds alone missed twelve
    // read-back lines (PREFLIGHT_CL, BEFORE_START_CL, BEFORE_TAXI_CL, SHUTDOWN_CL, SECURE_CL)
    // that a failed write still latched done.
    private static List<(FlowDefinition<AircraftStateEvaluator> Flow,
        List<ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator>> Items)> FlowsWithItems()
    {
        var groups = PMDG737ChecklistDefinitions.Build();
        var ids = groups.Select(g => g.Id).ToHashSet();
        return FlowChecklistLinkAudit.FlowsWithItems(PMDG737FlowDefinitions.Build(),
            groups, f => f.CompletionGroupIds(ids.Contains));
    }

    [Fact]
    public void Every_written_event_is_mapped_to_its_state_or_declared_stateless()
    {
        var unmapped = FlowsWithItems()
            .SelectMany(f => f.Flow.Steps.Where(FlowChecklistLinkAudit.IsWrite).SelectMany(FlowChecklistLinkAudit.Writes)
                .Where(w => !EventFields.ContainsKey(w.Event) && !NoStateEvents.Contains(w.Event))
                .Select(w => $"{f.Flow.Id}: {w.Event}"))
            .Distinct().ToList();
        Assert.True(unmapped.Count == 0, "Unmapped events: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void Every_write_step_completes_the_line_it_sets()
    {
        var problems = FlowChecklistLinkAudit.LinkProblems(FlowsWithItems(), Delivers);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void No_step_links_a_StayComplete_engine_start_selector_line()
    {
        var stay = PMDG737ChecklistDefinitions.Build().SelectMany(g => g.Items)
            .Where(i => i.RevertBehavior == RevertBehavior.StayComplete && i.IsAutoDetectable)
            .Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ES_E1_GRD", "ES_E2_GRD" }, stay);
        foreach (var flow in PMDG737FlowDefinitions.Build())
            foreach (var step in flow.Steps)
                Assert.False(step.CompletesChecklistItemId != null && stay.Contains(step.CompletesChecklistItemId),
                    $"{flow.Id}/{step.Id} links a StayComplete line - a GRD write is not a completed start");
    }

    // The verified arm completes BOTH "Speedbrake: ARMED" lines. It used to name only the
    // Landing Checklist's LDC_SPDBRK, so a failed arm left the Landing group's LDA_SPDBRK to
    // MarkGroupComplete's blanket sweep — latched complete over a lever that never armed.
    [Fact]
    public void Landing_speedbrake_arm_completes_both_speedbrake_lines()
    {
        var steps = PMDG737FlowDefinitions.Build().Single(f => f.Id == "LANDING").Steps;
        var arm = steps.Single(s => s.Id == "LD_SPDBRK");

        Assert.Equal("LDA_SPDBRK", arm.CompletesChecklistItemId);
        Assert.Equal(new[] { "LDA_SPDBRK", "LDC_SPDBRK" }, arm.LinkedChecklistItemIds.ToArray());
        Assert.Equal(FlowStepFailurePolicy.Skip, arm.FailurePolicy);
        // No step was added: the gear check stays the LAST step (CLAUDE.md gear invariant).
        Assert.Equal("LD_GEAR_DOWN_CHECK", steps[^1].Id);
    }

    // Characterization of what is still latched with no step behind it: auto-detect and
    // action lines (captain reminders are delivered by the reminder itself) in a latched
    // group that no step of the flow names. Each is a known, reviewed gap — a failed step
    // cannot leave these live. Change this list deliberately.
    [Fact]
    public void Lines_latched_with_no_delivering_step_are_the_known_set()
    {
        var undelivered = FlowChecklistLinkAudit.UndeliveredLines(FlowsWithItems());

        Assert.Equal(new[]
        {
            // After Takeoff Checklist: engine bleeds have no step in the After Takeoff flow
            // (they are set ON in Preflight).
            "AFTER_TAKEOFF/ATC_BLEEDS",
            // Before Start Checklist: passenger signs are set ON in Preflight, not Before Start.
            "BEFORE_START/BSC_BELTS",
            // Before Taxi Checklist: autobrake RTO is set in Preflight, not Before Taxi.
            "BEFORE_TAXI/BTC_AB",
            // Before Taxi: the six-pack recall has no flow step.
            "BEFORE_TAXI/BT_RECALL",
            // Descent Checklist: LAND ALT is set in Preflight, not Descent.
            "DESCENT/DC_PRESS",
            // Descent: recall has no flow step.
            "DESCENT/DSA_RECALL",
            // Engine start selectors: StayComplete on N2 (never linked — see above); the
            // Stop-policy start-valve / N2 waits abort the flow before any latch on a failure.
            "ENGINE_START/ES_E1_GRD",
            "ENGINE_START/ES_E2_GRD",
            // Preflight Checklist read-backs no Preflight step sets: the start levers, the parking
            // brake and the pressurization mode selector are never written by the flow.
            "PREFLIGHT/PFC_LEVERS",
            "PREFLIGHT/PFC_PARK",
            "PREFLIGHT/PFC_PRESS",
            // Two SimBrief DynSW steps + a Captain fallback; a missing plan is a quiet success.
            "PREFLIGHT/PF_PRESS",
            // Shutdown Checklist: parking brake is the Captain's, probe heat is switched off in
            // After Landing, not Shutdown.
            "SHUTDOWN/SDC_PARK",
            "SHUTDOWN/SDC_PROBE",
        }, undelivered);
    }
}
