using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Standby Instruments.
///
/// AFM legend items 17-20: the backup airspeed indicator, backup artificial horizon,
/// backup altimeter and the emergency compass. Only two of those are adjustable — the
/// altimeter's subscale and the horizon's cage knob — so they are the controls, and the
/// rest of the standby panel is reported.
///
/// THE GYRO IS PROPERLY MODELLED, which is why it gets more than an on/off readout. The
/// airframe simulates spin-up and topple: ATT_GYRO_SPEED runs 0 to 1 as the rotor comes
/// up (measured 0.936 on a running engine), ATT_GYRO_RIGID is its rigidity, and
/// ATT_GYRO_TOPPLE rises when it is tumbled. A vacuum-less electric standby horizon that
/// has toppled reads a lie, and a blind pilot has no other way to notice, so all three
/// are on the scan next to the attitude it is showing.
///
/// The cage knob is a HELD control (ASOBO_GT_Push_Button_Held on ATT_CAGE). The airframe
/// zeroes ATT_CAGE every frame, so a single write is discarded — this was the first
/// control that proved the point: written once it read back 0, but re-written every 40 ms
/// it held at 1 and drove ATT_GYRO_CAGE_SET from 0 to 1. It therefore goes through
/// HoldLVar like the ECU test.
///
/// The backup altimeter has its OWN subscale, L:KOHLSMAN SETTING HG:2, stepping 0.01 inHg
/// between 28.00 and 31.50 — a separate setting from the G1000's, which is exactly why
/// the AFM's descent checklist says "Altimeters (2) ... SET". Both must be set.
///
/// The Display Backup button lives here rather than with the audio panel it is physically
/// mounted on: reversionary mode is what a pilot reaches for when a display dies, which is
/// the same emergency the standby instruments exist for.
/// </summary>
public partial class CowsDA40Definition
{
    private const string StandbyPanel = "Standby Instruments";

    /// <summary>
    /// Kept SHORT. The held writer re-writes at 40 ms and the airframe plays the knob
    /// click on every write, so a long hold is audible as a burst of clicking.
    /// ATT_GYRO_CAGE_SET was observed set within about 400 ms, so this is enough.
    /// </summary>
    private const int GyroCageHoldMs = 700;

    private static Dictionary<string, SimVarDefinition> BuildStandbyVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_STBY_ALTIMETER_SET"] = new SimVarDefinition
        {
            // READS THE MIRROR, WRITES THE INPUT - this aeroplane's own rule, and the
            // one place it was not being followed.
            //
            // The subscale's INPUT is "L:KOHLSMAN SETTING HG:2", whose name carries a
            // space AND a colon. That shape is normally a stock SimVar, so it defeats both
            // of MSFSBA's normal paths at once: SetLVar refuses the calculator for it and
            // falls back to a data-def write that lands on the STOCK SimVar of that name
            // (a different variable - measured, the L:var moved to 30.11 while the SimVar
            // stayed at 29.85), and the data-def READ asked for it in "inHg", which makes
            // SimConnect convert a raw number from its base pressure unit. The pilot heard
            // "Standby 0 hectopascals, 0.01 inches" over a subscale that was set correctly.
            //
            // L:STATE_BARO2 is the airframe's own read-only mirror of the same subscale -
            // measured at 30.11 alongside the input - and its name is CLEAN, so it reads
            // through the ordinary path with no special case at all. The write goes to the
            // input through SetStandbyBaro.
            Name = "STATE_BARO2",
            DisplayName = "Standby Altimeter Setting",
            Type = SimVarType.LVar,
            // "number", never "inHg": an L:var holds a raw number and a pressure unit here
            // makes SimConnect convert it. Same trap as the A380's TCAS vertical-speed
            // L:vars, which must be "number" and not "feet per minute".
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            // ⚠️ SIM_FRAME, NOT THE 1 Hz BATCH - a subscale the pilot is TURNING moves faster
            // than the batch samples it, which is what forced the settle to outlast a batch
            // period and left every read-back a beat behind. Same reasoning as the radio
            // frequencies and as G_FORCE's touchdown spike; CHANGED means a still altimeter
            // costs nothing.
            ExcludeFromBatch = true,
            HighFrequency = true,
            // A TEXT FIELD, not a slider. MainForm's TrackBar is hardcoded 0-100 and maps
            // the value as a PERCENTAGE of the slider range — right for a lighting knob,
            // but it reported this subscale as "0 to 100" instead of 28 to 31.5. The key
            // ends in _SET, so dropping RenderAsSlider gives a typed entry instead.
            Format = "F2"
        };

