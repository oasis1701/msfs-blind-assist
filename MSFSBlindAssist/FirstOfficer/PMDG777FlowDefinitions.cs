using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Data-driven PMDG 777 flow definitions.
///
/// Source: Proprietary FO addon flow data translated to BA Assist action model.
/// Each flow executes a sequence of PMDG switch actions with waits and condition checks.
///
/// Captain-only and briefing items are CaptainReminder steps — announced but not automated.
/// Walk-around and timed waits are WaitSeconds/WalkAround steps.
///
/// PMDG event values:
/// - Transponder mode: 0=STBY, 1=ALT-OFF, 2=ON, 3=TA, 4=TA/RA
/// - Autobrake: 0=RTO, 1=Off, 2=1, 3=2, 4=3, 5=4, 6=Auto/Med
/// - Packs: 0=OFF, 1=Auto (single pack), 2=AUTO (both packs auto)
///   (PMDG pack switch: 0=OFF, 1=Auto — two separate events for L and R)
/// - Flaps lever position: 0=UP, 1=1, 2=5, 3=15, 4=20, 5=25, 6=30
/// - EFIS mode: 0=APP, 1=VOR, 2=MAP, 3=NAV, 4=PLN
/// - EFIS range: 0=10, 1=20, 2=40, 3=80, 4=160, 5=320, 6=640
/// - Bus tie: 0=ISLN, 1=AUTO
/// - Seat belts: 0=Off, 1=Auto, 2=On
/// </summary>
public static class PMDG777FlowDefinitions
{
    public static List<FlowDefinition<AircraftStateEvaluator>> Build() => new()
    {
        BuildElectricalPowerUp(),
        BuildCockpitPrep(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildBeforeTaxi(),
        BuildBeforeTakeoff(),
        BuildAfterTakeoff(),
        BuildDescentSetup(),
        BuildApproachSetup(),
        BuildAfterLanding(),
        BuildShutdown(),
        BuildSecure(),
    };

    // -----------------------------------------------------------------------
    // Flow 1: Electrical Power Up (Cold & Dark)
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildElectricalPowerUp() => new()
    {
        Id = "ELECTRICAL_POWER_UP",
        Name = "Electrical Power Up",
        Description = "Powers up the aircraft from cold and dark. Sets all overhead switches to correct initial positions.",
        RelatedChecklistGroupIds = new[] { "ELEC_POWER_UP" },
        Steps = new()
        {
            Skip(SW("EPU_BATTERY",     "Battery: ON",             "EVT_OH_ELEC_BATTERY_SWITCH",         1, "ELEC_Battery_Sw_ON",       v => v > 0.5, "ELEC_POWER_UP_BATTERY"),
                s => s.IsBatteryOn()),
            SW("EPU_STORM_OFF",   "Storm lights: OFF",        "EVT_OH_LIGHTS_STORM",                0),
            Skip(Multi("EPU_ELEC_PUMPS_OFF", "Electric pumps: OFF",
                ("EVT_OH_HYD_ELEC1", 0), ("EVT_OH_HYD_ELEC2", 0)),
                s => !s.IsElecPump1On() && !s.IsElecPump2On()),
            Multi("EPU_DEMAND_OFF", "Demand pumps: OFF",
                ("EVT_OH_HYD_DEMAND_ELEC1", 0), ("EVT_OH_HYD_DEMAND_ELEC2", 0),
                ("EVT_OH_HYD_AIR1", 0), ("EVT_OH_HYD_AIR2", 0)),
            SW("EPU_WIPERS_L",    "Left wiper: OFF",          "EVT_OH_WIPER_LEFT_SWITCH",           0),
            SW("EPU_WIPERS_R",    "Right wiper: OFF",         "EVT_OH_WIPER_RIGHT_SWITCH",          0),
            Skip(SW("EPU_GEAR_DOWN",   "Gear lever: DOWN",         "EVT_GEAR_LEVER",                     1),
                s => s.IsGearDown()),
            SW("EPU_ALT_FLAPS",   "Alternate flaps: OFF",     "EVT_ALTN_FLAPS_POS",                 0),
            Skip(Multi("EPU_BUS_TIES", "Bus ties: AUTO",
                ("EVT_OH_ELEC_BUS_TIE1_SWITCH", 1), ("EVT_OH_ELEC_BUS_TIE2_SWITCH", 1)),
                s => s.IsBusTie1Auto() && s.IsBusTie2Auto()),
            // Try GPU — push both buttons and wait to see if power comes on.
            // APU is never started here; it is always started during Before Start.
            Skip(Momentary("EPU_GND_PWR_PRIM", "Ground power primary: PUSH",  "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"),
                s => s.IsAnyGpuOn()),
            Skip(Momentary("EPU_GND_PWR_SEC",  "Ground power secondary: PUSH", "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),
                s => s.IsAnyGpuOn()),
            Wait("EPU_WAIT_GPU", "Waiting for GPU power", 8),
            Skip(SW("EPU_PARK_BRAKE",  "Parking brake: SET",       "EVT_CONTROL_STAND_PARK_BRAKE_LEVER", 1),
                s => s.IsParkingBrakeSet()),
            Skip(SW("EPU_NAV_LIGHTS",  "Nav lights: ON",            "EVT_OH_LIGHTS_NAV",                  1, "LTS_NAV_Sw_ON", v => v > 0.5),
                s => s.IsNavOn()),
            Skip(SW("EPU_LOGO_LIGHTS", "Logo lights: ON",           "EVT_OH_LIGHTS_LOGO",                 1),
                s => s.IsLogoOn()),
            Momentary("EPU_CVR",  "CVR test",                  "EVT_OH_CVR_TEST"),
            Skip(SW("EPU_ADIRU",       "ADIRU: ON",                 "EVT_OH_ADIRU_SWITCH",                1, "ADIRU_Sw_On", v => v > 0.5),
                s => s.IsADIRUOn()),
            Wait("EPU_WAIT_ADIRU", "ADIRU aligning — 30 seconds", 30),
            SW("EPU_EMER_EXIT_GRD", "Emer exit guard: closed",  "EVT_OH_EMER_EXIT_LIGHT_GUARD",     1),
            Skip(SW("EPU_THRUST_ASYM", "Thrust asym comp: AUTO",    "EVT_OH_THRUST_ASYM_COMP",            1),
                s => s.IsThrustAsymCompAuto()),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 2: Cockpit Preparation
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildCockpitPrep() => new()
    {
        Id = "COCKPIT_PREP",
        Name = "Cockpit Preparation",
        Description = "Sets all overhead systems to preflight state. Run after electrical power is established.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT" },
        Steps = new()
        {
            Skip(SW("CP_IFE",          "IFE/Pass Seats: ON",        "EVT_OH_ELEC_IFE",                    1),
                s => s.IsIFEPassSeatsOn()),
            SW("CP_CABIN_UTIL",   "Cabin/Utility: ON",         "EVT_OH_ELEC_CAB_UTIL",               1),
            Skip(SW("CP_APU_GEN",      "APU Generator: ON",         "EVT_OH_ELEC_APU_GEN_SWITCH",         1),
                s => s.IsApuGenOn()),
            Skip(Multi("CP_BUS_TIES",  "Bus ties: AUTO",
                ("EVT_OH_ELEC_BUS_TIE1_SWITCH", 1), ("EVT_OH_ELEC_BUS_TIE2_SWITCH", 1)),
                s => s.IsBusTie1Auto() && s.IsBusTie2Auto()),
            Skip(Multi("CP_GENERATORS","Generators: ON",
                ("EVT_OH_ELEC_GEN1_SWITCH", 1), ("EVT_OH_ELEC_GEN2_SWITCH", 1)),
                s => s.IsBackupGen1On() && s.IsBackupGen2On()),
            Multi("CP_BACKUP_GENS","Backup generators: ON",
                ("EVT_OH_ELEC_BACKUP_GEN1_SWITCH", 1), ("EVT_OH_ELEC_BACKUP_GEN2_SWITCH", 1)),
            SW("CP_WINDOW_HEAT_1","Window heat 1: ON",         "EVT_OH_ICE_WINDOW_HEAT_1",           1),
            SW("CP_WINDOW_HEAT_2","Window heat 2: ON",         "EVT_OH_ICE_WINDOW_HEAT_2",           1),
            SW("CP_WINDOW_HEAT_3","Window heat 3: ON",         "EVT_OH_ICE_WINDOW_HEAT_3",           1),
            SW("CP_WINDOW_HEAT_4","Window heat 4: ON",         "EVT_OH_ICE_WINDOW_HEAT_4",           1),
            Skip(Multi("CP_ENG_PUMPS", "Engine pumps: ON",
                ("EVT_OH_HYD_ENG1", 1), ("EVT_OH_HYD_ENG2", 1)),
                s => s.IsEngPump1On() && s.IsEngPump2On()),
            Skip(SW("CP_SEAT_BELTS",   "Seat belts: AUTO",          "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH",   1),
                s => s.SeatBeltsSelector() == 1),
            SW("CP_NO_SMOKING",   "No smoking: ON",            "EVT_OH_NO_SMOKING_LIGHT_SWITCH",     2),
            SW("CP_LIGHTS_MASTER","Lights master: ON",         "EVT_OH_LIGHTS_IND_LTS_SWITCH",       1),
            SW("CP_CARGO_FIRE_FWD","Cargo fire arm fwd: OFF",  "EVT_OH_FIRE_CARGO_ARM_FWD",          0),
            SW("CP_CARGO_FIRE_AFT","Cargo fire arm aft: OFF",  "EVT_OH_FIRE_CARGO_ARM_AFT",          0),
            Multi("CP_EEC_MODE",  "EEC mode: NORM",
                ("EVT_OH_EEC_L_SWITCH", 1), ("EVT_OH_EEC_R_SWITCH", 1)),
            SW("CP_AUTOSTART",    "Autostart: ON",             "EVT_OH_ENGINE_AUTOSTART",            1),
            Multi("CP_JETT_OFF",  "Fuel jettison: OFF",
                ("EVT_OH_FUEL_JETTISON_NOZZLE_L", 0), ("EVT_OH_FUEL_JETTISON_NOZZLE_R", 0)),
            SW("CP_JETT_ARM_OFF", "Jettison arm: OFF",         "EVT_OH_FUEL_JETTISON_ARM",           0),
            Multi("CP_XFEED_OFF", "Crossfeed: OFF",
                ("EVT_OH_FUEL_CROSSFEED_FORWARD", 0), ("EVT_OH_FUEL_CROSSFEED_AFT", 0)),
            SW("CP_WING_ANTI_ICE","Wing anti-ice: AUTO",       "EVT_OH_ICE_WING_ANTIICE",            1),
            Multi("CP_ENG_ANTI_ICE","Engine anti-ice: AUTO",
                ("EVT_OH_ICE_ENGINE_ANTIICE_1", 1), ("EVT_OH_ICE_ENGINE_ANTIICE_2", 1)),
            SW("CP_BEACON_OFF",   "Beacon: OFF",               "EVT_OH_LIGHTS_BEACON",               0),
            SW("CP_WING_LIGHTS",  "Wing lights: ON",           "EVT_OH_LIGHTS_WING",                 1),
            SW("CP_EQUIP_COOL",   "Equipment cooling: AUTO",   "EVT_OH_AIRCOND_EQUIP_COOLING_SWITCH",1),
            SW("CP_GASPER",       "Gasper: ON",                "EVT_OH_AIRCOND_GASPER_SWITCH",       1),
            SW("CP_RECIRC_FANS",  "Recirculation fans: ON",    "EVT_OH_AIRCOND_RECIRC_FANS_SWITCH",  1),
            Skip(Multi("CP_PACKS",     "Packs: AUTO",
                ("EVT_OH_AIRCOND_PACK_SWITCH_L", 1), ("EVT_OH_AIRCOND_PACK_SWITCH_R", 1)),
                s => s.IsPack1Auto() && s.IsPack2Auto()),
            Skip(Multi("CP_TRIM_AIR",  "Trim air: ON",
                ("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_L", 1), ("EVT_OH_AIRCOND_TRIM_AIR_SWITCH_R", 1)),
                s => s.IsTrimAir1On() && s.IsTrimAir2On()),
            Skip(Multi("CP_ENG_BLEEDS","Engine bleeds: ON",
                ("EVT_OH_BLEED_ENG_1_SWITCH", 1), ("EVT_OH_BLEED_ENG_2_SWITCH", 1)),
                s => s.IsEngBleed1On() && s.IsEngBleed2On()),
            Skip(SW("CP_APU_BLEED",    "APU bleed: AUTO",           "EVT_OH_BLEED_APU_SWITCH",            1),
                s => s.IsApuBleedOn()),
            Multi("CP_OUTFLOW",   "Outflow valves: AUTO",
                ("EVT_OH_PRESS_VALVE_SWITCH_1", 1), ("EVT_OH_PRESS_VALVE_SWITCH_2", 1)),
            // EFIS setup — Captain side
            SW("CP_EFIS_MODE_C",  "EFIS mode: MAP",            "EVT_EFIS_CPT_MODE",                  2),
            SW("CP_EFIS_RANGE_C", "EFIS range: 40",            "EVT_EFIS_CPT_RANGE",                 2),
            // FO side EFIS
            SW("CP_EFIS_MODE_FO", "FO EFIS mode: MAP",         "EVT_EFIS_FO_MODE",                   2),
            SW("CP_EFIS_RANGE_FO","FO EFIS range: 40",         "EVT_EFIS_FO_RANGE",                  2),
            // MCP — FD and AT Arm off for cold preflight state
            MouseFlag("CP_FD_L",  "Left flight director: OFF", "EVT_MCP_FD_SWITCH_L",     s => !s.IsFDLeftOn()),
            MouseFlag("CP_AT_ARM","AT Arm: OFF",               "EVT_MCP_AT_ARM_SWITCH_L", s => !s.IsATArmLeftOn()),
            MouseFlag("CP_FD_R",  "Right flight director: OFF","EVT_MCP_FD_SWITCH_R",     s => !s.IsFDRightOn()),
            // Autobrake
            Skip(SW("CP_AUTOBRAKE",    "Autobrake: RTO",            "EVT_ABS_AUTOBRAKE_SELECTOR",         0,
               "BRAKES_AutobrakeSelector", v => Math.Abs(v) < 0.1),
                s => s.IsAutobrakRTO()),
            // Fuel control — CUTOFF
            // Note: PMDG lever parameter is inverted: 1=CUTOFF, 0=RUN
            new FlowStep<AircraftStateEvaluator> { Id = "CP_FUEL_CTRL", Label = "Fuel Control: CUTOFF",
                ActionType = FlowStepActionType.SetSwitchMultiple,
                MultiActions = new() {
                    ("EVT_CONTROL_STAND_ENG1_START_LEVER", 1),
                    ("EVT_CONTROL_STAND_ENG2_START_LEVER", 1) },
                PostActionDelayMs = 400 },
            Captain("CP_RESET_CL",   "Reset checklists and obtain IFR clearance"),
            Captain("CP_ATIS",       "Obtain ATIS"),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 3: Before Start
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildBeforeStart() => new()
    {
        Id = "BEFORE_START",
        Name = "Before Start",
        Description = "Prepares the aircraft for engine start. APU started here always, hydraulics pressurised, fuel pumps on.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START", "BEFORE_START_CL" },
        Steps = new()
        {
            Captain("BS_MCP_SPEEDS",  "Set MCP: speed, heading and altitude"),
            Captain("BS_LNAV_VNAV",   "Arm LNAV / VNAV as required"),
            Captain("BS_TRIM_SET",    "Set elevator, aileron, and rudder trim"),
            // APU Start — always done here regardless of GPU status.
            // Selector: OFF → ON (1) → START (2, spring-loads back to ON internally).
            // Fixed 90-second wait because ELEC_APU_Selector returns to 1 immediately
            // after the ON command and is not a reliable "APU running" indicator.
            SW("BS_APU_ON",    "APU selector: ON",    "EVT_OH_ELEC_APU_SEL_SWITCH", 1),
            Wait("BS_APU_ON_WAIT", "Waiting before APU start", 2),
            SW("BS_APU_START", "APU selector: START",  "EVT_OH_ELEC_APU_SEL_SWITCH", 2),
            Wait("BS_APU_WAIT", "Waiting for APU to reach self-sustaining speed", 90),
            Skip(Multi("BS_HYD_ELEC",   "Hydraulic electric pumps: ON",
                ("EVT_OH_HYD_ELEC1", 1), ("EVT_OH_HYD_ELEC2", 1)),
                s => s.IsElecPump1On() && s.IsElecPump2On()),
            Skip(Multi("BS_HYD_ENG",    "Engine pumps: ON",
                ("EVT_OH_HYD_ENG1", 1), ("EVT_OH_HYD_ENG2", 1)),
                s => s.IsEngPump1On() && s.IsEngPump2On()),
            Multi("BS_DEMAND_AUTO","Demand pumps: AUTO",
                ("EVT_OH_HYD_DEMAND_ELEC1", 1), ("EVT_OH_HYD_DEMAND_ELEC2", 1),
                ("EVT_OH_HYD_AIR1", 1), ("EVT_OH_HYD_AIR2", 1)),
            Skip(Multi("BS_FUEL_PUMPS", "Fuel pumps: ON",
                ("EVT_OH_FUEL_PUMP_1_FORWARD", 1), ("EVT_OH_FUEL_PUMP_2_FORWARD", 1),
                ("EVT_OH_FUEL_PUMP_1_AFT", 1),     ("EVT_OH_FUEL_PUMP_2_AFT", 1)),
                s => s.AreWingFuelPumpsOn()),
            Skip(SW("BS_BEACON",    "Beacon: ON",  "EVT_OH_LIGHTS_BEACON", 1,
               "LTS_Beacon_Sw_ON", v => v > 0.5, "BSCL_BEACON"),
                s => s.IsBeaconOn()),
            // Disconnect ground power only if it is actually connected (APU is now running).
            // Each GPU is checked independently — skip if it is already off.
            Skip(Momentary("BS_GND_PWR_1", "Ground power primary: disconnect",
                "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"), s => !s.IsGpuPower1On()),
            Skip(Momentary("BS_GND_PWR_2", "Ground power secondary: disconnect",
                "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),  s => !s.IsGpuPower2On()),
            Captain("BS_TAXI_CLR", "Obtain taxi clearance"),
            Captain("BS_START_ACARS", "Start ACARS if required"),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 4: Engine Start
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildEngineStart() => new()
    {
        Id = "ENGINE_START",
        Name = "Engine Start",
        Description = "Starts both engines in sequence (Engine 2 first, then Engine 1). Waits for start valve to close before proceeding.",
        RelatedChecklistGroupIds = new[] { "ENGINE_START" },
        Steps = new()
        {
            // ---- Engine 2 ----
            // Start selector: send 0 = GND/START position.
            // Data mapping: ENG_Start_Selector [0]="Start", [1]="Norm"
            // So position index 0 is the start/GND position.
            SW("ES_ENG2_START", "Engine 2 start selector: START",
               "EVT_OH_ENGINE_R_START", 0,
               "ENG_Start_Selector_1", v => v < 0.5,
               "ES_ENG2_START_SEL"),
            Wait("ES_E2_WAIT1", "Cranking Engine 2", 3),
            // Fuel Control 2: RUN — PMDG inverted param: 0=RUN, 1=CUTOFF
            new FlowStep<AircraftStateEvaluator> { Id = "ES_FC2_RUN", Label = "Engine 2 fuel control: RUN",
                SpokenLabel = "Engine 2 fuel control run",
                ActionType = FlowStepActionType.SetSwitch,
                EventName = "EVT_CONTROL_STAND_ENG2_START_LEVER",
                TargetValue = 0,  // 0 = RUN (inverted)
                VerifyFieldName = "ENG_FuelControl_Sw_RUN_1", VerifyCondition = v => v > 0.5,
                CompletesChecklistItemId = "ES_ENG2_FUEL_CTRL",
                PostActionDelayMs = 500 },
            // Wait 30 s to ensure the start valve has had time to open before checking for close
            Wait("ES_E2_VALVE_WAIT", "Engine 2 light-off in progress", 30),
            // Start valve closes when the engine reaches self-sustaining speed (~55% N2)
            WaitForField("ES_ENG2_STABLE", "Waiting for Engine 2 start valve to close",
                "ENG_StartValve_1", v => v < 0.5, 120),
            Wait("ES_ENG2_SETTLE", "Engine 2 stabilising at idle", 30),
            // Return start selector to NORM
            SW("ES_ENG2_NORM", "Engine 2 start selector: NORM",
               "EVT_OH_ENGINE_R_START", 1,
               "ENG_Start_Selector_1", v => v > 0.5),

            // ---- Engine 1 ----
            SW("ES_ENG1_START", "Engine 1 start selector: START",
               "EVT_OH_ENGINE_L_START", 0,
               "ENG_Start_Selector_0", v => v < 0.5,
               "ES_ENG1_START_SEL"),
            Wait("ES_E1_WAIT1", "Cranking Engine 1", 3),
            new FlowStep<AircraftStateEvaluator> { Id = "ES_FC1_RUN", Label = "Engine 1 fuel control: RUN",
                SpokenLabel = "Engine 1 fuel control run",
                ActionType = FlowStepActionType.SetSwitch,
                EventName = "EVT_CONTROL_STAND_ENG1_START_LEVER",
                TargetValue = 0,  // 0 = RUN (inverted)
                VerifyFieldName = "ENG_FuelControl_Sw_RUN_0", VerifyCondition = v => v > 0.5,
                CompletesChecklistItemId = "ES_ENG1_FUEL_CTRL",
                PostActionDelayMs = 500 },
            Wait("ES_E1_VALVE_WAIT", "Engine 1 light-off in progress", 30),
            WaitForField("ES_ENG1_STABLE", "Waiting for Engine 1 start valve to close",
                "ENG_StartValve_0", v => v < 0.5, 120),
            Wait("ES_ENG1_SETTLE", "Engine 1 stabilising at idle", 30),
            SW("ES_ENG1_NORM", "Engine 1 start selector: NORM",
               "EVT_OH_ENGINE_L_START", 1,
               "ENG_Start_Selector_0", v => v > 0.5),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 5: Before Taxi
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildBeforeTaxi() => new()
    {
        Id = "BEFORE_TAXI",
        Name = "Before Taxi",
        Description = "Shuts down APU, sets both packs to AUTO, sets takeoff flaps and taxi lighting.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAXI", "BEFORE_TAXI_CL" },
        Steps = new()
        {
            SW("BT_APU_OFF",    "APU: OFF",       "EVT_OH_ELEC_APU_SEL_SWITCH", 0),
            Skip(SW("BT_TAXI_LIGHTS","Taxi lights: ON", "EVT_OH_LIGHTS_TAXI",   1),
                s => s.IsTaxiOn()),
            SW("BT_STORM_OFF",  "Storm lights: OFF", "EVT_OH_LIGHTS_STORM",     0),
            // Takeoff flaps — set from SimBrief perf data (defaults to flaps 5 if not loaded).
            // Only the step matching the planned setting runs; the others are skipped.
            Skip(Momentary("BT_FLAPS_1",  "Flaps: 1",  "EVT_CONTROL_STAND_FLAPS_LEVER_1"), s => s.GetTakeoffFlaps() != 1),
            Skip(Momentary("BT_FLAPS_5",  "Flaps: 5",  "EVT_CONTROL_STAND_FLAPS_LEVER_2"), s => s.GetTakeoffFlaps() != 5),
            Skip(Momentary("BT_FLAPS_15", "Flaps: 15", "EVT_CONTROL_STAND_FLAPS_LEVER_3"), s => s.GetTakeoffFlaps() != 15),
            Skip(Momentary("BT_FLAPS_20", "Flaps: 20", "EVT_CONTROL_STAND_FLAPS_LEVER_4"), s => s.GetTakeoffFlaps() != 20),
            Skip(Momentary("BT_FLAPS_25", "Flaps: 25", "EVT_CONTROL_STAND_FLAPS_LEVER_5"), s => s.GetTakeoffFlaps() != 25),
            // Packs: both to AUTO for departure (skip each if already AUTO)
            Skip(SW("BT_PACK_L_AUTO", "Pack left: AUTO",  "EVT_OH_AIRCOND_PACK_SWITCH_L", 1), s => s.IsPack1Auto()),
            Skip(SW("BT_PACK_R_AUTO", "Pack right: AUTO", "EVT_OH_AIRCOND_PACK_SWITCH_R", 1), s => s.IsPack2Auto()),
            Captain("BT_FCTL_CHECK",   "Check flight controls — confirm free and correct"),
            Captain("BT_SET_TRIM",     "Set stabiliser trim for takeoff"),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 6: Before Takeoff
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF",
        Name = "Before Takeoff",
        Description = "Arms lights, transponder, LNAV/VNAV, and FO instruments for runway lineup.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF", "BEFORE_TKOF_CL" },
        Steps = new()
        {
            Skip(SW("BTKOF_LANDING_L", "Landing lights: ON",         "EVT_OH_LIGHTS_LANDING_LNR",  1),
                s => s.AreLandingLightsOn()),
            Skip(SW("BTKOF_TURNOFF",   "Runway turnoff lights: ON",  "EVT_OH_LIGHTS_LR_TURNOFF",   1),
                s => s.IsRwyTurnoffLOn() || s.IsRwyTurnoffROn()),
            Skip(SW("BTKOF_STROBE",    "Strobe lights: ON",          "EVT_OH_LIGHTS_STROBE",        1),
                s => s.IsStrobeOn()),
            SW("BTKOF_XPNDR",     "Transponder: TA/RA",
               // XPDR_ModeSel via EVT_TCAS_MODE: 0=Stby,1=AltRptgOff,2=Xpndr,3=TA Only,4=TA/RA
               "EVT_TCAS_MODE",                                4),
            Skip(Momentary("BTKOF_LNAV", "LNAV: ARM", "EVT_MCP_LNAV_SWITCH"),
                s => s.IsOn("MCP_annunLNAV")),
            Skip(Momentary("BTKOF_VNAV", "VNAV: ARM", "EVT_MCP_VNAV_SWITCH"),
                s => s.IsOn("MCP_annunVNAV")),
            Captain("BTKOF_FLAPS_CONFIRM", "Confirm flap setting for takeoff"),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 7: After Takeoff
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF",
        Name = "After Takeoff",
        Description = "Cleans up lights and retracts gear/flaps after positive rate of climb.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF", "AFTER_TKOF_CL" },
        Steps = new()
        {
            Skip(SW("ATKOF_TURNOFF_OFF", "Runway turnoff: OFF", "EVT_OH_LIGHTS_LR_TURNOFF",  0),
                s => !s.IsRwyTurnoffLOn() && !s.IsRwyTurnoffROn()),
            Skip(SW("ATKOF_LANDING_OFF", "Landing lights: OFF", "EVT_OH_LIGHTS_LANDING_LNR", 0),
                s => !s.AreLandingLightsOn()),
            Skip(SW("ATKOF_GEAR_UP",     "Gear: UP",            "EVT_GEAR_LEVER",             0,
               "GEAR_Lever", v => v < 0.5, "ATKOF_GEAR"),
                s => s.IsGearUp()),
            Skip(SW("ATKOF_FLAPS_UP",    "Flaps: UP",           "EVT_CONTROL_STAND_FLAPS_LEVER_0", null,
               true, "FCTL_Flaps_Lever", v => v < 0.5, "ATKOF_FLAPS"),
                s => s.AreFlapsUp()),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 8: Descent Setup
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildDescentSetup() => new()
    {
        Id = "DESCENT_SETUP",
        Name = "Descent Setup",
        Description = "Prepares FO instruments and autobrake for descent and approach.",
        RelatedChecklistGroupIds = new[] { "DESCENT", "DESCENT_CL" },
        Steps = new()
        {
            Captain("DSC_LNDG_DATA",  "Set landing data in FMC — VREF and minimums"),
            SW("DSC_AUTOBRAKE",       "Autobrake: AUTO (medium)", "EVT_ABS_AUTOBRAKE_SELECTOR", 6),
            SW("DSC_EFIS_FO_MODE",    "FO EFIS: APP mode",        "EVT_EFIS_FO_MODE",           0),
            SW("DSC_EFIS_FO_RANGE",   "FO EFIS: 20nm range",      "EVT_EFIS_FO_RANGE",          1),
            Captain("DSC_RECALL",     "Recall: Check no unexpected messages"),
            Captain("DSC_APPROACH_BRIEF", "Approach briefing: Complete"),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 9: Approach Setup
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildApproachSetup() => new()
    {
        Id = "APPROACH_SETUP",
        Name = "Approach Setup",
        Description = "Sets altimeters and confirms configuration for approach.",
        RelatedChecklistGroupIds = new[] { "APPROACH", "APPROACH_CL", "LANDING_CL" },
        Steps = new()
        {
            Captain("APP_ALTIMETERS",   "Altimeters: Set local QNH / transition"),
            SW("APP_SPEEDBRAKE_ARM",    "Speedbrake: ARM",   "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", null,
               true, "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5, "LDG_SPEEDBRAKE"),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 10: After Landing
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING",
        Name = "After Landing",
        Description = "Post-touchdown cleanup: lights, speedbrake, autobrake, transponder, APU start.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING" },
        Steps = new()
        {
            Skip(SW("AL_TURNOFF_OFF",  "Runway turnoff: OFF",    "EVT_OH_LIGHTS_LR_TURNOFF",    0),
                s => !s.IsRwyTurnoffLOn() && !s.IsRwyTurnoffROn()),
            Skip(SW("AL_LANDING_OFF",  "Landing lights: OFF",    "EVT_OH_LIGHTS_LANDING_LNR",   0),
                s => !s.AreLandingLightsOn()),
            Skip(SW("AL_STROBE_OFF",   "Strobe: OFF",            "EVT_OH_LIGHTS_STROBE",        0),
                s => !s.IsStrobeOn()),
            Skip(SW("AL_AUTOBRAKE_OFF","Autobrake: OFF",         "EVT_ABS_AUTOBRAKE_SELECTOR",  1,
               "BRAKES_AutobrakeSelector", v => Math.Abs(v - 1) < 0.1, "AL_AUTOBRAKE_OFF"),
                s => s.IsAutobrakeOff()),
            Skip(SW("AL_FLAPS_UP",     "Flaps: UP",              "EVT_CONTROL_STAND_FLAPS_LEVER_0", null,
               true, "FCTL_Flaps_Lever", v => v < 0.5, "AL_FLAPS_UP"),
                s => s.AreFlapsUp()),
            Skip(Momentary("AL_SPEEDBRAKE_DN", "Speedbrake: DOWN", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", "AL_SPEEDBRAKE"),
                s => s.IsSpeedbrakeDown()),
            SW("AL_XPNDR_STBY",   "Transponder: STBY",     "EVT_TCAS_MODE",               0),
            // APU Start sequence for on-ground power
            SW("AL_APU_ON",       "APU selector: ON",       "EVT_OH_ELEC_APU_SEL_SWITCH",  1),
            Wait("AL_APU_WAIT",   "Waiting for APU selector", 2),
            SW("AL_APU_START",    "APU selector: START",    "EVT_OH_ELEC_APU_SEL_SWITCH",  2),
            WaitForField("AL_APU_RUNNING", "Waiting for APU",
                "ELEC_APU_Selector", v => Math.Abs(v - 1) < 0.1, 90),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 11: Shutdown
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildShutdown() => new()
    {
        Id = "SHUTDOWN",
        Name = "Shutdown",
        Description = "Shuts down engines, powers down hydraulic and fuel systems, sets parking configuration.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN", "SHUTDOWN_CL" },
        Steps = new()
        {
            SW("SD_STORM",       "Storm lights: ON",         "EVT_OH_LIGHTS_STORM",        1),
            Skip(SW("SD_PARK_BRAKE",  "Parking brake: SET",       "EVT_CONTROL_STAND_PARK_BRAKE_LEVER", 1,
               "BRAKES_ParkingBrakeLeverOn", v => v > 0.5, "SD_PARK_BRAKE"),
                s => s.IsParkingBrakeSet()),
            // Fuel control: CUTOFF — PMDG inverted: 1=CUTOFF
            new FlowStep<AircraftStateEvaluator> { Id = "SD_FC_CUTOFF", Label = "Fuel Control: CUTOFF",
                ActionType = FlowStepActionType.SetSwitchMultiple,
                MultiActions = new() {
                    ("EVT_CONTROL_STAND_ENG1_START_LEVER", 1),
                    ("EVT_CONTROL_STAND_ENG2_START_LEVER", 1) },
                VerifyFieldName = "ENG_FuelControl_Sw_RUN_0", VerifyCondition = v => v < 0.5,
                CompletesChecklistItemId = "SD_FUEL_CTRL",
                PostActionDelayMs = 500 },
            Wait("SD_ENG_SPOOL", "Engines spooling down", 60),
            SW("SD_SEAT_BELTS",  "Seat belts: OFF",          "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", 0,
               "SIGNS_SeatBeltsSelector", v => v < 0.5, "SD_SEAT_BELTS_OFF"),
            Multi("SD_ENG_PUMPS","Engine pumps: OFF",
                ("EVT_OH_HYD_ENG1", 0), ("EVT_OH_HYD_ENG2", 0)),
            Multi("SD_ELEC_PUMPS","Electric pumps: OFF",
                ("EVT_OH_HYD_ELEC1", 0), ("EVT_OH_HYD_ELEC2", 0)),
            Multi("SD_DEMAND",   "Demand pumps: OFF",
                ("EVT_OH_HYD_DEMAND_ELEC1", 0), ("EVT_OH_HYD_DEMAND_ELEC2", 0),
                ("EVT_OH_HYD_AIR1", 0), ("EVT_OH_HYD_AIR2", 0)),
            Multi("SD_FUEL_PUMPS","Fuel pumps: OFF",
                ("EVT_OH_FUEL_PUMP_1_FORWARD", 0), ("EVT_OH_FUEL_PUMP_2_FORWARD", 0),
                ("EVT_OH_FUEL_PUMP_1_AFT", 0),     ("EVT_OH_FUEL_PUMP_2_AFT", 0),
                ("EVT_OH_FUEL_PUMP_L_CENTER", 0),  ("EVT_OH_FUEL_PUMP_R_CENTER", 0)),
            Skip(SW("SD_BEACON_OFF",  "Beacon: OFF",              "EVT_OH_LIGHTS_BEACON",       0,
               "LTS_Beacon_Sw_ON", v => v < 0.5, "SD_BEACON_OFF"),
                s => !s.IsBeaconOn()),
            SW("SD_TAXI_OFF",    "Taxi lights: OFF",         "EVT_OH_LIGHTS_TAXI",         0),
            MouseFlag("SD_FD_L", "Left FD: OFF",             "EVT_MCP_FD_SWITCH_L", s => !s.IsFDLeftOn()),
            MouseFlag("SD_FD_R", "Right FD: OFF",            "EVT_MCP_FD_SWITCH_R", s => !s.IsFDRightOn()),
            SW("SD_XPNDR_STBY",  "Transponder: STBY",        "EVT_TCAS_MODE",              0),
        }
    };

    // -----------------------------------------------------------------------
    // Flow 12: Secure
    // -----------------------------------------------------------------------
    private static FlowDefinition<AircraftStateEvaluator> BuildSecure() => new()
    {
        Id = "SECURE",
        Name = "Secure Aircraft",
        Description = "Shuts down remaining systems: ADIRU, emergency lights, packs, APU, battery.",
        RelatedChecklistGroupIds = new[] { "SECURE", "SECURE_CL", "ELEC_POWER_DOWN" },
        Steps = new()
        {
            Skip(SW("SEC_ADIRU_OFF", "ADIRU: OFF",           "EVT_OH_ADIRU_SWITCH",         0,
               "ADIRU_Sw_On", v => v < 0.5, "SEC_ADIRU"),
                s => !s.IsADIRUOn()),
            SW("SEC_EMER_LIGHTS","Emer exit lights: OFF","EVT_OH_EMER_EXIT_LIGHT_SWITCH",0),
            Multi("SEC_PACKS",   "Packs: OFF",
                ("EVT_OH_AIRCOND_PACK_SWITCH_L", 0), ("EVT_OH_AIRCOND_PACK_SWITCH_R", 0)),
            SW("SEC_APU_OFF",    "APU: OFF",             "EVT_OH_ELEC_APU_SEL_SWITCH",  0),
            Wait("SEC_APU_WAIT", "APU cooling down", 30),
            Skip(SW("SEC_BATTERY_OFF","Battery: OFF",         "EVT_OH_ELEC_BATTERY_SWITCH",  0,
               "ELEC_Battery_Sw_ON", v => v < 0.5, "EPD_BATTERY_OFF"),
                s => !s.IsBatteryOn()),
        }
    };

    // -----------------------------------------------------------------------
    // Step builder helpers
    // -----------------------------------------------------------------------

    private static FlowStep<AircraftStateEvaluator> SW(string id, string label, string eventName, int? target,
        string? verifyField = null, Func<double, bool>? verifyCond = null,
        string? checklistItemId = null, bool isMomentary = false) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,
        IsMomentary = isMomentary || target == null,
        VerifyFieldName = verifyField,
        VerifyCondition = verifyCond,
        CompletesChecklistItemId = checklistItemId,
        PostActionDelayMs = 350,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static FlowStep<AircraftStateEvaluator> SW(string id, string label, string eventName, int? target,
        bool isMomentary, string? verifyField = null, Func<double, bool>? verifyCond = null,
        string? checklistItemId = null) =>
        SW(id, label, eventName, target, verifyField, verifyCond, checklistItemId, isMomentary);

    // FD / AT Arm are mouse-flag TOGGLES (no absolute target), so firing one while the
    // switch is already in the desired state flips it the wrong way. The skip predicate
    // is required so the step is no-op'd when already correct — the same guard the panel
    // (HandleUIVariableSet) and the checklists (SetFDLeft(target, state)) already apply.
    private static FlowStep<AircraftStateEvaluator> MouseFlag(string id, string label, string eventName,
        Func<AircraftStateEvaluator, bool> skipWhen) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        UsesMouseFlag = true,
        SkipCondition = skipWhen,
        PostActionDelayMs = 350,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static FlowStep<AircraftStateEvaluator> Momentary(string id, string label, string eventName,
        string? checklistItemId = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        IsMomentary = true,
        CompletesChecklistItemId = checklistItemId,
        PostActionDelayMs = 350,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static FlowStep<AircraftStateEvaluator> Multi(string id, string label,
        params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),
        PostActionDelayMs = 400,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static FlowStep<AircraftStateEvaluator> Wait(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,
        PostActionDelayMs = 0,
    };

    private static FlowStep<AircraftStateEvaluator> WaitForField(string id, string label, string field,
        Func<double, bool> condition, int timeoutSec) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = FlowStepFailurePolicy.Skip,
        PostActionDelayMs = 0,
    };

    private static FlowStep<AircraftStateEvaluator> Captain(string id, string label) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = label,
        PostActionDelayMs = 200,
    };

    /// <summary>
    /// Attach a skip condition to a step and return it.
    /// When the condition returns true, the flow engine skips the step
    /// because the aircraft is already in the desired state.
    /// </summary>
    private static FlowStep<AircraftStateEvaluator> Skip(FlowStep<AircraftStateEvaluator> step, Func<AircraftStateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }
}
