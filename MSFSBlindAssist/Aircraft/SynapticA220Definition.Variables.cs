using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Variable declarations for the Synaptic A220-300.
///
/// TRANSPORT RULE (recon-verified 2026-07-28, tools/a220-gen/reference/recon-2026-07-28.md):
/// A220 L:var names contain SPACES ("A22X APU Switch"), which SetLVar's space/colon guard
/// routes to the unreliable data-def write path — and CalcPathVerified never goes true for
/// non-FBW defs anyway. So EVERY L:var write for this aircraft goes through
/// ExecuteCalculatorCode directly in HandleUIVariableSet (the calc parser accepts spaces
/// inside "(>L:...)" — proven live via the MobiFlight default channel). READS are fine:
/// the L:var read path registers a data-def datum, which accepts spaces.
///
/// The aircraft plays its own aural callouts (V1, radio heights, TAWS, takeoff config) —
/// the ~91 "L:A22X Aural *" flags are deliberately NOT registered (see
/// SynapticA220SimVarData.AuralVars and the pin test).
/// </summary>
public partial class SynapticA220Definition
{
    // ---- declaration helpers -------------------------------------------------

    private static Dictionary<double, string> Labels(params string[] labels)
    {
        var d = new Dictionary<double, string>();
        for (int i = 0; i < labels.Length; i++) d[i] = labels[i];
        return d;
    }

    /// <summary>Multi-position L:var switch rendered as a combo (labels verbatim from simvars.mdx).</summary>
    private static SimVarDefinition LSwitch(string name, string display, params string[] labels) => new()
    {
        Name = name,
        DisplayName = display,
        Type = SimVarType.LVar,
        Units = "number",
        UpdateFrequency = UpdateFrequency.OnRequest,
        ValueDescriptions = Labels(labels)
    };

    /// <summary>Momentary L:var pushbutton (pulse 1 then release) — chrono, nav source, crosstune…</summary>
    private static SimVarDefinition LMomentary(string name, string display) => new()
    {
        Name = name,
        DisplayName = display,
        Type = SimVarType.LVar,
        Units = "number",
        UpdateFrequency = UpdateFrequency.OnRequest,
        RenderAsButton = true,
        SuppressRestingButtonState = true,
        ValueDescriptions = Labels("Released", "Pressed")
    };

    /// <summary>Annunciator lamp: Continuous+IsAnnounced batch var (0 individual defs, MD-11 pattern).</summary>
    private static SimVarDefinition Lamp(string name, string display) => new()
    {
        Name = name,
        DisplayName = display,
        Type = SimVarType.LVar,
        Units = "bool",
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ValueDescriptions = Labels("off", "illuminated")
    };

    /// <summary>
    /// Keys whose updates are swallowed silently in ProcessSimVarUpdate (MD-11
    /// _silentReadouts pattern): they ride the continuous batch so the cache stays
    /// fresh at 1 Hz for readouts and dialog walks, but a spoken bare number would be
    /// meaningless. NOTE: Continuous+IsAnnounced=false would get NO subscription at
    /// all (stale cache) — batch coverage requires IsAnnounced=true.
    /// </summary>
    private readonly HashSet<string> _silentReadoutKeys = new();

    /// <summary>Continuous silent var: cached for readouts/logic, never spoken.</summary>
    private void AddQuiet(Dictionary<string, SimVarDefinition> v, string key, string name,
        string units, SimVarType type = SimVarType.LVar)
    {
        v[key] = new SimVarDefinition
        {
            Name = name,
            DisplayName = name,
            Type = type,
            Units = units,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromMonitorManager = true
        };
        _silentReadoutKeys.Add(key);
    }

    /// <summary>Read-only numeric status row for a panel display box.</summary>
    private static SimVarDefinition Status(string name, string display, string units, string format = "F0", SimVarType type = SimVarType.LVar) => new()
    {
        Name = name,
        DisplayName = display,
        Type = type,
        Units = units,
        UpdateFrequency = UpdateFrequency.OnRequest,
        RenderAsReadOnlyStatus = true,
        Format = format
    };

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var v = GetBaseVariables();

        // ==== Overhead — Electrical ==========================================
        v["A22X_BATTERY_1"] = new SimVarDefinition
        {
            Name = "ELECTRICAL MASTER BATTERY:1", DisplayName = "Battery 1",
            Type = SimVarType.SimVar, Units = "bool", UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = Labels("Off", "On")
        };
        v["A22X_BATTERY_2"] = new SimVarDefinition
        {
            Name = "ELECTRICAL MASTER BATTERY:2", DisplayName = "Battery 2",
            Type = SimVarType.SimVar, Units = "bool", UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = Labels("Off", "On")
        };
        // simvars.mdx documents the stock "EXTERNAL POWER ON:1", but the Synaptic
        // WASM never DRIVES it — measured live 2026-09-22 with ground power genuinely
        // in use it still read 0, while the cockpit pb's own state var read 1. The
        // documented name is the doc's, not the aircraft's; read what the lamp reads.
        v["A22X_EXT_PWR"] = new SimVarDefinition
        {
            Name = "A22X External Power In Use", DisplayName = "External Power",
            Type = SimVarType.LVar, Units = "bool", UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = Labels("Off", "On")
        };
        v["A22X_GPU_AVAIL"] = new SimVarDefinition
        {
            Name = "INI_GPU_AVAIL", DisplayName = "Ground Power Unit",
            Type = SimVarType.LVar, Units = "bool", UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true, RenderAsReadOnlyStatus = true,
            ValueDescriptions = Labels("Not available", "Available")
        };
        // "Off" switches: 1 = selected OFF (the physical korry is pushed out).
        v["A22X_L_GEN_OFF"] = LSwitch("A22X L Gen Off", "Left Generator", "On", "Off");
        v["A22X_R_GEN_OFF"] = LSwitch("A22X R Gen Off", "Right Generator", "On", "Off");
        v["A22X_APU_GEN_OFF"] = LSwitch("A22X APU Gen Off", "APU Generator", "On", "Off");
        v["A22X_CABIN_POWER_OFF"] = LSwitch("A22X Cabin Power Off", "Cabin Power", "On", "Off");
        v["A22X_BUS_ISOLATION"] = LSwitch("A22X Bus Isolation Mode", "Bus Isolation", "Main", "Auto", "Ess");
        v["A22X_RAT_GEN"] = LSwitch("A22X RAT Gen", "RAT Generator (guarded)", "Stowed", "Deployed");
        v["A22X_L_GEN_DISC"] = LSwitch("A22X L Gen Disc", "Left Generator Disconnect (guarded)", "Normal", "Disconnected");
        v["A22X_R_GEN_DISC"] = LSwitch("A22X R Gen Disc", "Right Generator Disconnect (guarded)", "Normal", "Disconnected");