        // ---------- THE KNOB ITSELF ----------
        //
        // ⚠️ THE AEROPLANE HAS THIS KNOB AND MSFSBA COULD ONLY TYPE AT IT. The subscale
        // was a typed box alone, which is not a thing the cockpit has - a sighted pilot
        // grabs INSTRUMENT_Knob_Altimeter_1 and turns it. Same gap as the five GFC 700
        // selected values, found by the same question.
        //
        // ⚠️ THE STEP IS THE MODEL'S OWN RPN, COPIED VERBATIM FROM COWS_DA40NG_IN.xml
        // rather than written afresh - including its clamps, which are the aeroplane's
        // and not ours:
        //     CLOCKWISE      (L:KOHLSMAN SETTING HG:2) 0.01 + 31.5 min (>L:KOHLSMAN ...)
        //     ANTICLOCKWISE  (L:KOHLSMAN SETTING HG:2) 0.01 - 28   max (>L:KOHLSMAN ...)
        // Verified live on the airframe both ways: 29.92 -> 29.93 -> 29.92, with the
        // STATE_BARO2 mirror following each step.
        //
        // ⚠️ IT MUST GO THROUGH THE CALCULATOR, NEVER SetLVar. The input's name carries a
        // SPACE AND A COLON, so SetLVar refuses the calc path and its data-def fallback
        // lands on the STOCK SimVar of that name - a different variable entirely. That is
        // this aeroplane's documented write trap and the typed setter already avoids it.

        // ---------- THE MAIN ALTIMETER, ON A PANEL AT LAST ----------
        //
        // ⚠️ THE TWO ALTIMETERS WERE NOT SEPARATELY TUNABLE FROM ANY PANEL. The standby had
        // a typed box; the G1000's subscale had NO panel control at all - only Ctrl+B, which
        // sets BOTH together, and the display window's own knob keys. So the one thing a
        // standby exists for - setting it differently from the main, or catching that it
        // already is - could not be done from the panels a pilot browses.
        //
        // The step is the PFD bezel's own knob event, verified live from outside the display
        // window (29.899 -> 29.910 inHg on A:KOHLSMAN SETTING HG:1), which is what makes a
        // panel button possible: it is the same SimConnect H-event write the window makes.
        // ⚠️ A SEPARATE _SET KEY, AND THE "_SET" IS LOAD-BEARING, NOT DECORATION.
        // MainForm.PanelBuilder gives a text box + Set button to a key that CONTAINS
        // "_SET" and to nothing else; a numeric control without it falls through to a
        // plain button, which fires the setter with no value the pilot chose. Added to
        // the panel as the bare readout key first, this rendered as "Altimeter Setting
        // button" - a button that could not carry a number.
        //
        // It is write-only (Never, no SimVar of its own) so it does NOT duplicate
        // DA40_G1000_BARO's data definition; CurrentValueSourceKey is what puts the live
        // value in the box when the pilot tabs into it.
        v["DA40_G1000_BARO_SET"] = new SimVarDefinition
        {
            Name = "DA40_G1000_BARO_SET",
            DisplayName = "Altimeter Setting",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            IsAnnounced = false,
            ExcludeFromMonitorManager = true,
            CurrentValueSourceKey = "DA40_G1000_BARO"
        };

