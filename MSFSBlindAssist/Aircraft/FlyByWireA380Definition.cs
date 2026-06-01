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
        // Bool L:var: On / Auto (FBW "_ON_PB_IS_AUTO" pushbuttons — 0 = manually ON,
        // 1 = AUTO; the OFF-button variant uses OffAuto). Labelling the ON pushbutton
        // "Off/Auto" was wrong: selecting "Off" actually forced the pump ON.
        void OnAuto(string key, string display) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "On", [1] = "Auto" });
        // True momentary push-BUTTON on an L:var (renders as a Button, not a combo).
        // A press pulses the L:var 1→0 in HandleUIVariableSet so the sim sees the
        // edge — for TEST buttons, transponder ident, ATC message ack, rudder-trim
        // reset, tiller disconnect, rain repellent: actions with no meaningful
        // resting state. (The old `button:true` flag on OnOff/Sel was a no-op, so
        // these rendered as Released/Pressed combos.)
        void Btn(string key, string display)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key, DisplayName = display, Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest,
                // Rendered as a COMBO (Off / Activate), NOT a hardware button — every
                // panel control in MSFSBA is a combo box (user request). Selecting
                // "Activate" pulses the L:var 1→0 in HandleUIVariableSet (the
                // _momentaryButtons path); the momentary pulse returns it to "Off".
                // Not auto-announced — the handler speaks "<name> pressed" on activate.
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" },
                RenderAsButton = false
            };
            _momentaryButtons.Add(key);
        }
        // Latching/momentary PB L:var as a combo (Released / Pressed).
        void Press(string key, string display) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "Released", [1] = "Pressed" });
        // Momentary ECP push-button: settable + readable on demand, but NEVER
        // auto-announced. MSFSBA pulses these itself to drive the live ECL (the
        // checklist window) and the SD pages, so a transient "Pressed"/"Released"
        // call-out on every keystroke is pure noise — the meaningful result (the SD
        // page, the checklist line) is announced by its own var / the ECL scrape.
        void PressSilent(string key, string display)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key, DisplayName = display, Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Released", [1] = "Pressed" },
                RenderAsButton = false
            };
        }
        // Action / toggle combo — renders as a combo but has NO real backing var; its
        // set is fully handled in HandleUIVariableSet (fires a K-event). OnRequest so
        // the framework never spam-monitors a non-existent L:var. Used to standardise
        // momentary actions (transponder ident, radio swap, jetway/stairs toggle) into
        // combo boxes instead of buttons — the user picks the action option to fire it.
        void Act(string key, string display, Dictionary<double, string> vd)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key, DisplayName = display, Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest, ValueDescriptions = vd, RenderAsButton = false
            };
        }
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
        var discSw = new Dictionary<double, string> { [0] = "Disconnected", [1] = "Normal" };

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
            // ENG GEN 1-4 pushbutton. The L:var A32NX_OVHD_ELEC_ENG_GEN_n_PB_IS_ON is a
            // DEAD mirror on the shipping build — live-verified it stays 0 whether the
            // gen is producing 115 V or 0 V, so backing the combo on it showed "Off"
            // for a running gen and made the toggle-to-target write unreliable. Back
            // the combo on the STOCK simvar GENERAL ENG MASTER ALTERNATOR:n (which
            // correctly reads 1 on / 0 off) and control it with the stock
            // TOGGLE_MASTER_ALTERNATOR event (engine index) via HandleUIVariableSet —
            // same pattern as the ENG MASTER valves.
            vars[$"ELEC_ENG_GEN:{n}"] = new SimVarDefinition
            {
                Name = $"GENERAL ENG MASTER ALTERNATOR:{n}", DisplayName = $"Generator {n}",
                Type = SimVarType.SimVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            };
        }
        // APU GEN 1-2 pushbuttons. The L:var _APU_GEN_i_PB_IS_ON is likewise a dead
        // mirror (reads 0 with APU GENERATOR SWITCH:i = 1), so back the combo on the
        // STOCK simvar APU GENERATOR SWITCH:i and control it with the stock indexed
        // APU_GENERATOR_SWITCH_SET event via HandleUIVariableSet.
        for (int i = 1; i <= 2; i++)
            vars[$"ELEC_APU_GEN:{i}"] = new SimVarDefinition
            {
                Name = $"APU GENERATOR SWITCH:{i}", DisplayName = $"APU Generator {i}",
                Type = SimVarType.SimVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            };
        OnOff("A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON", "Emergency Generator 1 Line");
        Press("A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED", "RAT and Emergency Generator");
        Sel("A380X_OVHD_ELEC_BAT_SELECTOR_KNOB", "Battery Display Selector",
            new Dictionary<double, string> { [0] = "ESS", [1] = "APU", [2] = "Off", [3] = "Battery 1", [4] = "Battery 2" });
        for (int n = 1; n <= 4; n++) Read($"A32NX_ELEC_BAT_{n}_POTENTIAL", $"Battery {n} Voltage", "volts");

        // ---- APU ----
        OnOff("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "APU Master Switch");
        OnOff("A32NX_OVHD_APU_START_PB_IS_ON", "APU Start", button: true);
        // APU Available auto-announces ("APU Available: Available") AND shows in the
        // APU status readout. It overlaps the E/WD "APU AVAIL" memo — that twin
        // call-out is intentional (the user prefers both, the memo being a legit
        // separate ECAM source).
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
        // Each green/yellow electric hydraulic pump (A, B) has SEPARATE ON and OFF
        // pushbuttons on the A380 HYD overhead (faithful to the real cockpit's two
        // physical buttons per pump). The ON button is Auto/On, the OFF button is
        // Auto/Off. NOTE: these per-pump A/B buttons are managed by the FBW pump
        // controller — on the ground with the EDPs pressurising the system, a forced
        // ON/OFF is returned to AUTO by the system (correct behaviour; the
        // auto-announce re-reads the true state). The ENG-driven pumps and the two
        // main electric pumps (below) hold a manual command and are fully settable.
        foreach (var sys in new[] { "G", "Y" })
            foreach (var ab in new[] { "A", "B" })
            {
                string col = sys == "G" ? "Green" : "Yellow";
                OnAuto($"A32NX_OVHD_HYD_EPUMP{sys}{ab}_ON_PB_IS_AUTO", $"{col} Electric Pump {ab} On");
                OffAuto($"A32NX_OVHD_HYD_EPUMP{sys}{ab}_OFF_PB_IS_AUTO", $"{col} Electric Pump {ab} Off");
            }
        // The two MAIN electric hydraulic pumps on the HYD overhead (green "elec
        // hyd pump" = EPUMPB, and the yellow elec pump = EPUMPY) — settable via the
        // calculator path like the other OVHD PBs (#104 catalog diff: these were
        // missing entirely; the cockpit clicks PUSH_OVHD_HYD_ELECPUMP/ELECPUMPY).
        OffAuto("A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO", "Electric Hydraulic Pump");
        OffAuto("A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO", "Yellow Electric Hydraulic Pump");
        // Engine 1-4 hydraulic pump DISCONNECT pushbuttons (guarded). 1 = normal,
        // 0 = disconnected (an in-flight-irreversible action on the real jet; the
        // sim lets you reset it). #104: were missing entirely.
        for (int n = 1; n <= 4; n++)
            Sel($"A32NX_OVHD_HYD_ENG_{n}AB_PUMP_DISC_PB_IS_AUTO", $"Engine {n} Pumps Disconnect", discSw);
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
        // Wing anti-ice: the cockpit button is momentary, but its underlying
        // selected-state L:var is a plain writable toggle (verified live #56 — the
        // momentary XMLVAR_..._PRESSED pulse did NOT actuate; writing _SYSTEM_SELECTED
        // does and sticks). SYSTEM_ON (the actual valve-open output) is read-only and
        // shown separately as "Wing Anti-Ice Flowing".
        OnOff("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED", "Wing Anti-Ice");
        for (int n = 1; n <= 4; n++)
        {
            // Settable On/Off combo — fires K:ANTI_ICE_SET_ENGn via
            // HandleUIVariableSet. Verified live: the stock "ENG ANTI ICE:n"
            // SimVar AND the XMLVAR momentary push both fail to drive the A380
            // engine anti-ice; only the SET event toggles it.
            // Act() = action combo with NO backing var, so we don't monitor a dead
            // synthetic L-var (ENG{n}_ANTI_ICE doesn't exist → it would read a stale
            // 0). The set fires ANTI_ICE_SET_ENGn in HandleUIVariableSet; the live
            // state is the separate stock readout below.
            Act($"ENG{n}_ANTI_ICE", $"Engine {n} Anti-Ice", onOff);
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
        // Fire Test is a HOLD button (FBW <HOLD_SIMVAR>): the test runs only WHILE the
        // var is held at 1 (fire warnings + ECAM/EWD output appear), so a 250 ms
        // momentary pulse was too brief to be useful. Render as On/Off so the user
        // sets it On (test runs, the EWD speaks the fire-test result), then Off.
        OnOff("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", "Fire Test");

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
        // Seat-belt sign: the REAL state is the stock simvar CABIN SEATBELTS ALERT
        // SWITCH (On/Off). The XMLVAR switch position is model-recomputed, so a direct
        // L-var write to it just reverts (verified live) — that's why the old combo did
        // nothing. Settable; the set fires CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE in
        // HandleUIVariableSet when the desired state differs from current.
        vars["SEATBELT_SIGN"] = new SimVarDefinition
        {
            Name = "CABIN SEATBELTS ALERT SWITCH", DisplayName = "Seat Belts",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };
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
        // ADIRS ON BAT light: illuminates when the ADIRUs run on battery power (a
        // normal ~few-second self-test flash early in alignment, or abnormally on
        // electrical loss). Read-only annunciator, announce-on-change.
        Mon("A32NX_OVHD_ADIRS_ON_BAT_IS_ILLUMINATED", "ADIRS On Battery",
            new Dictionary<double, string> { [0] = "Off", [1] = "On Battery" });

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
        // Niche overhead/misc toggles (settable L:vars via the calculator catch-all;
        // low day-to-day value but exposed for completeness — #104/user request).
        OnOff("A32NX_ACMS_TRIGGER_ON", "ACMS Manual Trigger");
        OnOff("A32NX_CREW_HEAD_SET", "Crew Headset");
        OnOff("A32NX_SVGEINT_OVRD_ON", "Service Interphone Override");
        OnOff("A32NX_ENGMANSTARTALTN_TOGGLE", "Engine Manual Start, Alternate");
        Sel("A32NX_ENTERTAINMENT_CWS_OFF", "Cabin Crew Entertainment", new Dictionary<double, string> { [0] = "Normal", [1] = "Off" });
        Sel("A32NX_ENTERTAINMENT_IFEC_OFF", "Passenger Entertainment (IFE)", new Dictionary<double, string> { [0] = "Normal", [1] = "Off" });
        OnOff("A380X_REMOTE_CB_CTRL", "Remote Circuit Breaker Control");
        // Chronometer start/stop + reset (the glareshield CHRONO push). #107 gap.
        // Momentary actions driven by H-EVENTS (the FBW Clock subscribes to the
        // hEvent, NOT the L:var — writing the L:var does nothing). Rendered as COMBOS
        // (Idle / Activate, like every other control); HandleUIVariableSet fires
        // (>H:VAR) when "Activate" is chosen.
        vars["A32NX_CHRONO_TOGGLE"] = new SimVarDefinition
        { Name = "A32NX_CHRONO_TOGGLE", DisplayName = "Chronometer Start / Stop", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }, RenderAsButton = false };
        vars["A32NX_CHRONO_RST"] = new SimVarDefinition
        { Name = "A32NX_CHRONO_RST", DisplayName = "Chronometer Reset", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }, RenderAsButton = false };

        // ---- AUDIO CONTROL PANEL (ACP) — receive selectors, RMP 1 ----
        // Which sources the captain hears (#107 transcript: "ensure VHF1 + cabin
        // interphone selected"). Settable L:vars via the calculator catch-all.
        OnOff("A380X_RMP_1_VHF_VOL_RX_SWITCH_1", "VHF 1 Receive");
        OnOff("A380X_RMP_1_VHF_VOL_RX_SWITCH_2", "VHF 2 Receive");
        OnOff("A380X_RMP_1_CAB_VOL_RX_SWITCH", "Cabin Interphone Receive");
        OnOff("A380X_RMP_1_INT_VOL_RX_SWITCH", "Interphone Receive");
        OnOff("A380X_RMP_1_PA_VOL_RX_SWITCH", "PA Receive");
        OnOff("A380X_RMP_1_NAV_VOL_RX_SWITCH", "Navaid Receive");
        // Transmit (which radio the PTT keys) — the captain's TX select. (live-verified the L:var exists.)
        OnOff("A380X_RMP_1_VHF_TX_1", "VHF 1 Transmit");

        // ---- AUDIO CONTROL PANEL — First Officer (RMP 2), captain/F-O split ----
        // The RMP is identical hardware per seat; all RMP-2 switches live-verified to
        // exist. Same receive selectors + transmit as the captain side.
        OnOff("A380X_RMP_2_VHF_VOL_RX_SWITCH_1", "VHF 1 Receive");
        OnOff("A380X_RMP_2_VHF_VOL_RX_SWITCH_2", "VHF 2 Receive");
        OnOff("A380X_RMP_2_CAB_VOL_RX_SWITCH", "Cabin Interphone Receive");
        OnOff("A380X_RMP_2_INT_VOL_RX_SWITCH", "Interphone Receive");
        OnOff("A380X_RMP_2_PA_VOL_RX_SWITCH", "PA Receive");
        OnOff("A380X_RMP_2_NAV_VOL_RX_SWITCH", "Navaid Receive");
        OnOff("A380X_RMP_2_VHF_TX_1", "VHF 1 Transmit");

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
        // Strobe: On / Off via the stock STROBES_SET event (state = LIGHT STROBE).
        // The A380's real switch is 3-position (Off/Auto/On) on the FBW L:var
        // LIGHTING_STROBE_0, but that L:var is OUTPUT-ONLY on the shipping model —
        // writing it does NOT drive the strobe (live-verified: writing 0/1/2 leaves
        // LIGHT STROBE:1 = 0). The installed A380X uses Asobo stock-simvar lighting,
        // so the WORKING path is the stock SET event (live-verified: STROBES_SET 1 →
        // LIGHT STROBE:1 = 1). AUTO isn't reachable via any writable var, so this is
        // a functional On/Off — better than a dead 3-position combo.
        Light("LIGHT_STROBE", "LIGHT STROBE", "Strobe");
        Light("LIGHT_WING", "LIGHT WING", "Wing Lights");
        Light("LIGHT_LOGO", "LIGHT LOGO", "Logo Lights");
        Light("LIGHT_LANDING", "LIGHT LANDING", "Landing Lights");
        // Taxi light: On / Off. The real nose switch is 3-position (T.O/Taxi/Off) on
        // the FBW L:var A380X_OVHD_EXTLT_NOSE, but that L:var is DEAD on the shipping
        // model — writing it drives nothing (live-verified: writing 0/1/2 leaves
        // LIGHT TAXI/LANDING unchanged; the var isn't read anywhere in the FBW
        // source). The takeoff position lights the nose LANDING light, already
        // covered by "Landing Lights". The taxi function is the stock taxi light
        // (LIGHT TAXI:2), toggled by the stock TOGGLE_TAXI_LIGHTS event
        // (live-verified). State-mirrored combo: selecting the other option toggles.
        vars["LIGHT_TAXI_OVHD"] = new SimVarDefinition
        {
            Name = "LIGHT TAXI:2",
            DisplayName = "Taxi Light",
            Type = SimVarType.SimVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "bool",
            RenderAsButton = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

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
        // Auto-announce BTV state transitions (Armed -> Rotation Optimised -> Decel
        // -> End of Braking) so they frame the rollout distance call-outs below; the
        // raw value is also captured in ProcessSimVarUpdate to gate those call-outs.
        Mon("A32NX_BTV_STATE", "Brake To Vacate",
            new Dictionary<double, string> { [0] = "Disabled", [1] = "Armed", [2] = "Rotation Optimised", [3] = "Decel", [4] = "End of Braking" });
        for (int n = 1; n <= 16; n++) Read($"A32NX_BRAKE_TEMPERATURE_{n}", $"Brake {n} Temperature", "celsius");

        // ---- Source switching / ISIS ----
        Sel("A32NX_ATT_HDG_SWITCHING_KNOB", "Attitude / Heading Source", srcSw);
        Sel("A32NX_AIR_DATA_SWITCHING_KNOB", "Air Data Source", srcSw);
        Sel("A32NX_EIS_DMC_SWITCHING_KNOB", "EIS / DMC Source", srcSw);
        // Magnetic vs True heading reference (the ND/FMS TRUE REF pushbutton). The
        // pilot must confirm MAG unless TRUE is required. (#107 transcript gap.)
        Sel("A32NX_FMGC_TRUE_REF", "Heading Reference", new Dictionary<double, string> { [0] = "Magnetic", [1] = "True" });
        Sel("A32NX_CHRONO_ET_SWITCH_POS", "Elapsed Time",
            new Dictionary<double, string> { [0] = "Run", [1] = "Stop", [2] = "Reset" });

        // ============================ PEDESTAL ============================

        // ---- Engine state / start ----
        for (int n = 1; n <= 4; n++)
            Mon($"A32NX_ENGINE_STATE:{n}", $"Engine {n}",
                new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Starting", [3] = "Restarting", [4] = "Shutting Down" });

        // ---- Flaps / Speedbrake / Trim / Park brake ----
        // Flaps lever: settable 5-position detent (Up/1/2/3/Full). The handle index
        // is a computed output, so the write goes through the stock FLAPS_SET event
        // (axis value = index/4 * 16383) in HandleUIVariableSet — live-verified each
        // detent (FLAPS_2 -> 2, FLAPS_SET 4096 -> 1, 16383 -> 4). Lets a blind pilot
        // set flaps from the panel without relying on keyboard flap commands.
        Sel("A32NX_FLAPS_HANDLE_INDEX", "Flaps",
            new Dictionary<double, string> { [0] = "Up", [1] = "1", [2] = "2", [3] = "3", [4] = "Full" });
        Read("A32NX_SPOILERS_HANDLE_POSITION", "Speed Brake Handle");
        ReadEnum("A32NX_SPOILERS_ARMED", "Ground Spoilers", new Dictionary<double, string> { [0] = "Disarmed", [1] = "Armed" });
        OnOff("A32NX_PARK_BRAKE_LEVER_POS", "Parking Brake");

        // On-demand readout sources for global hotkeys (not paneled, not announced).
        Stock("KOHLSMAN_HG", "KOHLSMAN SETTING HG", "Altimeter", "inHg");
        Stock("GROSS_WEIGHT_KG", "TOTAL WEIGHT", "Gross Weight", "kilograms");

        // Metric/imperial WEIGHT unit (kg vs lb). `CONFIG_USING_METRIC_UNIT` (the
        // flyPad Units toggle, Alt+U) is an EFB persistent setting that the EFB
        // continuously mirrors to L:var `A32NX_EFB_USING_METRIC_UNIT` (fbw-common
        // EFB/Settings/sync.ts). MSFSBA reads its OWN raw kg simvars, so it bypasses
        // the FMS/EFB display conversion — registering this lets every MSFSBA weight
        // readout honour the pilot's choice (gross weight, total fuel, fuel flow).
        // Per FBW `NXUnits`, the toggle ONLY affects WEIGHTS (kg/lb) and short
        // distances (m/ft) — NOT altitude (that's A32NX_METRIC_ALT_TOGGLE), speed,
        // or NM route distance. Continuous so it tracks live; announce handled in
        // ProcessSimVarUpdate ("Weight units kilograms/pounds" on change).
        vars["A32NX_EFB_USING_METRIC_UNIT"] = new SimVarDefinition
        {
            Name = "A32NX_EFB_USING_METRIC_UNIT", DisplayName = "Weight Units",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true
        };

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
            PressSilent($"A32NX_BTN_{k}", $"ECAM {d}");

        // ---- Weather radar / SURV ----
        Sel("A32NX_SWITCH_RADAR_PWS_Position", "Predictive Windshear",
            new Dictionary<double, string> { [0] = "Auto", [1] = "Off" });
        OnOff("A32NX_RADAR_MULTISCAN_AUTO", "WXR Multiscan Auto");
        OnOff("A32NX_RADAR_GCS_AUTO", "WXR Ground Clutter Suppression");

        // ---- Transponder ----
        // Ident is a momentary action (squawk ident) — a BUTTON, not a combo. It's a
        // stock K-event, so use Evt (fires XPNDR_IDENT_ON); HandleUIVariableSet also
        // guards it. (Was an Off/Ident combo.)
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
        // Cabin Ready is a READ-ONLY status (the cabin/purser signals it via the EFB;
        // the cockpit just shows the light) — live-verified a cockpit write reverts
        // 0→1 within 2 s. So it's an auto-announced monitor var, not a settable combo.
        Mon("A32NX_CABIN_READY", "Cabin Ready", new Dictionary<double, string> { [0] = "Not Ready", [1] = "Ready" });

        // ============================ DISPLAYS / STATUS ============================
        // Settable toggle combo — fires K:AUTO_THROTTLE_ARM via HandleUIVariableSet
        // (the A380X A/THR button uses the stock arm event, not A32NX.FCU_ATHR_PUSH).
        // The toggle only commands Disengaged<->Armed; "Active" is an automatic state
        // (it engages itself with thrust), so it's kept in the dict for the READOUT
        // (so the combo reads "Active" when active) but picking it just arms — the
        // real active/armed status also comes through the FMA autothrust-mode announce.
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
            // ENG MASTER = a combo (Off/On) whose state is the stock fuel-valve
            // SimVar and whose set fires FUELSYSTEM_VALVE_OPEN/CLOSE (HandleUIVariableSet).
            // Registered as ENG_VALVE_SWITCH:n below in the engine-readback block.
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
        // APU running parameters (the start monitor: N2, EGT, inlet flap, fuel used).
        // EGT is an ARINC429 word -> decoded to celsius in TryGetDisplayOverride; the
        // rest are plain (0 when the APU is off).
        Read("A32NX_APU_N2", "APU N2", "percent");
        Read("A32NX_APU_EGT", "APU EGT", "celsius");
        Read("A32NX_APU_FLAP_OPEN_PERCENTAGE", "APU Inlet Flap", "percent");
        Read("A32NX_APU_FUEL_USED", "APU Fuel Used", "kilograms");

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
            // Pack ACTUAL operating state (true even if the PB is on but the pack is
            // not supplying air) — the real running indication, distinct from the PB.
            ReadEnum($"A32NX_COND_PACK_{n}_IS_OPERATING", $"Pack {n} Operating",
                new Dictionary<double, string> { [0] = "Off", [1] = "Operating" });
            // Pack flow control valves (each pack has 2) — open/closed status.
            for (int v = 1; v <= 2; v++)
                ReadEnum($"A32NX_COND_PACK_{n}_FLOW_VALVE_{v}_IS_OPEN", $"Pack {n} Flow Valve {v}", openVd);
            for (int ch = 1; ch <= 2; ch++)
                ReadEnum($"A32NX_COND_FDAC_{n}_CHANNEL_{ch}_FAILURE", $"FDAC {n} Channel {ch}", fault);
        }
        foreach (var z in new[] { "FWD", "BULK" })
        {
            ReadEnum($"A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{z}_PB_HAS_FAULT", $"Cargo {z} Isolation Fault", fault);
            // Actual isolation-valve open state (distinct from the pushbutton command).
            ReadEnum($"A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{z}_IS_ON", $"Cargo {z} Isolation Valve", openVd);
        }
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
        Read("A32NX_PRESS_MAN_CABIN_ALTITUDE", "Cabin Altitude", "feet");
        Read("A32NX_PRESS_MAN_CABIN_VS", "Cabin Vertical Speed", "feet per minute");
        Read("A32NX_PRESS_AUTO_LANDING_ELEVATION", "Landing Elevation", "feet");
        // The four cabin outflow valves — open percentage.
        for (int v = 1; v <= 4; v++)
            Read($"A32NX_PRESS_OUTFLOW_VALVE_{v}_OPEN_PERCENTAGE_ANIM", $"Outflow Valve {v}", "percent");

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
        // Metric altitude (the FCU MTRS button). Settable combo AND continuously
        // monitored so MSFSBA's altitude read-outs can switch to metres when it's on
        // (cached into _metricAlt in ProcessSimVarUpdate). A380-only feature.
        vars["A32NX_METRIC_ALT_TOGGLE"] = new SimVarDefinition
        {
            Name = "A32NX_METRIC_ALT_TOGGLE", DisplayName = "Metric Altitude",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

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
        // Autothrust mode — FMA column 1 (what the thrust automation is doing).
        // The third core automation cue alongside the vertical & lateral modes;
        // announced live on change. Enum from the FBW PFD FMA component.
        Mon("A32NX_AUTOTHRUST_MODE", "Autothrust Mode", new Dictionary<double, string>
        {
            [0] = "None", [1] = "Man TOGA", [2] = "Man GA Soft", [3] = "Man Flex",
            [4] = "Man Derated", [5] = "Man MCT", [6] = "Man Thrust", [7] = "Speed",
            [8] = "Mach", [9] = "Thrust MCT", [10] = "Thrust Climb", [11] = "Thrust Lever",
            [12] = "Thrust Idle", [13] = "Alpha Floor", [14] = "TOGA Lock"
        });
        // Autothrust special message — the "LVR CLB" / "THR LK" prompts that tell
        // the pilot to move the thrust levers (FMA column 1 lower line).
        Mon("A32NX_AUTOTHRUST_MODE_MESSAGE", "Autothrust Message", new Dictionary<double, string>
        {
            [0] = "None", [1] = "Thrust Lock", [2] = "Levers TOGA", [3] = "Levers Climb",
            [4] = "Levers MCT", [5] = "Levers Asymmetric"
        });
        // Thrust limit (rating) shown top-right on the E/WD — the active thrust
        // ceiling the levers command at the TO/GA detent. Enum from the FBW EWD
        // N1Limit component: ['', CLB, MCT, FLX, TOGA, MREV]. Announced on change.
        Mon("A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE", "Autothrust Thrust Limit Type", new Dictionary<double, string>
        {
            [0] = "None", [1] = "CLB", [2] = "MCT", [3] = "FLEX", [4] = "TOGA", [5] = "Max Reverse"
        });
        // Flight Director 1 / 2 (captain + F/O FD command bars). The engage-state
        // L:vars are DIRECTLY settable and STICK via the calculator path (live-verified
        // they hold for seconds, unlike the stock TOGGLE_FLIGHT_DIRECTOR event used in
        // an earlier attempt, which did not map) — written through the A32NX_ catch-all
        // in HandleUIVariableSet. Continuous + announced, so FD on/off speaks on change.
        OnOff("A32NX_FCU_EFIS_L_FD_ACTIVE", "Flight Director 1");
        OnOff("A32NX_FCU_EFIS_R_FD_ACTIVE", "Flight Director 2");
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
            Act($"COM{n}_RADIO_SWAP", $"VHF {n} Swap", new Dictionary<double, string> { [0] = "Idle", [1] = "Swap active and standby" });
            Stock($"COM_ACTIVE_FREQUENCY:{n}", $"COM ACTIVE FREQUENCY:{n}", $"VHF {n} Active", "MHz");
            Stock($"COM_STANDBY_FREQUENCY:{n}", $"COM STANDBY FREQUENCY:{n}", $"VHF {n} Standby", "MHz");
        }

        // ============================ TRANSPONDER / ATC ============================
        // "BCO16" makes SimConnect decode the BCD-packed code to the 4-digit squawk
        // (e.g. 4242); reading it as "number" gave the raw BCD integer (0x4242=16962).
        Stock("XPNDR_CODE", "TRANSPONDER CODE:1", "Squawk Code", "BCO16");
        // Key MUST be "TRANSPONDER_CODE_SET" so MainForm's squawk-input path
        // BCD16-encodes the entered code (4242 -> 0x4242). Sending the raw decimal
        // via the generic event path produced a wrong squawk. Event name stays XPNDR_SET.
        Evt("TRANSPONDER_CODE_SET", "XPNDR_SET", "Squawk Code");
        // Settable: the mode L:var is writable and sticks (verified live — STBY/AUTO/
        // ON all hold). Set via the calculator catch-all at the end of HandleUIVariableSet.
        Sel("A32NX_TRANSPONDER_MODE", "Transponder Mode",
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
        // Clock CHR stopwatch + ET counter. Real, live, FBW-Clock-written L:vars
        // (seconds; -1 = blank) — formatted to MM:SS / HH:MM in TryGetDisplayOverride.
        Read("A32NX_CHRONO_ELAPSED_TIME", "Chronometer", "seconds");
        Read("A32NX_CHRONO_ET_ELAPSED_TIME", "Elapsed Time", "seconds");

        // ISIS standby instrument.
        ReadEnum("A32NX_ISIS_LS_ACTIVE", "ISIS LS", onOff);
        ReadEnum("A32NX_ISIS_BUGS_ACTIVE", "ISIS Bugs Page", onOff);
        Sel("A32NX_ISIS_BARO_MODE", "ISIS Baro Mode", new Dictionary<double, string> { [0] = "Set", [1] = "Standard" });
        OnOff("A32NX_ISIS_BARO_UNIT_INHG", "ISIS Baro in inHg");

        // Brakes.
        OnOff("A32NX_BRAKE_FAN_BTN_PRESSED", "Brake Fan", button: true);
        ReadEnum("A32NX_BRAKE_FAN_RUNNING", "Brake Fan Running", new Dictionary<double, string> { [0] = "Off", [1] = "Running" });
        ReadEnum("A32NX_BRAKES_HOT", "Brakes Hot", new Dictionary<double, string> { [0] = "Normal", [1] = "HOT" });
        // Normal (green-system) brake pressure L/R — the "triple indicator". The
        // taxi brake check wants this at ~0 while braking (brakes on the normal
        // system) vs the accumulator below. (#107 transcript gap: brake check.)
        Read("A32NX_HYD_BRAKE_NORM_LEFT_PRESS", "Normal Brake Left", "psi");
        Read("A32NX_HYD_BRAKE_NORM_RIGHT_PRESS", "Normal Brake Right", "psi");
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

        // Fuel crossfeed + jettison status + volume. Each XFEED valve is driven by
        // the stock K:FUELSYSTEM_VALVE_OPEN / _CLOSE events (valve IDs 46-49), NOT the
        // XMLVAR_Momentary_..._Pressed L:var — that var is only the cockpit model's
        // press-animation flag and never actuates the valve (verified live #60:
        // pulsing it left FUELSYSTEM VALVE SWITCH unchanged; the FBW
        // FBW_Airbus_Fuel_Crossfeed template drives FUELSYSTEM_VALVE_TOGGLE on #ID#).
        // Each crossfeed is a single Closed/Open COMBO: live state from the stock
        // fuel-valve SimVar, and the set fires FUELSYSTEM_VALVE_OPEN/CLOSE (valve ids
        // 46-49) via HandleUIVariableSet. OPEN/CLOSE (not TOGGLE) because TOGGLE stuck
        // mid-transition on rapid presses; these are the same events the engine
        // masters use. Continuous + announced so it speaks Closed/Open on change.
        for (int n = 1; n <= 4; n++)
        {
            int id = 45 + n;
            vars[$"XFEED_{n}_STATE"] = new SimVarDefinition
            {
                Name = $"FUELSYSTEM VALVE SWITCH:{id}", DisplayName = $"Crossfeed {n}",
                Type = SimVarType.SimVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            };
        }
        ReadEnum("A380X_OVHD_FUEL_JETTISON_IS_OPEN", "Jettison Valve", openVd);
        Read("A32NX_TOTAL_FUEL_VOLUME", "Total Fuel Volume", "gallons");

        // FEED-TANK FUEL PUMPS (4 tanks × MAIN + STBY = 8). On the FBW A380 the
        // cockpit pump pushbuttons are modelled as ELECTRICAL CIRCUITS (state =
        // `A:CIRCUIT CONNECTION ON:<id>`, toggled by `<id> 1
        // (>K:2:ELECTRICAL_BUS_TO_CIRCUIT_CONNECTION_TOGGLE)`) — NOT the stock
        // FUELSYSTEM pumps. Each is an Off/On combo: live state from the circuit
        // simvar; the set toggles the circuit (only when desired != current) via
        // HandleUIVariableSet + _fuelPumpCircuits. The circuit IDs are from the
        // cockpit model (PUSH_OVHD_FUEL_FEEDTK*_MAIN/STBY). #107 transcript: "turn
        // the fuel pumps on in sequence, main then standby." Calculator-path toggle
        // verified live (circuit 2 flips 0↔1).
        foreach (var (key, circuit, label) in new[]
        {
            ("FUELPUMP_FEEDTK1_MAIN", 2, "Feed Tank 1 Main Pump"),
            ("FUELPUMP_FEEDTK1_STBY", 3, "Feed Tank 1 Standby Pump"),
            ("FUELPUMP_FEEDTK2_MAIN", 64, "Feed Tank 2 Main Pump"),
            ("FUELPUMP_FEEDTK2_STBY", 65, "Feed Tank 2 Standby Pump"),
            ("FUELPUMP_FEEDTK3_MAIN", 66, "Feed Tank 3 Main Pump"),
            ("FUELPUMP_FEEDTK3_STBY", 67, "Feed Tank 3 Standby Pump"),
            ("FUELPUMP_FEEDTK4_MAIN", 68, "Feed Tank 4 Main Pump"),
            ("FUELPUMP_FEEDTK4_STBY", 69, "Feed Tank 4 Standby Pump"),
        })
        {
            _fuelPumpCircuits[key] = circuit;
            vars[key] = new SimVarDefinition
            {
                Name = $"CIRCUIT CONNECTION ON:{circuit}", DisplayName = label,
                Type = SimVarType.SimVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            };
        }

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
        // These are all HOLD buttons in the FBW model (<HOLD_SIMVAR>): the action
        // runs only WHILE held at 1 (CVR test tone, ELT test, FDR event mark, rain-
        // repellent squirt), so render as On/Off — set On to run/hold, Off to stop —
        // rather than a too-brief momentary pulse.
        OnOff("A32NX_RCDR_TEST", "Recorder Test");
        OnOff("A32NX_ELT_TEST_RESET", "ELT Test / Reset");
        OnOff("A32NX_DFDR_EVENT_ON", "DFDR Event");
        OnOff("A32NX_RAIN_REPELLENT_LEFT_ON", "Rain Repellent Left");
        OnOff("A32NX_RAIN_REPELLENT_RIGHT_ON", "Rain Repellent Right");
        OnOff("A32NX_OVHD_NSS_DATA_TO_AVNCS_TOGGLE", "NSS Data to Avionics");
        OnOff("A32NX_NSS_MASTER_OFF", "NSS Master Off");

        // Wipers (3-speed; written via HandleUIVariableSet → circuit power events).
        // Wipers (captain = electrical circuit 141, F/O = 143). The FBW wiper-knob
        // template drives the circuit with K:ELECTRICAL_CIRCUIT_TOGGLE (on/off) +
        // K:2:ELECTRICAL_CIRCUIT_POWER_SETTING_SET (speed) — live-verified the toggle
        // flips CIRCUIT SWITCH ON:141 0<->1 and "<pct> <circuit> (>K:2:…)" sets the
        // power. The PREVIOUS code only set the power and never toggled the circuit
        // on, so the wipers never started — the "inop" bug. The combo now reads the
        // REAL circuit-switch state (so it shows On/Off correctly and auto-announces
        // on change); the set toggles the circuit (handled in HandleUIVariableSet).
        foreach (var (key, who, circuit) in new[]
                 { ("WIPER_LEFT", "Captain Wiper", 141), ("WIPER_RIGHT", "First Officer Wiper", 143) })
            vars[key] = new SimVarDefinition
            {
                Name = $"CIRCUIT SWITCH ON:{circuit}", DisplayName = who, Type = SimVarType.SimVar,
                Units = "bool", UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            };

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
            // #60 NOTE (verified in FBW source extras-host/BaroUnitSelector.ts):
            // the unit is NOT a freely-settable cockpit control — FBW derives it
            // from the flyPad config + the aircraft's GEOGRAPHIC POSITION (inHg in
            // N. America, hPa elsewhere) and the systems code re-writes this XMLVAR
            // from that, so a SimConnect write to it reverts. Kept as a combo for
            // the live READOUT (which is correct); a set is best-effort only.
            Sel($"XMLVAR_Baro_Selector_HPA_{(side == "L" ? "1" : "2")}", $"{who} Baro Unit",
                new Dictionary<double, string> { [0] = "inHg", [1] = "hPa" });
        }

        // ECAM control panel — checklist buttons + SD more. Silent: the ECL window
        // pulses CHECK to tick items, and announcing "ECAM Check, Pressed/Released"
        // on every Enter is noise (the ticked line is announced by the ECL scrape).
        PressSilent("A32NX_BTN_CHECK_LH", "ECAM Check Left");
        PressSilent("A32NX_BTN_CHECK_RH", "ECAM Check Right");
        ReadEnum("A32NX_SD_MORE_SHOWN", "SD More Page", new Dictionary<double, string> { [0] = "No", [1] = "Shown" });

        // ATC datalink (DCDU).
        Btn("A32NX_DCDU_ATC_MSG_ACK", "ATC Message Acknowledge");
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
        // VS + VFE-next back the Shift+5 / Shift+6 readout hotkeys (ReadSpeedVS /
        // ReadSpeedVFE -> these keys). They MUST be registered or RequestReadout
        // early-returns and the hotkey is silent (audit: dead hotkeys).
        Read("A32NX_SPEEDS_VS", "V S (stall)", "knots");
        Read("A32NX_SPEEDS_VFEN", "V F E next", "knots");

        // Lighting extras.
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
        OnOff("A32NX_GPWS_TEST", "GPWS Test");   // HOLD self-test: On runs the test audio, Off ends it

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
            // Per-engine fuel used since start (kg) — the post-flight even-burn check
            // (#107 transcript: "fuel used for each engine"). Stock indexed simvar.
            Stock($"ENG_FUEL_USED:{n}", $"GENERAL ENG FUEL USED SINCE START:{n}", $"Engine {n} Fuel Used", "kilograms");
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
        // Pitch trim (THS). The A380 SD shows `ELEVATOR TRIM POSITION` as degrees
        // UP/DN — the stock `ELEVATOR TRIM INDICATOR` (normalized -1..1) MSFSBA used
        // before was the WRONG representation for the A380. Read it in DEGREES (the
        // SAME unit the shared base `MON_ElevatorTrim` / Shift+T trim-announce uses —
        // so there is no unit mismatch on the same simvar) and decode to
        // "X.X degrees up/down" in TryGetDisplayOverride (positive = UP, matching SD).
        // This is panel-display only (OnRequest) and does NOT touch the Shift+T
        // continuous trim-announce, which runs entirely off the base MON_ElevatorTrim.
        Stock("ELEVATOR_TRIM", "ELEVATOR TRIM POSITION", "Pitch Trim", "degrees");
        // Flight-control SURFACE deflections (stock simvars, live-verified to give
        // real degrees) — for the accessible flight-control check (#107 transcript
        // gap): move the stick/pedals full travel and read each surface here. The
        // panel display refreshes on open; the values are also in Ctrl+M off (just
        // panel reads, not announced — they'd be far too chatty as you fly).
        Stock("ELEVATOR_DEFLECTION", "ELEVATOR DEFLECTION", "Elevator Deflection", "degrees");
        Stock("AILERON_DEFLECTION", "AILERON AVERAGE DEFLECTION", "Aileron Deflection", "degrees");
        Stock("RUDDER_DEFLECTION", "RUDDER DEFLECTION", "Rudder Deflection", "degrees");
        Stock("SPOILERS_LEFT_POSITION", "SPOILERS LEFT POSITION", "Left Spoilers", "percent");
        Stock("SPOILERS_RIGHT_POSITION", "SPOILERS RIGHT POSITION", "Right Spoilers", "percent");
        // FMS-computed takeoff THS / pitch-trim target (ARINC429) — the value the
        // pilot must set the trim wheel to before takeoff (auto-trim isn't modeled
        // on this build). Decoded in TryGetDisplayOverride; reads "Not computed"
        // until the FMS has the perf data. (#107 transcript gap: predicted T.O trim.)
        Read("A32NX_TO_PITCH_TRIM", "Takeoff Trim Target", "number");
        // Rudder trim ("weather trim"). The stock RUDDER TRIM PCT does not track the
        // FBW SEC-computed value; the real figure the PFD/SD show is the ARINC429
        // word A32NX_SEC_1_RUDDER_ACTUAL_POSITION (degrees, positive = nose-Left).
        // Decoded to "Left/Right X.X degrees" / "Neutral" in TryGetDisplayOverride.
        Read("A32NX_SEC_1_RUDDER_ACTUAL_POSITION", "Rudder Trim", "number");
        // Rudder trim RESET (zeroes the trim) + nosewheel-steering pedal disconnect
        // (held during the rudder flight-control check so the nose wheel doesn't
        // scrub). #107 transcript gaps. Settable via the calculator catch-all.
        // Rudder Trim Reset: the cockpit fires the stock K-event RUDDER_TRIM_RESET on
        // press (NOT an L:var — pulsing A32NX_RUDDER_TRIM_RESET drove nothing). Render
        // as an Idle/Reset action combo; the Reset option fires the K-event (verified).
        Act("A32NX_RUDDER_TRIM_RESET", "Rudder Trim Reset",
            new Dictionary<double, string> { [0] = "Idle", [1] = "Reset" });
        Btn("A32NX_TILLER_PEDAL_DISCONNECT", "Nosewheel Steering Pedal Disconnect");
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
            // ENG MASTER combo: live SimVar state (FUELSYSTEM VALVE SWITCH:n),
            // settable — the set fires FUELSYSTEM_VALVE_OPEN/CLOSE via HandleUIVariableSet.
            // Continuous + announced so it speaks Off/On on change like every combo.
            vars[$"ENG_VALVE_SWITCH:{n}"] = new SimVarDefinition
            {
                Name = $"FUELSYSTEM VALVE SWITCH:{n}", DisplayName = $"Engine {n} Master",
                Type = SimVarType.SimVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            };
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

        // ---- Runway Overrun Warning / Protection (ROW/ROP) + OANS RWY AHEAD ----
        // Landing-rollout and taxi safety call-outs. Both are ARINC429 discrete
        // words (plain L-vars, no colon index); each warning is a single bit,
        // decoded + announced on its rising edge in ProcessSimVarUpdate. Bits from
        // the FBW PFD AttitudeIndicatorWarnings: ROW_ROP_WORD_1 13=MAX BRAKING,
        // 14=IF WET RWY TOO SHORT, 15=RWY TOO SHORT; OANS_WORD_1 11=RWY AHEAD.
        vars["A32NX_ROW_ROP_WORD_1"] = new SimVarDefinition
        {
            Name = "A32NX_ROW_ROP_WORD_1", DisplayName = "Runway Overrun Protection",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };
        vars["A32NX_OANS_WORD_1"] = new SimVarDefinition
        {
            Name = "A32NX_OANS_WORD_1", DisplayName = "Runway Ahead Advisory",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };

        // ---- BTV (Brake-To-Vacate) rollout distances ----
        // ARINC429 metres (decode with Arinc429Word.ValueOr; SSM-normal only while
        // BTV is computing the rollout). Announced at descending thresholds during
        // the braking rollout in ProcessSimVarUpdate, gated on the BTV state, so the
        // user hears "X meters to exit" / "X meters runway remaining" as they slow.
        vars["A32NX_OANS_BTV_REMAINING_DIST_TO_EXIT"] = new SimVarDefinition
        {
            Name = "A32NX_OANS_BTV_REMAINING_DIST_TO_EXIT", DisplayName = "BTV Distance to Exit",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };
        vars["A32NX_OANS_BTV_REMAINING_DIST_TO_RWY_END"] = new SimVarDefinition
        {
            Name = "A32NX_OANS_BTV_REMAINING_DIST_TO_RWY_END", DisplayName = "Runway Remaining",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };

        // ---- Ground Services (flyPad Ground page, exposed as cockpit controls) ----
        // Doors: read the stock `INTERACTIVE POINT OPEN:n` as BOOL so the door
        // auto-announces Open/Closed exactly once per transition (a Percent read
        // would spam every frame of the open/close animation). Toggle via the
        // stock K:TOGGLE_AIRCRAFT_EXIT event with the door's interaction index —
        // the same mechanism the FBW flyPad uses (verified from FBW source).
        var openShut = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" };
        void Door(string key, int ip, uint exitId, string display)
        {
            // A door is one Closed/Open COMBO: live state from INTERACTIVE POINT
            // OPEN:ip, and the set fires TOGGLE_AIRCRAFT_EXIT (exit id via the
            // _doorExitIds map) through HandleUIVariableSet — no separate button.
            vars[key] = new SimVarDefinition
            {
                Name = $"INTERACTIVE POINT OPEN:{ip}", DisplayName = display,
                Type = SimVarType.SimVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = openShut
            };
        }
        Door("A380X_GND_DOOR_MAIN1L", 0, 1, "Main 1 Left Door");
        Door("A380X_GND_DOOR_MAIN2L", 2, 3, "Main 2 Left Door");
        Door("A380X_GND_DOOR_MAIN4R", 9, 10, "Main 4 Right Door");
        Door("A380X_GND_DOOR_UPPER1L", 10, 11, "Upper 1 Left Door");
        Door("A380X_GND_DOOR_FWDCARGO", 16, 17, "Forward Cargo Door");

        // Jet bridge + passenger stairs (stock MSFS ground-service events;
        // airport/parking dependent). Catering, fuel-truck, baggage and pushback
        // are intentionally NOT exposed — GSX owns those.
        var retractExtend = new Dictionary<double, string> { [0] = "Retracted", [1] = "Extended" };
        Act("A380X_GND_JETWAY", "Jet Bridge", retractExtend);
        Act("A380X_GND_STAIRS", "Passenger Stairs", retractExtend);

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

        // External-power availability per receptacle (read-only; the actual connect
        // is the overhead EXT PWR pushbuttons or a ground-handling add-on like GSX).
        // Announces when external power becomes available at the stand.
        //
        // Read the FBW availability L-var A32NX_OVHD_ELEC_EXT_PWR_n_PB_IS_AVAILABLE
        // (the overhead EXT PWR "AVAIL" light), NOT the stock simvar EXTERNAL POWER
        // AVAILABLE:n. The stock simvar means only "a GPU exists at this stand" — it
        // sits at 1 at any suitable gate regardless of whether power is actually
        // connected to the aircraft (verified live: it read 1 with engines running and
        // no power feeding the jet), so watching it never fired on a real GSX connect/
        // disconnect. The FBW aspect var reflects the actual AVAIL annunciation the
        // pilot sees and is what GSX drives through the electrical model (verified live
        // it read 0 in the same no-power state). It is colon-free, so the data-def read
        // is reliable (the OLD note avoided the colon-INDEXED source var
        // A32NX_EXT_PWR_AVAIL:n — this aspect var is the readable copy of it).
        for (int n = 1; n <= 4; n++)
        {
            vars[$"A380X_GND_GPU_AVAIL_{n}"] = new SimVarDefinition
            {
                Name = $"A32NX_OVHD_ELEC_EXT_PWR_{n}_PB_IS_AVAILABLE", DisplayName = $"External Power {n} Available",
                Type = SimVarType.LVar, Units = "Bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Yes" }
            };
        }

        _varCache = vars;   // for ProcessSimVarUpdate lookups (ARINC429 enum decode)
        return vars;
    }

    // Cached variable map + per-var last announced state for the ARINC429 enum guard.
    private Dictionary<string, SimVarDefinition>? _varCache;
    private readonly Dictionary<string, int> _arincEnumState = new();

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
            ["Instrument"] = new List<string> { "Gear", "Autobrake", "ISIS", "Source Switching", "Clock" },
            ["Pedestal"] = new List<string>
            {
                "Engines", "Thrust Levers", "Flaps and Brakes", "ECAM Control Panel", "Weather Radar",
                "Transponder", "Radios", "RMP", "Audio Control Panel Captain", "Audio Control Panel First Officer", "Cockpit Door"
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
            "ELEC_ENG_GEN:1", "ELEC_ENG_GEN:2", "ELEC_ENG_GEN:3", "ELEC_ENG_GEN:4",
            "ELEC_APU_GEN:1", "ELEC_APU_GEN:2",
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
            "A380X_OVHD_FUEL_JETTISON_ACTIVE_PB_IS_ON",
            "FUELPUMP_FEEDTK1_MAIN", "FUELPUMP_FEEDTK1_STBY",
            "FUELPUMP_FEEDTK2_MAIN", "FUELPUMP_FEEDTK2_STBY",
            "FUELPUMP_FEEDTK3_MAIN", "FUELPUMP_FEEDTK3_STBY",
            "FUELPUMP_FEEDTK4_MAIN", "FUELPUMP_FEEDTK4_STBY"
        };
        p["Hydraulics"] = new List<string>
        {
            "A32NX_OVHD_HYD_ENG_1A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_1B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_2A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_2B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_3A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_3B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_4A_PUMP_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_4B_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPGA_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPGA_OFF_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPGB_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPGB_OFF_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPYA_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPYA_OFF_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPYB_ON_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPYB_OFF_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO", "A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_1AB_PUMP_DISC_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_2AB_PUMP_DISC_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_3AB_PUMP_DISC_PB_IS_AUTO", "A32NX_OVHD_HYD_ENG_4AB_PUMP_DISC_PB_IS_AUTO",
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
            "A32NX_MAN_PITOT_HEAT", "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED",
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
            "SEATBELT_SIGN", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position",
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
            "A32NX_SEC_1_PUSHBUTTON_PRESSED", "A32NX_SEC_2_PUSHBUTTON_PRESSED", "A32NX_SEC_3_PUSHBUTTON_PRESSED",
            "A32NX_RUDDER_TRIM_RESET", "A32NX_TILLER_PEDAL_DISCONNECT"
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
            "A380X_OVHD_STORM_LT", "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE",
            "A32NX_ACMS_TRIGGER_ON", "A32NX_CREW_HEAD_SET", "A32NX_SVGEINT_OVRD_ON",
            "A32NX_ENGMANSTARTALTN_TOGGLE", "A32NX_ENTERTAINMENT_CWS_OFF",
            "A32NX_ENTERTAINMENT_IFEC_OFF", "A380X_REMOTE_CB_CTRL",
        };
        p["Audio Control Panel Captain"] = new List<string>
        {
            "A380X_RMP_1_VHF_VOL_RX_SWITCH_1", "A380X_RMP_1_VHF_VOL_RX_SWITCH_2",
            "A380X_RMP_1_CAB_VOL_RX_SWITCH", "A380X_RMP_1_INT_VOL_RX_SWITCH",
            "A380X_RMP_1_PA_VOL_RX_SWITCH", "A380X_RMP_1_NAV_VOL_RX_SWITCH",
            "A380X_RMP_1_VHF_TX_1"
        };
        p["Audio Control Panel First Officer"] = new List<string>
        {
            "A380X_RMP_2_VHF_VOL_RX_SWITCH_1", "A380X_RMP_2_VHF_VOL_RX_SWITCH_2",
            "A380X_RMP_2_CAB_VOL_RX_SWITCH", "A380X_RMP_2_INT_VOL_RX_SWITCH",
            "A380X_RMP_2_PA_VOL_RX_SWITCH", "A380X_RMP_2_NAV_VOL_RX_SWITCH",
            "A380X_RMP_2_VHF_TX_1"
        };
        p["Interior Lighting"] = new List<string>
        {
            "A380X_OVHD_ANN_LT_POSITION", "A32NX_OVHD_INTLT_ANN"
        };
        p["Exterior Lighting"] = new List<string>
        {
            "LIGHT_BEACON", "LIGHT_STROBE", "LIGHT_NAV", "LIGHT_WING", "LIGHT_LOGO",
            "LIGHT_LANDING", "LIGHT_TAXI_OVHD"
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
            "A32NX_EFIS_L_OANS_RANGE",
            // Flight Director 1 (captain). The earlier removal said writes "fail",
            // but the engage-state L:var IS settable and HOLDS via the calculator
            // path (re-verified live: set 1 → still 1 after 2.5 s).
            "A32NX_FCU_EFIS_L_FD_ACTIVE"
        };
        p["EFIS First Officer"] = new List<string>
        {
            "A380X_EFIS_R_LS_BUTTON_IS_ON", "A380X_EFIS_R_VV_BUTTON_IS_ON", "A380X_EFIS_R_CSTR_BUTTON_IS_ON",
            "A380X_EFIS_R_ARPT_BUTTON_IS_ON", "A380X_EFIS_R_TRAF_BUTTON_IS_ON",
            "A32NX_EFIS_R_ND_MODE", "A32NX_EFIS_R_ND_RANGE",
            "A380X_EFIS_R_ACTIVE_FILTER", "A380X_EFIS_R_ACTIVE_OVERLAY",
            "A32NX_EFIS_R_NAVAID_1_MODE", "A32NX_EFIS_R_NAVAID_2_MODE",
            "A32NX_FCU_RIGHT_EIS_BARO_IS_STD", "FO_QNH_SET", "XMLVAR_Baro_Selector_HPA_2",
            "A32NX_EFIS_R_OANS_RANGE",
            "A32NX_FCU_EFIS_R_FD_ACTIVE"   // Flight Director 2 (F/O) — see captain side
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
            // ANTISKID_BRAKES_ACTIVE is a read-only sim state — it lives in the
            // Autobrake read-out (d[...]), NOT here, so it never renders as a
            // pointlessly-settable combo.
            "A32NX_AUTOBRAKES_SELECTED_MODE", "A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED"
        };
        p["Source Switching"] = new List<string>
        {
            "A32NX_ATT_HDG_SWITCHING_KNOB", "A32NX_AIR_DATA_SWITCHING_KNOB",
            "A32NX_EIS_DMC_SWITCHING_KNOB", "A32NX_FMGC_TRUE_REF"
        };
        // Clock panel: the chronometer start/stop + reset buttons and the elapsed-time
        // (ET) Run/Stop/Reset knob are the controls; the elapsed-time readouts are the
        // status display (d["Clock"]).
        p["Clock"] = new List<string>
        {
            "A32NX_CHRONO_TOGGLE", "A32NX_CHRONO_RST", "A32NX_CHRONO_ET_SWITCH_POS"
        };

        p["Engines"] = new List<string>
        {
            "ENGINE_MODE_SELECTOR",
            // ENG MASTER 1-4 are now Off/On combos (state from the fuel-valve SimVar,
            // set fires FUELSYSTEM_VALVE_OPEN/CLOSE) — no separate On/Off buttons.
            "ENG_VALVE_SWITCH:1", "ENG_VALVE_SWITCH:2", "ENG_VALVE_SWITCH:3", "ENG_VALVE_SWITCH:4"
            // ENG MAN START 1-4 live in the overhead "Engine Start" panel (not duplicated here).
        };
        p["Thrust Levers"] = new List<string>
        {
            "THROTTLE_ALL_DETENT", "THROTTLE_1_DETENT", "THROTTLE_2_DETENT",
            "THROTTLE_3_DETENT", "THROTTLE_4_DETENT"
        };
        p["Flaps and Brakes"] = new List<string> { "A32NX_FLAPS_HANDLE_INDEX", "A32NX_PARK_BRAKE_LEVER_POS" };
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
        // Cabin Ready is read-only (auto-announced via Mon) — surfaced as a status
        // readout (d["Cockpit Door"], in the display section below), not a settable
        // control. Cockpit Door lock IS settable.
        p["Cockpit Door"] = new List<string> { "A32NX_COCKPIT_DOOR_LOCKED" };

        // ---- Ground Services (flyPad Ground page) ----
        p["Doors"] = new List<string>
        {
            "A380X_GND_DOOR_MAIN1L", "A380X_GND_DOOR_MAIN2L", "A380X_GND_DOOR_MAIN4R",
            "A380X_GND_DOOR_UPPER1L", "A380X_GND_DOOR_FWDCARGO"
        };
        p["Ground Equipment"] = new List<string>
        {
            // Only jetway/stairs are user-callable. CHOCKS, CONES and GPU-available
            // are read-only model/sim state (writing them reverts), so they live in
            // the read-out (d["Ground Equipment"]) instead of rendering as settable
            // combos a user could change to no effect.
            "A380X_GND_JETWAY", "A380X_GND_STAIRS"
        };

        p["Status"] = new List<string>
        {
            // FMS_PAX_NUMBER and ECAM_FAILURE_ACTIVE are read-only — they belong in
            // the Status read-out (d["Status"]), not here where they'd render as
            // pointlessly-settable controls. Only the FMS switching knob is settable.
            "A32NX_FMS_SWITCHING_KNOB"
        };

        // ---- A32NX shared gap controls folded into panels ----
        p["Fuel"].AddRange(new[]
        {
            "XFEED_1_STATE", "XFEED_2_STATE", "XFEED_3_STATE", "XFEED_4_STATE"
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
            "A32NX_GPWS_TERR_OFF", "A32NX_GPWS_FLAPS3", "A32NX_GPWS_TEST"
        };
        // "PFD" is NOT a navigable control panel — it's the variable set the PFD
        // window (ShowPFD hotkey) requests/reads. Intentionally absent from
        // GetPanelStructure so it isn't shown as a UI panel.
        p["PFD"] = new List<string>
        {
            "A32NX_AUTOTHRUST_MODE", "A32NX_AUTOTHRUST_MODE_MESSAGE",
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
            "A32NX_APU_LOW_FUEL_PRESSURE_FAULT", "A32NX_APU_BLEED_AIR_VALVE_OPEN", "A32NX_APU_N_RAW",
            "A32NX_APU_N2", "A32NX_APU_EGT", "A32NX_APU_FLAP_OPEN_PERCENTAGE", "A32NX_APU_FUEL_USED"
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
            cond.Add($"A32NX_COND_PACK_{n}_IS_OPERATING");
            cond.Add($"A32NX_COND_PACK_{n}_FLOW_VALVE_1_IS_OPEN");
            cond.Add($"A32NX_COND_PACK_{n}_FLOW_VALVE_2_IS_OPEN");
            cond.Add($"A32NX_OVHD_COND_HOT_AIR_{n}_PB_HAS_FAULT");
            for (int ch = 1; ch <= 2; ch++) cond.Add($"A32NX_COND_FDAC_{n}_CHANNEL_{ch}_FAILURE");
        }
        foreach (var z in new[] { "FWD", "BULK" })
        {
            cond.Add($"A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{z}_PB_HAS_FAULT");
            cond.Add($"A32NX_OVHD_CARGO_AIR_ISOL_VALVES_{z}_IS_ON");
        }
        cond.Add("A32NX_OVHD_CARGO_AIR_HEATER_PB_HAS_FAULT");
        d["Air Conditioning"] = cond;

        var press = new List<string>();
        for (int n = 1; n <= 4; n++)
        {
            for (int ch = 1; ch <= 2; ch++) press.Add($"A32NX_PRESS_OCSM_{n}_CHANNEL_{ch}_FAILURE");
            press.Add($"A32NX_PRESS_OCSM_{n}_AUTO_PARTITION_FAILURE");
        }
        press.Add("A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE");
        press.Add("A32NX_PRESS_MAN_CABIN_ALTITUDE");
        press.Add("A32NX_PRESS_MAN_CABIN_VS");
        press.Add("A32NX_PRESS_AUTO_LANDING_ELEVATION");
        for (int v = 1; v <= 4; v++) press.Add($"A32NX_PRESS_OUTFLOW_VALVE_{v}_OPEN_PERCENTAGE_ANIM");
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
        adirs.Add("A32NX_OVHD_ADIRS_ON_BAT_IS_ILLUMINATED");
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
            // Flaps handle is now a settable, auto-announced combo in the panel, so
            // it's not duplicated here as a read-only field.
            "A32NX_SPOILERS_HANDLE_POSITION", "A32NX_SPOILERS_ARMED"
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
            "A32NX_HYD_BRAKE_NORM_LEFT_PRESS", "A32NX_HYD_BRAKE_NORM_RIGHT_PRESS",
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
        d["Status"].AddRange(new[] { "A380X_FMS_DEST_EFOB_BELOW_MIN", "A32NX_FMS_PAX_NUMBER", "A32NX_ECAM_FAILURE_ACTIVE" });
        // ANTISKID is a read-only sim state (moved out of the Autobrake controls).
        d["Autobrake"].Add("ANTISKID_BRAKES_ACTIVE");
        // Ground equipment read-outs: chocks/cones model state + per-receptacle GPU
        // availability (all read-only; the GPU connect is the overhead EXT PWR PBs).
        d["Ground Equipment"] = new List<string>
        {
            "A380X_GND_CHOCKS", "A380X_GND_CONES",
            "A380X_GND_GPU_AVAIL_1", "A380X_GND_GPU_AVAIL_2", "A380X_GND_GPU_AVAIL_3", "A380X_GND_GPU_AVAIL_4"
        };

        // Clock readouts (the chrono + elapsed-time fields shown read-only in the
        // Clock panel; the controls live in p["Clock"]).
        d["Clock"] = new List<string> { "A32NX_CHRONO_ELAPSED_TIME", "A32NX_CHRONO_ET_ELAPSED_TIME" };
        d["Cockpit Door"] = new List<string> { "A32NX_CABIN_READY" };
        d["ISIS"] = new List<string> { "A32NX_ISIS_LS_ACTIVE", "A32NX_ISIS_BUGS_ACTIVE" };
        d["Oxygen"] = new List<string> { "A32NX_OXYGEN_TMR_RESET_FAULT" };
        d["Calls"] = new List<string> { "A32NX_SLIDES_ARMED", "A32NX_EVAC_COMMAND_FAULT" };
        // Status display shows which SD page is currently up (decoded to its name) +
        // the More flag. For the page CONTENTS, open the System Display window (it
        // live-scrapes any page); the combo here selects the page.
        d["ECAM Control Panel"] = new List<string> { "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", "A32NX_SD_MORE_SHOWN" };
        d["Wipers"] = new List<string> { "WIPER_LEFT", "WIPER_RIGHT" };
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
            // ENG_VALVE_SWITCH:n is now a settable control combo in the Engines panel,
            // so it is no longer listed here as a read-only display field.
            d["Engines"].AddRange(new[] { $"A32NX_ENGINE_OIL_QTY:{n}", $"ENG_FUEL_USED:{n}", $"ENG_VIBRATION:{n}", $"ENG_IGN_POS:{n}" });

        d["Air Conditioning"].AddRange(new[] { "A32NX_COND_CARGO_FWD_TEMP", "A32NX_COND_CARGO_BULK_TEMP", "A32NX_COND_CKPT_DUCT_TEMP" });

        d["Flight Control Computers"].AddRange(new[]
        {
            "A32NX_LEFT_FLAPS_POSITION_PERCENT", "A32NX_RIGHT_FLAPS_POSITION_PERCENT",
            "A32NX_LEFT_SLATS_POSITION_PERCENT", "A32NX_RIGHT_SLATS_POSITION_PERCENT",
            "ELEVATOR_TRIM", "A32NX_TO_PITCH_TRIM", "A32NX_SEC_1_RUDDER_ACTUAL_POSITION",
            "ELEVATOR_DEFLECTION", "AILERON_DEFLECTION", "RUDDER_DEFLECTION",
            "SPOILERS_LEFT_POSITION", "SPOILERS_RIGHT_POSITION",
            "A32NX_PRIORITY_TAKEOVER:1", "A32NX_PRIORITY_TAKEOVER:2"
        });

        d["Gear"].AddRange(new[] { "GEAR_LEFT_POS", "GEAR_CENTER_POS", "GEAR_RIGHT_POS", "A32NX_AUTOBRAKES_RTO_ARMED" });

        // NOTE: annunciators (faults + non-fault state lights) ARE listed here on
        // purpose. They render into the panel's single READ-ONLY "Status Display"
        // text field (MainForm.UpdateDisplayText) — never as a settable combo — so
        // they read on demand AND auto-announce on change. They legitimately overlap
        // some E/WD memos; that twin call-out (annunciator + memo) is fine.
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
    // Last seen E/WD code per line (live cache for ReadAllEwdWarnings).
    private readonly Dictionary<string, long> _lastEwdCode = new();
    // The set of E/WD codes currently on screen (across all lines) that have been
    // announced — so a message that scrolls between lines isn't re-announced.
    private readonly HashSet<long> _announcedEwdCodes = new();
    /// <summary>Set by MainForm when the E/WD DOM-scrape monitor (CoherentEWDClient)
    /// is running. While true, the SimVar EWD_LOWER memo auto-announce is suppressed
    /// so the scrape is the single source for E/WD call-outs (it announces both the
    /// memos and the DOM-only failure procedures). Default false = SimVar announces
    /// (safe fallback when no scrape monitor is active).</summary>
    public bool EwdScrapeHandlesAnnounce;
    private readonly double[] _tla = { double.NaN, double.NaN, double.NaN, double.NaN };
    private readonly string?[] _lastEngDetent = new string?[4];
    private string? _lastAllDetent;
    private bool _tlaBaselineDone;   // suppress the startup "thrust 1/2/3/4 / all" spam
    private readonly HashSet<string> _rowRopActive = new();   // active ROW/ROP/OANS warning bits

    // BTV (Brake-To-Vacate) rollout call-outs: current BTV state (gate) and which
    // distance thresholds have already been spoken this landing (reset between).
    private int _btvState = 0;
    private readonly int[] _gpuAvail = { -1, -1, -1, -1 };   // last external-power-available state per GPU (-1 = unseen)
    private readonly HashSet<int> _btvExitSpoken = new();
    private readonly HashSet<int> _btvRwyEndSpoken = new();
    private static readonly int[] BtvExitThresholdsM   = { 1200, 800, 500, 300, 150 };
    private static readonly int[] BtvRwyEndThresholdsM = { 900, 600, 300, 150 };
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
        // ARINC429 enum guard. Several FBW discretes (e.g. APU_LOW_FUEL_PRESSURE_FAULT,
        // written `write_arinc429`) come through as a huge SSM-encoded word (e.g.
        // 12884901888 = 0x3_00000000 = SSM NormalOp, payload 0) that matches no entry
        // in the var's 0/1 ValueDescriptions, so the generic announce would say
        // "<label>: 12884901888". Decode any ANNOUNCED enum var whose raw value is
        // ARINC-large (>= 2^32 -> an SSM is present) to its payload (0/1) and announce
        // the mapped state ON CHANGE only (default prev = 0 so the initial no-fault is
        // silent). Honours the Ctrl+M mute; returns true to suppress the raw announce.
        if (value >= 4294967296.0 && _varCache != null
            && _varCache.TryGetValue(varName, out var arDef)
            && arDef.IsAnnounced
            && arDef.ValueDescriptions is { Count: > 0 } arDesc)
        {
            int st = (int)Math.Round(new SimConnect.Arinc429Word(value).ValueOr(0f));
            int prevSt = _arincEnumState.TryGetValue(varName, out var ps) ? ps : 0;
            _arincEnumState[varName] = st;
            if (st != prevSt
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName)
                && arDesc.TryGetValue(st, out var sdesc))
                announcer.Announce($"{arDef.DisplayName}: {sdesc}");
            return true;
        }
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
                // Establish the baseline SILENTLY on first start: until all four
                // levers have reported once, just record their detents and don't
                // announce — otherwise MSFSBA reads out "Thrust lever 1 Idle …
                // Thrust lever 4 Idle, All thrust levers Idle" every startup. Only
                // real detent CHANGES after the baseline are announced.
                if (!_tlaBaselineDone)
                {
                    if (det != null) _lastEngDetent[eng - 1] = det;
                    bool allSeen = true;
                    for (int i = 0; i < 4; i++) if (double.IsNaN(_tla[i])) { allSeen = false; break; }
                    if (allSeen)
                    {
                        _tlaBaselineDone = true;
                        string? d0 = TlaDetent(_tla[0]);
                        bool same = d0 != null;
                        for (int i = 1; i < 4 && same; i++) if (TlaDetent(_tla[i]) != d0) same = false;
                        _lastAllDetent = same ? d0 : null;
                    }
                    return true;
                }
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

        // Runway Overrun Warning / Protection (ROW/ROP) + OANS RWY AHEAD — decode
        // the ARINC429 discrete word and announce each safety call-out on its rising
        // edge (0->1). On the ground pre-flight every bit is 0, so this is silent at
        // baseline and only speaks the real landing/taxi warnings ("Runway too
        // short", "Max braking", "Runway ahead"). Honours the Ctrl+M mute.
        if (varName is "A32NX_ROW_ROP_WORD_1" or "A32NX_OANS_WORD_1")
        {
            var word = new SimConnect.Arinc429Word(value);
            (int bit, string phrase)[] bits = varName == "A32NX_ROW_ROP_WORD_1"
                ? new[] { (13, "Max braking"), (14, "If wet, runway too short"), (15, "Runway too short") }
                : new[] { (11, "Runway ahead") };
            bool muted = Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName);
            foreach (var (bit, phrase) in bits)
            {
                bool active = word.BitValueOr(bit, false);
                string k = varName + ":" + bit;
                if (active && _rowRopActive.Add(k)) { if (!muted) announcer.Announce(phrase); }
                else if (!active) _rowRopActive.Remove(k);
            }
            return true;
        }

        // Track the BTV state (gates the rollout distance call-outs below). Captured
        // here but NOT consumed — fall through so the Mon registration still speaks
        // the state transition. Leaving the rollout (state < 2) resets the spoken
        // thresholds so the next landing starts fresh.
        if (varName == "A32NX_BTV_STATE")
        {
            _btvState = (int)value;
            if (_btvState < 2) { _btvExitSpoken.Clear(); _btvRwyEndSpoken.Clear(); }
        }

        // BTV rollout distances: distance to the selected exit, and runway remaining.
        // Both are ARINC429 metres, valid (SSM normal) only while BTV is computing
        // the rollout. Announce as each descends through fixed thresholds (once each),
        // gated on the BTV state (Rotation Optimised / Decel) so it only speaks during
        // the actual braking rollout. Verify the live numbers on a real landing.
        if (varName is "A32NX_OANS_BTV_REMAINING_DIST_TO_EXIT" or "A32NX_OANS_BTV_REMAINING_DIST_TO_RWY_END")
        {
            bool toExit = varName.EndsWith("EXIT");
            var spoken = toExit ? _btvExitSpoken : _btvRwyEndSpoken;
            var word = new SimConnect.Arinc429Word(value);
            bool rolling = _btvState == 2 || _btvState == 3;   // Rotation Optimised / Decel
            if (!rolling || !word.IsNormalOperation) { spoken.Clear(); return true; }
            double m = word.ValueOr(0);
            if (m <= 0 || m > 9000) return true;               // out of sensible range
            bool muted = Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName);
            // Mark EVERY band at/above the current distance as spoken in one pass —
            // so if the rollout starts already below the top band (short exit/runway),
            // the bands we skipped past don't each re-fire on later frames. Announce
            // once if we newly entered any band this update.
            bool announce = false;
            foreach (int t in (toExit ? BtvExitThresholdsM : BtvRwyEndThresholdsM))
                if (m <= t && spoken.Add(t)) announce = true;
            if (announce && !muted)
            {
                int rounded = (int)(Math.Round(m / 10.0) * 10);
                announcer.Announce(toExit ? $"{rounded} meters to exit" : $"{rounded} meters runway remaining");
            }
            return true;
        }

        // External power (GPU) available — explicit edge announce so connecting/
        // disconnecting ground power (incl. via GSX) clearly speaks, rather than
        // relying on the generic indexed-simvar path. The first-detect grace mutes the
        // baseline (power already available at connect), so this only fires on a real
        // connect/disconnect after startup. Honours the Ctrl+M mute.
        if (varName.StartsWith("A380X_GND_GPU_AVAIL_", StringComparison.Ordinal)
            && int.TryParse(varName.AsSpan("A380X_GND_GPU_AVAIL_".Length), out int gpuN)
            && gpuN >= 1 && gpuN <= 4)
        {
            int now = value > 0.5 ? 1 : 0;
            if (_gpuAvail[gpuN - 1] != now)
            {
                _gpuAvail[gpuN - 1] = now;
                if (!Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                    announcer.Announce(now == 1 ? $"External Power {gpuN} available" : $"External Power {gpuN} disconnected");
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
                // De-dup by the MESSAGE SET across ALL E/WD lines, not per line.
                // A message that merely scrolls to a different line (because a
                // higher one cleared) stays in the set, so it is NOT re-announced;
                // only a message that's NEWLY present anywhere is spoken. This
                // kills the "same caution repeats as it shifts lines" spam.
                var current = new HashSet<long>();
                foreach (var kv in _lastEwdCode) if (kv.Value != 0) current.Add(kv.Value);
                bool muted = Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains("FBWA380_ECAM_MEMOS");
                foreach (var c in current)
                {
                    if (_announcedEwdCodes.Contains(c)) continue;   // already on screen
                    if (muted) continue;                            // honour Ctrl+M mute
                    // When the E/WD DOM-scrape monitor (CoherentEWDClient) is running
                    // it is the single source for the E/WD auto-call-outs (failures
                    // AND memos), so suppress this SimVar announce to avoid double
                    // speech. The dedup sets below are still maintained so the
                    // on-demand ReadAllEwdWarnings (Alt+E) decode keeps working, and
                    // if the scrape monitor is NOT active this SimVar path still
                    // announces (safe default).
                    if (EwdScrapeHandlesAnnounce) continue;
                    string text = EWDMessageLookupA380.GetMessage(c);
                    if (!string.IsNullOrWhiteSpace(text) &&
                        !text.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                    {
                        string priority = EWDMessageLookupA380.GetMessagePriority(c);
                        announcer.Announce(string.IsNullOrEmpty(priority) ? text : $"{text}, {priority}");
                    }
                }
                // Snapshot the on-screen set so a cleared message can re-announce
                // if it genuinely returns later (but a moved one does not).
                _announcedEwdCodes.Clear();
                foreach (var c in current) _announcedEwdCodes.Add(c);
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
        // Doors read the stock INTERACTIVE POINT OPEN, a 0..1 FRACTION — so a
        // half-open door is e.g. 0.6, which matches neither the Closed(0) nor
        // Open(1) value description and would otherwise read as "0.6". Announce
        // open/closed once per transition (>0.05 = cracked open). The decoded
        // state + percentage is shown in the panel via TryGetDisplayOverride.
        if (varName.StartsWith("A380X_GND_DOOR_", StringComparison.Ordinal)
            && !varName.EndsWith("_TOGGLE", StringComparison.Ordinal))
        {
            bool open = value > 0.05;
            bool? prev = _doorOpen.TryGetValue(varName, out var p) ? p : null;
            _doorOpen[varName] = open;
            if (prev.HasValue && prev.Value != open && _doorNames.TryGetValue(varName, out var dn))
                announcer.Announce($"{dn} {(open ? "open" : "closed")}");
            return true;
        }
        if (_readoutKey != null && varName == _readoutKey)
        {
            string lbl = _readoutLabel ?? varName;
            string spoken;
            if (_readoutMap != null && _readoutMap.TryGetValue(Math.Round(value), out var dsc))
                spoken = lbl + " " + dsc;
            else if (_readoutIsWeight)
            {
                // The raw var is kilograms; speak it in the pilot's selected unit.
                var (wv, wu) = WeightUser(value);
                spoken = $"{lbl} {wv:0} {wu}";
            }
            else
                spoken = string.IsNullOrEmpty(_readoutUnit) ? $"{lbl} {value:0}" : $"{lbl} {value:0} {_readoutUnit}";
            announcer.AnnounceImmediate(spoken);
            _readoutKey = null; _readoutMap = null; _readoutIsWeight = false;
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
            var (gw, gwu) = WeightUser(value);
            announcer.AnnounceImmediate($"Gross weight {gw:0} {gwu}");
            return true;
        }
        // Weight-unit selection (kg/lb) mirror of the EFB "US Units" toggle. Seed
        // MSFSBA's read-out unit from the aircraft on first read (silent); on a
        // genuine AIRCRAFT change (someone flipped it in the flyPad EFB Settings),
        // follow it and announce. The MCDU "Units" button changes _metricWeight
        // directly without touching _aircraftMetric, so it never fights this.
        if (varName == "A32NX_EFB_USING_METRIC_UNIT")
        {
            bool m = value > 0.5;
            if (!_metricKnown) { _metricKnown = true; _aircraftMetric = m; _metricWeight = m; return true; }
            if (m != _aircraftMetric)
            {
                _aircraftMetric = m; _metricWeight = m;
                announcer.Announce($"Weight units {(m ? "kilograms" : "pounds")}");
            }
            return true;
        }
        // Metric-altitude (FCU MTRS) state — cache it so every MSFSBA altitude
        // read-out switches to metres; let the generic monitor announce On/Off.
        if (varName == "A32NX_METRIC_ALT_TOGGLE") { _metricAlt = value > 0.5; return false; }

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
                var (av, au) = AltUser(_pAltVal.Value);
                announcer.AnnounceImmediate($"FCU altitude {av:0} {au}, {st}");
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
        ["LIGHT_LANDING"] = "LANDING_LIGHTS_SET",
        ["LIGHT_STROBE"] = "STROBES_SET"
    };

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // System Display PAGE combo: drive the SD to the chosen page, then read that
        // page's decoded content off the real SD view and announce it (so the SD pages
        // are usable from the panel, no separate window). The combo's own value change
        // already announces the page NAME; this adds the page CONTENT.
        if (varKey == "A32NX_ECAM_SD_CURRENT_PAGE_INDEX")
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varKey})");
            AnnounceSdPageAsync(announcer);
            return true;
        }
        // Annunciator / integral lights knob (Test / Bright / Dim) is handled by the
        // generic catch-all below: it writes the L:var and the combo's Continuous +
        // IsAnnounced monitoring speaks the position ("Test" / "Bright" / "Dim").
        // We deliberately do NOT synthesise a spoken list of lights for the TEST
        // position: the bulb test is render-only in the FBW model (live-verified —
        // setting the knob to TEST changes NO _PB_HAS_FAULT or annunciator L:var), so
        // there is nothing real to announce. The actual annunciator/fault lights are
        // the per-system _PB_HAS_FAULT vars (already registered, announce-on-change),
        // which speak when a genuine fault appears — MSFSBA announces real state, it
        // does not fabricate a bulb-check narration.
        // Chronometer start/stop + reset fire H-EVENTS (the FBW Clock listens for the
        // hEvent, not an L:var write — live-verified: the H-event advances the elapsed
        // time, an L:var write does nothing).
        if (varKey == "A32NX_CHRONO_TOGGLE" || varKey == "A32NX_CHRONO_RST")
        {
            if (value > 0.5)   // only the "Activate" option fires
            {
                simConnect.ExecuteCalculatorCode($"(>H:{varKey})");
                announcer.Announce(varKey == "A32NX_CHRONO_RST" ? "Chronometer reset" : "Chronometer start stop");
            }
            return true;
        }
        // Momentary L:var push-buttons (TEST / ident / ack / trim reset / tiller /
        // rain repellent): pulse the L:var 1→0 so the sim registers the press edge
        // rather than leaving it latched on. ~250 ms is long enough for the FWS /
        // systems to act on the rising edge, then it auto-releases.
        if (_momentaryButtons.Contains(varKey))
        {
            // Combo now (Off / Activate): only the "Activate" option fires; choosing
            // "Off" does nothing (the pulse already returned the var to 0).
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode($"1 (>L:{varKey})");
                _ = Task.Run(async () =>
                {
                    try { await Task.Delay(250); simConnect.ExecuteCalculatorCode($"0 (>L:{varKey})"); } catch { }
                });
                announcer.Announce($"{varDef.DisplayName} pressed");
            }
            return true;
        }
        if (_extLightSetEvents.TryGetValue(varKey, out var lightEvent))
        {
            simConnect.SendEvent(lightEvent, (uint)Math.Round(value));
            return true;
        }
        // Rudder Trim Reset: fire the stock K-event the cockpit uses (the L:var does
        // nothing). Only the "Reset" option (value 1) fires.
        if (varKey == "A32NX_RUDDER_TRIM_RESET")
        {
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode("(>K:RUDDER_TRIM_RESET)");
                announcer.Announce("Rudder trim reset");
            }
            return true;
        }
        // Flaps lever: the handle index is a computed output; the stock FLAPS_SET
        // event (axis value 0-16383) drives the FBW handle. Map detent 0-4 to the
        // axis value (index/4 * 16383) — live-verified each detent lands correctly.
        if (varKey == "A32NX_FLAPS_HANDLE_INDEX")
        {
            int detent = Math.Max(0, Math.Min(4, (int)Math.Round(value)));
            int axis = (int)Math.Round(detent / 4.0 * 16383.0);
            simConnect.ExecuteCalculatorCode($"{axis} (>K:FLAPS_SET)");
            return true;
        }
        // ENG GEN 1-4: combo state is the stock GENERAL ENG MASTER ALTERNATOR:n; the
        // working actuator is the stock TOGGLE_MASTER_ALTERNATOR event (engine index).
        // Toggle only when the desired state differs from the live SimVar state.
        if (varKey.StartsWith("ELEC_ENG_GEN:", StringComparison.Ordinal)
            && int.TryParse(varKey.AsSpan("ELEC_ENG_GEN:".Length), out int genN))
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("TOGGLE_MASTER_ALTERNATOR", (uint)genN);
            return true;
        }
        // APU GEN 1-2: combo state is the stock APU GENERATOR SWITCH:i; the working
        // actuator is the stock indexed APU_GENERATOR_SWITCH_SET event (direct set).
        if (varKey.StartsWith("ELEC_APU_GEN:", StringComparison.Ordinal)
            && int.TryParse(varKey.AsSpan("ELEC_APU_GEN:".Length), out int apuGenI))
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>K:{apuGenI}:APU_GENERATOR_SWITCH_SET)");
            return true;
        }
        // Taxi light: state mirrors LIGHT TAXI:2; the only working actuator is the
        // stock TOGGLE_TAXI_LIGHTS (no indexed SET reaches the shipping model), so
        // toggle only when the desired state differs from the live state.
        if (varKey == "LIGHT_TAXI_OVHD")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue("LIGHT_TAXI_OVHD") ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("TOGGLE_TAXI_LIGHTS");
            return true;
        }
        // Seat-belt sign: there is no SET event, only CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE,
        // so toggle only when the desired state differs from the current (live) state.
        if (varKey == "SEATBELT_SIGN")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue("SEATBELT_SIGN") ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE");
            return true;
        }
        // --- Combos whose STATE is a SimVar but whose CONTROL is a K-event
        // (standardised: every cockpit control is a combo box, no buttons except
        // the FCU push/pull). These route here from the SimVar-combo set path. ---
        // Engine MASTER valves: state = FUELSYSTEM VALVE SWITCH:n (1-4), control =
        // FUELSYSTEM_VALVE_OPEN/CLOSE with the valve id (verified live).
        if (varKey.StartsWith("ENG_VALVE_SWITCH:", StringComparison.Ordinal)
            && int.TryParse(varKey.AsSpan("ENG_VALVE_SWITCH:".Length), out int engVid))
        {
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_VALVE_OPEN" : "FUELSYSTEM_VALVE_CLOSE", (uint)engVid);
            return true;
        }
        // Crossfeed valves: XFEED_n_STATE -> valve id 45+n.
        if (varKey.StartsWith("XFEED_", StringComparison.Ordinal)
            && varKey.EndsWith("_STATE", StringComparison.Ordinal)
            && int.TryParse(varKey.AsSpan(6, 1), out int xfn))
        {
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_VALVE_OPEN" : "FUELSYSTEM_VALVE_CLOSE", (uint)(45 + xfn));
            return true;
        }
        // Door combos: the combo shows the live SimVar open/closed state, so
        // selecting the OTHER option means "toggle" — fire TOGGLE_AIRCRAFT_EXIT.
        if (_doorExitIds.TryGetValue(varKey, out uint doorExit))
        {
            simConnect.SendEvent("TOGGLE_AIRCRAFT_EXIT", doorExit);
            return true;
        }
        // Ground-service toggle combos (no clean state SimVar) — any change toggles.
        if (varKey == "A380X_GND_JETWAY") { simConnect.SendEvent("TOGGLE_JETWAY"); return true; }
        if (varKey == "A380X_GND_STAIRS") { simConnect.SendEvent("TOGGLE_RAMPTRUCK"); return true; }
        // Momentary ACTION combos: fire only when the action option (value 1) is
        // chosen; the idle option (0) does nothing.
        if (varKey == "XPNDR_IDENT_ON") { if (value > 0.5) simConnect.SendEvent("XPNDR_IDENT_ON"); return true; }
        if (varKey.StartsWith("COM", StringComparison.Ordinal) && varKey.EndsWith("_RADIO_SWAP", StringComparison.Ordinal))
        {
            if (value > 0.5) simConnect.SendEvent(varKey);   // key == event name
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
        // Wipers: ON/OFF by TOGGLING the electrical circuit (the FBW knob template's
        // mechanism) — only toggle when the desired state differs from the live
        // circuit-switch state, then drive a visible speed when turning on. The old
        // code set power only and never toggled the circuit on, so it never started.
        if (varKey == "WIPER_LEFT" || varKey == "WIPER_RIGHT")
        {
            int circuit = varKey == "WIPER_LEFT" ? 141 : 143;
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? 0.0) > 0.5;
            if (desiredOn != currentOn)
                simConnect.ExecuteCalculatorCode($"{circuit} (>K:ELECTRICAL_CIRCUIT_TOGGLE)");
            if (desiredOn)   // percent then circuit index (verified order)
                simConnect.ExecuteCalculatorCode($"100 {circuit} (>K:2:ELECTRICAL_CIRCUIT_POWER_SETTING_SET)");
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
        // #56 — Wing anti-ice + probe/window heat (live-verified 2026-05).
        // ENGINE anti-ice needs K:ANTI_ICE_SET_ENGn (handled above; the L:var/XMLVAR
        // don't drive it). For WING anti-ice the momentary XMLVAR_..._PRESSED pulse
        // does NOT actuate the system — write the selected-state L:var directly
        // instead (verified: 0/1 both stick and drive _SYSTEM_ON when conditions
        // allow). PROBE/WINDOW heat toggles A32NX_MAN_PITOT_HEAT (the same var the
        // cockpit button toggles); note it auto-forces ON whenever AC2 is powered or
        // an engine is running, so a "set Off" reverts — that is real A380 behaviour
        // (probe heat is automatic), and the Mon auto-announce re-reads the true
        // state. Both go through the reliable MobiFlight calculator path.
        if (varKey == "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED"
            || varKey == "A32NX_MAN_PITOT_HEAT")
        {
            simConnect.ExecuteCalculatorCode($"{(value > 0.5 ? 1 : 0)} (>L:{varKey})");
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
        // Catch-all for the remaining settable FBW overhead / system pushbutton +
        // selector L:vars (the OnOff/OffAuto/Press/Sel combos for ELEC, FUEL, HYD,
        // PNEU, COND, PRESS, VENT, anti-ice, lighting). MainForm's generic fallback
        // would SetLVar these over the SimConnect data-def, which is unreliable for
        // FBW L:vars (same as the reads) — so route them through the MobiFlight
        // calculator path, which is the established reliable write for this aircraft.
        //
        // #103 CORRECTION (live Coherent SimVar write-then-readback, 2026-05): the
        // earlier #60 verdict that PACK/HOT-AIR, ENGINE BLEED, CABIN/AIR-EXTRACT
        // FANS, the HYD ENGINE/ELEC PUMP PBs, ELEC BUS-TIE/GALLEY, HYD PTU and the
        // EMERGENCY-EXIT sign are "computed outputs that revert and cannot be set
        // externally" was WRONG. It was an artifact of testing with the MCP's native
        // data-def write (set_lvar / AddToDataDefinition), which is unreliable for
        // FBW L:vars (exactly as the READS are). The MobiFlight CALCULATOR path
        // (`{val} (>L:{var})`) — the one used below — sets ALL of them and they
        // STICK in both directions for 3+ s (re-tested live: pack OFF stayed OFF,
        // bus-tie/PTU/pumps/bleeds/fans all stuck). The Rust `OnOffFaultPushButton`
        // READS `{name}_PB_IS_ON/_IS_AUTO` as the pilot input each frame (it only
        // WRITES the *_HAS_FAULT output), so an external set IS the press. So these
        // overhead combos all actuate correctly through the calculator path — there
        // is NO hard FBW limitation here. (The only PBs that still need a stock event
        // are the seatbelt sign — handled earlier via CABIN_SEATBELTS_ALERT_SWITCH_
        // TOGGLE — and any engine anti-ice, which uses ANTI_ICE_SET_ENGn.)
        // Feed-tank fuel pumps: toggle the pump's electrical circuit only when the
        // desired state differs from the live circuit (the event is a TOGGLE).
        if (_fuelPumpCircuits.TryGetValue(varKey, out int pumpCircuit))
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn)
                simConnect.ExecuteCalculatorCode($"{pumpCircuit} 1 (>K:2:ELECTRICAL_BUS_TO_CIRCUIT_CONNECTION_TOGGLE)");
            return true;
        }
        if (varKey.StartsWith("A32NX_OVHD_", StringComparison.Ordinal)
            || varKey.StartsWith("A380X_OVHD_", StringComparison.Ordinal)
            || varKey.StartsWith("A32NX_KNOB_OVHD_", StringComparison.Ordinal)
            // The overhead sign/increment XMLVARs (No Smoking, Emergency Exit,
            // Altitude Increment) also set reliably via the calculator path — they
            // were falling through to the unreliable data-def write before, which is
            // why the Emergency Exit sign appeared to "revert". (Verified live: the
            // EMEREXIT XMLVAR stuck via the calculator path.)
            || varKey.StartsWith("XMLVAR_", StringComparison.Ordinal))
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varKey})");
            return true;
        }
        // General catch-all for every remaining writable FBW L:var combo whose KEY is
        // the L:var itself (e.g. A32NX_TRANSPONDER_MODE, A32NX_SWITCH_ATC_ALT, ND mode/
        // range, ISIS, EFIS filters). Route through the reliable MobiFlight calculator
        // path rather than the base data-def SetLVar. Event-driven controls (engine
        // masters, FCU toggles, seat-belt, lights, …) are all handled in cases above,
        // so anything reaching here is a direct-write L:var. ARINC429/readout vars are
        // never settable, so they never get here.
        if (varKey.StartsWith("A32NX_", StringComparison.Ordinal)
            || varKey.StartsWith("A380X_", StringComparison.Ordinal)
            || varKey.StartsWith("FBW_", StringComparison.Ordinal))
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varKey})");
            return true;
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
    private readonly Dictionary<string, bool> _doorOpen = new(); // last open/closed state per door key
    private static readonly Dictionary<string, string> _doorNames = new()
    {
        ["A380X_GND_DOOR_MAIN1L"] = "Main 1 Left Door",
        ["A380X_GND_DOOR_MAIN2L"] = "Main 2 Left Door",
        ["A380X_GND_DOOR_MAIN4R"] = "Main 4 Right Door",
        ["A380X_GND_DOOR_UPPER1L"] = "Upper 1 Left Door",
        ["A380X_GND_DOOR_FWDCARGO"] = "Forward Cargo Door",
    };
    // Door key -> TOGGLE_AIRCRAFT_EXIT interaction id, so the door COMBO itself
    // drives the toggle (every control is a combo; no buttons).
    private static readonly Dictionary<string, uint> _doorExitIds = new()
    {
        ["A380X_GND_DOOR_MAIN1L"] = 1,
        ["A380X_GND_DOOR_MAIN2L"] = 3,
        ["A380X_GND_DOOR_MAIN4R"] = 10,
        ["A380X_GND_DOOR_UPPER1L"] = 11,
        ["A380X_GND_DOOR_FWDCARGO"] = 17,
    };

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
        // Doors: INTERACTIVE POINT OPEN is a 0..1 fraction — show a meaningful
        // state ("Open (60%)") instead of the bare "0.6".
        if (varKey.StartsWith("A380X_GND_DOOR_", StringComparison.Ordinal)
            && !varKey.EndsWith("_TOGGLE", StringComparison.Ordinal))
        {
            displayText = value < 0.05 ? "Closed"
                        : value > 0.95 ? "Open"
                        : $"Open ({(int)Math.Round(value * 100)}%)";
            return true;
        }
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
            case "A32NX_SEC_1_RUDDER_ACTUAL_POSITION":
            {
                // ARINC429 degrees, positive = nose-Left (matches PFD/SD: sign>0 -> L).
                var w = new Arinc429Word(value);
                if (!(w.IsNormalOperation || w.IsFunctionalTest)) { displayText = "Not available"; return true; }
                double deg = w.Value;
                displayText = Math.Abs(deg) < 0.1
                    ? "Neutral"
                    : $"{(deg > 0 ? "Left" : "Right")} {Math.Abs(deg):0.0} degrees";
                return true;
            }
            // Weight panel fields — show in the pilot's selected unit (kg/lb), the
            // same choice the EFB Units toggle drives. The raw simvars are kg.
            case "GROSS_WEIGHT_KG":
            case "A32NX_TOTAL_FUEL_QUANTITY":
            {
                var (wv, wu) = WeightUser(value);
                displayText = $"{wv:0} {wu}";
                return true;
            }
            case "A32NX_EFB_USING_METRIC_UNIT":
                displayText = value > 0.5 ? "Kilograms (metric)" : "Pounds (imperial)";
                return true;
            // Altitude panel fields honour the FCU metric-altitude (MTRS) selection
            // — feet by default, metres when A32NX_METRIC_ALT_TOGGLE is on.
            case "FCU_ALT_VALUE":
            case "INDICATED ALTITUDE":
            {
                if (!_metricAlt) return false;   // feet — let the generic "N feet" render
                displayText = $"{value * 0.3048:0} meters";
                return true;
            }
            case "A32NX_APU_EGT":
            {
                // ARINC429 word -> APU exhaust gas temperature in celsius (the start
                // monitor). "No data" until the APU FADEC is powered.
                var we = new Arinc429Word(value);
                if (!(we.IsNormalOperation || we.IsFunctionalTest)) { displayText = "No data"; return true; }
                displayText = $"{we.Value:0} degrees celsius";
                return true;
            }
            case "A32NX_TO_PITCH_TRIM":
            {
                // ARINC429 degrees; the FMS-computed takeoff trim. Positive = nose
                // UP. Reads "Not computed" until the FMS has perf data.
                var w = new Arinc429Word(value);
                if (!(w.IsNormalOperation || w.IsFunctionalTest)) { displayText = "Not computed"; return true; }
                double deg = w.Value;
                displayText = Math.Abs(deg) < 0.05
                    ? "Neutral"
                    : $"{Math.Abs(deg):0.0} degrees {(deg > 0 ? "up" : "down")}";
                return true;
            }
            case "ELEVATOR_TRIM":
            {
                // Actual THS position, read in DEGREES; positive = UP (matches the
                // A380 SD PITCH TRIM block and the base Shift+T trim-announce unit).
                double deg = value;
                displayText = Math.Abs(deg) < 0.05
                    ? "Neutral"
                    : $"{Math.Abs(deg):0.0} degrees {(deg > 0 ? "up" : "down")}";
                return true;
            }
            case "A32NX_CHRONO_ELAPSED_TIME":
            {
                // The clock CHR stopwatch (FBW Clock instrument writes this in
                // SECONDS, -1 = blank/reset). Spoken as minutes + seconds so a blind
                // pilot can time an approach/hold instead of hearing raw seconds.
                if (value < 0) { displayText = "Reset"; return true; }
                int total = (int)Math.Round(value);
                int mm = total / 60, ss = total % 60;
                displayText = mm > 0 ? $"{mm} minute{(mm == 1 ? "" : "s")} {ss} second{(ss == 1 ? "" : "s")}"
                                     : $"{ss} second{(ss == 1 ? "" : "s")}";
                return true;
            }
            case "A32NX_CHRONO_ET_ELAPSED_TIME":
            {
                // The clock ET (elapsed-time) counter — SECONDS, displayed HH:MM,
                // -1 = blank. Driven by the ET knob (A32NX_CHRONO_ET_SWITCH_POS).
                if (value < 0) { displayText = "Reset"; return true; }
                int total = (int)Math.Round(value);
                int hh = total / 3600, mm = (total % 3600) / 60;
                displayText = hh > 0 ? $"{hh} hour{(hh == 1 ? "" : "s")} {mm} minute{(mm == 1 ? "" : "s")}"
                                     : $"{mm} minute{(mm == 1 ? "" : "s")}";
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
    private bool _readoutIsWeight;
    // Feed-tank fuel-pump combo key -> electrical CIRCUIT id (set via toggle).
    private readonly Dictionary<string, int> _fuelPumpCircuits = new();
    // L:var momentary push-buttons (registered via Btn) — a press pulses the L:var
    // 1→0 so the sim sees the rising edge, instead of latching it on like a combo.
    private readonly HashSet<string> _momentaryButtons = new();

    // Lazy live-scrape client for the System Display: when the user picks an SD page
    // in the ECAM Control Panel "System Display Page" combo, MSFSBA drives the page
    // index then reads that page's DECODED content off the real SD Coherent view and
    // announces it — so the SD pages are usable straight from the panel, no separate
    // window or hotkey. On-demand scrapes only (background poll paused).
    private SimConnect.CoherentDisplayClient? _sdScrapeClient;
    private Dictionary<double, string>? _readoutMap;
    private static readonly Dictionary<double, string> _apprCapMap = new Dictionary<double, string>
    { [0] = "None", [1] = "CAT 1", [2] = "CAT 2", [3] = "CAT 3 Single", [4] = "CAT 3 Dual" };

    // Weight-unit state. `_metricWeight` is MSFSBA's EFFECTIVE read-out unit (what
    // the GW/fuel read-outs use); `_aircraftMetric` tracks the aircraft's own
    // setting via the EFB L:var mirror A32NX_EFB_USING_METRIC_UNIT. They are kept
    // separate so the MCDU "Units" button (a local, instant MSFSBA preference) and
    // the live L:var monitor don't fight: the monitor only re-syncs on a genuine
    // AIRCRAFT change. Default kg (Airbus default); `_metricKnown` gates the first
    // silent sync so connecting doesn't announce a "change".
    private bool _metricWeight = true;
    private bool _aircraftMetric = true;
    private bool _metricKnown;

    /// <summary>Convert a kilograms value to the pilot's selected weight unit + spoken word.</summary>
    private (double value, string unit) WeightUser(double kg)
        => _metricWeight ? (kg, "kilograms") : (kg * 2.204625, "pounds");

    /// <summary>True if MSFSBA is currently reading weights in kilograms.</summary>
    public bool MetricWeight => _metricWeight;

    // Metric ALTITUDE state (the A380 FCU MTRS button, A32NX_METRIC_ALT_TOGGLE).
    private bool _metricAlt;
    /// <summary>True when the A380 is in metric-altitude mode (FCU MTRS): MSFSBA reads altitudes in metres.</summary>
    public bool MetricAlt => _metricAlt;
    /// <summary>Convert a feet altitude to the pilot's selected unit + spoken word (A380 metric-alt).</summary>
    private (double value, string unit) AltUser(double feet)
        => _metricAlt ? (feet * 0.3048, "meters") : (feet, "feet");

    /// <summary>
    /// Read the currently-selected SD page off the real System Display Coherent view
    /// and announce its decoded content. Called when the ECAM-CP "System Display Page"
    /// combo changes (after the page index has been driven), so the pilot can read any
    /// SD page straight from the panel. The page NAME is announced by the combo itself;
    /// this adds the CONTENT. On-demand scrape (the background poll stays paused).
    /// </summary>
    private async void AnnounceSdPageAsync(ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_sdScrapeClient == null)
            {
                _sdScrapeClient = new SimConnect.CoherentDisplayClient("A380X_SDv2");
                _sdScrapeClient.Start();
                _sdScrapeClient.SetActive(false);   // on-demand only, no 1.2 s poll
            }
            await Task.Delay(900);   // let the SD render the newly-selected page
            var rows = await _sdScrapeClient.ScrapeNowAsync();
            if (rows == null || rows.Count == 0)
            {
                announcer.Announce("System display content not available. Open the System Display window to read it.");
                return;
            }
            // Drop the on-screen UI chrome (page buttons) so the read-out is just data.
            var clean = rows.Where(r =>
            {
                string u = (r ?? "").Trim().ToUpperInvariant();
                return u.Length > 0 && u != "CLOSE" && u != "MORE" && u != "PRINT"
                       && u != "RECALL" && u != "RECALL PRINT" && u != "RECALL  PRINT";
            });
            announcer.Announce("System display. " + string.Join(". ", clean));
        }
        catch { /* scrape best-effort; the combo still set the page */ }
    }

    /// <summary>Flip MSFSBA's weight read-out unit (kg ⇄ lb) instantly; returns the new state (true = kg).</summary>
    public bool ToggleMetricWeight() { _metricWeight = !_metricWeight; _metricKnown = true; return _metricWeight; }

    private void RequestReadout(SimConnectManager s, string key, string label, string unit = "", Dictionary<double, string>? map = null, bool weight = false)
    {
        if (!s.IsConnected) return;
        _readoutKey = key; _readoutLabel = label; _readoutUnit = unit; _readoutMap = map; _readoutIsWeight = weight;
        s.RequestVariable(key, forceUpdate: true);
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // D / Shift+D — FMS flight progress (distance to destination / top of
            // descent). The A380 has no PMDG-style SDK struct and no stock SimVar for
            // these; they come from the MFD's FMS guidance controller, read live via
            // the Coherent debugger. Delegate to MainForm, which owns that client.
            case HotkeyAction.ReadDistanceToDest:
                if (parentForm is MainForm mfDest) { mfDest.AnnounceA380FlightInfo(false); return true; }
                return false;
            case HotkeyAction.ReadDistanceToTOD:
                if (parentForm is MainForm mfTod) { mfTod.AnnounceA380FlightInfo(true); return true; }
                return false;

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
            case HotkeyAction.ReadFuelQuantity: RequestReadout(simConnect, "A32NX_TOTAL_FUEL_QUANTITY", "Total fuel", "kilograms", weight: true); return true;
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
                // Upper ECAM IS the E/WD: speak ALL current memo/warning lines on
                // demand (decoded, regardless of the live-call-out mute) for quick
                // audio, AND open the live-scrape E/WD window (engine N1/EGT/FF,
                // memos and active warnings/procedures, exactly as rendered). (Alt+E)
                ReadAllEwdWarnings(announcer);
                hotkeyManager.ExitOutputHotkeyMode();
                new Forms.FBWA380.FBWA380LiveDisplayForm(announcer, "A380X_EWD", "Engine Warning Display").Show();
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
                        lines.Add(string.IsNullOrEmpty(priority) ? text : $"{text}, {priority}");
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
        // When the A380 is in metric-altitude mode (FCU MTRS / A32NX_METRIC_ALT_TOGGLE)
        // the pilot thinks + is cleared in METRES, so the typed value is metres and we
        // convert to the feet target the FCU actually selects (the real FCU is feet
        // internally; the MTRS window just displays the metric equivalent). Off = feet,
        // exactly as before. 100..49000 ft  <=>  ~30..14935 m.
        bool metric = _metricAlt;
        var validator = new Func<string, (bool, string)>(input =>
        {
            if (!double.TryParse(input, out double v)) return (false, "Invalid number format");
            double ft = metric ? v / 0.3048 : v;
            return (ft >= 100 && ft <= 49000)
                ? (true, "")
                : (false, metric ? "Altitude must be between 30 and 14935 metres"
                                 : "Altitude must be between 100 and 49000 feet");
        });
        var dialog = new Forms.ValueInputForm("Set Altitude", "Altitude",
            metric ? "30-14935 metres" : "100-49000 feet", announcer, validator);
        if (dialog.ShowDialog(parentForm) == System.Windows.Forms.DialogResult.OK && dialog.IsValidInput
            && double.TryParse(dialog.InputValue, out double value))
        {
            double feet = metric ? value / 0.3048 : value;
            uint rounded = (uint)(Math.Round(feet / 100) * 100);
            simConnect.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", 100);
            System.Threading.Thread.Sleep(50);
            simConnect.SendEvent("A32NX.FCU_ALT_SET", rounded);
            if (metric)
            {
                // Echo what the FCU metric window will actually show: the feet target
                // (rounded to 100) converted back to metres, plus the feet, so the
                // pilot hears the achieved value rather than the raw request.
                int m = (int)Math.Round(rounded * 0.3048);
                announcer.AnnounceImmediate($"Altitude set to {m} metres, {rounded} feet");
            }
            else
            {
                announcer.AnnounceImmediate($"Altitude set to {rounded} feet");
            }
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