        // ==== Overhead — APU ==================================================
        // Start is a HOLD-to-start: the combo's Start action sustains 2 for ~3 s then
        // returns to 1 (Run) — implemented in HandleUIVariableSet, shared with the ECL.
        v["A22X_APU_SWITCH"] = LSwitch("A22X APU Switch", "APU Master", "Off", "Run", "Start");
        // Batch-covered, self-handled in ProcessSimVarUpdate (milestone announcements
        // only — a generic per-percent announce would chatter through the whole start).
        v["A22X_APU_RPM"] = new SimVarDefinition
        {
            Name = "A22X APU RPM", DisplayName = "APU RPM percent",
            Type = SimVarType.LVar, Units = "number", UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true, ExcludeFromMonitorManager = true, Format = "F0"
        };

        // ==== Overhead — Hydraulics ==========================================
        v["A22X_PTU"] = LSwitch("A22X PTU", "PTU Pump", "Off", "Auto", "On");
        v["A22X_ACMP_2B"] = LSwitch("A22X ACMP 2B", "AC Motor Pump 2B", "Off", "Auto", "On");
        v["A22X_ACMP_3A"] = LSwitch("A22X ACMP 3A", "AC Motor Pump 3A", "Off", "Auto", "On");
        v["A22X_ACMP_3B"] = LSwitch("A22X ACMP 3B", "AC Motor Pump 3B", "Off", "Auto", "On");
        v["A22X_HYD_1_SOV"] = LSwitch("A22X Hyd 1 SOV", "Hydraulic 1 Shutoff Valve", "Closed", "Open");
        v["A22X_HYD_2_SOV"] = LSwitch("A22X Hyd 2 SOV", "Hydraulic 2 Shutoff Valve", "Closed", "Open");

        // ==== Overhead — Fuel =================================================
        v["A22X_L_BOOST_PUMP"] = LSwitch("A22X L Boost Pump", "Left Boost Pump", "Off", "Auto", "On");
        v["A22X_R_BOOST_PUMP"] = LSwitch("A22X R Boost Pump", "Right Boost Pump", "Off", "Auto", "On");
        v["A22X_MANUAL_TRANSFER"] = LSwitch("A22X Manual Transfer", "Manual Fuel Transfer", "Off", "Right", "Center", "Left");
        v["A22X_GRAVITY_TRANSFER"] = LSwitch("A22X Gravity Transfer", "Gravity Transfer (guarded)", "Off", "On");

        // ==== Overhead — Air / Bleed =========================================
        v["A22X_L_BLEED_OFF"] = LSwitch("A22X L Bleed Off", "Left Engine Bleed", "On", "Off");
        v["A22X_R_BLEED_OFF"] = LSwitch("A22X R Bleed Off", "Right Engine Bleed", "On", "Off");
        v["A22X_APU_BLEED_OFF"] = LSwitch("A22X APU Bleed Off", "APU Bleed", "On", "Off");
        v["A22X_CROSSBLEED"] = LSwitch("A22X Crossbleed", "Crossbleed", "Closed", "Auto", "Open");
        v["A22X_L_PACK_OFF"] = LSwitch("A22X L Pack Off", "Left Pack", "On", "Off");
        v["A22X_R_PACK_OFF"] = LSwitch("A22X R Pack Off", "Right Pack", "On", "Off");
        v["A22X_PACK_FLOW"] = LSwitch("A22X Pack Flow", "Pack Flow", "Normal", "High");
        v["A22X_RAM_AIR"] = LSwitch("A22X Ram Air", "Ram Air (guarded)", "Closed", "Open");
        v["A22X_TRIM_AIR_OFF"] = LSwitch("A22X Trim Air Off", "Trim Air", "On", "Off");
        v["A22X_RECIRC_OFF"] = LSwitch("A22X Recirc Air Off", "Recirculation Fan", "On", "Off");
        v["A22X_MANUAL_TEMP"] = LSwitch("A22X Manual Temperature", "Manual Temperature Control", "Auto", "Manual");
        v["A22X_COCKPIT_AIR"] = new SimVarDefinition
        {
            Name = "A22X Cockpit Air", DisplayName = "Cockpit Temperature Knob",
            Type = SimVarType.LVar, Units = "percent over 100", UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsSlider = true, SliderMin = 0, SliderMax = 1
        };
        v["A22X_FWD_CABIN_AIR"] = new SimVarDefinition
        {
            Name = "A22X Fwd Cabin Air", DisplayName = "Forward Cabin Temperature Knob",
            Type = SimVarType.LVar, Units = "percent over 100", UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsSlider = true, SliderMin = 0, SliderMax = 1
        };
        v["A22X_AFT_CABIN_AIR"] = new SimVarDefinition
        {
            Name = "A22X Aft Cabin Air", DisplayName = "Aft Cabin Temperature Knob",
            Type = SimVarType.LVar, Units = "percent over 100", UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsSlider = true, SliderMin = 0, SliderMax = 1
        };
        v["A22X_FWD_CARGO_AIR"] = LSwitch("A22X Fwd Cargo Air", "Forward Cargo Air", "Off", "Vent", "Lo Heat", "Hi Heat");
        v["A22X_AFT_CARGO_AIR"] = LSwitch("A22X Aft Cargo Air", "Aft Cargo Air", "Off", "Vent");

