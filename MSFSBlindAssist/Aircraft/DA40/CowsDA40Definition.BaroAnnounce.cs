using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Announcing a barometric subscale that somebody else moved.
///
/// Both altimeters were SILENT on an external change. The G1000 one was in the
/// silent-readout set outright, and the standby one was reading zero for an unrelated
/// reason — so a pilot turning a knob on real hardware, or an add-on setting QNH, got
/// nothing. On an aeroplane with TWO subscales that must agree, and where a wrong one is
/// worth hundreds of feet, that is the change most worth hearing.
///
/// WHY IT IS DEBOUNCED. A subscale steps 0.01 inHg at a time and a knob sweeps dozens of
/// steps a second. Announcing each one is not a readout, it is a machine gun, and it would
/// bury the value the pilot actually stopped on. So a change only ARMS a timer, every
/// further change restarts it, and the announcement is made once the knob has been still
/// for <see cref="BaroSettleMs"/>. What gets spoken is where it ended up, which is the only
/// number that was ever of interest.
///
/// WHY IT DEFERS TO MSFSBA'S OWN WRITES. Ctrl+B and the standby panel's field already
/// confirm what they set, and the settle timer would then say it again a moment later.
/// Anything set from inside MSFSBA marks the clock, and a settle landing inside
/// <see cref="OwnWriteGraceMs"/> of that is dropped. The grace window is deliberately
/// longer than the settle: a set writes both subscales, so the second one's change can
/// arrive well after the first.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>How still the knob must be before the value is spoken.</summary>
    /// <summary>
    /// ⚠️ 700 ms WAS TOO LONG AND WAS REPORTED AS A LATE READOUT TWICE. The settle exists so a
    /// swept knob speaks once instead of forty times, and it still does: a G1000 subscale knob
    /// steps many times a SECOND, so consecutive steps arrive far closer together than 300 ms
    /// and coalesce exactly as before. What 700 ms also did was put most of a second of silence
    /// after a SINGLE nudge — the case where the pilot has already stopped and is waiting for
    /// the number — and that silence is indistinguishable from a dead key.
    ///
    /// The number is the gap between STEPS, not a speech delay, so shortening it costs nothing
    /// on a sweep and buys back the whole delay on a single press.
    /// </summary>
    // ⚠️ A SETTLE MUST OUTLAST THE SAMPLE PERIOD OF WHAT FEEDS IT. On the 1 Hz batch that
    // meant 1200 ms, because a shorter one expired between deliveries and announced every
    // intermediate setting the knob passed through - and it left the read-back a beat behind.
    //
    // FAST-SAMPLED: both subscales now run on per-var SIM_FRAME subscriptions
    // (ExcludeFromBatch + HighFrequency), so a moving value arrives within a frame and 250 ms
    // is comfortably longer than the gap between two deliveries.
    private const int BaroSettleMs = 250;

    /// <summary>How long after MSFSBA's own write to stay quiet about a subscale.</summary>
    private const int OwnWriteGraceMs = 3000;

    private System.Windows.Forms.Timer? _baroSettleTimer;
    private ScreenReaderAnnouncer? _baroAnnouncer;
    private DateTime _baroOwnWriteAt = DateTime.MinValue;

    private readonly Dictionary<string, double> _baroPending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _baroSpoken = new(StringComparer.Ordinal);

    /// <summary>
    /// Called by every path inside MSFSBA that sets a subscale, so the settle timer knows
    /// the change was ours and stays quiet.
    /// </summary>
    private void MarkBaroSetByUs() => _baroOwnWriteAt = DateTime.UtcNow;

    /// <summary>
    /// Records a subscale change and arms the settle timer. Returns true when the key was
    /// one of the two, so the caller can treat it as handled and keep the generic
    /// announcer out of it.
    /// </summary>
    private bool NoteBaroChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_G1000_BARO" && varKey != "DA40_STBY_ALTIMETER_SET") return false;

        _baroAnnouncer = announcer;

        // A first reading is not a change. Without this the very first batch after a
        // connect announces both subscales at a pilot who did nothing.
        bool known = _baroSpoken.ContainsKey(varKey);
        if (!known)
        {
            _baroSpoken[varKey] = value;
            return true;
        }

        // Below the display's own resolution there is nothing to say. The subscale steps
        // 0.01 inHg, so anything smaller is float noise on the wire rather than a turn.
        if (Math.Abs(value - _baroSpoken[varKey]) < 0.005) return true;

        _baroPending[varKey] = value;
        RestartBaroSettle();
        return true;
    }

    private void RestartBaroSettle()
    {
        if (_baroSettleTimer == null)
        {
            _baroSettleTimer = new System.Windows.Forms.Timer { Interval = BaroSettleMs };
            _baroSettleTimer.Tick += (_, _) => FlushBaroSettle();
        }

        // Stop-then-start is what makes it a SETTLE rather than a repeat: each further
        // step pushes the announcement back, so only the value the knob rests on is read.
        _baroSettleTimer.Stop();
        _baroSettleTimer.Start();
    }

    private void FlushBaroSettle()
    {
        _baroSettleTimer?.Stop();

        var pending = new Dictionary<string, double>(_baroPending);
        _baroPending.Clear();
        if (pending.Count == 0) return;

        bool ours = (DateTime.UtcNow - _baroOwnWriteAt).TotalMilliseconds < OwnWriteGraceMs;
        foreach (var kv in pending) _baroSpoken[kv.Key] = kv.Value;
        if (ours || _baroAnnouncer == null) return;

        // ⚠️ THE Ctrl+M MUTE IS CHECKED HERE, not left to the caller. This speaks from a
        // TIMER, and the generic monitor gate only wraps ProcessSimVarUpdate - a callback
        // outside that wrap would keep talking after the pilot un-ticked the row. Same
        // rule the A32NX's deferred armed-altitude flush follows.
        var muted = Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet;
        foreach (var key in new[] { "DA40_G1000_BARO", "DA40_STBY_ALTIMETER_SET" })
            if (muted.Contains(key)) pending.Remove(key);
        if (pending.Count == 0) return;

        // Both in one utterance when both moved. They are set together far more often than
        // separately, and two announcements a breath apart is how a pilot loses the first.
        var parts = new List<string>();
        if (pending.TryGetValue("DA40_G1000_BARO", out double g))
            parts.Add("Altimeter " + BaroPhrase(g));
        if (pending.TryGetValue("DA40_STBY_ALTIMETER_SET", out double s))
            parts.Add("Standby " + BaroPhrase(s));

        // ⚠️ IMMEDIATE, NOT QUEUED. Announce() hands NVDA the line with interrupt=false,
        // so it waits behind everything already speaking - and the moment a pilot turns
        // the barometric knob is exactly the moment the queue is busy, because they are
        // working the PFD window and it is reading itself back. Reported from the cockpit
        // as the setting arriving "very late": it was not late, it was in a queue.
        //
        // Interrupting is right here. This fires 700 ms after the pilot STOPPED turning,
        // so it is the number they are sitting waiting for.
        if (parts.Count > 0) _baroAnnouncer.AnnounceImmediate(string.Join(". ", parts));
    }

    /// <summary>Stops the settle timer. Called when the aircraft is switched away.</summary>
    private void StopBaroAnnounce()
    {
        try { _baroSettleTimer?.Stop(); _baroSettleTimer?.Dispose(); } catch { }
        _baroSettleTimer = null;
        _baroPending.Clear();
        _baroSpoken.Clear();
        _baroAnnouncer = null;
    }
}
