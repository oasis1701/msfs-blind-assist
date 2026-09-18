namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Cancels a set of walk cancellation sources for <c>TFDiMD11Definition.Dispose</c>. Pure so the
/// tolerance it needs is pinned: the definition's <c>_walkCts</c> snapshot can carry a source whose
/// walk removed and DISPOSED it a moment earlier (DebouncedWalk's finally), and one disposed source
/// must not abort the sweep before the live ones behind it are reached — a walk left running
/// finishes against the NEXT aircraft's registrations and speaks "did not move" for it.
/// </summary>
public static class Md11WalkCancellation
{
    /// <summary>Cancels every live source; skips null, disposed and already-cancelled ones. Returns how many it cancelled.</summary>
    public static int CancelAll(IEnumerable<CancellationTokenSource?> sources)
    {
        var cancelled = 0;
        foreach (var source in sources)
        {
            if (source == null) continue;
            try
            {
                if (source.IsCancellationRequested) continue;
                source.Cancel();
                cancelled++;
            }
            catch (ObjectDisposedException)
            {
                // Its walk already left; nothing to cancel.
            }
            catch (AggregateException)
            {
                // A registered callback threw; the token IS cancelled, which is all that matters here.
                cancelled++;
            }
        }
        return cancelled;
    }
}
