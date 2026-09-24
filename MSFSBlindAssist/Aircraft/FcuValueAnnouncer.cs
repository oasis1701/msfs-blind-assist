using System.Collections.Concurrent;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Decides WHEN a hardware-dialled FCU/MCP selected value is spoken — the FlyByWire jets' half of the
/// PMDG 777's MCP callouts (PR #140). Pure: no SimConnect, no speech, an injected clock. The definition
/// hands it the PHRASE for every delivery of a value var (<see cref="FcuValuePhrases"/>: null while the
/// window shows dashes, <see cref="FcuValuePhrases.Unavailable"/> while the FCU itself is off) and speaks
/// whatever <see cref="OnBatchDelivered"/> releases.
///
/// A change is STAGED, never spoken on delivery: whether it is a knob turn depends on the whole sample
/// (the FCU health var sorts after the value vars in the same batch), so it is judged when the batch has
/// finished dispatching. Comparing PHRASES, not numbers, is what makes dashes an ordinary state.
///
/// Thread use: everything runs on the UI thread except <see cref="SuppressEcho"/> (the A32NX altitude
/// setter arms it from a deferred continuation), so the echo maps are the only concurrent state.
/// Pinned by FcuValueAnnouncerTests.
/// </summary>
internal sealed class FcuValueAnnouncer
{
    /// <summary>How long after MSFSBA itself sets a value its echo is absorbed.</summary>
    internal const long EchoWindowMs = 2500;

    /// <summary>Consecutive first-batch deliveries with no FCU value moving that end a settle once the
    /// aircraft has published since it began. Five, as Md11SeedGate.</summary>
    internal const int SettleQuietDeliveries = 5;

    /// <summary>First-batch deliveries after which a settle ends whatever happened (~30 s).</summary>
    internal const int SettleMaxDeliveries = 30;

    private readonly Dictionary<string, string?> _lastPhrase = new(StringComparer.Ordinal);
    private readonly List<(string Key, string Phrase)> _staged = new();
    private readonly HashSet<string> _seenSinceSettle = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _echoUntilMs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string[]> _echoKeysByEvent = new(StringComparer.Ordinal);

    private bool _settling;
    private bool _refireIsEvidence;
    private bool _publishedSinceReset;
    private bool _movedSinceDelivery;
    private int _quietDeliveries;
    private int _settleDeliveries;
    private bool? _fcuHealthy;
    private bool _healthSeenTrue;

    /// <summary>A settle is still absorbing changes.</summary>
    public bool IsSettling => _settling;

    /// <summary>False once the FCU health var, having read healthy, reads unhealthy.</summary>
    public bool IsFcuAvailable => !(_healthSeenTrue && _fcuHealthy == false);

    /// <summary>
    /// Record <paramref name="phrase"/> as <paramref name="key"/>'s state and stage it when it should be
    /// spoken. The baseline is committed first, whatever the verdict. <paramref name="countsAsLoadEvidence"/>
    /// is false for a value the sim core can write itself (a stock SimVar restored from the flight file).
    /// </summary>
    public void Observe(string key, string? phrase, bool muted, long nowMs, bool countsAsLoadEvidence = true)
    {
        bool seen = _lastPhrase.TryGetValue(key, out string? previous);
        _lastPhrase[key] = phrase;
        bool moved = !seen || !string.Equals(previous, phrase, StringComparison.Ordinal);
        Unstage(key);   // the latest sample of a key replaces anything still staged for it

        if (_settling)
        {
            bool refire = _refireIsEvidence && _seenSinceSettle.Add(key);
            if (moved || refire)
            {
                if (countsAsLoadEvidence && ((seen && moved) || refire)) _publishedSinceReset = true;
                _movedSinceDelivery = true;
            }
            return;
        }

        if (!seen || !moved) return;                                     // baseline / no change
        if (phrase == null || phrase == FcuValuePhrases.Unavailable) return;   // dashes / FCU off: recorded
        if (previous == FcuValuePhrases.Unavailable) { BeginSettle(); return; } // the FCU came back
        if (!IsFcuAvailable || muted) return;
        if (_echoUntilMs.TryGetValue(key, out long until) && nowMs < until) return;
        _staged.Add((key, phrase));
    }

