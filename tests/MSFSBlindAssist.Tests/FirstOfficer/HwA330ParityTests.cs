using System.Collections.Generic;
using System.Linq;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using A330 = MSFSBlindAssist.FirstOfficer.HWA330;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Headwind A330 First Officer is a DUPLICATE of the A32NX profile, chosen
/// deliberately for freedom to diverge. The cost is a drift hazard this repository
/// has already paid for once: FBW #10855 renamed A32NX.FCU_TO_AP_HDG_PUSH, the A380
/// definition was migrated, and FbwA380ActionExecutor was not — because it lived in
/// a directory the migration never touched. A K-event nobody registers is swallowed
/// silently by the sim.
///
/// So: the two profiles must stay structurally identical EXCEPT where an entry below
/// names the divergence and why. A migration applied to one copy and not the other
/// fails here and names the drift. A deliberate A330 divergence is added to the
/// allow-list. An allow-list entry that stops being a divergence ALSO fails, so the
/// list cannot rot into a blanket suppression — including one that names something
/// NEITHER profile has, which the convergence check alone silently filters out.
///
/// Compared: checklist group/item ids and order, flow ids and step ids, the executors'
/// fired-event sets, each checklist item's StateFieldName, what each flow step WRITES
/// (event + target value), and the two evaluators' OnRequestPollFields lists.
/// </summary>
public class HwA330ParityTests
{
    /// <summary>Checklist item ids whose StateFieldName legitimately differs, and why.</summary>
    private static readonly Dictionary<string, string> KnownStateFieldDivergences = new()
    {
        ["BT_LANDING_LT"] = "A339X has one 2-position ganged landing-light switch on stock "
                          + "LIGHT LANDING:2; L:LIGHTING_LANDING_2 is an A32NX Retractable position.",
        ["AL_LANDING_OFF"] = "Same switch. The A32NX item tests for RETRACT (2), which does "
                           + "not exist on the A330.",
    };

    /// <summary>
    /// Poll fields present in only ONE evaluator on purpose, and why. These are the two
    /// ~60-entry literal lists in FbwA320StateEvaluator and HwA330StateEvaluator. Nothing
    /// else in this suite reads them, so a field added to one copy and not the other used
    /// to be completely invisible — while a field the evaluator never polls simply reads
    /// NaN forever, which the ChecklistManager treats as indeterminate: the affected item
    /// silently never auto-ticks and never reverts, with no error anywhere.
    /// </summary>
    private static readonly Dictionary<string, string> KnownPollFieldDivergences = new()
    {
        ["LIGHTING_LANDING_2"] = "A32NX only: the Retractable landing-light switch position. "
                               + "The A339X has one 2-position ganged switch and never writes it.",
        ["LIGHT LANDING:2"] = "A339X only: the stock simvar its single ganged landing-light "
                            + "switch reads back, standing in for L:LIGHTING_LANDING_2.",
    };

    /// <summary>
    /// Flow step ids whose write — EventName plus TargetValue, or the MultiActions pairs
    /// for a SetSwitchMultiple step — legitimately differs, and why. Step *ids* already have a
    /// parity test; what each step actually writes did not, which is exactly the shape of
    /// the seat-belt drift (converted in one file, missed in the other).
    /// </summary>
    private static readonly Dictionary<string, string> KnownFlowStepActionDivergences = new()
    {
        ["BS_SEATBELTS"] = "Seatbelt signs are 3-position on the A339X (0=On, 1=Auto, 2=Off), "
                         + "so the write goes through HwA330ActionExecutor's SEATBELT_SIGN "
                         + "pseudo-key. The A320's CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE reaches "
                         + "no A330 write branch and AUTO would undo it within 500 ms anyway.",
        ["DC_SEATBELTS"] = "Same switch, same pseudo-key — see BS_SEATBELTS.",
        ["SD_SEATBELTS_OFF"] = "Same switch, same pseudo-key — see BS_SEATBELTS.",
    };

    private static Dictionary<string, string?> ChecklistStateFields<TExec, TState>(
        IEnumerable<MSFSBlindAssist.FirstOfficer.Models.ChecklistGroup<TExec, TState>> groups)
        where TExec : MSFSBlindAssist.FirstOfficer.IFoActionExecutor
        where TState : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator =>
        groups.SelectMany(g => g.Items).ToDictionary(i => i.Id, i => i.StateFieldName);

