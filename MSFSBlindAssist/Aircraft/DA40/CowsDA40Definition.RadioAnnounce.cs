using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Announcing a radio frequency that somebody else changed, and saying what a swap
/// ACTUALLY did rather than what it was expected to do.
///
/// FOUR separate faults were reported together here and they had four separate causes.
///
/// 1. A frequency set from OUTSIDE MSFSBA said nothing. SayIntentions tuning COM 1, or the
///    pilot tuning it on the G1000 itself, changed the radio in silence, because the
///    active and standby readouts were <c>OnRequest</c> and <c>IsAnnounced = false</c> -
///    they were never polled and never spoken.
///
/// 2. A second swap in a row did not swap. <c>AnnounceSwap</c> fired the event through
///    <c>ExecuteCalculatorCode</c>, and two consecutive swaps of one radio are two
///    BYTE-IDENTICAL calculator strings, which MobiFlight coalesces - it drops the second.
///    The announcement is composed in C# and so was spoken anyway, which is why it
///    reported a swap that had not happened. This is the same trap as the squawk keypad
///    and the wiper switch; the fix is the same, <c>ExecuteCalculatorCodeUnique</c>.
///
/// 3. The announcement was a PREDICTION, not a read-back. It read the cached standby
///    BEFORE firing and announced that as the new active. So it could only ever be right
///    when the cache was right - and see (4) for why it was not - and it stayed confidently
///    wrong when the event had been dropped by (2).
///
/// 4. The cache it predicted from was never populated. Batch membership requires
///    <c>Continuous AND IsAnnounced</c>, and the standby readouts carried
///    <c>IsAnnounced = false</c>, so <c>GetCachedVariableValue</c> had nothing for them.
///    That is "COM 2 does not remember what the standby is": there was nothing to
///    remember. NAV 1 announcing 113.90 while the radio actually held 110.30 is the same
///    fault - a stale or absent cache read, presented as fact.
///
/// The fix removes the prediction entirely. Every frequency is now Continuous and
/// announced, and the value a pilot hears is the one the RADIO reported after the fact.
/// A swap says only that it swapped; the frequency that follows comes from the aircraft.
///
/// WHY IT IS DEBOUNCED, same reasoning as the altimeter subscales: tuning steps in 25 kHz
/// (COM) or 50 kHz (NAV) increments and a knob sweeps many steps a second. Only the
/// frequency the knob comes to rest on is of any interest.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>
    /// How still the tuning must be before the frequency is spoken.
    ///
    /// ⚠️ A SETTLE MUST OUTLAST THE SAMPLE PERIOD OF WHAT FEEDS IT, and for a long time that
    /// meant 1200 ms - the frequencies rode the 1 Hz continuous batch, so a shorter settle
    /// expired BETWEEN deliveries and announced every intermediate frequency the knob passed
    /// through. The cost was that every read-back was a beat behind, which is the other half
    /// of what made tuning feel unreliable.
    ///
    /// FAST-SAMPLED: the frequencies now run on a per-var SIM_FRAME subscription
    /// (ExcludeFromBatch + HighFrequency on each of the eight), so a moving value arrives
    /// within a frame rather than within a second and 250 ms is comfortably longer than the
    /// gap between two deliveries. That is what buys BOTH properties at once: no intermediate
    /// announcements, and an answer that lands about as fast as the pilot let go of the knob.
    /// </summary>
    // ⚠️ BACK OVER ONE DELIVERY PERIOD, BECAUSE THE FEED IS 1 Hz AGAIN. 250 ms was correct
    // only while these rode a SIM_FRAME subscription; that subscription used the CHANGED
    // flag and therefore never delivered a static frequency at all, so it is gone and the
    // readouts are on PERIOD.SECOND. A settle shorter than the delivery period expires
    // BETWEEN deliveries and announces every intermediate value a knob sweeps through -
    // the "it announces 700, 800" report. Just over the period means each delivery restarts
    // it and only the resting value is spoken.
    private const int RadioSettleMs = 1100;

    /// <summary>How long after MSFSBA's own set or swap to stay quiet.</summary>
    private const int RadioOwnWriteGraceMs = 2500;

    /// <summary>
    /// The frequency readouts, and what to call each one. These are IsAnnounced so they
    /// reach the batch cache and this announcer sees them at all; the generic monitor is
    /// kept out of them by <see cref="NoteRadioChange"/> returning true.
    /// </summary>
    private static readonly Dictionary<string, string> RadioLabels = new(StringComparer.Ordinal)
    {
        ["DA40_RADIO_COM1_ACTIVE"] = "COM 1 active",
        ["DA40_RADIO_COM2_ACTIVE"] = "COM 2 active",
        ["DA40_RADIO_NAV1_ACTIVE"] = "NAV 1 active",
        ["DA40_RADIO_NAV2_ACTIVE"] = "NAV 2 active",
        ["DA40_RADIO_COM1_SET"] = "COM 1 standby",
        ["DA40_RADIO_COM2_SET"] = "COM 2 standby",
        ["DA40_RADIO_NAV1_SET"] = "NAV 1 standby",
        ["DA40_RADIO_NAV2_SET"] = "NAV 2 standby",

        // The autopilot's SELECTED values ride the same settle timer, and for the same
        // reason: an altitude preselect knob steps 100 ft at a time and a heading bug one
        // degree, so announcing every step buries the value the pilot stopped on. They had
        // no announcement at all before this - moving a knob on real hardware, or the
        // G1000 changing a preselect, was completely silent.
        ["DA40_AP_ALT_SET"] = "Selected altitude",
        ["DA40_AP_VS_SET"] = "Selected vertical speed",
        ["DA40_AP_IAS_SET"] = "Selected airspeed",
        ["DA40_AP_HDG_SET"] = "Heading bug",
        // One knob, three courses: the CDI's own is the only one spoken (FlushRadioSettle),
        // so all three are simply "Course", as the knob is.
        ["DA40_AP_CRS_SET"] = "Course",
        ["DA40_AP_CRS_NAV2"] = "Course",
        ["DA40_AP_CRS_GPS"] = "Course"
    };

    /// <summary>
    /// Exposed for the suite. These carry IsAnnounced and sit in a panel display list, so
    /// the numeric-silence test would flag them - but they are not on the generic path at
    /// all, they are spoken by the settle timer below.
    /// </summary>
    public static IReadOnlyCollection<string> RadioAnnouncedKeys => RadioLabels.Keys;

    private System.Windows.Forms.Timer? _radioSettleTimer;
    private ScreenReaderAnnouncer? _radioAnnouncer;
    private DateTime _radioOwnWriteAt = DateTime.MinValue;

    private readonly Dictionary<string, double> _radioPending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _radioSpoken = new(StringComparer.Ordinal);

    /// <summary>
    /// Called by every path inside MSFSBA that tunes or swaps, so the settle timer knows
    /// the change was ours. The panel field and the swap button both confirm themselves.
    /// </summary>
    /// <summary>
    /// Remember WHICH key MSFSBA just wrote, so the settle skips that one and nothing else.
    ///
    /// ⚠️ THIS USED TO BE A CATEGORY, AND BOTH SHAPES OF THAT WERE WRONG. Suppressing every
    /// key ending "_SET" swallowed autopilot preselects a radio write had nothing to do with;
    /// narrowing it to radio standbys alone then UN-suppressed the autopilot's own echo, so
    /// Ctrl+A announced "Selected altitude 5000 feet" from the setter and "Selected altitude
    /// 5000" from the settle a moment later - the same value twice over one keypress.
    ///
    /// The key is the honest unit: a value MSFSBA wrote and already spoke is redundant, and
    /// any OTHER value moving in the same window is somebody else's news.
    /// </summary>
    private void MarkRadioSetByUs(params string[] varKeys)
    {
        _radioOwnWriteAt = DateTime.UtcNow;
        _radioOwnWriteKeys = varKeys;
    }

    // More than one only for a course typed before the CDI source is known (CdiCourseKeys).
    private string[] _radioOwnWriteKeys = Array.Empty<string>();

    /// <summary>
    /// The display window tuned a radio and has already spoken the result.
    ///
    /// Same grace as a typed set, reached from the one place that knows WHICH frequency a
    /// knob turn moved - the window reads the change off the Coherent socket, which is the
    /// only fast source, and the settle announcer would otherwise repeat it a second later
    /// off the 1 Hz batch.
    /// </summary>
    internal void MarkRadioTunedByWindow(string varKey) => MarkRadioSetByUs(varKey);

    /// <summary>
    /// A standby FREQUENCY the pilot typed, which the panel field already read back - never
    /// an autopilot selected value, which shares the "_SET" suffix and nothing else.
    /// </summary>
    /// <remarks>
    /// ⚠️ RETAINED FOR ITS TEST, NOT USED BY THE SUPPRESSION ANY MORE. The grace is per-KEY
    /// now (see MarkRadioSetByUs) because a category - either category - suppressed the wrong
    /// things. This predicate still records which keys are radio standbys, and its test still
    /// documents why "ends with _SET" was never the right question.
    /// </remarks>
    internal static bool IsRadioStandbyKey(string key)
        => key.StartsWith("DA40_RADIO_", StringComparison.Ordinal)
           && key.EndsWith("_SET", StringComparison.Ordinal);

    private bool NoteRadioChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (!RadioLabels.ContainsKey(varKey)) return false;

        _radioAnnouncer = announcer;

        // A first reading is not a change. Without this, the first batch after a connect
        // reads all eight frequencies at a pilot who has touched nothing.
        if (!_radioSpoken.ContainsKey(varKey))
        {
            _radioSpoken[varKey] = value;
            return true;
        }

        // Below half a COM channel there is nothing to say - that is float noise on the
        // wire, not a tuning step.
        if (Math.Abs(value - _radioSpoken[varKey]) < 0.005) return true;

        _radioPending[varKey] = value;

        if (_radioSettleTimer == null)
        {
            _radioSettleTimer = new System.Windows.Forms.Timer { Interval = RadioSettleMs };
            _radioSettleTimer.Tick += (_, _) => UnderLoadSettle(_radioAnnouncer, FlushRadioSettle);
        }

        // Stop-then-start is what makes it a settle rather than a repeat.
        _radioSettleTimer.Stop();
        _radioSettleTimer.Start();
        return true;
    }

    private void FlushRadioSettle()
    {
        _radioSettleTimer?.Stop();

        var pending = new Dictionary<string, double>(_radioPending);
        _radioPending.Clear();
        if (pending.Count == 0) return;

        bool ours = (DateTime.UtcNow - _radioOwnWriteAt).TotalMilliseconds < RadioOwnWriteGraceMs;
        foreach (var kv in pending) _radioSpoken[kv.Key] = kv.Value;
        if (_radioAnnouncer == null) return;

        // A SWAP is ours but must still be spoken: the whole point of the fix is that the
        // pilot hears what the radio actually did, and a swap moves the ACTIVE frequency.
        // Only a standby the pilot typed is genuinely redundant, because the field already
        // read it back.
        if (ours)
        {
            // ⚠️ RADIO STANDBYS ONLY. This tested key.EndsWith("_SET"), which is true of the
            // standby frequencies AND of all five autopilot selected values -
            // DA40_AP_ALT_SET, _VS_SET, _IAS_SET, _HDG_SET, _CRS_SET. So typing a COM
            // standby, or pressing swap, set the "ours" grace and then swallowed any
            // autopilot preselect that moved inside the next 2.5 seconds - a heading bug
            // turned on real hardware, or an altitude the G1000 changed, gone silently for a
            // reason that had nothing to do with either.
            //
            // The suppression is justified ONLY for a value MSFSBA itself echoed back as it
            // was typed; an autopilot preselect that happens to change in the same window is
            // somebody else's news and must still be spoken.
            foreach (var own in _radioOwnWriteKeys) if (own.Length > 0) pending.Remove(own);
            if (pending.Count == 0) return;
        }

        // The Ctrl+M mute is checked HERE, not left to the caller. This speaks from a
        // TIMER, and the generic monitor gate only wraps ProcessSimVarUpdate - a callback
        // outside that wrap would keep talking after the pilot un-ticked the row. Same
        // rule the altimeter settle and the A32NX armed-altitude flush follow.
        // A course the CDI is not on moved without the knob (a NAV 2 OBS while flying GPS):
        // nothing the pilot is flying changed, so it is not news.
        // Until the CDI source has been delivered every course may speak: dropping one on a
        // guess is how a NAV 2 course turned on hardware went unsaid after a connect.
        var cdiCourses = CdiCourseKeys();
        foreach (var key in CourseKeys)
            if (Array.IndexOf(cdiCourses, key) < 0) pending.Remove(key);
        if (pending.Count == 0) return;

        var muted = Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet;
        foreach (var key in RadioLabels.Keys)
            if (muted.Contains(key)) pending.Remove(key);
        if (pending.Count == 0) return;

        // One utterance when several moved. A swap changes active AND standby together, so
        // two announcements a breath apart is exactly how a pilot loses the first.
        var parts = new List<string>();
        foreach (var kv in RadioLabels)
            if (pending.TryGetValue(kv.Key, out double f))
            {
                // A COM frequency needs three decimals (8.33 kHz) and a NAV frequency two,
                // the same as the typed read-back ("NAV 1 standby 110.50"); a swap read NAV
                // as "110.500" beside it. An altitude of 9000 read as "9000.000" is a
                // different kind of wrong, so everything that is not a radio is whole.
                bool com = kv.Key.IndexOf("RADIO_COM", StringComparison.Ordinal) >= 0;
                bool nav = kv.Key.IndexOf("RADIO_NAV", StringComparison.Ordinal) >= 0;
                parts.Add(kv.Value + " " + f.ToString(com ? "0.000" : nav ? "0.00" : "0",
                    System.Globalization.CultureInfo.InvariantCulture));
            }

        // Immediate for the same reason as the barometric settle: this is the value the
        // knob came to REST on, 600 ms after the pilot stopped turning, and queueing it
        // behind the display window's own read-backs is what made the altimeter setting
        // arrive so late it read as broken.
        if (parts.Count > 0) _radioAnnouncer.AnnounceImmediate(string.Join(". ", parts));
    }

    /// <summary>Stops the settle timer. Called when the aircraft is switched away.</summary>
    private void StopRadioAnnounce()
    {
        try { _radioSettleTimer?.Stop(); _radioSettleTimer?.Dispose(); } catch { }
        _radioSettleTimer = null;
        _radioPending.Clear();
    }
}
