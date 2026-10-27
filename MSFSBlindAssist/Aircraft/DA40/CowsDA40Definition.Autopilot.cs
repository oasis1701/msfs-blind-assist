using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Autopilot → GFC 700 and Flight Director. Both variants.
///
/// The DA40's autopilot is a Garmin GFC 700, and it has NO AIRFRAME VARIABLES OF ITS OWN:
/// the mode controller is part of the G1000, so every button on it is a stock MSFS
/// autopilot event and every light is a stock autopilot SimVar. All fifteen were read live
/// on the aircraft before this panel was written — nothing here is inferred from the name.
///
/// WHY IT IS A PANEL AND NOT THE DISPLAY WINDOW. The standing rule is that anything doable
/// inside a G1000 display does not get a panel. The GFC 700 is the exception the rule is
/// shaped around: its buttons are NOT on the PFD or MFD bezel at all — they are a separate
/// mode controller between the two screens — so there is no display page to drive them
/// from. What the autopilot is DOING still belongs to the display (the FMA, read by the
/// PFD window as "Autopilot: ..."), and is not repeated here.
///
/// APPROACH IS A PUSH BUTTON, LIKE THE REAL KEY (DA40A-28). It sends the plain AP_APR_HOLD,
/// so the G1000 decides what the press means against its own approach flag, and it reads
/// that flag back. AP_APR_HOLD_ON was refused while the G1000's flag was stuck on after the
/// CDI left the localiser, and nothing BA read showed it (2026-10-26 in flight, reproduced
/// on the ground 2026-10-27). The other mode keys press the same way; the autopilot
/// master and flight director stay switches.
///
/// THE HEADING BUG AND COURSE MOVED HERE from the Radios panel. They are physically PFD
/// bezel knobs, which is why they started out beside the radio knobs, but functionally
/// they are what the autopilot flies — a pilot selecting HDG mode needs the bug in the
/// same place, not two panels away. They are not duplicated: the Radios panel no longer
/// carries them.
///
/// The AUTOPILOT DISCONNECT is deliberately NOT here. It lives on the Elevator Trim panel
/// because it is the red button on the control stick, next to the trim switch it also
/// interrupts, and one control belongs in one place.
/// </summary>
public partial class CowsDA40Definition
{
    private const string AutopilotPanel = "GFC 700";
    private const string FlightDirectorPanel = "Flight Director";

