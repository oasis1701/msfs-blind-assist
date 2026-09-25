using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Shared machinery for the per-aircraft "every write step completes the line it sets"
/// audits (<see cref="Pmdg737FlowChecklistLinkTests"/>, <see cref="FirstOfficer.IFly737FlowChecklistLinkTests"/>).
/// Each audit supplies only what is aircraft-specific: which state field(s) a written key
/// drives (and the value a written target produces there), which keys drive no readable
/// state, and which action-only lines a set of writes delivers. The matching rule itself —
/// a line is delivered when the step writes one of its auto-detect fields and EVERY such
/// write satisfies the line's own condition — lives here, once.
/// </summary>
internal static class FlowChecklistLinkAudit
{
    public static bool IsWrite<TState>(FlowStep<TState> s) where TState : IFoStateEvaluator =>
        s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple;

    // The (key, target) pairs a write step sends. A target-less step is a momentary press
    // or a mouse-flag toggle the flow fires only toward ON — both read as 1. A dynamic
    // (SimBrief) target has no fixed value; those keys are declared stateless, so 0 is inert.
    public static List<(string Event, int Target)> Writes<TState>(FlowStep<TState> s)
        where TState : IFoStateEvaluator =>
        s.ActionType == FlowStepActionType.SetSwitchMultiple
            ? s.MultiActions.Select(a => (a.EventName, a.TargetValue ?? 1)).ToList()
            : new List<(string, int)> { (s.EventName!, s.TargetValue ?? (s.TargetValueProvider != null ? 0 : 1)) };

    /// <summary>
    /// True when <paramref name="writes"/> deliver <paramref name="item"/>. An action-only line
    /// (named in <paramref name="actionItems"/>) is delivered by making ALL the writes its own
    /// CheckAction fires. An auto-detect line is delivered when the writes drive at least one of
    /// its state fields and every value they produce there satisfies its condition. A
    /// StayComplete line is never delivered by a write (the engine-start selectors: a GRD write
    /// is not a completed start).
    /// </summary>
    public static bool Delivers<TExec, TState>(
        IReadOnlyCollection<(string Event, int Target)> writes,
        ChecklistItem<TExec, TState> item,
        Func<string, IReadOnlyList<(string Field, Func<int, double> Value)>?> fieldsOf,
        IReadOnlyDictionary<string, (string Event, int Target)[]> actionItems)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator
    {
        if (actionItems.TryGetValue(item.Id, out var needed))
            return needed.All(writes.Contains);
        if (!item.IsAutoDetectable || item.RevertBehavior == RevertBehavior.StayComplete)
            return false;

        bool any = false;
        foreach (var (ev, target) in writes)
        {
            var fields = fieldsOf(ev);
            if (fields == null) continue;
            foreach (var (field, value) in fields)
            {
                Func<double, bool>? cond;
                if (field == item.StateFieldName) cond = item.StateCondition;
                else if (item.AdditionalStateFields.Contains(field))
                    cond = item.AdditionalStateCondition ?? item.StateCondition;
                else continue;

                Assert.True(cond != null, $"{item.Id} has no state condition");
                if (!cond!(value(target))) return false;   // the write does NOT achieve the line
                any = true;
            }
        }
        return any;
    }

    /// <summary>Each flow with every line of the checklist groups <paramref name="groupIdsOf"/>
    /// names for it (the groups a finished run latches).</summary>
    public static List<(FlowDefinition<TState> Flow, List<ChecklistItem<TExec, TState>> Items)> FlowsWithItems<TExec, TState>(
        IEnumerable<FlowDefinition<TState>> flows,
        IReadOnlyList<ChecklistGroup<TExec, TState>> groups,
        Func<FlowDefinition<TState>, IEnumerable<string>> groupIdsOf)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        flows.Select(flow => (flow, groupIdsOf(flow)
                .SelectMany(id => groups.Single(g => g.Id == id).Items).ToList()))
            .ToList();

    /// <summary>
    /// Every write step must link exactly the lines it delivers, with its own (non-read-back)
    /// group's line as <c>CompletesChecklistItemId</c> whenever it delivers one. Returns one
    /// line per offending step.
    /// </summary>
    public static List<string> LinkProblems<TExec, TState>(
        IEnumerable<(FlowDefinition<TState> Flow, List<ChecklistItem<TExec, TState>> Items)> flowsWithItems,
        Func<FlowStep<TState>, ChecklistItem<TExec, TState>, bool> delivers)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator
    {
        var problems = new List<string>();
        foreach (var (flow, items) in flowsWithItems)
        {
            foreach (var step in flow.Steps.Where(IsWrite))
            {
                var cands = items.Where(i => delivers(step, i)).ToList();
                var own = cands.Where(i => !i.GroupId.EndsWith("_CL", StringComparison.Ordinal)).ToList();
                var primaryOptions = own.Count > 0 ? own : cands;
                string? primary = step.CompletesChecklistItemId;
                var linked = step.LinkedChecklistItemIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
                var expected = cands.Select(i => i.Id).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

                if (!linked.SequenceEqual(expected))
                    problems.Add($"{flow.Id}/{step.Id} -> should complete [{string.Join(", ", expected)}], " +
                        $"completes [{string.Join(", ", linked)}]");
                else if (primaryOptions.Count > 0 && primaryOptions.All(i => i.Id != primary))
                    problems.Add($"{flow.Id}/{step.Id} -> primary link should be its own group's line " +
                        $"[{string.Join(", ", primaryOptions.Select(i => i.Id))}], is {primary}");
            }
        }
        return problems;
    }

    /// <summary>Auto-detect and action lines (captain reminders are delivered by the reminder
    /// itself) in a latched group that no step of the flow names, as "FLOW/LINE", sorted.</summary>
    public static List<string> UndeliveredLines<TExec, TState>(
        IEnumerable<(FlowDefinition<TState> Flow, List<ChecklistItem<TExec, TState>> Items)> flowsWithItems)
        where TExec : IFoActionExecutor where TState : IFoStateEvaluator =>
        flowsWithItems
            .SelectMany(f => f.Items
                .Where(i => i.Type != ChecklistItemType.CaptainReminder
                         && i.Type != ChecklistItemType.Informational)
                .Where(i => f.Flow.Steps.All(s => !s.LinkedChecklistItemIds.Contains(i.Id)))
                .Select(i => $"{f.Flow.Id}/{i.Id}"))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
}
