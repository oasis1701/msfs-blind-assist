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
    /// Whether a fresh read is answered straight from the cache: a <see cref="CacheIsFresh"/> var
    /// whose cache HOLDS a value. An empty cache is not an answer. A CHANGED stream never sends a
    /// value it has not seen change, and SimConnect's baseline starts at zero, so a lever resting at
    /// 0 when it subscribes (the MD-11 flaps up, the speedbrake stowed) stays uncached until it
    /// moves — measured live 2026-09-26, where the First Officer's flap and spoiler steps and the
    /// panels' read-outs all had nothing to read. That read is asked once instead
    /// (<see cref="MayIssueOnce"/>).
    /// </summary>
    public static bool AnswersFromCache(SimVarDefinition? def, double? cached) => CacheIsFresh(def) && cached != null;

    /// <summary>
    /// Whether a PERIOD.ONCE may be issued for the var. Never under the data-definition id of a var
    /// on its own periodic subscription: that id IS the subscription's request id, and a ONCE on it
    /// replaces the subscription for the rest of the session. A fresh read issues its ONCE under its
    /// OWN request id, which cannot replace anything — allowed for a SIM_FRAME + CHANGED stream,
    /// where it is the only way to learn a value the stream will not send. A PERIOD.SECOND
    /// subscription keeps waiting for its next sample (at most a second).
    /// </summary>
    public static bool MayIssueOnce(SimVarDefinition? def, bool underOwnRequestId)
        => !IsOwnSubscription(def) || (underOwnRequestId && CacheIsFresh(def));

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
