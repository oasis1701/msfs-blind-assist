using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Reads a fixed list of Fenix data-service names once a second and hands each value to a callback,
/// for the values the service's live subscription does not stream. Measured live (2026-10-22): the
/// engine N2 is sent once when a subscription opens and never again, so a subscription alone shows
/// a frozen N2. The First Officer and the Engines window poll those names through this and keep
/// the subscription for the rest.
/// </summary>
/// <remarks>
/// <para>The read is a delegate (in the app, <see cref="FenixDataService.ReadManyAsync"/>), so the
/// poller does not know the service and is tested without one. A value the service has no answer for
/// arrives as <see cref="FenixDataValue.Unknown"/> and is handed on as such, never as 0 ([FNX-1]).</para>
/// <para>Like the service's subscriptions ([FNX-2]), <c>onValue</c> runs on a thread-pool thread, never
/// the UI thread: a form marshals to its own thread, and nothing here speaks. Nothing here throws at
/// a caller because the Fenix is not loaded: a read or a callback that throws is swallowed and logged
/// once, and the loop goes on.</para>
/// </remarks>
public sealed class FenixDataPoller : IDisposable
{
    /// <summary>One read a second: fast enough for a gauge a pilot asks for, light on the gateway.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    private const string LogCategory = "FenixData";

    private readonly string[] _names;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, FenixDataValue>>> _read;
    private readonly Action<string, FenixDataValue> _onValue;
    private readonly TimeSpan _interval;
    // Never disposed, like FenixDataService's: it holds no timer or wait handle, and a read may still
    // be linked to its token after Dispose.
    private readonly CancellationTokenSource _lifetime = new();

    // _gate guards the two flags that decide whether the loop starts.
    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;

    // Each kind of failure is logged once for the poller's life, so a gateway that is down does not
    // write a line a second into debug.log.
    private int _readFailureLogged;
    private int _callbackFailureLogged;

    /// <param name="names">The data refs to read. Copied; null and empty names are dropped. None: the
    /// poller never reads.</param>
    /// <param name="read">Reads the names in one request (<see cref="FenixDataService.ReadManyAsync"/>).
    /// Its token is cancelled by <see cref="Dispose"/>.</param>
    /// <param name="onValue">Gets each name the read answered, in <paramref name="names"/> order.</param>
    /// <param name="interval">The wait between the end of one read and the start of the next;
    /// <see cref="DefaultInterval"/> when null.</param>
    public FenixDataPoller(
        IReadOnlyList<string> names,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyDictionary<string, FenixDataValue>>> read,
        Action<string, FenixDataValue> onValue,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(onValue);
        TimeSpan wait = interval ?? DefaultInterval;
        // Task.Delay refuses a negative wait, which would end the loop after its first read.
        if (wait < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The interval cannot be negative.");

        _names = names.Where(n => !string.IsNullOrEmpty(n)).ToArray();
        _read = read;
        _onValue = onValue;
        _interval = wait;
    }

    /// <summary>
    /// Starts the loop on the thread pool: read, wait the interval, read again, until
    /// <see cref="Dispose"/>. The first read is immediate and the next starts only after the previous
    /// one ended, so reads never overlap. A second call, a call after Dispose, or a poller with no
    /// names does nothing.
    /// </summary>
    public void Start()
    {
        if (_names.Length == 0) return;
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            var token = _lifetime.Token;
            _ = Task.Run(() => RunLoopAsync(token));
        }
    }

    /// <summary>
    /// One read, then <c>onValue</c> for each of the names the answer holds, in the names' order. A
    /// read that throws, or a callback that throws (the others still get theirs), is swallowed and
    /// logged once. Nothing is read, or handed on, once <paramref name="ct"/> or <see cref="Dispose"/>
    /// has cancelled. The loop calls this; it does not wait for the loop, so a caller that mixes the
    /// two can overlap a read.
    /// </summary>
    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        if (_names.Length == 0) return;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        if (linked.IsCancellationRequested) return;
        try
        {
            var answer = await _read(_names, linked.Token).ConfigureAwait(false);
            foreach (string name in _names)
            {
                // Disposed while the read was out: the owner is gone, so the answer is not handed on.
                if (linked.IsCancellationRequested) return;
                if (!answer.TryGetValue(name, out var value)) continue;
                try
                {
                    _onValue(name, value);
                }
                catch (Exception ex)
                {
                    LogOnce(ref _callbackFailureLogged,
                        $"Poll callback threw on {name}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // A cancelled read (Dispose, or the caller's token) is not the service failing.
            if (!linked.IsCancellationRequested)
                LogOnce(ref _readFailureLogged, $"Poll failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Cancels the loop and the read in flight (its token); a read already past its last await may still
    /// finish. Its answer is dropped on a best-effort basis only: a cancel that lands between the check
    /// before each callback and the callback itself lets that one value through, so a consumer guards
    /// against a late value itself (the First Officer's evaluator does, by generation). Never throws; a
    /// second call does nothing.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        // A callback registered on the read's token that throws would surface here.
        try { _lifetime.Cancel(); } catch (Exception) { }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
                await Task.Delay(_interval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Disposed.
        }
        catch (Exception ex)
        {
            // PollOnceAsync swallows its own failures, so this is not expected.
            Log.Debug(LogCategory, $"Poll loop stopped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogOnce(ref int flag, string message)
    {
        if (Interlocked.Exchange(ref flag, 1) == 0)
            Log.Debug(LogCategory, message);
    }
}
