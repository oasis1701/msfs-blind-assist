using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Audit: every iFly 737 MAX8 First Officer flow step that WRITES a switch is tied
/// (<c>CompletesChecklistItemId</c> / <c>AlsoCompletesChecklistItemIds</c>) to every checklist
/// line it sets in the groups the flow latches. Why it matters: when a flow finishes,
/// FirstOfficerForm calls <c>ChecklistManager.MarkGroupComplete</c> for each of the flow's
/// completion groups (<see cref="FlowDefinition{TState}.CompletionGroupIds"/> — the related
/// groups PLUS the flow's own read-back "_CL" checklist), ticking and latching every line
/// EXCEPT those a SKIPPED step links. A write step with no link therefore has its line latched
/// complete even when the write failed — a switch the checklist then claims is set.
///
/// The step→line match is derived, not hand-listed (the PMDG 737 audit's rule, shared through
/// <see cref="FlowChecklistLinkAudit"/>): a line is delivered when the step writes one of its
/// auto-detect fields and every such write satisfies the line's own condition. On this airframe
/// a flow step writes an iFly DEFINITION varKey, and a switch varKey IS the SDK status field of
/// the same name the checklist lines read (IFly737StateEvaluator.GetValue reads the snapshot
/// field by that name) — so a written key that some line reads maps to itself, value for value.
/// Only the keys whose state is NOT a same-named field are listed below (<see cref="MappedKeys"/>),
/// and keys with no readable state behind them are declared (<see cref="NoStateKeys"/>); a key
/// in none of the three fails the audit, so a new step cannot slip past it.
/// </summary>
public class IFly737FlowChecklistLinkTests
{
    // Every state field an iFly checklist line reads — a written key in this set drives the
    // same-named field with the written value.
    private static readonly HashSet<string> LineFields = IFly737ChecklistDefinitions.Build()
        .SelectMany(g => g.Items)
        .Where(i => i.StateFieldName != null)
        .SelectMany(i => i.AdditionalStateFields.Prepend(i.StateFieldName!))
        .ToHashSet(StringComparer.Ordinal);

    private static (string, Func<int, double>) Same(string field) => (field, t => t);

    // Written keys whose readable state is not (only) the same-named field.
    private static readonly Dictionary<string, (string Field, Func<int, double> Value)[]> MappedKeys = new(StringComparer.Ordinal)
    {
        // Momentary GEN ON buttons: a generator on the bus puts its GEN OFF BUS light out (the
        // line "Generators: ON" reads the light, 0 = out).
        ["BTN_GEN_1_ON"] = new (string, Func<int, double>)[] { ("ENG_GEN_OFF_BUS_Light_Status_0", _ => 0) },
        ["BTN_GEN_2_ON"] = new (string, Func<int, double>)[] { ("ENG_GEN_OFF_BUS_Light_Status_1", _ => 0) },
        // APU_START pseudo-key (StartApuCoreAsync): ON, dwell, START — the switch springs back
        // to ON, which is what "APU: ON line" reads.
        [IFly737ActionExecutor.KeyApuStart] = new (string, Func<int, double>)[]
        { ("APU_Switch_Status", _ => IFly737ActionExecutor.ApuOn) },
        // Every fuel pump also feeds the Before Start synthetic. ON is the intended Before
        // Start configuration either way: with centre fuel the centre pumps run, without it
        // the executor's CenterPumpGate suppresses the centre ON write — both read OK (1).
        ["Fuel_L_FWD_Switch_Status"] = Pump("Fuel_L_FWD_Switch_Status"),
        ["Fuel_L_AFT_Switch_Status"] = Pump("Fuel_L_AFT_Switch_Status"),
        ["Fuel_R_FWD_Switch_Status"] = Pump("Fuel_R_FWD_Switch_Status"),
        ["Fuel_R_AFT_Switch_Status"] = Pump("Fuel_R_AFT_Switch_Status"),
        ["Fuel_CENTER_L_Switch_Status"] = Pump("Fuel_CENTER_L_Switch_Status"),
        ["Fuel_CENTER_R_Switch_Status"] = Pump("Fuel_CENTER_R_Switch_Status"),
    };

    private static (string, Func<int, double>)[] Pump(string field) =>
        new[] { Same(field), ("FO_FUEL_PUMPS_BS_OK", (Func<int, double>)(t => t)) };

