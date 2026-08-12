using System.Collections.Concurrent;

namespace MSFSBlindAssist.Services.Gsx.Remote;

public sealed record GsxResult(bool Ok, string? ErrorCode, string? ErrorMessage)
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
    private readonly object _gate = new();
    private int _nextId;
    private string? _failReason;

    public int PendingCount => _pending.Count;

    public (string id, Task<GsxResult> task) Register()
    {
        string id = "msfsba-" + Interlocked.Increment(ref _nextId).ToString();

        lock (_gate)
        {
            if (_failReason != null)
            {
                // A sweep is currently in progress. Return an already-completed failed task
                // to prevent this registration from leaking in the dictionary.
                var failedTcs = new TaskCompletionSource<GsxResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                failedTcs.TrySetResult(GsxResult.Fail(_failReason));
                return (id, failedTcs.Task);
            }

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
        tcs.TrySetResult(new GsxResult(frame.Ok, frame.ErrorCode, frame.ErrorMessage));
        return true;
    }

    public void FailAll(string reason)
    {
        lock (_gate)
        {
            _failReason = reason;
            foreach (var key in _pending.Keys.ToArray())
                if (_pending.TryRemove(key, out var tcs))
                    tcs.TrySetResult(GsxResult.Fail(reason));
            _failReason = null;
        }
    }
}
