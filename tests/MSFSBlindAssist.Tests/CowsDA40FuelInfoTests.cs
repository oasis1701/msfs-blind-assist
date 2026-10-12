using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Shift+F on each variant reads that variant's own fuel flow and tank limit.</summary>
public class CowsDA40FuelInfoTests
{
    [Theory]
    [InlineData(DA40Variant.NG, 9.0)]
    [InlineData(DA40Variant.XLS, 10.0)]
    public void EachVariantHasItsOwnTankDifferenceLimit(DA40Variant variant, double limit)
        => Assert.Equal(limit, new CowsDA40Definition(variant).FuelMaxTankDifferenceGal);

    /// <summary>
    /// ⚠️ The XLS read the NG's fuel-flow key, which it does not register, so its endurance
    /// was always "engine not burning".
    /// </summary>
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheFuelFlowKeyExistsOnThatVariantAndReachesTheCache(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();

        Assert.True(vars.TryGetValue(def.FuelFlowKey, out var v), $"{def.FuelFlowKey} is not registered on the {variant}");
        Assert.True(v!.UpdateFrequency == MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous && v.IsAnnounced);
    }
}
