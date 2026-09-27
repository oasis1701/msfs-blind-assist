namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// A poll-loop sleep that <see cref="Wake"/> can cut short. A wake that arrives while nobody is
/// waiting ends the NEXT wait at once; several wakes before a wait end only that one wait. Used
/// by the A32NX MCDU Coherent client so that showing the window, or asking for a fresh frame, is
/// answered by the next read straight away instead of after the idle interval — without it the
/// screen reader read the screen from before the window was closed.
/// Not disposable on purpose: the loop that waits on it is never joined, and the semaphore's
/// wait handle is never materialised, so nothing leaks (the Coherent clients' send locks are
/// left undisposed for the same reason).
/// </summary>
internal sealed class WakeableDelay
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    /// <summary>Wait <paramref name="delay"/>, or until woken. Throws on cancellation.</summary>
    public Task WaitAsync(TimeSpan delay, CancellationToken ct) => _signal.WaitAsync(delay, ct);

    public void Wake()
    {
        // One slot: a second wake before anyone waits is the same wake.
        try { _signal.Release(); }
        catch (SemaphoreFullException) { }
    }
}
