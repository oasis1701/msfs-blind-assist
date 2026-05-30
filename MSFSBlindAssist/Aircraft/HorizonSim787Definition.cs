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

    // SimVar diagnostic: 0=unknown, 1=script loaded, 2=fetch failed, 3=connected.
    // Written by L:MSFSBA_787_STAGE from hs787-mfd-bridge.js; read here via SimConnect.
    public int BridgeStage { get; private set; } = 0;

    // Tri-state init (-1 = unset, suppresses first-poll announcement so MSFSBA
    // doesn't speak the entire panel state when it connects to a powered-up sim).
    private int  _previousAppHold      = -1;
    private int  _previousGSActive     = -1;
    private int  _previousAPMaster     = -1;
    private int  _previousATStatus     = -1;
    private int  _previousSpeedbrakeState = -1; // 0=down, 1=armed, 2=deployed
    private int  _previousExtPwr1On    = -1;
    private int  _previousExtPwr2On    = -1;
    private int  _previousExecActive   = -1;
    private int  _previousTOGA         = -1;
    private int  _previousFuelBalanceFault = -1;
    private int  _previousFLCH         = -1;
    private int  _previousALTHold      = -1;
    private int  _previousLNAV         = -1;
    private int  _previousVNAV         = -1;
    private int  _previousHDGHold      = -1;
    private int  _previousVSActive     = -1;
    private int  _previousAPDisconnected = -1;

    // System-setup announcement state (−1 = unset, suppresses first-poll announcement)
    private int  _previousApuKnob     = -1;
    private int  _previousEngState1   = -1;
    private int  _previousEngState2   = -1;
    // Cached autopilot window — created on first FCUSetAutopilot press, focused on subsequent presses.
    private Forms.HS787.HS787AutopilotWindow? _autopilotWindow;
    private int  _previousPackL       = -1;
    private int  _previousPackR       = -1;
    private int  _previousHydDemandL  = -1;
    private int  _previousHydDemandR  = -1;
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

    // BridgeVersion 18+ additions — annunciators / status / overhead extras.
    // -1 / unset suppresses the first-poll announcement (only transitions are spoken).
    private int  _previousMasterCaution    = -1;
    private int  _previousMasterWarning    = -1;
    private int  _previousStallWarning     = -1;
    private int  _previousIrsOnBat         = -1;
    private int  _previousLightMaster      = -1;
    private int  _previousEmerLightsCover  = -1;
    private int  _previousEfbPower         = -1;
    private int  _previousFlightComputerAuto = -1;
    private int  _previousPacksAuto        = -1;
    private int  _previousFdDoorPower      = -1;
    private int  _previousCoolingAft       = -1;
    private int  _previousEquipFwd         = -1;
    private int  _previousPressManAltOn    = -1;
    // BridgeVersion 19+ additions
    private int  _previousAcBusEnergized   = -1;
    private int  _previousAutoBacklight    = -1;
    private int  _previousNextGenFp        = -1;
    private int  _previousHudDown1         = -1;
    private int  _previousHudDown2         = -1;
    private int  _previousHudAutoBrt1      = -1;
    private int  _previousHudAutoBrt2      = -1;
    private int  _previousAirDataSrc1      = -1;
    private int  _previousAirDataSrc2      = -1;
    // Batch 5 — yaw damper, antiskid, avionics, pitot heat, interior lights
    private int  _previousYawDamper        = -1;
    private int  _previousAntiSkid         = -1;
    private int  _previousAvionicsMaster   = -1;
    private int  _previousPitotHeat        = -1;
    // Interior light bus state-trackers removed with the vars (see comment above
    // their definitions for why they were pulled).

    // Batch 6 — COM/squawk announce state. 0 = unset, so first poll establishes
    // a baseline silently; subsequent value changes are spoken in PMDG style:
    // "COM1 active 121.800", "COM2 standby 119.000", "Squawk 1200".
    private double _lastComActive1   = 0;
    private double _lastComActive2   = 0;
    private double _lastComStandby1  = 0;
    private double _lastComStandby2  = 0;
    private double _lastSquawkCode   = 0;
    // Batch 3 — AP modes / approach / flight timer / checklist / HUD symbology
    private int  _previousApAltHold        = -1;
    private int  _previousApFlch           = -1;
    private int  _previousApVs             = -1;
    private int  _previousApSpd            = -1;
    private int  _previousApThr            = -1;
    private int  _previousApHdgHold        = -1;
    private int  _previousApHdgSel         = -1;
    private int  _previousApClbCon         = -1;
    private int  _previousApproachIls      = -1;
    private int  _previousHudSymbology1    = -1;
    private int  _previousHudSymbology2    = -1;
    private int  _previousHudDecInhibit1   = -1;
    private int  _previousHudDecInhibit2   = -1;
    // Batch 4 — safety-critical fire + cabin/hydraulic monitoring
    private int  _previousEngFire1         = -1;
    private int  _previousEngFire2         = -1;
    private int  _previousFireBottleDisch1 = -1;
    private int  _previousFireBottleDisch2 = -1;

    // Batch 7 — panel switches promoted from OnRequest to Continuous so they
    // auto-announce on background changes (e.g. external set, ground crew, scripts).
    // First poll silent via -1 tri-state init.
    private int  _previousBatSwitch1       = -1;
    private int  _previousBatSwitch2       = -1;
    private int  _previousGen1             = -1;
    private int  _previousGen2             = -1;
    private int  _previousApuGen1          = -1;
    private int  _previousApuGen2          = -1;
    private int  _previousUtilityCabin     = -1;
    private int  _previousUtilityIfe       = -1;
    private int  _previousHydEngL          = -1;
    private int  _previousHydEngR          = -1;
    private int  _previousHydC1            = -1;
    private int  _previousHydC2            = -1;
    private int  _previousFuelBalance      = -1;
    private int  _previousFuelBalanceActive = -1;
    private int  _previousTrimAirL         = -1;
    private int  _previousTrimAirR         = -1;
    private int  _previousRecircUpper      = -1;
    private int  _previousRecircLower      = -1;
    private int  _previousWshldDeice1      = -1;
    private int  _previousWshldDeice2      = -1;
    private int  _previousWshldDeice3      = -1;
    private int  _previousWshldDeice4      = -1;
    private int  _previousAltnFlapsArmed   = -1;
    private int  _previousBaroSelector     = -1;
    private int  _previousMinsMode         = -1;
    private int  _previousFPVMode          = -1;
    private int  _previousTransponderMode  = -1;
    private int  _previousSATCOM           = -1;
    private int  _previousVBar             = -1;
    private int  _previousAutobrake        = -1;
    private int  _previousLightTaxi        = -1;
    private int  _previousLightLogo        = -1;
    private int  _previousLightWing        = -1;

    // Batch 8 — Fuel pumps, bleeds, fire system, cargo fire, standby power
    private int  _previousFuelPump_LFwd    = -1;
    private int  _previousFuelPump_LAft    = -1;
    private int  _previousFuelPump_RFwd    = -1;
    private int  _previousFuelPump_RAft    = -1;
    private int  _previousFuelPump_CtrL    = -1;
    private int  _previousFuelPump_CtrR    = -1;
    private int  _previousFuelPump_APU     = -1;
    private int  _previousFuelXfeedFwd     = -1;
    private int  _previousBleedEng1        = -1;
    private int  _previousBleedEng2        = -1;
    private int  _previousBleedAPU         = -1;
    private int  _previousBleedIso         = -1;
    private int  _previousFireTest         = -1;
    private int  _previousEngFireHandle1   = -1;
    private int  _previousEngFireHandle2   = -1;
    private int  _previousAPUFireHandle    = -1;
    private int  _previousCargoFireFwd     = -1;
    private int  _previousCargoFireAft     = -1;
    private int  _previousCargoFireDisch   = -1;
    private int  _previousStandbyPower     = -1;
    private int  _previousFuelControl1     = -1;
    private int  _previousFuelControl2     = -1;
    private int  _previousBaroSTD          = -1;
    private int  _previousGPUPipe          = -1;
    private int  _previousRefuelDoor       = -1;
    // Passenger + cargo doors (open ≥ 50 %, closed below). First-poll silent via −1.
    private int  _previousDoor1L           = -1;
    private int  _previousDoor1R           = -1;
    private int  _previousDoor2L           = -1;
    private int  _previousDoor2R           = -1;
    private int  _previousDoor3L           = -1;
    private int  _previousDoor3R           = -1;
    private int  _previousDoor4L           = -1;
    private int  _previousDoor4R           = -1;
    private int  _previousDoorFwdCargo     = -1;
    private int  _previousDoorAftCargo     = -1;

    public override string AircraftName => "HorizonSim 787-9";
    public override string AircraftCode => "HS_787";

    // ---- WT/Asobo Boeing 787 InputEvent (B:) name table -------------------------
    // Several switches on the WT/Asobo B787 panel are wired through B: InputEvents,
    // not K events or L: vars — so K-event writes succeed silently without flipping the
    // real subsystem. Names verified against the runtime catalog dumped to
    // %LocalAppData%\MSFSBlindAssist\logs\input_events.txt on every aircraft load.
    //
    // The map below routes HS787 panel var keys to InputEvent names so HandleUIVariableSet
    // can prefer the InputEvent and fall back to the existing K-event / SetLVar branches
    // when the InputEvent isn't in the catalog (e.g. base Asobo plane or older HS787).
    private static readonly Dictionary<string, string[]> HS787_INPUT_EVENT_MAP = new()
    {
        // Autothrottle arm — paired captain/FO switches. Firing either arms the AT.
        ["HS787_ATStatus"]      = new[] { "AUTOPILOT_AUTOTHROTTLE_ARM_1", "AUTOPILOT_AUTOTHROTTLE_ARM_2" },

        // Engine autostart — single button fires the WT/Asobo 787's full automatic
        // start procedure for both engines (cockpit "AUTO START" button equivalent).
        ["HS787_EngineAutoStart"] = new[] { "PROCEDURE_AUTOSTART" },

        // Per-engine start selector rotaries — write target position (0/1/2).
        ["HS787_EngineStart1"]    = new[] { "ENGINE_STARTER_1" },
        ["HS787_EngineStart2"]    = new[] { "ENGINE_STARTER_2" },

        // MCP Approach + Localizer buttons. K-event AP_APR_HOLD updates the L-var
        // displayed in the cockpit but the WT 787 doesn't actually arm the approach
        // director from it — the InputEvent does.
        ["HS787_APP"]           = new[] { "AUTOPILOT_APPROACH_BUTTON" },
        ["HS787_LOC"]           = new[] { "AUTOPILOT_LOCALIZER_BUTTON" },

        // Engine fuel control switches (Run / Cutoff). The WT 787's fuel valve is
        // an InputEvent; the standard MIXTUREn_RICH/LEAN K events don't reach it,
        // so the cockpit switch stays in RUN and the engines keep burning.
        ["HS787_FuelControl1"]  = new[] { "FUEL_VALVE_L" },
        ["HS787_FuelControl2"]  = new[] { "FUEL_VALVE_R" },

        // External power: AIRLINER_EXT_PWR_N is a MOMENTARY pushbutton (press = 1,
        // release = 0; each press toggles state). Not safe to route through the generic
        // first-pass dispatch — would toggle on every combo value change. Handled
        // separately in HandleUIVariableSet with state-aware press+release logic.

        // Battery master.
        ["HS787_Battery"]       = new[] { "ELECTRICAL_BATTERY_SWITCH" },

        // Fuel pumps — the WT model accepts the InputEvent directly with 0/1 values.
        ["HS787_FuelPump_LFwd"] = new[] { "FUEL_PUMP_FWD_L" },
        ["HS787_FuelPump_LAft"] = new[] { "FUEL_PUMP_AFT_L" },
        ["HS787_FuelPump_RFwd"] = new[] { "FUEL_PUMP_FWD_R" },
        ["HS787_FuelPump_RAft"] = new[] { "FUEL_PUMP_AFT_R" },
        ["HS787_FuelPump_CtrL"] = new[] { "FUEL_PUMP_CENTER_L" },
        ["HS787_FuelPump_CtrR"] = new[] { "FUEL_PUMP_CENTER_R" },

        // Fuel crossfeed — single valve in the WT model (panel exposes 3 logical
        // groupings but the model only has one valve; all three combos drive it).
        // Single crossfeed valve in the WT 787 model. The cockpit panel may show three
        // logical switches (Fwd/Aft/Ctr) but all drive the same B:FUEL_VALVE_XFEED.
        ["HS787_FuelXfeed"]     = new[] { "FUEL_VALVE_XFEED" },
    };

    /// <summary>
    /// Tries each candidate InputEvent name in order; fires the first one present in the
    /// catalog and returns true. Returns false if none match — caller should fall back.
    /// </summary>
    private static bool TryFireInputEvent(
        SimConnect.SimConnectManager simConnect, double value, string[] candidates)
    {
        foreach (var name in candidates)
        {
            if (simConnect.HasInputEvent(name) && simConnect.TrySetInputEvent(name, value))
                return true;
        }
        return false;
    }

    // 787 MCP uses direct-set dialogs (same as PMDG 777)
    public override FCUControlType GetAltitudeControlType()       => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType()        => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType()          => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType()  => FCUControlType.SetValue;

    // No button state mapping needed — 787 uses standard toggle logic
    public override Dictionary<string, string> GetButtonStateMapping() =>
        new Dictionary<string, string>();

    // Read-only display values rendered as a status field at the bottom of the panel
    // (not as a control row). Used for live numeric indicators the pilot reads but
    // does not set: hydraulic system pressures, cabin altitude/differential, thrust
    // reverser deployment percentages, baro setting, landing altitude, FMC status
    // (EXEC light, TOGA, FMS phase — all written by the FMC, not the pilot).
    public override Dictionary<string, List<string>> GetPanelDisplayVariables() =>
        new Dictionary<string, List<string>>
        {
            ["Hydraulics"]    = new List<string> { "HS787_HydPress1", "HS787_HydPress2", "HS787_HydPress3" },
            ["Pressurization"] = new List<string> { "HS787_PressLdgAlt", "HS787_CabinAltitude", "HS787_CabinPressureLevel" },
            ["Landing"]       = new List<string> { "HS787_ReverseNozzle1", "HS787_ReverseNozzle2" },
            ["EFIS"]          = new List<string> { "HS787_BaroSetting" },
            // IRS: read-only indicators. HS787_IRS_Align is the true,
            // Realistic-respecting alignment state (bridge-sourced); the
            // Position vars are the WT_IRS_POS_SET "position accepted" flags.
            ["IRS"]           = new List<string>
            {
                "HS787_IRS_Align", "HS787_IRS_AlignMinutes",
                "HS787_IRS_Aligned1", "HS787_IRS_Aligned2"
            },
            ["FMC Status"]    = new List<string>
            {
                "HS787_EXECActive", "HS787_TOGA", "HS787_FmsPhase",
                "HS787_ApproachIls", "HS787_ApproachCourse",
                "HS787_FltTimerMode", "HS787_FltTimerRunning", "HS787_FltTimerValue",
                "HS787_ChecklistPhase"
            },
            // Annunciators: status indicators the pilot reads, never sets. Press F5
            // in the display field to force-refresh OnRequest values if needed.
            ["Annunciators"]  = new List<string>
            {
                "HS787_MasterCaution", "HS787_MasterWarning", "HS787_StallWarning",
                "HS787_IrsOnBat", "HS787_AcBusEnergized"
            },
            // Fire detection: warning lights, never user-toggled.
            ["Fire"]          = new List<string>
            {
                "HS787_EngFire1", "HS787_EngFire2",
                "HS787_FireBottleDisch1", "HS787_FireBottleDisch2"
            },
            // Radio + Transponder intentionally NOT here. PMDG-style: each variable
            // (active freq button label, standby textbox, swap button) is its own
            // control row; announcements fire from ProcessSimVarUpdate on change.
            // The ReadSquawkCode hotkey handles on-demand squawk readback.
        };

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
                "Bleed Air",
                "Air Conditioning",
                "Pressurization",
                "Cooling",
                "Anti-Ice",
                "Signs",
                "Flight Controls",
                "Engines",
                "Fire Protection",
                "Cargo Fire"
            },
            ["Glareshield"] = new List<string>
            {
                "EFIS",
                "MCP",
                "HUD",
                "FMC Status",
                "Annunciators",
                "Warnings",
                "Fire"
            },
            ["Pedestal"] = new List<string>
            {
                "Radio",
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
        var aircraftVariables = new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // -----------------------------------------------------------------
            // OVERHEAD — Electrical
            // -----------------------------------------------------------------

            // Now that AIRLINER_EXT_PWR_N InputEvents drive the real switch state,
            // these are On/Off combo boxes that READ the actual delivered-power SimVar
            // (EXTERNAL POWER ON:N) directly. Writes route through the InputEvent map
            // (HS787_INPUT_EVENT_MAP) — no momentary press hack needed.
            // IsAnnounced = false here because the dedicated state vars HS787_ExtPwrOn1/2
            // (below) already publish "External Power N On/Off" through ProcessSimVarUpdate.
            // IsAnnounced=true is required for the monitoring engine to keep these in
            // the continuous batch, so the combo's displayed value tracks the sim from
            // the moment MSFSBA connects. Announcements are suppressed in the cache-only
            // switch at the bottom of ProcessSimVarUpdate — HS787_ExtPwrOn1/2 already
            // publishes "External Power N On/Off" so duplicating that here would double-talk.
            ["HS787_ExtPwr1"] = new SimConnect.SimVarDefinition
            {
                Name = "EXTERNAL POWER ON:1",
                DisplayName = "Ground Power 1",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            ["HS787_ExtPwr2"] = new SimConnect.SimVarDefinition
            {
                Name = "EXTERNAL POWER ON:2",
                DisplayName = "Ground Power 2",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // Note: ExcludeFromBatch = true routes these through per-var continuous
            // subscriptions instead of the shared GenericBatch struct. Required because
            // the batched read was delivering wrong/oscillating values for these specific
            // vars (likely batch-struct alignment drift), producing the spurious
            // On→Off cascade announce.
            ["HS787_BatSwitch1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELECTRICAL MASTER BATTERY:1",
                DisplayName = "Battery 1",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
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

            // CORRECTION: WT_IRS_POS_SET_N is NOT "alignment complete". The WT 787
            // IrsSystem sets this L-var from `isPositionInit` — true the moment the
            // IRS *accepts a position* (GPS auto-init, ~within a minute), regardless
            // of the Realistic align-time setting. True alignment (operating mode
            // Navigation / the "TIME TO ALIGN" countdown) lives only on the WT
            // internal Coherent bus and is NOT exposed as any L-var. So this var
            // is honestly a "position accepted" flag, not "aligned". Real
            // realistic-alignment status is sourced via the MFD bridge instead
            // (see HS787_IRS_Align below / hs787-mfd-bridge.js irs_align scrape).
            // These are read-only (the IRS system owns them) — exposed via
            // GetPanelDisplayVariables, NOT BuildPanelControls, so they render as
            // a read-only status field, not an editable combo.
            ["HS787_IRS_Aligned1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_IRS_POS_SET_1",
                DisplayName = "IRS Left Position",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                // Individual SimConnect subscription so the panel Refresh button's
                // RequestVariable(ONCE) call gets a per-var data definition to query.
                // Batched continuous vars share a struct slot and have no per-var
                // data def, so RequestVariable is a silent no-op and the on-demand
                // display path times out with "--" until a value-CHANGE event fires.
                ExcludeFromBatch = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "No position",
                    [1] = "Position set"
                }
            },

            ["HS787_IRS_Aligned2"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_IRS_POS_SET_2",
                DisplayName = "IRS Right Position",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "No position",
                    [1] = "Position set"
                }
            },

            // True IRS alignment status, sourced from the MFD bridge scraping the
            // WT "TIME TO ALIGN" element (the only place real, Realistic-respecting
            // alignment state is exposed). Read-only display. Values:
            //   0 = Off / unknown   1 = Aligning   2 = Aligned (Navigation)
            // The minutes-remaining detail is carried separately for the hotkey
            // readout; this var drives the panel field + the completion announce.
            ["HS787_IRS_Align"] = new SimConnect.SimVarDefinition
            {
                Name = "MSFSBA_IRS_ALIGN_STATE",   // synthetic L-var written by the bridge feed
                DisplayName = "IRS Alignment",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Aligning",
                    [2] = "Aligned"
                }
            },

            // Minutes remaining in the WT "TIME TO ALIGN" countdown (bridge feed).
            // -1 = not aligning / unknown. Cache-only (silent) — surfaced in the
            // read-only IRS display field so the pilot can check time remaining on
            // demand without a noisy per-minute callout. The completion is
            // announced by HS787_IRS_Align transitioning to Aligned.
            ["HS787_IRS_AlignMinutes"] = new SimConnect.SimVarDefinition
            {
                Name = "MSFSBA_IRS_ALIGN_MINUTES",
                DisplayName = "IRS Time To Align (min)",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
                ValueDescriptions = new Dictionary<double, string> { [-1] = "n/a" }
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            },

            ["HS787_HeatCargo"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Heat_Cargo",
                DisplayName = "Cargo Temp Target",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            },

            ["HS787_HeatFltDeck"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Heat_FltDeck",
                DisplayName = "Flight Deck Temp Target",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            },

            // -----------------------------------------------------------------
            // OVERHEAD — Anti-Ice
            // -----------------------------------------------------------------

            ["HS787_WshldDeice1"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_DeiceWindshield:1",
                DisplayName = "Window Heat 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_WshldDeice2"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_DeiceWindshield:2",
                DisplayName = "Window Heat 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_WshldDeice3"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_DeiceWindshield:3",
                DisplayName = "Window Heat 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_WshldDeice4"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_DeiceWindshield:4",
                DisplayName = "Window Heat 4",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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

            // Engine autostart procedure — the WT/Asobo 787 exposes a single B:PROCEDURE_AUTOSTART
            // InputEvent that runs the full automatic start sequence for both engines
            // (the cockpit "AUTO START" button on the engine panel). FADEC walks both
            // engines through the start states and brings them to running. This is the
            // accessible equivalent of MSFS Ctrl+E.
            ["HS787_EngineAutoStart"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_ENG_START_STATE_1",  // read-only mirror; write path = InputEvent
                DisplayName = "Auto Start Both Engines",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                StateVariable = "HS787_EngStartState1"
            },

            // Per-engine start selectors. The WT 787's start switch is a rotary
            // InputEvent (B:ENGINE_STARTER_N) that accepts a position value. Combined
            // with fuel control set to Run, putting the selector in Auto or Ground arms
            // FADEC to walk the engine to running state. Values:
            //   0 = Off, 1 = Auto, 2 = Ground (start)
            // Exact value semantics aren't documented; user can pick between Auto and
            // Ground depending on what the loaded model accepts.
            ["HS787_EngineStart1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_ENG_START_STATE_1",  // read-only mirror; write path = InputEvent
                DisplayName = "Engine 1 Start Selector",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto",
                    [2] = "Ground"
                }
            },
            ["HS787_EngineStart2"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_ENG_START_STATE_2",
                DisplayName = "Engine 2 Start Selector",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto",
                    [2] = "Ground"
                }
            },

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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
            // Writable via the AUTOPILOT_APPROACH_BUTTON InputEvent (see HS787_INPUT_EVENT_MAP);
            // the K-event AP_APR_HOLD is the fallback for non-WT models.
            ["HS787_APP"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_APR_ARMED",
                DisplayName = "Approach",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Armed"
                }
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
            // Writable via the AUTOPILOT_LOCALIZER_BUTTON InputEvent (see HS787_INPUT_EVENT_MAP).
            ["HS787_LOC"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_LOC_ARMED",
                DisplayName = "LOC",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                // Boeing TCAS/transponder mode positions: STBY, ALT OFF, XPNDR, TA, TA/RA.
                // Matches the spoken phrasing in ProcessSimVarUpdate (line ~5325).
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Standby",
                    [1] = "Alt Off",
                    [2] = "XPNDR",
                    [3] = "TA",
                    [4] = "TA/RA"
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                Name = "EXIT OPEN:0",
                DisplayName = "Door 1L (Fwd Left)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_1R"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:1",
                DisplayName = "Door 1R (Fwd Right)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_2L"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:2",
                DisplayName = "Door 2L (Mid Left)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_2R"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:3",
                DisplayName = "Door 2R (Mid Right)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_3L"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:4",
                DisplayName = "Door 3L (Rear Left)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_3R"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:5",
                DisplayName = "Door 3R (Rear Right)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_4L"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:6",
                DisplayName = "Door 4L (Far Rear Left)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_4R"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:7",
                DisplayName = "Door 4R (Far Rear Right)",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_FwdCargo"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:8",
                DisplayName = "Fwd Cargo Door",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_Door_AftCargo"] = new SimConnect.SimVarDefinition
            {
                Name = "EXIT OPEN:9",
                DisplayName = "Aft Cargo Door",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Closed",
                    [100] = "Open"
                }
            },

            ["HS787_GPUPipe"] = new SimConnect.SimVarDefinition
            {
                Name = "INTERACTIVE POINT OPEN:12",
                DisplayName = "GPU Cable",
                Type = SimConnect.SimVarType.SimVar,
                Units = "percent",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0]   = "Disconnected",
                    [100] = "Connected"
                }
            },

            // =====================================================================
            // BridgeVersion 18+ — additions discovered via live L-var probing
            // against running FS2024 + grep of horizonsim-aircraft-787-9 instrument
            // JS. Each variable below was confirmed to return sensible values in
            // cold-and-dark at the gate. All work on FS2020 too (same WT Boeing
            // base avionics).
            // =====================================================================

            // --- Annunciators (cockpit alert lights) ---
            ["HS787_MasterCaution"] = new SimConnect.SimVarDefinition
            {
                Name = "Generic_Master_Caution_Active",
                DisplayName = "Master Caution",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_MasterWarning"] = new SimConnect.SimVarDefinition
            {
                Name = "Generic_Master_Warning_Active",
                DisplayName = "Master Warning",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_StallWarning"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_787_STALL_WARNING",
                DisplayName = "Stall Warning",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Inactive",
                    [1] = "STALL"
                }
            },

            ["HS787_IrsOnBat"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Irs_On_Bat",
                DisplayName = "IRS On Battery",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Normal",
                    [1] = "IRS on battery"
                }
            },

            ["HS787_AcBusEnergized"] = new SimConnect.SimVarDefinition
            {
                Name = "B78_AC_BUS_ENERGIZED",
                DisplayName = "AC Bus Energized",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // --- Overhead extras (cooling, emer lights cover, light master, EFB power) ---
            ["HS787_CoolingAft"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Cooling_Aft",
                DisplayName = "Aft Equipment Cooling",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto"
                }
            },

            ["HS787_EquipFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Equip_Fwd",
                DisplayName = "Fwd Equipment Cooling",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto"
                }
            },

            ["HS787_EmerLightsCover"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_ELECTRICAL_EmerLights_Cover_Opened",
                DisplayName = "Emergency Lights Cover",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Closed",
                    [1] = "Open"
                }
            },

            ["HS787_LightMaster"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_LightMasterActive",
                DisplayName = "Light Master",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_EfbPower"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_EFB_POWER",
                DisplayName = "EFB Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_FlightComputerAuto"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_78_FLIGHT_COMPUTER_AUTO",
                DisplayName = "Flight Computer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Manual",
                    [1] = "Auto"
                }
            },

            ["HS787_PacksAuto"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_78_PACKS_ON",
                DisplayName = "Packs Master",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_FdDoorPower"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_FdDoor_Power",
                DisplayName = "Flight Deck Door Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // --- Pressurization ---
            ["HS787_PressLdgAlt"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_PRESS_LDG_ALT",
                DisplayName = "Landing Altitude",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "feet"
            },

            ["HS787_PressManAltOn"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_PRESS_MAN_ALT_ON",
                DisplayName = "Manual Pressurization",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Auto",
                    [1] = "Manual"
                }
            },

            // --- Status / background (no announcement, useful for hotkey reads) ---
            ["HS787_FlightStarted"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_Flight_Started",
                DisplayName = "Flight Started",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "No",
                    [1] = "Yes"
                }
            },

            // =====================================================================
            // BridgeVersion 19+ — second batch.
            //   - HUD panel (Captain + F/O HUD stow position + auto-brightness)
            //   - ADIRU air-data source knobs (added to IRS panel)
            //   - Auto Backlight and NextGen Flight Plan: announce-only (no panel),
            //     transitions spoken via custom handlers.
            // All values confirmed via live probe against running FS2024.
            // =====================================================================

            ["HS787_HudDown1"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_HUD_1_Down",
                DisplayName = "HUD Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Stowed",
                    [1] = "Deployed"
                }
            },

            ["HS787_HudDown2"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_HUD_2_Down",
                DisplayName = "HUD First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Stowed",
                    [1] = "Deployed"
                }
            },

            ["HS787_HudAutoBrt1"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_10_Hud_Brightness_Use_Auto:1",
                DisplayName = "HUD Captain Auto Brightness",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Manual",
                    [1] = "Auto"
                }
            },

            ["HS787_HudAutoBrt2"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_10_Hud_Brightness_Use_Auto:2",
                DisplayName = "HUD First Officer Auto Brightness",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Manual",
                    [1] = "Auto"
                }
            },

            ["HS787_AirDataSrc1"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Air_Data_Att_Source_Knob_State:1",
                DisplayName = "Air Data Source Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Normal",
                    [1] = "Alternate"
                }
            },

            ["HS787_AirDataSrc2"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Air_Data_Att_Source_Knob_State:2",
                DisplayName = "Air Data Source First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Normal",
                    [1] = "Alternate"
                }
            },

            // Background-only auto-announce (no panel; transitions spoken via handler).
            ["HS787_AutoBacklight"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_787_Auto_Backlight",
                DisplayName = "Auto Backlight",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_NextGenFP"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_NEXTGEN_FLIGHTPLAN_ENABLED",
                DisplayName = "NextGen Flight Plan",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // =====================================================================
            // Batch 3 — AP mode auto-announce, approach state, flight timer,
            // checklist phase, HUD symbology declutter. All probed live in FS2024.
            // =====================================================================

            // --- AP modes (background announce on activation/deactivation) ---
            ["HS787_ApAltHold"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_ALT_HOLD_ACTIVE",
                DisplayName = "AP Alt Hold",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            ["HS787_ApFlch"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_FLCH_ACTIVE",
                DisplayName = "AP FLCH",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            ["HS787_ApVs"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_VS_ACTIVE",
                DisplayName = "AP V/S",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            ["HS787_ApSpd"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_SPD_ACTIVE",
                DisplayName = "AP Speed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            ["HS787_ApThr"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_THR_ACTIVE",
                DisplayName = "AP Throttle",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            ["HS787_ApHdgHold"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_HEADING_LOCK_ACTIVE",
                DisplayName = "AP HDG Hold",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            ["HS787_ApHdgSel"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_HEADING_SELECT_ACTIVE",
                DisplayName = "AP HDG Sel",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            ["HS787_ApClbCon"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_CLBCON_ACTIVE",
                DisplayName = "AP CLB CON",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Active"
                }
            },

            // --- Approach state ---
            ["HS787_ApproachIls"] = new SimConnect.SimVarDefinition
            {
                Name = "FLIGHTPLAN_APPROACH_ILS",
                DisplayName = "ILS Approach Armed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "No",
                    [1] = "Yes"
                }
            },

            ["HS787_ApproachCourse"] = new SimConnect.SimVarDefinition
            {
                Name = "FLIGHTPLAN_APPROACH_COURSE",
                DisplayName = "Approach Course",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "degrees"
            },

            // --- Flight timer ---
            ["HS787_FltTimerMode"] = new SimConnect.SimVarDefinition
            {
                Name = "WTFltTimer_Mode",
                DisplayName = "Flight Timer Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Count up",
                    [1] = "Count down",
                    [2] = "ET"
                }
            },

            ["HS787_FltTimerRunning"] = new SimConnect.SimVarDefinition
            {
                Name = "WTFltTimer_Running",
                DisplayName = "Flight Timer Running",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Stopped",
                    [1] = "Running"
                }
            },

            // Timer value updates every second. OnRequest so it doesn't spam announcements
            // — read on demand via hotkey instead.
            ["HS787_FltTimerValue"] = new SimConnect.SimVarDefinition
            {
                Name = "WTFltTimer_Value",
                DisplayName = "Flight Timer Value",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "seconds"
            },

            // --- Checklist phase (integer; announces each phase change) ---
            ["HS787_ChecklistPhase"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_AVIONICS_CHECKLIST_AUTOCOMPLETE_PHASE",
                DisplayName = "Checklist Phase",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            },

            // --- HUD symbology declutter (added to HUD panel) ---
            ["HS787_HudSymbology1"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Hud_Symbology:1",
                DisplayName = "HUD Captain Symbology",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Full",
                    [1] = "Decluttered"
                }
            },

            ["HS787_HudSymbology2"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Hud_Symbology:2",
                DisplayName = "HUD First Officer Symbology",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Full",
                    [1] = "Decluttered"
                }
            },

            ["HS787_HudDeclutterInhibit1"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Hud_Symbology_Decluttered_Inhibit:1",
                DisplayName = "HUD Captain Declutter Inhibit",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_HudDeclutterInhibit2"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_Hud_Symbology_Decluttered_Inhibit:2",
                DisplayName = "HUD First Officer Declutter Inhibit",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // =====================================================================
            // Batch 4 — safety-critical fire detection + cabin/hydraulic monitoring.
            // Standard MSFS SimVars (not L-vars). Confirmed via RPN ferry probe.
            // Engine fire announcements are SAFETY CRITICAL — they fire even on
            // first poll (no -1 suppression) so a fire at startup still announces.
            // =====================================================================

            ["HS787_EngFire1"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG ON FIRE:1",
                DisplayName = "Engine 1 Fire",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "No fire",
                    [1] = "FIRE"
                }
            },

            ["HS787_EngFire2"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG ON FIRE:2",
                DisplayName = "Engine 2 Fire",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "No fire",
                    [1] = "FIRE"
                }
            },

            ["HS787_FireBottleDisch1"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE BOTTLE DISCHARGED:1",
                DisplayName = "Fire Bottle 1",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Armed",
                    [1] = "Discharged"
                }
            },

            ["HS787_FireBottleDisch2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE BOTTLE DISCHARGED:2",
                DisplayName = "Fire Bottle 2",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Armed",
                    [1] = "Discharged"
                }
            },

            // --- Cabin / pressurization indicators (read-only, on demand) ---
            ["HS787_CabinAltitude"] = new SimConnect.SimVarDefinition
            {
                Name = "CABIN ALTITUDE",
                DisplayName = "Cabin Altitude",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "feet"
            },

            ["HS787_CabinPressureLevel"] = new SimConnect.SimVarDefinition
            {
                Name = "CABIN PRESSURE LEVEL",
                DisplayName = "Cabin Pressure Differential",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "psi"
            },

            // --- Hydraulic pressure (system 1=Left, 2=Center, 3=Right) ---
            ["HS787_HydPress1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYDRAULIC PRESSURE:1",
                DisplayName = "Hydraulic Pressure Left",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "psf"
            },

            ["HS787_HydPress2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYDRAULIC PRESSURE:2",
                DisplayName = "Hydraulic Pressure Center",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "psf"
            },

            ["HS787_HydPress3"] = new SimConnect.SimVarDefinition
            {
                Name = "HYDRAULIC PRESSURE:3",
                DisplayName = "Hydraulic Pressure Right",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "psf"
            },

            // --- Thrust reverser position (% deployed) ---
            ["HS787_ReverseNozzle1"] = new SimConnect.SimVarDefinition
            {
                Name = "TURB ENG REVERSE NOZZLE PERCENT:1",
                DisplayName = "Reverser 1",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "percent"
            },

            ["HS787_ReverseNozzle2"] = new SimConnect.SimVarDefinition
            {
                Name = "TURB ENG REVERSE NOZZLE PERCENT:2",
                DisplayName = "Reverser 2",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                Units = "percent"
            },

            // --- Baro setting raw value (in current display unit, hPa or inHg) ---
            ["HS787_BaroSetting"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Baro",
                DisplayName = "Baro Setting",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            },

            // =====================================================================
            // Batch 5 — yaw damper, antiskid, avionics master, pitot heat, interior
            // cockpit lighting bus switches. Standard SimVars; live-confirmed.
            // =====================================================================

            ["HS787_YawDamper"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT YAW DAMPER",
                DisplayName = "Yaw Damper",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_AntiSkid"] = new SimConnect.SimVarDefinition
            {
                Name = "ANTISKID BRAKES ACTIVE",
                DisplayName = "Antiskid",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Armed"
                }
            },

            ["HS787_AvionicsMaster"] = new SimConnect.SimVarDefinition
            {
                Name = "AVIONICS MASTER SWITCH",
                DisplayName = "Avionics Master",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromBatch = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            ["HS787_PitotHeat"] = new SimConnect.SimVarDefinition
            {
                Name = "PITOT HEAT",
                DisplayName = "Pitot Heat",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "On"
                }
            },

            // Interior cockpit lighting bus switches (LIGHT PANEL, LIGHT INSTRUMENT,
            // LIGHT GLARESHIELD, LIGHT PEDESTRAL, LIGHT CABIN, LIGHT DOME) were
            // briefly added in batch 5 but pulled. They cluster alphabetically with
            // the working LIGHT BEACON / LANDING / NAV / STROBE / TAXI / WING /
            // LOGO vars in the continuous-monitoring sort, and adding them appears
            // to cause SimConnect data-definition slot misalignment for the whole
            // LIGHT * group. Not re-introducing without a clean fix for the
            // monitoring pipeline.

            // =====================================================================
            // Batch 6 — Radio panel (COM1 / COM2) + transponder squawk.
            // Layout mimics PMDG 777: each radio row has Active (display button),
            // Standby (TextBox + Set), Swap (button). Each SimVar is continuously
            // monitored — when the value changes (post-swap, post-set, or via VR
            // hand), ProcessSimVarUpdate announces the new frequency.
            //
            // MainForm rendering: keys with "_SET" + COM_ prefix render as TextBox
            // + Set button. PreventTextInput on the Active key forces a button
            // render. K-event firing for SET / SWAP goes through MainForm's existing
            // generic handling (COM_STBY_RADIO_SET_HZ, COM_STBY_RADIO_SWAP, etc.).
            // =====================================================================

            ["COM1_ActiveFreq"] = new SimConnect.SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:1",
                DisplayName = "COM1 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            },

            ["COM_STANDBY_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:1",
                DisplayName = "COM1 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["COM1_RADIO_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "COM_STBY_RADIO_SWAP",
                DisplayName = "COM1 Swap",
                Type = SimConnect.SimVarType.Event,
                RenderAsButton = true,
                IsMomentary = true,
                HelpText = "Swap COM1 active and standby frequencies"
            },

            ["COM2_ActiveFreq"] = new SimConnect.SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:2",
                DisplayName = "COM2 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            },

            ["COM_STANDBY_FREQUENCY_SET:2"] = new SimConnect.SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:2",
                DisplayName = "COM2 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            ["COM2_RADIO_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "COM2_RADIO_SWAP",
                DisplayName = "COM2 Swap",
                Type = SimConnect.SimVarType.Event,
                RenderAsButton = true,
                IsMomentary = true,
                HelpText = "Swap COM2 active and standby frequencies"
            },

            // Transponder squawk: the var doubles as text-input (key contains "_SET")
            // and as the live SimVar reading. Units=Bco16 so SimVar reads come back as
            // BCD16 (0x2000 = squawk 2000) — the announce handler can BCD-decode
            // cleanly. Without this MSFS returns the raw decimal int and the decoder
            // mis-shifts (squawk 2000 -> raw 2000 -> "07130").
            ["TRANSPONDER_CODE_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "TRANSPONDER CODE:1",
                DisplayName = "Squawk",
                Type = SimConnect.SimVarType.SimVar,
                Units = "Bco16",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // Warnings panel — momentary reset buttons for Master Caution / Master Warning.
            // K:MASTER_CAUTION_ACKNOWLEDGE drives L:Generic_Master_Caution_Active to 0;
            // K:MASTER_WARNING_ACKNOWLEDGE does the same for the warning. Name is a synthetic
            // marker L-var (the var is write-only from a panel-UI standpoint — never read).
            // StateVariable points at the underlying caution/warning so the button's label
            // reflects whether there's actually anything to acknowledge ("Master Caution Reset:
            // On" when caution is lit, "...: Off" when clear).
            ["HS787_MasterCautionReset"] = new SimConnect.SimVarDefinition
            {
                Name = "MSFSBA_HS787_MASTER_CAUTION_RESET_SYNTH",
                DisplayName = "Master Caution Reset",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                StateVariable = "HS787_MasterCaution"
            },
            ["HS787_MasterWarningReset"] = new SimConnect.SimVarDefinition
            {
                Name = "MSFSBA_HS787_MASTER_WARNING_RESET_SYNTH",
                DisplayName = "Master Warning Reset",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                StateVariable = "HS787_MasterWarning"
            },

            // Bridge diagnostic: written by L:MSFSBA_787_STAGE in hs787-mfd-bridge.js.
            // IsAnnounced = true puts it in the continuous batch; ProcessSimVarUpdate handles it silently.
            ["HS787_BridgeStage"] = new SimConnect.SimVarDefinition
            {
                Name = "MSFSBA_787_STAGE",
                DisplayName = "Bridge Stage",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // =====================================================================
            // Batch 8 — Fuel pumps, crossfeeds, bleed air, fire test, cargo fire,
            // engine/APU fire handles, standby power. Standard MSFS SimVars are
            // used where available (FUELSYSTEM PUMP SWITCH:N, BLEED AIR ENGINE:N,
            // APU BLEED AIR SWITCH) so the write path uses confirmed-working K
            // events (FUELSYSTEM_PUMP_TOGGLE, etc.). L-vars used for items the
            // standard SimVar layer doesn't expose (fire handles, fire test).
            // =====================================================================

            // ---- Fuel pumps (FUELSYSTEM PUMP SWITCH:N, K:FUELSYSTEM_PUMP_TOGGLE) ----
            // WT/Asobo 787 FUELSYSTEM PUMP SWITCH indices (verified empirically by firing
            // each FUEL_PUMP_* InputEvent and observing which SimVar index flipped):
            //   1: Center L   2: Center R   3: L Aft   4: L Fwd   5: R Aft   6: R Fwd   7: APU DC
            // Unusual order (not the generic-jet systems.cfg layout), so an obvious-looking
            // 1=LFwd ... 6=CtrR assumption gets the labels crossed.
            ["HS787_FuelPump_CtrL"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP SWITCH:1",
                DisplayName = "Left Center Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_FuelPump_CtrR"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP SWITCH:2",
                DisplayName = "Right Center Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_FuelPump_LAft"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP SWITCH:3",
                DisplayName = "Left Aft Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_FuelPump_LFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP SWITCH:4",
                DisplayName = "Left Forward Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_FuelPump_RAft"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP SWITCH:5",
                DisplayName = "Right Aft Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_FuelPump_RFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP SWITCH:6",
                DisplayName = "Right Forward Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_FuelPump_APU"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM PUMP SWITCH:7",
                DisplayName = "APU Fuel Pump",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // ---- Fuel crossfeed valve ----
            // The WT 787 model has a single crossfeed valve. The legacy panel exposed
            // three combos (Fwd / Aft / Ctr) but only one ever moved the valve and the
            // other two became permanently desynced. Collapsed to one combo reading the
            // truth SimVar; writes go through B:FUEL_VALVE_XFEED via HS787_INPUT_EVENT_MAP.
            ["HS787_FuelXfeed"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Fuel_XFEED_Fwd",
                DisplayName = "Fuel Crossfeed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },

            // ---- Bleed Air ----
            ["HS787_BleedEng1"] = new SimConnect.SimVarDefinition
            {
                Name = "BLEED AIR ENGINE:1",
                DisplayName = "Engine 1 Bleed",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_BleedEng2"] = new SimConnect.SimVarDefinition
            {
                Name = "BLEED AIR ENGINE:2",
                DisplayName = "Engine 2 Bleed",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_BleedAPU"] = new SimConnect.SimVarDefinition
            {
                Name = "APU BLEED AIR SWITCH",
                DisplayName = "APU Bleed",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HS787_BleedIso"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Bleed_Air_Isolation",
                DisplayName = "Bleed Isolation Valve",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },

            // ---- Fire system ----
            // Fire/OVHT Test — render-as-button momentary press: click runs the test
            // (HandleUIVariableSet writes the L-var to 1 then auto-releases to 0 after
            // ~4 seconds so the user gets the "in progress" → "complete" announce pair
            // without having to manually release. ValueDescriptions omitted so the
            // button label is just "Fire/Overheat Test".
            ["HS787_FireTest"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Fire_Test_Pushed",
                DisplayName = "Fire/Overheat Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            // Engine fire handles — pulled to arm fuel cutoff + arm extinguisher bottles.
            ["HS787_EngFireHandle1"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Eng_Fire_Pulled_1",
                DisplayName = "Engine 1 Fire Handle",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Stowed", [1] = "Pulled" }
            },
            ["HS787_EngFireHandle2"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Eng_Fire_Pulled_2",
                DisplayName = "Engine 2 Fire Handle",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Stowed", [1] = "Pulled" }
            },
            ["HS787_APUFireHandle"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_APU_Fire_Pulled",
                DisplayName = "APU Fire Handle",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Stowed", [1] = "Pulled" }
            },

            // ---- Cargo Fire ----
            ["HS787_CargoFireFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Cargo_Fire_Arm_Fwd",
                DisplayName = "Cargo Fire Arm Fwd",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Armed" }
            },
            ["HS787_CargoFireAft"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Cargo_Fire_Arm_Aft",
                DisplayName = "Cargo Fire Arm Aft",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Armed" }
            },
            ["HS787_CargoFireDisch"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Cargo_Fire_Discharge",
                DisplayName = "Cargo Fire Discharge",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Discharged" }
            },

            // ---- Standby Power Selector ----
            // Position 0 = Off, 1 = Auto, 2 = Battery
            ["HS787_StandbyPower"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_StandbyPower",
                DisplayName = "Standby Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Off",
                    [1] = "Auto",
                    [2] = "Battery"
                }
            },

            // ---- Baro STD pushbutton (EFIS) ----
            // Real-aircraft behaviour: momentary push. Press once → engage STD (altimeter
            // jumps to 29.92 inHg / 1013.25 hPa, "STD" indicator lights). Press again →
            // return to QNH (the altimeter restores to whatever value was set before STD
            // was engaged; WT cockpit logic handles the restore via the L-var transition).
            // Rendered as a button (RenderAsButton=true) — click toggles. ValueDescriptions
            // give the button its state-suffixed label ("Baro STD: QNH" / "Baro STD: STD").
            ["HS787_BaroSTD"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_Baro_Selector_STD_1",
                DisplayName = "Baro STD",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "QNH",
                    [1] = "STD"
                }
            },

            // ---- Engine Fuel Control switches (RUN / CUTOFF) ----
            // Read from FUELSYSTEM VALVE OPEN:N — the WT 787's actual fuel valve state.
            // The more obvious-looking GENERAL ENG FUEL VALVE:N is unreliable on this
            // model: it sits at 1 even with the cockpit switch in Cutoff and the engines
            // shut down, so the combo would default to "Run" at cold-and-dark and mislead
            // the user. Write path = B:FUEL_VALVE_L / FUEL_VALVE_R InputEvent (see
            // HS787_INPUT_EVENT_MAP); MIXTUREn_RICH/LEAN is the K-event fallback only.
            ["HS787_FuelControl1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM VALVE OPEN:1",
                DisplayName = "Engine 1 Fuel Control",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Cutoff", [1] = "Run" }
            },
            ["HS787_FuelControl2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUELSYSTEM VALVE OPEN:2",
                DisplayName = "Engine 2 Fuel Control",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Cutoff", [1] = "Run" }
            }
        };

        var variables = GetBaseVariables();
        foreach (var kvp in aircraftVariables)
            variables[kvp.Key] = kvp.Value;

        // Force ALL HS787 Continuous+IsAnnounced variables onto per-var continuous
        // subscriptions (ExcludeFromBatch = true) rather than the shared GenericBatch
        // struct. The batched read was observed to deliver wrong/oscillating values for
        // certain slots in the HS787 var list under FS2024, producing the spurious
        // On→Off cascade for batteries/generators/avionics master. Likely cause: a
        // silent AddToDataDefinition failure earlier in the alphabetic sort, shifting
        // subsequent vars' SimConnect-side slots out of sync with MSFSBA's
        // continuousVariableIndexMap. Per-var subscriptions use each var's own
        // SingleValue data def — no struct alignment to worry about. Performance cost
        // is small (~135 individual SECOND-period subscriptions vs 2 batch reads). The
        // batch system still works fine for other aircraft (FBW, Fenix, PMDG); this
        // bypass is HS787-specific.
        foreach (var kvp in aircraftVariables)
        {
            var v = kvp.Value;
            if (v.UpdateFrequency == SimConnect.UpdateFrequency.Continuous && v.IsAnnounced)
                v.ExcludeFromBatch = true;
        }

        return variables;
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
                "HS787_AvionicsMaster",
                "HS787_StandbyPower",
                "HS787_EmerLights",
                "HS787_UtilityCabin",
                "HS787_UtilityIfe"
            },
            ["IRS"] = new List<string>
            {
                // Knobs are user controls. The aligned/position indicators are
                // read-only — moved to GetPanelDisplayVariables so they render
                // as a status field, not an editable combo.
                "HS787_IRS_Knob1",
                "HS787_IRS_Knob2",
                "HS787_AirDataSrc1",
                "HS787_AirDataSrc2"
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
                "HS787_FuelPump_LFwd",
                "HS787_FuelPump_RFwd",
                "HS787_FuelPump_LAft",
                "HS787_FuelPump_RAft",
                "HS787_FuelPump_CtrL",
                "HS787_FuelPump_CtrR",
                "HS787_FuelPump_APU",
                "HS787_FuelXfeed",
                "HS787_FuelBalance",
                "HS787_FuelBalanceActive",
                "HS787_FuelBalanceFault"
            },
            ["Bleed Air"] = new List<string>
            {
                "HS787_BleedEng1",
                "HS787_BleedEng2",
                "HS787_BleedAPU",
                "HS787_BleedIso"
            },
            ["Fire Protection"] = new List<string>
            {
                "HS787_FireTest",
                "HS787_EngFireHandle1",
                "HS787_EngFireHandle2",
                "HS787_APUFireHandle"
            },
            ["Cargo Fire"] = new List<string>
            {
                "HS787_CargoFireFwd",
                "HS787_CargoFireAft",
                "HS787_CargoFireDisch"
            },
            ["Air Conditioning"] = new List<string>
            {
                "HS787_PackL",
                "HS787_PackR",
                "HS787_PacksAuto",
                "HS787_TrimAirL",
                "HS787_TrimAirR",
                "HS787_RecircUpper",
                "HS787_RecircLower"
            },
            ["Pressurization"] = new List<string>
            {
                "HS787_PressManAltOn"
            },
            ["Cooling"] = new List<string>
            {
                "HS787_CoolingAft",
                "HS787_EquipFwd",
                "HS787_EmerLightsCover"
            },
            ["Anti-Ice"] = new List<string>
            {
                "HS787_AntiIceEng1",
                "HS787_AntiIceEng2",
                "HS787_AntiIceWing",
                "HS787_PitotHeat",
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
                "HS787_AltnFlapsSelector",
                "HS787_FlightComputerAuto"
            },
            ["Engines"] = new List<string>
            {
                "HS787_EngineAutoStart",
                "HS787_EngineStart1",
                "HS787_EngineStart2",
                "HS787_FuelControl1",
                "HS787_FuelControl2"
            },

            // --- Glareshield ---
            ["EFIS"] = new List<string>
            {
                "HS787_BaroSelector",
                "HS787_BaroSTD",
                "HS787_MinsMode",
                "HS787_FPVMode"
            },
            ["MCP"] = new List<string>
            {
                "HS787_APMaster",
                "HS787_ATStatus",
                "HS787_YawDamper",
                "HS787_FPAMode",
                "HS787_TRKMode",
                "HS787_VNAV",
                "HS787_LOC",
                "HS787_APP"
            },
            ["HUD"] = new List<string>
            {
                "HS787_HudDown1",
                "HS787_HudDown2",
                "HS787_HudAutoBrt1",
                "HS787_HudAutoBrt2",
                "HS787_HudSymbology1",
                "HS787_HudSymbology2",
                "HS787_HudDeclutterInhibit1",
                "HS787_HudDeclutterInhibit2"
            },
            // FMC Status: all members are status indicators written by the FMS, not
            // user-toggleable. Rendered as a read-only display via GetPanelDisplayVariables
            // (which exposes them at the bottom of the panel as a status field).
            // Approach Course / Flight Timer Value / Checklist Phase are numeric reads.
            ["FMC Status"] = new List<string>(),
            // Annunciators / Fire: status indicators rendered as a read-only display
            // (see GetPanelDisplayVariables). Empty control list keeps the panel
            // section visible in the section nav.
            ["Annunciators"] = new List<string>(),
            ["Fire"] = new List<string>(),
            // Warnings: momentary reset buttons for the Master Caution / Master Warning.
            // Each button's label is suffixed with the current state (On / Off) so the
            // user can tell at a glance whether there is anything to acknowledge.
            ["Warnings"] = new List<string>
            {
                "HS787_MasterCautionReset",
                "HS787_MasterWarningReset"
            },

            // --- Pedestal ---
            // PMDG layout: per radio, [Active display button], [Standby textbox], [Swap button].
            ["Radio"] = new List<string>
            {
                "COM1_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:1", "COM1_RADIO_SWAP",
                "COM2_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:2", "COM2_RADIO_SWAP"
            },
            ["Transponder"] = new List<string>
            {
                "HS787_TransponderMode",
                "TRANSPONDER_CODE_SET"
            },
            ["Landing"] = new List<string>
            {
                "HS787_ParkBrake",
                "HS787_Autobrake",
                "HS787_AntiSkid",
                "HS787_FlapsHandle",
                "HS787_GearHandle"
            },
            ["Lighting"] = new List<string>
            {
                "HS787_LightMaster",
                "HS787_LightBeacon",
                "HS787_LightStrobe",
                "HS787_LightNav",
                "HS787_LightLanding",
                "HS787_LightTaxi",
                "HS787_LightLogo",
                "HS787_LightWing"
                // Interior light bus switches (Panel, Instrument, Glareshield, Pedestal,
                // Cabin, Dome) removed from the panel because their write events on HS787
                // aren't reliable via the standard MSFS K-events; the SimVars exist and
                // auto-announce on external change (still wired in continuous monitoring).
            },
            ["Options"] = new List<string>
            {
                "HS787_SATCOM",
                "HS787_VBar",
                "HS787_EfbPower"
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
                "HS787_Door_AftCargo",
                "HS787_FdDoorPower"
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
        // InputEvent (B:) first-pass — for switches whose real subsystem is wired through
        // InputEvents in the WT/Asobo 787 model (AT arm, ext power, battery, fuel pumps).
        // If the mapped name is in the catalog we win immediately; otherwise fall through
        // to the per-var K-event/SetLVar branches below so older models (or base sim
        // without HS787 loaded) keep working.
        if (HS787_INPUT_EVENT_MAP.TryGetValue(varKey, out var candidates) &&
            TryFireInputEvent(simConnect, value, candidates))
        {
            return true;
        }

        // AP master — toggle via K event (AUTOPILOT MASTER is a SimVar, not settable via SetLVar)
        if (varKey == "HS787_APMaster")
        {
            simConnect.SendEvent("AP_MASTER");
            return true;
        }

        // Autothrottle arm — fallback when the InputEvent path above didn't match.
        if (varKey == "HS787_ATStatus")
        {
            simConnect.SendEvent("AUTO_THROTTLE_ARM");
            return true;
        }

        // Engine autostart fallback — if PROCEDURE_AUTOSTART isn't in the catalog,
        // fire the standard MSFS ENGINE_AUTO_START K event (the Ctrl+E binding).
        // Works on any aircraft, including the base Asobo 787-10.
        if (varKey == "HS787_EngineAutoStart")
        {
            simConnect.SendEvent("ENGINE_AUTO_START");
            return true;
        }

        // Approach + Localizer fallback — K events for non-WT aircraft.
        if (varKey == "HS787_APP")
        {
            simConnect.SendEvent("AP_APR_HOLD");
            return true;
        }
        if (varKey == "HS787_LOC")
        {
            simConnect.SendEvent("AP_LOC_HOLD");
            return true;
        }

        // External power — state-aware toggle. AIRLINER_EXT_PWR_N is a momentary
        // pushbutton on the WT 787 (press = 1, release = 0; each press flips state).
        // Combo passes target value (0/1); we read current state from EXTERNAL POWER
        // ON:N and only fire the press+release cycle when state needs to change. Falls
        // back to the legacy EXT_PWR_COMMANDED L-var pulse if the InputEvent isn't in
        // the catalog (older models or base Asobo 787). The 200 ms release delay gives
        // the cockpit avionics time to latch the press before we drop it back to 0.
        if (varKey == "HS787_ExtPwr1" || varKey == "HS787_ExtPwr2")
        {
            string stateVar = varKey == "HS787_ExtPwr1" ? "HS787_ExtPwrOn1" : "HS787_ExtPwrOn2";
            int currentState = (int)(simConnect.GetCachedVariableValue(stateVar) ?? 0);
            int targetState  = (int)value;
            if (currentState == targetState)
                return true; // no-op — state already matches request

            string eventName = varKey == "HS787_ExtPwr1" ? "AIRLINER_EXT_PWR_1" : "AIRLINER_EXT_PWR_2";
            bool usedInputEvent = simConnect.HasInputEvent(eventName) &&
                                  simConnect.TrySetInputEvent(eventName, 1);
            if (!usedInputEvent)
            {
                // Fallback path for non-WT models.
                string lvarName = varKey == "HS787_ExtPwr1" ? "EXT_PWR_COMMANDED:1" : "EXT_PWR_COMMANDED:2";
                simConnect.SetLVar(lvarName, 1);
            }

            var releaseTimer = new System.Windows.Forms.Timer { Interval = 200 };
            releaseTimer.Tick += (_, __) =>
            {
                releaseTimer.Stop();
                releaseTimer.Dispose();
                if (usedInputEvent)
                    simConnect.TrySetInputEvent(eventName, 0);
                else
                {
                    string lvarName = varKey == "HS787_ExtPwr1" ? "EXT_PWR_COMMANDED:1" : "EXT_PWR_COMMANDED:2";
                    simConnect.SetLVar(lvarName, 0);
                }
            };
            releaseTimer.Start();
            return true;
        }

        // APU knob — use K:events to drive the WT Boeing APU state machine.
        // Direct LVar writes to XMLVAR_APU_StarterKnob_Pos are ignored by the WT system.
        // K:APU_ON_SWITCH doesn't exist in standard MSFS — the knob's "On" position
        // is reached by firing APU_STARTER (knob goes Start→On after spring release).
        if (varKey == "HS787_APU_Knob")
        {
            string apuEvent = (int)value switch
            {
                2 => "APU_STARTER",
                1 => "APU_STARTER",
                _ => "APU_OFF_SWITCH"
            };
            simConnect.SendEvent(apuEvent);
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

        // Flaps — K:FLAPS_SET is silently ignored on HS787 (WT Boeing intercepts).
        // Walk to the target detent using FLAPS_INCR / FLAPS_DECR. Each event moves
        // by one detent. We fire enough events to reach the desired index. RPN loop
        // would be cleaner but ExecuteCalculatorCode caps string length, so we
        // generate a flat sequence and let SimConnect queue them.
        if (varKey == "HS787_FlapsHandle")
        {
            int target = (int)value;
            double? cur = simConnect.GetCachedVariableValue("HS787_FlapsHandle");
            int from = cur.HasValue ? (int)cur.Value : 0;
            int delta = target - from;
            string evt = delta > 0 ? "FLAPS_INCR" : "FLAPS_DECR";
            int steps = System.Math.Abs(delta);
            for (int i = 0; i < steps; i++)
                simConnect.SendEvent(evt);
            return true;
        }

        // Pitot heat — TOGGLE if state differs.
        if (varKey == "HS787_PitotHeat")
        {
            simConnect.ExecuteCalculatorCode($"(A:PITOT HEAT,Bool) {(int)value} != if{{ (>K:PITOT_HEAT_TOGGLE) }}");
            return true;
        }

        // Yaw damper — YAW_DAMPER_SET takes 0/1 directly.
        if (varKey == "HS787_YawDamper")
        {
            simConnect.SendEvent("YAW_DAMPER_SET", (uint)(int)value);
            return true;
        }

        // Antiskid — TOGGLE if state differs.
        if (varKey == "HS787_AntiSkid")
        {
            simConnect.ExecuteCalculatorCode($"(A:ANTISKID BRAKES ACTIVE,Bool) {(int)value} != if{{ (>K:ANTISKID_BRAKES_TOGGLE) }}");
            return true;
        }

        // Avionics master — TOGGLE if state differs.
        if (varKey == "HS787_AvionicsMaster")
        {
            simConnect.ExecuteCalculatorCode($"(A:AVIONICS MASTER SWITCH,Bool) {(int)value} != if{{ (>K:TOGGLE_AVIONICS_MASTER) }}");
            return true;
        }

        // ===== Radio: COM standby SET (textbox value in MHz) =====
        // Intercept here so MainForm's generic path doesn't ALSO announce
        // "Standby frequency set to 121.500" (duplicate with our ProcessSimVarUpdate
        // announce of "COM1 standby 121.500" when the SimVar changes a frame later).
        if (varKey == "COM_STANDBY_FREQUENCY_SET:1" || varKey == "COM_STANDBY_FREQUENCY_SET:2")
        {
            if (value < 118.0 || value > 136.975)
            {
                announcer.Announce("Invalid COM frequency. Range: 118.000 to 136.975 MHz.");
                return true;
            }
            uint hz = (uint)System.Math.Round(value * 1_000_000.0);
            string evt = varKey.EndsWith(":2") ? "COM2_STBY_RADIO_SET_HZ" : "COM_STBY_RADIO_SET_HZ";
            simConnect.SendEvent(evt, hz);
            return true; // SimVar change -> ProcessSimVarUpdate fires the spoken announce
        }

        // ===== Radio: COM swap buttons (no "swap pressed" — value-change announce fires post-swap) =====
        if (varKey == "COM1_RADIO_SWAP")
        {
            simConnect.SendEvent("COM_STBY_RADIO_SWAP");
            return true;
        }
        if (varKey == "COM2_RADIO_SWAP")
        {
            simConnect.SendEvent("COM2_RADIO_SWAP");
            return true;
        }

        // TRANSPONDER_CODE_SET stays with MainForm's generic path (it doesn't
        // announce there per the existing comment in MainForm — the announce
        // fires from our ProcessSimVarUpdate handler when the SimVar changes).

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

        // Doors — toggled via K:TOGGLE_AIRCRAFT_EXIT with 1-based index. Internally this
        // operates on EXIT OPEN:N-1 (zero-based) which is what HS787_Door_* now reads.
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
            _                     => null
        };
        if (doorIdx.HasValue)
        {
            simConnect.SendEvent("TOGGLE_AIRCRAFT_EXIT", (uint)doorIdx.Value);
            return true;
        }

        // Interactive points (refuel cable @ 11, GPU cable @ 12) — these are NOT exits.
        // K:TOGGLE_AIRCRAFT_EXIT silently ignores parameters > 10. K:2:SET_INTERACTIVE_POINT
        // works for CONNECT (0→100) but not for DISCONNECT in our testing — direct SimVar
        // write `<percent> (>A:INTERACTIVE POINT OPEN:N, percent)` works in both directions
        // (the cockpit animates the value from current to target over ~1 second).
        int? interactivePointIdx = varKey switch
        {
            "HS787_RefuelDoor" => 11,
            "HS787_GPUPipe"    => 12,
            _                  => null
        };
        if (interactivePointIdx.HasValue)
        {
            int percent = (int)value > 0 ? 100 : 0;
            simConnect.ExecuteCalculatorCode($"{percent} (>A:INTERACTIVE POINT OPEN:{interactivePointIdx.Value}, percent)");
            return true;
        }

        // ===== Fuel pumps — FUELSYSTEM_PUMP_TOGGLE is param-indexed. WT/Asobo 787
        //       has an unusual order: 1=CtrL, 2=CtrR, 3=LAft, 4=LFwd, 5=RAft,
        //       6=RFwd, 7=APU. Verified empirically. TOGGLE flips state, so we
        //       check the current SimVar value and only fire if a flip is needed.
        int? fuelPumpIdx = varKey switch
        {
            "HS787_FuelPump_CtrL" => 1,
            "HS787_FuelPump_CtrR" => 2,
            "HS787_FuelPump_LAft" => 3,
            "HS787_FuelPump_LFwd" => 4,
            "HS787_FuelPump_RAft" => 5,
            "HS787_FuelPump_RFwd" => 6,
            "HS787_FuelPump_APU"  => 7,
            _                     => null
        };
        if (fuelPumpIdx.HasValue)
        {
            double? current = simConnect.GetCachedVariableValue(varKey);
            if (current == null || (int)current.Value != (int)value)
                simConnect.SendEvent("FUELSYSTEM_PUMP_TOGGLE", (uint)fuelPumpIdx.Value);
            return true;
        }

        // ===== Bleed valves =====
        if (varKey == "HS787_BleedEng1")
        {
            simConnect.ExecuteCalculatorCode($"(A:BLEED AIR ENGINE:1,Bool) {(int)value} != if{{ 1 (>K:TOGGLE_BLEED_AIR_SOURCE) }}");
            return true;
        }
        if (varKey == "HS787_BleedEng2")
        {
            simConnect.ExecuteCalculatorCode($"(A:BLEED AIR ENGINE:2,Bool) {(int)value} != if{{ 2 (>K:TOGGLE_BLEED_AIR_SOURCE) }}");
            return true;
        }
        if (varKey == "HS787_BleedAPU")
        {
            simConnect.ExecuteCalculatorCode($"(A:APU BLEED AIR SWITCH,Bool) {(int)value} != if{{ (>K:APU_BLEED_AIR_SOURCE_TOGGLE) }}");
            return true;
        }
        if (varKey == "HS787_BleedIso")
        {
            simConnect.SetLVar("XMLVAR_Bleed_Air_Isolation", value);
            return true;
        }

        // ===== Fuel crossfeed valves — L-var direct write =====
        // Crossfeed fallback for non-WT models — directly write the mirror L-var.
        if (varKey == "HS787_FuelXfeed") { simConnect.SetLVar("XMLVAR_Fuel_XFEED_Fwd", value); return true; }

        // ===== Fire/OVHT Test — momentary push: write 1, auto-release to 0 after 4 s =====
        // Click engages the test; the cockpit runs its fire-test sequence and lights up
        // the appropriate indicators for the duration. Auto-releasing the L-var after
        // ~4 s lets ProcessSimVarUpdate fire both "in progress" and "complete" announces
        // without the user having to manually release. Note: the test ANIMATION/AUDIO is
        // the WT cockpit's responsibility — if no chime fires in your sim, the WT B787
        // model just doesn't implement that audio (we can verify the L-var is set, but
        // we can't synthesize a cockpit sound from MSFSBA).
        if (varKey == "HS787_FireTest")
        {
            simConnect.SetLVar("XMLVAR_Fire_Test_Pushed", 1);
            var releaseTimer = new System.Windows.Forms.Timer { Interval = 4000 };
            releaseTimer.Tick += (_, __) =>
            {
                releaseTimer.Stop();
                releaseTimer.Dispose();
                simConnect.SetLVar("XMLVAR_Fire_Test_Pushed", 0);
            };
            releaseTimer.Start();
            return true;
        }

        // ===== Engine + APU fire handles (pulled = armed for discharge) =====
        if (varKey == "HS787_EngFireHandle1") { simConnect.SetLVar("XMLVAR_Eng_Fire_Pulled_1", value); return true; }
        if (varKey == "HS787_EngFireHandle2") { simConnect.SetLVar("XMLVAR_Eng_Fire_Pulled_2", value); return true; }
        if (varKey == "HS787_APUFireHandle")  { simConnect.SetLVar("XMLVAR_APU_Fire_Pulled",   value); return true; }

        // ===== Cargo Fire =====
        if (varKey == "HS787_CargoFireFwd")   { simConnect.SetLVar("XMLVAR_Cargo_Fire_Arm_Fwd",   value); return true; }
        if (varKey == "HS787_CargoFireAft")   { simConnect.SetLVar("XMLVAR_Cargo_Fire_Arm_Aft",   value); return true; }
        if (varKey == "HS787_CargoFireDisch") { simConnect.SetLVar("XMLVAR_Cargo_Fire_Discharge", value); return true; }

        // ===== Standby Power Selector =====
        if (varKey == "HS787_StandbyPower")
        {
            simConnect.SetLVar("XMLVAR_StandbyPower", value);
            return true;
        }

        // ===== Baro STD pushbutton =====
        // Momentary push: toggle STD ↔ QNH. MainForm's button-click path always passes
        // value=1, so we compute the new state ourselves by inverting the current cached
        // state. Going to STD: write the L-var flag AND fire K:BAROMETRIC_STD_PRESSURE
        // (jumps the altimeter to 29.92). Going to QNH: just clear the L-var flag; the
        // WT cockpit's panel logic restores the previously-set baro value when the flag
        // drops to 0. K event is NOT fired on QNH return — it only sets 29.92, never QNH.
        if (varKey == "HS787_BaroSTD")
        {
            double? cur = simConnect.GetCachedVariableValue("HS787_BaroSTD");
            int newState = (cur.HasValue && (int)cur.Value == 1) ? 0 : 1;
            simConnect.SetLVar("XMLVAR_Baro_Selector_STD_1", newState);
            if (newState == 1)
                simConnect.SendEvent("BAROMETRIC_STD_PRESSURE");
            return true;
        }

        // ===== Master Caution / Master Warning reset buttons =====
        // Momentary push: fire the standard MSFS acknowledge K event. Driving the
        // L:Generic_Master_*_Active L-var to 0 happens via the K event's internal
        // path — we don't have to write the L-var ourselves.
        if (varKey == "HS787_MasterCautionReset")
        {
            simConnect.SendEvent("MASTER_CAUTION_ACKNOWLEDGE");
            return true;
        }
        if (varKey == "HS787_MasterWarningReset")
        {
            simConnect.SendEvent("MASTER_WARNING_ACKNOWLEDGE");
            return true;
        }

        // ===== Engine fuel control switches (RUN / CUTOFF) =====
        // MIXTUREn_RICH = RUN (value 1), MIXTUREn_LEAN = CUTOFF (value 0).
        if (varKey == "HS787_FuelControl1")
        {
            simConnect.SendEvent((int)value == 1 ? "MIXTURE1_RICH" : "MIXTURE1_LEAN");
            return true;
        }
        if (varKey == "HS787_FuelControl2")
        {
            simConnect.SendEvent((int)value == 1 ? "MIXTURE2_RICH" : "MIXTURE2_LEAN");
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
        if (base.ProcessSimVarUpdate(variableKey, value, announcer))
            return true;

        // Bridge diagnostic L-var — store silently, no announcement.
        if (variableKey == "HS787_BridgeStage")
        {
            BridgeStage = (int)value;
            return true;
        }

        // =====================================================================
        // BridgeVersion 18+ — new annunciators / status. All use the same pattern:
        // track previous value (-1 = unset → suppresses first-poll), announce only
        // on transitions, optionally distinguish on→off vs off→on phrasing.
        // =====================================================================

        if (variableKey == "HS787_MasterCaution")
        {
            int now = (int)value;
            if (_previousMasterCaution >= 0 && now != _previousMasterCaution)
                announcer.Announce(now == 1 ? "Master Caution" : "Master Caution clear");
            _previousMasterCaution = now;
            return true;
        }

        if (variableKey == "HS787_MasterWarning")
        {
            int now = (int)value;
            if (_previousMasterWarning >= 0 && now != _previousMasterWarning)
                announcer.Announce(now == 1 ? "Master Warning" : "Master Warning clear");
            _previousMasterWarning = now;
            return true;
        }

        if (variableKey == "HS787_StallWarning")
        {
            int now = (int)value;
            if (_previousStallWarning >= 0 && now != _previousStallWarning && now == 1)
                announcer.Announce("STALL");
            _previousStallWarning = now;
            return true;
        }

        if (variableKey == "HS787_IrsOnBat")
        {
            int now = (int)value;
            if (_previousIrsOnBat >= 0 && now != _previousIrsOnBat)
                announcer.Announce(now == 1 ? "IRS on battery" : "IRS on aircraft power");
            _previousIrsOnBat = now;
            return true;
        }

        if (variableKey == "HS787_LightMaster")
        {
            int now = (int)value;
            if (_previousLightMaster >= 0 && now != _previousLightMaster)
                announcer.Announce(now == 1 ? "Light Master on" : "Light Master off");
            _previousLightMaster = now;
            return true;
        }

        if (variableKey == "HS787_EmerLightsCover")
        {
            int now = (int)value;
            if (_previousEmerLightsCover >= 0 && now != _previousEmerLightsCover)
                announcer.Announce(now == 1 ? "Emergency Lights cover open" : "Emergency Lights cover closed");
            _previousEmerLightsCover = now;
            return true;
        }

        if (variableKey == "HS787_EfbPower")
        {
            int now = (int)value;
            if (_previousEfbPower >= 0 && now != _previousEfbPower)
                announcer.Announce(now == 1 ? "EFB on" : "EFB off");
            _previousEfbPower = now;
            return true;
        }

        if (variableKey == "HS787_FlightComputerAuto")
        {
            int now = (int)value;
            if (_previousFlightComputerAuto >= 0 && now != _previousFlightComputerAuto)
                announcer.Announce(now == 1 ? "Flight Computer Auto" : "Flight Computer Manual");
            _previousFlightComputerAuto = now;
            return true;
        }

        if (variableKey == "HS787_PacksAuto")
        {
            int now = (int)value;
            if (_previousPacksAuto >= 0 && now != _previousPacksAuto)
                announcer.Announce(now == 1 ? "Packs on" : "Packs off");
            _previousPacksAuto = now;
            return true;
        }

        if (variableKey == "HS787_FdDoorPower")
        {
            int now = (int)value;
            if (_previousFdDoorPower >= 0 && now != _previousFdDoorPower)
                announcer.Announce(now == 1 ? "Flight Deck Door power on" : "Flight Deck Door power off");
            _previousFdDoorPower = now;
            return true;
        }

        if (variableKey == "HS787_CoolingAft")
        {
            int now = (int)value;
            if (_previousCoolingAft >= 0 && now != _previousCoolingAft)
                announcer.Announce(now == 1 ? "Aft Equipment Cooling auto" : "Aft Equipment Cooling off");
            _previousCoolingAft = now;
            return true;
        }

        if (variableKey == "HS787_EquipFwd")
        {
            int now = (int)value;
            if (_previousEquipFwd >= 0 && now != _previousEquipFwd)
                announcer.Announce(now == 1 ? "Forward Equipment Cooling auto" : "Forward Equipment Cooling off");
            _previousEquipFwd = now;
            return true;
        }

        if (variableKey == "HS787_PressManAltOn")
        {
            int now = (int)value;
            if (_previousPressManAltOn >= 0 && now != _previousPressManAltOn)
                announcer.Announce(now == 1 ? "Pressurization manual" : "Pressurization auto");
            _previousPressManAltOn = now;
            return true;
        }

        // ----- BridgeVersion 19+ -----

        if (variableKey == "HS787_AcBusEnergized")
        {
            int now = (int)value;
            if (_previousAcBusEnergized >= 0 && now != _previousAcBusEnergized)
                announcer.Announce(now == 1 ? "AC Bus energized" : "AC Bus de-energized");
            _previousAcBusEnergized = now;
            return true;
        }

        if (variableKey == "HS787_AutoBacklight")
        {
            int now = (int)value;
            if (_previousAutoBacklight >= 0 && now != _previousAutoBacklight)
                announcer.Announce(now == 1 ? "Auto Backlight on" : "Auto Backlight off");
            _previousAutoBacklight = now;
            return true;
        }

        if (variableKey == "HS787_NextGenFP")
        {
            int now = (int)value;
            if (_previousNextGenFp >= 0 && now != _previousNextGenFp)
                announcer.Announce(now == 1 ? "NextGen Flight Plan on" : "NextGen Flight Plan off");
            _previousNextGenFp = now;
            return true;
        }

        if (variableKey == "HS787_HudDown1")
        {
            int now = (int)value;
            if (_previousHudDown1 >= 0 && now != _previousHudDown1)
                announcer.Announce(now == 1 ? "Captain HUD deployed" : "Captain HUD stowed");
            _previousHudDown1 = now;
            return true;
        }

        if (variableKey == "HS787_HudDown2")
        {
            int now = (int)value;
            if (_previousHudDown2 >= 0 && now != _previousHudDown2)
                announcer.Announce(now == 1 ? "First Officer HUD deployed" : "First Officer HUD stowed");
            _previousHudDown2 = now;
            return true;
        }

        if (variableKey == "HS787_HudAutoBrt1")
        {
            int now = (int)value;
            if (_previousHudAutoBrt1 >= 0 && now != _previousHudAutoBrt1)
                announcer.Announce(now == 1 ? "Captain HUD brightness auto" : "Captain HUD brightness manual");
            _previousHudAutoBrt1 = now;
            return true;
        }

        if (variableKey == "HS787_HudAutoBrt2")
        {
            int now = (int)value;
            if (_previousHudAutoBrt2 >= 0 && now != _previousHudAutoBrt2)
                announcer.Announce(now == 1 ? "First Officer HUD brightness auto" : "First Officer HUD brightness manual");
            _previousHudAutoBrt2 = now;
            return true;
        }

        if (variableKey == "HS787_AirDataSrc1")
        {
            int now = (int)value;
            if (_previousAirDataSrc1 >= 0 && now != _previousAirDataSrc1)
                announcer.Announce(now == 1 ? "Captain Air Data Source Alternate" : "Captain Air Data Source Normal");
            _previousAirDataSrc1 = now;
            return true;
        }

        if (variableKey == "HS787_AirDataSrc2")
        {
            int now = (int)value;
            if (_previousAirDataSrc2 >= 0 && now != _previousAirDataSrc2)
                announcer.Announce(now == 1 ? "First Officer Air Data Source Alternate" : "First Officer Air Data Source Normal");
            _previousAirDataSrc2 = now;
            return true;
        }

        // ----- Batch 5 — yaw damper, antiskid, avionics master, pitot heat, interior lights -----

        if (variableKey == "HS787_YawDamper")
        {
            int now = (int)value;
            if (_previousYawDamper >= 0 && now != _previousYawDamper)
                announcer.Announce(now == 1 ? "Yaw Damper on" : "Yaw Damper off");
            _previousYawDamper = now;
            return true;
        }

        if (variableKey == "HS787_AntiSkid")
        {
            int now = (int)value;
            if (_previousAntiSkid >= 0 && now != _previousAntiSkid)
                announcer.Announce(now == 1 ? "Antiskid armed" : "Antiskid off");
            _previousAntiSkid = now;
            return true;
        }

        if (variableKey == "HS787_AvionicsMaster")
        {
            int now = (int)value;
            if (_previousAvionicsMaster >= 0 && now != _previousAvionicsMaster)
                announcer.Announce(now == 1 ? "Avionics Master on" : "Avionics Master off");
            _previousAvionicsMaster = now;
            return true;
        }

        if (variableKey == "HS787_PitotHeat")
        {
            int now = (int)value;
            if (_previousPitotHeat >= 0 && now != _previousPitotHeat)
                announcer.Announce(now == 1 ? "Pitot Heat on" : "Pitot Heat off");
            _previousPitotHeat = now;
            return true;
        }

        // Interior light bus announce handlers removed alongside the var defs.

        // ----- Batch 6 — COM frequencies + transponder squawk (PMDG-style) -----
        // First sample (baseline 0) is silent; only real changes are spoken.

        if (variableKey == "COM1_ActiveFreq")
        {
            if (_lastComActive1 > 0 && System.Math.Abs(value - _lastComActive1) > 0.001)
                announcer.Announce($"COM1 active {value:F3}");
            _lastComActive1 = value;
            return true;
        }
        if (variableKey == "COM_STANDBY_FREQUENCY_SET:1")
        {
            if (_lastComStandby1 > 0 && System.Math.Abs(value - _lastComStandby1) > 0.001)
                announcer.Announce($"COM1 standby {value:F3}");
            _lastComStandby1 = value;
            return true;
        }
        if (variableKey == "COM2_ActiveFreq")
        {
            if (_lastComActive2 > 0 && System.Math.Abs(value - _lastComActive2) > 0.001)
                announcer.Announce($"COM2 active {value:F3}");
            _lastComActive2 = value;
            return true;
        }
        if (variableKey == "COM_STANDBY_FREQUENCY_SET:2")
        {
            if (_lastComStandby2 > 0 && System.Math.Abs(value - _lastComStandby2) > 0.001)
                announcer.Announce($"COM2 standby {value:F3}");
            _lastComStandby2 = value;
            return true;
        }
        if (variableKey == "TRANSPONDER_CODE_SET")
        {
            if (_lastSquawkCode > 0 && System.Math.Abs(value - _lastSquawkCode) > 0.5)
            {
                int bcd = (int)value;
                int d1 = (bcd >> 12) & 0xF;
                int d2 = (bcd >> 8) & 0xF;
                int d3 = (bcd >> 4) & 0xF;
                int d4 = bcd & 0xF;
                announcer.Announce($"Squawk {d1}{d2}{d3}{d4}");
            }
            _lastSquawkCode = value;
            return true;
        }

        // ----- Batch 3 — AP modes (each announces when it engages/disengages) -----

        if (variableKey == "HS787_ApAltHold")
        {
            int now = (int)value;
            if (_previousApAltHold >= 0 && now != _previousApAltHold)
                announcer.Announce(now == 1 ? "Alt Hold engaged" : "Alt Hold off");
            _previousApAltHold = now;
            return true;
        }

        if (variableKey == "HS787_ApFlch")
        {
            int now = (int)value;
            if (_previousApFlch >= 0 && now != _previousApFlch)
                announcer.Announce(now == 1 ? "FLCH engaged" : "FLCH off");
            _previousApFlch = now;
            return true;
        }

        if (variableKey == "HS787_ApVs")
        {
            int now = (int)value;
            if (_previousApVs >= 0 && now != _previousApVs)
                announcer.Announce(now == 1 ? "V/S engaged" : "V/S off");
            _previousApVs = now;
            return true;
        }

        if (variableKey == "HS787_ApSpd")
        {
            int now = (int)value;
            if (_previousApSpd >= 0 && now != _previousApSpd)
                announcer.Announce(now == 1 ? "Speed engaged" : "Speed off");
            _previousApSpd = now;
            return true;
        }

        if (variableKey == "HS787_ApThr")
        {
            int now = (int)value;
            if (_previousApThr >= 0 && now != _previousApThr)
                announcer.Announce(now == 1 ? "Throttle engaged" : "Throttle off");
            _previousApThr = now;
            return true;
        }

        if (variableKey == "HS787_ApHdgHold")
        {
            int now = (int)value;
            if (_previousApHdgHold >= 0 && now != _previousApHdgHold)
                announcer.Announce(now == 1 ? "HDG Hold engaged" : "HDG Hold off");
            _previousApHdgHold = now;
            return true;
        }

        if (variableKey == "HS787_ApHdgSel")
        {
            int now = (int)value;
            if (_previousApHdgSel >= 0 && now != _previousApHdgSel)
                announcer.Announce(now == 1 ? "HDG Sel engaged" : "HDG Sel off");
            _previousApHdgSel = now;
            return true;
        }

        if (variableKey == "HS787_ApClbCon")
        {
            int now = (int)value;
            if (_previousApClbCon >= 0 && now != _previousApClbCon)
                announcer.Announce(now == 1 ? "Climb Continuous engaged" : "Climb Continuous off");
            _previousApClbCon = now;
            return true;
        }

        if (variableKey == "HS787_ApproachIls")
        {
            int now = (int)value;
            if (_previousApproachIls >= 0 && now != _previousApproachIls)
                announcer.Announce(now == 1 ? "ILS Approach armed" : "ILS Approach disarmed");
            _previousApproachIls = now;
            return true;
        }

        // Flight Timer mode/running and Checklist phase: silently cached so the FMC Status
        // display panel can read them, but no auto-announce. The WT 787 writes these L-vars
        // during FMS phase transitions (engine start, takeoff, climb, etc.) — auto-announcing
        // every transition produces noise the pilot doesn't need. The user reads them on
        // demand from the FMC Status panel display field.
        if (variableKey == "HS787_FltTimerMode")    return true;
        if (variableKey == "HS787_FltTimerRunning") return true;
        if (variableKey == "HS787_ChecklistPhase")  return true;

        if (variableKey == "HS787_HudSymbology1")
        {
            int now = (int)value;
            if (_previousHudSymbology1 >= 0 && now != _previousHudSymbology1)
                announcer.Announce(now == 1 ? "Captain HUD decluttered" : "Captain HUD full symbology");
            _previousHudSymbology1 = now;
            return true;
        }

        if (variableKey == "HS787_HudSymbology2")
        {
            int now = (int)value;
            if (_previousHudSymbology2 >= 0 && now != _previousHudSymbology2)
                announcer.Announce(now == 1 ? "First Officer HUD decluttered" : "First Officer HUD full symbology");
            _previousHudSymbology2 = now;
            return true;
        }

        if (variableKey == "HS787_HudDeclutterInhibit1")
        {
            int now = (int)value;
            if (_previousHudDecInhibit1 >= 0 && now != _previousHudDecInhibit1)
                announcer.Announce(now == 1 ? "Captain HUD declutter inhibit on" : "Captain HUD declutter inhibit off");
            _previousHudDecInhibit1 = now;
            return true;
        }

        if (variableKey == "HS787_HudDeclutterInhibit2")
        {
            int now = (int)value;
            if (_previousHudDecInhibit2 >= 0 && now != _previousHudDecInhibit2)
                announcer.Announce(now == 1 ? "First Officer HUD declutter inhibit on" : "First Officer HUD declutter inhibit off");
            _previousHudDecInhibit2 = now;
            return true;
        }

        // ----- Batch 4 — Fire detection -----
        // First-poll suppressed to avoid spurious "FIRE ENGINE N" on MSFSBA startup
        // (the WT Boeing fire SimVar transiently returns 1 during aircraft init in
        // some states). Real fires happen in flight, well after the baseline is
        // established, so suppressing first poll loses nothing in practice.

        if (variableKey == "HS787_EngFire1")
        {
            int now = (int)value;
            if (_previousEngFire1 >= 0 && now != _previousEngFire1)
                announcer.Announce(now == 1 ? "FIRE ENGINE 1" : "Engine 1 fire cleared");
            _previousEngFire1 = now;
            return true;
        }

        if (variableKey == "HS787_EngFire2")
        {
            int now = (int)value;
            if (_previousEngFire2 >= 0 && now != _previousEngFire2)
                announcer.Announce(now == 1 ? "FIRE ENGINE 2" : "Engine 2 fire cleared");
            _previousEngFire2 = now;
            return true;
        }

        if (variableKey == "HS787_FireBottleDisch1")
        {
            int now = (int)value;
            if (_previousFireBottleDisch1 >= 0 && now != _previousFireBottleDisch1 && now == 1)
                announcer.Announce("Fire Bottle 1 discharged");
            _previousFireBottleDisch1 = now;
            return true;
        }

        if (variableKey == "HS787_FireBottleDisch2")
        {
            int now = (int)value;
            if (_previousFireBottleDisch2 >= 0 && now != _previousFireBottleDisch2 && now == 1)
                announcer.Announce("Fire Bottle 2 discharged");
            _previousFireBottleDisch2 = now;
            return true;
        }

        // FuelBalanceFault: only announce when it turns ON. Suppress first poll
        // so a fault-on-startup (rare) is not re-announced every MSFSBA restart.
        if (variableKey == "HS787_FuelBalanceFault")
        {
            int now = (int)value;
            if (_previousFuelBalanceFault >= 0 && now == 1 && _previousFuelBalanceFault == 0)
                announcer.Announce("Fuel Balance Fault");
            _previousFuelBalanceFault = now;
            return true;
        }

        // EXECActive: announce both activation and deactivation (light on/off).
        // First poll suppressed (tri-state init).
        if (variableKey == "HS787_EXECActive")
        {
            int now = (int)value;
            if (_previousExecActive >= 0 && now != _previousExecActive)
                announcer.Announce(now == 1 ? "EXEC Active" : "EXEC Off");
            _previousExecActive = now;
            return true;
        }

        // TOGA: announce activation only. Suppress first poll.
        if (variableKey == "HS787_TOGA")
        {
            int now = (int)value;
            if (_previousTOGA >= 0 && now == 1 && _previousTOGA == 0)
                announcer.Announce("TOGA Active");
            _previousTOGA = now;
            return true;
        }

        // APDisconnected: announce disconnect only. Suppress first poll.
        if (variableKey == "HS787_APDisconnected")
        {
            int now = (int)value;
            if (_previousAPDisconnected >= 0 && now == 1 && _previousAPDisconnected == 0)
                announcer.Announce("Autopilot Disconnected");
            _previousAPDisconnected = now;
            return true;
        }

        // Approach mode: announce arm and disengage transitions.
        if (variableKey == "HS787_APP")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousAppHold >= 0 && now != _previousAppHold)
                announcer.Announce(now == 1 ? "Approach armed" : "Approach disengaged");
            _previousAppHold = now;
            return true;
        }

        // GS capture: announce once when glideslope becomes active.
        if (variableKey == "HS787_GS_Active")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousGSActive >= 0 && now == 1 && _previousGSActive == 0)
                announcer.Announce("Glideslope active");
            _previousGSActive = now;
            return true;
        }

        // Autopilot and autothrottle state — only actual transitions are announced.
        if (variableKey == "HS787_APMaster")
        {
            int now = (int)value;
            if (_previousAPMaster >= 0 && now != _previousAPMaster)
                announcer.Announce(now == 1 ? "Autopilot 1 On" : "Autopilot 1 Off");
            _previousAPMaster = now;
            return true;
        }

        if (variableKey == "HS787_ATStatus")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousATStatus >= 0 && now != _previousATStatus)
                announcer.Announce(now == 1 ? "Autothrottle Armed" : "Autothrottle Disarmed");
            _previousATStatus = now;
            return true;
        }

        // External power — announce changes only; suppress startup announcement.
        if (variableKey == "HS787_ExtPwrOn1")
        {
            int now = (int)value;
            if (_previousExtPwr1On >= 0 && now != _previousExtPwr1On)
                announcer.Announce(now == 1 ? "External Power 1 On" : "External Power 1 Off");
            _previousExtPwr1On = now;
            return true;
        }

        if (variableKey == "HS787_ExtPwrOn2")
        {
            int now = (int)value;
            if (_previousExtPwr2On >= 0 && now != _previousExtPwr2On)
                announcer.Announce(now == 1 ? "External Power 2 On" : "External Power 2 Off");
            _previousExtPwr2On = now;
            return true;
        }

        // Speedbrake: WT_SPEEDBRAKE_LEVER_POS is 0-16384; announce on state band changes.
        // DOWN_LIMIT=410, ARM_LIMIT=1230 (from BoeingSpeedbrakeSystem constants).
        // First poll suppressed via _previousSpeedbrakeState >= 0 guard.
        if (variableKey == "HS787_Speedbrake")
        {
            int state = value <= 410 ? 0 : value <= 1230 ? 1 : 2;
            if (_previousSpeedbrakeState >= 0 && state != _previousSpeedbrakeState)
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
        // First poll suppressed via tri-state init.
        if (variableKey == "HS787_FLCH")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousFLCH >= 0 && now != _previousFLCH)
                announcer.Announce(now == 1 ? "Level Change Engaged" : "Level Change Off");
            _previousFLCH = now;
            return true;
        }

        if (variableKey == "HS787_ALTHold")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousALTHold >= 0 && now != _previousALTHold)
                announcer.Announce(now == 1 ? "Altitude Hold" : "Altitude Hold Off");
            _previousALTHold = now;
            return true;
        }

        if (variableKey == "HS787_LNAV")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousLNAV >= 0 && now != _previousLNAV)
                announcer.Announce(now == 1 ? "LNAV Engaged" : "LNAV Off");
            _previousLNAV = now;
            return true;
        }

        if (variableKey == "HS787_VNAV")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousVNAV >= 0 && now != _previousVNAV)
                announcer.Announce(now == 1 ? "VNAV Engaged" : "VNAV Off");
            _previousVNAV = now;
            return true;
        }

        if (variableKey == "HS787_HDGHold")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousHDGHold >= 0 && now != _previousHDGHold)
                announcer.Announce(now == 1 ? "Heading Hold" : "Heading Hold Off");
            _previousHDGHold = now;
            return true;
        }

        if (variableKey == "HS787_VS_Active")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousVSActive >= 0 && now != _previousVSActive)
                announcer.Announce(now == 1 ? "V/S Engaged" : "V/S Off");
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
            int now = value > 0 ? 1 : 0;
            if (_previousPackL >= 0 && now != _previousPackL)
                announcer.Announce(now == 1 ? "Pack Left Auto" : "Pack Left Off");
            _previousPackL = now;
            return true;
        }

        if (variableKey == "HS787_PackR")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousPackR >= 0 && now != _previousPackR)
                announcer.Announce(now == 1 ? "Pack Right Auto" : "Pack Right Off");
            _previousPackR = now;
            return true;
        }

        // Hydraulic demand pumps
        if (variableKey == "HS787_HydDemandLeft")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousHydDemandL >= 0 && now != _previousHydDemandL)
                announcer.Announce(now == 1 ? "Hydraulic Demand Left On" : "Hydraulic Demand Left Off");
            _previousHydDemandL = now;
            return true;
        }

        if (variableKey == "HS787_HydDemandRight")
        {
            int now = value > 0 ? 1 : 0;
            if (_previousHydDemandR >= 0 && now != _previousHydDemandR)
                announcer.Announce(now == 1 ? "Hydraulic Demand Right On" : "Hydraulic Demand Right Off");
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

        // IRS position accepted (WT_IRS_POS_SET_N) — NOT alignment complete.
        // Announce the honest position semantics; true alignment completion is
        // owned by HS787_IRS_Align (bridge-sourced). Suppress first poll.
        if (variableKey == "HS787_IRS_Aligned1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousIrsAligned1 >= 0 && now != _previousIrsAligned1)
                announcer.Announce(now == 1 ? "IRS Left position set" : "IRS Left position lost");
            _previousIrsAligned1 = now;
            return true;
        }
        if (variableKey == "HS787_IRS_Aligned2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousIrsAligned2 >= 0 && now != _previousIrsAligned2)
                announcer.Announce(now == 1 ? "IRS Right position set" : "IRS Right position lost");
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

        // ----- Batch 7 — panel switches promoted to Continuous+Announced -----

        if (variableKey == "HS787_BatSwitch1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBatSwitch1 >= 0 && now != _previousBatSwitch1)
                announcer.Announce(now == 1 ? "Battery 1 On" : "Battery 1 Off");
            _previousBatSwitch1 = now;
            return true;
        }
        if (variableKey == "HS787_BatSwitch2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBatSwitch2 >= 0 && now != _previousBatSwitch2)
                announcer.Announce(now == 1 ? "Battery 2 On" : "Battery 2 Off");
            _previousBatSwitch2 = now;
            return true;
        }
        if (variableKey == "HS787_Gen1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousGen1 >= 0 && now != _previousGen1)
                announcer.Announce(now == 1 ? "Generator 1 On" : "Generator 1 Off");
            _previousGen1 = now;
            return true;
        }
        if (variableKey == "HS787_Gen2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousGen2 >= 0 && now != _previousGen2)
                announcer.Announce(now == 1 ? "Generator 2 On" : "Generator 2 Off");
            _previousGen2 = now;
            return true;
        }
        if (variableKey == "HS787_ApuGen1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousApuGen1 >= 0 && now != _previousApuGen1)
                announcer.Announce(now == 1 ? "APU Generator 1 On" : "APU Generator 1 Off");
            _previousApuGen1 = now;
            return true;
        }
        if (variableKey == "HS787_ApuGen2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousApuGen2 >= 0 && now != _previousApuGen2)
                announcer.Announce(now == 1 ? "APU Generator 2 On" : "APU Generator 2 Off");
            _previousApuGen2 = now;
            return true;
        }
        // HS787_ExtPwr1/2 announcements are suppressed via the cache-only switch at the
        // bottom of ProcessSimVarUpdate. HS787_ExtPwrOn1/2 (delivered-power SimVar) owns
        // the user-facing "External Power N On/Off" callout, so handling these here would
        // double-talk.
        if (variableKey == "HS787_UtilityCabin")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousUtilityCabin >= 0 && now != _previousUtilityCabin)
                announcer.Announce(now == 1 ? "Cabin Utility On" : "Cabin Utility Off");
            _previousUtilityCabin = now;
            return true;
        }
        if (variableKey == "HS787_UtilityIfe")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousUtilityIfe >= 0 && now != _previousUtilityIfe)
                announcer.Announce(now == 1 ? "IFE Utility On" : "IFE Utility Off");
            _previousUtilityIfe = now;
            return true;
        }
        if (variableKey == "HS787_HydEngL")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousHydEngL >= 0 && now != _previousHydEngL)
                announcer.Announce(now == 1 ? "Hydraulic Engine Left On" : "Hydraulic Engine Left Off");
            _previousHydEngL = now;
            return true;
        }
        if (variableKey == "HS787_HydEngR")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousHydEngR >= 0 && now != _previousHydEngR)
                announcer.Announce(now == 1 ? "Hydraulic Engine Right On" : "Hydraulic Engine Right Off");
            _previousHydEngR = now;
            return true;
        }
        if (variableKey == "HS787_HydC1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousHydC1 >= 0 && now != _previousHydC1)
                announcer.Announce(now == 1 ? "Hydraulic Center 1 On" : "Hydraulic Center 1 Off");
            _previousHydC1 = now;
            return true;
        }
        if (variableKey == "HS787_HydC2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousHydC2 >= 0 && now != _previousHydC2)
                announcer.Announce(now == 1 ? "Hydraulic Center 2 On" : "Hydraulic Center 2 Off");
            _previousHydC2 = now;
            return true;
        }
        if (variableKey == "HS787_FuelBalance")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelBalance >= 0 && now != _previousFuelBalance)
                announcer.Announce(now == 1 ? "Fuel Balance On" : "Fuel Balance Off");
            _previousFuelBalance = now;
            return true;
        }
        if (variableKey == "HS787_FuelBalanceActive")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelBalanceActive >= 0 && now != _previousFuelBalanceActive)
                announcer.Announce(now == 1 ? "Fuel Balance Active" : "Fuel Balance Inactive");
            _previousFuelBalanceActive = now;
            return true;
        }
        if (variableKey == "HS787_TrimAirL")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousTrimAirL >= 0 && now != _previousTrimAirL)
                announcer.Announce(now == 1 ? "Trim Air Left On" : "Trim Air Left Off");
            _previousTrimAirL = now;
            return true;
        }
        if (variableKey == "HS787_TrimAirR")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousTrimAirR >= 0 && now != _previousTrimAirR)
                announcer.Announce(now == 1 ? "Trim Air Right On" : "Trim Air Right Off");
            _previousTrimAirR = now;
            return true;
        }
        if (variableKey == "HS787_RecircUpper")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousRecircUpper >= 0 && now != _previousRecircUpper)
                announcer.Announce(now == 1 ? "Upper Recirc Fan On" : "Upper Recirc Fan Off");
            _previousRecircUpper = now;
            return true;
        }
        if (variableKey == "HS787_RecircLower")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousRecircLower >= 0 && now != _previousRecircLower)
                announcer.Announce(now == 1 ? "Lower Recirc Fan On" : "Lower Recirc Fan Off");
            _previousRecircLower = now;
            return true;
        }
        if (variableKey == "HS787_WshldDeice1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousWshldDeice1 >= 0 && now != _previousWshldDeice1)
                announcer.Announce(now == 1 ? "Window Heat 1 On" : "Window Heat 1 Off");
            _previousWshldDeice1 = now;
            return true;
        }
        if (variableKey == "HS787_WshldDeice2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousWshldDeice2 >= 0 && now != _previousWshldDeice2)
                announcer.Announce(now == 1 ? "Window Heat 2 On" : "Window Heat 2 Off");
            _previousWshldDeice2 = now;
            return true;
        }
        if (variableKey == "HS787_WshldDeice3")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousWshldDeice3 >= 0 && now != _previousWshldDeice3)
                announcer.Announce(now == 1 ? "Window Heat 3 On" : "Window Heat 3 Off");
            _previousWshldDeice3 = now;
            return true;
        }
        if (variableKey == "HS787_WshldDeice4")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousWshldDeice4 >= 0 && now != _previousWshldDeice4)
                announcer.Announce(now == 1 ? "Window Heat 4 On" : "Window Heat 4 Off");
            _previousWshldDeice4 = now;
            return true;
        }
        if (variableKey == "HS787_AltnFlapsArmed")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousAltnFlapsArmed >= 0 && now != _previousAltnFlapsArmed)
                announcer.Announce(now == 1 ? "Alternate Flaps Armed" : "Alternate Flaps Disarmed");
            _previousAltnFlapsArmed = now;
            return true;
        }
        if (variableKey == "HS787_BaroSelector")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBaroSelector >= 0 && now != _previousBaroSelector)
                announcer.Announce(now == 1 ? "Baro Selector HPa" : "Baro Selector Inches");
            _previousBaroSelector = now;
            return true;
        }
        if (variableKey == "HS787_MinsMode")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousMinsMode >= 0 && now != _previousMinsMode)
                announcer.Announce(now == 1 ? "Minimums Baro" : "Minimums Radio");
            _previousMinsMode = now;
            return true;
        }
        if (variableKey == "HS787_FPVMode")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFPVMode >= 0 && now != _previousFPVMode)
                announcer.Announce(now == 1 ? "FPV On" : "FPV Off");
            _previousFPVMode = now;
            return true;
        }
        if (variableKey == "HS787_TransponderMode")
        {
            int now = (int)value;
            if (_previousTransponderMode >= 0 && now != _previousTransponderMode)
            {
                string msg = now switch
                {
                    0 => "Transponder Standby",
                    1 => "Transponder Alt Off",
                    2 => "Transponder XPNDR",
                    3 => "Transponder TA",
                    4 => "Transponder TA/RA",
                    _ => $"Transponder Mode {now}"
                };
                announcer.Announce(msg);
            }
            _previousTransponderMode = now;
            return true;
        }
        if (variableKey == "HS787_SATCOM")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousSATCOM >= 0 && now != _previousSATCOM)
                announcer.Announce(now == 1 ? "SATCOM On" : "SATCOM Off");
            _previousSATCOM = now;
            return true;
        }
        if (variableKey == "HS787_VBar")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousVBar >= 0 && now != _previousVBar)
                announcer.Announce(now == 1 ? "V-Bar On" : "V-Bar Off");
            _previousVBar = now;
            return true;
        }
        if (variableKey == "HS787_Autobrake")
        {
            int now = (int)value;
            if (_previousAutobrake >= 0 && now != _previousAutobrake)
            {
                // Positions MUST match HS787_Autobrake.ValueDescriptions (~line 1700) and the
                // SET_AUTOBRAKE_CONTROL write in HandleUIVariableSet, which both use
                // 0=Off, 1=RTO, 2=1, 3=2, 4=3, 5=MAX. This switch previously used a different
                // mapping (0=RTO, 1=Disarmed), so the spoken callout contradicted the panel combo
                // label and the value actually commanded. Keep all three in sync.
                string msg = now switch
                {
                    0 => "Autobrakes Off",
                    1 => "Autobrakes RTO",
                    2 => "Autobrakes 1",
                    3 => "Autobrakes 2",
                    4 => "Autobrakes 3",
                    5 => "Autobrakes Max",
                    _ => $"Autobrakes {now}"
                };
                announcer.Announce(msg);
            }
            _previousAutobrake = now;
            return true;
        }
        if (variableKey == "HS787_LightTaxi")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightTaxi >= 0 && now != _previousLightTaxi)
                announcer.Announce(now == 1 ? "Taxi Light On" : "Taxi Light Off");
            _previousLightTaxi = now;
            return true;
        }
        if (variableKey == "HS787_LightLogo")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightLogo >= 0 && now != _previousLightLogo)
                announcer.Announce(now == 1 ? "Logo Light On" : "Logo Light Off");
            _previousLightLogo = now;
            return true;
        }
        if (variableKey == "HS787_LightWing")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousLightWing >= 0 && now != _previousLightWing)
                announcer.Announce(now == 1 ? "Runway Turnoff On" : "Runway Turnoff Off");
            _previousLightWing = now;
            return true;
        }

        // ----- Batch 8 — fuel pumps, bleeds, fire, cargo, standby, fuel control -----

        if (variableKey == "HS787_FuelPump_LFwd")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_LFwd >= 0 && now != _previousFuelPump_LFwd)
                announcer.Announce(now == 1 ? "Left Forward Fuel Pump On" : "Left Forward Fuel Pump Off");
            _previousFuelPump_LFwd = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_LAft")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_LAft >= 0 && now != _previousFuelPump_LAft)
                announcer.Announce(now == 1 ? "Left Aft Fuel Pump On" : "Left Aft Fuel Pump Off");
            _previousFuelPump_LAft = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_RFwd")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_RFwd >= 0 && now != _previousFuelPump_RFwd)
                announcer.Announce(now == 1 ? "Right Forward Fuel Pump On" : "Right Forward Fuel Pump Off");
            _previousFuelPump_RFwd = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_RAft")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_RAft >= 0 && now != _previousFuelPump_RAft)
                announcer.Announce(now == 1 ? "Right Aft Fuel Pump On" : "Right Aft Fuel Pump Off");
            _previousFuelPump_RAft = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_CtrL")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_CtrL >= 0 && now != _previousFuelPump_CtrL)
                announcer.Announce(now == 1 ? "Left Center Fuel Pump On" : "Left Center Fuel Pump Off");
            _previousFuelPump_CtrL = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_CtrR")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_CtrR >= 0 && now != _previousFuelPump_CtrR)
                announcer.Announce(now == 1 ? "Right Center Fuel Pump On" : "Right Center Fuel Pump Off");
            _previousFuelPump_CtrR = now;
            return true;
        }
        if (variableKey == "HS787_FuelPump_APU")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelPump_APU >= 0 && now != _previousFuelPump_APU)
                announcer.Announce(now == 1 ? "APU Fuel Pump On" : "APU Fuel Pump Off");
            _previousFuelPump_APU = now;
            return true;
        }
        if (variableKey == "HS787_FuelXfeed")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelXfeedFwd >= 0 && now != _previousFuelXfeedFwd)
                announcer.Announce(now == 1 ? "Fuel Crossfeed Open" : "Fuel Crossfeed Closed");
            _previousFuelXfeedFwd = now;
            return true;
        }
        if (variableKey == "HS787_BleedEng1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBleedEng1 >= 0 && now != _previousBleedEng1)
                announcer.Announce(now == 1 ? "Engine 1 Bleed On" : "Engine 1 Bleed Off");
            _previousBleedEng1 = now;
            return true;
        }
        if (variableKey == "HS787_BleedEng2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBleedEng2 >= 0 && now != _previousBleedEng2)
                announcer.Announce(now == 1 ? "Engine 2 Bleed On" : "Engine 2 Bleed Off");
            _previousBleedEng2 = now;
            return true;
        }
        if (variableKey == "HS787_BleedAPU")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBleedAPU >= 0 && now != _previousBleedAPU)
                announcer.Announce(now == 1 ? "APU Bleed On" : "APU Bleed Off");
            _previousBleedAPU = now;
            return true;
        }
        if (variableKey == "HS787_BleedIso")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBleedIso >= 0 && now != _previousBleedIso)
                announcer.Announce(now == 1 ? "Bleed Isolation Open" : "Bleed Isolation Closed");
            _previousBleedIso = now;
            return true;
        }
        if (variableKey == "HS787_FireTest")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFireTest >= 0 && now != _previousFireTest)
                announcer.Announce(now == 1 ? "Fire and overheat test in progress" : "Fire and overheat test complete");
            _previousFireTest = now;
            return true;
        }
        if (variableKey == "HS787_EngFireHandle1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousEngFireHandle1 >= 0 && now != _previousEngFireHandle1)
                announcer.Announce(now == 1 ? "Engine 1 Fire Handle Pulled" : "Engine 1 Fire Handle Stowed");
            _previousEngFireHandle1 = now;
            return true;
        }
        if (variableKey == "HS787_EngFireHandle2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousEngFireHandle2 >= 0 && now != _previousEngFireHandle2)
                announcer.Announce(now == 1 ? "Engine 2 Fire Handle Pulled" : "Engine 2 Fire Handle Stowed");
            _previousEngFireHandle2 = now;
            return true;
        }
        if (variableKey == "HS787_APUFireHandle")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousAPUFireHandle >= 0 && now != _previousAPUFireHandle)
                announcer.Announce(now == 1 ? "APU Fire Handle Pulled" : "APU Fire Handle Stowed");
            _previousAPUFireHandle = now;
            return true;
        }
        if (variableKey == "HS787_CargoFireFwd")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousCargoFireFwd >= 0 && now != _previousCargoFireFwd)
                announcer.Announce(now == 1 ? "Cargo Fire Arm Forward On" : "Cargo Fire Arm Forward Off");
            _previousCargoFireFwd = now;
            return true;
        }
        if (variableKey == "HS787_CargoFireAft")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousCargoFireAft >= 0 && now != _previousCargoFireAft)
                announcer.Announce(now == 1 ? "Cargo Fire Arm Aft On" : "Cargo Fire Arm Aft Off");
            _previousCargoFireAft = now;
            return true;
        }
        if (variableKey == "HS787_CargoFireDisch")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousCargoFireDisch >= 0 && now != _previousCargoFireDisch && now == 1)
                announcer.Announce("Cargo Fire Discharged");
            _previousCargoFireDisch = now;
            return true;
        }
        if (variableKey == "HS787_StandbyPower")
        {
            int now = (int)value;
            if (_previousStandbyPower >= 0 && now != _previousStandbyPower)
            {
                string msg = now switch
                {
                    1 => "Standby Power Auto",
                    2 => "Standby Power Battery",
                    _ => "Standby Power Off"
                };
                announcer.Announce(msg);
            }
            _previousStandbyPower = now;
            return true;
        }
        if (variableKey == "HS787_BaroSTD")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousBaroSTD >= 0 && now != _previousBaroSTD)
                announcer.Announce(now == 1 ? "Baro Standard" : "Baro QNH");
            _previousBaroSTD = now;
            return true;
        }
        // INTERACTIVE POINT OPEN:N is a 0/100 percent value, so anything above 50 = open/connected.
        if (variableKey == "HS787_GPUPipe")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousGPUPipe >= 0 && now != _previousGPUPipe)
                announcer.Announce(now == 1 ? "GPU Cable Connected" : "GPU Cable Disconnected");
            _previousGPUPipe = now;
            return true;
        }
        if (variableKey == "HS787_RefuelDoor")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousRefuelDoor >= 0 && now != _previousRefuelDoor)
                announcer.Announce(now == 1 ? "Refuel Door Open" : "Refuel Door Closed");
            _previousRefuelDoor = now;
            return true;
        }
        // Passenger + cargo doors (EXIT OPEN:0-9). Open ≥ 50 % is announced as "Open";
        // below as "Closed". The HS787 doesn't simulate door arming / slide deployment —
        // doors are just open/closed; no arm/disarm cycle required before takeoff or
        // disembark for this aircraft.
        if (variableKey == "HS787_Door_1L")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor1L >= 0 && now != _previousDoor1L)
                announcer.Announce(now == 1 ? "Door 1 left open" : "Door 1 left closed");
            _previousDoor1L = now;
            return true;
        }
        if (variableKey == "HS787_Door_1R")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor1R >= 0 && now != _previousDoor1R)
                announcer.Announce(now == 1 ? "Door 1 right open" : "Door 1 right closed");
            _previousDoor1R = now;
            return true;
        }
        if (variableKey == "HS787_Door_2L")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor2L >= 0 && now != _previousDoor2L)
                announcer.Announce(now == 1 ? "Door 2 left open" : "Door 2 left closed");
            _previousDoor2L = now;
            return true;
        }
        if (variableKey == "HS787_Door_2R")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor2R >= 0 && now != _previousDoor2R)
                announcer.Announce(now == 1 ? "Door 2 right open" : "Door 2 right closed");
            _previousDoor2R = now;
            return true;
        }
        if (variableKey == "HS787_Door_3L")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor3L >= 0 && now != _previousDoor3L)
                announcer.Announce(now == 1 ? "Door 3 left open" : "Door 3 left closed");
            _previousDoor3L = now;
            return true;
        }
        if (variableKey == "HS787_Door_3R")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor3R >= 0 && now != _previousDoor3R)
                announcer.Announce(now == 1 ? "Door 3 right open" : "Door 3 right closed");
            _previousDoor3R = now;
            return true;
        }
        if (variableKey == "HS787_Door_4L")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor4L >= 0 && now != _previousDoor4L)
                announcer.Announce(now == 1 ? "Door 4 left open" : "Door 4 left closed");
            _previousDoor4L = now;
            return true;
        }
        if (variableKey == "HS787_Door_4R")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoor4R >= 0 && now != _previousDoor4R)
                announcer.Announce(now == 1 ? "Door 4 right open" : "Door 4 right closed");
            _previousDoor4R = now;
            return true;
        }
        if (variableKey == "HS787_Door_FwdCargo")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoorFwdCargo >= 0 && now != _previousDoorFwdCargo)
                announcer.Announce(now == 1 ? "Forward cargo door open" : "Forward cargo door closed");
            _previousDoorFwdCargo = now;
            return true;
        }
        if (variableKey == "HS787_Door_AftCargo")
        {
            int now = value > 50 ? 1 : 0;
            if (_previousDoorAftCargo >= 0 && now != _previousDoorAftCargo)
                announcer.Announce(now == 1 ? "Aft cargo door open" : "Aft cargo door closed");
            _previousDoorAftCargo = now;
            return true;
        }
        if (variableKey == "HS787_FuelControl1")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelControl1 >= 0 && now != _previousFuelControl1)
                announcer.Announce(now == 1 ? "Engine 1 Fuel Control Run" : "Engine 1 Fuel Control Cutoff");
            _previousFuelControl1 = now;
            return true;
        }
        if (variableKey == "HS787_FuelControl2")
        {
            int now = value > 0.5 ? 1 : 0;
            if (_previousFuelControl2 >= 0 && now != _previousFuelControl2)
                announcer.Announce(now == 1 ? "Engine 2 Fuel Control Run" : "Engine 2 Fuel Control Cutoff");
            _previousFuelControl2 = now;
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
            // Ground-power combos: monitored continuously so the combo's displayed
            // state matches reality from MSFSBA connect; HS787_ExtPwrOn1/2 owns the
            // user-facing announcement, so suppress here to avoid duplicate speech.
            case "HS787_ExtPwr1":
            case "HS787_ExtPwr2":
            // IRS time-to-align: cached for the read-only display field only.
            // The Aligned transition is announced via HS787_IRS_Align; a
            // per-minute spoken countdown would be noise.
            case "HS787_IRS_AlignMinutes":
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
                    BridgeServer.EnqueueMfdCommand("fcu_key:ALTITUDE_INTERVENTION");
                else
                    simConnect.SendHVar("AS01B_FMC_1_ALTITUDE_INTERVENTION");
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
