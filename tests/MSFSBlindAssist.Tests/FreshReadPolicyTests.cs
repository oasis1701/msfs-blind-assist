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

    /// <summary>
    /// The seed read rides a request id that can never be a data-definition id (the counter restarts
    /// at 1000 on every connection and aircraft switch, and a connection registers well under 2000)
    /// and never the subscription's own id — re-issuing that one delivers nothing new, measured live —
    /// while staying on the individual-variable side of the dispatch (at or above 1000). There is no
    /// seed map: the dispatch recognises the id and maps it back to its definition id.
    /// </summary>
    [Fact]
    public void TheSeedRequestId_IsDistinctFromEveryDefinitionId_AndDispatchesAsAnIndividualVariable()
    {
        foreach (var defId in new[] { 1000, 1369, 2400 })
        {
            var seed = FreshReadPolicy.SeedRequestId(defId);
            Assert.NotEqual(defId, seed);
            Assert.True(seed >= FreshReadPolicy.SeedRequestIdOffset);
            Assert.True(FreshReadPolicy.IsSeedRequestId(seed));
            Assert.False(FreshReadPolicy.IsSeedRequestId(defId));
            Assert.Equal(defId, FreshReadPolicy.DataDefinitionIdOf(seed));
            // Routed as an individual variable, like the fresh ids are.
            Assert.True(seed >= (int)SimConnectManager.DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE);
        }
        Assert.False(FreshReadPolicy.IsSeedRequestId(SimConnectManager.FreshRequestIdBase));
        Assert.True(FreshReadPolicy.SeedRequestIdOffset > 10_000);
    }

    /// <summary>
    /// A seed id must never equal a per-read fresh id: the delivery path resolves the fresh map
    /// first, so a seed id equal to an outstanding fresh id would be taken for that fresh read —
    /// another var's value cached under its key and handed to its waiter. Data-definition ids
    /// restart at 1000 on every connection and aircraft switch (Disconnect, ReregisterAllVariables)
    /// and a connection registers well under 2000 of them, so the seed range stays below
    /// SeedRequestIdOffset + 3000.
    /// </summary>
    [Fact]
    public void TheSeedRange_LiesWhollyBelowTheFreshReadRange()
    {
        Assert.True(FreshReadPolicy.SeedRequestId(1000 + 2000) < SimConnectManager.FreshRequestIdBase);
    }

    /// <summary>
    /// A SIM_FRAME + CHANGED var answers a fresh read from its cache only once it HAS a value. An
    /// empty cache must fall through to a real read — handing back null is how the MD-11 speedbrake
    /// walk read "state var unreadable" for whole sessions without ever clicking.
    /// </summary>
    [Fact]
    public void ASimFrameSubscription_AnswersFromCache_OnlyOnceItHasBeenDelivered()
    {
        var simFrame = Def(UpdateFrequency.Continuous, true, true, highFrequency: true);

        Assert.True(FreshReadPolicy.AnswerFromCache(simFrame, 0.0));
        Assert.False(FreshReadPolicy.AnswerFromCache(simFrame, null));
    }

    [Fact]
    public void OnlyASimFrameSubscription_EverAnswersFromCache()
    {
        Assert.False(FreshReadPolicy.AnswerFromCache(Def(UpdateFrequency.Continuous, true, true, highFrequency: false), 1.0));
        Assert.False(FreshReadPolicy.AnswerFromCache(Def(UpdateFrequency.OnRequest, false, false, false), 1.0));
        Assert.False(FreshReadPolicy.AnswerFromCache(null, 1.0));
    }

    /// <summary>
    /// No route ever issues a read on a subscribed var's data-definition id — that replaces the
    /// subscription and freezes the var (measured on the A380 FCU panel). A SIM_FRAME one is read on
    /// the caller's fresh id or the seed id; a PERIOD.SECOND one waits for its next delivery, even
    /// for a fresh read.
    /// </summary>
    [Theory]
    [InlineData(true, false, VarRequestRoute.SeedId)]
    [InlineData(true, true, VarRequestRoute.FreshId)]
    [InlineData(false, false, VarRequestRoute.AwaitNextDelivery)]
    [InlineData(false, true, VarRequestRoute.AwaitNextDelivery)]
    public void AnOwnSubscription_IsNeverReadOnItsDefinitionId(bool highFrequency, bool hasFreshId, VarRequestRoute expected)
    {
        var def = Def(UpdateFrequency.Continuous, true, true, highFrequency);

        Assert.Equal(expected, FreshReadPolicy.RouteRequest(def, hasFreshId));
    }

    [Theory]
    [InlineData(false, VarRequestRoute.DataDefinitionId)]
    [InlineData(true, VarRequestRoute.FreshId)]
    public void APlainIndividualDefinition_IsReadOnItsDefinitionId_OrTheFreshId(bool hasFreshId, VarRequestRoute expected)
    {
        Assert.Equal(expected, FreshReadPolicy.RouteRequest(Def(UpdateFrequency.OnRequest, false, false, false), hasFreshId));
        Assert.Equal(expected, FreshReadPolicy.RouteRequest(null, hasFreshId));
    }

    /// <summary>The live MD-11 speedbrake lever is the SIM_FRAME shape this whole route exists for.</summary>
    [Fact]
    public void TheMd11SpeedbrakeLever_IsSeededBesideItsSubscription()
    {
        var lever = new TFDiMD11Definition().GetVariables()[Md11SpeedbrakeSystem.LeverKey];

        Assert.Equal(VarRequestRoute.SeedId, FreshReadPolicy.RouteRequest(lever, hasFreshRequestId: false));
    }
}