        // ==== Overhead — Anti-ice / Heat =====================================
        v["A22X_L_COWL_AI"] = LSwitch("A22X L Cowl Anti Ice", "Left Cowl Anti-Ice", "Off", "Auto", "On");
        v["A22X_R_COWL_AI"] = LSwitch("A22X R Cowl Anti Ice", "Right Cowl Anti-Ice", "Off", "Auto", "On");
        v["A22X_WING_AI"] = LSwitch("A22X Wing Anti Ice", "Wing Anti-Ice", "Off", "Auto", "On");
        v["A22X_PROBE_HEAT"] = LSwitch("A22X Probe Heat", "Probe Heat", "Auto", "On");
        v["A22X_L_WINDOW_HEAT_OFF"] = LSwitch("A22X L Side Window Heat Off", "Left Side Window Heat", "On", "Off");
        v["A22X_R_WINDOW_HEAT_OFF"] = LSwitch("A22X R Side Window Heat Off", "Right Side Window Heat", "On", "Off");
        v["A22X_L_WSHLD_HEAT_OFF"] = LSwitch("A22X L Windshield Heat Off", "Left Windshield Heat", "On", "Off");
        v["A22X_R_WSHLD_HEAT_OFF"] = LSwitch("A22X R Windshield Heat Off", "Right Windshield Heat", "On", "Off");

        // ==== Overhead — Pressurization ======================================
        v["A22X_MAN_PRESS"] = LSwitch("A22X Man Press", "Manual Pressurization Mode", "Auto", "Manual");
        v["A22X_MANUAL_RATE"] = new SimVarDefinition
        {
            Name = "A22X Manual Rate", DisplayName = "Manual Cabin Rate Knob",
            Type = SimVarType.LVar, Units = "percent over 100", UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsSlider = true, SliderMin = 0, SliderMax = 1
        };
        v["A22X_EMER_DEPRESS"] = LSwitch("A22X Emergency Depress", "Emergency Depressurization (guarded)", "Normal", "On");
        v["A22X_DITCHING"] = LSwitch("A22X Ditching", "Ditching (guarded)", "Normal", "On");
        v["A22X_PAX_OXYGEN"] = LSwitch("A22X Passenger Oxygen", "Passenger Oxygen (guarded)", "Normal", "Deployed");

        // ==== Overhead — Fire / Evac / ELT ===================================
        v["A22X_L_ENG_FIRE"] = LSwitch("A22X L Eng Fire", "Left Engine Fire Push (guarded)", "Normal", "Pushed");
        v["A22X_R_ENG_FIRE"] = LSwitch("A22X R Eng Fire", "Right Engine Fire Push (guarded)", "Normal", "Pushed");
        v["A22X_ELT"] = LSwitch("A22X Emer Transmitter", "Emergency Locator Transmitter", "Test", "Arm", "On");
        v["A22X_EVAC"] = LSwitch("A22X Evac", "Evacuation Command", "Normal", "Evac");

        // ==== Overhead — Flight controls / misc ==============================
        v["A22X_PFCC_1_OFF"] = LSwitch("A22X PFCC 1 Off", "PFCC 1", "On", "Off");
        v["A22X_PFCC_2_OFF"] = LSwitch("A22X PFCC 2 Off", "PFCC 2", "On", "Off");
        v["A22X_PFCC_3_OFF"] = LSwitch("A22X PFCC 3 Off", "PFCC 3", "On", "Off");
        v["A22X_AURAL_INHIBIT"] = LSwitch("A22X Aural Warn Inhibit", "Aural Warning Inhibit", "Normal", "Inhibited");
        v["A22X_TAWS_GEAR_INHIBIT"] = LSwitch("A22X TAWS Gear Inhibit", "TAWS Gear Inhibit", "Normal", "Inhibited");
        v["A22X_TAWS_TERRAIN_INHIBIT"] = LSwitch("A22X TAWS Terrain Inhibit", "TAWS Terrain Inhibit", "Normal", "Inhibited");
        v["A22X_TAWS_FLAPS_INHIBIT"] = LSwitch("A22X TAWS Flaps Inhibit", "TAWS Flaps Inhibit", "Normal", "Inhibited");
        v["A22X_TAWS_GS_INHIBIT"] = LSwitch("A22X TAWS GS Inhibit", "TAWS Glideslope Inhibit", "Normal", "Inhibited");

