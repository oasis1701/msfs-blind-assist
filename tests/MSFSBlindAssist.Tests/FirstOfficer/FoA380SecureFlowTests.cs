using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural tests over the FlyByWire A380 Securing phase (2026-08-15). The A380 was the
/// only First-Officer aircraft with no Secure flow or checklist — there was nowhere for the
/// power-down to happen, which is why the Parking flow had grown one (see
/// FoShutdownSecureTighteningTests for that supersession).
/// </summary>
public class FoA380SecureFlowTests
{
    private static readonly string[] ExpectedSecureIds =
    {
        "SC_OXY", "SC_EFB", "SC_ADIRS", "SC_EMEREXIT", "SC_NOSMOKE",
        "SC_EXTLT_OFF", "SC_APUBLEED_OFF", "SC_EXTPWR_OFF", "SC_APU_OFF", "SC_BAT_OFF",
    };

    [Fact]
    public void SecureFlowCarriesTheFullPowerDownInOrder()
    {
        var flow = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE");
        Assert.Equal(ExpectedSecureIds, flow.Steps.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void SecureFlowPointsAtTheSecuringReadbackGroup()
    {
        var flow = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE");
        Assert.Contains("SECURING_CL", flow.RelatedChecklistGroupIds ?? System.Array.Empty<string>());
    }

    [Fact]
    public void SecureChecklistGroupMirrorsTheFlowOneToOne()
    {
        var flowIds = FbwA380FlowDefinitions.Build()
            .Single(f => f.Id == "SECURE").Steps.Select(s => s.Id).ToHashSet();
        var itemIds = FbwA380ChecklistDefinitions.Build()
            .Single(g => g.Id == "SECURE").Items.Select(i => i.Id).ToHashSet();
        Assert.Equal(flowIds, itemIds);
    }

    [Fact]
    public void SecuringReadbackGroupExistsAndIsActionFree()
    {
        var group = FbwA380ChecklistDefinitions.Build().Single(g => g.Id == "SECURING_CL");
        Assert.NotEmpty(group.Items);
        foreach (var item in group.Items)
        {
            Assert.Null(item.CheckAction);
            Assert.NotEqual(MSFSBlindAssist.FirstOfficer.Models.ChecklistItemType.Actionable, item.Type);
        }
    }

    [Fact]
    public void BatteriesAreTheLastThingSecureTurnsOff()
    {
        var steps = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE").Steps;
        Assert.Equal("SC_BAT_OFF", steps[^1].Id);
    }

    [Fact]
    public void A380ChecklistItemIdsStayUnique()
    {
        var ids = FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryA380FlowRelatedGroupIdResolvesToAChecklistGroup()
    {
        var groupIds = FbwA380ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet();
        foreach (var flow in FbwA380FlowDefinitions.Build())
            foreach (var gid in flow.RelatedChecklistGroupIds ?? System.Array.Empty<string>())
                Assert.Contains(gid, groupIds);
    }
}
