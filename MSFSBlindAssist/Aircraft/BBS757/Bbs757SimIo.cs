namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// What the 757's panel writes (a button press, a position pick, an autopilot window press) need from
/// the sim, as delegates, so the decisions around them can be tested without a simulator: whether a
/// calculator write can land, a fresh read and the cached value of a registered variable, one
/// calculator write, a forced re-read (the combo resync) and an awaitable wait.
/// <see cref="BBS757Definition"/> builds the real one from its SimConnectManager.
/// </summary>
public sealed class Bbs757SimIo
{
    /// <summary>Whether a calculator write can land now (<c>CalcWriteCanLand</c>).</summary>
    public required Func<bool> CanWrite { get; init; }

    /// <summary>A registered variable read fresh; null when it cannot be read.</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadFresh { get; init; }

    /// <summary>A registered variable's last delivered value (at most a batch old for a streamed one), or null.</summary>
    public required Func<string, double?> Cached { get; init; }

    /// <summary>One calculator write (<c>ExecuteCalculatorCodeUnique</c>).</summary>
    public required Action<string> Write { get; init; }

    /// <summary>A forced re-read whose delivery reaches the panel (the combo resync).</summary>
    public required Action<string> RequestRead { get; init; }

    /// <summary>An awaitable wait.</summary>
    public required Func<int, CancellationToken, Task> Delay { get; init; }
}
