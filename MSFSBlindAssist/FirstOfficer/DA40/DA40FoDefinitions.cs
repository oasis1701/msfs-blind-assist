using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.FirstOfficer.DA40;

using Flow = FlowDefinition<DA40FoStateEvaluator>;
using Group = ChecklistGroup<DA40FoActionExecutor, DA40FoStateEvaluator>;

/// <summary>The DA40's flows, one per phase of <see cref="DA40FoProcedures"/>.</summary>
public static class DA40FoFlowDefinitions
{
    public static List<Flow> Build(DA40Variant variant) =>
        DA40FoProcedures.Build(variant).Select(p => p.ToFlow()).ToList();
}

/// <summary>
/// The DA40's checklist groups in flying order: each phase's action group, then the aircraft's own
/// page for it as the read-back (<see cref="DA40FoReadback"/>), then the reference pages. With no
/// pages (the package not found) the action groups stand alone.
/// </summary>
public static class DA40FoChecklistDefinitions
{
    public static List<Group> Build(DA40Variant variant, IReadOnlyList<NativeChecklistPage>? pages)
    {
        var readback = pages == null ? new List<Group>() : DA40FoReadback.Build(pages);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<Group>();

        foreach (var phase in DA40FoProcedures.Build(variant))
        {
            groups.Add(phase.ToGroup());
            // A phase's own page follows it; the XLS's flooded and reprime starts share the Engine
            // start page, which follows the cold start.
            var page = readback.FirstOrDefault(g => g.Id == phase.Id + "_CL");
            if (page != null && placed.Add(page.Id)) groups.Add(page);
        }

        foreach (var g in readback)
            if (placed.Add(g.Id)) groups.Add(g);

        return groups;
    }
}
