using System.Collections.Concurrent;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Decides WHEN a hardware-dialled FCU/MCP selected value is spoken — the FlyByWire jets' half of
/// the PMDG 777's MCP callouts (PR #140). Pure: no SimConnect, no speech, an injected clock. The
/// definition hands it the PHRASE for every delivery of a value var (null when the window shows
/// dashes or the value is not a selection, see <see cref="FcuValuePhrases"/>) and speaks whatever
/// <see cref="Observe"/> returns. Pinned by FcuValueAnnouncerTests.
///
/// Comparing PHRASES, not raw numbers, is what makes a dashed window an ordinary state: null is
/// recorded like any other phrase, so the value reappearing on a pull is a change even when it
/// equals the last selection, and a key first seen dashed still speaks its first selection.
///
/// Thread use: <see cref="Observe"/>, <see cref="BeginSettle"/> and <see cref="OnBatchDelivered"/>
/// run on the UI thread (ProcessSimVarUpdate and the two IAircraftDefinition hooks).
/// <see cref="SuppressEcho"/> does not — the A32NX altitude setter arms it from a deferred
/// continuation — so the echo deadlines are the one concurrent map.
/// </summary>
internal sealed class FcuValueAnnouncer
{
    /// <summary>How long after MSFSBA itself sets a value its echo is absorbed. The set method
    /// speaks its own confirmation, so the change arriving back from the sim must not repeat it.</summary>
    internal const long EchoWindowMs = 2500;

    /// <summary>Consecutive first-batch deliveries with no FCU value moving that end a settle once
    /// the aircraft has published since the reset. Five, as the MD-11's Md11SeedGate: an FBW load
    /// can publish its FCU values in more than one burst.</summary>
    internal const int SettleQuietDeliveries = 5;

    /// <summary>First-batch deliveries after which a settle ends whatever happened — a load that
    /// leaves every FCU value where it was never produces the change the quiet rule waits for.
    /// About thirty seconds, the MD-11 gate's ceiling: batches keep arriving with the OLD values
    /// while a flight loads (the MD-11 measured its first publish 8.5 s after AircraftLoaded), so
    /// this is the one bound that behaves like a clock, and it must outlast a slow load.</summary>
    internal const int SettleMaxDeliveries = 30;

    private readonly Dictionary<string, string?> _lastPhrase = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _echoUntilMs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string[]> _echoKeysByEvent = new(StringComparer.Ordinal);

    private bool _settling;
    private bool _publishedSinceReset;
    private bool _movedSinceDelivery;
    private int _quietDeliveries;
    private int _settleDeliveries;

    /// <summary>A context reset's settle is still absorbing changes.</summary>
    public bool IsSettling => _settling;

    /// <summary>
    /// Record <paramref name="phrase"/> as <paramref name="key"/>'s current state and return it
    /// when it should be spoken now; null otherwise. The baseline is committed FIRST, whatever the
    /// verdict, so a muted, echoed or settling change is absorbed rather than spoken late.
    /// <paramref name="countsAsLoadEvidence"/> is false for a value the sim core can write itself
    /// (a stock SimVar restored from the flight file before the aircraft's WASM has run): its
    /// moving still restarts a settle's quiet count, but is no proof the aircraft has published.
    /// </summary>
    public string? Observe(string key, string? phrase, bool muted, long nowMs, bool countsAsLoadEvidence = true)
    {
        bool seen = _lastPhrase.TryGetValue(key, out string? previous);
        _lastPhrase[key] = phrase;
        bool moved = !seen || !string.Equals(previous, phrase, StringComparison.Ordinal);

        if (_settling)
        {
            if (moved)
            {
                if (countsAsLoadEvidence) _publishedSinceReset = true;
                _movedSinceDelivery = true;
            }
            return null;
        }

        if (!seen || !moved) return null;          // first sample is the baseline; no change
        if (phrase == null) return null;           // dashes: recorded, nothing to say
        if (muted) return null;
        if (_echoUntilMs.TryGetValue(key, out long until) && nowMs < until) return null;
        return phrase;
    }

    /// <summary>Absorb every change of each named key for <see cref="EchoWindowMs"/> (not just the next
    /// one). Name only the keys the write moves. <paramref name="forEvent"/> remembers which keys that
    /// event armed, so <see cref="RearmEcho"/> can restart the window when a QUEUED event is finally sent.</summary>
    public void SuppressEcho(IEnumerable<string> keys, long nowMs, string? forEvent = null)
    {
        string[] list = keys.ToArray();
        long until = nowMs + EchoWindowMs;
        foreach (string key in list) _echoUntilMs[key] = until;
        if (forEvent != null && list.Length > 0) _echoKeysByEvent[forEvent] = list;
    }

    /// <summary>An event queued while the calc-path probe was running has just been sent: restart the echo
    /// window it was armed with (none if it never armed one).</summary>
    public void RearmEcho(string evt, long nowMs)
    {
        if (_echoKeysByEvent.TryGetValue(evt, out string[]? keys)) SuppressEcho(keys, nowMs);
    }

    /// <summary>
    /// The values about to arrive describe a different situation — a flight load or a reconnect
    /// (IAircraftDefinition.OnSimContextReset). Absorb changes until the aircraft has published
    /// and gone quiet. The baselines are KEPT: a value the load leaves alone is never re-delivered,
    /// and a wiped baseline would take the pilot's first turn of that knob as its silent seed.
    /// </summary>
    public void BeginSettle()
    {
        _settling = true;
        _publishedSinceReset = false;
        _movedSinceDelivery = false;
        _quietDeliveries = 0;
        _settleDeliveries = 0;
    }

    /// <summary>
    /// A continuous batch finished dispatching (IAircraftDefinition.OnContinuousBatchDelivered).
    /// Only batch 1 is counted, so the settle is measured in samples of the sim, not in however
    /// many batches an aircraft happens to need — and never on a wall clock, which a loading
    /// screen would run out.
    /// </summary>
    public void OnBatchDelivered(int batchNum)
    {
        if (!_settling || batchNum != 1) return;

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
}

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
