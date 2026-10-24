using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;
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
/// The app-wide cabin announcements while the Fenix is loaded, anywhere in the app, not only with the Cabin
/// window open. It speaks <see cref="SpokenText"/> once each time the Fenix cabin reports ready (in Fenix v3
/// the virtual purser sets it, not the cockpit cabin call), and is the one place in the app that says it: the
/// Cabin window and the First Officer only show the value. Since the owner's request of 2026-10-23 it also
/// says a door opening or closing, the slides all armed or all disarmed, and a cabin caution appearing
/// (<see cref="FenixCabinEvents"/>). Each of the four kinds has its own Fenix Monitor Manager (Ctrl+M) row
/// (<see cref="FenixCabinAnnouncements"/>). <c>FenixA320Definition</c> starts one from its cabin background
/// seam and disposes it when the pilot switches away.
/// </summary>
/// <remarks>
/// <para>The values come from one <see cref="FenixDataPoller"/> reading every cabin name
/// (<see cref="FenixCabinNames.All"/>) once a second, not from the data service's subscription, which
/// (measured live, 2026-10-22) does not stream every name. The poller's first read is immediate, so the
/// source's first Bool is the silent cabin-ready baseline (<see cref="FenixCabinReadyEdge"/>); there is no
/// separate seed read. Cabin ready is judged on its own value as it comes; the other kinds once per poll,
/// when its last name has come (the poller hands every name of each read on, in order, and the data service
/// answers every name it is asked, Unknown included).</para>
/// <para>The layout (which doors, slides and lavatories) is resolved as the Cabin window resolves it: from
/// the definition's variant and the Fenix's three cabin settings, read at start, again each time the Fenix
/// starts answering (no known value, then one), and on every variant change. An empty settings answer keeps
/// what an earlier read found.</para>
/// <para>A door or cargo door the pilot pressed in the Cabin window is left to the window's read-back while
/// that can still speak (<see cref="FenixCabinOwnPresses"/>), so the change is said once. Its
/// baseline still moves, so it is not said later either.</para>
/// <para>After the Fenix has not answered for more than <see cref="OutageRebaseline"/> (every polled value
/// unknown: the data service down, the Fenix reloading), the first values that answer again are a new silent
/// baseline for all four kinds, so a second flight loaded in the same session is not read out as
/// a burst of changes. A shorter outage, or a value unknown on its own, keeps the baseline.</para>
/// <para>Values arrive on a thread-pool thread ([FNX-2]). What to say is posted to the UI
/// <see cref="SynchronizationContext"/> captured by <see cref="Start"/>, inside a try/catch: a UI thread that
/// is gone refuses the post with <see cref="InvalidOperationException"/> and the call is dropped ([A380C-6]).
/// The posted call checks <see cref="Dispose"/>'s flag on the UI thread, so a post queued before Dispose,
/// or a late value from the source, says nothing after the switch; and it reads the mute list then, so a row
/// unticked in Ctrl+M silences its kind at once. Speech goes through
/// <see cref="ScreenReaderAnnouncer.Announce"/>, the automatic path, so it is silent during the start-up
/// grace period. Nothing is logged or spoken per value.</para>
/// </remarks>
public sealed class FenixCabinReadyMonitor : IDisposable
{
    public const string SpokenText = "Cabin ready";

    private const string LogCategory = "FenixCabin";

    /// <summary>How long the Fenix may not answer before its next answer is a new silent baseline.</summary>
    internal static readonly TimeSpan OutageRebaseline = TimeSpan.FromSeconds(10);

    // The last name of each poll: when it has come, the poll is whole and the other kinds are judged.
    private static readonly string LastPolledName = FenixCabinNames.All[^1];

    private readonly object _gate = new();
    // Both replaced, under _gate, after a long outage.
    private FenixCabinReadyEdge _edge = new();
    private FenixCabinEvents _events = new();
    // The values of the poll being handed on (Unknown included), under _gate.
    private readonly FenixCabinData _poll = new();
    private readonly SynchronizationContext? _ui;
    private readonly Action<string> _speak;
    private readonly Func<string, bool> _isMuted;
    private readonly FenixCabinOwnPresses _ownPresses;
    private readonly Func<DateTime> _clock;
    private readonly Func<Task<IReadOnlyList<FenixSetting>>>? _readSettings;
    private readonly IDisposable _source;
    // Set under _gate; read without it by the posted call on the UI thread.
    private volatile bool _disposed;
    // Run by Dispose: lets go of the definition's VariantChanged. Set once, by Start.
    private Action? _detach;

