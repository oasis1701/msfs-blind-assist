using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>One detent of a PMDG speed-brake lever: where the lever's read-back RESTS there, the
/// Control Stand combo's label for it, the SDK click event that moves the lever there, and the
/// sentence the settle announcer (<see cref="PmdgSpeedBrakeCallout"/>) speaks when the lever comes
/// to rest there. <paramref name="Tolerance"/>, when set, replaces the table's settle tolerance for
/// this one detent: ARM is exact on the 737 and the 777 (one step past it their spoilers are already
/// 34 percent up) and on the iFly (its ARMED light comes on at 34, and its spoilers deploy in step
/// with the lever from there).</summary>
public sealed record PmdgLeverDetent(double Value, string Label, string EventName, string? Spoken = null,
    double? Tolerance = null);

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
/// The NG3 SDK has no lever field. TFM's 272 for the flight detent was a P3D value. ARM is exact:
/// measured 2026-09-30 (hydraulics pressurised), the spoilers are down at 100 and 34 / 34 / 37 percent
/// up at 101 / 105 / 137. A hardware axis snaps the lever from Down straight to ARM.</item>
/// <item>777: <c>L:switch_498_a</c> — 0 / 200 / 300 / 400, measured 2026-09-30 (MSFS 2024): the
/// combo's Down / Armed / 50 percent / Up picks land exactly there. It is 4 x the SDK's
/// <c>FCTL_Speedbrake_Lever</c> byte (0 / 50 / 75 / 100), which TRUNCATES: a lever at 201-203 has
/// the spoilers 34 percent up (hydraulics pressurised) yet the byte still reads exactly 50, so it
/// cannot tell armed from extended. ARM is therefore exact (<see cref="PmdgLeverDetent.Tolerance"/>).
/// A hardware axis parks the lever at 22 for Down; the lever never rests between Down and ARM.
/// PMDG_777X_SDK.h's "25: ARMED" is WRONG. The 777 SDK has no flight-detent click event, so it has
/// four detents where the 737 has five.</item>
/// </list>
/// Both read-backs sweep through every value in between while the lever animates.
/// </summary>
public static class PmdgSpeedBrakeLever
{
    public static readonly IReadOnlyList<PmdgLeverDetent> Ng3 = new PmdgLeverDetent[]
    {
        new(0,   "Down",           "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN",    "Speed brake down"),
        // Exact: at 101 the spoilers are already 34 percent up (measured 2026-09-30).
        new(100, "Armed",          "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM",     "Speed brake armed", Tolerance: 0.25),
        new(250, "50 percent",     "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50PCT",   "Speed brake 50 percent"),
        new(337, "Flight detent",  "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_FLT_DET", "Speed brake flight"),
        new(400, "Fully deployed", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP",      "Speed brake fully deployed"),
    };

    /// <summary>The 737's settle announcer treats a lever within this distance of a detent as resting
    /// there (ARM is exact — see its row); further from every detent it speaks the deployment
    /// (<see cref="PartialDeployment"/>).</summary>
    public const double Ng3SettleTolerance = 10.0;

    /// <summary>The 737 settle delay. The L-var rides the 1 Hz continuous batch and the lever travels
    /// for one to two seconds, so a travelling lever is caught between detents: with a percentage
    /// spoken there, the trailing edge must outlast one batch, as the 777's does.</summary>
    public const int Ng3SettleMs = 1500;

    public static readonly IReadOnlyList<PmdgLeverDetent> B777 = new PmdgLeverDetent[]
    {
        new(0,   "Down",       "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", "Speed brake down"),
        // Exact: at 201 the spoilers are already 34 percent up (measured 2026-09-30).
        new(200, "Armed",      "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM",  "Speed brake armed", Tolerance: 0.25),
        new(300, "50 percent", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50",   "Speed brake 50 percent"),
        new(400, "Up (full)",  "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP",   "Speed brake 100 percent"),
    };

    /// <summary>The 777's picks land exactly on their rest values; this absorbs a hardware axis resting
    /// a little off the 50-percent or Up detent. ARM carries its own exact tolerance.</summary>
    public const double B777SettleTolerance = 8.0;

    /// <summary>The 777 settle delay. Its lever L-var rides the 1 Hz continuous batch and the lever
    /// takes about ten seconds end to end, so a travelling lever produces a sample every second: the
    /// trailing edge must outlast one batch or it would be announced mid-sweep.</summary>
    public const int B777SettleMs = 1500;

    /// <summary>How long the 777 lever takes from Down to Up, measured 2026-09-30 (about ten seconds):
    /// the slowest lever this table serves.</summary>
    public const int B777FullTravelMs = 10_000;

    /// <summary>
    /// What is said for a lever resting BETWEEN detents above ARMED (a hardware axis): the deployment
    /// as a percentage of ARMED to UP, measured on <paramref name="detents"/>' own rest values (ARMED
    /// is the second row, UP the last), so a table's 50-percent detent reads the same either way —
    /// "Speed brake 35 percent". Null at or below ARMED and above UP (a lever short of ARMED is spoken
    /// as Down, from <see cref="PositionIndex(IReadOnlyList{PmdgLeverDetent}, double, double)"/>). The
    /// ONE formula: <see cref="PmdgSpeedBrakeCallout"/> applies it to its own table, for every lever.
    /// </summary>
    public static string? PartialDeployment(IReadOnlyList<PmdgLeverDetent> detents, double value)
    {
        double armed = detents[1].Value, up = detents[^1].Value;
        if (value <= armed || value > up) return null;
        // Never "0 percent" past ARM: on the 777 the spoilers are 34 percent up a hair past it.
        int pct = Math.Max(1, (int)Math.Round((value - armed) / (up - armed) * 100));
        return $"Speed brake {pct} percent";
    }

    /// <summary>The combo's ValueDescriptions: each detent's rest value to its label.</summary>
    public static Dictionary<double, string> ComboDescriptions(IReadOnlyList<PmdgLeverDetent> detents)
        => detents.ToDictionary(d => d.Value, d => d.Label);

    /// <summary>
    /// The combo's <c>SimVarDefinition.ValueToDescriptionKey</c>: the rest value of the lever's
    /// <see cref="PositionIndex"/> position, else — past ARM, between detents — of the nearest DEPLOYED
    /// detent, never ARM (the combo is read aloud, and a lever past the exact ARM detent is deployed).
    /// Always a key, never "no match": MainForm's combo lookup is an exact key match, and a combo
    /// opened with nothing selected commits row 0 ("Down") on the pilot's first arrow press — a lever
    /// caught mid-travel, or resting a hair off its detent, would otherwise retract the speed brakes
    /// the moment the pilot touched the control (the MD-11 flap combos' exact-key-seed trap). A tie
    /// between two deployed detents goes to the lower one.
    /// </summary>
    public static double NearestDetentValue(IReadOnlyList<PmdgLeverDetent> detents, double value, double tolerance)
    {
        int idx = PositionIndex(detents, value, tolerance);
        if (idx >= 0) return detents[idx].Value;
        // Past ARM (which is exact) and at no detent: the lever is DEPLOYED, so the nearest deployed
        // detent — never "Armed" (the 777's spoilers are 34 percent up at 201) and never "Down" (the
        // 737 at 101 is nearer Down than 50 percent).
        var best = detents[2];
        for (int i = 3; i < detents.Count; i++)
            if (Math.Abs(value - detents[i].Value) < Math.Abs(value - best.Value)) best = detents[i];
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
            if (Math.Abs(off) <= (d.Tolerance ?? tolerance)) return i;
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
    /// <summary>How long a pick suppresses its own arrival. It must cover the slowest lever's whole
    /// journey to its settle: the 777's full travel (<see cref="PmdgSpeedBrakeLever.B777FullTravelMs"/>),
    /// up to one 1 Hz batch before the arrival is sampled, and the 777 settle — 12.5 s — rounded up,
    /// because the travel is measured only approximately. At 10 s a Down-to-Up pick on the 777 had
    /// expired before its arrival settled, and the arrival was read back over the pilot's pick.</summary>
    public const int PickMemoryMs = 15_000;

    private readonly IReadOnlyList<PmdgLeverDetent> _detents;
    private readonly double _tolerance;
    private readonly int _settleMs;
    private readonly string _muteKey;
    private readonly bool _speakFirst;
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
    /// change (the 737's and 777's L-var batch); true where the initial snapshot never reaches the
    /// announcer (the iFly's SDK).</param>
    /// <param name="picksLandAtOnce">True where a pick's write reads back exactly and at once, with no
    /// travel (the iFly): then ANY settle answers a pending pick, because a lever resting anywhere but
    /// the picked detent has gone somewhere else. False where a settle can fall mid-travel (the 737's
    /// and 777's 1 Hz L-var batch can deliver a travelling lever as its last sample): there only a
    /// lever resting AT a detent answers it.</param>
    public PmdgSpeedBrakeCallout(IReadOnlyList<PmdgLeverDetent> detents, double tolerance, int settleMs,
        string aircraftCode, string muteKey, bool speakFirst, bool picksLandAtOnce = false)
    {
        _detents = detents;
        _tolerance = tolerance;
        _settleMs = settleMs;
        _aircraftCode = aircraftCode;
        _muteKey = muteKey;
        _speakFirst = speakFirst;
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

    /// <summary>
    /// <see cref="Reset"/> with the lever's live position known (a context reset or reconnect whose
    /// opening value never arrives as a sample): that position becomes the one last spoken, so the
    /// lever then arriving there as a change is not news, while the next genuine move is.
    /// </summary>
    public void Seed(double value)
    {
        Reset();
        if (double.IsNaN(value)) return;
        string? text = SentenceFor(PmdgSpeedBrakeLever.PositionIndex(_detents, value, _tolerance), value);
        lock (_lock)
        {
            _lastSpoken = text;
            _hasBaseline = true;
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
        string? text = SentenceFor(idx, value);

        lock (_lock)
        {
            _picks.RemoveAll(p => nowTick - p.Tick > PickMemoryMs);
            // The LATEST pick of this detent: overshooting and coming back (Armed, 50 percent, Armed)
            // rests only once, and that rest answers all three.
            int match = _picks.FindLastIndex(p => p.Index == idx);
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

    // The sentence for a lever at position idx (PositionIndex): the detent's own, else its deployment
    // between detents.
    private string? SentenceFor(int idx, double value)
        => idx >= 0 ? _detents[idx].Spoken : PmdgSpeedBrakeLever.PartialDeployment(_detents, value);
}