    /// <summary>
    /// What a step WRITES, rendered as one comparable string: the single-switch pair, plus
    /// the MultiActions pairs when it is a SetSwitchMultiple step (same tuple shape, and a
    /// Multi step leaves EventName/TargetValue null, so comparing only the single pair
    /// would leave every multi-switch step silently unguarded).
    /// </summary>
    private static string DescribeStepAction<TState>(MSFSBlindAssist.FirstOfficer.Models.FlowStep<TState> s)
        where TState : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator
    {
        string single = $"{s.EventName ?? "-"}={(s.TargetValue?.ToString() ?? "-")}";
        return s.MultiActions.Count == 0
            ? single
            : single + " [" + string.Join(", ",
                s.MultiActions.Select(m => $"{m.EventName}={(m.TargetValue?.ToString() ?? "-")}")) + "]";
    }

    private static Dictionary<string, string> FlowStepActions<TState>(
        IEnumerable<MSFSBlindAssist.FirstOfficer.Models.FlowDefinition<TState>> flows)
        where TState : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator =>
        flows.SelectMany(f => f.Steps).ToDictionary(s => s.Id, DescribeStepAction);

    private static HashSet<string> PollFields(
        MSFSBlindAssist.FirstOfficer.Generic.LVarStateEvaluator evaluator) =>
        evaluator.OnRequestPollFields.ToHashSet();

    [Fact]
    public void Checklist_group_ids_and_order_match()
    {
        var a320 = A320.FbwA320ChecklistDefinitions.Build().Select(g => g.Id).ToList();
        var a330 = A330.HwA330ChecklistDefinitions.Build().Select(g => g.Id).ToList();
        Assert.Equal(a320, a330);
    }

