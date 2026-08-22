namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.SimConnect.IFly;

/// <summary>Pins the two generated files against each other: every entry in the
/// IFlySdkFields diff table must agree with the same-named IFlySdkOffsets const.
/// Catches a partial regeneration or hand-edit of either file that the two
/// hardcoded spot checks in IFly737FieldOffsetsByKeyTests would miss.</summary>
public class IFlySdkOffsetsCrossPinTests
{
    [Fact]
    public void EveryFieldTableEntry_MatchesItsOffsetsConst()
    {
        foreach (var f in IFlySdkFields.All)
        {
            var constField = typeof(IFlySdkOffsets).GetField(f.Name);
            Assert.True(constField != null, $"IFlySdkOffsets has no const named {f.Name}");
            Assert.Equal(f.Offset, (int)constField!.GetRawConstantValue()!);
        }
    }

    [Fact]
    public void Tick18_IsExcludedFromTheDiffTable_ButKeepsItsOffset()
    {
        // The staleness watchdog reads Tick18 directly via IFlySdkOffsets.Tick18;
        // keeping it in the diff table only produced a dropped 4 Hz change event
        // per poll (PR #163 review nit).
        Assert.DoesNotContain(IFlySdkFields.All, f => f.Name == "Tick18");
        Assert.NotNull(typeof(IFlySdkOffsets).GetField("Tick18"));
    }
}