        // ==== Eyebrow — Lights & signs =======================================
        v["A22X_NAV_LIGHTS"] = LSwitch("A22X Nav Lights", "Navigation Lights", "Off", "On");
        v["A22X_BEACON_LIGHTS"] = LSwitch("A22X Beacon Lights", "Beacon Lights", "Off", "On");
        v["A22X_STROBE_LIGHTS"] = LSwitch("A22X Strobe Lights", "Strobe Lights", "Off", "On");
        v["A22X_LOGO_LIGHTS"] = LSwitch("A22X Logo Lights", "Logo Lights", "Off", "On");
        v["A22X_WING_INSP_LIGHTS"] = LSwitch("A22X Wing Insp Lights", "Wing Inspection Lights", "Off", "On");
        v["A22X_TAXI_LIGHTS"] = LSwitch("A22X Taxi Lights", "Taxi Lights", "Off", "Narrow", "Wide");
        v["A22X_L_LANDING_LIGHTS"] = LSwitch("A22X L Landing Lights", "Left Landing Light", "Off", "On");
        v["A22X_R_LANDING_LIGHTS"] = LSwitch("A22X R Landing Lights", "Right Landing Light", "Off", "On");
        v["A22X_NOSE_LANDING_LIGHTS"] = LSwitch("A22X Nose Landing Lights", "Nose Landing Light", "Off", "On");
        v["A22X_EMERGENCY_LIGHTS"] = LSwitch("A22X Emergency Lights", "Emergency Lights", "Off", "Arm", "On");
        v["A22X_SEAT_BELT_LIGHTS"] = LSwitch("A22X Seat Belt Lights", "Seat Belt Signs", "Off", "Auto", "On");
        v["A22X_NO_PED_LIGHTS"] = LSwitch("A22X No PED Lights", "No Device Signs", "Off", "Auto", "On");
        v["A22X_DOME_LIGHTS"] = LSwitch("A22X Dome Lights", "Dome Light", "Off", "On");
        v["A22X_ANNUN_LIGHTS"] = LSwitch("A22X Annun Lights", "Annunciator Brightness", "Dim", "Bright", "Storm");
        v["A22X_LAMP_TEST"] = LSwitch("A22X Lamp Test", "Annunciator Lamp Test", "Off", "Test");

        // ==== Pedestal — Engine ==============================================
        // Engine masters use the documented stock events (ENGINE_MASTER_{n}_SET);
        // state read back from the write-only animation L:var.
        v["A22X_ENG_MASTER_1"] = new SimVarDefinition
        {
            Name = "A22X Engine 1 Master", DisplayName = "Engine 1 Master",
            Type = SimVarType.LVar, Units = "bool", UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = Labels("Off", "On")
        };
        v["A22X_ENG_MASTER_2"] = new SimVarDefinition
        {
            Name = "A22X Engine 2 Master", DisplayName = "Engine 2 Master",
            Type = SimVarType.LVar, Units = "bool", UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = Labels("Off", "On")
        };
        v["A22X_ENG_START_MODE"] = LSwitch("A22X Eng Start Mode", "Engine Start Mode", "L Eng Crank", "Auto", "R Eng Crank");
        v["A22X_CONT_IGNITION"] = LSwitch("A22X Continuous Ignition", "Continuous Ignition", "Off", "On");
        // Continuous so a hardware/keyboard toggle announces ("Parking Brake: Set");
        // panel-combo sets stay single-spoken via the global UI-echo suppression.
        v["A22X_PARKING_BRAKE"] = new SimVarDefinition
        {
            Name = "A22X Parking Brake", DisplayName = "Parking Brake",
            Type = SimVarType.LVar, Units = "bool", UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = Labels("Off", "Set")
        };
        v["RUDDER_TRIM_LEFT"] = new SimVarDefinition
        {
            Name = "RUDDER_TRIM_LEFT", DisplayName = "Rudder Trim Left",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest, RenderAsButton = true
        };
        v["RUDDER_TRIM_RIGHT"] = new SimVarDefinition
        {
            Name = "RUDDER_TRIM_RIGHT", DisplayName = "Rudder Trim Right",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest, RenderAsButton = true
        };
        // Trim READ-OUTS. Both are rendered by TryGetDisplayOverride from the aircraft's
        // "A22X.Flight Control Data" CommBus block — the same source the EICAS trim block
        // reads (efcs.pitch_trim / efcs.rudder_trim). Neither trim exists as a SimVar or
        // L:var on this aircraft, so these two defs are CARRIERS: they exist to give the
        // display rows a var that MainForm will refresh, and their own value is only the
        // fallback shown when the Coherent link is down. "L:A22X Horizontal Stabilizer" is
        // the trim ANIMATION ratio, not the units the cockpit prints — so this raw row is
        // still not the trim setting. It IS convertible though: the ratio spans the same
        // 0-17 travel the EICAS draws, so ratio * A220Afdx.StabTrimFullScaleUnits gives the
        // printed units (measured 2026-09-22 — see that constant). StartStabTrimWalk uses
        // exactly that as its bus-less read-back; converting this display row the same way
        // is an obvious follow-up that has NOT been done yet.
        v["A22X_STAB_TRIM"] = Status("A22X Horizontal Stabilizer", "Stabilizer Trim", "percent over 100", "F2");
        // Numeric entry: type the EFB takeoff-performance figure (e.g. 4.6) and the walk
        // drives ELEV_TRIM_UP/DN until efcs.pitch_trim reads it back. No CurrentValueSourceKey
        // — the only var that could fill it is the carrier's animation ratio, and prefilling
        // the box with a number that is not in these units would be worse than an empty box.
        v["A22X_STAB_TRIM_SET"] = new SimVarDefinition
        {
            Name = "A22X_STAB_TRIM_SET", DisplayName = "Set Stabilizer Trim (units nose up)",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.Never
        };
        v["A22X_RUDDER_TRIM_POS"] = Status("RUDDER TRIM PCT", "Rudder Trim", "percent over 100", "F2", SimVarType.SimVar);

