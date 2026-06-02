using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms.A32NX;

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
    private Accessibility.ScreenReaderAnnouncer? lastAnnouncer = null;

    // Boolean flags to track active FCU readout requests
    private bool isRequestingHeading = false;
    private bool isRequestingSpeed = false;
    private bool isRequestingAltitude = false;
    private bool isRequestingVSFPA = false;

    // Flight phase tracking
    private string currentFlightPhase = "";
    public string CurrentFlightPhase => currentFlightPhase;

    public override string AircraftName => "FlyByWire Airbus A320neo";
    public override string AircraftCode => "A320";

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

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
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
        // ---- ELEC parity with the A380: generators, bus tie, AC ESS feed, IDG disc,
        // commercial. All verified to exist live. (GEN PBs may be FBW computed mirrors;
        // kept settable + readable either way — the readout/announce always works.)
        ["A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON", DisplayName = "Generator 1",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_ENG_GEN_2_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_ENG_GEN_2_PB_IS_ON", DisplayName = "Generator 2",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON", DisplayName = "APU Generator",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
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
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Auto" }
        },
        ["XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION",
            DisplayName = "Emergency Exit Lights",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Auto" }
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
            RenderAsButton = true  // Render as button instead of combo box
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
        ["LIGHT STROBE"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT STROBE",
            DisplayName = "Strobe Light State",
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
        ["NAV_LIGHTS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV_LIGHTS_SET",
            DisplayName = "Nav Lights Set Event",
            Type = SimConnect.SimVarType.Event
        },
        ["NAV_LIGHTS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV_LIGHTS_ON",
            DisplayName = "Nav Lights On Event",
            Type = SimConnect.SimVarType.Event
        },
        ["NAV_LIGHTS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "NAV_LIGHTS_OFF",
            DisplayName = "Nav Lights Off Event",
            Type = SimConnect.SimVarType.Event
        },
        ["LOGO_LIGHTS_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LOGO_LIGHTS_SET",
            DisplayName = "Logo Lights Set Event",
            Type = SimConnect.SimVarType.Event
        },
        ["CIRCUIT_SWITCH_ON:21"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT SWITCH ON:21",
            DisplayName = "Left RWY Turn Off Light",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["CIRCUIT_SWITCH_ON:22"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT SWITCH ON:22",
            DisplayName = "Right RWY Turn Off Light",
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
        ["LIGHT NAV"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT NAV",
            DisplayName = "Nav Lights",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["LIGHT LOGO"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT LOGO",
            DisplayName = "Logo Lights",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "bool",
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
        ["A32NX_FIRE_BUTTON_ENG1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_BUTTON_ENG1",
            DisplayName = "Eng 1 Fire Handle",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Pulled" }
        },
        ["A32NX_FIRE_BUTTON_ENG2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_BUTTON_ENG2",
            DisplayName = "Eng 2 Fire Handle",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Pulled" }
        },
        ["A32NX_FIRE_BUTTON_APU"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FIRE_BUTTON_APU",
            DisplayName = "APU Fire Handle",
            Type = SimConnect.SimVarType.LVar,
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
        ["A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_ENG_2_PUMP_PB_IS_AUTO",
            DisplayName = "Blue Eng Pump",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
        },
        ["A32NX_OVHD_HYD_ENG_2_PUMP_PB_HAS_FAULT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_ENG_2_PUMP_PB_HAS_FAULT",
            DisplayName = "Blue Eng Pump Fault",
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
        ["A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_HYD_EPUMPY_PB_IS_AUTO",
            DisplayName = "Yellow Elec Pump",
            Type = SimConnect.SimVarType.LVar,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
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
        // ENG MAN START pushbuttons (parity with A380 Engine Start panel) — settable
        // L:vars, live-verified to hold a write via the calculator path.
        ["A32NX_ENGMANSTART1_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGMANSTART1_TOGGLE", DisplayName = "Engine 1 Manual Start",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ENGMANSTART2_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ENGMANSTART2_TOGGLE", DisplayName = "Engine 2 Manual Start",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // Fuel Panel (these are events with parameters)
        ["FUELSYSTEM_PUMP_TOGGLE:2"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_PUMP_TOGGLE",
            DisplayName = "Fuel Pump L1",
            Type = SimConnect.SimVarType.Event,
            EventParam = 2  // L1 = 2
        },
        ["FUELSYSTEM_PUMP_TOGGLE:5"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_PUMP_TOGGLE",
            DisplayName = "Fuel Pump L2",
            Type = SimConnect.SimVarType.Event,
            EventParam = 5  // L2 = 5
        },
        ["FUELSYSTEM_PUMP_TOGGLE:3"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_PUMP_TOGGLE",
            DisplayName = "Fuel Pump R1",
            Type = SimConnect.SimVarType.Event,
            EventParam = 3  // R1 = 3
        },
        ["FUELSYSTEM_PUMP_TOGGLE:6"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_PUMP_TOGGLE",
            DisplayName = "Fuel Pump R2",
            Type = SimConnect.SimVarType.Event,
            EventParam = 6  // R2 = 6
        },
        ["FUELSYSTEM_VALVE_TOGGLE:9"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_TOGGLE",
            DisplayName = "Fuel Pump C1 (Jet Pump)",
            Type = SimConnect.SimVarType.Event,
            EventParam = 9  // C1 = 9 (center tank jet pump valve)
        },
        ["FUELSYSTEM_VALVE_TOGGLE:10"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_TOGGLE",
            DisplayName = "Fuel Pump C2 (Jet Pump)",
            Type = SimConnect.SimVarType.Event,
            EventParam = 10  // C2 = 10 (center tank jet pump valve)
        },
        ["FUELSYSTEM_VALVE_TOGGLE:3"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_TOGGLE",
            DisplayName = "Fuel Crossfeed",
            Type = SimConnect.SimVarType.Event,
            EventParam = 3  // Crossfeed valve = 3
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

        // ---- Audio Control Panel (parity with the A380 ACP). The A32NX models radio
        // reception as per-channel VOLUME (0..100, live-verified settable: VHF1 held 42),
        // surfaced here as 5-step combos. RMP-L = Captain, RMP-R = First Officer. ----
        ["A32NX_RMP_L_VHF1_VOLUME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_L_VHF1_VOLUME", DisplayName = "VHF 1 Volume",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Low", [50] = "Medium", [75] = "High", [100] = "Full" }
        },
        ["A32NX_RMP_L_VHF2_VOLUME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_L_VHF2_VOLUME", DisplayName = "VHF 2 Volume",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Low", [50] = "Medium", [75] = "High", [100] = "Full" }
        },
        ["A32NX_RMP_L_VHF3_VOLUME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_L_VHF3_VOLUME", DisplayName = "VHF 3 Volume",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Low", [50] = "Medium", [75] = "High", [100] = "Full" }
        },
        ["A32NX_RMP_R_VHF1_VOLUME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_R_VHF1_VOLUME", DisplayName = "VHF 1 Volume",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Low", [50] = "Medium", [75] = "High", [100] = "Full" }
        },
        ["A32NX_RMP_R_VHF2_VOLUME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_R_VHF2_VOLUME", DisplayName = "VHF 2 Volume",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Low", [50] = "Medium", [75] = "High", [100] = "Full" }
        },
        ["A32NX_RMP_R_VHF3_VOLUME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RMP_R_VHF3_VOLUME", DisplayName = "VHF 3 Volume",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Low", [50] = "Medium", [75] = "High", [100] = "Full" }
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

        // ---- Wipers panel (parity with A380 Overhead > Wipers) — Captain + F/O wiper
        // selectors. Live-verified settable via the calculator path (held a 0->2 write).
        ["XMLVAR_A320_WiperSwitch_1"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_A320_WiperSwitch_1", DisplayName = "Wiper Captain",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Slow", [2] = "Fast" }
        },
        ["XMLVAR_A320_WiperSwitch_2"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_A320_WiperSwitch_2", DisplayName = "Wiper First Officer",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Slow", [2] = "Fast" }
        },

        // OVERHEAD FORWARD SECTION - Calls Panel
        ["PUSH_OVHD_CALLS_MECH"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_MECH",
            DisplayName = "Call MECH",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["PUSH_OVHD_CALLS_ALL"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_ALL",
            DisplayName = "Call ALL",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["PUSH_OVHD_CALLS_FWD"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_FWD",
            DisplayName = "Call FWD",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["PUSH_OVHD_CALLS_AFT"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_AFT",
            DisplayName = "Call AFT",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["A32NX_CALLS_EMER_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CALLS_EMER_ON",
            DisplayName = "Emergency Call",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
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
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_GPWS_GS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_GS_OFF",
            DisplayName = "GPWS Glideslope Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_GPWS_SYS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_SYS_OFF",
            DisplayName = "GPWS System",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_GPWS_TERR_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_TERR_OFF",
            DisplayName = "GPWS Terrain",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
        ["ENGINE_1_MASTER_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_OPEN",
            DisplayName = "Engine 1 Master ON",
            Type = SimConnect.SimVarType.Event,
            EventParam = 1
        },
        ["ENGINE_1_MASTER_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_CLOSE",
            DisplayName = "Engine 1 Master OFF",
            Type = SimConnect.SimVarType.Event,
            EventParam = 1
        },
        ["ENGINE_2_MASTER_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_OPEN",
            DisplayName = "Engine 2 Master ON",
            Type = SimConnect.SimVarType.Event,
            EventParam = 2
        },
        ["ENGINE_2_MASTER_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM_VALVE_CLOSE",
            DisplayName = "Engine 2 Master OFF",
            Type = SimConnect.SimVarType.Event,
            EventParam = 2
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

        // Lighting Events
        ["LANDING_LIGHTS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_ON",
            DisplayName = "Landing Lights On",
            Type = SimConnect.SimVarType.Event
        },
        ["LANDING_LIGHTS_OFF"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_OFF",
            DisplayName = "Landing Lights Off",
            Type = SimConnect.SimVarType.Event
        },
        ["CIRCUIT_SWITCH_ON_17"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT_SWITCH_ON",
            DisplayName = "Circuit Switch 17 On",
            Type = SimConnect.SimVarType.Event,
            EventParam = 17
        },
        ["CIRCUIT_SWITCH_ON_18"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT_SWITCH_ON",
            DisplayName = "Circuit Switch 18 On",
            Type = SimConnect.SimVarType.Event,
            EventParam = 18
        },
        ["CIRCUIT_SWITCH_ON_19"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT_SWITCH_ON",
            DisplayName = "Circuit Switch 19 On",
            Type = SimConnect.SimVarType.Event,
            EventParam = 19
        },
        ["CIRCUIT_SWITCH_ON_20"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT_SWITCH_ON",
            DisplayName = "Circuit Switch 20 On",
            Type = SimConnect.SimVarType.Event,
            EventParam = 20
        },
        ["LIGHT_TAXI"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT_TAXI",
            DisplayName = "Taxi Light",
            Type = SimConnect.SimVarType.Event
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
        ["LANDING_LIGHTS_ON_THIRD_PARTY"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_ON",
            DisplayName = "All landing lights on for third party programs",
            Type = SimConnect.SimVarType.Event
        },
        ["LANDING_LIGHTS_OFF_THIRD_PARTY"] = new SimConnect.SimVarDefinition
        {
            Name = "LANDING_LIGHTS_OFF",
            DisplayName = "All landing lights off for third party programs",
            Type = SimConnect.SimVarType.Event
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
        ["XPNDR_IDENT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "XPNDR_IDENT_ON",
            DisplayName = "IDENT",
            Type = SimConnect.SimVarType.Event
        },
        ["A32NX_SWITCH_TCAS_TRAFFIC_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_TCAS_TRAFFIC_POSITION",
            DisplayName = "TCAS Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "THRT", [1] = "ALL", [2] = "ABV", [3] = "BLW" }
        },
        ["A32NX_SWITCH_TCAS_POSITION"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SWITCH_TCAS_POSITION",
            DisplayName = "TCAS Traffic",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "STBY", [1] = "TA", [2] = "TA/RA" }
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
        // Idle (-0.44) verified live to snap to the A320 idle dead-zone; the rest mirror
        // the A380 default-calibration values. The displayed value reflects the last
        // command, not the live lever (the live angle is in d["Thrust Levers"]).
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
        ["A32NX_FMGC_1_PRESEL_SPEED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_PRESEL_SPEED",
            DisplayName = "Preselected Speed",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "knots"
        },
        ["A32NX_FMGC_1_PRESEL_MACH"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_PRESEL_MACH",
            DisplayName = "Preselected Mach",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "mach"
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
        ["A32NX_FMGC_1_CRUISE_FLIGHT_LEVEL"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_CRUISE_FLIGHT_LEVEL",
            DisplayName = "Cruise Flight Level",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
        ["A32NX_FM2_MINIMUM_DESCENT_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM2_MINIMUM_DESCENT_ALTITUDE",
            DisplayName = "Minimum Descent Height (Radio)",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "feet"
        },
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
            ValueDescriptions = new Dictionary<double, string> { [0] = "Selected speed", [1] = "Managed speed" }
        },
        ["A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_AFS_DISPLAY_LVL_CH_MANAGED",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
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
        ["A32NX_APPROACH_CAPABILITY"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_APPROACH_CAPABILITY",
            DisplayName = "Approach Capability",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "NONE", [1] = "CAT1", [2] = "CAT2", [3] = "CAT3 SINGLE", [4] = "CAT3 DUAL"
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
        ["A32NX_FM1_MINIMUM_DESCENT_ALTITUDE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FM1_MINIMUM_DESCENT_ALTITUDE",
            DisplayName = "Baro Minimum",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            Units = "feet"
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
            DisplayName = "Linear Deviation Active",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "Linear Deviation Active"
            }
        },
        ["A32NX_FMGC_1_LDEV_REQUEST"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMGC_1_LDEV_REQUEST",
            DisplayName = "FMGC L DEV Request",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "L/DEV Requested"
            }
        },
        ["A32NX_FMA_CRUISE_ALT_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_CRUISE_ALT_MODE",
            DisplayName = "Cruise Altitude Mode",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Not shown", [1] = "ALT CRZ"
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

        // ECAM STATUS PAGE VARIABLES (numeric codes for ECAM Status page - LEFT side)
        // Display-only, not announced (used by Status Display window via hotkey)
        ["A32NX_STATUS_LEFT_LINE_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_4"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_4",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_5"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_5",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_6"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_6",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_7"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_7",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_8"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_8",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_9"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_9",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_10"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_10",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_11"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_11",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_12"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_12",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_13"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_13",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_14"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_14",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_15"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_15",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_16"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_16",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_17"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_17",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_LEFT_LINE_18"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_LEFT_LINE_18",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },

        // ECAM STATUS PAGE VARIABLES (numeric codes for ECAM Status page - RIGHT side)
        ["A32NX_STATUS_RIGHT_LINE_1"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_1",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_2"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_2",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_3"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_3",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_4"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_4",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_5"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_5",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_6"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_6",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_7"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_7",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_8"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_8",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_9"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_9",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_10"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_10",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_11"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_11",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_12"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_12",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_13"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_13",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_14"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_14",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_15"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_15",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_16"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_16",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_17"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_17",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },
        ["A32NX_STATUS_RIGHT_LINE_18"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STATUS_RIGHT_LINE_18",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
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
        ["A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS",
            DisplayName = "ND FM Message",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true, // Enable announcements for ND FM messages
            Units = "number"
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
        ["A32NX_EFIS_L_LS_BUTTON_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_L_LS_BUTTON_IS_ON", DisplayName = "ILS",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_EFIS_R_LS_BUTTON_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_EFIS_R_LS_BUTTON_IS_ON", DisplayName = "ILS",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
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

        // "Activate" pulses the real glareshield MASTER WARN / CAUT pushbutton (handled
        // in HandleUIVariableSet) to acknowledge + silence the aural. (Was wrongly wired
        // to write the A32NX_MASTER_WARNING output var, which does nothing.)
        ["CLEAR_MASTER_WARNING"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_AUTOPILOT_MASTERWARN_L",
            DisplayName = "Clear Master Warning",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        ["CLEAR_MASTER_CAUTION"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_AUTOPILOT_MASTERCAUT_L",
            DisplayName = "Clear Master Caution",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
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


        // Fuel System Active State Variables (continuous monitoring with auto-announcement)
        ["FUELSYSTEM PUMP ACTIVE:2"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP ACTIVE:2",
            DisplayName = "Fuel Pump L1",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump L1 off", [1] = "Fuel Pump L1 active" }
        },
        ["FUELSYSTEM PUMP ACTIVE:5"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP ACTIVE:5",
            DisplayName = "Fuel Pump L2",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump L2 Off", [1] = "Fuel Pump L2 active" }
        },
        ["FUELSYSTEM PUMP ACTIVE:3"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP ACTIVE:3",
            DisplayName = "Fuel Pump R1",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump R1 Off", [1] = "Fuel Pump R1 active" }
        },
        ["FUELSYSTEM PUMP ACTIVE:6"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM PUMP ACTIVE:6",
            DisplayName = "Fuel Pump R2",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Pump R2 Off", [1] = "Fuel Pump R2 active" }
        },
        ["FUELSYSTEM VALVE OPEN:9"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE OPEN:9",
            DisplayName = "Fuel Jet Pump C1 Valve",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Jet Pump C1 Valve Closed", [1] = "Fuel Jet Pump C1 Valve Open" }
        },
        ["FUELSYSTEM VALVE OPEN:10"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE OPEN:10",
            DisplayName = "Fuel Jet Pump C2 Valve",
            Type = SimConnect.SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Fuel Jet Pump C2 Valve Closed", [1] = "Fuel Jet Pump C2 Valve Open" }
        },
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
        ["A32NX_TOTAL_FUEL_QUANTITY"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TOTAL_FUEL_QUANTITY",
            DisplayName = "Total Fuel Quantity",
            Type = SimConnect.SimVarType.LVar,
            Units = "kilograms",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsAnnounced = false
        },

        // PEDESTAL SECTION - RMP Panel
        ["COM_ACTIVE_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:1",
            DisplayName = "Set Active Frequency",
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
                [0] = "SEL", [1] = "VHF1", [2] = "VHF2", [3] = "VHF3"
            }
        },
        ["COM_ACTIVE_FREQUENCY:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM ACTIVE FREQUENCY:1",
            DisplayName = "COM 1 Active Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_STANDBY_FREQUENCY:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:1",
            DisplayName = "COM 1 Standby Frequency",
            Type = SimConnect.SimVarType.SimVar,
            Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        // ---- COM2 / VHF2 (parity: the Radios panel now exposes COM1 + COM2) ----
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
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_STANDBY_FREQUENCY:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM STANDBY FREQUENCY:2", DisplayName = "COM 2 Standby Frequency",
            Type = SimConnect.SimVarType.SimVar, Units = "kHz",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
        ["COM_TRANSMIT:1"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:1",
            DisplayName = "VHF1 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
        },
        ["COM_TRANSMIT:2"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:2",
            DisplayName = "VHF2 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Transmitting" }
        },
        ["COM_TRANSMIT:3"] = new SimConnect.SimVarDefinition
        {
            Name = "COM TRANSMIT:3",
            DisplayName = "VHF3 Transmit",
            Type = SimConnect.SimVarType.SimVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
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
        ["A32NX_FAC_1_V_FE_NEXT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FAC_1_V_FE_NEXT",
            Type = SimConnect.SimVarType.LVar,
            DisplayName = "V FE Speed",
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

        // GPWS Master Warning
        ["A32NX_GPWS_Warning_Active"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_Warning_Active",
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

        // GPWS Glide Slope Mode 5 Warning
        ["A32NX_GPWS_GS_Warning_Active"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_GPWS_GS_Warning_Active",
            DisplayName = "Glide Slope Mode 5 warning light",
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
            Name = "A32NX_ENGINE_TANK_OIL:1",
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
            Name = "A32NX_ENGINE_TANK_OIL:2",
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

        // ---- Bleed Air panel (parity with the A380 Overhead > Bleed Air) ----
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
        ["A32NX_OVHD_COND_RAM_AIR_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_COND_RAM_AIR_PB_IS_ON", DisplayName = "Ram Air",
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

        // ---- Pressurization panel (parity with A380 Overhead > Pressurization) ----
        ["A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO", DisplayName = "Pressurization Mode",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Manual", [1] = "Auto" }
        },
        ["A32NX_OVHD_PRESS_DITCHING_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_DITCHING_PB_IS_ON", DisplayName = "Ditching",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ---- Ventilation panel (parity with A380 Overhead > Ventilation) ----
        ["A32NX_OVHD_VENT_BLOWER_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_VENT_BLOWER_PB_IS_ON", DisplayName = "Blower",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Override" }
        },
        ["A32NX_OVHD_VENT_EXTRACT_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_VENT_EXTRACT_PB_IS_ON", DisplayName = "Extract",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Override" }
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

        // ---- Cargo Air panel (parity with A380 Overhead > Cargo Air) ----
        ["A32NX_OVHD_CARGO_AIR_HOT_AIR_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_CARGO_AIR_HOT_AIR_PB_IS_ON", DisplayName = "Cargo Hot Air",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_CARGO_AIR_ISOL_VALVES_FWD_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_FWD_PB_IS_ON", DisplayName = "Cargo Fwd Isolation Valve",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_CARGO_AIR_ISOL_VALVES_AFT_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_AFT_PB_IS_ON", DisplayName = "Aft Isolation Valve",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },

        // ---- Recorder and Misc panel (parity with A380 Overhead > Recorder and Misc) ----
        ["A32NX_RCDR_GROUND_CONTROL_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_RCDR_GROUND_CONTROL_ON", DisplayName = "Recorder Ground Control",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_ELT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ELT_ON", DisplayName = "ELT",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Armed", [1] = "On" }
        },

        // ---- Interior Lighting panel (parity with A380 Overhead > Interior Lighting) ----
        ["A32NX_OVHD_INTLT_ANN"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_INTLT_ANN", DisplayName = "Annunciator Lights",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Test", [1] = "Bright", [2] = "Dim" }
        },
        ["A32NX_OVHD_INTLT_DOME"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_INTLT_DOME", DisplayName = "Dome Light",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Dim", [2] = "Bright" }
        },

        // ---- Ground Services > Doors (parity with A380). One Closed/Open combo per
        // exit: live state from INTERACTIVE POINT OPEN:n (a 0..1 fraction during the
        // open/close animation), set toggles via TOGGLE_AIRCRAFT_EXIT:n in
        // HandleUIVariableSet. ProcessSimVarUpdate announces Open/Closed once per
        // transition; TryGetDisplayOverride renders the state cleanly. ----
        ["A32NX_MSFSBA_DOOR_0"] = new SimConnect.SimVarDefinition
        {
            Name = "INTERACTIVE POINT OPEN:0", DisplayName = "Front Left Door",
            Type = SimConnect.SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        },
        ["A32NX_MSFSBA_DOOR_1"] = new SimConnect.SimVarDefinition
        {
            Name = "INTERACTIVE POINT OPEN:1", DisplayName = "Front Right Door",
            Type = SimConnect.SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        },
        ["A32NX_MSFSBA_DOOR_2"] = new SimConnect.SimVarDefinition
        {
            Name = "INTERACTIVE POINT OPEN:2", DisplayName = "Rear Left Door",
            Type = SimConnect.SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        },
        ["A32NX_MSFSBA_DOOR_3"] = new SimConnect.SimVarDefinition
        {
            Name = "INTERACTIVE POINT OPEN:3", DisplayName = "Rear Right Door",
            Type = SimConnect.SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        },
        ["A32NX_MSFSBA_DOOR_4"] = new SimConnect.SimVarDefinition
        {
            Name = "INTERACTIVE POINT OPEN:4", DisplayName = "Forward Cargo Door",
            Type = SimConnect.SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        },
        ["A32NX_MSFSBA_DOOR_5"] = new SimConnect.SimVarDefinition
        {
            Name = "INTERACTIVE POINT OPEN:5", DisplayName = "Aft Cargo Door",
            Type = SimConnect.SimVarType.SimVar, Units = "percent over 100",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        },

        // ---- Ground Services > Ground Equipment (parity with A380). The A320 has no
        // clean jetway/stairs state var (unlike the A380's A380X_GND_*), so these are
        // momentary "Activate" combos that fire the stock toggle events in
        // HandleUIVariableSet. The flyPad Ground page remains the richer interface. ----
        ["A32NX_MSFSBA_JETWAY"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MSFSBA_JETWAY", DisplayName = "Jet Bridge",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        ["A32NX_MSFSBA_STAIRS"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MSFSBA_STAIRS", DisplayName = "Stairs",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Activate" }
        },
        };

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
                    && !key.EndsWith("_DETENT", StringComparison.Ordinal)  // synthetic thrust-lever detent combos — no real L:var to monitor
                    && variables.TryGetValue(key, out var cdef)
                    && (cdef.Type == SimConnect.SimVarType.LVar || cdef.Type == SimConnect.SimVarType.SimVar)
                    && cdef.ValueDescriptions != null && cdef.ValueDescriptions.Count > 0
                    && cdef.UpdateFrequency == SimConnect.UpdateFrequency.OnRequest
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
                    variables[sdVar] = new SimConnect.SimVarDefinition
                    {
                        Name = sdVar,
                        DisplayName = label,
                        Type = SimConnect.SimVarType.LVar,
                        UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
                    };
                }
            }
        }

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
            "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON"
        },
        ["Air Conditioning"] = new List<string>
        {
            // Actual zone temperatures (the selectors in the panel set the target).
            "A32NX_COND_CKPT_TEMP", "A32NX_COND_FWD_TEMP", "A32NX_COND_AFT_TEMP"
        },
        ["Flight Control Computers"] = new List<string>
        {
            // FAC-computed rudder trim position (ARINC; decoded in TryGetDisplayOverride).
            "A32NX_FAC_1_RUDDER_TRIM_POS"
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
        ["Radios"] = new List<string>
        {
            "COM_ACTIVE_FREQUENCY:1",
            "COM_STANDBY_FREQUENCY:1",
            "COM_ACTIVE_FREQUENCY:2",
            "COM_STANDBY_FREQUENCY:2"
        },
        ["RMP"] = new List<string>
        {
            "COM_TRANSMIT:1",
            "COM_TRANSMIT:2",
            "COM_TRANSMIT:3"
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
            "A32NX_APPROACH_CAPABILITY",
            "A32NX_PFD_TARGET_ALTITUDE",
            "PLANE_PITCH_DEGREES",
            "PLANE_BANK_DEGREES",
            "PLANE_HEADING_DEGREES_MAGNETIC",
            "AIRSPEED_INDICATED",
            "INDICATED_ALTITUDE",
            "A32NX_PFD_MSG_SET_HOLD_SPEED",
            "A32NX_PFD_MSG_TD_REACHED",
            "A32NX_PFD_MSG_CHECK_SPEED_MODE",
            "A32NX_PFD_LINEAR_DEVIATION_ACTIVE"
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
            "A32NX_RADIO_RECEIVER_LOC_IS_VALID",
            "A32NX_RADIO_RECEIVER_LOC_DEVIATION",
            "A32NX_RADIO_RECEIVER_GS_IS_VALID",
            "A32NX_RADIO_RECEIVER_GS_DEVIATION"
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
            "AIRSPEED_INDICATED",
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
["Overhead"] = new List<string> { "ELEC", "ADIRS", "APU", "Oxygen", "Fire", "Hydraulics", "Fuel", "Air Conditioning", "Bleed Air", "Pressurization", "Ventilation", "Cargo Air", "Anti Ice", "Wipers", "Signs", "Interior Lighting", "Exterior Lighting", "Calls", "GPWS", "Flight Control Computers", "Cockpit Door", "Evacuation", "Cargo Smoke", "Recorder and Misc", "Engine Start" },
        ["Glareshield"] = new List<string> { "FCU", "EFIS Captain", "EFIS First Officer", "Warnings" },
        ["Instrument"] = new List<string> { "Gear", "Autobrake", "PFD", "ND", "ISIS", "Source Switching", "Clock", "System Display" },
        ["Pedestal"] = new List<string> { "Flight Controls", "Speed Brake", "Parking Brake", "Engines", "Thrust Levers", "ECAM Control Panel", "Weather Radar", "Transponder", "Radios", "RMP", "Audio Control Panel Captain", "Audio Control Panel First Officer" },
        ["Ground Services"] = new List<string> { "Doors", "Ground Equipment" }
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
            "A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON",
            "A32NX_OVHD_ELEC_ENG_GEN_2_PB_IS_ON",
            "A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON",
            "A32NX_OVHD_ELEC_BUS_TIE_PB_IS_AUTO",
            "A32NX_OVHD_ELEC_AC_ESS_FEED_PB_IS_NORMAL",
            "A32NX_OVHD_ELEC_IDG_1_PB_IS_RELEASED",
            "A32NX_OVHD_ELEC_IDG_2_PB_IS_RELEASED",
            "A32NX_OVHD_ELEC_COMMERCIAL_PB_IS_ON"
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
            "A32NX_OVHD_APU_START_PB_IS_ON" 
        },
        ["Oxygen"] = new List<string>
        {
            "PUSH_OVHD_OXYGEN_CREW",
            "A32NX_OXYGEN_MASKS_DEPLOYED",
            "A32NX_OXYGEN_PASSENGER_LIGHT_ON"
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
        },
        ["Fuel"] = new List<string>
        { 
            "FUELSYSTEM_PUMP_TOGGLE:2", 
            "FUELSYSTEM_PUMP_TOGGLE:5", 
            "FUELSYSTEM_PUMP_TOGGLE:3", 
            "FUELSYSTEM_PUMP_TOGGLE:6", 
            "FUELSYSTEM_VALVE_TOGGLE:9", 
            "FUELSYSTEM_VALVE_TOGGLE:10",
            "FUELSYSTEM_VALVE_TOGGLE:3" 
        },
        ["Air Conditioning"] = new List<string>
        {
            "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
            "A32NX_OVHD_COND_PACK_1_PB_IS_ON",
            "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
            "COND_CKPT_TEMP_SET",
            "COND_FWD_TEMP_SET",
            "COND_AFT_TEMP_SET"
        },
        ["Bleed Air"] = new List<string>
        {
            "A32NX_OVHD_PNEU_ENG_1_BLEED_PB_IS_AUTO",
            "A32NX_OVHD_PNEU_ENG_2_BLEED_PB_IS_AUTO",
            "A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position",
            "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position",
            "A32NX_OVHD_COND_HOT_AIR_PB_IS_ON",
            "A32NX_OVHD_COND_RAM_AIR_PB_IS_ON"
        },
        ["Pressurization"] = new List<string>
        {
            "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO", "A32NX_OVHD_PRESS_DITCHING_PB_IS_ON"
        },
        ["Ventilation"] = new List<string>
        {
            "A32NX_OVHD_VENT_BLOWER_PB_IS_ON", "A32NX_OVHD_VENT_EXTRACT_PB_IS_ON",
            "A32NX_OVHD_VENT_CAB_FANS_PB_IS_ON"
        },
        ["Cargo Air"] = new List<string>
        {
            "A32NX_OVHD_CARGO_AIR_HOT_AIR_PB_IS_ON",
            "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_FWD_PB_IS_ON",
            "A32NX_OVHD_CARGO_AIR_ISOL_VALVES_AFT_PB_IS_ON"
        },
        ["Interior Lighting"] = new List<string>
        {
            "A32NX_OVHD_INTLT_ANN",
            "A32NX_OVHD_INTLT_DOME"
        },
        ["Recorder and Misc"] = new List<string>
        {
            "A32NX_RCDR_GROUND_CONTROL_ON",
            "A32NX_ELT_ON"
        },
        ["Flight Control Computers"] = new List<string>
        {
            "A32NX_ELAC_1_PUSHBUTTON_PRESSED", "A32NX_ELAC_2_PUSHBUTTON_PRESSED",
            "A32NX_SEC_1_PUSHBUTTON_PRESSED", "A32NX_SEC_2_PUSHBUTTON_PRESSED", "A32NX_SEC_3_PUSHBUTTON_PRESSED",
            "A32NX_FAC_1_PUSHBUTTON_PRESSED", "A32NX_FAC_2_PUSHBUTTON_PRESSED",
            "A32NX_RUDDER_TRIM_RESET"
        },
        ["Anti Ice"] = new List<string>
        {
            "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_SELECTED",
            "ENG_ANTI_ICE:1",
            "ENG_ANTI_ICE:2"
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
            "LIGHT NAV",
            "LIGHT LOGO",
            "CIRCUIT_SWITCH_ON:21",
            "CIRCUIT_SWITCH_ON:22",
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
        ["Cockpit Door"] = new List<string>
        {
            "A32NX_COCKPIT_DOOR_LOCKED",
            "A32NX_OVHD_COCKPITDOORVIDEO_TOGGLE",
        },
        ["Evacuation"] = new List<string>
        {
            "A32NX_EVAC_COMMAND_TOGGLE",
        },
        ["Cargo Smoke"] = new List<string>
        {
            "A32NX_FIRE_TEST_CARGO",
            "A32NX_CARGOSMOKE_FWD_DISCHARGED",
        },
        ["Engine Start"] = new List<string>
        {
            "A32NX_OVHD_FADEC_1",
            "A32NX_OVHD_FADEC_2",
            "A32NX_ENGMANSTART1_TOGGLE",
            "A32NX_ENGMANSTART2_TOGGLE",
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
            "A32NX_EFIS_L_LS_BUTTON_IS_ON",
            "A32NX.FCU_EFIS_L_FD_PUSH",
            "A32NX_FCU_EFIS_L_BARO_IS_INHG",
            "A32NX.FCU_EFIS_L_BARO_SET",
            "A32NX.FCU_EFIS_L_BARO_PUSH",
            "A32NX.FCU_EFIS_L_BARO_PULL"
        },
        ["EFIS First Officer"] = new List<string>
        {
            "A32NX_FCU_EFIS_R_EFIS_MODE",
            "A32NX_FCU_EFIS_R_EFIS_RANGE",
            "A32NX_EFIS_R_LS_BUTTON_IS_ON",
            "A32NX.FCU_EFIS_R_FD_PUSH",
            "A32NX_FCU_EFIS_R_BARO_IS_INHG",
            "A32NX.FCU_EFIS_R_BARO_SET",
            "A32NX.FCU_EFIS_R_BARO_PUSH",
            "A32NX.FCU_EFIS_R_BARO_PULL"
        },
        ["Warnings"] = new List<string>
        {
            "CLEAR_MASTER_WARNING",
            "CLEAR_MASTER_CAUTION"
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
            "SPOILERS_ARM_TOGGLE",
            "SPOILERS_OFF",
            "SPOILERS_ON"
        },
        ["Parking Brake"] = new List<string>
        {
            "A32NX_PARK_BRAKE_LEVER_POS"
        },
        ["Engines"] = new List<string>
        {
            "ENGINE_1_MASTER_ON",
            "ENGINE_1_MASTER_OFF",
            "ENGINE_2_MASTER_ON",
            "ENGINE_2_MASTER_OFF",
            "ENGINE_MODE_SELECTOR"
        },
        ["Thrust Levers"] = new List<string>
        {
            "THROTTLE_ALL_DETENT", "THROTTLE_1_DETENT", "THROTTLE_2_DETENT"
        },
        ["Doors"] = new List<string>
        {
            "A32NX_MSFSBA_DOOR_0", "A32NX_MSFSBA_DOOR_1", "A32NX_MSFSBA_DOOR_2",
            "A32NX_MSFSBA_DOOR_3", "A32NX_MSFSBA_DOOR_4", "A32NX_MSFSBA_DOOR_5"
        },
        ["Ground Equipment"] = new List<string>
        {
            "A32NX_MSFSBA_JETWAY", "A32NX_MSFSBA_STAIRS"
        },
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
            "A32NX_SWITCH_RADAR_PWS_POSITION"
        },
        ["Transponder"] = new List<string>
        {
            "A32NX_TRANSPONDER_MODE",
            "A32NX_TRANSPONDER_SYSTEM",
            "A32NX_SWITCH_ATC_ALT",
            "TRANSPONDER_CODE_SET",
            "XPNDR_IDENT_ON",
            "A32NX_SWITCH_TCAS_TRAFFIC_POSITION",
            "A32NX_SWITCH_TCAS_POSITION"
        },
        ["Radios"] = new List<string>
        {
            "COM_STANDBY_FREQUENCY_SET:1",
            "COM1_RADIO_SWAP",
            "COM_STANDBY_FREQUENCY_SET:2",
            "COM2_RADIO_SWAP"
        },
        ["RMP"] = new List<string>
        {
            "A32NX_RMP_L_TOGGLE_SWITCH",
            "A32NX_RMP_L_SELECTED_MODE"
        },
        ["Audio Control Panel Captain"] = new List<string>
        {
            "A32NX_RMP_L_VHF1_VOLUME", "A32NX_RMP_L_VHF2_VOLUME", "A32NX_RMP_L_VHF3_VOLUME"
        },
        ["Audio Control Panel First Officer"] = new List<string>
        {
            "A32NX_RMP_R_VHF1_VOLUME", "A32NX_RMP_R_VHF2_VOLUME", "A32NX_RMP_R_VHF3_VOLUME"
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
            "A32NX_EIS_DMC_SWITCHING_KNOB", "A32NX_FMGC_TRUE_REF"
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
            // FCU push/pull buttons
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
                announcer.AnnounceImmediate(BaroPhrase(_baroHpa < 0 ? 1013 : _baroHpa, _baroMode < 0 ? 1 : _baroMode));
                return true;
            // FCU value windows (Fenix-style: value entry + knob Push/Pull + mode
            // toggles + spoken read-out). Mirrors the A380's Ctrl+S/H/A/V/P/B windows;
            // replaces the old single-field ShowA320*InputDialog dialogs.
            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA320.FBWA320HeadingWindow(this, simConnect, announcer).ShowForm();
                return true;

            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA320.FBWA320SpeedWindow(this, simConnect, announcer).ShowForm();
                return true;

            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA320.FBWA320AltitudeWindow(this, simConnect, announcer).ShowForm();
                return true;

            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA320.FBWA320VSWindow(this, simConnect, announcer).ShowForm();
                return true;

            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA320.FBWA320AutopilotWindow(this, simConnect, announcer).ShowForm();
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
            case HotkeyAction.ReadWaypointInfo: // W -> "Gross weight N pounds"
                simConnect.RequestSingleValue((int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT, "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
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
            // ISIS / System Display), exactly like the A380. ShowPFD /
            // ShowNavigationDisplay / ShowECAM / ShowStatusPage fall through to no-op.
            // EWD on-demand read (output mode → ReadDisplayUpperECAM) — speaks the live
            // upper E/WD (engine row + memos + warnings) aloud, mirroring the A380's
            // Alt+E ReadAllEwdWarnings. The EWD also stays available as the display
            // (System Display page 0 status box + the continuous EWD monitor).
            case HotkeyAction.ReadDisplayUpperECAM:
                ReadEwdAloud(announcer);
                return true;
            // On-demand flaps / gear read (parity with the A380; L and Shift+G).
            case HotkeyAction.ReadFlaps:
                if (simConnect.IsConnected) { _reqFlaps = true; simConnect.RequestVariable("A32NX_FLAPS_HANDLE_INDEX", forceUpdate: true); }
                return true;
            case HotkeyAction.ReadGear:
                if (simConnect.IsConnected) { _reqGear = true; simConnect.RequestVariable("GEAR_HANDLE_POSITION", forceUpdate: true); }
                return true;
            case HotkeyAction.ReadFuelInfo:
                if (simConnect.IsConnected) { _reqFuelKg = true; simConnect.RequestVariable("FUEL_QUANTITY_KG", forceUpdate: true); }
                return true;

            case HotkeyAction.ToggleECAMMonitoring:
                ToggleA320ECAMMonitoring(simConnect, announcer);
                return true;

            case HotkeyAction.ReadGrossWeightKg:
                if (simConnect.IsConnected) { _reqGw = true; simConnect.RequestVariable("GROSS_WEIGHT_KG", forceUpdate: true); }
                return true;

            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                new Forms.FBWA320.FBWA320BaroWindow(this, simConnect, announcer).ShowForm();
                return true;
        }

        // Fall back to base class for simple variable mappings
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    // Helper methods for A32NX-specific hotkey actions
    private void HandleReadApproachCapability(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // Get cached value immediately
        var cachedValue = simConnect.GetCachedVariableValue("A32NX_APPROACH_CAPABILITY");
        if (cachedValue.HasValue)
        {
            string capabilityText = cachedValue.Value switch
            {
                0 => "RNP APCH",
                1 => "CAT 1",
                2 => "CAT 2",
                3 => "CAT 3 Single",
                4 => "CAT 3 Dual",
                _ => $"Unknown ({cachedValue.Value})"
            };
            announcer.AnnounceImmediate($"Approach capability: {capabilityText}");
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
    private double _baroHpa = -1;          // last decoded captain baro, hectopascals
    private int _baroMode = -1;            // A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE: 0=STD,1=hPa,2=inHg
    private int _lastAnnouncedBaroHpa = -1;
    private double _baroHpaR = -1;         // last decoded F/O baro, hectopascals
    private int _baroModeR = -1;           // A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE
    private int _lastAnnouncedBaroHpaR = -1;
    private static string BaroPhrase(double hpa, int mode)
    {
        if (mode == 0) return "Altimeter standard";
        if (mode == 2) return $"Altimeter {hpa * 0.0295299830714:F2} inches";
        return $"Altimeter {hpa:F0} hectopascals";
    }

    public const string SdPageVar = "A32NX_MSFSBA_SD_PAGE";
    private SimConnect.CoherentDisplayClient? _ewdScrapeClient;
    private string _sdBoxContent = "";

    // ---- ND status-box cache ---------------------------------------------------
    // The TO-waypoint ident is packed 6-bit-per-char (8 chars in word 0 — enough for
    // any real ident; word 1 cached for completeness). Cached as it flows through
    // ProcessSimVarUpdate; TryGetDisplayOverride on the *_0 word decodes it.
    private double _ndIdent0, _ndIdent1;

    // ---- Ground-service doors ----
    // The A320's exits toggle via the stock K:TOGGLE_AIRCRAFT_EXIT with the exit index,
    // and state reads from INTERACTIVE POINT OPEN:index (1:1 mapping, verified live —
    // toggling exit 0 set INTERACTIVE POINT OPEN:0 = 1). Exit types: 0-3 = passenger,
    // 4-5 = cargo (verified via EXIT TYPE:n).
    private static readonly Dictionary<string, string> _doorNames = new()
    {
        ["A32NX_MSFSBA_DOOR_0"] = "Front Left Door",
        ["A32NX_MSFSBA_DOOR_1"] = "Front Right Door",
        ["A32NX_MSFSBA_DOOR_2"] = "Rear Left Door",
        ["A32NX_MSFSBA_DOOR_3"] = "Rear Right Door",
        ["A32NX_MSFSBA_DOOR_4"] = "Forward Cargo Door",
        ["A32NX_MSFSBA_DOOR_5"] = "Aft Cargo Door",
    };
    private readonly Dictionary<string, bool> _doorOpen = new();

    // On-demand flaps/gear read (output-mode L / Shift+G) — request the var, announce
    // when it arrives in ProcessSimVarUpdate (parity with the A380).
    private bool _reqFlaps, _reqGear, _reqGw, _reqFuelKg;

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
    private static readonly Dictionary<double, string> SdPageNames = new()
    {
        [0] = "Upper E/WD", [1] = "Electrical", [2] = "Hydraulics", [3] = "Pressurization",
        [4] = "APU", [5] = "Air Conditioning", [6] = "Wheel / Brakes", [7] = "Bleed",
        [8] = "Fuel", [9] = "Doors", [10] = "Engine", [11] = "Flight Controls"
    };

    // Per-system SD readout rows (decoded SimVars). Added one system at a time.
    private static List<(string label, string var, Func<double, string> fmt)> SdSystemRows(int page)
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
        string Ft(double v) => $"{v:0} feet";
        string Fpm(double v) => $"{v:0} feet per minute";
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
        }
        else if (page == 3) // PRESSURIZATION
        {
            r.Add(("Cabin altitude", "A32NX_PRESS_CABIN_ALTITUDE", Ft));
            r.Add(("Cabin vertical speed", "A32NX_PRESS_CABIN_VS", Fpm));
            r.Add(("Differential pressure", "A32NX_PRESS_CABIN_DELTA_PRESSURE", v => $"{v:0.0} psi"));
            r.Add(("Outflow valve", "A32NX_PRESS_MAN_OUTFLOW_VALVE_OPEN_PERCENTAGE", Pct));
            r.Add(("Safety valve", "A32NX_PRESS_SAFETY_VALVE_OPEN_PERCENTAGE", Pct));
            r.Add(("Landing elevation", "A32NX_FM1_LANDING_ELEVATION", LElev));
        }
        else if (page == 4) // APU
        {
            r.Add(("APU N", "A32NX_APU_N", PctAir));
            r.Add(("APU EGT", "A32NX_APU_EGT", CAir));
            r.Add(("Inlet flap", "A32NX_APU_FLAP_OPEN_PERCENTAGE", Pct));
            r.Add(("Bleed valve", "A32NX_APU_BLEED_AIR_VALVE_OPEN", OpenShut));
            r.Add(("Low fuel pressure", "A32NX_APU_LOW_FUEL_PRESSURE_FAULT", YesNoAir));
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
        }
        else if (page == 7) // BLEED
        {
            r.Add(("Eng 1 precooler temp", "A32NX_PNEU_ENG_1_BLEED_TEMPERATURE_SENSOR_TEMPERATURE", C));
            r.Add(("Eng 1 bleed pressure", "A32NX_PNEU_ENG_1_REGULATED_TRANSDUCER_PRESSURE", Psi));
            r.Add(("Eng 1 bleed valve", "A32NX_PNEU_ENG_1_PR_VALVE_OPEN", OpenShut));
            r.Add(("Eng 2 precooler temp", "A32NX_PNEU_ENG_2_BLEED_TEMPERATURE_SENSOR_TEMPERATURE", C));
            r.Add(("Eng 2 bleed pressure", "A32NX_PNEU_ENG_2_REGULATED_TRANSDUCER_PRESSURE", Psi));
            r.Add(("Eng 2 bleed valve", "A32NX_PNEU_ENG_2_PR_VALVE_OPEN", OpenShut));
            r.Add(("Cross-bleed valve", "A32NX_PNEU_XBLEED_VALVE_FULLY_OPEN", OpenShut));
            r.Add(("APU bleed valve", "A32NX_APU_BLEED_AIR_VALVE_OPEN", OpenShut));
            r.Add(("Wing anti-ice", "A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", v => v > 0.5 ? "on" : "off"));
        }
        else if (page == 8) // FUEL
        {
            r.Add(("Fuel flow eng 1", "A32NX_ENGINE_FF:1", v => $"{v:0} kg per hour"));
            r.Add(("Fuel flow eng 2", "A32NX_ENGINE_FF:2", v => $"{v:0} kg per hour"));
            r.Add(("Fuel used eng 1", "A32NX_FUEL_USED:1", v => $"{v:0} kg"));
            r.Add(("Fuel used eng 2", "A32NX_FUEL_USED:2", v => $"{v:0} kg"));
            r.Add(("Total fuel on board", "A32NX_TOTAL_FUEL_QUANTITY", v => $"{v:0} kg"));
        }
        else if (page == 9) // DOORS
        {
            r.Add(("Forward cargo door", "A32NX_FWD_DOOR_CARGO_LOCKED", v => v > 0.5 ? "locked" : "unlocked"));
            r.Add(("Escape slides", "A32NX_SLIDES_ARMED", v => v > 0.5 ? "armed" : "disarmed"));
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
            r.Add(("Rudder trim", "A32NX_FAC_1_RUDDER_TRIM_POS", v => ArDeg(v, "left", "right")));
            r.Add(("Rudder travel limit", "A32NX_FAC_1_RUDDER_TRAVEL_LIMIT_COMMAND",
                v => { var w = new SimConnect.Arinc429Word(v); return (w.IsNormalOperation || w.IsFunctionalTest) ? $"{w.Value:0} degrees" : "not available"; }));
            r.Add(("Speed brake handle", "A32NX_SPOILERS_HANDLE_POSITION", v => $"{v * 100:0} %"));
            r.Add(("Ground spoilers armed", "A32NX_SPOILERS_ARMED", v => v > 0.5 ? "armed" : "disarmed"));
        }
        return r;
    }

    // On-demand spoken EWD read (output mode → ReadDisplayUpperECAM). Scrapes the live
    // upper E/WD (the same A32NX_EWD_1 view the System Display page-0 box uses) and
    // speaks each row. Mirrors the A380's Alt+E ReadAllEwdWarnings. The EWD also stays
    // on as the panel display + the continuous EWD-line monitor, so this is the
    // "read it now" companion to the always-on auto-announce.
    private async void ReadEwdAloud(ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_ewdScrapeClient == null)
            {
                _ewdScrapeClient = new SimConnect.CoherentDisplayClient("A32NX_EWD_1");
                _ewdScrapeClient.Start();
                _ewdScrapeClient.SetActive(false);   // on-demand only
            }
            await System.Threading.Tasks.Task.Delay(500);
            var rows = await _ewdScrapeClient.ScrapeNowAsync();
            if (rows == null || rows.Count == 0)
                announcer.AnnounceImmediate("E W D not available. Power up the displays and try again.");
            else
                announcer.AnnounceImmediate("E W D. " + string.Join(". ", rows));
        }
        catch { announcer.AnnounceImmediate("E W D read failed."); }
    }

    // Populate the System Display status box for the selected page, then force the box
    // to re-render (RequestVariable → ProcessSimVarUpdate → UpdateDisplayText →
    // TryGetDisplayOverride). No speech; the box just fills in on selection.
    private async void RefreshDisplayBoxAsync(int page, SimConnect.SimConnectManager sim)
    {
        try
        {
            string content;
            if (page == 0)   // E/WD — scrape the live display (engine row + memos/warnings)
            {
                if (_ewdScrapeClient == null)
                {
                    _ewdScrapeClient = new SimConnect.CoherentDisplayClient("A32NX_EWD_1");
                    _ewdScrapeClient.Start();
                    _ewdScrapeClient.SetActive(false);   // on-demand only
                }
                await System.Threading.Tasks.Task.Delay(700);
                var rows = await _ewdScrapeClient.ScrapeNowAsync();
                content = (rows == null || rows.Count == 0)
                    ? "(content not available — power up the displays / try again)"
                    : string.Join("\r\n", rows);
            }
            else
            {
                // SD system page — request its L:vars, let them arrive, then format.
                var rows = SdSystemRows(page);
                if (rows.Count == 0) { content = "(this SD page is not wired yet)"; }
                else
                {
                    foreach (var row in rows) sim.RequestVariable(row.var, forceUpdate: true);
                    await System.Threading.Tasks.Task.Delay(600);
                    var sb = new System.Text.StringBuilder();
                    foreach (var row in rows)
                    {
                        double? cv = sim.GetCachedVariableValue(row.var);
                        sb.AppendLine(cv.HasValue ? $"{row.label}: {row.fmt(cv.Value)}" : $"{row.label}: --");
                    }
                    content = sb.ToString().TrimEnd();
                }
            }
            _sdBoxContent = content;
            sim.RequestVariable(SdPageVar, forceUpdate: true);
        }
        catch { /* best-effort; the combo still recorded the selection */ }
    }

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";
        // Doors: INTERACTIVE POINT OPEN is a 0..1 fraction; render the state cleanly
        // (Open / Closed / mid-animation percentage) instead of "0.6".
        if (varKey.StartsWith("A32NX_MSFSBA_DOOR_", StringComparison.Ordinal))
        {
            displayText = value > 0.95 ? "Open" : value < 0.05 ? "Closed" : $"{value * 100:0}% open";
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
            double nm = value / 1852.0;   // metres -> NM (sign: + = right of track)
            displayText = Math.Abs(nm) < 0.01 ? "On track"
                : $"{Math.Abs(nm):F2} NM {(nm > 0 ? "right" : "left")}";
            return true;
        }
        if (varKey == "A32NX_RADIO_RECEIVER_LOC_DEVIATION" || varKey == "A32NX_RADIO_RECEIVER_GS_DEVIATION")
        {
            displayText = $"{value:F2} degrees";
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

        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    public override bool ProcessSimVarUpdate(string varName, double value, Accessibility.ScreenReaderAnnouncer announcer)
    {
        lastAnnouncer = announcer; // Store for when we announce

        // Doors read INTERACTIVE POINT OPEN, a 0..1 FRACTION (a half-open door is e.g.
        // 0.6, matching neither Closed(0) nor Open(1)), so announce Open/Closed once per
        // transition (>0.05 = cracked open) instead of spamming the animation.
        if (varName.StartsWith("A32NX_MSFSBA_DOOR_", StringComparison.Ordinal))
        {
            bool open = value > 0.05;
            bool? prev = _doorOpen.TryGetValue(varName, out var pv) ? pv : null;
            _doorOpen[varName] = open;
            if (prev.HasValue && prev.Value != open && _doorNames.TryGetValue(varName, out var dn))
                announcer.Announce($"{dn} {(open ? "open" : "closed")}");
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
        // On-demand gross-weight / fuel read (kilograms vars), spoken in the selected unit.
        if (_reqGw && varName == "GROSS_WEIGHT_KG")
        {
            _reqGw = false;
            var (gw, gu) = WeightUser(value);
            announcer.AnnounceImmediate($"Gross weight {gw:0} {gu}");
            return true;
        }
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
        }

        // EFIS baro (altimeter) — speak the setting on knob turn / unit change. The HPA
        // var is an ARINC429 word. Deduped to whole hPa so a steady knob doesn't repeat;
        // STD is spoken from the mode var. Returns true (the EFIS panel field still reads
        // it via the cache fallback in UpdateDisplayText).
        if (varName == "A32NX_FCU_LEFT_EIS_BARO_HPA")
        {
            double hpa = value >= 4294967296.0 ? new SimConnect.Arinc429Word(value).ValueOr(0f) : value;
            if (hpa > 0) _baroHpa = hpa;
            int mode = _baroMode < 0 ? 1 : _baroMode;
            if (mode != 0 && _baroHpa > 0)
            {
                int r = (int)System.Math.Round(_baroHpa);
                // First valid read: seed the cache SILENTLY (no first-start announce —
                // this + the mode handler below were each announcing on first detect,
                // which is the "altimeter spoken twice on start" bug). Only a genuine
                // later knob change speaks.
                if (_lastAnnouncedBaroHpa == -1) _lastAnnouncedBaroHpa = r;
                else if (r != _lastAnnouncedBaroHpa)
                {
                    _lastAnnouncedBaroHpa = r;
                    announcer.Announce(BaroPhrase(_baroHpa, mode));
                }
            }
            return true;
        }
        if (varName == "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE")
        {
            int mode = (int)System.Math.Round(value);
            bool firstMode = _baroMode < 0;   // first detect → seed silently, don't announce
            if (mode != _baroMode)
            {
                _baroMode = mode;
                if (!firstMode) announcer.Announce(BaroPhrase(_baroHpa < 0 ? 1013 : _baroHpa, mode));
            }
            return true;
        }
        // F/O EFIS baro — same logic as the captain side, prefixed "First Officer" so the
        // pilot knows which side changed (each knob announces only its own side, so this
        // is NOT chatty). Silent-seed on first detect (same first-start fix as captain).
        if (varName == "A32NX_FCU_RIGHT_EIS_BARO_HPA")
        {
            double hpa = value >= 4294967296.0 ? new SimConnect.Arinc429Word(value).ValueOr(0f) : value;
            if (hpa > 0) _baroHpaR = hpa;
            int mode = _baroModeR < 0 ? 1 : _baroModeR;
            if (mode != 0 && _baroHpaR > 0)
            {
                int r = (int)System.Math.Round(_baroHpaR);
                if (_lastAnnouncedBaroHpaR == -1) _lastAnnouncedBaroHpaR = r;
                else if (r != _lastAnnouncedBaroHpaR)
                {
                    _lastAnnouncedBaroHpaR = r;
                    announcer.Announce("First Officer " + BaroPhrase(_baroHpaR, mode));
                }
            }
            return true;
        }
        if (varName == "A32NX_FCU_EFIS_R_DISPLAY_BARO_VALUE_MODE")
        {
            int mode = (int)System.Math.Round(value);
            bool firstMode = _baroModeR < 0;
            if (mode != _baroModeR)
            {
                _baroModeR = mode;
                if (!firstMode) announcer.Announce("First Officer " + BaroPhrase(_baroHpaR < 0 ? 1013 : _baroHpaR, mode));
            }
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
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    /// <summary>
    /// Handles A32NX-specific variable setting from UI controls.
    /// Implements special validation, conversion, and multi-step logic for certain variables.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        // System Display page combo (MSFSBA-internal selector). Record the selection
        // and populate the status box — scrape the E/WD or read decoded SD-system
        // SimVars. The real A32NX SD index is read-only, so no real SD var is touched.
        if (varKey == "A32NX_MSFSBA_SD_PAGE")
        {
            int page = (int)Math.Round(value);
            simConnect.ExecuteCalculatorCode($"{page} (>L:A32NX_MSFSBA_SD_PAGE)");
            RefreshDisplayBoxAsync(page, simConnect);
            return true;
        }

        // Acknowledge / silence the MASTER WARNING / MASTER CAUTION (and its aural — the
        // repetitive "beep" / single chime). The real glareshield pushbutton is
        // PUSH_AUTOPILOT_MASTERWARN_L / _MASTERCAUT_L (a HELD momentary), NOT the
        // A32NX_MASTER_WARNING output the old "clear" wrongly wrote — so pulse 1->0
        // (press + release) via the calculator path. Selecting "Activate" fires it.
        if (varKey == "CLEAR_MASTER_WARNING")
        {
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_AUTOPILOT_MASTERWARN_L)");
                simConnect.ExecuteCalculatorCode("0 (>L:PUSH_AUTOPILOT_MASTERWARN_L)");
            }
            return true;
        }
        if (varKey == "CLEAR_MASTER_CAUTION")
        {
            if (value > 0.5)
            {
                simConnect.ExecuteCalculatorCode("1 (>L:PUSH_AUTOPILOT_MASTERCAUT_L)");
                simConnect.ExecuteCalculatorCode("0 (>L:PUSH_AUTOPILOT_MASTERCAUT_L)");
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

        // Door combos: the combo shows the live INTERACTIVE POINT OPEN state, so any
        // change means "toggle" -> fire TOGGLE_AIRCRAFT_EXIT with the exit index (1:1
        // with the door number, verified live).
        if (varKey.StartsWith("A32NX_MSFSBA_DOOR_", StringComparison.Ordinal))
        {
            if (int.TryParse(varKey.AsSpan("A32NX_MSFSBA_DOOR_".Length), out int exitIdx))
                simConnect.SendEvent("TOGGLE_AIRCRAFT_EXIT", (uint)exitIdx);
            return true;
        }
        // Rudder trim reset: fire the stock K-event the cockpit uses (the L:var alone
        // drives nothing). Only the "Activate" option (value > 0.5) fires.
        if (varKey == "A32NX_RUDDER_TRIM_RESET")
        {
            if (value > 0.5) { simConnect.ExecuteCalculatorCode("(>K:RUDDER_TRIM_RESET)"); announcer.Announce("Rudder trim reset"); }
            return true;
        }
        // Ground equipment: momentary toggles (no clean A320 state var).
        if (varKey == "A32NX_MSFSBA_JETWAY")
        {
            if (value > 0.5) simConnect.SendEvent("TOGGLE_JETWAY");
            return true;
        }
        if (varKey == "A32NX_MSFSBA_STAIRS")
        {
            if (value > 0.5) simConnect.SendEvent("TOGGLE_RAMPTRUCK");
            return true;
        }

        // Thrust-lever detent combos -> THROTTLEn_AXIS_SET_EX1 with the detent's axis
        // value (-1..1 scaled to +-16384). FBW default-style calibration (Reverse -1.0 /
        // Rev Idle -0.70 / Idle -0.44 / Climb -0.10 / Flex-MCT 0.53 / TOGA 1.0); the
        // throttle mapping snaps the lever to the detent. Idle verified live to snap to
        // the A320 idle dead-zone. Two engines on the A320.
        if (varKey == "THROTTLE_ALL_DETENT" || (varKey.StartsWith("THROTTLE_") && varKey.EndsWith("_DETENT")))
        {
            int didx = (int)Math.Round(value);
            double[] detentAxis = { -1.0, -0.70, -0.44, -0.10, 0.53, 1.0 };
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

        // Special handling for VS/FPA set - validate based on current mode
        if (varKey == "A32NX.FCU_VS_SET")
        {
            // Get variables dictionary to check TRK/FPA mode
            var variables = GetVariables();
            bool isFpaMode = false;

            // Try to get current FPA mode state from variables (would need to be in currentSimVarValues in MainForm)
            // For now, validate both ranges
            // NOTE: MainForm will need to pass currentSimVarValues or we need a different approach

            // Check if value is valid for either mode
            bool isValidVS = value >= -6000 && value <= 6000;  // VS mode range
            bool isValidFPA = value >= -9.9 && value <= 9.9;     // FPA mode range

            if (isValidVS || isValidFPA)
            {
                // Determine which mode based on value range
                isFpaMode = Math.Abs(value) <= 9.9;
                string modeText = isFpaMode ? "FPA" : "VS";

                uint valueToSend = isFpaMode ? (uint)(value * 10) : (uint)value;
                simConnect.SendEvent(varKey, valueToSend);
                announcer.Announce($"Vertical speed set to {value:F1} {modeText}");
                return true; // Handled
            }
            else
            {
                announcer.AnnounceImmediate("Invalid value. VS: -6000 to 6000, FPA: -9.9 to 9.9");
                return true; // Handled (rejected)
            }
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
        RequestFCUHeadingWithStatus(s);
        return true;
    }

    // internalSpeed: knots (100-399) OR Mach*100 (10-99). Caller does the *100.
    public bool SetFCUSpeedValue(int internalSpeed, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        s.SendEvent("A32NX.FCU_SPD_SET", (uint)internalSpeed);
        RequestFCUSpeedWithStatus(s);
        return true;
    }

    // feet: whole feet; rounded to the nearest 100 (FCU_ALT_SET requires multiples of 100).
    public bool SetFCUAltitudeValue(double feet, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        uint rounded = (uint)(Math.Round(feet / 100) * 100);
        s.SendEvent("A32NX.FCU_ALT_INCREMENT_SET", 100);
        System.Threading.Thread.Sleep(50);
        s.SendEvent("A32NX.FCU_ALT_SET", rounded);
        a.AnnounceImmediate($"Altitude set to {rounded} feet");
        return true;
    }

    // value: signed V/S (-6000..6000 fpm) OR FPA (-9.9..9.9 deg). Uses the calc-code
    // K: path (negatives overflow SendEvent's uint).
    public bool SetFCUVSValue(double value, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return false; }
        int toSend = Math.Abs(value) < 100 ? (int)(value * 100) : (int)value;
        s.ExecuteCalculatorCode($"{toSend} (>K:A32NX.FCU_VS_SET)");
        a.AnnounceImmediate($"Vertical speed set to {value}");
        return true;
    }

    // Fire a push/pull/toggle event and speak the resulting value (readback routed by
    // the same event→readout mapping the Shift+1-4/Ctrl+1-4 knob hotkeys use).
    public void FireFCUButton(string evt, SimConnect.SimConnectManager s, ScreenReaderAnnouncer a)
    {
        if (!s.IsConnected) { a.AnnounceImmediate("Not connected to simulator."); return; }
        s.SendEvent(evt);
        switch (evt)
        {
            case "A32NX.FCU_TO_AP_HDG_PUSH":
            case "A32NX.FCU_TO_AP_HDG_PULL":
            case "A32NX.FCU_TRK_FPA_TOGGLE_PUSH": RequestFCUHeadingWithStatus(s); break;
            case "A32NX.FCU_SPD_PUSH":
            case "A32NX.FCU_SPD_PULL":
            case "A32NX.FCU_SPD_MACH_TOGGLE_PUSH": RequestFCUSpeedWithStatus(s); break;
            case "A32NX.FCU_ALT_PUSH":
            case "A32NX.FCU_ALT_PULL": RequestFCUAltitudeWithStatus(s); break;
            case "A32NX.FCU_VS_PUSH":
            case "A32NX.FCU_TO_AP_VS_PULL": RequestFCUVerticalSpeedFPA(s); break;
        }
    }

    // Request the live AP/mode state vars so the Autopilot window can refresh labels.
    public void RequestAutopilotStates(SimConnect.SimConnectManager s)
    {
        if (!s.IsConnected) return;
        foreach (var v in new[] {
            "A32NX_AUTOPILOT_1_ACTIVE", "A32NX_AUTOPILOT_2_ACTIVE",
            "A32NX_FCU_LOC_MODE_ACTIVE", "A32NX_FCU_APPR_MODE_ACTIVE",
            "A32NX_FMA_EXPEDITE_MODE", "A32NX_FCU_EFIS_L_FD_ACTIVE",
            "A32NX_FCU_EFIS_R_FD_ACTIVE" })
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

    private void RequestFuelQuantityKg(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_FUEL_QUANTITY_KG;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "FUEL TOTAL QUANTITY WEIGHT", "pounds",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_FUEL_QUANTITY_KG,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting fuel quantity kg: {ex.Message}");
            }
        }
    }

    private void RequestGrossWeightKg(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                var tempDefId = SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "TOTAL WEIGHT", "pounds",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject(SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_GROSS_WEIGHT_KG,
                    tempDefId, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD.ONCE,
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting gross weight kg: {ex.Message}");
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
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)340;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_GD", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)340,
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
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)341;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_S", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)341,
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
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)342;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_F", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)342,
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
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)343;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_FAC_1_V_FE_NEXT", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)343,
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
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)344;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_VLS", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)344,
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
                var tempDefId = (SimConnect.SimConnectManager.DATA_DEFINITIONS)345;
                simConnect.ClearDataDefinition(tempDefId);
                simConnect.AddToDataDefinition(tempDefId,
                    "L:A32NX_SPEEDS_VS", "number",
                    Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE.FLOAT64, 0.0f, 0);
                simConnect.RegisterDataDefineStruct<SimConnect.SimConnectManager.SingleValue>(tempDefId);
                simConnect.RequestDataOnSimObject((SimConnect.SimConnectManager.DATA_REQUESTS)345,
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

    private void RequestECAMMessages(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_1");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_2");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_3");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_4");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_5");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_6");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_LEFT_LINE_7");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_1");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_2");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_3");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_4");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_5");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_6");
                simConnectMgr.RequestVariable("A32NX_Ewd_LOWER_RIGHT_LINE_7");
                simConnectMgr.RequestVariable("A32NX_MASTER_WARNING");
                simConnectMgr.RequestVariable("A32NX_MASTER_CAUTION");
                simConnectMgr.RequestVariable("A32NX_STALL_WARNING");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting ECAM messages: {ex.Message}");
            }
        }
    }

    private void RequestStatusMessages(SimConnect.SimConnectManager simConnectMgr)
    {
        var simConnect = simConnectMgr.SimConnectInstance;
        if (simConnectMgr.IsConnected && simConnect != null)
        {
            try
            {
                for (int i = 1; i <= 8; i++)
                {
                    simConnectMgr.RequestVariable($"A32NX_STATUS_LEFT_LINE_{i}");
                }
                for (int i = 1; i <= 8; i++)
                {
                    simConnectMgr.RequestVariable($"A32NX_STATUS_RIGHT_LINE_{i}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting status messages: {ex.Message}");
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
