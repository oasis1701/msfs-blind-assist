using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// When the cabin-ready call is due: a known false-to-true change of <see cref="FenixCabinNames.CabinReady"/>.
/// The first Bool is a silent baseline, so a cabin that is already ready when the app connects is never
/// announced. A value that is not a Bool (unknown, JSON null, a number) is ignored and keeps the baseline,
/// never read as false ([FNX-1]). Not thread-safe: <see cref="FenixCabinReadyMonitor"/> holds one lock
/// around it.
/// </summary>
public sealed class FenixCabinReadyEdge
{
    // The last Bool; null until the first one, the baseline.
    private bool? _last;

    /// <summary>The value read once at start. It becomes the baseline only when there is none yet (a
    /// value from the source may have arrived first) and it is a Bool.</summary>
    public void Seed(FenixDataValue value)
    {
        if (_last == null && value.Kind == FenixValueKind.Bool)
            _last = value.Bool;
    }

    /// <summary>True only when a Bool true follows a known false. The first Bool is the baseline and
    /// returns false; a value that is not a Bool returns false and changes nothing.</summary>
    public bool Next(FenixDataValue value)
    {
        if (value.Kind != FenixValueKind.Bool) return false;
        bool rose = _last == false && value.Bool;
        _last = value.Bool;
        return rose;
    }
}

/// <summary>
/// Speaks <see cref="SpokenText"/> once each time the Fenix cabin reports ready, anywhere in the app, while
/// the Fenix is loaded (in Fenix v3 the virtual purser sets it, not the cockpit cabin call). It is the one
/// place in the app that says it: the Cabin window and the First Officer only show the value.
/// <c>FenixA320Definition</c> starts one from its cabin background seam and disposes it when the pilot
/// switches away.
/// </summary>
/// <remarks>
/// <para>The value comes from a <see cref="FenixDataPoller"/> reading <see cref="FenixCabinNames.CabinReady"/>
/// once a second, not from the data service's subscription, which (measured live, 2026-10-22) does not
/// stream every name. The source starts first, then one read seeds the baseline; if the source's first value
/// arrives before that read, it is the baseline and the seed is ignored (<see cref="FenixCabinReadyEdge"/>).</para>
/// <para>Values arrive on a thread-pool thread ([FNX-2]). A rise is posted to the UI
/// <see cref="SynchronizationContext"/> captured by <see cref="Start"/>, inside a try/catch: a UI thread that
/// is gone refuses the post with <see cref="InvalidOperationException"/> and the call is dropped ([A380C-6]).
/// The posted call checks <see cref="Dispose"/>'s flag on the UI thread, so a post queued before Dispose,
/// or a late value from the source, says nothing after the switch. Speech goes through
/// <see cref="ScreenReaderAnnouncer.Announce"/>, the automatic path, so it is silent during the start-up
/// grace period. Nothing is logged or spoken per value.</para>
/// </remarks>
public sealed class FenixCabinReadyMonitor : IDisposable
{
    public const string SpokenText = "Cabin ready";

    private const string LogCategory = "FenixCabin";

    private readonly object _gate = new();
    private readonly FenixCabinReadyEdge _edge = new();
    private readonly SynchronizationContext? _ui;
    private readonly Action<string> _speak;
    private readonly IDisposable _source;
    // Set under _gate; read without it by the posted call on the UI thread.
    private volatile bool _disposed;

    /// <summary>
    /// Starts the monitor on the Fenix data service. Call it on the UI thread: it captures
    /// <see cref="SynchronizationContext.Current"/> to speak on.
    /// </summary>
    public static FenixCabinReadyMonitor Start(FenixDataService data, ScreenReaderAnnouncer announcer)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(announcer);
        var monitor = new FenixCabinReadyMonitor(
            onValue =>
            {
                var poller = new FenixDataPoller(new[] { FenixCabinNames.CabinReady }, data.ReadManyAsync, onValue);
                poller.Start();
                return poller;
            },
            () => data.ReadAsync(FenixCabinNames.CabinReady),
            SynchronizationContext.Current,
            announcer.Announce);
        Log.Debug(LogCategory, "Cabin-ready monitor started");
        return monitor;
    }

    /// <param name="subscribe">Starts the source of values with the callback (pool thread) and returns
    /// what stops it. Called first.</param>
    /// <param name="readOnce">The one read that seeds the baseline, started after the source.</param>
    /// <param name="ui">Where the speech runs; null speaks on the calling thread.</param>
    /// <param name="speak">Says the text.</param>
    internal FenixCabinReadyMonitor(
        Func<Action<string, FenixDataValue>, IDisposable> subscribe,
        Func<Task<FenixDataValue>> readOnce,
        SynchronizationContext? ui,
        Action<string> speak)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(readOnce);
        ArgumentNullException.ThrowIfNull(speak);
        _ui = ui;
        _speak = speak;
        // Every field OnValue reads is set before the source can call it.
        _source = subscribe(OnValue);
        _ = SeedAsync(readOnce);
    }

    /// <summary>Stops the source; from then on nothing is spoken, a call already posted included. Never
    /// throws; a second call does nothing.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _source.Dispose();
        Log.Debug(LogCategory, "Cabin-ready monitor stopped");
    }

    private async Task SeedAsync(Func<Task<FenixDataValue>> readOnce)
    {
        try
        {
            FenixDataValue value = await readOnce().ConfigureAwait(false);
            lock (_gate)
            {
                if (!_disposed) _edge.Seed(value);
            }
        }
        catch (Exception ex)
        {
            // FenixDataService.ReadAsync answers Unknown rather than throw; once per monitor at most.
            Log.Debug(LogCategory, $"Cabin-ready first read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The source's callback, on a pool thread.
    private void OnValue(string name, FenixDataValue value)
    {
        if (!string.Equals(name, FenixCabinNames.CabinReady, StringComparison.Ordinal)) return;
        bool rose;
        lock (_gate)
        {
            if (_disposed) return;
            rose = _edge.Next(value);
        }
        if (rose) Announce();
    }

    private void Announce()
    {
        if (_ui == null)
        {
            SpeakUnlessDisposed();
            return;
        }
        try
        {
            _ui.Post(_ => SpeakUnlessDisposed(), null);
        }
        catch (InvalidOperationException)
        {
            // The UI thread is gone (the app is closing): there is nothing to speak on.
        }
    }

    private void SpeakUnlessDisposed()
    {
        if (!_disposed) _speak(SpokenText);
    }
}