    /// <summary>The FCU health var was delivered (A32NX_FCU_HEALTHY / A32NX_FCU_AFS_CP_ACTIVE).</summary>
    public void ObserveFcuHealth(bool healthy)
    {
        bool cameBack = healthy && _fcuHealthy == false;
        _fcuHealthy = healthy;
        if (healthy) _healthSeenTrue = true;
        if (cameBack) BeginSettle();                     // power-up: absorb the FCU's start-up values
        else if (!IsFcuAvailable) _staged.Clear();       // power-down: this sample is the FCU going dark
    }

    /// <summary>Absorb every change of each named key for <see cref="EchoWindowMs"/>. Name only the keys
    /// the write moves. <paramref name="forEvent"/> remembers which keys that event armed, so
    /// <see cref="RearmEcho"/> can restart the window when a QUEUED event is finally sent.</summary>
    public void SuppressEcho(IEnumerable<string> keys, long nowMs, string? forEvent = null)
    {
        string[] list = keys.ToArray();
        long until = nowMs + EchoWindowMs;
        foreach (string key in list) _echoUntilMs[key] = until;
        if (forEvent != null && list.Length > 0) _echoKeysByEvent[forEvent] = list;
    }

    /// <summary>A queued event has just been sent: restart the echo window it was armed with.</summary>
    public void RearmEcho(string evt, long nowMs)
    {
        if (_echoKeysByEvent.TryGetValue(evt, out string[]? keys)) SuppressEcho(keys, nowMs);
    }

    /// <summary>Record a phrase silently (the words changed but the value did not — MTRS).</summary>
    public void Rebaseline(string key, string? phrase)
    {
        _lastPhrase[key] = phrase;
        Unstage(key);
    }

    /// <summary>What the FCU window for <paramref name="key"/> last showed, for the readouts.</summary>
    public FcuWindowState StateOf(string key)
    {
        if (!IsFcuAvailable) return FcuWindowState.Unavailable;
        if (!_lastPhrase.TryGetValue(key, out string? phrase)) return FcuWindowState.Unknown;
        if (phrase == null) return FcuWindowState.Dashes;
        return phrase == FcuValuePhrases.Unavailable ? FcuWindowState.Unavailable : FcuWindowState.Value;
    }

    /// <summary>
    /// The values about to arrive describe a different situation. Absorb changes until the aircraft has
    /// published and gone quiet. Baselines are KEPT. <paramref name="refireIsEvidence"/>: the variable
    /// cache was cleared (a SimConnect drop), so each key's first delivery since now IS the aircraft
    /// publishing. A plain settle begun while a re-fire settle runs keeps the re-fire rule.
    /// </summary>
    public void BeginSettle(bool refireIsEvidence = false)
    {
        if (refireIsEvidence || !_settling)
        {
            _refireIsEvidence = refireIsEvidence;
            _seenSinceSettle.Clear();
        }
        _settling = true;
        _publishedSinceReset = false;
        _movedSinceDelivery = false;
        _quietDeliveries = 0;
        _settleDeliveries = 0;
        _staged.Clear();
    }

    /// <summary>
    /// A continuous batch finished dispatching: every value it carried, and the health var, are current.
    /// Returns the phrases to speak now (arrival order) and clears them. Only batch 1 counts toward a
    /// settle, so it is measured in samples of the sim, never on a wall clock.
    /// </summary>
    public IReadOnlyList<string> OnBatchDelivered(int batchNum)
    {
        if (_settling)
        {
            if (batchNum == 1) CountSettleDelivery();
            _staged.Clear();
            return Array.Empty<string>();
        }
        if (_staged.Count == 0) return Array.Empty<string>();
        string[] due = IsFcuAvailable ? _staged.Select(s => s.Phrase).ToArray() : Array.Empty<string>();
        _staged.Clear();
        return due;
    }

    private void CountSettleDelivery()
    {
        _settleDeliveries++;
        if (_movedSinceDelivery)
        {
            _movedSinceDelivery = false;
            _quietDeliveries = 0;
        }
        else
        {
            _quietDeliveries++;
        }
        if ((_publishedSinceReset && _quietDeliveries >= SettleQuietDeliveries)
            || _settleDeliveries >= SettleMaxDeliveries)
        {
            _settling = false;
        }
    }

