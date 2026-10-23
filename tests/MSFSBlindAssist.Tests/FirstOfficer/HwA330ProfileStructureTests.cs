using System.Linq;
using MSFSBlindAssist.FirstOfficer.HWA330;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural invariants over the Headwind A330 First Officer's data-driven
/// definitions — the same four the A32NX profile is held to
/// (see FbwA320ProfileStructureTests).
/// </summary>
public class HwA330ProfileStructureTests
{
    [Fact]
    public void NoReadbackGroupHasACheckAction()
    {
        foreach (var g in HwA330ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL")))
            foreach (var item in g.Items)
                Assert.True(item.CheckAction == null,
                    $"{g.Id}/{item.Id}: *_CL item must have no CheckAction");
    }

    [Fact]
    public void EveryCompletesChecklistItemIdResolvesToAChecklistItem()
    {
        var itemIds = HwA330ChecklistDefinitions.Build()
            .SelectMany(g => g.Items).Select(i => i.Id).ToHashSet();
        foreach (var flow in HwA330FlowDefinitions.Build())
            foreach (var step in flow.Steps.Where(s => s.CompletesChecklistItemId != null))
                Assert.Contains(step.CompletesChecklistItemId!, itemIds);
    }

    [Fact]
    public void EveryFlowRelatedGroupIdResolvesToAChecklistGroup()
    {
        var groupIds = HwA330ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet();
        foreach (var flow in HwA330FlowDefinitions.Build())
            foreach (var gid in flow.RelatedChecklistGroupIds ?? System.Array.Empty<string>())
                Assert.Contains(gid, groupIds);
    }

    [Fact]
    public void ChecklistItemIdsAreUnique()
    {
        var ids = HwA330ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ProfileTitleNamesTheA330()
    {
        var title = new HwA330FoProfile(
            new MSFSBlindAssist.Aircraft.HeadwindA330Definition(), null!).Title;
        Assert.Contains("A330", title);
        Assert.DoesNotContain("A32NX", title);
    }
}
