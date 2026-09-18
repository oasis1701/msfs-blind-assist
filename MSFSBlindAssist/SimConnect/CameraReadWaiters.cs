using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Per-request waiters behind <see cref="SimConnectManager.ReadCameraViewAsync"/>: every camera
/// read is issued under its OWN request id, taken from a small rotating range, and only the
/// answer to that id completes it.
///
/// The first version shared one waiter — and one request id, REQUEST_CAMERA_VIEW — between every
/// read and protected only the "nobody is waiting" case: a read that timed out left its request
/// on the wire, and when the late answer arrived it completed the NEXT read with the camera as it
/// was before that read's switch. The AI display read then said it could not switch after a
/// switch that worked, or took a stale AlreadyThere, skipped the write and captured the wrong
/// display. Same rule as <see cref="FreshReadWaiters"/>, which does this for variable reads.
///
/// A rotating RANGE rather than FreshReadWaiters' ever-increasing ids because the answer is the
/// three-double CameraViewData and the dispatcher sends every id at or above
/// INDIVIDUAL_VARIABLE_BASE (1000) to the single-value path — so the camera ids live below it,
/// where only a small free block exists (pinned by CameraReadWaitersTests). An id comes round
/// again only after every other id in the range has been handed out, and never while a read is
/// still waiting on it. Reusing an ANSWERED id is safe: its one answer has been consumed.
/// Reusing an ABANDONED one is the residual: its late answer would complete the new read only if
/// it were still on its way back eight reads later — a request SimConnect has not yet answered is
/// simply replaced by the re-issue under the same id.
///
/// Thread-safe. Completions arrive on the dispatch (UI) thread; continuations run asynchronously
/// so the dispatch never runs the display read's code inline.
/// </summary>
internal sealed class CameraReadWaiters
{
    /// <summary>The id <see cref="Begin"/> hands out when every id in the range is still awaited.</summary>
    public const int NoRequestId = -1;

    private readonly object _lock = new();
    private readonly Dictionary<int, TaskCompletionSource<CameraViewReading?>> _pending = new();
    private readonly int _firstRequestId;
    private readonly int _count;
    private int _nextOffset;

    /// <param name="firstRequestId">The first id of the range.</param>
    /// <param name="count">How many ids the range holds, counting up from <paramref name="firstRequestId"/>.</param>
    public CameraReadWaiters(int firstRequestId, int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "The range needs at least one id.");
        _firstRequestId = firstRequestId;
        _count = count;
    }

    /// <summary>True for an id inside the range — the dispatch's test for "this answer is a camera read".</summary>
    public bool Owns(int requestId) => requestId >= _firstRequestId && requestId < _firstRequestId + _count;

    /// <summary>
    /// Registers a read under the next free id in the range (rotating, skipping any id still
    /// awaited) and returns that id — issue the request under it — and the task its answer
    /// completes. When every id is still awaited: <see cref="NoRequestId"/> and a task already
    /// completed with null, and the caller issues nothing.
    /// </summary>
    public (int RequestId, Task<CameraViewReading?> Result) Begin()
    {
        lock (_lock)
        {
            for (int tried = 0; tried < _count; tried++)
            {
                int requestId = _firstRequestId + _nextOffset;
                _nextOffset = (_nextOffset + 1) % _count;
                if (_pending.ContainsKey(requestId)) continue;

                var tcs = new TaskCompletionSource<CameraViewReading?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[requestId] = tcs;
                return (requestId, tcs.Task);
            }
        }
        return (NoRequestId, Task.FromResult<CameraViewReading?>(null));
    }

    /// <summary>
    /// Completes the read registered under <paramref name="requestId"/> with
    /// <paramref name="reading"/>. False when no read is waiting on that id — it was abandoned
    /// (timed out, or its request failed), already answered, or never handed out — and the answer
    /// then completes nothing.
    /// </summary>
    public bool Complete(int requestId, CameraViewReading reading)
    {
        TaskCompletionSource<CameraViewReading?>? tcs;
        lock (_lock)
        {
            if (!_pending.Remove(requestId, out tcs)) return false;
        }
        return tcs.TrySetResult(reading);
    }

    /// <summary>
    /// Forgets the read registered under <paramref name="requestId"/> — its caller gave up — and
    /// releases its task with null. Its late answer then completes nothing.
    /// </summary>
    public void Abandon(int requestId)
    {
        TaskCompletionSource<CameraViewReading?>? tcs;
        lock (_lock)
        {
            if (!_pending.Remove(requestId, out tcs)) return;
        }
        tcs.TrySetResult(null);
    }

    /// <summary>Releases every waiting read with null — a disconnect or aircraft switch means no answer is coming.</summary>
    public void FailAll()
    {
        List<TaskCompletionSource<CameraViewReading?>> waiting;
        lock (_lock)
        {
            waiting = new List<TaskCompletionSource<CameraViewReading?>>(_pending.Values);
            _pending.Clear();
        }
        foreach (var tcs in waiting) tcs.TrySetResult(null);
    }
}