        v["DA40_STBY_GYRO_CAGE"] = new SimVarDefinition
        {
            Name = "DA40_STBY_GYRO_CAGE",
            DisplayName = "Cage Attitude Indicator",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        v["DA40_STBY_DISPLAY_BACKUP"] = new SimVarDefinition
        {
            Name = "G1000_REV_FORCE",
            DisplayName = "Display Backup",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Normal",
                [1] = "Reversionary"
            }
        };

        // ---------- Status ----------

        // No separate "Standby Subscale" readout: the setting control above now reads
        // STATE_BARO2 itself, so a second row would report the same number twice under two
        // names - the duplication this aeroplane's panels are explicitly meant not to have.

        AddFlag(v, "DA40_STBY_GYRO_CAGED", "ATT_GYRO_CAGE_SET", "Gyro Caged", "No", "Yes");

        // The standby horizon's own attitude, which is the whole point of having it.
        // What the standby horizon is SHOWING - the instrument's own indication, not the
        // aeroplane's attitude. They drift apart: measured 2.2 degrees indicated against a
        // true -3.0, a five-degree error, on a gyro that had been running a while.
        AddReadout(v, "DA40_STBY_GYRO_PITCH", "ATT_GYRO_REL_PITCH", "Standby Horizon Pitch", "degrees", "F1");
        AddReadout(v, "DA40_STBY_GYRO_BANK", "ATT_GYRO_REL_BANK", "Standby Horizon Bank", "degrees", "F1");

        // Spin-up and topple. A toppled gyro reads a plausible lie; without these there is
        // no way to know the instrument has stopped being trustworthy.
        // The TRUE attitude, so the drift above is visible rather than implied. A sighted
        // pilot spots a leaning standby horizon by comparing it against the PFD; this is
        // that comparison, in numbers.
        AddTrueAttitude(v, "DA40_STBY_TRUE_PITCH", "ATTITUDE INDICATOR PITCH DEGREES", "Actual Pitch");
        AddTrueAttitude(v, "DA40_STBY_TRUE_BANK", "ATTITUDE INDICATOR BANK DEGREES", "Actual Bank");

        // Rotor speed as a percentage. The model spins it 0 to 1 from the EMERGENCY bus
        // (ELEC_BUS_EMER_VOLT / 30), which is why the standby horizon keeps working when
        // the main bus is dead. Below about 10 percent it is not usable.
        v["DA40_STBY_GYRO_SPEED"] = new SimVarDefinition
        {
            Name = "ATT_GYRO_SPEED",
            DisplayName = "Gyro Spin",
            Type = SimVarType.LVar,
            // "number", with Scale doing the 0-1 to percent conversion below. Asking a
            // data definition for an L:var in "percent" invites SimConnect to convert it
            // as well, which would scale the same number twice - the trap that read the
            // standby subscale as zero when it was asked for in inHg.
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Scale = 100.0,
            Format = "F0"
        };

        // Toppled means the instrument has tumbled and is showing a plausible lie.
        AddFlag(v, "DA40_STBY_GYRO_TOPPLE", "ATT_GYRO_TOPPLE", "Gyro Toppled", "No", "Yes, toppled");

        // The remaining standby instruments, which have no controls of their own.
        v["DA40_STBY_AIRSPEED"] = new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Backup Airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        // ⚠️ THE BACKUP ALTITUDE WAS THE MAIN ALTIMETER'S, so the standby subscale had no
        // readable consequence anywhere in MSFSBA.
        //
        // It read the stock INDICATED ALTITUDE, which the sim drives from KOHLSMAN SETTING
        // HG:1 - the G1000's subscale. So a pilot could set the standby subscale, and the
        // row labelled "Backup Altitude" would not move: it was reporting the instrument
        // they had NOT touched, under the name of the one they had. Two altimeters
        // disagreeing is the whole reason this aeroplane has a standby, and the disagreement
        // was invisible.
        //
        // Measured live, ten clicks of the standby subscale (+0.10 inHg) on the ground:
        //     L:PRESSURE_ALT_INDI   67.8 ft -> 155.5 ft
        //     A:INDICATED ALTITUDE  47.3 ft -> 47.3 ft   (unchanged)
        //
        // L:PRESSURE_ALT_INDI is the model's own needle position - the value its altimeter
        // strip and 100-foot needle are both animated from - and it carries the instrument's
        // simulated LAG, which is what a mechanical altimeter actually shows.
        v["DA40_STBY_ALTITUDE"] = new SimVarDefinition
        {
            Name = "PRESSURE_ALT_INDI",
            DisplayName = "Backup Altitude",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        v["DA40_STBY_COMPASS"] = new SimVarDefinition
        {
            Name = "MAGNETIC COMPASS",
            DisplayName = "Emergency Compass",
            Type = SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        return v;
    }

    private static void AddTrueAttitude(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F1"
        };
    }

