using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins which vars a "fresh" read can be fresh for. The MD-11 flap lever was once taken for
/// batch-covered when it streams on its own SIM_FRAME subscription — the kind of misclassification
/// that turns every walk read into a 1200 ms timeout — so the real definitions are checked too.
/// </summary>
public class FreshReadPolicyTests
{
    private static SimVarDefinition Def(UpdateFrequency frequency, bool announced, bool excludeFromBatch, bool highFrequency) => new()
    {
        Name = "X",
        UpdateFrequency = frequency,
        IsAnnounced = announced,
        ExcludeFromBatch = excludeFromBatch,
        HighFrequency = highFrequency,
    };

    [Fact]
    public void APlainOnRequestDefinition_SupportsFreshReads_ThroughItsOnceResponse()
    {
        var def = Def(UpdateFrequency.OnRequest, false, false, false);

        Assert.True(FreshReadPolicy.SupportsFreshReads(hasIndividualDefinition: true, def));
        Assert.False(FreshReadPolicy.CacheIsFresh(def));
    }

    [Fact]
    public void ABatchCoveredVar_DoesNot()
    {
        Assert.False(FreshReadPolicy.SupportsFreshReads(hasIndividualDefinition: false, Def(UpdateFrequency.Continuous, true, false, false)));
    }

    [Fact]
    public void ASimFrameOwnSubscription_SupportsFreshReads_FromItsCache()
    {
        var def = Def(UpdateFrequency.Continuous, true, true, true);

        Assert.True(FreshReadPolicy.IsOwnSubscription(def));
        Assert.True(FreshReadPolicy.CacheIsFresh(def));
        Assert.True(FreshReadPolicy.SupportsFreshReads(hasIndividualDefinition: true, def));
    }

    [Fact]
    public void APeriodSecondOwnSubscription_DoesNot_AndItsCacheIsNotFresh()
    {
        var def = Def(UpdateFrequency.Continuous, true, true, false);

        Assert.True(FreshReadPolicy.IsOwnSubscription(def));
        Assert.False(FreshReadPolicy.CacheIsFresh(def));
        Assert.False(FreshReadPolicy.SupportsFreshReads(hasIndividualDefinition: true, def));
    }

    [Fact]
    public void AnUnregisteredKey_DoesNot()
    {
        Assert.False(FreshReadPolicy.SupportsFreshReads(hasIndividualDefinition: false, null));
        Assert.False(FreshReadPolicy.CacheIsFresh(null));
    }

    /// <summary>The real MD-11 definitions: the levers that walk are SIM_FRAME own subscriptions, the seat-belt switch a plain individual def.</summary>
    [Fact]
    public void TheMd11FlapLeverAndSpeedbrake_ReadFreshFromTheCache_TheSeatBeltSwitchFromItsOnceResponse()
    {
        var vars = new TFDiMD11Definition().GetVariables();

        Assert.True(FreshReadPolicy.CacheIsFresh(vars[Md11FlapSystem.LeverKey]));
        Assert.True(FreshReadPolicy.CacheIsFresh(vars[Md11SpeedbrakeSystem.LeverKey]));
        Assert.False(FreshReadPolicy.IsOwnSubscription(vars["MD11_OVHD_LTS_SEAT_BELTS_SW"]));
        Assert.True(FreshReadPolicy.SupportsFreshReads(hasIndividualDefinition: true, vars["MD11_OVHD_LTS_SEAT_BELTS_SW"]));
    }
}