        // ==== Main panel — Gear / Brakes =====================================
        v["A22X_GEAR_LEVER"] = new SimVarDefinition
        {
            Name = "GEAR HANDLE POSITION", DisplayName = "Landing Gear Lever",
            Type = SimVarType.SimVar, Units = "percent over 100", UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = Labels("Up", "Down")
        };
        v["A22X_ALTERNATE_GEAR"] = LSwitch("A22X Alternate Gear", "Alternate Gear Extension (guarded)", "Normal", "Pulled");
        // Selector value labels need in-sim verification (L:A22X Autobrake is documented
        // as "current autobrake selector setting" without an enum table).
        v["A22X_AUTOBRAKE"] = LSwitch("A22X Autobrake", "Autobrake", "Off", "LO", "MED", "HI");
        v["A22X_NOSE_STEER_OFF"] = LSwitch("A22X Nose Steer Off", "Nose Wheel Steering", "On", "Off");
        v["A22X_ALTERNATE_BRAKE"] = LSwitch("A22X Alternate Brake", "Alternate Brakes", "Normal", "Alternate");
        v["A22X_GEAR_AURAL_CANCEL"] = LMomentary("A22X Gear Aural", "Gear Aural Warning Cancel");

        // ==== Flaps / Spoilers ===============================================
        // The flap lever is the STOCK handle index, not an L:var. The cockpit lever
        // (FCTL_FLAPS_LEVER, Pedestal/FlightControls.xml) reads
        // `(A:FLAPS HANDLE INDEX, number)` and writes `<n> 5 / 16383 * (>K:FLAPS_SET)`,
        // so that SimVar IS the lever position, 0-5.
        //
        // simvars.mdx DOES document "L:A22X Flap Lever" and this def used to read it —
        // but it is dead, exactly like the documented-and-undriven "EXTERNAL POWER ON:1"
        // beside it: measured live 2026-09-22 it read 0 with the flaps at FULL. That was
        // not a cosmetic miss. It read 0 forever, so the old inc/dec walk saw "0" against
        // a target of 2, fired FLAPS_INCR, re-read 0, and fired again for all ten rounds —
        // driving the flaps to FULL and reporting "Flaps did not reach 2, lever at 0".
        // The same dead 0 also made the approach callout's `< 4` landing-flap check
        // permanently claim "flaps not landing flap".
        // Continuous so detent changes announce ("Flaps: 3" — the PM flap callout).
        v["A22X_FLAP_LEVER"] = new SimVarDefinition
        {
            Name = "FLAPS HANDLE INDEX", DisplayName = "Flaps",
            Type = SimVarType.SimVar, Units = "number", UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = Labels("0", "1", "2", "3", "4", "5 (Full)")
        };
        v["A22X_ALTERNATE_FLAP"] = LSwitch("A22X Alternate Flap", "Alternate Flap Mode", "Normal", "Alternate");
        v["A22X_SPOILERS_RETRACT"] = new SimVarDefinition
        {
            Name = "SPOILERS_OFF", DisplayName = "Speedbrake Retract",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest, RenderAsButton = true
        };
        v["A22X_SPOILERS_DEPLOY"] = new SimVarDefinition
        {
            Name = "SPOILERS_ON", DisplayName = "Speedbrake Full",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest, RenderAsButton = true
        };
        v["A22X_SPOILER_LEVER_POS"] = Status("A22X Spoiler Lever", "Speedbrake Lever", "number", "F2");

        // ==== Glareshield — CTP (per side) ===================================
        // Baro STD / nav source / crosstune are momentary press flags the panel
        // consumes; unit (hPa/inHg) is read for display.
        v["A22X_L_BARO_STD"] = LMomentary("A22X L Altimeter STD", "Captain Baro STD");
        v["A22X_R_BARO_STD"] = LMomentary("A22X R Altimeter STD", "First Officer Baro STD");
        v["A22X_L_NAV_SOURCE"] = LMomentary("A22X L Nav Source", "Captain Nav Source Cycle");
        v["A22X_R_NAV_SOURCE"] = LMomentary("A22X R Nav Source", "First Officer Nav Source Cycle");
        v["A22X_L_CROSSTUNE"] = LMomentary("A22X L CTP Crosstune", "Captain Crosstune");
        v["A22X_R_CROSSTUNE"] = LMomentary("A22X R CTP Crosstune", "First Officer Crosstune");
        AddQuiet(v, "A22X_L_BARO_HPA", "A22X L Altimeter HPA", "bool");
        AddQuiet(v, "A22X_R_BARO_HPA", "A22X R Altimeter HPA", "bool");

        // ==== Glareshield — Warnings / chrono ================================
        v["A22X_MASTER_CW_CANCEL"] = new SimVarDefinition
        {
            // Settable to acknowledge — the button write is claimed in HandleUIVariableSet
            // (writes 0 to clear, via the calc path).
            Name = "A22X Master Caution Warning", DisplayName = "Master Warning/Caution Cancel",
            Type = SimVarType.LVar, Units = "bool", UpdateFrequency = UpdateFrequency.OnRequest,
            RenderAsButton = true, SuppressRestingButtonState = true,
            ValueDescriptions = Labels("Clear", "Active")
        };
        v["A22X_L_CHRONO"] = LMomentary("A22X L Chrono", "Captain Chrono");
        v["A22X_R_CHRONO"] = LMomentary("A22X R Chrono", "First Officer Chrono");
        // Self-handled: "Master Caution"/"Master Warning" (+ clear) on the edge.
        foreach (var (key, name) in new (string, string)[]
        {
            ("A22X_CAUTION_PBA", "A22X Caution PBA"),
            ("A22X_WARNING_PBA", "A22X Warning PBA"),
        })
        {
            v[key] = new SimVarDefinition
            {
                Name = name, DisplayName = name, Type = SimVarType.LVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ExcludeFromMonitorManager = true
            };
        }

