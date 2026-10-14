using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// What the A300 says on its own about its lights, levers and FCU altitude window: the master warning
/// and master caution as they come on, the fault and warning lights both ways, the flap handle,
/// ground spoiler arm, gear lever and parking brake when something other than the pilot's own pick
/// moves them (<see cref="A300Announcements"/>), and the altitude window when MSFSBA did not set it
/// (<see cref="A300FcuWindows"/>). Each is decided in ProcessSimVarUpdate, inside MainForm's wrap,
/// which applies both the Ctrl+M mute (<see cref="Settings.UserSettings.A300DisabledMonitorVariablesSet"/>)
/// and the UI echo, so the pilot's own lever pick is never spoken back. The fault lights are SPOKEN
/// later, when the next continuous batch ends (one sentence for every light changed since), so each
/// change is kept only when the announcer was not suppressed at the moment it was decided. Every other switch
/// position is consumed silently: the panel combo follows it.
/// </summary>
public partial class IniA300Definition
{
    private readonly A300AnnouncementTracker _tracker = new();

    /// <summary>The FCU altitude window's call-out (<see cref="A300FcuWindows"/>).</summary>
    private readonly A300WindowTracker _altitudeWindow = new();

    /// <summary>The fault and autobrake lights as the cockpit shows them ([A300-23]).</summary>
    private readonly A300LampBoard _lampBoard = new();

    /// <summary>The light changes waiting to be spoken: at a batch end, netted, once a power change has
    /// settled ([A300-24]).</summary>
    private readonly A300LampSpeech _lampSpeech = new();
    private ScreenReaderAnnouncer? _lampAnnouncer;

    /// <summary>A variable that changes several lights (a light power flag, the autobrake level or a DECEL
    /// light) checks each light's own Ctrl+M row itself, so MainForm must not wrap it in its own (VAR-8).</summary>
    public override bool IsMuteWrapExempt(string varName) => A300LampBoard.SharedInputKeys.Contains(varName);

    /// <summary>
    /// WHEN, after a context reset, the lights and levers a flight load left unchanged get their
    /// baselines from the cache (a load re-delivers only changed values): on the batch deliveries'
    /// evidence, never a wall clock. The MD-11's gate, reused as it stands.
    /// </summary>
    private readonly Md11SeedGate _seedGate = new();

    /// <summary>
    /// Whether a change of <paramref name="key"/> proves the aircraft's own module has republished after
    /// a context reset: only its own L:vars do, as for the MD-11 (<see cref="Md11SeedGate"/>). The sim core
    /// writes the stock SimVars from the flight file before that module runs (a light's stock input such as
    /// external power or a gear position, the flap handle index), so a change of one restarts the quiet
    /// count but never releases the seed. Every <see cref="Md11SeedGate.NoteValue"/> call asks this.
    /// </summary>
    internal bool IsAircraftOwned(string key) =>
        GetVariables().TryGetValue(key, out var def) && def.Type == SimVarType.LVar;

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        NotePickReached(varName, value);   // a picked switch got there (CheckMovedAsync)

        // First: the take-off call-outs peek SIM_ON_GROUND, which the base may consume.
        if (TryHandleTakeoffCallouts(varName, value, announcer))
            return true;
        if (base.ProcessSimVarUpdate(varName, value, announcer))
            return true;

        // An FCU button's lamp: shown on the button's label (TryDescribeControlState), never spoken.
        if (A300FcuState.LightKeys.Contains(varName))
            return true;

        // Ctrl+B's STD flags: shown on its buttons, never spoken.
        if (A300Baro.ModeKeys.Contains(varName))
            return true;