    private static readonly List<string> StandbyControls = new()
    {
        // Both altimeters, main first, each with its typed box and its knob. They sit
        // together because the question a standby answers is whether the two AGREE.
        // ⚠️ NO UP/DOWN BUTTONS HERE, ON THE PILOT'S RULING. They were added as "the correct
        // way" - a sighted pilot turns the knob - and then removed once both altimeters had
        // a typed box: if you can type the value you can just type the value, and four more
        // buttons on a seven-row panel is clutter that every pilot tabs through forever to
        // reach the two rows that do the work. The knob feel is still available where it
        // belongs, on the PFD bezel keys in the display window.
        //
        // ⚠️ This is NOT a general repeal - the GFC 700's ten step buttons stay, because
        // there the panel is the ONLY way to step those five values; no bezel key reaches
        // them. The rule that came out of it: a step button earns its place only where
        // nothing else can turn that knob.
        "DA40_G1000_BARO_SET",
        "DA40_STBY_ALTIMETER_SET",
        "DA40_STBY_GYRO_CAGE",
        "DA40_STBY_DISPLAY_BACKUP"
    };

    private static readonly List<string> StandbyDisplay = new()
    {
        "DA40_STBY_ALTITUDE",
        "DA40_STBY_AIRSPEED",
        "DA40_STBY_COMPASS",
        "DA40_STBY_GYRO_PITCH",
        "DA40_STBY_GYRO_BANK",
        "DA40_STBY_TRUE_PITCH",
        "DA40_STBY_TRUE_BANK",
        "DA40_STBY_GYRO_CAGED",
        "DA40_STBY_GYRO_SPEED",
        "DA40_STBY_GYRO_TOPPLE"
    };

    /// <summary>
    /// ⚠️ "1453 number" - THE UNIT FIELD IS THE DATA-DEFINITION UNIT, NOT A LABEL.
    ///
    /// A read-only readout renders "{value:Format} {Units}", and an L:var MUST be registered
    /// Units = "number" or SimConnect converts from its own base unit and returns garbage
    /// (this aeroplane's own rule, learned on the standby subscale). The two demands collide:
    /// the honest unit for the data definition is the wrong word to say out loud. So the
    /// UNIT a pilot hears comes from here, and the one SimConnect sees stays "number".
    ///
    /// It only became visible when Backup Altitude moved off the stock INDICATED ALTITUDE
    /// (feet, a real unit) onto the model's own PRESSURE_ALT_INDI.
    /// </summary>
    private bool TryGetStandbyDisplayOverride(string varKey, double value, out string text)
    {
        if (varKey == "DA40_STBY_ALTITUDE")
        {
            text = $"{value:0} feet";
            return true;
        }

        text = "";
        return false;
    }

