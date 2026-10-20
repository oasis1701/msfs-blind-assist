using System.Linq;
using MSFSBlindAssist.FirstOfficer.PMDG737;
using MSFSBlindAssist.FirstOfficer.IFly737;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Engine Start groups are pilot-paced start-switch + start-lever items ONLY.
/// The "Engine 1/2: running" N2 auto-verification items were removed by user request
/// (2026-08-16) on BOTH 737 variants — this pins them out so a future "parity" pass
/// doesn't reintroduce them.
/// </summary>
public class EngineStartChecklistShapeTests
{
    [Fact]
    public void B737_EngineStart_HasNoRunningVerificationItems()
    {
        var ids = PMDG737ChecklistDefinitions.Build()
            .First(g => g.Id == "ENGINE_START").Items.Select(i => i.Id).ToList();
        Assert.DoesNotContain("ES_E1_STAB", ids);
        Assert.DoesNotContain("ES_E2_STAB", ids);
        // The pilot-paced items stay.
        Assert.Contains("ES_E1_RUN", ids);
        Assert.Contains("ES_E2_RUN", ids);
        Assert.Contains("ES_E1_GRD", ids);
        Assert.Contains("ES_E2_GRD", ids);
    }

    [Fact]
    public void IFly737_EngineStart_HasNoRunningVerificationItems()
    {
        var ids = IFly737ChecklistDefinitions.Build()
            .First(g => g.Id == "ENGINE_START").Items.Select(i => i.Id).ToList();
        Assert.DoesNotContain("ES_E1_STAB", ids);
        Assert.DoesNotContain("ES_E2_STAB", ids);
        Assert.Contains("ES_E1_RUN", ids);
        Assert.Contains("ES_E2_RUN", ids);
        Assert.Contains("ES_E1_GRD", ids);
        Assert.Contains("ES_E2_GRD", ids);
    }

    // The "Engine 1/2 Oil Pressure: Verify increases" lines were removed by owner request
    // (2026-10-20): the 777 Engine Start group is the start selectors and fuel controls only.
    [Fact]
    public void B777_EngineStart_IsStartSelectorsAndFuelControlsOnly_TwoThenOne()
    {
        var ids = MSFSBlindAssist.FirstOfficer.PMDG777ChecklistDefinitions.Build()
            .First(g => g.Id == "ENGINE_START").Items.Select(i => i.Id).ToList();
        Assert.Equal(new[] { "ES_ENG2_START_SEL", "ES_ENG2_FUEL_CTRL",
                             "ES_ENG1_START_SEL", "ES_ENG1_FUEL_CTRL" }, ids);
    }

    // No Boeing First Officer has an oil pressure line anywhere in its checklists (owner, 2026-10-20).
    [Fact]
    public void No_Boeing_checklist_has_an_oil_pressure_line()
    {
        var labels = MSFSBlindAssist.FirstOfficer.PMDG777ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)
            .Concat(PMDG737ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label))
            .Concat(IFly737ChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label));
        Assert.DoesNotContain(labels, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\boil\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    [Fact]
    public void Md11_EngineStart_IsStartAndFuelSwitchesOnly_ThreeOneTwo()
    {
        var ids = MSFSBlindAssist.FirstOfficer.MD11.Md11FoChecklistDefinitions.Build()
            .First(g => g.Id == "ENGINE_START").Items.Select(i => i.Id).ToList();
        Assert.Equal(new[] { "ES_E3_START", "ES_E3_FUEL", "ES_E1_START", "ES_E1_FUEL",
                             "ES_E2_START", "ES_E2_FUEL", "ES_ANTI_ICE" }, ids);
    }
}
