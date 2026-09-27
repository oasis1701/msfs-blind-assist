using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The walker's window onto the sim, so the step protocol runs unchanged against a fake switch in
/// a test and against SimConnect + the CEVENT bus in production.
/// </summary>
internal sealed class Md11WalkIo
{
    /// <summary>Force-reads and completes on the next delivery; null on timeout. Fresh protocol only.</summary>
    public required Func<CancellationToken, Task<double?>> ReadFresh { get; init; }

    /// <summary>The last delivered value, or null when never delivered.</summary>
    public required Func<double?> ReadCached { get; init; }

    /// <summary>Issues a force-read without waiting — the legacy cache-poll protocol's request.</summary>
    public required Action RequestRead { get; init; }

    /// <summary>Queues one CEVENT id.</summary>
    public required Action<int> Fire { get; init; }

    /// <summary>CEVENTs already queued ahead of the next <see cref="Fire"/> — see <see cref="Md11EventBus.Pending"/>. Tests return 0 or a chosen backlog.</summary>
    public required Func<int> Pending { get; init; }

    /// <summary>Waits — Task.Delay in production, a virtual clock in tests.</summary>
    public required Func<int, CancellationToken, Task> Delay { get; init; }

    /// <summary>Milliseconds on a monotonic clock.</summary>
    public required Func<long> Now { get; init; }

    /// <summary>
    /// True when a fresh read of the var reflects the aircraft within about a frame — a plain
    /// individual-def var, or one streaming on its own SIM_FRAME subscription whose cache is the
    /// fresh value (the MD-11 flap lever and speedbrake). A var that answers only with the next
    /// 1 Hz delivery keeps the legacy cache-poll protocol. Decided by
    /// <see cref="SimConnectManager.SupportsFreshReads"/>.
    /// </summary>
    public required bool FreshReads { get; init; }

    /// <summary>Production wiring. <paramref name="bus"/> may be null for a read-only use.</summary>
    public static Md11WalkIo ForSim(SimConnectManager sim, Md11EventBus? bus, string varKey) => new()
    {
        ReadFresh = ct => sim.ReadFreshAsync(varKey, Md11SelectorWalker.FreshReadTimeoutMs, ct),
        ReadCached = () => sim.GetCachedVariableValue(varKey),
        RequestRead = () => sim.RequestVariable(varKey, forceUpdate: true),
        Fire = id => bus?.Fire(id),
        Pending = () => bus?.Pending ?? 0,
        Delay = (ms, ct) => Task.Delay(ms, ct),
        Now = () => Environment.TickCount64,
        FreshReads = sim.SupportsFreshReads(varKey),
    };
}