    /// <summary>
    /// Standby writes. The subscale is a plain latching L:var clamped to the knob's own
    /// travel; the cage knob is held, because the airframe zeroes it every frame.
    /// </summary>
    private bool HandleStandbySet(string varKey, double value, SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_STBY_ALTIMETER_SET":
            {
                // Accept hectopascals or inches. The ranges cannot overlap - inHg runs
                // 28.00 to 31.50 and hPa 948 to 1066 - so magnitude disambiguates.
                double inHg = Math.Clamp(value > 100 ? value / 33.8639 : value, 28.00, 31.50);
                SetStandbyBaro(simConnect, inHg);
                MarkBaroSetByUs();

                // A typed numeric entry gets a spoken confirmation — the pilot needs the
                // exact value back, and it is the one announcement CLAUDE.md explicitly
                // asks for. In BOTH units, since either could have been typed.
                announcer.AnnounceImmediate(
                    $"Standby altimeter {inHg * 33.8639:0} hectopascals, {inHg:0.00} inches");
                return true;
            }

            // ⚠️ UNIQUE, or a second detent in the same direction is a byte-identical calc
            // string and MobiFlight drops it - every other click of a sweep goes missing.
            //
            // ⚠️ AND IT MUST NOT CALL MarkBaroSetByUs(). It did, copied from the TYPED
            // setter beside it, and that made the knob SILENT: the mark opens a 3000 ms
            // own-write grace and FlushBaroSettle returns early inside it, so the one
            // channel that would have spoken the resting value was switched off by the
            // very press that needed it. The mark is right for the typed setter, which
            // announces the value ITSELF and would otherwise say it twice; it is exactly
            // wrong for a detent, which announces nothing and DEPENDS on the settle.
            // Shipped that way and caught by re-reading rather than by hearing it.
            // The G1000 subscale, typed. Same unit convention as everywhere else on this
            // aeroplane - the ranges cannot overlap, so magnitude says which was meant -
            // and the same K:KOHLSMAN_SET write Ctrl+B makes, in millibars times sixteen.
            case "DA40_G1000_BARO_SET":
            {
                double inHg = Math.Clamp(value > 100 ? value / 33.8639 : value, 28.00, 31.50);
                double mb = inHg * 33.8639;
                simConnect.ExecuteCalculatorCode(
                    $"{mb * 16:0.###} (>K:KOHLSMAN_SET)".Replace(",", "."));
                MarkBaroSetByUs();
                announcer.AnnounceImmediate(
                    $"Altimeter {mb:0} hectopascals, {inHg:0.00} inches");
                return true;
            }

            // ⚠️ NO MarkBaroSetByUs HERE EITHER - see the standby detents above for the
            // silent-knob trap this avoids.
            case "DA40_STBY_GYRO_CAGE":
                // Say what it did. A button that makes a noise and reports nothing is
                // indistinguishable from a button that does nothing.
                HoldLVar("ATT_CAGE", GyroCageHoldMs, simConnect);
                announcer.AnnounceImmediate("Caging standby horizon");
                return true;

            case "DA40_STBY_DISPLAY_BACKUP":
                simConnect.SetLVar("G1000_REV_FORCE", value >= 0.5 ? 1 : 0);
                return true;
        }

        return false;
    }

    /// <summary>
    /// Writes the standby altimeter's subscale.
    ///
    /// ⚠️ NOT through SetLVar. This L:var's name carries a SPACE AND A COLON, and SetLVar
    /// deliberately refuses the calculator path for such names because that shape is
    /// normally a stock SimVar ("TRANSPONDER STATE:1"). It falls back to a native
    /// data-definition write against "L:KOHLSMAN SETTING HG:2" — which lands on the STOCK
    /// SimVar of that name instead, a different variable entirely.
    ///
    /// Measured: writing the L:var through the calculator moved it to 30.11 while the
    /// stock KOHLSMAN SETTING HG:2 stayed at 29.85. So this aeroplane really does have a
    /// standby subscale that only the L:var reaches, exactly as the vendor's own bindings
    /// document says — and the general rule about space/colon names has a real exception
    /// here, which is why this write is spelt out rather than routed.
    ///
    /// Unique, because a pilot setting the same value twice must not have the second write
    /// coalesced away by MobiFlight.
    /// </summary>
    private static void SetStandbyBaro(SimConnectManager simConnect, double inHg)
    {
        simConnect.ExecuteCalculatorCodeUnique(
            inHg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            + " (>L:KOHLSMAN SETTING HG:2)");
    }

}
