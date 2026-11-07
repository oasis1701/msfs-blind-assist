using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Fenix A321 has no PACK FLOW selector (its pneumatic panel has an ECON FLOW button), so the
/// Preflight flow's PF_PACKFLOW step passes silently there (FO-20: no target, nothing sent, the line
/// stays out of the latch) and keeps the knob at NORMAL on every other variant, an unknown one included.
/// The variant is attached as the definition's would be; no data service is involved.
/// </summary>
public class FenixFoA321Tests
{
    private static readonly FenixVariant A321 =
        FenixVariant.Unknown with { Type = FenixAircraftType.A321, Engine = FenixEngineFamily.IaeV2500 };

    private static FlowStep<FenixStateEvaluator> PackFlowStep() =>
        FenixFlowDefinitions.Build().Single(f => f.Id == "PREFLIGHT").Steps.Single(s => s.Id == "PF_PACKFLOW");

    // The pack flow reads 1 (NORMAL), so the only thing that can stop the write is the variant.
    private static FenixStateEvaluator Attached(FenixVariant variant)
    {
        var e = new FenixStateEvaluator();
        e.AttachFenix(new object(), () => variant, null);
        e.SetSimConnect(SeededSimConnectCache.ConnectedWith(("S_OH_PNEUMATIC_PACK_FLOW", 1)));
        return e;
    }

    [Fact]
    public void A321_preflight_neither_writes_nor_claims_pack_flow()
    {
        var e = Attached(A321);
        var step = PackFlowStep();

        Assert.False(step.SkipCondition!(e));
        Assert.Null(step.TargetValueProvider!(e));
        Assert.Equal(new[] { "PF_PACKFLOW" }, step.LinkedChecklistItemIds);
    }

    [Theory]
    [InlineData(FenixAircraftType.Unknown)]
    [InlineData(FenixAircraftType.A318)]
    [InlineData(FenixAircraftType.A319)]
    [InlineData(FenixAircraftType.A320)]
    public void A320_and_unknown_keep_pack_flow_normal(FenixAircraftType type)
    {
        var e = Attached(FenixVariant.Unknown with { Type = type });
        var step = PackFlowStep();

        Assert.True(step.SkipCondition!(e));
        Assert.Equal(1, step.TargetValueProvider!(e));
    }
}
