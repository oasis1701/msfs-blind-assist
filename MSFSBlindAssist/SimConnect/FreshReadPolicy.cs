namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// What a "fresh" read of a var can be — the policy behind <see cref="SimConnectManager.ReadFreshAsync"/>
/// and <see cref="SimConnectManager.SupportsFreshReads"/>, kept pure so the classification of an
/// aircraft's vars can be pinned. Getting this wrong is silent and expensive: the MD-11 flap lever
/// was first taken for batch-covered, when it streams on its own SIM_FRAME subscription.
/// </summary>
public static class FreshReadPolicy
{
    /// <summary>
    /// A var streaming on its own periodic subscription: Continuous + IsAnnounced + ExcludeFromBatch,
    /// the predicate SetupDataDefinitions and RequestVariable share. RequestVariable issues NO
    /// PERIOD.ONCE for such a var (a ONCE on the same request id would replace the subscription).
    /// </summary>
    public static bool IsOwnSubscription(SimVarDefinition? def)
        => def != null && def.UpdateFrequency == UpdateFrequency.Continuous && def.IsAnnounced && def.ExcludeFromBatch;

    /// <summary>
    /// True when the CACHE is the fresh value: a SIM_FRAME + CHANGED own subscription updates it
    /// within a frame of any change and delivers nothing while the value stands still, so waiting
    /// for a delivery would only time out. The MD-11 flap lever and speedbrake are this kind.
    /// </summary>
    public static bool CacheIsFresh(SimVarDefinition? def) => IsOwnSubscription(def) && def!.HighFrequency;

    /// <summary>
    /// True when a fresh read reflects the aircraft within about a frame: a plain individual-def var
    /// (its PERIOD.ONCE answers on the next dispatch) or a SIM_FRAME own subscription (the cache).
    /// False for a batch-covered var (no individual definition) and for a PERIOD.SECOND own
    /// subscription — both answer with the next 1 Hz delivery at best, so a walker on one keeps the
    /// legacy cache-poll protocol.
    /// </summary>
    public static bool SupportsFreshReads(bool hasIndividualDefinition, SimVarDefinition? def)
        => hasIndividualDefinition && (!IsOwnSubscription(def) || def!.HighFrequency);
}
