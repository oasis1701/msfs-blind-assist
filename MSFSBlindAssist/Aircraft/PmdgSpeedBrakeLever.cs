using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>One detent of a PMDG speed-brake lever: where the lever's read-back RESTS there, the
/// Control Stand combo's label for it, the SDK click event that moves the lever there, and the
/// sentence the settle announcer (<see cref="PmdgSpeedBrakeCallout"/>) speaks when the lever comes
/// to rest there. <paramref name="NoToleranceBelow"/> makes the settle tolerance one-sided: the lever
/// counts as resting at this detent only at or above <paramref name="Value"/>, never short of it —
/// for a detent whose state the aircraft itself switches exactly at that value (the iFly's ARMED
/// light, measured off at 33 and on at 34).</summary>
public sealed record PmdgLeverDetent(double Value, string Label, string EventName, string? Spoken = null,
    bool NoToleranceBelow = false);

/// <summary>
/// The PMDG 737 NG3 and 777 speed-brake levers, as ONE table per aircraft. Everything that reads or
/// moves a lever goes through it — the Control Stand combo's labels, the value→position classifier
/// behind that combo, the per-detent click dispatch, and both jets' settle announcer — so a detent
/// re-measured once is re-measured everywhere.
///
/// Both levers are moved by the SDK's per-detent click events, committed ONLY with
/// <see cref="PmdgMouseFlags.LeftSingle"/> as the CDA parameter (live-verified on the NG3 2026-07-03:
/// CDA+LEFTSINGLE on _ARM lit the ARMED annunciator while the bare parameter was a silent no-op).
/// The base EVT_CONTROL_STAND_SPEED_BRAKE_LEVER on either aircraft is a DRAG event, never used here.
///
/// Rest values, measured live:
/// <list type="bullet">
/// <item>737: <c>L:switch_679_73X</c> — 0 / 100 / 250 / 337 / 400, verified against the MSFS NG3.
/// The NG3 SDK has no lever field. TFM's 272 for the flight detent was a P3D value.</item>
/// <item>777: <c>FCTL_Speedbrake_Lever</c> — 0 / 50 / 75 / 100, re-measured 2026-09-27 (MSFS 2024,
/// in cruise, lever moved through every stop), exact integers at each.
/// PMDG_777X_SDK.h's "25: ARMED" is WRONG; the lever rests at 50 armed. The 777 SDK has no
/// flight-detent click event, so it has four detents where the 737 has five.</item>
/// </list>
/// Both read-backs sweep through every value in between while the lever animates.
/// </summary>
public static class PmdgSpeedBrakeLever
{
    public static readonly IReadOnlyList<PmdgLeverDetent> Ng3 = new PmdgLeverDetent[]
    {
        new(0,   "Down",           "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN",    "Speed brake down"),
        new(100, "Armed",          "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM",     "Speed brake armed"),
        new(250, "50 percent",     "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50PCT",   "Speed brake 50 percent"),
        new(337, "Flight detent",  "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_FLT_DET", "Speed brake flight"),
        new(400, "Fully deployed", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP",      "Speed brake fully deployed"),
    };

    /// <summary>The 737's settle announcer treats a lever within this distance of a detent as resting
    /// there, and says nothing for a lever that comes to rest further from every detent.</summary>
    public const double Ng3SettleTolerance = 10.0;

    /// <summary>The 737 settle delay: the L-var rides the 1 Hz continuous batch and a resting lever
    /// produces one final change, so a short trailing edge is enough.</summary>
    public const int Ng3SettleMs = 300;

    public static readonly IReadOnlyList<PmdgLeverDetent> B777 = new PmdgLeverDetent[]
    {
        new(0,   "Down",       "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", "Speed brake down"),
        new(50,  "Armed",      "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM",  "Speed brake armed"),
        new(75,  "50 percent", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50",   "Speed brake 50 percent"),
        new(100, "Up (full)",  "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP",   "Speed brake 100 percent"),
    };

    /// <summary>The 777's rest values are exact integers; this only absorbs a CDA byte caught one
    /// step off.</summary>
    public const double B777SettleTolerance = 2.0;

    /// <summary>The 777 settle delay. The CDA is POLLED once a second
    /// (PMDG777DataManager._pollTimer) and a field fires only when it changes, so the trailing
    /// edge must outlast one poll or a lever still travelling would be announced mid-sweep.</summary>
    public const int B777SettleMs = 1500;

    /// <summary>
    /// What the 777 says for a lever resting BETWEEN detents above ARMED (a hardware axis in flight):
    /// the deployment as a percentage of ARMED to UP, measured on the table's own rest values, so the
    /// 50-percent detent reads the same either way. Below ARMED it says nothing, as the 737 does.
    /// </summary>
    public static string? B777PartialDeployment(double value) => PartialDeployment(B777, value);

    /// <summary>
    /// The deployment of a lever resting between detents above ARMED, as a percentage of ARMED to
    /// UP measured on <paramref name="detents"/>' own rest values (ARMED is the second row, UP the
    /// last): "Speed brake 35 percent". Null at or below ARMED and above UP. The ONE formula every
    /// lever that speaks a partial deployment uses (the 777, the iFly 737 MAX).
    /// </summary>
    public static string? PartialDeployment(IReadOnlyList<PmdgLeverDetent> detents, double value)
    {
        double armed = detents[1].Value, up = detents[^1].Value;
        if (value <= armed || value > up) return null;
        int pct = (int)Math.Round((value - armed) / (up - armed) * 100);
        return $"Speed brake {pct} percent";
    }

    /// <summary>The combo's ValueDescriptions: each detent's rest value to its label.</summary>
    public static Dictionary<double, string> ComboDescriptions(IReadOnlyList<PmdgLeverDetent> detents)
        => detents.ToDictionary(d => d.Value, d => d.Label);

    /// <summary>
    /// The combo's <c>SimVarDefinition.ValueToDescriptionKey</c>: the rest value of the lever's
    /// <see cref="PositionIndex"/> position, else of the detent NEAREST the lever. Always a key, never
    /// "no match": MainForm's combo lookup is an exact key match, and a combo opened with nothing
    /// selected commits row 0 ("Down") on the pilot's first arrow press — a lever caught mid-travel,
    /// or resting a hair off its detent, would otherwise retract the speed brakes the moment the pilot
    /// touched the control (the MD-11 flap combos' exact-key-seed trap). A tie between two detents goes
    /// to the lower one.
    /// </summary>
    public static double NearestDetentValue(IReadOnlyList<PmdgLeverDetent> detents, double value, double tolerance)
    {
        int idx = PositionIndex(detents, value, tolerance);
        if (idx >= 0) return detents[idx].Value;
        var best = detents[0];
        foreach (var d in detents)
            if (Math.Abs(value - d.Value) < Math.Abs(value - best.Value)) best = d;
        return best.Value;
    }

    /// <summary>
    /// Where the lever IS, as a detent index: the detent it rests at within
    /// <paramref name="tolerance"/>; DOWN (0) for a lever short of ARMED (the second row) and not
    /// within tolerance of it, because such a lever is not armed; else -1, between detents above ARMED.
    /// The nearest-detent rule alone called a lever resting short of ARMED "Armed" while the spoilers
    /// were not armed and nothing was spoken.
    /// </summary>
    public static int PositionIndex(IReadOnlyList<PmdgLeverDetent> detents, double value, double tolerance)
        => PositionIndex(detents, value, tolerance, out _);

    /// <summary><see cref="PositionIndex(IReadOnlyList{PmdgLeverDetent}, double, double)"/>, also
    /// saying whether the lever rests AT that detent (<paramref name="atDetent"/>) rather than being
    /// read as Down for resting short of ARMED.</summary>
    public static int PositionIndex(IReadOnlyList<PmdgLeverDetent> detents, double value, double tolerance,
        out bool atDetent)
    {
        int idx = SettledIndex(detents, value, tolerance);
        atDetent = idx >= 0;
        if (atDetent) return idx;
        return value < detents[1].Value ? 0 : -1;
    }

    /// <summary>The index of the detent the lever is resting at within <paramref name="tolerance"/>,
    /// or -1 when it rests between detents.</summary>
    public static int SettledIndex(IReadOnlyList<PmdgLeverDetent> detents, double value, double tolerance)
    {
        for (int i = 0; i < detents.Count; i++)
        {
            var d = detents[i];
            double off = value - d.Value;
            if (d.NoToleranceBelow ? off >= 0 && off <= tolerance : Math.Abs(off) <= tolerance) return i;
        }
        return -1;
    }

    /// <summary>The index of the detent a combo pick names (its value IS a detent's rest value), or -1.</summary>
    public static int IndexOfComboValue(IReadOnlyList<PmdgLeverDetent> detents, double value)
        => SettledIndex(detents, value, 0.5);
}

/// <summary>
/// The trailing-edge SETTLE announcer both PMDG jets and the iFly 737 MAX use for their speed-brake lever. The read-back
/// sweeps through every value while the lever animates, so every sample restarts a timer and only
/// the resting position is spoken, once. The timer speaks OUTSIDE MainForm's announcer.Suppressed
/// wrap, so this class applies the two suppressions that wrap would have applied itself:
/// <list type="bullet">
/// <item>the Ctrl+M mute: the lever's row in the aircraft's Ctrl+M list, looked up through
/// <see cref="Services.DefAnnounceMuteSets"/> (the one aircraft→list table MainForm's wrap uses),
/// silences it (the position is still recorded, so unmuting never speaks a stale one);</item>
/// <item>the pilot's own pick: <see cref="RecordPick"/>, called after a click is SENT, marks that
/// detent, and the lever arriving there within <see cref="PickMemoryMs"/> is recorded silently (the
/// screen reader already read the pick, and MainForm's echo window is used up by the first sample
/// of a sweep). A lever that stops anywhere else, or reaches the picked detent only after the memory
/// lapses (a click the aircraft ignored), is announced as usual. Every pick waiting for its arrival
/// is remembered, not only the last: arrowing through the combo sends the next pick before the
/// previous one's arrival has settled.</item>
/// </list>
/// The timer fires on a pool thread, so the settle decision and the speech are POSTED back to the
/// UI thread the samples arrive on: the announcer is not safe to call from a pool thread, and
/// reading <c>Suppressed</c> there raced MainForm toggling it for another variable. A posted settle
/// whose sample a newer one has overtaken does nothing: the newer sample's own settle follows.
/// </summary>
public sealed class PmdgSpeedBrakeCallout : IDisposable
{
    /// <summary>How long a pick suppresses its own arrival. Covers the lever's travel plus the
    /// settle delay on either jet.</summary>
    public const int PickMemoryMs = 10_000;

    private readonly IReadOnlyList<PmdgLeverDetent> _detents;
    private readonly double _tolerance;
    private readonly int _settleMs;
    private readonly string _muteKey;
    private readonly bool _speakFirst;
    private readonly Func<double, string?>? _betweenDetents;
    private readonly string _aircraftCode;
    private readonly bool _picksLandAtOnce;
    private readonly object _lock = new();

    private double _latest = double.NaN;
    private long _latestTick;
    private ScreenReaderAnnouncer? _announcer;
    private SynchronizationContext? _uiContext;
    private System.Threading.Timer? _timer;
    private string? _lastSpoken;
    private bool _hasBaseline;
    // Every sample OnSample has seen, counted: a pick records how many came before it, and a posted
    // settle records which sample it was timed for.
    private long _sampleCount;
    // The picks still waiting for their arrival — more than one: a pilot arrowing through the combo
    // sends a new pick before the previous one's arrival has settled.
    private readonly List<PendingPick> _picks = new();

    private readonly record struct PendingPick(int Index, long Tick, long SamplesBefore);

    /// <param name="aircraftCode">The owning definition's <c>AircraftCode</c>, which picks its
    /// Ctrl+M list in <see cref="Services.DefAnnounceMuteSets"/>.</param>
    /// <param name="speakFirst">False where the first sample is the value at load rather than a
    /// change (the 737's L-var batch); true where the initial snapshot never reaches the announcer
    /// (the 777's CDA and the iFly's SDK).</param>
    /// <param name="picksLandAtOnce">True where a pick's write reads back exactly and at once, with no
    /// travel (the iFly): then ANY settle answers a pending pick, because a lever resting anywhere but
    /// the picked detent has gone somewhere else. False where a settle can fall mid-travel (the 737's
    /// 1 Hz batch settles after 300 ms): there only a lever resting AT a detent answers it.</param>
    public PmdgSpeedBrakeCallout(IReadOnlyList<PmdgLeverDetent> detents, double tolerance, int settleMs,
        string aircraftCode, string muteKey, bool speakFirst, Func<double, string?>? betweenDetents = null,
        bool picksLandAtOnce = false)
    {
        _detents = detents;
        _tolerance = tolerance;
        _settleMs = settleMs;
        _aircraftCode = aircraftCode;
        _muteKey = muteKey;
        _speakFirst = speakFirst;
        _betweenDetents = betweenDetents;
        _picksLandAtOnce = picksLandAtOnce;
    }

    /// <summary>Whether the pilot muted the lever's row in this aircraft's Ctrl+M list.</summary>
    public bool IsMuted(Settings.UserSettings settings)
        => Services.DefAnnounceMuteSets.IsMuted(_aircraftCode, _muteKey, settings);

    /// <summary>A new read-back sample (UI thread): restarts the settle timer.</summary>
    public void OnSample(double value, ScreenReaderAnnouncer announcer)
    {
        NoteSample(value);
        lock (_lock)
        {
            _announcer = announcer;
            _uiContext = SynchronizationContext.Current;
        }
        (_timer ??= new System.Threading.Timer(OnSettle))
            .Change(_settleMs, System.Threading.Timeout.Infinite);
    }

    /// <summary>Records a sample and returns its number (the <c>sample</c> a settle of it passes to
    /// <see cref="Settle"/>). <see cref="OnSample"/>'s bookkeeping without the timer.</summary>
    internal long NoteSample(double value)
    {
        lock (_lock)
        {
            _latest = value;
            _latestTick = Environment.TickCount64;
            return ++_sampleCount;
        }
    }

    /// <summary>
    /// A reconnect or flight load: forget the last sentence, any pending pick and a pending settle,
    /// so the first genuine change afterwards is spoken even when it names the detent last spoken.
    /// </summary>
    public void Reset()
    {
        _timer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        lock (_lock)
        {
            _latest = double.NaN;
            _lastSpoken = null;
            _hasBaseline = false;
            _picks.Clear();
        }
    }

    /// <summary>Stops the settle timer for good (aircraft swap), so it never speaks over the next
    /// aircraft.</summary>
    public void Dispose()
    {
        lock (_lock) _announcer = null;
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>A click to detent <paramref name="index"/> was SENT. Call only after sending.</summary>
    public void RecordPick(int index)
    {
        if (index < 0) return;
        lock (_lock)
            _picks.Add(new PendingPick(index, Environment.TickCount64, _sampleCount));
    }

    // Thread-pool timer callback: hands the settle to the UI thread. An unhandled throw here would
    // crash the process.
    private void OnSettle(object? state)
    {
        try
        {
            SynchronizationContext? ui;
            long sample;
            lock (_lock)
            {
                ui = _uiContext;
                sample = _sampleCount;
            }
            if (ui != null) ui.Post(_ => SettleAndSpeak(sample), null);
            else SettleAndSpeak(sample);  // no UI context (headless): speak where we are
        }
        catch { /* never let a timer callback take down the app */ }
    }

    // Settles the sample the timer was timed for. A newer sample that reached the UI thread first has
    // restarted the timer, and its own settle follows: this one would speak a lever still moving.
    private void SettleAndSpeak(long sample)
    {
        try
        {
            double value;
            ScreenReaderAnnouncer? announcer;
            lock (_lock)
            {
                if (sample != _sampleCount) return;
                // The timer armed for an earlier sample can fire between OnSample counting this one
                // and re-arming it: this sample has not rested yet, and its own settle follows.
                if (Environment.TickCount64 - _latestTick < _settleMs / 2) return;
                value = _latest;
                announcer = _announcer;
            }
            if (announcer == null) return;  // disposed, or no sample yet
            string? say = Settle(value, Environment.TickCount64, IsMuted(Settings.SettingsManager.Current), sample);
            if (say != null) announcer.Announce(say);
        }
        catch { /* a posted callback must not take down the message loop */ }
    }

    /// <summary>
    /// The settle decision, pure apart from this instance's own state: the sentence to speak for a
    /// lever resting at <paramref name="value"/>, or null. Records the resting position either way.
    /// <paramref name="sample"/> is which sample (counted by <see cref="OnSample"/>) is settling; a
    /// pick sent after it arrived is not answered by it. The default is the newest sample there is.
    /// </summary>
    public string? Settle(double value, long nowTick, bool muted, long sample = long.MaxValue)
    {
        if (double.IsNaN(value)) return null;
        int idx = PmdgSpeedBrakeLever.PositionIndex(_detents, value, _tolerance, out bool atDetent);
        string? text = idx >= 0 ? _detents[idx].Spoken : _betweenDetents?.Invoke(value);

        lock (_lock)
        {
            _picks.RemoveAll(p => nowTick - p.Tick > PickMemoryMs);
            int match = _picks.FindIndex(p => p.Index == idx);
            bool picked = match >= 0;
            // A lever resting AT a detent answers picks. Anywhere else it may still be travelling (a
            // mid-travel 737 sample short of ARM reads as Down), unless picks land at once. Arriving
            // at a picked detent answers that pick and the ones sent before it, which it superseded,
            // and nothing after it: the 737's 1 Hz batch can catch the lever AT one pick on its way
            // to the next. Resting at an unpicked detent answers every pick sent before this sample
            // arrived (the lever went somewhere else); one sent after it waits for its own arrival.
            if (atDetent || _picksLandAtOnce)
            {
                if (picked) _picks.RemoveRange(0, match + 1);
                else _picks.RemoveAll(p => p.SamplesBefore < sample);
            }
            if (text == null) return null;  // resting between detents with nothing to say

            bool first = !_hasBaseline;
            _hasBaseline = true;
            if (text == _lastSpoken) return null;
            _lastSpoken = text;
            if (picked || muted || (first && !_speakFirst)) return null;
            return text;
        }
    }
}
