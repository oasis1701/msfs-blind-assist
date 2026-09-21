namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// One-shot "wake me on the NEXT delivery of this key" waiters behind
/// <see cref="SimConnectManager.ReadFreshAsync"/>.
///
/// A forced read of an individual-def var answers on the next SimConnect dispatch (a frame or
/// two); a batch-covered var answers with its next continuous batch (up to one period). Either
/// way the delivery is the only trustworthy "fresh" signal. Sleeping a fixed interval and reading
/// the cache — the MD-11 walker's previous protocol — read stale values, called real movement "no
/// movement", and mis-learned the control's step polarity. Same idea as
/// <c>PMDGNG3DataManager.RequestFreshSnapshotAsync</c>, generalised to a key.
///
/// Every read is issued under its OWN request id (allocated here, handed to <c>issueRequest</c>),
/// and an individual-def answer completes a waiter only while a caller that issued that request
/// is still waiting (<see cref="Complete(string, int, double)"/>). A read that timed out or was
/// cancelled is abandoned on the spot: its late answer updates the cache and wakes nobody, so the
/// next read of the key is answered by its own request and never by a value SimConnect sampled
/// before it asked. The first version issued the ONCE under the var's data-definition id — no
/// identity on the wire — and left a timed-out waiter registered, so the abandoned request's late
/// answer satisfied the next caller with a pre-request value. The camera read follows the same
/// rule (<see cref="CameraReadWaiters"/>). A continuous-batch or periodic-subscription
/// delivery answers no request; it is a sample taken after the waiter registered and so never
/// stale for it, which is what the id-less <see cref="Complete(string, double)"/> is for.
///
/// One waiter per key: concurrent callers share it and all wake on the one delivery — an answer
/// to any sharer's request that is still being waited on. Continuations run asynchronously so
/// the SimConnect dispatch that completes a waiter never runs walker code inline. Completion
/// happens on the dispatch (UI) thread; waits come from pool threads.
///
/// ⚠️ THE SHARING NARROWS THE GUARANTEE ABOVE, and the limit is structural rather than a bug to
/// fix here. "Never a value SimConnect sampled before it asked" holds for a caller whose request
/// is the one that answers. A SECOND caller that joins an entry already registered for the same
/// key is completed by the FIRST caller's answer — a sample that may predate the second caller's
/// own request, whose later answer then finds the entry gone and is dropped as late. That is the
/// very staleness this class exists to prevent, one caller along.
///
/// It is unreachable today and nothing here defends against it: every same-key caller is
/// serialised by the layer above (<c>DebouncedWalk</c> cancels the prior walk per node, a guard is
/// 1:1 with its control in the map, and the batched read-backs use distinct keys). Making each
/// caller its own waiter is the real fix and costs a list per key on a hot path, so it is not worth
/// paying for a case no caller can currently produce — but a future second concurrent reader of one
/// key inherits a pre-request value SILENTLY, so add the per-caller entry BEFORE introducing one.
/// </summary>
internal sealed class FreshReadWaiters
{
    private sealed class Entry
    {
        public readonly TaskCompletionSource<double?> Tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Request ids of the callers still waiting on this entry.</summary>
        public readonly HashSet<int> Live = new();
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _pending = new(StringComparer.Ordinal);
    private int _nextRequestId;

    /// <param name="firstRequestId">
    /// The first request id handed to <c>issueRequest</c>; ids count up from it and are never
    /// reused. The manager passes a range clear of every data-definition id.
    /// </param>
    public FreshReadWaiters(int firstRequestId = 1)
    {
        _nextRequestId = firstRequestId;
    }

    /// <summary>Keys with at least one caller still waiting. A timed-out or cancelled read leaves nothing behind.</summary>
    public int Count
    {
        get { lock (_lock) return _pending.Count; }
    }

    /// <summary>
    /// Registers a waiter for <paramref name="key"/>, runs <paramref name="issueRequest"/> with
    /// this read's request id (the force-read, issued under that id) and waits for the answer.
    /// Returns the delivered value, or null when nothing arrives within
    /// <paramref name="timeoutMs"/> — never a stale cached value. Throws
    /// <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled: a
    /// cancelled walk must stop, not read. On every exit the request is released, so a late
    /// answer to it completes nothing.
    /// </summary>
    public async Task<double?> WaitAsync(string key, Action<int> issueRequest, int timeoutMs, CancellationToken ct = default)
    {
        Entry entry;
        int requestId;
        lock (_lock)
        {
            requestId = _nextRequestId++;
            if (!_pending.TryGetValue(key, out var existing))
            {
                existing = new Entry();
                _pending[key] = existing;
            }
            entry = existing;
            entry.Live.Add(requestId);
        }
        try
        {
            issueRequest(requestId);
            return await entry.Tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            // Delivered, timed out, cancelled, or issueRequest threw: this request no longer
            // counts, and the entry goes with its last caller — if it is still the registered one
            // (a delivery may already have removed it and a newer caller re-registered the key).
            Release(key, entry, requestId);
        }
    }

    private void Release(string key, Entry entry, int requestId)
    {
        lock (_lock)
        {
            entry.Live.Remove(requestId);
            if (entry.Live.Count == 0 &&
                _pending.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _pending.Remove(key);
            }
        }
    }

    /// <summary>
    /// Completes the waiter for <paramref name="key"/> with the answer to <paramref name="requestId"/>,
    /// if a caller is still waiting on that request. False when the request was abandoned (its
    /// read timed out or was cancelled) or already answered: the answer is dropped from the
    /// waiter — the caller's own cache update is its business. Call from the individual-def
    /// delivery path for a request id handed out by <see cref="WaitAsync"/>.
    /// </summary>
    public bool Complete(string key, int requestId, double value)
    {
        Entry? entry;
        lock (_lock)
        {
            if (!_pending.TryGetValue(key, out entry) || !entry.Live.Contains(requestId)) return false;
            _pending.Remove(key);
        }
        return entry.Tcs.TrySetResult(value);
    }

    /// <summary>
    /// Completes the waiter for <paramref name="key"/>, if any, with a delivery that answers no
    /// particular request — a continuous-batch or periodic-subscription sample, taken after the
    /// waiter registered and so never stale for it. Call on EVERY such delivery, changed or not.
    /// </summary>
    public bool Complete(string key, double value)
    {
        Entry? entry;
        lock (_lock)
        {
            if (!_pending.Remove(key, out entry)) return false;
        }
        return entry.Tcs.TrySetResult(value);
    }

    /// <summary>Releases every waiter with null — a disconnect or aircraft switch means no delivery is coming.</summary>
    public void FailAll()
    {
        List<Entry> entries;
        lock (_lock)
        {
            entries = new List<Entry>(_pending.Values);
            _pending.Clear();
        }
        foreach (var entry in entries) entry.Tcs.TrySetResult(null);
    }
}