    // Written keys with no readable state behind them. Lines they deliver are action-only,
    // matched through ActionItems below.
    private static readonly HashSet<string> NoStateKeys = new(StringComparer.Ordinal)
    {
        "BTN_GRD_PWR_ON", "BTN_GRD_PWR_OFF",   // no GPU/ground-power readback on this SDK
        "BTN_APU_GEN_1_ON", "BTN_APU_GEN_2_ON", // stateless bus-transfer click pair
        "BTN_ATTENDANT_CALL",                   // cabin chime
        "Fuel_Flow_Switch_Status",              // spring-loaded RESET, no line reads it
        // SimBrief-driven: PF_PRESS reads the synthetic FO_PRESS_ALTS_MATCH, which needs a
        // loaded plan — with none the pseudo-key is a quiet no-op that still "succeeds".
        IFly737ActionExecutor.KeyPressAlts,
        // Held self-completing tests (no persistent "test performed" state).
        IFly737ActionExecutor.KeyFireTest, IFly737ActionExecutor.KeyStallTest1, IFly737ActionExecutor.KeyStallTest2,
        IFly737ActionExecutor.KeyOverspeedTest1, IFly737ActionExecutor.KeyOverspeedTest2,
        IFly737ActionExecutor.KeyTcasTest, IFly737ActionExecutor.KeyGpwsTest,
    };

    // Action-only lines (no auto-detect field) → the writes their CheckAction fires. A step
    // that makes ALL of those writes delivers the line.
    private static readonly Dictionary<string, (string Event, int Target)[]> ActionItems = new(StringComparer.Ordinal)
    {
        ["EPU_GPU"] = new[] { ("BTN_GRD_PWR_ON", 1) },
        ["SE_GND_PWR_OFF"] = new[] { ("BTN_GRD_PWR_OFF", 1) },
        ["BS_APUGEN"] = new[] { ("BTN_APU_GEN_1_ON", 1), ("BTN_APU_GEN_2_ON", 1) },
        ["SD_APUGEN"] = new[] { ("BTN_APU_GEN_1_ON", 1), ("BTN_APU_GEN_2_ON", 1) },
        ["BTKO_CABIN"] = new[] { ("BTN_ATTENDANT_CALL", 1) },
        ["AP_CABIN"] = new[] { ("BTN_ATTENDANT_CALL", 1) },
        ["PF_FIRE_TEST"] = new[] { (IFly737ActionExecutor.KeyFireTest, 1) },
        ["PF_STALL_TEST1"] = new[] { (IFly737ActionExecutor.KeyStallTest1, 1) },
        ["PF_STALL_TEST2"] = new[] { (IFly737ActionExecutor.KeyStallTest2, 1) },
        ["PF_OVSPD_TEST1"] = new[] { (IFly737ActionExecutor.KeyOverspeedTest1, 1) },
        ["PF_OVSPD_TEST2"] = new[] { (IFly737ActionExecutor.KeyOverspeedTest2, 1) },
        ["PF_TCAS_TEST"] = new[] { (IFly737ActionExecutor.KeyTcasTest, 1) },
        ["PF_GPWS_TEST"] = new[] { (IFly737ActionExecutor.KeyGpwsTest, 1) },
    };

    private static IReadOnlyList<(string Field, Func<int, double> Value)>? FieldsOf(string key) =>
        MappedKeys.TryGetValue(key, out var f) ? f
        : LineFields.Contains(key) ? new[] { Same(key) }
        : null;

    private static bool Delivers(FlowStep<IFly737StateEvaluator> step,
        ChecklistItem<IFly737ActionExecutor, IFly737StateEvaluator> item) =>
        FlowChecklistLinkAudit.Delivers(FlowChecklistLinkAudit.Writes(step), item, FieldsOf, ActionItems);

    // The groups FirstOfficerForm actually latches when each flow finishes.
    private static List<(FlowDefinition<IFly737StateEvaluator> Flow,
        List<ChecklistItem<IFly737ActionExecutor, IFly737StateEvaluator>> Items)> FlowsWithItems()
    {
        var groups = IFly737ChecklistDefinitions.Build();
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return FlowChecklistLinkAudit.FlowsWithItems(IFly737FlowDefinitions.Build(), groups,
            f => f.CompletionGroupIds(ids.Contains));
    }

    [Fact]
    public void A_flow_latches_its_related_groups_plus_its_own_and_read_back_groups()
    {
        var ids = IFly737ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var flows = IFly737FlowDefinitions.Build();

        // Related groups only when no group is named after the flow.
        Assert.Equal(new[] { "ELEC_POWER_UP" },
            flows.Single(f => f.Id == "ELECTRICAL_POWER_UP").CompletionGroupIds(ids.Contains).ToArray());
        // PREFLIGHT relates only its own group, yet a finished run latches PREFLIGHT_CL too.
        Assert.Equal(new[] { "PREFLIGHT", "PREFLIGHT_CL" },
            flows.Single(f => f.Id == "PREFLIGHT").CompletionGroupIds(ids.Contains).ToArray());
        // Already-related ids are not repeated.
        Assert.Equal(new[] { "LANDING", "LANDING_CL" },
            flows.Single(f => f.Id == "LANDING").CompletionGroupIds(ids.Contains).ToArray());
    }

