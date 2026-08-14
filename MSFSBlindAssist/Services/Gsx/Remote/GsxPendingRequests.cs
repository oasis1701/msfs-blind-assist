using System.Collections.Concurrent;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <param name="Ok">Whether the command succeeded.</param>
/// <param name="ErrorCode">GSX's <c>error.code</c> on failure; null on success.</param>
/// <param name="ErrorMessage">GSX's <c>error.message</c> on failure, or a locally-synthesized
/// reason ("not connected", "timed out", "send failed") when the failure never reached GSX.</param>
/// <param name="Frame">
/// The full result frame this <see cref="GsxResult"/> was built from — carries
/// <see cref="GsxFrame.Payload"/> and <see cref="GsxFrame.Error"/> for callers that need more
/// than the three members above (e.g. <c>GsxGateSelectResult.FromFrame</c> reading
/// <c>payload.gate</c>/<c>payload.warnings</c>/<c>error.candidates</c>). Null for a
/// locally-synthesized failure that never had a live frame — <see cref="Fail"/>, and any
/// <see cref="GsxPendingRequests.FailAll"/> sweep. Added additively: every pre-existing
/// <see cref="GsxResult"/> member keeps its prior meaning and every pre-existing call site
/// that builds or reads one still compiles and behaves identically.
/// </param>
public sealed record GsxResult(bool Ok, string? ErrorCode, string? ErrorMessage, GsxFrame? Frame = null)
{
    public static readonly GsxResult Success = new(true, null, null);
    public static GsxResult Fail(string message) => new(false, null, message);
}

/// <summary>
/// Correlates commands with their <c>result</c> frames. The GSX API echoes the
/// <c>id</c> we send on every result, including failures, so a plain dictionary
/// of TaskCompletionSources is sufficient — results may arrive out of order.
/// </summary>
public sealed class GsxPendingRequests
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<GsxResult>> _pending =
        new(StringComparer.Ordinal);
    // Register and FailAll share ONE lock, and THAT is what stops a registration
    // landing mid-sweep from leaking (never removed, never completed, its caller's
    // task pending forever). Nothing else here closes that race: an earlier version
    // also carried a "sweep in progress" flag, which read as the load-bearing part
    // but was unreachable once the lock existed. Do not drop the lock.
    //
    // The store must also stay fully usable AFTER FailAll returns — it is NOT
    // per-connection. GsxRemoteConnection holds one instance for its lifetime and
    // calls FailAll on every disconnect, and GSX drops its connection routinely.
    private readonly object _gate = new();
    private int _nextId;

    public int PendingCount => _pending.Count;

    public (string id, Task<GsxResult> task) Register()
    {
        string id = "msfsba-" + Interlocked.Increment(ref _nextId).ToString();

        lock (_gate)
        {
            // RunContinuationsAsynchronously: never run a caller's continuation on the
            // socket receive loop.
            var tcs = new TaskCompletionSource<GsxResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;
            return (id, tcs.Task);
        }
    }

    /// <summary>True when the frame matched a pending request.</summary>
    public bool Complete(GsxFrame frame)
    {
        if (frame.Type != GsxFrameType.Result || string.IsNullOrEmpty(frame.Id)) return false;
        if (!_pending.TryRemove(frame.Id, out var tcs)) return false;
        tcs.TrySetResult(new GsxResult(frame.Ok, frame.ErrorCode, frame.ErrorMessage, frame));
        return true;
    }

    public void FailAll(string reason)
    {
        lock (_gate)
        {
            // Under the shared lock the snapshot->sweep span is atomic with
            // respect to Register, so no insert can slip in behind the sweep.
            foreach (var key in _pending.Keys.ToArray())
                if (_pending.TryRemove(key, out var tcs))
                    tcs.TrySetResult(GsxResult.Fail(reason));
        }
    }
}