    private static Dictionary<string, SimVarDefinition> BuildAutopilotVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- TO/GA ----------
        //
        // The go-around button on the power lever. It had no way into MSFSBA at all, which
        // means a blind pilot could not fly a go-around the way the aeroplane is flown -
        // and the go-around is the one manoeuvre where you have no time to work out an
        // attitude for yourself.
        //
        // It is a BUTTON, not a state: pressing it again does not un-press it. There is no
        // variable to read back either, so the button carries no resting state and the
        // aeroplane answers through the flight director's own pitch command, which the
        // Flight Director panel already reads.
        v["DA40_AP_TOGA"] = new SimVarDefinition
        {
            Name = "DA40_AP_TOGA",
            DisplayName = "Go Around (TO/GA)",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        AddApMode(v, "DA40_AP_MASTER", "AUTOPILOT MASTER", "Autopilot");
        // ⚠️ NO YAW DAMPER CONTROL. THIS AEROPLANE HAS NONE, AND THE CONTROL WAS INERT.
        //
        // It read as a normal GFC 700 switch and did NOTHING. Measured in flight: firing
        // K:YAW_DAMPER_ON left AUTOPILOT YAW DAMPER at 0, and systems.cfg carries
        // `yaw_damper_gain = 0` - so there is no damper to engage and no authority if there
        // were. The G1000 draws a YD slot on the FMA because the stock strip has one; the
        // label scrapes empty.
        //
        // A control that answers a keypress with silence is worse than no control at all: a
        // blind pilot cannot tell it from a control they pressed wrongly, and this codebase's
        // own playbook says to confirm by READ-BACK rather than by the absence of complaint.
        // Removed rather than left as a decoration.
        //
        // A real DA40 NG is right to lack one - a yaw damper belongs on twins and faster
        // singles, not a fixed-gear four-seat trainer.

        // ---------- Lateral modes ----------
        AddApMode(v, "DA40_AP_HDG", "AUTOPILOT HEADING LOCK", "Heading Mode");
        AddApMode(v, "DA40_AP_NAV", "AUTOPILOT NAV1 LOCK", "Navigation Mode");
        // ⚠️ THE G1000'S OWN APPROACH FLAG, NOT AUTOPILOT APPROACH HOLD (DA40A-28). The stock
        // var dropped to 0 while the G1000 still had approach mode on, and every press was
        // refused with nothing to show why. A push button: the press is AP_APR_HOLD.
        AddApMode(v, "DA40_AP_APR", "WTAP_Garmin_Approach_Mode_On", "Approach Mode",
            SimVarType.LVar, "number");
        AddApMode(v, "DA40_AP_BC", "AUTOPILOT BACKCOURSE HOLD", "Backcourse Mode");

        // ---------- Vertical modes ----------
        AddApMode(v, "DA40_AP_ALT", "AUTOPILOT ALTITUDE LOCK", "Altitude Hold");
        AddApMode(v, "DA40_AP_VS", "AUTOPILOT VERTICAL HOLD", "Vertical Speed Mode");
        AddApMode(v, "DA40_AP_FLC", "AUTOPILOT FLIGHT LEVEL CHANGE", "Flight Level Change");

        // ---------- Selected values ----------
        AddApValue(v, "DA40_AP_ALT_SET", "AUTOPILOT ALTITUDE LOCK VAR", "Selected Altitude",
            "feet", "F0");
        AddApValue(v, "DA40_AP_VS_SET", "AUTOPILOT VERTICAL HOLD VAR", "Selected Vertical Speed",
            "feet per minute", "F0");
        AddApValue(v, "DA40_AP_IAS_SET", "AUTOPILOT AIRSPEED HOLD VAR", "Selected Airspeed",
            "knots", "F0");
        AddApValue(v, "DA40_AP_HDG_SET", "AUTOPILOT HEADING LOCK DIR", "Heading Bug",
            "degrees", "F0");
        AddApValue(v, "DA40_AP_CRS_SET", "NAV OBS:1", "Course",
            "degrees", "F0");

        // ⚠️ THE COURSE IS THE CDI'S, NOT NAV 1'S. The CRS knob sets the course of whatever
        // the CDI is on (measured live on the NG, 2026-10-06, one AS1000_PFD_CRS_INC each):
        // CDI on NAV 2 moved NAV OBS:2 and left NAV OBS:1 alone; CDI on GPS moved GPS OBS
        // VALUE, the course OBS mode will fly. The typed Course wrote VOR1_SET whatever the
        // CDI said, so with the CDI on NAV 2 it set a course nothing was flying. The two
        // other courses ride the same settle announcer, and only the CDI's own course speaks.
        AddApValue(v, "DA40_AP_CRS_NAV2", "NAV OBS:2", "NAV 2 Course", "degrees", "F0");
        AddApValue(v, "DA40_AP_CRS_GPS", "GPS OBS VALUE", "GPS OBS Course", "degrees", "F0");

        // Which source the CDI is on: GPS DRIVES NAV1 is 1 on GPS, otherwise AUTOPILOT NAV
        // SELECTED names the NAV radio (measured stepping the PFD's CDI softkey GPS, LOC1,
        // LOC2: 1/1, 0/1, 0/2). Cached, silent, never a Ctrl+M row.
        v["DA40_CDI_GPS"] = new SimVarDefinition
        {
            Name = "GPS DRIVES NAV1",
            DisplayName = "CDI on GPS",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            ExcludeFromBatch = true,   // per frame, with the courses it re-syncs
            HighFrequency = true
        };
        v["DA40_CDI_NAV"] = new SimVarDefinition
        {
            Name = "AUTOPILOT NAV SELECTED",
            DisplayName = "CDI NAV radio",
            Type = SimVarType.SimVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true,
            ExcludeFromBatch = true,
            HighFrequency = true
        };

        // ---------- THE KNOBS THEMSELVES ----------
        //
        // ⚠️ A TYPED BOX IS A CONVENIENCE, NOT THE CONTROL. Every one of the five selected
        // values above could only be TYPED, which is a thing the cockpit does not have -
        // a sighted pilot turns a knob and watches the number step. The pilot's question
        // was exactly that: "are we able to do the same thing as the sighted pilots do,
        // the same way they do?" For these five the answer was no, and the typed box hid
        // it because it worked.
        //
        // Both stay. The Radios panel already keeps its typed entry beside a working
        // bezel for the same reason: typing is faster when you know the number, stepping
        // is what you do when you are searching for it or matching a clearance you are
        // still being read.
        //
        // ⚠️ EVERY EVENT AND EVERY STEP SIZE HERE WAS MEASURED LIVE ON THIS AIRFRAME, not
        // taken from a list - the DA40's autopilot has no airframe variables at all, so
        // these are stock events and the stock names are the only thing that can be wrong.
        // Read before, fired, read after, both directions, then restored:
        //   VOR1_OBI_INC/DEC     NAV OBS:1                     45 -> 46 -> 45      1 deg
        //   HEADING_BUG_INC/DEC  AUTOPILOT HEADING LOCK DIR    127 -> 128 -> 127   1 deg
        //   AP_ALT_VAR_INC/DEC   AUTOPILOT ALTITUDE LOCK VAR   5000 -> 5100 -> 5000  100 ft
        //   AP_VS_VAR_INC/DEC    AUTOPILOT VERTICAL HOLD VAR   -1000 -> -900 -> -1000  100 fpm
        //   AP_SPD_VAR_INC/DEC   AUTOPILOT AIRSPEED HOLD VAR   90 -> 91 -> 90      1 kt
        // ⚠️ NO STEP BUTTONS HERE, BY THE PILOT'S RULING. Ten of them lived here - ALT, VS,
        // IAS, HDG and CRS, up and down - and every one duplicated the typed field beside
        // it: "if it's doable in an editable way, it should be done with numbers or
        // sliders... hardware will do the step ups and downs, left and rights, while the
        // panel should just have the edit fields." The knobs are on the pilot's hardware
        // and on Ctrl+A / S / H / V, so a pair of buttons per value bought nothing and
        // doubled the length of the panel a blind pilot has to walk.
        //
        // The VALUES are unaffected: DA40_AP_*_SET still take a number and still write the
        // same stock events. This removes the two extra controls, not the capability.

        // ---------- The GFC 700's pre-flight test ----------
        //
        // ⚠️ NOTHING IN MSFSBA COULD SEE THIS, and on a real GFC 700 it is the thing that
        // says whether the autopilot may be relied on at all. The PFD annunciates PFT while
        // it runs, and a pilot who takes off without seeing it pass has an autopilot of
        // unknown health.
        //
        // The state machine is the model's own, read out of its logic rather than guessed:
        //
        //   AFCS_TEST   0 idle, 1 running (10 s), 2 complete, -1 the AFCS itself failed
        //   AFCS_PFT    0 not started, 1 running, 2 passed, 3 passed and latched,
        //               -1 failed, -2 failed and latched
        //
        // ⚠️ AND PRESSING THE DISCONNECT DURING THE TEST FAILS IT. The model is explicit:
        // while AFCS_PFT is 1, INPUT_AP_DISC - or an elevator, aileron or trim servo
        // failure reaching 2 - drives it to -1. It needs five seconds undisturbed. That is
        // the opposite of what the disconnect button does at any other moment, which is
        // exactly why it is worth saying out loud.
        v["DA40_AP_PFT"] = new SimVarDefinition
        {
            Name = "AFCS_PFT",
            DisplayName = "Autopilot Pre-flight Test",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [-2] = "FAILED",
                [-1] = "FAILED",
                [0] = "not started",
                // ⚠️ "running", NOT "running - do not touch the disconnect". That tail was
                // COACHING - it told the pilot what not to do, which is the one thing an
                // announcement must never carry. That the disconnect fails the test is in
                // docs/da40.md; it does not belong on a state a pilot hears in flight.
                [1] = "running",
                [2] = "passed",
                [3] = "passed"
            }
        };

