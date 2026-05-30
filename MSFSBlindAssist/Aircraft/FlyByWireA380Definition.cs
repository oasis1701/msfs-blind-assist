using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for the FlyByWire A380X (development build).
///
/// Controls coverage (this revision): a full panel/section structure spanning
/// the overhead (ELEC, APU, FUEL, HYD, BLEED/AIR-COND, PRESS, VENT, CARGO,
/// ANTI-ICE, FIRE, OXYGEN, CALLS/EVAC, SIGNS, ADIRS, F/CTL computers, ENGINE
/// start, RCDR/misc, interior + exterior lighting), the glareshield (EFIS
/// Control Panel, master warn/caution, OIT — the FCU is intentionally handled
/// separately via the dedicated FCU readout dialogs), the main instrument panel
/// (gear, autobrake, anti-skid, source switching, ISIS), the pedestal (engine
/// start/mode, flaps/speedbrake/trim/park brake, ECAM control panel, weather
/// radar / SURV, transponder, RMP radios) and the display readouts.
///
/// Variable provenance follows the FBW source: the A380X reuses the historical
/// A32NX_ prefix for almost all systems (extending index ranges to 4 engines /
/// gens / IDGs / bleeds / FADECs, 11 fuel tanks, 16 brakes, 4 outflow valves),
/// and reserves A380X_ for genuinely new hardware (RMP suite, EFIS-CP/ND, fuel
/// transfer/jettison, MFD page URIs, battery display knob, ANN/INT-LT, OIT,
/// storm light). See tools/a380-simvars-catalog.md for the full catalog.
///
/// ARINC429-encoded words (most FQMS / PRESS / CPIOM outputs) and numeric-code
/// display strings (EWD/SD message lines, FMA modes) are deliberately NOT
/// surfaced as raw readouts — they would read as garbage. The ECAM upper/lower
/// (EWD memos + SD pages) readout is a dedicated follow-up that needs a ported
/// message-code lookup (see tools/ecam-display-readout-notes.md).
///
/// MCD U / flyPad are driven live through the Coherent GT debugger (no
/// injection) — see CoherentDebuggerClient / CoherentEFBClient and the
/// FBWA380MCDUForm / FBWA380EFBForm.
/// </summary>
public class FlyByWireA380Definition : BaseAircraftDefinition,
    ISupportsBridgedMCDU,
    ISupportsBridgedEFB
{
    public override string AircraftName => "FlyByWire A380X";
    public override string AircraftCode => "FBW_A380";

    // A380 FCU uses the same direct-set dialog pattern as the A320.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // ===================================================================
    // Variables
    // ===================================================================
    public override Dictionary<string, SimVarDefinition> GetVariables()
    {
        var vars = GetBaseVariables();

        // ---- local builders ------------------------------------------
        // Writable L:var rendered as a combo box of named positions.
        void Sel(string key, string display, Dictionary<double, string> vd,
                 bool button = false, bool reverse = false)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = display,
                Type = SimVarType.LVar,
                // Continuous + announced so a switch/selector change is spoken
                // automatically — exactly how the PMDG 777 marks its writable
                // switch combos (e.g. ELEC_Battery): Continuous + IsAnnounced.
                // Users can mute any individual var via the Monitor Manager (Ctrl+M).
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = vd,
                // Every writable L:var is a multi-state value, so render it as a
                // combo box (NOT a button). A button only writes one value and a
                // second press does nothing; a combo lets the user pick either
                // state and writes whatever they choose. Real momentary actions
                // with no readable state are the K-event Evt() entries, which the
                // framework renders as buttons because their Type is Event.
                RenderAsButton = false,
                ReverseDisplayOrder = reverse
            };
        }
        // Bool L:var: Off / On.
        void OnOff(string key, string display, bool button = false) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "Off", [1] = "On" });
        // Bool L:var: Off / Auto (FBW "_IS_AUTO" pushbuttons).
        void OffAuto(string key, string display, bool button = false) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "Off", [1] = "Auto" });
        // Latching/momentary PB L:var as a combo (Released / Pressed).
        void Press(string key, string display) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "Released", [1] = "Pressed" });
        // Read-only numeric L:var readout.
        void Read(string key, string display, string units = "number")
        {
            vars[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = display,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = units
            };
        }
        // Read-only enum/status L:var readout. Continuous + announced so status
        // and fault transitions (e.g. a fault light coming on) are spoken when
        // they happen — the pilot can't see the annunciator. Mutable per-var via
        // the Monitor Manager (Ctrl+M) if any proves too chatty.
        void ReadEnum(string key, string display, Dictionary<double, string> vd)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = display,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = vd
            };
        }
        // Like ReadEnum but NOT auto-announced — for status the sim toggles rapidly
        // (e.g. the A380 PACK_PB_HAS_FAULT oscillates 0/1 in flight, and the
        // crossbleed valve open-state), which would otherwise spam the screen
        // reader. Still registered + readable in the panel; just not spoken on
        // every change. User can't re-enable via Ctrl+M (it's not in the announced
        // monitor set) — that's intentional for known-noisy vars.
        void ReadEnumQuiet(string key, string display, Dictionary<double, string> vd)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = display,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = false,
                ValueDescriptions = vd
            };
        }
        // Continuously-monitored, auto-announced status L:var (warnings/state).
        void Mon(string key, string display, Dictionary<double, string> vd)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = display,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = vd
            };
        }
        // Continuously-monitored NUMERIC L:var that is announced live on change
        // via a custom ProcessSimVarUpdate branch (no ValueDescriptions). Used for
        // values the pilot sets and wants spoken as they change (e.g. EIS baro).
        void MonNum(string key, string display, string units = "number")
        {
            vars[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = display,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = units
            };
        }
        // Stock MSFS SimVar readback.
        void Stock(string key, string name, string display, string units = "bool",
                   Dictionary<double, string>? vd = null)
        {
            vars[key] = new SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = units,
                ValueDescriptions = vd ?? new Dictionary<double, string>()
            };
        }
        // Stock MSFS K: event (optionally parameterised).
        void Evt(string key, string name, string display, uint param = 0)
        {
            vars[key] = new SimVarDefinition
            {
                Name = name,
                DisplayName = display,
                Type = SimVarType.Event,
                EventParam = param
            };
        }

        var onOff = new Dictionary<double, string> { [0] = "Off", [1] = "On" };
        var normFault = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" };
        var signSw = new Dictionary<double, string> { [0] = "On", [1] = "Auto", [2] = "Off" };
        var srcSw = new Dictionary<double, string> { [0] = "Capt", [1] = "Norm", [2] = "F/O" };

        // ============================ OVERHEAD ============================

        // ---- ELEC ----
        foreach (var id in new[] { "1", "2", "ESS", "APU" })
        {
            OffAuto($"A32NX_OVHD_ELEC_BAT_{id}_PB_IS_AUTO", $"Battery {id}");
            ReadEnum($"A32NX_OVHD_ELEC_BAT_{id}_PB_HAS_FAULT", $"Battery {id} Fault", normFault);
        }
        OffAuto("A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO", "Bus Tie");
        Sel("A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL", "AC ESS Feed",
            new Dictionary<double, string> { [0] = "Altn", [1] = "Normal" });
        OffAuto("A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO", "Galley and Cabin");
        OnOff("A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON", "Commercial");
        for (int n = 1; n <= 4; n++)
        {
            Press($"A32NX_OVHD_ELEC_IDG_{n}_PB_IS_RELEASED", $"IDG {n} Disconnect");
            OnOff($"A32NX_OVHD_ELEC_EXT_PWR_{n}_PB_IS_ON", $"External Power {n}");
        }
        OnOff("A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON", "Emergency Generator 1 Line");
        Press("A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED", "RAT and Emergency Generator");
        Sel("A380X_OVHD_ELEC_BAT_SELECTOR_KNOB", "Battery Display Selector",
            new Dictionary<double, string> { [0] = "ESS", [1] = "APU", [2] = "Off", [3] = "Battery 1", [4] = "Battery 2" });
        for (int n = 1; n <= 4; n++) Read($"A32NX_ELEC_BAT_{n}_POTENTIAL", $"Battery {n} Voltage", "volts");

        // ---- APU ----
        OnOff("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "APU Master Switch");
        OnOff("A32NX_OVHD_APU_START_PB_IS_ON", "APU Start", button: true);
        ReadEnum("A32NX_OVHD_APU_START_PB_IS_AVAILABLE", "APU Available",
            new Dictionary<double, string> { [0] = "No", [1] = "Available" });

        // ---- FUEL ----
        OffAuto("A380X_OVHD_FUEL_OUTRTK_XFR_PB_IS_AUTO", "Outer Tank Transfer");
        OffAuto("A380X_OVHD_FUEL_MIDTK_XFR_PB_IS_AUTO", "Mid Tank Transfer");
        OffAuto("A380X_OVHD_FUEL_INRTK_XFR_PB_IS_AUTO", "Inner Tank Transfer");
        OffAuto("A380X_OVHD_FUEL_TRIMTK_XFR_PB_IS_AUTO", "Trim Tank Transfer");
        OnOff("A380X_OVHD_FUEL_EMER_OUTR_XFR_PB_IS_ON", "Emergency Outer Transfer");
        OnOff("A380X_OVHD_FUEL_JETTISON_ARM_PB_IS_ON", "Fuel Jettison Arm");
        OnOff("A380X_OVHD_FUEL_JETTISON_ACTIVE_PB_IS_ON", "Fuel Jettison Active");
        Read("A32NX_TOTAL_FUEL_QUANTITY", "Total Fuel", "kilograms");

        // ---- HYDRAULICS ----
        for (int n = 1; n <= 4; n++)
        {
            OffAuto($"A32NX_OVHD_HYD_ENG_{n}A_PUMP_PB_IS_AUTO", $"Engine {n} Pump A");
            OffAuto($"A32NX_OVHD_HYD_ENG_{n}B_PUMP_PB_IS_AUTO", $"Engine {n} Pump B");
        }
        foreach (var sys in new[] { "G", "Y" })
            foreach (var ab in new[] { "A", "B" })
                OffAuto($"A32NX_OVHD_HYD_EPUMP{sys}{ab}_ON_PB_IS_AUTO",
                        $"{(sys == "G" ? "Green" : "Yellow")} Electric Pump {ab}");
        Press("A32NX_OVHD_HYD_RAT_MAN_ON_IS_PRESSED", "RAT Manual On");
        Read("A32NX_RAT_RPM", "RAT R P M", "rpm"); // Ram Air Turbine speed (0 = stowed)

        // ---- BLEED / AIR ----
        OnOff("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", "APU Bleed");
        for (int n = 1; n <= 4; n++) OffAuto($"A32NX_OVHD_PNEU_ENG_{n}_BLEED_PB_IS_AUTO", $"Engine {n} Bleed");
        Sel("A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", "Crossbleed",
            new Dictionary<double, string> { [0] = "Close", [1] = "Auto", [2] = "Open" });
        Sel("A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION", "Pack Flow",
            new Dictionary<double, string> { [0] = "Manual", [1] = "Low", [2] = "Normal", [3] = "High" });
        for (int n = 1; n <= 2; n++)
        {
            OnOff($"A32NX_OVHD_COND_PACK_{n}_PB_IS_ON", $"Pack {n}");
            OnOff($"A32NX_OVHD_COND_HOT_AIR_{n}_PB_IS_ON", $"Hot Air {n}");
        }
        OnOff("A32NX_OVHD_COND_RAM_AIR_PB_IS_ON", "Ram Air");

        // ---- AIR COND (temperature) ----
        // Temperature selectors are SETTABLE target temps (18-30 C). The FBW knob
        // var ranges 0-300 mapped to 18-30 C, so we expose a Celsius numeric input
        // (key ends "_SET" -> MainForm renders a numeric textbox) and convert in
        // HandleUIVariableSet. Verified in fbw-a380x air_conditioning/mod.rs.
        vars["COND_CKPT_TEMP_SET"] = new SimVarDefinition
        {
            Name = "A32NX_OVHD_COND_CKPT_SELECTOR_KNOB", DisplayName = "Cockpit Target Temperature (18-30 C)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };
        vars["COND_CABIN_TEMP_SET"] = new SimVarDefinition
        {
            Name = "A32NX_OVHD_COND_CABIN_SELECTOR_KNOB", DisplayName = "Cabin Target Temperature (18-30 C)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };
        Read("A32NX_COND_CKPT_TEMP", "Cockpit Temperature", "celsius");
        for (int n = 1; n <= 8; n++) Read($"A32NX_COND_MAIN_DECK_{n}_TEMP", $"Main Deck {n} Temperature", "celsius");
        for (int n = 1; n <= 7; n++) Read($"A32NX_COND_UPPER_DECK_{n}_TEMP", $"Upper Deck {n} Temperature", "celsius");

        // ---- CARGO AIR ----
        // Cargo temp selectors: settable target temp 5-25 C (FBW knob 0-300 over
        // that range -> knob = (temp - 5) * 15). Converted in HandleUIVariableSet.
        vars["CARGO_FWD_TEMP_SET"] = new SimVarDefinition
        {
            Name = "A32NX_OVHD_CARGO_AIR_FWD_SELECTOR_KNOB", DisplayName = "Cargo Fwd Target Temperature (5-25 C)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };
        vars["CARGO_BULK_TEMP_SET"] = new SimVarDefinition
        {
            Name = "A32NX_OVHD_CARGO_AIR_BULK_SELECTOR_KNOB", DisplayName = "Cargo Bulk Target Temperature (5-25 C)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };
        OnOff("A32NX_OVHD_CARGO_AIR_ISOL_VALVES_FWD_PB_IS_ON", "Cargo Fwd Isolation Valve");
        OnOff("A32NX_OVHD_CARGO_AIR_ISOL_VALVES_BULK_PB_IS_ON", "Cargo Bulk Isolation Valve");
        OnOff("A32NX_OVHD_CARGO_AIR_HEATER_PB_IS_ON", "Cargo Heater");

        // ---- PRESSURIZATION ----
        OffAuto("A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO", "Cabin Altitude Mode");
        OffAuto("A32NX_OVHD_PRESS_MAN_VS_CTL_PB_IS_AUTO", "Cabin V/S Mode");
        OnOff("A32NX_OVHD_PRESS_DITCHING_PB_IS_ON", "Ditching");

        // ---- VENTILATION ----
        OnOff("A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON", "Cabin Fans");
        OnOff("A32NX_OVHD_VENT_AIR_EXTRACT_PB_IS_ON", "Air Extract");

        // ---- ANTI-ICE ----
        OnOff("A32NX_MAN_PITOT_HEAT", "Probe / Window Heat");
        Press("XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED", "Wing Anti-Ice");
        for (int n = 1; n <= 4; n++)
        {
            // Settable On/Off combo — fires K:ANTI_ICE_SET_ENGn via
            // HandleUIVariableSet. Verified live: the stock "ENG ANTI ICE:n"
            // SimVar AND the XMLVAR momentary push both fail to drive the A380
            // engine anti-ice; only the SET event toggles it.
            Sel($"ENG{n}_ANTI_ICE", $"Engine {n} Anti-Ice", onOff);
            // Live state readout from the stock SimVar.
            Stock($"ENG_ANTI_ICE:{n}", $"ENG ANTI ICE:{n}", $"Engine {n} Anti-Ice State", "bool", onOff);
        }
        Mon("A32NX_ICING_STATE_ICING_STICK_INDICATOR", "Icing Conditions",
            new Dictionary<double, string> { [0] = "None", [1] = "Icing" });

        // ---- FIRE ----
        for (int n = 1; n <= 4; n++)
        {
            Press($"A32NX_FIRE_BUTTON_ENG{n}", $"Engine {n} Fire Button");
            Mon($"A32NX_FIRE_DETECTED_ENG{n}", $"Engine {n} Fire",
                new Dictionary<double, string> { [0] = "Normal", [1] = "FIRE" });
        }
        Press("A32NX_FIRE_BUTTON_APU", "APU Fire Button");
        Mon("A32NX_FIRE_DETECTED_APU", "APU Fire",
            new Dictionary<double, string> { [0] = "Normal", [1] = "FIRE" });
        Press("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", "Fire Test");

        // ---- OXYGEN ----
        Sel("PUSH_OVHD_OXYGEN_CREW", "Crew Oxygen",
            new Dictionary<double, string> { [0] = "Auto", [1] = "Off" });
        OnOff("A32NX_OXYGEN_MASKS_DEPLOYED", "Passenger Masks Deployed");
        ReadEnum("A32NX_OXYGEN_PASSENGER_LIGHT_ON", "Passenger Oxygen Light", onOff);
        Press("A32NX_OXYGEN_TMR_RESET", "Oxygen Timer Reset");

        // ---- CALLS / EVAC ----
        OnOff("A32NX_CALLS_EMER_ON", "Emergency Call");
        OnOff("A32NX_EVAC_COMMAND_TOGGLE", "Evacuation Command");
        Sel("A32NX_EVAC_CAPT_TOGGLE", "Evacuation Capt / Purser",
            new Dictionary<double, string> { [0] = "Purser", [1] = "Capt and Purser" });

        // ---- SIGNS ----
        Sel("XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position", "Seat Belts", signSw);
        Sel("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", "No Smoking", signSw);
        Sel("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", "Emergency Exit Lights", signSw);

        // ---- ADIRS ----
        for (int n = 1; n <= 3; n++)
        {
            Sel($"A32NX_OVHD_ADIRS_IR_{n}_MODE_SELECTOR_KNOB", $"ADIRS {n} Mode",
                new Dictionary<double, string> { [0] = "Off", [1] = "Nav", [2] = "Att" });
            OnOff($"A32NX_OVHD_ADIRS_IR_{n}_PB_IS_ON", $"IR {n}");
            OnOff($"A32NX_OVHD_ADIRS_ADR_{n}_PB_IS_ON", $"ADR {n}");
        }
        Read("A32NX_ADIRS_REMAINING_IR_ALIGNMENT_TIME", "IR Alignment Time Remaining", "seconds");

        // ---- FLIGHT CONTROL COMPUTERS ----
        for (int n = 1; n <= 3; n++)
        {
            OnOff($"A32NX_PRIM_{n}_PUSHBUTTON_PRESSED", $"PRIM {n}");
            OnOff($"A32NX_SEC_{n}_PUSHBUTTON_PRESSED", $"SEC {n}");
        }

        // ---- ENGINE START (overhead) ----
        for (int n = 1; n <= 4; n++)
        {
            OnOff($"A32NX_OVHD_FADEC_{n}", $"FADEC {n} Ground Power");
            OnOff($"A32NX_ENGMANSTART{n}_TOGGLE", $"Engine {n} Manual Start");
        }

        // ---- RECORDER / MISC OVERHEAD ----
        OnOff("A32NX_RCDR_GROUND_CONTROL_ON", "Recorder Ground Control");
        OnOff("A32NX_ELT_ON", "ELT");
        OnOff("A32NX_AVIONICS_COMPLT_ON", "Avionics Compartment Light");
        OnOff("A380X_OVHD_STORM_LT", "Storm Light");
        OnOff("A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE", "Cockpit Door Video");

        // ---- INTERIOR LIGHTING ----
        Sel("A380X_OVHD_ANN_LT_POSITION", "Annunciator Lights",
            new Dictionary<double, string> { [0] = "Test", [1] = "Bright", [2] = "Dim" });
        Sel("A32NX_OVHD_INTLT_ANN", "Integral Lights",
            new Dictionary<double, string> { [0] = "Test", [1] = "Bright", [2] = "Dim" });

        // ---- EXTERIOR LIGHTING ----
        // On/Off COMBOS (not toggle buttons) backed by the stock *_LIGHTS_SET
        // events. The combo's value is the stock LIGHT * simvar, monitored
        // Continuous + announced so it reflects state and auto-announces changes
        // (same as every other switch). Writing the combo is intercepted in
        // HandleUIVariableSet, which fires the matching SET event with 0/1.
        void Light(string key, string simvar, string display)
        {
            vars[key] = new SimVarDefinition
            {
                Name = simvar,
                DisplayName = display,
                Type = SimVarType.SimVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "bool",
                RenderAsButton = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            };
        }
        Light("LIGHT_BEACON", "LIGHT BEACON", "Beacon");
        Light("LIGHT_NAV", "LIGHT NAV", "Navigation Lights");
        // Strobe is a 3-position switch (On / Auto / Off) backed by the writable
        // FBW L:var LIGHTING_STROBE_0 (verified live), same as the A320 — a plain
        // On/Off via STROBES_SET can't express AUTO. Rendered as a combo, written
        // via SetLVar by the framework, auto-announced. ReverseDisplayOrder so the
        // list reads Off / Auto / On top-to-bottom.
        Sel("LIGHTING_STROBE_0", "Strobe",
            new Dictionary<double, string> { [0] = "On", [1] = "Auto", [2] = "Off" }, false, true);
        Light("LIGHT_WING", "LIGHT WING", "Wing Lights");
        Light("LIGHT_LOGO", "LIGHT LOGO", "Logo Lights");
        Light("LIGHT_LANDING", "LIGHT LANDING", "Landing Lights");
        // Nose light is a 3-position switch (Takeoff / Taxi / Off) on the writable
        // FBW L:var A380X_OVHD_EXTLT_NOSE — verified live that writing it drives the
        // actual taxi/landing lights. Replaces the old on/off "Taxi Lights" proxy.
        Sel("A380X_OVHD_EXTLT_NOSE", "Nose Light",
            new Dictionary<double, string> { [0] = "Takeoff", [1] = "Taxi", [2] = "Off" }, false, true);

        // ============================ GLARESHIELD ============================

        // ---- Master Warning / Caution ----
        Press("PUSH_AUTOPILOT_MASTERWARN_L", "Master Warning (Capt)");
        Press("PUSH_AUTOPILOT_MASTERWARN_R", "Master Warning (F/O)");
        Press("PUSH_AUTOPILOT_MASTERCAUT_L", "Master Caution (Capt)");
        Press("PUSH_AUTOPILOT_MASTERCAUT_R", "Master Caution (F/O)");
        Mon("A32NX_MASTER_WARNING", "Master Warning",
            new Dictionary<double, string> { [0] = "Off", [1] = "WARNING" });
        Mon("A32NX_MASTER_CAUTION", "Master Caution",
            new Dictionary<double, string> { [0] = "Off", [1] = "CAUTION" });
        Mon("A32NX_AUTOPILOT_AUTOLAND_WARNING", "Autoland Warning",
            new Dictionary<double, string> { [0] = "Off", [1] = "AUTOLAND" });
        // NOTE: A32NX_APU_EGT_WARNING is NOT a 0/1/2 enum on the A380 — it's an
        // ARINC429 word (verified live: reads ~1.147e9, decoding to ~896, the EGT
        // warning threshold), so announcing/displaying it raw was garbage. Dropped:
        // the actual APU EGT is shown decoded in the SD window, and a real APU EGT
        // exceedance is an ECAM warning (covered by the EWD monitor).
        Mon("A32NX_AUTOTHRUST_THRUST_LEVER_WARNING", "Thrust Lever Warning",
            new Dictionary<double, string> { [0] = "Off", [1] = "WARNING" });
        Mon("A32NX_PERFORMANCE_WARNING", "Performance Warning",
            new Dictionary<double, string> { [0] = "Off", [1] = "WARNING" });
        Mon("A32NX_TCAS_FAULT", "TCAS Fault",
            new Dictionary<double, string> { [0] = "Normal", [1] = "FAULT" });

        // ---- EFIS Control Panel (per side) ----
        foreach (var side in new[] { "L", "R" })
        {
            string who = side == "L" ? "Capt" : "F/O";
            OnOff($"A380X_EFIS_{side}_LS_BUTTON_IS_ON", $"{who} LS");
            OnOff($"A380X_EFIS_{side}_VV_BUTTON_IS_ON", $"{who} V/V");
            OnOff($"A380X_EFIS_{side}_CSTR_BUTTON_IS_ON", $"{who} Constraints");
            OnOff($"A380X_EFIS_{side}_ARPT_BUTTON_IS_ON", $"{who} Airport");
            OnOff($"A380X_EFIS_{side}_TRAF_BUTTON_IS_ON", $"{who} Traffic");
            Sel($"A32NX_EFIS_{side}_ND_MODE", $"{who} ND Mode",
                new Dictionary<double, string> { [0] = "Rose ILS", [1] = "Rose VOR", [2] = "Rose Nav", [3] = "Arc", [4] = "Plan" });
            Sel($"A32NX_EFIS_{side}_ND_RANGE", $"{who} ND Range",
                new Dictionary<double, string> { [0] = "Zoom", [1] = "10", [2] = "20", [3] = "40", [4] = "80", [5] = "160", [6] = "320", [7] = "640" });
            for (int nav = 1; nav <= 2; nav++)
                Sel($"A32NX_EFIS_{side}_NAVAID_{nav}_MODE", $"{who} Navaid {nav}",
                    new Dictionary<double, string> { [0] = "Off", [1] = "ADF", [2] = "VOR" });
        }

        // ---- OIT side switches ----
        var oitSide = new Dictionary<double, string> { [0] = "NSS Avionics", [1] = "NSS Flight Ops" };
        Sel("A380X_SWITCH_OIT_SIDE_LEFT", "Capt OIT Side", oitSide);
        Sel("A380X_SWITCH_OIT_SIDE_RIGHT", "F/O OIT Side", oitSide);

        // ============================ INSTRUMENT PANEL ============================

        // ---- Gear ----
        Sel("A32NX_GEAR_HANDLE_POSITION", "Gear Lever",
            new Dictionary<double, string> { [0] = "Up", [1] = "Down" });
        Sel("A32NX_LG_GRVTY_SWITCH_POS", "Gravity Gear Extension",
            new Dictionary<double, string> { [0] = "Reset", [1] = "Off", [2] = "Down" });

        // ---- Autobrake / Anti-skid ----
        Sel("A32NX_AUTOBRAKES_SELECTED_MODE", "Autobrake",
            new Dictionary<double, string> { [0] = "Disarm", [1] = "BTV", [2] = "Low", [3] = "L2", [4] = "L3", [5] = "High" });
        Press("A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED", "RTO Autobrake Arm");
        Stock("ANTISKID_BRAKES_ACTIVE", "ANTISKID BRAKES ACTIVE", "Anti-Skid", "bool", onOff);
        ReadEnum("A32NX_AUTOBRAKES_ARMED_MODE", "Autobrake Armed Mode",
            new Dictionary<double, string> { [0] = "Disarmed", [1] = "BTV", [2] = "Low", [3] = "L2", [4] = "L3", [5] = "High", [6] = "RTO" });
        ReadEnum("A32NX_BTV_STATE", "Brake-To-Vacate State",
            new Dictionary<double, string> { [0] = "Disabled", [1] = "Armed", [2] = "Rotation Optimised", [3] = "Decel", [4] = "End of Braking" });
        for (int n = 1; n <= 16; n++) Read($"A32NX_BRAKE_TEMPERATURE_{n}", $"Brake {n} Temperature", "celsius");

        // ---- Source switching / ISIS ----
        Sel("A32NX_ATT_HDG_SWITCHING_KNOB", "Attitude / Heading Source", srcSw);
        Sel("A32NX_AIR_DATA_SWITCHING_KNOB", "Air Data Source", srcSw);
        Sel("A32NX_EIS_DMC_SWITCHING_KNOB", "EIS / DMC Source", srcSw);
        Sel("A32NX_CHRONO_ET_SWITCH_POS", "Elapsed Time",
            new Dictionary<double, string> { [0] = "Run", [1] = "Stop", [2] = "Reset" });

        // ============================ PEDESTAL ============================

        // ---- Engine state / start ----
        for (int n = 1; n <= 4; n++)
            Mon($"A32NX_ENGINE_STATE:{n}", $"Engine {n}",
                new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Starting", [3] = "Restarting", [4] = "Shutting Down" });

        // ---- Flaps / Speedbrake / Trim / Park brake ----
        ReadEnum("A32NX_FLAPS_HANDLE_INDEX", "Flaps",
            new Dictionary<double, string> { [0] = "Up", [1] = "1", [2] = "2", [3] = "3", [4] = "Full" });
        Read("A32NX_SPOILERS_HANDLE_POSITION", "Speed Brake Handle");
        ReadEnum("A32NX_SPOILERS_ARMED", "Ground Spoilers", new Dictionary<double, string> { [0] = "Disarmed", [1] = "Armed" });
        OnOff("A32NX_PARK_BRAKE_LEVER_POS", "Parking Brake");

        // On-demand readout sources for global hotkeys (not paneled, not announced).
        Stock("KOHLSMAN_HG", "KOHLSMAN SETTING HG", "Altimeter", "inHg");
        Stock("GROSS_WEIGHT_KG", "TOTAL WEIGHT", "Gross Weight", "kilograms");

        // ECAM System Display (SD) window read sources — raw doubles (the window
        // decodes the ARINC429 words itself). Registered on-request, add-if-absent
        // so existing defs (e.g. engine vars) are not clobbered. The window
        // (FBWA380SystemDisplayForm) is the single source of truth for the names.
        // ISIS standby simvars need explicit units (the generic auto-register below
        // forces "number", which is wrong for pitch/altitude/baro).
        Stock("PLANE PITCH DEGREES", "PLANE PITCH DEGREES", "Standby Pitch", "degrees");
        Stock("PLANE BANK DEGREES", "PLANE BANK DEGREES", "Standby Bank", "degrees");
        Stock("PLANE HEADING DEGREES MAGNETIC", "PLANE HEADING DEGREES MAGNETIC", "Standby Heading", "degrees");
        Stock("AIRSPEED INDICATED", "AIRSPEED INDICATED", "Standby Airspeed", "knots");
        Stock("AIRSPEED MACH", "AIRSPEED MACH", "Standby Mach", "mach");
        Stock("INDICATED ALTITUDE", "INDICATED ALTITUDE", "Standby Altitude", "feet");
        Stock("KOHLSMAN SETTING MB", "KOHLSMAN SETTING MB", "Standby Baro Setting", "millibars");

        var windowReadVars = Forms.FBWA380.FBWA380SystemDisplayForm.AllVariableNames()
            .Concat(Forms.FBWA380.FBWA380NavDisplayForm.AllVariableNames())
            .Concat(Forms.FBWA380.FBWA380ISISForm.AllVariableNames());
        foreach (var sdVar in windowReadVars)
        {
            if (vars.ContainsKey(sdVar)) continue;
            bool isStock = !sdVar.StartsWith("A32NX_") && !sdVar.StartsWith("A380X_") && !sdVar.StartsWith("FBW_");
            vars[sdVar] = new SimVarDefinition
            {
                Name = sdVar,
                DisplayName = sdVar,
                Type = isStock ? SimVarType.SimVar : SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                Units = "number"
            };
        }

        // ---- Thrust levers (detent combos) ----
        // Command a thrust-lever detent from a combo. The write is intercepted in
        // HandleUIVariableSet, which fires THROTTLEn_AXIS_SET_EX1 with the detent's
        // axis value; the FBW throttle mapping snaps the lever to that detent.
        // (Synthetic LVar key — never read/written as an L:var; it just gives the
        // framework a settable combo. The displayed value reflects the last
        // command, not live lever position.)
        var detents = new Dictionary<double, string>
        { [0] = "Reverse", [1] = "Reverse Idle", [2] = "Idle", [3] = "Climb", [4] = "Flex/MCT", [5] = "TOGA" };
        void Detent(string key, string display)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key, DisplayName = display, Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest, RenderAsButton = false,
                ValueDescriptions = detents
            };
        }
        Detent("THROTTLE_ALL_DETENT", "All Thrust Levers");
        for (int n = 1; n <= 4; n++) Detent($"THROTTLE_{n}_DETENT", $"Thrust Lever {n}");

        // Thrust lever angles — ALL FOUR monitored so ProcessSimVarUpdate
        // announces the actual detent per engine as the levers move (the raw
        // angle is suppressed there). Previously only engine 1 was monitored.
        for (int n = 1; n <= 4; n++)
            vars[$"A32NX_AUTOTHRUST_TLA:{n}"] = new SimVarDefinition
            {
                Name = $"A32NX_AUTOTHRUST_TLA:{n}", DisplayName = $"Thrust Lever {n} Angle",
                Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true, Units = "degrees"
            };

        // ---- ECAM Control Panel ----
        Sel("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", "System Display Page",
            new Dictionary<double, string>
            {
                [-1] = "None", [0] = "Engine", [1] = "APU", [2] = "Bleed", [3] = "Cond", [4] = "Press",
                [5] = "Door", [6] = "Elec AC", [7] = "Elec DC", [8] = "Fuel", [9] = "Wheel", [10] = "Hyd",
                [11] = "F/Ctl", [12] = "C/B", [13] = "Cruise", [14] = "Status", [15] = "Video"
            });
        foreach (var (k, d) in new[]
        {
            ("ALL", "All"), ("ABNPROC", "Abnormal Proc"), ("CL", "Checklist"), ("CLR", "Clear"),
            ("CLR2", "Clear 2"), ("RCL", "Recall"), ("TOCONFIG", "T.O Config"), ("EMERCANC", "Emergency Cancel"),
            ("UP", "Up"), ("DOWN", "Down"), ("MORE", "More")
        })
            Press($"A32NX_BTN_{k}", $"ECAM {d}");

        // ---- Weather radar / SURV ----
        Sel("A32NX_SWITCH_RADAR_PWS_Position", "Predictive Windshear",
            new Dictionary<double, string> { [0] = "Auto", [1] = "Off" });
        OnOff("A32NX_RADAR_MULTISCAN_AUTO", "WXR Multiscan Auto");
        OnOff("A32NX_RADAR_GCS_AUTO", "WXR Ground Clutter Suppression");

        // ---- Transponder ----
        Evt("XPNDR_IDENT_ON", "XPNDR_IDENT_ON", "Transponder Ident");

        // ---- RMP radios ----
        var rmpState = new Dictionary<double, string> { [0] = "Off (Failed)", [1] = "Off / Standby", [2] = "On", [3] = "On (Failed)" };
        for (int r = 1; r <= 3; r++)
            ReadEnum($"A380X_RMP_{r}_STATE", $"RMP {r} State", rmpState);
        for (int v = 1; v <= 3; v++)
        {
            Read($"FBW_RMP_FREQUENCY_ACTIVE_{v}", $"VHF {v} Active Frequency");
            Read($"FBW_RMP_FREQUENCY_STANDBY_{v}", $"VHF {v} Standby Frequency");
        }

        // ---- Cockpit door ----
        OnOff("A32NX_COCKPIT_DOOR_LOCKED", "Cockpit Door Locked");
        OnOff("A32NX_CABIN_READY", "Cabin Ready");

        // ============================ DISPLAYS / STATUS ============================
        // Settable toggle combo — fires A32NX.FCU_ATHR_PUSH via HandleUIVariableSet
        // when the picked engage state differs from current. "Active" is automatic.
        Sel("A32NX_AUTOTHRUST_STATUS", "Autothrust",
            new Dictionary<double, string> { [0] = "Disengaged", [1] = "Armed", [2] = "Active" });
        Read("A32NX_FMS_PAX_NUMBER", "Passenger Number");
        ReadEnum("A32NX_ECAM_FAILURE_ACTIVE", "ECAM Failure Active", onOff);
        Mon("A32NX_FMGC_FLIGHT_PHASE", "Flight Phase",
            new Dictionary<double, string>
            {
                [0] = "Preflight", [1] = "Takeoff", [2] = "Climb", [3] = "Cruise", [4] = "Descent",
                [5] = "Approach", [6] = "Go Around", [7] = "Done"
            });

        // ============================ ENGINE START + ENGINE READOUTS ============================
        // ENG MASTER 1-4 drive the fuel valves (same as the A320, per engine);
        // the mode selector fans TURBINE_IGNITION_SWITCH_SET out to all 4 engines
        // (handled in HandleUIVariableSet). Engine params are plain L:vars.
        var revVd = new Dictionary<double, string> { [0] = "Stowed", [1] = "Deployed" };
        for (int n = 1; n <= 4; n++)
        {
            Evt($"ENGINE_{n}_MASTER_ON", "FUELSYSTEM_VALVE_OPEN", $"Engine {n} Master On", (uint)n);
            Evt($"ENGINE_{n}_MASTER_OFF", "FUELSYSTEM_VALVE_CLOSE", $"Engine {n} Master Off", (uint)n);
            Read($"A32NX_ENGINE_N1:{n}", $"Engine {n} N1", "percent");
            Read($"A32NX_ENGINE_N2:{n}", $"Engine {n} N2", "percent");
            Read($"A32NX_ENGINE_N3:{n}", $"Engine {n} N3", "percent");
            Read($"A32NX_ENGINE_EGT:{n}", $"Engine {n} EGT", "celsius");
            Read($"A32NX_ENGINE_FF:{n}", $"Engine {n} Fuel Flow", "kilograms per hour");
            // Engine oil PRESSURE is not modelled on the FBW A380 dev build — every
            // candidate var returns garbage (stock ENG OIL PRESSURE ~217000, GENERAL
            // ~6061, A32NX_ENGINE_OIL_PRESSURE 0), so it's omitted rather than shown
            // as a fake value. Oil quantity + temperature are real and kept.
            Stock($"ENG_OIL_TEMP:{n}", $"GENERAL ENG OIL TEMPERATURE:{n}", $"Engine {n} Oil Temperature", "celsius");
        }
        // The A380 has thrust reversers ONLY on the inboard engines (2 and 3); the
        // outboard 1 and 4 have none (their _REVERSER_ vars stay 0 forever), so only
        // 2 and 3 are exposed/announced — "only engine 2 and 3 deploy" is correct.
        ReadEnum("A32NX_REVERSER_2_DEPLOYED", "Engine 2 Reverser", revVd);
        ReadEnum("A32NX_REVERSER_3_DEPLOYED", "Engine 3 Reverser", revVd);
        // Engine mode knob: combo writes via HandleUIVariableSet to all engines.
        vars["ENGINE_MODE_SELECTOR"] = new SimVarDefinition
        {
            Name = "XMLVAR_ENG_MODE_SEL", DisplayName = "Engine Mode", Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Crank", [1] = "Norm", [2] = "Ignition / Start" }
        };

        // ============================ FAULT / STATUS READOUTS ============================
        // All OnRequest + IsAnnounced=false (Read/ReadEnum) — surfaced only when
        // the user opens a panel, never auto-announced (no call-out spam).
        var fault = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" };
        var powered = new Dictionary<double, string> { [0] = "Unpowered", [1] = "Powered" };
        var abnNorm = new Dictionary<double, string> { [0] = "Abnormal", [1] = "Normal" };
        var openVd = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" };
        var armedVd = new Dictionary<double, string> { [0] = "No", [1] = "Armed" };
        var dischVd = new Dictionary<double, string> { [0] = "No", [1] = "Discharged" };
        var fireVd = new Dictionary<double, string> { [0] = "Normal", [1] = "FIRE" };
        var downlk = new Dictionary<double, string> { [0] = "Not Locked", [1] = "Downlocked" };
        var connVd = new Dictionary<double, string> { [0] = "Disconnected", [1] = "Connected" };

        // ELEC (BAT fault PBs already registered in the overhead ELEC section above)
        ReadEnum("A32NX_OVHD_ELEC_AC_ESS_FEED_PB_HAS_FAULT", "AC ESS Feed Fault", fault);
        ReadEnum("A32NX_OVHD_ELEC_GALY_AND_CAB_PB_HAS_FAULT", "Galley and Cabin Fault", fault);
        ReadEnum("A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_HAS_FAULT", "RAT and Emergency Gen Fault", fault);
        ReadEnum("A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_HAS_FAULT", "Emergency Generator 1 Line Fault", fault);
        for (int n = 1; n <= 4; n++)
        {
            ReadEnum($"A32NX_OVHD_ELEC_IDG_{n}_PB_HAS_FAULT", $"IDG {n} Fault", fault);
            ReadEnum($"A32NX_OVHD_ELEC_IDG_{n}_PB_IS_DISC", $"IDG {n} Disconnected", new Dictionary<double, string> { [0] = "No", [1] = "Disconnected" });
            ReadEnum($"A32NX_OVHD_ELEC_ENG_GEN_{n}_PB_HAS_FAULT", $"Engine Gen {n} Fault", fault);
            ReadEnum($"A32NX_ELEC_ENG_GEN_{n}_IDG_IS_CONNECTED", $"IDG {n} Connected", connVd);
            // (External-power availability is NOT duplicated here — it's the single
            //  "GPU {n} Available" readout in Ground Services, same A32NX_EXT_PWR_
            //  AVAIL:{n} SimVar. Having both double-announced when a GPU connects.)
        }
        foreach (var bus in new[] { "AC_1", "AC_2", "AC_3", "AC_4", "AC_ESS", "AC_ESS_SCHED", "AC_247XP",
                                    "DC_1", "DC_2", "DC_ESS", "DC_247PP", "DC_HOT_1", "DC_HOT_2", "DC_HOT_3", "DC_HOT_4", "DC_GND_FLT_SVC" })
            ReadEnum($"A32NX_ELEC_{bus}_BUS_IS_POWERED", $"{bus.Replace('_', ' ')} Bus", powered);

        // APU
        ReadEnum("A32NX_OVHD_APU_MASTER_SW_PB_HAS_FAULT", "APU Master Fault", fault);
        ReadEnum("A32NX_APU_LOW_FUEL_PRESSURE_FAULT", "APU Low Fuel Pressure", fault);
        ReadEnum("A32NX_APU_BLEED_AIR_VALVE_OPEN", "APU Bleed Valve", openVd);
        Read("A32NX_APU_N_RAW", "APU N", "percent");

        // HYDRAULICS
        for (int n = 1; n <= 4; n++)
        {
            ReadEnum($"A32NX_OVHD_HYD_ENG_{n}AB_PUMP_DISC_PB_HAS_FAULT", $"Engine {n} Pump Disc Fault", fault);
            ReadEnum($"A32NX_HYD_ENG_{n}AB_PUMP_DISC", $"Engine {n} Pump Disconnected", new Dictionary<double, string> { [0] = "No", [1] = "Disconnected" });
            ReadEnum($"A32NX_OVHD_HYD_ENG_{n}A_PUMP_PB_HAS_FAULT", $"Engine {n} Pump A Fault", fault);
            ReadEnum($"A32NX_OVHD_HYD_ENG_{n}B_PUMP_PB_HAS_FAULT", $"Engine {n} Pump B Fault", fault);
        }
        foreach (var sys in new[] { "G", "Y" })
            foreach (var ab in new[] { "A", "B" })
            {
                string lbl = (sys == "G" ? "Green" : "Yellow") + " Electric Pump " + ab;
                ReadEnum($"A32NX_OVHD_HYD_EPUMP{sys}{ab}_ON_PB_HAS_FAULT", $"{lbl} On Fault", fault);
                ReadEnum($"A32NX_OVHD_HYD_EPUMP{sys}{ab}_OFF_PB_HAS_FAULT", $"{lbl} Off Fault", fault);
            }
        ReadEnum("A32NX_OVHD_HYD_EPUMPB_PB_HAS_FAULT", "Electric Pump B Fault", fault);
        ReadEnum("A32NX_OVHD_HYD_EPUMPY_PB_HAS_FAULT", "Electric Pump Y Fault", fault);
        ReadEnum("A32NX_HYD_GREEN_SYSTEM_1_SECTION_PRESSURE_SWITCH", "Green System Pressure", new Dictionary<double, string> { [0] = "Low", [1] = "Normal" });
        ReadEnum("A32NX_HYD_YELLOW_SYSTEM_1_SECTION_PRESSURE_SWITCH", "Yellow System Pressure", new Dictionary<double, string> { [0] = "Low", [1] = "Normal" });

        // BLEED
        for (int n = 1; n <= 4; n++)
            ReadEnum($"A32NX_OVHD_PNEU_ENG_{n}_BLEED_PB_HAS_FAULT", $"Engine {n} Bleed Fault", fault);
        ReadEnum("A32NX_OVHD_PNEU_APU_BLEED_PB_HAS_FAULT", "APU Bleed Fault", fault);
        foreach (var s in new[] { "L", "C", "R" })
            ReadEnumQuiet($"A32NX_PNEU_XBLEED_VALVE_{s}_OPEN", $"Crossbleed Valve {s}", openVd);

        // AIR CONDITIONING
        for (int n = 1; n <= 2; n++)
        {
            ReadEnumQuiet($"A32NX_OVHD_COND_PACK_{n}_PB_HAS_FAULT", $"Pack {n} Fault", fault);
            ReadEnum($"A32NX_OVHD_COND_HOT_AIR_{n}_PB_HAS_FAULT", $"Hot Air {n} Fault", fault);
            for (int ch = 1; ch <= 2; ch++)
                ReadEnum($"A32NX_COND_FDAC_{n}_CHANNEL_{ch}_FAILURE", $"FDAC {n} Channel {ch}", fault);
        }
        foreach (var z in new[] { "FWD", "BULK" })
            ReadEnum($"A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{z}_PB_HAS_FAULT", $"Cargo {z} Isolation Fault", fault);
        ReadEnum("A32NX_OVHD_CARGO_AIR_HEATER_PB_HAS_FAULT", "Cargo Heater Fault", fault);
        ReadEnum("A32NX_OVHD_COND_RAM_AIR_PB_HAS_FAULT", "Ram Air Fault", fault);
        for (int ch = 1; ch <= 2; ch++)
            ReadEnum($"A32NX_COND_TADD_CHANNEL_{ch}_FAILURE", $"Temperature Control TADD Channel {ch}", fault);

        // PRESSURIZATION
        for (int n = 1; n <= 4; n++)
        {
            for (int ch = 1; ch <= 2; ch++)
                ReadEnum($"A32NX_PRESS_OCSM_{n}_CHANNEL_{ch}_FAILURE", $"OCSM {n} Channel {ch}", fault);
            ReadEnum($"A32NX_PRESS_OCSM_{n}_AUTO_PARTITION_FAILURE", $"OCSM {n} Auto Control", fault);
        }
        Read("A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE", "Cabin Delta Pressure", "psi");

        // VENTILATION
        foreach (var id in new[] { "FWD", "AFT" })
            for (int ch = 1; ch <= 2; ch++)
                ReadEnum($"A32NX_VENT_{id}_VCM_CHANNEL_{ch}_FAILURE", $"{id} VCM Channel {ch}", fault);
        ReadEnum("A32NX_VENT_OVERPRESSURE_RELIEF_VALVE_IS_OPEN", "Overpressure Relief Valve", openVd);

        // FIRE
        ReadEnum("A32NX_FIRE_DETECTED_MLG", "Main Gear Bay Fire", fireVd);
        for (int n = 1; n <= 4; n++)
        {
            ReadEnum($"A32NX_ENG_{n}_ON_FIRE", $"Engine {n} On Fire", fireVd);
            for (int b = 1; b <= 2; b++)
            {
                ReadEnum($"A32NX_OVHD_FIRE_SQUIB_{b}_ENG_{n}_IS_ARMED", $"Engine {n} Agent {b} Squib", armedVd);
                ReadEnum($"A32NX_OVHD_FIRE_SQUIB_{b}_ENG_{n}_IS_DISCHARGED", $"Engine {n} Agent {b} Discharged", dischVd);
            }
        }
        ReadEnum("A32NX_OVHD_FIRE_SQUIB_1_APU_1_IS_DISCHARGED", "APU Agent Discharged", dischVd);

        // ADIRS
        for (int n = 1; n <= 3; n++)
            ReadEnum($"A32NX_ADIRS_ADIRU_{n}_STATE", $"ADIRU {n} State",
                new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" });

        // FLIGHT CONTROL COMPUTERS (3 PRIM + 3 SEC)
        for (int n = 1; n <= 3; n++)
        {
            ReadEnum($"A32NX_PRIM_{n}_HEALTHY", $"PRIM {n} Healthy", new Dictionary<double, string> { [0] = "Failed", [1] = "Healthy" });
            ReadEnum($"A32NX_SEC_{n}_HEALTHY", $"SEC {n} Healthy", new Dictionary<double, string> { [0] = "Failed", [1] = "Healthy" });
        }
        ReadEnum("A32NX_FWS1_IS_HEALTHY", "FWS 1 Healthy", new Dictionary<double, string> { [0] = "Failed", [1] = "Healthy" });
        ReadEnum("A32NX_FWS2_IS_HEALTHY", "FWS 2 Healthy", new Dictionary<double, string> { [0] = "Failed", [1] = "Healthy" });

        // GEAR. Two LGCIUs (dual-channel) monitor the same gear, so announcing
        // BOTH on every gear cycle is redundant chatter. Channel 1 auto-announces
        // the downlock per side; channel 2 stays panel-visible (ReadEnumQuiet) for
        // cross-check, so a rare LGCIU 1/2 disagreement is still readable without
        // doubling every routine gear callout.
        foreach (var sd in new[] { "LEFT", "RIGHT" })
        {
            ReadEnum($"A32NX_LGCIU_1_{sd}_GEAR_DOWNLOCKED", $"LGCIU 1 {sd} Downlock", downlk);
            ReadEnumQuiet($"A32NX_LGCIU_2_{sd}_GEAR_DOWNLOCKED", $"LGCIU 2 {sd} Downlock", downlk);
        }
        ReadEnum("A32NX_LGCIU_1_NOSE_GEAR_COMPRESSED", "Nose Gear Compressed", new Dictionary<double, string> { [0] = "No", [1] = "Compressed" });

        // ============================ FCU / AFS + EFIS BARO ============================
        // Ported from the A320 FCU integration, retargeted to A380X autoflight
        // vars: the A380 has NO A32NX_FCU_AFS_DISPLAY_* value words and NO
        // A32NX_FCU_*_LIGHT_ON vars. Values come from A32NX_AUTOPILOT_*_SELECTED,
        // managed state from A32NX_FCU_*_MANAGED[_DOT|_DASHES], and engagement
        // from FG/FMA status vars. See tools/a380-fcu-vars.md.

        // Knob / pushbutton events (FCU panel buttons; also reached by hotkeys).
        Evt("A32NX.FCU_TO_AP_HDG_PUSH", "A32NX.FCU_TO_AP_HDG_PUSH", "Heading Push");
        Evt("A32NX.FCU_TO_AP_HDG_PULL", "A32NX.FCU_TO_AP_HDG_PULL", "Heading Pull");
        Evt("A32NX.FCU_SPD_PUSH", "A32NX.FCU_SPD_PUSH", "Speed Push");
        Evt("A32NX.FCU_SPD_PULL", "A32NX.FCU_SPD_PULL", "Speed Pull");
        Evt("A32NX.FCU_ALT_PUSH", "A32NX.FCU_ALT_PUSH", "Altitude Push");
        Evt("A32NX.FCU_ALT_PULL", "A32NX.FCU_ALT_PULL", "Altitude Pull");
        Evt("A32NX.FCU_VS_PUSH", "A32NX.FCU_VS_PUSH", "Vertical Speed Push");
        Evt("A32NX.FCU_TO_AP_VS_PULL", "A32NX.FCU_TO_AP_VS_PULL", "Vertical Speed Pull");
        Evt("A32NX.FCU_AP_1_PUSH", "A32NX.FCU_AP_1_PUSH", "Autopilot 1");
        Evt("A32NX.FCU_AP_2_PUSH", "A32NX.FCU_AP_2_PUSH", "Autopilot 2");
        Evt("A32NX.FCU_ATHR_PUSH", "A32NX.FCU_ATHR_PUSH", "Autothrust");
        Evt("A32NX.FCU_LOC_PUSH", "A32NX.FCU_LOC_PUSH", "Localizer");
        Evt("A32NX.FCU_APPR_PUSH", "A32NX.FCU_APPR_PUSH", "Approach");
        Evt("A32NX.FCU_EXPED_PUSH", "A32NX.FCU_EXPED_PUSH", "Expedite");
        Evt("A32NX.FCU_AP_DISCONNECT_PUSH", "A32NX.FCU_AP_DISCONNECT_PUSH", "Autopilot Disconnect");
        Evt("A32NX.FCU_ATHR_DISCONNECT_PUSH", "A32NX.FCU_ATHR_DISCONNECT_PUSH", "Autothrust Disconnect");
        Evt("A32NX.FCU_SPD_MACH_TOGGLE_PUSH", "A32NX.FCU_SPD_MACH_TOGGLE_PUSH", "Speed / Mach Toggle");
        Evt("A32NX.FCU_TRK_FPA_TOGGLE_PUSH", "A32NX.FCU_TRK_FPA_TOGGLE_PUSH", "TRK / FPA Toggle");
        Sel("XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT", "Altitude Increment",
            new Dictionary<double, string> { [100] = "100", [1000] = "1000" });
        OnOff("A32NX_METRIC_ALT_TOGGLE", "Metric Altitude");

        // Selected value readouts (OnRequest; read on demand via Shift+H/S/A/V).
        // NB: units MUST be "number" — this is an L:var. Reading it with the
        // SimVar angle-unit "degrees" returns a wrong/normalised value (e.g. 300
        // read back as 005), since the L:var read path doesn't apply SimVar units.
        Read("A32NX_AUTOPILOT_HEADING_SELECTED", "Selected Heading", "number");
        Read("A32NX_AUTOPILOT_SPEED_SELECTED", "Selected Speed");
        Read("A32NX_AUTOPILOT_VS_SELECTED", "Selected Vertical Speed", "number"); // L:var — must be "number"
        Read("A32NX_AUTOPILOT_FPA_SELECTED", "Selected FPA");
        // Managed-vs-selected indicators — AUTO-ANNOUNCED so a knob PUSH (managed)
        // or PULL (selected) speaks the resulting mode. Previously OnRequest/silent,
        // so pushing/pulling speed/heading/altitude/VS gave no audible feedback.
        var managedSel = new Dictionary<double, string> { [0] = "Selected", [1] = "Managed" };
        Mon("A32NX_FCU_HDG_MANAGED_DASHES", "Heading Mode", managedSel);
        Mon("A32NX_FCU_SPD_MANAGED_DOT", "Speed Mode", managedSel);
        Mon("A32NX_FCU_VS_MANAGED", "Vertical Speed Mode", managedSel);
        Mon("A32NX_FCU_ALT_MANAGED", "Altitude Mode", managedSel);
        // Settable toggle combo — fires A32NX.FCU_TRK_FPA_TOGGLE_PUSH on change.
        Sel("A32NX_TRK_FPA_MODE_ACTIVE", "Track FPA Mode",
            new Dictionary<double, string> { [0] = "HDG V/S", [1] = "TRK FPA" });
        // SimVars (key != Name — ProcessSimVarUpdate matches on the key).
        Stock("FCU_ALT_VALUE", "AUTOPILOT ALTITUDE LOCK VAR:3", "Selected Altitude", "feet");
        Stock("FCU_MACH_MODE", "AUTOPILOT MANAGED SPEED IN MACH", "Mach Mode", "bool", onOff);

        // ---- PFD / FMA live mode annunciations (ported from the A320; the
        //      A380X publishes these under the same A32NX_ names). Auto-announced
        //      so engaged mode changes (incl. VS/FPA, OP CLB, G/S, LAND, FLARE,
        //      SRS, NAV, LOC, RWY) are spoken live, like the FMA on the PFD. ----
        Mon("A32NX_FMA_VERTICAL_MODE", "Vertical Mode", new Dictionary<double, string>
        {
            [0] = "None", [10] = "ALT", [11] = "ALT star", [12] = "OP CLB", [13] = "OP DES",
            [14] = "V/S", [15] = "FPA", [20] = "ALT constraint", [21] = "ALT constraint star",
            [22] = "CLB", [23] = "DES", [24] = "FINAL", [30] = "G/S capture", [31] = "G/S track",
            [32] = "LAND", [33] = "FLARE", [34] = "ROLL OUT", [40] = "SRS", [41] = "SRS GA", [50] = "TCAS"
        });
        Mon("A32NX_FMA_LATERAL_MODE", "Lateral Mode", new Dictionary<double, string>
        {
            [0] = "None", [10] = "HDG", [11] = "TRACK", [20] = "NAV", [30] = "LOC capture",
            [31] = "LOC track", [32] = "LAND", [33] = "FLARE", [34] = "ROLL OUT",
            [40] = "RWY", [41] = "RWY track", [50] = "GA track"
        });
        Mon("A32NX_APPROACH_CAPABILITY", "Approach Capability", new Dictionary<double, string>
        {
            [0] = "None", [1] = "CAT 1", [2] = "CAT 2", [3] = "CAT 3 Single", [4] = "CAT 3 Dual"
        });

        // PFD messages + armed modes + approach settings (for the PFD window;
        // ported from the A320, shared A32NX_ names). The 3 PFD messages are
        // announced live (meaningful callouts); the rest are window-only readouts.
        Mon("A32NX_PFD_MSG_SET_HOLD_SPEED", "Set Hold Speed", onOff);
        Mon("A32NX_PFD_MSG_TD_REACHED", "Top of Descent Reached", onOff);
        Mon("A32NX_PFD_MSG_CHECK_SPEED_MODE", "Check Speed Mode", onOff);
        Read("A32NX_FMA_VERTICAL_ARMED", "Armed Vertical Modes");
        Read("A32NX_FMA_LATERAL_ARMED", "Armed Lateral Modes");
        Read("A32NX_FMA_CRUISE_ALT_MODE", "Cruise Altitude Mode");
        Read("A32NX_PFD_LINEAR_DEVIATION_ACTIVE", "Linear Deviation Active");
        Read("A32NX_FMGC_1_LDEV_REQUEST", "FMGC L DEV Request");
        // (A32NX_FM1_MINIMUM_DESCENT_ALTITUDE is registered once in the MINIMUMS section.)
        Read("A32NX_DESTINATION_QNH", "Destination QNH");

        // FCU engage/mode toggles — settable combos that show live engage state
        // and fire the matching A32NX.FCU_*_PUSH toggle via HandleUIVariableSet
        // when the picked state differs from current (A380 has no FCU light vars).
        Sel("A32NX_AUTOPILOT_1_ACTIVE", "Autopilot 1", onOff);
        Sel("A32NX_AUTOPILOT_2_ACTIVE", "Autopilot 2", onOff);
        Sel("A32NX_FCU_LOC_MODE_ACTIVE", "Localizer", onOff);
        Sel("A32NX_FCU_APPR_MODE_ACTIVE", "Approach", onOff);
        Sel("A32NX_FMA_EXPEDITE_MODE", "Expedite", onOff);

        // ---- EFIS Control Panel: flight director + baro (per side) ----
        // FD toggle event removed — non-functional on this A380X build (the sim
        // recomputes the L-var every tick; see the note in BuildPanelControls).
        // FD_ACTIVE is kept as a read-only STATUS so the pilot can still tell
        // whether the flight director is on.
        Stock("FD_ACTIVE", "AUTOPILOT FLIGHT DIRECTOR ACTIVE", "Flight Director", "bool", onOff);
        // (Legacy XMLVAR_BaroN_Mode combos are DEAD on the A380X — verified live
        //  that writing them changes nothing. STD/QNH is the IS_STD combo below.)
        // Auto-announced live as the pilot turns the baro knob (the EFIS baro
        // setting is non-visual; spoken on change, deduped to whole hPa).
        MonNum("A32NX_FCU_LEFT_EIS_BARO_HPA", "Captain Altimeter", "hectopascals");
        MonNum("A32NX_FCU_RIGHT_EIS_BARO_HPA", "First Officer Altimeter", "hectopascals");
        // STD(push)/QNH(pull) per side — SETTABLE combo (push=Standard, pull=QNH).
        // The A380X has NO event for this; HandleUIVariableSet writes the L:var
        // directly (verified live). This is the real "baro push/pull" control,
        // replacing the dead XMLVAR mode combo + the non-functional
        // A380X_EFIS_CP_BARO_PUSH/PULL events. Also drives the live announce.
        var baroStd = new Dictionary<double, string> { [0] = "QNH", [1] = "Standard" };
        Sel("A32NX_FCU_LEFT_EIS_BARO_IS_STD", "Capt Altimeter STD", baroStd);
        Sel("A32NX_FCU_RIGHT_EIS_BARO_IS_STD", "F/O Altimeter STD", baroStd);
        // hPa/inHg unit per side: the real selector is XMLVAR_Baro_Selector_HPA_
        // {1,2} (registered as the "Baro Unit" combo in the EFIS-CP section).
        // A32NX_FCU_EFIS_*_BARO_IS_INHG is stuck at 0 on the A380X (verified live:
        // F/O reads XMLVAR=0/inHg while IS_INHG stays 0), so it is NOT used — the
        // unit flag is tracked off the XMLVAR in ProcessSimVarUpdate.
        Read("A380X_EFIS_L_BARO_PRESELECTED", "Capt Preselected QNH");
        Read("A380X_EFIS_R_BARO_PRESELECTED", "F/O Preselected QNH");
        // Settable QNH — numeric input in the side's CURRENT unit (hPa or inHg).
        // HandleUIVariableSet validates, converts to millibars*16 and fires the
        // stock K:KOHLSMAN_SET (verified live: 16320 -> 1020 hPa; works in both
        // units since the event value is always mb*16). KOHLSMAN_SET moves both
        // altimeters together. Key ends "_SET" -> MainForm renders a numeric box.
        vars["CAPT_QNH_SET"] = new SimVarDefinition
        {
            Name = "CAPT_QNH_SET", DisplayName = "Set QNH (in Capt unit)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };
        vars["FO_QNH_SET"] = new SimVarDefinition
        {
            Name = "FO_QNH_SET", DisplayName = "Set QNH (in F/O unit)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };

        // (The A380X_EFIS_CP_BARO_PUSH/PULL H-events are NON-functional on the
        //  A380X — verified live — removed. STD/QNH is the IS_STD combo above.)

        // ============================ RADIOS (RMP) ============================
        // The A380 RMPs manage the radios on-screen, but the aircraft sits on
        // standard MSFS COM radios — the stock COM events/SimVars (as used by
        // the A320 RMP panel) drive and read them. Standby is the editable box;
        // swap moves it to active.
        for (int n = 1; n <= 3; n++)
        {
            vars[$"COM_STANDBY_FREQUENCY_SET:{n}"] = new SimVarDefinition
            {
                Name = "COM_STANDBY_FREQUENCY_SET", DisplayName = $"VHF {n} Standby (set)",
                Type = SimVarType.Event, EventParam = (uint)n
            };
            Evt($"COM{n}_RADIO_SWAP", $"COM{n}_RADIO_SWAP", $"VHF {n} Swap");
            Stock($"COM_ACTIVE_FREQUENCY:{n}", $"COM ACTIVE FREQUENCY:{n}", $"VHF {n} Active", "MHz");
            Stock($"COM_STANDBY_FREQUENCY:{n}", $"COM STANDBY FREQUENCY:{n}", $"VHF {n} Standby", "MHz");
        }

        // ============================ TRANSPONDER / ATC ============================
        Stock("XPNDR_CODE", "TRANSPONDER CODE:1", "Squawk Code", "number");
        // Key MUST be "TRANSPONDER_CODE_SET" so MainForm's squawk-input path
        // BCD16-encodes the entered code (4242 -> 0x4242). Sending the raw decimal
        // via the generic event path produced a wrong squawk. Event name stays XPNDR_SET.
        Evt("TRANSPONDER_CODE_SET", "XPNDR_SET", "Squawk Code");
        ReadEnum("A32NX_TRANSPONDER_MODE", "Transponder Mode",
            new Dictionary<double, string> { [0] = "Standby", [1] = "Auto", [2] = "On" });
        Sel("A32NX_SWITCH_ATC_ALT", "ATC Altitude Reporting",
            new Dictionary<double, string> { [0] = "Off", [1] = "On" });

        // ============================ MINIMUMS ============================
        // FMS-set decision minimums (best-effort; var reused from A32NX FM).
        // Minimums are ARINC429 words (decoded + announced in ProcessSimVarUpdate);
        // monitored so a minimum set/cleared on the MCDU PERF APPR page is spoken.
        MonNum("A32NX_FM1_MINIMUM_DESCENT_ALTITUDE", "Baro Minimum", "feet");
        MonNum("A32NX_FM1_DECISION_HEIGHT", "Radio Minimum (DH)", "feet");

        // ============================ A32NX SHARED GAP CONTROLS/READOUTS ============================
        // Pulled from the FBW A32NX API docs (shared with the A380); see
        // tools/a32nx-gap-vs-a380.md. MSFSBA's own A320 definition is incomplete,
        // so these come from the docs, not the A320 code.
        var dischargedVd = new Dictionary<double, string> { [0] = "No", [1] = "Discharged" };

        // Clock / chrono (readouts pair with the ET switch already present).
        Read("A32NX_CHRONO_ELAPSED_TIME", "Chronometer (seconds)", "seconds");
        Read("A32NX_CHRONO_ET_ELAPSED_TIME", "Elapsed Time (seconds)", "seconds");

        // ISIS standby instrument.
        ReadEnum("A32NX_ISIS_LS_ACTIVE", "ISIS LS", onOff);
        ReadEnum("A32NX_ISIS_BUGS_ACTIVE", "ISIS Bugs Page", onOff);
        Sel("A32NX_ISIS_BARO_MODE", "ISIS Baro Mode", new Dictionary<double, string> { [0] = "Set", [1] = "Standard" });
        OnOff("A32NX_ISIS_BARO_UNIT_INHG", "ISIS Baro in inHg");

        // Brakes.
        OnOff("A32NX_BRAKE_FAN_BTN_PRESSED", "Brake Fan", button: true);
        ReadEnum("A32NX_BRAKE_FAN_RUNNING", "Brake Fan Running", new Dictionary<double, string> { [0] = "Off", [1] = "Running" });
        ReadEnum("A32NX_BRAKES_HOT", "Brakes Hot", new Dictionary<double, string> { [0] = "Normal", [1] = "HOT" });
        Read("A32NX_HYD_BRAKE_ALTN_LEFT_PRESS", "Alternate Brake Left", "psi");
        Read("A32NX_HYD_BRAKE_ALTN_RIGHT_PRESS", "Alternate Brake Right", "psi");
        Read("A32NX_HYD_BRAKE_ALTN_ACC_PRESS", "Brake Accumulator", "psi");

        // Gear.
        ReadEnum("A32NX_GEAR_LEVER_LOCKED", "Gear Lever Locked", new Dictionary<double, string> { [0] = "Unlocked", [1] = "Locked" });
        ReadEnum("A32NX_LG_GRVTY_MASTER_SWITCH_GUARD", "Gravity Extension Guard", openVd);

        // Pressurization manual selectors (manual mode only). The FBW knob L:vars
        // are written directly with a rotary POSITION value (not feet/fpm — the
        // dev build doesn't expose a documented engineering-unit mapping), so
        // these are pass-through numeric inputs of the knob position. Key ends
        // _SET so MainForm renders a numeric box; HandleUIVariableSet writes it.
        vars["PRESS_MAN_ALT_SET"] = new SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB", DisplayName = "Manual Cabin Altitude Knob (position)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };
        vars["PRESS_MAN_VS_SET"] = new SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB", DisplayName = "Manual Cabin V/S Knob (position)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };

        // Fuel crossfeed + jettison status + volume.
        OnOff("XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED_Pressed", "Crossfeed 1", button: true);
        for (int n = 2; n <= 4; n++)
            OnOff($"XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED{n}_Pressed", $"Crossfeed {n}", button: true);
        ReadEnum("A380X_OVHD_FUEL_JETTISON_IS_OPEN", "Jettison Valve", openVd);
        Read("A32NX_TOTAL_FUEL_VOLUME", "Total Fuel Volume", "gallons");

        // Hydraulics PTU.
        OffAuto("A32NX_OVHD_HYD_PTU_PB_IS_AUTO", "PTU");
        ReadEnum("A32NX_OVHD_HYD_PTU_PB_HAS_FAULT", "PTU Fault", fault);

        // Ventilation avionics blower/extract.
        OnOff("A32NX_VENTILATION_BLOWER_TOGGLE", "Avionics Blower");
        OnOff("A32NX_VENTILATION_EXTRACT_TOGGLE", "Avionics Extract");
        ReadEnum("A32NX_VENTILATION_BLOWER_FAULT", "Blower Fault", fault);
        ReadEnum("A32NX_VENTILATION_EXTRACT_FAULT", "Extract Fault", fault);

        // Anti-ice status.
        ReadEnum("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", "Wing Anti-Ice Flowing", onOff);
        ReadEnum("A32NX_PNEU_WING_ANTI_ICE_HAS_FAULT", "Wing Anti-Ice Fault", fault);

        // Oxygen.
        ReadEnum("A32NX_OXYGEN_TMR_RESET_FAULT", "Oxygen Timer Reset Fault", fault);

        // Calls / EVAC / cabin / cargo smoke.
        ReadEnum("A32NX_SLIDES_ARMED", "Door Slides", armedVd);
        ReadEnum("A32NX_EVAC_COMMAND_FAULT", "Evacuation Command Fault", fault);
        ReadEnum("A32NX_CARGOSMOKE_FWD_DISCHARGED", "Cargo Fwd Smoke Agent", dischargedVd);
        ReadEnum("A32NX_CARGOSMOKE_AFT_DISCHARGED", "Cargo Aft Smoke Agent", dischargedVd);

        // Recorder / misc overhead.
        OnOff("A32NX_RCDR_TEST", "Recorder Test", button: true);
        OnOff("A32NX_ELT_TEST_RESET", "ELT Test / Reset", button: true);
        OnOff("A32NX_DFDR_EVENT_ON", "DFDR Event", button: true);
        OnOff("A32NX_RAIN_REPELLENT_LEFT_ON", "Rain Repellent Left", button: true);
        OnOff("A32NX_RAIN_REPELLENT_RIGHT_ON", "Rain Repellent Right", button: true);
        OnOff("A32NX_OVHD_NSS_DATA_TO_AVNCS_TOGGLE", "NSS Data to Avionics");
        OnOff("A32NX_NSS_MASTER_OFF", "NSS Master Off");

        // Wipers (3-speed; written via HandleUIVariableSet → circuit power events).
        var wiperVd = new Dictionary<double, string> { [0] = "Off", [75] = "Slow", [100] = "Fast" };
        Sel("WIPER_LEFT", "Wiper Left", wiperVd);
        Sel("WIPER_RIGHT", "Wiper Right", wiperVd);
        Stock("WIPER_LEFT_ON", "CIRCUIT SWITCH ON:141", "Wiper Left State", "bool", onOff);
        Stock("WIPER_RIGHT_ON", "CIRCUIT SWITCH ON:143", "Wiper Right State", "bool", onOff);

        // EFIS-CP filter / overlay / baro unit (per side).
        foreach (var side in new[] { "L", "R" })
        {
            string who = side == "L" ? "Capt" : "F/O";
            Sel($"A380X_EFIS_{side}_ACTIVE_FILTER", $"{who} ND Filter",
                new Dictionary<double, string> { [1] = "Waypoints", [2] = "VOR/DME", [3] = "NDB" });
            Sel($"A380X_EFIS_{side}_ACTIVE_OVERLAY", $"{who} ND Overlay",
                new Dictionary<double, string> { [0] = "Off", [1] = "Weather", [2] = "Terrain" });
            // A380 baro unit lives on XMLVAR_Baro_Selector_HPA_{1|2} (1=hPa, 0=inHg),
            // not A32NX_FCU_EFIS_*_BARO_IS_INHG (which doesn't exist on the A380).
            Sel($"XMLVAR_Baro_Selector_HPA_{(side == "L" ? "1" : "2")}", $"{who} Baro Unit",
                new Dictionary<double, string> { [0] = "inHg", [1] = "hPa" });
        }

        // ECAM control panel — checklist buttons + SD more.
        Press("A32NX_BTN_CHECK_LH", "ECAM Check Left");
        Press("A32NX_BTN_CHECK_RH", "ECAM Check Right");
        ReadEnum("A32NX_SD_MORE_SHOWN", "SD More Page", new Dictionary<double, string> { [0] = "No", [1] = "Shown" });

        // ATC datalink (DCDU).
        OnOff("A32NX_DCDU_ATC_MSG_ACK", "ATC Message Acknowledge", button: true);
        Mon("A32NX_DCDU_ATC_MSG_WAITING", "ATC Message Waiting", new Dictionary<double, string> { [0] = "No", [1] = "Message Waiting" });

        // FMS switching + destination fuel warning.
        Sel("A32NX_FMS_SWITCHING_KNOB", "FMS Switching",
            new Dictionary<double, string> { [0] = "Both on 2", [1] = "Normal", [2] = "Both on 1" });
        Mon("A380X_FMS_DEST_EFOB_BELOW_MIN", "Destination Fuel",
            new Dictionary<double, string> { [0] = "OK", [1] = "Below Minimum" });

        // Reference speeds (high value with no visible PFD tape).
        Read("A32NX_SPEEDS_VLS", "VLS (lowest selectable)", "knots");
        Read("A32NX_SPEEDS_VAPP", "Approach Speed", "knots");
        Read("A32NX_SPEEDS_GD", "Green Dot Speed", "knots");
        Read("A32NX_SPEEDS_F", "F Speed", "knots");
        Read("A32NX_SPEEDS_S", "S Speed", "knots");

        // Lighting extras. (Strobe AUTO is part of the LIGHTING_STROBE_0
        // 3-position combo above — no separate STROBE_0_AUTO control needed.)
        Sel("A380X_OVHD_EXTLT_STBY_COMPASS_ICE_IND_SWITCH_POS", "Standby Compass / Ice Light",
            new Dictionary<double, string> { [0] = "Off", [1] = "On" });

        // GPWS / TAWS pushbuttons (ported from the A320; shared A32NX_ vars,
        // verified live). The OFF buttons inhibit a function when pressed (1=Off).
        var normOff = new Dictionary<double, string> { [0] = "Normal", [1] = "Off" };
        Sel("A32NX_GPWS_SYS_OFF", "GPWS System", normOff);
        Sel("A32NX_GPWS_GS_OFF", "GPWS Glideslope Mode", normOff);
        Sel("A32NX_GPWS_FLAP_OFF", "GPWS Flap Mode", normOff);
        Sel("A32NX_GPWS_TERR_OFF", "GPWS Terrain", normOff);
        Sel("A32NX_GPWS_FLAPS3", "Landing Flap 3", new Dictionary<double, string> { [0] = "Off", [1] = "On" });

        // Ground / automation helpers (whole-aircraft state + pushback).
        Sel("A32NX_AIRCRAFT_PRESET_LOAD", "Load Aircraft Preset",
            new Dictionary<double, string> { [0] = "None", [1] = "Cold and Dark", [2] = "Powered", [3] = "Pushback", [4] = "Taxi", [5] = "Takeoff" });
        Read("A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS", "Preset Load Progress");
        OnOff("A32NX_PUSHBACK_SYSTEM_ENABLED", "Pushback System");
        Read("A32NX_PUSHBACK_SPD_FACTOR", "Pushback Speed Factor");
        Read("A32NX_PUSHBACK_HDG_FACTOR", "Pushback Heading Factor");

        // KCCU keyboard/cursor enable vars are intentionally NOT defined as
        // controls — the KCCU is the MCDU's input device, driven through the
        // MCDU form (Coherent agent), not a user-facing panel.

        // ============================ SD SYSTEM-PAGE READOUTS (plain) + DOC GAP ============================
        // Plain (non-ARINC429) SD-page scalars — readable directly; surfaced as
        // panel readouts. (Fuel tanks / FOB / GW / CG, cabin press, APU N/EGT are
        // ARINC429 → handled by the SD readout window.) See tools/a380-sd-pages.md
        // and tools/a380-doc-final-gap.md.

        // ELEC AC: per-source volts / load / frequency.
        for (int n = 1; n <= 4; n++)
        {
            Read($"A32NX_ELEC_ENG_GEN_{n}_POTENTIAL", $"Gen {n} Voltage", "volts");
            Read($"A32NX_ELEC_ENG_GEN_{n}_LOAD", $"Gen {n} Load", "percent");
            Read($"A32NX_ELEC_ENG_GEN_{n}_FREQUENCY", $"Gen {n} Frequency", "hertz");
            Read($"A32NX_ELEC_ENG_GEN_{n}_IDG_OIL_OUTLET_TEMPERATURE", $"IDG {n} Oil Temperature", "celsius");
        }
        for (int n = 1; n <= 2; n++)
        {
            Read($"A32NX_ELEC_APU_GEN_{n}_POTENTIAL", $"APU Gen {n} Voltage", "volts");
            Read($"A32NX_ELEC_APU_GEN_{n}_LOAD", $"APU Gen {n} Load", "percent");
            Read($"A32NX_ELEC_APU_GEN_{n}_FREQUENCY", $"APU Gen {n} Frequency", "hertz");
        }
        Read("A32NX_ELEC_EXT_PWR_POTENTIAL", "Ext Power Voltage", "volts");
        Read("A32NX_ELEC_EXT_PWR_FREQUENCY", "Ext Power Frequency", "hertz");
        Read("A32NX_ELEC_EMER_GEN_POTENTIAL", "Emergency Gen Voltage", "volts");
        Read("A32NX_ELEC_STAT_INV_POTENTIAL", "Static Inverter Voltage", "volts");
        // ELEC DC: TR + battery volts / amps.
        for (int n = 1; n <= 4; n++)
        {
            Read($"A32NX_ELEC_TR_{n}_POTENTIAL", $"TR {n} Voltage", "volts");
            Read($"A32NX_ELEC_TR_{n}_CURRENT", $"TR {n} Current", "amperes");
            Read($"A32NX_ELEC_BAT_{n}_CURRENT", $"Battery {n} Current", "amperes");
        }

        // HYDRAULICS: Green/Yellow pressures, reservoirs, electric pumps.
        foreach (var sys in new[] { "GREEN", "YELLOW" })
        {
            Read($"A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE", $"{sys} System Pressure", "psi");
            Read($"A32NX_HYD_{sys}_RESERVOIR_LEVEL", $"{sys} Reservoir Level", "gallons");
            ReadEnum($"A32NX_HYD_{sys}_RESERVOIR_LEVEL_IS_LOW", $"{sys} Reservoir Low", new Dictionary<double, string> { [0] = "Normal", [1] = "LOW" });
        }

        // BLEED: per-engine precooler temp/pressure + valves; pack outlet temps.
        for (int n = 1; n <= 4; n++)
        {
            Read($"A32NX_PNEU_ENG_{n}_PRECOOLER_OUTLET_TEMPERATURE", $"Engine {n} Bleed Temperature", "celsius");
            Read($"A32NX_PNEU_ENG_{n}_REGULATED_TRANSDUCER_PRESSURE", $"Engine {n} Bleed Pressure", "psi");
            ReadEnum($"A32NX_PNEU_ENG_{n}_STARTER_VALVE_OPEN", $"Engine {n} Starter Valve", openVd);
        }
        for (int n = 1; n <= 2; n++) Read($"A32NX_COND_PACK_{n}_OUTLET_TEMPERATURE", $"Pack {n} Outlet Temperature", "celsius");
        Read("A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE", "APU Bleed Pressure", "psi");

        // ENGINE extras: oil qty, vibration.
        for (int n = 1; n <= 4; n++)
        {
            Read($"A32NX_ENGINE_OIL_QTY:{n}", $"Engine {n} Oil Quantity", "quarts");
            Stock($"ENG_VIBRATION:{n}", $"TURB ENG VIBRATION:{n}", $"Engine {n} Vibration", "number");
        }

        // COND extras: cargo temps + duct temps.
        Read("A32NX_COND_CARGO_FWD_TEMP", "Cargo Fwd Temperature", "celsius");
        Read("A32NX_COND_CARGO_BULK_TEMP", "Cargo Bulk Temperature", "celsius");
        Read("A32NX_COND_CKPT_DUCT_TEMP", "Cockpit Duct Temperature", "celsius");

        // FLIGHT CONTROLS: surface positions + trim + sidestick priority.
        foreach (var (side, who) in new[] { ("LEFT", "Left"), ("RIGHT", "Right") })
        {
            // var key keeps the uppercase simvar token; label is title-case so TTS
            // doesn't shout "LEFT"/"RIGHT".
            Read($"A32NX_{side}_FLAPS_POSITION_PERCENT", $"{who} Flaps Position", "percent");
            Read($"A32NX_{side}_SLATS_POSITION_PERCENT", $"{who} Slats Position", "percent");
        }
        Stock("ELEVATOR_TRIM", "ELEVATOR TRIM INDICATOR", "Pitch Trim", "number");
        Stock("RUDDER_TRIM_PCT", "RUDDER TRIM PCT", "Rudder Trim", "percent");
        ReadEnum("A32NX_PRIORITY_TAKEOVER:1", "Capt Sidestick Priority", new Dictionary<double, string> { [0] = "Normal", [1] = "Priority Taken" });
        ReadEnum("A32NX_PRIORITY_TAKEOVER:2", "F/O Sidestick Priority", new Dictionary<double, string> { [0] = "Normal", [1] = "Priority Taken" });

        // GEAR strut positions + RTO armed.
        Stock("GEAR_LEFT_POS", "GEAR LEFT POSITION", "Left Gear Position", "percent");
        Stock("GEAR_CENTER_POS", "GEAR CENTER POSITION", "Center Gear Position", "percent");
        Stock("GEAR_RIGHT_POS", "GEAR RIGHT POSITION", "Right Gear Position", "percent");
        ReadEnum("A32NX_AUTOBRAKES_RTO_ARMED", "RTO Armed", new Dictionary<double, string> { [0] = "No", [1] = "Armed" });

        // ENGINE master / ignition position readbacks (we only sent before).
        for (int n = 1; n <= 4; n++)
        {
            Stock($"ENG_IGN_POS:{n}", $"TURB ENG IGNITION SWITCH EX1:{n}", $"Engine {n} Ignition",
                "enum", new Dictionary<double, string> { [0] = "Crank", [1] = "Norm", [2] = "Ignition / Start" });
            Stock($"ENG_VALVE_SWITCH:{n}", $"FUELSYSTEM VALVE SWITCH:{n}", $"Engine {n} Master", "bool",
                new Dictionary<double, string> { [0] = "Off", [1] = "On" });
        }

        // EFIS OANS range (airport map zoom).
        foreach (var side in new[] { "L", "R" })
            Sel($"A32NX_EFIS_{side}_OANS_RANGE", $"{(side == "L" ? "Capt" : "F/O")} OANS Range",
                new Dictionary<double, string> { [0] = "Max", [1] = "1", [2] = "2", [3] = "3", [4] = "Min" });

        // ---- ECAM upper (E/WD) memo + warning lines — live monitoring ----
        // The A380X publishes 10 lines per side as numeric message CODES
        // (uppercase EWD, vs the A320's lowercase Ewd / 7 lines). They are
        // continuously monitored and decoded to text in ProcessSimVarUpdate
        // via EWDMessageLookupA380 (the ported A380 EcamMemos dictionary), so
        // memos / cautions / warnings are announced in real time.
        for (int i = 1; i <= 10; i++)
        {
            foreach (var lr in new[] { "LEFT", "RIGHT" })
            {
                string key = $"A32NX_EWD_LOWER_{lr}_LINE_{i}";
                vars[key] = new SimVarDefinition
                {
                    Name = key,
                    DisplayName = $"E/WD {(lr == "LEFT" ? "Left" : "Right")} Line {i}",
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Continuous,
                    IsAnnounced = true
                };
            }
        }

        // ---- Ground Services (flyPad Ground page, exposed as cockpit controls) ----
        // Doors: read the stock `INTERACTIVE POINT OPEN:n` as BOOL so the door
        // auto-announces Open/Closed exactly once per transition (a Percent read
        // would spam every frame of the open/close animation). Toggle via the
        // stock K:TOGGLE_AIRCRAFT_EXIT event with the door's interaction index —
        // the same mechanism the FBW flyPad uses (verified from FBW source).
        var openShut = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" };
        void Door(string key, int ip, uint exitId, string display)
        {
            vars[key] = new SimVarDefinition
            {
                Name = $"INTERACTIVE POINT OPEN:{ip}", DisplayName = display,
                Type = SimVarType.SimVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = openShut
            };
            Evt(key + "_TOGGLE", "TOGGLE_AIRCRAFT_EXIT", "Toggle " + display, exitId);
        }
        Door("A380X_GND_DOOR_MAIN1L", 0, 1, "Main 1 Left Door");
        Door("A380X_GND_DOOR_MAIN2L", 2, 3, "Main 2 Left Door");
        Door("A380X_GND_DOOR_MAIN4R", 9, 10, "Main 4 Right Door");
        Door("A380X_GND_DOOR_UPPER1L", 10, 11, "Upper 1 Left Door");
        Door("A380X_GND_DOOR_FWDCARGO", 16, 17, "Forward Cargo Door");

        // Jet bridge + passenger stairs (stock MSFS ground-service events;
        // airport/parking dependent). Catering, fuel-truck, baggage and pushback
        // are intentionally NOT exposed — GSX owns those.
        Evt("A380X_GND_JETWAY", "TOGGLE_JETWAY", "Toggle Jet Bridge");
        Evt("A380X_GND_STAIRS", "TOGGLE_RAMPTRUCK", "Toggle Passenger Stairs");

        // Wheel chocks + safety cones (FBW model state; auto-announced).
        vars["A380X_GND_CHOCKS"] = new SimVarDefinition
        {
            Name = "A32NX_MODEL_WHEELCHOCKS_ENABLED", DisplayName = "Wheel Chocks",
            Type = SimVarType.LVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Removed", [1] = "Placed" }
        };
        vars["A380X_GND_CONES"] = new SimVarDefinition
        {
            Name = "A32NX_MODEL_CONES_ENABLED", DisplayName = "Safety Cones",
            Type = SimVarType.LVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Removed", [1] = "Placed" }
        };

        // External-power (GPU) availability per receptacle (read-only; the actual
        // connect is the overhead EXT PWR pushbuttons). Announces when a GPU
        // becomes available at the stand.
        for (int n = 1; n <= 4; n++)
        {
            vars[$"A380X_GND_GPU_AVAIL_{n}"] = new SimVarDefinition
            {
                Name = $"A32NX_EXT_PWR_AVAIL:{n}", DisplayName = $"GPU {n} Available",
                Type = SimVarType.LVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Yes" }
            };
        }

        return vars;
    }

    // ===================================================================
    // Panel structure
    // ===================================================================
    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "ELEC", "APU", "Fuel", "Hydraulics", "Bleed Air", "Air Conditioning",
                "Pressurization", "Ventilation", "Cargo Air", "Anti Ice", "Fire", "Oxygen",
                "Calls", "Signs", "Wipers", "ADIRS", "Flight Control Computers", "Engine Start",
                "Recorder and Misc", "GPWS", "Interior Lighting", "Exterior Lighting"
            },
            ["Glareshield"] = new List<string> { "FCU", "EFIS Captain", "EFIS First Officer", "Warnings", "OIT" },
            ["Instrument"] = new List<string> { "Gear", "Autobrake", "ISIS", "Source Switching" },
            ["Pedestal"] = new List<string>
            {
                "Engines", "Thrust Levers", "Flaps and Brakes", "ECAM Control Panel", "Weather Radar",
                "Transponder", "Radios", "RMP", "Cockpit Door"
            },
            ["Ground Services"] = new List<string> { "Doors", "Ground Equipment" },
            ["Displays"] = new List<string> { "Status", "Speeds", "Minimums", "Ground" }
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var p = new Dictionary<string, List<string>>();

        p["ELEC"] = new List<string>
        {
            "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO", "A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL",
            "A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO", "A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON",
            "A32NX_OVHD_ELEC_IDG_1_PB_IS_RELEASED", "A32NX_OVHD_ELEC_IDG_2_PB_IS_RELEASED",
            "A32NX_OVHD_ELEC_IDG_3_PB_IS_RELEASED", "A32NX_OVHD_ELEC_IDG_4_PB_IS_RELEASED",
            "A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON",
            "A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON",
            "A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON", "A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED",
            "A380X_OVHD_ELEC_BAT_SELECTOR_KNOB"
        };
        p["APU"] = new List<string>
        {
            "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_APU_START_PB_IS_ON"
        };
        p["Fuel"] = new List<string>
        {
            "A380X_OVHD_FUEL_OUTRTK_XFR_PB_IS_AUTO", "A380X_OVHD_FUEL_MIDTK_XFR_PB_IS_AUTO",
            "A380X_OVHD_FUEL_INRTK_XFR_PB_IS_AUTO", "A380X_OVHD_FUEL_TRIMTK_XFR_PB_IS_AUTO",
            "A380X_OVHD_FUEL_EMER_OUTR_XFR_PB_IS_ON", "A380X_OVHD_FUEL_JETTISON_ARM_PB_IS_ON",
            "A380X_OVHD_FUEL_JETTISON_ACTIVE_PB_IS_ON"
        };
        p["Hydraulics"] = new List<string>
        {
            "A32NX_OVHD_HYD_ENG_1A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_1B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_2A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_2B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_3A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_3B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_4A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_4B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPGA_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPGB_ON_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPYA_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPYB_ON_PB_IS_AUTO",
            "A32NX_OVHD_HYD_RAT_MAN_ON_IS_PRESSED"
        };
        p["Bleed Air"] = new List<string>
        {
            "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
            "A32NX_OVHD_PNEU_ENG_1_BLEED_PB_IS_AUTO", "A32NX_OVHD_PNEU_ENG_2_BLEED_PB_IS_AUTO",
            "A32NX_OVHD_PNEU_ENG_3_BLEED_PB_IS_AUTO", "A32NX_OVHD_PNEU_ENG_4_BLEED_PB_IS_AUTO",
            "A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION",
            "A32NX_OVHD_COND_PACK_1_PB_IS_ON", "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
            "A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON", "A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON",
            "A32NX_OVHD_COND_RAM_AIR_PB_IS_ON"
        };
        p["Air Conditioning"] = new List<string>
        {
            "COND_CKPT_TEMP_SET", "COND_CABIN_TEMP_SET"
        };
        p["Pressurization"] = new List<string>
        {
            "A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO", "PRESS_MAN_ALT_SET",
            "A32NX_OVHD_PRESS_MAN_VS_CTL_PB_IS_AUTO", "PRESS_MAN_VS_SET",
            "A32NX_OVHD_PRESS_DITCHING_PB_IS_ON"
        };
        p["Ventilation"] = new List<string>
        {
            "A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON", "A32NX_OVHD_VENT_AIR_EXTRACT_PB_IS_ON"
        };
        p["Cargo Air"] = new List<string>
        {
            "CARGO_FWD_TEMP_SET", "CARGO_BULK_TEMP_SET",
            "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_FWD_PB_IS_ON", "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_BULK_PB_IS_ON",
            "A32NX_OVHD_CARGO_AIR_HEATER_PB_IS_ON"
        };
        p["Anti Ice"] = new List<string>
        {
            "A32NX_MAN_PITOT_HEAT", "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED",
            "ENG1_ANTI_ICE", "ENG2_ANTI_ICE", "ENG3_ANTI_ICE", "ENG4_ANTI_ICE"
        };
        p["Fire"] = new List<string>
        {
            "A32NX_FIRE_BUTTON_ENG1", "A32NX_FIRE_BUTTON_ENG2", "A32NX_FIRE_BUTTON_ENG3",
            "A32NX_FIRE_BUTTON_ENG4", "A32NX_FIRE_BUTTON_APU", "A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED"
        };
        p["Oxygen"] = new List<string>
        {
            "PUSH_OVHD_OXYGEN_CREW", "A32NX_OXYGEN_MASKS_DEPLOYED", "A32NX_OXYGEN_TMR_RESET"
        };
        p["Calls"] = new List<string>
        {
            "A32NX_CALLS_EMER_ON", "A32NX_EVAC_COMMAND_TOGGLE", "A32NX_EVAC_CAPT_TOGGLE"
        };
        p["Signs"] = new List<string>
        {
            "XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position",
            "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position"
        };
        p["ADIRS"] = new List<string>
        {
            "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
            "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_1_PB_IS_ON",
            "A32NX_OVHD_ADIRS_IR_2_PB_IS_ON", "A32NX_OVHD_ADIRS_IR_3_PB_IS_ON",
            "A32NX_OVHD_ADIRS_ADR_1_PB_IS_ON", "A32NX_OVHD_ADIRS_ADR_2_PB_IS_ON",
            "A32NX_OVHD_ADIRS_ADR_3_PB_IS_ON"
        };
        p["Flight Control Computers"] = new List<string>
        {
            "A32NX_PRIM_1_PUSHBUTTON_PRESSED", "A32NX_PRIM_2_PUSHBUTTON_PRESSED", "A32NX_PRIM_3_PUSHBUTTON_PRESSED",
            "A32NX_SEC_1_PUSHBUTTON_PRESSED", "A32NX_SEC_2_PUSHBUTTON_PRESSED", "A32NX_SEC_3_PUSHBUTTON_PRESSED"
        };
        p["Engine Start"] = new List<string>
        {
            "A32NX_OVHD_FADEC_1", "A32NX_OVHD_FADEC_2", "A32NX_OVHD_FADEC_3", "A32NX_OVHD_FADEC_4",
            "A32NX_ENGMANSTART1_TOGGLE", "A32NX_ENGMANSTART2_TOGGLE",
            "A32NX_ENGMANSTART3_TOGGLE", "A32NX_ENGMANSTART4_TOGGLE"
        };
        p["Recorder and Misc"] = new List<string>
        {
            "A32NX_RCDR_GROUND_CONTROL_ON", "A32NX_ELT_ON", "A32NX_AVIONICS_COMPLT_ON",
            "A380X_OVHD_STORM_LT", "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE"
        };
        p["Interior Lighting"] = new List<string>
        {
            "A380X_OVHD_ANN_LT_POSITION", "A32NX_OVHD_INTLT_ANN"
        };
        p["Exterior Lighting"] = new List<string>
        {
            "LIGHT_BEACON", "LIGHTING_STROBE_0", "LIGHT_NAV", "LIGHT_WING", "LIGHT_LOGO",
            "LIGHT_LANDING", "A380X_OVHD_EXTLT_NOSE"
        };

        p["Warnings"] = new List<string>
        {
            "PUSH_AUTOPILOT_MASTERWARN_L", "PUSH_AUTOPILOT_MASTERWARN_R",
            "PUSH_AUTOPILOT_MASTERCAUT_L", "PUSH_AUTOPILOT_MASTERCAUT_R"
        };
        // EFIS Control Panel split per side (Captain / First Officer), PMDG-style.
        p["EFIS Captain"] = new List<string>
        {
            "A380X_EFIS_L_LS_BUTTON_IS_ON", "A380X_EFIS_L_VV_BUTTON_IS_ON", "A380X_EFIS_L_CSTR_BUTTON_IS_ON",
            "A380X_EFIS_L_ARPT_BUTTON_IS_ON", "A380X_EFIS_L_TRAF_BUTTON_IS_ON",
            "A32NX_EFIS_L_ND_MODE", "A32NX_EFIS_L_ND_RANGE",
            "A380X_EFIS_L_ACTIVE_FILTER", "A380X_EFIS_L_ACTIVE_OVERLAY",
            "A32NX_EFIS_L_NAVAID_1_MODE", "A32NX_EFIS_L_NAVAID_2_MODE",
            "A32NX_FCU_LEFT_EIS_BARO_IS_STD", "CAPT_QNH_SET", "XMLVAR_Baro_Selector_HPA_1",
            "A32NX_EFIS_L_OANS_RANGE"
            // Flight Director toggle removed: TOGGLE_FLIGHT_DIRECTOR (indexed or
            // not), the H-event, and direct L:var writes ALL fail to engage the FD
            // on this A380X build — the button did nothing but announce confusing
            // FMA-mode chatter. Re-add when FBW wires a working control.
        };
        p["EFIS First Officer"] = new List<string>
        {
            "A380X_EFIS_R_LS_BUTTON_IS_ON", "A380X_EFIS_R_VV_BUTTON_IS_ON", "A380X_EFIS_R_CSTR_BUTTON_IS_ON",
            "A380X_EFIS_R_ARPT_BUTTON_IS_ON", "A380X_EFIS_R_TRAF_BUTTON_IS_ON",
            "A32NX_EFIS_R_ND_MODE", "A32NX_EFIS_R_ND_RANGE",
            "A380X_EFIS_R_ACTIVE_FILTER", "A380X_EFIS_R_ACTIVE_OVERLAY",
            "A32NX_EFIS_R_NAVAID_1_MODE", "A32NX_EFIS_R_NAVAID_2_MODE",
            "A32NX_FCU_RIGHT_EIS_BARO_IS_STD", "FO_QNH_SET", "XMLVAR_Baro_Selector_HPA_2",
            "A32NX_EFIS_R_OANS_RANGE"
        };
        p["FCU"] = new List<string>
        {
            // Engage/mode controls as stateful combos (show live state, pick to
            // toggle) instead of blind buttons — see HandleUIVariableSet.
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE", "A32NX_AUTOTHRUST_STATUS",
            "A32NX_FCU_LOC_MODE_ACTIVE", "A32NX_FCU_APPR_MODE_ACTIVE", "A32NX_FMA_EXPEDITE_MODE",
            "A32NX_TRK_FPA_MODE_ACTIVE",
            // Genuine momentary knob push/pulls stay as buttons.
            "A32NX.FCU_TO_AP_HDG_PUSH", "A32NX.FCU_TO_AP_HDG_PULL",
            "A32NX.FCU_SPD_PUSH", "A32NX.FCU_SPD_PULL",
            "A32NX.FCU_ALT_PUSH", "A32NX.FCU_ALT_PULL", "XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT",
            "A32NX.FCU_VS_PUSH", "A32NX.FCU_TO_AP_VS_PULL",
            "A32NX.FCU_SPD_MACH_TOGGLE_PUSH",
            "A32NX.FCU_AP_DISCONNECT_PUSH", "A32NX.FCU_ATHR_DISCONNECT_PUSH",
            "A32NX_METRIC_ALT_TOGGLE"
        };
        p["OIT"] = new List<string> { "A380X_SWITCH_OIT_SIDE_LEFT", "A380X_SWITCH_OIT_SIDE_RIGHT" };

        p["Gear"] = new List<string> { "A32NX_GEAR_HANDLE_POSITION", "A32NX_LG_GRVTY_SWITCH_POS" };
        p["Autobrake"] = new List<string>
        {
            "A32NX_AUTOBRAKES_SELECTED_MODE", "A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED", "ANTISKID_BRAKES_ACTIVE"
        };
        p["Source Switching"] = new List<string>
        {
            "A32NX_ATT_HDG_SWITCHING_KNOB", "A32NX_AIR_DATA_SWITCHING_KNOB",
            "A32NX_EIS_DMC_SWITCHING_KNOB", "A32NX_CHRONO_ET_SWITCH_POS"
        };

        p["Engines"] = new List<string>
        {
            "ENGINE_MODE_SELECTOR",
            "ENGINE_1_MASTER_ON", "ENGINE_1_MASTER_OFF",
            "ENGINE_2_MASTER_ON", "ENGINE_2_MASTER_OFF",
            "ENGINE_3_MASTER_ON", "ENGINE_3_MASTER_OFF",
            "ENGINE_4_MASTER_ON", "ENGINE_4_MASTER_OFF"
            // ENG MAN START 1-4 live in the overhead "Engine Start" panel (not duplicated here).
        };
        p["Thrust Levers"] = new List<string>
        {
            "THROTTLE_ALL_DETENT", "THROTTLE_1_DETENT", "THROTTLE_2_DETENT",
            "THROTTLE_3_DETENT", "THROTTLE_4_DETENT"
        };
        p["Flaps and Brakes"] = new List<string> { "A32NX_PARK_BRAKE_LEVER_POS" };
        p["ECAM Control Panel"] = new List<string>
        {
            "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", "A32NX_BTN_ALL", "A32NX_BTN_ABNPROC", "A32NX_BTN_CL",
            "A32NX_BTN_CLR", "A32NX_BTN_CLR2", "A32NX_BTN_RCL", "A32NX_BTN_TOCONFIG",
            "A32NX_BTN_EMERCANC", "A32NX_BTN_UP", "A32NX_BTN_DOWN", "A32NX_BTN_MORE"
        };
        p["Weather Radar"] = new List<string>
        {
            "A32NX_SWITCH_RADAR_PWS_Position", "A32NX_RADAR_MULTISCAN_AUTO", "A32NX_RADAR_GCS_AUTO"
        };
        p["Transponder"] = new List<string>
        {
            "TRANSPONDER_CODE_SET", "XPNDR_IDENT_ON", "A32NX_TRANSPONDER_MODE", "A32NX_SWITCH_ATC_ALT"
        };
        p["Radios"] = new List<string>
        {
            "COM_STANDBY_FREQUENCY_SET:1", "COM1_RADIO_SWAP",
            "COM_STANDBY_FREQUENCY_SET:2", "COM2_RADIO_SWAP",
            "COM_STANDBY_FREQUENCY_SET:3", "COM3_RADIO_SWAP"
        };
        p["RMP"] = new List<string>
        {
            "A380X_RMP_1_STATE", "A380X_RMP_2_STATE", "A380X_RMP_3_STATE"
        };
        p["Minimums"] = new List<string>();
        p["Cockpit Door"] = new List<string> { "A32NX_COCKPIT_DOOR_LOCKED", "A32NX_CABIN_READY" };

        // ---- Ground Services (flyPad Ground page) ----
        p["Doors"] = new List<string>
        {
            "A380X_GND_DOOR_MAIN1L", "A380X_GND_DOOR_MAIN1L_TOGGLE",
            "A380X_GND_DOOR_MAIN2L", "A380X_GND_DOOR_MAIN2L_TOGGLE",
            "A380X_GND_DOOR_MAIN4R", "A380X_GND_DOOR_MAIN4R_TOGGLE",
            "A380X_GND_DOOR_UPPER1L", "A380X_GND_DOOR_UPPER1L_TOGGLE",
            "A380X_GND_DOOR_FWDCARGO", "A380X_GND_DOOR_FWDCARGO_TOGGLE"
        };
        p["Ground Equipment"] = new List<string>
        {
            "A380X_GND_JETWAY", "A380X_GND_STAIRS",
            "A380X_GND_CHOCKS", "A380X_GND_CONES",
            "A380X_GND_GPU_AVAIL_1", "A380X_GND_GPU_AVAIL_2",
            "A380X_GND_GPU_AVAIL_3", "A380X_GND_GPU_AVAIL_4"
        };

        p["Status"] = new List<string>
        {
            // A32NX_AUTOTHRUST_STATUS is a read-only state shown in the FCU display
            // (not duplicated here as a settable control).
            "A32NX_FMS_PAX_NUMBER", "A32NX_ECAM_FAILURE_ACTIVE",
            "A32NX_FMS_SWITCHING_KNOB"
        };

        // ---- A32NX shared gap controls folded into panels ----
        p["Fuel"].AddRange(new[]
        {
            "XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED_Pressed", "XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED2_Pressed",
            "XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED3_Pressed", "XMLVAR_Momentary_PUSH_OVHD_FUEL_XFEED4_Pressed"
        });
        p["Hydraulics"].Add("A32NX_OVHD_HYD_PTU_PB_IS_AUTO");
        p["Ventilation"].AddRange(new[] { "A32NX_VENTILATION_BLOWER_TOGGLE", "A32NX_VENTILATION_EXTRACT_TOGGLE" });
        p["Autobrake"].Add("A32NX_BRAKE_FAN_BTN_PRESSED");
        p["Recorder and Misc"].AddRange(new[]
        {
            "A32NX_RCDR_TEST", "A32NX_ELT_TEST_RESET", "A32NX_DFDR_EVENT_ON",
            "A32NX_RAIN_REPELLENT_LEFT_ON", "A32NX_RAIN_REPELLENT_RIGHT_ON",
            "A32NX_OVHD_NSS_DATA_TO_AVNCS_TOGGLE", "A32NX_NSS_MASTER_OFF"
        });
        p["GPWS"] = new List<string>
        {
            "A32NX_GPWS_SYS_OFF", "A32NX_GPWS_GS_OFF", "A32NX_GPWS_FLAP_OFF",
            "A32NX_GPWS_TERR_OFF", "A32NX_GPWS_FLAPS3"
        };
        // "PFD" is NOT a navigable control panel — it's the variable set the PFD
        // window (ShowPFD hotkey) requests/reads. Intentionally absent from
        // GetPanelStructure so it isn't shown as a UI panel.
        p["PFD"] = new List<string>
        {
            "A32NX_FMA_VERTICAL_MODE", "A32NX_FMA_LATERAL_MODE", "A32NX_FMA_VERTICAL_ARMED",
            "A32NX_FMA_LATERAL_ARMED", "A32NX_FMA_CRUISE_ALT_MODE", "A32NX_APPROACH_CAPABILITY",
            "A32NX_PFD_MSG_SET_HOLD_SPEED", "A32NX_PFD_MSG_TD_REACHED", "A32NX_PFD_MSG_CHECK_SPEED_MODE",
            "A32NX_PFD_LINEAR_DEVIATION_ACTIVE", "A32NX_FMGC_1_LDEV_REQUEST",
            "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE", "A32NX_DESTINATION_QNH",
            "A32NX_AUTOTHRUST_STATUS", "A32NX_AUTOBRAKES_SELECTED_MODE",
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE"
        };
        p["Interior Lighting"].Add("A380X_OVHD_EXTLT_STBY_COMPASS_ICE_IND_SWITCH_POS");
        // (EFIS filter/overlay/baro-unit/OANS folded into the per-side EFIS panels above.)
        p["ECAM Control Panel"].AddRange(new[] { "A32NX_BTN_CHECK_LH", "A32NX_BTN_CHECK_RH" });
        p["Transponder"].Add("A32NX_DCDU_ATC_MSG_ACK");

        // ---- new panels ----
        p["ISIS"] = new List<string> { "A32NX_ISIS_BARO_MODE", "A32NX_ISIS_BARO_UNIT_INHG" };
        p["Wipers"] = new List<string> { "WIPER_LEFT", "WIPER_RIGHT" };
        p["Speeds"] = new List<string>();
        // KCCU (keyboard/cursor control unit) is the MCDU's input device — it is
        // driven through the MCDU form (Coherent agent), not as a standalone
        // control panel, so it is intentionally NOT exposed as a panel here.
        p["Ground"] = new List<string>
        {
            "A32NX_AIRCRAFT_PRESET_LOAD", "A32NX_PUSHBACK_SYSTEM_ENABLED",
            "A32NX_PUSHBACK_SPD_FACTOR", "A32NX_PUSHBACK_HDG_FACTOR"
        };

        return p;
    }

    // ===================================================================
    // Read-only display variables per panel (auto-refreshing readouts)
    // ===================================================================
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        var d = new Dictionary<string, List<string>>();

        // ELEC: battery voltages + per-unit faults + bus-powered flags.
        var elec = new List<string>();
        for (int n = 1; n <= 4; n++) elec.Add($"A32NX_ELEC_BAT_{n}_POTENTIAL");
        foreach (var id in new[] { "1", "2", "ESS", "APU" }) elec.Add($"A32NX_OVHD_ELEC_BAT_{id}_PB_HAS_FAULT");
        elec.Add("A32NX_OVHD_ELEC_AC_ESS_FEED_PB_HAS_FAULT");
        elec.Add("A32NX_OVHD_ELEC_GALY_AND_CAB_PB_HAS_FAULT");
        elec.Add("A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_HAS_FAULT");
        for (int n = 1; n <= 4; n++)
        {
            elec.Add($"A32NX_OVHD_ELEC_IDG_{n}_PB_HAS_FAULT");
            elec.Add($"A32NX_OVHD_ELEC_IDG_{n}_PB_IS_DISC");
            elec.Add($"A32NX_OVHD_ELEC_ENG_GEN_{n}_PB_HAS_FAULT");
            elec.Add($"A32NX_ELEC_ENG_GEN_{n}_IDG_IS_CONNECTED");
            // (A32NX_EXT_PWR_AVAIL:{n} shows once as "GPU {n} Available" in Ground
            //  Services — not duplicated in the ELEC panel.)
        }
        foreach (var bus in new[] { "AC_1", "AC_2", "AC_3", "AC_4", "AC_ESS", "AC_ESS_SCHED", "AC_247XP",
                                    "DC_1", "DC_2", "DC_ESS", "DC_247PP", "DC_HOT_1", "DC_HOT_2", "DC_HOT_3", "DC_HOT_4", "DC_GND_FLT_SVC" })
            elec.Add($"A32NX_ELEC_{bus}_BUS_IS_POWERED");
        d["ELEC"] = elec;

        d["APU"] = new List<string>
        {
            "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", "A32NX_OVHD_APU_MASTER_SW_PB_HAS_FAULT",
            "A32NX_APU_LOW_FUEL_PRESSURE_FAULT", "A32NX_APU_BLEED_AIR_VALVE_OPEN", "A32NX_APU_N_RAW"
        };

        d["Fuel"] = new List<string> { "A32NX_TOTAL_FUEL_QUANTITY" };

        var hyd = new List<string>();
        for (int n = 1; n <= 4; n++)
        {
            hyd.Add($"A32NX_OVHD_HYD_ENG_{n}AB_PUMP_DISC_PB_HAS_FAULT");
            hyd.Add($"A32NX_HYD_ENG_{n}AB_PUMP_DISC");
        }
        hyd.Add("A32NX_HYD_GREEN_SYSTEM_1_SECTION_PRESSURE_SWITCH");
        hyd.Add("A32NX_HYD_YELLOW_SYSTEM_1_SECTION_PRESSURE_SWITCH");
        d["Hydraulics"] = hyd;

        var bleed = new List<string>();
        for (int n = 1; n <= 4; n++) bleed.Add($"A32NX_OVHD_PNEU_ENG_{n}_BLEED_PB_HAS_FAULT");
        bleed.Add("A32NX_OVHD_PNEU_APU_BLEED_PB_HAS_FAULT");
        foreach (var s in new[] { "L", "C", "R" }) bleed.Add($"A32NX_PNEU_XBLEED_VALVE_{s}_OPEN");
        d["Bleed Air"] = bleed;

        var cond = new List<string> { "A32NX_COND_CKPT_TEMP" };
        for (int n = 1; n <= 8; n++) cond.Add($"A32NX_COND_MAIN_DECK_{n}_TEMP");
        for (int n = 1; n <= 7; n++) cond.Add($"A32NX_COND_UPPER_DECK_{n}_TEMP");
        for (int n = 1; n <= 2; n++)
        {
            cond.Add($"A32NX_OVHD_COND_PACK_{n}_PB_HAS_FAULT");
            cond.Add($"A32NX_OVHD_COND_HOT_AIR_{n}_PB_HAS_FAULT");
            for (int ch = 1; ch <= 2; ch++) cond.Add($"A32NX_COND_FDAC_{n}_CHANNEL_{ch}_FAILURE");
        }
        foreach (var z in new[] { "FWD", "BULK" }) cond.Add($"A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{z}_PB_HAS_FAULT");
        cond.Add("A32NX_OVHD_CARGO_AIR_HEATER_PB_HAS_FAULT");
        d["Air Conditioning"] = cond;

        var press = new List<string>();
        for (int n = 1; n <= 4; n++)
        {
            for (int ch = 1; ch <= 2; ch++) press.Add($"A32NX_PRESS_OCSM_{n}_CHANNEL_{ch}_FAILURE");
            press.Add($"A32NX_PRESS_OCSM_{n}_AUTO_PARTITION_FAILURE");
        }
        press.Add("A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE");
        d["Pressurization"] = press;

        var vent = new List<string>();
        foreach (var id in new[] { "FWD", "AFT" })
            for (int ch = 1; ch <= 2; ch++) vent.Add($"A32NX_VENT_{id}_VCM_CHANNEL_{ch}_FAILURE");
        vent.Add("A32NX_VENT_OVERPRESSURE_RELIEF_VALVE_IS_OPEN");
        d["Ventilation"] = vent;

        d["Anti Ice"] = new List<string> { "A32NX_ICING_STATE_ICING_STICK_INDICATOR" };

        var fire = new List<string> { "A32NX_FIRE_DETECTED_MLG" };
        for (int n = 1; n <= 4; n++)
        {
            fire.Add($"A32NX_ENG_{n}_ON_FIRE");
            for (int b = 1; b <= 2; b++)
            {
                fire.Add($"A32NX_OVHD_FIRE_SQUIB_{b}_ENG_{n}_IS_ARMED");
                fire.Add($"A32NX_OVHD_FIRE_SQUIB_{b}_ENG_{n}_IS_DISCHARGED");
            }
        }
        fire.Add("A32NX_OVHD_FIRE_SQUIB_1_APU_1_IS_DISCHARGED");
        d["Fire"] = fire;

        var adirs = new List<string> { "A32NX_ADIRS_REMAINING_IR_ALIGNMENT_TIME" };
        for (int n = 1; n <= 3; n++) adirs.Add($"A32NX_ADIRS_ADIRU_{n}_STATE");
        d["ADIRS"] = adirs;

        var fcc = new List<string>();
        for (int n = 1; n <= 3; n++) { fcc.Add($"A32NX_PRIM_{n}_HEALTHY"); fcc.Add($"A32NX_SEC_{n}_HEALTHY"); }
        fcc.Add("A32NX_FWS1_IS_HEALTHY"); fcc.Add("A32NX_FWS2_IS_HEALTHY");
        d["Flight Control Computers"] = fcc;

        var gear = new List<string>();
        foreach (var lc in new[] { "1", "2" })
            foreach (var sd in new[] { "LEFT", "RIGHT" }) gear.Add($"A32NX_LGCIU_{lc}_{sd}_GEAR_DOWNLOCKED");
        gear.Add("A32NX_LGCIU_1_NOSE_GEAR_COMPRESSED");
        d["Gear"] = gear;

        d["Warnings"] = new List<string>
        {
            "A32NX_MASTER_WARNING", "A32NX_MASTER_CAUTION", "A32NX_AUTOPILOT_AUTOLAND_WARNING"
        };

        var ab = new List<string> { "A32NX_AUTOBRAKES_ARMED_MODE", "A32NX_BTV_STATE" };
        for (int n = 1; n <= 16; n++) ab.Add($"A32NX_BRAKE_TEMPERATURE_{n}");
        d["Autobrake"] = ab;

        // Engines: state + per-engine N1/N2/N3/EGT/FF/oil/reverser.
        var eng = new List<string>();
        for (int n = 1; n <= 4; n++) eng.Add($"A32NX_ENGINE_STATE:{n}");
        for (int n = 1; n <= 4; n++)
        {
            eng.Add($"A32NX_ENGINE_N1:{n}"); eng.Add($"A32NX_ENGINE_N2:{n}"); eng.Add($"A32NX_ENGINE_N3:{n}");
            eng.Add($"A32NX_ENGINE_EGT:{n}"); eng.Add($"A32NX_ENGINE_FF:{n}");
            eng.Add($"ENG_OIL_TEMP:{n}"); // oil pressure omitted — not modelled (see GetVariables)
            if (n == 2 || n == 3) eng.Add($"A32NX_REVERSER_{n}_DEPLOYED"); // only inboard engines have reversers
        }
        d["Engines"] = eng;
        // Live per-engine thrust-lever angle readouts (the detent is also spoken
        // automatically as the levers move). The set combos in the panel are
        // write-only, so these read-outs are the actual current state.
        d["Thrust Levers"] = new List<string>
        {
            "A32NX_AUTOTHRUST_TLA:1", "A32NX_AUTOTHRUST_TLA:2",
            "A32NX_AUTOTHRUST_TLA:3", "A32NX_AUTOTHRUST_TLA:4"
        };

        d["Flaps and Brakes"] = new List<string>
        {
            "A32NX_FLAPS_HANDLE_INDEX", "A32NX_SPOILERS_HANDLE_POSITION", "A32NX_SPOILERS_ARMED"
        };
        // Exterior lights are now On/Off combos in the panel itself (auto-announced),
        // so they are NOT duplicated as read-only display variables here.
        d["RMP"] = new List<string>
        {
            "FBW_RMP_FREQUENCY_ACTIVE_1", "FBW_RMP_FREQUENCY_STANDBY_1",
            "FBW_RMP_FREQUENCY_ACTIVE_2", "FBW_RMP_FREQUENCY_STANDBY_2",
            "FBW_RMP_FREQUENCY_ACTIVE_3", "FBW_RMP_FREQUENCY_STANDBY_3"
        };
        d["Status"] = new List<string> { "A32NX_FMGC_FLIGHT_PHASE" };
        d["FCU"] = new List<string>
        {
            // AP1/AP2/ATHR/LOC/APPR/EXPED/TRK-FPA are now stateful combos in the
            // FCU control panel, so they're not duplicated here as readouts.
            "A32NX_FMA_LATERAL_MODE", "A32NX_FMA_VERTICAL_MODE", "A32NX_APPROACH_CAPABILITY",
            "FD_ACTIVE"
        };
        // The EIS baro value is an ARINC429 word — NOT shown as a raw display field
        // (it reads ~14 billion) — but TryGetDisplayOverride decodes it to clean
        // text ("1013 hPa" / "29.92 inHg" / "Standard"), so the same value the
        // pilot hears auto-announced now also reads in the panel, alongside the
        // plain preselected QNH.
        d["EFIS Captain"] = new List<string> { "A32NX_FCU_LEFT_EIS_BARO_HPA", "A380X_EFIS_L_BARO_PRESELECTED" };
        d["EFIS First Officer"] = new List<string> { "A32NX_FCU_RIGHT_EIS_BARO_HPA", "A380X_EFIS_R_BARO_PRESELECTED" };
        d["Radios"] = new List<string>
        {
            "COM_ACTIVE_FREQUENCY:1", "COM_STANDBY_FREQUENCY:1",
            "COM_ACTIVE_FREQUENCY:2", "COM_STANDBY_FREQUENCY:2",
            "COM_ACTIVE_FREQUENCY:3", "COM_STANDBY_FREQUENCY:3"
        };
        d["Transponder"] = new List<string> { "XPNDR_CODE", "A32NX_DCDU_ATC_MSG_WAITING" };
        // Minimums are ARINC429 words — TryGetDisplayOverride decodes them to
        // "200 feet" / "Not set" (the raw word reads ~4.29e9). They also
        // auto-announce when set on the MCDU PERF APPR page.
        d["Minimums"] = new List<string> { "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE", "A32NX_FM1_DECISION_HEIGHT" };

        // ---- A32NX shared gap readouts ----
        d["Autobrake"].AddRange(new[]
        {
            "A32NX_BRAKE_FAN_RUNNING", "A32NX_BRAKES_HOT",
            "A32NX_HYD_BRAKE_ALTN_LEFT_PRESS", "A32NX_HYD_BRAKE_ALTN_RIGHT_PRESS", "A32NX_HYD_BRAKE_ALTN_ACC_PRESS"
        });
        d["Gear"].AddRange(new[] { "A32NX_GEAR_LEVER_LOCKED", "A32NX_LG_GRVTY_MASTER_SWITCH_GUARD" });
        d["Pressurization"].AddRange(new[] { "A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB", "A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB" });
        d["Fuel"].AddRange(new[] { "A380X_OVHD_FUEL_JETTISON_IS_OPEN", "A32NX_TOTAL_FUEL_VOLUME" });
        d["Hydraulics"].Add("A32NX_OVHD_HYD_PTU_PB_HAS_FAULT");
        d["Ventilation"].AddRange(new[] { "A32NX_VENTILATION_BLOWER_FAULT", "A32NX_VENTILATION_EXTRACT_FAULT" });
        d["Anti Ice"].AddRange(new[] { "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", "A32NX_PNEU_WING_ANTI_ICE_HAS_FAULT" });
        d["Anti Ice"].AddRange(new[] { "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4" });
        d["Fire"].AddRange(new[] { "A32NX_CARGOSMOKE_FWD_DISCHARGED", "A32NX_CARGOSMOKE_AFT_DISCHARGED" });
        d["Status"].Add("A380X_FMS_DEST_EFOB_BELOW_MIN");

        d["Source Switching"] = new List<string> { "A32NX_CHRONO_ELAPSED_TIME", "A32NX_CHRONO_ET_ELAPSED_TIME" };
        d["ISIS"] = new List<string> { "A32NX_ISIS_LS_ACTIVE", "A32NX_ISIS_BUGS_ACTIVE" };
        d["Oxygen"] = new List<string> { "A32NX_OXYGEN_TMR_RESET_FAULT" };
        d["Calls"] = new List<string> { "A32NX_SLIDES_ARMED", "A32NX_EVAC_COMMAND_FAULT" };
        d["ECAM Control Panel"] = new List<string> { "A32NX_SD_MORE_SHOWN" };
        d["Wipers"] = new List<string> { "WIPER_LEFT_ON", "WIPER_RIGHT_ON" };
        d["Speeds"] = new List<string> { "A32NX_SPEEDS_VLS", "A32NX_SPEEDS_VAPP", "A32NX_SPEEDS_GD", "A32NX_SPEEDS_F", "A32NX_SPEEDS_S" };
        d["Ground"] = new List<string> { "A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS" };

        // ---- plain SD-page scalar readouts ----
        for (int n = 1; n <= 4; n++)
        {
            d["ELEC"].AddRange(new[]
            {
                $"A32NX_ELEC_ENG_GEN_{n}_POTENTIAL", $"A32NX_ELEC_ENG_GEN_{n}_LOAD",
                $"A32NX_ELEC_ENG_GEN_{n}_FREQUENCY", $"A32NX_ELEC_ENG_GEN_{n}_IDG_OIL_OUTLET_TEMPERATURE",
                $"A32NX_ELEC_TR_{n}_POTENTIAL", $"A32NX_ELEC_TR_{n}_CURRENT", $"A32NX_ELEC_BAT_{n}_CURRENT"
            });
        }
        for (int n = 1; n <= 2; n++)
            d["ELEC"].AddRange(new[] { $"A32NX_ELEC_APU_GEN_{n}_POTENTIAL", $"A32NX_ELEC_APU_GEN_{n}_LOAD", $"A32NX_ELEC_APU_GEN_{n}_FREQUENCY" });
        d["ELEC"].AddRange(new[] { "A32NX_ELEC_EXT_PWR_POTENTIAL", "A32NX_ELEC_EXT_PWR_FREQUENCY", "A32NX_ELEC_EMER_GEN_POTENTIAL", "A32NX_ELEC_STAT_INV_POTENTIAL" });

        foreach (var sys in new[] { "GREEN", "YELLOW" })
            d["Hydraulics"].AddRange(new[] { $"A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE", $"A32NX_HYD_{sys}_RESERVOIR_LEVEL", $"A32NX_HYD_{sys}_RESERVOIR_LEVEL_IS_LOW" });

        for (int n = 1; n <= 4; n++)
            d["Bleed Air"].AddRange(new[] { $"A32NX_PNEU_ENG_{n}_PRECOOLER_OUTLET_TEMPERATURE", $"A32NX_PNEU_ENG_{n}_REGULATED_TRANSDUCER_PRESSURE", $"A32NX_PNEU_ENG_{n}_STARTER_VALVE_OPEN" });
        d["Bleed Air"].AddRange(new[] { "A32NX_COND_PACK_1_OUTLET_TEMPERATURE", "A32NX_COND_PACK_2_OUTLET_TEMPERATURE", "A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE" });

        for (int n = 1; n <= 4; n++)
            d["Engines"].AddRange(new[] { $"A32NX_ENGINE_OIL_QTY:{n}", $"ENG_VIBRATION:{n}", $"ENG_IGN_POS:{n}", $"ENG_VALVE_SWITCH:{n}" });

        d["Air Conditioning"].AddRange(new[] { "A32NX_COND_CARGO_FWD_TEMP", "A32NX_COND_CARGO_BULK_TEMP", "A32NX_COND_CKPT_DUCT_TEMP" });

        d["Flight Control Computers"].AddRange(new[]
        {
            "A32NX_LEFT_FLAPS_POSITION_PERCENT", "A32NX_RIGHT_FLAPS_POSITION_PERCENT",
            "A32NX_LEFT_SLATS_POSITION_PERCENT", "A32NX_RIGHT_SLATS_POSITION_PERCENT",
            "ELEVATOR_TRIM", "RUDDER_TRIM_PCT", "A32NX_PRIORITY_TAKEOVER:1", "A32NX_PRIORITY_TAKEOVER:2"
        });

        d["Gear"].AddRange(new[] { "GEAR_LEFT_POS", "GEAR_CENTER_POS", "GEAR_RIGHT_POS", "A32NX_AUTOBRAKES_RTO_ARMED" });

        return d;
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        return new Dictionary<string, string>
        {
            ["A32NX_AUTOBRAKES_SELECTED_MODE"] = "A32NX_AUTOBRAKES_ARMED_MODE",
            // FCU push/pull buttons → the engagement/mode state they drive.
            ["A32NX.FCU_AP_1_PUSH"] = "A32NX_AUTOPILOT_1_ACTIVE",
            ["A32NX.FCU_AP_2_PUSH"] = "A32NX_AUTOPILOT_2_ACTIVE",
            ["A32NX.FCU_ATHR_PUSH"] = "A32NX_AUTOTHRUST_STATUS",
            ["A32NX.FCU_LOC_PUSH"] = "A32NX_FCU_LOC_MODE_ACTIVE",
            ["A32NX.FCU_APPR_PUSH"] = "A32NX_FCU_APPR_MODE_ACTIVE",
            ["A32NX.FCU_EXPED_PUSH"] = "A32NX_FMA_EXPEDITE_MODE",
            ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = "A32NX_TRK_FPA_MODE_ACTIVE"
        };
    }

    // Input-mode FCU push/pull + AP chords → the A380 FCU events (HDG/VS pull use
    // the _TO_AP_ variants). The base HandleHotkeyAction consults this map for any
    // action our switch doesn't handle.
    protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        return new Dictionary<HotkeyAction, string>
        {
            // FCU knob push/pull are handled in HandleHotkeyAction (event + spoken
            // readback), so they are intentionally NOT mapped here — a map entry
            // would also fire a redundant post-action state announcement.
            [HotkeyAction.ToggleAutopilot1] = "A32NX.FCU_AP_1_PUSH",
            [HotkeyAction.ToggleAutopilot2] = "A32NX.FCU_AP_2_PUSH",
            [HotkeyAction.ToggleApproachMode] = "A32NX.FCU_APPR_PUSH",
        };
    }

    // ===================================================================
    // Update hook (bridge diagnostics)
    // ===================================================================
    // Last announced E/WD code per line, so a line only speaks when it changes.
    private readonly Dictionary<string, long> _lastEwdCode = new();
    private readonly double[] _tla = { double.NaN, double.NaN, double.NaN, double.NaN };
    private readonly string?[] _lastEngDetent = new string?[4];
    private string? _lastAllDetent;
    // FBW TLA detents: IDLE 0, CLB 25, FLX/MCT 35, TOGA 45, reverse negative.
    private static string? TlaDetent(double v) =>
        Math.Abs(v) < 1.5 ? "Idle" :
        Math.Abs(v - 25) < 2.5 ? "Climb" :
        Math.Abs(v - 35) < 2.5 ? "Flex M C T" :
        Math.Abs(v - 45) < 2.5 ? "TOGA" :
        v <= -15 ? "Maximum reverse" :
        v < -2 ? "Reverse idle" : null;

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Keep the live current state of the FCU engage/mode toggles so their
        // combos can decide whether a "set" needs to fire the toggle event. Fall
        // through so the base still auto-announces the state change.
        if (_fcuToggleEvents.ContainsKey(varName)) _fcuStateCache[varName] = value;
        // Thrust lever angle -> announce the DETENT when it changes (not the raw
        // angle, which would spam). FBW TLA detents: IDLE 0, CLB 25, FLX/MCT 35,
        // TOGA 45, reverse negative. Only speak when the lever is AT a detent so
        // mid-travel doesn't false-announce. Returns true to suppress the raw value.
        if (varName.StartsWith("A32NX_AUTOTHRUST_TLA:", StringComparison.Ordinal))
        {
            int eng = varName[varName.Length - 1] - '0';
            if (eng >= 1 && eng <= 4)
            {
                _tla[eng - 1] = value;
                string? det = TlaDetent(value);
                if (det != null)
                {
                    // When all four levers sit at the same detent (the usual case)
                    // announce once for "all"; when split, announce the engine that
                    // moved. Mid-travel (det == null) is silent.
                    bool allSame = true;
                    for (int i = 0; i < 4; i++)
                        if (double.IsNaN(_tla[i]) || TlaDetent(_tla[i]) != det) { allSame = false; break; }
                    if (allSame)
                    {
                        if (det != _lastAllDetent)
                        {
                            _lastAllDetent = det;
                            for (int i = 0; i < 4; i++) _lastEngDetent[i] = det;
                            announcer.Announce($"All thrust levers {det}");
                        }
                    }
                    else if (det != _lastEngDetent[eng - 1])
                    {
                        _lastEngDetent[eng - 1] = det;
                        _lastAllDetent = null;
                        announcer.Announce($"Thrust lever {eng} {det}");
                    }
                }
            }
            return true;
        }

        // ECAM upper (E/WD) memo/warning lines: decode the numeric code to text
        // and announce it (with its FWC colour as a priority word). Returning
        // true suppresses the generic raw-number announcement.
        if (varName.StartsWith("A32NX_EWD_LOWER_"))
        {
            long code = (long)value;
            if (!_lastEwdCode.TryGetValue(varName, out var prev) || prev != code)
            {
                _lastEwdCode[varName] = code;
                // Honour the "ECAM E/WD call-outs" mute from the Monitor Manager.
                if (Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains("FBWA380_ECAM_MEMOS"))
                    return true;
                string text = EWDMessageLookupA380.GetMessage(code);
                if (!string.IsNullOrWhiteSpace(text) &&
                    !text.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                {
                    string priority = EWDMessageLookupA380.GetMessagePriority(code);
                    announcer.Announce(string.IsNullOrEmpty(priority) ? text : $"{text}, {priority}");
                }
            }
            return true;
        }

        // ---- On-demand flaps / gear readouts (global hotkeys) ----
        // Only intercept while a readout is pending; otherwise fall through so the
        // normal continuous-monitor announcement (on change) still fires.
        if (_reqFlaps && varName == "A32NX_FLAPS_HANDLE_INDEX")
        {
            _reqFlaps = false;
            string[] detents = { "Up", "1", "2", "3", "Full" };
            int i = (int)Math.Round(value);
            announcer.AnnounceImmediate("Flaps " + (i >= 0 && i < detents.Length ? detents[i] : value.ToString()));
            return true;
        }
        if (_reqGear && varName == "A32NX_GEAR_HANDLE_POSITION")
        {
            _reqGear = false;
            announcer.AnnounceImmediate(value > 0.5 ? "Gear down" : "Gear up");
            return true;
        }
        if (_readoutKey != null && varName == _readoutKey)
        {
            string lbl = _readoutLabel ?? varName;
            string spoken;
            if (_readoutMap != null && _readoutMap.TryGetValue(Math.Round(value), out var dsc))
                spoken = lbl + " " + dsc;
            else
                spoken = string.IsNullOrEmpty(_readoutUnit) ? $"{lbl} {value:0}" : $"{lbl} {value:0} {_readoutUnit}";
            announcer.AnnounceImmediate(spoken);
            _readoutKey = null; _readoutMap = null;
            return true;
        }
        // Live EFIS baro auto-announce — spoken as the pilot turns the knob, in
        // whichever unit the EFIS is set to. The HPA var is an ARINC429 word
        // (decode it); BaroHpa range-detects so it still works if the value ever
        // comes through in inHg. Only spoken in QNH (STD is handled below).
        if (varName == "A32NX_FCU_LEFT_EIS_BARO_HPA" || varName == "A32NX_FCU_RIGHT_EIS_BARO_HPA")
        {
            bool capt = varName.Contains("LEFT");
            if (!BaroHpa(new Arinc429Word(value).ValueOr(0), out int hpa)) return true; // STD / no data
            if (hpa != (capt ? _lastBaroL : _lastBaroR))
            {
                if (capt) _lastBaroL = hpa; else _lastBaroR = hpa;
                if ((capt ? _baroStdL : _baroStdR) != true)
                    announcer.Announce(BaroPhrase(capt, hpa, false));
            }
            return true;
        }
        // EFIS baro push (STD) / pull (QNH) — announce the mode change.
        if (varName == "A32NX_FCU_LEFT_EIS_BARO_IS_STD" || varName == "A32NX_FCU_RIGHT_EIS_BARO_IS_STD")
        {
            bool capt = varName.Contains("LEFT");
            bool std = value > 0.5;
            bool? prev = capt ? _baroStdL : _baroStdR;
            if (capt) _baroStdL = std; else _baroStdR = std;
            if (prev.HasValue && prev.Value != std) // skip the baseline read
                announcer.Announce(std
                    ? $"{(capt ? "Captain" : "First officer")} altimeter standard"
                    : BaroPhrase(capt, capt ? _lastBaroL : _lastBaroR, true));
            return true;
        }
        // EFIS baro UNIT lives on XMLVAR_Baro_Selector_HPA_{1,2} (1=hPa, 0=inHg) —
        // NOT A32NX_FCU_EFIS_*_BARO_IS_INHG, which is stuck at 0 on the A380X
        // (verified live: F/O reads XMLVAR=0/inHg while IS_INHG stays 0/hPa, so
        // the readout always said hPa). Track the real unit here and re-announce
        // the setting in the new unit when the pilot switches it.
        if (varName == "XMLVAR_Baro_Selector_HPA_1" || varName == "XMLVAR_Baro_Selector_HPA_2")
        {
            bool capt = varName.EndsWith("_1", StringComparison.Ordinal);
            bool inHg = value < 0.5;
            bool? prev = capt ? _baroInHgL : _baroInHgR;
            if (capt) _baroInHgL = inHg; else _baroInHgR = inHg;
            if (prev.HasValue && prev.Value != inHg) // skip the baseline read
            {
                int last = capt ? _lastBaroL : _lastBaroR;
                if (last > 0 && (capt ? _baroStdL : _baroStdR) != true)
                    announcer.Announce(BaroPhrase(capt, last, false));
                else
                    announcer.Announce($"{(capt ? "Captain" : "First officer")} baro unit {(inHg ? "inches" : "hectopascals")}");
            }
            return true;
        }
        // Minimums are ARINC429 words — decode and announce when a minimum is set
        // or changed on the MCDU PERF APPR page (no announce on clear/NCD).
        if (varName == "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE" || varName == "A32NX_FM1_DECISION_HEIGHT")
        {
            bool baro = varName.EndsWith("DESCENT_ALTITUDE", StringComparison.Ordinal);
            var w = new Arinc429Word(value);
            int ft = (w.IsNormalOperation || w.IsFunctionalTest) ? (int)(Math.Round(w.Value / 10.0) * 10) : -1;
            if (ft != (baro ? _lastBaroMin : _lastDh))
            {
                if (baro) _lastBaroMin = ft; else _lastDh = ft;
                if (ft > 0) announcer.Announce($"{(baro ? "Baro minimum" : "Decision height")} {ft} feet");
            }
            return true;
        }
        if (_reqBaro && varName == "KOHLSMAN_HG")
        {
            _reqBaro = false;
            announcer.AnnounceImmediate($"Altimeter {value * 33.8639:0} hectopascals, {value:0.00} inches");
            return true;
        }
        if (_reqGw && varName == "GROSS_WEIGHT_KG")
        {
            _reqGw = false;
            announcer.AnnounceImmediate($"Gross weight {value:0} kilograms");
            return true;
        }

        // ---- FCU readouts: value + managed-indicator pairs ----
        // Each Read* hotkey requests the value var(s) and the managed indicator
        // and force-updates them; we announce once the pair (or, for VS, the
        // mode + matching value) has arrived. The _req* guards ensure these are
        // only intercepted during an active readout — otherwise they fall
        // through so the FCU panel's display readouts work normally.
        if (_reqHdg && (varName == "A32NX_AUTOPILOT_HEADING_SELECTED" || varName == "A32NX_FCU_HDG_MANAGED_DASHES"))
        {
            // SimConnect's native L-var read returns this ANGULAR var in RADIANS
            // (verified live: 250° reads 4.363, 300° reads 5.236) — non-angular FCU
            // vars like VS/speed come through unscaled. Convert to degrees only when
            // the magnitude is in the radian range (<= 2pi), so a future build/path
            // that returns degrees directly is handled correctly too.
            if (varName.EndsWith("HEADING_SELECTED"))
            {
                double hv = Math.Abs(value) <= (Math.PI * 2 + 0.05) ? value * 180.0 / Math.PI : value;
                _pHdgVal = ((hv % 360) + 360) % 360;
            }
            else _pHdgMgd = value;
            if (_pHdgVal.HasValue && _pHdgMgd.HasValue)
            {
                string st = _pHdgMgd.Value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU heading {_pHdgVal.Value:000} degrees, {st}");
                _pHdgVal = _pHdgMgd = null; _reqHdg = false;
            }
            return true;
        }
        if (_reqSpd && (varName == "A32NX_AUTOPILOT_SPEED_SELECTED" || varName == "A32NX_FCU_SPD_MANAGED_DOT"))
        {
            if (varName.EndsWith("SPEED_SELECTED")) _pSpdVal = value; else _pSpdMgd = value;
            if (_pSpdVal.HasValue && _pSpdMgd.HasValue)
            {
                // Managed speed parks SPEED_SELECTED at -1 (dashes on the FCU). Don't
                // format that as a bogus "mach -1.00" — announce the managed state.
                bool managed = _pSpdMgd.Value > 0 || _pSpdVal.Value < 0;
                string spoken;
                if (managed)
                    spoken = "FCU speed managed";
                else
                    // SPEED_SELECTED carries mach (< 1, dimensionless — no SI scaling)
                    // in mach mode; otherwise it's a velocity, so the SimConnect L:var
                    // read returns m/s (SI) and must be converted to knots (x1.943844).
                    // Unlike the A320 (which reads the pre-formatted A32NX_FCU_AFS_DISPLAY_*
                    // value), the A380 has no display var and reads the raw target.
                    spoken = _pSpdVal.Value < 10
                        ? $"FCU speed mach {_pSpdVal.Value:0.00}, selected"
                        : $"FCU speed {_pSpdVal.Value * 1.943844:000} knots, selected";
                announcer.AnnounceImmediate(spoken);
                _pSpdVal = _pSpdMgd = null; _reqSpd = false;
            }
            return true;
        }
        if (_reqAlt && (varName == "FCU_ALT_VALUE" || varName == "A32NX_FCU_ALT_MANAGED"))
        {
            if (varName == "FCU_ALT_VALUE") _pAltVal = value; else _pAltMgd = value;
            if (_pAltVal.HasValue && _pAltMgd.HasValue)
            {
                string st = _pAltMgd.Value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU altitude {_pAltVal.Value:0} feet, {st}");
                _pAltVal = _pAltMgd = null; _reqAlt = false;
            }
            return true;
        }
        if (_reqVs && (varName == "A32NX_AUTOPILOT_VS_SELECTED" || varName == "A32NX_AUTOPILOT_FPA_SELECTED" ||
                       varName == "A32NX_TRK_FPA_MODE_ACTIVE"))
        {
            // V/S is a RATE, so the SimConnect L:var read returns it in m/s (SI),
            // NOT feet/min (verified live: MobiFlight=2000 fpm reads as 10.16 here).
            // Convert m/s -> fpm (x196.85) and round to the FCU's 100-fpm step.
            if (varName.EndsWith("VS_SELECTED")) _pVsVal = Math.Round(value * 196.8503937 / 100.0) * 100.0;
            // FPA is angular, so SimConnect returns it in radians (like heading);
            // convert when the magnitude is in the radian range (FPA maxes at ~9.9°).
            else if (varName.EndsWith("FPA_SELECTED")) _pFpaVal = Math.Abs(value) <= 0.2 ? value * 180.0 / Math.PI : value;
            else _pVsMode = value;
            if (_pVsMode.HasValue && ((_pVsMode.Value > 0 && _pFpaVal.HasValue) || (_pVsMode.Value <= 0 && _pVsVal.HasValue)))
            {
                string spoken = _pVsMode.Value > 0
                    ? $"FCU flight path angle {_pFpaVal!.Value:0.0} degrees"
                    : $"FCU vertical speed {_pVsVal!.Value:0} feet per minute";
                announcer.AnnounceImmediate(spoken);
                _pVsVal = _pFpaVal = _pVsMode = null; _reqVs = false;
            }
            return true;
        }

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// Engine-mode selector fans the chosen position (0=Crank, 1=Norm,
    /// 2=Ignition/Start) out to all four engines via TURBINE_IGNITION_SWITCH_SET{N}.
    /// </summary>
    // Exterior-light On/Off combos -> the stock SET event fired with 0/1. The
    // combo's value comes from the matching LIGHT * simvar (Continuous +
    // announced), so it reflects state and auto-announces; this only handles the
    // write side.
    // Settable temperature selectors: the _SET combo key -> the FBW knob L:var
    // (0-300) and the zone's Celsius range. knob = (temp - Lo)/(Hi - Lo)*300.
    private sealed record TempSel(string Knob, double Lo, double Hi, string Label);
    private static readonly Dictionary<string, TempSel> _tempSelectors = new Dictionary<string, TempSel>
    {
        ["COND_CKPT_TEMP_SET"] = new TempSel("A32NX_OVHD_COND_CKPT_SELECTOR_KNOB", 18, 30, "Cockpit"),
        ["COND_CABIN_TEMP_SET"] = new TempSel("A32NX_OVHD_COND_CABIN_SELECTOR_KNOB", 18, 30, "Cabin"),
        ["CARGO_FWD_TEMP_SET"] = new TempSel("A32NX_OVHD_CARGO_AIR_FWD_SELECTOR_KNOB", 5, 25, "Forward cargo"),
        ["CARGO_BULK_TEMP_SET"] = new TempSel("A32NX_OVHD_CARGO_AIR_BULK_SELECTOR_KNOB", 5, 25, "Bulk cargo"),
    };

    private static readonly Dictionary<string, string> _extLightSetEvents = new Dictionary<string, string>
    {
        ["LIGHT_BEACON"] = "BEACON_LIGHTS_SET",
        ["LIGHT_NAV"] = "NAV_LIGHTS_SET",
        ["LIGHT_WING"] = "WING_LIGHTS_SET",
        ["LIGHT_LOGO"] = "LOGO_LIGHTS_SET",
        ["LIGHT_LANDING"] = "LANDING_LIGHTS_SET"
    };

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (_extLightSetEvents.TryGetValue(varKey, out var lightEvent))
        {
            simConnect.SendEvent(lightEvent, (uint)Math.Round(value));
            return true;
        }
        // Air-cond/cargo target temperature: user enters degrees C; the FBW
        // selector knob is 0-300 over the zone's range (cockpit/cabin 18-30 C,
        // cargo 5-25 C), so knob = (temp - lo) / (hi - lo) * 300.
        if (_tempSelectors.TryGetValue(varKey, out var ts))
        {
            if (value < ts.Lo || value > ts.Hi)
            {
                announcer.AnnounceImmediate($"Temperature must be between {ts.Lo} and {ts.Hi} degrees Celsius.");
                return true;
            }
            simConnect.SetLVar(ts.Knob, (value - ts.Lo) / (ts.Hi - ts.Lo) * 300.0);
            announcer.Announce($"{ts.Label} temperature set to {value:0} degrees");
            return true;
        }
        // Manual pressurization knobs — pass-through position write.
        if (varKey == "PRESS_MAN_ALT_SET" || varKey == "PRESS_MAN_VS_SET")
        {
            simConnect.SetLVar(varKey == "PRESS_MAN_ALT_SET"
                ? "A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB" : "A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB", value);
            announcer.Announce($"Set to {value:0.0}");
            return true;
        }
        // Thrust-lever detent combos -> THROTTLEn_AXIS_SET_EX1 with the detent's
        // axis value (-1..1 scaled to +-16384). Values are the FBW default-style
        // detent calibration (Reverse -1.0 / Rev Idle -0.70 / Idle -0.44 /
        // Climb -0.10 / Flex-MCT 0.53 / TOGA 1.0); the throttle mapping snaps the
        // lever to the detent. Assumes default throttle calibration.
        if (varKey == "THROTTLE_ALL_DETENT" || (varKey.StartsWith("THROTTLE_") && varKey.EndsWith("_DETENT")))
        {
            int idx = (int)Math.Round(value);
            double[] detentAxis = { -1.0, -0.70, -0.44, -0.10, 0.53, 1.0 };
            string[] names = { "Reverse", "Reverse Idle", "Idle", "Climb", "Flex M C T", "TOGA" };
            if (idx < 0 || idx >= detentAxis.Length) return true;
            uint ex1 = unchecked((uint)(int)Math.Round(detentAxis[idx] * 16384));
            if (varKey == "THROTTLE_ALL_DETENT")
            {
                for (int n = 1; n <= 4; n++) simConnect.SendEvent($"THROTTLE{n}_AXIS_SET_EX1", ex1);
                announcer.Announce($"All thrust levers {names[idx]}");
            }
            else
            {
                int eng = varKey.Length > 9 && char.IsDigit(varKey[9]) ? varKey[9] - '0' : 1;
                simConnect.SendEvent($"THROTTLE{eng}_AXIS_SET_EX1", ex1);
                announcer.Announce($"Thrust lever {eng} {names[idx]}");
            }
            return true;
        }
        if (varKey == "ENGINE_MODE_SELECTOR")
        {
            uint mode = (uint)Math.Round(value);
            for (int n = 1; n <= 4; n++) simConnect.SendEvent($"TURBINE_IGNITION_SWITCH_SET{n}", mode);
            return true;
        }
        // Wipers: 0=Off / 75=Slow / 100=Fast → the circuit-power-setting event.
        if (varKey == "WIPER_LEFT" || varKey == "WIPER_RIGHT")
        {
            int circuit = varKey == "WIPER_LEFT" ? 141 : 143;
            simConnect.SendEvent($"ELECTRICAL_CIRCUIT_POWER_SETTING_SET:{circuit}", (uint)Math.Round(value));
            return true;
        }
        // Engine anti-ice combo "ENGn_ANTI_ICE" -> stock K:ANTI_ICE_SET_ENGn
        // (the SimVar / XMLVAR can't be written directly on the A380).
        if (varKey.Length == 13 && varKey.StartsWith("ENG", StringComparison.Ordinal)
            && varKey.EndsWith("_ANTI_ICE", StringComparison.Ordinal)
            && varKey[3] >= '1' && varKey[3] <= '4')
        {
            simConnect.SendEvent($"ANTI_ICE_SET_ENG{varKey[3]}", (uint)Math.Round(value));
            return true;
        }
        // TRK/FPA reference: the A380X has NO toggle event for it — the cockpit
        // button writes the L:var directly. Write the absolute 0/1 via the
        // MobiFlight calculator path (FBW L:var writes over the SimConnect data-def
        // are as unreliable as the reads), not the default SetLVar.
        if (varKey == "A32NX_TRK_FPA_MODE_ACTIVE")
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>L:A32NX_TRK_FPA_MODE_ACTIVE)");
            return true;
        }
        // EFIS baro STD(push)/QNH(pull): also no event on the A380X — write the
        // IS_STD L:var directly (verified live), same as TRK/FPA.
        if (varKey == "A32NX_FCU_LEFT_EIS_BARO_IS_STD" || varKey == "A32NX_FCU_RIGHT_EIS_BARO_IS_STD")
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>L:{varKey})");
            return true;
        }
        // Set QNH: the entered value is in the side's current unit (hPa or inHg).
        // Convert to hPa, validate, then fire K:KOHLSMAN_SET with millibars*16
        // (verified live). KOHLSMAN_SET moves both altimeters together.
        if (varKey == "CAPT_QNH_SET" || varKey == "FO_QNH_SET")
        {
            bool inHg = (varKey == "CAPT_QNH_SET" ? _baroInHgL : _baroInHgR) == true;
            double hpa = inHg ? value * 33.8639 : value;
            if (hpa < 900 || hpa > 1100)
            {
                announcer.AnnounceImmediate(inHg
                    ? "QNH must be between 26.6 and 32.5 inches."
                    : "QNH must be between 900 and 1100 hectopascals.");
                return true;
            }
            simConnect.SendEvent("KOHLSMAN_SET", (uint)Math.Round(hpa * 16.0));
            announcer.Announce(inHg
                ? $"Altimeter set {hpa / 33.8639:0.00} inches"
                : $"Altimeter set {hpa:0} hectopascals");
            return true;
        }
        // EFIS Control Panel controls are ALL direct L:var writes on the A380X
        // (no events — confirmed from efis-cp.xml: ND mode/range, navaid 1/2, the
        // LS/VV/CSTR/ARPT/TRAF option buttons, the WPT/VOR/NDB filter + WX/TERR
        // overlay, OANS range, and the hPa/inHg baro-unit selector). The cockpit
        // buttons run RPN that writes the L:var; the SimConnect data-def write is
        // unreliable for FBW L:vars (same as the reads), so route every one of
        // them through the MobiFlight calculator path to guarantee they actuate.
        if (varKey.StartsWith("A32NX_EFIS_", StringComparison.Ordinal)
            || varKey.StartsWith("A380X_EFIS_", StringComparison.Ordinal)
            || varKey.StartsWith("XMLVAR_Baro_Selector_HPA_", StringComparison.Ordinal))
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varKey})");
            return true;
        }
        // #56 — Wing anti-ice + probe/window heat. Engine anti-ice needs the
        // K:ANTI_ICE_SET_ENGn event (the L:var/XMLVAR don't drive it), but wing
        // and probe heat have NO documented A380X event (the FBW a380-simvars doc
        // omits anti-ice entirely). Route their FBW L:var writes through the
        // MobiFlight calculator path — the SimConnect data-def SetLVar (the old
        // default for these two) is unreliable for FBW L:vars, same as the reads.
        // Wing is a MOMENTARY "PRESSED" XMLVAR: pulse 1 -> 0 so each press is a
        // fresh rising edge that toggles the system. NEEDS AN IN-SIM CONFIRM; if
        // it doesn't actuate, the fallback is the stock K:ANTI_ICE_TOGGLE (wing) /
        // K:PITOT_HEAT_TOGGLE (probe heat).
        if (varKey == "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED")
        {
            simConnect.ExecuteCalculatorCode("1 (>L:XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED)");
            simConnect.ExecuteCalculatorCode("0 (>L:XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED)");
            return true;
        }
        if (varKey == "A32NX_MAN_PITOT_HEAT")
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>L:A32NX_MAN_PITOT_HEAT)");
            return true;
        }
        // FCU engage/mode toggle combos: the backing L:var is read-only state, so
        // a "set" fires the matching toggle event — but only when the picked state
        // differs from the current one (the events toggle, they don't set an
        // absolute value). Current state comes from the live monitor cache.
        if (_fcuToggleEvents.TryGetValue(varKey, out var fcuEvt))
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (_fcuStateCache.TryGetValue(varKey, out var cur) ? cur : 0) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent(fcuEvt);
            return true; // never SetLVar the read-only state var
        }
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    // ===================================================================
    // FCU — ported from the A320 integration (same SET events; A380 readout
    // vars). Set dialogs send A32NX.FCU_*_SET; reads request value + managed
    // and announce via the pairing in ProcessSimVarUpdate.
    // ===================================================================
    private double? _pHdgVal, _pHdgMgd, _pSpdVal, _pSpdMgd, _pAltVal, _pAltMgd, _pVsVal, _pFpaVal, _pVsMode;
    private bool _reqHdg, _reqSpd, _reqAlt, _reqVs;
    private bool _reqFlaps, _reqGear, _reqBaro, _reqGw;
    private int _lastBaroL = -1, _lastBaroR = -1; // last announced EFIS baro (whole hPa)
    private bool? _baroStdL, _baroStdR; // last EFIS baro STD(true)/QNH(false) per side
    private bool? _baroInHgL, _baroInHgR; // last EFIS baro unit inHg(true)/hPa(false) per side
    private int _lastBaroMin = -2, _lastDh = -2; // last announced minimums (ft; -1 = none/NCD)

    // Decode/normalise an EFIS baro setting to whole hPa; false for STD/no-data.
    // The FBW _HPA var is hPa, but range-detect inHg too so the read-out still
    // works if the EFIS is switched to inches and the value comes through scaled.
    private static bool BaroHpa(double raw, out int hpa)
    {
        if (raw >= 800 && raw <= 1100) { hpa = (int)Math.Round(raw); return true; }
        if (raw >= 22 && raw <= 33) { hpa = (int)Math.Round(raw * 33.8639); return true; }
        hpa = 0; return false;
    }
    // Speak the baro setting in the side's selected unit (hPa or inHg).
    private string BaroPhrase(bool capt, int hpa, bool qnh)
    {
        string who = capt ? "Captain" : "First officer";
        string pre = qnh ? "Q N H " : "";
        return (capt ? _baroInHgL : _baroInHgR) == true
            ? $"{who} altimeter {pre}{hpa / 33.8639:0.00} inches"
            : $"{who} altimeter {pre}{hpa} hectopascals";
    }

    // Panel-display decode for the ARINC429 EFIS baro + minimums words, so the
    // same value the pilot HEARS auto-announced also reads cleanly in the panel
    // (the raw word would render as ~14 billion). See MainForm.UpdateDisplayText.
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        switch (varKey)
        {
            case "A32NX_FCU_LEFT_EIS_BARO_HPA":
            case "A32NX_FCU_RIGHT_EIS_BARO_HPA":
            {
                bool capt = varKey.Contains("LEFT");
                // STD flag wins; otherwise decode the word (range-aware) and show
                // in the side's selected unit.
                if ((capt ? _baroStdL : _baroStdR) == true ||
                    !BaroHpa(new Arinc429Word(value).ValueOr(0), out int hpa))
                {
                    displayText = "Standard";
                    return true;
                }
                displayText = (capt ? _baroInHgL : _baroInHgR) == true
                    ? $"{hpa / 33.8639:0.00} inHg"
                    : $"{hpa} hPa";
                return true;
            }
            case "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE":
            case "A32NX_FM1_DECISION_HEIGHT":
            {
                var w = new Arinc429Word(value);
                int ft = (w.IsNormalOperation || w.IsFunctionalTest)
                    ? (int)(Math.Round(w.Value / 10.0) * 10) : -1;
                displayText = ft > 0 ? $"{ft} feet" : "Not set";
                return true;
            }
        }
        return false;
    }

    // FCU engage/mode toggle combos -> the input event that flips them. The
    // combo's backing var is read-only engage state, so a set fires the event
    // only when the desired state differs from the cached current value.
    // Names verified against the A380X cockpit XML + FCU AutopilotManager source:
    //  - AP1/AP2/LOC/APPR/EXPED fire the A32NX.FCU_*_PUSH key events (the A380X
    //    H:A320_Neo_FCU_*_PUSH H-events translate to these).
    //  - A/THR uses the STOCK K:AUTO_THROTTLE_ARM (A32NX.FCU_ATHR_PUSH is not the
    //    event the A380X FCU button uses).
    //  - TRK/FPA has NO event on the A380X — the button writes the L:var directly,
    //    so it's intentionally NOT in this map: the combo's default L:var write
    //    (A32NX_TRK_FPA_MODE_ACTIVE = 0/1) drives it.
    private static readonly Dictionary<string, string> _fcuToggleEvents = new()
    {
        ["A32NX_AUTOPILOT_1_ACTIVE"] = "A32NX.FCU_AP_1_PUSH",
        ["A32NX_AUTOPILOT_2_ACTIVE"] = "A32NX.FCU_AP_2_PUSH",
        ["A32NX_AUTOTHRUST_STATUS"] = "AUTO_THROTTLE_ARM",
        ["A32NX_FCU_LOC_MODE_ACTIVE"] = "A32NX.FCU_LOC_PUSH",
        ["A32NX_FCU_APPR_MODE_ACTIVE"] = "A32NX.FCU_APPR_PUSH",
        ["A32NX_FMA_EXPEDITE_MODE"] = "A32NX.FCU_EXPED_PUSH",
    };
    private readonly Dictionary<string, double> _fcuStateCache = new();

    // Generic one-shot on-demand readout (speeds, fuel, approach capability):
    // request the var, then announce "<label> <value> <unit>" (or a mapped word)
    // when it arrives in ProcessSimVarUpdate.
    private string? _readoutKey, _readoutLabel, _readoutUnit;
    private Dictionary<double, string>? _readoutMap;
    private static readonly Dictionary<double, string> _apprCapMap = new Dictionary<double, string>
    { [0] = "None", [1] = "CAT 1", [2] = "CAT 2", [3] = "CAT 3 Single", [4] = "CAT 3 Dual" };

    private void RequestReadout(SimConnectManager s, string key, string label, string unit = "", Dictionary<double, string>? map = null)
    {
        if (!s.IsConnected) return;
        _readoutKey = key; _readoutLabel = label; _readoutUnit = unit; _readoutMap = map;
        s.RequestVariable(key, forceUpdate: true);
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUHeadingDialog(simConnect, announcer, parentForm);
            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUSpeedDialog(simConnect, announcer, parentForm);
            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUAltitudeDialog(simConnect, announcer, parentForm);
            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                return ShowFCUVSDialog(simConnect, announcer, parentForm);

            // FCU knob push/pull (Shift+1..4 push, Ctrl+1..4 pull). Fire the
            // A32NX.FCU_* event (same events the A320 uses), then read back the
            // managed/selected state so the user gets spoken confirmation — the
            // raw event is otherwise silent, which read as "doesn't work". Handled
            // here rather than via GetHotkeyVariableMap so we can add the readout.
            case HotkeyAction.FCUHeadingPush:
                simConnect.SendEvent("A32NX.FCU_TO_AP_HDG_PUSH"); RequestFCUHeadingWithStatus(simConnect); return true;
            case HotkeyAction.FCUHeadingPull:
                simConnect.SendEvent("A32NX.FCU_TO_AP_HDG_PULL"); RequestFCUHeadingWithStatus(simConnect); return true;
            case HotkeyAction.FCUSpeedPush:
                simConnect.SendEvent("A32NX.FCU_SPD_PUSH"); RequestFCUSpeedWithStatus(simConnect); return true;
            case HotkeyAction.FCUSpeedPull:
                simConnect.SendEvent("A32NX.FCU_SPD_PULL"); RequestFCUSpeedWithStatus(simConnect); return true;
            case HotkeyAction.FCUAltitudePush:
                simConnect.SendEvent("A32NX.FCU_ALT_PUSH"); RequestFCUAltitudeWithStatus(simConnect); return true;
            case HotkeyAction.FCUAltitudePull:
                simConnect.SendEvent("A32NX.FCU_ALT_PULL"); RequestFCUAltitudeWithStatus(simConnect); return true;
            case HotkeyAction.FCUVSPush:
                simConnect.SendEvent("A32NX.FCU_VS_PUSH"); RequestFCUVSWithStatus(simConnect); return true;
            case HotkeyAction.FCUVSPull:
                simConnect.SendEvent("A32NX.FCU_TO_AP_VS_PULL"); RequestFCUVSWithStatus(simConnect); return true;

            case HotkeyAction.ReadFlaps:
                if (simConnect.IsConnected) { _reqFlaps = true; simConnect.RequestVariable("A32NX_FLAPS_HANDLE_INDEX", forceUpdate: true); }
                return true;
            case HotkeyAction.ReadGear:
                if (simConnect.IsConnected) { _reqGear = true; simConnect.RequestVariable("A32NX_GEAR_HANDLE_POSITION", forceUpdate: true); }
                return true;
            // On-demand readouts ported from the A320 (vars shared / already defined).
            case HotkeyAction.ReadSpeedVLS: RequestReadout(simConnect, "A32NX_SPEEDS_VLS", "V L S", "knots"); return true;
            case HotkeyAction.ReadSpeedF: RequestReadout(simConnect, "A32NX_SPEEDS_F", "F speed", "knots"); return true;
            case HotkeyAction.ReadSpeedGD: RequestReadout(simConnect, "A32NX_SPEEDS_GD", "Green Dot speed", "knots"); return true;
            case HotkeyAction.ReadSpeedS: RequestReadout(simConnect, "A32NX_SPEEDS_S", "S speed", "knots"); return true;
            case HotkeyAction.ReadSpeedVS: RequestReadout(simConnect, "A32NX_SPEEDS_VS", "V S", "knots"); return true;
            case HotkeyAction.ReadSpeedVFE: RequestReadout(simConnect, "A32NX_SPEEDS_VFEN", "V F E next", "knots"); return true;
            case HotkeyAction.ReadFuelQuantity: RequestReadout(simConnect, "A32NX_TOTAL_FUEL_QUANTITY", "Total fuel", "kilograms"); return true;
            case HotkeyAction.ReadApproachCapability: RequestReadout(simConnect, "A32NX_APPROACH_CAPABILITY", "Approach capability", "", _apprCapMap); return true;
            case HotkeyAction.ShowPFD:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowPFDWindow(announcer, simConnect);
                return true;
            case HotkeyAction.ShowStatusPage:
            case HotkeyAction.ShowECAM:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowSystemDisplayWindow(announcer, simConnect);
                return true;
            case HotkeyAction.ShowNavigationDisplay:
            case HotkeyAction.ReadDisplayND:
                hotkeyManager.ExitOutputHotkeyMode();
                new Forms.FBWA380.FBWA380NavDisplayForm(announcer, simConnect).Show();
                return true;
            // The A320/PMDG "read display" hotkeys map onto the A380's readout
            // windows (same data, screen-reader friendly).
            case HotkeyAction.ReadDisplayPFD:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowPFDWindow(announcer, simConnect);
                return true;
            case HotkeyAction.ReadDisplayISIS:
                hotkeyManager.ExitOutputHotkeyMode();
                new Forms.FBWA380.FBWA380ISISForm(announcer, simConnect).Show();
                return true;
            case HotkeyAction.ReadDisplayUpperECAM:
                // Upper ECAM IS the E/WD: read ALL current memo/warning lines on
                // demand (decoded), regardless of the live-call-out mute. (Alt+E)
                ReadAllEwdWarnings(announcer);
                return true;
            case HotkeyAction.ReadDisplayLowerECAM:
            case HotkeyAction.ReadFuelInfo:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowSystemDisplayWindow(announcer, simConnect);
                return true;
            case HotkeyAction.ReadWaypointInfo:
                RequestWaypointInfo(simConnect);
                return true;
            case HotkeyAction.ReadAltimeter:
                if (simConnect.IsConnected) { _reqBaro = true; simConnect.RequestVariable("KOHLSMAN_HG", forceUpdate: true); }
                return true;
            case HotkeyAction.ReadGrossWeightKg:
                if (simConnect.IsConnected) { _reqGw = true; simConnect.RequestVariable("GROSS_WEIGHT_KG", forceUpdate: true); }
                return true;
            case HotkeyAction.ReadHeading: RequestFCUHeadingWithStatus(simConnect); return true;
            case HotkeyAction.ReadSpeed: RequestFCUSpeedWithStatus(simConnect); return true;
            case HotkeyAction.ReadAltitude: RequestFCUAltitudeWithStatus(simConnect); return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA: RequestFCUVSWithStatus(simConnect); return true;
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                if (parentForm is MainForm mf) mf.ShowA380MonitorManagerDialog();
                return true;
            // Toggle live ECAM E/WD memo/warning call-outs (A32NX-style). The A380
            // monitors A32NX_EWD_LOWER_* lines in ProcessSimVarUpdate and mutes them
            // when the FBWA380_ECAM_MEMOS sentinel is in A380DisabledMonitorVariables;
            // this flips it (also reflected in the Ctrl+M Monitor Manager) and persists.
            case HotkeyAction.ToggleECAMMonitoring:
            {
                var disabled = Settings.SettingsManager.Current.A380DisabledMonitorVariables;
                string key = Forms.FBWA380.FBWA380MonitorManagerForm.EcamMemosKey;
                bool turnedOff;
                if (disabled.Contains(key)) { disabled.Remove(key); turnedOff = false; }
                else { disabled.Add(key); turnedOff = true; }
                Settings.SettingsManager.Save();
                announcer.AnnounceImmediate(turnedOff ? "E W D monitoring disabled" : "E W D monitoring enabled");
                return true;
            }
            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    // PFD/FMA readout window — reuses the shared A32NX PFDForm, which reads the
    // current aircraft's PFD-panel vars by name (FMA modes, approach capability,
    // PFD messages, MDA, QNH — all shared A32NX_ names the A380 now defines).
    private void ShowPFDWindow(ScreenReaderAnnouncer announcer, SimConnectManager simConnect)
    {
        var dialog = new Forms.A32NX.PFDForm(announcer, simConnect) { CurrentAircraft = this };
        dialog.Show();
    }

    // Read ALL current upper-ECAM (E/WD) memo/warning lines on demand (Alt+E),
    // decoded via EWDMessageLookupA380. Reads the live cache of line codes
    // (_lastEwdCode, kept current by ProcessSimVarUpdate). Ignores the live
    // call-out mute — an explicit request should always speak.
    private void ReadAllEwdWarnings(ScreenReaderAnnouncer announcer)
    {
        var lines = new List<string>();
        foreach (var lr in new[] { "LEFT", "RIGHT" })
            for (int i = 1; i <= 10; i++)
            {
                if (_lastEwdCode.TryGetValue($"A32NX_EWD_LOWER_{lr}_LINE_{i}", out var code) && code != 0)
                {
                    string text = EWDMessageLookupA380.GetMessage(code);
                    if (!string.IsNullOrWhiteSpace(text) &&
                        !text.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                    {
                        string priority = EWDMessageLookupA380.GetMessagePriority(code);
                        lines.Add(string.IsNullOrEmpty(priority) ? text : $"{text} {priority}");
                    }
                }
            }
        announcer.Announce(lines.Count == 0
            ? "No ECAM warnings or memos."
            : $"ECAM E W D, {lines.Count} line{(lines.Count == 1 ? "" : "s")}: {string.Join(". ", lines)}");
    }

    // ECAM System Display (SD) window — decodes the FQMS/PRESS/APU ARINC429 words
    // + plain SD scalars (Fuel/Engine/Press/APU/Elec/Hyd/Cond). Opened from
    // ShowStatusPage and ShowECAM (the A380 also announces E/WD live).
    private void ShowSystemDisplayWindow(ScreenReaderAnnouncer announcer, SimConnectManager simConnect)
    {
        var dialog = new Forms.FBWA380.FBWA380SystemDisplayForm(announcer, simConnect);
        dialog.Show();
    }

    // TO waypoint readout (ident / distance / bearing). The A380X publishes the
    // same A32NX_EFIS_L_TO_WPT_* vars (verified live); the shared SimConnectManager
    // request-370 handler unpacks the ident and announces "WPT, X NM, Y degrees".
    private void RequestWaypointInfo(SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (!simConnectMgr.IsConnected || simConnect == null) return;
        try
        {
            var tempDefId = (SimConnectManager.DATA_DEFINITIONS)370;
            simConnect.ClearDataDefinition(tempDefId);
            simConnect.AddToDataDefinition(tempDefId, "L:A32NX_EFIS_L_TO_WPT_IDENT_0", "number",
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
            simConnect.AddToDataDefinition(tempDefId, "L:A32NX_EFIS_L_TO_WPT_IDENT_1", "number",
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
            simConnect.AddToDataDefinition(tempDefId, "L:A32NX_EFIS_L_TO_WPT_DISTANCE", "number",
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
            simConnect.AddToDataDefinition(tempDefId, "L:A32NX_EFIS_L_TO_WPT_BEARING", "radians",
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
            simConnect.RegisterDataDefineStruct<SimConnectManager.WaypointInfo>(tempDefId);
            simConnect.RequestDataOnSimObject((SimConnectManager.DATA_REQUESTS)370,
                tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error requesting A380 waypoint info: {ex.Message}");
        }
    }

    private bool ShowFCUHeadingDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, System.Windows.Forms.Form parentForm)
    {
        var validator = new Func<string, (bool, string)>(input =>
            double.TryParse(input, out double v)
                ? (v >= 0 && v <= 360 ? (true, "") : (false, "Heading must be between 0 and 360 degrees"))
                : (false, "Invalid number format"));
        return ShowFCUInputDialog("Set Heading", "Heading", "0-360 degrees",
            "A32NX.FCU_HDG_SET", simConnect, announcer, parentForm, validator);
    }

    private bool ShowFCUSpeedDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, System.Windows.Forms.Form parentForm)
    {
        var validator = new Func<string, (bool, string)>(input =>
            double.TryParse(input, out double v)
                ? (((v >= 0.10 && v <= 0.99) || (v >= 100 && v <= 399)) ? (true, "") : (false, "Speed must be 100-399 knots or 0.10-0.99 Mach"))
                : (false, "Invalid number format"));
        Func<double, uint> converter = v => v < 1.0 ? (uint)(v * 100) : (uint)v;
        return ShowFCUInputDialog("Set Speed", "Speed", "100-399 knots or 0.10-0.99 Mach",
            "A32NX.FCU_SPD_SET", simConnect, announcer, parentForm, validator, converter);
    }

    private bool ShowFCUAltitudeDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected) { announcer.AnnounceImmediate("Not connected to simulator."); return false; }
        var validator = new Func<string, (bool, string)>(input =>
            double.TryParse(input, out double v)
                ? (v >= 100 && v <= 49000 ? (true, "") : (false, "Altitude must be between 100 and 49000 feet"))
                : (false, "Invalid number format"));
        var dialog = new Forms.ValueInputForm("Set Altitude", "Altitude", "100-49000 feet", announcer, validator);
        if (dialog.ShowDialog(parentForm) == System.Windows.Forms.DialogResult.OK && dialog.IsValidInput
            && double.TryParse(dialog.InputValue, out double value))
        {
            uint rounded = (uint)(Math.Round(value / 100) * 100);
            simConnect.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", 100);
            System.Threading.Thread.Sleep(50);
            simConnect.SendEvent("A32NX.FCU_ALT_SET", rounded);
            announcer.AnnounceImmediate($"Altitude set to {rounded}");
            return true;
        }
        return false;
    }

    private bool ShowFCUVSDialog(SimConnectManager simConnect, ScreenReaderAnnouncer announcer, System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected) { announcer.AnnounceImmediate("Not connected to simulator."); return false; }
        var validator = new Func<string, (bool, string)>(input =>
            double.TryParse(input, out double v)
                ? (((v >= -6000 && v <= 6000) || (v >= -9.9 && v <= 9.9)) ? (true, "") : (false, "Value must be -6000 to 6000 ft/min or -9.9 to 9.9 degrees FPA"))
                : (false, "Invalid number format"));
        var dialog = new Forms.ValueInputForm("Set Vertical Speed / FPA", "VS/FPA",
            "-6000 to 6000 ft/min or -9.9 to 9.9 degrees FPA", announcer, validator);
        if (dialog.ShowDialog(parentForm) == System.Windows.Forms.DialogResult.OK && dialog.IsValidInput
            && double.TryParse(dialog.InputValue, out double value))
        {
            // V/S and FPA are SIGNED (e.g. -3000 ft/min, -3.5° FPA). SendEvent's
            // data is uint, so a negative value overflowed to a huge number and
            // the set did nothing. Fire the signed value via calculator code (the
            // dot-event path) instead. FPA is value*100 (deci-degrees), V/S is raw.
            int toSend = Math.Abs(value) < 100 ? (int)(value * 100) : (int)value;
            simConnect.ExecuteCalculatorCode($"{toSend} (>K:A32NX.FCU_VS_SET)");
            announcer.AnnounceImmediate($"Vertical speed set to {value}");
            return true;
        }
        return false;
    }

    private void RequestFCUHeadingWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqHdg = true; _pHdgVal = _pHdgMgd = null;
        s.RequestVariable("A32NX_AUTOPILOT_HEADING_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_HDG_MANAGED_DASHES", forceUpdate: true);
    }

    private void RequestFCUSpeedWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqSpd = true; _pSpdVal = _pSpdMgd = null;
        s.RequestVariable("A32NX_AUTOPILOT_SPEED_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_SPD_MANAGED_DOT", forceUpdate: true);
    }

    private void RequestFCUAltitudeWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqAlt = true; _pAltVal = _pAltMgd = null;
        s.RequestVariable("FCU_ALT_VALUE", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_ALT_MANAGED", forceUpdate: true);
    }

    private void RequestFCUVSWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqVs = true; _pVsVal = _pFpaVal = _pVsMode = null;
        // All three via the SimConnect data-def path, same as the heading/speed/alt
        // readouts that work. (VS is non-angular so it reads back unscaled — the
        // earlier "15 vs -2000" that prompted a MobiFlight read was a paused-sim
        // artifact, and routing VS through MobiFlight ReadLedVariable regressed the
        // read-out to silence: its numeric response was being consumed by the
        // shared ECAM string-read channel before the LED-value handler saw it.)
        s.RequestVariable("A32NX_TRK_FPA_MODE_ACTIVE", forceUpdate: true);
        s.RequestVariable("A32NX_AUTOPILOT_VS_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_AUTOPILOT_FPA_SELECTED", forceUpdate: true);
    }

    // Base FCU readout virtuals (rarely used path) → route to the paired readout.
    public override void RequestFCUHeading(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUHeadingWithStatus(simConnect);
    public override void RequestFCUSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUSpeedWithStatus(simConnect);
    public override void RequestFCUAltitude(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUAltitudeWithStatus(simConnect);
    public override void RequestFCUVerticalSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUVSWithStatus(simConnect);

    // Panel FCU knob push/pull buttons fire their A32NX.FCU_* event (which works),
    // but the generic panel-button path doesn't read anything back, so they were
    // silent. Speak the resulting selected/managed value here — identical to what
    // the Shift+1-4 (push) / Ctrl+1-4 (pull) hotkeys announce.
    public override void OnPanelButtonFired(string varKey, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "A32NX.FCU_TO_AP_HDG_PUSH":
            case "A32NX.FCU_TO_AP_HDG_PULL": RequestFCUHeadingWithStatus(simConnect); break;
            case "A32NX.FCU_SPD_PUSH":
            case "A32NX.FCU_SPD_PULL": RequestFCUSpeedWithStatus(simConnect); break;
            case "A32NX.FCU_ALT_PUSH":
            case "A32NX.FCU_ALT_PULL": RequestFCUAltitudeWithStatus(simConnect); break;
            case "A32NX.FCU_VS_PUSH":
            case "A32NX.FCU_TO_AP_VS_PULL": RequestFCUVSWithStatus(simConnect); break;
            // SPD/MACH toggle: re-read the speed — the read-out already says
            // "mach 0.78" vs "280 knots", so it announces the new mode.
            case "A32NX.FCU_SPD_MACH_TOGGLE_PUSH": RequestFCUSpeedWithStatus(simConnect); break;
            // VHF active/standby swap: announce the swap (the new active is then on
            // the "VHF N Active" read-out in the panel).
            case "COM1_RADIO_SWAP": announcer.Announce("VHF 1 active and standby swapped"); break;
            case "COM2_RADIO_SWAP": announcer.Announce("VHF 2 active and standby swapped"); break;
            case "COM3_RADIO_SWAP": announcer.Announce("VHF 3 active and standby swapped"); break;
        }
    }
}