        // ==== FCP annunciators (the P1 stopgap mode monitor) =================
        // Self-announced from ProcessSimVarUpdate with composed phrases — the real
        // FMA vocabulary needs the PFD scrape (P3). IsAnnounced=true is required
        // for the vars to be monitored at all.
        foreach (var (key, name) in new (string, string)[]
        {
            ("A22X_AP_MASTER", "A22X AP Master"),
            ("A22X_AT_MASTER", "A22X AT Master"),
            ("A22X_FG_HEADING", "A22X FG Heading"),
            ("A22X_FG_LNAV", "A22X FG LNAV"),
            ("A22X_FG_APPROACH", "A22X FG Approach"),
            ("A22X_FG_FLC", "A22X FG Flight Level"),
            ("A22X_FG_ALT", "A22X FG Altitude"),
            ("A22X_FG_VNAV", "A22X FG VNAV"),
            ("A22X_FG_VS", "A22X FG Vertical Speed"),
            ("A22X_FG_FPA", "A22X FG Flight Path Angle"),
            ("A22X_FG_HALF_BANK", "A22X FG Half Bank"),
            ("A22X_FG_EDM", "A22X FG EDM"),
        })
        {
            v[key] = new SimVarDefinition
            {
                Name = name, DisplayName = name, Type = SimVarType.LVar, Units = "bool",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ExcludeFromMonitorManager = false
            };
        }
        // FD BUTTONS, not state combos. "A22X L/R Flight Director" is bound to
        // A220_ButtonMomentary in Interior/Glareshield/Autopilot.xml — it is the button's
        // PRESS pulse, so it reads 0 whatever the flight director is doing, and a level
        // write to it ("set 1 = on") is meaningless. Pressing = a real press (the
        // RenderAsButton catch-all in HandleUIVariableSet pulses 1 then 0); the STATE is
        // read from the FG over the CommBus and shown by the two _STATE display rows.
        v["A22X_L_FD"] = LMomentary("A22X L Flight Director", "Left Flight Director (press)");
        v["A22X_R_FD"] = LMomentary("A22X R Flight Director", "Right Flight Director (press)");
        // Display-only carriers. The value read here is only a fallback: the real state
        // comes from "A22X.Autoflight Data" l_fd/r_fd via TryGetDisplayOverride. These
        // stock SimVars are what the msfs-sdk layer in the aircraft's own bundle maps FD
        // state to, so they are the best non-Coherent guess — UNVERIFIED in the sim, and
        // deliberately rendered as "Unknown" rather than "Off" when the link is down.
        v["A22X_L_FD_STATE"] = Status("AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", "Left FD", "bool", "F0", SimVarType.SimVar);
        v["A22X_R_FD_STATE"] = Status("AUTOPILOT FLIGHT DIRECTOR ACTIVE:2", "Right FD", "bool", "F0", SimVarType.SimVar);
        AddQuiet(v, "A22X_SELECTED_FPA", "A22X Selected FPA", "degrees");
        AddQuiet(v, "A22X_FG_SPEED_MODE", "A22X FG Speed Mode", "number");
        AddQuiet(v, "A22X_FG_ALT_UNIT", "A22X FG Altitude Unit", "number");
        AddQuiet(v, "A22X_FG_ALT_FINE", "A22X FG Altitude Fine", "bool");

        // Indicated airspeed for the display pump's VR-crossing "Rotate" callout
        // (compared against the PFD-scraped VR; see SynapticA220Definition.Displays.cs).
        AddQuiet(v, "A22X_IAS", "AIRSPEED INDICATED", "knots", SimVarType.SimVar);

        // Stock autoflight targets for readouts and dialog walks.
        AddQuiet(v, "A22X_AP_SPD", "AUTOPILOT AIRSPEED HOLD VAR", "knots", SimVarType.SimVar);
        AddQuiet(v, "A22X_AP_MACH", "AUTOPILOT MACH HOLD VAR", "number", SimVarType.SimVar);
        AddQuiet(v, "A22X_AP_HDG", "AUTOPILOT HEADING LOCK DIR", "degrees", SimVarType.SimVar);
        AddQuiet(v, "A22X_AP_ALT", "AUTOPILOT ALTITUDE LOCK VAR", "feet", SimVarType.SimVar);
        AddQuiet(v, "A22X_AP_VS", "AUTOPILOT VERTICAL HOLD VAR", "feet per minute", SimVarType.SimVar);
        AddQuiet(v, "A22X_AP_MACH_MODE", "AUTOPILOT MANAGED SPEED IN MACH", "bool", SimVarType.SimVar);
        AddQuiet(v, "A22X_KOHLSMAN", "KOHLSMAN SETTING HG:1", "inHg", SimVarType.SimVar);
        AddQuiet(v, "A22X_KOHLSMAN_MB", "KOHLSMAN SETTING MB:1", "millibars", SimVarType.SimVar);
        // EXEC prompt. This is the var that drives the PHYSICAL MKP EXEC key's light
        // (MKP.xml: the EXEC button's SEQ2_CODE is `(L:A22X Flight Plan Modified)`),
        // so it is exactly "a modification is pending and needs EXEC". It is
        // ANNOUNCED, not AddQuiet: a pending mod that nobody tells you about is
        // invisible to a blind pilot — there is no other cue, since the A220 shows
        // it as a lamp on the keypad plus a MOD flag on the display, and the FMS
        // form only polls while it is open. Self-announced on the rising edge in
        // ProcessSimVarUpdate; left in the Ctrl+M monitor manager so it can be
        // turned off (hence NO ExcludeFromMonitorManager).
        v["A22X_FPLN_MODIFIED"] = new SimVarDefinition
        {
            Name = "A22X Flight Plan Modified",
            DisplayName = "Flight plan modified (EXEC prompt)",
            Type = SimVarType.LVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true
        };