        // The fault lights and the autobrake lights, as the cockpit shows them ([A300-23]): a change is
        // kept only when it would be heard now, against each light's own Ctrl+M row, because a light
        // power flag or the autobrake level changes several lights at once (IsMuteWrapExempt).
        if (_lampBoard.Handles(varName))
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: IsAircraftOwned(varName));
            if (_lampBoard.PowerFlips(varName, value))
                _lampSpeech.NotePowerChange(Clock());
            foreach (var change in _lampBoard.Update(varName, value))
            {
                if (announcer.Suppressed || IsMuted(change.Lamp.MuteKey) || IsPressedButtonsLamp(change.Lamp.Id))
                {
                    // Not spoken, but the light has moved on from its earlier change, still held or pending: drop it,
                    // or it is spoken later although wrong ([A300-24]).
                    _lampSpeech.Forget(change.Lamp.Name);
                    continue;
                }
                _lampSpeech.Add(new A300LampChange(change.Lamp.Name, change.On), Clock());
                _lampAnnouncer = announcer;
            }
            return true;
        }

        // The TRP: shown on its buttons' labels and the Thrust Rating Panel's TRP line, never spoken.
        if (A300Trp.StateKeys.Contains(varName))
            return true;

        // The subscriptions only a label, a box line or a refusal reads (the clocks' digits and button states, the
        // rudder trim display, the radio windows, the cargo door, the memos, the MCDU lights, the IRS flags, the ILS
        // inputs): composed from, never spoken. OwnSubscription adds each key as BuildVariables registers it, and an
        // input that reads another key's cache (_inputAliases) is not registered. The set also holds the AC and DC light
        // power flags, the panel lamps' shared inputs and the TRP's keys, which the light board and TRP branches above have
        // already consumed: this check stays after them.
        if (SilentKeys.Contains(varName))
            return true;

        // The two master lights, the four levers and the SAS levers (the fault lights went to the board
        // above). Before the FMA sources: the pitch trim levers are both, and still feed the FMA from the cache.
        if (A300Announcements.AnnouncedKeys.Contains(varName))
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: IsAircraftOwned(varName));
            if (_tracker.Observe(varName, value) is string phrase)
                announcer.Announce(phrase);
            return true;
        }

        // An FMA source (some are switch rows too): read when its batch has finished dispatching
        // (OnDeferredFlushBatchDelivered), never spoken here.
        if (A300FmaSources.Keys.Contains(varName))
            return true;

        // The FCU altitude window: a change MSFSBA did not make ("Altitude 12,000 feet"). Spoken from
        // here, inside MainForm's mute wrap; MSFSBA's own typed value or knob step is its echo.
        if (varName == A300Readouts.AltitudeKey)
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: IsAircraftOwned(varName));
            if (_altitudeWindow.Observe(A300FcuWindows.Altitude(value), Clock()) is string window)
                announcer.Announce(window);
            return true;
        }

        // A position: the open control follows it (RefreshControlWhenDefHandled). Readouts and the
        // probe are not rows, so they go on to MainForm's display path.
        return _rows.ContainsKey(varName);
    }

    public override void OnContinuousBatchDelivered(int batchNum)
    {
        base.OnContinuousBatchDelivered(batchNum);
        FlushLamps();
        var sim = _sim;
        if (!_seedGate.Armed || sim == null)
            return;
        var trigger = _seedGate.OnBatchDelivered(batchNum, sim.ActiveContinuousBatches, Clock());
        if (trigger != Md11SeedTrigger.None)
        {
            OnLoadSettled();
            SeedFromCache(sim, trigger);
        }
    }

    /// <summary>
    /// The post-load seed gate has released (the load has settled, or its ceiling passed). The MCDU export sent
    /// before that may have landed ahead of the aircraft's own start-up, which can switch the option off again, so
    /// it is no longer known to be on and the open MCDU window's next poll sends it once more ([A300-4]; one extra
    /// write per flight load while the window is open).
    /// </summary>
    internal void OnLoadSettled() => _mcduExportOn = false;

    /// <summary>
    /// A disconnect or a flight load: every baseline goes, so the next delivery of each light and
    /// lever is silent, and the seed gate arms to fill in the ones a load leaves unchanged.
    /// </summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _tracker.Reset();
        _fmaTracker.Reset();
        _engagementTracker.Reset();
        _altitudeWindow.Reset();
        foreach (var entry in _vSpeedEntries.Values)
            entry.Reset();
        _takeoffCallouts.Reset();
        _lampBoard.Reset();
        _lampSpeech.Clear();
        _commanded.Clear();
        _pendingPicks.Clear();
        _memoTracker.Reset();
        _memoLine = null;
        _mcduExportOn = false;   // the MCDU export option is gone after a flight load and unknown after a disconnect ([A300-4])
        _seedGate.Arm(KnownSeedValues());
    }

    /// <summary>Speaks the lights changed since the last sentence, netted, as one sentence, unless a power
    /// change is still settling (<see cref="A300LampSpeech"/>).</summary>
    private void FlushLamps()
    {
        if (_lampSpeech.Flush(Clock()) is not string text)
            return;
        if (!_disposed)
            _lampAnnouncer?.Announce(text);
    }

    private IEnumerable<KeyValuePair<string, double>> KnownSeedValues()
    {
        var sim = _sim;
        if (sim == null)
            yield break;
        foreach (var key in A300Announcements.AnnouncedKeys.Union(A300LampBoard.InputKeys).Append(A300Readouts.AltitudeKey)
                     .Concat(_vSpeedEntries.Keys))
            if (Cached(sim, key) is double value)
                yield return new KeyValuePair<string, double>(key, value);
    }

    private void SeedFromCache(SimConnectManager sim, Md11SeedTrigger trigger)
    {
        int seeded = 0;
        foreach (var key in A300Announcements.AnnouncedKeys)
            if (Cached(sim, key) is double value && _tracker.Seed(key, value))
                seeded++;
        foreach (var key in A300LampBoard.InputKeys)
            if (Cached(sim, key) is double value && _lampBoard.Seed(key, value))
                seeded++;
        if (Cached(sim, A300Readouts.AltitudeKey) is double altitude && _altitudeWindow.Seed(A300FcuWindows.Altitude(altitude)))
            seeded++;
        foreach (var (key, entry) in _vSpeedEntries)
            if (Cached(sim, key) is double speed && entry.Seed(A300TakeoffCallouts.Entry(key, speed)))
                seeded++;
        Log.Debug("A300", $"Context reset: {seeded} baselines seeded from the cache ({trigger}, {_seedGate.Deliveries} batch deliveries).");
    }
}
