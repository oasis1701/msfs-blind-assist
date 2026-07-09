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
public partial class FlyByWireA380Definition : BaseAircraftDefinition,
    ISupportsBridgedMCDU,
    ISupportsBridgedEFB
{
    public override string AircraftName => "FlyByWire A380X";
    public override string AircraftCode => "FBW_A380";

    // Taxi-turn rollout-anticipation lead (see IAircraftDefinition.TaxiTurnLeadSeconds).
    // The PR-85 (A380) and PR-87 (taxi rollout anticipation) branches merged
    // independently, so the A380 silently inherited the 1.2 s base default. MEASURED
    // 2026-06-11 flying the A380 on the A320's 1.3 s lead: five rollouts ran 10-42 deg
    // LONG at 15-19 kt (the pilot could only cope by slowing below 13 kt) -- the heaviest
    // yaw inertia in the fleet needs the most lead. 1.8 s; re-measure from telemetry.
    public override double TaxiTurnLeadSeconds => 1.8;

    // A380 FCU uses the same direct-set dialog pattern as the A320.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // ===================================================================
    // Variables
    // ===================================================================
    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();

        // ---- local builders ------------------------------------------
        // Writable L:var rendered as a combo box of named positions.
        void Sel(string key, string display, Dictionary<double, string> vd,
                 bool reverse = false)
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
        void OnOff(string key, string display) =>
            Sel(key, display, new Dictionary<double, string> { [0] = "Off", [1] = "On" });
        // Bool L:var: Off / Auto (FBW "_IS_AUTO" pushbuttons).
        void OffAuto(string key, string display) =>
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
                RenderAsButton = true,
                SuppressRestingButtonState = true
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
                // The ECAM-CP keys (CLR/RCL/STS/ALL/…) are real push-BUTTONS (user request
                // 2026-06). The click routes through _momentaryButtons → pulse 1→0 + speak
                // "<name> pressed". MSFSBA's own ECL/SD code pulses these via the calculator
                // path directly (not HandleUIVariableSet), so those internal pulses stay silent.
                RenderAsButton = true,
                SuppressRestingButtonState = true
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
        // Emergency-exit sign switch: same 0=On/1=mid/2=Off mapping, but the middle
        // position is ARM (auto-on if power lost), not "Auto" (FBW A380_COCKPIT.xml
        // ANIMTIP_1 "Set emergency exit signs to ARM").
        var emerExitSw = new Dictionary<double, string> { [0] = "On", [1] = "Arm", [2] = "Off" };
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
        OnOff("A32NX_OVHD_APU_START_PB_IS_ON", "APU Start");
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
        // Avionics ventilation BLOWER pushbutton (completeness pass — live-verified settable).
        OnOff("A32NX_OVHD_VENT_BLOWER_PB_IS_ON", "Avionics Blower");

        // ---- ANTI-ICE ----
        OnOff("A32NX_MAN_PITOT_HEAT", "Probe / Window Heat");
        // Wing anti-ice (CORRECTED 2026-07 — back the combo on the var the REAL cockpit
        // button writes). The A380 overhead WING anti-ice PB (FBW_Airbus_AntiIce_Wing
        // template) toggles A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION; the PB light reads
        // A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED. The previous combo drove the stock
        // STRUCTURAL DEICE SWITCH, which the cockpit button does NOT touch — so MSFSBA's
        // combo diverged from the actual switch. Backing it on the button-position L:var
        // (live-verified settable + held) keeps the two in sync and drives the real input.
        // ⚠️ FBW-BUILD LIMITATION: the A380 wing anti-ice PNEUMATIC is not modelled yet —
        // live-verified that setting the button (or SELECTED, or the stock switch, or the
        // PB_IS_ON) at cruise with TAT -15 C leaves _SYSTEM_ON at 0 (no valve/flow), and
        // the a380_systems Rust has no wing-anti-ice pneumatic. So the SWITCH is faithful
        // and future-proof, but the flow won't engage until FBW implements it — the
        // read-only "Wing Anti-Ice Flowing" status (_SYSTEM_ON) correctly reports that.
        vars["WING_ANTI_ICE_OVHD"] = new SimVarDefinition
        {
            Name = "A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", DisplayName = "Wing Anti-Ice",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = onOff
        };
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
        // Each fire pushbutton sits under a GUARD (a hinged clear cover) that must be
        // lifted to reach the button. The guard position is a real cockpit switch
        // (A380X_OVHD_{ENGn,APU}_FIRE_GUARD, 0=Closed/1=Open — live-verified settable +
        // held), exposed for completeness (no-omissions pass).
        var fireGuardSw = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" };
        for (int n = 1; n <= 4; n++)
        {
            Sel($"A380X_OVHD_ENG{n}_FIRE_GUARD", $"Engine {n} Fire Button Guard", fireGuardSw);
            Press($"A32NX_FIRE_BUTTON_ENG{n}", $"Engine {n} Fire Button");
            Mon($"A32NX_FIRE_DETECTED_ENG{n}", $"Engine {n} Fire",
                new Dictionary<double, string> { [0] = "Normal", [1] = "FIRE" });
        }
        Sel("A380X_OVHD_APU_FIRE_GUARD", "APU Fire Button Guard", fireGuardSw);
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
        // Cargo smoke fire-agent discharge locks (completeness pass — live-verified settable).
        // Guarded discharge controls for the fwd/aft cargo fire bottles. Off/On combos.
        Sel("A32NX_CARGOSMOKE_DISCH1LOCK_TOGGLE", "Cargo Smoke Discharge 1",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Discharge" });
        Sel("A32NX_CARGOSMOKE_DISCH2LOCK_TOGGLE", "Cargo Smoke Discharge 2",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Discharge" });
        // Fire Test + Cargo Smoke Detection Test are HOLD on/off tests — they STAY combos
        // (Off/On): the user picks On (test runs, the EWD speaks the result), then Off, and
        // the combo always shows the current state. Only true one-shot momentary actions are
        // buttons; a toggle belongs in a combo (the value is visible + always correct).
        OnOff("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", "Fire Test");
        OnOff("A32NX_FIRE_TEST_CARGO", "Cargo Smoke Detection Test");

        // ---- OXYGEN ----
        // Ascending order (value 0 at top) to mirror the cockpit, consistent with the
        // signs above. (Value 0 = the armed/on position; FBW preset calls it "Crew Oxy On".)
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
        // ---- MSFS-2024-native-rebuild additions (interactive-parts.xml, #10758). Same
        // boolean-toggle family as the table/footrest/oxygen above ((L:VAR) ! (>L:VAR)),
        // so they route through the prefix-less calc-path allowlist in HandleUIVariableSet.
        // The model GATES meal table behind the table being deployed and the keyboard behind
        // the meal table, but the L:var itself is directly settable — the pilot just opens
        // table -> meal table -> keyboard in order. Stow polarity (0/1) is unverified visually;
        // flip the value dict if a control reads inverted in the sim.
        Sel("A380_CPT_MEALTABLE", "Captain Meal Table", stowedDeployed);
        Sel("A380_FO_MEALTABLE", "First Officer Meal Table", stowedDeployed);
        Sel("A380_CPT_KEYBOARD", "Captain Keyboard Tray", stowedDeployed);
        Sel("A380_FO_KEYBOARD", "First Officer Keyboard Tray", stowedDeployed);
        Sel("CAS_LH_OPENING", "Left CAS Panel", closedOpenC);
        Sel("CAS_RH_OPENING", "Right CAS Panel", closedOpenC);
        Sel("AFT_OIT_OPENING", "OIT Panel", closedOpenC);
        Sel("COCKPITDOOR_OPEN", "Cockpit Door (open/close)", closedOpenC);
        var deployedStowed = new Dictionary<double, string> { [0] = "Deployed", [1] = "Stowed" };
        Sel("BIGARMREST_CPT_STOW", "Captain Armrest Stow", deployedStowed);
        Sel("BIGARMREST_FO_STOW", "First Officer Armrest Stow", deployedStowed);
        Sel("SMALLARMREST_CPT_STOW", "Captain Small Armrest Stow", deployedStowed);
        Sel("SMALLARMREST_FO_STOW", "First Officer Small Armrest Stow", deployedStowed);
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
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, RenderAsButton = true,
            SuppressRestingButtonState = true
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
        // "Signal Cabin Ready" — discoverable action button. A32NX_CABIN_READY is a
        // READ-ONLY FWS status (a direct var write is IGNORED by the sim — live-verified:
        // wrote 0, it stayed 1). The real cockpit trigger is a CALLS pushbutton: FwsCore
        // sets CABIN_READY=1 whenever CALLS ALL/FWD/AFT is pressed. So this button pulses
        // PUSH_OVHD_CALLS_ALL via a dedicated HandleUIVariableSet branch (NOT added to
        // _momentaryButtons — that generic path would pulse the synthetic key itself).
        // It replaces the old dead "Cabin Ready" combo that used to sit in p["Calls"]
        // (a settable-looking box whose write did nothing). The read-only status stays in
        // d["Cockpit"] + the A32NX_CABIN_READY Mon auto-announce.
        vars["A380X_MSFSBA_SIGNAL_CABIN_READY"] = new SimVarDefinition
        {
            Name = "A380X_MSFSBA_SIGNAL_CABIN_READY", DisplayName = "Signal Cabin Ready",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" },
            RenderAsButton = true, SuppressRestingButtonState = true
        };

        // ---- SIGNS ----
        // Seat-belt sign: the REAL state is the stock simvar CABIN SEATBELTS ALERT
        // SEAT BELTS is a real 3-POSITION switch — ON / AUTO / OFF (like No Smoking
        // below), NOT On/Off. Position 1 = AUTO: the FBW model auto-drives the sign
        // from engines-running + slats/gear-down (behaviour XML 500 ms Update). The
        // switch position lives in XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position
        // (0=On/1=Auto/2=Off), live-verified settable + HELD (an old note claimed it
        // "reverts" — that was a mis-test; it holds like the No Smoking XMLVAR). The
        // set (HandleUIVariableSet) writes the position AND, for On/Off, drives the
        // stock CABIN SEATBELTS ALERT SWITCH (the model's one-shot manual sync doesn't
        // re-run on an external position write); AUTO leaves the sign to the model.
        vars["SEATBELT_SIGN"] = new SimVarDefinition
        {
            Name = "XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position", DisplayName = "Seat Belts",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = signSw
        };
        // Separate READ-ONLY status for the actual sign illumination (the stock bool),
        // so a blind pilot hears the sign come on/off — including when AUTO illuminates
        // it automatically at descent (which the position combo, unchanged at "Auto",
        // wouldn't announce). Not a control; not paneled.
        vars["SEATBELT_SIGN_LIGHT"] = new SimVarDefinition
        {
            Name = "CABIN SEATBELTS ALERT SWITCH", DisplayName = "Seat Belts Sign",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };
        // Order MIRRORS the real cockpit switch (user-confirmed): On (top) -> Auto/Arm ->
        // Off (bottom), ascending by the FBW value (On=0/mid=1/Off=2) — you flip the
        // physical overhead switch UP for On, so up-arrow from the resting position goes
        // toward On/Auto, matching the cockpit.
        Sel("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", "No Smoking", signSw);
        Sel("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", "Emergency Exit Lights", emerExitSw);

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
        OnOff("A32NX_AVIONICS_COMPLT_ON", "Avionics Compartment Light");
        // Storm Light — a real 2-position ON/OFF switch (correct shape + polarity:
        // A380X_OVHD_STORM_LT 0=Off/1=On, live-verified settable + held). NOTE: the
        // FBW A380 build models it as a DUMMY switch (SWITCH_OVHD_INTLT_STORM =
        // A32NX_GT_Switch_Dummy, empty CODE_POS, "TODO … Requires lighting logic to
        // be added"), so the position holds but drives NO light yet — toggling it has
        // no visible effect until FBW implements the storm-light lighting. Not an
        // MSFSBA bug: the switch is faithfully represented; the effect is FBW's TODO.
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
        // ---- No-omissions completeness pass (2026-07, user request): every remaining
        // pilot-operable A380 cockpit switch. All live-verified settable + held via the
        // calculator path (the generic SetLVar / OVHD catch-all drives them).
        OnOff("A380X_SWITCH_LAPTOP_POWER_LEFT", "Laptop Power (Capt)");
        OnOff("A380X_SWITCH_LAPTOP_POWER_RIGHT", "Laptop Power (F/O)");
        Sel("A32NX_SWITCH_DOORPANEL_LOCK", "Door Panel Lock",
            new Dictionary<double, string> { [0] = "Unlocked", [1] = "Locked" });
        // ELT + Data Loading System are modelled but INOP on the FBW build — the switch
        // holds its position (live-verified) but there is no working system behind it.
        OnOff("A32NX_ELT_ON", "Emergency Locator Transmitter (inop)");
        OnOff("A32NX_DLS_ON", "Data Loading System (inop)");
        // Rain repellent (Capt/F/O) — momentary hold buttons; INOP on this build.
        Btn("A32NX_RAIN_REPELLENT_LEFT_ON", "Rain Repellent (Capt) (inop)");
        Btn("A32NX_RAIN_REPELLENT_RIGHT_ON", "Rain Repellent (F/O) (inop)");
        // Visual model toggles — hide/show cockpit objects. No system effect (purely
        // cosmetic), but they are real cockpit-model switches, so exposed for
        // completeness. Live-verified settable + held.
        var shownHidden = new Dictionary<double, string> { [0] = "Shown", [1] = "Hidden" };
        Sel("A380X_CABIN_HIDDEN", "Cabin Model", shownHidden);
        Sel("A380X_CPT_SIDESTICK_HIDDEN", "Captain Sidestick Model", shownHidden);
        Sel("A380X_FO_SIDESTICK_HIDDEN", "First Officer Sidestick Model", shownHidden);
        Sel("A380X_CPT_EFB_HIDDEN", "Captain EFB Model", shownHidden);
        Sel("A380X_FO_EFB_HIDDEN", "First Officer EFB Model", shownHidden);
        // Chronometer start/stop + reset (the glareshield CHRONO push). #107 gap.
        // Momentary actions driven by H-EVENTS (the FBW Clock subscribes to the
        // hEvent, NOT the L:var — writing the L:var does nothing). Rendered as push-
        // BUTTONS (RenderAsButton=true); the dedicated HandleUIVariableSet branch fires
        // (>H:VAR) on press (checked before the generic _momentaryButtons pulse path, so
        // the H-event mechanism runs instead of a no-op L:var pulse).
        vars["A32NX_CHRONO_TOGGLE"] = new SimVarDefinition
        { Name = "A32NX_CHRONO_TOGGLE", DisplayName = "Chronometer Start / Stop", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }, RenderAsButton = true, SuppressRestingButtonState = true };
        vars["A32NX_CHRONO_RST"] = new SimVarDefinition
        { Name = "A32NX_CHRONO_RST", DisplayName = "Chronometer Reset", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false, ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }, RenderAsButton = true, SuppressRestingButtonState = true };

        // ---- AUDIO CONTROL PANEL (ACP) — receive selectors, RMP 1 ----
        // Which sources the captain hears (#107 transcript: "ensure VHF1 + cabin
        // interphone selected"). Settable L:vars via the calculator catch-all.
        // VHF receive on/off + the VHF receive VOLUME levels (0-100). These are the
        // ONLY real ACP audio controls — the FBW A380 RMP dev build models VHF +
        // transponder only (RmpPageStack routes VhfPage/SquawkPage), so the
        // HF/TEL/CAB/INT/PA/NAV receive switches, volumes and transmit selects were
        // DEAD L:vars (write holds, drives nothing — confirmed absent from the
        // MobiFlight registered-var catalog; only A380X_RMP_{1,2}_VHF_* +
        // A380X_RMP_3_PA exist). They were pruned 2026-06-13 because exposing radio
        // controls that do nothing is worse than omitting them. The VHF channels (3
        // radios), the screen brightness, and the VHF transmit select are kept.
        OnOff("A380X_RMP_1_VHF_VOL_RX_SWITCH_1", "VHF 1 Receive");
        OnOff("A380X_RMP_1_VHF_VOL_RX_SWITCH_2", "VHF 2 Receive");
        OnOff("A380X_RMP_1_VHF_VOL_RX_SWITCH_3", "VHF 3 Receive");
        Slider("A380X_RMP_1_VHF_VOL_1", "VHF 1 Volume");
        Slider("A380X_RMP_1_VHF_VOL_2", "VHF 2 Volume");
        Slider("A380X_RMP_1_VHF_VOL_3", "VHF 3 Volume");
        Slider("A380X_RMP_1_BRIGHTNESS_KNOB", "RMP Screen Brightness");
        OnOff("A380X_RMP_1_VHF_TX_1", "VHF 1 Transmit");
        OnOff("A380X_RMP_1_VHF_TX_2", "VHF 2 Transmit");
        OnOff("A380X_RMP_1_VHF_TX_3", "VHF 3 Transmit");

        // ---- AUDIO CONTROL PANEL — First Officer (RMP 2), captain/F-O split ----
        // VHF only (see the captain note above — non-VHF channels are unmodelled/dead).
        OnOff("A380X_RMP_2_VHF_VOL_RX_SWITCH_1", "VHF 1 Receive");
        OnOff("A380X_RMP_2_VHF_VOL_RX_SWITCH_2", "VHF 2 Receive");
        OnOff("A380X_RMP_2_VHF_VOL_RX_SWITCH_3", "VHF 3 Receive");
        Slider("A380X_RMP_2_VHF_VOL_1", "VHF 1 Volume");
        Slider("A380X_RMP_2_VHF_VOL_2", "VHF 2 Volume");
        Slider("A380X_RMP_2_VHF_VOL_3", "VHF 3 Volume");
        Slider("A380X_RMP_2_BRIGHTNESS_KNOB", "RMP Screen Brightness");
        OnOff("A380X_RMP_2_VHF_TX_1", "VHF 1 Transmit");
        OnOff("A380X_RMP_2_VHF_TX_2", "VHF 2 Transmit");
        OnOff("A380X_RMP_2_VHF_TX_3", "VHF 3 Transmit");

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
        // Wing LANDING lights (LDG LT L/R) are the stock LIGHT LANDING:2 (systems.cfg
        // Type:5#Index:2 = LIGHT_ASOBO_LAND_1/2 LH+RH), driven by indexed
        // `2 <value> (>K:2:LANDING_LIGHTS_SET)` (live-verified). The bare LIGHT LANDING
        // (index 0/1) hit the NOSE takeoff light, not the wing landing lights.
        Light("LIGHT_LANDING", "LIGHT LANDING:2", "Landing Lights");
        // NOSE light — a real 3-POSITION selector (T.O. / Taxi / Off), so it is exposed
        // as a 3-position combo exactly like the cockpit switch (never split into
        // separate On/Off controls). State = the FBW switch-position L:var
        // LIGHTING_LANDING_1 (0=T.O., 1=Taxi, 2=Off — live-verified readable). Writing
        // that L:var is DEAD on the shipping model (it HOLDS but drives no light —
        // live-verified: `1 (>L:LIGHTING_LANDING_1)` left LIGHT TAXI:1 = 0), so
        // HandleUIVariableSet writes it (state mirror) AND fires the working indexed
        // stock events per position (nose takeoff = LIGHT LANDING:1, nose taxi =
        // LIGHT TAXI:1; the separate RWY TURN OFF switch is LIGHT TAXI:2/3 below):
        //   T.O. → LANDING:1 on + TAXI:1 on ("allow TAXI LT with TO LT", per the
        //          SWITCH_OVHD_EXTLT_NOSE behaviour template) · Taxi → LANDING:1 off,
        //          TAXI:1 on · Off → both off.
        vars["NOSE_LIGHT"] = new SimVarDefinition
        {
            Name = "LIGHTING_LANDING_1",
            DisplayName = "Nose Light",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "T.O.", [1] = "Taxi", [2] = "Off" }
        };
        // Runway Turnoff lights: On / Off. The A380 RWY TURN OFF switch is
        // OnOff_TwoSimvars over SIMVAR_INDEX 2 + 3 (A380_Cockpit_Behavior.xml, the
        // SWITCH_OVHD_EXTLT_RWY template) = the stock LIGHT TAXI:2 (turnoff LEFT,
        // LIGHT_ASOBO_TURNOFF_LH) + LIGHT TAXI:3 (turnoff RIGHT). The FBW
        // A380X_OVHD_EXTLT_RWY_TURNOFF L:var is DEAD; the working actuator is the
        // indexed `<index> <value> (>K:2:TAXI_LIGHTS_SET)` for BOTH indices —
        // live-verified 2026-07 (`2 1 …SET` → LIGHT TAXI:2 = 1, `3 1 …SET` → :3 = 1).
        // State mirrors LIGHT TAXI:2 (both indices move together with the switch).
        vars["LIGHT_RWY_TURNOFF"] = new SimVarDefinition
        {
            Name = "LIGHT TAXI:2",
            DisplayName = "Runway Turnoff Lights",
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
        Mon("A32NX_AUTOBRAKES_ACTIVE", "Autobrake",
            new Dictionary<double, string> { [0] = "not braking", [1] = "braking" });
        Mon("A32NX_ROW_ROP_LOST", "Runway Overrun Protection",
            new Dictionary<double, string> { [0] = "available", [1] = "lost" });
        Mon("A32NX_BTV_APPR_DIFFERENT_RUNWAY", "BTV Runway Check",
            new Dictionary<double, string> { [0] = "matches approach", [1] = "DIFFERENT RUNWAY - BTV armed for another runway" });
        Mon("A32NX_TCAS_STATE", "TCAS advisory", new Dictionary<double, string>
            { [0] = "clear of conflict", [1] = "traffic advisory", [2] = "resolution advisory" });
        for (int n = 1; n <= 16; n++) Read($"A32NX_BRAKE_TEMPERATURE_{n}", $"Brake {n} Temperature", "celsius");

        // ---- Source switching / ISIS ----
        Sel("A32NX_ATT_HDG_SWITCHING_KNOB", "Attitude / Heading Source", srcSw);
        Sel("A32NX_AIR_DATA_SWITCHING_KNOB", "Air Data Source", srcSw);
        Sel("A32NX_EIS_DMC_SWITCHING_KNOB", "EIS / DMC Source", srcSw);
        // Magnetic vs True heading reference (the ND/FMS TRUE REF pushbutton). The
        // pilot must confirm MAG unless TRUE is required. (#107 transcript gap.)
        // CORRECTED 2026-06: the control + state var the A380 instruments (PFD/ND/FCU)
        // actually read is A32NX_PUSH_TRUE_REF, NOT A32NX_FMGC_TRUE_REF (what MSFSBA used
        // before). Live-verified: writing FMGC_TRUE_REF=1 left PUSH_TRUE_REF (the consumed
        // var) at 0 and changed nothing; writing PUSH_TRUE_REF=1 latched and is what the
        // displays read. FMGC_TRUE_REF is an FMGC-internal output, not the pilot control.
        Sel("A32NX_PUSH_TRUE_REF", "Heading Reference", new Dictionary<double, string> { [0] = "Magnetic", [1] = "True" });
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
        // PFD read-out additions (source-confirmed missing). V1/VR/V2 come from the FBW
        // FMS as L:vars (the MFD PERF page writes L:AIRLINER_V1/VR/V2_SPEED in knots). The
        // stock "AIRLINER V1 SPEED" simvars are NEVER written by the A380 -> always 0
        // ("not set"). Read the L:vars instead; the keys stay PFD_V* so the panel set +
        // TryGetDisplayOverride ("not set" on 0) are unchanged.
        //   These three are Continuous + IsAnnounced (knots) so MSFSBA AUTO-ANNOUNCES the
        // value the instant the pilot enters/changes it on the MFD PERF page — "V1: 125
        // knots" — mirroring the Fenix MCDU V-speed entry confirmation. FormatVariableValue
        // appends "knots" from Units; the simVarMonitor baseline + connect-grace keep the
        // initial values silent. Listed in the Ctrl+M monitor for opt-out like every other
        // announced var. (Was OnRequest/display-only before the Fenix-parity pass.)
        vars["PFD_V1"] = new SimVarDefinition { Name = "AIRLINER_V1_SPEED", DisplayName = "V1", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, Units = "knots" };
        vars["PFD_VR"] = new SimVarDefinition { Name = "AIRLINER_VR_SPEED", DisplayName = "VR", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, Units = "knots" };
        vars["PFD_V2"] = new SimVarDefinition { Name = "AIRLINER_V2_SPEED", DisplayName = "V2", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, Units = "knots" };
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
        ReadEnum("A32NX_PUSH_TRUE_REF", "Heading reference",
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
        // TCAS resolution-advisory vertical-speed band (green = fly toward, red = avoid).
        // FBW writes ONLY the :1/:2 INDEXED L:vars (LegacyTcasComputer.ts:1281-1285 —
        // min/max of each band); the unindexed names are never written, so the old
        // unindexed reads could never see an RA. Continuous (batch path — the proven
        // transport for colon-indexed L:vars, same as A32NX_AUTOTHRUST_TLA:n); cached
        // silently in ProcessSimVarUpdate and spoken as composed RA guidance.
        MonNum("A32NX_TCAS_VSPEED_GREEN:1", "TCAS target vertical speed minimum", "feet per minute");
        MonNum("A32NX_TCAS_VSPEED_GREEN:2", "TCAS target vertical speed maximum", "feet per minute");
        MonNum("A32NX_TCAS_VSPEED_RED:1", "TCAS avoid vertical speed minimum", "feet per minute");
        MonNum("A32NX_TCAS_VSPEED_RED:2", "TCAS avoid vertical speed maximum", "feet per minute");
        // RA detail (plain unindexed L:vars): corrective flag, up/down advisory status
        // (0 none, 1 climb/descend, 2 don't, 3/4/5 don't >500/1000/2000 fpm) and the
        // rate to maintain — together these compose the spoken "what to fly" guidance
        // (see Services.TcasRaGuidance). All reset by FBW on clear of conflict.
        MonNum("A32NX_TCAS_RA_CORRECTIVE", "TCAS RA corrective");
        MonNum("A32NX_TCAS_RA_UP_ADVISORY_STATUS", "TCAS RA up advisory");
        MonNum("A32NX_TCAS_RA_DOWN_ADVISORY_STATUS", "TCAS RA down advisory");
        MonNum("A32NX_TCAS_RA_RATE_TO_MAINTAIN", "TCAS RA rate to maintain", "feet per minute");
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
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true
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
            Units = "kilograms", UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ExcludeFromMonitorManager = true
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

        var windowReadVars = Forms.FBWA380.EisDisplayVars.All();
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
        // unregistered var). Classify by PREFIX, not by space/colon (matching the
        // EisDisplayVars loop above): a stock SimVar never carries an FBW prefix, so
        // "INTERACTIVE POINT OPEN:0" / "LIGHT TAXI:2" register as SimVar (forcing a
        // stock name through the L:var path corrupts registration + breaks detection).
        // But a colon-INDEXED FBW L:var — "A32NX_FUEL_USED:1", "A32NX_AUTOTHRUST_TLA:1"
        // — is a real L:var: the old "any colon = SimVar" rule read it as a nonexistent
        // stock SimVar (0), which is exactly why the SD Fuel/Cruise pages showed
        // Engine 1-4 fuel used with NO value (live: the L:var reads ~38,700 kg, the
        // stock read 0). Prefix classification keeps stock names on the SimVar path
        // AND lets indexed FBW L:vars read correctly.
        for (int sdPage = 0; sdPage <= 13; sdPage++)
            foreach (var (_, rowVar, _) in A380SdRows(sdPage))
                if (!vars.ContainsKey(rowVar))
                {
                    bool isStock = !rowVar.StartsWith("A32NX_", StringComparison.Ordinal)
                                   && !rowVar.StartsWith("A380X_", StringComparison.Ordinal)
                                   && !rowVar.StartsWith("FBW_", StringComparison.Ordinal);
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
                // C/B (12), Status (14) and Video (15) stay OUT of the picker. The page index now
                // STICKS (the old snap-back is gone — re-checked live 2026-06-13), BUT there is
                // nothing behind it: FBW's CbPage.tsx is a 7-line STUB that renders only the "C/B"
                // title (no circuit-breaker content), Status is FBW-WIP (empty), and Video is the
                // cockpit-door CAMERA feed (an image). Selecting any would show only the SD's
                // permanent status bar (TAT/SAT/GW/FOB/time) — misleading, so they're excluded.
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
        Read("A32NX_FMS_PAX_NUMBER", "Passengers on Board");
        // Per-station seat bitmasks → boarded total (see PaxStationVars). Continuous +
        // IsAnnounced so they ride the continuous batch (live-cached every second, so the
        // running total is always current when the Status panel is viewed — no timing race),
        // but handled in ProcessSimVarUpdate (returns true → never spoken) and
        // ExcludeFromMonitorManager so they don't clutter the Ctrl+M list.
        foreach (var pk in PaxStationVars)
            vars[pk] = new SimVarDefinition
            {
                Name = pk, DisplayName = pk, Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ExcludeFromMonitorManager = true, Units = "number"
            };
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
        // APU fuel used is an ARINC429 word (FBW SD useArinc429Var) — the old plain Read
        // showed the raw ~13.9-billion SSM word. Decode it to kg.
        vars["A32NX_APU_FUEL_USED"] = new SimVarDefinition
        {
            Name = "A32NX_APU_FUEL_USED", DisplayName = "APU Fuel Used",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "kg", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        };

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
                ReadEnum($"A32NX_FIRE_SQUIB_{b}_ENG_{n}_IS_ARMED", $"Engine {n} Agent {b} Squib", armedVd);
                ReadEnum($"A32NX_FIRE_SQUIB_{b}_ENG_{n}_IS_DISCHARGED", $"Engine {n} Agent {b} Discharged", dischVd);
            }
        }
        ReadEnum("A32NX_FIRE_SQUIB_1_APU_1_IS_DISCHARGED", "APU Agent Discharged", dischVd);

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
        // PFD messages + armed modes + approach settings (for the PFD window;
        // ported from the A320, shared A32NX_ names). The 3 PFD messages are
        // announced live (meaningful callouts); the rest are window-only readouts.
        Mon("A32NX_PFD_MSG_SET_HOLD_SPEED", "Set Hold Speed", onOff);
        // Speak the real PFD message text ("T/D REACHED") on reaching the top of
        // descent, matching the A32NX, instead of the generic on/off wording.
        Mon("A32NX_PFD_MSG_TD_REACHED", "Top of Descent Reached", new Dictionary<double, string> { [0] = "Not shown", [1] = "T/D REACHED" });
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
        // Flight Director 1 / 2 (CORRECTED 2026-06). The A32NX_FCU_EFIS_L/R_FD_ACTIVE
        // L:vars are DEAD on the A380X — live-verified the FD stayed ON with the lvar at
        // 0 (fully decoupled), and writing the lvar actuated nothing (the earlier
        // "sticks via the calculator path" note was the stickiness trap — it holds but
        // drives nothing). The real control is the cockpit FD button, which fires
        // K:TOGGLE_FLIGHT_DIRECTOR with the SIDE as the PARAMETER (1 = Capt, 2 = F/O) —
        // live-verified: param 1 flips FD1, param 2 flips FD2, per-side. (The earlier
        // attempt used param 0, which only ever turned the FD off and never on.) Back
        // the combos on the stock AUTOPILOT FLIGHT DIRECTOR ACTIVE:n and toggle-if-differs
        // via HandleUIVariableSet — same pattern as ENG GEN / taxi light.
        vars["FD_1_CTL"] = new SimVarDefinition
        {
            Name = "AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", DisplayName = "Flight Director 1",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = onOff
        };
        vars["FD_2_CTL"] = new SimVarDefinition
        {
            Name = "AUTOPILOT FLIGHT DIRECTOR ACTIVE:2", DisplayName = "Flight Director 2",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = onOff
        };
        // Monitored (so ProcessSimVarUpdate sees changes) + Ctrl+M-muteable; the raw
        // generic announce is suppressed by the decoded handler returning true.
        Mon("A32NX_FMA_VERTICAL_ARMED", "Armed Vertical Modes", new Dictionary<double, string>());
        Mon("A32NX_FMA_LATERAL_ARMED", "Armed Lateral Modes", new Dictionary<double, string>());
        Read("A32NX_FMA_CRUISE_ALT_MODE", "Cruise Altitude Mode");
        Read("A32NX_PFD_LINEAR_DEVIATION_ACTIVE", "Vertical Deviation");
        // FMS vertical-profile target altitude at the current position — the basis for
        // the PFD linear (V/DEV) deviation: deviation = current altitude − this, shown
        // only while LINEAR_DEVIATION_ACTIVE (managed climb/descent / FINAL APP).
        Read("A32NX_PFD_TARGET_ALTITUDE", "Vertical Profile Target Altitude", "feet");
        // FBW exposes the lateral-deviation request as _L_/_R_ (per FMGC), NOT _1_ — the _1_
        // name does not exist in source and always read 0. Use the captain's (_L_).
        Read("A32NX_FMGC_L_LDEV_REQUEST", "FMGC L DEV Request");
        // (Approach minimums are registered once in the MINIMUMS section, off the plain
        //  AIRLINER_MINIMUM_DESCENT_ALTITUDE / AIRLINER_DECISION_HEIGHT feet L:vars.)
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
        // FD control is the FD_1_CTL / FD_2_CTL combos registered above: per-side stock
        // AUTOPILOT FLIGHT DIRECTOR ACTIVE:n state, actuated by K:TOGGLE_FLIGHT_DIRECTOR
        // with the SIDE as the parameter (1 = Capt, 2 = F/O). The A32NX_FCU_EFIS_L/R_FD_ACTIVE
        // L:vars are DEAD on the A380X (read 0 while the FD is on; a calc-path write holds but
        // drives nothing — the stickiness trap), so DO NOT switch the combos back to them.
        // FD_ACTIVE below is the stock COMBINED FD state, a read-only status readout only.
        Stock("FD_ACTIVE", "AUTOPILOT FLIGHT DIRECTOR ACTIVE", "Flight Director", "bool", onOff);
        // (Legacy XMLVAR_BaroN_Mode combos are DEAD on the A380X — verified live
        //  that writing them changes nothing. STD/QNH is the IS_STD combo below.)
        // Auto-announced live as the pilot turns the baro knob (the EFIS baro
        // setting is non-visual; spoken on change, deduped to whole hPa).
        MonNum("A32NX_FCU_LEFT_EIS_BARO_HPA", "Captain Altimeter", "hectopascals");
        MonNum("A32NX_FCU_RIGHT_EIS_BARO_HPA", "First Officer Altimeter", "hectopascals");
        // STD(PUSH)/QNH(PULL) per side — note the A380's knob events are the OPPOSITE
        // of the A32NX's (live-verified 2026-06-11 in the installed fcu.js: onPush →
        // Std, onPull → leave Std). Dev FBW removed the *_EIS_BARO_IS_STD L:vars as
        // inputs; the FCU writes the stock KOHLSMAN SETTING STD:n simvar on every
        // mode TRANSITION (MsfsBaroManager.setupSyncToMsfs), which is the readback
        // here. That write is transition-only, so a session that starts with STD
        // already engaged can read stale 0 — the MB watchdog below back-fills it.
        // Keys keep the old names so the panel lists, window, hotkey readout and
        // announce branch stay stable.
        var baroStd = new Dictionary<double, string> { [0] = "QNH", [1] = "Standard" };
        vars["A32NX_FCU_LEFT_EIS_BARO_IS_STD"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING STD:1", DisplayName = "Capt Altimeter STD",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = baroStd
        };
        vars["A32NX_FCU_RIGHT_EIS_BARO_IS_STD"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING STD:2", DisplayName = "F/O Altimeter STD",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = baroStd
        };
        // Stock-altimeter MB mirrors — drive the STD-flag watchdog (the FCU forces
        // the stock altimeter to exactly 1013.25 hPa while STD; in QNH it applies
        // the preselect). Announce-suppressed in ProcessSimVarUpdate.
        vars["BARO_MB_WATCH_L"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING MB:1", DisplayName = "Baro MB Captain",
            Type = SimVarType.SimVar, Units = "millibars",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };
        vars["BARO_MB_WATCH_R"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING MB:2", DisplayName = "Baro MB First Officer",
            Type = SimVarType.SimVar, Units = "millibars",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };
        // Silent caches — never spoken individually (TCAS detail speech rides the
        // A32NX_TCAS_STATE monitor entry; CG feeds the W/Shift+W readouts; the BARO
        // MB mirrors only drive the STD-flag watchdog) — hide from the Ctrl+M list.
        foreach (var k in new[] {
            "A32NX_TCAS_VSPEED_GREEN:1", "A32NX_TCAS_VSPEED_GREEN:2",
            "A32NX_TCAS_VSPEED_RED:1", "A32NX_TCAS_VSPEED_RED:2",
            "A32NX_TCAS_RA_CORRECTIVE", "A32NX_TCAS_RA_UP_ADVISORY_STATUS",
            "A32NX_TCAS_RA_DOWN_ADVISORY_STATUS", "A32NX_TCAS_RA_RATE_TO_MAINTAIN",
            "A32NX_AIRFRAME_GW_CG_PERCENT_MAC", "BARO_MB_WATCH_L", "BARO_MB_WATCH_R" })
            vars[k].ExcludeFromMonitorManager = true;
        // End-to-end MobiFlight probe target: MainForm calc-writes a nonce here and
        // reads it back via the data-def path — the only reliable "calc path alive"
        // signal (response-based detection is invalid: a healthy install can execute
        // every command yet never send a single response — live-proven 2026-06-11).
        // Not in any panel; OnRequest; never announced.
        vars["MSFSBA_BRIDGE_PROBE"] = new SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE", DisplayName = "Bridge Probe",
            Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest
        };
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

        // (STD/QNH is the KOHLSMAN-backed combo above, driven by the
        //  H:A380X_EFIS_CP_BARO_PULL/PUSH_{n} events in HandleUIVariableSet — the
        //  supported dev-FBW path per MsfsBaroManager.ts. An earlier live test on an
        //  older build judged those H-events non-functional; dev FBW consumes them.)

        // (The legacy stock-COM "Radios" registrations — COM_STANDBY_FREQUENCY_SET:{n},
        //  COM{n}_RADIO_SWAP, COM_*_FREQUENCY:{n} — were removed: the FBW A380 IGNORES
        //  the stock COM events (live-verified), and the panel that referenced them is
        //  long gone. All tuning is RMP-only; the RMP window + the COM_ACTIVE_{n}/
        //  COM_STANDBY_{n} read-out keys cover it.)

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
        // FMS-set approach minimums. CORRECTED 2026-07: read the PLAIN-feet L:vars the
        // MFD PERF page writes directly (AIRLINER_MINIMUM_DESCENT_ALTITUDE = baro MDA,
        // AIRLINER_DECISION_HEIGHT = radio DH — the same vars the PFD/ISIS/GPWS read),
        // NOT the ARINC429 words A32NX_FM1_MINIMUM_DESCENT_ALTITUDE / _DECISION_HEIGHT.
        // Those FM1/FM2 words are NCD (read 2^32 → "Not set") until the FMC decides the
        // aircraft is in approach range (shouldTransmitMinimums(distanceToDestination) in
        // FmcAircraftInterface.ts), so at cruise a set minimum showed "Not set" (live-
        // verified: MDA 940 / DH 200 set, but FM1 words both NCD). The plain L:vars hold
        // the pilot's entry the instant it's set, at any phase. Unset sentinels: MDA <= 0,
        // DH < 0 (FBW resets MDA→0, DH→-1). Plain feet — decoded/announced in
        // ProcessSimVarUpdate + TryGetDisplayOverride (NOT ARINC429 now).
        MonNum("AIRLINER_MINIMUM_DESCENT_ALTITUDE", "Baro Minimum", "feet");
        MonNum("AIRLINER_DECISION_HEIGHT", "Radio Minimum (DH)", "feet");

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

        // FEED-TANK + TRANSFER FUEL PUMPS (8 feed + 12 transfer = 20). On the FBW A380 the
        // cockpit pump pushbuttons are modelled as ELECTRICAL CIRCUITS (state =
        // `A:CIRCUIT CONNECTION ON:<id>`, toggled by `<id> 1
        // (>K:2:ELECTRICAL_BUS_TO_CIRCUIT_CONNECTION_TOGGLE)`) — NOT the stock
        // FUELSYSTEM pumps. Each is an Off/On combo: live state from the circuit
        // simvar; the set toggles the circuit (only when desired != current) via
        // HandleUIVariableSet + _fuelPumpCircuits. Feed-tank circuits are 2-3, 64-69
        // from the cockpit model (PUSH_OVHD_FUEL_FEEDTK*_MAIN/STBY); transfer-pump
        // circuits are 70-81 from the same model. #107 transcript: "turn the fuel pumps
        // on in sequence, main then standby." Calculator-path toggle verified live
        // (circuit 2 flips 0↔1).
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
            ("FUELPUMP_OUTR_L", 70, "Left Outer Tank Pump"),
            ("FUELPUMP_MID_L_FWD", 71, "Left Mid Tank Forward Pump"),
            ("FUELPUMP_MID_L_AFT", 72, "Left Mid Tank Aft Pump"),
            ("FUELPUMP_INR_L_FWD", 73, "Left Inner Tank Forward Pump"),
            ("FUELPUMP_INR_L_AFT", 74, "Left Inner Tank Aft Pump"),
            ("FUELPUMP_OUTR_R", 75, "Right Outer Tank Pump"),
            ("FUELPUMP_MID_R_FWD", 76, "Right Mid Tank Forward Pump"),
            ("FUELPUMP_MID_R_AFT", 77, "Right Mid Tank Aft Pump"),
            ("FUELPUMP_INR_R_FWD", 78, "Right Inner Tank Forward Pump"),
            ("FUELPUMP_INR_R_AFT", 79, "Right Inner Tank Aft Pump"),
            ("FUELPUMP_TRIM_L", 80, "Trim Tank Left Pump"),
            ("FUELPUMP_TRIM_R", 81, "Trim Tank Right Pump"),
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

        // Anti-ice status.
        ReadEnum("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", "Wing Anti-Ice Flowing", onOff);
        ReadEnum("A32NX_PNEU_WING_ANTI_ICE_HAS_FAULT", "Wing Anti-Ice Fault", fault);

        // Oxygen.
        ReadEnum("A32NX_OXYGEN_TMR_RESET_FAULT", "Oxygen Timer Reset Fault", fault);

        // Calls / EVAC / cabin / cargo smoke.
        ReadEnum("A32NX_EVAC_COMMAND_FAULT", "Evacuation Command Fault", fault);
        ReadEnum("A32NX_CARGOSMOKE_FWD_DISCHARGED", "Cargo Fwd Smoke Agent", dischargedVd);
        ReadEnum("A32NX_CARGOSMOKE_AFT_DISCHARGED", "Cargo Aft Smoke Agent", dischargedVd);

        // Recorder / misc overhead.
        // DFDR event mark: HOLD button — set On to mark, Off to release.
        OnOff("A32NX_DFDR_EVENT_ON", "DFDR Event");
        OnOff("A32NX_OVHD_NSS_DATA_TO_AVNCS_TOGGLE", "NSS Data to Avionics");
        OnOff("A32NX_NSS_MASTER_OFF", "NSS Master Off");

        // Wipers — 3-position OFF/SLOW/FAST selector per side (Capt = electrical circuit
        // 141, F/O = 143), independent. CORRECTED 2026-07: the real FBW wiper knob
        // (ASOBO_GT_Switch_3States, FBW_Airbus_Wiper_Knob template) has THREE positions —
        // OFF = circuit switch off; SLOW = switch on + circuit POWER SETTING 75%; FAST =
        // switch on + POWER 100% (ANIMTIP_0/1/2 = OFF/SLOW/FAST). The old combo was On/Off
        // only, and On always drove 100%, so SLOW was unreachable. The live position is a
        // TWO-var state (switch + power) — the power setting persists at its default (100%)
        // while the switch is off, so a cold-start read of power=100 + switch=off must read
        // OFF, not FAST. Neither var alone encodes the position, so the settable control is a
        // SYNTHETIC 3-position combo (SelectedIndex authoritative, like the speed-brake) that
        // drives the circuit in HandleUIVariableSet; the true live position is read back from
        // the hidden switch+power vars below (WiperState → the "… Position" readouts).
        Act("WIPER_LEFT", "Captain Wiper", new Dictionary<double, string> { [0] = "Off", [1] = "Slow", [2] = "Fast" });
        Act("WIPER_RIGHT", "First Officer Wiper", new Dictionary<double, string> { [0] = "Off", [1] = "Slow", [2] = "Fast" });
        // Hidden live-state backers (circuit switch + power per side): the switch var doubles
        // as the OFF/SLOW/FAST readout (decoded in TryGetDisplayOverride from the cached pair,
        // via _wiperState*), and the set handler reads the switch to decide whether to toggle.
        foreach (var (key, who) in new[] { ("WIPER_L_SW", "Captain Wiper Position"), ("WIPER_R_SW", "First Officer Wiper Position") })
            vars[key] = new SimVarDefinition
            {
                Name = $"CIRCUIT SWITCH ON:{(key == "WIPER_L_SW" ? 141 : 143)}", DisplayName = who,
                Type = SimVarType.SimVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromMonitorManager = true
            };
        foreach (var key in new[] { "WIPER_L_PWR", "WIPER_R_PWR" })
            vars[key] = new SimVarDefinition
            {
                Name = $"CIRCUIT POWER SETTING:{(key == "WIPER_L_PWR" ? 141 : 143)}", DisplayName = key,
                Type = SimVarType.SimVar, Units = "percent",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromMonitorManager = true
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

        // ATC datalink — the A380 has NO DCDU instrument (CPDLC lives on the MFD
        // ATCCOM pages, NOT a DCDU). The A32NX_DCDU_ATC_MSG_ACK "acknowledge" button
        // acts on a DCDU that doesn't exist on this airframe, so it was REMOVED
        // 2026-06-13 (it's a dead control here; the A32NX keeps its real DCDU window).
        // Answering a CPDLC clearance on the A380 is done on the MFD ATCCOM pages,
        // which are unimplemented in the current FBW dev build (MfdNotFound). The
        // "ATC Message Waiting" indicator IS real (the shared ATSU datalink sets it)
        // and drives the incoming-CPDLC auto-announce — kept; the message content is
        // read on the MFD MSG RECORD page.
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
        // "Landing Flap 3" (A32NX_GPWS_FLAPS3) REMOVED 2026-06 — it is an A320 *pedestal*
        // GPWS control with NO A380 equivalent. Verified live: the A380's GPWS/TAWS UI
        // (MFD SURV CONTROLS) exposes exactly TERR SYS / GPWS / G/S MODE / FLAP MODE — all
        // four already covered above (FLAP MODE = A32NX_GPWS_FLAPS_OFF). There is no flap-3
        // selector on the A380, and A32NX_GPWS_FLAPS3 is read by nothing on this build.
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
            // Per-engine fuel used since start (kg) — the post-flight even-burn check.
            // MUST read the FBW L:var A32NX_FUEL_USED:n (what the SD ENGINE page uses) — the
            // stock GENERAL ENG FUEL USED SINCE START:n is NEVER populated by the FBW A380
            // (it runs its own FQMS) so it read a permanent 0. Key stays ENG_FUEL_USED:n.
            vars[$"ENG_FUEL_USED:{n}"] = new SimVarDefinition
            {
                Name = $"A32NX_FUEL_USED:{n}", DisplayName = $"Engine {n} Fuel Used",
                Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest, Units = "kilograms"
            };
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
        // Takeoff THS ("THS FOR" — the takeoff CG in %MAC the pilot enters on the MFD
        // PERF T.O page). CORRECTED 2026-07: read the ARINC429 word A32NX_FM1_TO_PITCH_TRIM
        // (what the FMC actually writes, via setTakeoffTrim → FmArinc429OutputWord, and what
        // the FWS reads for the TO-CONFIG stab check), NOT the bare A32NX_TO_PITCH_TRIM,
        // which is a dead/unwritten var that reads 0 → decoded "Not computed" even with a
        // value set. The word carries a PERCENT (%MAC), NOT degrees — the FWS compares it
        // straight to fqmsGrossWeightCgPercent, and the PERF field is a PercentageFormat;
        // the old "X.X degrees up/down" render was wrong. Decoded in TryGetDisplayOverride.
        Read("A32NX_FM1_TO_PITCH_TRIM", "Takeoff Trim (THS for CG)", "number");
        // Rudder trim ("weather trim"). The stock RUDDER TRIM PCT does not track the
        // FBW SEC-computed value; the real figure the PFD/SD show is the ARINC429
        // word A32NX_SEC_1_RUDDER_ACTUAL_POSITION (degrees, positive = nose-Left).
        // Decoded to "Left/Right X.X degrees" / "Neutral" in TryGetDisplayOverride.
        Read("A32NX_SEC_1_RUDDER_ACTUAL_POSITION", "Rudder Trim", "number");
        // Rudder trim RESET (zeroes the trim) + nosewheel-steering pedal disconnect
        // (held during the rudder flight-control check so the nose wheel doesn't
        // scrub). #107 transcript gaps. Settable via the calculator catch-all.
        // Rudder Trim Reset: the cockpit fires the stock K-event RUDDER_TRIM_RESET on
        // press (NOT an L:var — pulsing A32NX_RUDDER_TRIM_RESET drove nothing). Render as a
        // BUTTON (user request — it's a momentary action, not a state). The press fires the
        // K-event via the dedicated HandleUIVariableSet branch. NOT added to _momentaryButtons
        // (that path would pulse the dead L:var) — RenderAsButton + the K-event branch own it.
        vars["A32NX_RUDDER_TRIM_RESET"] = new SimVarDefinition
        {
            Name = "A32NX_RUDDER_TRIM_RESET", DisplayName = "Rudder Trim Reset", Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest, IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Reset" },
            RenderAsButton = true, SuppressRestingButtonState = true
        };
        // Nosewheel-steering PEDAL DISCONNECT is a HELD control (the FBW A380 reads
        // A32NX_TILLER_PEDAL_DISCONNECT every frame and cuts pedal steering while it's 1 —
        // hydraulic/mod.rs:4567/4664), so it's an On/Off TOGGLE, not a momentary button: set
        // On to disconnect for the rudder flight-control check, Off to reconnect. The L:var IS
        // the actuator (live-verified it latches); writes go via the HandleUIVariableSet branch.
        OnOff("A32NX_TILLER_PEDAL_DISCONNECT", "Nosewheel Steering Pedal Disconnect");
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

        // External-power availability per receptacle (read-only; the connect/disconnect
        // is done on the flyPad Ground page, the overhead EXT PWR pushbuttons, or GSX).
        // Announces when ground power is connected/available or disconnected at the stand.
        //
        // Read the FBW SOURCE L-var A32NX_EXT_PWR_AVAIL:n (1 = GPU connected/available at
        // receptacle n, 0 = disconnected). This is the SINGLE source of truth that BOTH the
        // flyPad GPU button (fbw GPUManagement.ts) and GSX (GsxSync.ts) write — verified live
        // by toggling the flyPad GPU: A32NX_EXT_PWR_AVAIL:n flips 1<->0 exactly with it, while
        // every other candidate stayed put.
        //
        // Do NOT read A32NX_OVHD_ELEC_EXT_PWR_n_PB_IS_AVAILABLE (the overhead AVAIL light) —
        // verified live it stays 0 even while the GPU is connected (it needs the receptacle
        // contactor / power-quality conditions, not met just by plugging in), so it NEVER fired
        // for the flyPad path: that was the long-standing "GPU connect not announced" bug. The
        // stock simvar EXTERNAL POWER AVAILABLE:n is useless too (stuck at 1 at any suitable
        // gate regardless of actual connection, verified live).
        //
        // The name is colon-indexed; it registers as an L-var via the same L:{name} data-def
        // path as A32NX_AUTOTHRUST_TLA:n (which reads fine), so the read is reliable.
        for (int n = 1; n <= 4; n++)
        {
            vars[$"A380X_GND_GPU_AVAIL_{n}"] = new SimVarDefinition
            {
                Name = $"A32NX_EXT_PWR_AVAIL:{n}", DisplayName = $"External Power {n} Available",
                Type = SimVarType.LVar, Units = "Bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Yes" }
            };
        }

        return vars;
    }

    // Per-var last announced state for the ARINC429 enum guard.
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
                "Calls", "Signs", "Wipers", "ADIRS", "Flight Control Computers", "Engine FADEC and Manual Start",
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





    // ===================================================================
    // Update hook (bridge diagnostics)
    // ===================================================================
    // Last seen E/WD code per line (live cache for the Alt+E E/WD window build).
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

    // Shared TCAS RA state + composer (Services/TcasRaGuidance.cs); the timer and
    // announcer stay per-def so disposal rides StopAllMotion.
    private readonly Services.TcasRaGuidance _tcasRa = new();
    private System.Windows.Forms.Timer? _tcasRaComposeTimer;
    private ScreenReaderAnnouncer? _tcasRaAnnouncer;


    // Icing conditions: the cockpit ice-accretion "stick" indicator is a CONTINUOUS
    // 0..1 ratio, not a 0/1 flag — so it's announced as a discrete state with
    // hysteresis (entered icing / cleared), not as a spammy raw value. _icingActive
    // holds the debounced state; _icingBaselineDone silences the first sample.
    private bool _icingActive;
    private bool _icingBaselineDone;
    private const double ICING_DETECT_RATIO = 0.05;   // rising edge → "icing conditions"
    private const double ICING_CLEAR_RATIO = 0.02;    // falling edge → "icing conditions cleared"

    // BTV (Brake-To-Vacate) rollout call-outs: current BTV state (gate) and which
    // distance thresholds have already been spoken this landing (reset between).
    private int _btvState = 0;
    // Self-tracked anti-skid switch state. The stock A:ANTISKID BRAKES ACTIVE reads
    // unreliably via the data-def path on the A380 (live-verified: same batch returned
    // 1 AND 0), so the cached "current" got stuck at On and the combo's "select On" never
    // fired the toggle. ANTISKID_BRAKES_TOGGLE reliably FLIPS the switch, so we track the
    // commanded state ourselves, seeded to the power-on default (ON) instead of the flaky
    // cache, and never re-read the data-def value.
    private bool? _antiskidOn;
    private string? _lastAutolandCap; // last decoded LAND capability ("none"/"LAND 2"/...)
    private int _fmgcPhaseA380 = -1; // numeric FMGC flight phase; gates the capability announce (taxi flicker spam)
    private readonly int[] _gpuAvail = { -1, -1, -1, -1 };   // last external-power-available state per GPU (-1 = unseen)
    private readonly HashSet<int> _btvExitSpoken = new();
    private readonly HashSet<int> _btvRwyEndSpoken = new();
    private static readonly int[] BtvExitThresholdsM   = { 1200, 800, 500, 300, 150 };
    private static readonly int[] BtvRwyEndThresholdsM = { 900, 600, 300, 150 };

    // FMA armed-mode decode. The legacy A32NX_FMA_{VERTICAL|LATERAL}_ARMED bitmasks
    // (bit 0 = ALT, live-verified = 1 at ready-for-taxi) are decoded to mode names so
    // arming a mode speaks "Altitude armed" / "NAV armed" — matching the A32NX (and
    // decoding it, vs the A32NX's old raw-number announce). Bits per the FBW a32nx-api.
    private int _prevVertArmed = -1, _prevLatArmed = -1;
    private string _lastFlightPhaseA380 = "";
    // ND TO-waypoint ident: packed 6 bits/char, 8 chars/word (low bits first),
    // char = code + 31. Cached from ProcessSimVarUpdate; decoded in TryGetDisplayOverride.
    private double _ndIdent0, _ndIdent1;

    // ---- Passengers on board (Status panel) ----
    // A32NX_FMS_PAX_NUMBER (the var the Status panel used to read) is written ONLY by
    // the MFD FUEL&LOAD page (MfdFmsFuelLoad.tsx) — so boarding via the flyPad never
    // sets it and it reads 0. The count is the sum of the per-station seat-bitmask L:vars
    // (each holds an integer whose set-bit count = filled seats in that cabin zone; max
    // 50 seats/station on the A380X, so every value is < 2^53 and exact as a double — the
    // same float64 the FBW EFB itself reads). We popcount each and sum; the result is
    // shown for "A32NX_FMS_PAX_NUMBER" in TryGetDisplayOverride.
    // CORRECTED 2026-07: sum the *_DESIRED* (target) bitmasks, NOT the boarded ones. The
    // flyPad headline number and GSX (FSDT_GSX_NUMPASSENGERS) both reflect totalPaxDesired
    // — the planned load — while the boarded bitmask (A32NX_PAX_<st>, no suffix) lags and,
    // under GSX-driven boarding, settles BELOW target and stays there (live: user planned
    // 466, boarded bitmask summed 428). The desired total is the number that matches the
    // flyPad, GSX, and the loadsheet, and is stable at every phase — that's what a pilot
    // means by "passengers on board". (Popcount of the boarded set is itself correct — it's
    // just the wrong quantity to show here.)
    private static readonly string[] PaxStationVars =
    {
        "A32NX_PAX_MAIN_FWD_A_DESIRED", "A32NX_PAX_MAIN_FWD_B_DESIRED",
        "A32NX_PAX_MAIN_MID_1A_DESIRED", "A32NX_PAX_MAIN_MID_1B_DESIRED", "A32NX_PAX_MAIN_MID_1C_DESIRED",
        "A32NX_PAX_MAIN_MID_2A_DESIRED", "A32NX_PAX_MAIN_MID_2B_DESIRED", "A32NX_PAX_MAIN_MID_2C_DESIRED",
        "A32NX_PAX_MAIN_AFT_A_DESIRED", "A32NX_PAX_MAIN_AFT_B_DESIRED",
        "A32NX_PAX_UPPER_FWD_DESIRED", "A32NX_PAX_UPPER_MID_A_DESIRED", "A32NX_PAX_UPPER_MID_B_DESIRED", "A32NX_PAX_UPPER_AFT_DESIRED"
    };
    private readonly Dictionary<string, int> _paxFilledByStation = new();
    private int _paxOnBoard;
    // Wiper live position per side (0 Off / 1 Slow / 2 Fast), computed from the hidden
    // switch+power backers in ProcessSimVarUpdate; rendered by TryGetDisplayOverride.
    // Default 0 = Off (the cold-start state, before the first circuit read arrives).
    private int _wiperStateL, _wiperStateR;
    // Last-seen circuit switch (0/1) + power (%) per side; power defaults 100 so a switch
    // read arriving before the power read still classifies On correctly (Fast until proven Slow).
    private double _wiperSwL, _wiperPwrL = 100.0, _wiperSwR, _wiperPwrR = 100.0;
    // Decode (switch wins over the persisted power setting) is the shared WiperPosition.
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
    private static readonly (int bit, string name)[] _vertArmedBits =
        { (1, "Altitude"), (2, "Altitude constraint"), (4, "Climb"), (8, "Descent"), (16, "Glideslope"), (32, "Final"), (64, "TCAS") };
    private static readonly (int bit, string name)[] _latArmedBits = { (1, "NAV"), (2, "Localizer") };


    /// <summary>
    /// Engine-mode selector fans the chosen position (0=Crank, 1=Norm,
    /// 2=Ignition/Start) out to all four engines via TURBINE_IGNITION_SWITCH_SET{N}.
    /// </summary>
    // Exterior-light On/Off combos -> the stock SET event fired with 0/1. The
    // combo's value comes from the matching LIGHT * simvar (Continuous +
    // announced), so it reflects state and auto-announces; this only handles the
    // write side.
    // Settable temperature selectors: the _SET combo key -> the FBW knob L:var
    // and the zone's Celsius range. knob = (temp - Lo)/(Hi - Lo)*300 + Offset.
    // Offset is the per-knob bias FBW applies: the cockpit/cargo knobs are a plain
    // 0-300 sweep (Offset 0), but the CABIN knob is biased by +50 — FBW's reader
    // (a380_systems air_conditioning/mod.rs selected_cabin_temperature does
    // `knob % 400 - 50`) and its own setter (command_cabin_selected_temperature:
    // `(temp-18)/0.04 + 50`) both carry the +50. Without it the cabin lands ~2 C
    // cold across the whole band (knob is ~50 units / 2 C too low).
    private sealed record TempSel(string Knob, double Lo, double Hi, string Label, double Offset = 0);
    private static readonly Dictionary<string, TempSel> _tempSelectors = new Dictionary<string, TempSel>
    {
        ["COND_CKPT_TEMP_SET"] = new TempSel("A32NX_OVHD_COND_CKPT_SELECTOR_KNOB", 18, 30, "Cockpit"),
        ["COND_CABIN_TEMP_SET"] = new TempSel("A32NX_OVHD_COND_CABIN_SELECTOR_KNOB", 18, 30, "Cabin", 50),
        ["CARGO_FWD_TEMP_SET"] = new TempSel("A32NX_OVHD_CARGO_AIR_FWD_SELECTOR_KNOB", 5, 25, "Forward cargo"),
        ["CARGO_BULK_TEMP_SET"] = new TempSel("A32NX_OVHD_CARGO_AIR_BULK_SELECTOR_KNOB", 5, 25, "Bulk cargo"),
    };

    private static readonly Dictionary<string, string> _extLightSetEvents = new Dictionary<string, string>
    {
        ["LIGHT_BEACON"] = "BEACON_LIGHTS_SET",
        ["LIGHT_NAV"] = "NAV_LIGHTS_SET",
        ["LIGHT_WING"] = "WING_LIGHTS_SET",
        ["LIGHT_LOGO"] = "LOGO_LIGHTS_SET",
        // LIGHT_LANDING + NOSE_LIGHT use the INDEXED LANDING_LIGHTS_SET (handled below).
        ["LIGHT_STROBE"] = "STROBES_SET"
    };







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

    private int _lastSpoilerBand = -1;   // speed-brake handle band (10% steps) last announced
    private double _lastBaroL = -1, _lastBaroR = -1; // last announced EFIS baro (hPa, 0.1 resolution)
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
        [-1] = "Default automatic", [0] = "Engine", [1] = "APU", [2] = "Bleed", [3] = "Air Cond",
        [4] = "Pressurization", [5] = "Doors", [6] = "Electrical AC", [7] = "Electrical DC",
        [8] = "Fuel", [9] = "Wheel", [10] = "Hydraulics", [11] = "Flight Controls",
        [12] = "Circuit Breakers", [13] = "Cruise", [14] = "Status", [15] = "Video",
        [16] = "Upper E/WD"
    };
    private Dictionary<double, string>? _readoutMap;

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



    // Metric ALTITUDE state (the A380 FCU MTRS button, A32NX_METRIC_ALT_TOGGLE).
    private bool _metricAlt;

    // Render-time SimConnect handle so A380SdRows fmt closures can read sibling ARINC words
    // (B1→B4 CPCS source selection, SEC1↔SEC3 rudder-trim) at paint time.
    private SimConnectManager? _sdRender;
    // Sim handle captured when any display panel is shown, so sibling-reading display
    // overrides (e.g. the computed Vertical Deviation) can read the PFD cache off-render.
    private SimConnectManager? _displaySim;







    // (The dedicated PFD/FMA readout WINDOW was removed — those flight values live on
    // the PFD panel + the individual readout hotkeys; only the Alt+E E/WD window remains.)

    // Active FWS content, pushed in by the CoherentFwsFailureClient (reads the FwsCore
    // directly). Two blocks matching where the real A380 shows them:
    //   _activeFwsFailures = E/WD block  — warnings/cautions + abnormal procedures
    //   _activeFwsStatus   = STATUS block — inoperative systems, limitations, deferred
    // The STATUS page can't be driven on the FBW SD (it rejects page index 14), so both are
    // surfaced in the E/WD window — warnings at the top, STATUS clearly separated below.
    // Volatile ref-swap = safe lock-free read from the window's build thread.
    private volatile List<string> _activeFwsFailures = new();
    private volatile List<string> _activeFwsStatus = new();
    // Normalised core texts of the active E/WD warnings (e.g. "SURV XPDR STBY"), cached on
    // each update so the live memo call-out path can suppress a memo that is ALSO an active
    // warning (audio dedup, mirroring the E/WD-window display dedup).
    private volatile HashSet<string> _warnCore = new(StringComparer.OrdinalIgnoreCase);



    // (The ECAM System Display readout WINDOW was removed — SD content reads via the ECAM
    // Control Panel "System Display Page" combo + status box; only Alt+E remains a window.)







    // ---- Public FCU API for the dedicated A380 FCU windows (Forms/FBWA380/*) ----
    // Forms validate input, then call these; all set/readback mechanism lives here
    // (already live-verified). Each setter fires the event and speaks the readback.



    // Brief window after a window-driven altitude set during which the side-effect
    // "Altitude Increment: 100" auto-announce is suppressed (the user set an altitude,
    // not the increment). Only armed when SetFCUAltitudeValue actually changes the increment.
    private DateTime _altIncrAnnounceSuppressUntil = DateTime.MinValue;




    // ---- Smooth slider ramp (motorised cockpit seats / armrests / sunshades / visors) ----
    // Writing the target L:var in ONE step makes the 3-D model SNAP there — you only hear a
    // single "tick" of the motor. Real motorised seats move gradually while the switch is held,
    // so we step the L:var toward the target a few units every 40 ms ON THE UI THREAD (a
    // WinForms timer — never a thread-pool timer, which would call SimConnect off-thread). The
    // FBW plays the sustained motor sound + smooth animation. _sliderCurrent is authoritative
    // once a slider is first touched (seeded from the cache) so the ramp stays smooth.
    private readonly Dictionary<string, double> _sliderTarget = new();
    private readonly Dictionary<string, double> _sliderCurrent = new();
    private readonly Dictionary<string, double> _sliderStep = new();
    private System.Windows.Forms.Timer? _sliderRampTimer;
    private SimConnectManager? _sliderRampSim;





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
    private long _rmpKeySeq;    // makes each RMP keypad calc string unique (anti-dedup; see SendRmpKey)

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





    // The A380's NEW FCU ignores the legacy dotted "A32NX.FCU_SPD_MACH_TOGGLE_PUSH" H-event the
    // A320 used — live-verified that firing it (either dot or underscore form) does NOTHING. Its
    // SpeedManager (FCU/Managers/SpeedManager.ts) switches SPD<->MACH via the STOCK K-events
    // AP_MANAGED_SPEED_IN_MACH_ON / _OFF. This one conditional RPN reads the current mode (the
    // stock SimVar AUTOPILOT MANAGED SPEED IN MACH) and fires the opposite — verified live to flip
    // the mode. (Used by both the FCU Speed window MACH button and the panel Speed/Mach toggle.)
    private const string SpdMachToggleRpn =
        "(A:AUTOPILOT MANAGED SPEED IN MACH, Bool) if{ 1 (>K:AP_MANAGED_SPEED_IN_MACH_OFF) } els{ 1 (>K:AP_MANAGED_SPEED_IN_MACH_ON) }";




}
