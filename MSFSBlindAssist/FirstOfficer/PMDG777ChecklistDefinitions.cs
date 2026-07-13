using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer;

// Shorthand for the generic checklist item type this aircraft's definitions build.
using Item = MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<
    MSFSBlindAssist.FirstOfficer.AircraftActionExecutor,
    MSFSBlindAssist.FirstOfficer.AircraftStateEvaluator>;

/// <summary>
/// Data-driven PMDG 777 checklist definitions.
///
/// Design rules (2026-07 parity audit — mirrors the 737 pass):
/// - Every switch step in a flow has a matching item in the flow's related state group,
///   fired through the SAME executor dispatch when ticked.
/// - AutoDetectable items are ALL <see cref="RevertBehavior.RevertToState"/> (live state
///   mirrors). StayComplete latched items whose target state coincidentally matched an
///   EARLIER phase (beacon OFF at cold-and-dark, the whole Shutdown/Secure set at session
///   start) and then showed complete while the switch was elsewhere — the "falsely
///   checked" bug. ChecklistManager's manual-tick grace keeps a fresh tick from reverting
///   before its frame-paced writes land.
/// - Toggle-type actions (FD, LNAV/VNAV pushes, GPU connect/disconnect) read current
///   state before pressing so a tick can never flip a switch the wrong way.
/// - Items whose target depends on runtime data use SimBrief-derived values where
///   available (takeoff flaps), otherwise stay manual.
/// </summary>
public static class PMDG777ChecklistDefinitions
{
    public static List<ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator>> Build() => new()
    {
        BuildElectricalPowerUp(),
        BuildPreflight(),
        BuildPreflightChecklist(),
        BuildBeforeStart(),
        BuildBeforeStartChecklist(),
        BuildEngineStart(),
        BuildBeforeTaxi(),
        BuildBeforeTaxiChecklist(),
        BuildBeforeTakeoff(),
        BuildBeforeTakeoffChecklist(),
        BuildAfterTakeoff(),
        BuildAfterTakeoffChecklist(),
        BuildDescent(),
        BuildDescentChecklist(),
        BuildApproach(),
        BuildApproachChecklist(),
        BuildLandingChecklist(),
        BuildAfterLanding(),
        BuildShutdown(),
        BuildShutdownChecklist(),
        BuildSecure(),
        BuildSecureChecklist(),
        BuildElectricalPowerDown(),
    };

