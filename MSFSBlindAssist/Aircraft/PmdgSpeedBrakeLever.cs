using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>One detent of a PMDG speed-brake lever: where the lever's read-back RESTS there, the
/// Control Stand combo's label for it, the SDK click event that moves the lever there, and the
/// sentence the settle announcer (<see cref="PmdgSpeedBrakeCallout"/>) speaks when the lever comes
/// to rest there.</summary>
public sealed record PmdgLeverDetent(double Value, string Label, string EventName, string? Spoken = null);

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
    public static string? B777PartialDeployment(double value)
    {
        double armed = B777[1].Value, up = B777[^1].Value;
        if (value <= armed || value > up) return null;
        int pct = (int)Math.Round((value - armed) / (up - armed) * 100);
        return $"Speed brake {pct} percent";
    }

    /// <summary>The combo's ValueDescriptions: each detent's rest value to its label.</summary>
    public static Dictionary<double, string> ComboDescriptions(IReadOnlyList<PmdgLeverDetent> detents)
        => detents.ToDictionary(d => d.Value, d => d.Label);

    /// <summary>
    /// The combo's <c>SimVarDefinition.ValueToDescriptionKey</c>: the rest value of the detent NEAREST
    /// the lever. Always a key, never "no match": MainForm's combo lookup is an exact key match, and a
    /// combo opened with nothing selected commits row 0 ("Down") on the pilot's first arrow press — a
    /// lever caught mid-travel, or resting a hair off its detent, would otherwise retract the speed
    /// brakes the moment the pilot touched the control (the MD-11 flap combos' exact-key-seed trap).
    /// A tie between two detents goes to the lower one.
    /// </summary>
    public static double NearestDetentValue(IReadOnlyList<PmdgLeverDetent> detents, double value)
    {
        var best = detents[0];
        foreach (var d in detents)
            if (Math.Abs(value - d.Value) < Math.Abs(value - best.Value)) best = d;
        return best.Value;
    }

    /// <summary>The index of the detent the lever is resting at within <paramref name="tolerance"/>,
    /// or -1 when it rests between detents.</summary>
    public static int SettledIndex(IReadOnlyList<PmdgLeverDetent> detents, double value, double tolerance)
    {
        for (int i = 0; i < detents.Count; i++)
            if (Math.Abs(value - detents[i].Value) <= tolerance) return i;
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
/// <item>the Ctrl+M mute: the lever's row in the aircraft's disabled-monitor set
/// (<c>PMDGDisabledMonitorVariablesSet</c> unless the constructor names another) silences it
/// (the position is still recorded, so unmuting never speaks a stale one);</item>
/// <item>the pilot's own pick: <see cref="RecordPick"/>, called after a click is SENT, marks that
/// detent, and the lever arriving there within <see cref="PickMemoryMs"/> is recorded silently (the
/// screen reader already read the pick, and MainForm's echo window is used up by the first sample
/// of a sweep). A lever that stops anywhere else, or reaches the picked detent only after the memory
/// lapses (a click the aircraft ignored), is announced as usual.</item>
/// </list>
/// </summary>
public sealed class PmdgSpeedBrakeCallout
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
    private readonly Func<Settings.UserSettings, HashSet<string>> _muteSet;
    private readonly object _lock = new();

    private double _latest = double.NaN;
    private ScreenReaderAnnouncer? _announcer;
    private System.Threading.Timer? _timer;
    private string? _lastSpoken;
    private bool _hasBaseline;
    private int _pickIndex = -1;
    private long _pickTick;

    /// <param name="speakFirst">False where the first sample is the value at load rather than a
    /// change (the 737's L-var batch); true where the initial snapshot never reaches the announcer
    /// (the 777's CDA and the iFly's SDK).</param>
    /// <param name="muteSet">The aircraft's Ctrl+M disabled-monitor set; the PMDG one when null.</param>
    public PmdgSpeedBrakeCallout(IReadOnlyList<PmdgLeverDetent> detents, double tolerance, int settleMs,
        string muteKey, bool speakFirst, Func<double, string?>? betweenDetents = null,
        Func<Settings.UserSettings, HashSet<string>>? muteSet = null)
    {
        _muteSet = muteSet ?? (s => s.PMDGDisabledMonitorVariablesSet);
        _detents = detents;
        _tolerance = tolerance;
        _settleMs = settleMs;
        _muteKey = muteKey;
        _speakFirst = speakFirst;
        _betweenDetents = betweenDetents;
    }

    /// <summary>A new read-back sample (UI thread): restarts the settle timer.</summary>
    public void OnSample(double value, ScreenReaderAnnouncer announcer)
    {
        lock (_lock)
        {
            _latest = value;
            _announcer = announcer;
        }
        (_timer ??= new System.Threading.Timer(OnSettle))
            .Change(_settleMs, System.Threading.Timeout.Infinite);
    }

    /// <summary>A click to detent <paramref name="index"/> was SENT. Call only after sending.</summary>
    public void RecordPick(int index)
    {
        lock (_lock)
        {
            _pickIndex = index;
            _pickTick = Environment.TickCount64;
        }
    }

    // Thread-pool timer callback: an unhandled throw here would crash the process.
    private void OnSettle(object? state)
    {
        try
        {
            double value;
            ScreenReaderAnnouncer? announcer;
            lock (_lock)
            {
                value = _latest;
                announcer = _announcer;
            }
            if (announcer == null) return;
            bool muted = _muteSet(Settings.SettingsManager.Current).Contains(_muteKey);
            string? say = Settle(value, Environment.TickCount64, muted);
            if (say != null) announcer.Announce(say);
        }
        catch { /* never let a timer callback take down the app */ }
    }

    /// <summary>
    /// The settle decision, pure apart from this instance's own state: the sentence to speak for a
    /// lever resting at <paramref name="value"/>, or null. Records the resting position either way.
    /// </summary>
    public string? Settle(double value, long nowTick, bool muted)
    {
        if (double.IsNaN(value)) return null;
        int idx = PmdgSpeedBrakeLever.SettledIndex(_detents, value, _tolerance);
        string? text = idx >= 0 ? _detents[idx].Spoken : _betweenDetents?.Invoke(value);
        if (text == null) return null;  // resting between detents with nothing to say

        lock (_lock)
        {
            bool picked = _pickIndex >= 0 && idx == _pickIndex && nowTick - _pickTick <= PickMemoryMs;
            // A lever resting at ANY detent answers the pick; between detents it may still be travelling.
            if (idx >= 0) _pickIndex = -1;

            bool first = !_hasBaseline;
            _hasBaseline = true;
            if (text == _lastSpoken) return null;
            _lastSpoken = text;
            if (picked || muted || (first && !_speakFirst)) return null;
            return text;
        }
    }
}
