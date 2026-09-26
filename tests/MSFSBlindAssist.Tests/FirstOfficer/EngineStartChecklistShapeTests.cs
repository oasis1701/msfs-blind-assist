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
}