    // The layout and what it is resolved from, under _gate.
    private FenixAircraftType _type;
    private string? _exitsSetting;
    private string? _variantSetting;
    private string? _slideMessageSetting;
    private FenixCabinLayout _layout;
    // Whether the last whole poll had any known value; the settings are read again when it turns true.
    private bool _wasAnswering;
    // When the Fenix last answered (a whole poll with a known value, or a known cabin-ready value after an
    // outage); null before the first answer.
    private DateTime? _lastAnsweredAt;
    // Settings reads, numbered as they start: an answer older than one already applied is dropped.
    private int _settingsStarted;
    private int _settingsApplied;

    /// <summary>
    /// Starts the monitor on the Fenix data service. Call it on the UI thread: it captures
    /// <see cref="SynchronizationContext.Current"/> to speak on, and subscribes to the definition's
    /// <see cref="FenixA320Definition.VariantChanged"/>, which is raised there.
    /// </summary>
    public static FenixCabinReadyMonitor Start(FenixDataService data, ScreenReaderAnnouncer announcer, FenixA320Definition definition)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(announcer);
        ArgumentNullException.ThrowIfNull(definition);
        var monitor = new FenixCabinReadyMonitor(
            onValue =>
            {
                var poller = new FenixDataPoller(FenixCabinNames.All, data.ReadManyAsync, onValue);
                poller.Start();
                return poller;
            },
            SynchronizationContext.Current,
            announcer.Announce,
            // The live mute list: SettingsManager.Save rebuilds the set right after every Ctrl+M toggle.
            isMuted: key => SettingsManager.Current.FenixDisabledMonitorVariablesSet.Contains(key),
            ownPresses: FenixCabinOwnPresses.Shared,
            clock: () => DateTime.UtcNow,
            type: definition.Variant.Type,
            readSettings: () => data.GetSettingsAsync());
        EventHandler<FenixVariant> onVariant = (_, variant) => monitor.OnVariantChanged(variant.Type);
        definition.VariantChanged += onVariant;
        monitor._detach = () => definition.VariantChanged -= onVariant;
        Log.Debug(LogCategory, "Cabin monitor started");
        return monitor;
    }

    /// <param name="subscribe">Starts the source of values with the callback (pool thread) and returns
    /// what stops it. Called first. It hands on every name of <see cref="FenixCabinNames.All"/> per read,
    /// in that order.</param>
    /// <param name="ui">Where the speech runs; null speaks on the calling thread.</param>
    /// <param name="speak">Says the text.</param>
    /// <param name="isMuted">Whether a kind's Ctrl+M row is unticked (<see cref="FenixCabinAnnouncements"/>),
    /// asked when its words are about to be said; null: never muted.</param>
    /// <param name="ownPresses">The Cabin window's door presses; null: a registry of its own, never marked.</param>
    /// <param name="clock">The time <paramref name="ownPresses"/> is asked at; null: <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="type">The loaded airframe when the monitor starts (<see cref="OnVariantChanged"/> follows it).</param>
    /// <param name="readSettings">Reads the Fenix's settings; null: never read, so the layout is the airframe's own.</param>
    internal FenixCabinReadyMonitor(
        Func<Action<string, FenixDataValue>, IDisposable> subscribe,
        SynchronizationContext? ui,
        Action<string> speak,
        Func<string, bool>? isMuted = null,
        FenixCabinOwnPresses? ownPresses = null,
        Func<DateTime>? clock = null,
        FenixAircraftType type = FenixAircraftType.Unknown,
        Func<Task<IReadOnlyList<FenixSetting>>>? readSettings = null)
    {
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(speak);
        _ui = ui;
        _speak = speak;
        _isMuted = isMuted ?? (_ => false);
        _ownPresses = ownPresses ?? new FenixCabinOwnPresses();
        _clock = clock ?? (() => DateTime.UtcNow);
        _readSettings = readSettings;
        _type = type;
        _layout = FenixCabinLayout.Resolve(type, null, null, null);
        // Every field OnValue reads is set before the source can call it.
        _source = subscribe(OnValue);
        _ = ReadSettingsAsync();
    }

    /// <summary>
    /// The loaded airframe changed (the definition's <c>VariantChanged</c>, UI thread): the layout follows at
    /// once, and the settings are read again, since another airframe has its own.
    /// </summary>
    internal void OnVariantChanged(FenixAircraftType type)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _type = type;
            ResolveLayout();
        }
        _ = ReadSettingsAsync();
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
        _detach?.Invoke();
        _source.Dispose();
        Log.Debug(LogCategory, "Cabin monitor stopped");
    }

    // One read of the Fenix's settings, on a pool thread: the layout follows its answer.
    private async Task ReadSettingsAsync()
    {
        if (_readSettings == null) return;
        int read;
        lock (_gate)
        {
            if (_disposed) return;
            read = ++_settingsStarted;
        }
        try
        {
            IReadOnlyList<FenixSetting> settings = await _readSettings().ConfigureAwait(false);
            // An empty answer (the Fenix did not answer) keeps what an earlier read found.
            if (settings.Count == 0) return;
            lock (_gate)
            {
                if (_disposed || read <= _settingsApplied) return;
                _settingsApplied = read;
                _exitsSetting = FenixCabinLayout.SettingValue(settings, FenixCabinLayout.ExitsSettingKey);
                _variantSetting = FenixCabinLayout.SettingValue(settings, FenixCabinLayout.VariantSettingKey);
                _slideMessageSetting = FenixCabinLayout.SettingValue(settings, FenixCabinLayout.SlideMessageSettingKey);
                ResolveLayout();
            }
        }
        catch (Exception ex)
        {
            // GetSettingsAsync answers empty rather than throw; logged in case it ever does.
            Log.Debug(LogCategory, $"Cabin settings read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Under _gate.
    private void ResolveLayout()
        => _layout = FenixCabinLayout.Resolve(_type, _exitsSetting, _variantSetting, _slideMessageSetting);

    // The source's callback, on a pool thread.
    private void OnValue(string name, FenixDataValue value)
    {
        bool rose = false;
        bool startedAnswering = false;
        IReadOnlyList<FenixCabinEvent>? events = null;
        lock (_gate)
        {
            if (_disposed) return;
            _poll.Apply(name, value);
            if (string.Equals(name, FenixCabinNames.CabinReady, StringComparison.Ordinal))
            {
                // The first name of a poll: a known value back after a long outage starts the new baseline
                // before the edge sees it.
                if (value.IsKnown) RebaselineAfterLongOutage(_clock());
                rose = _edge.Next(value);
            }
            if (string.Equals(name, LastPolledName, StringComparison.Ordinal))
            {
                DateTime now = _clock();
                bool answering = _poll.AnyKnown;
                if (answering)
                {
                    RebaselineAfterLongOutage(now);
                    _lastAnsweredAt = now;
                }
                events = WithoutOwnPresses(_events.Next(_poll, _layout), now);
                startedAnswering = answering && !_wasAnswering;
                _wasAnswering = answering;
            }
        }
        if (rose) Post(() => Say(FenixCabinAnnouncements.CabinReadyKey, SpokenText));
        if (events is { Count: > 0 }) Post(() => Say(events));
        if (startedAnswering) _ = ReadSettingsAsync();
    }

    // Under _gate. The Fenix answers again after more than OutageRebaseline without an answer, so what it
    // says now is a new silent baseline for every kind: a fresh edge and fresh events. Once per outage: the
    // answer time is set here, so the rest of the same poll does not start it again.
    private void RebaselineAfterLongOutage(DateTime now)
    {
        if (_wasAnswering || _lastAnsweredAt is not DateTime last || now - last <= OutageRebaseline) return;
        _edge = new FenixCabinReadyEdge();
        _events = new FenixCabinEvents();
        _lastAnsweredAt = now;
    }

    // A door the pilot pressed in the Cabin window is the window's read-back's to say.
    private IReadOnlyList<FenixCabinEvent> WithoutOwnPresses(IReadOnlyList<FenixCabinEvent> events, DateTime now)
    {
        if (events.Count == 0) return events;
        return events.Where(e => e.LineKey == null || !_ownPresses.IsPending(e.LineKey, now)).ToArray();
    }

    // Runs the call on the UI thread, or here without one.
    private void Post(Action call)
    {
        if (_ui == null)
        {
            call();
            return;
        }
        try
        {
            _ui.Post(_ => call(), null);
        }
        catch (InvalidOperationException)
        {
            // The UI thread is gone (the app is closing): there is nothing to speak on.
        }
    }

    private void Say(IReadOnlyList<FenixCabinEvent> events)
    {
        foreach (FenixCabinEvent e in events) Say(e.Kind, e.Text);
    }

    // The mute list is read as the words are said, so a Ctrl+M change counts at once.
    private void Say(string kind, string text)
    {
        if (!_disposed && !_isMuted(kind)) _speak(text);
    }
}