    private void Unstage(string key) => _staged.RemoveAll(s => s.Key == key);
}

/// <summary>What an FCU window last showed, as the announcer recorded it.</summary>
internal enum FcuWindowState { Unknown, Dashes, Value, Unavailable }

/// <summary>
/// The words the FCU hardware-dial announcer speaks for each selected value, or null when the
/// FCU window is not showing a selection. Pinned by FcuValuePhrasesTests.
///
/// ⚠️ Every input here is one that says ON ITS OWN whether the window shows a selection, and it
/// must stay that way. The FCU's plain display values do not: while a window shows dashes, FBW's
/// A320 FCU keeps copying the aircraft's LIVE heading, airspeed (clamped 100-399) and vertical
/// speed into them (FcuComputer: the dashes branches), and pairing them with a separate dashes flag
/// races — the two arrive on different SimConnect deliveries. So heading and speed come from the
/// shim L:vars the FCU writes as -1 while dashed, and V/S and FPA from ARINC429 words that are
/// Normal Operation only while the window shows a selection in that word's own mode.
///
/// Display units throughout (degrees, knots or Mach, feet per minute) — FBW #10855. No SI
/// conversion belongs here. Numbers format in the current culture, as the readouts do, so the dial
/// and the readout hotkey never describe one number two ways.
/// </summary>
internal static class FcuValuePhrases
{
    /// <summary>The phrase a source composes when the FCU itself produces no value — unpowered, failed
    /// or in self-test. Recorded, never spoken: it marks the window unavailable so the FCU coming back
    /// is a power-up (a settle), not a knob turn.</summary>
    public const string Unavailable = "\u0001FCU unavailable";

    /// <summary>A32NX_AUTOPILOT_HEADING_SELECTED: whole degrees; -1 while dashed (or the FCU failed).</summary>
    public static string? Heading(double shim)
    {
        if (shim < 0) return null;
        double degrees = Math.Round(shim) % 360;
        if (degrees == 0) degrees = 0;   // -0 would otherwise format as "-000"
        return $"Heading {degrees:000} degrees";
    }

    /// <summary>A32NX_AUTOPILOT_SPEED_SELECTED: the target itself — Mach below 10, else knots;
    /// -1 while dashed.</summary>
    public static string? Speed(double shim)
    {
        if (shim < 0) return null;
        return shim < 10 ? $"Mach {shim:F2}" : $"Speed {Math.Round(shim):0} knots";
    }

    /// <summary>The FCU altitude, already in the unit the pilot reads it in.</summary>
    public static string Altitude(double value, string unit) => $"Altitude {value:0} {unit}";

    /// <summary>A selected-altitude ARINC429 word, feet. Normal Operation whenever the FCU is
    /// working (the altitude window never shows dashes); a failed FCU publishes empty outputs,
    /// which read as Failure Warning here rather than as "Altitude 0 feet".</summary>
    public static string? AltitudeWord(double arincWord)
    {
        var word = new Arinc429Word(arincWord);
        return word.IsNormalOperation ? Altitude(Math.Round(word.Value), "feet") : null;
    }

    /// <summary>A selected-V/S ARINC429 word, feet per minute, snapped to the FCU's 100-fpm detent.</summary>
    public static string? VerticalSpeed(double arincWord)
    {
        var word = new Arinc429Word(arincWord);
        if (!word.IsNormalOperation) return null;
        double fpm = Math.Round(word.Value / 100.0) * 100.0;
        if (fpm == 0) fpm = 0;   // -0 → 0
        return $"Vertical speed {fpm:0} feet per minute";
    }

    /// <summary>A selected-FPA ARINC429 word, degrees to one decimal.</summary>
    public static string? FlightPathAngle(double arincWord)
    {
        var word = new Arinc429Word(arincWord);
        if (!word.IsNormalOperation) return null;
        double degrees = Math.Round(word.Value, 1);
        if (degrees == 0) degrees = 0;   // -0.0 would otherwise format as "-0.0"
        return $"FPA {degrees:0.0} degrees";
    }
}