        // ==== Engines (readouts + reverse callout source) ====================
        v["A22X_ENG1_N1"] = Status("A22X Engine 1 N1", "Engine 1 N1 percent", "number", "F1");
        v["A22X_ENG2_N1"] = Status("A22X Engine 2 N1", "Engine 2 N1 percent", "number", "F1");
        v["A22X_ENG1_N2"] = Status("A22X Engine 1 N2", "Engine 1 N2 percent", "number", "F1");
        v["A22X_ENG2_N2"] = Status("A22X Engine 2 N2", "Engine 2 N2 percent", "number", "F1");
        v["A22X_ENG1_EGT"] = Status("GENERAL ENG EXHAUST GAS TEMPERATURE:1", "Engine 1 EGT", "celsius", "F0", SimVarType.SimVar);
        v["A22X_ENG2_EGT"] = Status("GENERAL ENG EXHAUST GAS TEMPERATURE:2", "Engine 2 EGT", "celsius", "F0", SimVarType.SimVar);
        // Self-handled: "Reverse green"/"No reverse" rollout callouts (plan W4).
        foreach (var (key, name) in new (string, string)[]
        {
            ("A22X_ENG1_REVERSER", "A22X Engine 1 Reverser"),
            ("A22X_ENG2_REVERSER", "A22X Engine 2 Reverser"),
        })
        {
            v[key] = new SimVarDefinition
            {
                Name = name, DisplayName = name, Type = SimVarType.LVar, Units = "percent over 100",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
                ExcludeFromMonitorManager = true
            };
        }

        // ==== Electrical bus voltages (display panel) ========================
        v["A22X_AC_BUS_1_V"] = Status("A22X AC Bus 1 Voltage", "AC Bus 1", "volts", "F0");
        v["A22X_AC_BUS_2_V"] = Status("A22X AC Bus 2 Voltage", "AC Bus 2", "volts", "F0");
        v["A22X_AC_ESS_V"] = Status("A22X AC Ess Bus Voltage", "AC Ess Bus", "volts", "F0");
        v["A22X_DC_BUS_1_V"] = Status("A22X DC Bus 1 Voltage", "DC Bus 1", "volts", "F0");
        v["A22X_DC_BUS_2_V"] = Status("A22X DC Bus 2 Voltage", "DC Bus 2", "volts", "F0");
        v["A22X_DC_EMER_V"] = Status("A22X DC Emer Bus Voltage", "DC Emergency Bus", "volts", "F0");

        // ==== Flight stage (phase context; 0-based 0=Hangar…7=Final — the EFB's
        // start-state buttons write 0-3 to this var; see CurrentFlightPhase) ====
        v["A22X_FLIGHT_STAGE"] = new SimVarDefinition
        {
            Name = "A22X Flight Stage", DisplayName = "Flight Stage",
            Type = SimVarType.LVar, Units = "number", UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true, ExcludeFromMonitorManager = false
        };

        // ==== Radios / transponder (W8) ======================================
        AddQuiet(v, "A22X_COM1_ACTIVE", "COM ACTIVE FREQUENCY:1", "MHz", SimVarType.SimVar);
        AddQuiet(v, "A22X_COM1_STANDBY", "COM STANDBY FREQUENCY:1", "MHz", SimVarType.SimVar);
        AddQuiet(v, "A22X_COM2_ACTIVE", "COM ACTIVE FREQUENCY:2", "MHz", SimVarType.SimVar);
        AddQuiet(v, "A22X_COM2_STANDBY", "COM STANDBY FREQUENCY:2", "MHz", SimVarType.SimVar);
        AddQuiet(v, "A22X_NAV1_ACTIVE", "NAV ACTIVE FREQUENCY:1", "MHz", SimVarType.SimVar);
        AddQuiet(v, "A22X_NAV1_STANDBY", "NAV STANDBY FREQUENCY:1", "MHz", SimVarType.SimVar);
        AddQuiet(v, "A22X_NAV2_ACTIVE", "NAV ACTIVE FREQUENCY:2", "MHz", SimVarType.SimVar);
        AddQuiet(v, "A22X_NAV2_STANDBY", "NAV STANDBY FREQUENCY:2", "MHz", SimVarType.SimVar);
        AddQuiet(v, "A22X_XPDR_CODE", "TRANSPONDER CODE:1", "number", SimVarType.SimVar);