        v["DA40_AP_SELFTEST"] = new SimVarDefinition
        {
            Name = "AFCS_TEST",
            DisplayName = "Autopilot Self Test",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [-1] = "FAILED - partial panel",
                [0] = "not started",
                [1] = "running",
                [2] = "complete"
            }
        };

        // ⚠️ NOT AddFlag's defaults. That helper leaves a flag OnRequest and unannounced,
        // which is right for a state a pilot looks up and wrong for this one: an autopilot
        // that has failed is the definition of something that interrupts a sighted pilot,
        // and it would otherwise sit in a panel nobody had open.
        v["DA40_AP_FAILED"] = new SimVarDefinition
        {
            Name = "AFCS_FAILED",
            DisplayName = "Autopilot Failed",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "YES" }
        };

        // ---------- Flight Director ----------
        v["DA40_AP_FD"] = new SimVarDefinition
        {
            Name = "AUTOPILOT FLIGHT DIRECTOR ACTIVE:1",
            DisplayName = "Flight Director",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
            // On is said by the FMA (its modes appear); Off is said here, like the master's.
            RefreshControlWhenDefHandled = _ => true
        };

        // The command attitude the bars are showing. A sighted pilot flies the bars by
        // matching them; a blind pilot cannot see them at all, so the numbers ARE the
        // bars - and they are what makes a hand-flown departure to an FD command possible.
        // ⚠️ BOTH ARE RENDERED THROUGH TryGetDisplayOverride, NEVER as a raw signed number.
        // MSFS reports pitch NEGATIVE for nose up - established here rather than assumed:
        // the aeroplane sitting on its gear reads PLANE PITCH DEGREES -2.90 with
        // ATTITUDE INDICATOR PITCH DEGREES agreeing at -2.90, and a DA40 on its gear sits
        // nose UP. So a commanded -3.0 is three degrees nose UP, and the old note
        // saying "Positive is nose up" had it exactly backwards. That is not cosmetic: a
        // blind pilot hand-flying to this number would push when they should pull.
        AddApValue(v, "DA40_AP_FD_PITCH", "AUTOPILOT FLIGHT DIRECTOR PITCH", "Commanded Pitch",
            "degrees", "F1");
        AddApValue(v, "DA40_AP_FD_BANK", "AUTOPILOT FLIGHT DIRECTOR BANK", "Commanded Bank",
            "degrees", "F1");

        return v;
    }

    /// <summary>
    /// The flight director bars, as words rather than a signed number.
    ///
    /// SIGN. MSFS reports pitch NEGATIVE for nose up and bank LEFT-positive - the same
    /// left-positive convention the visual-guidance tone already has to undo. Read raw,
    /// a three-degree nose-up command was spoken as "-3", which a pilot flying the bars
    /// would answer by pushing.
    ///
    /// DENORMALS. Bank was measured live at 1.43e-305, which is not a small angle, it is
    /// floating-point dust. Anything under a twentieth of a degree is level.
    /// </summary>
    private static string DescribeFlightDirector(string varKey, double value)
    {
        // Undo the sign here, once, so everything downstream reads naturally.
        double v = -value;
        if (Math.Abs(v) < 0.05) v = 0;

        string magnitude = Math.Abs(v).ToString("F1",
            System.Globalization.CultureInfo.InvariantCulture);

        if (varKey == "DA40_AP_FD_PITCH")
        {
            if (v == 0) return "level";
            return magnitude + " degrees nose " + (v > 0 ? "up" : "down");
        }

        if (v == 0) return "wings level";
        return magnitude + " degrees " + (v > 0 ? "right" : "left");
    }

    /// <summary>
    /// The GFC 700 mode keys, which are push buttons (DA40A-28). The autopilot master and the
    /// flight director stay switches: BA reads the same state the sim keeps for them.
    /// </summary>
    internal static bool ApModeIsPress(string key) => key is "DA40_AP_HDG" or "DA40_AP_NAV"
        or "DA40_AP_APR" or "DA40_AP_BC" or "DA40_AP_ALT" or "DA40_AP_VS" or "DA40_AP_FLC";

    /// <summary>An autopilot mode: a two-state row backed by a stock SimVar; a mode key renders as a button.</summary>
    private static void AddApMode(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display, SimVarType type = SimVarType.SimVar, string units = "bool")
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = type,
            Units = units,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
            // The mode buttons speak through the FMA (ApModeIsSpokenByFma), so their combos
            // are def-handled - and must still follow the aeroplane in an open panel.
            RefreshControlWhenDefHandled = _ => true,
            ExcludeFromMonitorManager = key != "DA40_AP_MASTER",
            // Per frame, like the selected values (AddApValue): the mode change and the target
            // it syncs (VS to the current rate, FLC to the current speed) then arrive in the
            // same frame, so the target's settle knows a mode engaged (ApModeSyncMs).
            ExcludeFromBatch = true,
            HighFrequency = true,
            RenderAsButton = ApModeIsPress(key)
        };
    }

    /// <summary>
    /// Refuses a typed value outside what the instrument accepts, saying the range, and
    /// returns true when it did. Nothing is sent: a value the pilot did not type is never
    /// put into the aeroplane on their behalf.
    /// </summary>
    internal static bool OutOfRange(Accessibility.ScreenReaderAnnouncer announcer, double value,
        double low, double high, string format, string what)
    {
        if (value >= low && value <= high) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        announcer.AnnounceImmediate(
            $"{value.ToString(format, inv)} is outside {what}, {low.ToString(format, inv)} to {high.ToString(format, inv)}. Not set.");
        return true;
    }

    // ---------- The AFCS's own health, settled ----------
    //
    // ⚠️ A POWER DIP MADE THESE CHURN. An in-flight restart's crank dropped the AFCS bus, and
    // in eight seconds the rows said "Autopilot Self Test: not started", "Autopilot Failed:
    // YES", "Self Test: FAILED - partial panel", "Failed: No", "Self Test: not started",
    // "Failed: YES", "Failed: No", "Self Test: running" (2026-10-12) - eight lines for one
    // event whose outcome was "running". The rows now settle and say where they came to rest,
    // once, in one sentence; a value back where it was says nothing. DA40A-5 still holds: the
    // test and its result are announced.
    private static readonly string[] AfcsStatusKeys = { "DA40_AP_SELFTEST", "DA40_AP_PFT", "DA40_AP_FAILED" };

    /// <summary>
    /// How still the AFCS status must be. A batch-fed value (DA40S-2): over one 1 Hz period,
    /// and long enough for a crank's dip to come and go.
    /// </summary>
    private const int AfcsStatusSettleMs = 2500;

    private System.Windows.Forms.Timer? _afcsSettle;
    private Accessibility.ScreenReaderAnnouncer? _afcsAnnouncer;
    private readonly Dictionary<string, double> _afcsPending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _afcsSpoken = new(StringComparer.Ordinal);

    /// <summary>How long after a mode press the FMA is asked whether it moved: two of the
    /// watcher's 1.5 s polls, so the change has had a scrape to land in.</summary>
    internal const int ApPressCheckMs = 3500;

    /// <summary>
    /// Whether a mode press went unanswered: the FMA did not move, and the watcher has read
    /// the PFD since (at least a second after the press). ⚠️ THE GFC 700 REFUSES PRESSES
    /// SILENTLY - APR from HDG after a go-around, NAV and APR in a quick run, AP while in GA
    /// (MTL/VCRI, 2026-10-19) - and the pilot heard nothing at all, the same as a press that
    /// worked and was still being drawn. Without fresh rows nothing is known, so nothing is said.
    /// ⚠️ ITS OWN ROW MOVING IS AN ANSWER TOO: the G1000 keeps "AP" flashing on the FMA for
    /// seconds after a disconnect, so AP OFF found the FMA unmoved and said "Autopilot: no
    /// change" over an autopilot that had just let go (VCRI, 2026-10-19).
    /// </summary>
    internal static bool ApPressUnanswered(int changesBefore, int changesNow, DateTime lastRowsAt,
        DateTime pressedAt, bool rowMoved)
        => !rowMoved && changesNow == changesBefore && lastRowsAt >= pressedAt.AddSeconds(1);

    internal static string ApPressNoChangeText(string control) => control + ": no change";

    /// <summary>
    /// What an Approach press says once <see cref="ApPressCheckMs"/> has passed, or null for
    /// nothing (DA40A-28). Its answer is the G1000's own flag, never the FMA counter: a
    /// refused press that also turned the flight director on moved the FMA and said nothing,
    /// and the same press again said "no change" (2026-10-27). The flag compared by VALUE at
    /// the press and now, not by edges. A flag that changed is spoken only when the FMA's
    /// approach modes did not show it on fresh rows: clearing a stuck flag moves nothing on
    /// the FMA, and without fresh rows the flag is the only answer there is. Pure so the
    /// suite can pin it.
    /// </summary>
    internal static string? ApproachPressAnswer(double? flagBefore, double? flagNow,
        string approachBefore, string approachNow, bool rowsFresh)
    {
        if (flagBefore is not double b || flagNow is not double n) return null;
        bool was = b >= 0.5, on = n >= 0.5;
        if (was == on) return ApPressNoChangeText("Approach Mode");
        if (rowsFresh && approachBefore != approachNow) return null;
        return "Approach Mode: " + (on ? "On" : "Off");
    }

    /// <summary>
    /// The approach modes on an FMA string ("ROL, LOC armed, PIT, ALTS armed, GS armed" gives
    /// "LOC armed, GS armed"): LOC, BC, VAPP, GS and GP, and GPS only beside GP, since GPS
    /// alone is the ordinary lateral mode. Pure so the suite can pin it.
    /// </summary>
    internal static string ApproachModesOnFma(string fma)
    {
        static string Base(string t) => t.EndsWith(" armed", StringComparison.Ordinal) ? t[..^6] : t;
        var tokens = G1000.G1000CasWatcher.FmaModes(fma)
            .Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool gp = tokens.Any(t => Base(t) == "GP");
        return string.Join(", ", tokens.Where(t => Base(t) switch
        {
            "LOC" or "BC" or "VAPP" or "GS" or "GP" => true,
            "GPS" => gp,
            _ => false
        }));
    }

    /// <summary>
    /// The Approach button's state in the autopilot window: the flag, and while it is on the
    /// approach modes the FMA shows ("LOC armed, GS armed"), or "On, nothing armed" when the
    /// flag is on and the FMA shows none. An FMA not read lately says plain "On", never a false
    /// "nothing armed". Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeApproachState(double? flag, string fma, bool fmaFresh)
    {
        if (flag is not double f) return "";
        if (f < 0.5) return "Off";
        if (!fmaFresh) return "On";
        string modes = ApproachModesOnFma(fma);
        return modes.Length > 0 ? modes : "On, nothing armed";
    }

    /// <summary>
    /// A mode key's state as the FMA shows it ("GPS armed", "GPS", "Off"), for its button label.
    /// ⚠️ NOT THE STOCK ROW: `AUTOPILOT NAV1 LOCK` read 0 with LOC/BC armed and 1 with GPS armed
    /// (2026-10-27), so the button said Off for one armed state and On for the other. NAV owns
    /// GPS, VOR, LOC and BC unless the approach flag is on (then LOC is Approach's). An FMA not
    /// read lately falls back to the row's On/Off. Approach has its own (DescribeApproachState).
    /// Pure so the suite can pin it.
    /// </summary>
    internal static string DescribeModeState(string key, bool? rowOn, string fma, bool fmaFresh, bool approachOn)
    {
        if (!fmaFresh) return rowOn is bool on ? (on ? "On" : "Off") : "";
        string[] mine = key switch
        {
            "DA40_AP_HDG" => new[] { "HDG" },
            "DA40_AP_NAV" => approachOn ? Array.Empty<string>() : new[] { "GPS", "VOR", "LOC", "BC" },
            "DA40_AP_BC" => new[] { "BC" },
            "DA40_AP_ALT" => new[] { "ALT" },
            "DA40_AP_VS" => new[] { "VS" },
            "DA40_AP_FLC" => new[] { "FLC" },
            _ => Array.Empty<string>()
        };
        var shown = G1000.G1000CasWatcher.FmaModes(fma)
            .Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => mine.Contains(t.EndsWith(" armed", StringComparison.Ordinal) ? t[..^6] : t));
        string s = string.Join(", ", shown);
        return s.Length > 0 ? s : "Off";
    }

    /// <summary>A mode key's label state now, from the FMA (Approach from its flag and the FMA).</summary>
    private string ModeLabelState(string key)
    {
        bool fresh = FmaIsFresh();
        if (key == "DA40_AP_APR")
            return DescribeApproachState(_apRowState.TryGetValue(key, out bool f) ? (f ? 1 : 0) : null,
                Cas.CurrentFma, fresh);
        bool? row = _apRowState.TryGetValue(key, out bool r) ? r : null;
        bool apr = _apRowState.TryGetValue("DA40_AP_APR", out bool a) && a;
        return DescribeModeState(key, row, Cas.CurrentFma, fresh, apr);
    }

    /// <summary>The panel's mode buttons are labelled the same way as the autopilot window's.</summary>
    public override bool TryDescribeControlState(string varKey, out string stateText)
    {
        stateText = ApModeIsPress(varKey) ? ModeLabelState(varKey) : "";
        return stateText.Length > 0;
    }

    /// <summary>Whether the FMA was read recently enough to describe (two of the watcher's polls).</summary>
    private bool FmaIsFresh() => Cas.LastRowsAt >= Cas.Now().AddSeconds(-4);

    // One per key: a press of another mode inside the check must not cancel this one's answer.
    private readonly Dictionary<string, System.Windows.Forms.Timer> _apPressTimers = new(StringComparer.Ordinal);

    /// <summary>Each GFC 700 mode row's last state and how often it has moved, for the press check.</summary>
    private readonly Dictionary<string, bool> _apRowState = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _apRowMoves = new(StringComparer.Ordinal);

    private static readonly HashSet<string> ApModeRows = new(StringComparer.Ordinal)
    {
        "DA40_AP_MASTER", "DA40_AP_HDG", "DA40_AP_NAV", "DA40_AP_APR", "DA40_AP_BC",
        "DA40_AP_ALT", "DA40_AP_VS", "DA40_AP_FLC"
    };

    private void NoteApRowForPress(string varKey, double value)
    {
        if (!ApModeRows.Contains(varKey)) return;
        bool on = value >= 0.5;
        if (_apRowState.TryGetValue(varKey, out bool was) && was != on)
            _apRowMoves[varKey] = _apRowMoves.GetValueOrDefault(varKey) + 1;
        _apRowState[varKey] = on;
    }

    private void WatchApPress(string varKey, ScreenReaderAnnouncer announcer)
    {
        if (!GetVariables().TryGetValue(varKey, out var def)) return;
        int before = Cas.FmaChanges;
        int rowBefore = _apRowMoves.GetValueOrDefault(varKey);
        DateTime pressedAt = Cas.Now();
        double? flagBefore = _apRowState.TryGetValue(varKey, out bool st) ? (st ? 1 : 0) : null;
        string approachBefore = ApproachModesOnFma(Cas.CurrentFma);

        if (_apPressTimers.Remove(varKey, out var old)) { old.Stop(); old.Dispose(); }
        var timer = new System.Windows.Forms.Timer { Interval = ApPressCheckMs };
        _apPressTimers[varKey] = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_apPressTimers.TryGetValue(varKey, out var mine) && mine == timer) _apPressTimers.Remove(varKey);
            timer.Dispose();

            if (varKey == "DA40_AP_APR")
            {
                double? flagNow = _apRowState.TryGetValue(varKey, out bool now) ? (now ? 1 : 0) : null;
                string? say = ApproachPressAnswer(flagBefore, flagNow, approachBefore,
                    ApproachModesOnFma(Cas.CurrentFma), Cas.LastRowsAt >= pressedAt.AddSeconds(1));
                if (say != null) announcer.AnnounceImmediate(say);
                return;
            }

            bool rowMoved = _apRowMoves.GetValueOrDefault(varKey) != rowBefore;
            if (ApPressUnanswered(before, Cas.FmaChanges, Cas.LastRowsAt, pressedAt, rowMoved))
                announcer.AnnounceImmediate(ApPressNoChangeText(def.DisplayName));
        };
        timer.Start();
    }

    private void StopApPressWatch()
    {
        foreach (var t in _apPressTimers.Values)
            try { t.Stop(); t.Dispose(); } catch { }
        _apPressTimers.Clear();
    }

    /// <summary>
    /// Whether a settled AFCS status is worth saying. ⚠️ "NOT STARTED" NEVER IS: the self test
    /// and pre-flight test return to 0 only when the AFCS loses power or resets, and the avionics
    /// master off said "Autopilot Self Test: not started. Autopilot Pre-flight Test: not started"
    /// over a shutdown (VCRI, 2026-10-19), reading like two results. The power-up's "running" and
    /// its outcome still speak (DA40A-5); the failure flag keeps both edges.
    /// </summary>
    internal static bool AfcsStatusIsNews(string key, double was, double now)
    {
        if (Math.Abs(was - now) <= 0.01) return false;
        bool test = key == "DA40_AP_SELFTEST" || key == "DA40_AP_PFT";
        return !(test && Math.Round(now) == 0);
    }

    private bool NoteAfcsStatus(string varKey, double value, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (Array.IndexOf(AfcsStatusKeys, varKey) < 0) return false;
        _afcsAnnouncer = announcer;

        // The first reading is the baseline, never news.
        if (!_afcsSpoken.ContainsKey(varKey)) { _afcsSpoken[varKey] = value; return true; }

        _afcsPending[varKey] = value;
        if (_afcsSettle == null)
        {
            _afcsSettle = new System.Windows.Forms.Timer { Interval = AfcsStatusSettleMs };
            _afcsSettle.Tick += (_, _) => UnderLoadSettle(_afcsAnnouncer, FlushAfcsStatus);
        }
        _afcsSettle.Stop();
        _afcsSettle.Start();
        return true;
    }

    private void FlushAfcsStatus()
    {
        _afcsSettle?.Stop();
        if (_afcsAnnouncer == null) return;
        var muted = Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet;
        var vars = GetVariables();
        var parts = new List<string>();
        foreach (string key in AfcsStatusKeys)
        {
            if (!_afcsPending.TryGetValue(key, out double v)) continue;
            bool changed = !_afcsSpoken.TryGetValue(key, out double was) || AfcsStatusIsNews(key, was, v);
            _afcsSpoken[key] = v;
            if (!changed || muted.Contains(key) || !vars.TryGetValue(key, out var def)) continue;
            string state = def.ValueDescriptions.TryGetValue(Math.Round(v), out var d) ? d : v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            parts.Add(def.DisplayName + ": " + state);
        }
        _afcsPending.Clear();
        if (parts.Count > 0) _afcsAnnouncer.AnnounceImmediate(string.Join(". ", parts));
    }

    /// <summary>
    /// The GFC 700 mode buttons the FMA already speaks.
    ///
    /// ⚠️ EVERY MODE CHANGE WAS SAID TWICE. Pressing FLC in flight produced "Altitude Hold:
    /// Off" from the generic announcer AND "Autopilot AP, ROL, GPS armed, FLC, ALTS armed" from
    /// the CAS watcher's FMA; engaging the autopilot added "Flight Director: On", "Autopilot:
    /// On" and "Navigation Mode: On" to the FMA's own sentence (2026-10-12). The FMA is what a
    /// sighted pilot reads, and it says active and armed modes in one breath, so these combos
    /// are silent. The autopilot master keeps ONE word: going OFF, which the FMA deliberately
    /// does not say (CasMonitor) and which interrupts a sighted pilot with its own tone.
    /// </summary>
    internal static bool ApModeIsSpokenByFma(string key, double value) => key switch
    {
        "DA40_AP_HDG" or "DA40_AP_NAV" or "DA40_AP_APR" or "DA40_AP_BC" or
        "DA40_AP_ALT" or "DA40_AP_VS" or "DA40_AP_FLC" => true,
        "DA40_AP_MASTER" or "DA40_AP_FD" => value >= 0.5,
        _ => false
    };

    /// <summary>
    /// A selected value: a typed entry, never a slider. MainForm's TrackBar is hardcoded
    /// 0-100 and maps its value as a percentage of that range, which is meaningless for an
    /// altitude or a vertical speed; a key ending _SET renders as a typed field instead.
    /// </summary>
    private static void AddApValue(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display, string units, string format)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = units,
            UpdateFrequency = UpdateFrequency.Continuous,
            // ⚠️ REQUIRED, and it is not about announcing. Batch membership is
            // Continuous AND IsAnnounced, so with this false the selected values never
            // reached the cache at all: output mode + A answered "Selected not available",
            // and the settle announcer never saw a knob move because ProcessSimVarUpdate
            // was never called for them. Exactly the fault the radios had. The generic
            // announcer is kept out by NoteRadioChange returning true, not by this flag.
            IsAnnounced = true,
            // ⚠️ FAST-SAMPLED, because a knob turned on external hardware was answered one
            // to two seconds late (reported from the cockpit as laggy): the 1 Hz batch took
            // up to a second to notice the knob had stopped and the settle another 1.1 s on
            // top. A per-var SIM_FRAME subscription (seeded at connect, so a static value is
            // still cached) delivers within a frame, and ApSettleMs is sized for that.
            ExcludeFromBatch = true,
            HighFrequency = true,
            Format = format
        };
    }


    /// <summary>
    /// The GFC 700's own health, which is read-only and belongs in the scan rather than
    /// among the controls: there is nothing for a pilot to press here, only something to
    /// check before relying on the autopilot.
    /// </summary>
    private static readonly List<string> AutopilotDisplayRows = new()
    {
        // Found by diffing FS Copilot's COWS_DA40NG.yaml against this definition.
        "DA40_AP_SELFTEST",
        "DA40_AP_PFT",
        "DA40_AP_FAILED",
        // Whether the G1000 shows the FMA at all: the model hides it whenever the autopilot
        // computer is dead (Options.cs).
        "DA40_OPT_KILL_FMA"
    };

    private static readonly List<string> AutopilotControls = new()
    {
        "DA40_AP_TOGA",
        "DA40_AP_MASTER",
        "DA40_AP_HDG",
        "DA40_AP_NAV",
        "DA40_AP_APR",
        "DA40_AP_BC",
        "DA40_AP_ALT",
        "DA40_AP_VS",
        "DA40_AP_FLC",
        "DA40_AP_ALT_SET",
        "DA40_AP_VS_SET",
        "DA40_AP_IAS_SET",
        "DA40_AP_HDG_SET",
        "DA40_AP_CRS_SET",
    };

    /// <summary>
    /// The Ctrl+3 status display.
    ///
    /// This was EMPTIED once, on the reasoning that every selected value is already a
    /// control and a control shows its own value. The reasoning was wrong and the cost was
    /// immediate: Ctrl+3 on the GFC 700 panel produced a status display with nothing in it
    /// at all. A panel's controls and its STATUS DISPLAY answer different questions - one
    /// is what you operate, the other is what you sweep to see where the autopilot stands -
    /// and a pilot checking the selected altitude before a climb wants the second.
    ///
    /// It was then restored, and the suite objected AGAIN and was right the second time:
    /// ScansDoNotRepeatControls exists because a control reads its own position when you
    /// tab to it, so repeating it on the scan is duplication that also drags an announcing
    /// variable into a list meant to be silent.
    ///
    /// So it stays empty, and that is the honest answer for THIS panel: every meaningful
    /// item on the GFC 700 is something you operate, and there is no read-only state left
    /// over to sweep. The pilot's real need - knowing what is selected without tabbing
    /// through five controls - is met twice over instead: the values ANNOUNCE THEMSELVES
    /// on settle when anything moves them, and output mode + A, H, S and V answer with the
    /// current value AND the selected one together.
    /// </summary>
    // Kept as a named empty list so the reasoning above has something to hang on, but
    // the panel no longer registers a display entry at all - see CowsDA40Definition.cs.
    private static readonly List<string> AutopilotDisplay = new();

    private static readonly List<string> FlightDirectorControls = new() { "DA40_AP_FD" };

    private static readonly List<string> FlightDirectorDisplay = new()
    {
        "DA40_AP_FD_PITCH",
        "DA40_AP_FD_BANK"
    };

    /// <summary>
    /// Every mode takes its own ON and OFF event, so a combo set lands where the pilot
    /// asked regardless of what MSFSBA believed the state was.
    /// </summary>
    // The CDI source as last delivered. Null until both have arrived: assuming GPS before the
    // first batch dropped a NAV 2 course change turned on hardware right after a connect.
    private bool? _cdiOnGps;
    private int? _cdiNavRadio;

    private bool NoteCdiSource(string varName, double value)
    {
        switch (varName)
        {
            // A source change re-syncs the course to the new receiver's, and that course is
            // the CDI's own move, not a knob: it keeps its name (ApModeSyncMs) - a bare "283"
            // after the CDI went to LOC2 on short final said nothing placeable (2026-10-12).
            case "DA40_CDI_GPS":
                if (_cdiOnGps != (value >= 0.5)) _apModeChangedAt = DateTime.UtcNow;
                _cdiOnGps = value >= 0.5; return true;
            case "DA40_CDI_NAV":
                if (_cdiNavRadio != (value >= 1.5 ? 2 : 1)) _apModeChangedAt = DateTime.UtcNow;
                _cdiNavRadio = value >= 1.5 ? 2 : 1; return true;
            default: return false;
        }
    }

    /// <summary>The course key(s) the CRS knob is moving right now: one once the CDI source is
    /// known, all three before it is.</summary>
    private string[] CdiCourseKeys() => CdiCourseKeysFor(_cdiOnGps, _cdiNavRadio);

    internal static string[] CdiCourseKeysFor(bool? onGps, int? navRadio)
        => onGps is null || (onGps == false && navRadio is null)
            ? CourseKeys
            : new[] { CourseKeyFor(onGps.Value, navRadio ?? 1) };

    internal static string CourseKeyFor(bool onGps, int navRadio)
        => onGps ? "DA40_AP_CRS_GPS" : navRadio == 2 ? "DA40_AP_CRS_NAV2" : "DA40_AP_CRS_SET";

    /// <summary>The three courses the CRS knob can be moving; only the CDI's speaks.</summary>
    internal static readonly string[] CourseKeys = { "DA40_AP_CRS_SET", "DA40_AP_CRS_NAV2", "DA40_AP_CRS_GPS" };

    /// <summary>
    /// The course write, choosing the receiver in the sim at the moment it lands - the same
    /// choice the CRS knob makes (GPS: GPS OBS; NAV 2: NAV OBS:2; otherwise NAV OBS:1).
    /// </summary>
    internal static string CourseWrite(int degrees) => FormattableString.Invariant(
        $"(A:GPS DRIVES NAV1, Bool) if{{ {degrees} (>K:GPS_OBS_SET) }} els{{ (A:AUTOPILOT NAV SELECTED, number) 2 == if{{ {degrees} (>K:VOR2_SET) }} els{{ {degrees} (>K:VOR1_SET) }} }}");

    /// <summary>The exact preselect, once the knob step that marks it selected has landed.</summary>
    private static async Task WriteSelectedAltitudeAfterStepAsync(SimConnectManager simConnect, int feet)
    {
        // Measured: 250 ms between the two leaves the exact value and the selection marked.
        // Awaited on the caller's (UI) context, never Task.Run, like every other SimConnect
        // write from this definition.
        await Task.Delay(300);
        simConnect.ExecuteCalculatorCodeUnique(
            FormattableString.Invariant($"{feet} (>A:AUTOPILOT ALTITUDE LOCK VAR, feet)"));
    }

    private bool HandleAutopilotSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        bool on = value >= 0.5;

        string? onOff = varKey switch
        {
            "DA40_AP_MASTER" => on ? "AUTOPILOT_ON" : "AUTOPILOT_OFF",
            // ⚠️ THE MODE KEYS ARE PRESSES, whatever the value (DA40A-28): the plain event,
            // which the G1000 toggles against its OWN state, exactly like the real key. The
            // _ON event was refused while that state disagreed with what BA read.
            "DA40_AP_HDG" => "AP_HDG_HOLD",
            "DA40_AP_NAV" => "AP_NAV1_HOLD",
            "DA40_AP_APR" => "AP_APR_HOLD",
            "DA40_AP_BC" => "AP_BC_HOLD",
            "DA40_AP_ALT" => "AP_ALT_HOLD",
            // ⚠️ AP_VS_HOLD, NEVER AP_VS_HOLD_ON / _OFF: the Working Title GFC 700's state
            // manager has no case for the _HOLD_ pair, which left the autopilot in pitch hold
            // (measured live on the XLS, 2026-10-05). The plain AP_VS_HOLD is in its intercept
            // list (garminsdk.js:11731, 11861), like every other event here.
            "DA40_AP_VS" => "AP_VS_HOLD",
            "DA40_AP_FLC" => "FLIGHT_LEVEL_CHANGE",
            _ => null
        };
        // A press turns the mode the other way from where its row stands (for VS and FLC's
        // target sync, DA40A-24).
        if (ApModeIsPress(varKey))
            on = !(simConnect.GetCachedVariableValue(varKey) is double row && row >= 0.5);

        // ---------- TO/GA ----------
        //
        // The go-around button on the power lever, and the one control on this autopilot
        // that had no way in at all. The vendor's own bindings name it
        // KEY_AUTO_THROTTLE_TO_GA, which is a misleading name on an aeroplane with no
        // autothrottle: what it actually does here is command the FLIGHT DIRECTOR.
        //
        // Verified live rather than assumed: with the flight director off and its pitch
        // command at zero, one AUTO_THROTTLE_TO_GA turned the director ON and set the pitch
        // command to 10 degrees NOSE UP - wings level, ten degrees, which is the go-around
        // attitude. (The SimVar reports -10; MSFS signs flight-director pitch the other way
        // round, which is why DescribeFlightDirector negates it.)
        //
        // It is a BUTTON, not a switch: pressing it again does not un-press it, and the way
        // out of the mode is to select another one or to disconnect.
        if (varKey == "DA40_AP_TOGA")
        {
            simConnect.ExecuteCalculatorCodeUnique("1 (>K:AUTO_THROTTLE_TO_GA)");
            announcer.AnnounceImmediate("Go around, flight director 10 degrees nose up");
            return true;
        }

        if (onOff != null)
        {
            NoteOwnApModePress(varKey, on);
            // Unique, because a mode selected twice running is the same string twice and
            // MobiFlight coalesces byte-identical consecutive commands.
            simConnect.ExecuteCalculatorCodeUnique(FormattableString.Invariant($"1 (>K:{onOff})"));
            WatchApPress(varKey, announcer);
            return true;
        }

        switch (varKey)
        {
            // The flight director is a toggle with no ON/OFF pair, so it is compared in
            // RPN rather than read in C# and written back - the same shape the electrical
            // switches use, and for the same reason.
            case "DA40_AP_FD":
                simConnect.ExecuteCalculatorCodeUnique(
                    FormattableString.Invariant($"(A:AUTOPILOT FLIGHT DIRECTOR ACTIVE:1, Bool) {(on ? 0 : 1)} == if{{ 1 (>K:TOGGLE_FLIGHT_DIRECTOR) }}"));
                return true;

            case "DA40_AP_ALT_SET":
            {
                // ⚠️ THE G1000'S OWN RANGE, REFUSED OUTSIDE IT, NEVER CLAMPED. This clamped to
                // 0-18,000 ft, a limit of MSFSBA's own invention, and confirmed the clamped value
                // as if it had been typed: 50000 became "Selected altitude 18000 feet". The
                // preselect itself holds -1,000 to 30,000 ft (measured 2026-10-12: 60000 and
                // -2000 written directly settled at 30000 and -1000).
                if (OutOfRange(announcer, value, -1000, 30000, "0", "the selected altitude range, in feet")) return true;
                int feet = (int)(Math.Round(value / 100.0) * 100.0);
                MarkApTargetSpoken("DA40_AP_ALT_SET", feet);
                // ⚠️ AP_ALT_VAR_SET_ENGLISH IS AN INCREMENT AND IGNORES ITS PARAMETER, so
                // this control never once set the altitude a pilot typed. Measured on the
                // live DA40 with the preselect at 0: "5000 (>K:AP_ALT_VAR_SET_ENGLISH)"
                // left it at 100 - one step of the G1000's preselect knob, not five
                // thousand feet. Writing the SimVar directly reads back exactly, verified
                // the same way ("3000 (>A:AUTOPILOT ALTITUDE LOCK VAR, feet)" -> 3000).
                //
                // ⚠️ ALTITUDE IS THE ONLY ONE LIKE THIS. AP_VS_VAR_SET_ENGLISH,
                // AP_SPD_VAR_SET, HEADING_BUG_SET and VOR1_SET were all re-measured in the
                // same pass and every one landed on the value asked for (700, 90, 123, 45),
                // so do not "harmonise" them onto the A: form on the strength of this one.
                //
                // ⚠️ AND A DIRECT WRITE NEVER COUNTS AS SELECTING AN ALTITUDE. The G1000's
                // altitude-select manager marks the preselect "initialized" only when one of
                // its own key events passes through it, and the GFC 700 refuses FLC until it
                // is (G1000Autopilot.verticalPressed: !isAltSelectInitialized). Measured live
                // on the NG: 3000 typed here, the preselect read 3000, and FLC stayed refused.
                // So one knob step first - it marks the selection - then, after it has
                // landed, the exact altitude. Sent together, the step lands after the write
                // and leaves the preselect 100 ft from the old value (measured).
                simConnect.ExecuteCalculatorCodeUnique("0 (>K:AP_ALT_VAR_INC)");
                _ = WriteSelectedAltitudeAfterStepAsync(simConnect, feet);
                announcer.AnnounceImmediate($"Selected altitude {feet} feet");
                return true;
            }

            case "DA40_AP_VS_SET":
            {
                // The GFC 700's range, measured: +1,500 fpm up (3000 settled at 1500) and at
                // least -3,000 down. Refused outside it, never clamped.
                if (OutOfRange(announcer, value, -3000, 1500, "0", "the vertical speed range, in feet per minute")) return true;
                int fpm = (int)(Math.Round(value / 100.0) * 100.0);
                MarkApTargetSpoken("DA40_AP_VS_SET", fpm);
                simConnect.ExecuteCalculatorCodeUnique(FormattableString.Invariant($"{fpm} (>K:AP_VS_VAR_SET_ENGLISH)"));
                announcer.AnnounceImmediate(fpm == 0
                    ? "Selected vertical speed level"
                    : $"Selected vertical speed {Math.Abs(fpm)} feet per minute {(fpm > 0 ? "up" : "down")}");
                return true;
            }

            case "DA40_AP_IAS_SET":
            {
                // The GFC 700's own FLC reference range, measured: 300 settled at 165 and 20 at
                // 70. Refused outside it, never clamped (60 to 172 was MSFSBA's guess, and 30
                // became a confirmed "Selected airspeed 60 knots").
                if (OutOfRange(announcer, value, 70, 165, "0", "the flight level change speed range, in knots")) return true;
                int kt = (int)Math.Round(value);
                MarkApTargetSpoken("DA40_AP_IAS_SET", kt);
                simConnect.ExecuteCalculatorCodeUnique(FormattableString.Invariant($"{kt} (>K:AP_SPD_VAR_SET)"));
                announcer.AnnounceImmediate($"Selected airspeed {kt} knots");
                return true;
            }

            case "DA40_AP_HDG_SET":
            {
                // Refused outside 0-360, never wrapped: a typed 400 used to set 040 and confirm it
                // as if typed (same rule as the altitude, VS and speed above). 0 and 360 are north.
                if (OutOfRange(announcer, value, 0, 360, "0", "the heading range, in degrees")) return true;
                int deg = ((int)Math.Round(value) % 360 + 360) % 360;
                MarkApTargetSpoken("DA40_AP_HDG_SET", deg);
                simConnect.ExecuteCalculatorCodeUnique(FormattableString.Invariant($"{deg} (>K:HEADING_BUG_SET)"));
                announcer.AnnounceImmediate("Heading bug " + HeadingWords(deg));
                return true;
            }

            case "DA40_AP_CRS_SET":
            {
                // The CDI's course, chosen in the sim at the moment of the write so a stale
                // cache cannot send it to the wrong receiver; the cache only says which
                // settle key to mark as ours.
                if (OutOfRange(announcer, value, 0, 360, "0", "the course range, in degrees")) return true;
                int deg = ((int)Math.Round(value) % 360 + 360) % 360;
                // The spoken value is recorded only for the one receiver the CDI is known to be
                // on: before that, all three are marked ours, and the two not written keep the
                // baseline they really have.
                var courseKeys = CdiCourseKeys();
                if (courseKeys.Length == 1) MarkApTargetSpoken(courseKeys[0], deg);
                else MarkRadioSetByUs(courseKeys);
                simConnect.ExecuteCalculatorCodeUnique(CourseWrite(deg));
                announcer.AnnounceImmediate("Course " + HeadingWords(deg));
                return true;
            }

            // ---------- ONE DETENT OF A KNOB ----------
            //
            // ⚠️ EACH ONE MARKS ITS OWN VALUE'S KEY, NOT THE STEP BUTTON'S. The settle
            // announcer watches the VALUE (DA40_AP_ALT_SET and friends), so marking the
            // button key would suppress nothing and the pilot would hear the step twice -
            // once here and once when the batch delivered the same change. Same reason
            // the typed setters above mark their own keys.
            //
            // ⚠️ UNIQUE, because a knob is turned in bursts and MobiFlight coalesces two
            // byte-identical calc strings in a row - which on a knob means every second
            // detent of a sweep goes missing. The same trap the radio knobs hit.
        }

        return false;
    }

}
