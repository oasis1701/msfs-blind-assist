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
public partial class HorizonSim787Definition : BaseAircraftDefinition
{

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
    // Single live-display read-out window (ND / Synoptic-System / Standby). Only one open at a
    // time so it never runs more than one extra Coherent socket; replaced when another is opened.
    private Forms.HS787.HS787DisplayForm? _displayWindow;

    // FD combo intent-tracking. TOGGLE_FLIGHT_DIRECTOR is RELATIVE, and the HS787_FlightDirector
    // cache lags a monitor batch after a toggle — so a rapid combo re-select (On->Off within that
    // lag) would read a stale "current" and double-fire / land inverted. Within a short window we
    // trust our own last-commanded intent instead of the stale cache; after it, the cache (which
    // also reflects an external cockpit FD change) wins again.
    private long _lastFdSetTicks;
    private int  _lastFdDesired;

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
    private int  _previousIrsAlignState = -1;
    // MCP selected-value announce baselines (first value silent, then speak external knob turns).
    private int    _prevMcpHeading = -1;
    private int    _prevMcpAlt     = -1;
    private int    _prevMcpVs      = int.MinValue;
    private int    _prevMcpIas     = -1;
    private double _prevMcpMach    = -1;
    private bool   _mcpIsMach      = false;   // cached from HS787_MCP_IsMach (ProcessSimVarUpdate has no simConnect)
    private bool   _mcpSpdManual   = false;   // cached from HS787_MCP_SpdManual

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
    // The value MSFSBA just commanded via the autobrake combo (-1 = none pending). The single
    // continuous update that lands on this value is swallowed so the user's own set isn't spoken
    // twice (the screen reader already reads the combo). Latched by VALUE, not by a step COUNT —
    // see the autobrake handlers for why a count leaks under the var's 1 Hz sampling.
    private int  _autobrakeSuppressTarget  = -1;
    private long _autobrakeSuppressTicks   = 0;
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
            // IRS: read-only indicators. HS787_IRS_Align is the true, Realistic-respecting
            // alignment state (Coherent-sourced: CoherentHS787IrsClient reads the WT
            // .time-to-align element); the Position vars are the WT_IRS_POS_SET flags.
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
            ["VNAV"] = new List<string>
            {
                "HS787_LV_WTAP_Boeing_VNav_Desired_State",
                "HS787_LV_WTAP_Boeing_VNav_RNP",
                "HS787_LV_WTAP_VNAV_Required_VS",
                "HS787_LV_WTAP_VNav_Alt_Capture_Type",
                "HS787_LV_WTAP_VNav_BOC_Leg_Index",
                "HS787_LV_WTAP_VNav_BOD_Leg_Index",
                "HS787_LV_WTAP_VNav_Constraint_Altitude",
                "HS787_LV_WTAP_VNav_Constraint_Leg_Index",
                "HS787_LV_WTAP_VNav_Distance_To_BOC",
                "HS787_LV_WTAP_VNav_Distance_To_BOD",
                "HS787_LV_WTAP_VNav_Distance_To_Performance_TOD",
                "HS787_LV_WTAP_VNav_Distance_To_TOC",
                "HS787_LV_WTAP_VNav_FPA",
                "HS787_LV_WTAP_VNav_Next_Constraint_Altitude",
                "HS787_LV_WTAP_VNav_Path_Available",
                "HS787_LV_WTAP_VNav_Path_Mode",
                "HS787_LV_WTAP_VNav_Performance_TOD_Distance_In_Leg",
                "HS787_LV_WTAP_VNav_Performance_TOD_Leg_Index",
                "HS787_LV_WTAP_VNav_State",
                "HS787_LV_WTAP_VNav_TOC_Distance_In_Leg",
                "HS787_LV_WTAP_VNav_TOC_Leg_Index",
                "HS787_LV_WTAP_VNav_TOD_Distance_In_Leg",
                "HS787_LV_WTAP_VNav_TOD_Leg_Index",
                "HS787_LV_WTAP_VNav_Target_Altitude",
                "HS787_LV_WTAP_VNav_Vertical_Deviation",
            },
            ["LNAV and Progress"] = new List<string>
            {
                "HS787_LV_WTAP_LNav_Along_Track_Speed",
                "HS787_LV_WTAP_LNav_CDI_Scale",
                "HS787_LV_WTAP_LNav_CDI_Scale_Label",
                "HS787_LV_WTAP_LNav_Course_To_Steer",
                "HS787_LV_WTAP_LNav_DIS",
                "HS787_LV_WTAP_LNav_DTK",
                "HS787_LV_WTAP_LNav_DTK_Mag",
                "HS787_LV_WTAP_LNav_Is_Suspended",
                "HS787_LV_WTAP_LNav_Is_Tracking",
                "HS787_LV_WTAP_LNav_Leg_Distance_Along",
                "HS787_LV_WTAP_LNav_Leg_Distance_Remaining",
                "HS787_LV_WTAP_LNav_Tracked_Leg_Index",
                "HS787_LV_WTAP_LNav_Tracked_Vector_Index",
                "HS787_LV_WTAP_LNav_Transition_Mode",
                "HS787_LV_WTAP_LNav_Vector_Anticipation_Distance",
                "HS787_LV_WTAP_LNav_Vector_Distance_Along",
                "HS787_LV_WTAP_LNav_Vector_Distance_Remaining",
                "HS787_LV_WTAP_LNav_XTK",
                "HS787_LV_WTAP_LPV_Vertical_Deviation",
                "HS787_LV_WTBoeing_LNavData_CDI_Scale_Label",
                "HS787_LV_WTBoeing_LNavData_Destination_Distance_Direct",
                "HS787_LV_WTBoeing_LNavData_Destination_Runway_Distance_Direct",
                "HS787_LV_WTBoeing_LNavData_Faf_Distance",
                "HS787_LV_WTBoeing_LNavData_Map_Distance",
                "HS787_LV_WTBoeing_LNavData_Nominal_Leg_Index",
                "HS787_LV_WTBoeing_LNavData_RNP",
                "HS787_LV_WTBoeing_LNavData_Total_Distance_Direct",
                "HS787_LV_WTBoeing_LNavData_Tracked_Leg_End_Distance",
                "HS787_LV_WT_LNavData_CDI_Scale",
                "HS787_LV_WT_LNavData_DTK_Mag",
                "HS787_LV_WT_LNavData_DTK_True",
                "HS787_LV_WT_LNavData_Destination_Distance",
                "HS787_LV_WT_LNavData_Waypoint_Bearing_Mag",
                "HS787_LV_WT_LNavData_Waypoint_Bearing_True",
                "HS787_LV_WT_LNavData_Waypoint_Distance",
                "HS787_LV_WT_LNavData_XTK",
            },
            ["Glidepath"] = new List<string>
            {
                "HS787_LV_WTAP_GP_Approach_Mode",
                "HS787_LV_WTAP_GP_Distance",
                "HS787_LV_WTAP_GP_FPA",
                "HS787_LV_WTAP_GP_Required_VS",
                "HS787_LV_WTAP_GP_Service_Level",
                "HS787_LV_WTAP_GP_Vertical_Deviation",
            },
            // The WT_FADEC_* family is DEAD on the HS787 (every one reads 0 — verified live in
            // climb + cruise, with cost index entered), so it was replaced with the live engine
            // indications (the same SimVars the Alt+E EICAS window uses).
            ["Engine Data"] = new List<string>
            {
                "HS787_EicasN1_1", "HS787_EicasN1_2",
                "HS787_EicasEGT_1", "HS787_EicasEGT_2",
                "HS787_EicasN2_1", "HS787_EicasN2_2",
                "HS787_EicasOilP_1", "HS787_EicasOilP_2",
                "HS787_EicasOilT_1", "HS787_EicasOilT_2",
                "HS787_EicasFuelKg", "HS787_EicasGwKg", "HS787_EicasTat",
            },
            ["Flight Control Inputs"] = new List<string>
            {
                "HS787_LV_WT_78_AILERON_INPUT",
                "HS787_LV_WT_78_ELEVATOR_INPUT",
                "HS787_LV_WT_78_RUDDER_INPUT",
                "HS787_LV_WT_78_STABILIZER_TRIM_INPUT",
            },
            ["Timers"] = new List<string>
            {
                "HS787_LV_WTFltTimer_Initial_Value",
                "HS787_LV_WTFltTimer_Reference_Time",
                "HS787_LV_WTFltTimer_Reference_Value",
            },
            ["Other Data"] = new List<string>
            {
                "HS787_LV_AIRLINER_V1_SPEED",
                "HS787_LV_AP_VNAV_ARMED",
                "HS787_LV_B787_10_Hud_Brightness_Level",
                "HS787_LV_B787_10_Hud_Brightness_Mode",
                "HS787_LV_HUD_AP_SELECTED_ALTITUDE",
                "HS787_LV_VHF_ACTIVE_INDEX",
                "HS787_LV_WT_BOEING_MINIMUMS_MODE",
                "HS787_LV_WT_Boeing_Autothrottle_Status",
                "HS787_LV_WT_MFD_1_CONTRAST",
                "HS787_LV_WT_MFD_2_CONTRAST",
                "HS787_LV_WT_MINIMUMS_MODE",
                "HS787_LV_WT_PFD_1_CONTRAST",
                "HS787_LV_WT_Virtual_Throttle_Lever_Pos_",
                "HS787_LV_XMLVAR_MFD_Side_",
                "HS787_LV_XMLVAR_ThrottlePosition_",
            },
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
            },
            ["Flight Data"] = new List<string>
            {
                "VNAV",
                "LNAV and Progress",
                "Glidepath",
                "Engine Data",
                "Flight Control Inputs",
                "Timers",
                "Other Data",
            }
        };
    }

    // Panel display-value formatting hook. Renders the baro setting (read as inHg) as hPa for the
    // EFIS panel, instead of a raw "29.92".
    public override bool TryGetDisplayOverride(string varKey, double value, out string text)
    {
        if (varKey == "HS787_BaroSetting")
        {
            int hpa = (int)Math.Round(value * 33.8639);
            text = $"{hpa} hPa ({value:0.00} inHg)";
            return true;
        }
        // EICAS N1/N2 are 0..1 ratios -> percent; gross weight kg -> tonnes.
        if (varKey is "HS787_EicasN1_1" or "HS787_EicasN1_2" or "HS787_EicasN2_1" or "HS787_EicasN2_2")
        {
            text = $"{value * 100:0.0} percent";
            return true;
        }
        if (varKey == "HS787_EicasGwKg")
        {
            text = $"{value / 1000.0:0.0} tonnes";
            return true;
        }
        text = "";
        return false;
    }

    // =========================================================================
    // Variables
    // =========================================================================

    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
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
            // subscriptions instead of the shared GenericBatch1..5 structs. Required because
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
            // is honestly a "position accepted" flag, not "aligned". The realistic
            // "TIME TO ALIGN" countdown is not on any L-var (only the WT Coherent bus);
            // reading it over the Coherent ND view is a verification-flight TODO.
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

            // True IRS alignment status. The WT "TIME TO ALIGN" countdown is exposed only on
            // the ND/PFD .time-to-align DOM element (the one IRS L-var WT sets, WT_IRS_POS_SET_N,
            // is "position accepted", NOT alignment complete). CoherentHS787IrsClient reads that
            // element over the Coherent debugger and writes these synthetic L-vars; we read them
            // here through the normal SimVar path. Read-only display. Values:
            //   0 = Off   1 = Aligning   2 = Aligned (Navigation)
            ["HS787_IRS_Align"] = new SimConnect.SimVarDefinition
            {
                Name = "MSFSBA_IRS_ALIGN_STATE",   // synthetic L-var written by the Coherent IRS client
                DisplayName = "IRS",
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

            // Minutes remaining in the WT "TIME TO ALIGN" countdown (Coherent IRS client feed).
            // -1 = not aligning / unknown. Cache-only (silent) — surfaced in the read-only IRS
            // display field so the pilot can check time remaining on demand without a noisy
            // per-minute callout. The completion is announced by HS787_IRS_Align -> Aligned.
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
                // The WT 787 tracks the selector on AUTO BRAKE SWITCH CB (0..6), NOT
                // AUTOBRAKE CONTROL SWITCH POSITION (which is stuck at 0). Live-verified 7 detents.
                Name = "AUTO BRAKE SWITCH CB",
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
                    [5] = "4",
                    [6] = "MAX"
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

            // --- EICAS engine indications: cached for the Alt+E EICAS window (suppressed in
            // ProcessSimVarUpdate so they never auto-announce). N1/N2 are ratios (x100 in the
            // window), EGT in celsius, fuel in kg. ---
            ["HS787_EicasN1_1"] = new SimConnect.SimVarDefinition { Name = "TURB ENG CORRECTED N1:1", DisplayName = "Engine 1 N1", Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasN1_2"] = new SimConnect.SimVarDefinition { Name = "TURB ENG CORRECTED N1:2", DisplayName = "Engine 2 N1", Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasN2_1"] = new SimConnect.SimVarDefinition { Name = "TURB ENG CORRECTED N2:1", DisplayName = "Engine 1 N2", Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasN2_2"] = new SimConnect.SimVarDefinition { Name = "TURB ENG CORRECTED N2:2", DisplayName = "Engine 2 N2", Type = SimConnect.SimVarType.SimVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasEGT_1"] = new SimConnect.SimVarDefinition { Name = "ENG EXHAUST GAS TEMPERATURE:1", DisplayName = "Engine 1 EGT", Type = SimConnect.SimVarType.SimVar, Units = "celsius", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasEGT_2"] = new SimConnect.SimVarDefinition { Name = "ENG EXHAUST GAS TEMPERATURE:2", DisplayName = "Engine 2 EGT", Type = SimConnect.SimVarType.SimVar, Units = "celsius", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasFuelKg"] = new SimConnect.SimVarDefinition { Name = "FUEL TOTAL QUANTITY WEIGHT", DisplayName = "Total Fuel", Type = SimConnect.SimVarType.SimVar, Units = "kilograms", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasGwKg"]   = new SimConnect.SimVarDefinition { Name = "TOTAL WEIGHT", DisplayName = "Gross Weight", Type = SimConnect.SimVarType.SimVar, Units = "kilograms", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasOilP_1"] = new SimConnect.SimVarDefinition { Name = "GENERAL ENG OIL PRESSURE:1", DisplayName = "Engine 1 Oil Pressure", Type = SimConnect.SimVarType.SimVar, Units = "psi", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasOilP_2"] = new SimConnect.SimVarDefinition { Name = "GENERAL ENG OIL PRESSURE:2", DisplayName = "Engine 2 Oil Pressure", Type = SimConnect.SimVarType.SimVar, Units = "psi", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasOilT_1"] = new SimConnect.SimVarDefinition { Name = "GENERAL ENG OIL TEMPERATURE:1", DisplayName = "Engine 1 Oil Temperature", Type = SimConnect.SimVarType.SimVar, Units = "celsius", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasOilT_2"] = new SimConnect.SimVarDefinition { Name = "GENERAL ENG OIL TEMPERATURE:2", DisplayName = "Engine 2 Oil Temperature", Type = SimConnect.SimVarType.SimVar, Units = "celsius", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },
            ["HS787_EicasTat"]    = new SimConnect.SimVarDefinition { Name = "TOTAL AIR TEMPERATURE", DisplayName = "TAT", Type = SimConnect.SimVarType.SimVar, Units = "celsius", UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true },

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
                // LIGHT LOGO lags the toggle by one frame (read it right after TOGGLE_LOGO_LIGHTS and
                // it's stale); LIGHT LOGO ON updates immediately — use it for a correct read-back.
                Name = "LIGHT LOGO ON",
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
                // Was the dead L:var XMLVAR_Baro (read a constant 0 -> "Baro 0"). The real captain
                // baro is the stock KOHLSMAN SETTING (same source the working altimeter hotkey uses);
                // TryGetDisplayOverride formats it as hPa / Standard.
                Name = "KOHLSMAN SETTING HG",
                DisplayName = "Baro Setting",
                Type = SimConnect.SimVarType.SimVar,
                Units = "inHg",
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

            // Transponder IDENT — momentary action button. Fires XPNDR_IDENT_ON via SendEvent
            // (the A380 def uses the same event; MSFSBA's SendEvent maps it by raw name even
            // though the MCP event registry doesn't list it). Lives in the Transponder panel.
            ["HS787_XpndrIdent"] = new SimConnect.SimVarDefinition
            {
                Name = "MSFSBA_787_XPNDR_IDENT",   // synthetic marker — action only, never read
                DisplayName = "Transponder Ident",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                IsAnnounced = false
            },

            // Flight Director (combined L+R) — live-verified settable: TOGGLE_FLIGHT_DIRECTOR
            // flips AUTOPILOT FLIGHT DIRECTOR ACTIVE 0<->1. Off/On combo in the MCP panel
            // (the FD had no control before).
            ["HS787_FlightDirector"] = new SimConnect.SimVarDefinition
            {
                Name = "AUTOPILOT FLIGHT DIRECTOR ACTIVE",
                DisplayName = "Flight Director",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
            },

            // ===================================================================
            // FLIGHT DATA — read-only L:var telemetry auto-extracted from the WT/HS787
            // instrument JS var surface (VNAV / LNAV / glidepath / FADEC / timers / etc.).
            // Numeric readouts; exact units + enum decode pending an in-flight pass.
            // ===================================================================
            ["HS787_LV_AIRLINER_V1_SPEED"] = new SimConnect.SimVarDefinition
            {
                Name = "AIRLINER_V1_SPEED",
                DisplayName = "AIRLINER V1 SPEED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_AP_VNAV_ARMED"] = new SimConnect.SimVarDefinition
            {
                Name = "AP_VNAV_ARMED",
                DisplayName = "AP VNAV ARMED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_B787_10_Hud_Brightness_Level"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_10_Hud_Brightness_Level",
                DisplayName = "Hud Brightness Level",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_B787_10_Hud_Brightness_Mode"] = new SimConnect.SimVarDefinition
            {
                Name = "B787_10_Hud_Brightness_Mode",
                DisplayName = "Hud Brightness Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_HUD_AP_SELECTED_ALTITUDE"] = new SimConnect.SimVarDefinition
            {
                Name = "HUD_AP_SELECTED_ALTITUDE",
                DisplayName = "HUD AP SELECTED ALTITUDE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_VHF_ACTIVE_INDEX"] = new SimConnect.SimVarDefinition
            {
                Name = "VHF_ACTIVE_INDEX",
                DisplayName = "VHF ACTIVE INDEX",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_Boeing_VNav_Desired_State"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_Boeing_VNav_Desired_State",
                DisplayName = "VNAV Desired State",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_Boeing_VNav_RNP"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_Boeing_VNav_RNP",
                DisplayName = "VNAV RNP",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_GP_Approach_Mode"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_GP_Approach_Mode",
                DisplayName = "GP Approach Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_GP_Distance"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_GP_Distance",
                DisplayName = "GP Distance",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_GP_FPA"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_GP_FPA",
                DisplayName = "GP FPA",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_GP_Required_VS"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_GP_Required_VS",
                DisplayName = "GP Required VS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_GP_Service_Level"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_GP_Service_Level",
                DisplayName = "GP Service Level",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_GP_Vertical_Deviation"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_GP_Vertical_Deviation",
                DisplayName = "GP Vertical Deviation",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Along_Track_Speed"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Along_Track_Speed",
                DisplayName = "LNAV Along Track Speed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_CDI_Scale"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_CDI_Scale",
                DisplayName = "LNAV CDI Scale",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_CDI_Scale_Label"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_CDI_Scale_Label",
                DisplayName = "LNAV CDI Scale Label",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Course_To_Steer"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Course_To_Steer",
                DisplayName = "LNAV Course To Steer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_DIS"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_DIS",
                DisplayName = "LNAV DIS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_DTK"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_DTK",
                DisplayName = "LNAV DTK",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_DTK_Mag"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_DTK_Mag",
                DisplayName = "LNAV DTK Mag",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Is_Suspended"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Is_Suspended",
                DisplayName = "LNAV Is Suspended",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Not suspended", [1] = "Suspended" }
            },
            ["HS787_LV_WTAP_LNav_Is_Tracking"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Is_Tracking",
                DisplayName = "LNAV Is Tracking",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Not tracking", [1] = "Tracking" }
            },
            ["HS787_LV_WTAP_LNav_Leg_Distance_Along"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Leg_Distance_Along",
                DisplayName = "LNAV Leg Distance Along",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Leg_Distance_Remaining"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Leg_Distance_Remaining",
                DisplayName = "LNAV Leg Distance Remaining",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Tracked_Leg_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Tracked_Leg_Index",
                DisplayName = "LNAV Tracked Leg Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Tracked_Vector_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Tracked_Vector_Index",
                DisplayName = "LNAV Tracked Vector Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Transition_Mode"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Transition_Mode",
                DisplayName = "LNAV Transition Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Vector_Anticipation_Distance"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Vector_Anticipation_Distance",
                DisplayName = "LNAV Vector Anticipation Distance",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Vector_Distance_Along"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Vector_Distance_Along",
                DisplayName = "LNAV Vector Distance Along",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_Vector_Distance_Remaining"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_Vector_Distance_Remaining",
                DisplayName = "LNAV Vector Distance Remaining",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LNav_XTK"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LNav_XTK",
                DisplayName = "LNAV XTK",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_LPV_Vertical_Deviation"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_LPV_Vertical_Deviation",
                DisplayName = "LPV Vertical Deviation",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNAV_Required_VS"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNAV_Required_VS",
                DisplayName = "VNAV Required VS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Alt_Capture_Type"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Alt_Capture_Type",
                DisplayName = "VNAV Alt Capture Type",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_BOC_Leg_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_BOC_Leg_Index",
                DisplayName = "VNAV BOC Leg Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_BOD_Leg_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_BOD_Leg_Index",
                DisplayName = "VNAV BOD Leg Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Constraint_Altitude"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Constraint_Altitude",
                DisplayName = "VNAV Constraint Altitude",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Constraint_Leg_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Constraint_Leg_Index",
                DisplayName = "VNAV Constraint Leg Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Distance_To_BOC"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Distance_To_BOC",
                DisplayName = "VNAV Distance To BOC",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Distance_To_BOD"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Distance_To_BOD",
                DisplayName = "VNAV Distance To BOD",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Distance_To_Performance_TOD"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Distance_To_Performance_TOD",
                DisplayName = "VNAV Distance To Performance TOD",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Distance_To_TOC"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Distance_To_TOC",
                DisplayName = "VNAV Distance To TOC",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_FPA"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_FPA",
                DisplayName = "VNAV FPA",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Next_Constraint_Altitude"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Next_Constraint_Altitude",
                DisplayName = "VNAV Next Constraint Altitude",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Path_Available"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Path_Available",
                DisplayName = "VNAV Path Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Not available", [1] = "Available" }
            },
            ["HS787_LV_WTAP_VNav_Path_Mode"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Path_Mode",
                DisplayName = "VNAV Path Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Performance_TOD_Distance_In_Leg"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Performance_TOD_Distance_In_Leg",
                DisplayName = "VNAV Performance TOD Distance In Leg",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Performance_TOD_Leg_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Performance_TOD_Leg_Index",
                DisplayName = "VNAV Performance TOD Leg Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_State"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_State",
                DisplayName = "VNAV State",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_TOC_Distance_In_Leg"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_TOC_Distance_In_Leg",
                DisplayName = "VNAV TOC Distance In Leg",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_TOC_Leg_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_TOC_Leg_Index",
                DisplayName = "VNAV TOC Leg Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_TOD_Distance_In_Leg"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_TOD_Distance_In_Leg",
                DisplayName = "VNAV TOD Distance In Leg",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_TOD_Leg_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_TOD_Leg_Index",
                DisplayName = "VNAV TOD Leg Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Target_Altitude"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Target_Altitude",
                DisplayName = "VNAV Target Altitude",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTAP_VNav_Vertical_Deviation"] = new SimConnect.SimVarDefinition
            {
                Name = "WTAP_VNav_Vertical_Deviation",
                DisplayName = "VNAV Vertical Deviation",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_CDI_Scale_Label"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_CDI_Scale_Label",
                DisplayName = "LNavData CDI Scale Label",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_Destination_Distance_Direct"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_Destination_Distance_Direct",
                DisplayName = "LNavData Destination Distance Direct",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_Destination_Runway_Distance_Direct"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_Destination_Runway_Distance_Direct",
                DisplayName = "LNavData Destination Runway Distance Direct",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_Faf_Distance"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_Faf_Distance",
                DisplayName = "LNavData FAF Distance",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_Map_Distance"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_Map_Distance",
                DisplayName = "LNavData Map Distance",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_Nominal_Leg_Index"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_Nominal_Leg_Index",
                DisplayName = "LNavData Nominal Leg Index",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_RNP"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_RNP",
                DisplayName = "LNavData RNP",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_Total_Distance_Direct"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_Total_Distance_Direct",
                DisplayName = "LNavData Total Distance Direct",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTBoeing_LNavData_Tracked_Leg_End_Distance"] = new SimConnect.SimVarDefinition
            {
                Name = "WTBoeing_LNavData_Tracked_Leg_End_Distance",
                DisplayName = "LNavData Tracked Leg End Distance",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTFltTimer_Initial_Value"] = new SimConnect.SimVarDefinition
            {
                Name = "WTFltTimer_Initial_Value",
                DisplayName = "Initial Value",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTFltTimer_Reference_Time"] = new SimConnect.SimVarDefinition
            {
                Name = "WTFltTimer_Reference_Time",
                DisplayName = "Reference Time",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WTFltTimer_Reference_Value"] = new SimConnect.SimVarDefinition
            {
                Name = "WTFltTimer_Reference_Value",
                DisplayName = "Reference Value",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_78_AILERON_INPUT"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_78_AILERON_INPUT",
                DisplayName = "AILERON INPUT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_78_ELEVATOR_INPUT"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_78_ELEVATOR_INPUT",
                DisplayName = "ELEVATOR INPUT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_78_RUDDER_INPUT"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_78_RUDDER_INPUT",
                DisplayName = "RUDDER INPUT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_78_STABILIZER_TRIM_INPUT"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_78_STABILIZER_TRIM_INPUT",
                DisplayName = "STABILIZER TRIM INPUT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_BOEING_MINIMUMS_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_BOEING_MINIMUMS_MODE",
                DisplayName = "BOEING MINIMUMS MODE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_Boeing_Autothrottle_Status"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_Boeing_Autothrottle_Status",
                DisplayName = "Autothrottle Status",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_CLB_N1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_CLB_N1",
                DisplayName = "CLB N1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_CRU_N1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_CRU_N1",
                DisplayName = "CRU N1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_EGT_AMBER"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_EGT_AMBER",
                DisplayName = "EGT AMBER",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_EGT_RED"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_EGT_RED",
                DisplayName = "EGT RED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_EGT_START_LIMIT"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_EGT_START_LIMIT",
                DisplayName = "EGT START LIMIT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_IDLE_N1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_IDLE_N1",
                DisplayName = "IDLE N1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_IDLE_N2"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_IDLE_N2",
                DisplayName = "IDLE N2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_N1_AMBER"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_N1_AMBER",
                DisplayName = "N1 AMBER",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_N1_RED"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_N1_RED",
                DisplayName = "N1 RED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_N2_AMBER"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_N2_AMBER",
                DisplayName = "N2 AMBER",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_N2_RED"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_N2_RED",
                DisplayName = "N2 RED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_OIL_TEMP_HIGH_AMBER"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_OIL_TEMP_HIGH_AMBER",
                DisplayName = "OIL TEMP HIGH AMBER",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_OIL_TEMP_LOW_AMBER"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_OIL_TEMP_LOW_AMBER",
                DisplayName = "OIL TEMP LOW AMBER",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_OIL_TEMP_LOW_RED"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_OIL_TEMP_LOW_RED",
                DisplayName = "OIL TEMP LOW RED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Exceedance" }
            },
            ["HS787_LV_WT_FADEC_REF_N1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_REF_N1",
                DisplayName = "REF N1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_REF_TPR"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_REF_TPR",
                DisplayName = "REF TPR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_TGT_N1"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_TGT_N1",
                DisplayName = "TGT N1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_FADEC_TGT_TPR"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_FADEC_TGT_TPR",
                DisplayName = "TGT TPR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_LNavData_CDI_Scale"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_LNavData_CDI_Scale",
                DisplayName = "CDI Scale",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_LNavData_DTK_Mag"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_LNavData_DTK_Mag",
                DisplayName = "DTK Mag",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_LNavData_DTK_True"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_LNavData_DTK_True",
                DisplayName = "DTK True",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_LNavData_Destination_Distance"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_LNavData_Destination_Distance",
                DisplayName = "Destination Distance",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_LNavData_Waypoint_Bearing_Mag"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_LNavData_Waypoint_Bearing_Mag",
                DisplayName = "Waypoint Bearing Mag",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_LNavData_Waypoint_Bearing_True"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_LNavData_Waypoint_Bearing_True",
                DisplayName = "Waypoint Bearing True",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_LNavData_Waypoint_Distance"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_LNavData_Waypoint_Distance",
                DisplayName = "Waypoint Distance",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_LNavData_XTK"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_LNavData_XTK",
                DisplayName = "XTK",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_MFD_1_CONTRAST"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_MFD_1_CONTRAST",
                DisplayName = "MFD 1 CONTRAST",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_MFD_2_CONTRAST"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_MFD_2_CONTRAST",
                DisplayName = "MFD 2 CONTRAST",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_MINIMUMS_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_MINIMUMS_MODE",
                DisplayName = "MINIMUMS MODE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_PFD_1_CONTRAST"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_PFD_1_CONTRAST",
                DisplayName = "PFD 1 CONTRAST",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_WT_Virtual_Throttle_Lever_Pos_"] = new SimConnect.SimVarDefinition
            {
                Name = "WT_Virtual_Throttle_Lever_Pos_",
                DisplayName = "Virtual Throttle Lever Pos",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_XMLVAR_MFD_Side_"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_MFD_Side_",
                DisplayName = "XMLVAR MFD Side",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
            ["HS787_LV_XMLVAR_ThrottlePosition_"] = new SimConnect.SimVarDefinition
            {
                Name = "XMLVAR_ThrottlePosition_",
                DisplayName = "XMLVAR ThrottlePosition",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false
            },
        };

        var variables = GetBaseVariables();
        foreach (var kvp in aircraftVariables)
            variables[kvp.Key] = kvp.Value;

        // Force ALL HS787 Continuous+IsAnnounced variables onto per-var continuous
        // subscriptions (ExcludeFromBatch = true) rather than the shared GenericBatch1..5
        // structs. The batched read was observed to deliver wrong/oscillating values for
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

}