    [Fact]
    public void Every_written_key_is_a_line_field_mapped_or_declared_stateless()
    {
        var unmapped = FlowsWithItems()
            .SelectMany(f => f.Flow.Steps.Where(FlowChecklistLinkAudit.IsWrite).SelectMany(FlowChecklistLinkAudit.Writes)
                .Where(w => FieldsOf(w.Event) == null && !NoStateKeys.Contains(w.Event))
                .Select(w => $"{f.Flow.Id}: {w.Event}"))
            .Distinct().ToList();
        Assert.True(unmapped.Count == 0, "Unmapped keys: " + string.Join(", ", unmapped));
    }

    [Fact]
    public void Every_write_step_completes_the_line_it_sets()
    {
        var problems = FlowChecklistLinkAudit.LinkProblems(FlowsWithItems(), Delivers);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // The engine-start selector lines are StayComplete on N2 (CLAUDE.md's one sanctioned
    // exception): a GRD write is not a completed start, so no step may name them.
    [Fact]
    public void No_step_links_a_StayComplete_engine_start_selector_line()
    {
        var stay = IFly737ChecklistDefinitions.Build().SelectMany(g => g.Items)
            .Where(i => i.RevertBehavior == RevertBehavior.StayComplete && i.IsAutoDetectable)
            .Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ES_E1_GRD", "ES_E2_GRD" }, stay);
        foreach (var flow in IFly737FlowDefinitions.Build())
            foreach (var step in flow.Steps)
                Assert.False(step.LinkedChecklistItemIds.Any(stay.Contains),
                    $"{flow.Id}/{step.Id} links a StayComplete line - a GRD write is not a completed start");
    }

    // The SimBrief pressurization write can "succeed" without setting anything (no plan
    // loaded is a quiet no-op), so it must never claim the line it would set.
    [Fact]
    public void The_pressurization_write_links_no_line()
    {
        var step = IFly737FlowDefinitions.Build().Single(f => f.Id == "PREFLIGHT")
            .Steps.Single(s => s.Id == "PF_PRESS_ALTS");
        Assert.Equal(IFly737ActionExecutor.KeyPressAlts, step.EventName);
        Assert.Empty(step.LinkedChecklistItemIds);
    }

    // Characterization of what is still latched with no step behind it: auto-detect and
    // action lines in a latched group that no step of the flow names. Each is a known,
    // reviewed gap — a failed step cannot leave these live. Change this list deliberately.
    [Fact]
    public void Lines_latched_with_no_delivering_step_are_the_known_set()
    {
        Assert.Equal(new[]
        {
            // After Takeoff Checklist: engine bleeds are set ON in Preflight, not here.
            "AFTER_TAKEOFF/ATC_BLEEDS",
            // Before Start Checklist: the seatbelt signs are set ON in Preflight.
            "BEFORE_START/BSC_BELTS",
            // Before Taxi Checklist: autobrake RTO is set in Preflight.
            "BEFORE_TAXI/BTC_AB",
            // Before Taxi: the six-pack recall has no flow step.
            "BEFORE_TAXI/BT_RECALL",
            // Descent Checklist: LAND ALT is set in Preflight (SimBrief), not Descent.
            "DESCENT/DC_PRESS",
            // Descent: recall has no flow step.
            "DESCENT/DSA_RECALL",
            // Engine start selectors: StayComplete on N2 (never linked — see above); the
            // Stop-policy N2 waits abort the flow before any latch on a failed start.
            "ENGINE_START/ES_E1_GRD",
            "ENGINE_START/ES_E2_GRD",
            // Preflight Checklist: start levers CUTOFF, parking brake and pressurization mode
            // are Captain-set / cold-and-dark states no Preflight step writes.
            "PREFLIGHT/PFC_LEVERS",
            "PREFLIGHT/PFC_PARK",
            "PREFLIGHT/PFC_PRESS",
            // The SimBrief pressurization write + a Captain fallback; no plan is a quiet success.
            "PREFLIGHT/PF_PRESS",
            // Shutdown Checklist: parking brake is the Captain's; probe heat AUTO was set in
            // After Landing.
            "SHUTDOWN/SDC_PARK",
            "SHUTDOWN/SDC_PROBE",
        }, FlowChecklistLinkAudit.UndeliveredLines(FlowsWithItems()));
    }
}
