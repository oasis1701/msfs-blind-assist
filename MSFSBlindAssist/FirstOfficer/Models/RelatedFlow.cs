using MSFSBlindAssist.FirstOfficer;

namespace MSFSBlindAssist.FirstOfficer.Models;

/// <summary>
/// "Run Related Flow" on the First Officer's Checklists tab (<c>FirstOfficerForm.RunRelatedFlow</c>):
/// the flow a selected checklist group starts. Moved out of the form unchanged (2026-10-09) so it is
/// pinned by tests (<c>RelatedFlowTests</c>), not only through the window.
/// </summary>
public static class RelatedFlow
{
    /// <summary>Strip a trailing "_CL" so a readback group and its action group share one base
    /// phase key (e.g. BEFORE_START_CL → BEFORE_START).</summary>
    public static string BasePhaseId(string groupId)
        => groupId.EndsWith("_CL", StringComparison.Ordinal) ? groupId[..^3] : groupId;

    /// <summary>
    /// Resolve the flow for a selected checklist group. Robust across aircraft whose flows
    /// reference the ACTION group, the readback "_CL" group, or neither: a flow's Id is the
    /// phase base (COCKPIT_PREP, BEFORE_START, …), so we match the exact related id, then the
    /// flow Id against the group's base phase, then any related id's base phase. This fixed
    /// the A380 (its flows list only the "_CL" groups, so selecting an action group found
    /// nothing) and hardens every aircraft against the same action-vs-readback mismatch.
    /// </summary>
    public static FlowDefinition<TState>? For<TState>(IReadOnlyList<FlowDefinition<TState>> flows, string groupId)
        where TState : IFoStateEvaluator
    {
        string baseId = BasePhaseId(groupId);
        return flows.FirstOrDefault(f => f.RelatedChecklistGroupIds.Contains(groupId))
            ?? flows.FirstOrDefault(f => f.Id == baseId)
            ?? flows.FirstOrDefault(f => f.RelatedChecklistGroupIds.Any(id => BasePhaseId(id) == baseId));
    }
}
