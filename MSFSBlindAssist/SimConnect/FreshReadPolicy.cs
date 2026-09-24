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
    /// the predicate SetupDataDefinitions and RequestVariable share. RequestVariable never issues a
    /// PERIOD.ONCE on such a var's OWN request id (that would replace the subscription): a
    /// PERIOD.SECOND one waits for its next delivery, a SIM_FRAME one is read on a separate id
    /// (<see cref="RouteRequest"/>).
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

    /// <summary>
    /// The request id of the one-shot SEED read that rides beside a SIM_FRAME + CHANGED own
    /// subscription, on the SAME data definition. Measured live (FS2024, 2026-09-09): a ONCE on a
    /// separate request id delivers within a frame and leaves the subscription untouched (a later
    /// change still arrived on the subscription's own id), whereas re-issuing the subscription's
    /// id — as ONCE or as the same SIM_FRAME request — delivers NOTHING new. The seed is what
    /// puts a value in the cache when the subscription's initial delivery never lands — and on a
    /// connect it never does: the DoEvents pump inside SetupDataDefinitions drains it before
    /// SetupEvents attaches the handler, which is how the MD-11 speedbrake lever sat uncached for
    /// whole sessions (2026-09-09, 2026-09-10) with every walk reading "state var unreadable".
    /// Seeds go out once the handler exists (SeedSimFrameSubscriptions: after SetupEvents on a
    /// connect, after an aircraft switch's re-registration) and from any RequestVariable that finds
    /// the cache empty or is forced. A seed answers NO waiter — a
    /// <see cref="SimConnectManager.ReadFreshAsync"/> that finds the cache empty asks under its OWN
    /// fresh id, so its answer carries the read's identity like any other — which is why it can be
    /// a fixed per-var id, resolved back through its definition id (<see cref="DataDefinitionIdOf"/>).
    /// Well above every data-definition id (the counter restarts at 1000 on every connection and
    /// aircraft switch, and a connection registers well under 2000), below
    /// <see cref="SimConnectManager.FreshRequestIdBase"/> (one million) so the two ranges cannot
    /// meet, and below the client-data ids the MD-11 MCDU namespaces at 0x4D44xxxx. Not the MD-11's
    /// context-reset "seed" (Md11SeedGate / SeedFromCache), which re-seeds its trackers FROM this cache.
    /// </summary>
    public const int SeedRequestIdOffset = 900000;

    public static int SeedRequestId(int dataDefId) => dataDefId + SeedRequestIdOffset;

    /// <summary>True for a request id in the seed band: at or above the offset, below the fresh-read range.</summary>
    public static bool IsSeedRequestId(int requestId)
        => requestId >= SeedRequestIdOffset && requestId < SimConnectManager.FreshRequestIdBase;

    /// <summary>The data-definition id a seed request id was issued for — the inverse of <see cref="SeedRequestId"/>.</summary>
    public static int DataDefinitionIdOf(int seedRequestId) => seedRequestId - SeedRequestIdOffset;

    /// <summary>
    /// True when <see cref="SimConnectManager.ReadFreshAsync"/> may answer from the cache without
    /// issuing anything: the var's cache IS the fresh value (<see cref="CacheIsFresh"/>) AND it has
    /// ever been delivered. An EMPTY cache must fall through to a read under the caller's own fresh
    /// id — handing back null here is how the MD-11 speedbrake walk read "state var unreadable" for
    /// whole sessions while never sending a click.
    /// </summary>
    public static bool AnswerFromCache(SimVarDefinition? def, double? cached)
        => CacheIsFresh(def) && cached != null;

    /// <summary>
    /// Where <see cref="SimConnectManager.RequestVariable(string, bool)"/> sends a read of an
    /// individual-def var. A var on its own subscription must NEVER be issued on its data-definition
    /// id (that replaces the subscription): a PERIOD.SECOND one waits for its next delivery, a
    /// SIM_FRAME + CHANGED one — which has no next delivery while it stands still — is read on the
    /// caller's fresh id when it has one, else on the seed id.
    /// </summary>
    public static VarRequestRoute RouteRequest(SimVarDefinition? def, bool hasFreshRequestId)
    {
        if (IsOwnSubscription(def) && !def!.HighFrequency) return VarRequestRoute.AwaitNextDelivery;
        if (hasFreshRequestId) return VarRequestRoute.FreshId;
        return IsOwnSubscription(def) ? VarRequestRoute.SeedId : VarRequestRoute.DataDefinitionId;
    }
}

/// <summary>The request id a read of an individual-def var goes out on — see <see cref="FreshReadPolicy.RouteRequest"/>.</summary>
public enum VarRequestRoute
{
    /// <summary>A PERIOD.ONCE on the var's own data-definition id (no subscription to disturb).</summary>
    DataDefinitionId,
    /// <summary>A PERIOD.ONCE on the caller's fresh-read id, answering that read alone.</summary>
    FreshId,
    /// <summary>A PERIOD.ONCE on the var's fixed seed id, beside its SIM_FRAME subscription.</summary>
    SeedId,
    /// <summary>Nothing issued: the PERIOD.SECOND subscription's next delivery honours the force flag.</summary>
    AwaitNextDelivery,
}
