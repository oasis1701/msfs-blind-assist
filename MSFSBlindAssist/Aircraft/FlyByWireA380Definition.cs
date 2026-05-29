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
    public override string AircraftName => "FlyByWire A380X (Dev)";
    public override string AircraftCode => "FBW_A380";

    /// <summary>Bridge JS execution stage (legacy injection diagnostic). See
    /// FBWA380ModPackageManager; retained for the MCDU form's status surface.</summary>
    public int BridgeStage { get; private set; }

    /// <summary>Override-HTML loaded marker (legacy injection diagnostic).</summary>
    public int BridgeHtmlLoaded { get; private set; }

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
                UpdateFrequency = UpdateFrequency.OnRequest,
                ValueDescriptions = vd,
                RenderAsButton = button,
                ReverseDisplayOrder = reverse
            };
        }
        // Bool L:var: Off / On.
        void OnOff(string key, string display, bool button = false) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "Off", [1] = "On" }, button);
        // Bool L:var: Off / Auto (FBW "_IS_AUTO" pushbuttons).
        void OffAuto(string key, string display, bool button = false) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }, button);
        // Momentary press L:var rendered as a button.
        void Press(string key, string display) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "Off", [1] = "Pressed" }, button: true);
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
        // Read-only enum/status L:var readout.
        void ReadEnum(string key, string display, Dictionary<double, string> vd)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = display,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
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

        // Legacy injection diagnostics (continuous, silent — read by the form).
        vars["MSFSBA_FBWA380_STAGE"] = new SimVarDefinition
        {
            Name = "L:MSFSBA_FBWA380_STAGE", DisplayName = "A380 Bridge Stage",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = false
        };
        vars["MSFSBA_FBWA380_HTML_LOADED"] = new SimVarDefinition
        {
            Name = "L:MSFSBA_FBWA380_HTML_LOADED", DisplayName = "A380 Bridge HTML Loaded",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = false
        };

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
            new Dictionary<double, string> { [0] = "Off", [1] = "APU", [2] = "Off (2)", [3] = "Battery 1", [4] = "Battery 2" });
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
        Read("A32NX_OVHD_COND_CKPT_SELECTOR_KNOB", "Cockpit Temp Selector");
        Read("A32NX_OVHD_COND_CABIN_SELECTOR_KNOB", "Cabin Temp Selector");
        Read("A32NX_COND_CKPT_TEMP", "Cockpit Temperature", "celsius");
        for (int n = 1; n <= 8; n++) Read($"A32NX_COND_MAIN_DECK_{n}_TEMP", $"Main Deck {n} Temperature", "celsius");
        for (int n = 1; n <= 7; n++) Read($"A32NX_COND_UPPER_DECK_{n}_TEMP", $"Upper Deck {n} Temperature", "celsius");

        // ---- CARGO AIR ----
        Read("A32NX_OVHD_CARGO_AIR_FWD_SELECTOR_KNOB", "Cargo Fwd Temp Selector");
        Read("A32NX_OVHD_CARGO_AIR_BULK_SELECTOR_KNOB", "Cargo Bulk Temp Selector");
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
            Stock($"ENG_ANTI_ICE:{n}", $"ENG ANTI ICE:{n}", $"Engine {n} Anti-Ice", "bool", onOff);
            Press($"XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_ENG{n}_PRESSED", $"Engine {n} Anti-Ice Push");
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

        // ---- EXTERIOR LIGHTING (stock toggles + readbacks) ----
        Evt("TOGGLE_BEACON_LIGHTS", "TOGGLE_BEACON_LIGHTS", "Beacon Toggle");
        Evt("TOGGLE_NAV_LIGHTS", "TOGGLE_NAV_LIGHTS", "Navigation Toggle");
        Evt("STROBES_TOGGLE", "STROBES_TOGGLE", "Strobe Toggle");
        Evt("TOGGLE_WING_LIGHTS", "TOGGLE_WING_LIGHTS", "Wing Toggle");
        Evt("TOGGLE_LOGO_LIGHTS", "TOGGLE_LOGO_LIGHTS", "Logo Toggle");
        Evt("LANDING_LIGHTS_TOGGLE", "LANDING_LIGHTS_TOGGLE", "Landing Lights Toggle");
        Evt("TOGGLE_TAXI_LIGHTS", "TOGGLE_TAXI_LIGHTS", "Taxi Lights Toggle");
        Stock("LIGHT_BEACON", "LIGHT BEACON", "Beacon", "bool", onOff);
        Stock("LIGHT_NAV", "LIGHT NAV", "Navigation Lights", "bool", onOff);
        Stock("LIGHT_STROBE", "LIGHT STROBE", "Strobe", "bool", onOff);
        Stock("LIGHT_WING", "LIGHT WING", "Wing Lights", "bool", onOff);
        Stock("LIGHT_LOGO", "LIGHT LOGO", "Logo Lights", "bool", onOff);
        Stock("LIGHT_TAXI", "LIGHT TAXI:2", "Taxi Lights", "bool", onOff);
        Stock("LIGHT_LANDING", "LIGHT LANDING", "Landing Lights", "bool", onOff);

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

        // ---- EFIS Control Panel (per side) ----
        foreach (var side in new[] { "L", "R" })
        {
            string who = side == "L" ? "Capt" : "F/O";
            OnOff($"A380X_EFIS_{side}_LS_BUTTON_IS_ON", $"{who} LS");
            OnOff($"A380X_EFIS_{side}_VV_BUTTON_IS_ON", $"{who} V/V");
            OnOff($"A380X_EFIS_{side}_CSTR_BUTTON_IS_ON", $"{who} Constraints");
            OnOff($"A380X_EFIS_{side}_ARPT_BUTTON_IS_ON", $"{who} Airport");
            OnOff($"A380X_EFIS_{side}_TRAF_BUTTON_IS_ON", $"{who} Traffic");
            Sel($"A380X_EFIS_{side}_ND_MODE", $"{who} ND Mode",
                new Dictionary<double, string> { [0] = "Rose ILS", [1] = "Rose VOR", [2] = "Rose Nav", [3] = "Arc", [4] = "Plan" });
            Sel($"A380X_EFIS_{side}_ND_RANGE", $"{who} ND Range",
                new Dictionary<double, string> { [0] = "Zoom", [1] = "10", [2] = "20", [3] = "40", [4] = "80", [5] = "160", [6] = "320", [7] = "640" });
            for (int nav = 1; nav <= 2; nav++)
                Sel($"A380X_EFIS_{side}_NAVAID_{nav}_BUTTON_IS_ON", $"{who} Navaid {nav}",
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
                new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Starting", [3] = "Shutting Down", [4] = "Restarting" });

        // ---- Flaps / Speedbrake / Trim / Park brake ----
        ReadEnum("A32NX_FLAPS_HANDLE_INDEX", "Flap Lever",
            new Dictionary<double, string> { [0] = "Up", [1] = "1", [2] = "2", [3] = "3", [4] = "Full" });
        Read("A32NX_SPOILERS_HANDLE_POSITION", "Speed Brake Handle");
        ReadEnum("A32NX_SPOILERS_ARMED", "Ground Spoilers", new Dictionary<double, string> { [0] = "Disarmed", [1] = "Armed" });
        OnOff("A32NX_PARK_BRAKE_LEVER_POS", "Parking Brake");

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
            new Dictionary<double, string> { [0] = "Off", [1] = "Auto" });
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
        ReadEnum("A32NX_AUTOTHRUST_STATUS", "Autothrust Status",
            new Dictionary<double, string> { [0] = "Disengaged", [1] = "Armed", [2] = "Active" });
        Read("A32NX_FMS_PAX_NUMBER", "Passenger Number");
        ReadEnum("A32NX_ECAM_FAILURE_ACTIVE", "ECAM Failure Active", onOff);
        Mon("A32NX_FMGC_FLIGHT_PHASE", "Flight Phase",
            new Dictionary<double, string>
            {
                [0] = "Preflight", [1] = "Takeoff", [2] = "Climb", [3] = "Cruise", [4] = "Descent",
                [5] = "Approach", [6] = "Go Around", [7] = "Done"
            });

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

        // Readout value + managed-indicator vars (OnRequest; requested in pairs).
        Read("A32NX_AUTOPILOT_HEADING_SELECTED", "Selected Heading", "degrees");
        Read("A32NX_FCU_HDG_MANAGED_DASHES", "Heading Managed");
        Read("A32NX_AUTOPILOT_SPEED_SELECTED", "Selected Speed");
        Read("A32NX_FCU_SPD_MANAGED_DOT", "Speed Managed");
        Read("A32NX_AUTOPILOT_VS_SELECTED", "Selected Vertical Speed", "feet per minute");
        Read("A32NX_AUTOPILOT_FPA_SELECTED", "Selected FPA");
        Read("A32NX_FCU_VS_MANAGED", "Vertical Speed Managed");
        Read("A32NX_FCU_ALT_MANAGED", "Altitude Managed");
        ReadEnum("A32NX_TRK_FPA_MODE_ACTIVE", "Vertical Mode",
            new Dictionary<double, string> { [0] = "HDG V/S", [1] = "TRK FPA" });
        // SimVars (key != Name — ProcessSimVarUpdate matches on the key).
        Stock("FCU_ALT_VALUE", "AUTOPILOT ALTITUDE LOCK VAR:3", "Selected Altitude", "feet");
        Stock("FCU_MACH_MODE", "AUTOPILOT MANAGED SPEED IN MACH", "Mach Mode", "bool", onOff);

        // Engagement / mode readouts (A380 has no FCU light vars).
        ReadEnum("A32NX_AUTOPILOT_1_ACTIVE", "Autopilot 1", onOff);
        ReadEnum("A32NX_AUTOPILOT_2_ACTIVE", "Autopilot 2", onOff);
        ReadEnum("A32NX_FCU_LOC_MODE_ACTIVE", "Localizer Mode", onOff);
        ReadEnum("A32NX_FCU_APPR_MODE_ACTIVE", "Approach Mode", onOff);
        ReadEnum("A32NX_FMA_EXPEDITE_MODE", "Expedite Mode", onOff);

        // ---- EFIS Control Panel: flight director + baro (per side) ----
        Evt("TOGGLE_FLIGHT_DIRECTOR", "TOGGLE_FLIGHT_DIRECTOR", "Flight Director Toggle");
        Stock("FD_ACTIVE", "AUTOPILOT FLIGHT DIRECTOR ACTIVE", "Flight Director", "bool", onOff);
        var baroMode = new Dictionary<double, string> { [0] = "QFE", [1] = "QNH", [2] = "Standard" };
        Sel("XMLVAR_Baro1_Mode", "Capt Baro Mode", baroMode);
        Sel("XMLVAR_Baro2_Mode", "F/O Baro Mode", baroMode);
        Read("A32NX_FCU_LEFT_EIS_BARO_HPA", "Capt Baro (hPa)", "hectopascals");
        Read("A32NX_FCU_RIGHT_EIS_BARO_HPA", "F/O Baro (hPa)", "hectopascals");
        Read("A380X_EFIS_L_BARO_PRESELECTED", "Capt Preselected QNH");
        Read("A380X_EFIS_R_BARO_PRESELECTED", "F/O Preselected QNH");

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
                "Calls", "Signs", "ADIRS", "Flight Control Computers", "Engine Start",
                "Recorder and Misc", "Interior Lighting", "Exterior Lighting"
            },
            ["Glareshield"] = new List<string> { "FCU", "EFIS Control Panel", "Warnings", "OIT" },
            ["Instrument"] = new List<string> { "Gear", "Autobrake", "Source Switching" },
            ["Pedestal"] = new List<string>
            {
                "Engines", "Flaps and Brakes", "ECAM Control Panel", "Weather Radar",
                "Transponder", "RMP", "Cockpit Door"
            },
            ["Displays"] = new List<string> { "Status" }
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
            "A32NX_OVHD_COND_CKPT_SELECTOR_KNOB", "A32NX_OVHD_COND_CABIN_SELECTOR_KNOB"
        };
        p["Pressurization"] = new List<string>
        {
            "A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO", "A32NX_OVHD_PRESS_MAN_VS_CTL_PB_IS_AUTO",
            "A32NX_OVHD_PRESS_DITCHING_PB_IS_ON"
        };
        p["Ventilation"] = new List<string>
        {
            "A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON", "A32NX_OVHD_VENT_AIR_EXTRACT_PB_IS_ON"
        };
        p["Cargo Air"] = new List<string>
        {
            "A32NX_OVHD_CARGO_AIR_FWD_SELECTOR_KNOB", "A32NX_OVHD_CARGO_AIR_BULK_SELECTOR_KNOB",
            "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_FWD_PB_IS_ON", "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_BULK_PB_IS_ON",
            "A32NX_OVHD_CARGO_AIR_HEATER_PB_IS_ON"
        };
        p["Anti Ice"] = new List<string>
        {
            "A32NX_MAN_PITOT_HEAT", "XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_WING_PRESSED",
            "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4"
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
            "TOGGLE_BEACON_LIGHTS", "TOGGLE_NAV_LIGHTS", "STROBES_TOGGLE", "TOGGLE_WING_LIGHTS",
            "TOGGLE_LOGO_LIGHTS", "LANDING_LIGHTS_TOGGLE", "TOGGLE_TAXI_LIGHTS"
        };

        p["Warnings"] = new List<string>
        {
            "PUSH_AUTOPILOT_MASTERWARN_L", "PUSH_AUTOPILOT_MASTERWARN_R",
            "PUSH_AUTOPILOT_MASTERCAUT_L", "PUSH_AUTOPILOT_MASTERCAUT_R"
        };
        p["EFIS Control Panel"] = new List<string>
        {
            "A380X_EFIS_L_LS_BUTTON_IS_ON", "A380X_EFIS_L_VV_BUTTON_IS_ON", "A380X_EFIS_L_CSTR_BUTTON_IS_ON",
            "A380X_EFIS_L_ARPT_BUTTON_IS_ON", "A380X_EFIS_L_TRAF_BUTTON_IS_ON",
            "A380X_EFIS_L_ND_MODE", "A380X_EFIS_L_ND_RANGE",
            "A380X_EFIS_L_NAVAID_1_BUTTON_IS_ON", "A380X_EFIS_L_NAVAID_2_BUTTON_IS_ON",
            "A380X_EFIS_R_LS_BUTTON_IS_ON", "A380X_EFIS_R_VV_BUTTON_IS_ON", "A380X_EFIS_R_CSTR_BUTTON_IS_ON",
            "A380X_EFIS_R_ARPT_BUTTON_IS_ON", "A380X_EFIS_R_TRAF_BUTTON_IS_ON",
            "A380X_EFIS_R_ND_MODE", "A380X_EFIS_R_ND_RANGE",
            "A380X_EFIS_R_NAVAID_1_BUTTON_IS_ON", "A380X_EFIS_R_NAVAID_2_BUTTON_IS_ON",
            "TOGGLE_FLIGHT_DIRECTOR", "XMLVAR_Baro1_Mode", "XMLVAR_Baro2_Mode"
        };
        p["FCU"] = new List<string>
        {
            "A32NX.FCU_AP_1_PUSH", "A32NX.FCU_AP_2_PUSH", "A32NX.FCU_ATHR_PUSH",
            "A32NX.FCU_TO_AP_HDG_PUSH", "A32NX.FCU_TO_AP_HDG_PULL",
            "A32NX.FCU_SPD_PUSH", "A32NX.FCU_SPD_PULL",
            "A32NX.FCU_ALT_PUSH", "A32NX.FCU_ALT_PULL", "XMLVAR_AUTOPILOT_ALTITUDE_INCREMENT",
            "A32NX.FCU_VS_PUSH", "A32NX.FCU_TO_AP_VS_PULL",
            "A32NX.FCU_LOC_PUSH", "A32NX.FCU_APPR_PUSH", "A32NX.FCU_EXPED_PUSH",
            "A32NX.FCU_SPD_MACH_TOGGLE_PUSH", "A32NX.FCU_TRK_FPA_TOGGLE_PUSH",
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
            "A32NX_ENGMANSTART1_TOGGLE", "A32NX_ENGMANSTART2_TOGGLE",
            "A32NX_ENGMANSTART3_TOGGLE", "A32NX_ENGMANSTART4_TOGGLE"
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
        p["Transponder"] = new List<string> { "XPNDR_IDENT_ON" };
        p["RMP"] = new List<string>
        {
            "A380X_RMP_1_STATE", "A380X_RMP_2_STATE", "A380X_RMP_3_STATE"
        };
        p["Cockpit Door"] = new List<string> { "A32NX_COCKPIT_DOOR_LOCKED", "A32NX_CABIN_READY" };

        p["Status"] = new List<string>
        {
            "A32NX_AUTOTHRUST_STATUS", "A32NX_FMS_PAX_NUMBER", "A32NX_ECAM_FAILURE_ACTIVE"
        };

        return p;
    }

    // ===================================================================
    // Read-only display variables per panel (auto-refreshing readouts)
    // ===================================================================
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        return new Dictionary<string, List<string>>
        {
            ["ELEC"] = new List<string>
            {
                "A32NX_ELEC_BAT_1_POTENTIAL", "A32NX_ELEC_BAT_2_POTENTIAL",
                "A32NX_ELEC_BAT_3_POTENTIAL", "A32NX_ELEC_BAT_4_POTENTIAL"
            },
            ["APU"] = new List<string> { "A32NX_OVHD_APU_START_PB_IS_AVAILABLE" },
            ["Fuel"] = new List<string> { "A32NX_TOTAL_FUEL_QUANTITY" },
            ["Air Conditioning"] = new List<string>
            {
                "A32NX_COND_CKPT_TEMP",
                "A32NX_COND_MAIN_DECK_1_TEMP", "A32NX_COND_MAIN_DECK_2_TEMP", "A32NX_COND_MAIN_DECK_3_TEMP",
                "A32NX_COND_MAIN_DECK_4_TEMP", "A32NX_COND_MAIN_DECK_5_TEMP", "A32NX_COND_MAIN_DECK_6_TEMP",
                "A32NX_COND_MAIN_DECK_7_TEMP", "A32NX_COND_MAIN_DECK_8_TEMP",
                "A32NX_COND_UPPER_DECK_1_TEMP", "A32NX_COND_UPPER_DECK_2_TEMP", "A32NX_COND_UPPER_DECK_3_TEMP",
                "A32NX_COND_UPPER_DECK_4_TEMP", "A32NX_COND_UPPER_DECK_5_TEMP", "A32NX_COND_UPPER_DECK_6_TEMP",
                "A32NX_COND_UPPER_DECK_7_TEMP"
            },
            ["Anti Ice"] = new List<string> { "A32NX_ICING_STATE_ICING_STICK_INDICATOR" },
            ["ADIRS"] = new List<string> { "A32NX_ADIRS_REMAINING_IR_ALIGNMENT_TIME" },
            ["Warnings"] = new List<string>
            {
                "A32NX_MASTER_WARNING", "A32NX_MASTER_CAUTION", "A32NX_AUTOPILOT_AUTOLAND_WARNING"
            },
            ["Autobrake"] = new List<string>
            {
                "A32NX_AUTOBRAKES_ARMED_MODE", "A32NX_BTV_STATE",
                "A32NX_BRAKE_TEMPERATURE_1", "A32NX_BRAKE_TEMPERATURE_2", "A32NX_BRAKE_TEMPERATURE_3",
                "A32NX_BRAKE_TEMPERATURE_4", "A32NX_BRAKE_TEMPERATURE_5", "A32NX_BRAKE_TEMPERATURE_6",
                "A32NX_BRAKE_TEMPERATURE_7", "A32NX_BRAKE_TEMPERATURE_8", "A32NX_BRAKE_TEMPERATURE_9",
                "A32NX_BRAKE_TEMPERATURE_10", "A32NX_BRAKE_TEMPERATURE_11", "A32NX_BRAKE_TEMPERATURE_12",
                "A32NX_BRAKE_TEMPERATURE_13", "A32NX_BRAKE_TEMPERATURE_14", "A32NX_BRAKE_TEMPERATURE_15",
                "A32NX_BRAKE_TEMPERATURE_16"
            },
            ["Engines"] = new List<string>
            {
                "A32NX_ENGINE_STATE:1", "A32NX_ENGINE_STATE:2", "A32NX_ENGINE_STATE:3", "A32NX_ENGINE_STATE:4"
            },
            ["Flaps and Brakes"] = new List<string>
            {
                "A32NX_FLAPS_HANDLE_INDEX", "A32NX_SPOILERS_HANDLE_POSITION", "A32NX_SPOILERS_ARMED"
            },
            ["Exterior Lighting"] = new List<string>
            {
                "LIGHT_BEACON", "LIGHT_NAV", "LIGHT_STROBE", "LIGHT_WING", "LIGHT_LOGO",
                "LIGHT_TAXI", "LIGHT_LANDING"
            },
            ["RMP"] = new List<string>
            {
                "FBW_RMP_FREQUENCY_ACTIVE_1", "FBW_RMP_FREQUENCY_STANDBY_1",
                "FBW_RMP_FREQUENCY_ACTIVE_2", "FBW_RMP_FREQUENCY_STANDBY_2",
                "FBW_RMP_FREQUENCY_ACTIVE_3", "FBW_RMP_FREQUENCY_STANDBY_3"
            },
            ["Status"] = new List<string> { "A32NX_FMGC_FLIGHT_PHASE" },
            ["FCU"] = new List<string>
            {
                "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE", "A32NX_AUTOTHRUST_STATUS",
                "A32NX_FCU_LOC_MODE_ACTIVE", "A32NX_FCU_APPR_MODE_ACTIVE", "A32NX_FMA_EXPEDITE_MODE",
                "A32NX_TRK_FPA_MODE_ACTIVE", "FD_ACTIVE"
            },
            ["EFIS Control Panel"] = new List<string>
            {
                "A32NX_FCU_LEFT_EIS_BARO_HPA", "A380X_EFIS_L_BARO_PRESELECTED",
                "A32NX_FCU_RIGHT_EIS_BARO_HPA", "A380X_EFIS_R_BARO_PRESELECTED"
            }
        };
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
            ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = "A32NX_TRK_FPA_MODE_ACTIVE",
            ["TOGGLE_FLIGHT_DIRECTOR"] = "FD_ACTIVE"
        };
    }

    // ===================================================================
    // Update hook (bridge diagnostics)
    // ===================================================================
    // Last announced E/WD code per line, so a line only speaks when it changes.
    private readonly Dictionary<string, long> _lastEwdCode = new();

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (varName == "MSFSBA_FBWA380_STAGE") { BridgeStage = (int)value; return true; }
        if (varName == "MSFSBA_FBWA380_HTML_LOADED") { BridgeHtmlLoaded = (int)value; return true; }

        // ECAM upper (E/WD) memo/warning lines: decode the numeric code to text
        // and announce it (with its FWC colour as a priority word). Returning
        // true suppresses the generic raw-number announcement.
        if (varName.StartsWith("A32NX_EWD_LOWER_"))
        {
            long code = (long)value;
            if (!_lastEwdCode.TryGetValue(varName, out var prev) || prev != code)
            {
                _lastEwdCode[varName] = code;
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

        // ---- FCU readouts: value + managed-indicator pairs ----
        // Each Read* hotkey requests the value var(s) and the managed indicator
        // and force-updates them; we announce once the pair (or, for VS, the
        // mode + matching value) has arrived. The _req* guards ensure these are
        // only intercepted during an active readout — otherwise they fall
        // through so the FCU panel's display readouts work normally.
        if (_reqHdg && (varName == "A32NX_AUTOPILOT_HEADING_SELECTED" || varName == "A32NX_FCU_HDG_MANAGED_DASHES"))
        {
            if (varName.EndsWith("HEADING_SELECTED")) _pHdgVal = value; else _pHdgMgd = value;
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
                string st = _pSpdMgd.Value > 0 ? "managed" : "selected";
                // The A380 SPEED_SELECTED carries mach (< 1) when in mach mode.
                string spoken = _pSpdVal.Value < 10
                    ? $"FCU speed mach {_pSpdVal.Value:0.00}, {st}"
                    : $"FCU speed {_pSpdVal.Value:000} knots, {st}";
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
            if (varName.EndsWith("VS_SELECTED")) _pVsVal = value;
            else if (varName.EndsWith("FPA_SELECTED")) _pFpaVal = value;
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

    // ===================================================================
    // FCU — ported from the A320 integration (same SET events; A380 readout
    // vars). Set dialogs send A32NX.FCU_*_SET; reads request value + managed
    // and announce via the pairing in ProcessSimVarUpdate.
    // ===================================================================
    private double? _pHdgVal, _pHdgMgd, _pSpdVal, _pSpdMgd, _pAltVal, _pAltMgd, _pVsVal, _pFpaVal, _pVsMode;
    private bool _reqHdg, _reqSpd, _reqAlt, _reqVs;

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
            case HotkeyAction.ReadHeading: RequestFCUHeadingWithStatus(simConnect); return true;
            case HotkeyAction.ReadSpeed: RequestFCUSpeedWithStatus(simConnect); return true;
            case HotkeyAction.ReadAltitude: RequestFCUAltitudeWithStatus(simConnect); return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA: RequestFCUVSWithStatus(simConnect); return true;
            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
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
            uint toSend = Math.Abs(value) < 100 ? (uint)(value * 100) : (uint)value;
            simConnect.SendEvent("A32NX.FCU_VS_SET", toSend);
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
        s.RequestVariable("A32NX_TRK_FPA_MODE_ACTIVE", forceUpdate: true);
        s.RequestVariable("A32NX_AUTOPILOT_VS_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_AUTOPILOT_FPA_SELECTED", forceUpdate: true);
    }

    // Base FCU readout virtuals (rarely used path) → route to the paired readout.
    public override void RequestFCUHeading(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUHeadingWithStatus(simConnect);
    public override void RequestFCUSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUSpeedWithStatus(simConnect);
    public override void RequestFCUAltitude(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUAltitudeWithStatus(simConnect);
    public override void RequestFCUVerticalSpeed(SimConnectManager simConnect, ScreenReaderAnnouncer announcer) => RequestFCUVSWithStatus(simConnect);
}
