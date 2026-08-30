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
/// list cannot rot into a blanket suppression.
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

    private static Dictionary<string, string?> ChecklistStateFields<TExec, TState>(
        IEnumerable<MSFSBlindAssist.FirstOfficer.Models.ChecklistGroup<TExec, TState>> groups)
        where TExec : MSFSBlindAssist.FirstOfficer.IFoActionExecutor
        where TState : MSFSBlindAssist.FirstOfficer.IFoStateEvaluator =>
        groups.SelectMany(g => g.Items).ToDictionary(i => i.Id, i => i.StateFieldName);

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

        var stale = KnownStateFieldDivergences.Keys
            .Where(id => a320.TryGetValue(id, out var x)
                      && a330.TryGetValue(id, out var y) && x == y)
            .Order().ToList();

        Assert.True(stale.Count == 0,
            "These allow-list entries no longer describe a divergence — the two profiles "
            + "now agree. Remove them so the list cannot rot into a blanket suppression: "
            + string.Join(", ", stale));
    }
}