    // -----------------------------------------------------------------------
    // 1. Electrical Power Up (mirrors the Electrical Power Up flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildElectricalPowerUp() => new()
    {
        Id = "ELEC_POWER_UP", Name = "Electrical Power Up",
        Items = new()
        {
            Auto("EPU_BATTERY", "ELEC_POWER_UP", "Battery: ON",
                "ELEC_Battery_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetBattery(1)),
            Auto("EPU_STORM_OFF", "ELEC_POWER_UP", "Storm lights: OFF",
                "LTS_Storm_Sw_ON", v => v < 0.5,
                action: (e, _) => e.SetStormLights(0)),
            Auto("EPU_ELEC_PUMPS_OFF", "ELEC_POWER_UP", "Electric primary pump switches: OFF",
                "HYD_PrimaryElecPump_Sw_ON_0", v => v < 0.5,
                new[] { "HYD_PrimaryElecPump_Sw_ON_1" },
                (e, _) => e.SetElecPumps(0)),
            Auto("EPU_DEMAND_PUMPS_OFF", "ELEC_POWER_UP", "Demand pump selectors: OFF",
                "HYD_DemandElecPump_Selector_0", v => v < 0.5,
                new[] { "HYD_DemandElecPump_Selector_1", "HYD_DemandAirPump_Selector_0", "HYD_DemandAirPump_Selector_1" },
                (e, _) => e.SetDemandPumps(0)),
            // No wiper-selector state field exists in the 777 CDA struct — action-only.
            ActionManual("EPU_WIPERS_OFF", "ELEC_POWER_UP", "Wiper selectors: OFF",
                (e, _) => e.SetWipers(0)),
            Auto("EPU_GEAR_DOWN", "ELEC_POWER_UP", "Landing gear lever: DOWN",
                "GEAR_Lever", v => Math.Abs(v - 1) < 0.1,
                action: (e, _) => e.SetGearLever(1)),
            // No reliable alternate-flaps position field — action-only.
            ActionManual("EPU_ALT_FLAPS_OFF", "ELEC_POWER_UP", "Alternate Flaps selector: OFF",
                (e, _) => e.SetAltFlaps(0)),
            Auto("EPU_BUS_TIES", "ELEC_POWER_UP", "Bus Tie switches: AUTO",
                "ELEC_BusTie_Sw_AUTO_0", v => v > 0.5,
                new[] { "ELEC_BusTie_Sw_AUTO_1" },
                (e, _) => e.SetBusTies(1)),
            // GPU buttons are momentary TOGGLES — press only the side that is not yet on,
            // or a tick on an already-connected GPU would DISCONNECT it. Auto-ticks from
            // the FO_ANY_GPU_ON synthetic (either ext-power annunciator lit).
            Auto("EPU_GND_PWR", "ELEC_POWER_UP", "External power: ON (if available)",
                "FO_ANY_GPU_ON", v => v > 0.5,
                action: (e, s) =>
                {
                    if (!s.IsGpuPower1On()) e.PushGroundPowerPrimary();
                    if (!s.IsGpuPower2On()) e.PushGroundPowerSecondary();
                }),
            Auto("EPU_PARK_BRAKE", "ELEC_POWER_UP", "Parking brake: SET",
                "BRAKES_ParkingBrakeLeverOn", v => v > 0.5,
                action: (e, _) => e.SetParkingBrake(1)),
            Auto("EPU_NAV_LIGHTS", "ELEC_POWER_UP", "Navigation lights: ON",
                "LTS_NAV_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetNavLights(1)),
            Auto("EPU_LOGO_LIGHTS", "ELEC_POWER_UP", "Logo lights: ON",
                "LTS_Logo_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetLogoLights(1)),
            Auto("EPU_ADIRU", "ELEC_POWER_UP", "ADIRU switch: ON",
                "ADIRU_Sw_On", v => v > 0.5,
                action: (e, _) => e.SetAdiru(1)),
            Auto("EPU_THRUST_ASYM", "ELEC_POWER_UP", "Thrust Asymmetry Compensation: AUTO",
                "FCTL_ThrustAsymComp_Sw_AUTO", v => v > 0.5,
                action: (e, _) => e.SetThrustAsymComp(1)),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Preflight (mirrors the Cockpit Preparation flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = new()
        {
            Auto("PF_BATTERY", "PREFLIGHT", "Battery switch: ON",
                "ELEC_Battery_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetBattery(1)),
            Auto("PF_ADIRU", "PREFLIGHT", "ADIRU switch: ON",
                "ADIRU_Sw_On", v => v > 0.5,
                action: (e, _) => e.SetAdiru(1)),
            Auto("PF_IFE", "PREFLIGHT", "IFE/Pass Seats power: ON",
                "ELEC_IFEPassSeatsSw", v => v > 0.5,
                action: (e, _) => e.SetIFEPassSeats(1)),
            Auto("PF_CABIN_UTIL", "PREFLIGHT", "Cabin/Utility power: ON",
                "ELEC_CabUtilSw", v => v > 0.5,
                action: (e, _) => e.SetCabinUtility(1)),
            Auto("PF_APU_GEN", "PREFLIGHT", "APU Generator: ON",
                "ELEC_APUGen_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetApuGenerator(1)),
            Auto("PF_BUS_TIES", "PREFLIGHT", "Bus Tie switches: AUTO",
                "ELEC_BusTie_Sw_AUTO_0", v => v > 0.5,
                new[] { "ELEC_BusTie_Sw_AUTO_1" },
                (e, _) => e.SetBusTies(1)),
            Auto("PF_GENERATORS", "PREFLIGHT", "Generator Control switches: ON",
                "ELEC_Gen_Sw_ON_0", v => v > 0.5,
                new[] { "ELEC_Gen_Sw_ON_1" },
                (e, _) => e.SetGenerators(1)),
            Auto("PF_BACKUP_GENS", "PREFLIGHT", "Backup Generator switches: ON",
                "ELEC_BackupGen_Sw_ON_0", v => v > 0.5,
                new[] { "ELEC_BackupGen_Sw_ON_1" },
                (e, _) => e.SetBackupGenerators(1)),
            // Manual-tick held test — no persistent state for "test performed"
            // (Fenix PF_FIRE_* pattern); ticking runs the same held test as the flow.
            ActionManualAsync("PF_FIRE_TEST", "PREFLIGHT", "Fire and overheat test",
                (e, _) => e.FireOvhtTestAsync()),
            Auto("PF_WINDOW_HEAT", "PREFLIGHT", "Window Heat switches: ON",
                "ICE_WindowHeat_Sw_ON_0", v => v > 0.5,
                new[] { "ICE_WindowHeat_Sw_ON_1", "ICE_WindowHeat_Sw_ON_2", "ICE_WindowHeat_Sw_ON_3" },
                (e, _) => e.SetWindowHeat(1)),
            Auto("PF_ENG_PUMPS", "PREFLIGHT", "Engine Primary pump switches: ON",
                "HYD_PrimaryEngPump_Sw_ON_0", v => v > 0.5,
                new[] { "HYD_PrimaryEngPump_Sw_ON_1" },
                (e, _) => e.SetEngPumps(1)),
            Auto("PF_CTR_PUMPS_OFF", "PREFLIGHT", "Center Electric pump switches: OFF",
                "FUEL_PumpCtr_Sw_0", v => v < 0.5,
                new[] { "FUEL_PumpCtr_Sw_1" },
                (e, _) => e.SetCenterFuelPumps(0)),
            Auto("PF_DEMAND_PUMPS_OFF", "PREFLIGHT", "Demand pump selectors: OFF",
                "HYD_DemandElecPump_Selector_0", v => v < 0.5,
                new[] { "HYD_DemandElecPump_Selector_1", "HYD_DemandAirPump_Selector_0", "HYD_DemandAirPump_Selector_1" },
                (e, _) => e.SetDemandPumps(0)),
            Auto("PF_SEAT_BELTS", "PREFLIGHT", "Seat Belts selector: AUTO",
                "SIGNS_SeatBeltsSelector", v => v >= 1,
                action: (e, _) => e.SetSeatBelts(1)),
            Auto("PF_NO_SMOKING", "PREFLIGHT", "No Smoking selector: ON",
                "SIGNS_NoSmokingSelector", v => v > 1.5,
                action: (e, _) => e.SetNoSmoking(2)),
            // No indicator-lights-master state field — action-only.
            ActionManual("PF_MASTER_LIGHTS", "PREFLIGHT", "Indicator Lights master: ON",
                (e, _) => e.SetMasterLights(1)),
            Auto("PF_CARGO_FIRE", "PREFLIGHT", "Cargo Fire ARM switches: OFF",
                "FIRE_CargoFire_Sw_Arm_0", v => v < 0.5,
                new[] { "FIRE_CargoFire_Sw_Arm_1" },
                (e, _) => e.SetCargoFireArm(0)),
            Auto("PF_EEC_MODE", "PREFLIGHT", "EEC Mode switches: NORM",
                "ENG_EECMode_Sw_NORM_0", v => v > 0.5,
                new[] { "ENG_EECMode_Sw_NORM_1" },
                (e, _) => e.SetEECMode(1)),
            Auto("PF_AUTOSTART", "PREFLIGHT", "Autostart switch: ON",
                "ENG_Autostart_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetAutoStart(1)),
            ActionManual("PF_JETTISON_OFF", "PREFLIGHT", "Fuel jettison nozzles and arm: OFF",
                (e, _) => e.SetFuelJettisonOff()),
            ActionManual("PF_CROSSFEED_OFF", "PREFLIGHT", "Crossfeed switches: OFF",
                (e, _) => e.SetCrossfeeds(0)),
            Auto("PF_WING_ANTI_ICE", "PREFLIGHT", "Wing Anti-Ice: AUTO",
                "ICE_WingAntiIceSw", v => v > 0.5,
                action: (e, _) => e.SetWingAntiIce(1)),  // 1=Auto (panel ValueDescriptions: 0=Off,1=Auto,2=On)
            Auto("PF_ENG_ANTI_ICE", "PREFLIGHT", "Engine Anti-Ice selectors: AUTO",
                "ICE_EngAntiIceSw_0", v => v > 0.5,
                new[] { "ICE_EngAntiIceSw_1" },
                (e, _) => e.SetEngAntiIce(1)),            // 1=Auto
            Auto("PF_NAV_LIGHTS", "PREFLIGHT", "Navigation light: ON",
                "LTS_NAV_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetNavLights(1)),
            Auto("PF_BEACON_OFF", "PREFLIGHT", "Beacon light: OFF",
                "LTS_Beacon_Sw_ON", v => v < 0.5,
                action: (e, _) => e.SetBeacon(0)),
            Auto("PF_WING_LIGHTS", "PREFLIGHT", "Wing lights: ON",
                "LTS_Wing_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetWingLights(1)),
            Auto("PF_EQUIP_COOL", "PREFLIGHT", "Equipment cooling: AUTO",
                "AIR_EquipCooling_Sw_AUTO", v => v > 0.5,
                action: (e, _) => e.SetEquipCooling(1)),
            Auto("PF_GASPER", "PREFLIGHT", "Gasper: ON",
                "AIR_Gasper_Sw_On", v => v > 0.5,
                action: (e, _) => e.SetGasper(1)),
            Auto("PF_RECIRC_FANS", "PREFLIGHT", "Recirculation fans: ON",
                "AIR_RecircFan_Sw_On_0", v => v > 0.5,
                new[] { "AIR_RecircFan_Sw_On_1" },
                (e, _) => e.SetRecircFans(1)),
            Auto("PF_PACKS", "PREFLIGHT", "Pack switches: AUTO",
                "AIR_Pack_Sw_AUTO_0", v => v > 0.5,
                new[] { "AIR_Pack_Sw_AUTO_1" },
                (e, _) => e.SetPacks(1)),
            Auto("PF_TRIM_AIR", "PREFLIGHT", "Trim Air switches: ON",
                "AIR_TrimAir_Sw_On_0", v => v > 0.5,
                new[] { "AIR_TrimAir_Sw_On_1" },
                (e, _) => e.SetTrimAir(1)),
            Auto("PF_ENG_BLEED", "PREFLIGHT", "Engine Bleed switches: ON",
                "AIR_EngBleedAir_Sw_AUTO_0", v => v > 0.5,
                new[] { "AIR_EngBleedAir_Sw_AUTO_1" },
                (e, _) => e.SetEngBleeds(1)),
            Auto("PF_APU_BLEED", "PREFLIGHT", "APU Bleed switch: AUTO",
                "AIR_APUBleedAir_Sw_AUTO", v => v > 0.5,
                action: (e, _) => e.SetApuBleed(1)),
            Auto("PF_OUTFLOW_VALVES", "PREFLIGHT", "Outflow Valve switches: AUTO",
                "AIR_OutflowValve_Sw_AUTO_0", v => v > 0.5,
                new[] { "AIR_OutflowValve_Sw_AUTO_1" },
                (e, _) => e.SetOutflowValves(1)),
            ActionManual("PF_EFIS_SET", "PREFLIGHT", "EFIS: Mode MAP, range 40nm",
                (e, _) => { e.SetEFISModeCapt(2); e.SetEFISModeFO(2); e.SetEFISRangeCapt(2); e.SetEFISRangeFO(2); }),
            ActionManual("PF_FD_ON", "PREFLIGHT", "Flight Director switches: ON",
                (e, s) => { e.SetFDLeft(1, s); e.SetFDRight(1, s); }),
            Auto("PF_AUTOBRAKE_RTO", "PREFLIGHT", "Autobrake selector: RTO",
                "BRAKES_AutobrakeSelector", v => Math.Abs(v) < 0.1,
                action: (e, _) => e.SetAutobrake(0)),
            Auto("PF_FUEL_CONTROL", "PREFLIGHT", "Fuel Control switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                (e, _) => e.SetFuelControlLevers(0)),
            Auto("PF_FUEL_PUMPS_OFF", "PREFLIGHT", "Fuel Pump switches: OFF",
                "FUEL_PumpFwd_Sw_0", v => v < 0.5,
                new[] { "FUEL_PumpFwd_Sw_1", "FUEL_PumpAft_Sw_0", "FUEL_PumpAft_Sw_1" },
                (e, _) => e.SetWingFuelPumps(0)),
            Auto("PF_GEAR_DOWN", "PREFLIGHT", "Landing gear lever: DOWN",
                "GEAR_Lever", v => Math.Abs(v - 1) < 0.1,
                action: (e, _) => e.SetGearLever(1)),
            Manual("PF_CDU_PREFLIGHT", "PREFLIGHT", "CDU Preflight: Complete"),
            Manual("PF_FMC_PERF", "PREFLIGHT", "Performance data: Entered in FMC"),
            Reminder("PF_OXYGEN", "PREFLIGHT", "Oxygen: Tested 100%"),
            Manual("PF_BARO_SET", "PREFLIGHT", "Barometric reference: Set local setting"),
            Reminder("PF_RESET_CL", "PREFLIGHT", "Reset checklists and obtain IFR clearance"),
            Reminder("PF_ATIS", "PREFLIGHT", "Obtain ATIS"),
        }
    };

    // -----------------------------------------------------------------------
    // 3. Preflight Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildPreflightChecklist() => new()
    {
        Id = "PREFLIGHT_CL", Name = "Preflight Checklist",
        Items = new()
        {
            Reminder("PFCL_OXYGEN", "PREFLIGHT_CL", "Oxygen: Tested 100%"),
            Manual("PFCL_FLT_INST", "PREFLIGHT_CL", "Flight Instruments: Set heading and altimeter"),
            Auto("PFCL_PARK_BRAKE", "PREFLIGHT_CL", "Parking Brake: SET",
                "BRAKES_ParkingBrakeLeverOn", v => v > 0.5,
                action: null),
            Auto("PFCL_FUEL_CTRL", "PREFLIGHT_CL", "Fuel Control Switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                null),
        }
    };

    // -----------------------------------------------------------------------
    // 4. Before Start
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            Manual("BS_CDU_COMPLETE", "BEFORE_START", "CDU Preflight: Verify complete"),
            Manual("BS_V2_SET", "BEFORE_START", "IAS/MACH selector: Set V2"),
            Manual("BS_VNAV_ARM", "BEFORE_START", "VNAV: ARM"),
            Manual("BS_LNAV_SET", "BEFORE_START", "LNAV: Arm as needed"),
            Manual("BS_INIT_HDG", "BEFORE_START", "Initial heading or track: Set"),
            Manual("BS_INIT_ALT", "BEFORE_START", "Initial altitude: Set"),
            // Trim follows the MCP setup here — the flow briefs MCP → LNAV/VNAV → trim,
            // and the trim wheel is easier to reach before the start clutter begins.
            Manual("BS_STAB_TRIM", "BEFORE_START", "Stabilizer trim: Set for takeoff, verify green band"),
            Manual("BS_AIL_TRIM", "BEFORE_START", "Aileron trim: 0 degrees"),
            Manual("BS_RUD_TRIM", "BEFORE_START", "Rudder trim: 0 degrees"),
            Reminder("BS_DOORS_VERIFY", "BEFORE_START", "Exterior doors: Verify closed"),
            Reminder("BS_WINDOWS", "BEFORE_START", "Flight deck windows: Closed and locked"),
            Auto("BS_SEAT_BELTS", "BEFORE_START", "Seat Belts selector: AUTO",
                "SIGNS_SeatBeltsSelector", v => v >= 1,
                action: (e, _) => e.SetSeatBelts(1)),
            ActionManualAsync("BS_APU_START", "BEFORE_START", "APU: START (ON then START; wait for self-sustaining)",
                (e, _) => e.StartApuAsync()),
            // GPU buttons are momentary TOGGLES — disconnect only the side that is
            // actually on, or ticking with no GPU connected would CONNECT one.
            Auto("BS_EXT_PWR_OFF", "BEFORE_START", "External power: Disconnect when APU available",
                "FO_ANY_GPU_ON", v => v < 0.5,
                action: (e, s) =>
                {
                    if (s.IsGpuPower1On()) e.PushGroundPowerPrimary();
                    if (s.IsGpuPower2On()) e.PushGroundPowerSecondary();
                }),
            Manual("BS_HYD_PRESSURIZE", "BEFORE_START", "Obtain clearance to pressurize hydraulics"),
            Auto("BS_HYD_PUMPS_ON", "BEFORE_START", "Engine and Electric primary hydraulic pumps: ON",
                "HYD_PrimaryEngPump_Sw_ON_0", v => v > 0.5,
                new[] { "HYD_PrimaryEngPump_Sw_ON_1", "HYD_PrimaryElecPump_Sw_ON_0", "HYD_PrimaryElecPump_Sw_ON_1" },
                (e, _) => { e.SetEngPumps(1); e.SetElecPumps(1); }),
            Auto("BS_HYD_DEMAND", "BEFORE_START", "Demand pump selectors: AUTO",
                "HYD_DemandElecPump_Selector_0", v => v > 0.5 && v < 1.5,
                new[] { "HYD_DemandElecPump_Selector_1", "HYD_DemandAirPump_Selector_0", "HYD_DemandAirPump_Selector_1" },
                (e, _) => e.SetDemandPumps(1)),  // 1=Auto (0=Off,1=Auto,2=On)
            Auto("BS_CTR_PUMPS_ON", "BEFORE_START", "Center Electric Primary pump switches: ON (check quantity)",
                "FUEL_PumpCtr_Sw_0", v => v > 0.5,
                new[] { "FUEL_PumpCtr_Sw_1" },
                (e, _) => e.SetCenterFuelPumps(1)),
            Auto("BS_WING_PUMPS_ON", "BEFORE_START", "Left and Right Fuel Pump switches: ON",
                "FUEL_PumpFwd_Sw_0", v => v > 0.5,
                new[] { "FUEL_PumpFwd_Sw_1", "FUEL_PumpAft_Sw_0", "FUEL_PumpAft_Sw_1" },
                (e, _) => e.SetWingFuelPumps(1)),
            Auto("BS_BEACON_ON", "BEFORE_START", "Beacon light: ON",
                "LTS_Beacon_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetBeacon(1)),
            ActionManual("BS_CANCEL_RECALL", "BEFORE_START", "Cancel/Recall: PUSH, verify expected alerts",
                (e, _) => e.PushCancelRecall()),
            Auto("BS_TRANSPONDER", "BEFORE_START", "Transponder: XPNDR",
                "XPDR_ModeSel", v => v > 1.5 && v < 2.5,
                action: (e, _) => e.SetTransponderMode(2)),
            Reminder("BS_ACARS", "BEFORE_START", "Start ACARS"),
            Reminder("BS_TAXI_CLR", "BEFORE_START", "Obtain pushback and start clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 5. Before Start Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildBeforeStartChecklist() => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = new()
        {
            Reminder("BSCL_DOOR", "BEFORE_START_CL", "Flight Deck Door: Closed and locked"),
            Auto("BSCL_SIGNS", "BEFORE_START_CL", "Passenger Signs: Set",
                "SIGNS_SeatBeltsSelector", v => v >= 1,
                action: null),
            Manual("BSCL_MCP", "BEFORE_START_CL", "MCP: Set speed, heading and altitude"),
            Manual("BSCL_V_SPEEDS", "BEFORE_START_CL", "Takeoff Speeds: Set V1, VR and V2 from CDU"),
            Manual("BSCL_CDU_COMPLETE", "BEFORE_START_CL", "CDU Preflight: Complete"),
            Manual("BSCL_TRIM", "BEFORE_START_CL", "Trim: Elevator set, aileron 0, rudder 0"),
            Reminder("BSCL_BRIEFING", "BEFORE_START_CL", "Taxi and Takeoff Briefing: Complete"),
            Auto("BSCL_BEACON", "BEFORE_START_CL", "Beacon: ON",
                "LTS_Beacon_Sw_ON", v => v > 0.5,
                action: null),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Engine Start
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            // Start selectors spring back to NORM once the start completes, so these are
            // action-only (an auto-detect on the transient START position would untick).
            ActionManual("ES_ENG2_START_SEL", "ENGINE_START", "Engine 2 Start/Ignition selector: START",
                (e, _) => e.SetEngStartSelector2(0)),  // 0=START/GND
            Auto("ES_ENG2_FUEL_CTRL", "ENGINE_START", "Engine 2 Fuel Control: RUN",
                "ENG_FuelControl_Sw_RUN_1", v => v > 0.5,
                action: (e, _) => e.SetFuelControl2(1)),  // logical 1=RUN
            Manual("ES_ENG2_OIL_PRESS", "ENGINE_START", "Engine 2 Oil Pressure: Verify increases"),
            ActionManual("ES_ENG1_START_SEL", "ENGINE_START", "Engine 1 Start/Ignition selector: START",
                (e, _) => e.SetEngStartSelector1(0)),  // 0=START/GND
            Auto("ES_ENG1_FUEL_CTRL", "ENGINE_START", "Engine 1 Fuel Control: RUN",
                "ENG_FuelControl_Sw_RUN_0", v => v > 0.5,
                action: (e, _) => e.SetFuelControl1(1)),  // logical 1=RUN
            Manual("ES_ENG1_OIL_PRESS", "ENGINE_START", "Engine 1 Oil Pressure: Verify increases"),
        }
    };

    // -----------------------------------------------------------------------
    // 7. Before Taxi (mirrors the Before Taxi flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildBeforeTaxi() => new()
    {
        Id = "BEFORE_TAXI", Name = "Before Taxi",
        Items = new()
        {
            // ELEC_APU_Selector reads 0 only when OFF (springs to ON=1 while running).
            Auto("BT_APU_OFF", "BEFORE_TAXI", "APU selector: OFF (if no longer needed)",
                "ELEC_APU_Selector", v => v < 0.5,
                action: (e, _) => e.SetApuSelector(0)),
            Auto("BT_TAXI_LIGHTS", "BEFORE_TAXI", "Taxi lights: ON",
                "LTS_Taxi_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetTaxiLights(1)),
            Auto("BT_STORM_OFF", "BEFORE_TAXI", "Storm lights: OFF",
                "LTS_Storm_Sw_ON", v => v < 0.5,
                action: (e, _) => e.SetStormLights(0)),
            Manual("BT_ANTI_ICE", "BEFORE_TAXI", "Engine Anti-Ice selectors: As needed"),
            // Sets the SimBrief-planned takeoff flaps (defaults to flaps 5 with no OFP) —
            // the same source the Before Taxi flow uses.
            ActionManual("BT_FLAPS", "BEFORE_TAXI", "Flap lever: Set for takeoff (from SimBrief perf data)",
                (e, s) => e.SetFlapsPosition(AircraftActionExecutor.FlapDegreesToPosition(s.GetTakeoffFlaps()))),
            Auto("BT_PACKS", "BEFORE_TAXI", "Packs: AUTO",
                "AIR_Pack_Sw_AUTO_0", v => v > 0.5,
                new[] { "AIR_Pack_Sw_AUTO_1" },
                (e, _) => e.SetPacks(1)),
            Manual("BT_FCTL_CHECK", "BEFORE_TAXI", "Flight controls: CHECK"),
            Reminder("BT_SET_TRIM", "BEFORE_TAXI", "Set stabiliser trim for takeoff"),
            ActionManual("BT_RECALL", "BEFORE_TAXI", "Recall: Check",
                (e, _) => e.PushCancelRecall()),
        }
    };

    // -----------------------------------------------------------------------
    // 8. Before Taxi Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildBeforeTaxiChecklist() => new()
    {
        Id = "BEFORE_TAXI_CL", Name = "Before Taxi Checklist",
        Items = new()
        {
            Manual("BTCL_ANTI_ICE", "BEFORE_TAXI_CL", "Anti-Ice: As required"),
            Manual("BTCL_RECALL", "BEFORE_TAXI_CL", "Recall: Checked"),
            Auto("BTCL_AUTOBRAKE", "BEFORE_TAXI_CL", "Auto Brake: RTO",
                "BRAKES_AutobrakeSelector", v => Math.Abs(v) < 0.1,
                action: null),
            Manual("BTCL_FCTL", "BEFORE_TAXI_CL", "Flight Controls: Checked"),
            Reminder("BTCL_GND_EQUIP", "BEFORE_TAXI_CL", "Ground Equipment: Clear"),
        }
    };

    // -----------------------------------------------------------------------
    // 9. Before Takeoff (mirrors the Before Takeoff flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Items = new()
        {
            Auto("BTKO_LANDING", "BEFORE_TAKEOFF", "Landing lights: ON",
                "LTS_LandingLights_Sw_ON_0", v => v > 0.5,
                new[] { "LTS_LandingLights_Sw_ON_1", "LTS_LandingLights_Sw_ON_2" },
                (e, _) => e.SetLandingLights(1)),
            Auto("BTKO_TURNOFF", "BEFORE_TAKEOFF", "Runway turnoff lights: ON",
                "LTS_RunwayTurnoff_Sw_ON_0", v => v > 0.5,
                new[] { "LTS_RunwayTurnoff_Sw_ON_1" },
                (e, _) => e.SetRunwayTurnoff(1)),
            Auto("BTKO_STROBE", "BEFORE_TAKEOFF", "Strobe lights: ON",
                "LTS_Strobe_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetStrobeLights(1)),
            Auto("BTKO_XPNDR", "BEFORE_TAKEOFF", "Transponder: TA/RA",
                "XPDR_ModeSel", v => v > 3.5,
                action: (e, _) => e.SetTransponderMode(4)),
            // LNAV/VNAV pushes are TOGGLES — press only when the annunciator shows unarmed,
            // or a tick on an already-armed mode would disarm it (the FD-switch trap).
            Auto("BTKO_LNAV", "BEFORE_TAKEOFF", "LNAV: ARM",
                "MCP_annunLNAV", v => v > 0.5,
                action: (e, s) => { if (!s.IsOn("MCP_annunLNAV")) e.PushLNAV(); }),
            Auto("BTKO_VNAV", "BEFORE_TAKEOFF", "VNAV: ARM",
                "MCP_annunVNAV", v => v > 0.5,
                action: (e, s) => { if (!s.IsOn("MCP_annunVNAV")) e.PushVNAV(); }),
        }
    };

    // -----------------------------------------------------------------------
    // 10. Before Takeoff Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildBeforeTakeoffChecklist() => new()
    {
        Id = "BEFORE_TKOF_CL", Name = "Before Takeoff Checklist",
        Items = new()
        {
            // Flap target depends on SimBrief perf data — no fixed action value
            Auto("BTKOF_FLAPS", "BEFORE_TKOF_CL", "Flaps: Set for takeoff",
                "FCTL_Flaps_Lever", v => v >= 1 && v <= 3),
            Manual("BTKOF_V_SPEEDS", "BEFORE_TKOF_CL", "V speeds: Checked"),
            Manual("BTKOF_ALT", "BEFORE_TKOF_CL", "Altitude: Set initial climb"),
        }
    };

    // -----------------------------------------------------------------------
    // 11. After Takeoff (mirrors the After Takeoff flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = new()
        {
            Auto("ATKO_TURNOFF_OFF", "AFTER_TAKEOFF", "Runway turnoff lights: OFF",
                "LTS_RunwayTurnoff_Sw_ON_0", v => v < 0.5,
                new[] { "LTS_RunwayTurnoff_Sw_ON_1" },
                (e, _) => e.SetRunwayTurnoff(0)),
            Auto("ATKO_LANDING_OFF", "AFTER_TAKEOFF", "Landing lights: OFF",
                "LTS_LandingLights_Sw_ON_0", v => v < 0.5,
                new[] { "LTS_LandingLights_Sw_ON_1", "LTS_LandingLights_Sw_ON_2" },
                (e, _) => e.SetLandingLights(0)),
            Auto("ATKO_GEAR_UP", "AFTER_TAKEOFF", "Gear: UP",
                "GEAR_Lever", v => v < 0.5,
                action: (e, _) => e.SetGearLever(0)),
            Auto("ATKO_FLAPS_UP", "AFTER_TAKEOFF", "Flaps: UP",
                "FCTL_Flaps_Lever", v => v < 0.5,
                action: (e, _) => e.SetFlapsPosition(0)),
        }
    };

    // -----------------------------------------------------------------------
    // 12. After Takeoff Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildAfterTakeoffChecklist() => new()
    {
        Id = "AFTER_TKOF_CL", Name = "After Takeoff Checklist",
        Items = new()
        {
            Auto("ATKOF_GEAR", "AFTER_TKOF_CL", "Landing Gear: UP",
                "GEAR_Lever", v => v < 0.5,
                action: null),
            Auto("ATKOF_FLAPS", "AFTER_TKOF_CL", "Flaps: UP",
                "FCTL_Flaps_Lever", v => v < 0.5,
                action: null),
        }
    };

    // -----------------------------------------------------------------------
    // 13. Descent (mirrors the Descent Setup flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Items = new()
        {
            Reminder("DSCA_LNDG_DATA", "DESCENT", "Set landing data in FMC — VREF and minimums"),
            Reminder("DSCA_AUTOBRAKE", "DESCENT", "Set the landing autobrake — Forward Panel, Brakes, Autobrake Selector"),
            Auto("DSCA_EFIS_FO_MODE", "DESCENT", "FO EFIS: APP mode",
                "EFIS_ModeSel_1", v => v < 0.5,
                action: (e, _) => e.SetEFISModeFO(0)),
            Auto("DSCA_EFIS_FO_RANGE", "DESCENT", "FO EFIS: 20nm range",
                "EFIS_RangeSel_1", v => v > 0.5 && v < 1.5,
                action: (e, _) => e.SetEFISRangeFO(1)),
            ActionManual("DSCA_RECALL", "DESCENT", "Recall: Check no unexpected messages",
                (e, _) => e.PushCancelRecall()),
        }
    };

    // -----------------------------------------------------------------------
    // 14. Approach (mirrors the Approach Setup flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = new()
        {
            Reminder("APPA_ALTIMETERS", "APPROACH", "Altimeters: Set local QNH / transition"),
            Auto("APPA_SPEEDBRAKE", "APPROACH", "Speedbrake: ARM",
                "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5,
                action: (e, _) => e.SetSpeedbrakeArmed()),
        }
    };

    // -----------------------------------------------------------------------
    // 15. Descent Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildDescentChecklist() => new()
    {
        Id = "DESCENT_CL", Name = "Descent Checklist",
        Items = new()
        {
            Manual("DSC_RECALL", "DESCENT_CL", "Recall: Checked"),
            Manual("DSC_AUTOBRAKE", "DESCENT_CL", "Autobrake: As required"),
            Manual("DSC_LANDING_DATA", "DESCENT_CL", "Landing Data: Set VREF and minimums"),
        }
    };

    // -----------------------------------------------------------------------
    // 16. Approach Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildApproachChecklist() => new()
    {
        Id = "APPROACH_CL", Name = "Approach Checklist",
        Items = new()
        {
            Manual("APP_ALTIMETERS", "APPROACH_CL", "Altimeters: SET"),
        }
    };

    // -----------------------------------------------------------------------
    // 17. Landing Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildLandingChecklist() => new()
    {
        Id = "LANDING_CL", Name = "Landing Checklist",
        Items = new()
        {
            Auto("LDG_SPEEDBRAKE", "LANDING_CL", "Speedbrake: ARMED",
                "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5,
                action: null),
            Auto("LDG_GEAR", "LANDING_CL", "Landing Gear: DOWN",
                "GEAR_Lever", v => Math.Abs(v - 1) < 0.1,
                action: null),
            // Landing flap target depends on approach — no fixed action value
            Auto("LDG_FLAPS", "LANDING_CL", "Flaps: Set for landing",
                "FCTL_Flaps_Lever", v => v >= 4),
        }
    };

    // -----------------------------------------------------------------------
    // 18. After Landing (mirrors the After Landing flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_SPEEDBRAKE", "AFTER_LANDING", "Speed Brake lever: DOWN",
                "FCTL_Speedbrake_Lever", v => v < 0.5,
                action: (e, _) => e.SetSpeedbrakeDown()),
            Auto("AL_EXT_LIGHTS", "AFTER_LANDING", "Landing and turnoff lights: OFF",
                "LTS_LandingLights_Sw_ON_0", v => v < 0.5,
                new[] { "LTS_LandingLights_Sw_ON_1", "LTS_LandingLights_Sw_ON_2", "LTS_RunwayTurnoff_Sw_ON_0", "LTS_RunwayTurnoff_Sw_ON_1" },
                (e, _) => { e.SetLandingLights(0); e.SetRunwayTurnoff(0); }),
            Auto("AL_STROBE_OFF", "AFTER_LANDING", "Strobe lights: OFF",
                "LTS_Strobe_Sw_ON", v => v < 0.5,
                action: (e, _) => e.SetStrobeLights(0)),
            Auto("AL_AUTOBRAKE_OFF", "AFTER_LANDING", "Auto Brake: OFF",
                "BRAKES_AutobrakeSelector", v => Math.Abs(v - 1) < 0.1,
                action: (e, _) => e.SetAutobrake(1)),  // 1=Off
            Auto("AL_FLAPS_UP", "AFTER_LANDING", "Flap lever: UP",
                "FCTL_Flaps_Lever", v => v < 0.5,
                action: (e, _) => e.SetFlapsPosition(0)),
            // Transponder → STBY moved to the Shutdown group (SD_XPNDR_STBY); after landing
            // it must stay active so ground/tower still see the aircraft.
            ActionManualAsync("AL_APU", "AFTER_LANDING", "APU: START (for gate power)",
                (e, _) => e.StartApuAsync()),
            Manual("AL_WX_RADAR_OFF", "AFTER_LANDING", "WX Radar: OFF"),
        }
    };

    // -----------------------------------------------------------------------
    // 19. Shutdown (mirrors the Shutdown flow)
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            Auto("SD_STORM_ON", "SHUTDOWN", "Storm lights: ON",
                "LTS_Storm_Sw_ON", v => v > 0.5,
                action: (e, _) => e.SetStormLights(1)),
            Auto("SD_PARK_BRAKE", "SHUTDOWN", "Parking Brake/Chocks: SET",
                "BRAKES_ParkingBrakeLeverOn", v => v > 0.5,
                action: (e, _) => e.SetParkingBrake(1)),
            Manual("SD_APU", "SHUTDOWN", "APU: START (if needed for power)"),
            Auto("SD_FUEL_CTRL", "SHUTDOWN", "Fuel Control switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                (e, _) => e.SetFuelControlLevers(0)),
            Auto("SD_SEAT_BELTS_OFF", "SHUTDOWN", "Fasten Belts switch: OFF",
                "SIGNS_SeatBeltsSelector", v => v < 0.5,
                action: (e, _) => e.SetSeatBelts(0)),
            Auto("SD_ENG_PUMPS_OFF", "SHUTDOWN", "Engine primary pump switches: OFF",
                "HYD_PrimaryEngPump_Sw_ON_0", v => v < 0.5,
                new[] { "HYD_PrimaryEngPump_Sw_ON_1" },
                (e, _) => e.SetEngPumps(0)),
            Auto("SD_ELEC_PUMPS_OFF", "SHUTDOWN", "Electric primary pump switches: OFF",
                "HYD_PrimaryElecPump_Sw_ON_0", v => v < 0.5,
                new[] { "HYD_PrimaryElecPump_Sw_ON_1" },
                (e, _) => e.SetElecPumps(0)),
            Auto("SD_DEMAND_OFF", "SHUTDOWN", "Demand pump selectors: OFF",
                "HYD_DemandElecPump_Selector_0", v => v < 0.5,
                new[] { "HYD_DemandElecPump_Selector_1", "HYD_DemandAirPump_Selector_0", "HYD_DemandAirPump_Selector_1" },
                (e, _) => e.SetDemandPumps(0)),
            Auto("SD_FUEL_PUMPS", "SHUTDOWN", "Fuel Pump switches: OFF",
                "FUEL_PumpFwd_Sw_0", v => v < 0.5,
                new[] { "FUEL_PumpFwd_Sw_1", "FUEL_PumpAft_Sw_0", "FUEL_PumpAft_Sw_1", "FUEL_PumpCtr_Sw_0", "FUEL_PumpCtr_Sw_1" },
                (e, _) => { e.SetWingFuelPumps(0); e.SetCenterFuelPumps(0); }),
            Auto("SD_BEACON_OFF", "SHUTDOWN", "Beacon light: OFF",
                "LTS_Beacon_Sw_ON", v => v < 0.5,
                action: (e, _) => e.SetBeacon(0)),
            Auto("SD_TAXI_OFF", "SHUTDOWN", "Taxi lights: OFF",
                "LTS_Taxi_Sw_ON", v => v < 0.5,
                action: (e, _) => e.SetTaxiLights(0)),
            Auto("SD_FD_OFF", "SHUTDOWN", "Flight Director switches: OFF",
                "MCP_FD_Sw_On_0", v => v < 0.5,
                new[] { "MCP_FD_Sw_On_1" },
                (e, s) => { e.SetFDLeft(0, s); e.SetFDRight(0, s); }),
            Auto("SD_XPNDR_STBY", "SHUTDOWN", "Transponder: STBY",
                "XPDR_ModeSel", v => v < 0.5,
                action: (e, _) => e.SetTransponderMode(0)),
        }
    };

    // -----------------------------------------------------------------------
    // 20. Shutdown Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildShutdownChecklist() => new()
    {
        Id = "SHUTDOWN_CL", Name = "Shutdown Checklist",
        Items = new()
        {
            Manual("SDCL_HYD", "SHUTDOWN_CL", "Hydraulic panel: SET"),
            Auto("SDCL_FUEL_PUMPS", "SHUTDOWN_CL", "Fuel Pumps: OFF",
                "FUEL_PumpFwd_Sw_0", v => v < 0.5,
                action: null),
            Auto("SDCL_FLAPS_UP", "SHUTDOWN_CL", "Flaps: UP",
                "FCTL_Flaps_Lever", v => v < 0.5,
                action: null),
            Auto("SDCL_PARK_BRAKE", "SHUTDOWN_CL", "Parking Brake: SET",
                "BRAKES_ParkingBrakeLeverOn", v => v > 0.5,
                action: null),
            Auto("SDCL_FUEL_CTRL", "SHUTDOWN_CL", "Fuel Control Switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                null),
            Manual("SDCL_WX_RADAR", "SHUTDOWN_CL", "Weather Radar: OFF"),
        }
    };

    // -----------------------------------------------------------------------
    // 21. Secure
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildSecure() => new()
    {
        Id = "SECURE", Name = "Secure",
        Items = new()
        {
            Auto("SEC_ADIRU", "SECURE", "ADIRU switch: OFF",
                "ADIRU_Sw_On", v => v < 0.5,
                action: (e, _) => e.SetAdiru(0)),
            ActionManual("SEC_EMER_EXIT_OFF", "SECURE", "Emergency Exit Lights switch: OFF",
                (e, _) => { e.CloseEmerExitLightGuard(); e.SetEmerExitLights(0); }),
            Auto("SEC_PACKS_OFF", "SECURE", "Pack switches: OFF",
                "AIR_Pack_Sw_AUTO_0", v => v < 0.5,
                new[] { "AIR_Pack_Sw_AUTO_1" },
                (e, _) => e.SetPacks(0)),
            // GPU buttons are momentary TOGGLES — press only the side that is actually on,
            // or a tick with no GPU connected would CONNECT one. Guarded per side (mirrors
            // the ELEC_POWER_DOWN disconnect); a single confirm line for the Secure flow.
            ActionManual("SEC_GND_PWR_OFF", "SECURE", "Ground power: OFF",
                (e, s) =>
                {
                    if (s.IsGpuPower1On()) e.PushGroundPowerPrimary();
                    if (s.IsGpuPower2On()) e.PushGroundPowerSecondary();
                }),
        }
    };

    // -----------------------------------------------------------------------
    // 22. Secure Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildSecureChecklist() => new()
    {
        Id = "SECURE_CL", Name = "Secure Checklist",
        Items = new()
        {
            Auto("SECCL_ADIRU", "SECURE_CL", "ADIRU: OFF",
                "ADIRU_Sw_On", v => v < 0.5,
                action: null),
            Manual("SECCL_EMER_LIGHTS", "SECURE_CL", "Emergency Lights: OFF"),
            Auto("SECCL_PACKS", "SECURE_CL", "Packs: OFF",
                "AIR_Pack_Sw_AUTO_0", v => v < 0.5,
                new[] { "AIR_Pack_Sw_AUTO_1" },
                null),
        }
    };

    // -----------------------------------------------------------------------
    // 23. Electrical Power Down
    // -----------------------------------------------------------------------
    private static ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator> BuildElectricalPowerDown() => new()
    {
        Id = "ELEC_POWER_DOWN", Name = "Electrical Power Down",
        Items = new()
        {
            // GPU buttons are toggles — disconnect only sides that are actually on.
            ActionManualAsync("EPD_APU_GND_OFF", "ELEC_POWER_DOWN", "APU or Ground Power switches: OFF",
                (e, s) =>
                {
                    e.SetApuSelector(0);
                    if (s.IsGpuPower1On()) e.PushGroundPowerPrimary();
                    if (s.IsGpuPower2On()) e.PushGroundPowerSecondary();
                    return Task.CompletedTask;
                }),
            Auto("EPD_BATTERY_OFF", "ELEC_POWER_DOWN", "Battery switch: OFF",
                "ELEC_Battery_Sw_ON", v => v < 0.5,
                action: (e, _) => e.SetBattery(0)),
        }
    };

    // -----------------------------------------------------------------------
    // Builder helpers — every auto-detect item is RevertToState (see class doc)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Adapt a synchronous switch action (the original <c>Action</c> form used by every
    /// call site above) to the model's async <c>Func&lt;...,Task&gt;</c> CheckAction.
    /// The body runs synchronously to completion and the wrapper returns a completed Task.
    /// </summary>
    private static Func<AircraftActionExecutor, AircraftStateEvaluator, Task>? AsCheckAction(
        Action<AircraftActionExecutor, AircraftStateEvaluator>? action)
        => action == null ? null : (e, s) => { action(e, s); return Task.CompletedTask; };

    /// <summary>AutoDetectable item — state is read from sim vars; optional CheckAction fires on manual check.</summary>
    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition,
        string[]? additionalFields = null,
        Action<AircraftActionExecutor, AircraftStateEvaluator>? action = null) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = RevertBehavior.RevertToState,
        AdditionalStateFields = additionalFields ?? Array.Empty<string>(),
        AdditionalStateCondition = condition,
        CheckAction = AsCheckAction(action),
    };

    /// <summary>Overload without additionalFields — cleaner call sites when only an action is needed.</summary>
    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition,
        Action<AircraftActionExecutor, AircraftStateEvaluator>? action) =>
        Auto(id, groupId, label, field, condition, null, action);

    /// <summary>Manual item with no linked sim action — user ticks after doing it themselves.</summary>
    private static Item Manual(string id, string groupId, string label) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
    };

    /// <summary>Manual item WITH a linked sim action — checking the box fires the action.</summary>
    private static Item ActionManual(string id, string groupId, string label,
        Action<AircraftActionExecutor, AircraftStateEvaluator> action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = AsCheckAction(action),
    };

    /// <summary>Manual item whose linked action is async (time-spaced CDA writes).</summary>
    private static Item ActionManualAsync(string id, string groupId, string label,
        Func<AircraftActionExecutor, AircraftStateEvaluator, Task> action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };

    /// <summary>Captain reminder — user reads/confirms and manually ticks. No sim action.</summary>
    private static Item Reminder(string id, string groupId, string text) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
    };
}
