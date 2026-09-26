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

    /// <summary>
    /// A CHANGED stream never sends a value it has not seen change, and SimConnect's baseline starts
    /// at zero: a lever resting at 0 when it subscribes (flaps up, speedbrake stowed) stays uncached
    /// until it moves (measured live, 2026-09-26). An EMPTY cache is therefore not an answer — the
    /// read must be asked once instead — while a cached 0 is.
    /// </summary>
    [Fact]
    public void AnEmptyCache_IsNotAnAnswer_ACachedZeroIs()
    {
        var simFrame = Def(UpdateFrequency.Continuous, true, true, true);

        Assert.False(FreshReadPolicy.AnswersFromCache(simFrame, null));
        Assert.True(FreshReadPolicy.AnswersFromCache(simFrame, 0.0));
        Assert.False(FreshReadPolicy.AnswersFromCache(Def(UpdateFrequency.OnRequest, false, false, false), 5.0));
    }

    /// <summary>
    /// A PERIOD.ONCE under a periodic subscription's own data-definition id REPLACES the
    /// subscription, so it is never issued there. Under a fresh read's own request id it cannot
    /// replace anything — and for a SIM_FRAME + CHANGED stream it is the only way to learn a value
    /// the stream will not send. A PERIOD.SECOND subscription keeps waiting for its next sample.
    /// </summary>
    [Fact]
    public void AOnce_IsNeverIssuedUnderASubscriptionsOwnId_OnlyUnderAFreshReadsIdForASimFrameStream()
    {
        var plain = Def(UpdateFrequency.OnRequest, false, false, false);
        var perSecond = Def(UpdateFrequency.Continuous, true, true, false);
        var simFrame = Def(UpdateFrequency.Continuous, true, true, true);

        Assert.True(FreshReadPolicy.MayIssueOnce(plain, underOwnRequestId: false));
        Assert.True(FreshReadPolicy.MayIssueOnce(plain, underOwnRequestId: true));
        Assert.False(FreshReadPolicy.MayIssueOnce(perSecond, underOwnRequestId: false));
        Assert.False(FreshReadPolicy.MayIssueOnce(perSecond, underOwnRequestId: true));
        Assert.False(FreshReadPolicy.MayIssueOnce(simFrame, underOwnRequestId: false));
        Assert.True(FreshReadPolicy.MayIssueOnce(simFrame, underOwnRequestId: true));
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
