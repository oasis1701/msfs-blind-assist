using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural invariant tests over the FlyByWire A32NX First Officer profile's
/// data-driven definitions — the automated safety net for
/// <see cref="FbwA320FlowDefinitions"/> and <see cref="FbwA320ChecklistDefinitions"/>.
/// </summary>
public class FbwA320ProfileStructureTests
{
    [Fact]
    public void NoReadbackGroupHasACheckAction()
    {
        var groups = FbwA320ChecklistDefinitions.Build();
        foreach (var g in groups.Where(g => g.Id.EndsWith("_CL")))
            foreach (var item in g.Items)
                Assert.True(item.CheckAction == null,
                    $"{g.Id}/{item.Id}: *_CL item must have no CheckAction");
    }

    [Fact]
    public void EveryCompletesChecklistItemIdResolvesToAChecklistItem()
    {
        var itemIds = FbwA320ChecklistDefinitions.Build()
            .SelectMany(g => g.Items).Select(i => i.Id).ToHashSet();
        foreach (var flow in FbwA320FlowDefinitions.Build())
            foreach (var step in flow.Steps.Where(s => s.CompletesChecklistItemId != null))
                Assert.Contains(step.CompletesChecklistItemId!, itemIds);
    }

    [Fact]
    public void EveryFlowRelatedGroupIdResolvesToAChecklistGroup()
    {
        var groupIds = FbwA320ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet();
        foreach (var flow in FbwA320FlowDefinitions.Build())
            foreach (var gid in flow.RelatedChecklistGroupIds ?? System.Array.Empty<string>())
                Assert.Contains(gid, groupIds);
    }

    [Fact]
    public void ChecklistItemIdsAreUnique()
    {
        var ids = FbwA320ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
