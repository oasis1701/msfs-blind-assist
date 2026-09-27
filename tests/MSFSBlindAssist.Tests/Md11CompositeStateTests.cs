using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The spoken state of a composite control (<see cref="Md11CompositeState"/>, spec D10), on the
/// engine fire handle's own words. Pure: the definition hands it the two cached values, and these
/// pin what it makes of them.
/// </summary>
public class Md11CompositeStateTests
{
    private static Md11CompositeSpec EngineHandle() => new()
    {
        OuterVar = "MD11_AOVHD_ENG1FIRE_SW",
        OuterWords = new Dictionary<string, string> { ["0"] = "Normal", ["1"] = "Generator Field Disconnect" },
        Delegate = "2",
        InnerVar = "MD11_AOVHD_ENG1FIRE_KB",
        InnerWords = new Dictionary<string, string>
        {
            ["0"] = "Bottle 1", ["1"] = "Fuel and Hydraulic Disconnect", ["2"] = "Bottle 2",
        },
    };

    /// <summary>TFDi's tooltip, position by position: the pull's word, or fully pulled the rotation's.</summary>
    [Theory]
    [InlineData(0.0, 1.0, "Normal")]                          // stowed; the rotation rests at 1
    [InlineData(1.0, 1.0, "Generator Field Disconnect")]
    [InlineData(2.0, 1.0, "Fuel and Hydraulic Disconnect")]   // fully pulled, not turned
    [InlineData(2.0, 0.0, "Bottle 1")]
    [InlineData(2.0, 2.0, "Bottle 2")]
    public void SaysWhatTheTooltipSays(double pull, double rotation, string expected)
        => Assert.Equal(expected, Md11CompositeState.Describe(EngineHandle(), pull, rotation));

    [Fact]
    public void AHandleNotFullyPulled_NeedsNoRotationReading()
    {
        Assert.Equal("Normal", Md11CompositeState.Describe(EngineHandle(), 0.0, null));
        Assert.Equal("Generator Field Disconnect", Md11CompositeState.Describe(EngineHandle(), 1.0, null));
    }

    [Fact]
    public void NoPullReading_SaysNothing()
        => Assert.Null(Md11CompositeState.Describe(EngineHandle(), null, 1.0));

    [Fact]
    public void FullyPulled_WithNoRotationReading_SaysNothing()
        => Assert.Null(Md11CompositeState.Describe(EngineHandle(), 2.0, null));

    [Theory]
    [InlineData(7.0, 1.0)]    // a pull that is no position
    [InlineData(2.0, 9.0)]    // a rotation that is no position
    [InlineData(0.5, 1.0)]    // exactly between two positions: neither
    public void AReadingThatIsNoPosition_SaysNothing(double pull, double rotation)
        => Assert.Null(Md11CompositeState.Describe(EngineHandle(), pull, rotation));

    [Fact]
    public void AReadingNearAPosition_IsThatPosition()
    {
        Assert.Equal("Generator Field Disconnect", Md11CompositeState.Describe(EngineHandle(), 1.02, null));
        Assert.Equal("Bottle 2", Md11CompositeState.Describe(EngineHandle(), 1.98, 2.01));
    }

    [Fact]
    public void NoBlock_SaysNothing()
        => Assert.Null(Md11CompositeState.Describe(null, 0.0, 1.0));

    [Fact]
    public void TheRowsDescriptions_AreThePullsWords_WithoutTheDelegatePosition()
    {
        var d = Md11CompositeState.OuterDescriptions(EngineHandle());
        Assert.Equal(2, d.Count);
        Assert.Equal("Normal", d[0]);
        Assert.Equal("Generator Field Disconnect", d[1]);
    }

    [Fact]
    public void TheRefusal_NamesTheControl()
        => Assert.Equal("Engine 1 Fire Handle cannot be operated from this panel yet.",
                        Md11CompositeState.RefusalSentence("Engine 1 Fire Handle"));

    [Fact]
    public void TheInnerKey_IsTheNodeIdWithASuffix_ThatNoMapNodeCarries()
    {
        Assert.Equal("MD11_AOVHD_ENG1FIRE_KB__INNER", Md11CompositeState.InnerKeyFor("MD11_AOVHD_ENG1FIRE_KB"));
        Assert.DoesNotContain(Md11ControlMap.Load().Controls,
            c => c.NodeId.EndsWith(Md11CompositeState.InnerKeySuffix, StringComparison.Ordinal));
    }
}
