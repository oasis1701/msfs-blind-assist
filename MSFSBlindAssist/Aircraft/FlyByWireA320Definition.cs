using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

public class FlyByWireA320Definition : BaseAircraftDefinition,
    ISupportsECAM,
    ISupportsNavigationDisplay,
    ISupportsPFDDisplay
{
    // FCU request tracking - stores pending value and status for each parameter
    private double? pendingHeadingValue = null;
    private double? pendingHeadingStatus = null;
    private double? pendingSpeedValue = null;
    private double? pendingSpeedStatus = null;
    private double? pendingAltitudeValue = null;
    private double? pendingAltitudeStatus = null;
    private double? pendingVSFPAValue = null;
    private double? pendingVSFPAMode = null;

    // Boolean flags to track active FCU readout requests
    private bool isRequestingHeading = false;
    private bool isRequestingSpeed = false;
    private bool isRequestingAltitude = false;
    private bool isRequestingVSFPA = false;

    // Flight phase tracking
    private string currentFlightPhase = "";
    public new string CurrentFlightPhase => currentFlightPhase;

    public override string AircraftName => "FlyByWire Airbus A320neo";
    public override string AircraftCode => "A320";

    // Coherent GT view title-needle hosting the MCDU instrument, used by the
    // D / Shift+D FMS flight-info eval (CoherentEvalClient → coherent-a32nx-flightinfo.js).
    // Overridden by the Headwind A330 fork, whose view is "A339X_MCDU".
    public virtual string FlightInfoMcduView => "A32NX_MCDU";

    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // Visual-guidance profile — FlyByWire A320. Declared explicitly (not inherited from the
    // base default) so the math is unambiguously keyed to this airframe and the glidepath
    // biases can be calibrated for FBW independently of Fenix. Approach AoA / Vref / rate
    // caps are the historically-tuned A320 numbers; the glidepath biases (GlideslopeAltitude
    // / FlareAltitude) are estimates pending an in-sim coupled-ILS-autoland check.
    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg     = 6.0,
        ReferenceVrefKnots        = 140.0,
        MaxPitchRateDegPerSec     = 2.5,
        MaxBankRateDegPerSec      = 3.0,
        GlideslopeAltitudeBiasFt  = 60.0,   // estimate — calibrate vs a coupled ILS autoland
        FlareAltitudeBiasFt       = 12.0,   // estimate
        FlareTriggerWheelHeightFt = 30.0,   // A320 FCTM: flare initiation at 30 ft RA
        FlareTargetPitchDeg       = 6.0     // A320 FCTM: flare attitude ~+5–6°
    };

    // Cached variable-definition dictionary. The definitions are static, but this method
    // rebuilds the whole dict (huge literal + the SD auto-register loops); the panel-build
    // loop calls GetVariables() twice per control, so without this cache a panel switch
    // rebuilt it dozens of times — the subpanel-navigation lag. Built once, then reused.
    private Dictionary<string, SimConnect.SimVarDefinition>? _cachedVariables;
    // Helper for fault annunciators: auto-announce-only (Continuous + IsAnnounced),
    // Normal/Fault, not placed in any panel list (faults aren't navigable controls —
    // mirrors the A380 ReadEnum-fault pattern; surfaced via change-announce + Ctrl+M).
    private static SimConnect.SimVarDefinition Fault(string name, string display) =>
        new SimConnect.SimVarDefinition
        {
            Name = name, DisplayName = display,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" }
        };

    // MEASURED 2026-06-10 (open-loop, old no-lead tone; 13 turn rollouts,
    // KATL/KIAH/KPHX, A20N): required lead median 0.95 s, IQR 0.52–1.94 s
    // → initial value 1.3. CLOSED-LOOP REVALIDATION 2026-06-11 (KSFO, A20N,
    // rate-lead active at 1.3): both clean turns rolled out ~15° LONG —
    // with the cue available the pilot now WAITS for tone-centre before
    // unwinding (unlike the Boeings, where ingrained self-anticipation made
    // the same leads come out SHORT), exposing the full reaction+inertia
    // chain. Raw correction suggested +1.6–1.9 s; stepped conservatively to
    // 1.6 (n=2). Re-fly to converge.
    public override double TaxiTurnLeadSeconds => 1.6;

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        if (_cachedVariables != null) return _cachedVariables;
        // Start with common base variables (e.g., SIM ON GROUND)
        var variables = GetBaseVariables();

        // Add aircraft-specific variables
        var aircraftVariables = new Dictionary<string, SimConnect.SimVarDefinition>
        {
// ELEC Panel Display Variables
        ["A32NX_ELEC_BAT_1_POTENTIAL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELEC_BAT_1_POTENTIAL",
            DisplayName = "Battery 1 Voltage",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            Units = "volts"
        },
        ["A32NX_ELEC_BAT_2_POTENTIAL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELEC_BAT_2_POTENTIAL",
            DisplayName = "Battery 2 Voltage",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            Units = "volts"
        },

        // OVERHEAD FORWARD SECTION
        // ELEC Panel
        ["A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
            DisplayName = "Battery One",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
            DisplayName = "Battery Two",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON",
            DisplayName = "EXT Power",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["EXT_PWR_AVAILABLE"] = new SimConnect.SimVarDefinition
        {
            Name = "EXTERNAL POWER AVAILABLE:1", DisplayName = "External Power Available",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            RenderAsReadOnlyStatus = true, // ground-power state, not a control
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not available", [1] = "Available" }
        },
        // End-to-end MobiFlight probe target: MainForm calc-writes a nonce here and
        // reads it back via the data-def path — the only reliable "calc path alive"
        // signal (response-based detection is invalid: a healthy install can execute
        // every command yet never send a single response — live-proven 2026-06-11).
        // Not in any panel; OnRequest; never announced.
        ["MSFSBA_BRIDGE_PROBE"] = new SimConnect.SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE", DisplayName = "Bridge Probe",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        // ---- ELEC: generators. The FBW Rust system reads the STOCK simvars (copied
        // to aspects each tick — a320_systems_wasm lib.rs); the _PB_IS_ON L:vars are
        // dead mirrors. State = stock simvar, set = toggle event when desired !=
        // current (HandleUIVariableSet branch).
        ["A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "GENERAL ENG MASTER ALTERNATOR:1", DisplayName = "Generator 1",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_ENG_GEN_2_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "GENERAL ENG MASTER ALTERNATOR:2", DisplayName = "Generator 2",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "APU GENERATOR SWITCH", DisplayName = "APU Generator",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO", DisplayName = "Bus Tie",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL", DisplayName = "AC ESS Feed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Altn", [1] = "Normal" }
        },
        ["A32NX_OVHD_ELEC_IDG_1_PB_IS_RELEASED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_IDG_1_PB_IS_RELEASED", DisplayName = "IDG 1 Disconnect",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Disconnected" }
        },
        ["A32NX_OVHD_ELEC_IDG_2_PB_IS_RELEASED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_IDG_2_PB_IS_RELEASED", DisplayName = "IDG 2 Disconnect",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Disconnected" }
        },
        ["A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON", DisplayName = "Commercial",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ELEC Monitoring Variables (continuous monitoring with auto-announcement)
        ["A32NX_ELEC_AC_ESS_BUS_IS_POWERED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELEC_AC_ESS_BUS_IS_POWERED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Electrical systems shutting down",
                [1] = "ND, PFD, E W D, SD display message: SELF TEST IN PROGRESS | (MAX 40 SECONDS)"
            }
        },

        // ADIRS Panel
        ["A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
            DisplayName = "ADIRS 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
        },
        ["A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
            DisplayName = "ADIRS 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
        },
        ["A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB",
            DisplayName = "ADIRS 3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "NAV", [2] = "ATT" }
        },
        // ---- ADIRS parity with A380: IR + ADR pushbuttons (verified live) ----
        ["A32NX_OVHD_ADIRS_IR_1_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_1_PB_IS_ON", DisplayName = "IR 1",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ADIRS_IR_2_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_2_PB_IS_ON", DisplayName = "IR 2",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ADIRS_IR_3_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_IR_3_PB_IS_ON", DisplayName = "IR 3",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ADIRS_ADR_1_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_ADR_1_PB_IS_ON", DisplayName = "ADR 1",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ADIRS_ADR_2_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_ADR_2_PB_IS_ON", DisplayName = "ADR 2",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ADIRS_ADR_3_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ADIRS_ADR_3_PB_IS_ON", DisplayName = "ADR 3",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ADIRS Panel Display Variables
        ["A32NX_ADIRS_ADIRU_1_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADIRU_1_STATE",
            DisplayName = "IRS 1 Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
        },
        ["A32NX_ADIRS_ADIRU_2_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADIRU_2_STATE",
            DisplayName = "IRS 2 Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
        },
        ["A32NX_ADIRS_ADIRU_3_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADIRU_3_STATE",
            DisplayName = "IRS 3 Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Only on refresh button
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Aligning", [2] = "Aligned" }
        },

        // Signs Panel
        ["CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE",
            DisplayName = "Seat Belts Sign",
            Type = SimConnect.SimVarType.Event
        },
        ["CABIN SEATBELTS ALERT SWITCH"] = new SimConnect.SimVarDefinition
        {
            Name = "CABIN SEATBELTS ALERT SWITCH",
            DisplayName = "Seat Belts Sign State",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION",
            DisplayName = "No Smoking Signs",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Auto", [2] = "Off" }
        },
        ["XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION",
            DisplayName = "Emergency Exit Lights",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Arm", [2] = "Off" }
        },

        // APU Panel
        ["A32NX_OVHD_APU_MASTER_SW_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
            DisplayName = "APU Master Switch",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_APU_START_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_APU_START_PB_IS_ON",
            DisplayName = "APU Start",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" },
            // COMBO, not a button: APU Start is an on/off STATE pushbutton. As a button its
            // label never updated (press did X but the second press appeared to do nothing);
            // a combo always shows the current Off/On and toggles correctly. (User request 2026-06.)
            RenderAsButton = false
        },

        // Exterior Lighting Panel
        ["LIGHTING_LANDING_1"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHTING_LANDING_1",
            DisplayName = "Nose Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "T.O.", [1] = "Taxi", [2] = "Off" }
        },
        ["LIGHTING_LANDING_2"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHTING_LANDING_2",
            DisplayName = "Left Landing Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off", [2] = "Retract" }
        },
        ["LIGHTING_LANDING_3"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHTING_LANDING_3",
            DisplayName = "Right Landing Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off", [2] = "Retract" }
        },
        ["LIGHTING_STROBE_0"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHTING_STROBE_0",
            DisplayName = "Strobe Lights",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ReverseDisplayOrder = true,
            ValueDescriptions = new Dictionary<double, string> { [2] = "Off", [0] = "On", [1] = "Auto" }
        },

        // Supporting Strobe Light Variables
        ["STROBE_0_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "STROBE_0_AUTO",
            DisplayName = "Strobe Auto Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        // Strobe Light Events
        ["STROBES_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "STROBES_OFF",
            DisplayName = "Strobes Off",
            Type = SimConnect.SimVarType.Event
        },
        ["STROBES_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "STROBES_ON",
            DisplayName = "Strobes On",
            Type = SimConnect.SimVarType.Event
        },

        // Light Control Events
        ["BEACON_LIGHTS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "BEACON_LIGHTS_SET",
            DisplayName = "Beacon Lights Set Event",
            Type = SimConnect.SimVarType.Event
        },
        ["WING_LIGHTS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "WING_LIGHTS_SET",
            DisplayName = "Wing Lights Set Event",
            Type = SimConnect.SimVarType.Event
        },
        // NAV/LOGO stock single-param events removed: dead on the FBW A32NX. The combined
        // Nav & Logo switch (A32NX_LIGHTS_NAV_LOGO) uses the indexed K:2:NAV_LIGHTS_SET /
        // K:2:LOGO_LIGHTS_SET calc RPN in HandleUIVariableSet instead.
        // Runway turn-off lights. The real A320 has ONE RWY TURN OFF switch
        // (SWITCH_OVHD_EXTLT_RWY, A320_NEO_INTERIOR.xml:1880-1895). It reads state from
        // LIGHT TAXI:2 (left) and LIGHT TAXI:3 (right) and fires the indexed K:2:TAXI_LIGHTS_SET
        // RPN: "INDEX VALUE (>K:2:TAXI_LIGHTS_SET)" (from FBW_Switch_LeftClick_MouseWheel template,
        // Airbus.xml:250-255). MSFSBA exposes a single combo (this "LIGHT TAXI:2" entry, displayed
        // as "Runway Turn Off Lights") whose set drives BOTH sides via that same RPN — see the
        // "LIGHT TAXI:2" branch in MainForm's lighting block. LIGHT TAXI:2 is the representative
        // read-back. The "LIGHT TAXI:3" entry below is kept as a read-only state source so the
        // combined branch can re-read the right side after firing. Using LIGHT TAXI:2/3 (not
        // CIRCUIT SWITCH ON:21/22) keeps MSFSBA in sync with the cockpit switch, FBW presets,
        // and the EFB, which all drive LIGHT TAXI:2/3 via TAXI_LIGHTS_SET and never the circuits.
        ["LIGHT TAXI:2"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT TAXI:2",
            DisplayName = "Runway Turn Off Lights",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["LIGHT TAXI:3"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT TAXI:3",
            DisplayName = "Right RWY Turn Off Light (state only)",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Light State SimVars (for monitoring)
        ["LIGHT BEACON"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT BEACON",
            DisplayName = "Beacon Light",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["LIGHT WING"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT WING",
            DisplayName = "Wing Lights",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // NAV and LOGO are a single combined switch on the A320 (see A32NX_LIGHTS_NAV_LOGO
        // below). The old separate "LIGHT NAV" / "LIGHT LOGO" combos drove dead stock events
        // and were removed.
        // Combined NAV & LOGO switch (the real A320 has ONE switch, FBW models it as the
        // FBW_A32NX_NAV_LOGO_LT_SW with state L:A32NX_LIGHTS_NAV_LOGO = OFF(0)/SYS1(1)/SYS2(2)).
        // The old separate "Nav"/"Logo" combos fired stock NAV_LIGHTS_SET/LOGO_LIGHTS_SET which
        // are DEAD on FBW (live-verified). The set is handled in HandleUIVariableSet by replaying
        // the FBW switch RPN. State reads the switch L-var (0=Off, 2=On). See HandleUIVariableSet.
        ["A32NX_LIGHTS_NAV_LOGO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_LIGHTS_NAV_LOGO",
            DisplayName = "Nav and Logo Lights",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [2] = "On" }
        },

        // Light Events
        ["BEACON_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "BEACON_OFF",
            DisplayName = "Beacon Off",
            Type = SimConnect.SimVarType.Event
        },
        ["BEACON_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "BEACON_ON",
            DisplayName = "Beacon On",
            Type = SimConnect.SimVarType.Event
        },

        // Oxygen Panel
        ["PUSH_OVHD_OXYGEN_CREW"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_OXYGEN_CREW",
            DisplayName = "Crew Supply",
            Type = SimConnect.SimVarType.LVar,
            ReverseDisplayOrder = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off" }
        },
        ["A32NX_OXYGEN_MASKS_DEPLOYED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OXYGEN_MASKS_DEPLOYED",
            DisplayName = "Passenger Masks Deployed",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Deployed" }
        },
        ["A32NX_OXYGEN_PASSENGER_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OXYGEN_PASSENGER_LIGHT_ON",
            DisplayName = "Passenger Oxygen Light",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Fire Panel
        // Handles are Continuous+IsAnnounced: the agent-discharge interlock in
        // HandleUIVariableSet reads their CACHED state, which must stay live even
        // when the handle was pulled via this panel's own combo (a calc write does
        // not refresh the cache) or from the cockpit. Also announces handle pulls —
        // a safety-relevant state change in its own right.
        ["A32NX_FIRE_BUTTON_ENG1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_BUTTON_ENG1",
            DisplayName = "Eng 1 Fire Handle",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Pulled" }
        },
        ["A32NX_FIRE_BUTTON_ENG2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_BUTTON_ENG2",
            DisplayName = "Eng 2 Fire Handle",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Pulled" }
        },
        ["A32NX_FIRE_BUTTON_APU"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_BUTTON_APU",
            DisplayName = "APU Fire Handle",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Pulled" }
        },
        ["A32NX_FIRE_TEST_ENG1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_TEST_ENG1",
            DisplayName = "Eng 1 Fire Test",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Active" }
        },
        ["A32NX_FIRE_TEST_ENG2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_TEST_ENG2",
            DisplayName = "Eng 2 Fire Test",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Active" }
        },
        ["A32NX_FIRE_TEST_APU"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_TEST_APU",
            DisplayName = "APU Fire Test",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Active" }
        },
        ["A32NX_FIRE_ENG1_AGENT1_Discharge"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_ENG1_AGENT1_Discharge",
            DisplayName = "Eng 1 Agent 1",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Ready", [1] = "Discharged" }
        },
        ["A32NX_FIRE_ENG1_AGENT2_Discharge"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_ENG1_AGENT2_Discharge",
            DisplayName = "Eng 1 Agent 2",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Ready", [1] = "Discharged" }
        },
        ["A32NX_FIRE_ENG2_AGENT1_Discharge"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_ENG2_AGENT1_Discharge",
            DisplayName = "Eng 2 Agent 1",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Ready", [1] = "Discharged" }
        },
        ["A32NX_FIRE_ENG2_AGENT2_Discharge"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_ENG2_AGENT2_Discharge",
            DisplayName = "Eng 2 Agent 2",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Ready", [1] = "Discharged" }
        },
        ["A32NX_FIRE_APU_AGENT1_Discharge"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_APU_AGENT1_Discharge",
            DisplayName = "APU Agent",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Ready", [1] = "Discharged" }
        },

        // Hydraulic Panel
        ["A32NX_OVHD_HYD_ENG_1_PUMP_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_ENG_1_PUMP_PB_IS_AUTO",
            DisplayName = "Green Eng Pump",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_HYD_ENG_1_PUMP_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_ENG_1_PUMP_PB_HAS_FAULT",
            DisplayName = "Green Eng Pump Fault",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" }
        },
        // ENG 2 PUMP feeds the YELLOW circuit (Eng 1 = green, Eng 2 = yellow; blue has no
        // engine-driven pump). FBW binds A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO to "yellowPumpPBOn"
        // (Hyd.tsx). AutoOffFaultPushButton: 0 = Off, 1 = Auto.
        ["A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO",
            DisplayName = "Yellow Eng Pump",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_HYD_ENG_2_PUMP_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_ENG_2_PUMP_PB_HAS_FAULT",
            DisplayName = "Yellow Eng Pump Fault",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" }
        },
        ["A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO",
            DisplayName = "Blue Elec Pump",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_HYD_EPUMPB_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_EPUMPB_PB_HAS_FAULT",
            DisplayName = "Blue Elec Pump Fault",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" }
        },
        // YELLOW ELEC PUMP is the ONE pump on this panel built as an AutoOnFaultPushButton in the
        // FBW source (mod.rs: yellow_epump_push_button), not AutoOffFaultPushButton like the rest.
        // It shares the _PB_IS_AUTO var, but the non-auto state is ON, not OFF: is_on() = !is_auto,
        // and the controller pressurises the pump exactly when is_on() (mod.rs ~L3310). So 0 = On,
        // 1 = Auto (auto only pressurises for cargo-door ops). Do NOT relabel 0 as "Off" — that was
        // the bug where selecting "Off" actually switched the pump ON.
        ["A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO",
            DisplayName = "Yellow Elec Pump",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Auto" }
        },
        ["A32NX_OVHD_HYD_EPUMPY_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_EPUMPY_PB_HAS_FAULT",
            DisplayName = "Yellow Elec Pump Fault",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" }
        },
        ["A32NX_OVHD_HYD_PTU_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_PTU_PB_IS_AUTO",
            DisplayName = "PTU",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_HYD_PTU_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_PTU_PB_HAS_FAULT",
            DisplayName = "PTU Fault",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" }
        },

        // Cockpit Door Panel
        ["A32NX_COCKPIT_DOOR_LOCKED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_COCKPIT_DOOR_LOCKED",
            DisplayName = "Cockpit Door",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Unlocked", [1] = "Locked" }
        },
        ["A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE",
            DisplayName = "Cockpit Door Video",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ---- Cockpit furniture. The A32NX cockpit model exposes NO motorized seats /
        // sunshades / visors / tilting armrests like the A380 (those L:vars don't exist in the
        // FBW A320 source). The ONLY interactive furniture it models is these 4 BINARY toggles,
        // so they are simple combos (no motor/slider machinery — nothing for it to drive).
        // Settable via the A32NX_ calculator-path catch-all in HandleUIVariableSet; auto-
        // announced + Ctrl+M-mutable via the flip-to-monitored loop. NOTE: the 0/1 -> label
        // polarity below is NOT visually verified yet (the FBW template is a `!`-toggle with a
        // `100 *` anim); confirm in-sim and flip the ValueDescriptions if a state reads inverted.
        ["A32NX_ARMREST_CPT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ARMREST_CPT",
            DisplayName = "Captain Armrest",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Stowed", [1] = "Deployed" }
        },
        ["A32NX_ARMREST_FO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ARMREST_FO",
            DisplayName = "First Officer Armrest",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Stowed", [1] = "Deployed" }
        },
        ["A32NX_COCKPIT_SEAT_BACK"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_COCKPIT_SEAT_BACK",
            DisplayName = "Third Occupant Seat",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Stowed", [1] = "Slid out" }
        },
        ["A32NX_COCKPIT_SEAT_BACK_TOP"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_COCKPIT_SEAT_BACK_TOP",
            DisplayName = "Third Occupant Seat Backrest",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Folded", [1] = "Up" }
        },

        // Evacuation Panel
        ["A32NX_EVAC_COMMAND_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EVAC_COMMAND_TOGGLE",
            DisplayName = "Evacuation Command",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Cargo Smoke Panel
        ["A32NX_FIRE_TEST_CARGO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_TEST_CARGO",
            DisplayName = "Cargo Smoke Test",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Active" }
        },
        ["A32NX_CARGOSMOKE_FWD_DISCHARGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CARGOSMOKE_FWD_DISCHARGED",
            DisplayName = "Cargo Fwd Smoke Agent",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Ready", [1] = "Discharged" }
        },
        // Aft cargo-smoke bottle discharge status (FBW models 2 bottles — only FWD was
        // exposed; A32NX_CARGOSMOKE_AFT_DISCHARGED confirmed in a320-simvars.md). Mirrors
        // FWD; auto-announce + Ctrl+M via the panel-control loop.
        ["A32NX_CARGOSMOKE_AFT_DISCHARGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CARGOSMOKE_AFT_DISCHARGED",
            DisplayName = "Cargo Aft Smoke Agent",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Ready", [1] = "Discharged" }
        },
        // Wing anti-ice FAULT (read-only status; confirmed in a320-simvars.md). Continuous +
        // IsAnnounced so it speaks on change from any source and lists in Ctrl+M; shown
        // read-only in the Anti Ice status box.
        ["A32NX_PNEU_WING_ANTI_ICE_HAS_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PNEU_WING_ANTI_ICE_HAS_FAULT",
            DisplayName = "Wing Anti-Ice Fault",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" }
        },
        // Oxygen timer-reset FAULT (read-only status; confirmed in a320-simvars.md).
        // Continuous + IsAnnounced — announce-only (in Ctrl+M); no Oxygen status box exists.
        ["A32NX_OXYGEN_TMR_RESET_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OXYGEN_TMR_RESET_FAULT",
            DisplayName = "Oxygen Timer Fault",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Fault" }
        },

        // Engine Maintenance Panel
        ["A32NX_OVHD_FADEC_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_FADEC_1",
            DisplayName = "FADEC 1",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Powered" }
        },
        ["A32NX_OVHD_FADEC_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_FADEC_2",
            DisplayName = "FADEC 2",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Powered" }
        },
        // ENG MAN START pushbuttons removed: A32NX_ENGMANSTART{1,2}_TOGGLE is referenced
        // ONLY by the cockpit model XML (button animation + light) — NOTHING in FBW's
        // systems, instruments, or Rust reads it, so the write held but drove nothing
        // (the classic dead-var trap). Engine start on the A320 is the pedestal "Engines"
        // panel (ENG MODE selector + ENG MASTER), not the overhead. FADEC 1/2 above stay
        // (they power the FADEC, consumed by A32NX_FADEC.ts).

        // Fuel pumps — Off/On combo boxes (parity with the engine-master combos).
        // STATE = the commanded switch position FUELSYSTEM PUMP SWITCH:n (main pumps) /
        // FUELSYSTEM VALVE SWITCH:n (centre jet-pump valves) — the instant switch the
        // pilot sets; NOT the PUMP ACTIVE / VALVE OPEN running-state, which is a computed
        // output that would REVERT a set while unpowered. The SET fires the stock fuel-
        // system events in HandleUIVariableSet: FUELSYSTEM_PUMP_ON/_OFF (main pumps) or
        // FUELSYSTEM_VALVE_OPEN/_CLOSE (jet pumps — the exact path proven on the engine
        // masters). Continuous + announced so each speaks Off/On on change — this REPLACES
        // the old PUMP ACTIVE:n / VALVE OPEN:9,10 monitors (removed to avoid double-speak).
        // Pump indices: L1=2, L2=5, R1=3, R2=6; centre jet-pump valves C1=9, C2=10.
        ["FUEL_PUMP_L1"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP SWITCH:2", DisplayName = "Fuel Pump L1",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["FUEL_PUMP_L2"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP SWITCH:5", DisplayName = "Fuel Pump L2",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["FUEL_PUMP_R1"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP SWITCH:3", DisplayName = "Fuel Pump R1",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["FUEL_PUMP_R2"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP SWITCH:6", DisplayName = "Fuel Pump R2",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["FUEL_PUMP_C1"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE SWITCH:9", DisplayName = "Fuel Pump C1 (Jet Pump)",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["FUEL_PUMP_C2"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE SWITCH:10", DisplayName = "Fuel Pump C2 (Jet Pump)",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // Fuel crossfeed: state = the stock valve switch; set fires
        // FUELSYSTEM_VALVE_OPEN/_CLOSE (id 3 = CrossFeedValve) — the TOGGLE event
        // could stick mid-transition (same fix as the A380 crossfeeds).
        ["FUEL_XFEED"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE SWITCH:3", DisplayName = "Fuel Crossfeed",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        },

        // Air Con Panel
        ["A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
            DisplayName = "APU Bleed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_COND_PACK_1_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_COND_PACK_1_PB_IS_ON",
            DisplayName = "Pack 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_COND_PACK_2_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
            DisplayName = "Pack 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Anti Ice Panel. The old XMLVAR_MOMENTARY_PUSH_OVHD_ANTIICE_*_PRESSED vars are
        // model-only press-animation flags that do NOT actuate the systems (same finding
        // as the A380 #56 work). The real controls (live-verified on the A32NX):
        //   - WING anti-ice = A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED (calc-path write;
        //     reverts on the ground due to the on-ground inhibit, holds in flight —
        //     A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON is the read-only valve-open status).
        //   - ENGINE 1/2 anti-ice = the stock K-event ANTI_ICE_SET_ENGn (state read from
        //     the stock simvar ENG ANTI ICE:n; verified 0->1 actuates). Routed in
        //     HandleUIVariableSet.
        ["A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED",
            DisplayName = "Wing Anti-Ice",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["ENG_ANTI_ICE:1"] = new SimConnect.SimVarDefinition
        {
            Name = "ENG ANTI ICE:1", DisplayName = "Engine 1 Anti-Ice",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["ENG_ANTI_ICE:2"] = new SimConnect.SimVarDefinition
        {
            Name = "ENG ANTI ICE:2", DisplayName = "Engine 2 Anti-Ice",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // ENGINE SD-page stock simvars (the FBW SD ENG page reads these, NOT A32NX_
        // L:vars — verified in fbw-a32nx SD/Pages/Eng). Pre-declared as SimVar (key
        // underscored, Name spaced + index) so the SD-page auto-register loop leaves
        // them as SimVar rather than mis-registering them as L:vars.
        ["GENERAL_ENG_OIL_TEMPERATURE:1"] = new SimConnect.SimVarDefinition
        { Name = "GENERAL ENG OIL TEMPERATURE:1", DisplayName = "Engine 1 Oil Temperature", Type = SimConnect.SimVarType.SimVar, Units = "celsius", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
        ["GENERAL_ENG_OIL_TEMPERATURE:2"] = new SimConnect.SimVarDefinition
        { Name = "GENERAL ENG OIL TEMPERATURE:2", DisplayName = "Engine 2 Oil Temperature", Type = SimConnect.SimVarType.SimVar, Units = "celsius", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
        ["ENG_OIL_PRESSURE:1"] = new SimConnect.SimVarDefinition
        { Name = "ENG OIL PRESSURE:1", DisplayName = "Engine 1 Oil Pressure", Type = SimConnect.SimVarType.SimVar, Units = "psi", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
        ["ENG_OIL_PRESSURE:2"] = new SimConnect.SimVarDefinition
        { Name = "ENG OIL PRESSURE:2", DisplayName = "Engine 2 Oil Pressure", Type = SimConnect.SimVarType.SimVar, Units = "psi", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
        ["ENG_OIL_QUANTITY:1"] = new SimConnect.SimVarDefinition
        { Name = "ENG OIL QUANTITY:1", DisplayName = "Engine 1 Oil Quantity", Type = SimConnect.SimVarType.SimVar, Units = "percent", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
        ["ENG_OIL_QUANTITY:2"] = new SimConnect.SimVarDefinition
        { Name = "ENG OIL QUANTITY:2", DisplayName = "Engine 2 Oil Quantity", Type = SimConnect.SimVarType.SimVar, Units = "percent", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
        ["TURB_ENG_VIBRATION:1"] = new SimConnect.SimVarDefinition
        { Name = "TURB ENG VIBRATION:1", DisplayName = "Engine 1 Vibration", Type = SimConnect.SimVarType.SimVar, Units = "number", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
        ["TURB_ENG_VIBRATION:2"] = new SimConnect.SimVarDefinition
        { Name = "TURB ENG VIBRATION:2", DisplayName = "Engine 2 Vibration", Type = SimConnect.SimVarType.SimVar, Units = "number", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest },
        // Read-only wing anti-ice valve-open status (the actual flowing state). The
        // SELECTED control reverts on the ground inhibit, so this is what tells the
        // pilot whether wing anti-ice is genuinely flowing. Auto-announced on change.
        ["A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", DisplayName = "Wing Anti-Ice Flowing",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // PROBE & WINDOW HEAT PB: A32NX_MAN_PITOT_HEAT (A32NX_Interior_Misc.xml:263).
        // Auto logic forces heat on with engines running, so an Off set can revert —
        // correct aircraft behaviour (same as the A380 probe heat).
        ["A32NX_MAN_PITOT_HEAT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MAN_PITOT_HEAT", DisplayName = "Probe and Window Heat",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Auto", [1] = "On" }
        },

        // ---- Air-conditioning ZONE TEMPERATURE selectors (5th-audit gap: the A380 has
        // these, the A320 didn't). The cockpit knob is A32NX_OVHD_COND_{CKPT,FWD,AFT}_
        // SELECTOR_KNOB (0..300, live-verified settable: CKPT held 200), which maps to
        // 18-30 C. Surfaced as numeric Celsius inputs (key ends "_SET" → MainForm numeric
        // box); HandleUIVariableSet converts C → the 0..300 knob. The actual zone temps
        // (A32NX_COND_{CKPT,FWD,AFT}_TEMP, celsius) are the read-only display. ----
        ["COND_CKPT_TEMP_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "COND_CKPT_TEMP_SET", DisplayName = "Cockpit Temperature",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "celsius"
        },
        ["COND_FWD_TEMP_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "COND_FWD_TEMP_SET", DisplayName = "Forward Cabin Temperature",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "celsius"
        },
        ["COND_AFT_TEMP_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "COND_AFT_TEMP_SET", DisplayName = "Aft Cabin Temperature",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "celsius"
        },
        ["A32NX_COND_CKPT_TEMP"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_COND_CKPT_TEMP", DisplayName = "Cockpit Temperature",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "celsius"
        },
        ["A32NX_COND_FWD_TEMP"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_COND_FWD_TEMP", DisplayName = "Forward Cabin Temperature",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "celsius"
        },
        ["A32NX_COND_AFT_TEMP"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_COND_AFT_TEMP", DisplayName = "Aft Cabin Temperature",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "celsius"
        },
        // APU "AVAIL" status (5th-audit gap). Read-only display only — NOT auto-announced,
        // because the EWD memo "APU AVAIL" already speaks it (avoids the A380 #62
        // double-announce). Lets the pilot read whether the APU is ready on the APU panel.
        ["A32NX_OVHD_APU_START_PB_IS_AVAILABLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", DisplayName = "APU Available",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Available" }
        },
        // Rudder trim (parity with the A380). The FAC-computed trim position is an
        // ARINC429 degrees word (positive = nose-Left), decoded in TryGetDisplayOverride.
        // Reset fires the stock K-event RUDDER_TRIM_RESET (the cockpit's reset path).
        ["A32NX_FAC_1_RUDDER_TRIM_POS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FAC_1_RUDDER_TRIM_POS", DisplayName = "Rudder Trim",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "number"
        },
        ["A32NX_RUDDER_TRIM_RESET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RUDDER_TRIM_RESET", DisplayName = "Rudder Trim Reset",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        // Rudder-trim NUDGE buttons (parity with the A380). Stock K-events; fired in
        // HandleUIVariableSet. Momentary push-buttons (no resting state to read).
        ["RUDDER_TRIM_LEFT"] = new SimConnect.SimVarDefinition
        {
            Name = "RUDDER_TRIM_LEFT", DisplayName = "Rudder Trim Left",
            Type = SimConnect.SimVarType.Event, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsButton = true
        },
        ["RUDDER_TRIM_RIGHT"] = new SimConnect.SimVarDefinition
        {
            Name = "RUDDER_TRIM_RIGHT", DisplayName = "Rudder Trim Right",
            Type = SimConnect.SimVarType.Event, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsButton = true
        },
        // Nosewheel-steering angle + tiller-handle position read-outs (parity with the
        // A380; SAME var names — decoded in TryGetDisplayOverride). Read-only, OnRequest.
        ["A32NX_NOSE_WHEEL_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_NOSE_WHEEL_POSITION", DisplayName = "Nosewheel angle",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "number"
        },
        ["A32NX_TILLER_HANDLE_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TILLER_HANDLE_POSITION", DisplayName = "Tiller",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "number"
        },
        // Oxygen timer reset — momentary L-var pulse 1->0 (parity with the A380).
        ["A32NX_OXYGEN_TMR_RESET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OXYGEN_TMR_RESET", DisplayName = "Oxygen Timer Reset",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        // APU auto-exit TEST + emergency-generator TEST — momentary L-var pulse 1->0.
        ["A32NX_APU_AUTOEXITING_TEST_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_APU_AUTOEXITING_TEST_ON", DisplayName = "APU Auto Exit Test",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            // TOGGLE-type test (sustained while active) — latching Off/On.
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_EMERELECPWR_GEN_TEST"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EMERELECPWR_GEN_TEST", DisplayName = "Emergency Generator Test",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            // HOLD-type test (emer gen runs WHILE active) — latching Off/On.
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // Speed-brake FINE slider (synthetic — no real backing var). The TrackBar maps
        // 0-16383 directly; HandleUIVariableSet fires the stock SPOILERS_SET. Parity A380.
        ["A32NX_MSFSBA_SPEEDBRAKE_SLIDER"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MSFSBA_SPEEDBRAKE_SLIDER", DisplayName = "Speed Brake (fine)",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsSlider = true, SliderMin = 0, SliderMax = 16383
        },
        // Speed-brake COARSE combo (Retracted/Half/Full) — parity with the A380's
        // A380X_MSFSBA_SPEEDBRAKE. Synthetic: no backing L:var; the set fires the stock
        // SPOILERS_SET (0 / 8192 / 16383) in HandleUIVariableSet. OnRequest + excluded from
        // the auto-announce loop (no real var to monitor — mirrors the A380 Act() helper).
        // The real handle STATE is A32NX_SPOILERS_HANDLE_POSITION in the display box.
        ["A32NX_MSFSBA_SPEEDBRAKE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MSFSBA_SPEEDBRAKE", DisplayName = "Speed Brake",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Retracted", [1] = "Half", [2] = "Full" }
        },
        // Metric / imperial WEIGHT units (parity with the A380). The A32NX EFB "US Units"
        // setting is mirrored continuously to A32NX_EFB_USING_METRIC_UNIT (1=kg, 0=lb);
        // MSFSBA follows it and speaks gross-weight + total-fuel read-outs in that unit
        // (the EFB toggle, Shift+T, is the control — no separate MSFSBA toggle). The
        // gross-weight/fuel readout vars are requested in kilograms; WeightUser converts.
        ["A32NX_EFB_USING_METRIC_UNIT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFB_USING_METRIC_UNIT", DisplayName = "Weight Units",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Pounds", [1] = "Kilograms" }
        },
        ["GROSS_WEIGHT_KG"] = new SimConnect.SimVarDefinition
        {
            Name = "TOTAL WEIGHT", DisplayName = "Gross Weight",
            Type = SimConnect.SimVarType.SimVar, Units = "kilograms", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        // Gross weight (kg, stock) + CG (%MAC, FBW L-var) — monitored + cached for the
        // W / Shift+W readouts. CG MUST read with Units="number" (L-var). Both are
        // cached silently in ProcessSimVarUpdate (return true → never auto-announced).
        ["GW_KG_CACHE"] = new SimConnect.SimVarDefinition
        {
            Name = "TOTAL WEIGHT", DisplayName = "Gross Weight (cache)",
            Type = SimConnect.SimVarType.SimVar, Units = "kilograms", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ExcludeFromMonitorManager = true
        },
        ["A32NX_AIRFRAME_GW_CG_PERCENT_MAC"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AIRFRAME_GW_CG_PERCENT_MAC", DisplayName = "Gross Weight CG",
            Type = SimConnect.SimVarType.LVar, Units = "number", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ExcludeFromMonitorManager = true
        },
        ["FUEL_QUANTITY_KG"] = new SimConnect.SimVarDefinition
        {
            Name = "FUEL TOTAL QUANTITY WEIGHT", DisplayName = "Fuel on board",
            Type = SimConnect.SimVarType.SimVar, Units = "kilograms", UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        // Brake pressures (the "triple indicator" — normal/alternate/accumulator, psi) +
        // brake status (parity with the A380; useful for the taxi brake check). Live:
        // NORM 0 while not braking, ALTN ~2100, ACC ~3000.
        ["A32NX_HYD_BRAKE_NORM_LEFT_PRESS"] = new SimConnect.SimVarDefinition
        { Name = "A32NX_HYD_BRAKE_NORM_LEFT_PRESS", DisplayName = "Normal Brake Left", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "psi" },
        ["A32NX_HYD_BRAKE_NORM_RIGHT_PRESS"] = new SimConnect.SimVarDefinition
        { Name = "A32NX_HYD_BRAKE_NORM_RIGHT_PRESS", DisplayName = "Normal Brake Right", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "psi" },
        ["A32NX_HYD_BRAKE_ALTN_LEFT_PRESS"] = new SimConnect.SimVarDefinition
        { Name = "A32NX_HYD_BRAKE_ALTN_LEFT_PRESS", DisplayName = "Alternate Brake Left", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "psi" },
        ["A32NX_HYD_BRAKE_ALTN_RIGHT_PRESS"] = new SimConnect.SimVarDefinition
        { Name = "A32NX_HYD_BRAKE_ALTN_RIGHT_PRESS", DisplayName = "Alternate Brake Right", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "psi" },
        ["A32NX_HYD_BRAKE_ALTN_ACC_PRESS"] = new SimConnect.SimVarDefinition
        { Name = "A32NX_HYD_BRAKE_ALTN_ACC_PRESS", DisplayName = "Brake Accumulator", Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "psi" },
        ["A32NX_BRAKES_HOT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_BRAKES_HOT", DisplayName = "Brakes Hot",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Hot" }
        },
        ["A32NX_BRAKE_FAN_RUNNING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_BRAKE_FAN_RUNNING", DisplayName = "Brake Fan Running",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Running" }
        },
        // GPWS self-test (parity with the A380 GPWS panel). Settable via the catch-all.
        ["A32NX_GPWS_TEST"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_TEST", DisplayName = "GPWS Test",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Test" }
        },

        // ---- Wipers: FBW_Airbus_Wiper_Knob drives electrical circuit 77 (Captain)
        // / 80 (F/O): off/on = ELECTRICAL_CIRCUIT_TOGGLE, speed = circuit power
        // setting 75 (slow) / 100 (fast). Same pattern as the A380 wipers (141/143).
        // The circuit-switch state is a bool, so the read-back shows "Slow" whenever
        // the wiper is running — even at fast (exact speed read-back would need
        // CIRCUIT POWER SETTING:n). The three options stay so the SET path can
        // drive the full off/slow/fast state.
        // (XMLVAR_A320_WiperSwitch_* does not exist in FBW — dead-var trap.)
        ["XMLVAR_A320_WiperSwitch_1"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT SWITCH ON:77", DisplayName = "Wiper Captain",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Slow", [2] = "Fast" }
        },
        ["XMLVAR_A320_WiperSwitch_2"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT SWITCH ON:80", DisplayName = "Wiper First Officer",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Slow", [2] = "Fast" }
        },

        // OVERHEAD FORWARD SECTION - Calls Panel
        // The PUSH calls are HOLD vars FBW reads per-frame (MECH gates a Continuous
        // Wwise horn loop) — IsMomentary makes MainForm pulse 1 then 0 after 150 ms;
        // a write that stays 1 means an endless mech horn / stuck cabin call.
        ["PUSH_OVHD_CALLS_MECH"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_MECH",
            DisplayName = "Call MECH",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsMomentary = true
        },
        ["PUSH_OVHD_CALLS_ALL"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_ALL",
            DisplayName = "Call ALL",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsMomentary = true
        },
        ["PUSH_OVHD_CALLS_FWD"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_FWD",
            DisplayName = "Call FWD",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsMomentary = true
        },
        ["PUSH_OVHD_CALLS_AFT"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_AFT",
            DisplayName = "Call AFT",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsMomentary = true
        },
        ["A32NX_CALLS_EMER_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CALLS_EMER_ON",
            DisplayName = "Emergency Call",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // OVERHEAD FORWARD SECTION - GPWS Panel
        ["A32NX_GPWS_FLAPS3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_FLAPS3",
            DisplayName = "Landing Flap 3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_GPWS_FLAP_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_FLAP_OFF",
            DisplayName = "GPWS Flap Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off" }
        },
        ["A32NX_GPWS_GS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_GS_OFF",
            DisplayName = "GPWS Glideslope Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off" }
        },
        ["A32NX_GPWS_SYS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_SYS_OFF",
            DisplayName = "GPWS System",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off" }
        },
        ["A32NX_GPWS_TERR_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_TERR_OFF",
            DisplayName = "GPWS Terrain",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off" }
        },

        // INSTRUMENT SECTION - ISIS Panel
        ["A32NX_ISIS_BARO_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ISIS_BARO_MODE",
            DisplayName = "ISIS Baro Mode",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "QNH", [1] = "STD" }
        },
        ["A32NX_ISIS_BUGS_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ISIS_BUGS_ACTIVE",
            DisplayName = "ISIS Bugs Page",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Active" }
        },
        ["A32NX_ISIS_LS_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ISIS_LS_ACTIVE",
            DisplayName = "ISIS LS",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Active" }
        },

        // GLARESHIELD SECTION - FCU Panel
        ["A32NX.FCU_HDG_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_HDG_SET",
            DisplayName = "Heading",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_HDG_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_HDG_PUSH",
            DisplayName = "Push Heading Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_HDG_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_HDG_PULL",
            DisplayName = "Pull Heading Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_LOC_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_LOC_PUSH",
            DisplayName = "LOC Mode",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_SPD_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_SPD_SET",
            DisplayName = "Speed",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_SPD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_SPD_PUSH",
            DisplayName = "Push Speed Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_SPD_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_SPD_PULL",
            DisplayName = "Pull Speed Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ALT_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ALT_SET",
            DisplayName = "Altitude",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ALT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ALT_PUSH",
            DisplayName = "Push Altitude Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ALT_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ALT_PULL",
            DisplayName = "Pull Altitude Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EXPED_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EXPED_PUSH",
            DisplayName = "Expedite",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_APPR_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_APPR_PUSH",
            DisplayName = "APPR Mode",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_AP_1_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_AP_1_PUSH",
            DisplayName = "Autopilot 1",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_AP_2_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_AP_2_PUSH",
            DisplayName = "Autopilot 2",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ATHR_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ATHR_PUSH",
            DisplayName = "AutoThrust",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_AP_DISCONNECT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_AP_DISCONNECT_PUSH",
            DisplayName = "Red AP Disconnect",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_ATHR_DISCONNECT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_ATHR_DISCONNECT_PUSH",
            DisplayName = "Red ATHR Disconnect",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_SPD_MACH_TOGGLE_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_SPD_MACH_TOGGLE_PUSH",
            DisplayName = "Speed/Mach Mode",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_VS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_VS_SET",
            DisplayName = "VS/FPA",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_VS_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_VS_PUSH",
            DisplayName = "Push VS/FPA Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_VS_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_VS_PULL",
            DisplayName = "Pull VS/FPA Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_TRK_FPA_TOGGLE_PUSH",
            DisplayName = "Toggle TRK/FPA",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_FD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_FD_PUSH",
            DisplayName = "Left Flight Director",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_FD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_FD_PUSH",
            DisplayName = "Right Flight Director",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_BARO_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_BARO_PUSH",
            DisplayName = "Push Left Baro Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_BARO_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_BARO_PULL",
            DisplayName = "Pull Left Baro Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_BARO_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_BARO_PUSH",
            DisplayName = "Push Right Baro Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_BARO_PULL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_BARO_PULL",
            DisplayName = "Pull Right Baro Knob",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_CSTR_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_CSTR_PUSH",
            DisplayName = "Toggle Constraints",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_WPT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_WPT_PUSH",
            DisplayName = "Toggle Waypoints",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_VORD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_VORD_PUSH",
            DisplayName = "Toggle VOR/DME",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_NDB_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_NDB_PUSH",
            DisplayName = "Toggle NDB",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_L_ARPT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_ARPT_PUSH",
            DisplayName = "Toggle Airports",
            Type = SimConnect.SimVarType.Event
        },
        // First Officer EFIS filter push buttons (mirror of L-side)
        ["A32NX.FCU_EFIS_R_CSTR_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_CSTR_PUSH",
            DisplayName = "Toggle Constraints",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_WPT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_WPT_PUSH",
            DisplayName = "Toggle Waypoints",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_VORD_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_VORD_PUSH",
            DisplayName = "Toggle VOR/DME",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_NDB_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_NDB_PUSH",
            DisplayName = "Toggle NDB",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX.FCU_EFIS_R_ARPT_PUSH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_ARPT_PUSH",
            DisplayName = "Toggle Airports",
            Type = SimConnect.SimVarType.Event
        },

        // INSTRUMENT SECTION - Autobrake and Gear Panel
        // NOTE: Autobrakes may only be settable under specific flight conditions
        // TODO: Test during different flight phases (ground, approach, landing, etc.)
        ["AUTOBRAKE_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOBRAKES_ARMED_MODE", // Read current state from this LVar
            DisplayName = "Autobrake Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "DIS", [1] = "LO", [2] = "MED", [3] = "MAX" }
        },
        // Autobrake button events - alternative approach for testing
        ["A32NX.AUTOBRAKE_SET_DISARM"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.AUTOBRAKE_SET_DISARM",
            DisplayName = "Autobrake Disarm Button",
            Type = SimConnect.SimVarType.Event,
            PreventTextInput = true  // Don't show text input UI for this event
        },
        ["A32NX.AUTOBRAKE_BUTTON_LO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.AUTOBRAKE_BUTTON_LO",
            DisplayName = "Autobrake LO Button",
            Type = SimConnect.SimVarType.Event,
            PreventTextInput = true
        },
        ["A32NX.AUTOBRAKE_BUTTON_MED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.AUTOBRAKE_BUTTON_MED",
            DisplayName = "Autobrake MED Button",
            Type = SimConnect.SimVarType.Event,
            PreventTextInput = true
        },
        ["A32NX.AUTOBRAKE_BUTTON_MAX"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.AUTOBRAKE_BUTTON_MAX",
            DisplayName = "Autobrake MAX Button",
            Type = SimConnect.SimVarType.Event,
            PreventTextInput = true
        },
        ["A32NX_BRAKE_FAN_BTN_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_BRAKE_FAN_BTN_PRESSED",
            DisplayName = "Brake Fan",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["GEAR_HANDLE_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "GEAR HANDLE POSITION",
            DisplayName = "Gear",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Up", [1] = "Down" }
        },

        // PEDESTAL SECTION - Speed Brake Panel
        ["SPOILERS_ARM_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "SPOILERS_ARM_TOGGLE",
            DisplayName = "Arm/Disarm Spoilers",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX_SPOILERS_ARMED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPOILERS_ARMED",
            DisplayName = "Spoilers Armed Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Disarmed", [1] = "Armed" }
        },
        ["A32NX_SPOILERS_HANDLE_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPOILERS_HANDLE_POSITION",
            DisplayName = "Spoiler Handle Position",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Retracted", [0.5] = "Half Extended", [1] = "Full Extended" }
        },
        ["SPOILERS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "SPOILERS_ON",
            DisplayName = "Extend Spoilers Full",
            Type = SimConnect.SimVarType.Event
        },
        ["SPOILERS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "SPOILERS_OFF",
            DisplayName = "Retract Spoilers",
            Type = SimConnect.SimVarType.Event
        },
        // PEDESTAL SECTION - Parking Brake Panel
        ["A32NX_PARK_BRAKE_LEVER_POS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PARK_BRAKE_LEVER_POS",
            DisplayName = "Parking Brake",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical for ground operations
            ValueDescriptions = new Dictionary<double, string> { [0] = "Released", [1] = "Set" }
        },

        // PEDESTAL SECTION - Engines Panel
        // ENG MASTER 1/2 combos (parity with the A380): live SimVar state =
        // FUELSYSTEM VALVE SWITCH:n (the commanded switch position, instant — the
        // FUELSYSTEM VALVE OPEN:n physical-position var lags during the close), and the
        // set fires FUELSYSTEM_VALVE_OPEN/CLOSE (param n) via HandleUIVariableSet.
        // Replaces the old separate ENGINE_n_MASTER_ON / _OFF push buttons. Continuous +
        // announced so each speaks Off/On on change like every other combo. Live-verified:
        // firing FUELSYSTEM_VALVE_OPEN param 1 moves SWITCH:1 0->1 (engine 2 unaffected).
        ["ENGINE_1_MASTER"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE SWITCH:1",
            DisplayName = "Engine 1 Master",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["ENGINE_2_MASTER"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE SWITCH:2",
            DisplayName = "Engine 2 Master",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["ENGINE_MODE_SELECTOR"] = new SimConnect.SimVarDefinition
        {
            Name = "ENGINE_MODE_SELECTOR",
            DisplayName = "Engine Mode",
            Type = SimConnect.SimVarType.Event,  // We'll handle this specially
            ValueDescriptions = new Dictionary<double, string> { [0] = "CRANK", [1] = "NORM", [2] = "IGN" }
        },

        // Engine State Monitoring Variables (continuous monitoring with auto-announcement)
        ["A32NX_ENGINE_STATE:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_STATE:1",
            DisplayName = "Engine 1",
            Type = SimConnect.SimVarType.LVar,
            Units = "number",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On",
                [2] = "Starting",
                [3] = "Shutting Down"
            }
        },
        ["A32NX_ENGINE_STATE:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_STATE:2",
            DisplayName = "Engine 2",
            Type = SimConnect.SimVarType.LVar,
            Units = "number",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On",
                [2] = "Starting",
                [3] = "Shutting Down"
            }
        },

        // Engine Igniter Monitoring Variables (continuous monitoring with auto-announcement)
        ["A32NX_FADEC_IGNITER_A_ACTIVE_ENG1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FADEC_IGNITER_A_ACTIVE_ENG1",
            DisplayName = "Igniter A Engine 1",
            Type = SimConnect.SimVarType.LVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Active"
            }
        },
        ["A32NX_FADEC_IGNITER_B_ACTIVE_ENG1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FADEC_IGNITER_B_ACTIVE_ENG1",
            DisplayName = "Igniter B Engine 1",
            Type = SimConnect.SimVarType.LVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Active"
            }
        },
        ["A32NX_FADEC_IGNITER_A_ACTIVE_ENG2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FADEC_IGNITER_A_ACTIVE_ENG2",
            DisplayName = "Igniter A Engine 2",
            Type = SimConnect.SimVarType.LVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Active"
            }
        },
        ["A32NX_FADEC_IGNITER_B_ACTIVE_ENG2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FADEC_IGNITER_B_ACTIVE_ENG2",
            DisplayName = "Igniter B Engine 2",
            Type = SimConnect.SimVarType.LVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "Active"
            }
        },

        ["LANDING_2_RETRACTED"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_2_RETRACTED",
            DisplayName = "Left Landing Light Retracted",
            Type = SimConnect.SimVarType.Event
        },
        ["LANDING_3_RETRACTED"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_3_RETRACTED",
            DisplayName = "Right Landing Light Retracted",
            Type = SimConnect.SimVarType.Event
        },
        // "All Landing Lights" action buttons. RenderAsButton routes the click through
        // HandleUIVariableSet (NOT the stock LANDING_LIGHTS_ON/OFF events these used to fire —
        // those bypass the FBW switch and desync the LDG LT memo, per FBW issues #1507/#1528).
        // On = both LAND lights extend + illuminate; Off = both RETRACT (stows them, clears
        // LDG LT). Nose light is left independent. See HandleUIVariableSet.
        ["LANDING_LIGHTS_ON_THIRD_PARTY"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_ON",
            DisplayName = "All Landing Lights On",
            Type = SimConnect.SimVarType.Event,
            RenderAsButton = true
        },
        ["LANDING_LIGHTS_OFF_THIRD_PARTY"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_OFF",
            DisplayName = "All Landing Lights Off (Retract)",
            Type = SimConnect.SimVarType.Event,
            RenderAsButton = true
        },

        // PEDESTAL SECTION - Other panels
        // ECAM Panel (using MobiFlight WASM H-variables)
        ["ECAM_ENG"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_ENG",
            DisplayName = "ENG",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_ENG_PRESSED",
            ReleaseEvent = "A32NX_ECP_ENG_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_ENG",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_APU"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_APU",
            DisplayName = "APU",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_APU_PRESSED",
            ReleaseEvent = "A32NX_ECP_APU_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_APU",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_BLEED"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_BLEED",
            DisplayName = "BLEED",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_BLEED_PRESSED",
            ReleaseEvent = "A32NX_ECP_BLEED_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_BLEED",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_COND"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_COND",
            DisplayName = "COND",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_COND_PRESSED",
            ReleaseEvent = "A32NX_ECP_COND_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_COND",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_ELEC"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_ELEC",
            DisplayName = "ELEC",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_ELEC_PRESSED",
            ReleaseEvent = "A32NX_ECP_ELEC_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_ELEC",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_HYD"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_HYD",
            DisplayName = "HYD",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_HYD_PRESSED",
            ReleaseEvent = "A32NX_ECP_HYD_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_HYD",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_FUEL"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_FUEL",
            DisplayName = "FUEL",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_FUEL_PRESSED",
            ReleaseEvent = "A32NX_ECP_FUEL_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_FUEL",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_PRESS"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_PRESS",
            DisplayName = "PRESS",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_PRESS_PRESSED",
            ReleaseEvent = "A32NX_ECP_PRESS_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_PRESS",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_DOOR"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_DOOR",
            DisplayName = "DOOR",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_DOOR_PRESSED",
            ReleaseEvent = "A32NX_ECP_DOOR_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_DOOR",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_BRAKES"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_BRAKES",
            DisplayName = "BRAKES",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_BRAKES_PRESSED",
            ReleaseEvent = "A32NX_ECP_BRAKES_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_BRAKES",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_FLT_CTL"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_FLT_CTL",
            DisplayName = "FLT CTL",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_FLT_CTL_PRESSED",
            ReleaseEvent = "A32NX_ECP_FLT_CTL_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_FLT_CTL",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_ALL"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_ALL",
            DisplayName = "ALL",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_ALL_PRESSED",
            ReleaseEvent = "A32NX_ECP_ALL_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_ALL",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_STS"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_STS",
            DisplayName = "STS",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_STS_PRESSED",
            ReleaseEvent = "A32NX_ECP_STS_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_STS",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_RCL"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_RCL",
            DisplayName = "RCL",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_RCL_PRESSED",
            ReleaseEvent = "A32NX_ECP_RCL_RELEASED",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_TO_CONF"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_TO_CONF",
            DisplayName = "T.O. CONF",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_TO_CONF_TEST_PRESSED",
            ReleaseEvent = "A32NX_ECP_TO_CONF_TEST_RELEASED",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_EMER_CANC"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_EMER_CANC",
            DisplayName = "EMER CANC",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_EMER_CANCEL_PRESSED",
            ReleaseEvent = "A32NX_ECP_EMER_CANCEL_RELEASED",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_CLR_1"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_CLR_1",
            DisplayName = "CLR 1",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_CLR_1_PRESSED",
            ReleaseEvent = "A32NX_ECP_CLR_1_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_CLR_1",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ECAM_CLR_2"] = new SimConnect.SimVarDefinition
        {
            Name = "ECAM_CLR_2",
            DisplayName = "CLR 2",
            Type = SimConnect.SimVarType.HVar,
            UseMobiFlight = true,
            PressEvent = "A32NX_ECP_CLR_2_PRESSED",
            ReleaseEvent = "A32NX_ECP_CLR_2_RELEASED",
            LedVariable = "A32NX_ECP_LIGHT_CLR_2",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_ECAM_SFAIL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECAM_SFAIL",
            DisplayName = "ECAM Warning Page",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [-1] = "ECAM warning, none",
                [0] = "ECAM warning, ENG",
                [1] = "ECAM warning, BLEED",
                [2] = "ECAM warning, PRESS",
                [3] = "ECAM warning, ELEC",
                [4] = "ECAM warning, HYD",
                [5] = "ECAM warning, FUEL",
                [6] = "ECAM warning, APU",
                [7] = "ECAM warning, COND",
                [8] = "ECAM warning, DOOR",
                [9] = "ECAM warning, WHEEL",
                [10] = "ECAM warning, F-CTL",
                [11] = "ECAM warning, STS",
                [12] = "ECAM warning, CRUISE"
            }
        },

        // ECAM LED Variables (for monitoring LED states)
        ["A32NX_ECP_LIGHT_ENG"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_ENG",
            DisplayName = "ENG LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_APU"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_APU",
            DisplayName = "APU LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_BLEED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_BLEED",
            DisplayName = "BLEED LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_COND"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_COND",
            DisplayName = "COND LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_ELEC"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_ELEC",
            DisplayName = "ELEC LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_HYD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_HYD",
            DisplayName = "HYD LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_FUEL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_FUEL",
            DisplayName = "FUEL LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_PRESS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_PRESS",
            DisplayName = "PRESS LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_DOOR"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_DOOR",
            DisplayName = "DOOR LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_BRAKES"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_BRAKES",
            DisplayName = "BRAKES LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_FLT_CTL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_FLT_CTL",
            DisplayName = "FLT CTL LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_ALL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_ALL",
            DisplayName = "ALL LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_STS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_STS",
            DisplayName = "STS LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_CLR_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_CLR_1",
            DisplayName = "CLR 1 LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ECP_LIGHT_CLR_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECP_LIGHT_CLR_2",
            DisplayName = "CLR 2 LED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // WX Panel
        ["A32NX_SWITCH_RADAR_PWS_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_RADAR_PWS_POSITION",
            DisplayName = "PWS Mode",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        // Weather-radar SYSTEM on/off + MODE + multiscan + ground-clutter-suppression
        // (parity with the A380 Weather Radar panel — the A320 only had PWS before).
        // XMLVAR_A320_WeatherRadar_Sys/_Mode + A32NX_RADAR_* are settable via the
        // calculator-path catch-all in HandleUIVariableSet; Sys/Mode write-stick verified
        // live on the A32NX (separate-eval read-back) 2026-06-04.
        ["XMLVAR_A320_WeatherRadar_Sys"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_A320_WeatherRadar_Sys",
            DisplayName = "Weather Radar System",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "System 1", [1] = "Off", [2] = "System 2" }
        },
        ["XMLVAR_A320_WeatherRadar_Mode"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_A320_WeatherRadar_Mode",
            DisplayName = "Weather Radar Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Weather", [1] = "Weather plus Turbulence", [2] = "Turbulence", [3] = "Map" }
        },
        ["A32NX_RADAR_MULTISCAN_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADAR_MULTISCAN_AUTO",
            DisplayName = "WXR Multiscan Auto",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_RADAR_GCS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADAR_GCS_AUTO",
            DisplayName = "WXR Ground Clutter Suppression",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ATC-TCAS Panel
        ["A32NX_TRANSPONDER_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TRANSPONDER_MODE",
            DisplayName = "Transponder mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STBY", [1] = "AUTO", [2] = "ON" }
        },
        ["A32NX_TRANSPONDER_SYSTEM"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TRANSPONDER_SYSTEM",
            DisplayName = "ATC System",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "System 1", [1] = "System 2" }
        },
        ["A32NX_SWITCH_ATC_ALT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_ATC_ALT",
            DisplayName = "ALT RPTG",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "OFF", [1] = "ON" }
        },
        ["TRANSPONDER_CODE_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "XPNDR_SET",
            DisplayName = "SQUAWK",
            Type = SimConnect.SimVarType.Event,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        // Squawk read-back: TRANSPONDER CODE:1 reads as a BCD16 word (0x2000 = 8192) — decoded
        // to the 4-digit squawk in TryGetDisplayOverride. Distinct key (NOT SQUAWK_CODE, which is
        // a MainForm special-announce key). Matches the A380. The A320 already had squawk INPUT
        // (TRANSPONDER_CODE_SET); this is the read-back.
        ["XPNDR_CODE"] = new SimConnect.SimVarDefinition
        {
            Name = "TRANSPONDER CODE:1",
            DisplayName = "Squawk Code",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "BCO16"
        },
        ["XPNDR_IDENT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "XPNDR_IDENT_ON",
            DisplayName = "IDENT",
            Type = SimConnect.SimVarType.Event
        },
        // ATC datalink (DCDU) — CPDLC message-waiting announce + acknowledge (A380 parity).
        ["A32NX_DCDU_ATC_MSG_WAITING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DCDU_ATC_MSG_WAITING", DisplayName = "ATC Message Waiting",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Message Waiting" }
        },
        ["A32NX_DCDU_ATC_MSG_ACK"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DCDU_ATC_MSG_ACK", DisplayName = "ATC Message Acknowledge",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsMomentary = true, RenderAsButton = true, SuppressRestingButtonState = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" }
        },
        ["A32NX_SWITCH_TCAS_TRAFFIC_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_TCAS_TRAFFIC_POSITION",
            DisplayName = "TCAS Traffic Display",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "THRT", [1] = "ALL", [2] = "ABV", [3] = "BLW" }
        },
        ["A32NX_SWITCH_TCAS_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_TCAS_POSITION",
            DisplayName = "TCAS Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STBY", [1] = "TA", [2] = "TA/RA" }
        },

        // ---- Situational-awareness auto-announces + SAFETY AURAL CALLOUTS (parity with the
        // A380). All batch-covered (Continuous + IsAnnounced) → no SimConnect-def cost, announce
        // on change, and appear in the Ctrl+M monitor. The aurals (GPWS/stall/AP-disconnect) are
        // the escape-maneuver / warning sounds a blind pilot otherwise can't hear; each L-var holds
        // the current warning so it speaks once per event (not per aural repeat). The A380's FMA
        // speed-protection / mode-reversion vars are A380-only (verified read 0 / unpublished on the
        // A32NX — the A32NX computes those PFD-locally), so they are intentionally NOT ported. ----
        // TCAS computed system mode (distinct from the ND traffic-display switch above).
        ["A32NX_TCAS_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_MODE",
            DisplayName = "TCAS active mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "standby", [1] = "traffic advisory only", [2] = "traffic and resolution advisories" }
        },
        ["A32NX_TCAS_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_FAULT",
            DisplayName = "TCAS Fault",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "normal", [1] = "fault" }
        },
        // FMA triple-click: mode reversion cue (1 = reversion occurred, reverts to 0).
        // OnlyAnnounceValueDescriptionMatches suppresses the 1→0 reset announcement —
        // without it the generic path would speak "FMA mode reversion cue: 0".
        ["A32NX_FMA_TRIPLE_CLICK"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_TRIPLE_CLICK", DisplayName = "FMA mode reversion cue",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, OnlyAnnounceValueDescriptionMatches = true,
            ValueDescriptions = new Dictionary<double, string> { [1] = "Mode reversion, check FMA" }
        },
        // Hydraulic PTU active memo (shown on the ECAM).
        ["A32NX_HYD_PTU_ON_ECAM_MEMO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_HYD_PTU_ON_ECAM_MEMO", DisplayName = "PTU",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "stopped", [1] = "transferring" }
        },
        // TCAS advisory state (distinct from A32NX_TCAS_MODE which is the system mode switch).
        ["A32NX_TCAS_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_STATE", DisplayName = "TCAS advisory",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "clear of conflict", [1] = "traffic advisory", [2] = "resolution advisory" }
        },
        // TCAS resolution-advisory DETAIL — what to FLY during an RA. The V/S bands
        // are written ONLY as the :1/:2 INDEXED L:vars (TcasComputer.ts:1267-1271,
        // min/max of the green fly-to and red avoid bands; reset on clear of
        // conflict since upstream #10662); the detail vars are plain L:vars.
        // All cached SILENTLY in ProcessSimVarUpdate and spoken as one composed
        // guidance sentence ("TCAS: Climb. Fly vertical speed plus 1500 to plus
        // 2000 feet per minute.") — see Services.TcasRaGuidance. The colon-indexed
        // names ride the continuous batch, the proven transport for indexed
        // L:vars (A32NX_AUTOTHRUST_TLA:n precedent).
        // Silent caches — their speech rides the A32NX_TCAS_STATE monitor entry,
        // so they're hidden from the Ctrl+M list (ExcludeFromMonitorManager).
        ["A32NX_TCAS_VSPEED_GREEN:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_VSPEED_GREEN:1", DisplayName = "TCAS target vertical speed minimum",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "feet per minute", ExcludeFromMonitorManager = true
        },
        ["A32NX_TCAS_VSPEED_GREEN:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_VSPEED_GREEN:2", DisplayName = "TCAS target vertical speed maximum",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "feet per minute", ExcludeFromMonitorManager = true
        },
        ["A32NX_TCAS_VSPEED_RED:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_VSPEED_RED:1", DisplayName = "TCAS avoid vertical speed minimum",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "feet per minute", ExcludeFromMonitorManager = true
        },
        ["A32NX_TCAS_VSPEED_RED:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_VSPEED_RED:2", DisplayName = "TCAS avoid vertical speed maximum",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "feet per minute", ExcludeFromMonitorManager = true
        },
        ["A32NX_TCAS_RA_CORRECTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_RA_CORRECTIVE", DisplayName = "TCAS RA corrective",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, ExcludeFromMonitorManager = true
        },
        ["A32NX_TCAS_RA_UP_ADVISORY_STATUS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_RA_UP_ADVISORY_STATUS", DisplayName = "TCAS RA up advisory",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, ExcludeFromMonitorManager = true
        },
        ["A32NX_TCAS_RA_DOWN_ADVISORY_STATUS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_RA_DOWN_ADVISORY_STATUS", DisplayName = "TCAS RA down advisory",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, ExcludeFromMonitorManager = true
        },
        ["A32NX_TCAS_RA_RATE_TO_MAINTAIN"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_RA_RATE_TO_MAINTAIN", DisplayName = "TCAS RA rate to maintain",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "feet per minute", ExcludeFromMonitorManager = true
        },
        // EGPWS (GPWS/TAWS) escape-maneuver callouts — enum verified against the FBW EGPWS source.
        ["A32NX_GPWS_AURAL_OUTPUT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_AURAL_OUTPUT",
            DisplayName = "GPWS",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "none", [1] = "PULL UP", [2] = "TERRAIN", [3] = "TOO LOW TERRAIN", [4] = "TOO LOW GEAR",
                [5] = "TOO LOW FLAPS", [6] = "SINK RATE", [7] = "DON'T SINK", [8] = "GLIDESLOPE",
                [9] = "GLIDESLOPE", [10] = "TERRAIN AHEAD", [11] = "OBSTACLE AHEAD"
            }
        },
        ["A32NX_AUDIO_STALL_WARNING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUDIO_STALL_WARNING",
            DisplayName = "Stall warning",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "STALL" }
        },
        ["A32NX_FWC_CAVALRY_CHARGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FWC_CAVALRY_CHARGE",
            DisplayName = "Autopilot disconnect",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "autopilot disconnect" }
        },
        // FWC discrete word 124: baro-reference discrepancy between the two sides
        // (bit 24 = STD discrepancy, bit 25 = baro discrepancy) — a blind pilot can't
        // glance at both PFDs to compare. Decoded in ProcessSimVarUpdate.
        ["A32NX_FWC_1_DISCRETE_WORD_124"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FWC_1_DISCRETE_WORD_124", DisplayName = "Baro Reference Discrepancy",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },

        ["A32NX_AUTOTHRUST_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_MODE",
            DisplayName = "Autothrust Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "NONE", [1] = "MAN_TOGA", [2] = "MAN_GA_SOFT", [3] = "MAN_FLEX", [4] = "MAN_DTO",
                [5] = "MAN_MCT", [6] = "MAN_THR", [7] = "SPEED", [8] = "MACH", [9] = "THR_MCT",
                [10] = "THR_CLB", [11] = "THR_LVR", [12] = "THR_IDLE", [13] = "A_FLOOR", [14] = "TOGA_LK"
            }
        },
        ["A32NX_AUTOTHRUST_MODE_MESSAGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_MODE_MESSAGE",
            DisplayName = "Autothrust Message",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE",
            DisplayName = "Autothrust Thrust Limit Type",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number",
            // Enum from the FBW EWD N1Limit component: ['', CLB, MCT, FLX, TOGA, MREV].
            // Read as "Autothrust Thrust Limit Type: CLB" (no redundant "Thrust:" prefix).
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "None",
                [1] = "CLB",
                [2] = "MCT",
                [3] = "FLEX",
                [4] = "TOGA",
                [5] = "Max Reverse"
            }
        },
        ["A32NX_AUTOTHRUST_THRUST_LIMIT_FLX"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_FLX",
            DisplayName = "Autothrust FLX Limit",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_THRUST_LIMIT_TOGA",
            DisplayName = "Autothrust TOGA Limit",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        // The single ACTIVE thrust-limit % (abs) the FBW EWD N1 gauge shows — used by the
        // decoded Upper-E/WD (SD page 0) readout. (The A320 has no per-engine THR% clamp,
        // unlike the A380; this one number is the limit for both engines.)
        ["A32NX_AUTOTHRUST_THRUST_LIMIT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_THRUST_LIMIT",
            DisplayName = "Autothrust Thrust Limit",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_AUTOTHRUST_N1_COMMANDED:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_N1_COMMANDED:1",
            DisplayName = "N1 Commanded",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_AUTOTHRUST_N1_COMMANDED:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_N1_COMMANDED:2",
            DisplayName = "N1 Commanded",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        // Idle-N1 reference + FWC flight phase (gate the "IDLE" memo on the Upper E/WD).
        ["A32NX_ENGINE_IDLE_N1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_IDLE_N1",
            DisplayName = "Idle N1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_FWC_FLIGHT_PHASE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FWC_FLIGHT_PHASE",
            DisplayName = "FWC Flight Phase",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        // Reverser deployed flags (bool) — Upper E/WD reverser annunciation.
        ["A32NX_REVERSER_1_DEPLOYED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_REVERSER_1_DEPLOYED",
            DisplayName = "Engine 1 Reverser Deployed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool"
        },
        ["A32NX_REVERSER_2_DEPLOYED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_REVERSER_2_DEPLOYED",
            DisplayName = "Engine 2 Reverser Deployed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool"
        },
        ["A32NX_AUTOPILOT_VS_SELECTED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_VS_SELECTED",
            DisplayName = "Selected Vertical Speed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet per minute"
        },
        ["A32NX_AUTOPILOT_FPA_SELECTED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_FPA_SELECTED",
            DisplayName = "Selected FPA",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },

        // ==== PFD / ND status-box display fields (A380 parity) ====================
        // All OnRequest (force-read on F5 / panel-show), NOT IsAnnounced (would spam).
        // Decoded in TryGetDisplayOverride unless ARINC429 / ValueDescriptions handle it.
        // ---- PFD: managed / preselect speeds (LVar; decoded) ----
        ["A32NX_SPEEDS_MANAGED_PFD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_MANAGED_PFD", DisplayName = "Managed speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        // Units = knots is REQUIRED (live-verified): the raw number is m/s; reading it
        // with the knots unit returns -1 unset / the real preselect in knots.
        ["A32NX_SpeedPreselVal"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SpeedPreselVal", DisplayName = "Preselected speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        ["A32NX_MachPreselVal"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MachPreselVal", DisplayName = "Preselected Mach",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        // ---- PFD: Expedite (LVar enum; generic path renders the description) ----
        ["A32NX_FMA_EXPEDITE_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_EXPEDITE_MODE", DisplayName = "Expedite",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
        },
        // ---- PFD: flight directors (stock SimVar bool) ----
        ["FD_1"] = new SimConnect.SimVarDefinition
        {
            Name = "AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", DisplayName = "Flight director 1",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool", ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
        },
        ["FD_2"] = new SimConnect.SimVarDefinition
        {
            Name = "AUTOPILOT FLIGHT DIRECTOR ACTIVE:2", DisplayName = "Flight director 2",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool", ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
        },
        // ---- PFD: speed-tape weights (LVar; distinct PFD_ alias keys; decoded) ----
        ["PFD_VLS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_VLS", DisplayName = "VLS",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        ["PFD_VMAX"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_VMAX", DisplayName = "VMAX",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        ["PFD_GREENDOT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_GD", DisplayName = "Green dot",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        ["PFD_VF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_F", DisplayName = "F speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        ["PFD_VSLOW"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_S", DisplayName = "S speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        // ---- Takeoff V-speeds (V1/VR/V2) — entered/computed on the MCDU PERF TAKEOFF page.
        // The FBW FMC writes L:AIRLINER_V1/VR/V2_SPEED in knots (A32NX_FMCMainDisplay.ts);
        // 0 = not set. Continuous + IsAnnounced (knots) so MSFSBA AUTO-ANNOUNCES the value
        // the instant the pilot enters/changes it — "V1: 125 knots" — mirroring the Fenix
        // MCDU V-speed entry confirmation. FormatVariableValue appends "knots" from Units;
        // the simVarMonitor baseline + connect-grace keep the initial values silent; listed
        // in the Ctrl+M monitor for opt-out like every other announced var. ----
        ["PFD_V1"] = new SimConnect.SimVarDefinition
        {
            Name = "AIRLINER_V1_SPEED", DisplayName = "V1",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "knots"
        },
        ["PFD_VR"] = new SimConnect.SimVarDefinition
        {
            Name = "AIRLINER_VR_SPEED", DisplayName = "VR",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "knots"
        },
        ["PFD_V2"] = new SimConnect.SimVarDefinition
        {
            Name = "AIRLINER_V2_SPEED", DisplayName = "V2",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "knots"
        },
        // ---- PFD: alpha-protection speeds (FAC1 ARINC429 words, knots; in-flight-only ----
        // -> "not available" on the ground is CORRECT). Auto-decoded by the generic ARINC path.
        ["PFD_VALPHAPROT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FAC_1_V_ALPHA_PROT", DisplayName = "Alpha prot speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "knots", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        ["PFD_VALPHAMAX"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FAC_1_V_ALPHA_LIM", DisplayName = "Alpha max speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "knots", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        ["PFD_VSW"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FAC_1_V_STALL_WARN", DisplayName = "Stall warning speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "knots", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        // ---- PFD: transition level (LVar ARINC word; FL decoded MANUALLY, NOT IsArinc429) ----
        ["A32NX_FM1_TRANS_LVL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM1_TRANS_LVL", DisplayName = "Transition level",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        // ---- PFD: transition altitude (LVar ARINC429, feet) ----
        ["A32NX_FM1_TRANS_ALT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM1_TRANS_ALT", DisplayName = "Transition altitude",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "feet", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        // ---- PFD: SAT / TAT (ADR-1 ARINC429 words, celsius) ----
        ["A32NX_ADIRS_ADR_1_STATIC_AIR_TEMPERATURE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADR_1_STATIC_AIR_TEMPERATURE", DisplayName = "Static air temperature",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "celsius", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        ["A32NX_ADIRS_ADR_1_TOTAL_AIR_TEMPERATURE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADR_1_TOTAL_AIR_TEMPERATURE", DisplayName = "Total air temperature",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "celsius", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        // ---- PFD: FCU selected ALT + HDG (stock SimVar; decoded) ----
        ["FCU_SEL_ALT"] = new SimConnect.SimVarDefinition
        {
            Name = "AUTOPILOT ALTITUDE LOCK VAR:3", DisplayName = "FCU selected altitude",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },
        ["FCU_SEL_HDG"] = new SimConnect.SimVarDefinition
        {
            Name = "AUTOPILOT HEADING LOCK DIR", DisplayName = "FCU selected heading",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        // ---- PFD: autoland capability (LVar ARINC discrete word; FMGC_1, decoded) ----
        ["PFD_AUTOLAND"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_DISCRETE_WORD_4", DisplayName = "Autoland capability",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, Units = "number"
        },
        // ---- ND: GS / TAS / wind (ADIRS ARINC429 BNR words) ----
        ["A32NX_ADIRS_IR_1_GROUND_SPEED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_IR_1_GROUND_SPEED", DisplayName = "Ground speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "knots", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        ["A32NX_ADIRS_ADR_1_TRUE_AIRSPEED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_ADR_1_TRUE_AIRSPEED", DisplayName = "True airspeed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "knots", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        ["A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR", DisplayName = "Wind direction",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "degrees", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        ["A32NX_ADIRS_IR_1_WIND_SPEED_BNR"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ADIRS_IR_1_WIND_SPEED_BNR", DisplayName = "Wind speed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "knots", Arinc429Format = "0",
            Arinc429NotAvailableText = "not available"
        },
        // ---- ND: tuned nav-radio frequencies (stock SimVar; decoded) ----
        ["ND_VOR1_FREQ"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV ACTIVE FREQUENCY:1", DisplayName = "VOR 1 frequency",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "MHz"
        },
        ["ND_VOR2_FREQ"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV ACTIVE FREQUENCY:2", DisplayName = "VOR 2 frequency",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "MHz"
        },
        ["ND_VOR1_DME"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV DME:1", DisplayName = "DME 1",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "nautical miles"
        },
        ["ND_VOR2_DME"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV DME:2", DisplayName = "DME 2",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "nautical miles"
        },
        ["ND_ADF1_FREQ"] = new SimConnect.SimVarDefinition
        {
            Name = "ADF ACTIVE FREQUENCY:1", DisplayName = "ADF 1 frequency",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "kHz"
        },
        ["ND_ADF2_FREQ"] = new SimConnect.SimVarDefinition
        {
            Name = "ADF ACTIVE FREQUENCY:2", DisplayName = "ADF 2 frequency",
            Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "kHz"
        },

        // Auto-announce AP1/AP2 engage state so an AUTOMATIC disconnect (override,
        // failed autoland, etc.) is called out, not just a manual button press.
        // Continuous + IsAnnounced -> appears in the Ctrl+M monitor (muteable there).
        ["A32NX_AUTOPILOT_1_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_1_ACTIVE",
            DisplayName = "Autopilot 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Autopilot 1 off", [1] = "Autopilot 1 on" }
        },
        ["A32NX_AUTOPILOT_2_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_2_ACTIVE",
            DisplayName = "Autopilot 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Autopilot 2 off", [1] = "Autopilot 2 on" }
        },
        ["A32NX_FCU_DISCRETE_WORD_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_DISCRETE_WORD_1",
            DisplayName = "FCU Discrete Word 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_AIRLINER_TO_FLEX_TEMP"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AIRLINER_TO_FLEX_TEMP",
            DisplayName = "Flex Temperature",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "celsius"
        },
        ["A32NX_AUTOTHRUST_TLA:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_TLA:1",
            DisplayName = "Thrust Lever Angle 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_AUTOTHRUST_TLA:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_TLA:2",
            DisplayName = "Thrust Lever Angle 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        // Thrust-lever DETENT combos (parity with the A380 Thrust Levers panel).
        // Synthetic L:var keys — never read/written as L:vars; the set is intercepted
        // in HandleUIVariableSet, which fires THROTTLEn_AXIS_SET_EX1 with the detent's
        // axis value so the FBW throttle mapping snaps the lever to that detent.
        // Axis values are the FBW default-calibration band CENTERS (see the
        // detent handler in HandleUIVariableSet). The displayed value reflects the
        // last command, not the live lever (the live angle is in d["Thrust Levers"]).
        ["THROTTLE_ALL_DETENT"] = new SimConnect.SimVarDefinition
        {
            Name = "THROTTLE_ALL_DETENT", DisplayName = "All Thrust Levers",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsButton = false,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "Reverse", [1] = "Reverse Idle", [2] = "Idle", [3] = "Climb", [4] = "Flex/MCT", [5] = "TOGA" }
        },
        ["THROTTLE_1_DETENT"] = new SimConnect.SimVarDefinition
        {
            Name = "THROTTLE_1_DETENT", DisplayName = "Thrust Lever 1",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsButton = false,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "Reverse", [1] = "Reverse Idle", [2] = "Idle", [3] = "Climb", [4] = "Flex/MCT", [5] = "TOGA" }
        },
        ["THROTTLE_2_DETENT"] = new SimConnect.SimVarDefinition
        {
            Name = "THROTTLE_2_DETENT", DisplayName = "Thrust Lever 2",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsButton = false,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "Reverse", [1] = "Reverse Idle", [2] = "Idle", [3] = "Climb", [4] = "Flex/MCT", [5] = "TOGA" }
        },
        ["A32NX_FMGC_1_FD_ENGAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_FD_ENGAGED",
            DisplayName = "Flight Director 1 Engaged",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool"
        },
        ["A32NX_FMGC_2_FD_ENGAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_2_FD_ENGAGED",
            DisplayName = "Flight Director 2 Engaged",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool"
        },
        ["A32NX_FMGC_FLIGHT_PHASE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_FLIGHT_PHASE",
            DisplayName = "Flight Phase",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Enable continuous monitoring
            ValueDescriptions = new Dictionary<double, string>
            {
                { 0, "Preflight" },
                { 1, "Takeoff" },
                { 2, "Climb" },
                { 3, "Cruise" },
                { 4, "Descent" },
                { 5, "Approach" },
                { 6, "Go Around" },
                { 7, "Done" }
            }
        },
        ["A32NX_DMC_DISPLAYTEST:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DMC_DISPLAYTEST:1",
            DisplayName = "DMC Display Test",
            Type = SimConnect.SimVarType.LVar,
            Units = "number",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                { 0, "Inactive" },
                { 1, "Maintenance Mode active" },
                { 2, "Engineering display test in progress" }
            }
        },
        // (FM2_MINIMUM_DESCENT_ALTITUDE was a dead registration — never paneled or
        // requested; FM1 carries the entered minimums for the readout. Removed.)
        ["A32NX_FCU_EFIS_L_FD_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_FD_LIGHT_ON",
            DisplayName = "Flight Director 1 Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_R_FD_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_FD_LIGHT_ON",
            DisplayName = "Flight Director 2 Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // EFIS Baro Controls
        ["A32NX_FCU_EFIS_L_BARO_IS_INHG"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_BARO_IS_INHG",
            DisplayName = "Left inHg/hPa Toggle",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "hPa", [1] = "inHg" }
        },
        ["A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
            DisplayName = "Left Baro Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "QNH", [2] = "QFE" }
        },
        ["A32NX.FCU_EFIS_L_BARO_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_L_BARO_SET",
            DisplayName = "Left Baro Value",
            Type = SimConnect.SimVarType.Event,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["A32NX_FCU_EFIS_R_BARO_IS_INHG"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_BARO_IS_INHG",
            DisplayName = "Right inHg/hPa Toggle",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "hPa", [1] = "inHg" }
        },
        ["A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE",
            DisplayName = "Right Baro Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "QNH", [2] = "QFE" }
        },
        ["A32NX.FCU_EFIS_R_BARO_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX.FCU_EFIS_R_BARO_SET",
            DisplayName = "Right Baro Value",
            Type = SimConnect.SimVarType.Event,
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },

        // EFIS Baro Display Variables
        ["KOHLSMAN SETTING MB:1"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING MB:1",
            DisplayName = "Left Baro hPa",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "millibars"
        },
        ["KOHLSMAN SETTING HG:1"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG:1",
            DisplayName = "Left Baro inHg",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "inHg"
        },
        ["A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE",
            DisplayName = "Left Baro Display Mode",
            Type = SimConnect.SimVarType.LVar,
            // Continuous so a unit/STD change re-announces the altimeter; the custom baro
            // handler in ProcessSimVarUpdate speaks it (and suppresses the generic announce).
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "hPa", [2] = "inHg" }
        },
        // ARINC429 hPa word — the actual EFIS baro setting (same var the A380 uses).
        // Decoded + auto-announced on the captain's knob turn (custom logic), and read
        // on demand by the ReadAltimeter hotkey (output mode + B).
        ["A32NX_FCU_LEFT_EIS_BARO_HPA"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_LEFT_EIS_BARO_HPA",
            DisplayName = "Captain Altimeter",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },
        // ARINC429 IN-ACTIVE-UNIT word (0.001 resolution). REQUIRED for exact inHg
        // readouts: unlike the A380, the A32NX quantizes its _HPA word to WHOLE hPa
        // (live KORD 2026-06-12: HPA=1002.0 while this word read 29.60), so
        // converting the hPa word to inches is ±0.01 off. Cached silently; the
        // announce phrase uses it in inHg mode.
        ["A32NX_FCU_LEFT_EIS_BARO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_LEFT_EIS_BARO",
            DisplayName = "Captain Altimeter (active unit)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },
        ["KOHLSMAN SETTING MB:2"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING MB:2",
            DisplayName = "Right Baro hPa",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "millibars"
        },
        ["KOHLSMAN SETTING HG:2"] = new SimConnect.SimVarDefinition
        {
            Name = "KOHLSMAN SETTING HG:2",
            DisplayName = "Right Baro inHg",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "inHg"
        },
        ["A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE",
            DisplayName = "Right Baro Display Mode",
            Type = SimConnect.SimVarType.LVar,
            // Continuous so the F/O baro re-announces on a unit/STD change (the custom
            // F/O baro handler in ProcessSimVarUpdate speaks it + suppresses the generic).
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STD", [1] = "hPa", [2] = "inHg" }
        },
        // F/O EFIS baro (ARINC429 hPa word, same as the captain side) — auto-announced
        // on the F/O knob turn, prefixed "First Officer" so the pilot knows the side.
        ["A32NX_FCU_RIGHT_EIS_BARO_HPA"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_RIGHT_EIS_BARO_HPA",
            DisplayName = "First Officer Altimeter",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },
        // F/O in-active-unit word — see the captain entry for why (whole-hPa
        // quantization of the _HPA word makes converted inches ±0.01 off).
        ["A32NX_FCU_RIGHT_EIS_BARO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_RIGHT_EIS_BARO",
            DisplayName = "First Officer Altimeter (active unit)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },

        ["RADIO_HEIGHT"] = new SimConnect.SimVarDefinition
        {
            Name = "RADIO HEIGHT",
            DisplayName = "Radio Altitude",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },
        ["GEAR_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "GEAR POSITION",
            DisplayName = "Gear Position",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        // Continuously monitored so the 1,000-ft crossing announcer (MainForm) is fed.
        // IsAnnounced=true is required for continuous batched monitoring, but the generic
        // announce gate skips INDICATED_ALTITUDE (it's spoken by the callout announcer, not
        // as a raw "Altitude: 5234"). Still works as an OnRequest-style display var for the
        // PFD/ISIS boxes (force-read + live update both function on a continuous var).
        ["INDICATED_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "INDICATED ALTITUDE",
            DisplayName = "Indicated Altitude",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "feet"
        },
        // Indicated airspeed — surfaced in the PFD + ISIS accessible status boxes
        // (the speed "tape" a sighted pilot reads off the glass).
        ["AIRSPEED_INDICATED"] = new SimConnect.SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Indicated Airspeed",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        // Non-special alias of indicated airspeed for the PFD/ISIS status boxes. The box must
        // NOT use the key "AIRSPEED_INDICATED" because MainForm.HandleSpecialAnnouncements
        // re-announces "indicated airspeed, N kts" every time that key updates — so a panel-focus
        // request spammed the readout. This distinct key feeds the box silently; the universal
        // airspeed hotkey keeps using AIRSPEED_INDICATED. (Same pattern as the A380 PFD_GROSS_WEIGHT.)
        ["PFD_IAS"] = new SimConnect.SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Indicated Airspeed",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },

        // TAKEOFF ASSIST VARIABLES (dynamically monitored when takeoff assist is active)
        // PLANE_PITCH_DEGREES and PLANE_BANK_DEGREES now in BaseAircraftDefinition.cs
        ["PLANE_HEADING_DEGREES_MAGNETIC"] = new SimConnect.SimVarDefinition
        {
            Name = "PLANE HEADING DEGREES MAGNETIC",
            DisplayName = "Magnetic Heading",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, // Registered at startup, monitored when takeoff assist is active
            IsAnnounced = false, // Handled by TakeoffAssistManager
            Units = "radians" // Note: Despite name, returns radians!
        },

        // MONITORED VARIABLES
        ["A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            // Keep an individual data def. The FCU readout force-reads this status leg via
            // RequestVariable(forceUpdate), which NO-OPS for batch-covered vars (the SimConnect-
            // ceiling strengthening skips their individual def). Without this the HDG readout's
            // managed-status leg never arrives and the readout goes silent. (Regression fix.)
            ExcludeFromBatch = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Select heading mode", [1] = "Managed heading mode" }
        },
        ["A32NX_FCU_LOC_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_LOC_LIGHT_ON",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,  // Only announce when button pressed
            ValueDescriptions = new Dictionary<double, string> { [0] = "LOC mode off", [1] = "LOC mode on" }
        },
        ["A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true,   // see HDG_TRK_MANAGED — keep individual def for the forced readout
            ValueDescriptions = new Dictionary<double, string> { [0] = "Selected speed", [1] = "Managed speed" }
        },
        ["A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true,   // see HDG_TRK_MANAGED — keep individual def for the forced readout
            ValueDescriptions = new Dictionary<double, string> { [0] = "Selected Altitude", [1] = "Managed altitude" }
        },
        ["A32NX_FCU_EXPED_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EXPED_LIGHT_ON",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,  // Only announce when button pressed
            ValueDescriptions = new Dictionary<double, string> { [0] = "Expedite mode off", [1] = "Expedite mode on" }
        },
        ["A32NX_FCU_APPR_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_APPR_LIGHT_ON",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "APPR mode off", [1] = "APPR mode on" }
        },
        ["A32NX_FCU_AP_1_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AP_1_LIGHT_ON",
            DisplayName = "Autopilot 1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_AP_2_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AP_2_LIGHT_ON",
            DisplayName = "Autopilot 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_AUTOTHRUST_STATUS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOTHRUST_STATUS",
            DisplayName = "Autothrust",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Important AP state
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Disengaged",
                [1] = "Armed",
                [2] = "Active"
            }
        },
        ["A32NX_FCU_ATHR_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_ATHR_LIGHT_ON",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,  // Only announce when button pressed
            ValueDescriptions = new Dictionary<double, string> { [0] = "ATHR Light off", [1] = "ATHR Light on" }
        },
        // PFD/FMA Variables
        ["A32NX_FMA_VERTICAL_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_VERTICAL_MODE",
            DisplayName = "Vertical Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "NONE", [10] = "ALT", [11] = "ALT_CPT", [12] = "OP_CLB", [13] = "OP_DES",
                [14] = "VS", [15] = "FPA", [20] = "ALT_CST", [21] = "ALT_CST_CPT", [22] = "CLB",
                [23] = "DES", [24] = "FINAL", [30] = "GS_CPT", [31] = "GS_TRACK", [32] = "LAND",
                [33] = "FLARE", [34] = "ROLL_OUT", [40] = "SRS", [41] = "SRS_GA", [50] = "TCAS"
            }
        },
        ["A32NX_FMA_LATERAL_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_LATERAL_MODE",
            DisplayName = "Lateral Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "NONE", [10] = "HDG", [11] = "TRACK", [20] = "NAV", [30] = "LOC_CPT",
                [31] = "LOC_TRACK", [32] = "LAND", [33] = "FLARE", [34] = "ROLL_OUT",
                [40] = "RWY", [41] = "RWY_TRACK", [50] = "GA_TRACK"
            }
        },
        ["A32NX_FMA_LATERAL_ARMED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_LATERAL_ARMED",
            DisplayName = "Armed Lateral Modes",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },
        ["A32NX_FMA_VERTICAL_ARMED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_VERTICAL_ARMED",
            DisplayName = "Armed Vertical Modes",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },
        // MSFSBA-internal System Display page selector (the A32NX SD index is read-only,
        // so this drives the accessible status box, not the real SD). Selecting a page
        // scrapes (E/WD) or reads decoded SimVars (system pages) into the status box.
        ["A32NX_MSFSBA_SD_PAGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MSFSBA_SD_PAGE",
            DisplayName = "System Display Page",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number",
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Upper E/WD", [1] = "Electrical", [2] = "Hydraulics", [3] = "Pressurization",
                [4] = "APU", [5] = "Air Conditioning", [6] = "Wheel / Brakes", [7] = "Bleed",
                [8] = "Fuel", [9] = "Doors", [10] = "Engine", [11] = "Flight Controls"
            }
        },
        // FM minimums are ARINC429 words (FmArinc429OutputWord — live-verified 2^32
        // no-data at the gate); without the flag the announce spoke the raw word.
        ["A32NX_FM1_MINIMUM_DESCENT_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE",
            DisplayName = "Baro Minimum",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "feet",
            IsArinc429 = true, Arinc429Unit = "feet", Arinc429NotAvailableText = "Not set"
        },
        // Decision Height (CAT II/III minimums) — same ARINC FM word family; was
        // entirely missing (only MDA was read), so DH entries were silent.
        ["A32NX_FM1_DECISION_HEIGHT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM1_DECISION_HEIGHT",
            DisplayName = "Decision Height",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "feet",
            IsArinc429 = true, Arinc429Unit = "feet", Arinc429NotAvailableText = "Not set"
        },
        // Predicted takeoff pitch trim (FMS-computed THS target; ARINC degrees).
        // SIGN IS INVERTED vs the A380: the A32NX FMS writes -ths
        // (A32NX_FMCMainDisplay.ts:4133 setBnrValue(ths ? -ths : 0)), so
        // NEGATIVE = nose up. Decoded in TryGetDisplayOverride.
        ["A32NX_FM1_TO_PITCH_TRIM"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM1_TO_PITCH_TRIM",
            DisplayName = "Predicted Takeoff Pitch Trim",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        // FUEL MODE SEL pushbutton (overhead). The cockpit click toggles the L:var
        // AND routes the center-tank transfer junctions 4+5 — replicated verbatim
        // in HandleUIVariableSet (live-verified: junction 4 follows the set).
        ["A32NX_OVHD_FUEL_MODESEL_MANUAL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_FUEL_MODESEL_MANUAL",
            DisplayName = "Fuel Mode Selector",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Auto", [1] = "Manual" }
        },
        // Evacuation HORN SHUT OFF. Writing 1 silences the cockpit evac horn
        // (sound.xml plays it only while PUSH_OVHD_EVAC_HORN <= 0); re-arming the
        // EVAC COMMAND resets the L:var, so the set is one-way by design — do NOT
        // pulse it back to 0 (that would resume the horn). Synthetic Act combo.
        ["A32NX_EVAC_HORN_SHUTOFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EVAC_HORN_SHUTOFF",
            DisplayName = "Evacuation Horn Shut Off",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Silence horn" }
        },
        // Blue (yellow-named in FBW vars) electric pump OVERRIDE — the maintenance
        // panel PB that forces the blue e-pump on ground for flight-control checks.
        // Momentary press-to-toggle: the combo state reads _IS_ON and the set
        // pulses _IS_PRESSED (live-verified: separated press/release toggles the
        // latch; a same-frame 1->0 pulse is NOT seen by the Rust sampler).
        ["A32NX_OVHD_HYD_EPUMPY_OVRD_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_EPUMPY_OVRD_IS_ON",
            DisplayName = "Blue Electric Pump Override",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Override on" }
        },
        ["A32NX_DESTINATION_QNH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DESTINATION_QNH",
            DisplayName = "Destination QNH",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "millibar"
        },
        ["A32NX_PFD_MSG_SET_HOLD_SPEED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_MSG_SET_HOLD_SPEED",
            DisplayName = "Set Hold Speed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "SET HOLD SPEED"
            }
        },
        ["A32NX_PFD_MSG_TD_REACHED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_MSG_TD_REACHED",
            DisplayName = "Top of Descent Reached",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "T/D REACHED"
            }
        },
        ["A32NX_PFD_MSG_CHECK_SPEED_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_MSG_CHECK_SPEED_MODE",
            DisplayName = "Check Speed Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "CHECK SPEED MODE"
            }
        },
        ["A32NX_PFD_LINEAR_DEVIATION_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_LINEAR_DEVIATION_ACTIVE",
            DisplayName = "Vertical Deviation",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "Linear Deviation Active"
            }
        },
        ["A32NX_FMGC_L_LDEV_REQUEST"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_L_LDEV_REQUEST",   // _1_ does not exist in FBW source; _L_ is real
            DisplayName = "FMGC L DEV Request",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "L/DEV Requested"
            }
        },
        ["A32NX_FCU_AFS_DISPLAY_MACH_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_MACH_MODE",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Check after SPD/MACH toggle
            ValueDescriptions = new Dictionary<double, string> { [0] = "Mach mode off", [1] = "Mach mode on" }
        },
        ["A32NX_TRK_FPA_MODE_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TRK_FPA_MODE_ACTIVE",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,  // Check after TRK/FPA toggle
            ValueDescriptions = new Dictionary<double, string> { [0] = "HDG/VS mode", [1] = "TRK/FPA mode" }
        },
        ["A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE",
            DisplayName = "FCU VS/FPA Value",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_MASTER_CAUTION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MASTER_CAUTION",
            DisplayName = "Master Caution",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string> { [0] = "Master caution off", [1] = "Master caution on" }
        },
        ["A32NX_MASTER_WARNING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MASTER_WARNING",
            DisplayName = "Master Warning",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string> { [0] = "Master warning off", [1] = "Master warning on" }
        },

        // ECAM MESSAGE LINE VARIABLES (numeric codes that map to messages via EWDMessageLookup)
        ["A32NX_Ewd_LOWER_LEFT_LINE_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,  // Continuously monitored for real-time ECAM announcements
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_4"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_4",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_5"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_5",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_6"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_6",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_LEFT_LINE_7"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_LEFT_LINE_7",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_4"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_4",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_5"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_5",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_6"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_6",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["A32NX_Ewd_LOWER_RIGHT_LINE_7"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_Ewd_LOWER_RIGHT_LINE_7",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },

        ["A32NX_AUTOPILOT_AUTOLAND_WARNING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOPILOT_AUTOLAND_WARNING",
            DisplayName = "Autoland Warning",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string> { [0] = "Autoland warning off", [1] = "Auto land warning on" }
        },
        // NAVIGATION DISPLAY VARIABLES (for Navigation Display window - on-demand only)
        // Waypoint Information
        ["A32NX_EFIS_L_TO_WPT_IDENT_0"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_IDENT_0",
            DisplayName = "To Waypoint",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_EFIS_L_TO_WPT_IDENT_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_IDENT_1",
            DisplayName = "Waypoint Ident Part 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_EFIS_L_TO_WPT_DISTANCE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_DISTANCE",
            DisplayName = "Waypoint Distance",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "nautical miles"
        },
        ["A32NX_EFIS_L_TO_WPT_BEARING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_BEARING",
            DisplayName = "Waypoint Bearing (Magnetic)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_EFIS_L_TO_WPT_TRUE_BEARING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_TRUE_BEARING",
            DisplayName = "Waypoint Bearing (True)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_EFIS_L_TO_WPT_ETA"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_TO_WPT_ETA",
            DisplayName = "Waypoint ETA",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "seconds"
        },

        // Cross Track Error (Flight Plan Mode)
        ["A32NX_FG_CROSS_TRACK_ERROR"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FG_CROSS_TRACK_ERROR",
            DisplayName = "Cross Track Error",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "meters"
        },

        // ILS/Radio Navigation Deviation
        ["A32NX_RADIO_RECEIVER_LOC_IS_VALID"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADIO_RECEIVER_LOC_IS_VALID",
            DisplayName = "Localizer Signal Valid",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Invalid", [1] = "Valid" }
        },
        ["A32NX_RADIO_RECEIVER_LOC_DEVIATION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
            DisplayName = "Localizer Deviation",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },
        ["A32NX_RADIO_RECEIVER_GS_IS_VALID"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADIO_RECEIVER_GS_IS_VALID",
            DisplayName = "Glideslope Signal Valid",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Invalid", [1] = "Valid" }
        },
        ["A32NX_RADIO_RECEIVER_GS_DEVIATION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RADIO_RECEIVER_GS_DEVIATION",
            DisplayName = "Glideslope Deviation",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "degrees"
        },

        // Navigation Display Settings (Read-only status)
        ["A32NX_EFIS_L_ND_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_ND_MODE",
            DisplayName = "ND Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "ROSE ILS", [1] = "ROSE VOR", [2] = "ROSE NAV", [3] = "ARC", [4] = "PLAN"
            }
        },
        ["A32NX_EFIS_L_ND_RANGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_ND_RANGE",
            DisplayName = "ND Range",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "10 NM", [1] = "20 NM", [2] = "40 NM", [3] = "80 NM", [4] = "160 NM", [5] = "320 NM"
            }
        },

        // EFIS Control Variables (Writable - for changing ND mode/range)
        ["A32NX_FCU_EFIS_L_EFIS_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_EFIS_MODE",
            DisplayName = "EFIS Mode Control",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "ROSE ILS", [1] = "ROSE VOR", [2] = "ROSE NAV", [3] = "ARC", [4] = "PLAN"
            }
        },
        ["A32NX_FCU_EFIS_L_EFIS_RANGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_EFIS_RANGE",
            DisplayName = "EFIS Range Control",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "10 NM", [1] = "20 NM", [2] = "40 NM", [3] = "80 NM", [4] = "160 NM", [5] = "320 NM"
            }
        },
        // First Officer EFIS mode/range knobs (mirror of the captain's; the FCU knob
        // var is the settable control — the A32NX_EFIS_R_ND_* vars are computed display
        // outputs that revert, verified live). LS (ILS) buttons per side are directly
        // settable L:vars (held a write live).
        ["A32NX_FCU_EFIS_R_EFIS_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_EFIS_MODE", DisplayName = "EFIS Mode Control",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "ROSE ILS", [1] = "ROSE VOR", [2] = "ROSE NAV", [3] = "ARC", [4] = "PLAN" }
        },
        ["A32NX_FCU_EFIS_R_EFIS_RANGE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_EFIS_RANGE", DisplayName = "EFIS Range Control",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "10 NM", [1] = "20 NM", [2] = "40 NM", [3] = "80 NM", [4] = "160 NM", [5] = "320 NM" }
        },
        // LS (ILS) pushbutton per side — dev FCU: control = A32NX.FCU_EFIS_*_LS_PUSH
        // input event, state = the FCU's *_LS_LIGHT_ON output. The old
        // A32NX_EFIS_*_LS_BUTTON_IS_ON L:var no longer exists (held writes but drove
        // nothing — dead-var trap).
        ["A32NX_EFIS_L_LS_BUTTON_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_LS_LIGHT_ON", DisplayName = "ILS",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_EFIS_R_LS_BUTTON_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_LS_LIGHT_ON", DisplayName = "ILS",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // EFIS NAVAID selectors (ADF/OFF/VOR x2 per side): the FCU_EFIS_* variants are
        // the live knob inputs (read every frame by the FCU model) — the bare
        // A32NX_EFIS_* NAVAID vars are computed outputs and stay read-only.
        ["A32NX_FCU_EFIS_L_NAVAID_1_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_NAVAID_1_MODE", DisplayName = "Navaid 1 Selector",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ADF", [2] = "VOR" }
        },
        ["A32NX_FCU_EFIS_L_NAVAID_2_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_NAVAID_2_MODE", DisplayName = "Navaid 2 Selector",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ADF", [2] = "VOR" }
        },
        ["A32NX_FCU_EFIS_R_NAVAID_1_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_NAVAID_1_MODE", DisplayName = "Navaid 1 Selector",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ADF", [2] = "VOR" }
        },
        ["A32NX_FCU_EFIS_R_NAVAID_2_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_NAVAID_2_MODE", DisplayName = "Navaid 2 Selector",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ADF", [2] = "VOR" }
        },

        // EFIS filter-light readbacks (read-only state for each side/filter)
        ["A32NX_FCU_EFIS_L_CSTR_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_CSTR_LIGHT_ON", DisplayName = "CSTR Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_L_WPT_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_WPT_LIGHT_ON", DisplayName = "WPT Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_L_VORD_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_VORD_LIGHT_ON", DisplayName = "VORD Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_L_NDB_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_NDB_LIGHT_ON", DisplayName = "NDB Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_L_ARPT_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_ARPT_LIGHT_ON", DisplayName = "ARPT Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_R_CSTR_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_CSTR_LIGHT_ON", DisplayName = "CSTR Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_R_WPT_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_WPT_LIGHT_ON", DisplayName = "WPT Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_R_VORD_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_VORD_LIGHT_ON", DisplayName = "VORD Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_R_NDB_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_NDB_LIGHT_ON", DisplayName = "NDB Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FCU_EFIS_R_ARPT_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_ARPT_LIGHT_ON", DisplayName = "ARPT Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Navigation Performance
        ["A32NX_FMGC_L_RNP"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_L_RNP",
            DisplayName = "Required Navigation Performance",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },

        // Approach Messages (encoded strings)
        ["A32NX_EFIS_L_APPR_MSG_0"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_APPR_MSG_0",
            DisplayName = "Approach Message",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_EFIS_L_APPR_MSG_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_APPR_MSG_1",
            DisplayName = "Approach Message Part 2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },

        // FM Message Flags
        ["A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS",
            DisplayName = "ND FM Messages",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "number"
        },

        // Vertical Navigation
        ["A32NX_PFD_TARGET_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_TARGET_ALTITUDE",
            DisplayName = "Target Altitude",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },
        ["A32NX_PFD_VERTICAL_PROFILE_LATCHED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_PFD_VERTICAL_PROFILE_LATCHED",
            DisplayName = "Vertical Profile Latched",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not Latched", [1] = "Latched" }
        },

        // Master Warning / Caution ACKNOWLEDGE — momentary push-BUTTONS (mirrors the A380's
        // four Btn acks). Pressing pulses the real glareshield PB L:var 1->0 (handled in
        // HandleUIVariableSet) to acknowledge + silence the aural. Captain (_L) + First Officer
        // (_R); the FWS ORs both sides (PseudoFWC.ts reads all four).
        // SOURCE-VERIFIED 2026-06-04: the warning var is PUSH_AUTOPILOT_MASTER**A**WARN_L/_R
        // (extra A) — the A320_NEO_INTERIOR.xml cockpit model WRITES it and PseudoFWC.ts:2375
        // READS it. The old "no A" PUSH_AUTOPILOT_MASTERWARN_L was a DEAD var (ack never
        // cleared the warning). Caution is PUSH_AUTOPILOT_MASTERCAUT_L/_R (no A — correct).
        ["CLEAR_MASTER_WARNING"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_AUTOPILOT_MASTERAWARN_L",
            DisplayName = "Master Warning Acknowledge (Capt)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" }
        },
        ["CLEAR_MASTER_WARNING_FO"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_AUTOPILOT_MASTERAWARN_R",
            DisplayName = "Master Warning Acknowledge (First Officer)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" }
        },
        ["CLEAR_MASTER_CAUTION"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_AUTOPILOT_MASTERCAUT_L",
            DisplayName = "Master Caution Acknowledge (Capt)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" }
        },
        ["CLEAR_MASTER_CAUTION_FO"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_AUTOPILOT_MASTERCAUT_R",
            DisplayName = "Master Caution Acknowledge (First Officer)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" }
        },
        ["A32NX_AUTOBRAKES_ARMED_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOBRAKES_ARMED_MODE",
            DisplayName = "Autobrake Status",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "DIS",
                [1] = "LO",
                [2] = "MED",
                [3] = "MAX"
            }
        },
        ["A32NX_AUTOBRAKES_ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOBRAKES_ACTIVE",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Important when braking
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not Braking", [1] = "Braking" }
        },
        ["A32NX_AUTOBRAKES_DECEL_LIGHT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AUTOBRAKES_DECEL_LIGHT",
            DisplayName = "Autobrake Decel Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical for landing phase
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Autobrakes Decel Light Off",
                [1] = "Autobrakes Decel Light On"
            }
        },


        // (Removed the FUELSYSTEM PUMP ACTIVE:2/5/3/6 and VALVE OPEN:9/10 auto-announce
        // monitors — the new Fuel Pump L1/L2/R1/R2/C1/C2 combos above are Continuous +
        // announced on the commanded switch, so those running-state monitors would
        // double-speak. The crossfeed VALVE OPEN:3 monitor is kept — crossfeed stays a
        // momentary button, not a combo.)
        ["FUELSYSTEM VALVE OPEN:3"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE OPEN:3",
            DisplayName = "Crossfeed Valve",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Crossfeed Valve Closed", [1] = "Crossfeed Valve Open" }
        },
        // Stock per-tank fuel quantities (gallons base unit) for the SD FUEL page — the
        // row formatter converts gallons → weight via FUEL WEIGHT PER GALLON and follows
        // the metric toggle. Pre-declared (not auto-registered) so the Units are gallons,
        // not the auto-loop's "number".
        ["FUEL TANK LEFT AUX QUANTITY"] = new SimConnect.SimVarDefinition
        {
            Name = "FUEL TANK LEFT AUX QUANTITY",
            DisplayName = "Left outer tank",
            Type = SimConnect.SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["FUEL TANK LEFT MAIN QUANTITY"] = new SimConnect.SimVarDefinition
        {
            Name = "FUEL TANK LEFT MAIN QUANTITY",
            DisplayName = "Left inner tank",
            Type = SimConnect.SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["FUEL TANK CENTER QUANTITY"] = new SimConnect.SimVarDefinition
        {
            Name = "FUEL TANK CENTER QUANTITY",
            DisplayName = "Center tank",
            Type = SimConnect.SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["FUEL TANK RIGHT MAIN QUANTITY"] = new SimConnect.SimVarDefinition
        {
            Name = "FUEL TANK RIGHT MAIN QUANTITY",
            DisplayName = "Right inner tank",
            Type = SimConnect.SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["FUEL TANK RIGHT AUX QUANTITY"] = new SimConnect.SimVarDefinition
        {
            Name = "FUEL TANK RIGHT AUX QUANTITY",
            DisplayName = "Right outer tank",
            Type = SimConnect.SimVarType.SimVar,
            Units = "gallons",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["ANTISKID BRAKES ACTIVE"] = new SimConnect.SimVarDefinition
        {
            Name = "ANTISKID BRAKES ACTIVE",
            DisplayName = "Anti-skid",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_TOTAL_FUEL_QUANTITY"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TOTAL_FUEL_QUANTITY",
            DisplayName = "Total Fuel Quantity",
            Type = SimConnect.SimVarType.LVar,
            Units = "kilograms",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },

        // PEDESTAL SECTION - RMP Panel. The A32NX RMP drives the STOCK COM radios
        // (its React panel writes K:COM*_STBY_RADIO_SET_HZ / reads the stock simvars),
        // so the stock set + swap events work here — live-verified 2026-06 (unlike the
        // A380, whose RMP ignores them). The *_SET keys render numeric input fields;
        // set logic (validate MHz → Hz event, active = set-standby + swap) lives in
        // HandleUIVariableSet so the auto-announce below is the single confirmation.
        ["COM_ACTIVE_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:1",
            DisplayName = "COM 1 Set Active Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_STANDBY_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:1",
            DisplayName = "COM 1 Set Standby Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM1_RADIO_SWAP"] = new SimConnect.SimVarDefinition
        {
            Name = "COM1_RADIO_SWAP",
            DisplayName = "COM 1 XFER Frequency",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX_RMP_L_TOGGLE_SWITCH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_L_TOGGLE_SWITCH",
            DisplayName = "RMP ON/OFF",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_RMP_L_SELECTED_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_L_SELECTED_MODE",
            DisplayName = "RMP Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string>
            {
                [1] = "VHF1", [2] = "VHF2", [3] = "VHF3", [6] = "VOR", [7] = "ILS", [8] = "GLS"
            }
        },
        // Active + standby freqs are Continuous + IsAnnounced so frequency changes and
        // swaps auto-announce (Fenix/A380 parity); the announce itself is formatted in
        // ProcessSimVarUpdate ("COM 1 active 121.500"), seeded silently on first read.
        ["COM_ACTIVE_FREQUENCY:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:1",
            DisplayName = "COM 1 Active Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["COM_STANDBY_FREQUENCY:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:1",
            DisplayName = "COM 1 Standby Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        // ---- COM2 / VHF2 (parity: the Radios panel now exposes COM1 + COM2) ----
        ["COM_ACTIVE_FREQUENCY_SET:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:2", DisplayName = "COM 2 Set Active Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_STANDBY_FREQUENCY_SET:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:2", DisplayName = "COM 2 Set Standby Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM2_RADIO_SWAP"] = new SimConnect.SimVarDefinition
        {
            Name = "COM2_RADIO_SWAP", DisplayName = "COM 2 XFER Frequency",
            Type = SimConnect.SimVarType.Event
        },
        ["COM_ACTIVE_FREQUENCY:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:2", DisplayName = "COM 2 Active Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["COM_STANDBY_FREQUENCY:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:2", DisplayName = "COM 2 Standby Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        // COM3 / VHF3 — mirrors the COM2 pattern above. Stock events COM3_STBY_RADIO_SET_HZ /
        // COM3_RADIO_SWAP; HandleUIVariableSet already branches on idx "3" (varKey.EndsWith(":3")).
        ["COM_ACTIVE_FREQUENCY_SET:3"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:3", DisplayName = "COM 3 Set Active Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_STANDBY_FREQUENCY_SET:3"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:3", DisplayName = "COM 3 Set Standby Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM3_RADIO_SWAP"] = new SimConnect.SimVarDefinition
        {
            Name = "COM3_RADIO_SWAP", DisplayName = "COM 3 XFER Frequency",
            Type = SimConnect.SimVarType.Event
        },
        ["COM_ACTIVE_FREQUENCY:3"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:3", DisplayName = "COM 3 Active Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        ["COM_STANDBY_FREQUENCY:3"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:3", DisplayName = "COM 3 Standby Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        // Transmit selectors are Continuous + IsAnnounced: ProcessSimVarUpdate speaks
        // "Transmitting on VHF n" when the mic moves to a radio (rising edge only).
        ["COM_TRANSMIT:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:1",
            DisplayName = "VHF1 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
        },
        ["COM_TRANSMIT:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:2",
            DisplayName = "VHF2 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
        },
        ["COM_TRANSMIT:3"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:3",
            DisplayName = "VHF3 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
        },

        // FCU READOUT VALUES (for hotkeys)
        ["A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "FCU Heading",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "FCU Speed",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_FCU_AFS_DISPLAY_ALT_VALUE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_ALT_VALUE",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "FCU Altitude",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },

        // SPEED TAPE VALUES (for hotkey readouts)
        ["A32NX_SPEEDS_GD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_GD",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "Green Dot Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_SPEEDS_S"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_S",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "S Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_SPEEDS_F"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_F",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "F Speed",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_SPEEDS_VLS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_VLS",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "VLS (lowest selectable)",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_SPEEDS_VS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SPEEDS_VS",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "V S (stall)",
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },

        // WIND INFORMATION (for hotkey readouts)
        ["AMBIENT_WIND_DIRECTION"] = new SimConnect.SimVarDefinition
        {
            Name = "AMBIENT WIND DIRECTION",
            DisplayName = "Wind Direction",
            Type = SimConnect.SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },
        ["AMBIENT_WIND_VELOCITY"] = new SimConnect.SimVarDefinition
        {
            Name = "AMBIENT WIND VELOCITY",
            DisplayName = "Wind Speed",
            Type = SimConnect.SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        },

        // FLIGHT CONTROLS - Flaps
        ["A32NX_FLAPS_HANDLE_INDEX"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FLAPS_HANDLE_INDEX",
            DisplayName = "Flaps",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical for landing/takeoff
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Up",
                [1] = "1",
                [2] = "2",
                [3] = "3",
                [4] = "Full"
            }
        },

        // SFCC actual surface positions (ARINC429 degrees) — confirms config compliance
        // and surfaces jams/asymmetry a sighted pilot reads off the E/WD flap indicator.
        ["A32NX_SFCC_1_FLAP_ACTUAL_POSITION_WORD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SFCC_1_FLAP_ACTUAL_POSITION_WORD", DisplayName = "Flaps Actual Position",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "degrees", Arinc429Format = "0.0"
        },
        ["A32NX_SFCC_1_SLAT_ACTUAL_POSITION_WORD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SFCC_1_SLAT_ACTUAL_POSITION_WORD", DisplayName = "Slats Actual Position",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "degrees", Arinc429Format = "0.0"
        },

        // GPWS Master Warning
        ["A32NX_GPWS_WARNING_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_WARNING_LIGHT_ON",
            DisplayName = "Master GPWS Warning Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On"
            }
        },

        // GPWS amber alert light (incl. glideslope mode 5)
        ["A32NX_GPWS_ALERT_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_ALERT_LIGHT_ON",
            DisplayName = "GPWS Alert Light",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,  // Critical alert
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Off",
                [1] = "On"
            }
        },

        // FLIGHT CONTROLS - Sidestick
        ["A32NX_SIDESTICK_POSITION_X"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SIDESTICK_POSITION_X",
            DisplayName = "Sidestick Roll",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            IsAnnounced = false,
            Units = "number"
        },
        ["A32NX_SIDESTICK_POSITION_Y"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SIDESTICK_POSITION_Y",
            DisplayName = "Sidestick Pitch",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            IsAnnounced = false,
            Units = "number"
        },

        // ENGINE PARAMETERS - Engine 1
        ["A32NX_ENGINE_N1:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_N1:1",
            DisplayName = "N1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_ENGINE_N2:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_N2:1",
            DisplayName = "N2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_ENGINE_EGT:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_EGT:1",
            DisplayName = "EGT",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "celsius"
        },
        ["A32NX_ENGINE_FF:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_FF:1",
            DisplayName = "Fuel Flow",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "kg per hour"
        },
        ["A32NX_ENGINE_OIL_QTY:1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_OIL_QTY:1",
            DisplayName = "Oil Quantity",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },

        // ENGINE PARAMETERS - Engine 2
        ["A32NX_ENGINE_N1:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_N1:2",
            DisplayName = "N1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_ENGINE_N2:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_N2:2",
            DisplayName = "N2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "percent"
        },
        ["A32NX_ENGINE_EGT:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_EGT:2",
            DisplayName = "EGT",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "celsius"
        },
        ["A32NX_ENGINE_FF:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_FF:2",
            DisplayName = "Fuel Flow",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "kg per hour"
        },
        ["A32NX_ENGINE_OIL_QTY:2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGINE_OIL_QTY:2",
            DisplayName = "Oil Quantity",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },

        // ---- Clock panel (parity with the A380 Instrument > Clock) ----
        // CHR start/stop = H-event A32NX_CHRONO_TOGGLE; reset = H-event A32NX_CHRONO_RST
        // (both fired in HandleUIVariableSet). ET counter knob = A32NX_CHRONO_ET_SWITCH_POS
        // (settable L:var). Elapsed times in seconds (-1 = blank), formatted in
        // TryGetDisplayOverride.
        ["A32NX_MSFSBA_CHRONO_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MSFSBA_CHRONO_TOGGLE", DisplayName = "Chronometer Start/Stop",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        ["A32NX_MSFSBA_CHRONO_RESET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MSFSBA_CHRONO_RESET", DisplayName = "Chronometer Reset",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        ["A32NX_CHRONO_ET_SWITCH_POS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CHRONO_ET_SWITCH_POS", DisplayName = "Elapsed Time",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Run", [1] = "Stop", [2] = "Reset" }
        },
        ["A32NX_CHRONO_ELAPSED_TIME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CHRONO_ELAPSED_TIME", DisplayName = "Chronometer",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "seconds"
        },
        ["A32NX_CHRONO_ET_ELAPSED_TIME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CHRONO_ET_ELAPSED_TIME", DisplayName = "Elapsed Time",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, Units = "seconds"
        },

        // ---- Bleed panel (matches the ECAM BLEED System Display page) ----
        // All verified live. XBLEED/PACKFLOW are A32NX_KNOB_* selectors; eng bleeds +
        // hot-air + ram-air are PBs. Settable via the calculator-path catch-all.
        ["A32NX_OVHD_PNEU_ENG_1_BLEED_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PNEU_ENG_1_BLEED_PB_IS_AUTO", DisplayName = "Engine 1 Bleed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_PNEU_ENG_2_BLEED_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PNEU_ENG_2_BLEED_PB_IS_AUTO", DisplayName = "Engine 2 Bleed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position", DisplayName = "Cross Bleed",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Shut", [1] = "Auto", [2] = "Open" }
        },
        ["A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position", DisplayName = "Pack Flow",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Low", [1] = "Normal", [2] = "High" }
        },
        ["A32NX_OVHD_COND_HOT_AIR_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_COND_HOT_AIR_PB_IS_ON", DisplayName = "Hot Air",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // Ram Air: the A32NX control var is A32NX_AIRCOND_RAMAIR_TOGGLE (read by PseudoFWC); the A380-style _PB_IS_ON name does not exist on this aircraft.
        ["A32NX_AIRCOND_RAMAIR_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AIRCOND_RAMAIR_TOGGLE", DisplayName = "Ram Air",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ---- Source Switching panel (parity with A380 Instrument > Source Switching) ----
        ["A32NX_ATT_HDG_SWITCHING_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ATT_HDG_SWITCHING_KNOB", DisplayName = "Attitude / Heading Source",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Captain on 3", [1] = "Normal", [2] = "First Officer on 3" }
        },
        ["A32NX_AIR_DATA_SWITCHING_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AIR_DATA_SWITCHING_KNOB", DisplayName = "Air Data Source",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Captain on 3", [1] = "Normal", [2] = "First Officer on 3" }
        },
        ["A32NX_EIS_DMC_SWITCHING_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EIS_DMC_SWITCHING_KNOB", DisplayName = "EIS / DMC Source",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Captain on 3", [1] = "Normal", [2] = "First Officer on 3" }
        },
        ["A32NX_FMGC_TRUE_REF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_TRUE_REF", DisplayName = "Heading Reference",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Magnetic", [1] = "True" }
        },
        ["A32NX_ECAM_ND_XFR_SWITCHING_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECAM_ND_XFR_SWITCHING_KNOB", DisplayName = "ECAM ND XFR",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Captain", [1] = "Normal", [2] = "First Officer" }
        },

        // ---- Pressurization panel (parity with A380 Overhead > Pressurization) ----
        ["A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO", DisplayName = "Pressurization Mode",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Manual", [1] = "Auto" }
        },
        // Manual cabin V/S control switch — used when MODE SEL is in Manual: drives the
        // outflow valve (Up = valve opens / cabin climbs, Down = valve closes / cabin
        // descends). 3-position latching switch (0=Up, 1=Neutral, 2=Down — FBW a320-simvars).
        // Was missing from the A320 panel (only Mode + Ditching were exposed). Settable +
        // latching, verified live (separate-eval read-back). Auto-announce + Ctrl+M via the loop.
        ["A32NX_OVHD_PRESS_MAN_VS_CTL_SWITCH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_MAN_VS_CTL_SWITCH", DisplayName = "Manual Cabin V/S Control",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Up", [1] = "Neutral", [2] = "Down" }
        },
        ["A32NX_OVHD_PRESS_DITCHING_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_DITCHING_PB_IS_ON", DisplayName = "Ditching",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // LDG ELEV knob: Rust ValueKnob input read every frame (air_conditioning.rs:987).
        // -4000 = AUTO detent, else landing elevation in feet.
        ["PRESS_LDG_ELEV_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_LDG_ELEV_KNOB", DisplayName = "Landing Elevation (feet, -4000 = Auto)",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },

        // ---- Ventilation panel (parity with A380 Overhead > Ventilation) ----
        // CORRECTNESS FIX (A380-parity check): the avionics blower/extract controls used
        // A32NX_OVHD_VENT_{BLOWER,EXTRACT}_PB_IS_ON, which DO NOT EXIST (not in a320-simvars.md
        // nor the cockpit) — the control wrote a dead var and did nothing. The real cockpit
        // controls are A32NX_VENTILATION_{BLOWER,EXTRACT}_TOGGLE (TOGGLE_SIMVAR; live-confirmed
        // the old var read 0 while the real one read 1). 0=Off, 1=Auto per the cockpit tooltip.
        ["A32NX_VENTILATION_BLOWER_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_VENTILATION_BLOWER_TOGGLE", DisplayName = "Blower",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_VENTILATION_EXTRACT_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_VENTILATION_EXTRACT_TOGGLE", DisplayName = "Extract",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON", DisplayName = "Cabin Fans",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ---- Flight Control Computers panel (parity with A380 Overhead > FCC) ----
        // A32NX uses ELAC 1/2, SEC 1/2/3, FAC 1/2 (pushbutton pressed = computer on).
        ["A32NX_ELAC_1_PUSHBUTTON_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELAC_1_PUSHBUTTON_PRESSED", DisplayName = "ELAC 1",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ELAC_2_PUSHBUTTON_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELAC_2_PUSHBUTTON_PRESSED", DisplayName = "ELAC 2",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_SEC_1_PUSHBUTTON_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SEC_1_PUSHBUTTON_PRESSED", DisplayName = "SEC 1",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_SEC_2_PUSHBUTTON_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SEC_2_PUSHBUTTON_PRESSED", DisplayName = "SEC 2",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_SEC_3_PUSHBUTTON_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SEC_3_PUSHBUTTON_PRESSED", DisplayName = "SEC 3",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FAC_1_PUSHBUTTON_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FAC_1_PUSHBUTTON_PRESSED", DisplayName = "FAC 1",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_FAC_2_PUSHBUTTON_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FAC_2_PUSHBUTTON_PRESSED", DisplayName = "FAC 2",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ---- Recorder and Misc panel (parity with A380 Overhead > Recorder and Misc) ----
        // Avionics-compartment ventilation light (Auto/On). Latching toggle L:var
        // (TOGGLE_SIMVAR in the cockpit XML; tooltip "Set to AUTO" / "Turn ON" → 0=Auto,
        // 1=On). FBW-flagged Inop. but the switch state is real; settable via the calc-path
        // catch-all + auto-announce via the loop.
        ["A32NX_AVIONICS_COMPLT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AVIONICS_COMPLT_ON", DisplayName = "Avionics Compartment",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Auto", [1] = "On" }
        },
        // ---- Source-grounded overhead control additions (2026-06-04) — clickable A32NX
        // cockpit controls (A320_NEO_INTERIOR.xml) absent from MSFSBA; all write-stick
        // verified live + settable via the A32NX_ calc-path catch-all + auto-announce loop. ----
        // ELEC: galley/cabin power + emergency-generator-1 line.
        ["A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO", DisplayName = "Galley and Cabin",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON", DisplayName = "Emergency Generator 1 Line",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // HYD: leak-measurement valves (Green/Blue/Yellow). TOGGLE_SIMVAR _PB_IS_AUTO → Off/Auto.
        ["A32NX_OVHD_HYD_LEAK_MEASUREMENT_G_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_LEAK_MEASUREMENT_G_PB_IS_AUTO", DisplayName = "Green Leak Measurement",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_HYD_LEAK_MEASUREMENT_B_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_LEAK_MEASUREMENT_B_PB_IS_AUTO", DisplayName = "Blue Leak Measurement",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_HYD_LEAK_MEASUREMENT_Y_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_LEAK_MEASUREMENT_Y_PB_IS_AUTO", DisplayName = "Yellow Leak Measurement",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        // Evacuation Capt/Purser selector (0=Purser, 1=Capt and Purser — from FBW + A380 def).
        ["A32NX_EVAC_CAPT_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EVAC_CAPT_TOGGLE", DisplayName = "Evacuation Capt / Purser",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Purser", [1] = "Capt and Purser" }
        },
        // Recorder & Misc: DFDR event, crew headset (parity with the A380).
        ["A32NX_DFDR_EVENT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DFDR_EVENT_ON", DisplayName = "DFDR Event",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_CREW_HEAD_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CREW_HEAD_SET", DisplayName = "Crew Headset",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        // APU auto-exit RESET — momentary held button (HOLD_SIMVAR, pairs with the existing TEST).
        ["A32NX_APU_AUTOEXITING_RESET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_APU_AUTOEXITING_RESET", DisplayName = "APU Auto Exit Reset",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        // ---- Fault annunciators (auto-announce-only, in Ctrl+M) — cockpit-driven panel
        // FAULT lights (A320_NEO_INTERIOR.xml lamp conditions) the A320 def did not surface.
        // Continuous + IsAnnounced so they speak on the fault transition (silent at baseline);
        // not in panel lists (faults aren't navigable controls — the A380 ReadEnum pattern). ----
        ["A32NX_AIRCOND_RAMAIR_FAULT"] = Fault("A32NX_AIRCOND_RAMAIR_FAULT", "Ram Air Fault"),
        ["A32NX_GPWS_SYS_FAULT"] = Fault("A32NX_GPWS_SYS_FAULT", "GPWS System Fault"),
        ["A32NX_GPWS_TERR_FAULT"] = Fault("A32NX_GPWS_TERR_FAULT", "GPWS Terrain Fault"),
        ["A32NX_OVHD_APU_MASTER_SW_PB_HAS_FAULT"] = Fault("A32NX_OVHD_APU_MASTER_SW_PB_HAS_FAULT", "APU Master Fault"),
        ["A32NX_OVHD_COND_HOT_AIR_PB_HAS_FAULT"] = Fault("A32NX_OVHD_COND_HOT_AIR_PB_HAS_FAULT", "Hot Air Fault"),
        ["A32NX_OVHD_COND_PACK_1_PB_HAS_FAULT"] = Fault("A32NX_OVHD_COND_PACK_1_PB_HAS_FAULT", "Pack 1 Fault"),
        ["A32NX_OVHD_COND_PACK_2_PB_HAS_FAULT"] = Fault("A32NX_OVHD_COND_PACK_2_PB_HAS_FAULT", "Pack 2 Fault"),
        ["A32NX_OVHD_ELEC_AC_ESS_FEED_PB_HAS_FAULT"] = Fault("A32NX_OVHD_ELEC_AC_ESS_FEED_PB_HAS_FAULT", "AC Essential Feed Fault"),
        ["A32NX_OVHD_ELEC_GALY_AND_CAB_PB_HAS_FAULT"] = Fault("A32NX_OVHD_ELEC_GALY_AND_CAB_PB_HAS_FAULT", "Galley and Cabin Fault"),
        ["A32NX_OVHD_ELEC_IDG_1_PB_HAS_FAULT"] = Fault("A32NX_OVHD_ELEC_IDG_1_PB_HAS_FAULT", "IDG 1 Fault"),
        ["A32NX_OVHD_ELEC_IDG_2_PB_HAS_FAULT"] = Fault("A32NX_OVHD_ELEC_IDG_2_PB_HAS_FAULT", "IDG 2 Fault"),
        ["A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_HAS_FAULT"] = Fault("A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_HAS_FAULT", "Emergency Generator 1 Line Fault"),
        ["A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_HAS_FAULT"] = Fault("A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_HAS_FAULT", "RAT and Emergency Gen Fault"),
        ["A32NX_OVHD_PRESS_MODE_SEL_PB_HAS_FAULT"] = Fault("A32NX_OVHD_PRESS_MODE_SEL_PB_HAS_FAULT", "Pressurization Mode Fault"),
        ["A32NX_VENTILATION_BLOWER_FAULT"] = Fault("A32NX_VENTILATION_BLOWER_FAULT", "Blower Fault"),
        ["A32NX_VENTILATION_EXTRACT_FAULT"] = Fault("A32NX_VENTILATION_EXTRACT_FAULT", "Extract Fault"),
        // RAT (ram-air-turbine) emergency deploys — momentary guarded pushbuttons (the A380
        // exposes these as Btn; _IS_PRESSED is the momentary press). Real emergency controls.
        ["A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED", DisplayName = "RAT and Emergency Generator Deploy",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        ["A32NX_OVHD_HYD_RAT_MAN_ON_IS_PRESSED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_RAT_MAN_ON_IS_PRESSED", DisplayName = "RAT Manual On",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },

        // ---- Interior Lighting panel (parity with A380 Overhead > Interior Lighting) ----
        ["A32NX_OVHD_INTLT_ANN"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_INTLT_ANN", DisplayName = "Annunciator Lights",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Test", [1] = "Bright", [2] = "Dim" }
        },
        // Dome light: stock 3-state switch — state = LIGHT POTENTIOMETER:7 (0 off /
        // 20 dim / 100 bright), set = CABIN_LIGHTS_SET + LIGHT_POTENTIOMETER_7_SET
        // (A320_NEO_INTERIOR.xml:2153). The A32NX_OVHD_INTLT_DOME L:var exists only on
        // the A380 — the KEY is kept for panel-list stability; the backing var is stock.
        ["A32NX_OVHD_INTLT_DOME"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:7", DisplayName = "Dome Light",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [20] = "Dim", [100] = "Bright" }
        },
        ["A32NX_STBY_COMPASS_LIGHT_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STBY_COMPASS_LIGHT_TOGGLE", DisplayName = "Standby Compass Light",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ---- Panel brightness knobs — stock potentiometers (A320_NEO_INTERIOR.xml). ----
        // Pedestal flood = 76, main panel flood = 85, glareshield flood Capt = 10 / FO = 11,
        // glareshield integral = 83, overhead integral = 86. Written via the indexed
        // LIGHT_POTENTIOMETER_SET event (2-arg calc path). Range 0-100 percent.
        ["BRIGHT_PEDESTAL_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:76", DisplayName = "Pedestal Flood Brightness (0-100)",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["BRIGHT_MAINPANEL_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:85", DisplayName = "Main Panel Flood Brightness (0-100)",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["BRIGHT_GLARESHIELD_CAPT_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:10", DisplayName = "Glareshield Flood Captain Brightness (0-100)",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["BRIGHT_GLARESHIELD_FO_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:11", DisplayName = "Glareshield Flood First Officer Brightness (0-100)",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["BRIGHT_GLARESHIELD_INTEG_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:83", DisplayName = "Glareshield Integral Brightness (0-100)",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["BRIGHT_OVERHEAD_INTEG_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:86", DisplayName = "Overhead Integral Brightness (0-100)",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },

        // ---- DOORS — read-only auto-announced status (parity with A380, NO combos). ----
        // The user opens/closes doors via the flyPad Ground page; MSFSBA only ANNOUNCES
        // each open/closed transition (ProcessSimVarUpdate) and renders the state in the
        // SD DOOR page / TryGetDisplayOverride. Settable combos + the TOGGLE_AIRCRAFT_EXIT
        // handler were removed — the passenger doors all share the base SimVar name
        // "INTERACTIVE POINT OPEN" in the control-update path, which garbled a settable
        // combo; a read-only status keyed on the distinct MSFSBA var key is correct.
        // Passenger doors = stock SimVar INTERACTIVE POINT OPEN:0..3 (0..1 fraction, open
        // > 0.05). Cargo doors = FBW L-vars *_DOOR_CARGO_LOCKED (bool, INVERTED:
        // 1 = locked = Closed, < 0.5 = Open) — FBW does NOT model cargo via interactive
        // points 4/5. Stock SimVar names contain a space+colon, so they MUST register as
        // Type = SimVar (the colon-named SimVar through the L:var path is the documented
        // "not connected" crash). See _doorDefs for the authoritative map.
        // (registered via the _doorDefs loop below the dictionary)

        // ---- Jet-bridge MOTION readout (parity with A380). Ground-service ACTION combos
        // (jetway/stairs toggles) were removed — the flyPad Ground page is the interface.
        // Only the jet-bridge MOTION stays so a blind pilot hears Moving/Stopped after a
        // flyPad jet-bridge call (stock SimVar — the only readable jetway state). ----
        ["JETWAY_MOVING_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "JETWAY MOVING", DisplayName = "Jet Bridge",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "stopped", [1] = "moving" }
        },

        // ---- Door slides armed — standalone auto-announced readout (parity with A380). ----
        ["A32NX_SLIDES_ARMED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SLIDES_ARMED", DisplayName = "Door Slides",
            Type = SimConnect.SimVarType.LVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "disarmed", [1] = "armed" }
        },

        // ---- Aircraft-preset load progress — auto-announced at 10% milestones by a custom
        // ProcessSimVarUpdate branch (IsAnnounced = false: the generic gate is bypassed). ----
        ["A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS", DisplayName = "Preset Load Progress",
            // MUST be IsAnnounced=true so it enters the continuous-monitoring batch and
            // ProcessSimVarUpdate actually fires on it (the custom branch returns true to
            // suppress the generic announce). IsAnnounced=false leaves it unmonitored — the
            // same class of bug as the landing-rate fix (f1028d1). Matches the A380.
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
        };

        // ---- DOORS read-only status registration (parity with A380 _doorDefs loop). ----
        // Distinct alias keys so the announce/display logic distinguishes passenger
        // (_DOOR_) from cargo (_CARGO_) by key prefix.
        foreach (var dd in _doorDefs)
        {
            aircraftVariables[dd.Key] = new SimConnect.SimVarDefinition
            {
                Name = dd.Var, DisplayName = dd.Name,
                Type = dd.IsSimVar ? SimConnect.SimVarType.SimVar : SimConnect.SimVarType.LVar,
                Units = dd.IsSimVar ? "percent over 100" : "bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
                ValueDescriptions = dd.CargoLocked
                    ? new Dictionary<double, string> { [0] = "Open", [1] = "Closed" }
                    : new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            };
        }

        // Merge aircraft-specific variables into base variables
        foreach (var kvp in aircraftVariables)
        {
            variables[kvp.Key] = kvp.Value;
        }

        // Make EVERY discrete control combo AUTO-ANNOUNCE on change (Continuous +
        // IsAnnounced) — exactly like the A380 (Sel helper) and PMDG, where every
        // writable switch combo is monitored and the user mutes any chatty one via the
        // Ctrl+M monitor. NO hand-curated list: any panel-control var that is a readable
        // discrete combo (LVar/SimVar WITH value descriptions, currently OnRequest) is
        // flipped. The guards skip numeric input fields (_SET, no descriptions) and
        // event-only controls (Type == Event), so only real readable state combos change.
        foreach (var panelKeys in BuildPanelControls().Values)
        {
            foreach (var key in panelKeys)
            {
                if (key != SdPageVar   // synthetic SD-page selector — drives the scrape, not a monitored state
                    && key != "A32NX_MSFSBA_SPEEDBRAKE"   // synthetic speed-brake combo — fires SPOILERS_SET, no backing L:var
                    && !key.EndsWith("_DETENT", StringComparison.Ordinal)  // synthetic thrust-lever detent combos — no real L:var to monitor
                    && variables.TryGetValue(key, out var cdef)
                    && (cdef.Type == SimConnect.SimVarType.LVar || cdef.Type == SimConnect.SimVarType.SimVar)
                    && cdef.ValueDescriptions != null && cdef.ValueDescriptions.Count > 0
                    // Flip ANY non-Continuous readable combo. Was "== OnRequest", which
                    // MISSED combos that omit UpdateFrequency entirely — those default to
                    // the enum's 0 = Never (e.g. the overhead Evacuation Command toggle),
                    // so they never auto-announced. "!= Continuous" catches both.
                    && cdef.UpdateFrequency != SimConnect.UpdateFrequency.Continuous
                    // Skip momentary ACTIONS (Activate / Pressed) — they fire-and-return,
                    // so continuously monitoring + announcing them is noise.
                    && !cdef.ValueDescriptions.ContainsValue("Activate")
                    && !cdef.ValueDescriptions.ContainsValue("Pressed"))
                {
                    cdef.UpdateFrequency = SimConnect.UpdateFrequency.Continuous;
                    cdef.IsAnnounced = true;
                }
            }
        }

        // Auto-register every System Display page L:var (OnRequest) so the accessible
        // SD status box can read it. RequestVariable SILENTLY no-ops on any var that
        // has no data definition, so an SD var that isn't registered here reads as
        // "--" in the box (this was the bug: only SD vars that happened to also be a
        // panel control/display var elsewhere — battery, fuel flow, AC ESS — worked).
        // The add-if-absent guard preserves the richer existing definitions.
        for (int sdPage = 1; sdPage <= 11; sdPage++)
        {
            foreach (var (label, sdVar, _) in SdSystemRows(sdPage))
            {
                if (!variables.ContainsKey(sdVar))
                {
                    // A name with a SPACE is a stock SimVar ("FUEL TANK CENTER QUANTITY",
                    // "ANTISKID BRAKES ACTIVE") and MUST register as Type = SimVar — forcing
                    // it through the L:var path is the documented "not connected" crash.
                    // Test SPACE only (NOT colon): FBW indexed L:vars like A32NX_ENGINE_FF:1
                    // legitimately use a colon and must stay LVar. Stock SimVars get a "number"
                    // base-unit read here as a safety net; prefer pre-declaring them with the
                    // correct Units where the base unit isn't what the row formatter expects.
                    bool isStock = sdVar.IndexOf(' ') >= 0;
                    variables[sdVar] = new SimConnect.SimVarDefinition
                    {
                        Name = sdVar,
                        DisplayName = label,
                        Type = isStock ? SimConnect.SimVarType.SimVar : SimConnect.SimVarType.LVar,
                        UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                        Units = isStock ? "number" : null!
                    };
                }
            }
        }

        _cachedVariables = variables;
        return variables;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        return new Dictionary<string, List<string>>
        {
["ELEC"] = new List<string>
        {
            "A32NX_ELEC_BAT_1_POTENTIAL",
            "A32NX_ELEC_BAT_2_POTENTIAL"
        },
        ["ADIRS"] = new List<string>
        {
            "A32NX_ADIRS_ADIRU_1_STATE",
            "A32NX_ADIRS_ADIRU_2_STATE",
            "A32NX_ADIRS_ADIRU_3_STATE"
        },
        ["Anti Ice"] = new List<string>
        {
            // The actual wing-anti-ice flowing status (the SELECTED control reverts on
            // the ground inhibit; this is what's really happening at the valve).
            "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON",
            // Wing anti-ice fault (auto-announces on change; read-only here).
            "A32NX_PNEU_WING_ANTI_ICE_HAS_FAULT"
        },
        ["Air Conditioning"] = new List<string>
        {
            // Actual zone temperatures (the selectors in the panel set the target).
            "A32NX_COND_CKPT_TEMP", "A32NX_COND_FWD_TEMP", "A32NX_COND_AFT_TEMP"
        },
        ["Flight Control Computers"] = new List<string>
        {
            // FAC-computed rudder trim position (ARINC; decoded in TryGetDisplayOverride).
            "A32NX_FAC_1_RUDDER_TRIM_POS",
            // FMS-predicted takeoff THS setting (ARINC; decoded in TryGetDisplayOverride).
            "A32NX_FM1_TO_PITCH_TRIM",
            // Nosewheel-steering angle + tiller-handle read-outs (decoded in TryGetDisplayOverride).
            "A32NX_NOSE_WHEEL_POSITION", "A32NX_TILLER_HANDLE_POSITION"
        },
        ["Autobrake"] = new List<string>
        {
            // Brake "triple indicator" (normal/alternate/accumulator pressures) + status
            // — the taxi/landing brake check (parity with the A380).
            "A32NX_HYD_BRAKE_NORM_LEFT_PRESS", "A32NX_HYD_BRAKE_NORM_RIGHT_PRESS",
            "A32NX_HYD_BRAKE_ALTN_LEFT_PRESS", "A32NX_HYD_BRAKE_ALTN_RIGHT_PRESS",
            "A32NX_HYD_BRAKE_ALTN_ACC_PRESS", "A32NX_BRAKES_HOT", "A32NX_BRAKE_FAN_RUNNING"
        },
        ["APU"] = new List<string>
        {
            // APU AVAIL annunciation (read-only; the EWD memo speaks it).
            "A32NX_OVHD_APU_START_PB_IS_AVAILABLE"
        },
        ["Thrust Levers"] = new List<string>
        {
            // Live lever angles (the detent set-combos in the panel show the last
            // command, so the actual angle is read here).
            "A32NX_AUTOTHRUST_TLA:1", "A32NX_AUTOTHRUST_TLA:2"
        },
        ["EFIS Captain"] = new List<string>
        {
            "KOHLSMAN SETTING MB:1",
            "KOHLSMAN SETTING HG:1",
            "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE"
        },
        ["EFIS First Officer"] = new List<string>
        {
            "KOHLSMAN SETTING MB:2",
            "KOHLSMAN SETTING HG:2",
            "A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE"
        },
        // Flight Controls: actual surface positions from SFCC (ARINC429 degrees).
        // These are display-only (decoded via the generic IsArinc429 path) and show
        // whether the surfaces have reached the commanded config — the sighted pilot
        // reads this off the E/WD flap indicator.
        ["Flight Controls"] = new List<string>
        {
            "A32NX_SFCC_1_FLAP_ACTUAL_POSITION_WORD",
            "A32NX_SFCC_1_SLAT_ACTUAL_POSITION_WORD"
        },
        ["Speed Brake"] = new List<string>
        {
            "A32NX_SPOILERS_ARMED",
            "A32NX_SPOILERS_HANDLE_POSITION"
        },
        ["Warnings"] = new List<string>
        {
            "A32NX_MASTER_WARNING",
            "A32NX_MASTER_CAUTION",
            "A32NX_AUTOPILOT_AUTOLAND_WARNING"
        },
        // RMP readout: tuned frequencies (rendered as MHz via TryGetDisplayOverride)
        // first, then which radio the mic transmits on. (The former separate "Radios"
        // panel was merged into RMP — frequency tuning IS the RMP on the A320.)
        ["RMP"] = new List<string>
        {
            "COM_ACTIVE_FREQUENCY:1",
            "COM_STANDBY_FREQUENCY:1",
            "COM_ACTIVE_FREQUENCY:2",
            "COM_STANDBY_FREQUENCY:2",
            "COM_ACTIVE_FREQUENCY:3",
            "COM_STANDBY_FREQUENCY:3",
            "COM_TRANSMIT:1",
            "COM_TRANSMIT:2",
            "COM_TRANSMIT:3"
        },
        // Squawk read-back (decoded from the BCD16 word). The mode/system are settable
        // controls in the Transponder panel; this read-only box shows the active code.
        // DCDU ATC message waiting is also shown here (A380 parity).
        ["Transponder"] = new List<string>
        {
            "XPNDR_CODE",
            "A32NX_DCDU_ATC_MSG_WAITING"
        },
        ["ECAM Control Panel"] = new List<string>
        {
            "A32NX_ECAM_SFAIL"
        },
        // System Display: the status box shows the selected page's content (E/WD scrape
        // or decoded SD-system SimVars), via TryGetDisplayOverride on the page var.
        ["System Display"] = new List<string>
        {
            "A32NX_MSFSBA_SD_PAGE"
        },
        // PFD accessible snapshot — the "glass" content a sighted pilot reads off the
        // Primary Flight Display: FMA modes + armed, autothrust, approach capability,
        // target altitude, attitude/heading/speed/altitude, and the PFD message line.
        // Single status box (force-read on F5; FMA lines also auto-refresh). Decoded
        // via TryGetDisplayOverride (armed bitmasks + radian attitude/heading).
        ["PFD"] = new List<string>
        {
            "A32NX_FMA_VERTICAL_MODE",
            "A32NX_FMA_VERTICAL_ARMED",
            "A32NX_FMA_LATERAL_MODE",
            "A32NX_FMA_LATERAL_ARMED",
            "A32NX_AUTOTHRUST_MODE",
            "A32NX_AUTOTHRUST_STATUS",
            "A32NX_PFD_TARGET_ALTITUDE",
            "PLANE_PITCH_DEGREES",
            "PLANE_BANK_DEGREES",
            "PLANE_HEADING_DEGREES_MAGNETIC",
            "PFD_IAS",
            "INDICATED_ALTITUDE",
            "A32NX_PFD_MSG_SET_HOLD_SPEED",
            "A32NX_PFD_MSG_TD_REACHED",
            "A32NX_PFD_MSG_CHECK_SPEED_MODE",
            "A32NX_PFD_LINEAR_DEVIATION_ACTIVE",
            // ---- A380-parity status-box additions ----
            "FCU_SEL_ALT",
            "FCU_SEL_HDG",
            "A32NX_SPEEDS_MANAGED_PFD",
            "A32NX_SpeedPreselVal",
            "A32NX_MachPreselVal",
            "A32NX_AUTOPILOT_VS_SELECTED",
            "A32NX_FMA_EXPEDITE_MODE",
            "FD_1",
            "FD_2",
            "PFD_VLS",
            "PFD_VMAX",
            "PFD_GREENDOT",
            "PFD_VF",
            "PFD_VSLOW",
            "PFD_VALPHAPROT",
            "PFD_VALPHAMAX",
            "PFD_VSW",
            "A32NX_FM1_TRANS_LVL",
            "A32NX_FM1_TRANS_ALT",
            "A32NX_ADIRS_ADR_1_STATIC_AIR_TEMPERATURE",
            "A32NX_ADIRS_ADR_1_TOTAL_AIR_TEMPERATURE",
            // Approach minimums (ARINC FM words; auto-decoded "N feet"/"Not set")
            // — on-demand readout to complement the on-change announce.
            "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE",
            "A32NX_FM1_DECISION_HEIGHT",
            "PFD_AUTOLAND"
        },
        // ND accessible snapshot — the navigation picture: mode/range, the TO
        // waypoint (decoded ident + distance/bearing/ETA), cross-track, RNP, and
        // ILS LOC/GS validity + deviation, plus the approach message. Single status
        // box (force-read on F5). Idents/messages decoded via TryGetDisplayOverride.
        ["ND"] = new List<string>
        {
            "A32NX_EFIS_L_ND_MODE",
            "A32NX_EFIS_L_ND_RANGE",
            "A32NX_EFIS_L_TO_WPT_IDENT_0",
            "A32NX_EFIS_L_TO_WPT_DISTANCE",
            "A32NX_EFIS_L_TO_WPT_BEARING",
            "A32NX_EFIS_L_TO_WPT_ETA",
            "A32NX_FG_CROSS_TRACK_ERROR",
            "A32NX_FMGC_L_RNP",
            "A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS",
            "A32NX_EFIS_L_APPR_MSG_0",
            "A32NX_RADIO_RECEIVER_LOC_IS_VALID",
            "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
            "A32NX_RADIO_RECEIVER_GS_IS_VALID",
            "A32NX_RADIO_RECEIVER_GS_DEVIATION",
            // ---- A380-parity status-box additions ----
            "A32NX_FMGC_TRUE_REF",
            "A32NX_ADIRS_IR_1_GROUND_SPEED",
            "A32NX_ADIRS_ADR_1_TRUE_AIRSPEED",
            "A32NX_ADIRS_IR_1_WIND_DIRECTION_BNR",
            "A32NX_ADIRS_IR_1_WIND_SPEED_BNR",
            "ND_VOR1_FREQ",
            "ND_VOR2_FREQ",
            "ND_VOR1_DME",
            "ND_VOR2_DME",
            "ND_ADF1_FREQ",
            "ND_ADF2_FREQ"
        },
        // ISIS accessible snapshot — the standby instrument: attitude, heading,
        // speed, altitude, baro reference, and ILS state, as a single status box
        // (force-read on F5). The baro mode + ILS also remain settable controls
        // (BuildPanelControls["ISIS"]). Attitude/heading decoded via TryGetDisplayOverride.
        ["ISIS"] = new List<string>
        {
            "PLANE_PITCH_DEGREES",
            "PLANE_BANK_DEGREES",
            "PLANE_HEADING_DEGREES_MAGNETIC",
            "PFD_IAS",
            "INDICATED_ALTITUDE",
            "A32NX_ISIS_BARO_MODE",
            "A32NX_ISIS_LS_ACTIVE"
        },
        // Clock readouts (chronometer + elapsed time), formatted via TryGetDisplayOverride.
        ["Clock"] = new List<string> { "A32NX_CHRONO_ELAPSED_TIME", "A32NX_CHRONO_ET_ELAPSED_TIME" }
        // Add more panels and their display variables here as needed
        };
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
["Overhead"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", "Hydraulics", "Fuel", "Air Conditioning", "Bleed", "Pressurization", "Ventilation", "Anti Ice", "Wipers", "Signs", "Interior Lighting", "Exterior Lighting", "Calls", "GPWS", "Flight Control Computers", "Cockpit", "Evacuation", "Cargo Smoke", "Recorder and Misc", "Engine Start" },
        ["Glareshield"] = new List<string> { "FCU", "EFIS Captain", "EFIS First Officer", "Warnings" },
        ["Instrument"] = new List<string> { "Gear", "Autobrake", "PFD", "ND", "ISIS", "Source Switching", "Clock", "System Display" },
        ["Pedestal"] = new List<string> { "Flight Controls", "Speed Brake", "Parking Brake", "Engines", "Thrust Levers", "ECAM Control Panel", "Weather Radar", "Transponder", "RMP" }
        // "Ground Services" (Doors + Ground Equipment) was removed — doors are read-only
        // auto-announced status now and ground services are done via the flyPad Ground
        // page (parity with the A380, which has no Doors panel).
        };
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
["ELEC"] = new List<string>
        {
            "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON",
            "EXT_PWR_AVAILABLE",
            "A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON",
            "A32NX_OVHD_ELEC_ENG_GEN_2_PB_IS_ON",
            "A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON",
            "A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL",
            "A32NX_OVHD_ELEC_IDG_1_PB_IS_RELEASED",
            "A32NX_OVHD_ELEC_IDG_2_PB_IS_RELEASED",
            "A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON",
            "A32NX_OVHD_ELEC_GALY_AND_CAB_PB_IS_AUTO",
            "A32NX_OVHD_EMER_ELEC_GEN_1_LINE_PB_IS_ON",
            "A32NX_OVHD_EMER_ELEC_RAT_AND_EMER_GEN_IS_PRESSED",
            "A32NX_EMERELECPWR_GEN_TEST"
        },
        ["ADIRS"] = new List<string>
        {
            "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
            "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
            "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB",
            "A32NX_OVHD_ADIRS_IR_1_PB_IS_ON",
            "A32NX_OVHD_ADIRS_IR_2_PB_IS_ON",
            "A32NX_OVHD_ADIRS_IR_3_PB_IS_ON",
            "A32NX_OVHD_ADIRS_ADR_1_PB_IS_ON",
            "A32NX_OVHD_ADIRS_ADR_2_PB_IS_ON",
            "A32NX_OVHD_ADIRS_ADR_3_PB_IS_ON"
        },
        ["APU"] = new List<string>
        {
            "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
            "A32NX_OVHD_APU_START_PB_IS_ON",
            "A32NX_APU_AUTOEXITING_TEST_ON",
            "A32NX_APU_AUTOEXITING_RESET"
        },
        ["Oxygen"] = new List<string>
        {
            "PUSH_OVHD_OXYGEN_CREW",
            "A32NX_OXYGEN_MASKS_DEPLOYED",
            "A32NX_OXYGEN_PASSENGER_LIGHT_ON",
            "A32NX_OXYGEN_TMR_RESET"
        },
        ["Fire"] = new List<string>
        {
            "A32NX_FIRE_BUTTON_ENG1",
            "A32NX_FIRE_BUTTON_ENG2",
            "A32NX_FIRE_BUTTON_APU",
            "A32NX_FIRE_TEST_ENG1",
            "A32NX_FIRE_TEST_ENG2",
            "A32NX_FIRE_TEST_APU",
            "A32NX_FIRE_ENG1_AGENT1_Discharge",
            "A32NX_FIRE_ENG1_AGENT2_Discharge",
            "A32NX_FIRE_ENG2_AGENT1_Discharge",
            "A32NX_FIRE_ENG2_AGENT2_Discharge",
            "A32NX_FIRE_APU_AGENT1_Discharge",
        },
        ["Hydraulics"] = new List<string>
        {
            "A32NX_OVHD_HYD_EPUMPY_OVRD_IS_ON",
            "A32NX_OVHD_HYD_ENG_1_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_1_PUMP_PB_HAS_FAULT",
            "A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO",
            "A32NX_OVHD_HYD_ENG_2_PUMP_PB_HAS_FAULT",
            "A32NX_OVHD_HYD_EPUMPB_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPB_PB_HAS_FAULT",
            "A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO",
            "A32NX_OVHD_HYD_EPUMPY_PB_HAS_FAULT",
            "A32NX_OVHD_HYD_PTU_PB_IS_AUTO",
            "A32NX_OVHD_HYD_PTU_PB_HAS_FAULT",
            "A32NX_OVHD_HYD_LEAK_MEASUREMENT_G_PB_IS_AUTO",
            "A32NX_OVHD_HYD_LEAK_MEASUREMENT_B_PB_IS_AUTO",
            "A32NX_OVHD_HYD_LEAK_MEASUREMENT_Y_PB_IS_AUTO",
            "A32NX_OVHD_HYD_RAT_MAN_ON_IS_PRESSED",
        },
        ["Fuel"] = new List<string>
        {
            "A32NX_OVHD_FUEL_MODESEL_MANUAL",
            "FUEL_PUMP_L1",
            "FUEL_PUMP_L2",
            "FUEL_PUMP_R1",
            "FUEL_PUMP_R2",
            "FUEL_PUMP_C1",
            "FUEL_PUMP_C2",
            "FUEL_XFEED"   // Crossfeed — stateful OPEN/CLOSE combo (valve id 3)
        },
        // Functional split that matches the FBW ECAM System Display pages — the
        // source's OWN grouping (fbw-a32nx SD/Pages/Cond + SD/Pages/Bleed): the COND
        // page shows the zone temperatures + HOT AIR (hotAir/trim), and the BLEED page
        // shows the bleed sources (APU + engine bleeds), X-bleed, pack flow, PACK 1/2
        // and RAM AIR ("Ram air", Bleed.tsx). (On the real A320 these are all one
        // physical AIR COND overhead panel — node prefix _OVHD_AIRCOND_ — but the ECAM
        // splits them Bleed vs Cond, the more navigable grouping for a screen reader.)
        ["Air Conditioning"] = new List<string>
        {
            "COND_CKPT_TEMP_SET",
            "COND_FWD_TEMP_SET",
            "COND_AFT_TEMP_SET",
            "A32NX_OVHD_COND_HOT_AIR_PB_IS_ON"
        },
        ["Bleed"] = new List<string>
        {
            "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
            "A32NX_OVHD_PNEU_ENG_1_BLEED_PB_IS_AUTO",
            "A32NX_OVHD_PNEU_ENG_2_BLEED_PB_IS_AUTO",
            "A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position",
            "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position",
            "A32NX_OVHD_COND_PACK_1_PB_IS_ON",
            "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
            "A32NX_AIRCOND_RAMAIR_TOGGLE"
        },
        ["Pressurization"] = new List<string>
        {
            "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO", "A32NX_OVHD_PRESS_MAN_VS_CTL_SWITCH", "A32NX_OVHD_PRESS_DITCHING_PB_IS_ON",
            "PRESS_LDG_ELEV_SET"
        },
        ["Ventilation"] = new List<string>
        {
            "A32NX_VENTILATION_BLOWER_TOGGLE", "A32NX_VENTILATION_EXTRACT_TOGGLE",
            "A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON"
        },
        ["Interior Lighting"] = new List<string>
        {
            "A32NX_OVHD_INTLT_ANN",
            "A32NX_OVHD_INTLT_DOME",
            "A32NX_STBY_COMPASS_LIGHT_TOGGLE",
            "BRIGHT_PEDESTAL_SET",
            "BRIGHT_MAINPANEL_SET",
            "BRIGHT_GLARESHIELD_CAPT_SET",
            "BRIGHT_GLARESHIELD_FO_SET",
            "BRIGHT_GLARESHIELD_INTEG_SET",
            "BRIGHT_OVERHEAD_INTEG_SET"
        },
        ["Recorder and Misc"] = new List<string>
        {
            "A32NX_DFDR_EVENT_ON",
            "A32NX_CREW_HEAD_SET",
            "A32NX_AVIONICS_COMPLT_ON"
        },
        ["Flight Control Computers"] = new List<string>
        {
            "A32NX_ELAC_1_PUSHBUTTON_PRESSED", "A32NX_ELAC_2_PUSHBUTTON_PRESSED",
            "A32NX_SEC_1_PUSHBUTTON_PRESSED", "A32NX_SEC_2_PUSHBUTTON_PRESSED", "A32NX_SEC_3_PUSHBUTTON_PRESSED",
            "A32NX_FAC_1_PUSHBUTTON_PRESSED", "A32NX_FAC_2_PUSHBUTTON_PRESSED",
            "A32NX_RUDDER_TRIM_RESET",
            "RUDDER_TRIM_LEFT", "RUDDER_TRIM_RIGHT"
        },
        ["Anti Ice"] = new List<string>
        {
            "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED",
            "ENG_ANTI_ICE:1",
            "ENG_ANTI_ICE:2",
            "A32NX_MAN_PITOT_HEAT"
        },
        ["Wipers"] = new List<string>
        {
            "XMLVAR_A320_WiperSwitch_1",
            "XMLVAR_A320_WiperSwitch_2"
        },
        ["Signs"] = new List<string>
        {
            "CABIN SEATBELTS ALERT SWITCH",
            "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION",
            "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION"
        },
        ["Exterior Lighting"] = new List<string>
        {
            "LIGHTING_LANDING_1",
            "LIGHTING_LANDING_2",
            "LIGHTING_LANDING_3",
            "LIGHTING_STROBE_0",
            "LIGHT BEACON",
            "LIGHT WING",
            "A32NX_LIGHTS_NAV_LOGO",
            "LIGHT TAXI:2",
            "LANDING_LIGHTS_ON_THIRD_PARTY",
            "LANDING_LIGHTS_OFF_THIRD_PARTY"
        },
        ["Calls"] = new List<string>
        {
            "PUSH_OVHD_CALLS_MECH",
            "PUSH_OVHD_CALLS_ALL",
            "PUSH_OVHD_CALLS_FWD",
            "PUSH_OVHD_CALLS_AFT",
            "A32NX_CALLS_EMER_ON"
        },
        ["GPWS"] = new List<string>
        {
            "A32NX_GPWS_FLAPS3",
            "A32NX_GPWS_FLAP_OFF",
            "A32NX_GPWS_GS_OFF",
            "A32NX_GPWS_SYS_OFF",
            "A32NX_GPWS_TERR_OFF",
            "A32NX_GPWS_TEST"
        },
        ["Cockpit"] = new List<string>
        {
            "A32NX_COCKPIT_DOOR_LOCKED",
            "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE",
            // Cockpit furniture (binary toggles — the only ones the A32NX models)
            "A32NX_ARMREST_CPT",
            "A32NX_ARMREST_FO",
            "A32NX_COCKPIT_SEAT_BACK",
            "A32NX_COCKPIT_SEAT_BACK_TOP",
        },
        ["Evacuation"] = new List<string>
        {
            "A32NX_EVAC_COMMAND_TOGGLE",
            "A32NX_EVAC_CAPT_TOGGLE",
            "A32NX_EVAC_HORN_SHUTOFF",
        },
        ["Cargo Smoke"] = new List<string>
        {
            "A32NX_FIRE_TEST_CARGO",
            "A32NX_CARGOSMOKE_FWD_DISCHARGED",
            "A32NX_CARGOSMOKE_AFT_DISCHARGED",
        },
        ["Engine Start"] = new List<string>
        {
            "A32NX_OVHD_FADEC_1",
            "A32NX_OVHD_FADEC_2",
        },
        ["FCU"] = new List<string>
        {
            "A32NX.FCU_HDG_SET",
            "A32NX.FCU_HDG_PUSH",
            "A32NX.FCU_HDG_PULL",
            "A32NX.FCU_LOC_PUSH",
            "A32NX.FCU_SPD_SET",
            "A32NX.FCU_SPD_PUSH",
            "A32NX.FCU_SPD_PULL",
            "A32NX.FCU_ALT_SET",
            "A32NX.FCU_ALT_PUSH",
            "A32NX.FCU_ALT_PULL",
            "A32NX.FCU_VS_SET",
            "A32NX.FCU_VS_PUSH",
            "A32NX.FCU_VS_PULL",
            "A32NX.FCU_EXPED_PUSH",
            "A32NX.FCU_APPR_PUSH",
            "A32NX.FCU_AP_1_PUSH",
            "A32NX.FCU_AP_2_PUSH",
            "A32NX.FCU_ATHR_PUSH",
            "A32NX.FCU_AP_DISCONNECT_PUSH",
            "A32NX.FCU_ATHR_DISCONNECT_PUSH",
            "A32NX.FCU_SPD_MACH_TOGGLE_PUSH",
            "A32NX.FCU_TRK_FPA_TOGGLE_PUSH"
        },
        ["EFIS Captain"] = new List<string>
        {
            "A32NX_FCU_EFIS_L_EFIS_MODE",
            "A32NX_FCU_EFIS_L_EFIS_RANGE",
            "A32NX_FCU_EFIS_L_NAVAID_1_MODE",
            "A32NX_FCU_EFIS_L_NAVAID_2_MODE",
            "A32NX_EFIS_L_LS_BUTTON_IS_ON",
            "A32NX.FCU_EFIS_L_FD_PUSH",
            "A32NX_FCU_EFIS_L_BARO_IS_INHG",
            "A32NX.FCU_EFIS_L_BARO_SET",
            "A32NX.FCU_EFIS_L_BARO_PUSH",
            "A32NX.FCU_EFIS_L_BARO_PULL",
            "A32NX.FCU_EFIS_L_CSTR_PUSH",
            "A32NX.FCU_EFIS_L_WPT_PUSH",
            "A32NX.FCU_EFIS_L_VORD_PUSH",
            "A32NX.FCU_EFIS_L_NDB_PUSH",
            "A32NX.FCU_EFIS_L_ARPT_PUSH",
            "A32NX_FCU_EFIS_L_CSTR_LIGHT_ON",
            "A32NX_FCU_EFIS_L_WPT_LIGHT_ON",
            "A32NX_FCU_EFIS_L_VORD_LIGHT_ON",
            "A32NX_FCU_EFIS_L_NDB_LIGHT_ON",
            "A32NX_FCU_EFIS_L_ARPT_LIGHT_ON"
        },
        ["EFIS First Officer"] = new List<string>
        {
            "A32NX_FCU_EFIS_R_EFIS_MODE",
            "A32NX_FCU_EFIS_R_EFIS_RANGE",
            "A32NX_FCU_EFIS_R_NAVAID_1_MODE",
            "A32NX_FCU_EFIS_R_NAVAID_2_MODE",
            "A32NX_EFIS_R_LS_BUTTON_IS_ON",
            "A32NX.FCU_EFIS_R_FD_PUSH",
            "A32NX_FCU_EFIS_R_BARO_IS_INHG",
            "A32NX.FCU_EFIS_R_BARO_SET",
            "A32NX.FCU_EFIS_R_BARO_PUSH",
            "A32NX.FCU_EFIS_R_BARO_PULL",
            "A32NX.FCU_EFIS_R_CSTR_PUSH",
            "A32NX.FCU_EFIS_R_WPT_PUSH",
            "A32NX.FCU_EFIS_R_VORD_PUSH",
            "A32NX.FCU_EFIS_R_NDB_PUSH",
            "A32NX.FCU_EFIS_R_ARPT_PUSH",
            "A32NX_FCU_EFIS_R_CSTR_LIGHT_ON",
            "A32NX_FCU_EFIS_R_WPT_LIGHT_ON",
            "A32NX_FCU_EFIS_R_VORD_LIGHT_ON",
            "A32NX_FCU_EFIS_R_NDB_LIGHT_ON",
            "A32NX_FCU_EFIS_R_ARPT_LIGHT_ON"
        },
        ["Warnings"] = new List<string>
        {
            "CLEAR_MASTER_WARNING",
            "CLEAR_MASTER_WARNING_FO",
            "CLEAR_MASTER_CAUTION",
            "CLEAR_MASTER_CAUTION_FO"
        },
        ["Gear"] = new List<string>
        {
            "GEAR_HANDLE_POSITION"
        },
        ["Autobrake"] = new List<string>
        {
            "AUTOBRAKE_MODE",
            "A32NX_BRAKE_FAN_BTN_PRESSED"
        },
        ["ISIS"] = new List<string>
        {
            "A32NX_ISIS_BARO_MODE",
            "A32NX_ISIS_BUGS_ACTIVE",
            "A32NX_ISIS_LS_ACTIVE",
        },
        ["Speed Brake"] = new List<string>
        {
            "A32NX_MSFSBA_SPEEDBRAKE",
            "SPOILERS_ARM_TOGGLE",
            "SPOILERS_OFF",
            "SPOILERS_ON",
            "A32NX_MSFSBA_SPEEDBRAKE_SLIDER"
        },
        ["Parking Brake"] = new List<string>
        {
            "A32NX_PARK_BRAKE_LEVER_POS"
        },
        ["Engines"] = new List<string>
        {
            "ENGINE_MODE_SELECTOR",
            "ENGINE_1_MASTER",
            "ENGINE_2_MASTER"
        },
        ["Thrust Levers"] = new List<string>
        {
            "THROTTLE_ALL_DETENT", "THROTTLE_1_DETENT", "THROTTLE_2_DETENT"
        },
        // "Doors" and "Ground Equipment" display lists were removed — doors are read-only
        // auto-announced status (shown on the SD DOOR page) and ground services live on
        // the flyPad Ground page (parity with the A380, which has no Doors panel).
        ["ECAM Control Panel"] = new List<string>
        {
            "ECAM_ENG",
            "ECAM_APU",
            "ECAM_BLEED",
            "ECAM_COND",
            "ECAM_ELEC",
            "ECAM_HYD",
            "ECAM_FUEL",
            "ECAM_PRESS",
            "ECAM_DOOR",
            "ECAM_BRAKES",
            "ECAM_FLT_CTL",
            "ECAM_ALL",
            "ECAM_STS",
            "ECAM_RCL",
            "ECAM_TO_CONF",
            "ECAM_EMER_CANC",
            "ECAM_CLR_1",
            "ECAM_CLR_2"
        },
        ["System Display"] = new List<string>
        {
            "A32NX_MSFSBA_SD_PAGE"
        },
        ["Weather Radar"] = new List<string>
        {
            "XMLVAR_A320_WeatherRadar_Sys",
            "XMLVAR_A320_WeatherRadar_Mode",
            "A32NX_SWITCH_RADAR_PWS_POSITION",
            "A32NX_RADAR_MULTISCAN_AUTO",
            "A32NX_RADAR_GCS_AUTO"
        },
        ["Transponder"] = new List<string>
        {
            "A32NX_TRANSPONDER_MODE",
            "A32NX_TRANSPONDER_SYSTEM",
            "A32NX_SWITCH_ATC_ALT",
            "TRANSPONDER_CODE_SET",
            "XPNDR_IDENT_ON",
            "A32NX_SWITCH_TCAS_TRAFFIC_POSITION",
            "A32NX_SWITCH_TCAS_POSITION",
            "A32NX_DCDU_ATC_MSG_ACK"
        },
        // RMP = the one radio panel (the former "Radios" panel was merged in). Tab
        // order is deliberate: the frequency-management controls come FIRST so tuning
        // is one Tab away from the panel list; the rarely-touched RMP power/mode
        // switches sit at the end.
        ["RMP"] = new List<string>
        {
            "COM_STANDBY_FREQUENCY_SET:1",
            "COM_ACTIVE_FREQUENCY_SET:1",
            "COM1_RADIO_SWAP",
            "COM_STANDBY_FREQUENCY_SET:2",
            "COM_ACTIVE_FREQUENCY_SET:2",
            "COM2_RADIO_SWAP",
            "COM_STANDBY_FREQUENCY_SET:3",
            "COM_ACTIVE_FREQUENCY_SET:3",
            "COM3_RADIO_SWAP",
            "A32NX_RMP_L_TOGGLE_SWITCH",
            "A32NX_RMP_L_SELECTED_MODE"
        },
        // PFD / ND are status-box-only panels (no interactive controls — the readout
        // lives in GetPanelDisplayVariables); FCU/EFIS controls live in their own panels.
        ["PFD"] = new List<string>(),
        ["ND"] = new List<string>(),
        ["Clock"] = new List<string>
        {
            "A32NX_MSFSBA_CHRONO_TOGGLE", "A32NX_MSFSBA_CHRONO_RESET", "A32NX_CHRONO_ET_SWITCH_POS"
        },
        ["Source Switching"] = new List<string>
        {
            "A32NX_ATT_HDG_SWITCHING_KNOB", "A32NX_AIR_DATA_SWITCHING_KNOB",
            "A32NX_EIS_DMC_SWITCHING_KNOB", "A32NX_FMGC_TRUE_REF",
            "A32NX_ECAM_ND_XFR_SWITCHING_KNOB"
        },
        ["Flight Controls"] = new List<string>
        {
            "A32NX_SPOILERS_ARMED",
            "A32NX_SPOILERS_HANDLE_POSITION",
            "A32NX_FLAPS_HANDLE_INDEX",
        },
        };
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        return new Dictionary<string, string>
        {
// FCU buttons
        ["A32NX.FCU_HDG_PUSH"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
        ["A32NX.FCU_HDG_PULL"] = "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED",
        ["A32NX.FCU_SPD_PUSH"] = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
        ["A32NX.FCU_SPD_PULL"] = "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED",
        ["A32NX.FCU_ALT_PUSH"] = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
        ["A32NX.FCU_ALT_PULL"] = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
        ["A32NX.FCU_LOC_PUSH"] = "A32NX_FCU_LOC_LIGHT_ON",
        ["A32NX.FCU_APPR_PUSH"] = "A32NX_FCU_APPR_LIGHT_ON",
        ["A32NX.FCU_AP_1_PUSH"] = "A32NX_FCU_AP_1_LIGHT_ON",
        ["A32NX.FCU_AP_2_PUSH"] = "A32NX_FCU_AP_2_LIGHT_ON",
        ["A32NX.FCU_ATHR_PUSH"] = "A32NX_FCU_ATHR_LIGHT_ON",
        ["A32NX.FCU_EXPED_PUSH"] = "A32NX_FCU_EXPED_LIGHT_ON",
        ["A32NX.FCU_SPD_MACH_TOGGLE_PUSH"] = "A32NX_FCU_AFS_DISPLAY_MACH_MODE",
        ["A32NX.FCU_TRK_FPA_TOGGLE_PUSH"] = "A32NX_TRK_FPA_MODE_ACTIVE",

        // EFIS Control Panel buttons
        ["A32NX.FCU_EFIS_L_FD_PUSH"] = "A32NX_FCU_EFIS_L_FD_LIGHT_ON",
        ["A32NX.FCU_EFIS_R_FD_PUSH"] = "A32NX_FCU_EFIS_R_FD_LIGHT_ON",
        ["A32NX.FCU_EFIS_L_BARO_PUSH"] = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
        ["A32NX.FCU_EFIS_L_BARO_PULL"] = "A32NX_FCU_EFIS_L_DISPLAY_BARO_MODE",
        ["A32NX.FCU_EFIS_R_BARO_PUSH"] = "A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE",
        ["A32NX.FCU_EFIS_R_BARO_PULL"] = "A32NX_FCU_EFIS_R_DISPLAY_BARO_MODE",

        // Autobrake buttons
        ["A32NX.AUTOBRAKE_SET_DISARM"] = "A32NX_AUTOBRAKES_ARMED_MODE",
        ["A32NX.AUTOBRAKE_BUTTON_LO"] = "A32NX_AUTOBRAKES_ARMED_MODE",
        ["A32NX.AUTOBRAKE_BUTTON_MED"] = "A32NX_AUTOBRAKES_ARMED_MODE",
        ["A32NX.AUTOBRAKE_BUTTON_MAX"] = "A32NX_AUTOBRAKES_ARMED_MODE",

        // Pedestal buttons
        ["SPOILERS_ARM_TOGGLE"] = "A32NX_SPOILERS_ARMED",
        ["SPOILERS_ON"] = "A32NX_SPOILERS_HANDLE_POSITION",
        ["SPOILERS_OFF"] = "A32NX_SPOILERS_HANDLE_POSITION",

        // ECAM panel buttons
        ["ECAM_ENG"] = "A32NX_ECP_LIGHT_ENG",
        ["ECAM_APU"] = "A32NX_ECP_LIGHT_APU",
        ["ECAM_BLEED"] = "A32NX_ECP_LIGHT_BLEED",
        ["ECAM_COND"] = "A32NX_ECP_LIGHT_COND",
        ["ECAM_ELEC"] = "A32NX_ECP_LIGHT_ELEC",
        ["ECAM_HYD"] = "A32NX_ECP_LIGHT_HYD",
        ["ECAM_FUEL"] = "A32NX_ECP_LIGHT_FUEL",
        ["ECAM_PRESS"] = "A32NX_ECP_LIGHT_PRESS",
        ["ECAM_DOOR"] = "A32NX_ECP_LIGHT_DOOR",
        ["ECAM_BRAKES"] = "A32NX_ECP_LIGHT_BRAKES",
        ["ECAM_FLT_CTL"] = "A32NX_ECP_LIGHT_FLT_CTL",
        ["ECAM_ALL"] = "A32NX_ECP_LIGHT_ALL",
        ["ECAM_STS"] = "A32NX_ECP_LIGHT_STS",
        ["ECAM_CLR_1"] = "A32NX_ECP_LIGHT_CLR_1",
        ["ECAM_CLR_2"] = "A32NX_ECP_LIGHT_CLR_2",
        };
    }

    /// <summary>
    /// Maps hotkey actions to A32NX SimConnect event names for simple button actions.
    /// </summary>
    protected override Dictionary<HotkeyAction, string> GetHotkeyVariableMap()
    {
        return new Dictionary<HotkeyAction, string>
        {
            // FCU push/pull buttons. Routed through the map so MainForm's post-handle
            // HandleButtonStateAnnouncement fires the managed-state announce (the
            // GetButtonStateMapping below points each event at its …_MANAGED var), i.e.
            // the knob actuates and the resulting Managed/Selected change is spoken only
            // when it actually changes — the same Fenix-style behaviour the user wants.
            // Do NOT add a RequestFCU*WithStatus readback here: that speaks the value on
            // every press like an output-mode read query and is misleading.
            [HotkeyAction.FCUHeadingPush] = "A32NX.FCU_HDG_PUSH",
            [HotkeyAction.FCUHeadingPull] = "A32NX.FCU_HDG_PULL",
            [HotkeyAction.FCUAltitudePush] = "A32NX.FCU_ALT_PUSH",
            [HotkeyAction.FCUAltitudePull] = "A32NX.FCU_ALT_PULL",
            [HotkeyAction.FCUSpeedPush] = "A32NX.FCU_SPD_PUSH",
            [HotkeyAction.FCUSpeedPull] = "A32NX.FCU_SPD_PULL",
            [HotkeyAction.FCUVSPush] = "A32NX.FCU_VS_PUSH",
            [HotkeyAction.FCUVSPull] = "A32NX.FCU_VS_PULL",

            // Autopilot buttons
            [HotkeyAction.ToggleAutopilot1] = "A32NX.FCU_AP_1_PUSH",
            [HotkeyAction.ToggleAutopilot2] = "A32NX.FCU_AP_2_PUSH",
            [HotkeyAction.ToggleApproachMode] = "A32NX.FCU_APPR_PUSH",
            // Phase 4 parity: A/THR (Ctrl+J) + LOC (Ctrl+L) global hotkeys. The FCU
            // A/THR pushbutton arms/disconnects A/THR; LOC arms localizer.
            [HotkeyAction.ToggleAutothrust] = "A32NX.FCU_ATHR_PUSH",
            [HotkeyAction.ToggleLocalizer] = "A32NX.FCU_LOC_PUSH",
        };
    }

    // ---- Tracked single-instance hotkey windows (FCU value windows, Baro, E/WD pop-out). ----
    // Reuse-if-open: a second press of the hotkey focuses the existing window instead of
    // stacking a duplicate (HS787 _autopilotWindow pattern). All tracked windows are
    // disposed on aircraft swap via StopAllMotion() so a discarded def instance can't
    // keep live windows (and the E/WD window's refresh timer) running against the
    // new aircraft.
    private readonly Dictionary<Type, Form> _trackedWindows = new();

    private void ShowTrackedWindow<T>(Func<T> factory, Action<T> show) where T : Form
    {
        if (_trackedWindows.TryGetValue(typeof(T), out var existing) && !existing.IsDisposed)
        {
            show((T)existing);
            return;
        }
        var form = factory();
        _trackedWindows[typeof(T)] = form;
        // Only evict OUR entry — guards against a stale close (e.g. a future
        // hide-on-close window's deferred real close) removing a successor window.
        form.FormClosed += (s, _) =>
        {
            if (_trackedWindows.TryGetValue(typeof(T), out var cur) && ReferenceEquals(cur, s))
                _trackedWindows.Remove(typeof(T));
        };
        show(form);
    }

    private void DisposeTrackedWindows()
    {
        foreach (var f in _trackedWindows.Values.ToList())
        {
            try
            {
                if (f.IsDisposed) continue;
                // Form.Dispose() raises neither FormClosing nor FormClosed (the documented
                // hide-on-close/RMP trap), but the FCU windows tear their refresh timers
                // down in OnFormClosing — Close() first so the timers actually stop. None
                // of the tracked windows hide-on-close, so Close() really closes (and the
                // FormClosed dict self-removal is safe against the ToList copy).
                if (f.IsHandleCreated) f.Close();
                if (!f.IsDisposed) f.Dispose();
            }
            catch { }
        }
        _trackedWindows.Clear();
    }

    /// <summary>
    /// Handles complex hotkey actions that require custom dialogs or logic.
    /// </summary>
    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        // Handle aircraft-specific actions
        switch (action)
        {
            // Ctrl+M variable monitor manager (parity with the A380/Fenix/PMDG).
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                if (parentForm is MainForm mfMon) mfMon.ShowA320MonitorManagerDialog();
                return true;
            // On-demand altimeter / baro setting (output mode + B). Reads the cached EFIS
            // baro (continuously monitored), so it's instant.
            case HotkeyAction.ReadAltimeter:
            {
                // Fenix/PMDG-style dual-unit readout (parity with the A380's B readout):
                // "Altimeter: 1013, 29.92" / "Altimeter standard". The live knob-turn
                // auto-announce + panel still use BaroPhrase (single-unit, per-side).
                // Read STD + the baro value LIVE from the cache (both are continuously
                // monitored) so a stale change-tracked field can never make this say
                // "standard" while the EFIS is actually on QNH — same fix as the A380's
                // B readout (41cd7b8).
                double? modeC = simConnect.GetCachedVariableValue("A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE");
                int baroMode = modeC.HasValue ? (int)Math.Round(modeC.Value) : (_baroMode < 0 ? 1 : _baroMode);
                double baroHpa = _baroHpa < 0 ? 1013 : _baroHpa;
                double? baroW = simConnect.GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO_HPA");
                if (baroW.HasValue)
                {
                    double w = baroW.Value >= 4294967296.0 ? new SimConnect.Arinc429Word(baroW.Value).ValueOr(0f) : baroW.Value;
                    if (w > 0) baroHpa = w;
                }
                // The _HPA word is WHOLE-hPa quantized on the A32NX, so the inches
                // half converts from the IN-UNIT word when the FCU is in inHg mode
                // (0.001 res); the hPa half keeps the whole-hPa word (exact there).
                double inchesOut = baroHpa * 0.0295299830714;
                double? inUnitW = simConnect.GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO");
                if (inUnitW.HasValue)
                {
                    double iu = inUnitW.Value >= 4294967296.0 ? new SimConnect.Arinc429Word(inUnitW.Value).ValueOr(0f) : inUnitW.Value;
                    if (iu >= 22 && iu <= 33) inchesOut = iu;            // FCU in inHg mode
                    else if (iu >= 745 && iu <= 1100) baroHpa = iu;       // FCU in hPa mode (same value, full res)
                }
                announcer.AnnounceImmediate(baroMode == 0
                    ? "Altimeter standard"
                    : $"Altimeter: {baroHpa:F0}, {inchesOut:F2}");
                return true;
            }
            // Ctrl+W (output): ND TO-waypoint name/distance/bearing via SimVars (no Coherent — see NdWaypointReadout).
            case HotkeyAction.ReadNDWaypoint:
                Services.NdWaypointReadout.Announce(simConnect, announcer);
                return true;
            // D / Shift+D: distance + time to destination / Top of Descent. The A32NX FMS
            // exposes the same guidanceController as the A380 over the Coherent debugger
            // (A32NX_MCDU view) — read it via the one-shot CoherentEvalClient and announce
            // identically to the A380 (PMDG-format TOD). MainForm owns the eval + readout.
            case HotkeyAction.ReadDistanceToDest:
                if (parentForm is MainForm mfDestA32) mfDestA32.AnnounceA32NXFlightInfo(false);
                return true;
            case HotkeyAction.ReadDistanceToTOD:
                if (parentForm is MainForm mfTodA32) mfTodA32.AnnounceA32NXFlightInfo(true);
                return true;
            // FCU value windows (Fenix-style: value entry + knob Push/Pull + mode
            // toggles + spoken read-out). Mirrors the A380's Ctrl+S/H/A/V/P/B windows;
            // replaces the old single-field ShowA320*InputDialog dialogs.
            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA320.FBWA320HeadingWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;

            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA320.FBWA320SpeedWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;

            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA320.FBWA320AltitudeWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;

            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA320.FBWA320VSWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;

            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA320.FBWA320AutopilotWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;

            // A32NX FCU value readouts
            case HotkeyAction.ReadHeading:
                RequestFCUHeadingWithStatus(simConnect);
                return true;

            case HotkeyAction.ReadSpeed:
                RequestFCUSpeedWithStatus(simConnect);
                return true;

            case HotkeyAction.ReadAltitude:
                RequestFCUAltitudeWithStatus(simConnect);
                return true;

            case HotkeyAction.ReadFCUVerticalSpeedFPA:
                RequestFCUVerticalSpeedFPA(simConnect);
                return true;

            // A32NX-specific data readouts
            case HotkeyAction.ReadFuelQuantity:
                RequestFuelQuantity(simConnect);
                return true;

            // W repurposed to gross weight in pounds (matches PMDG / Fenix, which also
            // repurpose the waypoint key). Fuel (F=lb / Shift+F=kg) and Shift+W (kg)
            // already use the shared fleet requests; this aligns W to the fleet too.
            case HotkeyAction.ReadWaypointInfo: // W -> "Gross weight N pounds, center of gravity X% MAC"
                announcer.AnnounceImmediate(_gwKgCache > 0
                    ? $"Gross weight {_gwKgCache * 2.204625:0} pounds{CgMacPhrase()}"
                    : "Gross weight not available");
                return true;

            case HotkeyAction.ReadApproachCapability:
                HandleReadApproachCapability(simConnect, announcer);
                return true;

            // A32NX-specific speed tape readouts
            case HotkeyAction.ReadSpeedGD:
                RequestSpeedGD(simConnect);
                return true;

            case HotkeyAction.ReadSpeedS:
                RequestSpeedS(simConnect);
                return true;

            case HotkeyAction.ReadSpeedF:
                RequestSpeedF(simConnect);
                return true;

            case HotkeyAction.ReadSpeedVLS:
                RequestSpeedVLS(simConnect);
                return true;

            case HotkeyAction.ReadSpeedVS:
                RequestSpeedVS(simConnect);
                return true;

            case HotkeyAction.ReadSpeedVFE:
                RequestSpeedVFE(simConnect);
                return true;

            // PFD / ND / ECAM / SD display WINDOWS are removed — the A32NX now reads
            // these through the accessible status-box panels (Instrument > PFD / ND /
            // ISIS / System Display), exactly like the A380. The ShowPFD /
            // ShowNavigationDisplay / ShowECAM / ShowStatusPage hotkeys were retired
            // app-wide and no longer exist.
            // EWD on-demand read (output mode → ReadDisplayUpperECAM): opens the E/WD as a
            // pop-out WINDOW (auto-refreshing, F5 to refresh, Escape to close) showing the
            // DECODED upper E/WD (engine row + memos + warnings) from SimVars — NOT a Coherent
            // scrape (the schematic engine row scraped as "XX XX" garbage and the scrape socket
            // is a documented native-crash risk). The continuous EWD-line monitor still
            // auto-announces new memos/warnings independently.
            case HotkeyAction.ReadDisplayUpperECAM:
                hotkeyManager.ExitOutputHotkeyMode();
                ShowTrackedWindow(
                    () => new Forms.FbwEwdWindow("A320 E/WD — Engine / Warning Display",
                        () => BuildEwdWindowTextAsync(simConnect), announcer),
                    w => { w.Show(); w.BringToFront(); w.Activate(); });
                return true;
            // On-demand flaps / gear read (parity with the A380; L and Shift+G). Read
            // straight from the live cache — a forced request of an UNCHANGED monitored
            // var never re-fires ProcessSimVarUpdate, which left the read silent.
            case HotkeyAction.ReadFlaps:
            {
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
                double? gv = simConnect.GetCachedVariableValue("GEAR_HANDLE_POSITION");
                if (gv.HasValue) announcer.AnnounceImmediate(gv.Value > 0.5 ? "Gear down" : "Gear up");
                else if (simConnect.IsConnected) { _reqGear = true; simConnect.RequestVariable("GEAR_HANDLE_POSITION", forceUpdate: true); }
                return true;
            }
            case HotkeyAction.ReadFuelInfo:
                if (simConnect.IsConnected) { _reqFuelKg = true; simConnect.RequestVariable("FUEL_QUANTITY_KG", forceUpdate: true); }
                return true;

            case HotkeyAction.ToggleECAMMonitoring:
                ToggleA320ECAMMonitoring(simConnect, announcer);
                return true;

            case HotkeyAction.ReadGrossWeightKg: // Shift+W -> gross weight KILOGRAMS + CG
                // Deterministic kilograms (like the A380), NOT WeightUser — so a kg
                // readout is always available regardless of the EFB US-Units toggle
                // (otherwise imperial mode made Shift+W duplicate W's pounds).
                announcer.AnnounceImmediate(_gwKgCache > 0
                    ? $"Gross weight {_gwKgCache:0} kilograms{CgMacPhrase()}"
                    : "Gross weight not available");
                return true;

            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                ShowTrackedWindow(() => new Forms.FBWA320.FBWA320BaroWindow(this, simConnect, announcer), w => w.ShowForm());
                return true;
        }

        // Fall back to base class for simple variable mappings
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    // Helper methods for A32NX-specific hotkey actions
    private void HandleReadApproachCapability(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // A32NX_APPROACH_CAPABILITY no longer exists in FBW — decode the FMGC FG
        // discrete word 4 (same source the PFD FMA uses).
        var cachedValue = simConnect.GetCachedVariableValue("PFD_AUTOLAND");
        if (cachedValue.HasValue)
        {
            var w = new SimConnect.Arinc429Word(cachedValue.Value);
            string cap = (!w.IsNormalOperation && !w.IsFunctionalTest) ? "none computed"
                : w.BitValueOr(25, false) ? "LAND 3 dual"
                : w.BitValueOr(24, false) ? "LAND 3 single"
                : w.BitValueOr(23, false) ? "LAND 2" : "none computed";
            announcer.AnnounceImmediate($"Approach capability {cap}");
        }
        else
        {
            announcer.AnnounceImmediate("Approach capability not available");
        }
    }

    private void ToggleA320ECAMMonitoring(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        bool isEnabled = simConnect.ToggleECAMMonitoring();
        string statusMessage = isEnabled ? "E W D monitoring enabled" : "E W D monitoring disabled";
        announcer.AnnounceImmediate(statusMessage);
    }

    // ==================================================================================
    // A32NX-Specific Data Request Methods
    // These methods request A320-specific data from the simulator
    // ==================================================================================

    /// <summary>
    /// Processes incoming A32NX variable updates, including FCU display variables that need combining.
    /// Called from MainForm.OnSimVarUpdated for every variable update to allow aircraft-specific processing.
    /// Returns true if the variable was fully processed and no further generic processing is needed.
    /// </summary>
    // FMA armed-mode decode (legacy A32NX_FMA_*_ARMED bitmasks; bit 0 = ALT). Decodes
    // to mode names so arming a mode speaks "Altitude armed" / "NAV armed" instead of
    // the old raw bitmask number. Matches the A380.
    private int _prevVertArmed = -1, _prevLatArmed = -1;
    private static readonly (int bit, string name)[] _vertArmedBits =
        { (1, "Altitude"), (2, "Altitude constraint"), (4, "Climb"), (8, "Descent"), (16, "Glideslope"), (32, "Final"), (64, "TCAS") };
    private static readonly (int bit, string name)[] _latArmedBits = { (1, "NAV"), (2, "Localizer") };
    private static string DecodeArmedModes(int v, (int bit, string name)[] bits)
    {
        var names = new List<string>();
        foreach (var b in bits) if ((v & b.bit) != 0) names.Add(b.name);
        return string.Join(", ", names);
    }

    // ---- A320 System Display (SD) + E/WD accessible read-out -------------------
    // The A32NX SD page index is system-written/read-only (verified PagesContainer.tsx:111),
    // so — unlike the A380 — we cannot force a page to scrape it. The "System Display"
    // panel combo selects a page: the E/WD is SCRAPED (single page, no switching needed);
    // the SD system pages (ELEC/HYD/... added one at a time) read decoded SimVars. The
    // status box shows the selected page's content, populated on selection — no
    // auto-speech, no manual refresh. Combo backed by an MSFSBA-internal L:var.
    // EFIS baro (altimeter) state — auto-announced on knob turn + read on demand (B).
    private string? _lastAutolandCap; // last decoded LAND capability ("none"/"LAND 2"/...)
    private int _fmgcPhase = -1; // numeric FMGC flight phase (0 Preflight..7 Done); gates the capability announce
    private bool _baroStdDiscrep, _baroRefDiscrep; // FWC word 124 bits 24/25 (announced edges)
    private double _baroHpa = -1;          // last decoded captain baro, hectopascals (FBW quantizes to WHOLE hPa)
    private int _baroMode = -1;            // A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE: 0=STD,1=hPa,2=inHg
    private double _baroHpaR = -1;         // last decoded F/O baro, hectopascals
    private int _baroModeR = -1;           // A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE
    private double _baroInUnitL = -1, _baroInUnitR = -1; // FCU in-active-unit words (0.001 res — the inHg precision source)
    private string? _lastBaroPhraseL, _lastBaroPhraseR;  // phrase-level dedup (seeded silently)

    // ---- COM radio auto-announce state (Fenix/A380 RMP parity) ----
    // Keyed by var key ("COM_ACTIVE_FREQUENCY:1", "COM_TRANSMIT:2"); freqs in kHz.
    // Seeded silently on first sample so connecting doesn't read the whole stack.
    private readonly Dictionary<string, double> _lastComKhz = new();
    private readonly Dictionary<string, bool> _comTxOn = new();
    // inUnit = the FCU's IN-ACTIVE-UNIT word (0.001 resolution). The A32NX
    // quantizes its _HPA word to WHOLE hPa, so converting it to inches is ±0.01
    // off (live KORD 2026-06-12: HPA=1002.0 vs in-unit 29.60 → spoke "29.59").
    // Use the in-unit value whenever it is in the unit's sane range.
    private static string BaroPhrase(double hpa, int mode, double inUnit = -1)
    {
        if (mode == 0) return "Altimeter standard";
        if (mode == 2)
        {
            double inches = (inUnit >= 22 && inUnit <= 33) ? inUnit : hpa * 0.0295299830714;
            return $"Altimeter {inches:F2} inches";
        }
        double hpaVal = (inUnit >= 745 && inUnit <= 1100) ? inUnit : hpa;
        return $"Altimeter {hpaVal:F0} hectopascals";
    }

    // Phrase-level dedup + announce for either side's baro. Phrase-keyed (not
    // whole-hPa-keyed) so a 0.01-inch knob click that doesn't cross a whole-hPa
    // boundary still announces, and a repeated identical value stays silent.
    // First valid phrase per side seeds SILENTLY (the startup double-announce fix).
    private void AnnounceBaroIfChanged(bool capt, ScreenReaderAnnouncer announcer)
    {
        int mode = capt ? _baroMode : _baroModeR;
        double hpa = capt ? _baroHpa : _baroHpaR;
        double inUnit = capt ? _baroInUnitL : _baroInUnitR;
        if (mode < 0) return;                       // mode not seeded yet
        if (mode != 0 && hpa <= 0 && inUnit <= 0) return; // no value yet
        string phrase = BaroPhrase(hpa, mode, inUnit);
        string? last = capt ? _lastBaroPhraseL : _lastBaroPhraseR;
        if (capt) _lastBaroPhraseL = phrase; else _lastBaroPhraseR = phrase;
        if (last != null && last != phrase)
            announcer.Announce(capt ? phrase : "First Officer " + phrase);
    }

    public const string SdPageVar = "A32NX_MSFSBA_SD_PAGE";
    private long _sdWriteSeq;   // makes the SD-page calc write unique each time (anti-dedup, see HandleUIVariableSet)
    private string _sdBoxContent = "";
    private int _sdRefreshSeq;   // "latest request wins" guard for SD-page refresh (mirrors A380)

    // ---- ND status-box cache ---------------------------------------------------
    // The TO-waypoint ident is packed 6-bit-per-char (8 chars in word 0 — enough for
    // any real ident; word 1 cached for completeness). Cached as it flows through
    // ProcessSimVarUpdate; TryGetDisplayOverride on the *_0 word decodes it.
    private double _ndIdent0, _ndIdent1;
    private double _apprMsg0, _apprMsg1;   // packed ND approach message (e.g. "CAT 3 DUAL")

    // ---- Doors — read-only status map (parity with A380 _doorDefs) ----
    // Verified live on the running A32NX (TRUST THIS map):
    //  - Passenger doors = stock SimVar INTERACTIVE POINT OPEN:0..3 (exit type 0). A 0..1
    //    open fraction; open when value > 0.05. MUST be Type = SimVar (space+colon name).
    //  - Cargo doors = FBW L-vars A32NX_FWD/AFT_DOOR_CARGO_LOCKED (bool, INVERTED:
    //    1 = locked = Closed, < 0.5 = Open). FBW senses cargo via the LOCKED L-vars, NOT
    //    via interactive points 4/5.
    // Distinct alias keys (_DOOR_ vs _CARGO_) let the announce/display logic tell
    // passenger from cargo by prefix. Doors are read-only — the flyPad opens/closes them.
    private static readonly (string Key, string Name, string Var, bool IsSimVar, bool CargoLocked)[] _doorDefs = new[]
    {
        ("A32NX_MSFSBA_DOOR_0",  "Forward Left Door",  "INTERACTIVE POINT OPEN:0", true,  false),
        ("A32NX_MSFSBA_DOOR_1",  "Forward Right Door", "INTERACTIVE POINT OPEN:1", true,  false),
        ("A32NX_MSFSBA_DOOR_2",  "Aft Left Door",      "INTERACTIVE POINT OPEN:2", true,  false),
        ("A32NX_MSFSBA_DOOR_3",  "Aft Right Door",     "INTERACTIVE POINT OPEN:3", true,  false),
        ("A32NX_MSFSBA_CARGO_FWD", "Forward Cargo Door", "A32NX_FWD_DOOR_CARGO_LOCKED", false, true),
        ("A32NX_MSFSBA_CARGO_AFT", "Aft Cargo Door",     "A32NX_AFT_DOOR_CARGO_LOCKED", false, true),
    };
    private readonly Dictionary<string, bool> _doorOpen = new();

    // Last-announced aircraft-preset load-progress 10%-bucket (-1 = idle).
    private int _presetBucket = -1;

    // On-demand flaps/gear read (output-mode L / Shift+G) — request the var, announce
    // when it arrives in ProcessSimVarUpdate (parity with the A380).
    private bool _reqFlaps, _reqGear, _reqFuelKg;
    private double _gwCgMac = -1;   // gross-weight CG %MAC (FBW L-var, cached)
    private double _gwKgCache = -1; // gross weight in kg (stock TOTAL WEIGHT, cached)

    // Spoken CG suffix for the gross-weight readouts. Empty (suppressed) when the CG
    // isn't available/sane, so the gross-weight readout never breaks or says "CG 0".
    private string CgMacPhrase() => (_gwCgMac > 5 && _gwCgMac < 60) ? $", center of gravity {_gwCgMac:0.0} percent MAC" : "";

    // Weight-unit read-out preference (kg/lb), followed from the A32NX EFB "US Units"
    // setting (A32NX_EFB_USING_METRIC_UNIT). The raw GW/fuel vars are kilograms.
    private bool _metricWeight = true;
    private bool _metricWeightKnown;
    private (double value, string unit) WeightUser(double kg)
        => _metricWeight ? (kg, "kilograms") : (kg * 2.204625, "pounds");

    // FBW packs idents/messages 6 bits per char, 8 chars per word (low bits first),
    // char = code + 31 (matches the old NavigationDisplayForm decoder).
    private static string UnpackSixBit(double w0, double w1)
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

    // FBW ND FM message flag bits (EfisNdFmMessageFlags) — same table the deleted
    // NavigationDisplayForm.DecodeFmMessageFlags used, and the same bits
    // SimConnectManager.FormatNDFMMessage announces (priority-first) on change.
    private static readonly string[] FmMessageNames =
    {
        "Select True Ref",               // bit 0
        "Check North Ref",               // bit 1
        "Nav Accuracy Downgrade",        // bit 2
        "Nav Accuracy Upgrade No GPS",   // bit 3
        "Specified VOR DME Unavailable", // bit 4
        "Nav Accuracy Upgrade GPS",      // bit 5
        "GPS Primary",                   // bit 6
        "Map Partly Displayed",          // bit 7
        "Set Offside Range Mode",        // bit 8
        "Offside FM Control",            // bit 9
        "Offside FM Weather Control",    // bit 10
        "Offside Weather Control",       // bit 11
        "GPS Primary Lost",              // bit 12
        "RTA Missed",                    // bit 13
        "Backup Nav",                    // bit 14
    };

    private static string DecodeFmMessageFlags(int flags)
    {
        var msgs = new List<string>();
        for (int bit = 0; bit < FmMessageNames.Length; bit++)
            if ((flags & (1 << bit)) != 0) msgs.Add(FmMessageNames[bit]);
        return msgs.Count == 0 ? "" : string.Join(", ", msgs);
    }

    private static readonly Dictionary<double, string> SdPageNames = new()
    {
        [0] = "Upper E/WD", [1] = "Electrical", [2] = "Hydraulics", [3] = "Pressurization",
        [4] = "APU", [5] = "Air Conditioning", [6] = "Wheel / Brakes", [7] = "Bleed",
        [8] = "Fuel", [9] = "Doors", [10] = "Engine", [11] = "Flight Controls"
    };

    // Per-system SD readout rows (decoded SimVars). Added one system at a time.
    // Instance (not static) so the FUEL rows can follow the metric toggle via WeightUser.
    // `cache` (optional) lets a page pick the ACTIVE source among redundant controllers
    // (COND ACSC 1/2, PRESS CPC 1/2, FCTL FAC 1/2) at row-BUILD time — the row model reads
    // ONE var per row, so the active var must be chosen here. When `cache` is null (the
    // auto-register pass), each selector defaults to controller 1 AND every candidate var
    // is still emitted as a hidden registration row, so whichever the live cache later
    // picks is already registered for OnRequest reads.
    private List<(string label, string var, Func<double, string> fmt)> SdSystemRows(int page, Func<string, double?> cache = null!)
    {
        // ARINC429 decoders — for vars FBW publishes as ARINC words (verified via
        // useArinc429Var in the fbw-a32nx SD source: APU N/EGT/LOW_FUEL_PRESSURE_FAULT,
        // brake temps, landing elevation). These ALWAYS decode — a magnitude gate is
        // wrong because a valid no-data word (e.g. APU EGT with the APU off, ~1.1e9)
        // is BELOW 2^32 yet still an ARINC word, so the gate rendered the raw number.
        string CAir(double v) { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} degrees" : "not available"; }
        string PctAir(double v) { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} %" : "not available"; }
        string YesNoAir(double v) { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? (w.Value > 0.5 ? "yes" : "no") : "not available"; }
        // Plain (non-ARINC) formatters.
        string V(double v) => $"{v:0} V";
        string Pct(double v) => $"{v:0} %";
        string Psi(double v) => $"{v:0} psi";
        string C(double v) => $"{v:0} degrees";
        // Landing elevation is an ARINC429 word; the no-data sentinel (~2^32, SSM
        // not NormalOp) means the FMS is in AUTO (computes it from the dest runway).
        string LElev(double v)
        {
            if (v >= 4294967296.0)
            {
                var w = new SimConnect.Arinc429Word(v);
                return w.IsNormalOperation || w.IsFunctionalTest ? $"{w.ValueOr(0f):0} feet" : "auto";
            }
            return $"{v:0} feet";
        }
        string Lvl(double v) => $"{v:0.0} gal";
        string OnOff(double v) => v > 0.5 ? "powered" : "not powered";
        string OpenShut(double v) => v > 0.5 ? "open" : "closed";
        string YesNo(double v) => v > 0.5 ? "yes" : "no";
        var r = new List<(string, string, Func<double, string>)>();
        // True when an ARINC429 discrete word in the cache is a VALID (NormalOp/FuncTest)
        // signal — used to pick the active controller among a redundant 1/2 pair. When
        // `cache` is null (the auto-register pass) we can't read validity, so callers
        // default to source 1 but ALSO emit the source-2 candidates as hidden rows
        // (var-only, fmt never invoked) so both are registered.
        bool ArincValid(string v)
        {
            double? raw = cache?.Invoke(v);
            if (!raw.HasValue) return false;
            var w = new SimConnect.Arinc429Word(raw.Value);
            return w.IsNormalOperation || w.IsFunctionalTest;
        }
        if (page == 2) // HYDRAULICS
        {
            r.Add(("Green pressure", "A32NX_HYD_GREEN_SYSTEM_1_SECTION_PRESSURE", Psi));
            r.Add(("Green reservoir", "A32NX_HYD_GREEN_RESERVOIR_LEVEL", Lvl));
            r.Add(("Yellow pressure", "A32NX_HYD_YELLOW_SYSTEM_1_SECTION_PRESSURE", Psi));
            r.Add(("Yellow reservoir", "A32NX_HYD_YELLOW_RESERVOIR_LEVEL", Lvl));
            r.Add(("Yellow elec pump", "A32NX_HYD_YELLOW_EPUMP_ACTIVE", v => v > 0.5 ? "running" : "off"));
            r.Add(("Blue pressure", "A32NX_HYD_BLUE_SYSTEM_1_SECTION_PRESSURE", Psi));
            r.Add(("Blue reservoir", "A32NX_HYD_BLUE_RESERVOIR_LEVEL", Lvl));
            r.Add(("Blue elec pump", "A32NX_HYD_BLUE_EPUMP_ACTIVE", v => v > 0.5 ? "running" : "off"));
            r.Add(("PTU valve", "A32NX_HYD_PTU_VALVE_OPENED", OpenShut));
            r.Add(("RAT stowed", "A32NX_RAT_STOW_POSITION", v => v < 0.05 ? "stowed" : $"deployed {v * 100:0}%"));
            // Reservoir status per system (overheat / air-pressure-low / level-low).
            r.Add(("Green reservoir overheat", "A32NX_HYD_GREEN_RESERVOIR_OVHT", YesNo));
            r.Add(("Green reservoir air pressure low", "A32NX_HYD_GREEN_RESERVOIR_AIR_PRESSURE_IS_LOW", YesNo));
            r.Add(("Green reservoir level low", "A32NX_HYD_GREEN_RESERVOIR_LEVEL_IS_LOW", YesNo));
            r.Add(("Blue reservoir overheat", "A32NX_HYD_BLUE_RESERVOIR_OVHT", YesNo));
            r.Add(("Blue reservoir air pressure low", "A32NX_HYD_BLUE_RESERVOIR_AIR_PRESSURE_IS_LOW", YesNo));
            r.Add(("Blue reservoir level low", "A32NX_HYD_BLUE_RESERVOIR_LEVEL_IS_LOW", YesNo));
            r.Add(("Yellow reservoir overheat", "A32NX_HYD_YELLOW_RESERVOIR_OVHT", YesNo));
            r.Add(("Yellow reservoir air pressure low", "A32NX_HYD_YELLOW_RESERVOIR_AIR_PRESSURE_IS_LOW", YesNo));
            r.Add(("Yellow reservoir level low", "A32NX_HYD_YELLOW_RESERVOIR_LEVEL_IS_LOW", YesNo));
            // Electric-pump overheat.
            r.Add(("Blue elec pump overheat", "A32NX_HYD_BLUE_EPUMP_OVHT", YesNo));
            r.Add(("Yellow elec pump overheat", "A32NX_HYD_YELLOW_EPUMP_OVHT", YesNo));
            // Engine-pump fire valves.
            r.Add(("Green pump fire valve", "A32NX_HYD_GREEN_PUMP_1_FIRE_VALVE_OPENED", OpenShut));
            r.Add(("Yellow pump fire valve", "A32NX_HYD_YELLOW_PUMP_1_FIRE_VALVE_OPENED", OpenShut));
        }
        else if (page == 3) // PRESSURIZATION
        {
            // The plain A32NX_PRESS_CABIN_* names DO NOT EXIST (read static 0) — the A32NX
            // publishes cab-press via the CPC ARINC429 words. The ACTIVE CPC is selected below
            // (CPC 1 discrete bit 11); "not available" when the word's SSM is invalid (matches
            // the SD showing XX).
            string FtAir(double v) { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} feet" : "not available"; }
            string FpmAir(double v) { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} feet per minute" : "not available"; }
            string PsiAir(double v) { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0.0} psi" : "not available"; }
            // Select the ACTIVE CPC: CPC 1's discrete word bit 11 set = CPC 1 active, else CPC 2.
            // On the auto-register pass (cache == null) default to CPC 1 and emit the CPC 2
            // candidates as hidden registration rows.
            int activeCpc = 1;
            double? cpc1Disc = cache?.Invoke("A32NX_PRESS_CPC_1_DISCRETE_WORD");
            if (cache != null)
                activeCpc = (cpc1Disc.HasValue && new SimConnect.Arinc429Word(cpc1Disc.Value).BitValueOr(11, false)) ? 1 : 2;
            string cpc = $"A32NX_PRESS_CPC_{activeCpc}_";
            // In MANUAL pressurization mode (MODE SEL -> MAN) the CPC ARINC words go
            // invalid — exactly the abnormal scenario where the pilot hand-flies cabin
            // V/S. Fall back to the plain A32NX_PRESS_MAN_* L:vars the FBW SD itself
            // reads in MAN mode (live-verified plain values at the gate).
            if (cache == null || ArincValid(cpc + "CABIN_ALTITUDE"))
            {
                r.Add(("Cabin altitude", cpc + "CABIN_ALTITUDE", FtAir));
                r.Add(("Cabin vertical speed", cpc + "CABIN_VS", FpmAir));
                r.Add(("Differential pressure", cpc + "CABIN_DELTA_PRESSURE", PsiAir));
                r.Add(("Outflow valve", cpc + "OUTFLOW_VALVE_OPEN_PERCENTAGE", PctAir));
            }
            else
            {
                r.Add(("Cabin altitude (manual mode)", "A32NX_PRESS_MAN_CABIN_ALTITUDE", v => $"{v:0} feet"));
                r.Add(("Cabin vertical speed (manual mode)", "A32NX_PRESS_MAN_CABIN_VS", v => $"{v:0} feet per minute"));
                r.Add(("Differential pressure (manual mode)", "A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE", v => $"{v:0.0} psi"));
                r.Add(("Outflow valve (manual mode)", "A32NX_PRESS_MAN_OUTFLOW_VALVE_OPEN_PERCENTAGE", v => $"{v:0} %"));
            }
            r.Add(("Safety valve", "A32NX_PRESS_SAFETY_VALVE_OPEN_PERCENTAGE", Pct));
            // Landing elevation: prefer the active CPC's word when it's a valid ARINC signal;
            // fall back to the FM value (which renders "auto" when the FMS computes it).
            string lElevVar = ArincValid(cpc + "LANDING_ELEVATION")
                ? cpc + "LANDING_ELEVATION"
                : "A32NX_FM1_LANDING_ELEVATION";
            r.Add(("Landing elevation", lElevVar, LElev));
            r.Add(("Manual pressurization mode", "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO", v => v > 0.5 ? "auto" : "manual"));
            if (cache == null)
            {
                // Hidden registration rows so the active-CPC selection always has its vars cached.
                r.Add(("Pressure controller 2 discrete", "A32NX_PRESS_CPC_2_DISCRETE_WORD", _ => ""));
                r.Add(("Pressure controller 2 cabin altitude", "A32NX_PRESS_CPC_2_CABIN_ALTITUDE", _ => ""));
                r.Add(("Pressure controller 2 cabin VS", "A32NX_PRESS_CPC_2_CABIN_VS", _ => ""));
                r.Add(("Pressure controller 2 delta pressure", "A32NX_PRESS_CPC_2_CABIN_DELTA_PRESSURE", _ => ""));
                r.Add(("Pressure controller 2 outflow valve", "A32NX_PRESS_CPC_2_OUTFLOW_VALVE_OPEN_PERCENTAGE", _ => ""));
                r.Add(("Pressure controller 1 landing elevation", "A32NX_PRESS_CPC_1_LANDING_ELEVATION", _ => ""));
                r.Add(("Pressure controller 2 landing elevation", "A32NX_PRESS_CPC_2_LANDING_ELEVATION", _ => ""));
                r.Add(("Pressure controller 1 discrete", "A32NX_PRESS_CPC_1_DISCRETE_WORD", _ => ""));
                // Manual-mode fallback vars (read when the CPC words go invalid).
                r.Add(("Manual cabin altitude", "A32NX_PRESS_MAN_CABIN_ALTITUDE", _ => ""));
                r.Add(("Manual cabin VS", "A32NX_PRESS_MAN_CABIN_VS", _ => ""));
                r.Add(("Manual delta pressure", "A32NX_PRESS_MAN_CABIN_DELTA_PRESSURE", _ => ""));
                r.Add(("Manual outflow valve", "A32NX_PRESS_MAN_OUTFLOW_VALVE_OPEN_PERCENTAGE", _ => ""));
            }
        }
        else if (page == 4) // APU
        {
            r.Add(("APU N", "A32NX_APU_N", PctAir));
            r.Add(("APU EGT", "A32NX_APU_EGT", CAir));
            r.Add(("Inlet flap", "A32NX_APU_FLAP_OPEN_PERCENTAGE", Pct));
            r.Add(("Bleed valve", "A32NX_APU_BLEED_AIR_VALVE_OPEN", OpenShut));
            r.Add(("Low fuel pressure", "A32NX_APU_LOW_FUEL_PRESSURE_FAULT", YesNoAir));
            r.Add(("Bleed pressure", "A32NX_PNEU_APU_BLEED_CONTAINER_PRESSURE", Psi));
            r.Add(("Master switch", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", v => v > 0.5 ? "on" : "off"));
            r.Add(("APU available", "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5 ? "available" : "not available"));
            r.Add(("Gen voltage", "A32NX_ELEC_APU_GEN_1_POTENTIAL", V));
            r.Add(("Gen load", "A32NX_ELEC_APU_GEN_1_LOAD", Pct));
        }
        else if (page == 5) // AIR CONDITIONING (COND)
        {
            r.Add(("Cockpit temp", "A32NX_COND_CKPT_TEMP", C));
            r.Add(("Forward cabin temp", "A32NX_COND_FWD_TEMP", C));
            r.Add(("Aft cabin temp", "A32NX_COND_AFT_TEMP", C));
            r.Add(("Cockpit duct temp", "A32NX_COND_CKPT_DUCT_TEMP", C));
            r.Add(("Forward duct temp", "A32NX_COND_FWD_DUCT_TEMP", C));
            r.Add(("Aft duct temp", "A32NX_COND_AFT_DUCT_TEMP", C));
            r.Add(("Pack 1 flow valve", "A32NX_COND_PACK_FLOW_VALVE_1_IS_OPEN", OpenShut));
            r.Add(("Pack 2 flow valve", "A32NX_COND_PACK_FLOW_VALVE_2_IS_OPEN", OpenShut));
            r.Add(("Cockpit trim air valve", "A32NX_COND_CKPT_TRIM_AIR_VALVE_POSITION", Pct));
            r.Add(("Forward trim air valve", "A32NX_COND_FWD_TRIM_AIR_VALVE_POSITION", Pct));
            r.Add(("Aft trim air valve", "A32NX_COND_AFT_TRIM_AIR_VALVE_POSITION", Pct));
            // ACSC discrete word 1: hot-air valve open = bit 20 CLEAR (inverted); switch = bit 23;
            // cabin fan 1/2 fault = bits 25/26. SSM-gated. Pick the ACTIVE ACSC: use 1 unless
            // its word is invalid (then 2). On the auto-register pass (cache == null) default
            // to 1 and emit the ACSC_2 word as a hidden registration row.
            string acscWord = ArincValid("A32NX_COND_ACSC_1_DISCRETE_WORD_1")
                ? "A32NX_COND_ACSC_1_DISCRETE_WORD_1"
                : (cache != null ? "A32NX_COND_ACSC_2_DISCRETE_WORD_1" : "A32NX_COND_ACSC_1_DISCRETE_WORD_1");
            r.Add(("Hot air valve", acscWord, v => { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? (w.BitValueOr(20, false) ? "closed" : "open") : "not available"; }));
            r.Add(("Hot air switch", acscWord, v => { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? (w.BitValueOr(23, false) ? "on" : "off") : "not available"; }));
            r.Add(("Cabin fan 1", acscWord, v => { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? (w.BitValueOr(25, false) ? "fault" : "normal") : "not available"; }));
            r.Add(("Cabin fan 2", acscWord, v => { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? (w.BitValueOr(26, false) ? "fault" : "normal") : "not available"; }));
            if (cache == null) r.Add(("Air conditioning controller 2", "A32NX_COND_ACSC_2_DISCRETE_WORD_1", _ => ""));
        }
        else if (page == 6) // WHEEL / BRAKES
        {
            r.Add(("Brake 1 temp", "A32NX_REPORTED_BRAKE_TEMPERATURE_1", CAir));
            r.Add(("Brake 2 temp", "A32NX_REPORTED_BRAKE_TEMPERATURE_2", CAir));
            r.Add(("Brake 3 temp", "A32NX_REPORTED_BRAKE_TEMPERATURE_3", CAir));
            r.Add(("Brake 4 temp", "A32NX_REPORTED_BRAKE_TEMPERATURE_4", CAir));
            r.Add(("Autobrake mode", "A32NX_AUTOBRAKES_ARMED_MODE",
                v => v < 0.5 ? "Off" : v < 1.5 ? "Low" : v < 2.5 ? "Medium" : "Max"));
            r.Add(("Autobrake active", "A32NX_AUTOBRAKES_ACTIVE", YesNo));
            // Landing-gear positions (% extended).
            string Gear(double v) => v > 95 ? "down" : v < 5 ? "up" : "in transit";
            r.Add(("Nose gear", "A32NX_GEAR_CENTER_POSITION", Gear));
            r.Add(("Left gear", "A32NX_GEAR_LEFT_POSITION", Gear));
            r.Add(("Right gear", "A32NX_GEAR_RIGHT_POSITION", Gear));
            r.Add(("Anti-skid", "ANTISKID BRAKES ACTIVE", v => v > 0.5 ? "active" : "inactive"));
        }
        else if (page == 7) // BLEED
        {
            r.Add(("Eng 1 precooler temp", "A32NX_PNEU_ENG_1_BLEED_TEMPERATURE_SENSOR_TEMPERATURE", C));
            r.Add(("Eng 1 bleed pressure", "A32NX_PNEU_ENG_1_REGULATED_TRANSDUCER_PRESSURE", Psi));
            r.Add(("Eng 1 bleed valve", "A32NX_PNEU_ENG_1_PR_VALVE_OPEN", OpenShut));
            r.Add(("Eng 1 HP valve", "A32NX_PNEU_ENG_1_HP_VALVE_OPEN", OpenShut));
            r.Add(("Eng 2 precooler temp", "A32NX_PNEU_ENG_2_BLEED_TEMPERATURE_SENSOR_TEMPERATURE", C));
            r.Add(("Eng 2 bleed pressure", "A32NX_PNEU_ENG_2_REGULATED_TRANSDUCER_PRESSURE", Psi));
            r.Add(("Eng 2 bleed valve", "A32NX_PNEU_ENG_2_PR_VALVE_OPEN", OpenShut));
            r.Add(("Eng 2 HP valve", "A32NX_PNEU_ENG_2_HP_VALVE_OPEN", OpenShut));
            r.Add(("Pack 1 flow", "A32NX_COND_PACK_FLOW_1", Pct));
            r.Add(("Pack 2 flow", "A32NX_COND_PACK_FLOW_2", Pct));
            r.Add(("Cross-bleed valve", "A32NX_PNEU_XBLEED_VALVE_FULLY_OPEN", OpenShut));
            r.Add(("APU bleed valve", "A32NX_APU_BLEED_AIR_VALVE_OPEN", OpenShut));
            r.Add(("Ram air valve", "A32NX_AIRCOND_RAMAIR_TOGGLE", v => v > 0.5 ? "open" : "closed"));
            r.Add(("Wing anti-ice", "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", v => v > 0.5 ? "on" : "off"));
        }
        else if (page == 8) // FUEL
        {
            // Fuel rows follow the metric toggle (kg/lb per the EFB "US Units" setting).
            string Wt(double kg) { var (val, u) = WeightUser(kg); return $"{val:0} {u}"; }
            string Wth(double kgh) { var (val, u) = WeightUser(kgh); return $"{val:0} {u} per hour"; }
            // Per-tank quantities are stock SimVars in GALLONS; convert to weight using a
            // fixed Jet-A density (~3.039 kg/gal — SdSystemRows has no SimConnect handle to
            // read the live FUEL WEIGHT PER GALLON, and Jet-A density barely varies) and
            // route through WeightUser so the value follows the metric toggle.
            const double kgPerGal = 3.039;
            string TankWt(double gal)
            {
                var (val, u) = WeightUser(gal * kgPerGal);
                return $"{val:0} {u}";
            }
            r.Add(("Left outer tank", "FUEL TANK LEFT AUX QUANTITY", TankWt));
            r.Add(("Left inner tank", "FUEL TANK LEFT MAIN QUANTITY", TankWt));
            r.Add(("Center tank", "FUEL TANK CENTER QUANTITY", TankWt));
            r.Add(("Right inner tank", "FUEL TANK RIGHT MAIN QUANTITY", TankWt));
            r.Add(("Right outer tank", "FUEL TANK RIGHT AUX QUANTITY", TankWt));
            r.Add(("Fuel flow eng 1", "A32NX_ENGINE_FF:1", Wth));
            r.Add(("Fuel flow eng 2", "A32NX_ENGINE_FF:2", Wth));
            r.Add(("Fuel used eng 1", "A32NX_FUEL_USED:1", Wt));
            r.Add(("Fuel used eng 2", "A32NX_FUEL_USED:2", Wt));
            r.Add(("Total fuel on board", "A32NX_TOTAL_FUEL_QUANTITY", Wt));
            // Pumps + valves.
            string Running(double v) => v > 0.5 ? "running" : "off";
            r.Add(("Left pump 1", "FUELSYSTEM PUMP ACTIVE:2", Running));
            r.Add(("Left pump 2", "FUELSYSTEM PUMP ACTIVE:5", Running));
            r.Add(("Right pump 1", "FUELSYSTEM PUMP ACTIVE:3", Running));
            r.Add(("Right pump 2", "FUELSYSTEM PUMP ACTIVE:6", Running));
            r.Add(("Center pump", "FUELSYSTEM PUMP ACTIVE:1", Running));
            r.Add(("Engine 1 LP valve", "FUELSYSTEM VALVE OPEN:1", OpenShut));
            r.Add(("Engine 2 LP valve", "FUELSYSTEM VALVE OPEN:2", OpenShut));
            r.Add(("Crossfeed valve", "FUELSYSTEM VALVE OPEN:3", OpenShut));
        }
        else if (page == 9) // DOORS
        {
            r.Add(("Forward cargo door", "A32NX_FWD_DOOR_CARGO_LOCKED", v => v > 0.5 ? "locked" : "unlocked"));
            r.Add(("Escape slides", "A32NX_SLIDES_ARMED", v => v > 0.5 ? "armed" : "disarmed"));
            // Crew oxygen supply pushbutton — pushbutton-out (0) = supply ON (inverted).
            r.Add(("Crew oxygen", "PUSH_OVHD_OXYGEN_CREW", v => v > 0.5 ? "off" : "on"));
        }
        else if (page == 1) // ELEC
        {
            r.Add(("Gen 1", "A32NX_ELEC_ENG_GEN_1_POTENTIAL", V));
            r.Add(("Gen 1 load", "A32NX_ELEC_ENG_GEN_1_LOAD", Pct));
            r.Add(("Gen 2", "A32NX_ELEC_ENG_GEN_2_POTENTIAL", V));
            r.Add(("Gen 2 load", "A32NX_ELEC_ENG_GEN_2_LOAD", Pct));
            r.Add(("APU gen", "A32NX_ELEC_APU_GEN_1_POTENTIAL", V));
            r.Add(("APU gen load", "A32NX_ELEC_APU_GEN_1_LOAD", Pct));
            r.Add(("Battery 1", "A32NX_ELEC_BAT_1_POTENTIAL", V));
            r.Add(("Battery 2", "A32NX_ELEC_BAT_2_POTENTIAL", V));
            r.Add(("Emergency gen", "A32NX_ELEC_EMER_GEN_POTENTIAL", V));
            r.Add(("AC bus 1", "A32NX_ELEC_AC_1_BUS_IS_POWERED", OnOff));
            r.Add(("AC bus 2", "A32NX_ELEC_AC_2_BUS_IS_POWERED", OnOff));
            r.Add(("AC ESS bus", "A32NX_ELEC_AC_ESS_BUS_IS_POWERED", OnOff));
            r.Add(("DC bus 1", "A32NX_ELEC_DC_1_BUS_IS_POWERED", OnOff));
            r.Add(("DC bus 2", "A32NX_ELEC_DC_2_BUS_IS_POWERED", OnOff));
            r.Add(("DC bat bus", "A32NX_ELEC_DC_BAT_BUS_IS_POWERED", OnOff));
            r.Add(("DC ESS bus", "A32NX_ELEC_DC_ESS_BUS_IS_POWERED", OnOff));
            r.Add(("AC ESS shed bus", "A32NX_ELEC_AC_ESS_SHED_BUS_IS_POWERED", OnOff));
            r.Add(("DC ESS shed bus", "A32NX_ELEC_DC_ESS_SHED_BUS_IS_POWERED", OnOff));
            r.Add(("APU gen frequency", "A32NX_ELEC_APU_GEN_1_FREQUENCY", v => $"{v:0} hertz"));
            r.Add(("Emergency gen frequency", "A32NX_ELEC_EMER_GEN_FREQUENCY", v => $"{v:0} hertz"));
            // Battery charge direction (signed amps: + = charging into the battery).
            string BatDir(double v) => Math.Abs(v) < 1 ? "idle" : (v > 0 ? "charging" : "discharging");
            r.Add(("Battery 1 status", "A32NX_ELEC_BAT_1_CURRENT", BatDir));
            r.Add(("Battery 2 status", "A32NX_ELEC_BAT_2_CURRENT", BatDir));
            // Contactors.
            string Closed(double v) => v > 0.5 ? "closed" : "open";
            r.Add(("Gen 1 line contactor", "A32NX_ELEC_CONTACTOR_9XU1_IS_CLOSED", Closed));
            r.Add(("Gen 2 line contactor", "A32NX_ELEC_CONTACTOR_9XU2_IS_CLOSED", Closed));
            r.Add(("Bus tie contactor", "A32NX_ELEC_CONTACTOR_11XU1_IS_CLOSED", Closed));
            r.Add(("APU gen contactor", "A32NX_ELEC_CONTACTOR_3XS_IS_CLOSED", Closed));
            r.Add(("External power contactor", "A32NX_ELEC_CONTACTOR_3XG_IS_CLOSED", Closed));
            // Transformer rectifiers (volts + amps).
            string Amp(double v) => $"{v:0} A";
            r.Add(("TR 1 voltage", "A32NX_ELEC_TR_1_POTENTIAL", V));
            r.Add(("TR 1 current", "A32NX_ELEC_TR_1_CURRENT", Amp));
            r.Add(("TR 2 voltage", "A32NX_ELEC_TR_2_POTENTIAL", V));
            r.Add(("TR 2 current", "A32NX_ELEC_TR_2_CURRENT", Amp));
            r.Add(("ESS TR voltage", "A32NX_ELEC_TR_3_POTENTIAL", V));
            r.Add(("ESS TR current", "A32NX_ELEC_TR_3_CURRENT", Amp));
            // IDG oil outlet temperature.
            r.Add(("IDG 1 temperature", "A32NX_ELEC_ENG_GEN_1_IDG_OIL_OUTLET_TEMPERATURE", C));
            r.Add(("IDG 2 temperature", "A32NX_ELEC_ENG_GEN_2_IDG_OIL_OUTLET_TEMPERATURE", C));
            // Galley load shed.
            r.Add(("Galley", "A32NX_ELEC_GALLEY_IS_SHED", v => v > 0.5 ? "shed" : "normal"));
        }
        else if (page == 10) // ENGINE — oil + vibration + igniters (N1/N2/EGT/FF are on the E/WD)
        {
            string Vib(double v) => $"{v:0.0}";
            string IgOnOff(double v) => v > 0.5 ? "on" : "off";
            r.Add(("Engine 1 oil quantity", "ENG_OIL_QUANTITY:1", Pct));
            r.Add(("Engine 1 oil temperature", "GENERAL_ENG_OIL_TEMPERATURE:1", C));
            r.Add(("Engine 1 oil pressure", "ENG_OIL_PRESSURE:1", Psi));
            r.Add(("Engine 1 vibration", "TURB_ENG_VIBRATION:1", Vib));
            r.Add(("Engine 1 igniter A", "A32NX_FADEC_IGNITER_A_ACTIVE_ENG1", IgOnOff));
            r.Add(("Engine 1 igniter B", "A32NX_FADEC_IGNITER_B_ACTIVE_ENG1", IgOnOff));
            r.Add(("Engine 2 oil quantity", "ENG_OIL_QUANTITY:2", Pct));
            r.Add(("Engine 2 oil temperature", "GENERAL_ENG_OIL_TEMPERATURE:2", C));
            r.Add(("Engine 2 oil pressure", "ENG_OIL_PRESSURE:2", Psi));
            r.Add(("Engine 2 vibration", "TURB_ENG_VIBRATION:2", Vib));
            r.Add(("Engine 2 igniter A", "A32NX_FADEC_IGNITER_A_ACTIVE_ENG2", IgOnOff));
            r.Add(("Engine 2 igniter B", "A32NX_FADEC_IGNITER_B_ACTIVE_ENG2", IgOnOff));
            r.Add(("Engine 1 starter valve", "A32NX_PNEU_ENG_1_STARTER_VALVE_OPEN", OpenShut));
            r.Add(("Engine 2 starter valve", "A32NX_PNEU_ENG_2_STARTER_VALVE_OPEN", OpenShut));
        }
        else if (page == 11) // FLIGHT CONTROLS — surface positions + trims (FCDC/FAC ARINC words)
        {
            // FCDC/FAC surface words are ARINC429, value in degrees. Direction words per FBW.
            string ArDeg(double v, string pos, string neg)
            {
                var w = new SimConnect.Arinc429Word(v);
                if (!(w.IsNormalOperation || w.IsFunctionalTest)) return "not available";
                double d = w.Value;
                return Math.Abs(d) < 0.1 ? "neutral" : $"{Math.Abs(d):0.0} degrees {(d > 0 ? pos : neg)}";
            }
            // THS: FBW shows DN for positive (nose-down trim), UP for negative.
            r.Add(("Pitch trim, THS", "A32NX_FCDC_1_ELEVATOR_TRIM_POS", v => ArDeg(v, "nose down", "nose up")));
            r.Add(("Left elevator", "A32NX_FCDC_1_ELEVATOR_LEFT_POS", v => ArDeg(v, "down", "up")));
            r.Add(("Right elevator", "A32NX_FCDC_1_ELEVATOR_RIGHT_POS", v => ArDeg(v, "down", "up")));
            r.Add(("Left aileron", "A32NX_FCDC_1_AILERON_LEFT_POS", v => ArDeg(v, "down", "up")));
            r.Add(("Right aileron", "A32NX_FCDC_1_AILERON_RIGHT_POS", v => ArDeg(v, "down", "up")));
            // Rudder deflection is a plain percent L:var (±100% ≈ ±25°); positive = right.
            r.Add(("Rudder", "A32NX_HYD_RUDDER_DEFLECTION",
                v => Math.Abs(v) < 0.5 ? "neutral" : $"{Math.Abs(v * 0.25):0.0} degrees {(v > 0 ? "right" : "left")}"));
            // Rudder trim ARINC word, positive = nose-left (matches the FCC-panel decode).
            // Pick the active FAC: use FAC 1 unless its discrete word 2 is invalid (then FAC 2).
            // On the auto-register pass (cache == null) default to FAC 1 and register FAC 2.
            string rudTrimVar = ArincValid("A32NX_FAC_1_DISCRETE_WORD_2")
                ? "A32NX_FAC_1_RUDDER_TRIM_POS"
                : (cache != null ? "A32NX_FAC_2_RUDDER_TRIM_POS" : "A32NX_FAC_1_RUDDER_TRIM_POS");
            r.Add(("Rudder trim", rudTrimVar, v => ArDeg(v, "left", "right")));
            if (cache == null)
            {
                r.Add(("Flight augmentation computer 1 discrete", "A32NX_FAC_1_DISCRETE_WORD_2", _ => ""));
                r.Add(("Flight augmentation computer 2 rudder trim", "A32NX_FAC_2_RUDDER_TRIM_POS", _ => ""));
            }
            r.Add(("Rudder travel limit", "A32NX_FAC_1_RUDDER_TRAVEL_LIMIT_COMMAND",
                v => { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} degrees" : "not available"; }));
            r.Add(("Speed brake handle", "A32NX_SPOILERS_HANDLE_POSITION", v => $"{v * 100:0} %"));
            r.Add(("Ground spoilers armed", "A32NX_SPOILERS_ARMED", v => v > 0.5 ? "armed" : "disarmed"));
        }
        return r;
    }

    // Build the DECODED Upper E/WD (SD page 0) text from SimVars — the engine row
    // (thrust rating/limit + per-engine N1 / N1-command / EGT / N2 / FF / state /
    // reverser) plus the live ECAM memo lines (A32NX_Ewd_LOWER_* codes → EWDMessageLookup).
    // This REPLACES the garbage schematic-engine scrape (which read as "XX XX N1 XX %…").
    // A320-specific: 2 engines, NO N3, NO per-engine THR% clamp (one A32NX_AUTOTHRUST_THRUST_LIMIT),
    // 7 memo lines per side, A32NX EWDMessageLookup (NOT the A380 table). Force-reads the
    // source vars, waits ~0.4 s, then builds from cache. Returns "" when there's no real
    // engine data AND no memos (the caller then shows a placeholder).
    private async Task<string> BuildEwdDecodedTextAsync(SimConnect.SimConnectManager sim)
    {
        try
        {
            // Force-read every var the decode consumes (engine params, thrust, idle/phase,
            // reversers, memos) so the cache is fresh before we read it.
            string[] toRead =
            {
                "A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE", "A32NX_AUTOTHRUST_THRUST_LIMIT",
                "A32NX_AIRLINER_TO_FLEX_TEMP",
                "A32NX_ENGINE_N1:1", "A32NX_ENGINE_N1:2",
                "A32NX_AUTOTHRUST_N1_COMMANDED:1", "A32NX_AUTOTHRUST_N1_COMMANDED:2",
                "A32NX_ENGINE_EGT:1", "A32NX_ENGINE_EGT:2",
                "A32NX_ENGINE_N2:1", "A32NX_ENGINE_N2:2",
                "A32NX_ENGINE_FF:1", "A32NX_ENGINE_FF:2",
                "A32NX_ENGINE_STATE:1", "A32NX_ENGINE_STATE:2",
                "A32NX_REVERSER_1_DEPLOYED", "A32NX_REVERSER_2_DEPLOYED",
                "A32NX_ENGINE_IDLE_N1", "A32NX_FWC_FLIGHT_PHASE", "A32NX_AUTOTHRUST_STATUS",
                "A32NX_TOTAL_FUEL_QUANTITY",
            };
            foreach (var v in toRead) sim.RequestVariable(v, forceUpdate: true);
            for (int i = 1; i <= 7; i++)
            {
                sim.RequestVariable($"A32NX_Ewd_LOWER_LEFT_LINE_{i}", forceUpdate: true);
                sim.RequestVariable($"A32NX_Ewd_LOWER_RIGHT_LINE_{i}", forceUpdate: true);
            }
            await System.Threading.Tasks.Task.Delay(400);

            int[] engs = { 1, 2 };
            string Grp(string varFmt, Func<double, string> fmt)
            {
                var parts = new List<string>();
                foreach (int e in engs)
                {
                    double? cv = sim.GetCachedVariableValue(string.Format(varFmt, e));
                    parts.Add($"Engine {e} " + (cv.HasValue && !double.IsNaN(cv.Value) ? fmt(cv.Value) : "--"));
                }
                return string.Join(", ", parts);
            }

            // Thrust rating type — enum from A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE
            // ['', CLB, MCT, FLX, TOGA, MREV]; FLX appends the flex temp when set.
            string[] thrModes = { "", "CLB", "MCT", "FLX", "TOGA", "MREV" };
            int tltI = (int)Math.Round(sim.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT_TYPE") ?? 0);
            string thrMode = (tltI >= 1 && tltI < thrModes.Length) ? thrModes[tltI] : "none";
            double? flex = sim.GetCachedVariableValue("A32NX_AIRLINER_TO_FLEX_TEMP");
            if (tltI == 3 && flex.HasValue && flex.Value > 0) thrMode += $" {flex.Value:0}°C";
            double? thrLim = sim.GetCachedVariableValue("A32NX_AUTOTHRUST_THRUST_LIMIT");
            string thrLimStr = thrLim.HasValue && !double.IsNaN(thrLim.Value) ? $"{Math.Abs(thrLim.Value):0}%" : "--";

            string EngState(double v) => v >= 2 ? "starting" : v >= 1 ? "on" : "off";

            // Fuel on board (kg var; WeightUser follows the metric toggle).
            double? fobKg = sim.GetCachedVariableValue("A32NX_TOTAL_FUEL_QUANTITY");
            string fobStr = "--";
            if (fobKg.HasValue && !double.IsNaN(fobKg.Value)) { var (fv, fu) = WeightUser(fobKg.Value); fobStr = $"{fv:0} {fu}"; }

            // Track whether any per-engine value is real (placeholder shown when nothing's cached).
            bool anyEngineData =
                engs.Any(e => sim.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}").HasValue
                           || sim.GetCachedVariableValue($"A32NX_ENGINE_EGT:{e}").HasValue
                           || sim.GetCachedVariableValue($"A32NX_ENGINE_STATE:{e}").HasValue);

            var lines = new List<string>
            {
                "Thrust rating: " + thrMode,
                "Thrust limit: "  + thrLimStr,
                "N1: "          + Grp("A32NX_ENGINE_N1:{0}",  v => $"{v:0.0}%"),
                "N1 command: "  + Grp("A32NX_AUTOTHRUST_N1_COMMANDED:{0}", v => $"{v:0.0}%"),
                "EGT: "         + Grp("A32NX_ENGINE_EGT:{0}", v => $"{v:0}°C"),
                "N2: "          + Grp("A32NX_ENGINE_N2:{0}",  v => $"{v:0.0}%"),
                "Fuel Flow: "   + Grp("A32NX_ENGINE_FF:{0}",  v => $"{v:0} kg/h"),
                "Engine state: "+ Grp("A32NX_ENGINE_STATE:{0}", EngState),
                "Fuel on board: " + fobStr,
            };

            // Reversers (engine 1/2) — only annunciate when deployed.
            var revOn = engs.Where(e => (sim.GetCachedVariableValue($"A32NX_REVERSER_{e}_DEPLOYED") ?? 0) > 0.5).ToList();
            if (revOn.Count > 0)
                lines.Add("Reverser: " + string.Join(" and ", revOn.Select(e => $"engine {e}")) + " deployed");

            // IDLE memo — both engines at/near idle AND in a flight phase AND autothrust active
            // (mirrors the FBW EWD Idle.tsx rule, A320 vars).
            double idleN1 = sim.GetCachedVariableValue("A32NX_ENGINE_IDLE_N1") ?? 0;
            double? fwcPhase = sim.GetCachedVariableValue("A32NX_FWC_FLIGHT_PHASE");
            bool athrActive = (sim.GetCachedVariableValue("A32NX_AUTOTHRUST_STATUS") ?? 0) > 0.5;
            bool bothIdle = engs.All(e => { var n1 = sim.GetCachedVariableValue($"A32NX_ENGINE_N1:{e}"); return n1.HasValue && n1.Value <= idleN1 + 2; });
            if (bothIdle && athrActive && fwcPhase.HasValue && fwcPhase.Value >= 5 && fwcPhase.Value <= 7)
                lines.Add("IDLE");

            // ECAM memo lines (7 per side) — decode each numeric code via EWDMessageLookup
            // (the A320 table), exactly as SimConnectManager does. Skip 0/blank codes.
            var memos = new List<string>();
            foreach (var lr in new[] { "LEFT", "RIGHT" })
                for (int i = 1; i <= 7; i++)
                {
                    // The memo CODE vars are diverted to ecamStringData in SimConnectManager and
                    // skipped from the numeric cache (the batch handler `continue`s past the cache
                    // write), so GetCachedVariableValue returns null for them — read the already-
                    // decoded string via GetEcamLineRaw instead.
                    string raw = sim.GetEcamLineRaw($"A32NX_Ewd_LOWER_{lr}_LINE_{i}");
                    string clean = SimConnect.EWDMessageLookup.CleanANSICodes(raw);
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        // Append the ECAM colour name after the memo (e.g. "AUTO BRK OFF, Amber"),
                        // matching the live EWD monitoring announcements so the screen reader conveys
                        // the severity colour in the Alt+E viewer too.
                        string color = SimConnect.EWDMessageLookup.GetMessagePriority(raw);
                        memos.Add(string.IsNullOrEmpty(color) ? clean : $"{clean}, {color}");
                    }
                }

            // No real engine data AND no memos → caller shows a placeholder.
            if (!anyEngineData && memos.Count == 0) return "";

            lines.Add("");
            lines.Add(memos.Count == 0 ? "Memo / warnings: none" : "Memo / warnings:");
            lines.AddRange(memos);
            return string.Join("\r\n", lines);
        }
        catch { return ""; }
    }

    // Build the upper-E/WD text for the Alt+E pop-out (FbwEwdWindow): the DECODED engine
    // row + memos from SimVars/SimConnect — NO Coherent scrape. The schematic ENGINE row
    // scraped as garbage ("XX XX N1 XX %…"), and the Coherent scrape socket is a documented
    // native-crash risk; the decode covers the content whenever the aircraft is powered.
    // Returns a placeholder when the decode has nothing (displays not powered).
    public async Task<string> BuildEwdWindowTextAsync(SimConnect.SimConnectManager sim)
    {
        if (sim == null || !sim.IsConnected) return "E/WD not available — not connected.";
        string decoded = await BuildEwdDecodedTextAsync(sim);
        return string.IsNullOrEmpty(decoded)
            ? "E/WD not available — power up the displays."
            : decoded;
    }

    // Populate the System Display status box for the selected page, then force the box
    // to re-render (RequestVariable → ProcessSimVarUpdate → UpdateDisplayText →
    // TryGetDisplayOverride). No speech; the box just fills in on selection.
    // Populate the "System Display" status box with the combo's CURRENT page as soon
    // as the panel is shown — so the user doesn't have to cycle the combo to get
    // content on first display.
    // Sim handle captured when any display panel is shown, so sibling-reading display
    // overrides (the computed Vertical Deviation) can read the PFD cache off-render.
    private SimConnect.SimConnectManager? _displaySim;
    public override void OnDisplayPanelShown(string panelKey, SimConnect.SimConnectManager simConnect)
    {
        if (simConnect.IsConnected) _displaySim = simConnect;   // for sibling-reading overrides (V/DEV)
        // The packed-word *_1 halves aren't in the ND display set (only the *_0 keys render),
        // so a panel open / F5 wouldn't otherwise re-read them — request them here so the
        // decoded ident / approach message is built from fresh halves.
        if (panelKey == "ND" && simConnect.IsConnected)
        {
            simConnect.RequestVariable("A32NX_EFIS_L_TO_WPT_IDENT_1");
            simConnect.RequestVariable("A32NX_EFIS_L_APPR_MSG_1");
        }
        if (panelKey != "System Display" || !simConnect.IsConnected) return;
        int page = (int)Math.Round(simConnect.GetCachedVariableValue(SdPageVar) ?? 0);
        RefreshDisplayBoxAsync(page, simConnect);
    }

    private async void RefreshDisplayBoxAsync(int page, SimConnect.SimConnectManager sim)
    {
        try
        {
            string content;
            if (page == 0)   // E/WD — DECODED engine row + memos from SimVars (NO Coherent scrape)
            {
                content = await BuildEwdDecodedTextAsync(sim);
                if (string.IsNullOrEmpty(content))
                    content = "(content not available — power up the displays)";
            }
            else
            {
                // SD system page. PAINT IMMEDIATELY from cache so content never lags the page
                // selection, then force-read + repaint ~0.4 s later (guarded so a newer page
                // wins — mirrors the A380 fix). Rows are rebuilt INSIDE Paint() with the live
                // cache so the redundant-controller selection (COND ACSC / PRESS CPC / FCTL FAC)
                // re-evaluates against fresh ARINC validity on the second paint.
                Func<string, double?> cacheGet = sim.GetCachedVariableValue;
                var rows0 = SdSystemRows(page, cacheGet);
                if (rows0.Count == 0) { _sdBoxContent = "(this SD page is not wired yet)"; sim.RequestVariable(SdPageVar, forceUpdate: true); return; }
                int seq = ++_sdRefreshSeq;
                void Paint()
                {
                    var rows = SdSystemRows(page, cacheGet);
                    var sb = new System.Text.StringBuilder();
                    foreach (var row in rows)
                    {
                        double? cv = sim.GetCachedVariableValue(row.var);
                        // Guard NaN (a var that doesn't exist on this airframe reads back NaN,
                        // e.g. A32NX_ELEC_TR_3_CURRENT) so the box shows "--", not "NaN".
                        sb.AppendLine(cv.HasValue && !double.IsNaN(cv.Value) ? $"{row.label}: {row.fmt(cv.Value)}" : $"{row.label}: --");
                    }
                    _sdBoxContent = sb.ToString().TrimEnd();
                    sim.RequestVariable(SdPageVar, forceUpdate: true);
                }
                Paint();
                // Request every candidate var (incl. BOTH redundant controllers, via the
                // null/auto pass which emits the hidden source-2 rows) so the second paint's
                // selection always has fresh ARINC validity to choose from.
                foreach (var row in SdSystemRows(page)) sim.RequestVariable(row.var, forceUpdate: true);
                await System.Threading.Tasks.Task.Delay(400);
                if (seq != _sdRefreshSeq) return;
                Paint();
                return;
            }
            _sdBoxContent = content;
            sim.RequestVariable(SdPageVar, forceUpdate: true);
        }
        catch { /* best-effort; the combo still recorded the selection */ }
    }

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        // COM frequencies arrive in kHz (134175) — render as MHz ("134.175") in the
        // Radios panel readout fields.
        if (varKey.StartsWith("COM_ACTIVE_FREQUENCY:", StringComparison.Ordinal)
            || varKey.StartsWith("COM_STANDBY_FREQUENCY:", StringComparison.Ordinal))
        {
            displayText = $"{value / 1000.0:F3}";
            return true;
        }
        // Predicted takeoff pitch trim ("Not computed" until the FMS has perf
        // data). SIGN INVERTED vs the A380: the A32NX FMS writes -ths into the
        // word (A32NX_FMCMainDisplay.ts:4133), so NEGATIVE = nose UP — a pilot
        // entering UP2.5 on the PERF page must hear "2.5 degrees up".
        if (varKey == "A32NX_FM1_TO_PITCH_TRIM")
        {
            var w = new SimConnect.Arinc429Word(value);
            if (!(w.IsNormalOperation || w.IsFunctionalTest)) { displayText = "Not computed"; return true; }
            double deg = w.Value;
            displayText = Math.Abs(deg) < 0.05
                ? "Neutral"
                : $"{Math.Abs(deg):0.0} degrees {(deg < 0 ? "up" : "down")}";
            return true;
        }

        // Vertical Deviation (this var is "Vertical Deviation" in the panel) — show the real
        // deviation, not a 0/1 flag: glideslope dots on an ILS approach (GS_DEVIATION deg/0.4,
        // >0 = above), else the FMS linear V/DEV in feet during managed descent (altitude −
        // TARGET_ALTITUDE, >0 = above), else no guidance. Mirrors the A380.
        if (varKey == "A32NX_PFD_LINEAR_DEVIATION_ACTIVE")
        {
            var s = _displaySim;
            bool gsValid = (s?.GetCachedVariableValue("A32NX_RADIO_RECEIVER_GS_IS_VALID") ?? 0) > 0.5;
            if (gsValid)
            {
                double dots = (s?.GetCachedVariableValue("A32NX_RADIO_RECEIVER_GS_DEVIATION") ?? 0) / 0.4;
                displayText = Math.Abs(dots) < 0.05 ? "on the glideslope"
                    : $"{Math.Abs(dots):0.0} dots {(dots > 0 ? "above" : "below")} glideslope";
            }
            else if (value > 0.5)
            {
                double? tgt = s?.GetCachedVariableValue("A32NX_PFD_TARGET_ALTITUDE");
                double? alt = s?.GetCachedVariableValue("INDICATED ALTITUDE");
                if (tgt.HasValue && alt.HasValue && tgt.Value != 0)
                {
                    double dev = alt.Value - tgt.Value;
                    displayText = Math.Abs(dev) < 10 ? "on profile"
                        : $"{Math.Abs(dev):0} feet {(dev >= 0 ? "above" : "below")} profile";
                }
                else displayText = "active";
            }
            else displayText = "no vertical guidance";
            return true;
        }
        // Doors: passenger = INTERACTIVE POINT OPEN 0..1 fraction (Open / Closed /
        // mid-animation %); cargo = inverted *_DOOR_CARGO_LOCKED L:var. Render cleanly
        // instead of a raw "0.6" / "1".
        if (varKey.StartsWith("A32NX_MSFSBA_DOOR_", StringComparison.Ordinal))
        {
            displayText = value > 0.95 ? "Open" : value < 0.05 ? "Closed" : $"{value * 100:0}% open";
            return true;
        }
        if (varKey.StartsWith("A32NX_MSFSBA_CARGO_", StringComparison.Ordinal))
        {
            displayText = value < 0.5 ? "Open" : "Closed";   // LOCKED inverted
            return true;
        }
        // Rudder trim: ARINC429 degrees word, positive = nose-Left (matches the A380).
        if (varKey == "A32NX_FAC_1_RUDDER_TRIM_POS")
        {
            var w = new SimConnect.Arinc429Word(value);
            if (!(w.IsNormalOperation || w.IsFunctionalTest)) { displayText = "Not available"; return true; }
            double deg = w.Value;
            displayText = Math.Abs(deg) < 0.1 ? "Neutral"
                : $"{(deg > 0 ? "Left" : "Right")} {Math.Abs(deg):0.0} degrees";
            return true;
        }
        // Nosewheel steering angle: 0.5 = centred, (v-0.5)*140 = degrees (±70° authority).
        // (Mirrors the A380 decode verbatim — same var name.)
        if (varKey == "A32NX_NOSE_WHEEL_POSITION")
        {
            double deg = (value - 0.5) * 140.0;
            displayText = Math.Abs(deg) < 0.5 ? "Centred"
                        : $"{Math.Abs(deg):0} degrees {(deg < 0 ? "left" : "right")}";
            return true;
        }
        // Tiller handle: ±1 full-scale; show as a left/right percentage. (Mirrors A380.)
        if (varKey == "A32NX_TILLER_HANDLE_POSITION")
        {
            int pct = (int)Math.Round(Math.Abs(value) * 100);
            displayText = pct < 1 ? "Centred" : $"{pct}% {(value < 0 ? "left" : "right")}";
            return true;
        }
        if (varKey == SdPageVar)
        {
            int p = (int)Math.Round(value);
            string nm = SdPageNames.TryGetValue(p, out var n) ? n : $"Page {p}";
            displayText = string.IsNullOrEmpty(_sdBoxContent)
                ? $"{nm} — select this page to load its content"
                : $"{nm}\r\n{_sdBoxContent}";
            return true;
        }

        // ---- PFD / ISIS / ND status-box decode --------------------------------
        // FMA armed-mode bitmasks → readable list (e.g. "NAV, Glideslope" / "None").
        if (varKey == "A32NX_FMA_VERTICAL_ARMED" || varKey == "A32NX_FMA_LATERAL_ARMED")
        {
            int iv = (int)Math.Round(value);
            string modes = DecodeArmedModes(iv, varKey == "A32NX_FMA_VERTICAL_ARMED" ? _vertArmedBits : _latArmedBits);
            displayText = string.IsNullOrEmpty(modes) ? "None" : modes;
            return true;
        }
        // Attitude / heading are stored in RADIANS despite the simvar names.
        if (varKey == "PLANE_PITCH_DEGREES")
        {
            double deg = value * 180.0 / Math.PI;   // SimConnect: positive = nose DOWN
            displayText = Math.Abs(deg) < 0.5 ? "Level"
                : $"{Math.Abs(deg):F1} degrees {(deg < 0 ? "up" : "down")}";
            return true;
        }
        if (varKey == "PLANE_BANK_DEGREES")
        {
            double deg = value * 180.0 / Math.PI;   // SimConnect: positive = bank LEFT
            displayText = Math.Abs(deg) < 0.5 ? "Wings level"
                : $"{Math.Abs(deg):F1} degrees {(deg > 0 ? "left" : "right")}";
            return true;
        }
        if (varKey == "PLANE_HEADING_DEGREES_MAGNETIC")
        {
            double deg = value * 180.0 / Math.PI;
            deg = ((deg % 360) + 360) % 360;
            displayText = $"{(int)Math.Round(deg):000}";
            return true;
        }

        // ---- ND status box ----------------------------------------------------
        if (varKey == "A32NX_EFIS_L_TO_WPT_IDENT_0")
        {
            string wpt = UnpackSixBit(_ndIdent0, _ndIdent1);
            displayText = string.IsNullOrWhiteSpace(wpt) ? "None" : wpt;
            return true;
        }
        if (varKey == "A32NX_EFIS_L_TO_WPT_DISTANCE")
        {
            displayText = value <= 0 ? "--" : $"{value:F1} NM";
            return true;
        }
        if (varKey == "A32NX_EFIS_L_TO_WPT_BEARING")
        {
            double deg = value * 180.0 / Math.PI;
            deg = ((deg % 360) + 360) % 360;
            displayText = $"{(int)Math.Round(deg):000} magnetic";
            return true;
        }
        if (varKey == "A32NX_EFIS_L_TO_WPT_ETA")
        {
            if (value <= 0) { displayText = "--"; return true; }
            int h = (int)(value / 3600), m = (int)((value % 3600) / 60), s = (int)(value % 60);
            displayText = $"{h}:{m:D2}:{s:D2} UTC";
            return true;
        }
        if (varKey == "A32NX_FG_CROSS_TRACK_ERROR")
        {
            // FBW writes this L:var in NAUTICAL MILES (LnavDriver), NOT metres — the old
            // /1852 made it read ~0.00 NM always. Sign: + = right of track.
            double nm = value;
            displayText = Math.Abs(nm) < 0.01 ? "On track"
                : $"{Math.Abs(nm):F2} NM {(nm > 0 ? "right" : "left")}";
            return true;
        }
        if (varKey == "A32NX_RADIO_RECEIVER_LOC_DEVIATION" || varKey == "A32NX_RADIO_RECEIVER_GS_DEVIATION")
        {
            displayText = $"{value:F2} degrees";
            return true;
        }
        if (varKey == "A32NX_EFIS_L_ND_FM_MESSAGE_FLAGS")
        {
            string msgs = DecodeFmMessageFlags((int)Math.Round(value));
            displayText = msgs.Length == 0 ? "None" : msgs;
            return true;
        }
        if (varKey == "A32NX_EFIS_L_APPR_MSG_0")
        {
            string msg = UnpackSixBit(_apprMsg0, _apprMsg1);
            displayText = string.IsNullOrWhiteSpace(msg) ? "No approach active" : msg;
            return true;
        }

        // ---- Clock elapsed times (seconds; -1 = blank/reset) ------------------
        if (varKey == "A32NX_CHRONO_ELAPSED_TIME")   // chronometer -> M minutes S seconds
        {
            if (value < 0) { displayText = "Reset"; return true; }
            int t = (int)value;
            displayText = $"{t / 60} minutes {t % 60} seconds";
            return true;
        }
        if (varKey == "A32NX_CHRONO_ET_ELAPSED_TIME")   // elapsed time -> H hours M minutes
        {
            if (value < 0) { displayText = "Reset"; return true; }
            int t = (int)value;
            displayText = $"{t / 3600} hours {(t % 3600) / 60} minutes";
            return true;
        }

        // ---- PFD / ND A380-parity status-box decodes --------------------------
        // Indicated airspeed in the PFD/ISIS box (non-special alias — see the PFD_IAS registration).
        if (varKey == "PFD_IAS") { displayText = $"{value:0} knots"; return true; }
        // Squawk read-back: TRANSPONDER CODE:1 reads as a BCD16 word (0x2000 = 8192) -> "2000".
        if (varKey == "XPNDR_CODE")
        {
            int bcd = (int)Math.Round(value);
            displayText = $"{(bcd >> 12) & 0xF}{(bcd >> 8) & 0xF}{(bcd >> 4) & 0xF}{bcd & 0xF}";
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
        // Weight/config speeds sourced from A32NX_SPEEDS_* (valid on the ground too); 0 = not computed.
        if (varKey == "PFD_VLS" || varKey == "PFD_VMAX" || varKey == "PFD_GREENDOT" || varKey == "PFD_VF" || varKey == "PFD_VSLOW")
        {
            displayText = value < 1 ? "not available" : $"{value:0} knots";
            return true;
        }
        // Transition LEVEL — ARINC429 word; engineering value is the flight level (60 = FL060).
        if (varKey == "A32NX_FM1_TRANS_LVL")
        {
            var w = new SimConnect.Arinc429Word(value);
            displayText = (w.IsNormalOperation || w.IsFunctionalTest)
                ? (w.Value > 0 ? $"flight level {w.Value:0}" : "not set")
                : "not set";
            return true;
        }
        // FCU selected altitude (stock simvar, feet) / heading (degrees, 000-359).
        if (varKey == "FCU_SEL_ALT") { displayText = $"{value:0} feet"; return true; }
        if (varKey == "FCU_SEL_HDG") { displayText = $"{((int)Math.Round(value) % 360 + 360) % 360:000}"; return true; }
        // Autoland capability (FMGC FG discrete word 4): bit 23 LAND2, 24 LAND3 single, 25 LAND3 dual.
        if (varKey == "PFD_AUTOLAND")
        {
            var w = new SimConnect.Arinc429Word(value);
            if (!w.IsNormalOperation && !w.IsFunctionalTest) displayText = "none";
            else if (w.BitValueOr(25, false)) displayText = "LAND3 dual";
            else if (w.BitValueOr(24, false)) displayText = "LAND3 single";
            else if (w.BitValueOr(23, false)) displayText = "LAND2";
            else displayText = "none";
            return true;
        }
        // ND tuned nav-radio frequencies / DME (stock simvars).
        if (varKey == "ND_VOR1_FREQ" || varKey == "ND_VOR2_FREQ")
        {
            displayText = value > 1 ? $"{value:0.00} megahertz" : "not tuned";
            return true;
        }
        if (varKey == "ND_VOR1_DME" || varKey == "ND_VOR2_DME")
        {
            displayText = value > 0 ? $"{value:0.0} nautical miles" : "no DME";
            return true;
        }
        if (varKey == "ND_ADF1_FREQ" || varKey == "ND_ADF2_FREQ")
        {
            displayText = value > 1 ? $"{value:0} kilohertz" : "not tuned";
            return true;
        }

        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    // Shared TCAS RA state + composer (Services/TcasRaGuidance.cs); the timer and
    // announcer stay per-def so disposal rides StopAllMotion.
    private readonly Services.TcasRaGuidance _tcasRa = new();
    private System.Windows.Forms.Timer? _tcasRaComposeTimer;
    private Accessibility.ScreenReaderAnnouncer? _tcasRaAnnouncer;

    /// <summary>
    /// Aircraft-swap cleanup hook (named for symmetry with the A380 def, which also
    /// halts seat-motor timers here). Stops + disposes the TCAS RA compose timer and
    /// disposes any hotkey windows this def created, so a discarded instance can't
    /// keep UI-thread timers or windows alive against the new aircraft.
    /// </summary>
    public void StopAllMotion()
    {
        try
        {
            _tcasRaComposeTimer?.Stop();
            _tcasRaComposeTimer?.Dispose();
            _tcasRaComposeTimer = null;
            _tcasRaAnnouncer = null;
        }
        catch { }
        try { DisposeTrackedWindows(); } catch { }
    }

    private void MaybeAnnounceTcasRaGuidance(Accessibility.ScreenReaderAnnouncer announcer)
    {
        string? text = _tcasRa.ComposeIfChanged();
        if (text == null) return;
        // Mute rides the TCAS_STATE monitor entry — one Ctrl+M checkbox governs
        // both the state announce and the composed guidance.
        if (!Settings.SettingsManager.Current.A32NXDisabledMonitorVariables.Contains("A32NX_TCAS_STATE"))
            announcer.AnnounceImmediate(text);
    }

    public override bool ProcessSimVarUpdate(string varName, double value, Accessibility.ScreenReaderAnnouncer announcer)
    {
        // ---- TCAS resolution-advisory guidance (cache + composed announce) ----
        // The detail vars cache silently; during an RA (A32NX_TCAS_STATE == 2) each
        // update recomposes the spoken "what to fly" guidance and announces only
        // when the sentence changes. The state var itself returns FALSE so the
        // generic ValueDescriptions announce ("TCAS advisory: resolution advisory")
        // still fires (queued; a detail-driven AnnounceImmediate may land first).
        // Mirrors the A380 implementation.
        if (_tcasRa.TryHandleDetailVar(varName, value))
        {
            MaybeAnnounceTcasRaGuidance(announcer);
            return true;
        }
        if (varName == "A32NX_TCAS_STATE")
        {
            _tcasRa.AdvisoryState = (int)value;
            if (_tcasRa.AdvisoryState != 2)
            {
                _tcasRa.ResetSpoken();
                _tcasRaComposeTimer?.Stop();
            }
            else
            {
                // Do NOT compose synchronously here: FBW resets corrective +
                // the V/S bands only in TCAS STBY (NOT on clear-of-conflict —
                // TcasComputer.ts:1381), so the cache can still hold the
                // PREVIOUS RA's values and a new opposite-sense RA would
                // briefly speak "Climb" for a Descend. Instead defer ~800 ms:
                // detail vars that CHANGED for this RA arrive within the batch
                // frame and announce fresh from their own handlers; if nothing
                // changed, the cached guidance is identical to the previous
                // RA's and therefore still correct — the timer speaks it.
                _tcasRa.ResetSpoken();
                _tcasRaAnnouncer = announcer;
                if (_tcasRaComposeTimer == null)
                {
                    _tcasRaComposeTimer = new System.Windows.Forms.Timer { Interval = 800 };
                    _tcasRaComposeTimer.Tick += (_, _) =>
                    {
                        _tcasRaComposeTimer!.Stop();
                        if (_tcasRaAnnouncer != null) MaybeAnnounceTcasRaGuidance(_tcasRaAnnouncer);
                    };
                }
                _tcasRaComposeTimer.Stop();
                _tcasRaComposeTimer.Start();
            }
            return false; // generic ValueDescriptions announce still speaks the state
        }

        // Doors — read-only auto-announce. Passenger doors (key contains _DOOR_) read the
        // stock INTERACTIVE POINT OPEN SimVar, a 0..1 FRACTION (a half-open door is e.g.
        // 0.6), so open = value > 0.05. Cargo doors (key contains _CARGO_) read the FBW
        // *_DOOR_CARGO_LOCKED L:var, INVERTED (1 = locked = closed), so open = value < 0.5.
        // Announce Open/Closed once per transition (honours the Ctrl+M mute).
        if (varName.StartsWith("A32NX_MSFSBA_DOOR_", StringComparison.Ordinal)
            || varName.StartsWith("A32NX_MSFSBA_CARGO_", StringComparison.Ordinal))
        {
            foreach (var dd in _doorDefs)
            {
                if (dd.Key != varName) continue;
                bool open = dd.CargoLocked ? value < 0.5 : value > 0.05;
                bool? prev = _doorOpen.TryGetValue(varName, out var pv) ? pv : null;
                _doorOpen[varName] = open;
                if (prev.HasValue && prev.Value != open
                    && !Settings.SettingsManager.Current.A32NXDisabledMonitorVariables.Contains(varName))
                    announcer.Announce($"{dd.Name} {(open ? "open" : "closed")}");
                break;
            }
            return true;
        }

        // Aircraft-preset load progress (the flyPad loads the preset; MSFSBA narrates it).
        // The L:var runs 0..1 while loading then resets to 0. Announce each 10% milestone
        // once, "complete" at 100%, and stay silent at idle (0). Honours the Ctrl+M mute.
        if (varName == "A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS")
        {
            int pct = value <= 1.0 ? (int)Math.Round(value * 100) : (int)Math.Round(value);
            pct = Math.Max(0, Math.Min(100, pct));
            if (pct <= 0) { _presetBucket = -1; return true; }   // idle / reset — silent
            if (!Settings.SettingsManager.Current.A32NXDisabledMonitorVariables.Contains(varName))
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

        // ---- COM radios — auto-announce (Fenix/A380 RMP parity) ----
        // Active/standby arrive in kHz (e.g. 121500). Seed silently on first sample;
        // announce genuine changes as "COM 1 active 121.500", so a swap (XFER) reads
        // both the new active and the new standby. Range-gated to the VHF airband so
        // unpowered/garbage values cache silently. Honours the Ctrl+M mute.
        if (varName.StartsWith("COM_ACTIVE_FREQUENCY:", StringComparison.Ordinal)
            || varName.StartsWith("COM_STANDBY_FREQUENCY:", StringComparison.Ordinal))
        {
            bool seeded = _lastComKhz.TryGetValue(varName, out double prevKhz);
            _lastComKhz[varName] = value;
            if (seeded && Math.Abs(value - prevKhz) > 0.5
                && value >= 118000 && value <= 137000
                && !Settings.SettingsManager.Current.A32NXDisabledMonitorVariables.Contains(varName))
            {
                string com = varName.EndsWith(":2") ? "COM 2" : varName.EndsWith(":3") ? "COM 3" : "COM 1";
                string kind = varName.Contains("ACTIVE") ? "active" : "standby";
                announcer.Announce($"{com} {kind} {value / 1000.0:F3}");
            }
            return true;
        }

        // Transmit selector — speak only the radio the mic moved TO (rising edge);
        // the old radio dropping to 0 is implied and would just be noise.
        if (varName.StartsWith("COM_TRANSMIT:", StringComparison.Ordinal))
        {
            bool txOn = value > 0.5;
            bool txKnown = _comTxOn.TryGetValue(varName, out bool txPrev);
            _comTxOn[varName] = txOn;
            if (txKnown && txOn && !txPrev
                && !Settings.SettingsManager.Current.A32NXDisabledMonitorVariables.Contains(varName))
            {
                announcer.Announce($"Transmitting on VHF {varName[^1]}");
            }
            return true;
        }

        // On-demand flaps / gear readout (the L / Shift+G hotkeys request the var; we
        // announce when the fresh value arrives). Parity with the A380.
        if (_reqFlaps && varName == "A32NX_FLAPS_HANDLE_INDEX")
        {
            _reqFlaps = false;
            string[] detents = { "Up", "1", "2", "3", "Full" };
            int i = (int)Math.Round(value);
            announcer.AnnounceImmediate("Flaps " + (i >= 0 && i < detents.Length ? detents[i] : value.ToString()));
            return true;
        }
        if (_reqGear && varName == "GEAR_HANDLE_POSITION")
        {
            _reqGear = false;
            announcer.AnnounceImmediate(value > 0.5 ? "Gear down" : "Gear up");
            return true;
        }

        // Weight-unit (kg/lb) selection — follow the EFB "US Units" toggle. Seed silently
        // on first read; announce on a genuine change. Mirrors the A380.
        if (varName == "A32NX_EFB_USING_METRIC_UNIT")
        {
            bool m = value > 0.5;
            if (!_metricWeightKnown) { _metricWeightKnown = true; _metricWeight = m; return true; }
            if (m != _metricWeight)
            {
                _metricWeight = m;
                announcer.Announce($"Weight units {(m ? "kilograms" : "pounds")}");
            }
            return true;
        }
        // Cache gross weight (kg, stock) + CG (%MAC, FBW L-var) silently for the
        // W / Shift+W readouts; the hotkeys read these caches and speak immediately.
        if (varName == "GW_KG_CACHE") { _gwKgCache = value; return true; }
        if (varName == "A32NX_AIRFRAME_GW_CG_PERCENT_MAC") { _gwCgMac = value; return true; }
        if (_reqFuelKg && varName == "FUEL_QUANTITY_KG")
        {
            _reqFuelKg = false;
            var (fv, fu) = WeightUser(value);
            announcer.AnnounceImmediate($"Fuel on board {fv:0} {fu}");
            return true;
        }

        // Cache the ND packed-word halves so TryGetDisplayOverride can decode the
        // To-Waypoint ident (no announcement; fall through to normal processing).
        switch (varName)
        {
            case "A32NX_EFIS_L_TO_WPT_IDENT_0": _ndIdent0 = value; break;
            case "A32NX_EFIS_L_TO_WPT_IDENT_1": _ndIdent1 = value; break;
            case "A32NX_EFIS_L_APPR_MSG_0": _apprMsg0 = value; break;
            case "A32NX_EFIS_L_APPR_MSG_1": _apprMsg1 = value; break;
        }

        // EFIS baro (altimeter) — speak the setting on knob turn / unit change. The HPA
        // var is an ARINC429 word. Deduped to whole hPa so a steady knob doesn't repeat;
        // STD is spoken from the mode var. Returns true (the EFIS panel field still reads
        // it via the cache fallback in UpdateDisplayText).
        if (varName == "A32NX_FCU_LEFT_EIS_BARO_HPA")
        {
            double hpa = value >= 4294967296.0 ? new SimConnect.Arinc429Word(value).ValueOr(0f) : value;
            if (hpa > 0) _baroHpa = hpa;
            AnnounceBaroIfChanged(true, announcer);
            return true;
        }
        // IN-ACTIVE-UNIT word (0.001 res) — the precision source for inHg mode;
        // cached silently, the announce flows through the shared phrase dedup.
        if (varName == "A32NX_FCU_LEFT_EIS_BARO")
        {
            double v = value >= 4294967296.0 ? new SimConnect.Arinc429Word(value).ValueOr(0f) : value;
            if (v > 0) _baroInUnitL = v;
            AnnounceBaroIfChanged(true, announcer);
            return true;
        }
        if (varName == "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE")
        {
            _baroMode = (int)System.Math.Round(value);
            AnnounceBaroIfChanged(true, announcer);
            return true;
        }
        // F/O EFIS baro — same logic as the captain side, prefixed "First Officer" so the
        // pilot knows which side changed (each knob announces only its own side, so this
        // is NOT chatty). Silent-seed on first detect (same first-start fix as captain).
        if (varName == "A32NX_FCU_RIGHT_EIS_BARO_HPA")
        {
            double hpa = value >= 4294967296.0 ? new SimConnect.Arinc429Word(value).ValueOr(0f) : value;
            if (hpa > 0) _baroHpaR = hpa;
            AnnounceBaroIfChanged(false, announcer);
            return true;
        }
        if (varName == "A32NX_FCU_RIGHT_EIS_BARO")
        {
            double vr = value >= 4294967296.0 ? new SimConnect.Arinc429Word(value).ValueOr(0f) : value;
            if (vr > 0) _baroInUnitR = vr;
            AnnounceBaroIfChanged(false, announcer);
            return true;
        }
        if (varName == "A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE")
        {
            _baroModeR = (int)System.Math.Round(value);
            AnnounceBaroIfChanged(false, announcer);
            return true;
        }

        // FMA armed modes — decode the bitmask and announce NEWLY-armed modes on change
        // (suppresses the old raw "Armed Vertical Mode 1" generic announce).
        if (varName == "A32NX_FMA_VERTICAL_ARMED" || varName == "A32NX_FMA_LATERAL_ARMED")
        {
            bool vert = varName == "A32NX_FMA_VERTICAL_ARMED";
            int iv = (int)Math.Round(value);
            int prev = vert ? _prevVertArmed : _prevLatArmed;
            if (vert) _prevVertArmed = iv; else _prevLatArmed = iv;
            if (prev >= 0 && (iv & ~prev) != 0)
            {
                string nm = DecodeArmedModes(iv & ~prev, vert ? _vertArmedBits : _latArmedBits);
                if (!string.IsNullOrEmpty(nm))
                    foreach (var one in nm.Split(new[] { ", " }, StringSplitOptions.None))
                        announcer.Announce($"{one} armed");
            }
            return true;
        }

        // Flight phase tracking (A32NX-specific)
        if (varName == "A32NX_FMGC_FLIGHT_PHASE")
        {
            _fmgcPhase = (int)Math.Round(value);
            var variables = GetVariables();
            if (variables.ContainsKey(varName) && variables[varName].ValueDescriptions.TryGetValue(value, out string? phaseName))
            {
                // Only announce if phase has actually changed
                if (currentFlightPhase != phaseName)
                {
                    currentFlightPhase = phaseName;
                    announcer.Announce($"Entering {phaseName} phase");
                    // Note: Window title update happens in MainForm by checking CurrentFlightPhase property
                }
            }
            return true; // Processed
        }

        // Autoland capability (FMGC FG discrete word 4, bits 23/24/25). Announce
        // decoded transitions only; suppress the raw ARINC word from the generic path.
        // GATED on the in-flight FMGC phases (Climb..Go-around): on the ground the
        // capability word flickers none↔LAND 3 dual as systems align during taxi,
        // which spammed "Approach capability dual" callouts (user report, KORD
        // taxi-in 2026-06-12). The capability only matters when an approach can
        // actually be flown; the hotkey readout stays available at all times.
        if (varName == "PFD_AUTOLAND")
        {
            var w = new SimConnect.Arinc429Word(value);
            string cap = (!w.IsNormalOperation && !w.IsFunctionalTest) ? "none"
                : w.BitValueOr(25, false) ? "LAND 3 dual"
                : w.BitValueOr(24, false) ? "LAND 3 single"
                : w.BitValueOr(23, false) ? "LAND 2" : "none";
            bool inFlightPhase = _fmgcPhase >= 2 && _fmgcPhase <= 6; // Climb..Go-around
            if (inFlightPhase && _lastAutolandCap != null && _lastAutolandCap != cap && cap != "none")
                announcer.Announce($"Approach capability {cap}");
            _lastAutolandCap = cap;
            return true;
        }

        // FWC word 124: baro-reference discrepancy between the two sides. Rising edges only.
        if (varName == "A32NX_FWC_1_DISCRETE_WORD_124")
        {
            var w = new SimConnect.Arinc429Word(value);
            bool stdD = w.BitValueOr(24, false), refD = w.BitValueOr(25, false);
            if (stdD && !_baroStdDiscrep) announcer.Announce("Baro standard mode discrepancy between sides");
            if (refD && !_baroRefDiscrep) announcer.Announce("Baro reference discrepancy between sides");
            _baroStdDiscrep = stdD; _baroRefDiscrep = refD;
            return true;
        }

        // ECAM Control Panel LED state announcements (A32NX-specific)
        if (varName?.StartsWith("A32NX_ECP_LIGHT_") == true)
        {
            var variables = GetVariables();
            if (variables.ContainsKey(varName))
            {
                var varDef = variables[varName];
                string state = value > 0 ? "On" : "Off";
                announcer.AnnounceImmediate($"{varDef.DisplayName} {state}");
            }
            return true; // Processed
        }

        // Heading
        if (varName == "A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE")
        {
            // Only intercept if we're actively requesting heading
            if (!isRequestingHeading)
                return false; // Not part of a readout request, let normal processing continue

            pendingHeadingValue = value;
            if (pendingHeadingStatus.HasValue)
            {
                string status = pendingHeadingStatus.Value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU heading {pendingHeadingValue.Value:000} degrees, {status}");
                pendingHeadingValue = null;
                pendingHeadingStatus = null;
                isRequestingHeading = false; // Clear flag after announcement
            }
            return true; // Processed as part of FCU readout
        }
        else if (varName == "A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED")
        {
            // Only intercept if we're actively requesting heading
            if (!isRequestingHeading)
                return false; // Not part of a readout request

            pendingHeadingStatus = value;
            if (pendingHeadingValue.HasValue)
            {
                string status = value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU heading {pendingHeadingValue.Value:000} degrees, {status}");
                pendingHeadingValue = null;
                pendingHeadingStatus = null;
                isRequestingHeading = false; // Clear flag after announcement
            }
            return true; // Processed as part of FCU readout
        }
        // Speed
        else if (varName == "A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE")
        {
            // Only intercept if we're actively requesting speed
            if (!isRequestingSpeed)
                return false;

            pendingSpeedValue = value;
            if (pendingSpeedStatus.HasValue)
            {
                string status = pendingSpeedStatus.Value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU speed {pendingSpeedValue.Value:000} knots, {status}");
                pendingSpeedValue = null;
                pendingSpeedStatus = null;
                isRequestingSpeed = false; // Clear flag after announcement
            }
            return true;
        }
        else if (varName == "A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED")
        {
            // Only intercept if we're actively requesting speed
            if (!isRequestingSpeed)
                return false;

            pendingSpeedStatus = value;
            if (pendingSpeedValue.HasValue)
            {
                string status = value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU speed {pendingSpeedValue.Value:000} knots, {status}");
                pendingSpeedValue = null;
                pendingSpeedStatus = null;
                isRequestingSpeed = false; // Clear flag after announcement
            }
            return true;
        }
        // Altitude
        else if (varName == "A32NX_FCU_AFS_DISPLAY_ALT_VALUE")
        {
            // Only intercept if we're actively requesting altitude
            if (!isRequestingAltitude)
                return false;

            pendingAltitudeValue = value;
            if (pendingAltitudeStatus.HasValue)
            {
                string status = pendingAltitudeStatus.Value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU altitude {pendingAltitudeValue.Value:00000} feet, {status}");
                pendingAltitudeValue = null;
                pendingAltitudeStatus = null;
                isRequestingAltitude = false; // Clear flag after announcement
            }
            return true;
        }
        else if (varName == "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED")
        {
            // Only intercept if we're actively requesting altitude
            if (!isRequestingAltitude)
                return false;

            pendingAltitudeStatus = value;
            if (pendingAltitudeValue.HasValue)
            {
                string status = value > 0 ? "managed" : "selected";
                announcer.AnnounceImmediate($"FCU altitude {pendingAltitudeValue.Value:00000} feet, {status}");
                pendingAltitudeValue = null;
                pendingAltitudeStatus = null;
                isRequestingAltitude = false; // Clear flag after announcement
            }
            return true;
        }
        // VS/FPA
        else if (varName == "A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE")
        {
            // Only intercept if we're actively requesting VS/FPA
            if (!isRequestingVSFPA)
                return false;

            pendingVSFPAValue = value;
            if (pendingVSFPAMode.HasValue)
            {
                bool isFpaMode = pendingVSFPAMode.Value > 0;
                string modeText = isFpaMode ? "FPA" : "VS";
                string units = isFpaMode ? "degrees" : "feet per minute";
                string valueText = isFpaMode ? $"{value:+0.0;-0.0;0.0}" : $"{value:+0;-0;0}";
                announcer.AnnounceImmediate($"FCU {modeText} {valueText} {units}");
                pendingVSFPAValue = null;
                pendingVSFPAMode = null;
                isRequestingVSFPA = false; // Clear flag after announcement
            }
            return true;
        }
        else if (varName == "A32NX_TRK_FPA_MODE_ACTIVE")
        {
            // Only intercept if we're actively requesting VS/FPA
            if (!isRequestingVSFPA)
                return false;

            pendingVSFPAMode = value;
            if (pendingVSFPAValue.HasValue)
            {
                bool isFpaMode = value > 0;
                string modeText = isFpaMode ? "FPA" : "VS";
                string units = isFpaMode ? "degrees" : "feet per minute";
                string valueText = isFpaMode ? $"{pendingVSFPAValue.Value:+0.0;-0.0;0.0}" : $"{pendingVSFPAValue.Value:+0;-0;0}";
                announcer.AnnounceImmediate($"FCU {modeText} {valueText} {units}");
                pendingVSFPAValue = null;
                pendingVSFPAMode = null;
                isRequestingVSFPA = false; // Clear flag after announcement
            }
            return true;
        }

        // Call base implementation to handle common variables (e.g., altitude thousand-foot crossings)
        return base.ProcessSimVarUpdate(varName!, value, announcer);
    }

    /// <summary>
    /// Handles A32NX-specific variable setting from UI controls.
    /// Implements special validation, conversion, and multi-step logic for certain variables.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        // COM set fields (Radios panel). Validate, convert MHz → Hz, fire the stock
        // events — live-verified on the A32NX (its RMP drives the stock COM radios;
        // unlike the A380, which ignores these). "Set active" = set standby then swap
        // (there is no direct active-set that keeps the RMP display honest). SILENT on
        // success: the COM_*_FREQUENCY monitor announces the resulting active/standby
        // change, confirming what the sim actually accepted instead of echoing input.
        if (varKey.StartsWith("COM_STANDBY_FREQUENCY_SET", StringComparison.Ordinal)
            || varKey.StartsWith("COM_ACTIVE_FREQUENCY_SET", StringComparison.Ordinal))
        {
            if (value < 118.0 || value > 136.975)
            {
                announcer.AnnounceImmediate("Invalid frequency. Range: 118.000 to 136.975");
                return true;
            }
            uint hz = (uint)Math.Round(value * 1000000);
            string idx = varKey.EndsWith(":2") ? "2" : varKey.EndsWith(":3") ? "3" : "1";
            string setEvent = idx == "1" ? "COM_STBY_RADIO_SET_HZ" : $"COM{idx}_STBY_RADIO_SET_HZ";
            simConnect.SendEvent(setEvent, hz);
            if (varKey.Contains("ACTIVE"))
            {
                System.Threading.Thread.Sleep(100); // let the standby write land before swapping
                simConnect.SendEvent($"COM{idx}_RADIO_SWAP", 0);
            }
            return true;
        }

        // System Display page combo (MSFSBA-internal selector). Record the selection
        // and populate the status box — scrape the E/WD or read decoded SD-system
        // SimVars. The real A32NX SD index is read-only, so no real SD var is touched.
        if (varKey == "A32NX_MSFSBA_SD_PAGE")
        {
            int page = (int)Math.Round(value);
            // UNIQUE-prefix the write ("{seq} 0 *" pushes 0, then it's discarded): re-selecting a
            // page you already visited sends an IDENTICAL calc string, which MobiFlight
            // de-duplicates -> the L:var doesn't re-set, so the combo's read-back can snap to the
            // stale page (you hear/land on the wrong page). The unique prefix forces every write
            // to fire. (Same MobiFlight dedup that made the A380 seat motor "tick once and stop".)
            simConnect.ExecuteCalculatorCode($"{++_sdWriteSeq} 0 * {page} (>L:A32NX_MSFSBA_SD_PAGE)");
            RefreshDisplayBoxAsync(page, simConnect);
            return true;
        }

        // Fire / cargo-smoke TEST: the test PB drives its L:var, but the CRC ("beep beep beep")
        // can keep sounding until the master warning is acknowledged — so on TEST OFF, also pulse
        // the master-warning acknowledge to guarantee the aural cancels. Uses the SAME var as the
        // CLEAR_MASTER_WARNING button: PUSH_AUTOPILOT_MASTERAWARN_L (extra A — SOURCE-VERIFIED
        // 2026-06-04 against A320_NEO_INTERIOR.xml [writes it] + PseudoFWC.ts:2375 [reads it]; the
        // old "no A" PUSH_AUTOPILOT_MASTERWARN_L was DEAD, so this aural-cancel never worked).
        // Calc-path write. The combo announces its own On/Off, so no extra speech here.
        // FUEL MODE SEL — the cockpit click toggles the L:var AND routes the
        // center-tank transfer junctions 4+5. Junction OPTIONS ARE 1-BASED:
        // the cockpit XML LEFT_SINGLE_CODE sends "1 l0 +" (= 1 + toggled), and
        // flight_model.cfg Junction.4/5 define Option 1 = auto transfer-valve
        // routing, Option 2 = manual direct-to-inner. So the junction value is
        // t+1, NOT t — sending t selected the AUTO routing for "Manual" and an
        // invalid option 0 for "Auto" while the L:var and light said otherwise.
        if (varKey == "A32NX_OVHD_FUEL_MODESEL_MANUAL")
        {
            int t = value > 0.5 ? 1 : 0;
            simConnect.ExecuteCalculatorCode(
                $"{t} (>L:A32NX_OVHD_FUEL_MODESEL_MANUAL) {t + 1} 4 (>K:2:FUELSYSTEM_JUNCTION_SET) {t + 1} 5 (>K:2:FUELSYSTEM_JUNCTION_SET)");
            return true;
        }

        // Evacuation HORN SHUT OFF — one-way write by design: 1 silences the horn
        // (sound gate plays while the L:var <= 0) and the EVAC COMMAND re-arm
        // resets it. Never pulse back to 0 — that would resume the horn.
        if (varKey == "A32NX_EVAC_HORN_SHUTOFF")
        {
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_OVHD_EVAC_HORN)");
                announcer.AnnounceImmediate("Evacuation horn silenced");
            }
            return true;
        }

        // Blue electric pump OVERRIDE — momentary press-to-toggle (the Rust
        // controller latches _IS_ON on each press edge). Pulse only when the
        // requested state differs from the cached latch; the press and release
        // are SEPARATE calc calls so they land in different frames (a same-frame
        // 1->0 is not seen by the sampler — live-verified).
        if (varKey == "A32NX_OVHD_HYD_EPUMPY_OVRD_IS_ON")
        {
            double? current = simConnect.GetCachedVariableValue(varKey);
            int target = value > 0.5 ? 1 : 0;
            if (!current.HasValue || (current.Value > 0.5 ? 1 : 0) != target)
            {
                simConnect.ExecuteCalculatorCode("1 (>L:A32NX_OVHD_HYD_EPUMPY_OVRD_IS_PRESSED)");
                simConnect.ExecuteCalculatorCode("0 (>L:A32NX_OVHD_HYD_EPUMPY_OVRD_IS_PRESSED)");
            }
            // Re-read the latch so the cache (and combo) track the result — the
            // var is OnRequest, and a stale cache here inverts the desired-vs-
            // current guard on the NEXT set (a cockpit click or our own toggle
            // would otherwise go unseen until the next panel open). Delayed:
            // the Rust controller latches a frame AFTER the pulse, so an
            // immediate read would cache the pre-toggle value.
            _ = System.Threading.Tasks.Task.Delay(400).ContinueWith(
                _ => { if (simConnect.IsConnected) simConnect.RequestVariable(varKey, forceUpdate: true); });
            return true;
        }

        if (varKey == "A32NX_FIRE_TEST_ENG1" || varKey == "A32NX_FIRE_TEST_ENG2"
            || varKey == "A32NX_FIRE_TEST_APU" || varKey == "A32NX_FIRE_TEST_CARGO")
        {
            int on = value > 0.5 ? 1 : 0;
            simConnect.ExecuteCalculatorCode($"{on} (>L:{varKey})");
            if (on == 0)
            {
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_AUTOPILOT_MASTERAWARN_L)");
                simConnect.ExecuteCalculatorCode("0 (>L:PUSH_AUTOPILOT_MASTERAWARN_L)");
            }
            return true;
        }

        // Acknowledge / silence the MASTER WARNING / MASTER CAUTION (and its aural — the
        // repetitive "beep" / single chime). Momentary push-BUTTONS: pulse the real glareshield
        // PB L:var 1->0 (press + release) via the calculator path, then speak "<name> pressed"
        // (mirrors the A380 _momentaryButtons pattern; returning true suppresses MainForm's raw
        // click re-announce). varDef.Name carries the correct per-button var —
        // PUSH_AUTOPILOT_MASTERAWARN_L/_R (warning, extra A) or _MASTERCAUT_L/_R (caution) —
        // so Captain (_L) and First Officer (_R) each fire the right side. The FWS ORs both.
        if (varKey == "CLEAR_MASTER_WARNING" || varKey == "CLEAR_MASTER_WARNING_FO"
            || varKey == "CLEAR_MASTER_CAUTION" || varKey == "CLEAR_MASTER_CAUTION_FO")
        {
            if (value > 0.5)
            {
                string lvar = varDef.Name;
                simConnect.ExecuteCalculatorCode($"1 (>L:{lvar})");
                simConnect.ExecuteCalculatorCode($"0 (>L:{lvar})");
                announcer.Announce($"{varDef.DisplayName} pressed");
            }
            return true;
        }

        // Clock: CHR start/stop + reset are H-events; the ET knob is a settable L:var.
        if (varKey == "A32NX_MSFSBA_CHRONO_TOGGLE")
        {
            if (value > 0.5) simConnect.ExecuteCalculatorCode("(>H:A32NX_CHRONO_TOGGLE)");
            return true;
        }
        if (varKey == "A32NX_MSFSBA_CHRONO_RESET")
        {
            if (value > 0.5) simConnect.ExecuteCalculatorCode("(>H:A32NX_CHRONO_RST)");
            return true;
        }
        if (varKey == "A32NX_CHRONO_ET_SWITCH_POS")
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:A32NX_CHRONO_ET_SWITCH_POS)");
            return true;
        }

        // Doors are READ-ONLY now (auto-announced status, opened via the flyPad Ground
        // page) — no TOGGLE_AIRCRAFT_EXIT handler. Ground-equipment jetway/stairs combos
        // were removed too; the flyPad Ground page is the interface.
        // Rudder trim reset: fire the stock K-event the cockpit uses (the L:var alone
        // drives nothing). Only the "Activate" option (value > 0.5) fires.
        if (varKey == "A32NX_RUDDER_TRIM_RESET")
        {
            if (value > 0.5) { simConnect.ExecuteCalculatorCode("(>K:RUDDER_TRIM_RESET)"); announcer.Announce("Rudder trim reset"); }
            return true;
        }

        // Thrust-lever detent combos -> THROTTLEn_AXIS_SET_EX1 with the detent's axis
        // value (-1..1 scaled to +-16384). FBW default-style calibration (Reverse -1.0 /
        // Rev Idle -0.80 / Idle -0.50 / Climb 0.0 / Flex-MCT 0.50 / TOGA 1.0); the
        // throttle mapping snaps the lever to the detent. Values are the FBW default-
        // calibration band centers; custom EFB calibrations may differ — see pass-2 checklist. Two engines on the A320.
        // NOTE (2026-06-12): a live-mapping in-RPN variant (band center computed from
        // A32NX_THROTTLE_MAPPING_*_LOW/HIGH:n) was tried and REVERTED at the user's
        // request — it broke the detent announcements in their setup. See commit
        // 34a97a2a / the revert commit for the variant if ever revisited.
        if (varKey == "THROTTLE_ALL_DETENT" || (varKey.StartsWith("THROTTLE_") && varKey.EndsWith("_DETENT")))
        {
            int didx = (int)Math.Round(value);
            // Band CENTERS of the FBW default calibration (ThrottleAxisMapping.h):
            // REV [-1,-0.95] / REV-IDLE [-0.85,-0.75] / IDLE [-0.55,-0.45] /
            // CLB [-0.05,0.05] / FLX [0.45,0.55] / TOGA [0.95,1]. The old -0.70 fell
            // in the gap between REV-IDLE and IDLE and never reached the detent.
            double[] detentAxis = { -1.0, -0.80, -0.50, 0.0, 0.50, 1.0 };
            string[] dnames = { "Reverse", "Reverse Idle", "Idle", "Climb", "Flex M C T", "TOGA" };
            if (didx < 0 || didx >= detentAxis.Length) return true;
            uint ex1 = unchecked((uint)(int)Math.Round(detentAxis[didx] * 16384));
            if (varKey == "THROTTLE_ALL_DETENT")
            {
                for (int n = 1; n <= 2; n++) simConnect.SendEvent($"THROTTLE{n}_AXIS_SET_EX1", ex1);
                announcer.Announce($"All thrust levers {dnames[didx]}");
            }
            else
            {
                int eng = varKey.Length > 9 && char.IsDigit(varKey[9]) ? varKey[9] - '0' : 1;
                simConnect.SendEvent($"THROTTLE{eng}_AXIS_SET_EX1", ex1);
                announcer.Announce($"Thrust lever {eng} {dnames[didx]}");
            }
            return true;
        }

        // Air-conditioning zone temperature: numeric Celsius input (18-30) -> the FBW
        // 0..300 selector knob (knob = (C-18)/12*300). Live-verified the knob is settable.
        if (varKey == "COND_CKPT_TEMP_SET" || varKey == "COND_FWD_TEMP_SET" || varKey == "COND_AFT_TEMP_SET")
        {
            double t = Math.Max(18.0, Math.Min(30.0, value));
            int knob = (int)Math.Round((t - 18.0) / 12.0 * 300.0);
            string zone = varKey.Contains("CKPT") ? "CKPT" : varKey.Contains("FWD") ? "FWD" : "AFT";
            simConnect.ExecuteCalculatorCode($"{knob} (>L:A32NX_OVHD_COND_{zone}_SELECTOR_KNOB)");
            announcer.Announce($"{(zone == "CKPT" ? "Cockpit" : zone == "FWD" ? "Forward cabin" : "Aft cabin")} temperature {t:0.#} degrees");
            return true;
        }

        // ENG MASTER 1/2 combo (state = FUELSYSTEM VALVE SWITCH:n, a SimVar that can't be
        // written directly): the set fires FUELSYSTEM_VALVE_OPEN / _CLOSE with the valve id
        // (param 1/2), mirroring the A380 ENG_VALVE_SWITCH handler. Live-verified the event
        // moves SWITCH:n. The combo's own auto-announce speaks Off/On, so no speech here.
        if (varKey == "ENGINE_1_MASTER" || varKey == "ENGINE_2_MASTER")
        {
            uint eng = varKey == "ENGINE_2_MASTER" ? 2u : 1u;
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_VALVE_OPEN" : "FUELSYSTEM_VALVE_CLOSE", eng);
            return true;
        }

        // Fuel pump combos (state = FUELSYSTEM PUMP/VALVE SWITCH:n, not directly settable):
        // main pumps fire FUELSYSTEM_PUMP_ON/_OFF; centre jet pumps fire FUELSYSTEM_VALVE_
        // OPEN/_CLOSE (same proven path as the engine masters). Pump idx L1=2/L2=5/R1=3/R2=6;
        // jet-pump valves C1=9/C2=10. The combo auto-announces Off/On, so no speech here.
        if (varKey == "FUEL_PUMP_L1" || varKey == "FUEL_PUMP_L2"
            || varKey == "FUEL_PUMP_R1" || varKey == "FUEL_PUMP_R2")
        {
            uint pump = varKey == "FUEL_PUMP_L1" ? 2u
                      : varKey == "FUEL_PUMP_L2" ? 5u
                      : varKey == "FUEL_PUMP_R1" ? 3u : 6u;
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_PUMP_ON" : "FUELSYSTEM_PUMP_OFF", pump);
            return true;
        }
        if (varKey == "FUEL_PUMP_C1" || varKey == "FUEL_PUMP_C2")
        {
            uint vId = varKey == "FUEL_PUMP_C1" ? 9u : 10u;
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_VALVE_OPEN" : "FUELSYSTEM_VALVE_CLOSE", vId);
            return true;
        }
        if (varKey == "FUEL_XFEED")
        {
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_VALVE_OPEN" : "FUELSYSTEM_VALVE_CLOSE", 3);
            return true;
        }

        // Engine 1/2 anti-ice: the cockpit pushbutton drives the stock K-event
        // ANTI_ICE_SET_ENGn (the XMLVAR _PRESSED flag is animation-only). State is read
        // back from the stock simvar ENG ANTI ICE:n. Verified live: a calc-path
        // "{val} (>K:ANTI_ICE_SET_ENG1)" flips ENG ANTI ICE:1 0<->1.
        if (varKey == "ENG_ANTI_ICE:1" || varKey == "ENG_ANTI_ICE:2")
        {
            string eng = varKey.EndsWith(":2") ? "2" : "1";
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>K:ANTI_ICE_SET_ENG{eng})");
            return true;
        }

        // Fire-agent discharge: the cockpit button only fires when the matching fire
        // handle is PULLED, and a discharged squib can never be un-discharged. Mirror
        // both interlocks (A32NX_Interior_Fire.xml FBW_Airbus_FIRE_AGENT).
        if (varKey.StartsWith("A32NX_FIRE_", StringComparison.Ordinal) && varKey.EndsWith("_Discharge", StringComparison.Ordinal))
        {
            if (value < 0.5) { announcer.AnnounceImmediate("Agent bottles cannot be reset."); return true; }
            string handleVar = varKey.Contains("_APU_") ? "A32NX_FIRE_BUTTON_APU"
                : varKey.Contains("_ENG2_") ? "A32NX_FIRE_BUTTON_ENG2" : "A32NX_FIRE_BUTTON_ENG1";
            bool handlePulled = (simConnect.GetCachedVariableValue(handleVar) ?? 0) > 0.5;
            if (!handlePulled)
            {
                announcer.AnnounceImmediate("Pull the fire handle first.");
                return true;
            }
            simConnect.ExecuteCalculatorCode($"1 (>L:{varKey})");
            return true;
        }

        // Generators: toggle the stock event only when desired != current (no SET
        // event exists). The Rust elec system reads the stock simvars, not L:vars.
        if (varKey == "A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON" || varKey == "A32NX_OVHD_ELEC_ENG_GEN_2_PB_IS_ON"
            || varKey == "A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn)
            {
                if (varKey == "A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON")
                    simConnect.SendEvent("APU_GENERATOR_SWITCH_TOGGLE");
                else
                    simConnect.SendEvent(varKey.Contains("_GEN_2_") ? "TOGGLE_ALTERNATOR2" : "TOGGLE_ALTERNATOR1");
            }
            return true;
        }

        // LS button: fire the FCU input event only when desired != current (toggle).
        // The def's Name is the FCU's LIGHT output — never write it.
        if (varKey == "A32NX_EFIS_L_LS_BUTTON_IS_ON" || varKey == "A32NX_EFIS_R_LS_BUTTON_IS_ON")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn)
                simConnect.SendEvent(varKey.Contains("_L_") ? "A32NX.FCU_EFIS_L_LS_PUSH" : "A32NX.FCU_EFIS_R_LS_PUSH");
            return true;
        }

        // Wipers: circuit 77 (Captain) / 80 (F/O). Off = circuit off; Slow/Fast =
        // circuit on + power setting 75/100 (the FBW_Airbus_Wiper_Knob sequence).
        if (varKey == "XMLVAR_A320_WiperSwitch_1" || varKey == "XMLVAR_A320_WiperSwitch_2")
        {
            int circuit = varKey.EndsWith("_2") ? 80 : 77;
            int pos = (int)Math.Round(value);
            bool wantOn = pos > 0;
            bool isOn = (simConnect.GetCachedVariableValue(varKey) ?? 0) > 0.5;
            if (wantOn != isOn)
                simConnect.ExecuteCalculatorCode($"{circuit} (>K:ELECTRICAL_CIRCUIT_TOGGLE)");
            if (wantOn)
                simConnect.ExecuteCalculatorCode($"{(pos >= 2 ? 100 : 75)} {circuit} (>K:2:ELECTRICAL_CIRCUIT_POWER_SETTING_SET)");
            return true;
        }

        // Dome light: mirror the cockpit XML (A320_NEO_INTERIOR.xml:2153-2155).
        // BRT=100: 1 (>K:2:CABIN_LIGHTS_SET) 100 (>K:LIGHT_POTENTIOMETER_7_SET)
        // DIM=20:  1 (>K:2:CABIN_LIGHTS_SET) 20  (>K:LIGHT_POTENTIOMETER_7_SET)
        // OFF=0:   0 (>K:2:CABIN_LIGHTS_SET) 0   (>K:LIGHT_POTENTIOMETER_7_SET)
        if (varKey == "A32NX_OVHD_INTLT_DOME")
        {
            int pct = (int)Math.Round(value);
            int onOff = pct > 0 ? 1 : 0;
            simConnect.ExecuteCalculatorCode($"{onOff} (>K:2:CABIN_LIGHTS_SET)");
            simConnect.ExecuteCalculatorCode($"{pct} (>K:LIGHT_POTENTIOMETER_7_SET)");
            return true;
        }

        // Special handling for autobrake mode. Write the input L:var via the reliable
        // MobiFlight CALCULATOR path (not SetLVar — the data-def SetLVar is unreliable
        // for FBW L:vars). Verified live: "{mode} (>L:A32NX_AUTOBRAKES_ARMED_MODE_SET)"
        // armed A32NX_AUTOBRAKES_ARMED_MODE to the requested mode (the SET var then
        // auto-resets to -1 once consumed). The event is kept as a harmless backup.
        if (varKey == "AUTOBRAKE_MODE")
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:A32NX_AUTOBRAKES_ARMED_MODE_SET)");
            simConnect.SendEvent("A32NX.AUTOBRAKE_SET", (uint)value);
            announcer.Announce($"{varDef.DisplayName} set to {varDef.ValueDescriptions[value]}");
            return true; // Handled
        }

        // VS/FPA set — delegate to SetFCUVSValue: the calc-code K: path (negatives
        // can't go through SendEvent's uint cast) with the correct FPA ×10 scaling.
        if (varKey == "A32NX.FCU_VS_SET")
        {
            bool isValidVS = value >= -6000 && value <= 6000 && Math.Abs(value) >= 100;
            bool isValidFPA = value >= -9.9 && value <= 9.9; // includes 0
            if (!isValidVS && !isValidFPA)
            {
                announcer.AnnounceImmediate("Invalid value. VS: plus or minus 100 to 6000, FPA: plus or minus 9.9 or less");
                return true;
            }
            SetFCUVSValue(value, simConnect, announcer);
            return true;
        }

        // Special handling for Left baro setting - requires unit conversion
        if (varKey == "A32NX.FCU_EFIS_L_BARO_SET")
        {
            // Check if we're in inHg or hPa mode (would need currentSimVarValues from MainForm)
            // For now, assume hPa mode and apply FlyByWire's * 16 conversion
            // A better implementation would check A32NX_FCU_EFIS_L_BARO_IS_INHG

            // Assume hPa mode (common default)
            uint convertedValue = (uint)(value * 16);  // FlyByWire expects hPa * 16

            simConnect.SendEvent(varKey, convertedValue);
            announcer.Announce($"Left baro set to {value:F2} hPa");
            return true; // Handled
        }

        // Special handling for Right baro setting - requires unit conversion
        if (varKey == "A32NX.FCU_EFIS_R_BARO_SET")
        {
            // Same as left baro
            uint convertedValue = (uint)(value * 16);  // FlyByWire expects hPa * 16

            simConnect.SendEvent(varKey, convertedValue);
            announcer.Announce($"Right baro set to {value:F2} hPa");
            return true; // Handled
        }

        // Rudder-trim NUDGE buttons (parity with the A380): momentary stock K-events.
        // Only the active press (value > 0.5) fires; the button reports no state.
        if (varKey == "RUDDER_TRIM_LEFT" || varKey == "RUDDER_TRIM_RIGHT")
        {
            if (value > 0.5) { simConnect.SendEvent(varKey); announcer.Announce($"{varDef.DisplayName} pressed"); }
            return true;
        }

        // Momentary L-var actions — "Activate" pulses the L:var 1 then 0 via the calculator
        // path so the control springs back (mirrors the A380 _momentaryButtons pulse). Covers
        // BOTH shapes: a RenderAsButton momentary button, AND an Idle/Activate COMBO (the
        // screen-reader-preferred form for TEST/RESET/DEPLOY actions — selecting "Activate"
        // fires, "Idle" does nothing). Only value > 0.5 fires. The auto-announce loop skips
        // "Activate" combos; this handler speaks "<name> pressed". MUST stay before the catch-all.
        if (varDef.Type == SimConnect.SimVarType.LVar && varKey.StartsWith("A32NX_", StringComparison.Ordinal)
            && ((varDef.RenderAsButton && (varDef.ValueDescriptions == null || varDef.ValueDescriptions.Count == 0))
                || (varDef.ValueDescriptions != null && varDef.ValueDescriptions.ContainsValue("Activate"))))
        {
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode($"1 (>L:{varKey})");
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try { await System.Threading.Tasks.Task.Delay(250); simConnect.ExecuteCalculatorCode($"0 (>L:{varKey})"); } catch { }
                });
                announcer.Announce($"{varDef.DisplayName} pressed");
            }
            return true;
        }

        // Speed-brake FINE slider — the TrackBar already maps 0-16383; fire the stock
        // SPOILERS_SET (mirrors the A380 handler). Synthetic var, no backing L:var.
        if (varKey == "A32NX_MSFSBA_SPEEDBRAKE_SLIDER")
        {
            int axis = Math.Max(0, Math.Min(16383, (int)Math.Round(value)));
            simConnect.ExecuteCalculatorCode($"{axis} (>K:SPOILERS_SET)");
            return true;
        }
        // Speed-brake COARSE combo (Retracted/Half/Full) -> stock SPOILERS_SET (0 / 8192 /
        // 16383), mirroring the A380 handler. Synthetic var, no backing L:var.
        if (varKey == "A32NX_MSFSBA_SPEEDBRAKE")
        {
            int pos = Math.Max(0, Math.Min(2, (int)Math.Round(value)));
            int[] axis = { 0, 8192, 16383 };
            simConnect.ExecuteCalculatorCode($"{axis[pos]} (>K:SPOILERS_SET)");
            return true;
        }

        // "All Landing Lights" buttons (RenderAsButton, Exterior Lighting panel). Drive the
        // FBW switch L:vars directly — the SAME calls as the per-light Left/Right Landing Light
        // combos (MainForm lighting block) — so the LDG LT memo stays in sync. The old wiring
        // fired the stock LANDING_LIGHTS_ON/OFF events, which bypass the FBW switch and desync
        // the memo (FBW issues #1507/#1528). On = 0 (extend + illuminate); Off = 2 (RETRACT —
        // stows the lights and clears LDG LT). Nose light (LIGHTING_LANDING_1) stays independent.
        if (varKey == "LANDING_LIGHTS_ON_THIRD_PARTY" || varKey == "LANDING_LIGHTS_OFF_THIRD_PARTY")
        {
            if (value > 0.5)
            {
                bool on = varKey == "LANDING_LIGHTS_ON_THIRD_PARTY";
                int pos = on ? 0 : 2;     // LIGHTING_LANDING_x: 0 = On, 2 = Retract
                int retr = on ? 0 : 1;    // LANDING_x_RETRACTED: 1 = retracted
                simConnect.SetLVar("LIGHTING_LANDING_2", pos);
                simConnect.SetLVar("LANDING_2_RETRACTED", retr);
                simConnect.SetLVar("LIGHTING_LANDING_3", pos);
                simConnect.SetLVar("LANDING_3_RETRACTED", retr);
                announcer.Announce(on ? "All landing lights on" : "All landing lights retracted");
            }
            return true;
        }

        // Combined NAV & LOGO lights. The stock NAV_LIGHTS_SET / LOGO_LIGHTS_SET single-param
        // events are DEAD on the FBW A32NX (live-verified). The real cockpit switch
        // (FBW_A32NX_NAV_LOGO_LT_SW, source A32NX_Lights.xml) writes the A:LIGHT NAV / A:LIGHT
        // LOGO simvars AND fires the INDEXED 2-param K:2:NAV_LIGHTS_SET / K:2:LOGO_LIGHTS_SET
        // events. We replay that exact RPN via the calculator path (the reliable FBW write),
        // and set the switch state L-var (0=Off, 2=On/SYS2) so the cockpit knob + the combo
        // read-back agree. Verbatim from the switch template's OFF and SYS2 branches.
        if (varKey == "A32NX_LIGHTS_NAV_LOGO")
        {
            bool on = value >= 0.5;
            if (on)
            {
                simConnect.ExecuteCalculatorCode(
                    "1 (>A:LIGHT NAV) 1 (>A:LIGHT LOGO) 0 1 (>K:2:LOGO_LIGHTS_SET) " +
                    "1 0 (>K:2:NAV_LIGHTS_SET) 2 0 (>K:2:NAV_LIGHTS_SET) 3 0 (>K:2:NAV_LIGHTS_SET) " +
                    "4 1 (>K:2:NAV_LIGHTS_SET) 5 1 (>K:2:NAV_LIGHTS_SET) 6 1 (>K:2:NAV_LIGHTS_SET) " +
                    "2 (>L:A32NX_LIGHTS_NAV_LOGO)");
            }
            else
            {
                simConnect.ExecuteCalculatorCode(
                    "0 (>A:LIGHT NAV) 0 (>A:LIGHT LOGO) 0 0 (>K:2:NAV_LIGHTS_SET) " +
                    "0 0 (>K:2:LOGO_LIGHTS_SET) 0 (>L:A32NX_LIGHTS_NAV_LOGO)");
            }
            return true;
        }

        // Brightness knobs: percent 0-100 via the indexed potentiometer set event.
        // Potentiometer IDs from A320_NEO_INTERIOR.xml: pedestal flood = 76,
        // main panel flood = 85, glareshield flood Capt = 10 / FO = 11,
        // glareshield integral = 83, overhead integral = 86.
        if (varKey.StartsWith("BRIGHT_", StringComparison.Ordinal) && varKey.EndsWith("_SET", StringComparison.Ordinal))
        {
            int pot = varKey switch
            {
                "BRIGHT_PEDESTAL_SET" => 76,
                "BRIGHT_MAINPANEL_SET" => 85,
                "BRIGHT_GLARESHIELD_CAPT_SET" => 10,
                "BRIGHT_GLARESHIELD_FO_SET" => 11,
                "BRIGHT_GLARESHIELD_INTEG_SET" => 83,
                "BRIGHT_OVERHEAD_INTEG_SET" => 86,
                _ => -1
            };
            if (pot < 0) return false;
            int pct = Math.Clamp((int)Math.Round(value), 0, 100);
            simConnect.ExecuteCalculatorCode($"{pct} {pot} (>K:2:LIGHT_POTENTIOMETER_SET)");
            announcer.Announce($"{varDef.DisplayName.Split('(')[0].Trim()} {pct} percent");
            return true;
        }

        // LDG ELEV: numeric feet, or -4000 for the AUTO detent. Range-check per the knob.
        if (varKey == "PRESS_LDG_ELEV_SET")
        {
            if (value != -4000 && (value < -2000 || value > 15000))
            {
                announcer.AnnounceImmediate("Landing elevation must be -2000 to 15000 feet, or -4000 for Auto.");
                return true;
            }
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:A32NX_OVHD_PRESS_LDG_ELEV_KNOB)");
            announcer.Announce(value == -4000 ? "Landing elevation Auto" : $"Landing elevation {value:0} feet");
            return true;
        }

        // Reliable write catch-all for FBW discrete L:var combos (mirrors the A380 #103
        // fix): MainForm's generic data-def SetLVar is unreliable for FBW L:vars, so route
        // any settable A32NX_/XMLVAR_ discrete combo through the MobiFlight calculator path.
        // Only plain (non-indexed) L:vars with value descriptions reach here — all the
        // special cases (events, _SET fields, scaled values) returned above.
        if (varDef.Type == SimConnect.SimVarType.LVar
            && varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count > 0
            && !varDef.Name.Contains(":")
            && (varDef.Name.StartsWith("A32NX_", StringComparison.Ordinal)
                || varDef.Name.StartsWith("XMLVAR_", StringComparison.Ordinal)))
        {
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:{varDef.Name})");
            return true;
        }

        return false; // Not handled - use generic logic
    }

    private void RequestFCUHeadingWithStatus(SimConnect.SimConnectManager simConnectMgr)
    {
        if (simConnectMgr.IsConnected)
        {
            // Set flag to indicate we're actively requesting heading
            isRequestingHeading = true;

            // Reset pending values
            pendingHeadingValue = null;
            pendingHeadingStatus = null;

            // Request both variables using existing registrations
            // ProcessSimVarUpdate will combine them when both arrive
            simConnectMgr.RequestVariable("A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE", forceUpdate: true);
            simConnectMgr.RequestVariable("A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED", forceUpdate: true);
        }
    }

    private void RequestFCUSpeedWithStatus(SimConnect.SimConnectManager simConnectMgr)
    {
        if (simConnectMgr.IsConnected)
        {
            // Set flag to indicate we're actively requesting speed
            isRequestingSpeed = true;

            // Reset pending values
            pendingSpeedValue = null;
            pendingSpeedStatus = null;

            // Request both variables using existing registrations
            // ProcessSimVarUpdate will combine them when both arrive
            simConnectMgr.RequestVariable("A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE", forceUpdate: true);
            simConnectMgr.RequestVariable("A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED", forceUpdate: true);
        }
    }

    private void RequestFCUAltitudeWithStatus(SimConnect.SimConnectManager simConnectMgr)
    {
        if (simConnectMgr.IsConnected)
        {
            // Set flag to indicate we're actively requesting altitude
            isRequestingAltitude = true;

            // Reset pending values
            pendingAltitudeValue = null;
            pendingAltitudeStatus = null;

            // Request both variables using existing registrations
            // ProcessSimVarUpdate will combine them when both arrive
            simConnectMgr.RequestVariable("A32NX_FCU_AFS_DISPLAY_ALT_VALUE", forceUpdate: true);
            simConnectMgr.RequestVariable("A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED", forceUpdate: true);
        }
    }

    private void RequestFCUVerticalSpeedFPA(SimConnect.SimConnectManager simConnectMgr)
    {
        if (simConnectMgr.IsConnected)
        {
            // Set flag to indicate we're actively requesting VS/FPA
            isRequestingVSFPA = true;

            // Reset pending values
            pendingVSFPAValue = null;
            pendingVSFPAMode = null;

            // Request both variables using existing registrations
            // ProcessSimVarUpdate will combine them when both arrive
            simConnectMgr.RequestVariable("A32NX_FCU_AFS_DISPLAY_VS_FPA_VALUE", forceUpdate: true);
            simConnectMgr.RequestVariable("A32NX_TRK_FPA_MODE_ACTIVE", forceUpdate: true);
        }
    }

    // ==================================================================================
    // Public FCU API for the dedicated A320 FCU windows (Forms/FBWA320/*)
    // Mirrors the A380 def's window API so the A32NX gets the same Fenix-style FCU
    // value-entry windows. The windows validate input, then call these; the set/
    // readback mechanism lives here. Each setter fires the (shared A32NX) event and
    // re-requests the existing AFS_DISPLAY read-out so the new value is spoken.
    // ==================================================================================

    // FCU set/push/pull events update the FBW AFS_DISPLAY_* vars ASYNCHRONOUSLY (a frame or
    // two later). Reading them back in the same breath force-reads the STALE pre-event value,
    // so the read-out speaks the OLD value/status (e.g. set 300 → speaks the previous 284,
    // or a push reads the pre-push managed/selected state). Defer the read-out ~300 ms so the
    // FBW FCU has processed the event first. Non-blocking (RequestVariable just queues the read;
    // the response is announced from ProcessSimVarUpdate when it arrives).
    private static void DeferReadback(Action readback)
    {
        _ = System.Threading.Tasks.Task.Run(async () =>
        { try { await System.Threading.Tasks.Task.Delay(300); readback(); } catch { } });
    }

    // Public readout wrappers (the windows call these on open + after a push/pull).
    public void RequestFCUHeadingReadout(SimConnect.SimConnectManager s) => RequestFCUHeadingWithStatus(s);
    public void RequestFCUSpeedReadout(SimConnect.SimConnectManager s) => RequestFCUSpeedWithStatus(s);
    public void RequestFCUAltitudeReadout(SimConnect.SimConnectManager s) => RequestFCUAltitudeWithStatus(s);
    public void RequestFCUVSReadout(SimConnect.SimConnectManager s) => RequestFCUVerticalSpeedFPA(s);

    // hdg: 0-360 whole degrees.
    public bool SetFCUHeadingValue(int hdg, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        s.SendEvent("A32NX.FCU_HDG_SET", (uint)hdg);
        // Clean readback (NOT the deferred RequestFCUHeadingWithStatus, which re-read the cache):
        // the value we set + the cached managed dot, once, bare number to match the A380.
        string hdgStatus = (s.GetCachedVariableValue("A32NX_FCU_AFS_DISPLAY_HDG_TRK_MANAGED") ?? 0) > 0.5 ? "managed" : "selected";
        a.AnnounceImmediate($"FCU heading {hdg:000}, {hdgStatus}");
        return true;
    }

    // internalSpeed: knots (100-399) OR Mach*100 (10-99). Caller does the *100.
    public bool SetFCUSpeedValue(int internalSpeed, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        s.SendEvent("A32NX.FCU_SPD_SET", (uint)internalSpeed);
        // Clean readback (NOT the deferred RequestFCUSpeedWithStatus): value set + cached managed
        // dot, once. internalSpeed < 100 is Mach*100 (e.g. 78 = 0.78).
        string spdStatus = (s.GetCachedVariableValue("A32NX_FCU_AFS_DISPLAY_SPD_MACH_MANAGED") ?? 0) > 0.5 ? "managed" : "selected";
        if (internalSpeed < 100)
            a.AnnounceImmediate($"FCU speed mach {internalSpeed / 100.0:0.00}, {spdStatus}");
        else
            a.AnnounceImmediate($"FCU speed {internalSpeed}, {spdStatus}");
        return true;
    }

    // feet: whole feet; rounded to the nearest 100 (FCU_ALT_SET requires multiples of 100).
    public bool SetFCUAltitudeValue(double feet, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        uint rounded = (uint)(Math.Round(feet / 100) * 100);
        // Only force the 100-ft increment when the target isn't already a 1000-multiple (the
        // A320 doesn't announce its increment var, so no announce-suppression is needed here).
        if (rounded % 1000 != 0)
        {
            s.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", 100);
            System.Threading.Thread.Sleep(50);
        }
        s.SendEvent("A32NX.FCU_ALT_SET", rounded);
        // Clean Fenix-style readback: value set + cached managed dot, bare number.
        string altStatus = (s.GetCachedVariableValue("A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED") ?? 0) > 0.5 ? "managed" : "selected";
        a.AnnounceImmediate($"FCU altitude {rounded}, {altStatus}");
        return true;
    }

    // value: signed V/S (-6000..6000 fpm) OR FPA (-9.9..9.9 deg). Uses the calc-code
    // K: path (negatives overflow SendEvent's uint).
    public bool SetFCUVSValue(double value, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        // FPA is sent ×10 per the FBW protocol (FcuComputer consumes vs_fpa/10 in
        // TRK/FPA mode; a320-events.md: "FPA * 10, i.e. 15 for 1.5 degrees").
        // Edge case: FPA exactly -0.1° encodes to -1, the FCU's "no input" sentinel,
        // and is silently ignored by the aircraft — unfixable protocol quirk.
        int toSend = Math.Abs(value) < 100 ? (int)Math.Round(value * 10) : (int)Math.Round(value);
        s.ExecuteCalculatorCode($"{toSend} (>K:A32NX.FCU_VS_SET)");
        // Consistent Fenix-style readback (V/S has no managed/selected dot, so just the value).
        if (Math.Abs(value) < 100)
            a.AnnounceImmediate($"FCU flight path angle {value:0.0}");
        else
            a.AnnounceImmediate($"FCU vertical speed {value:0}");
        return true;
    }

    // Fire a push/pull/toggle event. When readback is true (the default — used by the
    // dedicated FCU value-entry windows where a value confirmation is wanted), also
    // speak the resulting value (routed by the same event→readout mapping the knob
    // hotkeys use). The window Push/Pull/mode-toggle buttons pass readback:false so the
    // knob actuates SILENTLY (Fenix-style) and only the always-on managed-state monitor
    // (A32NX_FCU_AFS_DISPLAY_*_MANAGED, Continuous+IsAnnounced) speaks, and only on a
    // real Managed↔Selected transition. The old unconditional readback spoke the full
    // value on every press — the verbose, "wonky" behaviour the user flagged.
    public void FireFCUButton(string evt, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a, bool readback = true)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return; }
        s.SendEvent(evt);
        if (!readback) return;
        // Defer the read-out so the FBW FCU has processed the push/pull before we read the
        // managed/selected status + value (otherwise it speaks the pre-push state — see DeferReadback).
        Action? readbackAction = evt switch
        {
            "A32NX.FCU_HDG_PUSH" or "A32NX.FCU_HDG_PULL" or "A32NX.FCU_TRK_FPA_TOGGLE_PUSH" => () => RequestFCUHeadingWithStatus(s),
            "A32NX.FCU_SPD_PUSH" or "A32NX.FCU_SPD_PULL" or "A32NX.FCU_SPD_MACH_TOGGLE_PUSH" => () => RequestFCUSpeedWithStatus(s),
            "A32NX.FCU_ALT_PUSH" or "A32NX.FCU_ALT_PULL" => () => RequestFCUAltitudeWithStatus(s),
            "A32NX.FCU_VS_PUSH" or "A32NX.FCU_VS_PULL" => () => RequestFCUVerticalSpeedFPA(s),
            _ => null
        };
        if (readbackAction != null) DeferReadback(readbackAction);
    }

    // Request the live AP/mode state vars so the Autopilot window can refresh labels.
    public void RequestAutopilotStates(SimConnect.SimConnectManager s)
    {
        if (!s.IsConnected) return;
        // Use the REGISTERED FCU button-light L:vars. The old _MODE_ACTIVE / _FD_ACTIVE
        // names don't exist in FBW and weren't registered, so the requests no-op'd and
        // the LOC/APPR/FD labels never refreshed (the reported bug).
        foreach (var v in new[] {
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE",
            "A32NX_FCU_LOC_LIGHT_ON", "A32NX_FCU_APPR_LIGHT_ON",
            "A32NX_FMA_EXPEDITE_MODE", "A32NX_FCU_EFIS_L_FD_LIGHT_ON",
            "A32NX_FCU_EFIS_R_FD_LIGHT_ON" })
            s.RequestVariable(v, forceUpdate: true);
    }

    // Set the FCU altitude increment (100 or 1000 ft).
    public void SetAltIncrement(int inc, SimConnect.SimConnectManager s)
    {
        if (!s.IsConnected) return;
        s.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", (uint)inc);
    }

    private void RequestFuelQuantity(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "FUEL TOTAL QUANTITY WEIGHT", "pounds",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_FUEL_QUANTITY,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting fuel quantity: {ex.Message}");
            }
        }
    }

    private void RequestSpeedGD(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                // 330 (NOT 340) — 340-345 are the Fuel/Payload dispatch IDs; the speed-tape
                // responses are dispatched at 330/331/332/335/336/337 in SimConnectManager.
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)330;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_GD", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)330,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting GD speed: {ex.Message}");
            }
        }
    }

    private void RequestSpeedS(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)331;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_S", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)331,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting S speed: {ex.Message}");
            }
        }
    }

    private void RequestSpeedF(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)332;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_F", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)332,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting F speed: {ex.Message}");
            }
        }
    }

    private void RequestSpeedVFE(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)335;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    // VFEN (next-flap VFE) is a PLAIN L-var, valid on the ground; the FAC
                    // word A32NX_FAC_1_V_FE_NEXT is an ARINC429 word that this raw temp-def
                    // path can't decode (it'd read the ~14-billion raw word). Matches the A380.
                    "L:A32NX_SPEEDS_VFEN", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)335,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting VFE speed: {ex.Message}");
            }
        }
    }

    private void RequestSpeedVLS(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)336;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_VLS", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)336,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting VLS speed: {ex.Message}");
            }
        }
    }

    private void RequestSpeedVS(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)337;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_VS", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)337,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting VS speed: {ex.Message}");
            }
        }
    }

    // ========================================
    // FCU Request Methods (Aircraft-Specific)
    // ========================================
    // These methods request and announce A320-specific FCU values
    // Called by hotkey handlers in MainForm

    /// <summary>
    /// Requests the current FCU heading value and announces it via screen reader.
    /// Uses A320-specific variable: L:A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE
    /// </summary>
    public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (simConnect.IsConnected)
        {
            try
            {
                // Request A320 FCU heading display value
                simConnect.RequestSingleValue(300, "L:A32NX_FCU_AFS_DISPLAY_HDG_TRK_VALUE", "number", "FCU_HEADING");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[A320] Error requesting FCU heading: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Requests the current FCU speed value and announces it via screen reader.
    /// Uses A320-specific variable: L:A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE
    /// </summary>
    public override void RequestFCUSpeed(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (simConnect.IsConnected)
        {
            try
            {
                // Request A320 FCU speed display value
                simConnect.RequestSingleValue(301, "L:A32NX_FCU_AFS_DISPLAY_SPD_MACH_VALUE", "number", "FCU_SPEED");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[A320] Error requesting FCU speed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Requests the current FCU altitude value and announces it via screen reader.
    /// Uses A320-specific variable: L:A32NX_FCU_AFS_DISPLAY_ALT_VALUE
    /// </summary>
    public override void RequestFCUAltitude(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (simConnect.IsConnected)
        {
            try
            {
                // Request A320 FCU altitude display value
                simConnect.RequestSingleValue(302, "L:A32NX_FCU_AFS_DISPLAY_ALT_VALUE", "number", "FCU_ALTITUDE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[A320] Error requesting FCU altitude: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Requests the current FCU vertical speed value and announces it via screen reader.
    /// Uses A320-specific variable: VERTICAL SPEED (standard SimVar)
    /// </summary>
    public override void RequestFCUVerticalSpeed(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (simConnect.IsConnected)
        {
            try
            {
                // Request vertical speed (standard SimVar, not A320-specific)
                simConnect.RequestSingleValue(308, "VERTICAL SPEED", "feet per second", "VERTICAL_SPEED");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[A320] Error requesting FCU vertical speed: {ex.Message}");
            }
        }
    }
}
