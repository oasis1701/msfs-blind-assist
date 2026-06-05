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
/// FBWA380MCDUForm / FbwEfbForm.
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
        // CACHE: the variable DEFINITIONS are static, but this method rebuilt the whole
        // ~400-var dictionary (every Sel/Stock/Mon + the SD-register loops) on EVERY call
        // — and the panel-build loop calls GetVariables() twice per control, so an A380
        // overhead subpanel rebuilt the dict ~30× per switch. That was the subpanel lag.
        // Build once, then return the cached instance (also reused by ProcessSimVarUpdate).
        if (_varCache != null) return _varCache;
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
                // Rendered as a real push-BUTTON (user request 2026-06: the ECAM-CP and
                // other momentary actions are buttons, not combos). Clicking it routes
                // through HandleUIVariableSet's _momentaryButtons path, which pulses the
                // L:var 1→0 and speaks "<name> pressed".
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" },
                RenderAsButton = true
            };
            _momentaryButtons.Add(key);
        }
        // Push-BUTTON whose click is handled by a DEDICATED HandleUIVariableSet branch (fires a
        // stock K-event etc.) rather than the generic _momentaryButtons L:var pulse. Used for
        // synthetic action keys (ground services) that have no backing L:var to pulse — NOT
        // added to _momentaryButtons so HandleUIVariableSet falls through to their event branch.
        void EvtBtn(string key, string display)
        {
            vars[key] = new SimVarDefinition
            {
                Name = key, DisplayName = display, Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" },
                RenderAsButton = true
            };
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
                // The ECAM-CP keys (CLR/RCL/STS/ALL/…) are real push-BUTTONS (user request
                // 2026-06). The click routes through _momentaryButtons → pulse 1→0 + speak
                // "<name> pressed". MSFSBA's own ECL/SD code pulses these via the calculator
                // path directly (not HandleUIVariableSet), so those internal pulses stay silent.
                RenderAsButton = true
            };
            _momentaryButtons.Add(key);
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
            // Momentary disconnect PUSH (FBW <MOMENTARY/>; the LATCHED result is the
            // separate "IDG n Disconnected" readout, A32NX_OVHD_ELEC_IDG_n_PB_IS_DISC).
            // A button (pulse 1→0) is the correct shape — a Released/Pressed combo never
            // settles since this var is just the press edge.
            Btn($"A32NX_OVHD_ELEC_IDG_{n}_PB_IS_RELEASED", $"IDG {n} Disconnect");
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
        // Momentary RAT/emer-gen manual-deploy PUSH (FBW <MOMENTARY/>) — a button, not a
        // Released/Pressed combo. (Marked Inop in the current FBW build, but the render
        // shape is now correct.)
        Btn("A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED", "RAT and Emergency Generator Deploy");
        // Emergency electrical power GEN TEST pushbutton (momentary self-test).
        Btn("A32NX_EMERELECPWR_GEN_TEST", "Emergency Generator Test");
        Sel("A380X_OVHD_ELEC_BAT_SELECTOR_KNOB", "Battery Display Selector",
            new Dictionary<double, string> { [0] = "ESS", [1] = "APU", [2] = "Off", [3] = "Battery 1", [4] = "Battery 2" });
        for (int n = 1; n <= 4; n++) Read($"A32NX_ELEC_BAT_{n}_POTENTIAL", $"Battery {n} Voltage", "volts");

        // ---- RESET (computer-reset panel, overhead behind the captain) ----
        // 10 latching reset pushbuttons (FMC A/B/C, FWS 1/2, AESU 1/2, NSS AVNCS / FLT
        // OPS, ARPT NAV) — used to reset a faulted computer. Plain A32NX_ L:vars,
        // calc-write (live-verified settable). 0 = normal (in), 1 = reset (held out).
        var resetVd = new Dictionary<double, string> { [0] = "Normal", [1] = "Reset" };
        foreach (var (rk, rn) in _resetPanelVars)
            Sel(rk, rn, resetVd);

        // ---- APU ----
        OnOff("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "APU Master Switch");
        // APU auto-exit TEST pushbutton (momentary maintenance self-test).
        Btn("A32NX_APU_AUTOEXITING_TEST_ON", "APU Auto Exit Test");
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
        // Momentary RAT manual-deploy PUSH (FBW <MOMENTARY/>, tooltip "Deploys ram air
        // turbine") — a button, not a Released/Pressed combo.
        Btn("A32NX_OVHD_HYD_RAT_MAN_ON_IS_PRESSED", "RAT Manual On");
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

        // ---- CABIN LIGHTING (passenger-cabin brightness) ----
        // App-side panel controls because the flyPad rc-slider can't be set through the
        // injected agent (Coherent blocks the agent's SimVar WRITES — see CLAUDE.md flyPad
        // note). Written via the reliable calc path in HandleUIVariableSet. Manual
        // brightness 0-100% (the numeric _SET box); Auto-Brightness Off/On combo; the live
        // auto value is a read-only display.
        vars["CABIN_BRIGHTNESS_SET"] = new SimVarDefinition
        {
            Name = "A32NX_CABIN_MANUAL_BRIGHTNESS", DisplayName = "Cabin Brightness (0-100 percent)",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
        };
        vars["A32NX_CABIN_USING_AUTOBRIGHTNESS"] = new SimVarDefinition
        {
            Name = "A32NX_CABIN_USING_AUTOBRIGHTNESS", DisplayName = "Cabin Auto Brightness",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };
        Read("A32NX_CABIN_AUTOBRIGHTNESS", "Cabin Auto Brightness Value", "number");

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
        // Fire-extinguisher AGENT discharge pushbuttons (momentary). After pulling a fire
        // handle, these discharge the bottle into the affected engine/APU — completing the
        // fire drill the handles begin. Each engine has 2 agents; the APU has 1. Source:
        // behaviour/overhead/fire.xml FBW_Airbus_FIRE_AGENT (momentary 1 -> 0). The squib
        // armed/discharged states are already read in d["Fire"]. (Not live-fired in test —
        // pressing actually discharges the bottle; registration follows the proven Press path.)
        for (int n = 1; n <= 4; n++)
        {
            Press($"A32NX_OVHD_FIRE_AGENT_1_ENG_{n}_IS_PRESSED", $"Engine {n} Agent 1 Discharge");
            Press($"A32NX_OVHD_FIRE_AGENT_2_ENG_{n}_IS_PRESSED", $"Engine {n} Agent 2 Discharge");
        }
        Press("A32NX_OVHD_FIRE_AGENT_1_APU_1_IS_PRESSED", "APU Agent Discharge");
        // Fire Test + Cargo Smoke Detection Test are HOLD on/off tests — they STAY combos
        // (Off/On): the user picks On (test runs, the EWD speaks the result), then Off, and
        // the combo always shows the current state. Only true one-shot momentary actions are
        // buttons; a toggle belongs in a combo (the value is visible + always correct).
        OnOff("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", "Fire Test");
        OnOff("A32NX_FIRE_TEST_CARGO", "Cargo Smoke Detection Test");

        // ---- OXYGEN ----
        Sel("PUSH_OVHD_OXYGEN_CREW", "Crew Oxygen",
            new Dictionary<double, string> { [0] = "Auto", [1] = "Off" });
        OnOff("A32NX_OXYGEN_MASKS_DEPLOYED", "Passenger Masks Deployed");
        ReadEnum("A32NX_OXYGEN_PASSENGER_LIGHT_ON", "Passenger Oxygen Light", onOff);
        Btn("A32NX_OXYGEN_TMR_RESET", "Oxygen Timer Reset");   // momentary push-button (was a combo)

        // ---- CALLS / EVAC ----
        OnOff("A32NX_CALLS_EMER_ON", "Emergency Call");
        OnOff("A32NX_EVAC_COMMAND_TOGGLE", "Evacuation Command");
        Sel("A32NX_EVAC_CAPT_TOGGLE", "Evacuation Capt / Purser",
            new Dictionary<double, string> { [0] = "Purser", [1] = "Capt and Purser" });
        // ---- WINDOWS + side sunshades — continuous 0..1 SLIDE axes, exposed as accessible
        // sliders (the cockpit "sliding window" + the side sunshade are drag positions, live-
        // verified to hold a fractional value, e.g. 0.5). Written via the prefix-less calc-path
        // HandleUIVariableSet branch, which now preserves the fraction. Opening the window is
        // pressure-gated in the sim (>=1.2 psi cabin delta snaps it shut), so realistically only
        // on the ground / depressurised. (Slider() is defined below; C# hoists local functions.)
        Slider("CPT_SLIDING_WINDOW", "Captain Sliding Window", 0, 1);
        Slider("FO_SLIDING_WINDOW", "First Officer Sliding Window", 0, 1);
        Slider("SUNSHADE_CPT_OPENING", "Captain Sunshade", 0, 1);
        Slider("SUNSHADE_FO_OPENING", "First Officer Sunshade", 0, 1);
        // ---- COCKPIT comfort/misc openables (interactive-parts.xml bool toggles, prefix-
        // less L:vars -> calc-path write branch). Low day-to-day value but exposed per user
        // request. The 0..100 drag-axis items (seats, armrests, forward visors) are skipped.
        var closedOpenC = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" };
        var stowedDeployed = new Dictionary<double, string> { [0] = "Stowed", [1] = "Deployed" };
        Sel("CPT_OXY_FWD_OPENING", "Captain Oxygen Panel", closedOpenC);
        Sel("AFT_OXY_OPENING", "Aft Oxygen Panel", closedOpenC);
        Sel("A380_CPT_TABLE", "Captain Table", stowedDeployed);
        Sel("A380_FO_TABLE", "First Officer Table", stowedDeployed);
        Sel("A380_CPT_FOOTREST", "Captain Footrest", stowedDeployed);
        Sel("A380_FO_FOOTREST", "First Officer Footrest", stowedDeployed);
        Sel("A380_LGPIN_DOOR", "Landing Gear Pins Compartment", closedOpenC);
        // ---- COCKPIT axis controls as ACCESSIBLE SLIDERS (the 0..100 drag-axis items that used
        // to be skipped — seats, forward/aft windshield sunshades). A WinForms TrackBar that
        // writes the L-var live as the user arrows/drags it (live-verified the writes stick:
        // SUNSHADE_FWD_LH/CTR, AFT_LH_SUNSHADE_OPENING, SEAT_CPT_MOVE_FWD_AFT all held at 30/50).
        void Slider(string key, string display, double min = 0, double max = 100) => vars[key] = new SimVarDefinition
        {
            Name = key, DisplayName = display, Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsSlider = true, SliderMin = min, SliderMax = max
        };
        // Crew SEAT axes are a START/STOP MOTOR, driven by TOGGLE BUTTONS (one per direction:
        // up / down / forward / aft, per pilot). PRESS to start moving that way, press AGAIN to
        // stop (and hear the position); pressing the opposite direction reverses. The seat motor
        // SOUND is a Wwise Continuous loop keyed to the DERIVATIVE of the position L:var, so it
        // only sustains while the var is actively changing — the motor drive (SeatMotorTick) writes
        // a UNIQUE clamped read-modify-write each 20 ms tick (anti MobiFlight-dedup). Buttons (not a
        // synthetic combo) avoid the combo's periodic read-back snapping to "Stopped" and re-firing
        // — which caused the start/stop/start + spurious position read-out. Synthetic SEATBTN_* keys
        // (OnRequest, RenderAsButton) -> ToggleSeatMotor in HandleUIVariableSet.
        void SeatBtn(string key, string display) => vars[key] = new SimVarDefinition
        {
            Name = key, DisplayName = display, Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, RenderAsButton = true
        };
        // The real 0..100 position vars, registered read-only so the motor can seed its tracked
        // position from a fresh cache (force-read on start/stop) for an accurate spoken read-out.
        void SeatPos(string key, string display) => vars[key] = new SimVarDefinition
        {
            Name = key, DisplayName = display, Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false
        };
        Slider("SUNSHADE_FWD_LH", "Forward sunshade left");
        Slider("SUNSHADE_FWD_CTR", "Forward sunshade centre");
        Slider("SUNSHADE_FWD_RH", "Forward sunshade right");
        Slider("AFT_LH_SUNSHADE_OPENING", "Aft sunshade left");
        Slider("AFT_RH_SUNSHADE_OPENING", "Aft sunshade right");
        SeatBtn("SEATBTN_CPT_UP", "Captain seat up");
        SeatBtn("SEATBTN_CPT_DOWN", "Captain seat down");
        SeatBtn("SEATBTN_CPT_FWD", "Captain seat forward");
        SeatBtn("SEATBTN_CPT_AFT", "Captain seat aft");
        SeatBtn("SEATBTN_FO_UP", "First officer seat up");
        SeatBtn("SEATBTN_FO_DOWN", "First officer seat down");
        SeatBtn("SEATBTN_FO_FWD", "First officer seat forward");
        SeatBtn("SEATBTN_FO_AFT", "First officer seat aft");
        SeatPos("SEAT_CPT_MOVE_UP_DOWN", "Captain seat up/down position");
        SeatPos("SEAT_CPT_MOVE_FWD_AFT", "Captain seat forward/aft position");
        SeatPos("SEAT_FO_MOVE_UP_DOWN", "First officer seat up/down position");
        SeatPos("SEAT_FO_MOVE_FWD_AFT", "First officer seat forward/aft position");
        // Armrests (big = up/down + tilt; small = fwd) + forward windshield visors — all 0..100
        // drag axes, live-verified writable (held at 40/50).
        Slider("BIGARMREST_CPT_UP_DOWN", "Captain armrest up/down");
        Slider("BIGARMREST_CPT_TILT", "Captain armrest tilt");
        Slider("SMALLARMREST_CPT_FWD", "Captain small armrest forward");
        Slider("BIGARMREST_FO_UP_DOWN", "First officer armrest up/down");
        Slider("BIGARMREST_FO_TILT", "First officer armrest tilt");
        Slider("SMALLARMREST_FO_FWD", "First officer small armrest forward");
        Slider("CPT_SMALL_SHADE", "Captain forward visor");
        Slider("FO_SMALL_SHADE", "First officer forward visor");
        // Speed-brake handle as a FINE slider (0-100% of full deflection) alongside the
        // Retract/Half/Full combo — written via the stock SPOILERS_SET event (0-16383),
        // handled in HandleUIVariableSet.
        Slider("A380X_MSFSBA_SPEEDBRAKE_SLIDER", "Speed Brake (fine)", 0, 16383);
        // Cabin Ready is registered ONCE, read-only, further down via Mon (auto-announce on
        // change). The earlier settable Sel here was a duplicate of the same L-var — removed so
        // the read-only Mon is the sole registration and the auto-announce path is unambiguous.
        // (It reads 1 after a ready-for-flight preset; the announce only fires on a live 0->1
        // change, not on the instant preset set during first-aircraft-detect suppression.)
        // Cabin-crew / ground-mechanic call push-buttons (momentary). Buttons that pulse.
        Btn("PUSH_OVHD_CALLS_ALL", "Call All Stations");
        Btn("PUSH_OVHD_CALLS_FWD", "Call Forward");
        Btn("PUSH_OVHD_CALLS_AFT", "Call Aft");
        Btn("PUSH_OVHD_CALLS_MECH", "Call Mechanic");

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
        // hEvent, NOT the L:var — writing the L:var does nothing). Rendered as push-
        // BUTTONS (RenderAsButton=true); the dedicated HandleUIVariableSet branch fires
        // (>H:VAR) on press (checked before the generic _momentaryButtons pulse path, so
        // the H-event mechanism runs instead of a no-op L:var pulse).
        vars["A32NX_CHRONO_TOGGLE"] = new SimVarDefinition
        { Name = "A32NX_CHRONO_TOGGLE", DisplayName = "Chronometer Start / Stop", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }, RenderAsButton = true };
        vars["A32NX_CHRONO_RST"] = new SimVarDefinition
        { Name = "A32NX_CHRONO_RST", DisplayName = "Chronometer Reset", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }, RenderAsButton = true };

        // ---- AUDIO CONTROL PANEL (ACP) — receive selectors, RMP 1 ----
        // Which sources the captain hears (#107 transcript: "ensure VHF1 + cabin
        // interphone selected"). Settable L:vars via the calculator catch-all.
        OnOff("A380X_RMP_1_VHF_VOL_RX_SWITCH_1", "VHF 1 Receive");
        OnOff("A380X_RMP_1_VHF_VOL_RX_SWITCH_2", "VHF 2 Receive");
        OnOff("A380X_RMP_1_VHF_VOL_RX_SWITCH_3", "VHF 3 Receive");
        // VHF receive VOLUME levels (0-100 sliders) — settable via the calc path (live-verified
        // A380X_RMP_1_VHF_VOL_1 holds 40/80). Lets a blind pilot balance the radios: VHF1 (ATC)
        // loud, the rest quieter. The HF/TEL/CAB/INT/PA/NAV volume knobs exist too but are niche.
        Slider("A380X_RMP_1_VHF_VOL_1", "VHF 1 Volume");
        Slider("A380X_RMP_1_VHF_VOL_2", "VHF 2 Volume");
        Slider("A380X_RMP_1_VHF_VOL_3", "VHF 3 Volume");
        // The rest of the ACP receive-volume knobs (all settable 0-100 sliders, same calc path).
        // Niche for a sim pilot (HF/TEL/cabin comms) but exposed for completeness; NAV volume is
        // the navaid (VOR/ILS) ident audio level. Plus the RMP screen brightness knob.
        Slider("A380X_RMP_1_HF_VOL_1", "HF 1 Volume");
        Slider("A380X_RMP_1_HF_VOL_2", "HF 2 Volume");
        Slider("A380X_RMP_1_TEL_VOL_1", "Telephone Volume");
        Slider("A380X_RMP_1_CAB_VOL", "Cabin Interphone Volume");
        Slider("A380X_RMP_1_INT_VOL", "Interphone Volume");
        Slider("A380X_RMP_1_PA_VOL", "PA Volume");
        Slider("A380X_RMP_1_NAV_VOL", "Navaid Volume");
        Slider("A380X_RMP_1_BRIGHTNESS_KNOB", "RMP Screen Brightness");
        OnOff("A380X_RMP_1_HF_VOL_RX_SWITCH_1", "HF 1 Receive");
        OnOff("A380X_RMP_1_HF_VOL_RX_SWITCH_2", "HF 2 Receive");
        OnOff("A380X_RMP_1_TEL_VOL_RX_SWITCH_1", "TEL 1 Receive");
        OnOff("A380X_RMP_1_TEL_VOL_RX_SWITCH_2", "TEL 2 Receive");
        OnOff("A380X_RMP_1_CAB_VOL_RX_SWITCH", "Cabin Interphone Receive");
        OnOff("A380X_RMP_1_INT_VOL_RX_SWITCH", "Interphone Receive");
        OnOff("A380X_RMP_1_PA_VOL_RX_SWITCH", "PA Receive");
        OnOff("A380X_RMP_1_NAV_VOL_RX_SWITCH", "Navaid Receive");
        // Transmit (which radio the PTT keys) — the captain's TX select. All channels
        // live-verified ({CHANNEL}_TX_n). The real RMP transmit is mutually exclusive
        // (radio-button); modelled here as independent On/Off combos like the verified
        // VHF_TX_1, so the pilot can select/clear each channel's transmit.
        OnOff("A380X_RMP_1_VHF_TX_1", "VHF 1 Transmit");
        OnOff("A380X_RMP_1_VHF_TX_2", "VHF 2 Transmit");
        OnOff("A380X_RMP_1_VHF_TX_3", "VHF 3 Transmit");
        OnOff("A380X_RMP_1_HF_TX_1", "HF 1 Transmit");
        OnOff("A380X_RMP_1_HF_TX_2", "HF 2 Transmit");
        OnOff("A380X_RMP_1_TEL_TX_1", "TEL 1 Transmit");
        OnOff("A380X_RMP_1_TEL_TX_2", "TEL 2 Transmit");
        OnOff("A380X_RMP_1_INT_TX_1", "Interphone Transmit");
        OnOff("A380X_RMP_1_CAB_TX_1", "Cabin Interphone Transmit");
        OnOff("A380X_RMP_1_PA_TX_1", "PA Transmit");
        OnOff("A380X_RMP_1_NAV_TX_1", "Navaid Transmit");

        // ---- AUDIO CONTROL PANEL — First Officer (RMP 2), captain/F-O split ----
        // The RMP is identical hardware per seat; all RMP-2 switches live-verified to
        // exist. Same receive selectors + transmit as the captain side.
        Slider("A380X_RMP_2_VHF_VOL_1", "VHF 1 Volume");
        Slider("A380X_RMP_2_VHF_VOL_2", "VHF 2 Volume");
        Slider("A380X_RMP_2_VHF_VOL_3", "VHF 3 Volume");
        Slider("A380X_RMP_2_HF_VOL_1", "HF 1 Volume");
        Slider("A380X_RMP_2_HF_VOL_2", "HF 2 Volume");
        Slider("A380X_RMP_2_TEL_VOL_1", "Telephone Volume");
        Slider("A380X_RMP_2_CAB_VOL", "Cabin Interphone Volume");
        Slider("A380X_RMP_2_INT_VOL", "Interphone Volume");
        Slider("A380X_RMP_2_PA_VOL", "PA Volume");
        Slider("A380X_RMP_2_NAV_VOL", "Navaid Volume");
        Slider("A380X_RMP_2_BRIGHTNESS_KNOB", "RMP Screen Brightness");
        OnOff("A380X_RMP_2_VHF_VOL_RX_SWITCH_1", "VHF 1 Receive");
        OnOff("A380X_RMP_2_VHF_VOL_RX_SWITCH_2", "VHF 2 Receive");
        OnOff("A380X_RMP_2_VHF_VOL_RX_SWITCH_3", "VHF 3 Receive");
        OnOff("A380X_RMP_2_HF_VOL_RX_SWITCH_1", "HF 1 Receive");
        OnOff("A380X_RMP_2_HF_VOL_RX_SWITCH_2", "HF 2 Receive");
        OnOff("A380X_RMP_2_TEL_VOL_RX_SWITCH_1", "TEL 1 Receive");
        OnOff("A380X_RMP_2_TEL_VOL_RX_SWITCH_2", "TEL 2 Receive");
        OnOff("A380X_RMP_2_CAB_VOL_RX_SWITCH", "Cabin Interphone Receive");
        OnOff("A380X_RMP_2_INT_VOL_RX_SWITCH", "Interphone Receive");
        OnOff("A380X_RMP_2_PA_VOL_RX_SWITCH", "PA Receive");
        OnOff("A380X_RMP_2_NAV_VOL_RX_SWITCH", "Navaid Receive");
        OnOff("A380X_RMP_2_VHF_TX_1", "VHF 1 Transmit");
        OnOff("A380X_RMP_2_VHF_TX_2", "VHF 2 Transmit");
        OnOff("A380X_RMP_2_VHF_TX_3", "VHF 3 Transmit");
        OnOff("A380X_RMP_2_HF_TX_1", "HF 1 Transmit");
        OnOff("A380X_RMP_2_HF_TX_2", "HF 2 Transmit");
        OnOff("A380X_RMP_2_TEL_TX_1", "TEL 1 Transmit");
        OnOff("A380X_RMP_2_TEL_TX_2", "TEL 2 Transmit");
        OnOff("A380X_RMP_2_INT_TX_1", "Interphone Transmit");
        OnOff("A380X_RMP_2_CAB_TX_1", "Cabin Interphone Transmit");
        OnOff("A380X_RMP_2_PA_TX_1", "PA Transmit");
        OnOff("A380X_RMP_2_NAV_TX_1", "Navaid Transmit");

        // ---- RADIO MANAGEMENT PANEL (RMP) ----
        // The A380 RMP (VHF + transponder tuning — the only two pages the FBW dev build models)
        // is operated through the dedicated accessible RMP WINDOW (Ctrl+Shift+R in input mode →
        // FBWA380RmpForm), NOT a control panel: it scrapes the live A380X_RMP_1/2 Coherent views
        // and drives the cockpit keypad H-events (RMP_n_<KEY>_PRESSED/_RELEASED) via SendRmpKey.
        // The old per-key button panel was impractical (one digit at a time) and has been removed.

        // ---- INTERIOR LIGHTING ----
        Sel("A380X_OVHD_ANN_LT_POSITION", "Annunciator Lights",
            new Dictionary<double, string> { [0] = "Test", [1] = "Bright", [2] = "Dim" });
        Sel("A32NX_OVHD_INTLT_ANN", "Integral Lights",
            new Dictionary<double, string> { [0] = "Test", [1] = "Bright", [2] = "Dim" });
        // Cockpit lighting preset load / save (the only cockpit-side light control on this
        // build — the individual dome/flood knobs are not modelled as L:vars; lighting is
        // otherwise flyPad-preset driven). Pulsing the L:var with the preset id triggers it;
        // here the buttons load/save preset 1 as a momentary action.
        Btn("A32NX_LIGHTING_PRESET_LOAD", "Load Lighting Preset");
        Btn("A32NX_LIGHTING_PRESET_SAVE", "Save Lighting Preset");

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
        // Momentary acknowledge BUTTONS (pulse 1→0 = a press) — clears the master
        // warning/caution + cancels the aural. NOTE the FBW var is MASTER**A**WARN
        // (an extra A — a typo in the FBW glareshield.xml HOLD_SIMVAR; verified live:
        // pulsing PUSH_AUTOPILOT_MASTERAWARN_L/R clears A32NX_MASTER_WARNING, while the
        // no-A name does nothing). Caution is spelled normally.
        Btn("PUSH_AUTOPILOT_MASTERAWARN_L", "Master Warning Acknowledge (Capt)");
        Btn("PUSH_AUTOPILOT_MASTERAWARN_R", "Master Warning Acknowledge (F/O)");
        Btn("PUSH_AUTOPILOT_MASTERCAUT_L", "Master Caution Acknowledge (Capt)");
        Btn("PUSH_AUTOPILOT_MASTERCAUT_R", "Master Caution Acknowledge (F/O)");
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
        // Gravity-extension guards — ALL three must be lifted before the gravity lever
        // above can reach DOWN (the FBW CODE_POS_2_VERIF gates the Down position on
        // master + guard 1 + guard 2). Plain 0/1 L:vars, calc-write (live-verified
        // settable). The master guard was previously a read-only readout.
        var guardSw = new Dictionary<double, string> { [0] = "Stowed", [1] = "Lifted" };
        Sel("A32NX_LG_GRVTY_MASTER_SWITCH_GUARD", "Gravity Extension Master Guard", guardSw);
        Sel("A32NX_LG_GRVTY_SWITCH_GUARD_1", "Gravity Extension Guard Left", guardSw);
        Sel("A32NX_LG_GRVTY_SWITCH_GUARD_2", "Gravity Extension Guard Right", guardSw);

        // ---- Autobrake / Anti-skid ----
        Sel("A32NX_AUTOBRAKES_SELECTED_MODE", "Autobrake",
            new Dictionary<double, string> { [0] = "Disarm", [1] = "BTV", [2] = "Low", [3] = "L2", [4] = "L3", [5] = "High" });
        // Momentary RTO-arm PUSH (FBW option_button_press, auto-resets to 0 after a 1.5 s
        // debounce; the ARMED state is the separate "RTO Armed" readout). A button (pulse
        // 1→0) is correct — the old Released/Pressed combo never held "Pressed".
        Btn("A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED", "RTO Autobrake Arm");
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
        // Speed brake: the handle position is a 0..1 computed output, now auto-announced
        // by band via ProcessSimVarUpdate (it had no control + no announce before). The
        // SETTABLE control is a synthetic combo that drives the stock SPOILERS_SET event
        // (Retract 0 / Half 8192 / Full 16383), mirroring the flaps Sel->FLAPS_SET
        // pattern; ground-spoiler ARM is a synthetic Disarm/Arm combo driving
        // SPOILERS_ARM_OFF/ON. (Speculative — added without a live A380 to verify; the
        // events are stock MSFS and the pattern matches the verified flaps lever.)
        MonNum("A32NX_SPOILERS_HANDLE_POSITION", "Speed Brake Handle");
        Act("A380X_MSFSBA_SPEEDBRAKE", "Speed Brake",
            new Dictionary<double, string> { [0] = "Retracted", [1] = "Half", [2] = "Full" });
        ReadEnum("A32NX_SPOILERS_ARMED", "Ground Spoilers", new Dictionary<double, string> { [0] = "Disarmed", [1] = "Armed" });
        Act("A380X_MSFSBA_SPOILERS_ARM", "Ground Spoilers Arm",
            new Dictionary<double, string> { [0] = "Disarm", [1] = "Arm" });
        OnOff("A32NX_PARK_BRAKE_LEVER_POS", "Parking Brake");

        // On-demand readout sources for global hotkeys (not paneled, not announced).
        Stock("KOHLSMAN_HG", "KOHLSMAN SETTING HG", "Altimeter", "inHg");
        // PFD display gross weight. KEY is "PFD_GROSS_WEIGHT" (NOT "GROSS_WEIGHT_KG") on purpose:
        // MainForm's special-announce list speaks "GROSS_WEIGHT_KG" on every SimVarUpdated, so a
        // status-box force-read of that key auto-announced gross weight every refresh. A distinct
        // display key avoids the collision; the W/Shift+W hotkeys read the separate GW_KG_CACHE.
        Stock("PFD_GROSS_WEIGHT", "TOTAL WEIGHT", "Gross Weight", "kilograms");
        // PFD read-out additions (source-confirmed missing). All OnRequest stock simvars
        // (NOT continuous) so they add nothing to the monitoring batch / aircraft detection.
        Stock("PFD_V1", "AIRLINER V1 SPEED", "V1", "knots");
        Stock("PFD_VR", "AIRLINER VR SPEED", "VR", "knots");
        Stock("PFD_V2", "AIRLINER V2 SPEED", "V2", "knots");
        Stock("PFD_MACH", "AIRSPEED MACH", "Mach", "mach");          // decoded in TryGetDisplayOverride
        Stock("PFD_TRACK", "GPS GROUND MAGNETIC TRACK", "Track", "degrees");
        Stock("PFD_ILS_FREQ", "NAV ACTIVE FREQUENCY:3", "ILS Frequency", "MHz");
        Stock("PFD_ILS_DME", "NAV DME:3", "ILS DME", "nautical miles");
        // Speed-tape protection bugs (FAC ARINC429 words, knots). OnRequest; auto-decoded by
        // the generic ARINC path (raw ~14-billion when the FAC isn't computing -> "not
        // available"). Local helper keeps the SSM-gated decode + knots unit consistent.
        void ArincKt(string key, string name, string display) => vars[key] = new SimVarDefinition
        {
            Name = name, DisplayName = display, Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "knots", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        };
        // WEIGHT/CONFIG-BASED speeds (VLS, VMAX, green-dot, F, S) — source from the FBW
        // managed-speed L-vars `A32NX_SPEEDS_*`, which compute continuously and have real
        // values on the GROUND as well as in flight (live-verified VLS 147, VMAX 222, GD 211,
        // F 159, S 191 cold on the ground). The FAC ARINC words `A32NX_FAC_1_V_*` were NCD on
        // the ground → "not available"; the SPEEDS_* set fixes that and matches the PFD tape in
        // flight. Plain numeric L-vars (knots); formatted in TryGetDisplayOverride.
        void SpdLvar(string key, string name, string display) => vars[key] = new SimVarDefinition
        {
            Name = name, DisplayName = display, Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest, Units = "knots"
        };
        SpdLvar("PFD_VMAX", "A32NX_SPEEDS_VMAX", "Vmax");
        SpdLvar("PFD_VLS", "A32NX_SPEEDS_VLS", "VLS lowest selectable speed");
        SpdLvar("PFD_GREENDOT", "A32NX_SPEEDS_GD", "Green dot speed");
        SpdLvar("PFD_V3", "A32NX_SPEEDS_F", "F speed (slat retract)");
        SpdLvar("PFD_V4", "A32NX_SPEEDS_S", "S speed (flap retract)");
        // PROTECTION speeds (alpha-prot, alpha-max, stall-warn, VFE-next) — genuinely
        // in-flight-only: the FAC computes them only with live AoA/airspeed, so they read
        // "not available" on the ground (NCD) and that is correct + unavoidable (no managed-
        // speed equivalent exists). FAC1 ARINC429 words, knots.
        ArincKt("PFD_VALPHAPROT", "A32NX_FAC_1_V_ALPHA_PROT", "Alpha Prot speed");
        ArincKt("PFD_VALPHAMAX", "A32NX_FAC_1_V_ALPHA_LIM", "Alpha Max speed");
        ArincKt("PFD_VSW", "A32NX_FAC_1_V_STALL_WARN", "Stall Warning speed");
        ArincKt("PFD_VFENEXT", "A32NX_FAC_1_V_FE_NEXT", "VFE next");
        // ARINC429 words in non-knots units (RA, vertical speed, transition altitude).
        // Same SSM-gated decode as ArincKt; live-verified RA1 + VS read Normal Operation.
        void ArincUnit(string key, string name, string display, string unit) => vars[key] = new SimVarDefinition
        {
            Name = name, DisplayName = display, Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = unit, Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        };
        ArincUnit("PFD_RA", "A32NX_RA_1_RADIO_ALTITUDE", "Radio altitude", "feet");
        ArincUnit("PFD_VS", "A32NX_ADIRS_IR_1_VERTICAL_SPEED", "Vertical speed", "feet per minute");
        ArincUnit("PFD_TRANS_ALT", "A32NX_FM1_TRANS_ALT", "Transition altitude", "feet");
        // Transition LEVEL (descent) — ARINC429 word, engineering value already in FL hundreds
        // (e.g. 60 = FL060). Decoded to "flight level N" / "not set" in TryGetDisplayOverride
        // (NOT IsArinc429 so the generic feet decoder doesn't grab it). Complements TRANS ALT.
        vars["PFD_TRANS_LVL"] = new SimVarDefinition
        {
            Name = "A32NX_FM1_TRANS_LVL", DisplayName = "Transition level",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest
        };
        // FCU selected altitude + heading targets (stock autopilot simvars; the A380 has no
        // A32NX_FCU_*_DISPLAY var for these). On the PFD status box next to the FMA.
        Stock("FCU_SEL_ALT", "AUTOPILOT ALTITUDE LOCK VAR:3", "FCU selected altitude", "feet");
        Stock("FCU_SEL_HDG", "AUTOPILOT HEADING LOCK DIR", "FCU selected heading", "degrees");
        // Static + total air temperature (ADIRS ADR-1 ARINC429 words, celsius) — what the SD
        // permanent footer shows. Auto-decoded by the generic ARINC path.
        ArincUnit("PFD_SAT", "A32NX_ADIRS_ADR_1_STATIC_AIR_TEMPERATURE", "Static air temperature", "celsius");
        ArincUnit("PFD_TAT", "A32NX_ADIRS_ADR_1_TOTAL_AIR_TEMPERATURE", "Total air temperature", "celsius");
        // ND velocities + wind (ADIRS ARINC429 BNR words). Registered with the RAW var name as
        // the key (so the ND form keeps reading them) but as IsArinc429, so the monitored ND
        // status box decodes them instead of showing the ~14-billion raw word. "not available"
        // (NCD) on the ground until the ADIRS aligns + the aircraft is moving.
        ArincUnit("A32NX_ADIRS_IR_1_GROUND_SPEED", "A32NX_ADIRS_IR_1_GROUND_SPEED", "Ground speed", "knots");
        ArincUnit("A32NX_ADIRS_ADR_1_TRUE_AIRSPEED", "A32NX_ADIRS_ADR_1_TRUE_AIRSPEED", "True airspeed", "knots");
        ArincUnit("A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR", "A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR", "Wind direction", "degrees");
        ArincUnit("A32NX_ADIRS_IR_1_WIND_SPEED_BNR", "A32NX_ADIRS_IR_1_WIND_SPEED_BNR", "Wind speed", "knots");
        // ND heading reference (magnetic vs true) — auto-announced on change.
        ReadEnum("A32NX_FMGC_TRUE_REF", "Heading reference",
            new Dictionary<double, string> { [0] = "magnetic", [1] = "true" });
        // ISIS speed-bugs active flag (the bug VALUES are JS-only on the FBW ISIS, no L-var;
        // the ATT-10s realign flag is likewise scrape-only). Friendly label for the status box.
        ReadEnumQuiet("A32NX_ISIS_BUGS_ACTIVE", "ISIS speed bugs",
            new Dictionary<double, string> { [0] = "off", [1] = "on" });
        // CPCS B2-B4 sibling words (PRESS/CRUISE) + SEC3 rudder + SEC1 status word — read-only,
        // force-read on the relevant SD pages so the best-source decode can fall back off B1/SEC1.
        foreach (var bas in new[] { "A32NX_PRESS_CABIN_ALTITUDE", "A32NX_PRESS_CABIN_VS",
                                    "A32NX_PRESS_CABIN_DELTA_PRESSURE", "A32NX_PRESS_CABIN_ALTITUDE_TARGET" })
            foreach (var sfx in new[] { "_B2", "_B3", "_B4" })
                Read(bas + sfx, bas + sfx);
        Read("A32NX_SEC_3_RUDDER_ACTUAL_POSITION", "SEC 3 rudder position");
        Read("A32NX_SEC_1_RUDDER_STATUS_WORD", "SEC 1 rudder status word");
        // Beta-target (sideslip target on engine-out / crosswind approach) — gated on _ACTIVE,
        // decoded in TryGetDisplayOverride ("X.X degrees left/right" / "not active").
        Read("A32NX_BETA_TARGET", "Sideslip target", "degrees");
        // Continuous + announced: speaks "Sideslip target active/inactive" when it engages on an
        // engine-out / crosswind approach (silent baseline on the ground), and the ProcessSimVarUpdate
        // cache feeds the beta-target decode above.
        Mon("A32NX_BETA_TARGET_ACTIVE", "Sideslip target",
            new Dictionary<double, string> { [0] = "inactive", [1] = "active" });
        // TCAS resolution-advisory vertical-speed band (green = fly toward, red = avoid). Only
        // meaningful during an RA; decoded "fly to N fpm" / "—" in TryGetDisplayOverride.
        Read("A32NX_TCAS_VSPEED_GREEN", "TCAS target vertical speed", "feet per minute");
        Read("A32NX_TCAS_VSPEED_RED", "TCAS avoid vertical speed", "feet per minute");
        // Managed / preselected target speeds + selected V/S + expedite + flight directors.
        // Decoded in TryGetDisplayOverride (none / knots / mach / fpm). Preselect = -1 when unset.
        Read("A32NX_SPEEDS_MANAGED_PFD", "Managed speed", "knots");
        Read("A32NX_SpeedPreselVal", "Preselected speed", "knots");
        Read("A32NX_MachPreselVal", "Preselected Mach", "mach");
        Read("A32NX_AUTOPILOT_VS_SELECTED", "Selected vertical speed", "feet per minute");
        ReadEnum("A32NX_FMA_EXPEDITE_MODE", "Expedite", new Dictionary<double, string> { [0] = "off", [1] = "on" });
        Stock("FD_1", "AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", "Flight director 1", "bool",
            new Dictionary<double, string> { [0] = "off", [1] = "on" });
        Stock("FD_2", "AUTOPILOT FLIGHT DIRECTOR ACTIVE:2", "Flight director 2", "bool",
            new Dictionary<double, string> { [0] = "off", [1] = "on" });
        // Autoland capability (the PFD D1/D2 cell: LAND2 / LAND3 SINGLE / LAND3 DUAL) — decoded
        // from the FCDC flight-guidance discrete word 4 (bit 23 = LAND2, 24 = LAND3 fail-passive
        // "single", 25 = LAND3 fail-operational "dual"). NCD until on an ILS approach with the
        // capability computed → "none" on the ground. Decoded in TryGetDisplayOverride.
        vars["PFD_AUTOLAND"] = new SimVarDefinition
        {
            Name = "A32NX_FCDC_1_FG_DISCRETE_WORD_4", DisplayName = "Autoland capability",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest
        };
        // Nav radios — VOR 1/2 frequency + DME and ADF 1/2 frequency (stock simvars; the IDENTS
        // are read by the Output+N "nav radio" hotkey via the NAV IDENT string struct). On the ND.
        Stock("ND_VOR1_FREQ", "NAV ACTIVE FREQUENCY:1", "VOR 1 frequency", "MHz");
        Stock("ND_VOR2_FREQ", "NAV ACTIVE FREQUENCY:2", "VOR 2 frequency", "MHz");
        Stock("ND_VOR1_DME", "NAV DME:1", "VOR 1 DME", "nautical miles");
        Stock("ND_VOR2_DME", "NAV DME:2", "VOR 2 DME", "nautical miles");
        Stock("ND_ADF1_FREQ", "ADF ACTIVE FREQUENCY:1", "ADF 1 frequency", "KHz");
        Stock("ND_ADF2_FREQ", "ADF ACTIVE FREQUENCY:2", "ADF 2 frequency", "KHz");
        // Marker beacon (stock enum) + ILS/LS course (plain L:var; -1 = no course set,
        // decoded in TryGetDisplayOverride). These complete the ILS block alongside the
        // existing PFD_ILS_FREQ / PFD_ILS_DME.
        Stock("MARKER_BEACON", "MARKER BEACON STATE", "Marker beacon", "number",
            new Dictionary<double, string> { [0] = "None", [1] = "Outer marker", [2] = "Middle marker", [3] = "Inner marker" });
        Read("A32NX_FM_LS_COURSE", "ILS course");
        // Gross-weight CG (%MAC), cached for the W / Shift+W readouts (Gus's GW-CG; kept
        // across the doors/PFD revert). Plain numeric FBW L-var (~40% MAC live). MonNum
        // registers it Units="number" (required for the L-var read) and routes it to a
        // ProcessSimVarUpdate branch that caches it and returns true (never auto-announced).
        MonNum("A32NX_AIRFRAME_GW_CG_PERCENT_MAC", "Gross Weight CG");
        // Gross weight (kg) — stock TOTAL WEIGHT, monitored + cached for the W / Shift+W
        // readouts. IsAnnounced so the monitor reads it; ProcessSimVarUpdate caches it.
        vars["GW_KG_CACHE"] = new SimVarDefinition
        {
            Name = "TOTAL WEIGHT", DisplayName = "Gross Weight (cache)", Type = SimVarType.SimVar,
            Units = "kilograms", UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };

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
        // ISIS extras: baro in inHg (the non-indexed KOHLSMAN, shown alongside the hPa
        // value the ISIS already reads) + body-X accel for the slip/skid ball.
        // NOTE: deliberately NOT using the indexed `:3` standby air-data source. Although
        // the real ISIS reads ADR :3, the stock `INDICATED ALTITUDE:3` name is NOT a valid
        // SimConnect data-definition var (INDICATED ALTITUDE is not an indexed simvar) —
        // adding it made SimConnect raise a name exception that broke aircraft detection
        // (the "MSFS detected but every hotkey says not connected" bug). The A320 ISIS uses
        // the non-indexed simvars for the same reason; mirror that here.
        Stock("KOHLSMAN_SETTING_INHG", "KOHLSMAN SETTING MB", "Standby Baro inHg", "inHg");
        Stock("ACCELERATION BODY X", "ACCELERATION BODY X", "Standby Slip/Skid", "GFORCE");

        // ENGINE SD-page stock simvars (oil temp/press + vibration per engine — the
        // FBW SD ENG page reads these, like the A32NX). Pre-declared as SimVar so the
        // A380SdRows auto-register loop leaves them as SimVar (not L:var).
        for (int e = 1; e <= 4; e++)
        {
            Stock($"GENERAL_ENG_OIL_TEMPERATURE:{e}", $"GENERAL ENG OIL TEMPERATURE:{e}", $"Engine {e} Oil Temperature", "celsius");
            Stock($"ENG_OIL_PRESSURE:{e}", $"ENG OIL PRESSURE:{e}", $"Engine {e} Oil Pressure", "psi");
            Stock($"TURB_ENG_VIBRATION:{e}", $"TURB ENG VIBRATION:{e}", $"Engine {e} Vibration", "number");
        }
        // Landing-gear position (Wheel SD page) — stock simvars, percent extended.
        Stock("GEAR_CENTER_POSITION", "GEAR CENTER POSITION", "Nose Gear", "percent");
        Stock("GEAR_LEFT_POSITION", "GEAR LEFT POSITION", "Left Main Gear", "percent");
        Stock("GEAR_RIGHT_POSITION", "GEAR RIGHT POSITION", "Right Main Gear", "percent");

        // FUEL SD-page valve states — stock FUELSYSTEM VALVE OPEN simvars (the FBW fuel
        // system layer). Pre-declared as SimVar (clean key, spaced Name) so the A380SdRows
        // auto-register loop leaves them as SimVar, exactly like the GEAR/ENG rows above.
        // Indices from the FBW A380 flight_model.cfg fuel system: 1-4 engine LP valves,
        // 46-49 crossfeed, 57/58 jettison. Live-verified the names resolve (valve 1 = open).
        for (int v = 1; v <= 4; v++)  Stock($"FUEL_LP_VALVE:{v}",  $"FUELSYSTEM VALVE OPEN:{v}",      $"Engine {v} LP valve", "number");
        for (int v = 1; v <= 4; v++)  Stock($"FUEL_XFEED:{v}",     $"FUELSYSTEM VALVE OPEN:{45 + v}", $"Crossfeed valve {v}", "number");
        Stock("FUEL_JETT_L", "FUELSYSTEM VALVE OPEN:57", "Left jettison valve", "number");
        Stock("FUEL_JETT_R", "FUELSYSTEM VALVE OPEN:58", "Right jettison valve", "number");

        // NOTE: The DOORS SD page gets its door states from the live Coherent SD
        // scrape (door states are part of the decoded scrape), so no INTERACTIVE
        // POINT OPEN SimVars are registered here. Registering those as SimConnect
        // data definitions was the root cause of the aircraft-detection break, and
        // nothing in the SD form referenced them anyway.

        // ND status-box vars — pre-register with FRIENDLY display names so the status box
        // shows "TO Waypoint: PPOS" instead of the raw key "A32NX_EFIS_L_TO_WPT_IDENT_0: PPOS"
        // (the windowReadVars loop below otherwise registers them with DisplayName = key).
        // Values still decode via TryGetDisplayOverride / the ND form.
        Read("A32NX_EFIS_L_TO_WPT_IDENT_0", "TO Waypoint");
        Read("A32NX_EFIS_L_TO_WPT_DISTANCE", "Distance to Waypoint");
        Read("A32NX_EFIS_L_TO_WPT_BEARING", "Bearing to Waypoint");
        Read("A32NX_EFIS_L_TO_WPT_ETA", "Time to Waypoint");
        Read("A32NX_FG_CROSS_TRACK_ERROR", "Cross-track Error");
        Read("A32NX_FMGC_L_RNP", "Required Nav Performance");
        Read("A32NX_RADIO_RECEIVER_LOC_IS_VALID", "Localizer Valid");
        Read("A32NX_RADIO_RECEIVER_LOC_DEVIATION", "Localizer Deviation");
        Read("A32NX_RADIO_RECEIVER_GS_IS_VALID", "Glideslope Valid");
        Read("A32NX_RADIO_RECEIVER_GS_DEVIATION", "Glideslope Deviation");

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
        // Register every decoded SD-page row var (A380SdRows) OnRequest so the ECAM-CP
        // "System Display Page" combo can read it (RequestVariable no-ops on any
        // unregistered var). Most A380SdRows vars are L:vars (underscore identifiers),
        // but DON'T blindly register everything as an L:var — a stock SimVar name
        // (contains a space or colon, e.g. "INTERACTIVE POINT OPEN:0") MUST register as
        // a SimVar: forcing a stock-SimVar name through the L:var/MobiFlight path
        // corrupts SimConnect registration and breaks aircraft detection.
        for (int sdPage = 0; sdPage <= 13; sdPage++)
            foreach (var (_, rowVar, _) in A380SdRows(sdPage))
                if (!vars.ContainsKey(rowVar))
                {
                    bool isStock = rowVar.Contains(' ') || rowVar.Contains(':');
                    vars[rowVar] = new SimVarDefinition
                    {
                        Name = rowVar, DisplayName = rowVar,
                        Type = isStock ? SimVarType.SimVar : SimVarType.LVar,
                        UpdateFrequency = UpdateFrequency.OnRequest, Units = "number"
                    };
                }

        // ---- Situational-awareness auto-announces (status enums; batch-covered, so no
        // SimConnect-def cost). These announce on change and appear in the Ctrl+M monitor.
        // TCAS surveillance mode + system fault; FMA speed-protection + mode-reversion. ----
        Mon("A32NX_TCAS_MODE", "TCAS Mode", new Dictionary<double, string>
            { [0] = "standby", [1] = "traffic advisory only", [2] = "traffic and resolution advisories" });
        ReadEnum("A32NX_TCAS_FAULT", "TCAS Fault", new Dictionary<double, string> { [0] = "normal", [1] = "fault" });
        Mon("A32NX_FMA_SPEED_PROTECTION_MODE", "Speed Protection",
            new Dictionary<double, string> { [0] = "off", [1] = "active" });
        Mon("A32NX_FMA_MODE_REVERSION", "FMA Reversion",
            new Dictionary<double, string> { [0] = "none", [1] = "mode reversion" });

        // ---- SAFETY AURAL CALLOUTS — the EGPWS / stall / AP-disconnect aurals a blind pilot
        // otherwise can't hear. Auto-announced on onset (the L-var holds the current warning,
        // so it speaks once per event, not per aural repeat). Mutable via Ctrl+M. ----
        // EGPWS (GPWS/TAWS) escape-maneuver callouts — A32NX_GPWS_AURAL_OUTPUT enum (FBW EGPWS).
        ReadEnum("A32NX_GPWS_AURAL_OUTPUT", "GPWS", new Dictionary<double, string>
        {
            [0] = "none", [1] = "PULL UP", [2] = "TERRAIN", [3] = "TOO LOW TERRAIN", [4] = "TOO LOW GEAR",
            [5] = "TOO LOW FLAPS", [6] = "SINK RATE", [7] = "DON'T SINK", [8] = "GLIDESLOPE",
            [9] = "GLIDESLOPE", [10] = "TERRAIN AHEAD", [11] = "OBSTACLE AHEAD"
        });
        // Stall warning aural ("STALL STALL").
        ReadEnum("A32NX_AUDIO_STALL_WARNING", "Stall warning",
            new Dictionary<double, string> { [0] = "off", [1] = "STALL" });
        // Autopilot-disconnect cavalry charge (the AP-disconnect aural).
        ReadEnum("A32NX_FWC_CAVALRY_CHARGE", "Autopilot disconnect",
            new Dictionary<double, string> { [0] = "off", [1] = "autopilot disconnect" });

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
                [-1] = "Default automatic page", [0] = "Engine", [1] = "APU", [2] = "Bleed", [3] = "Cond", [4] = "Press",
                [5] = "Door", [6] = "Elec AC", [7] = "Elec DC", [8] = "Fuel", [9] = "Wheel", [10] = "Hyd",
                [11] = "F/Ctl", [13] = "Cruise",
                // C/B (12), Status (14) and Video (15) were REMOVED from the picker: the FBW SD
                // rejects those page indices (rewrites the index back a few frames later), so the
                // combo snapped back and the slow DOM-scrape fallback ran for nothing. The
                // remaining pages are all SimVar-decoded.
                // Not an SD page — selecting this scrapes the UPPER ECAM / E-WD instead
                // (engine N1/EGT/N2/FF + memos/warnings) into the same status box.
                [16] = "Upper E/WD"
            });
        foreach (var (k, d) in new[]
        {
            ("ALL", "All"), ("ABNPROC", "Abnormal Proc"), ("CL", "Checklist"), ("CLR", "Clear"),
            ("CLR2", "Clear 2"), ("RCL", "Recall"), ("TOCONFIG", "T.O Config"), ("EMERCANC", "Emergency Cancel"),
            ("UP", "Up"), ("DOWN", "Down"), ("MORE", "More")
        })
            PressSilent($"A32NX_BTN_{k}", $"ECAM {d}");

        // ---- Weather radar / SURV ----
        // The WEATHER RADAR (the radar that paints precip/storms) and PWS (Predictive WindShear)
        // are DIFFERENT systems sharing the antenna. The radar itself is the Sys on/off + the
        // mode knob (WX / WX+T / TURB / MAP) — XMLVAR_A320_WeatherRadar_Sys/_Mode (the A380 reuses
        // the A320 weather-radar model vars; live-verified settable). PWS is the separate AUTO/OFF
        // switch below. (The radar IMAGE on the ND is still WIP in the FBW A380 dev build, but the
        // switch states are real and settable, so they're exposed for the pilot.)
        Sel("XMLVAR_A320_WeatherRadar_Sys", "Weather Radar System",
            new Dictionary<double, string> { [0] = "Off", [1] = "On" });
        Sel("XMLVAR_A320_WeatherRadar_Mode", "Weather Radar Mode",
            new Dictionary<double, string> { [0] = "Weather", [1] = "Weather plus Turbulence", [2] = "Turbulence", [3] = "Map" });
        // PWS_Position: 0 = Off, 1 = Auto (verified from FBW source — PseudoFWC reads
        // predWSOn = PWS_Position as Bool, so position 1 = predictive-windshear ON = Auto).
        // The labels were reversed here; the A32NX has them correct.
        Sel("A32NX_SWITCH_RADAR_PWS_Position", "Predictive Windshear",
            new Dictionary<double, string> { [0] = "Off", [1] = "Auto" });
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
            // The FBW_RMP_FREQUENCY_* L:vars read an UNINITIALISED ~19 MHz garbage value in many
            // states (live-confirmed), so they can't drive the announce — kept as plain read-outs
            // only. The STOCK COM ACTIVE/STANDBY FREQUENCY:n simvars are RELIABLE (live-verified they
            // track the RMP-tuned freq: set 121.900 + swap -> COM1 active read 121.9), so THOSE are
            // the auto-announce source: a blind pilot hears "VHF n active/standby X" on every
            // load/swap, completely independent of the (flaky) Coherent RMP-window scrape.
            Read($"FBW_RMP_FREQUENCY_ACTIVE_{v}", $"VHF {v} Active Frequency");
            Read($"FBW_RMP_FREQUENCY_STANDBY_{v}", $"VHF {v} Standby Frequency");
            vars[$"COM_ACTIVE_{v}"] = new SimVarDefinition
            {
                Name = $"COM ACTIVE FREQUENCY:{v}", DisplayName = $"VHF {v} Active Frequency",
                Type = SimVarType.SimVar, Units = "MHz",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
            };
            vars[$"COM_STANDBY_{v}"] = new SimVarDefinition
            {
                Name = $"COM STANDBY FREQUENCY:{v}", DisplayName = $"VHF {v} Standby Frequency",
                Type = SimVarType.SimVar, Units = "MHz",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
            };
        }

        // ---- Cockpit door ----
        OnOff("A32NX_COCKPIT_DOOR_LOCKED", "Cockpit Door Locked");
        // Cabin Ready is a READ-ONLY status (the cabin/purser signals it via the EFB;
        // the cockpit just shows the light) — live-verified a cockpit write reverts
        // 0→1 within 2 s. So it's an auto-announced monitor var, not a settable combo.
        Mon("A32NX_CABIN_READY", "Cabin Ready", new Dictionary<double, string> { [0] = "Not Ready", [1] = "Ready" });

        // ============================ DISPLAYS / STATUS ============================
        // Autothrust = READ-ONLY status (auto-announced on change: Disengaged / Armed / Active).
        // It used to be a settable combo, but "Active" is an automatic state you can't command
        // (the toggle could only arm), which was confusing. A/THR is now ENGAGED/DISCONNECTED from
        // the Autopilot dialog (Ctrl+P) — the single control point — and this Mon just SPEAKS the
        // resulting state when it changes, so the pilot always hears A/THR engaging/dropping.
        Mon("A32NX_AUTOTHRUST_STATUS", "Autothrust",
            new Dictionary<double, string> { [0] = "Disengaged", [1] = "Armed", [2] = "Active" });
        // Render read-only in its panel (it stays in the Autopilot panel list so the
        // user can Tab to read the live state) — WITHOUT this the renderer treats any
        // multi-state ValueDescriptions var as a settable combo (MainForm ~5178), which
        // is exactly the confusing A/THR combo we're dropping.
        vars["A32NX_AUTOTHRUST_STATUS"].RenderAsReadOnlyStatus = true;
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
            // Upper E/WD extras (N1 command target). Thrust-rating mode + flex below.
            Read($"A32NX_AUTOTHRUST_N1_COMMANDED:{n}", $"Engine {n} N1 Command", "percent");
            // Engine oil PRESSURE is not modelled on the FBW A380 dev build — every
            // candidate var returns garbage (stock ENG OIL PRESSURE ~217000, GENERAL
            // ~6061, A32NX_ENGINE_OIL_PRESSURE 0), so it's omitted rather than shown
            // as a fake value. Oil quantity + temperature are real and kept.
            Stock($"ENG_OIL_TEMP:{n}", $"GENERAL ENG OIL TEMPERATURE:{n}", $"Engine {n} Oil Temperature", "celsius");
        }
        Read("A32NX_AIRLINER_TO_FLEX_TEMP", "Flex Temperature", "celsius");   // Upper E/WD flex readout
        // EWD thrust limit — the green max-N1 % shown beside the thrust-rating mode
        // (CLB/FLX/TOGA). Tells the pilot how much thrust the current mode allows; the
        // limit TYPE is already announced via A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE.
        Read("A32NX_AUTOTHRUST_THRUST_LIMIT", "Thrust limit N1", "percent");
        // The A380 has thrust reversers ONLY on the inboard engines (2 and 3); the
        // outboard 1 and 4 have none (their _REVERSER_ vars stay 0 forever), so only
        // 2 and 3 are exposed/announced — "only engine 2 and 3 deploy" is correct.
        ReadEnum("A32NX_REVERSER_2_DEPLOYED", "Engine 2 Reverser", revVd);
        ReadEnum("A32NX_REVERSER_3_DEPLOYED", "Engine 3 Reverser", revVd);
        // Engine mode knob: combo writes via HandleUIVariableSet to all engines.
        // READBACK is the stock ignition-switch simvar (TURB ENG IGNITION SWITCH EX1:1,
        // Enum: 0=Crank/1=Norm/2=Ignition), NOT XMLVAR_ENG_MODE_SEL. Verified live: the
        // TURBINE_IGNITION_SWITCH_SETn events the combo fires move the stock simvar but
        // do NOT move XMLVAR_ENG_MODE_SEL (the knob-position var, only updated by cockpit
        // interaction) — so reading XMLVAR left the combo stale and unable to cycle.
        // Continuous so the combo tracks the real state + announces mode changes.
        vars["ENGINE_MODE_SELECTOR"] = new SimVarDefinition
        {
            Name = "TURB ENG IGNITION SWITCH EX1:1", DisplayName = "Engine Mode", Type = SimVarType.SimVar,
            Units = "Enum", UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
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
        foreach (var bus in new[] { "AC_1", "AC_2", "AC_3", "AC_4", "AC_ESS", "AC_ESS_SHED", "247XP",
                                    "DC_1", "DC_2", "DC_ESS", "247PP", "DC_HOT_1", "DC_HOT_2", "DC_HOT_3", "DC_HOT_4", "DC_GND_FLT_SVC" })
            ReadEnum($"A32NX_ELEC_{bus}_BUS_IS_POWERED", $"{bus.Replace('_', ' ')} Bus", powered);

        // APU
        ReadEnum("A32NX_OVHD_APU_MASTER_SW_PB_HAS_FAULT", "APU Master Fault", fault);
        ReadEnum("A32NX_APU_LOW_FUEL_PRESSURE_FAULT", "APU Low Fuel Pressure", fault);
        ReadEnum("A32NX_APU_BLEED_AIR_VALVE_OPEN", "APU Bleed Valve", openVd);
        Read("A32NX_APU_N_RAW", "APU N", "percent");
        // APU running parameters (the start monitor: N2, EGT, inlet flap, fuel used).
        // EGT is an ARINC429 word -> decoded to celsius in TryGetDisplayOverride; the
        // rest are plain (0 when the APU is off).
        // APU N2 is an ARINC429 word (FBW ApuPage useArinc429Var) — the old plain Read showed
        // the raw ~12.8-billion SSM word when the APU was running. Decode it ("not available"
        // when the APU FADEC isn't powered, like APU EGT).
        vars["A32NX_APU_N2"] = new SimVarDefinition
        {
            Name = "A32NX_APU_N2", DisplayName = "APU N2",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "%", Arinc429Format = "0.0",
            Arinc429NotAvailableText = "not available"
        };
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
        // Cabin pressurization — the CPIOM-B1 ARINC429 words (what the SD PRESS page shows).
        // The old MAN_CABIN_* L:vars were WRONG: they're written only in manual/emergency mode
        // and read static garbage in AUTO (live: MAN_CABIN_ALTITUDE = -415 ft on the ground,
        // while _CABIN_ALTITUDE_B1 = a real ARINC word). Auto-decoded by the generic ARINC
        // path ("not available" on bad SSM). Delta keeps one decimal.
        ArincUnit("A32NX_PRESS_CABIN_ALTITUDE_B1", "A32NX_PRESS_CABIN_ALTITUDE_B1", "Cabin Altitude", "feet");
        ArincUnit("A32NX_PRESS_CABIN_VS_B1", "A32NX_PRESS_CABIN_VS_B1", "Cabin Vertical Speed", "feet per minute");
        vars["A32NX_PRESS_CABIN_DELTA_PRESSURE_B1"] = new SimVarDefinition
        {
            Name = "A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", DisplayName = "Cabin Delta Pressure",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "psi", Arinc429Format = "0.0",
            Arinc429NotAvailableText = "not available"
        };
        // Landing elevation — the FM1 ARINC429 word (the _AUTO_ var reads static 0). Same
        // source + "not set (auto)" the SD PRESS page uses.
        vars["A32NX_FM1_LANDING_ELEVATION"] = new SimVarDefinition
        {
            Name = "A32NX_FM1_LANDING_ELEVATION", DisplayName = "Landing Elevation",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "feet", Arinc429Format = "0",
            Arinc429NotAvailableText = "not set (auto)"
        };
        // The four cabin outflow valves — the CPIOM-B1 ARINC429 % word (the _ANIM var reads 0).
        for (int v = 1; v <= 4; v++)
            vars[$"A32NX_PRESS_OUTFLOW_VALVE_{v}_OPEN_PERCENTAGE_B1"] = new SimVarDefinition
            {
                Name = $"A32NX_PRESS_OUTFLOW_VALVE_{v}_OPEN_PERCENTAGE_B1", DisplayName = $"Outflow Valve {v}",
                Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
                IsArinc429 = true, Arinc429Unit = "%", Arinc429Format = "0",
                Arinc429NotAvailableText = "not available"
            };

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
        // Keep an individual data def for the four managed-status legs the OUTPUT-mode FCU
        // readouts (Shift+H/S/A/V) force-read via RequestVariable(forceUpdate). That call
        // NO-OPS for batch-covered vars (the SimConnect-ceiling strengthening skips their
        // individual def), so without this the managed leg never arrives, the value+managed
        // pair-gate in ProcessSimVarUpdate never closes, and the readout is SILENT. Mirrors
        // the A320 fix (FlyByWireA320Definition's *_MANAGED vars). 4 extra defs, well within
        // the A380's data-def headroom. (A32NX_FCU_VS_MANAGED is not force-read by any readout
        // — VS keys on TRK_FPA_MODE_ACTIVE — so it intentionally stays batch-covered.)
        vars["A32NX_FCU_HDG_MANAGED_DASHES"].ExcludeFromBatch = true;
        vars["A32NX_FCU_SPD_MANAGED_DOT"].ExcludeFromBatch = true;
        vars["A32NX_FCU_ALT_MANAGED"].ExcludeFromBatch = true;
        vars["A32NX_TRK_FPA_MODE_ACTIVE"].ExcludeFromBatch = true;
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
        // Monitored (so ProcessSimVarUpdate sees changes) + Ctrl+M-muteable; the raw
        // generic announce is suppressed by the decoded handler returning true.
        Mon("A32NX_FMA_VERTICAL_ARMED", "Armed Vertical Modes", new Dictionary<double, string>());
        Mon("A32NX_FMA_LATERAL_ARMED", "Armed Lateral Modes", new Dictionary<double, string>());
        Read("A32NX_FMA_CRUISE_ALT_MODE", "Cruise Altitude Mode");
        Read("A32NX_PFD_LINEAR_DEVIATION_ACTIVE", "Linear Deviation Active");
        // FMS vertical-profile target altitude at the current position — the basis for
        // the PFD linear (V/DEV) deviation: deviation = current altitude − this, shown
        // only while LINEAR_DEVIATION_ACTIVE (managed climb/descent / FINAL APP).
        Read("A32NX_PFD_TARGET_ALTITUDE", "Vertical Profile Target Altitude", "feet");
        // FBW exposes the lateral-deviation request as _L_/_R_ (per FMGC), NOT _1_ — the _1_
        // name does not exist in source and always read 0. Use the captain's (_L_).
        Read("A32NX_FMGC_L_LDEV_REQUEST", "FMGC L DEV Request");
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
        // FD IS controllable after all: the per-side engage L:vars A32NX_FCU_EFIS_L/R_FD_ACTIVE
        // are settable via the calculator path and STICK (live-verified again 2026-06: writing 1
        // held for 2 s+, restored to 0). They are exposed as the "Flight Director 1/2" combos
        // (registered above via OnOff). The earlier "FD toggle event removed / non-functional"
        // conclusion was about the STOCK TOGGLE_FLIGHT_DIRECTOR event + the data-def write path,
        // which don't work — the FBW L:var calc-path write does. FD_ACTIVE below is the stock
        // COMBINED FD state, kept as an additional read-only status readout.
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
        // Baro PRESELECT (the descent-QNH preselect shown while in STD) was removed entirely:
        // the FBW marks A380X_EFIS_{side}_BARO_PRESELECTED "Not for FBW systems use!" — it's a
        // display-only output, not settable by any L:var/K-event we can reach (proven live), so
        // both the read-out and the numeric SET box are gone. Set QNH directly (Ctrl+B) at the
        // transition instead.
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
            // Units "kHz" (NOT "MHz"): the stock COM freq simvars return kHz, and MainForm's
            // formatter only has a "kHz" case (÷1000, 3 decimals -> "122.800 MHz"). "MHz" fell to
            // the default integer format -> "123". Matches the A320.
            Stock($"COM_ACTIVE_FREQUENCY:{n}", $"COM ACTIVE FREQUENCY:{n}", $"VHF {n} Active", "kHz");
            Stock($"COM_STANDBY_FREQUENCY:{n}", $"COM STANDBY FREQUENCY:{n}", $"VHF {n} Standby", "kHz");
        }

        // ============================ TRANSPONDER / ATC ============================
        // "BCO16" makes SimConnect decode the BCD-packed code to the 4-digit squawk
        // (e.g. 4242); reading it as "number" gave the raw BCD integer (0x4242=16962).
        // Continuous + IsAnnounced so the squawk AUTO-ANNOUNCES on change ("Squawk 1234") whenever
        // it's set — from the RMP, the flyPad, or anywhere (decode + announce in ProcessSimVarUpdate).
        vars["XPNDR_CODE"] = new SimVarDefinition
        {
            Name = "TRANSPONDER CODE:1", DisplayName = "Squawk Code",
            Type = SimVarType.SimVar, Units = "BCO16",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };
        // Key MUST be "TRANSPONDER_CODE_SET" so MainForm's squawk-input path
        // BCD16-encodes the entered code (4242 -> 0x4242). Sending the raw decimal
        // via the generic event path produced a wrong squawk. Event name stays XPNDR_SET.
        Evt("TRANSPONDER_CODE_SET", "XPNDR_SET", "Squawk Code");
        // Transponder MODE read-out (the stock SimVar VATSIM/vPilot read). The FBW A380's
        // systems-host OWNS this and AUTO-manages it: Standby on the ground (Mode C inhibited),
        // Mode C once airborne. Verified live it is NOT externally settable — XPNDR_SET_MODE,
        // a direct `set TRANSPONDER STATE:1 = 4`, and the old A32NX_TRANSPONDER_MODE L:var all
        // revert within a frame (the systems-host restores Standby on the ground). So the only
        // manual surveillance writes are the squawk code + IDENT (above); the mode is correct on
        // VATSIM automatically. We surface it as a Continuous+IsAnnounced read-out so a blind
        // pilot HEARS "Transponder Mode C" when it goes active in the climb (confirming VATSIM is
        // seeing their altitude) and "Standby" after landing. The AUTO<->STBY + ALT RPTG + TCAS
        // alert-level toggles are EventBus-only (mfd_xpdr_set_auto / mfd_tcas_alert_level, consumed
        // in-process) so they are reachable ONLY via the MFD SURV CONTROLS page (in AllPages,
        // surv/controls) — rarely needed since AUTO + TA/RA are the correct VATSIM defaults.
        vars["XPNDR_STATE"] = new SimVarDefinition
        {
            Name = "TRANSPONDER STATE:1", DisplayName = "Transponder Mode",
            Type = SimVarType.SimVar, Units = "Enum",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "Off", [1] = "Standby", [2] = "Test", [3] = "Mode A", [4] = "Mode C", [5] = "Mode S" }
        };

        // TCAS mode is NOT settable on the FBW A380 — REMOVED after a live in-flight test.
        // A32NX_SWITCH_TCAS_POSITION / _TRAFFIC_POSITION drive TCAS on the A32NX, but the A380
        // does not consume them: airborne at 4000 ft (above the TCAS threshold), cycling the
        // switch through 0/1/2 left A32NX_TCAS_MODE (and TA_ONLY/FAULT) flat at 0. Source agrees —
        // the A380 has zero consumers of the switch and nothing writes A32NX_TCAS_MODE. The mode
        // stays read-only (announced via the A32NX_TCAS_MODE Mon above); the MFD SURV TCAS radios
        // are dead stubs too. (Squawk code + IDENT remain the only working surveillance writes.)

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

        // ISIS standby instrument. LS is a settable toggle (shows the ILS scales on the
        // standby instrument) — promoted from readout to control (live-verified writable).
        OnOff("A32NX_ISIS_LS_ACTIVE", "ISIS LS");
        // Quiet (ReadEnumQuiet, NOT ReadEnum): this flag toggles rapidly with the ISIS bugs page
        // and must not auto-announce. It is registered earlier too (~line 1043) as ReadEnumQuiet;
        // this is the winning registration (last write wins), so it MUST stay quiet or the chatter
        // the earlier ReadEnumQuiet exists to prevent comes back.
        ReadEnumQuiet("A32NX_ISIS_BUGS_ACTIVE", "ISIS Bugs Page", onOff);
        Sel("A32NX_ISIS_BARO_MODE", "ISIS Baro Mode", new Dictionary<double, string> { [0] = "Set", [1] = "Standard" });
        OnOff("A32NX_ISIS_BARO_UNIT_INHG", "ISIS Baro in inHg");

        // Abnormal-procedure LANDING IMPACT (FWS): an active failure / abnormal procedure that
        // DEGRADES landing performance or increases landing distance. Boolean L:vars the FwsCore
        // writes (driven by activeAbnormalNonSensedKeys — real spoiler/braking failure logic).
        // Auto-announced on change so a blind pilot is told a failure affects the landing BEFORE
        // the approach, instead of having to infer it from the abnormal-procedure body text. Use
        // the FWC-1 channel (agrees with FWC-2 in normal ops). Live-verified the vars exist + read 0
        // with no failure; flip to 1 under a relevant failure (e.g. a ground-spoiler/braking fault).
        ReadEnum("A32NX_FWC_1_ABN_PROC_IMPACT_LDG_PERF", "Landing Performance Impact",
            new Dictionary<double, string> { [0] = "None", [1] = "Degraded by failure" });
        ReadEnum("A32NX_FWC_1_ABN_PROC_IMPACT_LDG_DIST", "Landing Distance Impact",
            new Dictionary<double, string> { [0] = "None", [1] = "Affected by failure" });

        // Brakes.
        // NOTE: the BRAKE FAN is NOT modelled on the FBW A380 dev build — all four brake
        // assemblies pass `None` for the brake-fan electrical bus (a `// TODO` in
        // a380x .../hydraulic/mod.rs), so `A32NX_BRAKE_FAN_RUNNING` is hardwired to 0 and
        // pressing `A32NX_BRAKE_FAN_BTN_PRESSED` does nothing (live-verified: writing the
        // button = 1 left RUNNING at 0). The control + RUNNING readout were therefore removed
        // (they presented a dead button); BRAKES HOT is real (written from brake temperature).
        ReadEnum("A32NX_BRAKES_HOT", "Brakes Hot", new Dictionary<double, string> { [0] = "Normal", [1] = "HOT" });
        // Autobrake DECEL light — illuminates while the autobrake is achieving its target
        // deceleration on the rollout. Auto-announced on change.
        ReadEnum("A32NX_AUTOBRAKES_DECEL_LIGHT", "Autobrake DECEL Light", onOff);
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
        // (A32NX_LG_GRVTY_MASTER_SWITCH_GUARD promoted to a settable control in the Gear panel.)

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
                new Dictionary<double, string> { [0] = "Off", [1] = "Waypoints", [2] = "VOR/DME", [3] = "NDB" });
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
        Sel("A32NX_GPWS_FLAPS_OFF", "GPWS Flap Mode", normOff);
        Sel("A32NX_GPWS_TERR_OFF", "GPWS Terrain", normOff);
        Sel("A32NX_GPWS_FLAPS3", "Landing Flap 3", new Dictionary<double, string> { [0] = "Off", [1] = "On" });
        OnOff("A32NX_GPWS_TEST", "GPWS Test");   // HOLD self-test: On runs the test audio, Off ends it

        // Aircraft-preset LOAD selector — registered OnRequest, NOT announced. The panel that
        // showed it was removed (presets load from the flyPad); leaving it as the old Continuous
        // +IsAnnounced Sel made it announce "Load Aircraft Preset: None" every time the flyPad's
        // load completed and the L:var reset to 0. The PROGRESS var below is the only announce now.
        vars["A32NX_AIRCRAFT_PRESET_LOAD"] = new SimVarDefinition
        {
            Name = "A32NX_AIRCRAFT_PRESET_LOAD", DisplayName = "Load Aircraft Preset",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "None", [1] = "Cold and Dark", [2] = "Powered", [3] = "Pushback", [4] = "Taxi", [5] = "Takeoff" }
        };
        // Preset load progress — auto-announced as "Aircraft preset loading N percent" at
        // milestones while the flyPad loads a preset (and "complete" at the end). Continuous +
        // IsAnnounced; the custom ProcessSimVarUpdate branch does the milestone throttling.
        vars["A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS"] = new SimVarDefinition
        {
            Name = "A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS", DisplayName = "Preset Load Progress",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };
        OnOff("A32NX_PUSHBACK_SYSTEM_ENABLED", "Pushback System");
        Read("A32NX_PUSHBACK_SPD_FACTOR", "Pushback Speed Factor");
        Read("A32NX_PUSHBACK_HDG_FACTOR", "Pushback Heading Factor");
        // NOTE: pushback tug-state readouts (PUSHBACK STATE / PUSHBACK ATTACHED) were
        // dropped — they are non-standard stock SimVar names that risk a SimConnect
        // data-definition name exception (the same failure mode that broke aircraft
        // detection). Re-add only if each name is verified addable to a data def first.

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
        // Rudder trim ADJUST — the A380 has no working "set to value" (stock RUDDER_TRIM_SET
        // is a no-op in the FBW WASM); the cockpit knob fires RUDDER_TRIM_LEFT/RIGHT. Expose
        // them as nudge BUTTONS (Evt = stock K-event; not monitored, so no batch/connection
        // impact). Read the result back from "Rudder Trim".
        Evt("RUDDER_TRIM_LEFT", "RUDDER_TRIM_LEFT", "Rudder Trim Left (nudge)");
        Evt("RUDDER_TRIM_RIGHT", "RUDDER_TRIM_RIGHT", "Rudder Trim Right (nudge)");
        // Nosewheel steering ANGLE + tiller handle — taxi-awareness read-outs (OnRequest, not
        // monitored). NOSE_WHEEL_POSITION: 0.5 = centred, (v−0.5)×140 = degrees (±70°);
        // TILLER_HANDLE_POSITION: ±1. Decoded in TryGetDisplayOverride.
        Read("A32NX_NOSE_WHEEL_POSITION", "Nose Wheel Steering Angle");
        Read("A32NX_TILLER_HANDLE_POSITION", "Tiller Handle");
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

        // ---- Ground Services > DOORS — read-only status + auto-announce (NO combos) ----
        // All 18 modelled doors (see _doorDefs for the authoritative ip→door map from the FBW
        // DoorPage.tsx). The user opens/closes doors via the flyPad; MSFSBA only ANNOUNCES each
        // open/closed transition and shows them in the Doors status display. Settable combos were
        // removed — they garbled because all the passenger doors share the base SimVar name
        // "INTERACTIVE POINT OPEN" in the panel control-update path; a read-only status keyed on
        // the distinct MSFSBA var key is correct. Passenger doors register SimVar (0..1 fraction);
        // cargo doors register LVar (LOCKED, inverted). Connection-safe (SimVar, never L:var path).
        foreach (var dd in _doorDefs)
        {
            vars[dd.Key] = new SimVarDefinition
            {
                Name = dd.Var, DisplayName = dd.Name,
                Type = dd.IsSimVar ? SimVarType.SimVar : SimVarType.LVar,
                Units = dd.IsSimVar ? "percent over 100" : "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = dd.CargoLocked
                    ? new Dictionary<double, string> { [0] = "Open", [1] = "Closed" }
                    : new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            };
        }

        // Ground-service ACTION buttons (jet bridge, stairs, fuel/baggage/catering trucks)
        // were REMOVED — they're done on the flyPad Ground page now, not via panels. Only the
        // jet-bridge MOTION readout stays, so a blind pilot still hears Moving/Stopped after a
        // flyPad jet-bridge call (stock SimVar — the only readable jetway state; the FBW EFB
        // itself only infers connection from the fwd door). Auto-announces on change.
        vars["JETWAY_MOVING_STATE"] = new SimVarDefinition
        {
            Name = "JETWAY MOVING", DisplayName = "Jet Bridge Motion",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Stopped", [1] = "Moving" }
        };

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
                "Recorder and Misc", "GPWS", "Reset", "Interior Lighting", "Exterior Lighting"
            },
            ["Glareshield"] = new List<string> { "FCU", "EFIS Captain", "EFIS First Officer", "Warnings", "OIT" },
            ["Instrument"] = new List<string> { "Gear", "Autobrake", "ISIS", "Source Switching", "Clock" },
            ["Pedestal"] = new List<string>
            {
                "Engines", "Thrust Levers", "Flaps and Brakes", "Speed Brake", "ECAM Control Panel", "Weather Radar",
                "Transponder", "RMP", "Audio Control Panel Captain", "Audio Control Panel First Officer",
                "Cockpit"
            },
            // Ground services (doors + equipment) were removed from the panels entirely —
            // everything ground/handling is done through the flyPad. The door / jetway /
            // chocks / cones / external-power STATE still auto-announces on change (the vars
            // stay registered + IsAnnounced); they just no longer have navigable panels here.
            ["Displays"] = new List<string> { "PFD", "ND", "Status", "Speeds", "Minimums" }
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var p = new Dictionary<string, List<string>>();

        p["ELEC"] = new List<string>
        {
            "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO",
            // Ground / external power kept next to the battery controls (user request).
            "A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON",
            "A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON",
            "A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO", "A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL",
            "A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO", "A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON",
            "A32NX_OVHD_ELEC_IDG_1_PB_IS_RELEASED", "A32NX_OVHD_ELEC_IDG_2_PB_IS_RELEASED",
            "A32NX_OVHD_ELEC_IDG_3_PB_IS_RELEASED", "A32NX_OVHD_ELEC_IDG_4_PB_IS_RELEASED",
            "ELEC_ENG_GEN:1", "ELEC_ENG_GEN:2", "ELEC_ENG_GEN:3", "ELEC_ENG_GEN:4",
            "ELEC_APU_GEN:1", "ELEC_APU_GEN:2",
            "A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON", "A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED",
            "A32NX_EMERELECPWR_GEN_TEST", "A380X_OVHD_ELEC_BAT_SELECTOR_KNOB"
        };
        p["APU"] = new List<string>
        {
            "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_APU_START_PB_IS_ON",
            "A32NX_APU_AUTOEXITING_TEST_ON"
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
            "A32NX_FIRE_BUTTON_ENG4", "A32NX_FIRE_BUTTON_APU",
            "A32NX_OVHD_FIRE_AGENT_1_ENG_1_IS_PRESSED", "A32NX_OVHD_FIRE_AGENT_2_ENG_1_IS_PRESSED",
            "A32NX_OVHD_FIRE_AGENT_1_ENG_2_IS_PRESSED", "A32NX_OVHD_FIRE_AGENT_2_ENG_2_IS_PRESSED",
            "A32NX_OVHD_FIRE_AGENT_1_ENG_3_IS_PRESSED", "A32NX_OVHD_FIRE_AGENT_2_ENG_3_IS_PRESSED",
            "A32NX_OVHD_FIRE_AGENT_1_ENG_4_IS_PRESSED", "A32NX_OVHD_FIRE_AGENT_2_ENG_4_IS_PRESSED",
            "A32NX_OVHD_FIRE_AGENT_1_APU_1_IS_PRESSED",
            "A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", "A32NX_FIRE_TEST_CARGO"
        };
        p["Oxygen"] = new List<string>
        {
            "PUSH_OVHD_OXYGEN_CREW", "A32NX_OXYGEN_MASKS_DEPLOYED", "A32NX_OXYGEN_TMR_RESET"
        };
        p["Calls"] = new List<string>
        {
            "A32NX_CALLS_EMER_ON", "A32NX_EVAC_COMMAND_TOGGLE", "A32NX_EVAC_CAPT_TOGGLE",
            "A32NX_CABIN_READY",
            "PUSH_OVHD_CALLS_ALL", "PUSH_OVHD_CALLS_FWD", "PUSH_OVHD_CALLS_AFT", "PUSH_OVHD_CALLS_MECH"
        };
        // UNIFIED Cockpit panel — the cockpit door, the sliding windows + shades, and the
        // openable cockpit panels/seats are now one organized group (was three separate panels:
        // "Cockpit Door", "Windows and Shades", "Cockpit"). Ordered: door → windows → shades →
        // openable panels → seats/armrests.
        p["Cockpit"] = new List<string>
        {
            // ---- Door ----
            "A32NX_COCKPIT_DOOR_LOCKED", "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE",
            // ---- Sliding windows ----
            "CPT_SLIDING_WINDOW", "FO_SLIDING_WINDOW",
            // ---- Sunshades / visors (accessible 0-100% drag sliders) ----
            "SUNSHADE_CPT_OPENING", "SUNSHADE_FO_OPENING",
            "SUNSHADE_FWD_LH", "SUNSHADE_FWD_CTR", "SUNSHADE_FWD_RH",
            "AFT_LH_SUNSHADE_OPENING", "AFT_RH_SUNSHADE_OPENING",
            "CPT_SMALL_SHADE", "FO_SMALL_SHADE",
            // ---- Openable cockpit panels (oxygen, tables, footrests, LG-pins door) ----
            "CPT_OXY_FWD_OPENING", "AFT_OXY_OPENING",
            "A380_CPT_TABLE", "A380_FO_TABLE",
            "A380_CPT_FOOTREST", "A380_FO_FOOTREST",
            "A380_LGPIN_DOOR",
            // ---- Crew seats (start/stop motor toggle BUTTONS only) + armrests ----
            // The 4 SEAT_*_MOVE_* position read-outs were REMOVED from the panel (user request):
            // the moving buttons + the spoken position-on-stop are enough. The position L:vars stay
            // REGISTERED (OnRequest) so ToggleSeatMotor can seed/read them and AnnounceSeatPosition
            // can speak the band when a motor stops — they are just no longer listed as panel fields.
            "SEATBTN_CPT_UP", "SEATBTN_CPT_DOWN", "SEATBTN_CPT_FWD", "SEATBTN_CPT_AFT",
            "SEATBTN_FO_UP", "SEATBTN_FO_DOWN", "SEATBTN_FO_FWD", "SEATBTN_FO_AFT",
            "BIGARMREST_CPT_UP_DOWN", "BIGARMREST_CPT_TILT", "SMALLARMREST_CPT_FWD",
            "BIGARMREST_FO_UP_DOWN", "BIGARMREST_FO_TILT", "SMALLARMREST_FO_FWD"
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
            "A32NX_RUDDER_TRIM_RESET", "RUDDER_TRIM_LEFT", "RUDDER_TRIM_RIGHT",
            "A32NX_TILLER_PEDAL_DISCONNECT"
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
            "A380X_OVHD_STORM_LT",   // cockpit door video moved to the unified p["Cockpit"]
            "A32NX_ACMS_TRIGGER_ON", "A32NX_CREW_HEAD_SET", "A32NX_SVGEINT_OVRD_ON",
            "A32NX_ENGMANSTARTALTN_TOGGLE", "A32NX_ENTERTAINMENT_CWS_OFF",
            "A32NX_ENTERTAINMENT_IFEC_OFF", "A380X_REMOTE_CB_CTRL",
        };
        p["Audio Control Panel Captain"] = new List<string>
        {
            "A380X_RMP_1_VHF_VOL_RX_SWITCH_1", "A380X_RMP_1_VHF_VOL_RX_SWITCH_2", "A380X_RMP_1_VHF_VOL_RX_SWITCH_3",
            "A380X_RMP_1_VHF_VOL_1", "A380X_RMP_1_VHF_VOL_2", "A380X_RMP_1_VHF_VOL_3",
            "A380X_RMP_1_HF_VOL_1", "A380X_RMP_1_HF_VOL_2", "A380X_RMP_1_TEL_VOL_1",
            "A380X_RMP_1_CAB_VOL", "A380X_RMP_1_INT_VOL", "A380X_RMP_1_PA_VOL", "A380X_RMP_1_NAV_VOL",
            "A380X_RMP_1_BRIGHTNESS_KNOB",
            "A380X_RMP_1_HF_VOL_RX_SWITCH_1", "A380X_RMP_1_HF_VOL_RX_SWITCH_2",
            "A380X_RMP_1_TEL_VOL_RX_SWITCH_1", "A380X_RMP_1_TEL_VOL_RX_SWITCH_2",
            "A380X_RMP_1_CAB_VOL_RX_SWITCH", "A380X_RMP_1_INT_VOL_RX_SWITCH",
            "A380X_RMP_1_PA_VOL_RX_SWITCH", "A380X_RMP_1_NAV_VOL_RX_SWITCH",
            "A380X_RMP_1_VHF_TX_1", "A380X_RMP_1_VHF_TX_2", "A380X_RMP_1_VHF_TX_3",
            "A380X_RMP_1_HF_TX_1", "A380X_RMP_1_HF_TX_2", "A380X_RMP_1_TEL_TX_1", "A380X_RMP_1_TEL_TX_2",
            "A380X_RMP_1_INT_TX_1", "A380X_RMP_1_CAB_TX_1", "A380X_RMP_1_PA_TX_1", "A380X_RMP_1_NAV_TX_1"
        };
        p["Audio Control Panel First Officer"] = new List<string>
        {
            "A380X_RMP_2_VHF_VOL_RX_SWITCH_1", "A380X_RMP_2_VHF_VOL_RX_SWITCH_2", "A380X_RMP_2_VHF_VOL_RX_SWITCH_3",
            "A380X_RMP_2_VHF_VOL_1", "A380X_RMP_2_VHF_VOL_2", "A380X_RMP_2_VHF_VOL_3",
            "A380X_RMP_2_HF_VOL_1", "A380X_RMP_2_HF_VOL_2", "A380X_RMP_2_TEL_VOL_1",
            "A380X_RMP_2_CAB_VOL", "A380X_RMP_2_INT_VOL", "A380X_RMP_2_PA_VOL", "A380X_RMP_2_NAV_VOL",
            "A380X_RMP_2_BRIGHTNESS_KNOB",
            "A380X_RMP_2_HF_VOL_RX_SWITCH_1", "A380X_RMP_2_HF_VOL_RX_SWITCH_2",
            "A380X_RMP_2_TEL_VOL_RX_SWITCH_1", "A380X_RMP_2_TEL_VOL_RX_SWITCH_2",
            "A380X_RMP_2_CAB_VOL_RX_SWITCH", "A380X_RMP_2_INT_VOL_RX_SWITCH",
            "A380X_RMP_2_PA_VOL_RX_SWITCH", "A380X_RMP_2_NAV_VOL_RX_SWITCH",
            "A380X_RMP_2_VHF_TX_1", "A380X_RMP_2_VHF_TX_2", "A380X_RMP_2_VHF_TX_3",
            "A380X_RMP_2_HF_TX_1", "A380X_RMP_2_HF_TX_2", "A380X_RMP_2_TEL_TX_1", "A380X_RMP_2_TEL_TX_2",
            "A380X_RMP_2_INT_TX_1", "A380X_RMP_2_CAB_TX_1", "A380X_RMP_2_PA_TX_1", "A380X_RMP_2_NAV_TX_1"
        };
        // (Radio Management Panel removed — the RMP is now the dedicated accessible RMP WINDOW,
        // Ctrl+Shift+R in input mode → FBWA380RmpForm, scraping A380X_RMP_1/2 + firing the keypad H-events.)
        p["Interior Lighting"] = new List<string>
        {
            "A380X_OVHD_ANN_LT_POSITION", "A32NX_OVHD_INTLT_ANN",
            "A32NX_LIGHTING_PRESET_LOAD", "A32NX_LIGHTING_PRESET_SAVE",
            // Passenger-cabin lighting (moved here from the flyPad Quick Controls, which
            // can't be set through the injected agent — see CLAUDE.md flyPad note).
            "CABIN_BRIGHTNESS_SET", "A32NX_CABIN_USING_AUTOBRIGHTNESS", "A32NX_CABIN_AUTOBRIGHTNESS"
        };
        p["Exterior Lighting"] = new List<string>
        {
            "LIGHT_BEACON", "LIGHT_STROBE", "LIGHT_NAV", "LIGHT_WING", "LIGHT_LOGO",
            "LIGHT_LANDING", "LIGHT_TAXI_OVHD"
        };

        p["Warnings"] = new List<string>
        {
            "PUSH_AUTOPILOT_MASTERAWARN_L", "PUSH_AUTOPILOT_MASTERAWARN_R",
            "PUSH_AUTOPILOT_MASTERCAUT_L", "PUSH_AUTOPILOT_MASTERCAUT_R",
            "A32NX_MASTER_WARNING", "A32NX_MASTER_CAUTION"
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

        p["Gear"] = new List<string> { "A32NX_GEAR_HANDLE_POSITION", "A32NX_LG_GRVTY_SWITCH_POS",
            "A32NX_LG_GRVTY_MASTER_SWITCH_GUARD", "A32NX_LG_GRVTY_SWITCH_GUARD_1", "A32NX_LG_GRVTY_SWITCH_GUARD_2" };
        // Computer-reset (CB) overhead panel — the 10 latching reset pushbuttons.
        p["Reset"] = _resetPanelVars.Select(t => t.key).ToList();
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
        p["Speed Brake"] = new List<string> { "A380X_MSFSBA_SPEEDBRAKE", "A380X_MSFSBA_SPEEDBRAKE_SLIDER", "A380X_MSFSBA_SPOILERS_ARM" };
        p["ECAM Control Panel"] = new List<string>
        {
            "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", "A32NX_BTN_ALL", "A32NX_BTN_ABNPROC", "A32NX_BTN_CL",
            "A32NX_BTN_CLR", "A32NX_BTN_CLR2", "A32NX_BTN_RCL", "A32NX_BTN_TOCONFIG",
            "A32NX_BTN_EMERCANC", "A32NX_BTN_UP", "A32NX_BTN_DOWN", "A32NX_BTN_MORE"
        };
        p["Weather Radar"] = new List<string>
        {
            "XMLVAR_A320_WeatherRadar_Sys", "XMLVAR_A320_WeatherRadar_Mode",
            "A32NX_SWITCH_RADAR_PWS_Position", "A32NX_RADAR_MULTISCAN_AUTO", "A32NX_RADAR_GCS_AUTO"
        };
        p["Transponder"] = new List<string>
        {
            // Only squawk code + IDENT are settable here (working stock events). The transponder
            // MODE is AUTO-managed by the FBW systems-host (Standby on ground / Mode C airborne)
            // and not externally settable — it is surfaced as the read-only auto-announcing
            // "Transponder Mode" read-out (XPNDR_STATE in d["Transponder"]). The AUTO/STBY +
            // ALT RPTG + TCAS toggles are EventBus-only -> the MFD SURV CONTROLS page (rarely
            // needed; AUTO + TA/RA are the correct VATSIM defaults). See the GetVariables note.
            "TRANSPONDER_CODE_SET", "XPNDR_IDENT_ON"
        };
        // "Radios" (stock COM standby-set + swap) REMOVED — the FBW A380 ignores the stock
        // COM_STBY_RADIO_SET_HZ / COM*_RADIO_SWAP events (live-verified: setting COM1 standby
        // to 119.000 left it at 121.95). All radio tuning goes through the RMP, so the dedicated
        // RMP window (Ctrl+Shift+R) is the real interface. "RMP" stays as a read-only quick-glance.
        p["RMP"] = new List<string>
        {
            "A380X_RMP_1_STATE", "A380X_RMP_2_STATE", "A380X_RMP_3_STATE"
        };
        p["Minimums"] = new List<string>();
        // Cockpit Door lock + door video live in the unified p["Cockpit"] now (see above).
        // Cabin Ready is read-only (auto-announced via Mon) — surfaced as a status readout in
        // d["Cockpit"] (display section below), not a settable control.

        // ---- Ground Services panels REMOVED (everything ground/handling via the flyPad) ----
        // No "Doors", "Ground Equipment" or "Ground Services" panels: the jetway / stairs /
        // fuel-truck / baggage / catering ACTIONS are done on the flyPad Ground page, not here.
        // The door / jetway-motion / chocks / cones / external-power STATE still auto-announces
        // on change (those vars stay registered + IsAnnounced) — there's just no panel to Tab to.

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
        // (Brake Fan control removed — not modelled on the A380 dev build; see the Brakes section.)
        p["Recorder and Misc"].AddRange(new[]
        {
            "A32NX_RCDR_TEST", "A32NX_ELT_TEST_RESET", "A32NX_DFDR_EVENT_ON",
            "A32NX_RAIN_REPELLENT_LEFT_ON", "A32NX_RAIN_REPELLENT_RIGHT_ON",
            "A32NX_OVHD_NSS_DATA_TO_AVNCS_TOGGLE", "A32NX_NSS_MASTER_OFF"
        });
        p["GPWS"] = new List<string>
        {
            "A32NX_GPWS_SYS_OFF", "A32NX_GPWS_GS_OFF", "A32NX_GPWS_FLAPS_OFF",
            "A32NX_GPWS_TERR_OFF", "A32NX_GPWS_FLAPS3", "A32NX_GPWS_TEST"
        };
        // "PFD" is NOT a navigable control panel — it's the variable set the PFD
        // window (ShowPFD hotkey) requests/reads. Intentionally absent from
        // GetPanelStructure so it isn't shown as a UI panel.
        // PFD / ND are status-box-only display panels (the read-out lives in
        // GetPanelDisplayVariables); no interactive controls.
        p["PFD"] = new List<string>();
        p["ND"] = new List<string>();
        p["Interior Lighting"].Add("A380X_OVHD_EXTLT_STBY_COMPASS_ICE_IND_SWITCH_POS");
        // (EFIS filter/overlay/baro-unit/OANS folded into the per-side EFIS panels above.)
        p["ECAM Control Panel"].AddRange(new[] { "A32NX_BTN_CHECK_LH", "A32NX_BTN_CHECK_RH" });
        p["Transponder"].Add("A32NX_DCDU_ATC_MSG_ACK");

        // ---- new panels ----
        p["ISIS"] = new List<string> { "A32NX_ISIS_LS_ACTIVE", "A32NX_ISIS_BARO_MODE", "A32NX_ISIS_BARO_UNIT_INHG" };
        p["Wipers"] = new List<string> { "WIPER_LEFT", "WIPER_RIGHT" };
        p["Speeds"] = new List<string>();
        // KCCU (keyboard/cursor control unit) is the MCDU's input device — it is
        // driven through the MCDU form (Coherent agent), not as a standalone
        // control panel, so it is intentionally NOT exposed as a panel here.
        // (Aircraft-preset loading + pushback panels REMOVED per user request — both are done
        // from the flyPad. MSFSBA auto-announces the preset LOAD PROGRESS instead, see the
        // A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS branch in ProcessSimVarUpdate.)

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
        foreach (var bus in new[] { "AC_1", "AC_2", "AC_3", "AC_4", "AC_ESS", "AC_ESS_SHED", "247XP",
                                    "DC_1", "DC_2", "DC_ESS", "247PP", "DC_HOT_1", "DC_HOT_2", "DC_HOT_3", "DC_HOT_4", "DC_GND_FLT_SVC" })
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
        press.Add("A32NX_PRESS_CABIN_ALTITUDE_B1");
        press.Add("A32NX_PRESS_CABIN_VS_B1");
        press.Add("A32NX_PRESS_CABIN_DELTA_PRESSURE_B1");
        press.Add("A32NX_FM1_LANDING_ELEVATION");
        for (int v = 1; v <= 4; v++) press.Add($"A32NX_PRESS_OUTFLOW_VALVE_{v}_OPEN_PERCENTAGE_B1");
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
            "A32NX_AUTOTHRUST_TLA:3", "A32NX_AUTOTHRUST_TLA:4",
            "A32NX_AUTOTHRUST_THRUST_LIMIT"   // EWD green N1-limit % for the current mode
        };

        // Flaps handle is a settable, auto-announced combo in the panel; the speed-brake
        // readouts moved to their own panel below.
        d["Speed Brake"] = new List<string> { "A32NX_SPOILERS_HANDLE_POSITION", "A32NX_SPOILERS_ARMED" };
        // Exterior lights are now On/Off combos in the panel itself (auto-announced),
        // so they are NOT duplicated as read-only display variables here.
        d["RMP"] = new List<string>
        {
            // Reliable STOCK COM freqs (the FBW_RMP_FREQUENCY L:vars read ~19 MHz garbage).
            "COM_ACTIVE_1", "COM_STANDBY_1",
            "COM_ACTIVE_2", "COM_STANDBY_2",
            "COM_ACTIVE_3", "COM_STANDBY_3"
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
        // pilot hears auto-announced now also reads in the panel. (The preselect QNH
        // read-out was removed — see the baro-preselect note above.)
        d["EFIS Captain"] = new List<string> { "A32NX_FCU_LEFT_EIS_BARO_HPA" };
        d["EFIS First Officer"] = new List<string> { "A32NX_FCU_RIGHT_EIS_BARO_HPA" };
        // d["Radios"] removed with the dead "Radios" panel — the RMP active/standby freqs are
        // in d["RMP"] (FBW L:vars) and the RMP window.
        d["Transponder"] = new List<string> { "XPNDR_CODE", "XPNDR_STATE", "A32NX_DCDU_ATC_MSG_WAITING" };
        // Minimums are ARINC429 words — TryGetDisplayOverride decodes them to
        // "200 feet" / "Not set" (the raw word reads ~4.29e9). They also
        // auto-announce when set on the MCDU PERF APPR page.
        d["Minimums"] = new List<string> { "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE", "A32NX_FM1_DECISION_HEIGHT" };

        // ---- A32NX shared gap readouts ----
        d["Autobrake"].AddRange(new[]
        {
            "A32NX_BRAKES_HOT",
            "A32NX_HYD_BRAKE_NORM_LEFT_PRESS", "A32NX_HYD_BRAKE_NORM_RIGHT_PRESS",
            "A32NX_HYD_BRAKE_ALTN_LEFT_PRESS", "A32NX_HYD_BRAKE_ALTN_RIGHT_PRESS", "A32NX_HYD_BRAKE_ALTN_ACC_PRESS"
        });
        d["Gear"].Add("A32NX_GEAR_LEVER_LOCKED");   // master guard moved to p["Gear"] as a control
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
        d["Autobrake"].Add("A32NX_AUTOBRAKES_DECEL_LIGHT");   // DECEL light (auto-announced)
        // (Ground Equipment + Doors read-out panels REMOVED — ground handling is flyPad-only.
        // The chocks / cones / jetway-motion / external-power / door vars stay registered and
        // IsAnnounced, so every change still auto-announces; there's just no panel to read.)

        // Clock readouts (the chrono + elapsed-time fields shown read-only in the
        // Clock panel; the controls live in p["Clock"]).
        d["Clock"] = new List<string> { "A32NX_CHRONO_ELAPSED_TIME", "A32NX_CHRONO_ET_ELAPSED_TIME" };
        d["Cockpit"] = new List<string> { "A32NX_CABIN_READY" };
        // ISIS standby-instrument snapshot (attitude/heading/speed/altitude/baro +
        // ILS), decoded in TryGetDisplayOverride. Standby simvars read in DEGREES on
        // the A380 (registered with "degrees" units), unlike the A320 (radians).
        d["ISIS"] = new List<string>
        {
            "PLANE PITCH DEGREES", "PLANE BANK DEGREES", "PLANE HEADING DEGREES MAGNETIC",
            "AIRSPEED INDICATED", "INDICATED ALTITUDE",
            "A32NX_ISIS_BARO_MODE", "A32NX_ISIS_BUGS_ACTIVE"   // LS moved to p["ISIS"] as a control
        };
        // PFD accessible snapshot — FMA modes + armed, autothrust, approach capability,
        // attitude/heading/speed/altitude, and the PFD message line. Single status box.
        d["PFD"] = new List<string>
        {
            "A32NX_FMA_VERTICAL_MODE", "A32NX_FMA_VERTICAL_ARMED",
            "A32NX_FMA_LATERAL_MODE", "A32NX_FMA_LATERAL_ARMED",
            "A32NX_AUTOTHRUST_MODE", "A32NX_AUTOTHRUST_STATUS", "A32NX_APPROACH_CAPABILITY",
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE",
            "PLANE PITCH DEGREES", "PLANE BANK DEGREES", "PLANE HEADING DEGREES MAGNETIC",
            "AIRSPEED INDICATED", "INDICATED ALTITUDE",
            "A32NX_PFD_MSG_SET_HOLD_SPEED", "A32NX_PFD_MSG_TD_REACHED",
            "A32NX_PFD_MSG_CHECK_SPEED_MODE", "A32NX_PFD_LINEAR_DEVIATION_ACTIVE", "A32NX_PFD_TARGET_ALTITUDE",
            // Source-confirmed PFD additions: weight/CG, takeoff V-speeds, Mach, track, ILS.
            "PFD_GROSS_WEIGHT", "A32NX_AIRFRAME_GW_CG_PERCENT_MAC",
            "PFD_V1", "PFD_VR", "PFD_V2", "PFD_MACH", "PFD_TRACK",
            "PFD_RA", "PFD_VS", "PFD_TRANS_ALT", "PFD_TRANS_LVL",
            "FCU_SEL_ALT", "FCU_SEL_HDG", "PFD_SAT", "PFD_TAT",
            "A32NX_BETA_TARGET", "A32NX_TCAS_VSPEED_GREEN", "A32NX_TCAS_VSPEED_RED",
            "PFD_ILS_FREQ", "PFD_ILS_DME", "A32NX_FM_LS_COURSE", "MARKER_BEACON",
            "PFD_VMAX", "PFD_VLS", "PFD_VALPHAPROT", "PFD_VALPHAMAX", "PFD_VSW",
            "PFD_GREENDOT", "PFD_V3", "PFD_V4", "PFD_VFENEXT",
            // Target/preselect speeds + selected V/S + expedite + flight directors + autobrake.
            "A32NX_SPEEDS_MANAGED_PFD", "A32NX_SpeedPreselVal", "A32NX_MachPreselVal",
            "A32NX_AUTOPILOT_VS_SELECTED", "A32NX_FMA_EXPEDITE_MODE", "FD_1", "FD_2",
            "A32NX_AUTOBRAKES_ARMED_MODE", "PFD_AUTOLAND"
        };
        // ND accessible snapshot — mode/range, TO waypoint (decoded ident + distance/
        // bearing/ETA), cross-track, RNP, and ILS LOC/GS validity + deviation.
        d["ND"] = new List<string>
        {
            "A32NX_EFIS_L_ND_MODE", "A32NX_EFIS_L_ND_RANGE",
            "A32NX_EFIS_L_TO_WPT_IDENT_0", "A32NX_EFIS_L_TO_WPT_DISTANCE",
            "A32NX_EFIS_L_TO_WPT_BEARING", "A32NX_EFIS_L_TO_WPT_ETA",
            "A32NX_FG_CROSS_TRACK_ERROR", "A32NX_FMGC_L_RNP",
            "A32NX_RADIO_RECEIVER_LOC_IS_VALID", "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
            "A32NX_RADIO_RECEIVER_GS_IS_VALID", "A32NX_RADIO_RECEIVER_GS_DEVIATION",
            // Tuned nav radios (frequencies + DME). The tuned-station IDENTS are read by the
            // Output+N "nav radio" hotkey (NAV IDENT string struct), which the numeric display
            // pipeline can't carry.
            "ND_VOR1_FREQ", "ND_VOR1_DME", "ND_VOR2_FREQ", "ND_VOR2_DME",
            "ND_ADF1_FREQ", "ND_ADF2_FREQ",
            // Velocities + wind + heading reference (ARINC, decoded; "not available" on the ground).
            "A32NX_ADIRS_IR_1_GROUND_SPEED", "A32NX_ADIRS_ADR_1_TRUE_AIRSPEED",
            "A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR", "A32NX_ADIRS_IR_1_WIND_SPEED_BNR",
            "A32NX_FMGC_TRUE_REF"
        };
        d["Oxygen"] = new List<string> { "A32NX_OXYGEN_TMR_RESET_FAULT" };
        d["Calls"] = new List<string> { "A32NX_SLIDES_ARMED", "A32NX_EVAC_COMMAND_FAULT" };
        // The ECP "Status display" box shows the SELECTED SD page's live CONTENT,
        // scraped on each page switch (see RefreshSdPageDisplayAsync + the
        // TryGetDisplayOverride case for this var). The page name + rows render there;
        // the old A32NX_SD_MORE_SHOWN "more flag" line was dropped (it read as the
        // useless "SD more: no").
        d["ECAM Control Panel"] = new List<string> { "A32NX_ECAM_SD_CURRENT_PAGE_INDEX" };
        d["Wipers"] = new List<string> { "WIPER_LEFT", "WIPER_RIGHT" };
        d["Speeds"] = new List<string> { "A32NX_SPEEDS_VLS", "A32NX_SPEEDS_VAPP", "A32NX_SPEEDS_GD", "A32NX_SPEEDS_F", "A32NX_SPEEDS_S" };

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
            "A32NX_NOSE_WHEEL_POSITION", "A32NX_TILLER_HANDLE_POSITION",
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
            // Phase 4 parity with the A320: A/THR (Ctrl+J) + LOC (Ctrl+L). The A380 WASM
            // handles the same FBW input events (SimConnectInterface.cpp).
            [HotkeyAction.ToggleAutothrust] = "A32NX.FCU_ATHR_PUSH",
            [HotkeyAction.ToggleLocalizer] = "A32NX.FCU_LOC_PUSH",
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
    // Last announced RMP active VHF frequency per channel ("1"/"2"/"3"), raw Hz — for the
    // change-detect + silent-baseline of the active-frequency auto-announce in ProcessSimVarUpdate.
    private readonly Dictionary<string, double> _rmpActiveFreq = new();
    // Stock COM active/standby freq (MHz) per channel "1"/"2"/"3" — last-announced, for the
    // reliable VHF freq auto-announce (the FBW_RMP_FREQUENCY L:vars read garbage). + last squawk.
    private readonly Dictionary<string, double> _comActiveFreq = new();
    private readonly Dictionary<string, double> _comStandbyFreq = new();
    private int _lastSquawkBcd = -1;
    // Squawk the RMP window just set via XPNDR_SET — the XPNDR_CODE monitor SKIPS announcing this exact
    // code (the window already spoke it), so a window set doesn't double-announce. Consumed on match.
    private int _formSetSquawkBcd = -1;
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

    // FMA armed-mode decode. The legacy A32NX_FMA_{VERTICAL|LATERAL}_ARMED bitmasks
    // (bit 0 = ALT, live-verified = 1 at ready-for-taxi) are decoded to mode names so
    // arming a mode speaks "Altitude armed" / "NAV armed" — matching the A32NX (and
    // decoding it, vs the A32NX's old raw-number announce). Bits per the FBW a32nx-api.
    private int _prevVertArmed = -1, _prevLatArmed = -1;
    private string _lastFlightPhaseA380 = "";
    // ND TO-waypoint ident: packed 6 bits/char, 8 chars/word (low bits first),
    // char = code + 31. Cached from ProcessSimVarUpdate; decoded in TryGetDisplayOverride.
    private double _ndIdent0, _ndIdent1;
    // ---- Doors (read-only status + auto-announce; NO settable combos — the user opens/closes
    // via the flyPad). ALL 18 modelled doors, authoritative ip→door mapping from the FBW SD
    // DoorPage.tsx: 16 passenger doors = stock SimVar INTERACTIVE POINT OPEN:0..15 (0..1 anim
    // fraction, open > 0.05); 2 cargo doors = FBW L:vars A32NX_{FWD,AFT}_DOOR_CARGO_LOCKED
    // (1 = locked = CLOSED → inverted). AVNCS + BULK cargo are hardcoded-closed in FBW (not
    // modelled → not surfaced). (Key, spoken Name, backing Var, IsSimVar, CargoLocked-inverted). ----
    private static readonly (string Key, string Name, string Var, bool IsSimVar, bool CargoLocked)[] _doorDefs = new[]
    {
        ("A380X_MSFSBA_DOOR_0",  "Main Door 1 Left",   "INTERACTIVE POINT OPEN:0",  true,  false),
        ("A380X_MSFSBA_DOOR_1",  "Main Door 1 Right",  "INTERACTIVE POINT OPEN:1",  true,  false),
        ("A380X_MSFSBA_DOOR_2",  "Main Door 2 Left",   "INTERACTIVE POINT OPEN:2",  true,  false),
        ("A380X_MSFSBA_DOOR_3",  "Main Door 2 Right",  "INTERACTIVE POINT OPEN:3",  true,  false),
        ("A380X_MSFSBA_DOOR_4",  "Main Door 3 Left",   "INTERACTIVE POINT OPEN:4",  true,  false),
        ("A380X_MSFSBA_DOOR_5",  "Main Door 3 Right",  "INTERACTIVE POINT OPEN:5",  true,  false),
        ("A380X_MSFSBA_DOOR_6",  "Main Door 4 Left",   "INTERACTIVE POINT OPEN:6",  true,  false),
        ("A380X_MSFSBA_DOOR_7",  "Main Door 4 Right",  "INTERACTIVE POINT OPEN:7",  true,  false),
        ("A380X_MSFSBA_DOOR_8",  "Main Door 5 Left",   "INTERACTIVE POINT OPEN:8",  true,  false),
        ("A380X_MSFSBA_DOOR_9",  "Main Door 5 Right",  "INTERACTIVE POINT OPEN:9",  true,  false),
        ("A380X_MSFSBA_DOOR_10", "Upper Door 1 Left",  "INTERACTIVE POINT OPEN:10", true,  false),
        ("A380X_MSFSBA_DOOR_11", "Upper Door 1 Right", "INTERACTIVE POINT OPEN:11", true,  false),
        ("A380X_MSFSBA_DOOR_12", "Upper Door 2 Left",  "INTERACTIVE POINT OPEN:12", true,  false),
        ("A380X_MSFSBA_DOOR_13", "Upper Door 2 Right", "INTERACTIVE POINT OPEN:13", true,  false),
        ("A380X_MSFSBA_DOOR_14", "Upper Door 3 Left",  "INTERACTIVE POINT OPEN:14", true,  false),
        ("A380X_MSFSBA_DOOR_15", "Upper Door 3 Right", "INTERACTIVE POINT OPEN:15", true,  false),
        ("A380X_MSFSBA_CARGO_FWD", "Forward Cargo Door", "A32NX_FWD_DOOR_CARGO_LOCKED", false, true),
        ("A380X_MSFSBA_CARGO_AFT", "Aft Cargo Door",     "A32NX_AFT_DOOR_CARGO_LOCKED", false, true),
        // Ground-SERVICE interactive points OUTSIDE the 0..15 door range that the flyPad Ground
        // page drives (A380Services.tsx): point 16 = the front cargo hatch the BAGGAGE truck opens,
        // point 18 = the FUEL truck / fuelling connection. Same INTERACTIVE POINT OPEN mechanism as
        // the doors, so they ride the proven once-per-transition announce — a flyPad baggage/fuel
        // call is now spoken ("Front Cargo Door: Open", "Fuel Truck: Open"). (Catering/jet-bridge/
        // stairs attach to cabin doors 0/2/9 which already announce; GPU = EXT PWR AVAIL announce.)
        ("A380X_MSFSBA_DOOR_16", "Front Cargo Door (Baggage)", "INTERACTIVE POINT OPEN:16", true, false),
        ("A380X_MSFSBA_DOOR_18", "Fuel Truck",                 "INTERACTIVE POINT OPEN:18", true, false),
    };
    private readonly Dictionary<string, bool> _doorOpen = new();
    private int _presetBucket = -1;   // last-announced preset-load progress 10%-bucket (-1 = idle)
    private bool _betaTargetActive;   // A32NX_BETA_TARGET_ACTIVE, cached for the beta-target decode
    private static string UnpackSixBitIdent(double w0, double w1)
    {
        double[] words = { w0, w1 };
        string s = "";
        for (int i = 0; i < words.Length * 8; i++)
        {
            int code = (int)(words[i / 8] / Math.Pow(2, (i % 8) * 6)) & 0x3F;
            if (code > 0) s += (char)(code + 31);
        }
        return s.Trim();
    }
    private static readonly (int bit, string name)[] _vertArmedBits =
        { (1, "Altitude"), (2, "Altitude constraint"), (4, "Climb"), (8, "Descent"), (16, "Glideslope"), (32, "Final"), (64, "TCAS") };
    private static readonly (int bit, string name)[] _latArmedBits = { (1, "NAV"), (2, "Localizer") };
    private static string DecodeArmedModes(int v, (int bit, string name)[] bits)
    {
        var names = new List<string>();
        foreach (var b in bits) if ((v & b.bit) != 0) names.Add(b.name);
        return string.Join(", ", names);
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Cache the ND TO-waypoint packed-word halves for the ND status box decode
        // (no announcement; fall through to normal processing).
        if (varName == "A32NX_EFIS_L_TO_WPT_IDENT_0") _ndIdent0 = value;
        else if (varName == "A32NX_EFIS_L_TO_WPT_IDENT_1") _ndIdent1 = value;
        else if (varName == "A32NX_BETA_TARGET_ACTIVE") _betaTargetActive = value > 0.5;

        // RMP active VHF frequency — auto-announce on change so a blind pilot hears the new
        // ACTIVE frequency after a transfer/swap (the standby stays an on-request panel readout,
        // so this is the single non-duplicate call-out). The register is raw Hz; range-gate to a
        // valid VHF band (118.000–136.975 MHz) so the UNINITIALISED value the FBW RMP holds while
        // unpowered (reads ~19 MHz) is cached silently and never spoken. Honours the Ctrl+M mute.
        // RELIABLE VHF freq auto-announce off the stock COM simvars (the FBW_RMP_FREQUENCY L:vars
        // read garbage). value is MHz; gate to the VHF band so an uninitialised 0 stays silent.
        // Active change = "VHF n active 121.900" (after a swap); standby change = "VHF n standby …"
        // (the autocomplete/load read-back). prev>0 skips the first-seen baseline. Honours Ctrl+M.
        if (varName.StartsWith("COM_ACTIVE_", StringComparison.Ordinal))
        {
            string ch = varName.Substring("COM_ACTIVE_".Length);
            bool plausible = value >= 118.0 && value <= 137.0;
            bool changed = !_comActiveFreq.TryGetValue(ch, out var prev) || Math.Abs(prev - value) > 0.0004;
            _comActiveFreq[ch] = value;
            if (plausible && changed && prev > 0
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                announcer.Announce($"VHF {ch} active {value:0.000}");
            return true;
        }
        if (varName.StartsWith("COM_STANDBY_", StringComparison.Ordinal))
        {
            string ch = varName.Substring("COM_STANDBY_".Length);
            bool plausible = value >= 118.0 && value <= 137.0;
            bool changed = !_comStandbyFreq.TryGetValue(ch, out var prev) || Math.Abs(prev - value) > 0.0004;
            _comStandbyFreq[ch] = value;
            if (plausible && changed && prev > 0
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                announcer.Announce($"VHF {ch} standby {value:0.000}");
            return true;
        }
        // Transponder squawk auto-announce: TRANSPONDER CODE:1 reads a BCD16 word; decode (same as
        // the display) and speak "Squawk 1234" whenever it changes. _lastSquawkBcd<0 skips the first.
        if (varName == "XPNDR_CODE")
        {
            int bcd = (int)Math.Round(value);
            if (bcd != _lastSquawkBcd)
            {
                bool first = _lastSquawkBcd < 0;
                bool formSet = bcd == _formSetSquawkBcd;   // the RMP window set this code and already spoke it
                _lastSquawkBcd = bcd;
                if (formSet) _formSetSquawkBcd = -1;        // consume
                if (!first && !formSet && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                    announcer.Announce($"Squawk {(bcd >> 12) & 0xF}{(bcd >> 8) & 0xF}{(bcd >> 4) & 0xF}{bcd & 0xF}");
            }
            return true;
        }
        if (varName.StartsWith("FBW_RMP_FREQUENCY_ACTIVE_", StringComparison.Ordinal))
        {
            string ch = varName.Substring("FBW_RMP_FREQUENCY_ACTIVE_".Length);
            double mhz = value / 1_000_000.0;
            bool plausible = mhz >= 118.0 && mhz <= 137.0;
            bool changed = !_rmpActiveFreq.TryGetValue(ch, out var prev) || Math.Abs(prev - value) > 0.5;
            _rmpActiveFreq[ch] = value;
            if (plausible && changed && prev != 0
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                announcer.Announce($"VHF {ch} active {mhz:0.000}");
            return true;
        }

        // Doors — read-only auto-announce. Passenger doors are INTERACTIVE POINT OPEN, a 0..1
        // animation fraction (open > 0.05); cargo doors are the inverted LOCKED L:var (1 = locked
        // = closed). Announce Open/Closed once per transition (honours the Ctrl+M mute).
        if (varName.StartsWith("A380X_MSFSBA_DOOR_", StringComparison.Ordinal)
            || varName.StartsWith("A380X_MSFSBA_CARGO_", StringComparison.Ordinal))
        {
            foreach (var dd in _doorDefs)
            {
                if (dd.Key != varName) continue;
                bool open = dd.CargoLocked ? value < 0.5 : value > 0.05;
                bool? prev = _doorOpen.TryGetValue(varName, out var pv) ? pv : null;
                _doorOpen[varName] = open;
                if (prev.HasValue && prev.Value != open
                    && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                {
                    // The fuel-truck service point (interactive point 18) reads more naturally as
                    // connected/disconnected than open/closed; doors + the cargo hatch keep open/closed.
                    string verb = varName == "A380X_MSFSBA_DOOR_18"
                        ? (open ? "connected" : "disconnected")
                        : (open ? "open" : "closed");
                    announcer.Announce($"{dd.Name} {verb}");
                }
                break;
            }
            return true;
        }

        // Aircraft-preset load progress (flyPad loads the preset; MSFSBA narrates it). The L:var
        // runs 0..1 while loading then resets to 0. Announce each 10% milestone once, "complete"
        // at 100%, and stay silent at idle (0). Honours the Ctrl+M mute.
        if (varName == "A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS")
        {
            int pct = value <= 1.0 ? (int)Math.Round(value * 100) : (int)Math.Round(value);
            pct = Math.Max(0, Math.Min(100, pct));
            if (pct <= 0) { _presetBucket = -1; return true; }   // idle / reset — silent
            if (!Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
            {
                if (pct >= 100)
                {
                    if (_presetBucket < 100) { _presetBucket = 100; announcer.Announce("Aircraft preset loading complete"); }
                }
                else
                {
                    int bucket = (pct / 10) * 10;
                    if (bucket > _presetBucket) { _presetBucket = bucket; announcer.Announce($"Aircraft preset loading {bucket} percent"); }
                }
            }
            return true;
        }

        // Speed-brake handle: a 0..1 fraction. Announce by 10% band (with Retracted/Full
        // at the ends) so a steady lever doesn't spam, but movement is spoken. Silent
        // baseline on the first sample. (Speculative A380 addition.)
        if (varName == "A32NX_SPOILERS_HANDLE_POSITION")
        {
            int band = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, value)) * 10.0);
            if (_lastSpoilerBand < 0) { _lastSpoilerBand = band; return true; }
            if (band != _lastSpoilerBand)
            {
                _lastSpoilerBand = band;
                string phrase = band == 0 ? "Speed brake retracted"
                              : band == 10 ? "Speed brake full"
                              : $"Speed brake {band * 10} percent";
                announcer.Announce(phrase);
            }
            return true;
        }

        // FMA armed modes — decode the legacy bitmask and announce NEWLY-armed modes
        // on change (so arming ALT/NAV speaks "Altitude armed"/"NAV armed"). Parity
        // with the A32NX, which the A380 previously lacked (it was read-only).
        if (varName == "A32NX_FMA_VERTICAL_ARMED" || varName == "A32NX_FMA_LATERAL_ARMED")
        {
            bool vert = varName == "A32NX_FMA_VERTICAL_ARMED";
            int iv = (int)Math.Round(value);
            int prev = vert ? _prevVertArmed : _prevLatArmed;
            if (vert) _prevVertArmed = iv; else _prevLatArmed = iv;
            if (prev >= 0 && (iv & ~prev) != 0
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
            {
                string nm = DecodeArmedModes(iv & ~prev, vert ? _vertArmedBits : _latArmedBits);
                if (!string.IsNullOrEmpty(nm))
                    foreach (var one in nm.Split(new[] { ", " }, StringSplitOptions.None))
                        announcer.Announce($"{one} armed");
            }
            return true;
        }
        // Flight phase — match the A32NX "Entering X phase" wording (was the generic
        // "Flight Phase: X" via the monitor).
        if (varName == "A32NX_FMGC_FLIGHT_PHASE")
        {
            if (_varCache != null && _varCache.TryGetValue(varName, out var fpDef)
                && fpDef.ValueDescriptions != null && fpDef.ValueDescriptions.TryGetValue(value, out var phase)
                && _lastFlightPhaseA380 != phase)
            {
                _lastFlightPhaseA380 = phase;
                if (!Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                    announcer.Announce($"Entering {phase} phase");
            }
            return true;
        }
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
        // relying on the generic indexed-simvar path. SEED SILENTLY on the first read
        // per GPU (prev < 0): the global timed first-detect grace can expire before all
        // four AVAIL vars first arrive, which made MSFSBA call out "External Power 1..4"
        // on startup. Now only a genuine post-startup connect/disconnect speaks. Ctrl+M
        // mute honoured.
        if (varName.StartsWith("A380X_GND_GPU_AVAIL_", StringComparison.Ordinal)
            && int.TryParse(varName.AsSpan("A380X_GND_GPU_AVAIL_".Length), out int gpuN)
            && gpuN >= 1 && gpuN <= 4)
        {
            int now = value > 0.5 ? 1 : 0;
            int prev = _gpuAvail[gpuN - 1];
            _gpuAvail[gpuN - 1] = now;
            if (prev >= 0 && prev != now
                && !Settings.SettingsManager.Current.A380DisabledMonitorVariables.Contains(varName))
                announcer.Announce(now == 1 ? $"External Power {gpuN} available" : $"External Power {gpuN} disconnected");
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

        // Cache gross weight (kg, stock) + CG (%MAC, FBW L-var) silently for the
        // W / Shift+W readouts. Both monitored continuously; the hotkeys read these
        // caches and speak immediately (no async request, no flag timing).
        if (varName == "GW_KG_CACHE") { _gwKgCache = value; return true; }
        if (varName == "A32NX_AIRFRAME_GW_CG_PERCENT_MAC") { _gwCgMac = value; return true; }

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
                // SEED SILENTLY on the first read (last == -1) so MSFSBA doesn't call out
                // "Captain/First officer altimeter ..." on startup; only a genuine later
                // knob turn speaks.
                bool first = (capt ? _lastBaroL : _lastBaroR) < 0;
                if (capt) _lastBaroL = hpa; else _lastBaroR = hpa;
                if (!first && (capt ? _baroStdL : _baroStdR) != true)
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
                    // A32NX_AUTOPILOT_SPEED_SELECTED holds the target DIRECTLY: a mach number
                    // when < 1 (e.g. 0.82), otherwise the speed already in KNOTS (e.g. 220 = 220 kt).
                    // It is NOT an SI velocity — the earlier ×1.943844 m/s→kt conversion was wrong
                    // and reported 220 kt as "428 knots" (220 × 1.943844). Live-verified airborne:
                    // the L:var read = 220 with IAS 220. So announce knots verbatim, no scaling.
                    spoken = _pSpdVal.Value < 10
                        ? $"FCU speed mach {_pSpdVal.Value:0.00}, selected"
                        : $"FCU speed {_pSpdVal.Value:000} knots, selected";
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
        // Crew SEAT toggle button (up/down/fwd/aft per pilot): press to start moving that way,
        // press again to stop (+ speak the position); opposite direction reverses. value>0.5 is the
        // press edge (RenderAsButton click sends 1); ignore anything else.
        if (_seatButtonMap.TryGetValue(varKey, out var seatBtn) && value > 0.5)
        {
            ToggleSeatMotor(seatBtn.PosVar, seatBtn.Dir, simConnect, announcer);
            return true;
        }
        // Continuous-axis SLIDERS (cockpit seats, armrests, sunshades, forward visors, fine
        // speed-brake) are FBW L:vars. Don't SNAP them to the target in one write — the 3-D
        // model jumps there and you only hear a single "tick" of the motor. A real motorised
        // seat moves gradually while you hold the switch, so we RAMP the L:var toward the
        // target a few units per 40 ms (calc path, on the UI thread). The FBW then plays the
        // sustained motor sound + smooth animation. (Writing via the calculator path also
        // avoids SetLVar's data-def write, which is unreliable for FBW L:vars.)
        if (varDef.RenderAsSlider)
        {
            RampSliderTo(varDef.Name, value, simConnect);
            return true;
        }
        // FCU SPD/MACH toggle from a panel button: the legacy dotted event is inert on the A380's
        // new FCU — switch via the stock K-events instead (see SpdMachToggleRpn). Then re-read.
        if (varKey == "A32NX.FCU_SPD_MACH_TOGGLE_PUSH")
        {
            simConnect.ExecuteCalculatorCode(SpdMachToggleRpn);
            RequestFCUSpeedWithStatus(simConnect);
            return true;
        }
        // Fire Test / Cargo Smoke Test (HOLD on/off tests). Setting ON triggers the fire
        // MASTER WARNING + the continuous repetitive chime (CRC) aural. Writing the var 0
        // ends the test, but the CRC can keep sounding until the master warning is
        // acknowledged — so on TEST OFF, also pulse the (correctly-spelled) MASTERAWARN
        // acknowledge to guarantee the "beep beep beep" cancels. Write via the calc path.
        if (varKey == "A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED" || varKey == "A32NX_FIRE_TEST_CARGO")
        {
            int on = value > 0.5 ? 1 : 0;
            simConnect.ExecuteCalculatorCode($"{on} (>L:{varKey})");
            if (on == 0)
            {
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_AUTOPILOT_MASTERAWARN_L)");
                simConnect.ExecuteCalculatorCode("0 (>L:PUSH_AUTOPILOT_MASTERAWARN_L)");
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_AUTOPILOT_MASTERAWARN_R)");
                simConnect.ExecuteCalculatorCode("0 (>L:PUSH_AUTOPILOT_MASTERAWARN_R)");
            }
            announcer.Announce(varKey == "A32NX_FIRE_TEST_CARGO"
                ? (on == 1 ? "Cargo smoke test on" : "Cargo smoke test off")
                : (on == 1 ? "Fire test on" : "Fire test off"));
            return true;
        }
        // System Display PAGE combo: drive the SD to the chosen page, then scrape that
        // page's decoded content off the real SD view INTO the panel "Status display"
        // box (no separate window). The combo's own value change announces the page
        // NAME; the CONTENT populates the box silently and updates on every page switch
        // — NO auto-speech of the content, no manual refresh.
        if (varKey == "A32NX_ECAM_SD_CURRENT_PAGE_INDEX")
        {
            int idx = (int)Math.Round(value);
            // 16 = our synthetic "Upper E/WD" option — scrape the E/WD view instead of an
            // SD page. Still record the combo value (so the box header reads "Upper E/WD"
            // and the selection persists); the real SD view ignores the out-of-range index.
            // UNIQUE-prefix the write ("{seq} 0 *" pushes 0, discarded): re-selecting a page
            // you already visited (e.g. ELEC -> HYD -> ELEC) sends an IDENTICAL calc string,
            // which MobiFlight de-duplicates -> the write never re-fires and the real SD page
            // doesn't switch back (the scraped C/B / STATUS / VIDEO pages then show stale text).
            simConnect.ExecuteCalculatorCode($"{++_sdWriteSeq} 0 * {idx} (>L:{varKey})");
            RefreshSdPageDisplayAsync(simConnect, idx, ewd: idx == 16);
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
        // Nosewheel-steering PEDAL DISCONNECT. The public L:var A32NX_TILLER_PEDAL_DISCONNECT
        // is NOT consumed by the FBW NWS systems (live-verified: writing 1 sticks and is
        // never read/reset), so the old momentary-button pulse on it did nothing. FBW maps
        // the disconnect to the otherwise-unused stock event TOGGLE_WATER_RUDDER -> internal
        // aspect (nose_wheel_steering.rs). Fire that instead. (Held in the real cockpit; a
        // single momentary fire is the accessible equivalent — confirm effect on a taxi test.)
        if (varKey == "A32NX_TILLER_PEDAL_DISCONNECT")
        {
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode("(>K:TOGGLE_WATER_RUDDER)");
                announcer.Announce("Nosewheel steering pedal disconnect");
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
        // Speed brake: synthetic Retracted/Half/Full combo -> stock SPOILERS_SET
        // (0 / 8192 / 16383), mirroring the flaps lever. (Speculative — stock event.)
        if (varKey == "A380X_MSFSBA_SPEEDBRAKE")
        {
            int pos = Math.Max(0, Math.Min(2, (int)Math.Round(value)));
            int[] axis = { 0, 8192, 16383 };
            simConnect.ExecuteCalculatorCode($"{axis[pos]} (>K:SPOILERS_SET)");
            return true;
        }
        // Speed-brake FINE slider — the TrackBar already maps 0-100% to 0-16383; fire SPOILERS_SET.
        if (varKey == "A380X_MSFSBA_SPEEDBRAKE_SLIDER")
        {
            int axis = Math.Max(0, Math.Min(16383, (int)Math.Round(value)));
            simConnect.ExecuteCalculatorCode($"{axis} (>K:SPOILERS_SET)");
            return true;
        }
        // Ground-spoiler arm: synthetic Disarm/Arm combo -> SPOILERS_ARM_OFF / _ON.
        if (varKey == "A380X_MSFSBA_SPOILERS_ARM")
        {
            simConnect.ExecuteCalculatorCode(value > 0.5 ? "(>K:SPOILERS_ARM_ON)" : "(>K:SPOILERS_ARM_OFF)");
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
        // (Baro preselect QNH was removed — the FBW var is display-only and not settable.)
        // (Doors + ground-service action buttons were removed from the panels — jet bridge,
        // stairs, fuel/baggage/catering and all door open/close are done on the flyPad now.
        // Their write handlers are gone with them; the ground STATE still auto-announces.)
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
            // Calculator path (not SetLVar — the data-def write is unreliable for FBW L:vars).
            simConnect.ExecuteCalculatorCode($"{(value - ts.Lo) / (ts.Hi - ts.Lo) * 300.0} (>L:{ts.Knob})");
            announcer.Announce($"{ts.Label} temperature set to {value:0} degrees");
            return true;
        }
        // Manual pressurization knobs — pass-through position write (calc path).
        if (varKey == "PRESS_MAN_ALT_SET" || varKey == "PRESS_MAN_VS_SET")
        {
            string knob = varKey == "PRESS_MAN_ALT_SET" ? "A32NX_OVHD_PRESS_MAN_ALTITUDE_KNOB" : "A32NX_OVHD_PRESS_MAN_VS_CTL_KNOB";
            simConnect.ExecuteCalculatorCode($"{value} (>L:{knob})");
            announcer.Announce($"Set to {value:0.0}");
            return true;
        }
        // Cabin lighting (passenger-cabin brightness). Calc-path write (the reliable FBW
        // L:var write). The brightness box also forces Auto-Brightness OFF so the manual
        // value actually takes effect; the auto combo is a plain 0/1 write.
        if (varKey == "CABIN_BRIGHTNESS_SET")
        {
            int b = (int)Math.Max(0, Math.Min(100, Math.Round(value)));
            simConnect.ExecuteCalculatorCode("0 (>L:A32NX_CABIN_USING_AUTOBRIGHTNESS)");
            simConnect.ExecuteCalculatorCode($"{b} (>L:A32NX_CABIN_MANUAL_BRIGHTNESS)");
            announcer.Announce($"Cabin brightness {b} percent");
            return true;
        }
        if (varKey == "A32NX_CABIN_USING_AUTOBRIGHTNESS")
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:A32NX_CABIN_USING_AUTOBRIGHTNESS)");
            return true;   // combo announces its own Off/On
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
            // Drive the real ignition state on all four engines (verified: this moves
            // the stock TURB ENG IGNITION SWITCH simvar the combo now reads back).
            for (int n = 1; n <= 4; n++) simConnect.SendEvent($"TURBINE_IGNITION_SWITCH_SET{n}", mode);
            // Also nudge the knob-position L:var the FWS/EWD reads, so the cockpit
            // display matches (the events above don't touch it).
            simConnect.ExecuteCalculatorCode($"{mode} (>L:XMLVAR_ENG_MODE_SEL)");
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
        // Prefix-less FBW L:vars (cockpit sliding windows + sunshades): their KEY is the
        // L:var but they lack the A32NX_/A380X_/FBW_ prefix the catch-all below keys on, so
        // route them through the calculator path explicitly.
        if (varKey == "CPT_SLIDING_WINDOW" || varKey == "FO_SLIDING_WINDOW"
            || varKey == "SUNSHADE_CPT_OPENING" || varKey == "SUNSHADE_FO_OPENING"
            || varKey == "CPT_OXY_FWD_OPENING" || varKey == "AFT_OXY_OPENING"
            || varKey == "A380_CPT_TABLE" || varKey == "A380_FO_TABLE"
            || varKey == "A380_CPT_FOOTREST" || varKey == "A380_FO_FOOTREST"
            || varKey == "A380_LGPIN_DOOR")
        {
            // Write the RAW value (not rounded) so 0..1 slider positions (sliding windows, side
            // sunshades) carry their fraction; the 0/1 combo items still write 0.0/1.0 fine.
            simConnect.ExecuteCalculatorCode($"{value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} (>L:{varKey})");
            return true;
        }
        // (The RMP keypad panel was removed — the RMP is now the dedicated accessible window,
        // FBWA380RmpForm, which calls SendRmpKey / SendRmpKeypad directly.)
        if (varKey.StartsWith("A32NX_", StringComparison.Ordinal)
            || varKey.StartsWith("A380X_", StringComparison.Ordinal)
            || varKey.StartsWith("FBW_", StringComparison.Ordinal))
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varKey})");
            return true;
        }
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    /// <summary>Fire a single RMP keypad key (press + release) on RMP <paramref name="rmp"/>.
    /// Used by the RMP window for the page selectors / line keys / swap / clear / digit entry.</summary>
    public void SendRmpKey(int rmp, string key, SimConnectManager s)
    {
        if (s == null || !s.IsConnected) return;
        if (rmp < 1 || rmp > 3) rmp = 1;   // 1=Captain, 2=First Officer, 3=Overhead (RMP 3)
        // CRITICAL: fire PRESS and RELEASE in ONE calculator call. MobiFlight's command channel
        // is a single shared buffer it reads once per frame — two back-to-back ExecuteCalculatorCode
        // calls (separate SetClientData writes) land in the same frame, so the RELEASE overwrites the
        // PRESS before the WASM module processes it. The key then never registers as pressed and the
        // page switch / digit / swap silently does nothing. One call = one buffer write = both events
        // run together (live-verified: page switch + digit entry only work this way through the app).
        s.ExecuteCalculatorCode($"(>H:RMP_{rmp}_{key}_PRESSED) (>H:RMP_{rmp}_{key}_RELEASED)");
    }

    /// <summary>Set the transponder squawk straight from the RMP window via the stock <c>XPNDR_SET</c>
    /// event (BCD16) — INDEPENDENT of the RMP SQWK page / keypad chain, which proved unreliable to drive
    /// externally. Live-verified: <c>0x{code} (&gt;K:XPNDR_SET)</c> changes TRANSPONDER CODE:1 regardless
    /// of which RMP page the cockpit shows. Speaks the code once and primes the XPNDR_CODE monitor to skip
    /// its duplicate announce. <paramref name="fourOctalDigits"/> must be 4 chars, each 0–7.</summary>
    public void SetSquawkFromForm(string fourOctalDigits, SimConnectManager s, ScreenReaderAnnouncer? ann)
    {
        if (s == null || !s.IsConnected || string.IsNullOrEmpty(fourOctalDigits) || fourOctalDigits.Length != 4) return;
        foreach (char c in fourOctalDigits) if (c < '0' || c > '7') return;   // squawk is octal
        _formSetSquawkBcd = Convert.ToInt32(fourOctalDigits, 16);   // "2222" -> 0x2222 (each nibble = a digit)
        s.ExecuteCalculatorCode($"0x{fourOctalDigits} (>K:XPNDR_SET)");
        ann?.AnnounceImmediate($"Squawk {fourOctalDigits}");
    }

    /// <summary>Fire an IDENT pulse via the stock <c>XPNDR_IDENT_ON</c> event (the same one the FBW
    /// TransponderController uses) — RMP-page-independent. Announced by the caller.</summary>
    public void SendTransponderIdent(SimConnectManager s)
    {
        if (s == null || !s.IsConnected) return;
        s.ExecuteCalculatorCode("(>K:XPNDR_IDENT_ON)");
    }

    /// <summary>Press a single RMP keypad key WITHOUT releasing it. Pair with
    /// <see cref="SendRmpKeyRelease"/> — used by the RMP window to HOLD the Clear key: held for
    /// &gt;1 s the FBW RMP does a FULL scratchpad clear (vs a single-digit backspace on a tap).
    /// A full clear is REQUIRED before typing a new frequency, because an invalid scratchpad entry
    /// blocks all further digits (<c>VhfComController.onDigitEntered</c> early-returns when invalid).</summary>
    public void SendRmpKeyPress(int rmp, string key, SimConnectManager s)
    {
        if (s == null || !s.IsConnected) return;
        if (rmp < 1 || rmp > 3) rmp = 1;   // 1=Captain, 2=First Officer, 3=Overhead (RMP 3)
        s.ExecuteCalculatorCode($"(>H:RMP_{rmp}_{key}_PRESSED)");
    }

    /// <summary>Release a single RMP keypad key (the up half of <see cref="SendRmpKeyPress"/>).</summary>
    public void SendRmpKeyRelease(int rmp, string key, SimConnectManager s)
    {
        if (s == null || !s.IsConnected) return;
        if (rmp < 1 || rmp > 3) rmp = 1;   // 1=Captain, 2=First Officer, 3=Overhead (RMP 3)
        s.ExecuteCalculatorCode($"(>H:RMP_{rmp}_{key}_RELEASED)");
    }

    // ===================================================================
    // FCU — ported from the A320 integration (same SET events; A380 readout
    // vars). Set dialogs send A32NX.FCU_*_SET; reads request value + managed
    // and announce via the pairing in ProcessSimVarUpdate.
    // ===================================================================
    private double? _pHdgVal, _pHdgMgd, _pSpdVal, _pSpdMgd, _pAltVal, _pAltMgd, _pVsVal, _pFpaVal, _pVsMode;
    private bool _reqHdg, _reqSpd, _reqAlt, _reqVs;
    private bool _reqFlaps, _reqGear, _reqBaro;
    private double _gwCgMac = -1;   // gross-weight CG %MAC (FBW L-var, cached)
    private double _gwKgCache = -1; // gross weight in kg (stock TOTAL WEIGHT, cached)

    // Spoken CG suffix for the gross-weight readouts. Empty (suppressed) when the CG
    // isn't available/sane, so the gross-weight readout never breaks or says "CG 0".
    private string CgMacPhrase() => (_gwCgMac > 5 && _gwCgMac < 60) ? $", center of gravity {_gwCgMac:0.0} percent MAC" : "";
    private int _lastSpoilerBand = -1;   // speed-brake handle band (10% steps) last announced
    private int _lastBaroL = -1, _lastBaroR = -1; // last announced EFIS baro (whole hPa)
    private bool? _baroStdL, _baroStdR; // last EFIS baro STD(true)/QNH(false) per side
    private bool? _baroInHgL, _baroInHgR; // last EFIS baro unit inHg(true)/hPa(false) per side
    private int _lastBaroMin = -2, _lastDh = -2; // last announced minimums (ft; -1 = none/NCD)

    // Computer-reset (CB) panel pushbuttons — (L:var, label). Shared by GetVariables
    // (registration) and BuildPanelControls (the "Reset" overhead panel). Source:
    // fbw-a380x behaviour/overhead/reset.xml + A380_COCKPIT Overhead_Reset_Panel.
    private static readonly (string key, string name)[] _resetPanelVars =
    {
        ("A32NX_RESET_PANEL_FMC_A", "Reset FMC A"), ("A32NX_RESET_PANEL_FMC_B", "Reset FMC B"),
        ("A32NX_RESET_PANEL_FMC_C", "Reset FMC C"), ("A32NX_RESET_PANEL_FWS1", "Reset FWS 1"),
        ("A32NX_RESET_PANEL_FWS2", "Reset FWS 2"), ("A32NX_RESET_PANEL_AESU1", "Reset AESU 1"),
        ("A32NX_RESET_PANEL_AESU2", "Reset AESU 2"), ("A32NX_RESET_PANEL_NSS_AVNCS", "Reset NSS Avionics"),
        ("A32NX_RESET_PANEL_NSS_FLT_OPS", "Reset NSS Flight Ops"), ("A32NX_RESET_PANEL_ARPT_NAV", "Reset Airport Nav"),
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
        // Crew-seat position read-outs: show a spoken-style band + percent, not a raw "50".
        if (_seatMotorMeta.TryGetValue(varKey, out var sm))
        {
            displayText = $"{SeatBand(value, sm.Hi, sm.Lo)}, {(int)Math.Round(value)} percent";
            return true;
        }
        // Doors: passenger = INTERACTIVE POINT OPEN 0..1 fraction (Open / Closed / mid-animation
        // %); cargo = inverted LOCKED L:var. Render cleanly instead of a raw "0.6" / "1".
        if (varKey.StartsWith("A380X_MSFSBA_DOOR_", StringComparison.Ordinal))
        {
            displayText = value > 0.95 ? "Open" : value < 0.05 ? "Closed" : $"{value * 100:0}% open";
            return true;
        }
        if (varKey.StartsWith("A380X_MSFSBA_CARGO_", StringComparison.Ordinal))
        {
            displayText = value < 0.5 ? "Open" : "Closed";   // LOCKED inverted
            return true;
        }
        // Transition LEVEL — ARINC429 word; engineering value is the flight level (60 = FL060).
        if (varKey == "PFD_TRANS_LVL")
        {
            var w = new SimConnect.Arinc429Word(value);
            displayText = (w.IsNormalOperation || w.IsFunctionalTest) ? $"flight level {w.Value:0}" : "not set";
            return true;
        }
        // Transponder squawk: TRANSPONDER CODE:1 reads as a raw BCD16 word (0x2000 = 8192);
        // decode each nibble to the 4-digit squawk (8192 -> "2000").
        if (varKey == "XPNDR_CODE")
        {
            int bcd = (int)Math.Round(value);
            displayText = $"{(bcd >> 12) & 0xF}{(bcd >> 8) & 0xF}{(bcd >> 4) & 0xF}{bcd & 0xF}";
            return true;
        }
        // RMP frequencies are FBW L:vars in raw Hz (122800000 = 122.800 MHz).
        if (varKey.StartsWith("FBW_RMP_FREQUENCY_", StringComparison.Ordinal))
        {
            displayText = $"{value / 1_000_000.0:0.000} MHz";
            return true;
        }
        // Beta-target (sideslip target). Only valid when _ACTIVE (cached in ProcessSimVarUpdate).
        if (varKey == "A32NX_BETA_TARGET")
        {
            displayText = !_betaTargetActive ? "not active"
                        : Math.Abs(value) < 0.05 ? "centred"
                        : $"{Math.Abs(value):0.0} degrees {(value > 0 ? "left" : "right")}";
            return true;
        }
        // TCAS RA vertical-speed band: green = fly toward, red = avoid; 0 = no advisory.
        if (varKey == "A32NX_TCAS_VSPEED_GREEN" || varKey == "A32NX_TCAS_VSPEED_RED")
        {
            displayText = Math.Abs(value) < 1 ? "no advisory" : $"{value:0} feet per minute";
            return true;
        }
        // Speed-brake handle: a 0..1 fraction — show "Retracted" / "Full" / "N percent".
        if (varKey == "A32NX_SPOILERS_HANDLE_POSITION")
        {
            displayText = value < 0.05 ? "Retracted" : value > 0.95 ? "Full" : $"{(int)Math.Round(value * 100)} percent";
            return true;
        }
        // Nosewheel steering angle: 0.5 = centred, (v-0.5)*140 = degrees (±70° authority).
        if (varKey == "A32NX_NOSE_WHEEL_POSITION")
        {
            double deg = (value - 0.5) * 140.0;
            displayText = Math.Abs(deg) < 0.5 ? "Centred"
                        : $"{Math.Abs(deg):0} degrees {(deg < 0 ? "left" : "right")}";
            return true;
        }
        // Tiller handle: ±1 full-scale; show as a left/right percentage.
        if (varKey == "A32NX_TILLER_HANDLE_POSITION")
        {
            int pct = (int)Math.Round(Math.Abs(value) * 100);
            displayText = pct < 1 ? "Centred" : $"{pct}% {(value < 0 ? "left" : "right")}";
            return true;
        }
        // GW CG % of MAC (Gus's FBW airframe value, more accurate than the stock CG PERCENT).
        if (varKey == "A32NX_AIRFRAME_GW_CG_PERCENT_MAC")
        {
            displayText = (value > 5 && value < 60) ? $"{value:0.0} percent MAC" : "not available";
            return true;
        }
        // Mach — two decimals (default F0 would render "0").
        if (varKey == "PFD_MACH") { displayText = $"{value:0.00}"; return true; }
        // Autoland capability (FCDC FG discrete word 4): bit 23 LAND2, 24 LAND3 single, 25 LAND3 dual.
        if (varKey == "PFD_AUTOLAND")
        {
            var w = new SimConnect.Arinc429Word(value);
            if (!w.IsNormalOperation && !w.IsFunctionalTest) displayText = "none";
            else if (w.BitValueOr(23, false)) displayText = "LAND2";
            else if (w.BitValueOr(24, false)) displayText = "LAND3 single";
            else if (w.BitValueOr(25, false)) displayText = "LAND3 dual";
            else displayText = "none";
            return true;
        }
        // Managed target speed on the PFD (0 = none shown).
        if (varKey == "A32NX_SPEEDS_MANAGED_PFD") { displayText = value < 1 ? "none" : $"{value:0} knots"; return true; }
        // Preselected speed / Mach (set in the MCDU PERF page; -1 = none).
        if (varKey == "A32NX_SpeedPreselVal") { displayText = value < 0 ? "none" : $"{value:0} knots"; return true; }
        if (varKey == "A32NX_MachPreselVal") { displayText = value < 0 ? "none" : $"{value:0.00}"; return true; }
        // Selected vertical speed (FCU V/S window; 0 = not selected / not in V/S).
        if (varKey == "A32NX_AUTOPILOT_VS_SELECTED")
        {
            displayText = Math.Abs(value) < 1 ? "not selected" : $"{Math.Abs(value):0} feet per minute {(value > 0 ? "up" : "down")}";
            return true;
        }
        // Takeoff V-speeds: 0 = not entered in the MCDU.
        if (varKey == "PFD_V1" || varKey == "PFD_VR" || varKey == "PFD_V2")
        {
            displayText = value < 1 ? "not set" : $"{value:0} knots";
            return true;
        }
        // Weight/config speeds sourced from A32NX_SPEEDS_* (valid on the ground too); 0 = not computed.
        if (varKey == "PFD_VMAX" || varKey == "PFD_VLS" || varKey == "PFD_GREENDOT" || varKey == "PFD_V3" || varKey == "PFD_V4")
        {
            displayText = value < 1 ? "not available" : $"{value:0} knots";
            return true;
        }
        // ILS DME — one decimal nm; ILS freq — three decimals MHz.
        if (varKey == "PFD_ILS_DME") { displayText = value < 0.05 ? "no DME" : $"{value:0.0} nautical miles"; return true; }
        if (varKey == "PFD_ILS_FREQ") { displayText = value < 100 ? "none" : $"{value:0.000} MHz"; return true; }
        // ILS/LS course — -1 (or any negative) means no course is set.
        if (varKey == "A32NX_FM_LS_COURSE") { displayText = value < 0 ? "no course set" : $"{value:000} degrees"; return true; }
        // Cross-track error — magnitude in NM with left/right of track (FBW sign: positive = right).
        if (varKey == "A32NX_FG_CROSS_TRACK_ERROR")
        {
            displayText = Math.Abs(value) < 0.01 ? "on track" : $"{Math.Abs(value):0.00} NM {(value > 0 ? "right" : "left")} of track";
            return true;
        }
        // EWD thrust limit — the max-N1 % for the current thrust-rating mode.
        if (varKey == "A32NX_AUTOTHRUST_THRUST_LIMIT") { displayText = $"{value:0} percent N1"; return true; }
        switch (varKey)
        {
            // ECAM Control Panel "Status display" box: show the SELECTED SD page name
            // plus its live scraped CONTENT (populated by RefreshSdPageDisplayAsync on
            // each page switch). Before the first scrape it prompts to switch a page.
            case "A32NX_ECAM_SD_CURRENT_PAGE_INDEX":
            {
                int pi = (int)Math.Round(value);
                string pname = _sdPageNames.TryGetValue(pi, out var pn) ? pn : $"Page {pi}";
                displayText = string.IsNullOrEmpty(_sdPageContent)
                    ? $"{pname} page (select a page to load its content)"
                    : $"{pname} page\r\n{_sdPageContent}";
                return true;
            }
            case "A32NX_FMA_VERTICAL_ARMED":
            {
                string s = DecodeArmedModes((int)Math.Round(value), _vertArmedBits);
                displayText = string.IsNullOrEmpty(s) ? "None" : s;
                return true;
            }
            case "A32NX_FMA_LATERAL_ARMED":
            {
                string s = DecodeArmedModes((int)Math.Round(value), _latArmedBits);
                displayText = string.IsNullOrEmpty(s) ? "None" : s;
                return true;
            }
            // ---- PFD / ISIS / ND status-box decode (A380 attitude is in DEGREES) ----
            case "PLANE PITCH DEGREES":   // positive = nose down
                displayText = Math.Abs(value) < 0.5 ? "Level" : $"{Math.Abs(value):F1} degrees {(value < 0 ? "up" : "down")}";
                return true;
            case "PLANE BANK DEGREES":    // positive = bank left
                displayText = Math.Abs(value) < 0.5 ? "Wings level" : $"{Math.Abs(value):F1} degrees {(value > 0 ? "left" : "right")}";
                return true;
            case "PLANE HEADING DEGREES MAGNETIC":
            {
                double hdg = ((value % 360) + 360) % 360;
                displayText = $"{(int)Math.Round(hdg):000}";
                return true;
            }
            case "A32NX_EFIS_L_TO_WPT_IDENT_0":
            {
                string wpt = UnpackSixBitIdent(_ndIdent0, _ndIdent1);
                displayText = string.IsNullOrWhiteSpace(wpt) ? "None" : wpt;
                return true;
            }
            case "A32NX_EFIS_L_TO_WPT_DISTANCE":
                displayText = value <= 0 ? "--" : $"{value:F1} NM";
                return true;
            case "A32NX_EFIS_L_TO_WPT_BEARING":   // stored as radians
            {
                double deg = value * 180.0 / Math.PI;
                deg = ((deg % 360) + 360) % 360;
                displayText = $"{(int)Math.Round(deg):000} magnetic";
                return true;
            }
            case "A32NX_EFIS_L_TO_WPT_ETA":
            {
                if (value <= 0) { displayText = "--"; return true; }
                int h = (int)(value / 3600), m = (int)((value % 3600) / 60), s2 = (int)(value % 60);
                displayText = $"{h}:{m:D2}:{s2:D2} UTC";
                return true;
            }
            case "A32NX_FG_CROSS_TRACK_ERROR":
            {
                double nm = value / 1852.0;
                displayText = Math.Abs(nm) < 0.01 ? "On track" : $"{Math.Abs(nm):F2} NM {(nm > 0 ? "right" : "left")}";
                return true;
            }
            case "A32NX_RADIO_RECEIVER_LOC_DEVIATION":
            case "A32NX_RADIO_RECEIVER_GS_DEVIATION":
                displayText = $"{value:F2} degrees";
                return true;
            case "A32NX_RADIO_RECEIVER_LOC_IS_VALID":
            case "A32NX_RADIO_RECEIVER_GS_IS_VALID":
                displayText = value > 0.5 ? "valid" : "invalid";
                return true;
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
            case "PFD_GROSS_WEIGHT":
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
    // Toggle BUTTONS (e.g. fire test / cargo smoke test HOLD pushbuttons): a click reads

    // Lazy live-scrape client for the System Display: when the user picks an SD page
    // in the ECAM Control Panel "System Display Page" combo, MSFSBA drives the page
    // index then reads that page's DECODED content off the real SD Coherent view and
    // announces it — so the SD pages are usable straight from the panel, no separate
    // window or hotkey. On-demand scrapes only (background poll paused).
    private SimConnect.CoherentDisplayClient? _sdScrapeClient;
    private SimConnect.CoherentDisplayClient? _ewdScrapeClient;   // legacy fallback only (see below)

    /// <summary>
    /// The always-on E/WD failure monitor (owns the ONLY inspector socket to the
    /// A380X_EWD view). MainForm sets this when it starts the monitor. The SD "Upper
    /// E/WD" page reads the live E/WD content through THIS shared socket — a second
    /// CoherentDisplayClient on A380X_EWD is rejected (one inspector per page), which
    /// is what produced the "content not available" box.
    /// </summary>
    public SimConnect.CoherentEWDClient? EwdMonitor { get; set; }
    // Cached live System-Display content for the ECP "System Display Page" combo. On a
    // page change we scrape the SD view ONCE, store the decoded rows here, then force a
    // refresh of the ECAM Control Panel "Status display" box (via the page-index var) —
    // TryGetDisplayOverride surfaces this content there. NO auto-speech: the combo
    // announces the page NAME; the CONTENT is read on demand in the box, and it updates
    // immediately on a page switch with no manual refresh.
    private string _sdPageContent = "";
    private int _sdRefreshSeq;   // bumped per SD-page refresh; "latest request wins" guard
    private static readonly Dictionary<int, string> _sdPageNames = new()
    {
        [-1] = "Default automatic page", [0] = "Engine", [1] = "APU", [2] = "Bleed", [3] = "Air Cond",
        [4] = "Pressurization", [5] = "Doors", [6] = "Electrical AC", [7] = "Electrical DC",
        [8] = "Fuel", [9] = "Wheel", [10] = "Hydraulics", [11] = "Flight Controls",
        [12] = "Circuit Breakers", [13] = "Cruise", [14] = "Status", [15] = "Video",
        [16] = "Upper E/WD"
    };
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
    // Render-time SimConnect handle so A380SdRows fmt closures can read sibling ARINC words
    // (B1→B4 CPCS source selection, SEC1↔SEC3 rudder-trim) at paint time.
    private SimConnectManager? _sdRender;

    private async void RefreshSdPageDisplayAsync(SimConnectManager simConnect, int pageIndex = -99, bool ewd = false)
    {
        try
        {
            _sdRender = simConnect;
            // DECODED path (preferred): build clean "Label: value" rows from SimVars for
            // the data pages, instead of the schematic Coherent scrape (which interleaves
            // the 4 engines'/gens' values with their labels). Pages without a decode (C/B,
            // Status, Video) and the E/WD fall through to the live scrape below.
            if (!ewd)
            {
                var decoded = A380SdRows(pageIndex);
                if (decoded.Count > 0)
                {
                    // "Latest request wins" — a newer page switch invalidates this one so a slow
                    // refresh can never stamp the box with a stale page's content (the old race
                    // that made the content trail the title by up to a minute).
                    int seq = ++_sdRefreshSeq;
                    void Paint()
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var row in decoded)
                        {
                            double? cv = simConnect.GetCachedVariableValue(row.var);
                            sb.AppendLine(cv.HasValue ? $"{row.label}: {row.fmt(cv.Value)}" : $"{row.label}: --");
                        }
                        _sdPageContent = sb.ToString().TrimEnd();
                        simConnect.RequestVariable("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", forceUpdate: true);
                    }
                    // Paint IMMEDIATELY from the cache (decoded vars are already monitored), so the
                    // content appears the instant the page is selected — no 550 ms blank/lag.
                    Paint();
                    // Then force a fresh read and repaint ~0.4 s later — but only if this is still
                    // the most-recent page request.
                    foreach (var row in decoded) simConnect.RequestVariable(row.var, forceUpdate: true);
                    // Sibling ARINC sources for B1→B4 CPCS selection (PRESS/CRUISE) and the SEC3
                    // rudder-trim backup (F/CTL) — force-read so the best-source fmt can pick a
                    // NormalOp word if B1/SEC1 has failed.
                    if (pageIndex == 4 || pageIndex == 13)
                        foreach (var bas in new[] { "A32NX_PRESS_CABIN_ALTITUDE", "A32NX_PRESS_CABIN_VS",
                                                    "A32NX_PRESS_CABIN_DELTA_PRESSURE", "A32NX_PRESS_CABIN_ALTITUDE_TARGET" })
                            foreach (var sfx in new[] { "_B2", "_B3", "_B4" })
                                simConnect.RequestVariable(bas + sfx, forceUpdate: true);
                    if (pageIndex == 11)
                    {
                        simConnect.RequestVariable("A32NX_SEC_3_RUDDER_ACTUAL_POSITION", forceUpdate: true);
                        simConnect.RequestVariable("A32NX_SEC_1_RUDDER_STATUS_WORD", forceUpdate: true);
                    }
                    await Task.Delay(400);
                    if (seq != _sdRefreshSeq) return;
                    Paint();
                    return;
                }
            }
            // Upper ECAM / E-WD — DECODE into clean per-parameter rows from SimVars. The
            // schematic A380X_EWD scrape flat-joined its X-sorted leaves and interleaved
            // the four engines' values with their labels ("THR XX THR XX THR XX THR XX /
            // N1 / XX XX % XX XX"), nonsensical for a screen reader. Mirrors the SD-page
            // decode: engine primaries grouped per parameter, then the live ECAM memo/
            // warning lines. Falls through to the scrape only if nothing is cached yet.
            if (ewd)
            {
                int[] engs = { 1, 2, 3, 4 };
                foreach (int e in engs)
                {
                    foreach (var p in new[] { "N1", "EGT", "N2", "N3", "FF" })
                        simConnect.RequestVariable($"A32NX_ENGINE_{p}:{e}", forceUpdate: true);
                    simConnect.RequestVariable($"A32NX_AUTOTHRUST_N1_COMMANDED:{e}", forceUpdate: true);
                    simConnect.RequestVariable($"A32NX_ENGINE_STATE:{e}", forceUpdate: true);
                }
                foreach (var g in new[] { "A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE", "A32NX_AIRLINER_TO_FLEX_TEMP",
                                          "A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE", "A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA",
                                          // BLEED line consumers + AGS word for PACKS.
                                          "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD",
                                          "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4",
                                          // Autothrust mode message + inboard reversers (eng 2/3).
                                          "A32NX_AUTOTHRUST_MODE_MESSAGE", "A32NX_AUTOTHRUST_REVERSE:2", "A32NX_AUTOTHRUST_REVERSE:3" })
                    simConnect.RequestVariable(g, forceUpdate: true);
                await Task.Delay(550);

                bool anyReal = false;
                string Grp(string varFmt, Func<double, string> fmt)
                {
                    var parts = new List<string>();
                    foreach (int e in engs)
                    {
                        double? cv = simConnect.GetCachedVariableValue(string.Format(varFmt, e));
                        if (cv.HasValue) anyReal = true;
                        parts.Add($"Engine {e} " + (cv.HasValue ? fmt(cv.Value) : "--"));
                    }
                    return string.Join(", ", parts);
                }

                // Thrust rating mode (the big EWD label) + optional FLEX temp.
                string[] thrModes = { "", "CLB", "MCT", "FLX", "TOGA", "MREV" };
                double? tlt = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE");
                int tltI = (int)Math.Round(tlt ?? 0);
                string thrMode = (tltI >= 1 && tltI < thrModes.Length) ? thrModes[tltI] : "none";
                double? flex = simConnect.GetCachedVariableValue("A32NX_AIRLINER_TO_FLEX_TEMP");
                if (thrMode == "FLX" && flex.HasValue && flex.Value > 0) thrMode += $" {flex.Value:0}°C";
                // Engine state enum → text.
                string EngState(double v) => v >= 2 ? "starting" : v >= 1 ? "on" : "off";

                // Computed thrust-limit % per engine (the EWD ThrustGauge green number). FBW
                // formula: pct = clamp01((N1-idle)/(toga-idle))*(1-off)+off, where off = 0.042
                // when the engine is starting (state==1). idle/toga = AUTOTHRUST_THRUST_LIMIT_*.
                double idleLim = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE") ?? 0;
                double togaLim = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA") ?? 100;
                string ThrPct(int e)
                {
                    double? n1 = simConnect.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}");
                    if (!n1.HasValue || togaLim <= idleLim) return $"Engine {e} --";
                    double off = (simConnect.GetCachedVariableValue($"A32NX_ENGINE_STATE:{e}") ?? 0) == 1 ? 0.042 : 0;
                    double frac = Math.Min(1.0, Math.Max(0.0, (n1.Value - idleLim) / (togaLim - idleLim)) * (1 - off) + off);
                    return $"Engine {e} {frac * 100:0}%";
                }

                var ewdLines = new List<string>
                {
                    "Thrust rating: " + thrMode,
                    "Thrust limit: " + string.Join(", ", engs.Select(ThrPct)),
                    "N1: "          + Grp("A32NX_ENGINE_N1:{0}",  v => $"{v:0.0}%"),
                    "N1 command: "  + Grp("A32NX_AUTOTHRUST_N1_COMMANDED:{0}", v => $"{v:0.0}%"),
                    "EGT: "         + Grp("A32NX_ENGINE_EGT:{0}", v => $"{v:0}°C"),
                    "N2: "          + Grp("A32NX_ENGINE_N2:{0}",  v => $"{v:0.0}%"),
                    "N3: "          + Grp("A32NX_ENGINE_N3:{0}",  v => $"{v:0.0}%"),
                    "Fuel Flow: "   + Grp("A32NX_ENGINE_FF:{0}",  v => $"{v:0} kg/h"),
                    "Engine state: "+ Grp("A32NX_ENGINE_STATE:{0}", EngState),
                };
                // Autothrust mode message (the amber/white E/WD memo above the thrust gauges):
                // THR LK / LVR TOGA / LVR CLB / LVR MCT / LVR ASYM.
                string[] athrMsgs = { "", "THR LK", "LVR TOGA", "LVR CLB", "LVR MCT", "LVR ASYM" };
                int athrMsg = (int)Math.Round(simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_MODE_MESSAGE") ?? 0);
                if (athrMsg >= 1 && athrMsg < athrMsgs.Length) ewdLines.Add("Autothrust message: " + athrMsgs[athrMsg]);
                // Inboard thrust reversers (engines 2 & 3 on the A380).
                var revOn = new[] { 2, 3 }.Where(e => (simConnect.GetCachedVariableValue($"A32NX_AUTOTHRUST_REVERSE:{e}") ?? 0) > 0.5).ToList();
                if (revOn.Count > 0) ewdLines.Add("Reverser: " + string.Join(" and ", revOn.Select(e => $"engine {e}")) + " deployed");
                // BLEED line — what's drawing engine bleed air (PACKS / nacelle anti-ice / wing
                // anti-ice), the FBW upper-E/WD BleedSupply element.
                var bleed = new List<string>();
                var agsWord = new SimConnect.Arinc429Word(simConnect.GetCachedVariableValue("A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD") ?? 0);
                if (agsWord.BitValueOr(13, false) || agsWord.BitValueOr(14, false)) bleed.Add("packs");
                if (engs.Any(e => (simConnect.GetCachedVariableValue($"ENG_ANTI_ICE:{e}") ?? 0) > 0.5)) bleed.Add("nacelle anti-ice");
                if ((simConnect.GetCachedVariableValue("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON") ?? 0) > 0.5) bleed.Add("wing anti-ice");
                if (bleed.Count > 0) ewdLines.Add("Bleed: " + string.Join(", ", bleed));
                // IDLE memo — thrust at idle (≥3 engines at/near idle N1) in descent or later
                // (FMGC phase ≥ 4 Descent), so it can't false-fire on the ground.
                double? fmgcPhase = simConnect.GetCachedVariableValue("A32NX_FMGC_FLIGHT_PHASE");
                int idleEngs = engs.Count(e => { var n1 = simConnect.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}"); return n1.HasValue && n1.Value <= idleLim + 2; });
                if (fmgcPhase.HasValue && fmgcPhase.Value >= 4 && idleEngs >= 3) ewdLines.Add("IDLE");
                // Live ECAM memo / warning lines — decoded from the EWD_LOWER code cache
                // (the same source ReadAllEwdWarnings / Alt+E uses).
                int memoCount = 0;
                foreach (var lr in new[] { "LEFT", "RIGHT" })
                    for (int i = 1; i <= 10; i++)
                        if (_lastEwdCode.TryGetValue($"A32NX_EWD_LOWER_{lr}_LINE_{i}", out var code) && code != 0)
                        {
                            string mtext = EWDMessageLookupA380.GetMessage(code);
                            if (!string.IsNullOrWhiteSpace(mtext) &&
                                !mtext.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                            {
                                // Append the ECAM colour name (e.g. "ENG 1 FAIL, Amber") so the
                                // System Display page conveys severity, matching the EWD viewer + the
                                // live monitoring announcements.
                                string priority = EWDMessageLookupA380.GetMessagePriority(code);
                                ewdLines.Add(string.IsNullOrEmpty(priority) ? mtext : $"{mtext}, {priority}");
                                memoCount++;
                            }
                        }

                if (anyReal || memoCount > 0)
                {
                    _sdPageContent = string.Join("\r\n", ewdLines);
                    simConnect.RequestVariable("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", forceUpdate: true);
                    return;
                }
                // Nothing cached yet (SimConnect not ready / displays off) → fall through
                // to the DOM scrape below (through the shared monitor socket).
            }

            List<string>? rows;
            if (ewd)
            {
                // Fallback only (decode above returned nothing). The A380X_EWD view allows
                // only ONE inspector socket, owned by the always-on CoherentEWDClient
                // failure monitor — so scrape THROUGH it, never a second client (that
                // rejection was the "content not available" bug).
                rows = EwdMonitor != null ? await EwdMonitor.ScrapeDisplayAsync() : null;
                if (rows == null)
                {
                    // No monitor running (shouldn't happen on the A380) → legacy direct
                    // client, which only works when nothing else owns the socket.
                    if (_ewdScrapeClient == null)
                    {
                        _ewdScrapeClient = new SimConnect.CoherentDisplayClient("A380X_EWD");
                        _ewdScrapeClient.Start();
                        _ewdScrapeClient.SetActive(false);
                    }
                    await Task.Delay(900);
                    rows = await _ewdScrapeClient.ScrapeNowAsync();
                }
            }
            else
            {
                if (_sdScrapeClient == null)
                {
                    _sdScrapeClient = new SimConnect.CoherentDisplayClient("A380X_SDv2");
                    _sdScrapeClient.Start();
                    _sdScrapeClient.SetActive(false);   // on-demand only, no 1.2 s poll
                }
                await Task.Delay(900);   // let the display render the newly-selected page
                rows = await _sdScrapeClient.ScrapeNowAsync();
            }
            string content;
            if (rows == null || rows.Count == 0)
            {
                content = "(content not available — power up the displays / try again)";
            }
            else
            {
                // Drop the on-screen UI chrome (page buttons) so the read-out is just data.
                var clean = rows.Where(r =>
                {
                    string u = (r ?? "").Trim().ToUpperInvariant();
                    return u.Length > 0 && u != "CLOSE" && u != "MORE" && u != "PRINT"
                           && u != "RECALL" && u != "RECALL PRINT" && u != "RECALL  PRINT";
                });
                content = string.Join("\r\n", clean);
            }
            _sdPageContent = content;
            // Push the freshly-scraped content into the ECAM Control Panel "Status display"
            // box by forcing a refresh of its display var — UpdateDisplayText then calls
            // TryGetDisplayOverride, which returns _sdPageContent. NO speech: the page name
            // was already announced by the combo; this only POPULATES the box, immediately,
            // with no manual refresh.
            simConnect.RequestVariable("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", forceUpdate: true);
        }
        catch { /* scrape best-effort; the combo still set the page */ }
    }

    // Populate the ECAM-CP "System Display Page" status box with the combo's CURRENT
    // page as soon as the panel is shown — so the user no longer has to cycle the combo
    // down/up to get content on first display.
    public override void OnDisplayPanelShown(string panelKey, SimConnectManager simConnect)
    {
        if (panelKey != "ECAM Control Panel" || !simConnect.IsConnected) return;
        int idx = (int)Math.Round(simConnect.GetCachedVariableValue("A32NX_ECAM_SD_CURRENT_PAGE_INDEX") ?? -1);
        RefreshSdPageDisplayAsync(simConnect, idx, ewd: idx == 16);
    }

    // ---- Decoded SD-page rows (clean labelled SimVar read-out) -----------------
    // The A380 SD pages are SCHEMATIC: scraping the Coherent view and flat-joining the
    // X-sorted leaves interleaves the four engines'/generators' values with their
    // labels ("115 V 115 APU 115 V 115") — nonsensical for a screen reader. So, like
    // the A32NX, decode the underlying SimVars into clean "Label: value" rows. ARINC429
    // words (fuel/press/apu) decode via Arinc429Word; plain L:vars read directly. Pages
    // not decoded here (C/B, Status, Video) fall back to the live scrape. Var names are
    // from the fbw-a380x SD/Pages source; values spot-verified live.
    public List<(string label, string var, Func<double, string> fmt)> A380SdRows(int page)
    {
        string Pct(double v) => $"{v:0} %";
        string Pct1(double v) => $"{v:0.0} %";
        string V(double v) => $"{v:0} volts";
        string Psi(double v) => $"{v:0} psi";
        string C(double v) => $"{v:0} degrees";
        string Qt(double v) => $"{v:0.0} quarts";
        // Weight/fuel rows FOLLOW the metric toggle (WeightUser: kg or lb per the EFB
        // "US Units" setting) so the displays change unit automatically, like the
        // on-demand read-outs. Wt = plain kg in; AWt = ARINC429 kg word in.
        string Wt(double kg) { var (val, u) = WeightUser(kg); return $"{val:0} {u}"; }
        string Kgh(double kgh) { var (val, u) = WeightUser(kgh); return $"{val:0} {u} per hour"; }
        string OnOff(double v) => v > 0.5 ? "powered" : "not powered";
        string OpenShut(double v) => v > 0.5 ? "open" : "closed";
        string Healthy(double v) => v > 0.5 ? "healthy" : "failed";
        string Auto(double v) => v > 0.5 ? "auto" : "off";
        string Active(double v) => v > 0.5 ? "running" : "off";
        string Flag(double v, string set, string clr) => v > 0.5 ? set : clr;
        // Signed surface-deflection as a percentage of travel (the FBW F/CTL page draws a
        // bar from a normalized -1..1 value); a blind pilot sweeping the controls hears
        // the percentage change.
        string Defl(double v) => $"{v * 100:0} percent";
        // Engine oil pressure is NOT modelled on the A380 dev build (stock simvar returns
        // negative garbage); the FBW page clamps negatives to 0, so mirror that.
        string OilP(double v) => v <= 0 ? "not available" : $"{v:0} psi";
        // ARINC429 decoder: payload + unit, or "not available" when the SSM isn't normal.
        string A(double v, string unit, string fmt = "0") { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value.ToString(fmt)} {unit}" : "not available"; }
        // ARINC429 DISCRETE-word single-bit decoder (1-based bit, SSM-gated). For the CPIOM
        // VCS/TCS/AGS discrete words (cabin fans, cargo isolation, hot air, pack operative).
        Func<double, string> Bit(int bit, string set, string clr, bool invert = false)
            => v => { var w = new SimConnect.Arinc429Word(v); if (!(w.IsNormalOperation || w.IsFunctionalTest)) return "not available"; bool b = w.BitValueOr(bit, false); if (invert) b = !b; return b ? set : clr; };
        string FlowPct(double v) => $"{v * 100:0} %";   // PNEU pack flow-rate 0..1 -> percent
        // ARINC429 kg word -> user weight units (kg/lb per the metric toggle).
        string AWt(double v) { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? Wt(w.Value) : "not available"; }
        // Best-source CPCS decode: the FBW PRESS/CRUISE pages pick the first NormalOp word among
        // the B1→B4 CPIOM sources. Reading B1 only shows "not available" on a B1 failure while the
        // real ECAM is still valid on B2-B4. Read all four (force-read in RefreshSdPageDisplayAsync)
        // and return the first NormalOp. Ignores the passed B1 value (reads from the render cache).
        Func<double, string> ABest(string b1Var, string unit, string fmt = "0") => _ =>
        {
            foreach (var sfx in new[] { "_B1", "_B2", "_B3", "_B4" })
            {
                string name = b1Var.EndsWith("_B1") ? b1Var.Substring(0, b1Var.Length - 3) + sfx : b1Var;
                double? cv = _sdRender?.GetCachedVariableValue(name);
                if (cv.HasValue) { var w = new SimConnect.Arinc429Word(cv.Value); if (w.IsNormalOperation || w.IsFunctionalTest) return $"{w.Value.ToString(fmt)} {unit}"; }
            }
            return "not available";
        };
        var r = new List<(string, string, Func<double, string>)>();
        switch (page)
        {
            case 0: // ENGINE
                for (int e = 1; e <= 4; e++)
                {
                    r.Add(($"Engine {e} N1", $"A32NX_ENGINE_N1:{e}", Pct1));
                    r.Add(($"Engine {e} N2", $"A32NX_ENGINE_N2:{e}", Pct1));
                    r.Add(($"Engine {e} N3", $"A32NX_ENGINE_N3:{e}", Pct1));
                    r.Add(($"Engine {e} fuel flow", $"A32NX_ENGINE_FF:{e}", Kgh));
                    r.Add(($"Engine {e} oil quantity", $"A32NX_ENGINE_OIL_QTY:{e}", Qt));
                    r.Add(($"Engine {e} oil temperature", $"GENERAL_ENG_OIL_TEMPERATURE:{e}", C));
                    r.Add(($"Engine {e} oil pressure", $"ENG_OIL_PRESSURE:{e}", OilP));
                    r.Add(($"Engine {e} vibration", $"TURB_ENG_VIBRATION:{e}", v => $"{v:0.0}"));
                    // Starter (cranking) valve — open while motoring/starting the engine.
                    r.Add(($"Engine {e} starter valve", $"A32NX_PNEU_ENG_{e}_STARTER_VALVE_OPEN", OpenShut));
                    // Nacelle temperature — the FBW ENG page hardcodes 240°C (not yet modelled);
                    // surfaced to match what the real SD shows. Constant, gated on FADEC power.
                    r.Add(($"Engine {e} nacelle temperature", $"A32NX_ENGINE_N1:{e}", _ => "240 degrees"));
                }
                break;
            case 1: // APU
                // APU N / N2 are ARINC429 words (FBW ApuPage useArinc429Var) — decode, not plain
                // (plain would show the raw ~1.1e9 word as "%"). Confirmed: APU_EGT on this page
                // reads a raw ARINC word too.
                r.Add(("APU available", "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5 ? "available" : "not available"));
                r.Add(("APU master switch", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", v => v > 0.5 ? "on" : "off"));
                r.Add(("APU N", "A32NX_APU_N", v => A(v, "%", "0.0")));
                r.Add(("APU N2", "A32NX_APU_N2", v => A(v, "%", "0.0")));
                r.Add(("APU EGT", "A32NX_APU_EGT", v => A(v, "degrees")));
                r.Add(("APU fuel used", "A32NX_APU_FUEL_USED", AWt));
                r.Add(("APU flap open", "A32NX_APU_FLAP_OPEN_PERCENTAGE", Pct));
                r.Add(("APU bleed valve", "A32NX_APU_BLEED_AIR_VALVE_OPEN", OpenShut));
                r.Add(("APU bleed pressure", "A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE", Psi));
                r.Add(("APU generator 1 voltage", "A32NX_ELEC_APU_GEN_1_POTENTIAL", V));
                r.Add(("APU generator 1 frequency", "A32NX_ELEC_APU_GEN_1_FREQUENCY", v => $"{v:0} hertz"));
                r.Add(("APU generator 1 load", "A32NX_ELEC_APU_GEN_1_LOAD", Pct));
                r.Add(("APU generator 2 voltage", "A32NX_ELEC_APU_GEN_2_POTENTIAL", V));
                r.Add(("APU generator 2 frequency", "A32NX_ELEC_APU_GEN_2_FREQUENCY", v => $"{v:0} hertz"));
                r.Add(("APU generator 2 load", "A32NX_ELEC_APU_GEN_2_LOAD", Pct));
                break;
            case 2: // BLEED
                for (int e = 1; e <= 4; e++)
                {
                    r.Add(($"Engine {e} bleed valve", $"A32NX_PNEU_ENG_{e}_PR_VALVE_OPEN", OpenShut));
                    r.Add(($"Engine {e} HP valve", $"A32NX_PNEU_ENG_{e}_HP_VALVE_OPEN", OpenShut));
                    r.Add(($"Engine {e} bleed pressure", $"A32NX_PNEU_ENG_{e}_REGULATED_TRANSDUCER_PRESSURE", Psi));
                    r.Add(($"Engine {e} precooler outlet temp", $"A32NX_PNEU_ENG_{e}_PRECOOLER_OUTLET_TEMPERATURE", C));
                }
                r.Add(("Pack 1 outlet temp", "A32NX_COND_PACK_1_OUTLET_TEMPERATURE", C));
                r.Add(("Pack 2 outlet temp", "A32NX_COND_PACK_2_OUTLET_TEMPERATURE", C));
                r.Add(("Pack 1 flow valve 1", "A32NX_COND_PACK_1_FLOW_VALVE_1_IS_OPEN", OpenShut));
                r.Add(("Pack 1 flow valve 2", "A32NX_COND_PACK_1_FLOW_VALVE_2_IS_OPEN", OpenShut));
                r.Add(("Pack 2 flow valve 1", "A32NX_COND_PACK_2_FLOW_VALVE_1_IS_OPEN", OpenShut));
                r.Add(("Pack 2 flow valve 2", "A32NX_COND_PACK_2_FLOW_VALVE_2_IS_OPEN", OpenShut));
                r.Add(("Crossbleed valve left", "A32NX_PNEU_XBLEED_VALVE_L_OPEN", OpenShut));
                r.Add(("Crossbleed valve centre", "A32NX_PNEU_XBLEED_VALVE_C_OPEN", OpenShut));
                r.Add(("Crossbleed valve right", "A32NX_PNEU_XBLEED_VALVE_R_OPEN", OpenShut));
                r.Add(("APU bleed valve", "A32NX_APU_BLEED_AIR_VALVE_OPEN", OpenShut));
                r.Add(("Ram air valve", "A32NX_OVHD_COND_RAM_AIR_PB_IS_ON", v => v > 0.5 ? "open" : "closed"));
                // Pack inlet flow per flow valve (PNEU FLOW_RATE 0..1 -> %).
                r.Add(("Pack 1 valve 1 flow", "A32NX_PNEU_PACK_1_FLOW_VALVE_1_FLOW_RATE", FlowPct));
                r.Add(("Pack 1 valve 2 flow", "A32NX_PNEU_PACK_1_FLOW_VALVE_2_FLOW_RATE", FlowPct));
                r.Add(("Pack 2 valve 1 flow", "A32NX_PNEU_PACK_2_FLOW_VALVE_1_FLOW_RATE", FlowPct));
                r.Add(("Pack 2 valve 2 flow", "A32NX_PNEU_PACK_2_FLOW_VALVE_2_FLOW_RATE", FlowPct));
                // Pack operative (AGS discrete word bits 13/14).
                r.Add(("Pack 1 operative", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD", Bit(13, "operative", "off")));
                r.Add(("Pack 2 operative", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD", Bit(14, "operative", "off")));
                // FDAC (pack-controller) channel-failure flags — 2 packs × 2 channels.
                for (int f = 1; f <= 2; f++)
                    for (int c = 1; c <= 2; c++) r.Add(($"Pack {f} FDAC channel {c}", $"A32NX_COND_FDAC_{f}_CHANNEL_{c}_FAILURE", v => Flag(v, "failed", "normal")));
                break;
            case 3: // COND (Air Conditioning)
                r.Add(("Cockpit temp", "A32NX_COND_CKPT_TEMP", v => $"{v:0.0} degrees"));
                for (int z = 1; z <= 8; z++) r.Add(($"Main deck zone {z} temp", $"A32NX_COND_MAIN_DECK_{z}_TEMP", v => $"{v:0.0} degrees"));
                for (int z = 1; z <= 7; z++) r.Add(($"Upper deck zone {z} temp", $"A32NX_COND_UPPER_DECK_{z}_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Forward cargo temp", "A32NX_COND_CARGO_FWD_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Bulk cargo temp", "A32NX_COND_CARGO_BULK_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Cabin air extract valve", "A32NX_VENT_OVERPRESSURE_RELIEF_VALVE_IS_OPEN", OpenShut));
                r.Add(("Ram air valve", "A32NX_OVHD_COND_RAM_AIR_PB_IS_ON", v => v > 0.5 ? "open" : "closed"));
                // CPIOM VCS/TCS/AGS discrete-word states (cabin fans, cargo isolation, hot air,
                // pack operative). Bit numbers from the FBW a380x Cond source (1-based).
                r.Add(("Cabin fans", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(17, "enabled", "off")));
                for (int f = 1; f <= 4; f++) r.Add(($"Cabin fan {f}", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(17 + f, "fault", "normal")));
                r.Add(("Forward cargo extract fan", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(13, "on", "off")));
                r.Add(("Bulk cargo extract fan", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(15, "on", "off")));
                r.Add(("Forward cargo isolation valve", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(14, "open", "closed")));
                r.Add(("Bulk cargo isolation valve", "A32NX_COND_CPIOM_B1_VCS_DISCRETE_WORD", Bit(16, "open", "closed")));
                r.Add(("Hot air 1 valve", "A32NX_COND_CPIOM_B1_TCS_DISCRETE_WORD", Bit(15, "open", "closed")));
                r.Add(("Hot air 2 valve", "A32NX_COND_CPIOM_B1_TCS_DISCRETE_WORD", Bit(16, "open", "closed")));
                r.Add(("Pack 1 operative", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD", Bit(13, "operative", "off")));
                r.Add(("Pack 2 operative", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD", Bit(14, "operative", "off")));
                // Temperature/ventilation controller channel-failure flags (TADD + FWD/AFT VCM).
                for (int c = 1; c <= 2; c++) r.Add(($"TADD channel {c}", $"A32NX_COND_TADD_CHANNEL_{c}_FAILURE", v => Flag(v, "failed", "normal")));
                foreach (var z in new[] { ("FWD", "Forward"), ("AFT", "Aft") })
                    for (int c = 1; c <= 2; c++) r.Add(($"{z.Item2} ventilation controller channel {c}", $"A32NX_VENT_{z.Item1}_VCM_CHANNEL_{c}_FAILURE", v => Flag(v, "failed", "normal")));
                break;
            case 4: // PRESS (Pressurization) — block-1 ARINC words
                // The PRESS page's prominent AUTO/MAN cabin-pressure mode label.
                r.Add(("Pressurization mode", "A32NX_OVHD_PRESS_MAN_ALTITUDE_PB_IS_AUTO", v => v > 0.5 ? "auto" : "manual"));
                r.Add(("Cabin altitude", "A32NX_PRESS_CABIN_ALTITUDE_B1", ABest("A32NX_PRESS_CABIN_ALTITUDE_B1", "feet")));
                r.Add(("Cabin vertical speed", "A32NX_PRESS_CABIN_VS_B1", ABest("A32NX_PRESS_CABIN_VS_B1", "feet per minute")));
                r.Add(("Differential pressure", "A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", ABest("A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", "psi", "0.0")));
                r.Add(("Cabin altitude target", "A32NX_PRESS_CABIN_ALTITUDE_TARGET_B1", ABest("A32NX_PRESS_CABIN_ALTITUDE_TARGET_B1", "feet")));
                // FM1 landing elevation: 0 = not set / AUTO (no destination elevation).
                // Landing elevation is an ARINC429 word (FBW LandingElevation useArinc429Var):
                // decode it; "not set (auto)" when the word isn't NormalOp (no destination).
                r.Add(("Landing elevation", "A32NX_FM1_LANDING_ELEVATION", v => {
                    var w = new SimConnect.Arinc429Word(v);
                    return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} feet" : "not set (auto)";
                }));
                // Outflow valves are the ARINC429 `_OPEN_PERCENTAGE_B1` words (the un-suffixed
                // name does not exist → read 0). B1 = the normally-active CPCS system.
                for (int n = 1; n <= 4; n++) r.Add(($"Outflow valve {n}", $"A32NX_PRESS_OUTFLOW_VALVE_{n}_OPEN_PERCENTAGE_B1", v => A(v, "%")));
                r.Add(("Pack 1", "A32NX_COND_PACK_1_FLOW_VALVE_1_IS_OPEN", v => v > 0.5 ? "on" : "off"));
                r.Add(("Pack 2", "A32NX_COND_PACK_2_FLOW_VALVE_1_IS_OPEN", v => v > 0.5 ? "on" : "off"));
                // Separate cabin-V/S manual/auto control (distinct from the cabin-altitude mode above).
                r.Add(("Cabin V/S control", "A32NX_OVHD_PRESS_MAN_VS_CTL_PB_IS_AUTO", v => v > 0.5 ? "auto" : "manual"));
                // Outflow-valve controller (OCSM) channel-failure flags — 4 valves × 2 channels.
                for (int o = 1; o <= 4; o++)
                    for (int c = 1; c <= 2; c++) r.Add(($"Outflow controller {o} channel {c}", $"A32NX_PRESS_OCSM_{o}_CHANNEL_{c}_FAILURE", v => Flag(v, "failed", "normal")));
                break;
            case 5: // DOORS
                // NOTE: passenger-door states (the 16 `INTERACTIVE POINT OPEN:n` stock
                // SimVars) are intentionally NOT listed here. This A380SdRows set is
                // auto-registered as L:vars (see the loop in the ctor), and forcing a
                // `INTERACTIVE POINT OPEN:n` stock-SimVar name through the L:var path
                // corrupted SimConnect registration and broke aircraft detection. The
                // live Coherent SD scrape already decodes passenger-door states, so the
                // fallback only lists the real-L:var door/window items below.
                r.Add(("Forward cargo door", "A32NX_FWD_DOOR_CARGO_LOCKED", v => v > 0.5 ? "closed" : "open"));
                r.Add(("Aft cargo door", "A32NX_AFT_DOOR_CARGO_LOCKED", v => v > 0.5 ? "closed" : "open"));
                r.Add(("Captain sliding window", "CPT_SLIDING_WINDOW", v => v > 0.05 ? "open" : "closed"));
                r.Add(("First officer sliding window", "FO_SLIDING_WINDOW", v => v > 0.05 ? "open" : "closed"));
                r.Add(("Crew oxygen supply", "PUSH_OVHD_OXYGEN_CREW", v => v > 0.5 ? "on" : "off"));
                // Crew/cabin oxygen pressure — the FBW DOOR page hardcodes 1829 / 1854 PSI (not yet
                // modelled); surfaced to match the real SD. Constant.
                r.Add(("Crew oxygen pressure", "PUSH_OVHD_OXYGEN_CREW", _ => "1829 psi"));
                r.Add(("Cabin oxygen pressure", "PUSH_OVHD_OXYGEN_CREW", _ => "1854 psi"));
                r.Add(("Escape slides", "A32NX_SLIDES_ARMED", v => v > 0.5 ? "armed" : "disarmed"));
                break;
            case 6: // ELEC AC
                for (int n = 1; n <= 4; n++)
                {
                    r.Add(($"Generator {n} voltage", $"A32NX_ELEC_ENG_GEN_{n}_POTENTIAL", V));
                    r.Add(($"Generator {n} load", $"A32NX_ELEC_ENG_GEN_{n}_LOAD", Pct));
                }
                // Engine gens are variable-frequency and the FBW ECAM shows only V + Load
                // for them (no Hz). The APU gens / ext power / static inverter are the
                // constant-~400 Hz sources the ECAM DOES show a frequency for.
                string Hz(double v) => $"{v:0} hertz";
                r.Add(("APU generator 1 voltage", "A32NX_ELEC_APU_GEN_1_POTENTIAL", V));
                r.Add(("APU generator 1 frequency", "A32NX_ELEC_APU_GEN_1_FREQUENCY", Hz));
                r.Add(("APU generator 1 load", "A32NX_ELEC_APU_GEN_1_LOAD", Pct));
                r.Add(("APU generator 2 voltage", "A32NX_ELEC_APU_GEN_2_POTENTIAL", V));
                r.Add(("APU generator 2 frequency", "A32NX_ELEC_APU_GEN_2_FREQUENCY", Hz));
                r.Add(("APU generator 2 load", "A32NX_ELEC_APU_GEN_2_LOAD", Pct));
                r.Add(("External power voltage", "A32NX_ELEC_EXT_PWR_POTENTIAL", V));
                r.Add(("External power frequency", "A32NX_ELEC_EXT_PWR_FREQUENCY", Hz));
                r.Add(("Emergency gen voltage", "A32NX_ELEC_EMER_GEN_POTENTIAL", V));
                r.Add(("Emergency gen load", "A32NX_ELEC_EMER_GEN_LOAD", Pct));
                r.Add(("RAT", "A32NX_RAT_STOW_POSITION", v => v > 0.9 ? "deployed" : "stowed"));
                r.Add(("Static inverter voltage", "A32NX_ELEC_STAT_INV_POTENTIAL", V));
                r.Add(("Static inverter frequency", "A32NX_ELEC_STAT_INV_FREQUENCY", Hz));
                for (int n = 1; n <= 4; n++) r.Add(($"AC bus {n}", $"A32NX_ELEC_AC_{n}_BUS_IS_POWERED", OnOff));
                r.Add(("AC ESS bus", "A32NX_ELEC_AC_ESS_SHED_BUS_IS_POWERED", OnOff));
                r.Add(("AC EMER bus", "A32NX_ELEC_AC_ESS_BUS_IS_POWERED", OnOff));
                // Generator line contactors (990XU1-4) + emergency-gen contactor (5XE).
                for (int n = 1; n <= 4; n++) r.Add(($"Generator {n} line contactor", $"A32NX_ELEC_CONTACTOR_990XU{n}_IS_CLOSED", v => Flag(v, "closed", "open")));
                r.Add(("Emergency generator contactor", "A32NX_ELEC_CONTACTOR_5XE_IS_CLOSED", v => Flag(v, "closed", "open")));
                // AC EHA bus (electro-hydraulic actuators) + its supply contactors (911XN from AC3,
                // 911XH from AC ESS).
                r.Add(("AC EHA bus", "A32NX_ELEC_AC_EHA_BUS_IS_POWERED", OnOff));
                r.Add(("AC EHA contactor from AC 3", "A32NX_ELEC_CONTACTOR_911XN_IS_CLOSED", v => Flag(v, "closed", "open")));
                r.Add(("AC EHA contactor from AC ESS", "A32NX_ELEC_CONTACTOR_911XH_IS_CLOSED", v => Flag(v, "closed", "open")));
                break;
            case 7: // ELEC DC
                // The A380 batteries are NUMERIC-indexed 1/2/3/4 (3 = ESS, 4 = APU). The
                // string-named ..._BAT_ESS_/_APU_POTENTIAL vars do NOT exist (read 0) — only
                // the pushbuttons use the ESS/APU names.
                foreach (var (idx, name) in new[] { ("1", "1"), ("2", "2"), ("3", "ESS"), ("4", "APU") })
                {
                    r.Add(($"Battery {name} voltage", $"A32NX_ELEC_BAT_{idx}_POTENTIAL", v => $"{v:0.0} volts"));
                    r.Add(($"Battery {name} current", $"A32NX_ELEC_BAT_{idx}_CURRENT", v => $"{v:0} amps"));
                    // Charge direction from the current sign (positive = charging into the battery).
                    r.Add(($"Battery {name} status", $"A32NX_ELEC_BAT_{idx}_CURRENT", v => Math.Abs(v) < 1 ? "idle" : v > 0 ? "charging" : "discharging"));
                    // Pushbutton AUTO/OFF (named BAT_1/BAT_2/BAT_ESS/BAT_APU).
                    r.Add(($"Battery {name} pushbutton", $"A32NX_OVHD_ELEC_BAT_{name}_PB_IS_AUTO", v => v > 0.5 ? "auto" : "off"));
                }
                // 4 TRs: TR1(idx1), TR2(idx2), ESS TR(idx3), APU TR(idx4) — voltage + current.
                foreach (var (idx, name, ctc) in new[] { ("1", "1", "990PU1"), ("2", "2", "990PU2"), ("3", "ESS", "6PE"), ("4", "APU", "7PU") })
                {
                    r.Add(($"TR {name} voltage", $"A32NX_ELEC_TR_{idx}_POTENTIAL", V));
                    r.Add(($"TR {name} current", $"A32NX_ELEC_TR_{idx}_CURRENT", v => $"{v:0} amps"));
                    r.Add(($"TR {name} contactor", $"A32NX_ELEC_CONTACTOR_{ctc}_IS_CLOSED", v => Flag(v, "closed", "open")));
                }
                for (int n = 1; n <= 2; n++) r.Add(($"DC bus {n}", $"A32NX_ELEC_DC_{n}_BUS_IS_POWERED", OnOff));
                r.Add(("DC ESS bus", "A32NX_ELEC_DC_ESS_BUS_IS_POWERED", OnOff));
                r.Add(("DC APU bus", "A32NX_ELEC_309PP_BUS_IS_POWERED", OnOff));
                // DC EHA bus + its supply contactors (14PH from DC ESS, 970PN2 from DC 2).
                r.Add(("DC EHA bus", "A32NX_ELEC_DC_EHA_BUS_IS_POWERED", OnOff));
                r.Add(("DC EHA contactor from DC ESS", "A32NX_ELEC_CONTACTOR_14PH_IS_CLOSED", v => Flag(v, "closed", "open")));
                r.Add(("DC EHA contactor from DC 2", "A32NX_ELEC_CONTACTOR_970PN2_IS_CLOSED", v => Flag(v, "closed", "open")));
                break;
            case 8: // FUEL — per-tank quantities are ARINC429 words (kg); FQMS is the page's
                    // primary source (the app previously read the FQDC fallback).
                foreach (var t in new[] { "FEED_1", "FEED_2", "FEED_3", "FEED_4", "LEFT_OUTER", "LEFT_MID", "LEFT_INNER", "RIGHT_OUTER", "RIGHT_MID", "RIGHT_INNER", "TRIM" })
                    r.Add(($"{t.Replace('_', ' ')} tank", $"A32NX_FQMS_{t}_TANK_QUANTITY", AWt));
                r.Add(("Total fuel on board", "A32NX_FQMS_TOTAL_FUEL_ON_BOARD", AWt));
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} fuel used", $"A32NX_FUEL_USED:{e}", Wt));
                r.Add(("APU fuel used", "A32NX_APU_FUEL_USED", AWt));
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} fuel flow", $"A32NX_ENGINE_FF:{e}", Kgh));
                // Fuel-system valve layer (stock FUELSYSTEM VALVE OPEN simvars, pre-registered above).
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} LP valve", $"FUEL_LP_VALVE:{e}", OpenShut));
                for (int x = 1; x <= 4; x++) r.Add(($"Crossfeed valve {x}", $"FUEL_XFEED:{x}", OpenShut));
                r.Add(("Left jettison valve", "FUEL_JETT_L", OpenShut));
                r.Add(("Right jettison valve", "FUEL_JETT_R", OpenShut));
                // Fuel-pump running states — FQMS ARINC429 discrete words (exact bit map from the
                // FBW FuelPage source). LEFT word: feed 1&2 (bits 12-15), left transfer (16-20),
                // left trim (21). RIGHT word: feed 3&4 (bits 12-15), right transfer (16-20),
                // right trim (21). running = the pump is commanded on.
                r.Add(("Feed 1 main pump",   "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(12, "running", "off")));
                r.Add(("Feed 1 standby pump","A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(13, "running", "off")));
                r.Add(("Feed 2 main pump",   "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(14, "running", "off")));
                r.Add(("Feed 2 standby pump","A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(15, "running", "off")));
                r.Add(("Feed 3 main pump",   "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(12, "running", "off")));
                r.Add(("Feed 3 standby pump","A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(13, "running", "off")));
                r.Add(("Feed 4 main pump",   "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(14, "running", "off")));
                r.Add(("Feed 4 standby pump","A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(15, "running", "off")));
                r.Add(("Left outer transfer pump", "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(16, "running", "off")));
                r.Add(("Left mid forward pump",    "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(17, "running", "off")));
                r.Add(("Left mid aft pump",        "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(18, "running", "off")));
                r.Add(("Left inner forward pump",  "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(19, "running", "off")));
                r.Add(("Left inner aft pump",      "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(20, "running", "off")));
                r.Add(("Right outer transfer pump","A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(16, "running", "off")));
                r.Add(("Right mid forward pump",   "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(17, "running", "off")));
                r.Add(("Right mid aft pump",       "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(18, "running", "off")));
                r.Add(("Right inner forward pump", "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(19, "running", "off")));
                r.Add(("Right inner aft pump",     "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(20, "running", "off")));
                r.Add(("Left trim pump",  "A32NX_FQMS_LEFT_FUEL_PUMP_RUNNING_WORD",  Bit(21, "running", "off")));
                r.Add(("Right trim pump", "A32NX_FQMS_RIGHT_FUEL_PUMP_RUNNING_WORD", Bit(21, "running", "off")));
                break;
            case 9: // WHEEL — gear + doors + braked-wheel temperatures (FBW Wheel page L:vars)
                r.Add(("Nose gear", "A32NX_GEAR_CENTER_POSITION", v => v > 98 ? "down and locked" : v < 2 ? "up" : $"in transit {v:0} percent"));
                r.Add(("Left main gear", "A32NX_GEAR_LEFT_POSITION", v => v > 98 ? "down and locked" : v < 2 ? "up" : $"in transit {v:0} percent"));
                r.Add(("Right main gear", "A32NX_GEAR_RIGHT_POSITION", v => v > 98 ? "down and locked" : v < 2 ? "up" : $"in transit {v:0} percent"));
                r.Add(("Nose gear door", "A32NX_GEAR_DOOR_CENTER_POSITION", v => v > 2 ? "open" : "closed"));
                r.Add(("Left gear door", "A32NX_GEAR_DOOR_LEFT_POSITION", v => v > 2 ? "open" : "closed"));
                r.Add(("Right gear door", "A32NX_GEAR_DOOR_RIGHT_POSITION", v => v > 2 ? "open" : "closed"));
                for (int w = 1; w <= 16; w++) r.Add(($"Brake {w} temp", $"A32NX_REPORTED_BRAKE_TEMPERATURE_{w}", C));
                // Tire pressure — the FBW WHEEL page hardcodes 220 psi for every wheel (not yet
                // modelled); surfaced once to match the real SD. Constant.
                r.Add(("Tire pressure (all wheels)", "GEAR_CENTER_POSITION", _ => "220 psi"));
                // Wing-brake accumulator pressure — likewise hardcoded (4.8 × 1000 psi) on the FBW
                // WHEEL page; surfaced to match the SD. Constant. (Body-wheel-steering angle,
                // A-SKID per-bogie + BRK/STEER/LG CTL computer status are not modelled — no L-vars.)
                r.Add(("Wing accumulator pressure", "GEAR_CENTER_POSITION", _ => "4800 psi"));
                break;
            case 10: // HYD (A380 has Green + Yellow)
                foreach (var (sys, e1, e2) in new[] { ("GREEN", 1, 2), ("YELLOW", 3, 4) })
                {
                    string s = sys[0] == 'G' ? "Green" : "Yellow";
                    r.Add(($"{s} pressure", $"A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE", Psi));
                    r.Add(($"{s} system pressurised", $"A32NX_HYD_{sys}_SYSTEM_1_SECTION_PRESSURE_SWITCH", v => Flag(v, "yes", "no")));
                    r.Add(($"{s} reservoir level", $"A32NX_HYD_{sys}_RESERVOIR_LEVEL", v => $"{v:0.0} gallons"));
                    r.Add(($"{s} reservoir low", $"A32NX_HYD_{sys}_RESERVOIR_LEVEL_IS_LOW", v => Flag(v, "LOW", "normal")));
                    r.Add(($"{s} reservoir overheat", $"A32NX_HYD_{sys}_RESERVOIR_OVHT", v => Flag(v, "OVERHEAT", "normal")));
                    r.Add(($"{s} reservoir air pressure low", $"A32NX_HYD_{sys}_RESERVOIR_AIR_PRESSURE_IS_LOW", v => Flag(v, "LOW", "normal")));
                    // Two engine-driven pumps per system (one per engine), pushbutton + DISC.
                    foreach (int e in new[] { e1, e2 })
                    {
                        // Pump-section index per the FBW Engine.tsx: pump A = 1+2*((e-1)%2), pump B = +1.
                        int pi = 1 + 2 * ((e - 1) % 2);
                        r.Add(($"Engine {e} pump A", $"A32NX_OVHD_HYD_ENG_{e}A_PUMP_PB_IS_AUTO", Auto));
                        r.Add(($"Engine {e} pump A pressure", $"A32NX_HYD_{sys}_PUMP_{pi}_SECTION_PRESSURE_SWITCH", v => Flag(v, "pressurised", "low")));
                        r.Add(($"Engine {e} pump A fire valve", $"A32NX_HYD_{sys}_PUMP_{pi}_FIRE_VALVE_OPENED", OpenShut));
                        r.Add(($"Engine {e} pump B", $"A32NX_OVHD_HYD_ENG_{e}B_PUMP_PB_IS_AUTO", Auto));
                        r.Add(($"Engine {e} pump B pressure", $"A32NX_HYD_{sys}_PUMP_{pi + 1}_SECTION_PRESSURE_SWITCH", v => Flag(v, "pressurised", "low")));
                        r.Add(($"Engine {e} pump B fire valve", $"A32NX_HYD_{sys}_PUMP_{pi + 1}_FIRE_VALVE_OPENED", OpenShut));
                        r.Add(($"Engine {e} pumps disconnect", $"A32NX_HYD_ENG_{e}AB_PUMP_DISC", v => Flag(v, "disconnected", "normal")));
                    }
                    // Two electric pumps per system (A/B) — pump 5 (A) / 6 (B) section switch + OFF-PB.
                    foreach (var p in new[] { "A", "B" })
                    {
                        string ep = $"{sys[0]}{p}";   // GA, GB, YA, YB
                        int epi = p == "A" ? 5 : 6;
                        r.Add(($"{s} electric pump {p}", $"A32NX_HYD_{ep}_EPUMP_ACTIVE", Active));
                        r.Add(($"{s} electric pump {p} pushbutton", $"A32NX_OVHD_HYD_EPUMP{ep}_OFF_PB_IS_AUTO", Auto));
                        r.Add(($"{s} electric pump {p} pressure", $"A32NX_HYD_{sys}_PUMP_{epi}_SECTION_PRESSURE_SWITCH", v => Flag(v, "pressurised", "low")));
                        r.Add(($"{s} electric pump {p} overheat", $"A32NX_HYD_{ep}_EPUMP_OVHT", v => Flag(v, "OVERHEAT", "normal")));
                    }
                }
                break;
            case 11: // F/CTL — computer health + surface deflections + trims (FBW Fctl page)
                for (int n = 1; n <= 3; n++) r.Add(($"PRIM {n}", $"A32NX_PRIM_{n}_HEALTHY", Healthy));
                for (int n = 1; n <= 3; n++) r.Add(($"SEC {n}", $"A32NX_SEC_{n}_HEALTHY", Healthy));
                // Aileron / elevator / rudder deflections (normalized → percent of travel).
                foreach (var side in new[] { "LEFT", "RIGHT" })
                    foreach (var pos in new[] { "OUTWARD", "MIDDLE", "INWARD" })
                        r.Add(($"{(side == "LEFT" ? "Left" : "Right")} {pos.ToLower()} aileron", $"A32NX_HYD_AILERON_{side}_{pos}_DEFLECTION", Defl));
                foreach (var side in new[] { "LEFT", "RIGHT" })
                    foreach (var pos in new[] { "OUTWARD", "INWARD" })
                        r.Add(($"{(side == "LEFT" ? "Left" : "Right")} {pos.ToLower()} elevator", $"A32NX_HYD_ELEVATOR_{side}_{pos}_DEFLECTION", Defl));
                r.Add(("Upper rudder", "A32NX_HYD_UPPER_RUDDER_DEFLECTION", Defl));
                r.Add(("Lower rudder", "A32NX_HYD_LOWER_RUDDER_DEFLECTION", Defl));
                for (int sp = 1; sp <= 8; sp++)
                {
                    r.Add(($"Left spoiler {sp}", $"A32NX_HYD_SPOILER_{sp}_LEFT_DEFLECTION", Defl));
                    r.Add(($"Right spoiler {sp}", $"A32NX_HYD_SPOILER_{sp}_RIGHT_DEFLECTION", Defl));
                }
                r.Add(("Pitch trim (THS)", "ELEVATOR_TRIM", v => $"{Math.Abs(v):0.0} degrees {(v >= 0 ? "up" : "down")}"));
                // Rudder trim — the SEC ARINC429 degrees word (positive = nose-LEFT), same
                // source + convention as the FCC-panel "Rudder Trim" readout (the SD F/CTL page
                // shows it but A380SdRows previously omitted it).
                r.Add(("Rudder trim", "A32NX_SEC_1_RUDDER_ACTUAL_POSITION", v =>
                {
                    var w = new SimConnect.Arinc429Word(v);
                    // FBW selects SEC3 as the rudder-trim source if SEC1's word is invalid.
                    if (!w.IsNormalOperation && !w.IsFunctionalTest)
                    {
                        double? cv = _sdRender?.GetCachedVariableValue("A32NX_SEC_3_RUDDER_ACTUAL_POSITION");
                        if (!cv.HasValue) return "not available";
                        var w3 = new SimConnect.Arinc429Word(cv.Value);
                        if (!w3.IsNormalOperation && !w3.IsFunctionalTest) return "not available";
                        w = w3;
                    }
                    return Math.Abs(w.Value) < 0.05 ? "Neutral" : $"{Math.Abs(w.Value):0.0} degrees {(w.Value > 0 ? "left" : "right")}";
                }));
                r.Add(("Speed brake handle", "A32NX_SPOILERS_HANDLE_POSITION", v => $"{v * 100:0} %"));
                r.Add(("Ground spoilers armed", "A32NX_SPOILERS_ARMED", v => v > 0.5 ? "armed" : "disarmed"));
                r.Add(("Flaps angle", "A32NX_LEFT_FLAPS_ANGLE", v => $"{v:0.0} degrees"));
                r.Add(("Slats angle", "A32NX_LEFT_SLATS_ANGLE", v => $"{v:0.0} degrees"));
                break;
            case 13: // CRUISE — fuel + cabin summary
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} fuel flow", $"A32NX_ENGINE_FF:{e}", Kgh));
                for (int e = 1; e <= 4; e++) r.Add(($"Engine {e} fuel used", $"A32NX_FUEL_USED:{e}", Wt));
                r.Add(("APU fuel used", "A32NX_APU_FUEL_USED", AWt));
                r.Add(("Cabin altitude", "A32NX_PRESS_CABIN_ALTITUDE_B1", ABest("A32NX_PRESS_CABIN_ALTITUDE_B1", "feet")));
                r.Add(("Cabin vertical speed", "A32NX_PRESS_CABIN_VS_B1", ABest("A32NX_PRESS_CABIN_VS_B1", "feet per minute")));
                r.Add(("Differential pressure", "A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", ABest("A32NX_PRESS_CABIN_DELTA_PRESSURE_B1", "psi", "0.0")));
                r.Add(("Landing elevation", "A32NX_FM1_LANDING_ELEVATION", v => {
                    var w = new SimConnect.Arinc429Word(v);
                    return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} feet" : "not set (auto)";
                }));
                r.Add(("Cockpit temp", "A32NX_COND_CKPT_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Forward cargo temp", "A32NX_COND_CARGO_FWD_TEMP", v => $"{v:0.0} degrees"));
                r.Add(("Bulk cargo temp", "A32NX_COND_CARGO_BULK_TEMP", v => $"{v:0.0} degrees"));
                break;
        }
        return r;
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
                new Forms.FBWA380.FBWA380HeadingWindow(this, simConnect, announcer).ShowForm();
                return true;
            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA380.FBWA380SpeedWindow(this, simConnect, announcer).ShowForm();
                return true;
            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA380.FBWA380AltitudeWindow(this, simConnect, announcer).ShowForm();
                return true;
            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA380.FBWA380VSWindow(this, simConnect, announcer).ShowForm();
                return true;
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA380.FBWA380AutopilotWindow(this, simConnect, announcer).ShowForm();
                return true;

            // FCU knob push/pull (Shift+1..4 push, Ctrl+1..4 pull). Drive the FCU via
            // the legacy cockpit H-events `A320_Neo_FCU_<axis>_PUSH/PULL` — the same
            // path the physical knob uses. The A380X FCU is a self-contained instrument
            // whose managers listen to these H-events (verified live: firing
            // A320_Neo_FCU_HDG_PUSH/PULL and _ALT_PUSH/PULL on the FCU bus moves the
            // autopilot slot index 1<->2; SPEED/VS names confirmed from the FBW FCU
            // source). The previously-fired `A32NX.FCU_TO_AP_*` events were the FCU's
            // *internal* downstream events and do NOT route to the autopilot via
            // TransmitClientEvent — a live probe confirmed A32NX.FCU_TO_AP_HDG_PUSH left
            // the slot unchanged while the H-event moved it. Fired via the calculator
            // (>H:) path (same as the clock CHR H-event and other cockpit controls).
            //
            // NO readback here (Fenix-style): the managed<->selected RESULT is announced
            // by the always-on managed-state monitor (Mon "…_MANAGED…" -> "Heading Mode:
            // Managed/Selected"), which fires only on a REAL transition. The old
            // RequestFCU*WithStatus readback spoke the value on every press regardless of
            // whether anything changed, which was identical to the output-mode read query
            // and masked the dead actuation.
            // CRITICAL: the A380's NEW FCU consumes K-events (K:A32NX.FCU_*), NOT the dotted
            // A320 H-events — firing the H-event does NOTHING (this is why one push of the ALT
            // knob "did nothing" and you had to push again: the first push was the dead H-event).
            // Route through FireFCUButton, exactly like the FCU window Push/Pull buttons, so a
            // single push actually fires (and re-reads the value). Event names match the windows.
            case HotkeyAction.FCUHeadingPush: FireFCUButton("A32NX.FCU_TO_AP_HDG_PUSH", simConnect, announcer); return true;
            case HotkeyAction.FCUHeadingPull: FireFCUButton("A32NX.FCU_TO_AP_HDG_PULL", simConnect, announcer); return true;
            case HotkeyAction.FCUSpeedPush: FireFCUButton("A32NX.FCU_SPD_PUSH", simConnect, announcer); return true;
            case HotkeyAction.FCUSpeedPull: FireFCUButton("A32NX.FCU_SPD_PULL", simConnect, announcer); return true;
            case HotkeyAction.FCUAltitudePush: FireFCUButton("A32NX.FCU_ALT_PUSH", simConnect, announcer); return true;
            case HotkeyAction.FCUAltitudePull: FireFCUButton("A32NX.FCU_ALT_PULL", simConnect, announcer); return true;
            // The A380X V/S knob is pull-to-engage (managed vertical is armed via the ALT knob),
            // so VS push is a no-op on the jet; fire the K-event anyway for consistency.
            case HotkeyAction.FCUVSPush: FireFCUButton("A32NX.FCU_VS_PUSH", simConnect, announcer); return true;
            case HotkeyAction.FCUVSPull: FireFCUButton("A32NX.FCU_TO_AP_VS_PULL", simConnect, announcer); return true;

            case HotkeyAction.ReadFlaps:
            {
                // Announce straight from the live cache — the handle index is a monitored
                // combo, so a forced request of an UNCHANGED value never re-fires
                // ProcessSimVarUpdate and the read stayed silent (the "] L does nothing"
                // bug). Fall back to a request only if it isn't cached yet.
                double? fv = simConnect.GetCachedVariableValue("A32NX_FLAPS_HANDLE_INDEX");
                if (fv.HasValue)
                {
                    string[] detents = { "Up", "1", "2", "3", "Full" };
                    int i = (int)Math.Round(fv.Value);
                    announcer.AnnounceImmediate("Flaps " + (i >= 0 && i < detents.Length ? detents[i] : fv.Value.ToString()));
                }
                else if (simConnect.IsConnected) { _reqFlaps = true; simConnect.RequestVariable("A32NX_FLAPS_HANDLE_INDEX", forceUpdate: true); }
                return true;
            }
            case HotkeyAction.ReadGear:
            {
                double? gv = simConnect.GetCachedVariableValue("A32NX_GEAR_HANDLE_POSITION");
                if (gv.HasValue) announcer.AnnounceImmediate(gv.Value > 0.5 ? "Gear down" : "Gear up");
                else if (simConnect.IsConnected) { _reqGear = true; simConnect.RequestVariable("A32NX_GEAR_HANDLE_POSITION", forceUpdate: true); }
                return true;
            }
            // On-demand readouts ported from the A320 (vars shared / already defined).
            case HotkeyAction.ReadSpeedVLS: RequestReadout(simConnect, "A32NX_SPEEDS_VLS", "V L S", "knots"); return true;
            case HotkeyAction.ReadSpeedF: RequestReadout(simConnect, "A32NX_SPEEDS_F", "F speed", "knots"); return true;
            case HotkeyAction.ReadSpeedGD: RequestReadout(simConnect, "A32NX_SPEEDS_GD", "Green Dot speed", "knots"); return true;
            case HotkeyAction.ReadSpeedS: RequestReadout(simConnect, "A32NX_SPEEDS_S", "S speed", "knots"); return true;
            case HotkeyAction.ReadSpeedVS: RequestReadout(simConnect, "A32NX_SPEEDS_VS", "V S", "knots"); return true;
            case HotkeyAction.ReadSpeedVFE: RequestReadout(simConnect, "A32NX_SPEEDS_VFEN", "V F E next", "knots"); return true;
            // Fuel + gross weight are spoken fleet-consistently (matching PMDG / Fenix):
            // plain key = pounds, Shift key = kilograms, via the shared SimConnectManager
            // requests (identical phrasing across all aircraft). Deterministic units —
            // NOT the EFB-following _metricWeight path, so they never surprise the pilot.
            case HotkeyAction.ReadFuelQuantity: // F -> "Fuel on board N pounds"
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY, "FUEL TOTAL QUANTITY WEIGHT", "pounds", "FUEL_QUANTITY"); return true;
            // Phase 4 parity with the A320: ReadFuelInfo (same as ReadFuelQuantity) + a
            // Ctrl+B "Set Altimeter" dialog (the A380 baro uses the stock KOHLSMAN_SET,
            // unit = millibars*16, NOT the A320's A32NX.FCU_EFIS_*_BARO_SET events).
            case HotkeyAction.ReadFuelInfo: // Shift+F -> "Fuel on board N kilograms"
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_KG, "FUEL TOTAL QUANTITY WEIGHT", "pounds", "FUEL_QUANTITY_KG"); return true;
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA380.FBWA380BaroWindow(this, simConnect, announcer).ShowForm();
                return true;
            case HotkeyAction.ReadApproachCapability: RequestReadout(simConnect, "A32NX_APPROACH_CAPABILITY", "Approach capability", "", _apprCapMap); return true;
            // Dedicated display WINDOWS were removed for the FBW aircraft: the SD reads
            // via the ECAM Control Panel page combo + status box, the E/WD has its own
            // status box (Displays > E/WD panel), and PFD/ND/ISIS flight values stay on
            // the individual readout hotkeys (ReadAltimeter/ReadSpeed/... — kept). The
            // ShowPFD/ShowNavigationDisplay/ShowECAM/ShowStatusPage hotkeys are gone;
            // the shared ReadDisplay* actions fall through to no-op on the FBW jets
            // (still used by PMDG/Fenix). Alt+E still speaks the current E/WD lines.
            // Alt+E now opens the E/WD as a pop-out WINDOW (auto-refreshing, F5 to
            // refresh, Escape to close) showing the whole E/WD — engine parameters plus
            // the live ECAM memo / warning lines — instead of speaking it once. The old
            // spoken read lives on as ReadAllEwdWarnings (still used by the live monitor).
            case HotkeyAction.ReadDisplayUpperECAM:
                hotkeyManager.ExitOutputHotkeyMode();
                new Forms.FbwEwdWindow("A380 E/WD — Engine / Warning Display",
                    () => BuildEwdWindowTextAsync(simConnect), announcer).Show();
                return true;
            // W repurposed to gross weight in pounds (matches PMDG / Fenix, which also
            // repurpose the waypoint key). The MCDU/MFD covers waypoint data.
            case HotkeyAction.ReadWaypointInfo: // W -> "Gross weight N pounds, center of gravity X% MAC"
                announcer.AnnounceImmediate(_gwKgCache > 0
                    ? $"Gross weight {_gwKgCache * 2.204625:0} pounds{CgMacPhrase()}"
                    : "Gross weight not available");
                return true;
            case HotkeyAction.ReadAltimeter:
                // Announce the captain's FBW EIS baro — STD/unit-aware, the SAME value + phrasing
                // as the live auto-announce and the set (PMDG/Fenix-style), instead of the stock
                // KOHLSMAN in inches (which ignored STD + the selected unit = the "funky" read).
                // Fenix/PMDG-style: terse, no "Captain" prefix, both units (the A380 EFIS is
                // split per side but every sibling reads ONE altimeter with no side prefix, so
                // we speak the captain side only, like the existing handler already chose).
                // Read STD + the baro value LIVE from the cache so a stale change-tracked
                // flag can never make this say "standard" while the EFIS is actually on QNH
                // (same class of fix as the flaps cache read). NOTE: above the transition
                // altitude the EFIS genuinely IS standard, so "standard" there is correct.
                double? isStdNow = simConnect.GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO_IS_STD");
                bool stdNow = isStdNow.HasValue ? isStdNow.Value > 0.5 : (_baroStdL == true);
                if (stdNow) { announcer.AnnounceImmediate("Altimeter standard"); return true; }
                int hpaNow = _lastBaroL;
                double? baroWord = simConnect.GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO_HPA");
                if (baroWord.HasValue && BaroHpa(new Arinc429Word(baroWord.Value).ValueOr(0), out int hpaDec) && hpaDec > 0) hpaNow = hpaDec;
                if (hpaNow > 0) { announcer.AnnounceImmediate($"Altimeter: {hpaNow}, {hpaNow / 33.8639:0.00}"); return true; }
                // Fallback (EIS baro not seeded yet): stock KOHLSMAN.
                if (simConnect.IsConnected) { _reqBaro = true; simConnect.RequestVariable("KOHLSMAN_HG", forceUpdate: true); }
                return true;
            case HotkeyAction.ReadGrossWeightKg: // Shift+W -> "Gross weight N kilograms, center of gravity X% MAC"
                announcer.AnnounceImmediate(_gwKgCache > 0
                    ? $"Gross weight {_gwKgCache:0} kilograms{CgMacPhrase()}"
                    : "Gross weight not available");
                return true;
            case HotkeyAction.ReadHeading: RequestFCUHeadingWithStatus(simConnect); return true;
            case HotkeyAction.ReadSpeed: RequestFCUSpeedWithStatus(simConnect); return true;
            case HotkeyAction.ReadAltitude: RequestFCUAltitudeWithStatus(simConnect); return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA: RequestFCUVSWithStatus(simConnect); return true;
            // Ctrl+W (output): ND TO-waypoint name/distance/bearing via SimVars (no Coherent — see NdWaypointReadout).
            case HotkeyAction.ReadNDWaypoint: Services.NdWaypointReadout.Announce(simConnect, announcer); return true;
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

    // Build the FULL upper-E/WD text for the Alt+E pop-out window (FbwEwdWindow):
    // engine primaries grouped per parameter + thrust rating/limit + autothrust message
    // + reversers + bleed + IDLE memo + the live ECAM memo / warning lines. Self-contained
    // (mirrors the SD "Upper E/WD" decode at the ECAM-CP combo) so the always-on SD path is
    // never touched. Requests the engine vars, gives the WASM a moment, then reads the cache.
    public async Task<string> BuildEwdWindowTextAsync(SimConnectManager simConnect)
    {
        if (!simConnect.IsConnected) return "(not connected to the simulator)";
        int[] engs = { 1, 2, 3, 4 };
        foreach (int e in engs)
        {
            foreach (var p in new[] { "N1", "EGT", "N2", "N3", "FF" })
                simConnect.RequestVariable($"A32NX_ENGINE_{p}:{e}", forceUpdate: true);
            simConnect.RequestVariable($"A32NX_AUTOTHRUST_N1_COMMANDED:{e}", forceUpdate: true);
            simConnect.RequestVariable($"A32NX_ENGINE_STATE:{e}", forceUpdate: true);
        }
        foreach (var g in new[] { "A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE", "A32NX_AIRLINER_TO_FLEX_TEMP",
                                  "A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE", "A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA",
                                  "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", "A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD",
                                  "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4",
                                  "A32NX_AUTOTHRUST_MODE_MESSAGE", "A32NX_AUTOTHRUST_REVERSE:2", "A32NX_AUTOTHRUST_REVERSE:3" })
            simConnect.RequestVariable(g, forceUpdate: true);
        await Task.Delay(500);

        string Grp(string varFmt, Func<double, string> fmt)
        {
            var parts = new List<string>();
            foreach (int e in engs)
            {
                double? cv = simConnect.GetCachedVariableValue(string.Format(varFmt, e));
                parts.Add($"Engine {e} " + (cv.HasValue ? fmt(cv.Value) : "--"));
            }
            return string.Join(", ", parts);
        }
        string[] thrModes = { "", "CLB", "MCT", "FLX", "TOGA", "MREV" };
        int tltI = (int)Math.Round(simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE") ?? 0);
        string thrMode = (tltI >= 1 && tltI < thrModes.Length) ? thrModes[tltI] : "none";
        double? flex = simConnect.GetCachedVariableValue("A32NX_AIRLINER_TO_FLEX_TEMP");
        if (thrMode == "FLX" && flex.HasValue && flex.Value > 0) thrMode += $" {flex.Value:0}°C";
        string EngState(double v) => v >= 2 ? "starting" : v >= 1 ? "on" : "off";
        double idleLim = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE") ?? 0;
        double togaLim = simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA") ?? 100;
        string ThrPct(int e)
        {
            double? n1 = simConnect.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}");
            if (!n1.HasValue || togaLim <= idleLim) return $"Engine {e} --";
            double off = (simConnect.GetCachedVariableValue($"A32NX_ENGINE_STATE:{e}") ?? 0) == 1 ? 0.042 : 0;
            double frac = Math.Min(1.0, Math.Max(0.0, (n1.Value - idleLim) / (togaLim - idleLim)) * (1 - off) + off);
            return $"Engine {e} {frac * 100:0}%";
        }
        var lines = new List<string>
        {
            "Thrust rating: " + thrMode,
            "Thrust limit: " + string.Join(", ", engs.Select(ThrPct)),
            "N1: "          + Grp("A32NX_ENGINE_N1:{0}",  v => $"{v:0.0}%"),
            "N1 command: "  + Grp("A32NX_AUTOTHRUST_N1_COMMANDED:{0}", v => $"{v:0.0}%"),
            "EGT: "         + Grp("A32NX_ENGINE_EGT:{0}", v => $"{v:0}°C"),
            "N2: "          + Grp("A32NX_ENGINE_N2:{0}",  v => $"{v:0.0}%"),
            "N3: "          + Grp("A32NX_ENGINE_N3:{0}",  v => $"{v:0.0}%"),
            "Fuel Flow: "   + Grp("A32NX_ENGINE_FF:{0}",  v => $"{v:0} kg/h"),
            "Engine state: "+ Grp("A32NX_ENGINE_STATE:{0}", EngState),
        };
        string[] athrMsgs = { "", "THR LK", "LVR TOGA", "LVR CLB", "LVR MCT", "LVR ASYM" };
        int athrMsg = (int)Math.Round(simConnect.GetCachedVariableValue("A32NX_AUTOTHRUST_MODE_MESSAGE") ?? 0);
        if (athrMsg >= 1 && athrMsg < athrMsgs.Length) lines.Add("Autothrust message: " + athrMsgs[athrMsg]);
        var revOn = new[] { 2, 3 }.Where(e => (simConnect.GetCachedVariableValue($"A32NX_AUTOTHRUST_REVERSE:{e}") ?? 0) > 0.5).ToList();
        if (revOn.Count > 0) lines.Add("Reverser: " + string.Join(" and ", revOn.Select(e => $"engine {e}")) + " deployed");
        var bleed = new List<string>();
        var agsWord = new SimConnect.Arinc429Word(simConnect.GetCachedVariableValue("A32NX_COND_CPIOM_B1_AGS_DISCRETE_WORD") ?? 0);
        if (agsWord.BitValueOr(13, false) || agsWord.BitValueOr(14, false)) bleed.Add("packs");
        if (engs.Any(e => (simConnect.GetCachedVariableValue($"ENG_ANTI_ICE:{e}") ?? 0) > 0.5)) bleed.Add("nacelle anti-ice");
        if ((simConnect.GetCachedVariableValue("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON") ?? 0) > 0.5) bleed.Add("wing anti-ice");
        if (bleed.Count > 0) lines.Add("Bleed: " + string.Join(", ", bleed));
        double? fmgcPhase = simConnect.GetCachedVariableValue("A32NX_FMGC_FLIGHT_PHASE");
        int idleEngs = engs.Count(e => { var n1 = simConnect.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}"); return n1.HasValue && n1.Value <= idleLim + 2; });
        if (fmgcPhase.HasValue && fmgcPhase.Value >= 4 && idleEngs >= 3) lines.Add("IDLE");

        var memos = new List<string>();
        foreach (var lr in new[] { "LEFT", "RIGHT" })
            for (int i = 1; i <= 10; i++)
                if (_lastEwdCode.TryGetValue($"A32NX_EWD_LOWER_{lr}_LINE_{i}", out var code) && code != 0)
                {
                    string mtext = EWDMessageLookupA380.GetMessage(code);
                    if (!string.IsNullOrWhiteSpace(mtext) && !mtext.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                    {
                        string priority = EWDMessageLookupA380.GetMessagePriority(code);
                        memos.Add(string.IsNullOrEmpty(priority) ? mtext : $"{mtext} ({priority})");
                    }
                }
        lines.Add("");
        lines.Add(memos.Count == 0 ? "Memo / warnings: none" : "Memo / warnings:");
        lines.AddRange(memos);
        return string.Join("\r\n", lines);
    }

    // ECAM System Display (SD) window — decodes the FQMS/PRESS/APU ARINC429 words
    // + plain SD scalars (Fuel/Engine/Press/APU/Elec/Hyd/Cond). Opened from
    // ShowStatusPage and ShowECAM (the A380 also announces E/WD live).
    private void ShowSystemDisplayWindow(ScreenReaderAnnouncer announcer, SimConnectManager simConnect)
    {
        var dialog = new Forms.FBWA380.FBWA380SystemDisplayForm(announcer, simConnect);
        dialog.Show();
    }

    public void RequestFCUHeadingWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqHdg = true; _pHdgVal = _pHdgMgd = null;
        s.RequestVariable("A32NX_AUTOPILOT_HEADING_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_HDG_MANAGED_DASHES", forceUpdate: true);
    }

    public void RequestFCUSpeedWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqSpd = true; _pSpdVal = _pSpdMgd = null;
        s.RequestVariable("A32NX_AUTOPILOT_SPEED_SELECTED", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_SPD_MANAGED_DOT", forceUpdate: true);
    }

    public void RequestFCUAltitudeWithStatus(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        _reqAlt = true; _pAltVal = _pAltMgd = null;
        s.RequestVariable("FCU_ALT_VALUE", forceUpdate: true);
        s.RequestVariable("A32NX_FCU_ALT_MANAGED", forceUpdate: true);
    }

    public void RequestFCUVSWithStatus(SimConnectManager s)
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
            // HDG·V/S <-> TRK·FPA toggle: re-read heading (its label flips HDG<->TRK).
            case "A32NX.FCU_TRK_FPA_TOGGLE_PUSH": RequestFCUHeadingWithStatus(simConnect); break;
            // VHF active/standby swap: announce the swap (the new active is then on
            // the "VHF N Active" read-out in the panel).
            case "COM1_RADIO_SWAP": announcer.Announce("VHF 1 active and standby swapped"); break;
            case "COM2_RADIO_SWAP": announcer.Announce("VHF 2 active and standby swapped"); break;
            case "COM3_RADIO_SWAP": announcer.Announce("VHF 3 active and standby swapped"); break;
        }
    }

    // ---- Public FCU API for the dedicated A380 FCU windows (Forms/FBWA380/*) ----
    // Forms validate input, then call these; all set/readback mechanism lives here
    // (already live-verified). Each setter fires the event and speaks the readback.

    // hdg: 0-360 whole degrees.
    public bool SetFCUHeadingValue(int hdg, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        s.SendEvent("A32NX.FCU_HDG_SET", (uint)hdg);
        RequestFCUHeadingWithStatus(s);
        return true;
    }

    // internalSpeed: knots (100-399) OR Mach*100 (10-99). Caller does the *100.
    public bool SetFCUSpeedValue(int internalSpeed, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        s.SendEvent("A32NX.FCU_SPD_SET", (uint)internalSpeed);
        RequestFCUSpeedWithStatus(s);
        return true;
    }

    // feet: already converted from metres by the caller if metric.
    public bool SetFCUAltitudeValue(double feet, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        uint rounded = (uint)(Math.Round(feet / 100) * 100);
        s.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", 100);
        System.Threading.Thread.Sleep(50);
        s.SendEvent("A32NX.FCU_ALT_SET", rounded);
        if (_metricAlt)
        {
            int m = (int)Math.Round(rounded * 0.3048);
            a.AnnounceImmediate($"Altitude set to {m} metres, {rounded} feet");
        }
        else a.AnnounceImmediate($"Altitude set to {rounded} feet");
        return true;
    }

    // value: signed V/S (-6000..6000 fpm) OR FPA (-9.9..9.9 deg). Same calc-code
    // path the old dialog used (negatives overflow SendEvent's uint).
    public bool SetFCUVSValue(double value, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        int toSend = Math.Abs(value) < 100 ? (int)(value * 100) : (int)value;
        s.ExecuteCalculatorCode($"{toSend} (>K:A32NX.FCU_VS_SET)");
        a.AnnounceImmediate($"Vertical speed set to {value}");
        return true;
    }

    // Fire a push/pull/toggle event and speak the resulting value (readback routed
    // through OnPanelButtonFired's switch — heading/speed/alt/vs/trk-fpa/spd-mach).
    public void FireFCUButton(string evt, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return; }
        if (evt == "A32NX.FCU_SPD_MACH_TOGGLE_PUSH") s.ExecuteCalculatorCode(SpdMachToggleRpn);
        // The A380's NEW FCU consumes EVERY A32NX.FCU_* button as a K-EVENT, not the A320-era
        // H-event the SendEvent path produces — live-verified: (>H:A32NX.FCU_SPD_PUSH) left the
        // managed dot at 0, (>K:A32NX.FCU_SPD_PUSH) set it to 1. The Speed/Alt/Hdg/VS push-pull
        // windows already pass the correct A380 event names (incl. the TO_AP_HDG/VS variants), so
        // firing them as K-events makes all those FCU knob buttons work. (SPD/MACH stays the
        // conditional RPN above; TRK/FPA toggle goes through here too — verify it separately.)
        else if (evt.StartsWith("A32NX.FCU_", StringComparison.Ordinal)) s.ExecuteCalculatorCode($"(>K:{evt})");
        else s.SendEvent(evt);
        OnPanelButtonFired(evt, s, a);
    }

    // ---- Smooth slider ramp (motorised cockpit seats / armrests / sunshades / visors) ----
    // Writing the target L:var in ONE step makes the 3-D model SNAP there — you only hear a
    // single "tick" of the motor. Real motorised seats move gradually while the switch is held,
    // so we step the L:var toward the target a few units every 40 ms ON THE UI THREAD (a
    // WinForms timer — never a thread-pool timer, which would call SimConnect off-thread). The
    // FBW plays the sustained motor sound + smooth animation. _sliderCurrent is authoritative
    // once a slider is first touched (seeded from the cache) so the ramp stays smooth.
    private readonly Dictionary<string, double> _sliderTarget = new();
    private readonly Dictionary<string, double> _sliderCurrent = new();
    private System.Windows.Forms.Timer? _sliderRampTimer;
    private SimConnectManager? _sliderRampSim;

    private void RampSliderTo(string lvar, double target, SimConnectManager simConnect)
    {
        _sliderRampSim = simConnect;
        target = Math.Max(0.0, Math.Min(100.0, target));
        _sliderTarget[lvar] = target;
        if (!_sliderCurrent.ContainsKey(lvar))
            _sliderCurrent[lvar] = simConnect.GetCachedVariableValue(lvar) ?? target;
        if (_sliderRampTimer == null)
        {
            _sliderRampTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _sliderRampTimer.Tick += (s, e) => SliderRampTick();
            _sliderRampTimer.Start();
        }
    }

    private void SliderRampTick()
    {
        var sim = _sliderRampSim;
        if (sim == null || !sim.IsConnected) { StopSliderRamp(); return; }
        const double step = 3.0;   // ~100 units in ~1.3 s — a believable seat-motor speed
        foreach (var lvar in _sliderTarget.Keys.ToList())
        {
            double target = _sliderTarget[lvar];
            double cur = _sliderCurrent.TryGetValue(lvar, out var c) ? c : target;
            if (Math.Abs(target - cur) <= step) { cur = target; _sliderTarget.Remove(lvar); }
            else cur += Math.Sign(target - cur) * step;
            _sliderCurrent[lvar] = cur;
            sim.ExecuteCalculatorCode(cur.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " (>L:" + lvar + ")");
        }
        if (_sliderTarget.Count == 0) StopSliderRamp();
    }

    private void StopSliderRamp() { _sliderRampTimer?.Stop(); _sliderRampTimer?.Dispose(); _sliderRampTimer = null; }

    // ---- Crew-seat START/STOP motor ----
    // A seat motor RUNS while a direction is selected and HALTS on "Stopped". The motor SOUND is a
    // Wwise Continuous loop keyed to the DERIVATIVE of the position L:var, so it sustains only while
    // the var changes every frame. We drive a small step every 20 ms on a UI-thread timer.
    //
    // RELIABILITY FIX (the "moves one tick then stops" bug): the calc string must be UNIQUE every
    // tick. MobiFlight's WASM REGISTERS each distinct "MF.SimVars.Set.<code>" string and fires it
    // ONCE; sending the SAME read-modify-write string every tick got de-duplicated -> one tick of
    // movement then silence. We keep the read-modify-write (so it always reads the LIVE value ->
    // continues smoothly from wherever the seat is, no snap, no seeding) but PREFIX a harmless
    // ever-increasing "<seq> 0 *" (pushes 0, left on the stack, ignored) so every tick's string is
    // distinct and each one fires -> continuous motion + sustained motor sound. (RampSliderTo never
    // hit this because it writes the changing absolute value, which is already unique each tick.)
    private readonly Dictionary<string, int> _seatMotorDir = new();     // posVar -> +1 (toward 100) / -1 (toward 0)
    private readonly Dictionary<string, double> _seatMotorPos = new();  // posVar -> approx tracked position (for the spoken read-out only)
    private System.Windows.Forms.Timer? _seatMotorTimer;
    private SimConnectManager? _seatMotorSim;
    private ScreenReaderAnnouncer? _seatMotorAnnouncer;
    private long _seatMotorSeq;
    private int _seatMotorTicks;
    private long _sdWriteSeq;   // makes the SD-page-index calc write unique each time (anti-dedup)

    private static readonly Dictionary<string, (string Disp, string Hi, string Lo)> _seatMotorMeta = new()
    {
        ["SEAT_CPT_MOVE_UP_DOWN"] = ("Captain seat up and down", "up", "down"),
        ["SEAT_CPT_MOVE_FWD_AFT"] = ("Captain seat forward and aft", "forward", "aft"),
        ["SEAT_FO_MOVE_UP_DOWN"] = ("First officer seat up and down", "up", "down"),
        ["SEAT_FO_MOVE_FWD_AFT"] = ("First officer seat forward and aft", "forward", "aft"),
    };

    // The 8 toggle buttons -> (position L:var, direction +1 toward 100 / -1 toward 0).
    private static readonly Dictionary<string, (string PosVar, int Dir)> _seatButtonMap = new()
    {
        ["SEATBTN_CPT_UP"]   = ("SEAT_CPT_MOVE_UP_DOWN", +1),
        ["SEATBTN_CPT_DOWN"] = ("SEAT_CPT_MOVE_UP_DOWN", -1),
        ["SEATBTN_CPT_FWD"]  = ("SEAT_CPT_MOVE_FWD_AFT", +1),
        ["SEATBTN_CPT_AFT"]  = ("SEAT_CPT_MOVE_FWD_AFT", -1),
        ["SEATBTN_FO_UP"]    = ("SEAT_FO_MOVE_UP_DOWN", +1),
        ["SEATBTN_FO_DOWN"]  = ("SEAT_FO_MOVE_UP_DOWN", -1),
        ["SEATBTN_FO_FWD"]   = ("SEAT_FO_MOVE_FWD_AFT", +1),
        ["SEATBTN_FO_AFT"]   = ("SEAT_FO_MOVE_FWD_AFT", -1),
    };

    // Toggle button: press a direction -> if already moving that way, STOP (+ speak position);
    // otherwise start moving that way (reversing if it was going the other way). No combo re-read,
    // so no start/stop/start and no spurious read-out.
    private void ToggleSeatMotor(string posVar, int dir, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _seatMotorSim = simConnect;
        _seatMotorAnnouncer = announcer;
        if (_seatMotorDir.TryGetValue(posVar, out var cur) && cur == dir)
        {
            // pressing the SAME direction again -> stop this axis + say where it ended up
            _seatMotorDir.Remove(posVar);
            AnnounceSeatPosition(posVar);
            simConnect.RequestVariable(posVar, forceUpdate: true);   // refresh cache for the read-out + next seed
            if (_seatMotorDir.Count == 0) _seatMotorTimer?.Stop();
            return;
        }
        // start (or reverse) -> seed the tracked position from the live var on a fresh start
        // (movement itself reads the live value each tick, so the seed only feeds the spoken read-out)
        if (!_seatMotorDir.ContainsKey(posVar))
            _seatMotorPos[posVar] = Math.Max(0.0, Math.Min(100.0,
                simConnect.GetCachedVariableValue(posVar) ?? (_seatMotorPos.TryGetValue(posVar, out var lp) ? lp : 50.0)));
        _seatMotorDir[posVar] = dir;
        _seatMotorTicks = 0;
        if (_seatMotorTimer == null)
        {
            _seatMotorTimer = new System.Windows.Forms.Timer { Interval = 20 };
            _seatMotorTimer.Tick += (s, e) => SeatMotorTick();
        }
        if (!_seatMotorTimer.Enabled) _seatMotorTimer.Start();
    }

    private void SeatMotorTick()
    {
        var sim = _seatMotorSim;
        if (sim == null || !sim.IsConnected || _seatMotorDir.Count == 0) { _seatMotorTimer?.Stop(); return; }
        const double step = 0.4;   // ~100 units in ~5 s
        bool hitSafety = ++_seatMotorTicks > 400;   // ~8 s cap so a forgotten "moving" combo can't drive forever
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var v in _seatMotorDir.Keys.ToList())
        {
            long seq = ++_seatMotorSeq;   // makes each tick's calc string unique so MobiFlight re-fires it
            string expr = _seatMotorDir[v] > 0
                ? seq + " 0 * (L:" + v + ") " + step.ToString(inv) + " + 100 min (>L:" + v + ")"
                : seq + " 0 * (L:" + v + ") " + step.ToString(inv) + " - 0 max (>L:" + v + ")";
            sim.ExecuteCalculatorCode(expr);
            double pos = Math.Max(0.0, Math.Min(100.0, (_seatMotorPos.TryGetValue(v, out var p) ? p : 50.0) + _seatMotorDir[v] * step));
            _seatMotorPos[v] = pos;
            if (pos <= 0.01 || pos >= 99.99 || hitSafety) { _seatMotorDir.Remove(v); AnnounceSeatPosition(v); }
        }
        if (_seatMotorDir.Count == 0) _seatMotorTimer?.Stop();
    }

    private void StopSeatMotor() { _seatMotorDir.Clear(); _seatMotorTimer?.Stop(); }

    // The user asked, after the seat stops, to hear WHERE it is as a position (not just a number).
    // Spoken band + percent, derived from the approximate tracked position. Queued (not Immediate)
    // so it follows NVDA's own "Stopped" combo read-out instead of cutting it off.
    private void AnnounceSeatPosition(string posVar)
    {
        var ann = _seatMotorAnnouncer;
        if (ann == null) return;
        double pos = _seatMotorPos.TryGetValue(posVar, out var p) ? p : 50.0;
        var (disp, hi, lo) = _seatMotorMeta.TryGetValue(posVar, out var m) ? m : ("Seat", "high", "low");
        ann.Announce($"{disp}: {SeatBand(pos, hi, lo)}, {(int)Math.Round(pos)} percent");
    }

    private static string SeatBand(double pos, string hi, string lo) =>
        pos >= 97 ? "fully " + hi :
        pos >= 70 ? hi :
        pos >= 56 ? "slightly " + hi :
        pos >= 44 ? "mid travel" :
        pos >= 30 ? "slightly " + lo :
        pos >= 3 ? lo :
        "fully " + lo;

    // The A380's NEW FCU ignores the legacy dotted "A32NX.FCU_SPD_MACH_TOGGLE_PUSH" H-event the
    // A320 used — live-verified that firing it (either dot or underscore form) does NOTHING. Its
    // SpeedManager (FCU/Managers/SpeedManager.ts) switches SPD<->MACH via the STOCK K-events
    // AP_MANAGED_SPEED_IN_MACH_ON / _OFF. This one conditional RPN reads the current mode (the
    // stock SimVar AUTOPILOT MANAGED SPEED IN MACH) and fires the opposite — verified live to flip
    // the mode. (Used by both the FCU Speed window MACH button and the panel Speed/Mach toggle.)
    private const string SpdMachToggleRpn =
        "(A:AUTOPILOT MANAGED SPEED IN MACH, Bool) if{ 1 (>K:AP_MANAGED_SPEED_IN_MACH_OFF) } els{ 1 (>K:AP_MANAGED_SPEED_IN_MACH_ON) }";

    // Request the live AP/mode state vars so a window can refresh its button labels.
    public void RequestAutopilotStates(SimConnectManager s)
    {
        if (!s.IsConnected) return;
        foreach (var v in new[] {
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE",
            "A32NX_FCU_LOC_MODE_ACTIVE", "A32NX_FCU_APPR_MODE_ACTIVE",
            "A32NX_FMA_EXPEDITE_MODE", "A32NX_FCU_EFIS_L_FD_ACTIVE",
            "A32NX_FCU_EFIS_R_FD_ACTIVE" })
            s.RequestVariable(v, forceUpdate: true);
    }

    // Toggle the FCU metric-altitude pushbutton (cockpit does !L then write-back).
    public void ToggleMetricAltitude(SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) return;
        s.ExecuteCalculatorCode($"{(_metricAlt ? 0 : 1)} (>L:A32NX_METRIC_ALT_TOGGLE)");
    }

    // Set the FCU altitude increment (100 or 1000 ft).
    public void SetAltIncrement(int inc, SimConnectManager s)
    {
        if (!s.IsConnected) return;
        s.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", (uint)inc);
    }

    // Apply a settable UI variable through the A380's existing HandleUIVariableSet
    // routing, looking up its registered definition (so callers without a panel
    // varDef can reuse the proven set paths). Used by the FCU Baro window for the
    // CAPT_QNH_SET / *_EIS_BARO_IS_STD / XMLVAR_Baro_Selector routes.
    public bool ApplyUIVariable(string varKey, double value, SimConnectManager s, ScreenReaderAnnouncer a)
    {
        SimVarDefinition def = (_varCache != null && _varCache.TryGetValue(varKey, out var d))
            ? d : new SimVarDefinition { Name = varKey, DisplayName = varKey };
        return HandleUIVariableSet(varKey, value, def, s, a);
    }
}
