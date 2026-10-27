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

    /// <summary>
    /// How still an autopilot selected value must be before it is spoken.
    ///
    /// FAST-SAMPLED: the selected values (AddApValue) run on per-var SIM_FRAME subscriptions,
    /// so a knob that is still moving delivers well inside this and only the value it stops
    /// on is said - within a third of a second of the pilot letting go, where the shared
    /// 1100 ms radio settle on the 1 Hz batch answered one to two seconds late.
    /// </summary>
    private const int ApSettleMs = 300;

    /// <summary>The autopilot's selected values, which settle on their own fast timer.</summary>
    private static bool IsApSelectedKey(string key) => key.StartsWith("DA40_AP_", StringComparison.Ordinal);

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
    ///
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
    ///
    /// ⚠️ EVERY KEY WRITTEN IN THE GRACE, NOT ONLY THE LAST. Holding just the latest write
    /// meant three entries in a row - altitude, heading, airspeed - left the first two looking
    /// like somebody else's change: each setter confirmed itself and then the settle said
    /// "Selected altitude 10000. Heading bug 164" again (live, LOWG climb-out 2026-10-12).
    /// </summary>
    internal void MarkRadioSetByUs(params string[] varKeys)
    {
        _radioOwnWriteAt = DateTime.UtcNow;
        foreach (var key in varKeys) _radioOwnWrites[key] = _radioOwnWriteAt;
    }

    /// <summary>The keys MSFSBA wrote, and when; each is ours for RadioOwnWriteGraceMs.</summary>
    private readonly Dictionary<string, DateTime> _radioOwnWrites = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="key"/> was written by MSFSBA inside the grace.</summary>
    internal bool WrittenByUs(string key, DateTime now) =>
        _radioOwnWrites.TryGetValue(key, out var at) &&
        (now - at).TotalMilliseconds < RadioOwnWriteGraceMs;

    /// <summary>
    /// The selected value a GFC 700 mode OVERWRITES as it engages: VS syncs its reference to
    /// the current vertical speed, FLC to the current airspeed. Null for every other mode.
    /// </summary>
    internal static string? ApTargetSyncedBy(string modeKey, bool on) => !on ? null : modeKey switch
    {
        "DA40_AP_VS" => "DA40_AP_VS_SET",
        "DA40_AP_FLC" => "DA40_AP_IAS_SET",
        _ => null
    };

    /// <summary>
    /// MSFSBA pressed a mode button. ⚠️ A mode that syncs its target makes that target
    /// somebody else's news, even if MSFSBA typed it a moment ago: at VCBI (2026-10-15)
    /// "Selected vertical speed 500 feet per minute up" was followed by VS, the GFC 700
    /// synced the reference to the current 0, and the 0 settled inside the 500's own-write
    /// grace and was dropped as our echo - before the mode-sync naming below was ever asked -
    /// so the aeroplane sat level on a climb the pilot had heard set.
    /// </summary>
    internal void NoteOwnApModePress(string modeKey, bool on)
    {
        if (on) ForgetSyncedTarget(modeKey);
    }

    /// <summary>
    /// A GFC 700 mode row arrived (per frame). Stamps the mode change, and on the OFF-to-ON
    /// edge only does what <see cref="NoteOwnApModePress"/> does, so VS or FLC engaged from
    /// the bezel speaks its sync too. Never on a repeat: in VS already, a rate typed now is
    /// our own echo.
    /// </summary>
    internal void NoteApModeRow(string modeKey, double value)
    {
        bool on = value >= 0.5;
        bool was = _apModeOn.TryGetValue(modeKey, out bool w) && w;
        _apModeOn[modeKey] = on;
        _apModeChangedAt = DateTime.UtcNow;
        if (on && !was) ForgetSyncedTarget(modeKey);
    }

    private readonly Dictionary<string, bool> _apModeOn = new(StringComparer.Ordinal);

    private void ForgetSyncedTarget(string modeKey)
    {
        string? synced = ApTargetSyncedBy(modeKey, on: true);
        if (synced == null) return;
        _radioOwnWrites.Remove(synced);
        // Names kept on the sync's read-back ("Selected vertical speed 0"), as for any sync.
        _apModeChangedAt = DateTime.UtcNow;
    }

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

    /// <summary>
    /// An autopilot setter just SPOKE <paramref name="value"/>: that is now what the pilot last
    /// heard, so the settle measures the next change from it.
    ///
    /// ⚠️ WITHOUT THIS A SYNC BACK TO THE OLD VALUE WAS LOST AND THE ECHO SPOKEN INSTEAD. MTL
    /// climb, 2026-10-19: FLC pressed, 76 typed ("Selected airspeed 76 knots"), and the GFC 700
    /// synced the reference back to the current 70. The 70 matched the last SETTLED value and
    /// was dropped as noise, while the 76 echo still sat queued - "Selected airspeed 76" over a
    /// target of 70.
    /// </summary>
    internal void MarkApTargetSpoken(string varKey, double value)
    {
        MarkRadioSetByUs(varKey);
        _radioSpoken[varKey] = value;
    }

    /// <summary>A value queued for the settle, for the suite.</summary>
    internal double? PendingRadioValue(string varKey) =>
        _radioPending.TryGetValue(varKey, out double v) ? v : null;

    /// <summary>
    /// Queues a delivered value for the settle; false when there is nothing to say. Pure of the
    /// timer so the suite can drive it.
    /// </summary>
    internal bool StageRadioValue(string varKey, double value)
    {
        // A first reading is not a change. Without this, the first batch after a connect
        // reads all eight frequencies at a pilot who has touched nothing.
        if (!_radioSpoken.ContainsKey(varKey))
        {
            _radioSpoken[varKey] = value;
            return false;
        }

        // Below half a COM channel there is nothing to say - that is float noise on the
        // wire, not a tuning step. A value BACK where the pilot last heard it also takes any
        // queued value with it: a knob turned away and back, or a target synced back to the
        // value its setter had replaced, leaves nothing new, and the queued one is stale.
        if (Math.Abs(value - _radioSpoken[varKey]) < 0.005)
        {
            _radioPending.Remove(varKey);
            return false;
        }

        _radioPending[varKey] = value;
        return true;
    }

    private bool NoteRadioChange(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (!RadioLabels.ContainsKey(varKey)) return false;

        _radioAnnouncer = announcer;

        if (!StageRadioValue(varKey, value)) return true;

        // The autopilot's selected values settle on their own, shorter timer: their feed is
        // per-frame, the radios' is 1 Hz (see ApSettleMs and RadioSettleMs).
        bool ap = IsApSelectedKey(varKey);
        ref var timer = ref (ap ? ref _apSettleTimer : ref _radioSettleTimer);
        if (timer == null)
        {
            timer = new System.Windows.Forms.Timer { Interval = ap ? ApSettleMs : RadioSettleMs };
            timer.Tick += ap
                ? (_, _) => UnderLoadSettle(_radioAnnouncer, () => FlushRadioSettle(apOnly: true))
                : (_, _) => UnderLoadSettle(_radioAnnouncer, () => FlushRadioSettle(apOnly: false));
        }

        // Stop-then-start is what makes it a settle rather than a repeat.
        timer.Stop();
        timer.Start();
        return true;
    }

    private System.Windows.Forms.Timer? _apSettleTimer;

    private void FlushRadioSettle(bool apOnly)
    {
        (apOnly ? _apSettleTimer : _radioSettleTimer)?.Stop();

        var pending = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var kv in _radioPending)
            if (IsApSelectedKey(kv.Key) == apOnly) pending[kv.Key] = kv.Value;
        foreach (var key in pending.Keys) _radioPending.Remove(key);
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
            var now = DateTime.UtcNow;
            foreach (var key in pending.Keys.ToList()) if (WrittenByUs(key, now)) pending.Remove(key);
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

        // Immediate for the same reason as the barometric settle: this is the value the
        // knob came to REST on, moments after the pilot stopped turning, and queueing it
        // behind the display window's own read-backs is what made the altimeter setting
        // arrive so late it read as broken.
        // A target the autopilot moved itself as a mode engaged (VS syncs to the current
        // vertical speed, FLC to the current airspeed) keeps its name: a bare "0" right after
        // VS engaged said nothing a pilot could place (2026-10-12).
        // A sync is queued instead (SettledValueInterrupts): it follows the read-back of the press
        // that caused it rather than cutting it off.
        bool modeSync = (DateTime.UtcNow - _apModeChangedAt).TotalMilliseconds < ApModeSyncMs;
        string said = ComposeSettled(pending, keepNames: modeSync);
        if (said.Length == 0) return;
        if (SettledValueInterrupts(modeSync)) _radioAnnouncer.AnnounceImmediate(said);
        else _radioAnnouncer.Announce(said);
    }

    /// <summary>
    /// A heading or course as the G1000 draws it: three digits, and north as 360, never 000
    /// ("Heading bug 000" was spoken for a bug at north; the HDG box says 360°).
    /// </summary>
    internal static string HeadingWords(double degrees)
    {
        int d = ((int)Math.Round(degrees) % 360 + 360) % 360;
        return (d == 0 ? 360 : d).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether a settled value interrupts (DA40A-9). A knob at rest does: the pilot is waiting for
    /// it. A value a mode or the CDI moved by itself (<see cref="ApModeSyncMs"/>) waits its turn:
    /// switching the CDI to LOC1 with the PFD softkey, "Course 206" cut off the window's own
    /// "Navigation source LOC1" (MSFS 2024, 2026-10-27). Pure so the suite can pin it.
    /// </summary>
    internal static bool SettledValueInterrupts(bool modeSync) => !modeSync;

    /// <summary>How long after a GFC 700 mode change a target that moves is that mode's sync.</summary>
    private const int ApModeSyncMs = 2000;

    /// <summary>When a GFC 700 mode last changed (set from the mode rows as they arrive).</summary>
    private DateTime _apModeChangedAt = DateTime.MinValue;

    /// <summary>
    /// The settled values as one utterance. Pure so the suite can pin it.
    ///
    /// One utterance when several moved: a swap changes active AND standby together, and two
    /// announcements a breath apart is how a pilot loses the first.
    ///
    /// ⚠️ ONE AUTOPILOT VALUE ALONE IS JUST THE VALUE. A heading bug turned on external
    /// hardware was answered "Heading bug 250", and the pilot asked for "250": the knob they
    /// turned already says which value it is. Two selected values settling together keep
    /// their names, because two bare numbers would not say which was which. Radios always
    /// keep theirs - "COM 1 standby" is which radio, not decoration.
    /// </summary>
    internal static string ComposeSettled(IReadOnlyDictionary<string, double> pending, bool keepNames = false)
    {
        int apCount = 0;
        foreach (var key in pending.Keys) if (IsApSelectedKey(key)) apCount++;

        var parts = new List<string>();
        foreach (var kv in RadioLabels)
        {
            if (!pending.TryGetValue(kv.Key, out double f)) continue;

            // A COM frequency needs three decimals (8.33 kHz) and a NAV frequency two, the
            // same as the typed read-back ("NAV 1 standby 110.50"); a swap read NAV as
            // "110.500" beside it. A heading or course is three digits, as the bug is read
            // everywhere else ("005"); anything else is whole.
            bool com = kv.Key.IndexOf("RADIO_COM", StringComparison.Ordinal) >= 0;
            bool nav = kv.Key.IndexOf("RADIO_NAV", StringComparison.Ordinal) >= 0;
            bool angle = kv.Key == "DA40_AP_HDG_SET" || kv.Value == "Course";
            string value = angle
                ? HeadingWords(f)
                : f.ToString(com ? "0.000" : nav ? "0.00" : "0", System.Globalization.CultureInfo.InvariantCulture);

            parts.Add(IsApSelectedKey(kv.Key) && apCount == 1 && !keepNames ? value : kv.Value + " " + value);
        }
        return string.Join(". ", parts);
    }

    /// <summary>Stops the settle timer. Called when the aircraft is switched away.</summary>
    private void StopRadioAnnounce()
    {
        try { _radioSettleTimer?.Stop(); _radioSettleTimer?.Dispose(); } catch { }
        _radioSettleTimer = null;
        try { _apSettleTimer?.Stop(); _apSettleTimer?.Dispose(); } catch { }
        _apSettleTimer = null;
        _radioPending.Clear();
    }
}
