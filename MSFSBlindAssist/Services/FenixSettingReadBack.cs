namespace MSFSBlindAssist.Services;

/// <summary>
/// What a <see cref="FenixSettingReadBack.AwaitAsync{T}"/> wait found: the last read (null when that read got no
/// answer), whether a read showed the change, and whether any read was answered at all.
/// </summary>
public readonly record struct FenixReadBackResult<T>(T? Last, bool Landed, bool Answered) where T : class;

/// <summary>
/// The wait the Fenix windows make after a settings write (FNX-13): the settings query can still return the old
/// value for up to about 2 s after <c>setSetting</c> answered success, so a read that does not show the change
/// yet is read again, never taken as the result. Pure logic: the read, the test and the delay are the caller's.
/// </summary>
public static class FenixSettingReadBack
{
    /// <summary>The time between reads: 16 reads span 3.75 s, past the longest lag measured (1.9 s).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    /// <summary>The most reads one wait makes.</summary>
    public const int Attempts = 16;

    /// <summary>
    /// Reads until <paramref name="landed"/> says a read shows the change, waiting <paramref name="interval"/>
    /// (through <paramref name="delay"/>) between reads, at most <paramref name="attempts"/> reads. A read is
    /// null when the Fenix did not answer it: with <paramref name="stopOnNoAnswer"/> the wait ends there (each
    /// such read can take the data service's 10 s time-out, so it is not repeated against a Fenix that is gone),
    /// else the wait goes on. Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> is
    /// cancelled: the token is checked right before every read, after its delay, so a cancel during a delay
    /// that ignores the token still never starts another read.
    /// </summary>
    public static async Task<FenixReadBackResult<T>> AwaitAsync<T>(
        Func<CancellationToken, Task<T?>> read, Func<T, bool> landed, TimeSpan interval, int attempts,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct, bool stopOnNoAnswer) where T : class
    {
        bool answered = false;
        T? last = null;
        for (int i = 0; i < attempts; i++)
        {
            if (i > 0) await delay(interval, ct);
            ct.ThrowIfCancellationRequested();
            last = await read(ct);
            if (last == null)
            {
                if (stopOnNoAnswer) break;
                continue;
            }
            answered = true;
            if (landed(last)) return new FenixReadBackResult<T>(last, true, true);
        }
        return new FenixReadBackResult<T>(last, false, answered);
    }
}