    [Fact]
    public void Checklist_item_ids_and_order_match()
    {
        var a320 = A320.FbwA320ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Id).ToList();
        var a330 = A330.HwA330ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(a320, a330);
    }

    [Fact]
    public void Flow_ids_and_step_ids_match()
    {
        var a320 = A320.FbwA320FlowDefinitions.Build()
            .Select(f => (f.Id, Steps: f.Steps.Select(s => s.Id).ToList())).ToList();
        var a330 = A330.HwA330FlowDefinitions.Build()
            .Select(f => (f.Id, Steps: f.Steps.Select(s => s.Id).ToList())).ToList();

        Assert.Equal(a320.Select(x => x.Id), a330.Select(x => x.Id));
        for (int i = 0; i < a320.Count; i++)
            Assert.Equal(a320[i].Steps, a330[i].Steps);
    }

    [Fact]
    public void Fired_event_sets_match()
    {
        Assert.Equal(
            A320.FbwA320ActionExecutor.FiredEventNames.Order().ToList(),
            A330.HwA330ActionExecutor.FiredEventNames.Order().ToList());
    }

    [Fact]
    public void Flow_step_actions_match_except_where_allow_listed()
    {
        var a320 = FlowStepActions(A320.FbwA320FlowDefinitions.Build());
        var a330 = FlowStepActions(A330.HwA330FlowDefinitions.Build());

        var drifted = a320.Keys
            .Where(id => a330.ContainsKey(id) && a320[id] != a330[id])
            .Where(id => !KnownFlowStepActionDivergences.ContainsKey(id))
            .Order()
            .Select(id => $"{id} (A320 {a320[id]} vs A330 {a330[id]})")
            .ToList();

        Assert.True(drifted.Count == 0,
            "These flow steps write a different event or target value in the two profiles "
            + "with no recorded reason — the shape of the seat-belt drift, where a "
            + "conversion landed in one file and was missed in the other. Either the change "
            + "belongs on both, or add it to KnownFlowStepActionDivergences with a reason: "
            + string.Join("; ", drifted));
    }

    [Fact]
    public void Every_allow_listed_flow_step_divergence_is_still_a_real_divergence()
    {
        var a320 = FlowStepActions(A320.FbwA320FlowDefinitions.Build());
        var a330 = FlowStepActions(A330.HwA330FlowDefinitions.Build());

        var converged = KnownFlowStepActionDivergences.Keys
            .Where(id => a320.TryGetValue(id, out var x)
                      && a330.TryGetValue(id, out var y) && x == y)
            .Order().ToList();

        var unknown = KnownFlowStepActionDivergences.Keys
            .Where(id => !a320.ContainsKey(id) || !a330.ContainsKey(id))
            .Order().ToList();

        Assert.True(converged.Count == 0,
            "These allow-list entries no longer describe a divergence — the two profiles "
            + "now write the same thing. Remove them so the list cannot rot into a blanket "
            + "suppression: " + string.Join(", ", converged));

        Assert.True(unknown.Count == 0,
            "These allow-list entries name no flow step present in both profiles, so they "
            + "suppress nothing and document nothing. Remove or correct them: "
            + string.Join(", ", unknown));
    }

    [Fact]
    public void State_evaluator_poll_fields_match_except_where_allow_listed()
    {
        var a320 = PollFields(new A320.FbwA320StateEvaluator());
        var a330 = PollFields(new A330.HwA330StateEvaluator());

        var drifted = a320.Except(a330).Where(f => !KnownPollFieldDivergences.ContainsKey(f))
                .Select(f => $"{f} (A320 only)")
            .Concat(a330.Except(a320).Where(f => !KnownPollFieldDivergences.ContainsKey(f))
                .Select(f => $"{f} (A330 only)"))
            .Order().ToList();

        Assert.True(drifted.Count == 0,
            "These poll fields are listed by only one of the two evaluators with no "
            + "recorded reason. A field the evaluator never polls reads NaN forever, so the "
            + "item that depends on it silently never auto-ticks. Either the change belongs "
            + "on both, or add it to KnownPollFieldDivergences with a reason: "
            + string.Join(", ", drifted));
    }

    [Fact]
    public void Every_allow_listed_poll_field_divergence_is_still_a_real_divergence()
    {
        var a320 = PollFields(new A320.FbwA320StateEvaluator());
        var a330 = PollFields(new A330.HwA330StateEvaluator());

        // A real one-sided divergence is exactly "in one list and not the other". An entry
        // in BOTH has converged; an entry in NEITHER names a field that no longer exists.
        var stale = KnownPollFieldDivergences.Keys
            .Where(f => a320.Contains(f) == a330.Contains(f))
            .Order()
            .Select(f => a320.Contains(f)
                ? $"{f} (now polled by both)"
                : $"{f} (polled by neither)")
            .ToList();

        Assert.True(stale.Count == 0,
            "These allow-list entries no longer describe a divergence. Remove them so the "
            + "list cannot rot into a blanket suppression: " + string.Join(", ", stale));
    }

    [Fact]
    public void Checklist_state_fields_match_except_where_allow_listed()
    {
        var a320 = ChecklistStateFields(A320.FbwA320ChecklistDefinitions.Build());
        var a330 = ChecklistStateFields(A330.HwA330ChecklistDefinitions.Build());

        var drifted = a320.Keys
            .Where(id => a330.ContainsKey(id) && a320[id] != a330[id])
            .Where(id => !KnownStateFieldDivergences.ContainsKey(id))
            .Order().ToList();

        Assert.True(drifted.Count == 0,
            "These items' state fields differ between the A320 and A330 profiles with no "
            + "recorded reason. Either the change belongs on both, or add it to "
            + "KnownStateFieldDivergences with a reason: " + string.Join(", ", drifted));
    }

    [Fact]
    public void Every_allow_listed_divergence_is_still_a_real_divergence()
    {
        var a320 = ChecklistStateFields(A320.FbwA320ChecklistDefinitions.Build());
        var a330 = ChecklistStateFields(A330.HwA330ChecklistDefinitions.Build());

        var converged = KnownStateFieldDivergences.Keys
            .Where(id => a320.TryGetValue(id, out var x)
                      && a330.TryGetValue(id, out var y) && x == y)
            .Order().ToList();

        // Second arm: an entry naming an item NEITHER profile has is filtered out by both
        // TryGetValue calls above, so the convergence arm can never see it. Without this
        // an entry survives the item being renamed or deleted, and reads as authoritative.
        var unknown = KnownStateFieldDivergences.Keys
            .Where(id => !a320.ContainsKey(id) || !a330.ContainsKey(id))
            .Order().ToList();

        Assert.True(converged.Count == 0,
            "These allow-list entries no longer describe a divergence — the two profiles "
            + "now agree. Remove them so the list cannot rot into a blanket suppression: "
            + string.Join(", ", converged));

        Assert.True(unknown.Count == 0,
            "These allow-list entries name no checklist item present in both profiles, so "
            + "they suppress nothing and document nothing. Remove or correct them: "
            + string.Join(", ", unknown));
    }
}
