using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for the HorizonSim Boeing 787-9 (GEnx).
/// Uses Working Title avionics (WT Boeing SDK) — no proprietary SDK.
/// All panel state via L: variables; AP via standard K: SimConnect events;
/// FMC keyboard via H: events through MobiFlight WASM.
/// Phase 1: panels, MCP dialogs, hotkeys. Phase 2: CDU screen reading via JS bridge.
/// </summary>
public class HorizonSim787Definition : BaseAircraftDefinition
{
    // Bridge server reference — set by MainForm after the HS787 bridge starts.
    // Used to fire alt INTV from inside Coherent GT when the FMC bridge is connected.
    public EFBBridgeServer? BridgeServer { get; set; }

    private bool _previousAppHold = false;
    private bool _previousGSActive = false;
    private bool _previousAPMaster = false;
    private int  _previousATStatus = 0;
    private int  _previousSpeedbrakeState = 0; // 0=down, 1=armed, 2=deployed
    private bool _previousExtPwr1On = false;
    private bool _previousExtPwr2On = false;
    private bool _previousExecActive = false;
    private bool _previousFLCH = false;
    private bool _previousALTHold = false;
    private bool _previousLNAV = false;
    private bool _previousVNAV = false;
    private bool _previousHDGHold = false;
    private bool _previousVSActive = false;

    // System-setup announcement state (−1 = unset, suppresses first-poll announcement)
    private int  _previousApuKnob     = -1;
    private int  _previousEngState1   = -1;
    private int  _previousEngState2   = -1;
    // Cached autopilot window — created on first FCUSetAutopilot press, focused on subsequent presses.
    private Forms.HS787.HS787AutopilotWindow? _autopilotWindow;
    private bool _previousPackL       = false;
    private bool _previousPackR       = false;
    private bool _previousHydDemandL  = false;
    private bool _previousHydDemandR  = false;
    private int  _previousEmerLights  = -1;
    private int  _previousSeatbelts   = -1;

    // IRS
    private int  _previousIrsKnob1    = -1;
    private int  _previousIrsKnob2    = -1;
    private int  _previousIrsAligned1 = -1;
    private int  _previousIrsAligned2 = -1;

    // Anti-ice, signs, lights, landing — -1 suppresses first-poll announcement
    private int  _previousAntiIceEng1  = -1;
    private int  _previousAntiIceEng2  = -1;
    private int  _previousAntiIceWing  = -1;
    private int  _previousNoSmoking    = -1;
    private int  _previousParkBrake    = -1;
    private int  _previousFlapsHandle  = -1;
    private int  _previousGearHandleI  = -1;
    private int  _previousLightBeacon  = -1;
    private int  _previousLightStrobe  = -1;
    private int  _previousLightNav     = -1;
    private int  _previousLightLanding = -1;

    // Altimeter change tracking — NaN suppresses the first-poll announcement
    private double _lastAnnouncedAltimeter = double.NaN;

    public override string AircraftName => "HorizonSim 787-9";
    public override string AircraftCode => "HS_787";

    // 787 MCP uses direct-set dialogs (same as PMDG 777)
    public override FCUControlType GetAltitudeControlType()       => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType()        => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType()          => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType()  => FCUControlType.SetValue;

    // No button state mapping needed — 787 uses standard toggle logic
    public override Dictionary<string, string> GetButtonStateMapping() =>
        new Dictionary<string, string>();

    // No additional display-only variables
    public override Dictionary<string, List<string>> GetPanelDisplayVariables() =>
        new Dictionary<string, List<string>>();