        v["A22X_COM1_STBY_SET"] = new SimVarDefinition
        {
            Name = "A22X_COM1_STBY_SET", DisplayName = "COM 1 Standby Frequency",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.Never,
            CurrentValueSourceKey = "A22X_COM1_STANDBY"
        };
        v["A22X_COM2_STBY_SET"] = new SimVarDefinition
        {
            Name = "A22X_COM2_STBY_SET", DisplayName = "COM 2 Standby Frequency",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.Never,
            CurrentValueSourceKey = "A22X_COM2_STANDBY"
        };
        // NAV-to-NAV transfer protection (pilot guide / A220-FOT-22-30-012): the FMS
        // autotunes approach frequencies; MSFSBA only ever sets NAV STANDBY, never
        // active, and never auto-swaps NAV.
        v["A22X_NAV1_STBY_SET"] = new SimVarDefinition
        {
            Name = "A22X_NAV1_STBY_SET", DisplayName = "NAV 1 Standby Frequency",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.Never,
            CurrentValueSourceKey = "A22X_NAV1_STANDBY"
        };
        v["A22X_NAV2_STBY_SET"] = new SimVarDefinition
        {
            Name = "A22X_NAV2_STBY_SET", DisplayName = "NAV 2 Standby Frequency",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.Never,
            CurrentValueSourceKey = "A22X_NAV2_STANDBY"
        };
        v["A22X_COM1_SWAP"] = new SimVarDefinition
        {
            Name = "COM_STBY_RADIO_SWAP", DisplayName = "COM 1 Swap",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest, RenderAsButton = true
        };
        v["A22X_COM2_SWAP"] = new SimVarDefinition
        {
            Name = "COM2_RADIO_SWAP", DisplayName = "COM 2 Swap",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest, RenderAsButton = true
        };
        v["TRANSPONDER_CODE_SET"] = new SimVarDefinition
        {
            // MainForm's built-in _SET special case handles the BCD encoding + XPNDR_SET.
            Name = "XPNDR_SET", DisplayName = "Transponder Code",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.Never,
            CurrentValueSourceKey = "A22X_XPDR_CODE"
        };
        v["A22X_XPDR_IDENT"] = new SimVarDefinition
        {
            Name = "XPNDR_IDENT_ON", DisplayName = "Transponder Ident",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest, RenderAsButton = true
        };

        // ==== TOD pause (plain writable L:vars — cheap win) ==================
        v["A22X_TOD_PAUSE"] = new SimVarDefinition
        {
            Name = "INI_PAUSE_AT_TOD_ENABLED", DisplayName = "Pause at Top of Descent",
            Type = SimVarType.LVar, Units = "bool", UpdateFrequency = UpdateFrequency.OnRequest,
            ValueDescriptions = Labels("Off", "On")
        };
        v["A22X_TOD_DISTANCE_SET"] = new SimVarDefinition
        {
            Name = "A22X_TOD_DISTANCE_SET", DisplayName = "Pause Distance Before TOD (nm)",
            Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.Never,
            CurrentValueSourceKey = "A22X_TOD_DISTANCE"
        };
        AddQuiet(v, "A22X_TOD_DISTANCE", "INI_PAUSE_AT_TOD_DISTANCE", "number");

        // ==== Approach-callout + waypoint readout sources (W4) ================
        AddQuiet(v, "A22X_RA", "RADIO HEIGHT", "feet", SimVarType.SimVar);
        AddQuiet(v, "A22X_VSI", "VERTICAL SPEED", "feet per minute", SimVarType.SimVar);
        AddQuiet(v, "A22X_PITCH", "PLANE PITCH DEGREES", "degrees", SimVarType.SimVar);
        AddQuiet(v, "A22X_BANK", "PLANE BANK DEGREES", "degrees", SimVarType.SimVar);
        AddQuiet(v, "A22X_NAV1_CDI", "NAV CDI:1", "number", SimVarType.SimVar);
        AddQuiet(v, "A22X_NAV1_HAS_LOC", "NAV HAS LOCALIZER:1", "bool", SimVarType.SimVar);
        AddQuiet(v, "A22X_GEAR_EXT", "GEAR TOTAL PCT EXTENDED", "percent over 100", SimVarType.SimVar);
        AddQuiet(v, "A22X_GPS_WP_DIST", "GPS WP DISTANCE", "nautical miles", SimVarType.SimVar);
        AddQuiet(v, "A22X_GPS_WP_BRG", "GPS WP BEARING", "degrees", SimVarType.SimVar);

        // ==== Annunciator lamps: batch monitor (baseline-first, generic path) ==
        foreach (var doc in A220.SynapticA220SimVarData.Vars)
        {
            if (!doc.IsLamp) continue;
            string bare = doc.Name.StartsWith("L:") ? doc.Name.Substring(2) : doc.Name;
            string key = "LAMP_" + bare.Replace("A22X ", "").Replace(' ', '_').ToUpperInvariant();
            v[key] = Lamp(bare, FriendlyLampName(bare));
        }

        return v;
    }

    /// <summary>"A22X L Gen Fail Lamp" → "Left Generator Fail light".</summary>
    private static string FriendlyLampName(string bare)
    {
        string s = bare.StartsWith("A22X ") ? bare.Substring(5) : bare;
        if (s.EndsWith(" Lamp")) s = s.Substring(0, s.Length - 5);
        s = s.Replace("L Gen", "Left Generator").Replace("R Gen", "Right Generator")
             .Replace("L Bleed", "Left Bleed").Replace("R Bleed", "Right Bleed")
             .Replace("L Pack", "Left Pack").Replace("R Pack", "Right Pack")
             .Replace("L Side Window", "Left Side Window").Replace("R Side Window", "Right Side Window")
             .Replace("L Windshield", "Left Windshield").Replace("R Windshield", "Right Windshield");
        return s + " light";
    }
}
