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
/// ON/OFF EVENTS, NEVER TOGGLES. Every mode takes a distinct _ON and _OFF event rather
/// than a toggle, so a combo set is deterministic: a toggle fired against a state MSFSBA
/// misread would put the mode the other way round, and on an autopilot that is the kind of
/// error a blind pilot cannot see and would only discover from the aeroplane's behaviour.
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
        AddApMode(v, "DA40_AP_APR", "AUTOPILOT APPROACH HOLD", "Approach Mode");
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
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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

    /// <summary>An autopilot mode: a two-state combo backed by a stock SimVar.</summary>
    private static void AddApMode(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };
    }

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
        "DA40_AP_FAILED"
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
    private bool HandleAutopilotSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        bool on = value >= 0.5;

        string? onOff = varKey switch
        {
            "DA40_AP_MASTER" => on ? "AUTOPILOT_ON" : "AUTOPILOT_OFF",
            "DA40_AP_HDG" => on ? "AP_HDG_HOLD_ON" : "AP_HDG_HOLD_OFF",
            "DA40_AP_NAV" => on ? "AP_NAV1_HOLD_ON" : "AP_NAV1_HOLD_OFF",
            "DA40_AP_APR" => on ? "AP_APR_HOLD_ON" : "AP_APR_HOLD_OFF",
            "DA40_AP_BC" => on ? "AP_BC_HOLD_ON" : "AP_BC_HOLD_OFF",
            "DA40_AP_ALT" => on ? "AP_ALT_HOLD_ON" : "AP_ALT_HOLD_OFF",
            "DA40_AP_VS" => on ? "AP_VS_HOLD_ON" : "AP_VS_HOLD_OFF",
            "DA40_AP_FLC" => on ? "FLIGHT_LEVEL_CHANGE_ON" : "FLIGHT_LEVEL_CHANGE_OFF",
            _ => null
        };

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
            // Unique, because a mode selected twice running is the same string twice and
            // MobiFlight coalesces byte-identical consecutive commands.
            simConnect.ExecuteCalculatorCodeUnique($"1 (>K:{onOff})");
            return true;
        }

        switch (varKey)
        {
            // The flight director is a toggle with no ON/OFF pair, so it is compared in
            // RPN rather than read in C# and written back - the same shape the electrical
            // switches use, and for the same reason.
            case "DA40_AP_FD":
                simConnect.ExecuteCalculatorCode(
                    $"(A:AUTOPILOT FLIGHT DIRECTOR ACTIVE:1, Bool) {(on ? 0 : 1)} == " +
                    "if{ 1 (>K:TOGGLE_FLIGHT_DIRECTOR) }");
                return true;

            case "DA40_AP_ALT_SET":
            {
                MarkRadioSetByUs("DA40_AP_ALT_SET");
                // The selected altitude is entered in FEET and the event takes feet, but
                // the aeroplane's ceiling is the sane clamp: an entry slip of one digit
                // would otherwise command a climb the aircraft cannot make.
                int feet = (int)Math.Clamp(Math.Round(value / 100.0) * 100.0, 0, 18000);
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
                simConnect.ExecuteCalculatorCodeUnique(
                    $"{feet} (>A:AUTOPILOT ALTITUDE LOCK VAR, feet)");
                announcer.AnnounceImmediate($"Selected altitude {feet} feet");
                return true;
            }

            case "DA40_AP_VS_SET":
            {
                MarkRadioSetByUs("DA40_AP_VS_SET");
                int fpm = (int)Math.Clamp(Math.Round(value / 100.0) * 100.0, -2000, 2000);
                simConnect.ExecuteCalculatorCodeUnique($"{fpm} (>K:AP_VS_VAR_SET_ENGLISH)");
                announcer.AnnounceImmediate(fpm == 0
                    ? "Selected vertical speed level"
                    : $"Selected vertical speed {Math.Abs(fpm)} feet per minute {(fpm > 0 ? "up" : "down")}");
                return true;
            }

            case "DA40_AP_IAS_SET":
            {
                MarkRadioSetByUs("DA40_AP_IAS_SET");
                // Between the flap-limit end of the envelope and Vne. Flight level change
                // pitches for this speed, so a value outside the envelope is a command to
                // stall or to overspeed.
                int kt = (int)Math.Clamp(Math.Round(value), 60, 172);
                simConnect.ExecuteCalculatorCodeUnique($"{kt} (>K:AP_SPD_VAR_SET)");
                announcer.AnnounceImmediate($"Selected airspeed {kt} knots");
                return true;
            }

            case "DA40_AP_HDG_SET":
            {
                MarkRadioSetByUs("DA40_AP_HDG_SET");
                int deg = ((int)Math.Round(value) % 360 + 360) % 360;
                simConnect.ExecuteCalculatorCodeUnique($"{deg} (>K:HEADING_BUG_SET)");
                announcer.AnnounceImmediate($"Heading bug {deg:000}");
                return true;
            }

            case "DA40_AP_CRS_SET":
            {
                MarkRadioSetByUs("DA40_AP_CRS_SET");
                int deg = ((int)Math.Round(value) % 360 + 360) % 360;
                simConnect.ExecuteCalculatorCodeUnique($"{deg} (>K:VOR1_SET)");
                announcer.AnnounceImmediate($"Course {deg:000}");
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