    // =========================================================================
    // Panel Structure
    // =========================================================================

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "Electrical",
                "IRS",
                "Hydraulics",
                "Fuel",
                "Air Conditioning",
                "Anti-Ice",
                "Signs",
                "Flight Controls",
                "Engines"
            },
            ["Glareshield"] = new List<string>
            {
                "EFIS",
                "MCP",
                "FMC Status"
            },
            ["Pedestal"] = new List<string>
            {
                "Transponder",
                "Landing",
                "Lighting",
                "Options"
            },
            ["Ground Services"] = new List<string>
            {
                "Doors",
                "Services"
            }
        };
    }

    // =========================================================================
    // Variables
    // =========================================================================

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // -----------------------------------------------------------------
            // OVERHEAD — Electrical
            // -----------------------------------------------------------------

            ["HS787_ExtPwr1"] = new SimConnect.SimVarDefinition
            {
                Name = "EXT_PWR_COMMANDED:1",
                DisplayName = "Ext Power 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_ExtPwr2"] = new SimConnect.SimVarDefinition
            {
                Name = "EXT_PWR_COMMANDED:2",
                DisplayName = "Ext Power 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_ExtPwrOn1"] = new SimConnect.SimVarDefinition
            {
                Name = "EXTERNAL POWER ON:1",
                DisplayName = "Ext Power 1 Active",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "External Power 1 Off",
                    [1] = "External Power 1 On"
                }
            },

            ["HS787_ExtPwrOn2"] = new SimConnect.SimVarDefinition
            {
                Name = "EXTERNAL POWER ON:2",
                DisplayName = "Ext Power 2 Active",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "External Power 2 Off",
                    [1] = "External Power 2 On"
                }
            },

            ["HS787_ApuGen1"] = new SimConnect.SimVarDefinition
            {
                Name = "APU GENERATOR SWITCH:1",
                DisplayName = "APU Generator 1",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_ApuGen2"] = new SimConnect.SimVarDefinition
            {
                Name = "APU GENERATOR SWITCH:2",
                DisplayName = "APU Generator 2",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_APU_Knob"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_APU_StarterKnob_Pos",
                DisplayName = "APU Selector",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On",
                    [2] = "Start"
                }
            },

            ["HS787_EmerLights"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_EMER_LIGHTS_ARMED",
                DisplayName = "Emergency Lights",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Armed",
                    [2] = "On"
                }
            },

            ["HS787_UtilityCabin"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Utility_Cabin",
                DisplayName = "Utility Power Cabin",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_UtilityIfe"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Utility_Ife",
                DisplayName = "Utility Power IFE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_BatSwitch1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELECTRICAL MASTER BATTERY:1",
                DisplayName = "Battery 1",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_BatSwitch2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELECTRICAL MASTER BATTERY:2",
                DisplayName = "Battery 2",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_Gen1"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG GENERATOR SWITCH:1",
                DisplayName = "Generator 1",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_Gen2"] = new SimConnect.SimVarDefinition
            {
                Name = "GENERAL ENG GENERATOR SWITCH:2",
                DisplayName = "Generator 2",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // -----------------------------------------------------------------
            // OVERHEAD — IRS
            // B787_IRS_Knob_State:N: 0=Off, 1=On(NAV) — confirmed from MFD789.GE.js IrsKnobState enum.
            // WT_IRS_POS_SET_N: written true when alignment completes and GPS position is accepted.
            // Both are Continuous+Announced so knob changes and alignment are called out.
            // -----------------------------------------------------------------

            ["HS787_IRS_Knob1"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_IRS_Knob_State:1",
                DisplayName = "IRS Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_IRS_Knob2"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_IRS_Knob_State:2",
                DisplayName = "IRS Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // WT_IRS_POS_SET_N becomes true when WT Boeing IRS alignment completes and
            // GPS position has been accepted. False during alignment, true when nav-ready.
            ["HS787_IRS_Aligned1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_IRS_POS_SET_1",
                DisplayName = "IRS Left Aligned",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Aligning",
                    [1] = "Aligned"
                }
            },

            ["HS787_IRS_Aligned2"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_IRS_POS_SET_2",
                DisplayName = "IRS Right Aligned",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Aligning",
                    [1] = "Aligned"
                }
            },

            // -----------------------------------------------------------------
            // OVERHEAD — Hydraulics
            // -----------------------------------------------------------------

            ["HS787_HydDemandLeft"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_HYDRAULICS_DEMAND_LEFT",
                DisplayName = "Hydraulic Demand Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_HydDemandRight"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_HYDRAULICS_DEMAND_RIGHT",
                DisplayName = "Hydraulic Demand Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_HydC1"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_HYDRAULICS_C1",
                DisplayName = "Hydraulic Center 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_HydC2"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_HYDRAULICS_C2",
                DisplayName = "Hydraulic Center 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // Engine-driven hydraulic pumps (HYDRAULIC SWITCH:N returns On when engine pump is selected)
            ["HS787_HydEngL"] = new SimConnect.SimVarDefinition
            {
                Name = "HYDRAULIC SWITCH:1",
                DisplayName = "Hydraulic Engine L",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_HydEngR"] = new SimConnect.SimVarDefinition
            {
                Name = "HYDRAULIC SWITCH:2",
                DisplayName = "Hydraulic Engine R",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // -----------------------------------------------------------------
            // OVERHEAD — Fuel
            // -----------------------------------------------------------------

            ["HS787_FuelBalance"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_FuelBalance_Switch_On",
                DisplayName = "Fuel Balance",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_FuelBalanceActive"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_FuelBalance_Active",
                DisplayName = "Fuel Balance Active",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Inactive",
                    [1] = "Active"
                }
            },

            ["HS787_FuelBalanceFault"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_FuelBalance_Fault",
                DisplayName = "Fuel Balance Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Normal",
                    [1] = "Fuel Balance Fault"
                }
            },

            // -----------------------------------------------------------------
            // OVERHEAD — Air Conditioning
            // -----------------------------------------------------------------

            ["HS787_PackL"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Packs_L_Switch",
                DisplayName = "Pack Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto"
                }
            },

            ["HS787_PackR"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Packs_R_Switch",
                DisplayName = "Pack Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto"
                }
            },

            ["HS787_TrimAirL"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_TrimAir_L",
                DisplayName = "Trim Air Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_TrimAirR"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_TrimAir_R",
                DisplayName = "Trim Air Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_RecircUpper"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_RecircUpper",
                DisplayName = "Upper Recirc Fan",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_RecircLower"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_FansLower",
                DisplayName = "Lower Recirc Fan",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // Temperature target LVars (~24°C cabin/flight deck, 16°C cargo) — numeric, not on/off.
            // Not shown in panels; retained here in case future hotkey readouts need them.
            ["HS787_HeatCabin"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Heat_Cabin",
                DisplayName = "Cabin Temp Target",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },

            ["HS787_HeatCargo"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Heat_Cargo",
                DisplayName = "Cargo Temp Target",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },

            ["HS787_HeatFltDeck"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Heat_FltDeck",
                DisplayName = "Flight Deck Temp Target",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },

            // -----------------------------------------------------------------
            // OVERHEAD — Anti-Ice
            // -----------------------------------------------------------------

            ["HS787_WshldDeice1"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_DeiceWindshield:1",
                DisplayName = "Windshield Deice 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_WshldDeice2"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_DeiceWindshield:2",
                DisplayName = "Windshield Deice 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_WshldDeice3"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_DeiceWindshield:3",
                DisplayName = "Windshield Deice 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_WshldDeice4"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_DeiceWindshield:4",
                DisplayName = "Windshield Deice 4",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // WT Boeing 787 uses dedicated LVars for anti-ice knob position.
            // AntiIceKnobState: 0=Off, 1=Auto, 2=On (confirmed from MFD789.GE.js source).
            ["HS787_AntiIceEng1"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Engine_AntiIce_Knob_State:1",
                DisplayName = "Engine 1 Anti-Ice",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto",
                    [2] = "On"
                }
            },

            ["HS787_AntiIceEng2"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Engine_AntiIce_Knob_State:2",
                DisplayName = "Engine 2 Anti-Ice",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto",
                    [2] = "On"
                }
            },

            ["HS787_AntiIceWing"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Wing_AntiIce_Knob_State",
                DisplayName = "Wing Anti-Ice",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto",
                    [2] = "On"
                }
            },

            // -----------------------------------------------------------------
            // OVERHEAD — Signs
            // -----------------------------------------------------------------

            ["HS787_Seatbelts"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_SEAT_BELTS_MODE",
                DisplayName = "Seat Belts Sign",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto",
                    [2] = "On"
                }
            },

            ["HS787_NoSmoking"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_NO_SMOKING_MODE",
                DisplayName = "No Smoking Sign",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto",
                    [2] = "On"
                }
            },

            // -----------------------------------------------------------------
            // OVERHEAD — Flight Controls
            // -----------------------------------------------------------------

            ["HS787_AltnFlapsArmed"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_ALTN_FLAPS_ARMED",
                DisplayName = "Alternate Flaps Armed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Armed"
                }
            },

            ["HS787_AltnFlapsSelector"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_ALTN_FLAPS_SELECTOR",
                DisplayName = "Alternate Flaps Selector",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "1",
                    [2] = "5",
                    [3] = "15",
                    [4] = "20"
                }
            },

            // -----------------------------------------------------------------
            // OVERHEAD — Engines (FADEC)
            // -----------------------------------------------------------------

            ["HS787_EngStartState1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_ENG_START_STATE_1",
                DisplayName = "Engine 1 Start State",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Stopped",
                    [1] = "Auto Start",
                    [2] = "Running"
                }
            },

            ["HS787_EngStartState2"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_ENG_START_STATE_2",
                DisplayName = "Engine 2 Start State",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Stopped",
                    [1] = "Auto Start",
                    [2] = "Running"
                }
            },

            // -----------------------------------------------------------------
            // GLARESHIELD — EFIS
            // -----------------------------------------------------------------

            ["HS787_BaroSelector"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Baro_Selector_HPA_1",
                DisplayName = "Barometer Selector",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "inHg",
                    [1] = "hPa"
                }
            },

            ["HS787_MinsMode"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Mins_Selector_Baro",
                DisplayName = "Minimums Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Radio",
                    [1] = "Baro"
                }
            },

            ["HS787_FPVMode"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_FPV_MODE_ACTIVE",
                DisplayName = "FPV Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            // -----------------------------------------------------------------
            // GLARESHIELD — MCP (announced continuous for state monitoring)
            // -----------------------------------------------------------------

            ["HS787_FPAMode"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_FPA_MODE_ACTIVE",
                DisplayName = "FPA Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "V/S Mode",
                    [1] = "FPA Mode"
                }
            },

            ["HS787_TRKMode"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_TRK_MODE_ACTIVE",
                DisplayName = "TRK Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "HDG Mode",
                    [1] = "TRK Mode"
                }
            },

            ["HS787_APMaster"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT MASTER",
                DisplayName = "Autopilot Master",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Autopilot 1 Off",
                    [1] = "Autopilot 1 On"
                }
            },

            ["HS787_ATStatus"] = new SimConnect.SimVarDefinition
            {
                Name = "AS01B_AUTO_THROTTLE_ARM_STATE",
                DisplayName = "Autothrottle Arm State",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Autothrottle Disarmed",
                    [1] = "Autothrottle Armed"
                }
            },

            ["HS787_APDisconnected"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_Boeing_Autopilot_Disconnected",
                DisplayName = "AP Disconnected",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "",   // suppress "connected" announcements
                    [1] = "Autopilot Disconnected"
                }
            },

            // -----------------------------------------------------------------
            // GLARESHIELD — MCP cached values (Continuous, not announced)
            // These stay in cache so hotkey handlers can read instantly.
            // -----------------------------------------------------------------

            // All of these are Continuous+IsAnnounced=true so they enter the cache and
            // are available to hotkey readouts and dialog toggle state queries.
            // ProcessSimVarUpdate returns true for all of them to suppress announcements.

            ["HS787_MCP_IAS"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT AIRSPEED HOLD VAR:1",
                DisplayName = "MCP IAS",
                Type = SimConnect.SimVarType.SimVar,
                Units = "knots",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_MCP_Mach"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT MACH HOLD VAR:1",
                DisplayName = "MCP Mach",
                Type = SimConnect.SimVarType.SimVar,
                Units = "mach",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_MCP_IsMach"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_AirSpeedIsInMach",
                DisplayName = "Speed Mode Mach",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_MCP_SpdManual"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_SpeedIsManuallySet",
                DisplayName = "Speed Manually Set",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_MCP_Heading"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT HEADING LOCK DIR:1",
                DisplayName = "MCP Heading",
                Type = SimConnect.SimVarType.SimVar,
                Units = "degrees",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_MCP_Altitude"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT ALTITUDE LOCK VAR:1",
                DisplayName = "MCP Altitude",
                Type = SimConnect.SimVarType.SimVar,
                Units = "feet",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_MCP_VS"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT VERTICAL HOLD VAR:1",
                DisplayName = "MCP VS",
                Type = SimConnect.SimVarType.SimVar,
                Units = "feet per minute",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_MCP_FPA"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_AP_FPA_Target:1",
                DisplayName = "MCP FPA Target",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_VNAV"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_VNAVButtonValue",
                DisplayName = "VNAV",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // WT Boeing uses L:AP_LNAV_ARMED for GPSS/LNAV state.
            // AUTOPILOT NAV1 LOCK is set by both the LNAV and LOC directors, so it's unreliable here.
            ["HS787_LNAV"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_LNAV_ARMED",
                DisplayName = "LNAV",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_FLCH"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT FLIGHT LEVEL CHANGE",
                DisplayName = "Level Change",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_VS_Active"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT VERTICAL HOLD",
                DisplayName = "VS Active",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_ALTHold"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT ALTITUDE LOCK",
                DisplayName = "ALT Hold",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_HDGHold"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT HEADING LOCK",
                DisplayName = "HDG Hold",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // WT Boeing intercepts AP_APR_HOLD and writes L:AP_APR_ARMED (not AUTOPILOT APPROACH HOLD).
            ["HS787_APP"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_APR_ARMED",
                DisplayName = "Approach",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // Approach mode progression: Armed → LOC captured → GS captured
            // AP_APR_ARMED = approach armed; GLIDESLOPE ARM = armed (pre-intercept);
            // LOC HOLD = actively tracking localizer; GLIDESLOPE ACTIVE = actively tracking GS.
            ["HS787_GS_Armed"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT GLIDESLOPE ARM",
                DisplayName = "GS Armed",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // WT Boeing writes L:AP_LOC_ARMED (=1 when LOC is armed/active but approach not yet full).
            // AUTOPILOT LOC HOLD (SimVar) is never written by WT.
            ["HS787_LOC"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_LOC_ARMED",
                DisplayName = "LOC Active",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_GS_Active"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT GLIDESLOPE ACTIVE",
                DisplayName = "GS Active",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // -----------------------------------------------------------------
            // GLARESHIELD — FMC Status
            // -----------------------------------------------------------------

            ["HS787_EXECActive"] = new SimConnect.SimVarDefinition
            {
                Name = "FMC_EXEC_ACTIVE",
                DisplayName = "EXEC Active",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "EXEC Active"
                }
            },

            ["HS787_TOGA"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_TOGA_ACTIVE",
                DisplayName = "TOGA Active",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "TOGA Active"
                }
            },

            ["HS787_FmsPhase"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_Boeing_Fms_Operating_Phase",
                DisplayName = "FMS Phase",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Preflight",
                    [1] = "Takeoff",
                    [2] = "Climb",
                    [3] = "Cruise",
                    [4] = "Descent",
                    [5] = "Approach",
                    [6] = "Complete"
                }
            },

            // -----------------------------------------------------------------
            // PEDESTAL — Transponder
            // -----------------------------------------------------------------

            ["HS787_TransponderMode"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Transponder_Mode",
                DisplayName = "Transponder Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Standby",
                    [1] = "TA Only",
                    [2] = "TA/RA"
                }
            },

            // -----------------------------------------------------------------
            // PEDESTAL — Speedbrake
            // WT_SPEEDBRAKE_LEVER_POS is a continuous axis: 0-410=down, 411-1230=armed, >1230=deployed.
            // Announcements are handled via ProcessSimVarUpdate threshold logic.
            // -----------------------------------------------------------------

            ["HS787_Speedbrake"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_SPEEDBRAKE_LEVER_POS",
                DisplayName = "Speedbrake Lever",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // -----------------------------------------------------------------
            // PEDESTAL — Options
            // -----------------------------------------------------------------

            ["HS787_SATCOM"] = new SimConnect.SimVarDefinition
            {
                Name = "B789_SATCOM_ENABLED",
                DisplayName = "SATCOM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Disabled",
                    [1] = "Enabled"
                }
            },

            ["HS787_VBar"] = new SimConnect.SimVarDefinition
            {
                Name = "B789_VBAR_ENABLED",
                DisplayName = "V-Bar",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Disabled",
                    [1] = "Enabled"
                }
            },

            // -----------------------------------------------------------------
            // PEDESTAL — Landing
            // -----------------------------------------------------------------

            // AUTOBRAKE CONTROL SWITCH POSITION: 0=Off, 1=RTO, 2=1, 3=2, 4=3, 5=MAX
            ["HS787_Autobrake"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOBRAKE CONTROL SWITCH POSITION",
                DisplayName = "Autobrakes",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "RTO",
                    [2] = "1",
                    [3] = "2",
                    [4] = "3",
                    [5] = "MAX"
                }
            },

            ["HS787_ParkBrake"] = new SimConnect.SimVarDefinition
            {
                Name = "BRAKE PARKING INDICATOR",
                DisplayName = "Parking Brake",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Released",
                    [1] = "Set"
                }
            },

            // -----------------------------------------------------------------
            // MCP — Alt INTV: no state LVar exists (unlike speed, the WT Boeing altitude
            // intervention system delegates entirely to VNavManager with no LVar write).
            // HS787_AltManual is kept as a dummy continuous poll so the cache entry exists
            // and the dialog toggle can be displayed; it will always read 0.
            // -----------------------------------------------------------------

            ["HS787_AltManual"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_AltitudeIsManuallySet",
                DisplayName = "Alt Manually Set",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // -----------------------------------------------------------------
            // Cached standard SimVars for hotkey readouts (not shown in panels)
            // All IsAnnounced=true so they're continuously polled into cache;
            // ProcessSimVarUpdate suppresses the unwanted announcements.
            // -----------------------------------------------------------------

            ["HS787_FlapsHandle"] = new SimConnect.SimVarDefinition
            {
                Name = "FLAPS HANDLE INDEX",
                DisplayName = "Flaps Handle",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Up",
                    [1] = "1",
                    [2] = "5",
                    [3] = "10",
                    [4] = "15",
                    [5] = "17",
                    [6] = "18",
                    [7] = "20",
                    [8] = "25",
                    [9] = "30"
                }
            },

            ["HS787_GearHandle"] = new SimConnect.SimVarDefinition
            {
                Name = "GEAR HANDLE POSITION",
                DisplayName = "Gear Handle",
                Type = SimConnect.SimVarType.SimVar,
                Units = "bool",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Up",
                    [1] = "Down"
                }
            },

            ["HS787_Altimeter"] = new SimConnect.SimVarDefinition
            {
                Name = "KOHLSMAN SETTING HG",
                DisplayName = "Altimeter Setting",
                Type = SimConnect.SimVarType.SimVar,
                Units = "inHg",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_FuelLH"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL TANK LEFT MAIN QUANTITY",
                DisplayName = "Fuel Left Main",
                Type = SimConnect.SimVarType.SimVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_FuelRH"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL TANK RIGHT MAIN QUANTITY",
                DisplayName = "Fuel Right Main",
                Type = SimConnect.SimVarType.SimVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_FuelCtr"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL TANK CENTER QUANTITY",
                DisplayName = "Fuel Center",
                Type = SimConnect.SimVarType.SimVar,
                Units = "gallons",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_FuelWtPerGal"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL WEIGHT PER GALLON",
                DisplayName = "Fuel Weight Per Gallon",
                Type = SimConnect.SimVarType.SimVar,
                Units = "pounds",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_DistDest"] = new SimConnect.SimVarDefinition
            {
                Name = "GPS WP DISTANCE",
                DisplayName = "Distance to Destination",
                Type = SimConnect.SimVarType.SimVar,
                Units = "meters",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_GroundSpeed"] = new SimConnect.SimVarDefinition
            {
                Name = "GROUND VELOCITY",
                DisplayName = "Ground Speed",
                Type = SimConnect.SimVarType.SimVar,
                Units = "knots",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["HS787_EteDest"] = new SimConnect.SimVarDefinition
            {
                Name = "GPS ETE",
                DisplayName = "ETE to Destination",
                Type = SimConnect.SimVarType.SimVar,
                Units = "seconds",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // WT Boeing 787 FMS top-of-descent distance in meters (type Meters per WTAP VNavVars).
            // Converted to NM in the ReadDistanceToTOD handler.
            ["HS787_DistTOD"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Distance_To_TOD",
                DisplayName = "Distance to TOD",
                Type = SimConnect.SimVarType.LVar,
                Units = "meters",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // -----------------------------------------------------------------
            // PEDESTAL — Lighting
            // Standard MSFS SimVars. Primary lights (beacon/strobe/nav/landing) are
            // Continuous+Announced so changes are called out during flight.
            // Secondary lights (taxi/logo/wing) are OnRequest for panel display only.
            // -----------------------------------------------------------------

            ["HS787_LightBeacon"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT BEACON",
                DisplayName = "Beacon",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_LightStrobe"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT STROBE",
                DisplayName = "Strobe",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_LightNav"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT NAV",
                DisplayName = "Nav Lights",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_LightLanding"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT LANDING",
                DisplayName = "Landing Lights",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_LightTaxi"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT TAXI",
                DisplayName = "Taxi Light",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_LightLogo"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT LOGO",
                DisplayName = "Logo Light",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // LIGHT WING = runway turnoff lights on the 787
            ["HS787_LightWing"] = new SimConnect.SimVarDefinition
            {
                Name = "LIGHT WING",
                DisplayName = "Runway Turnoff",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // -----------------------------------------------------------------
            // GROUND SERVICES — Doors (INTERACTIVE POINT OPEN:N, 1-based index)
            // HS787 uses interactive points. The EFB confirms 1-based indexing:
            // Entry doors: 1=1L, 2=1R, 3=2L, 4=2R, 5=3L, 6=3R, 7=4L, 8=4R
            // Cargo doors: 9=Fwd, 10=Aft
            // Toggled via K:TOGGLE_AIRCRAFT_EXIT with the same 1-based index.
            // SimVar returns 0 (closed) or 100 (fully open) as a percent value.
            // -----------------------------------------------------------------

            ["HS787_Door_1L"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:1",
                DisplayName = "Door 1L (Fwd Left)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_1R"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:2",
                DisplayName = "Door 1R (Fwd Right)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_2L"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:3",
                DisplayName = "Door 2L (Mid Left)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_2R"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:4",
                DisplayName = "Door 2R (Mid Right)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_3L"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:5",
                DisplayName = "Door 3L (Rear Left)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_3R"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:6",
                DisplayName = "Door 3R (Rear Right)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_4L"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:7",
                DisplayName = "Door 4L (Far Rear Left)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_4R"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:8",
                DisplayName = "Door 4R (Far Rear Right)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_FwdCargo"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:9",
                DisplayName = "Fwd Cargo Door",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_AftCargo"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:10",
                DisplayName = "Aft Cargo Door",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_RefuelDoor"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:11",
                DisplayName = "Refuel Panel Door",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_GPUPipe"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:12",
                DisplayName = "GPU Connection",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Disconnected",
                    [100] = "Connected"
                }
            }
        };
    }

    // =========================================================================
    // Panel Controls
    // =========================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            // --- Overhead ---
            ["Electrical"] = new List<string>
            {
                "HS787_BatSwitch1",
                "HS787_BatSwitch2",
                "HS787_ExtPwr1",
                "HS787_ExtPwr2",
                "HS787_APU_Knob",
                "HS787_ApuGen1",
                "HS787_ApuGen2",
                "HS787_Gen1",
                "HS787_Gen2",
                "HS787_EmerLights",
                "HS787_UtilityCabin",
                "HS787_UtilityIfe"
            },
            ["IRS"] = new List<string>
            {
                "HS787_IRS_Knob1",
                "HS787_IRS_Knob2",
                "HS787_IRS_Aligned1",
                "HS787_IRS_Aligned2"
            },
            ["Hydraulics"] = new List<string>
            {
                "HS787_HydEngL",
                "HS787_HydEngR",
                "HS787_HydDemandLeft",
                "HS787_HydDemandRight",
                "HS787_HydC1",
                "HS787_HydC2"
            },
            ["Fuel"] = new List<string>
            {
                "HS787_FuelBalance",
                "HS787_FuelBalanceActive",
                "HS787_FuelBalanceFault"
            },
            ["Air Conditioning"] = new List<string>
            {
                "HS787_PackL",
                "HS787_PackR",
                "HS787_TrimAirL",
                "HS787_TrimAirR",
                "HS787_RecircUpper",
                "HS787_RecircLower"
            },
            ["Anti-Ice"] = new List<string>
            {
                "HS787_AntiIceEng1",
                "HS787_AntiIceEng2",
                "HS787_AntiIceWing",
                "HS787_WshldDeice1",
                "HS787_WshldDeice2",
                "HS787_WshldDeice3",
                "HS787_WshldDeice4"
            },
            ["Signs"] = new List<string>
            {
                "HS787_Seatbelts",
                "HS787_NoSmoking"
            },
            ["Flight Controls"] = new List<string>
            {
                "HS787_AltnFlapsArmed",
                "HS787_AltnFlapsSelector"
            },
            ["Engines"] = new List<string>
            {
                "HS787_EngStartState1",
                "HS787_EngStartState2"
            },

            // --- Glareshield ---
            ["EFIS"] = new List<string>
            {
                "HS787_BaroSelector",
                "HS787_MinsMode",
                "HS787_FPVMode"
            },
            ["MCP"] = new List<string>
            {
                "HS787_APMaster",
                "HS787_ATStatus",
                "HS787_FPAMode",
                "HS787_TRKMode",
                "HS787_VNAV"
            },
            ["FMC Status"] = new List<string>
            {
                "HS787_EXECActive",
                "HS787_TOGA",
                "HS787_FmsPhase"
            },

            // --- Pedestal ---
            ["Transponder"] = new List<string>
            {
                "HS787_TransponderMode"
            },
            ["Landing"] = new List<string>
            {
                "HS787_ParkBrake",
                "HS787_Autobrake",
                "HS787_FlapsHandle",
                "HS787_GearHandle"
            },
            ["Lighting"] = new List<string>
            {
                "HS787_LightBeacon",
                "HS787_LightStrobe",
                "HS787_LightNav",
                "HS787_LightLanding",
                "HS787_LightTaxi",
                "HS787_LightLogo",
                "HS787_LightWing"
            },
            ["Options"] = new List<string>
            {
                "HS787_SATCOM",
                "HS787_VBar"
            },

            // --- Ground Services ---
            ["Doors"] = new List<string>
            {
                "HS787_Door_1L",
                "HS787_Door_1R",
                "HS787_Door_2L",
                "HS787_Door_2R",
                "HS787_Door_3L",
                "HS787_Door_3R",
                "HS787_Door_4L",
                "HS787_Door_4R",
                "HS787_Door_FwdCargo",
                "HS787_Door_AftCargo"
            },
            ["Services"] = new List<string>
            {
                "HS787_RefuelDoor",
                "HS787_GPUPipe"
            }
        };
    }

    // =========================================================================
    // Hotkey Handling
    // =========================================================================

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm,
        HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // ------------------------------------------------------------------
            // MCP Readouts
            // ------------------------------------------------------------------

            case HotkeyAction.ReadHeading:
            {
                double? hdg = simConnect.GetCachedVariableValue("HS787_MCP_Heading");
                if (hdg == null)
                {
                    announcer.AnnounceImmediate("Heading not available");
                    return true;
                }
                bool lnavOn = (simConnect.GetCachedVariableValue("HS787_LNAV") ?? 0) > 0;
                bool hdgHold = (simConnect.GetCachedVariableValue("HS787_HDGHold") ?? 0) > 0;
                bool trkMode = (simConnect.GetCachedVariableValue("HS787_TRKMode") ?? 0) > 0;
                string mode = lnavOn ? "LNAV" : hdgHold ? "HDG Hold" : trkMode ? "TRK" : "HDG";
                announcer.AnnounceImmediate($"{(trkMode ? "Track" : "Heading")} {(int)hdg.Value}, {mode}");
                return true;
            }

            case HotkeyAction.ReadSpeed:
            {
                bool isMach = (simConnect.GetCachedVariableValue("HS787_MCP_IsMach") ?? 0) > 0;
                bool spdManual = (simConnect.GetCachedVariableValue("HS787_MCP_SpdManual") ?? 0) > 0;
                bool flchOn = (simConnect.GetCachedVariableValue("HS787_FLCH") ?? 0) > 0;

                if (!spdManual)
                {
                    string mode = flchOn ? "FLCH" : "FMC speed";
                    announcer.AnnounceImmediate($"Speed managed by {mode}");
                    return true;
                }

                if (isMach)
                {
                    double? mach = simConnect.GetCachedVariableValue("HS787_MCP_Mach");
                    string machStr = mach != null ? $"Mach {mach.Value:0.00}" : "Mach unavailable";
                    string mode = flchOn ? " FLCH" : "";
                    announcer.AnnounceImmediate($"{machStr}{mode}");
                }
                else
                {
                    double? ias = simConnect.GetCachedVariableValue("HS787_MCP_IAS");
                    string iasStr = ias != null ? $"{(int)ias.Value} knots" : "Speed unavailable";
                    string mode = flchOn ? " FLCH" : "";
                    announcer.AnnounceImmediate($"{iasStr}{mode}");
                }
                return true;
            }

            case HotkeyAction.ReadAltitude:
            {
                double? alt = simConnect.GetCachedVariableValue("HS787_MCP_Altitude");
                if (alt == null)
                {
                    announcer.AnnounceImmediate("Altitude not available");
                    return true;
                }
                bool vnavOn = (simConnect.GetCachedVariableValue("HS787_VNAV") ?? 0) > 0;
                bool altHold = (simConnect.GetCachedVariableValue("HS787_ALTHold") ?? 0) > 0;
                bool flchOn = (simConnect.GetCachedVariableValue("HS787_FLCH") ?? 0) > 0;
                string mode = vnavOn ? " VNAV" : altHold ? " ALT Hold" : flchOn ? " FLCH" : "";
                announcer.AnnounceImmediate($"{(int)alt.Value} feet{mode}");
                return true;
            }

            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                bool isFPA = (simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0) > 0;
                bool vsActive = (simConnect.GetCachedVariableValue("HS787_VS_Active") ?? 0) > 0;
                bool appOn = (simConnect.GetCachedVariableValue("HS787_APP") ?? 0) > 0;

                if (appOn)
                {
                    bool gsActive  = (simConnect.GetCachedVariableValue("HS787_GS_Active") ?? 0) > 0;
                    bool locActive = (simConnect.GetCachedVariableValue("HS787_LOC")       ?? 0) > 0;
                    string phase   = gsActive  ? "Glideslope active"
                                   : locActive ? "Localizer active"
                                   : "Approach armed";
                    announcer.AnnounceImmediate(phase);
                    return true;
                }

                if (!vsActive && !isFPA)
                {
                    announcer.AnnounceImmediate("V/S not engaged");
                    return true;
                }

                if (isFPA)
                {
                    double? fpa = simConnect.GetCachedVariableValue("HS787_MCP_FPA");
                    if (fpa != null)
                        announcer.AnnounceImmediate($"FPA {fpa.Value:+0.0;-0.0} degrees");
                    else
                        announcer.AnnounceImmediate("FPA not available");
                }
                else
                {
                    double? vs = simConnect.GetCachedVariableValue("HS787_MCP_VS");
                    if (vs != null)
                        announcer.AnnounceImmediate($"V/S {(int)vs.Value} feet per minute");
                    else
                        announcer.AnnounceImmediate("V/S not available");
                }
                return true;
            }

            // ------------------------------------------------------------------
            // MCP Set Dialogs
            // ------------------------------------------------------------------

            case HotkeyAction.FCUSetHeading:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowHeadingDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetSpeed:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowSpeedDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetAltitude:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowAltitudeDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetVS:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowVSDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetBaro:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowBaroDialog(simConnect, announcer, parentForm);
                return true;
            }

            // ------------------------------------------------------------------
            // Aircraft State Readouts
            // ------------------------------------------------------------------

            case HotkeyAction.ReadFlaps:
            {
                double? idx = simConnect.GetCachedVariableValue("HS787_FlapsHandle");
                if (idx == null)
                {
                    announcer.AnnounceImmediate("Flaps not available");
                    return true;
                }
                string position = (int)idx.Value switch
                {
                    0 => "Up",
                    1 => "1",
                    2 => "5",
                    3 => "10",
                    4 => "15",
                    5 => "17",
                    6 => "18",
                    7 => "20",
                    8 => "25",
                    9 => "30",
                    _ => idx.Value.ToString("F0")
                };
                announcer.AnnounceImmediate($"Flaps {position}");
                return true;
            }

            case HotkeyAction.ReadGear:
            {
                double? gear = simConnect.GetCachedVariableValue("HS787_GearHandle");
                if (gear == null)
                {
                    announcer.AnnounceImmediate("Gear not available");
                    return true;
                }
                string gearState = gear.Value > 0.5 ? "Down" : "Up";
                announcer.AnnounceImmediate($"Gear {gearState}");
                return true;
            }

            case HotkeyAction.ReadFuelQuantity:
            {
                double? lh  = simConnect.GetCachedVariableValue("HS787_FuelLH");
                double? rh  = simConnect.GetCachedVariableValue("HS787_FuelRH");
                double? ctr = simConnect.GetCachedVariableValue("HS787_FuelCtr");
                double? wtPerGal = simConnect.GetCachedVariableValue("HS787_FuelWtPerGal");

                if (lh == null || rh == null || ctr == null || wtPerGal == null)
                {
                    announcer.AnnounceImmediate("Fuel quantity not available");
                    return true;
                }

                int lhLbs  = (int)Math.Round(lh.Value  * wtPerGal.Value);
                int rhLbs  = (int)Math.Round(rh.Value  * wtPerGal.Value);
                int ctrLbs = (int)Math.Round(ctr.Value * wtPerGal.Value);
                int total  = lhLbs + rhLbs + ctrLbs;

                announcer.AnnounceImmediate($"Left {lhLbs}, Center {ctrLbs}, Right {rhLbs}, Total {total} pounds");
                return true;
            }

            case HotkeyAction.ReadFuelInfo:
            {
                double? lh  = simConnect.GetCachedVariableValue("HS787_FuelLH");
                double? rh  = simConnect.GetCachedVariableValue("HS787_FuelRH");
                double? ctr = simConnect.GetCachedVariableValue("HS787_FuelCtr");
                double? wtPerGal = simConnect.GetCachedVariableValue("HS787_FuelWtPerGal");

                if (lh == null || rh == null || ctr == null || wtPerGal == null)
                {
                    announcer.AnnounceImmediate("Fuel quantity not available");
                    return true;
                }

                double kgPerGal = wtPerGal.Value / 2.20462;
                int lhKg  = (int)Math.Round(lh.Value  * kgPerGal);
                int rhKg  = (int)Math.Round(rh.Value  * kgPerGal);
                int ctrKg = (int)Math.Round(ctr.Value * kgPerGal);
                int total  = lhKg + rhKg + ctrKg;

                announcer.AnnounceImmediate($"Left {lhKg}, Center {ctrKg}, Right {rhKg}, Total {total} kilograms");
                return true;
            }

            case HotkeyAction.ReadAltimeter:
            {
                double? inHg = simConnect.GetCachedVariableValue("HS787_Altimeter");
                if (inHg == null)
                {
                    announcer.AnnounceImmediate("Altimeter not available");
                    return true;
                }
                if (Math.Abs(inHg.Value - 29.92) < 0.005)
                {
                    announcer.AnnounceImmediate("Altimeter standard");
                    return true;
                }
                int hpa = (int)Math.Round(inHg.Value * 33.8639);
                announcer.AnnounceImmediate($"Altimeter: {hpa}, {inHg.Value:0.00}");
                return true;
            }

            case HotkeyAction.ReadDistanceToDest:
            {
                double? meters = simConnect.GetCachedVariableValue("HS787_DistDest");
                double gs = simConnect.GetCachedVariableValue("HS787_GroundSpeed") ?? 0;
                var parts = new System.Collections.Generic.List<string>();

                if (meters != null && meters.Value > 0)
                {
                    double nm = meters.Value / 1852.0;
                    string ete = FormatEte(nm, gs);
                    parts.Add(ete.Length > 0
                        ? $"Next waypoint, {(int)nm} miles, {ete}"
                        : $"Next waypoint, {(int)nm} miles");
                }

                double? eteSec = simConnect.GetCachedVariableValue("HS787_EteDest");
                if (eteSec != null && eteSec.Value > 0 && gs >= 30)
                {
                    double destNm = (eteSec.Value / 3600.0) * gs;
                    string destEte = FormatEteSeconds(eteSec.Value);
                    parts.Add($"Destination, {(int)destNm} miles, {destEte}");
                }

                announcer.AnnounceImmediate(parts.Count > 0
                    ? string.Join("; ", parts)
                    : "Distance to destination not available");
                return true;
            }

            case HotkeyAction.ReadDistanceToTOD:
            {
                double? todMeters = simConnect.GetCachedVariableValue("HS787_DistTOD");
                if (todMeters == null || todMeters.Value <= 0)
                {
                    announcer.AnnounceImmediate("Top of descent not available");
                    return true;
                }
                double todNm = todMeters.Value / 1852.0;
                double gs = simConnect.GetCachedVariableValue("HS787_GroundSpeed") ?? 0;
                string ete = FormatEte(todNm, gs);
                announcer.AnnounceImmediate(ete.Length > 0
                    ? $"{(int)todNm} miles to top of descent, {ete}"
                    : $"{(int)todNm} miles to top of descent");
                return true;
            }

            case HotkeyAction.ReadWaypointInfo:
            {
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
                return true;
            }

            case HotkeyAction.ReadGrossWeightKg:
            {
                simConnect.RequestSingleValue(
                    (int)SimConnect.SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;
            }

            // FMC keyboard not available in Phase 1 (requires JS bridge)
            // MainForm will handle ShowFenixMCDU for other aircraft; return false here
            case HotkeyAction.ShowFenixMCDU:
                return false;

            case HotkeyAction.FCUSetAutopilot:
            {
                hotkeyManager.ExitInputHotkeyMode();
                if (!simConnect.IsConnected)
                {
                    announcer.AnnounceImmediate("Not connected to simulator.");
                    return true;
                }

                if (_autopilotWindow != null && !_autopilotWindow.IsDisposed)
                {
                    _autopilotWindow.ShowForm();
                    return true;
                }

                _autopilotWindow = new Forms.HS787.HS787AutopilotWindow(simConnect, announcer);
                _autopilotWindow.FormClosed += (_, _) => _autopilotWindow = null;
                _autopilotWindow.ShowForm();
                return true;
            }

            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    // =========================================================================
    // HandleUIVariableSet — panel control actions
    // =========================================================================

    public override bool HandleUIVariableSet(string varKey, double value,
        SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        // AP master — toggle via K event (AUTOPILOT MASTER is a SimVar, not settable via SetLVar)
        if (varKey == "HS787_APMaster")
        {
            simConnect.SendEvent("AP_MASTER");
            return true;
        }

        // Autothrottle arm — K event (WT Boeing intercepts this and updates the LVar)
        if (varKey == "HS787_ATStatus")
        {
            simConnect.SendEvent("AUTO_THROTTLE_ARM");
            return true;
        }

        // External power — toggle using K:SET_EXTERNAL_POWER via calculator code.
        // Index 1 or 2 depending on which source. Value 0=Off, 1=On.
        if (varKey == "HS787_ExtPwr1")
        {
            int state = (int)value;
            simConnect.ExecuteCalculatorCode($"1 {state} (>K:2:SET_EXTERNAL_POWER)");
            return true;
        }
        if (varKey == "HS787_ExtPwr2")
        {
            int state = (int)value;
            simConnect.ExecuteCalculatorCode($"2 {state} (>K:2:SET_EXTERNAL_POWER)");
            return true;
        }

        // APU Generator switches — toggle using K: events
        if (varKey == "HS787_ApuGen1")
        {
            simConnect.ExecuteCalculatorCode($"{(int)value} (>K:APU_GEN1_SWITCH_SET)");
            return true;
        }
        if (varKey == "HS787_ApuGen2")
        {
            simConnect.ExecuteCalculatorCode($"{(int)value} (>K:APU_GEN2_SWITCH_SET)");
            return true;
        }

        // Autobrakes — SET_AUTOBRAKE_CONTROL with position 0=Off, 1=RTO, 2=1, 3=2, 4=3, 5=MAX
        if (varKey == "HS787_Autobrake")
        {
            simConnect.SendEvent("SET_AUTOBRAKE_CONTROL", (uint)(int)value);
            return true;
        }

        // IRS knob — LVar: 0=Off, 1=On. Aligned LVars are read-only (written by WT Boeing IRS system).
        if (varKey == "HS787_IRS_Knob1")
        {
            simConnect.SetLVar("B787_IRS_Knob_State:1", value);
            return true;
        }
        if (varKey == "HS787_IRS_Knob2")
        {
            simConnect.SetLVar("B787_IRS_Knob_State:2", value);
            return true;
        }

        // Anti-ice — WT Boeing LVars, 3-state: 0=Off, 1=Auto, 2=On
        if (varKey == "HS787_AntiIceEng1")
        {
            simConnect.SetLVar("B787_Engine_AntiIce_Knob_State:1", value);
            return true;
        }
        if (varKey == "HS787_AntiIceEng2")
        {
            simConnect.SetLVar("B787_Engine_AntiIce_Knob_State:2", value);
            return true;
        }
        if (varKey == "HS787_AntiIceWing")
        {
            simConnect.SetLVar("B787_Wing_AntiIce_Knob_State", value);
            return true;
        }

        // No smoking sign — direct LVar set
        if (varKey == "HS787_NoSmoking")
        {
            simConnect.SetLVar("XMLVAR_NO_SMOKING_MODE", value);
            return true;
        }

        // Parking brake — toggle if target differs from current state
        if (varKey == "HS787_ParkBrake")
        {
            double? current = simConnect.GetCachedVariableValue("HS787_ParkBrake");
            if (current == null || (int)current.Value != (int)value)
                simConnect.SendEvent("PARKING_BRAKES");
            return true;
        }

        // Flaps — FLAPS_SET takes index 0-9
        if (varKey == "HS787_FlapsHandle")
        {
            simConnect.SendEvent("FLAPS_SET", (uint)(int)value);
            return true;
        }

        // Gear — GEAR_SET: 0=up, 1=down
        if (varKey == "HS787_GearHandle")
        {
            simConnect.SendEvent("GEAR_SET", (uint)(int)value);
            return true;
        }

        // Master battery switches — toggle via calc code only if target differs from current.
        // TOGGLE_MASTER_BATTERY takes a 1-based battery index parameter.
        if (varKey == "HS787_BatSwitch1")
        {
            simConnect.ExecuteCalculatorCode($"(A:ELECTRICAL MASTER BATTERY:1,Bool) {(int)value} != if{{ 1 (>K:TOGGLE_MASTER_BATTERY) }}");
            return true;
        }
        if (varKey == "HS787_BatSwitch2")
        {
            simConnect.ExecuteCalculatorCode($"(A:ELECTRICAL MASTER BATTERY:2,Bool) {(int)value} != if{{ 2 (>K:TOGGLE_MASTER_BATTERY) }}");
            return true;
        }

        // Engine-driven hydraulic pump switches — toggle via calc code only if target differs.
        // HYDRAULIC_SWITCH_TOGGLE takes a 1-based engine index parameter.
        if (varKey == "HS787_HydEngL")
        {
            simConnect.ExecuteCalculatorCode($"(A:HYDRAULIC SWITCH:1,Bool) {(int)value} != if{{ 1 (>K:HYDRAULIC_SWITCH_TOGGLE) }}");
            return true;
        }
        if (varKey == "HS787_HydEngR")
        {
            simConnect.ExecuteCalculatorCode($"(A:HYDRAULIC SWITCH:2,Bool) {(int)value} != if{{ 2 (>K:HYDRAULIC_SWITCH_TOGGLE) }}");
            return true;
        }

        // Generators — toggle if target differs (TOGGLE_MASTER_ALTERNATOR:N not standard; use calc code)
        if (varKey == "HS787_Gen1")
        {
            simConnect.ExecuteCalculatorCode($"(A:GENERAL ENG GENERATOR SWITCH:1,Bool) {(int)value} != if{{ (>K:TOGGLE_MASTER_ALTERNATOR) }}");
            return true;
        }
        if (varKey == "HS787_Gen2")
        {
            simConnect.ExecuteCalculatorCode($"(A:GENERAL ENG GENERATOR SWITCH:2,Bool) {(int)value} != if{{ (>K:TOGGLE_MASTER_ALTERNATOR2) }}");
            return true;
        }

        // Lights — use SET events for beacon/strobe/nav/landing/taxi; toggle approach for logo/wing
        if (varKey == "HS787_LightBeacon")
        {
            simConnect.SendEvent("BEACON_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightStrobe")
        {
            simConnect.SendEvent("STROBE_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightNav")
        {
            simConnect.SendEvent("NAV_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightLanding")
        {
            simConnect.SendEvent("LANDING_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightTaxi")
        {
            simConnect.SendEvent("TAXI_LIGHTS_SET", (uint)(int)value);
            return true;
        }
        if (varKey == "HS787_LightLogo")
        {
            simConnect.ExecuteCalculatorCode($"(A:LIGHT LOGO,Bool) {(int)value} != if{{ (>K:TOGGLE_LOGO_LIGHTS) }}");
            return true;
        }
        if (varKey == "HS787_LightWing")
        {
            simConnect.ExecuteCalculatorCode($"(A:LIGHT WING,Bool) {(int)value} != if{{ (>K:TOGGLE_WING_LIGHTS) }}");
            return true;
        }

        // Doors — toggled via K:TOGGLE_AIRCRAFT_EXIT with 1-based index (matches INTERACTIVE POINT OPEN:N).
        // The current state is checked so we only fire the toggle when a change is needed.
        // Passenger: 1=1L, 2=1R, 3=2L, 4=2R, 5=3L, 6=3R, 7=4L, 8=4R  Cargo: 9=Fwd, 10=Aft
        int? doorIdx = varKey switch
        {
            "HS787_Door_1L"       => 1,
            "HS787_Door_1R"       => 2,
            "HS787_Door_2L"       => 3,
            "HS787_Door_2R"       => 4,
            "HS787_Door_3L"       => 5,
            "HS787_Door_3R"       => 6,
            "HS787_Door_4L"       => 7,
            "HS787_Door_4R"       => 8,
            "HS787_Door_FwdCargo" => 9,
            "HS787_Door_AftCargo" => 10,
            "HS787_RefuelDoor"    => 11,
            "HS787_GPUPipe"       => 12,
            _                     => null
        };
        if (doorIdx.HasValue)
        {
            simConnect.SendEvent("TOGGLE_AIRCRAFT_EXIT", (uint)doorIdx.Value);
            return true;
        }

        return false;
    }

    // =========================================================================
    // ProcessSimVarUpdate — suppress raw value announcements where needed
    // =========================================================================

    public override bool ProcessSimVarUpdate(string variableKey, double value,
        ScreenReaderAnnouncer announcer)
    {
        // FuelBalanceFault: only announce when it turns ON (value = 1)
        if (variableKey == "HS787_FuelBalanceFault")
        {
            if ((int)value == 1)
                announcer.Announce("Fuel Balance Fault");
            return true;
        }

        // EXECActive: announce both activation and deactivation (light on/off)
        if (variableKey == "HS787_EXECActive")
        {
            bool now = (int)value == 1;
            if (now && !_previousExecActive)
                announcer.Announce("EXEC Active");
            else if (!now && _previousExecActive)
                announcer.Announce("EXEC Off");
            _previousExecActive = now;
            return true;
        }

        // TOGA: announce activation only
        if (variableKey == "HS787_TOGA")
        {
            if ((int)value == 1)
                announcer.Announce("TOGA Active");
            return true;
        }

        // APDisconnected: announce disconnect only
        if (variableKey == "HS787_APDisconnected")
        {
            if ((int)value == 1)
                announcer.Announce("Autopilot Disconnected");
            return true;
        }

        // Approach mode: announce arm and disengage transitions.
        // Startup suppressed — _previousAppHold defaults false so first value=0 is silent.
        if (variableKey == "HS787_APP")
        {
            bool now = value > 0;
            if (now != _previousAppHold)
                announcer.Announce(now ? "Approach armed" : "Approach disengaged");
            _previousAppHold = now;
            return true;
        }

        // GS capture: announce once when glideslope becomes active.
        if (variableKey == "HS787_GS_Active")
        {
            bool now = value > 0;
            if (now && !_previousGSActive)
                announcer.Announce("Glideslope active");
            _previousGSActive = now;
            return true;
        }

        // Autopilot and autothrottle state — track previous value so startup "Off" state
        // doesn't produce an announcement; only actual transitions are announced.
        if (variableKey == "HS787_APMaster")
        {
            bool now = (int)value == 1;
            if (now != _previousAPMaster)
                announcer.Announce(now ? "Autopilot 1 On" : "Autopilot 1 Off");
            _previousAPMaster = now;
            return true;
        }

        if (variableKey == "HS787_ATStatus")
        {
            bool now = value > 0;
            bool previous = _previousATStatus > 0;
            if (now != previous)
                announcer.Announce(now ? "Autothrottle Armed" : "Autothrottle Disarmed");
            _previousATStatus = now ? 1 : 0;
            return true;
        }

        // External power — announce changes only; suppress startup "Off" announcement.
        if (variableKey == "HS787_ExtPwrOn1")
        {
            bool now = (int)value == 1;
            if (now != _previousExtPwr1On)
                announcer.Announce(now ? "External Power 1 On" : "External Power 1 Off");
            _previousExtPwr1On = now;
            return true;
        }

        if (variableKey == "HS787_ExtPwrOn2")
        {
            bool now = (int)value == 1;
            if (now != _previousExtPwr2On)
                announcer.Announce(now ? "External Power 2 On" : "External Power 2 Off");
            _previousExtPwr2On = now;
            return true;
        }

        // Speedbrake: WT_SPEEDBRAKE_LEVER_POS is 0-16384; announce on state band changes.
        // DOWN_LIMIT=410, ARM_LIMIT=1230 (from BoeingSpeedbrakeSystem constants).
        if (variableKey == "HS787_Speedbrake")
        {
            int state = value <= 410 ? 0 : value <= 1230 ? 1 : 2;
            if (state != _previousSpeedbrakeState)
            {
                string msg = state switch
                {
                    1 => "Speedbrake Armed",
                    2 => "Speedbrake Deployed",
                    _ => "Speedbrake Down"
                };
                announcer.Announce(msg);
            }
            _previousSpeedbrakeState = state;
            return true;
        }

        // MCP mode engagement/disengagement — announce both on and off transitions.
        if (variableKey == "HS787_FLCH")
        {
            bool now = value > 0;
            if (now != _previousFLCH)
                announcer.Announce(now ? "Level Change Engaged" : "Level Change Off");
            _previousFLCH = now;
            return true;
        }

        if (variableKey == "HS787_ALTHold")
        {
            bool now = value > 0;
            if (now != _previousALTHold)
                announcer.Announce(now ? "Altitude Hold" : "Altitude Hold Off");
            _previousALTHold = now;
            return true;
        }

        if (variableKey == "HS787_LNAV")
        {
            bool now = value > 0;
            if (now != _previousLNAV)
                announcer.Announce(now ? "LNAV Engaged" : "LNAV Off");
            _previousLNAV = now;
            return true;
        }

        if (variableKey == "HS787_VNAV")
        {
            bool now = value > 0;
            if (now != _previousVNAV)
                announcer.Announce(now ? "VNAV Engaged" : "VNAV Off");
            _previousVNAV = now;
            return true;
        }

        if (variableKey == "HS787_HDGHold")
        {
            bool now = value > 0;
            if (now != _previousHDGHold)
                announcer.Announce(now ? "Heading Hold" : "Heading Hold Off");
            _previousHDGHold = now;
            return true;
        }

        if (variableKey == "HS787_VS_Active")
        {
            bool now = value > 0;
            if (now != _previousVSActive)
                announcer.Announce(now ? "V/S Engaged" : "V/S Off");
            _previousVSActive = now;
            return true;
        }

        // APU knob — announce transitions; suppress first poll (startup state)
        if (variableKey == "HS787_APU_Knob")
        {
            int now = (int)value;
            if (_previousApuKnob >= 0 && now != _previousApuKnob)
            {
                string msg = now switch { 1 => "APU On", 2 => "APU Starting", _ => "APU Off" };
                announcer.Announce(msg);
            }
            _previousApuKnob = now;
            return true;
        }

        // Engine start states
        if (variableKey == "HS787_EngStartState1")
        {
            int now = (int)value;
            if (_previousEngState1 >= 0 && now != _previousEngState1)
            {
                string msg = now switch { 1 => "Engine 1 Starting", 2 => "Engine 1 Running", _ => "Engine 1 Stopped" };
                announcer.Announce(msg);
            }
            _previousEngState1 = now;
            return true;
        }

        if (variableKey == "HS787_EngStartState2")
        {
            int now = (int)value;
            if (_previousEngState2 >= 0 && now != _previousEngState2)
            {
                string msg = now switch { 1 => "Engine 2 Starting", 2 => "Engine 2 Running", _ => "Engine 2 Stopped" };
                announcer.Announce(msg);
            }
            _previousEngState2 = now;
            return true;
        }

        // Pack switches
        if (variableKey == "HS787_PackL")
        {
            bool now = value > 0;
            if (now != _previousPackL)
                announcer.Announce(now ? "Pack Left Auto" : "Pack Left Off");
            _previousPackL = now;
            return true;
        }

        if (variableKey == "HS787_PackR")
        {
            bool now = value > 0;
            if (now != _previousPackR)
                announcer.Announce(now ? "Pack Right Auto" : "Pack Right Off");
            _previousPackR = now;
            return true;
        }

        // Hydraulic demand pumps
        if (variableKey == "HS787_HydDemandLeft")
        {
            bool now = value > 0;
            if (now != _previousHydDemandL)
                announcer.Announce(now ? "Hydraulic Demand Left On" : "Hydraulic Demand Left Off");
            _previousHydDemandL = now;
            return true;
        }

        if (variableKey == "HS787_HydDemandRight")
        {
            bool now = value > 0;
            if (now != _previousHydDemandR)
                announcer.Announce(now ? "Hydraulic Demand Right On" : "Hydraulic Demand Right Off");
            _previousHydDemandR = now;
            return true;
        }

        // Emergency lights
        if (variableKey == "HS787_EmerLights")
        {
            int now = (int)value;
            if (_previousEmerLights >= 0 && now != _previousEmerLights)
            {
                string msg = now switch { 1 => "Emergency Lights Armed", 2 => "Emergency Lights On", _ => "Emergency Lights Off" };
                announcer.Announce(msg);
            }
            _previousEmerLights = now;
            return true;
        }

        // Seat belts sign
        if (variableKey == "HS787_Seatbelts")
        {
            int now = (int)value;
            if (_previousSeatbelts >= 0 && now != _previousSeatbelts)
            {
                string msg = now switch { 1 => "Seat Belts Auto", 2 => "Seat Belts On", _ => "Seat Belts Off" };
                announcer.Announce(msg);
            }
            _previousSeatbelts = now;
            return true;
        }

        // IRS knobs — announce On/Off transitions, suppress first poll
        if (variableKey == "HS787_IRS_Knob1")
        {
            int now = (int)value;
            if (_previousIrsKnob1 >= 0 && now != _previousIrsKnob1)
                announcer.Announce(now == 1 ? "IRS Left On" : "IRS Left Off");
            _previousIrsKnob1 = now;
            return true;
        }
        if (variableKey == "HS787_IRS_Knob2")
        {
            int now = (int)value;
            if (_previousIrsKnob2 >= 0 && now != _previousIrsKnob2)
                announcer.Announce(now == 1 ? "IRS Right On" : "IRS Right Off");
            _previousIrsKnob2 = now;
            return true;
        }

        // IRS aligned — announce when alignment completes or is lost, suppress first poll
        if (variableKey == "HS787_IRS_Aligned1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousIrsAligned1 >= 0 && now != _previousIrsAligned1)
                announcer.Announce(now == 1 ? "IRS Left Aligned" : "IRS Left Alignment Lost");
            _previousIrsAligned1 = now;
            return true;
        }
        if (variableKey == "HS787_IRS_Aligned2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousIrsAligned2 >= 0 && now != _previousIrsAligned2)
                announcer.Announce(now == 1 ? "IRS Right Aligned" : "IRS Right Alignment Lost");
            _previousIrsAligned2 = now;
            return true;
        }

        // Anti-ice — 3-state (Off/Auto/On), suppress first poll
        if (variableKey == "HS787_AntiIceEng1")
        {
            int now = (int)value;
            if (_previousAntiIceEng1 >= 0 && now != _previousAntiIceEng1)
            {
                string msg = now switch { 1 => "Engine 1 Anti-Ice Auto", 2 => "Engine 1 Anti-Ice On", _ => "Engine 1 Anti-Ice Off" };
                announcer.Announce(msg);
            }
            _previousAntiIceEng1 = now;
            return true;
        }
        if (variableKey == "HS787_AntiIceEng2")
        {
            int now = (int)value;
            if (_previousAntiIceEng2 >= 0 && now != _previousAntiIceEng2)
            {
                string msg = now switch { 1 => "Engine 2 Anti-Ice Auto", 2 => "Engine 2 Anti-Ice On", _ => "Engine 2 Anti-Ice Off" };
                announcer.Announce(msg);
            }
            _previousAntiIceEng2 = now;
            return true;
        }
        if (variableKey == "HS787_AntiIceWing")
        {
            int now = (int)value;
            if (_previousAntiIceWing >= 0 && now != _previousAntiIceWing)
            {
                string msg = now switch { 1 => "Wing Anti-Ice Auto", 2 => "Wing Anti-Ice On", _ => "Wing Anti-Ice Off" };
                announcer.Announce(msg);
            }
            _previousAntiIceWing = now;
            return true;
        }

        // No smoking sign
        if (variableKey == "HS787_NoSmoking")
        {
            int now = (int)value;
            if (_previousNoSmoking >= 0 && now != _previousNoSmoking)
            {
                string msg = now switch { 1 => "No Smoking Auto", 2 => "No Smoking On", _ => "No Smoking Off" };
                announcer.Announce(msg);
            }
            _previousNoSmoking = now;
            return true;
        }

        // Parking brake
        if (variableKey == "HS787_ParkBrake")
        {
            int now = (int)value;
            if (_previousParkBrake >= 0 && now != _previousParkBrake)
                announcer.Announce(now == 1 ? "Parking Brake Set" : "Parking Brake Released");
            _previousParkBrake = now;
            return true;
        }

        // Flaps — announce position on lever movement, suppress first poll
        if (variableKey == "HS787_FlapsHandle")
        {
            int now = (int)value;
            if (_previousFlapsHandle >= 0 && now != _previousFlapsHandle)
            {
                string pos = now switch
                {
                    0 => "Up", 1 => "1", 2 => "5", 3 => "10",
                    4 => "15", 5 => "17", 6 => "18", 7 => "20",
                    8 => "25", 9 => "30", _ => now.ToString()
                };
                announcer.Announce($"Flaps {pos}");
            }
            _previousFlapsHandle = now;
            return true;
        }

        // Gear handle — announce Up/Down transitions, suppress first poll
        if (variableKey == "HS787_GearHandle")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousGearHandleI >= 0 && now != _previousGearHandleI)
                announcer.Announce(now == 1 ? "Gear Down" : "Gear Up");
            _previousGearHandleI = now;
            return true;
        }

        // Exterior lights — announce primary lights on change, suppress first poll
        if (variableKey == "HS787_LightBeacon")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightBeacon >= 0 && now != _previousLightBeacon)
                announcer.Announce(now == 1 ? "Beacon On" : "Beacon Off");
            _previousLightBeacon = now;
            return true;
        }
        if (variableKey == "HS787_LightStrobe")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightStrobe >= 0 && now != _previousLightStrobe)
                announcer.Announce(now == 1 ? "Strobe On" : "Strobe Off");
            _previousLightStrobe = now;
            return true;
        }
        if (variableKey == "HS787_LightNav")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightNav >= 0 && now != _previousLightNav)
                announcer.Announce(now == 1 ? "Nav Lights On" : "Nav Lights Off");
            _previousLightNav = now;
            return true;
        }
        if (variableKey == "HS787_LightLanding")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightLanding >= 0 && now != _previousLightLanding)
                announcer.Announce(now == 1 ? "Landing Lights On" : "Landing Lights Off");
            _previousLightLanding = now;
            return true;
        }

        // Altimeter setting — announce changes, suppress first-poll value
        if (variableKey == "HS787_Altimeter")
        {
            double inHg = value;
            if (double.IsNaN(_lastAnnouncedAltimeter))
            {
                _lastAnnouncedAltimeter = inHg;
                return true;
            }
            if (Math.Abs(inHg - _lastAnnouncedAltimeter) < 0.005)
                return true;
            _lastAnnouncedAltimeter = inHg;
            if (Math.Abs(inHg - 29.92) < 0.005)
                announcer.Announce("Altimeter standard");
            else
            {
                int hpa = (int)Math.Round(inHg * 33.8639);
                announcer.Announce($"Altimeter {hpa}");
            }
            return true;
        }

        // Cache-only variables — suppress all automatic announcements.
        // These are IsAnnounced=true purely so the monitoring engine caches them;
        // hotkey readouts and dialog toggles read the cached values on demand.
        switch (variableKey)
        {
            case "HS787_MCP_IAS":
            case "HS787_MCP_Mach":
            case "HS787_MCP_IsMach":
            case "HS787_MCP_SpdManual":
            case "HS787_MCP_Heading":
            case "HS787_MCP_Altitude":
            case "HS787_MCP_VS":
            case "HS787_MCP_FPA":
            case "HS787_FPAMode":
            case "HS787_TRKMode":
            case "HS787_GS_Armed":
            case "HS787_LOC":
            case "HS787_AltManual":
            case "HS787_FuelLH":
            case "HS787_FuelRH":
            case "HS787_FuelCtr":
            case "HS787_FuelWtPerGal":
            case "HS787_DistDest":
            case "HS787_GroundSpeed":
            case "HS787_DistTOD":
            case "HS787_EteDest":
                return true; // cached — no announcement
        }

        return false;
    }

    // =========================================================================
    // MCP Dialogs
    // =========================================================================

    private void ShowHeadingDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&LNAV", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_LNAV") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("AP_NAV1_HOLD")),

            new("&Heading Hold", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_HDGHold") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("AP_HDG_HOLD")),

            new("HDG / &TRK", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_TRKMode") ?? 0;
                return v > 0 ? "TRK" : "HDG";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_TRKMode") ?? 0;
                simConnect.SetLVar("XMLVAR_TRK_MODE_ACTIVE", current > 0 ? 0 : 1);
            })
        };

        var dialog = new ValueInputForm(
            "MCP Heading", "heading", "0-359", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 0 && val <= 359)
                    return (true, "");
                return (false, "Enter a heading between 0 and 359");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int hdg))
                    simConnect.SendEvent("HEADING_BUG_SET", (uint)hdg);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowSpeedDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&Mode", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_MCP_IsMach") ?? 0;
                return v > 0 ? "Mach" : "IAS";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_MCP_IsMach") ?? 0;
                simConnect.SetLVar("XMLVAR_AirSpeedIsInMach", current > 0 ? 0 : 1);
            }),

            new("&FLCH", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_FLCH") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("FLIGHT_LEVEL_CHANGE")),

            new("Speed &INTV", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_MCP_SpdManual") ?? 0;
                return v > 0 ? "Manual" : "FMC";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_MCP_SpdManual") ?? 0;
                simConnect.SetLVar("XMLVAR_SpeedIsManuallySet", current > 0 ? 0 : 1);
            })
        };

        var dialog = new ValueInputForm(
            "MCP Speed", "speed", "IAS: 100-399 knots / Mach: 0.40-0.99", announcer,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    if (val >= 100 && val <= 399) return (true, "");
                    if (val >= 0.4 && val < 1.0)  return (true, "");
                }
                return (false, "Enter knots (100-399) or Mach (0.40-0.99)");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double spd))
                    return;

                if (spd < 10.0)
                {
                    // AP_MACH_VAR_SET takes value × 100 (e.g. Mach 0.82 → 82)
                    simConnect.SendEvent("AP_MACH_VAR_SET", (uint)(int)Math.Round(spd * 100));
                }
                else
                {
                    simConnect.SendEvent("AP_SPD_VAR_SET", (uint)(int)spd);
                }
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowAltitudeDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&VNAV", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_VNAV") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_VNAV") ?? 0;
                simConnect.SetLVar("XMLVAR_VNAVButtonValue", current > 0 ? 0 : 1);
            }),

            new("&Level Change", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_FLCH") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("FLIGHT_LEVEL_CHANGE")),

            new("Altitude &Hold", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_ALTHold") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("AP_ALT_HOLD")),

            new("Alt &INTV", () => "Momentary", () =>
            {
                // Fire from inside Coherent GT via bridge when available — more reliable for
                // WT Boeing H events that are internal to the sim's JS runtime. Fall back to
                // MobiFlight WASM if the bridge is not connected.
                if (BridgeServer != null && BridgeServer.IsBridgeConnected)
                    BridgeServer.EnqueueMfdCommand("type_key:ALTITUDE_INTERVENTION");
                else
                    simConnect.SendHVar("AS01B_FMC_1_BTN_ALTITUDE_INTERVENTION");
            })
        };

        var dialog = new ValueInputForm(
            "MCP Altitude", "altitude", "0-45000 feet", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 0 && val <= 45000)
                    return (true, "");
                return (false, "Enter a value between 0 and 45000");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int alt))
                    simConnect.SendEvent("AP_ALT_VAR_SET_ENGLISH", (uint)alt);
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowVSDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&Engage V/S", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_VS_Active") ?? 0;
                return v > 0 ? "Engaged" : "Off";
            }, () => simConnect.SendEvent("AP_VS_HOLD")),

            new("V/S &FPA", () =>
            {
                double v = simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0;
                return v > 0 ? "FPA" : "V/S";
            }, () =>
            {
                double current = simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0;
                simConnect.SetLVar("XMLVAR_FPA_MODE_ACTIVE", current > 0 ? 0 : 1);
            }),

            new("&Approach", () =>
            {
                bool gsActive  = (simConnect.GetCachedVariableValue("HS787_GS_Active") ?? 0) > 0;
                bool locActive = (simConnect.GetCachedVariableValue("HS787_LOC")       ?? 0) > 0;
                bool appHold   = (simConnect.GetCachedVariableValue("HS787_APP")       ?? 0) > 0;
                if (gsActive)  return "GS Active";
                if (locActive) return "LOC Active";
                if (appHold)   return "Armed";
                return "Off";
            }, () => simConnect.SendEvent("AP_APR_HOLD"))
        };

        var dialog = new ValueInputForm(
            "MCP Vertical Speed", "V/S or FPA",
            "V/S: -6000 to 6000 fpm / FPA: -9.9 to 9.9 deg",
            announcer,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    bool isFPA = (simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0) > 0;
                    if (isFPA)
                    {
                        if (val >= -9.9 && val <= 9.9) return (true, "");
                        return (false, "Enter FPA between -9.9 and 9.9 degrees");
                    }
                    else
                    {
                        if (val >= -6000 && val <= 6000) return (true, "");
                        return (false, "Enter V/S between -6000 and 6000 fpm");
                    }
                }
                return (false, "Enter a numeric value");
            },
            toggles,
            input =>
            {
                if (!double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                    return;

                bool isFPA = (simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0) > 0;
                if (isFPA)
                {
                    simConnect.SetLVar("WT_AP_FPA_Target:1", val);
                }
                else
                {
                    // AP_VS_VAR_SET_ENGLISH handles negative values via two's complement
                    simConnect.SendEvent("AP_VS_VAR_SET_ENGLISH", (uint)(int)val);
                }
            },
            inputEnabledCheck: () => (simConnect.GetCachedVariableValue("HS787_VS_Active") ?? 0) > 0
                                  || (simConnect.GetCachedVariableValue("HS787_FPAMode") ?? 0) > 0);

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    private void ShowBaroDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var toggles = new List<ToggleButtonDef>
        {
            new("&Standard (STD)", () =>
            {
                double? v = simConnect.GetCachedVariableValue("HS787_Altimeter");
                return v != null && Math.Abs(v.Value - 29.92) < 0.005 ? "Set" : "Not set";
            }, () =>
            {
                // Standard pressure = 1013 HPA = 29.92 inHg
                simConnect.SendEvent("KOHLSMAN_SET", (uint)Math.Round(29.92 * 16));
            })
        };

        var dialog = new ValueInputForm(
            "Altimeter", "QNH", "HPA (e.g. 1013)", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 900 && val <= 1100)
                    return (true, "");
                return (false, "Enter QNH in HPA between 900 and 1100");
            },
            toggles,
            input =>
            {
                if (int.TryParse(input, out int hpa))
                {
                    double inHg = hpa / 33.8639;
                    simConnect.SendEvent("KOHLSMAN_SET", (uint)Math.Round(inHg * 16));
                }
            });

        dialog.ShowCancelButton = false;
        dialog.Show(parentForm);
    }

    // Returns "Xh Ym" or "Ym" ETE string, or "" if ground speed is too low to be meaningful.
    private static string FormatEte(double distanceNm, double gsKnots)
    {
        if (gsKnots < 30 || distanceNm <= 0) return "";
        double hours = distanceNm / gsKnots;
        int totalMinutes = (int)Math.Round(hours * 60);
        int hh = totalMinutes / 60;
        int mm = totalMinutes % 60;
        return hh > 0 ? $"{hh}h {mm}m" : $"{mm}m";
    }

    // Returns "Xh Ym" or "Ym" from a raw seconds value.
    private static string FormatEteSeconds(double seconds)
    {
        int totalMinutes = (int)Math.Round(seconds / 60.0);
        int hh = totalMinutes / 60;
        int mm = totalMinutes % 60;
        return hh > 0 ? $"{hh}h {mm}m" : $"{mm}m";
    }
}
