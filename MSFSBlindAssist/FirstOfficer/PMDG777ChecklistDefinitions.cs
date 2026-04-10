using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Data-driven PMDG 777 checklist definitions.
///
/// Design rules:
/// - AutoDetectable items with a clear binary sim action carry a CheckAction lambda.
///   Checking the box executes the action; auto-detection then confirms state on next poll.
/// - Manual items with an unambiguous single-call action also carry CheckAction.
/// - Verify/brief items (no sim action, or context-dependent) leave CheckAction null.
/// - Items where the target value depends on runtime data (flap setting, autobrake level
///   during approach) leave CheckAction null — user must set manually then check.
/// </summary>
public static class PMDG777ChecklistDefinitions
{
    public static List<ChecklistGroup> Build() => new()
    {
        BuildElectricalPowerUp(),
        BuildPreflight(),
        BuildPreflightChecklist(),
        BuildBeforeStart(),
        BuildBeforeStartChecklist(),
        BuildEngineStart(),
        BuildBeforeTaxi(),
        BuildBeforeTaxiChecklist(),
        BuildBeforeTakeoffChecklist(),
        BuildAfterTakeoffChecklist(),
        BuildDescentChecklist(),
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
    // 1. Electrical Power Up
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildElectricalPowerUp() => new()
    {
        Id = "ELEC_POWER_UP", Name = "Electrical Power Up",
        Items = new()
        {
            Auto("EPU_BATTERY", "ELEC_POWER_UP", "Battery: ON",
                "ELEC_Battery_Sw_ON", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetBattery(1)),
            ActionManual("EPU_GEAR_DOWN", "ELEC_POWER_UP", "Landing gear lever: DOWN",
                (e, _) => e.SetGearLever(1)),
            Auto("EPU_BUS_TIES", "ELEC_POWER_UP", "Bus Tie switches: AUTO",
                "ELEC_BusTie_Sw_AUTO_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "ELEC_BusTie_Sw_AUTO_1" },
                (e, _) => e.SetBusTies(1)),
            ActionManual("EPU_GND_PWR", "ELEC_POWER_UP", "External power: PUSH (if available)",
                (e, _) => { e.PushGroundPowerPrimary(); e.PushGroundPowerSecondary(); }),
            ActionManual("EPU_ELEC_PUMPS_OFF", "ELEC_POWER_UP", "Electric primary pump switches: OFF",
                (e, _) => e.SetElecPumps(0)),
            ActionManual("EPU_DEMAND_PUMPS_OFF", "ELEC_POWER_UP", "Demand pump selectors: OFF",
                (e, _) => e.SetDemandPumps(0)),
            ActionManual("EPU_WIPERS_OFF", "ELEC_POWER_UP", "Wiper selectors: OFF",
                (e, _) => e.SetWipers(0)),
            ActionManual("EPU_ALT_FLAPS_OFF", "ELEC_POWER_UP", "Alternate Flaps selector: OFF",
                (e, _) => e.SetAltFlaps(0)),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Preflight
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = new()
        {
            Auto("PF_ADIRU", "PREFLIGHT", "ADIRU switch: ON",
                "ADIRU_Sw_On", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetAdiru(1)),
            Auto("PF_BATTERY", "PREFLIGHT", "Battery switch: ON",
                "ELEC_Battery_Sw_ON", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetBattery(1)),
            Auto("PF_IFE", "PREFLIGHT", "IFE/Pass Seats power: ON",
                "ELEC_IFEPassSeatsSw", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetIFEPassSeats(1)),
            ActionManual("PF_CABIN_UTIL", "PREFLIGHT", "Cabin/Utility power: ON",
                (e, _) => e.SetCabinUtility(1)),
            Auto("PF_APU_GEN", "PREFLIGHT", "APU Generator: ON",
                "ELEC_APUGen_Sw_ON", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetApuGenerator(1)),
            Auto("PF_BUS_TIES", "PREFLIGHT", "Bus Tie switches: AUTO",
                "ELEC_BusTie_Sw_AUTO_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "ELEC_BusTie_Sw_AUTO_1" },
                (e, _) => e.SetBusTies(1)),
            ActionManual("PF_GENERATORS", "PREFLIGHT", "Generator Control switches: ON",
                (e, _) => e.SetGenerators(1)),
            ActionManual("PF_BACKUP_GENS", "PREFLIGHT", "Backup Generator switches: ON",
                (e, _) => e.SetBackupGenerators(1)),
            Auto("PF_WINDOW_HEAT", "PREFLIGHT", "Window Heat switches: ON",
                "ICE_WindowHeat_Sw_ON_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "ICE_WindowHeat_Sw_ON_1", "ICE_WindowHeat_Sw_ON_2", "ICE_WindowHeat_Sw_ON_3" },
                (e, _) => e.SetWindowHeat(1)),
            ActionManual("PF_ENG_PUMPS", "PREFLIGHT", "Engine Primary pump switches: ON",
                (e, _) => e.SetEngPumps(1)),
            ActionManual("PF_CTR_PUMPS_OFF", "PREFLIGHT", "Center Electric pump switches: OFF",
                (e, _) => e.SetCenterFuelPumps(0)),
            ActionManual("PF_DEMAND_PUMPS_OFF", "PREFLIGHT", "Demand pump selectors: OFF",
                (e, _) => e.SetDemandPumps(0)),
            Auto("PF_PACKS", "PREFLIGHT", "Pack switches: AUTO",
                "AIR_Pack_Sw_AUTO_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "AIR_Pack_Sw_AUTO_1" },
                (e, _) => e.SetPacks(1)),
            Auto("PF_TRIM_AIR", "PREFLIGHT", "Trim Air switches: ON",
                "AIR_TrimAir_Sw_On_0", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetTrimAir(1)),
            Auto("PF_ENG_BLEED", "PREFLIGHT", "Engine Bleed switches: ON",
                "AIR_EngBleedAir_Sw_AUTO_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "AIR_EngBleedAir_Sw_AUTO_1" },
                (e, _) => e.SetEngBleeds(1)),
            Auto("PF_APU_BLEED", "PREFLIGHT", "APU Bleed switch: AUTO",
                "AIR_APUBleedAir_Sw_AUTO", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetApuBleed(1)),
            Auto("PF_OUTFLOW_VALVES", "PREFLIGHT", "Outflow Valve switches: AUTO",
                "AIR_OutflowValve_Sw_AUTO_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "AIR_OutflowValve_Sw_AUTO_1" },
                (e, _) => e.SetOutflowValves(1)),
            Auto("PF_EEC_MODE", "PREFLIGHT", "EEC Mode switches: NORM",
                "ENG_EECMode_Sw_NORM_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "ENG_EECMode_Sw_NORM_1" },
                (e, _) => e.SetEECMode(1)),
            Auto("PF_AUTOSTART", "PREFLIGHT", "Autostart switch: ON",
                "ENG_Autostart_Sw_ON", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetAutoStart(1)),
            Auto("PF_FUEL_CONTROL", "PREFLIGHT", "Fuel Control switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                (e, _) => e.SetFuelControlLevers(0)),
            ActionManual("PF_FUEL_PUMPS_OFF", "PREFLIGHT", "Fuel Pump switches: OFF",
                (e, _) => e.SetWingFuelPumps(0)),
            ActionManual("PF_CARGO_FIRE", "PREFLIGHT", "Cargo Fire ARM switches: OFF",
                (e, _) => e.SetCargoFireArm(0)),
            Auto("PF_WING_ANTI_ICE", "PREFLIGHT", "Wing Anti-Ice: AUTO",
                "ICE_WingAntiIceSw", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetWingAntiIce(0)),  // 0=AUTO on PMDG
            Auto("PF_ENG_ANTI_ICE", "PREFLIGHT", "Engine Anti-Ice selectors: AUTO",
                "ICE_EngAntiIceSw_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "ICE_EngAntiIceSw_1" },
                (e, _) => e.SetEngAntiIce(0)),            // 0=AUTO on PMDG
            Auto("PF_NAV_LIGHTS", "PREFLIGHT", "Navigation light: ON",
                "LTS_NAV_Sw_ON", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetNavLights(1)),
            Auto("PF_BEACON_OFF", "PREFLIGHT", "Beacon light: OFF",
                "LTS_Beacon_Sw_ON", v => v < 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetBeacon(0)),
            Auto("PF_GEAR_DOWN", "PREFLIGHT", "Landing gear lever: DOWN",
                "GEAR_Lever", v => Math.Abs(v - 1) < 0.1, RevertBehavior.StayComplete,
                action: (e, _) => e.SetGearLever(1)),
            Auto("PF_AUTOBRAKE_RTO", "PREFLIGHT", "Autobrake selector: RTO",
                "BRAKES_AutobrakeSelector", v => Math.Abs(v) < 0.1, RevertBehavior.RevertToState,
                action: (e, _) => e.SetAutobrake(0)),
            Auto("PF_FUEL_CTRL_CUTOFF", "PREFLIGHT", "Fuel Control switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                (e, _) => e.SetFuelControlLevers(0)),
            Manual("PF_CDU_PREFLIGHT", "PREFLIGHT", "CDU Preflight: Complete"),
            ActionManual("PF_THRUST_ASYM", "PREFLIGHT", "Thrust Asymmetry Compensation: AUTO",
                (e, _) => e.SetThrustAsymComp(1)),
            Manual("PF_FMC_PERF", "PREFLIGHT", "Performance data: Entered in FMC"),
            Reminder("PF_OXYGEN", "PREFLIGHT", "Oxygen: Tested 100%"),
            Manual("PF_BARO_SET", "PREFLIGHT", "Barometric reference: Set local setting"),
            ActionManual("PF_EFIS_SET", "PREFLIGHT", "EFIS: Mode MAP, range 40nm",
                (e, _) => { e.SetEFISModeCapt(2); e.SetEFISModeFO(2); e.SetEFISRangeCapt(2); e.SetEFISRangeFO(2); }),
            ActionManual("PF_FD_ON", "PREFLIGHT", "Flight Director switches: ON",
                (e, s) => { e.SetFDLeft(1, s); e.SetFDRight(1, s); }),
        }
    };

    // -----------------------------------------------------------------------
    // 3. Preflight Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildPreflightChecklist() => new()
    {
        Id = "PREFLIGHT_CL", Name = "Preflight Checklist",
        Items = new()
        {
            Reminder("PFCL_OXYGEN", "PREFLIGHT_CL", "Oxygen: Tested 100%"),
            Manual("PFCL_FLT_INST", "PREFLIGHT_CL", "Flight Instruments: Set heading and altimeter"),
            Auto("PFCL_PARK_BRAKE", "PREFLIGHT_CL", "Parking Brake: SET",
                "BRAKES_ParkingBrakeLeverOn", v => v > 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetParkingBrake(1)),
            Auto("PFCL_FUEL_CTRL", "PREFLIGHT_CL", "Fuel Control Switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5, RevertBehavior.RevertToState,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                (e, _) => e.SetFuelControlLevers(0)),
        }
    };

    // -----------------------------------------------------------------------
    // 4. Before Start
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildBeforeStart() => new()
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
            Reminder("BS_DOORS_VERIFY", "BEFORE_START", "Exterior doors: Verify closed"),
            Reminder("BS_WINDOWS", "BEFORE_START", "Flight deck windows: Closed and locked"),
            Auto("BS_SEAT_BELTS", "BEFORE_START", "Seat Belts selector: AUTO",
                "SIGNS_SeatBeltsSelector", v => v >= 1, RevertBehavior.StayComplete,
                action: (e, _) => e.SetSeatBelts(1)),
            Manual("BS_APU_START", "BEFORE_START", "APU: START (ON → START → wait for self-sustaining)"),
            ActionManual("BS_EXT_PWR_OFF", "BEFORE_START", "External power: Disconnect when APU available",
                (e, _) => { e.PushGroundPowerPrimary(); e.PushGroundPowerSecondary(); }),
            Manual("BS_HYD_PRESSURIZE", "BEFORE_START", "Obtain clearance to pressurize hydraulics"),
            ActionManual("BS_HYD_DEMAND", "BEFORE_START", "Demand pump selectors: AUTO",
                (e, _) => e.SetDemandPumps(2)),  // 2=AUTO
            ActionManual("BS_CTR_PUMPS_ON", "BEFORE_START", "Center Electric Primary pump switches: ON (check quantity)",
                (e, _) => e.SetCenterFuelPumps(1)),
            Auto("BS_WING_PUMPS_ON", "BEFORE_START", "Left and Right Fuel Pump switches: ON",
                "FUEL_PumpFwd_Sw_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "FUEL_PumpFwd_Sw_1", "FUEL_PumpAft_Sw_0", "FUEL_PumpAft_Sw_1" },
                (e, _) => e.SetWingFuelPumps(1)),
            Auto("BS_BEACON_ON", "BEFORE_START", "Beacon light: ON",
                "LTS_Beacon_Sw_ON", v => v > 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetBeacon(1)),
            ActionManual("BS_CANCEL_RECALL", "BEFORE_START", "Cancel/Recall: PUSH, verify expected alerts",
                (e, _) => e.PushCancelRecall()),
            ActionManual("BS_TRANSPONDER", "BEFORE_START", "Transponder: XPNDR",
                (e, _) => e.SetTransponderMode(2)),
            Manual("BS_STAB_TRIM", "BEFORE_START", "Stabilizer trim: Set for takeoff, verify green band"),
            Manual("BS_AIL_TRIM", "BEFORE_START", "Aileron trim: 0 degrees"),
            Manual("BS_RUD_TRIM", "BEFORE_START", "Rudder trim: 0 degrees"),
        }
    };

    // -----------------------------------------------------------------------
    // 5. Before Start Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildBeforeStartChecklist() => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = new()
        {
            Reminder("BSCL_DOOR", "BEFORE_START_CL", "Flight Deck Door: Closed and locked"),
            Auto("BSCL_SIGNS", "BEFORE_START_CL", "Passenger Signs: Set",
                "SIGNS_SeatBeltsSelector", v => v >= 1, RevertBehavior.RevertToState,
                action: (e, _) => e.SetSeatBelts(1)),
            Manual("BSCL_MCP", "BEFORE_START_CL", "MCP: Set speed, heading and altitude"),
            Manual("BSCL_V_SPEEDS", "BEFORE_START_CL", "Takeoff Speeds: Set V1, VR and V2 from CDU"),
            Manual("BSCL_CDU_COMPLETE", "BEFORE_START_CL", "CDU Preflight: Complete"),
            Manual("BSCL_TRIM", "BEFORE_START_CL", "Trim: Elevator set, aileron 0, rudder 0"),
            Reminder("BSCL_BRIEFING", "BEFORE_START_CL", "Taxi and Takeoff Briefing: Complete"),
            Auto("BSCL_BEACON", "BEFORE_START_CL", "Beacon: ON",
                "LTS_Beacon_Sw_ON", v => v > 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetBeacon(1)),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Engine Start
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            ActionManual("ES_ENG2_START_SEL", "ENGINE_START", "Engine 2 Start/Ignition selector: START",
                (e, _) => e.SetEngStartSelector2(0)),  // 0=START/GND
            Auto("ES_ENG2_FUEL_CTRL", "ENGINE_START", "Engine 2 Fuel Control: RUN",
                "ENG_FuelControl_Sw_RUN_1", v => v > 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetFuelControl2(1)),  // logical 1=RUN
            Manual("ES_ENG2_OIL_PRESS", "ENGINE_START", "Engine 2 Oil Pressure: Verify increases"),
            ActionManual("ES_ENG1_START_SEL", "ENGINE_START", "Engine 1 Start/Ignition selector: START",
                (e, _) => e.SetEngStartSelector1(0)),  // 0=START/GND
            Auto("ES_ENG1_FUEL_CTRL", "ENGINE_START", "Engine 1 Fuel Control: RUN",
                "ENG_FuelControl_Sw_RUN_0", v => v > 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetFuelControl1(1)),  // logical 1=RUN
            Manual("ES_ENG1_OIL_PRESS", "ENGINE_START", "Engine 1 Oil Pressure: Verify increases"),
        }
    };

    // -----------------------------------------------------------------------
    // 7. Before Taxi
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildBeforeTaxi() => new()
    {
        Id = "BEFORE_TAXI", Name = "Before Taxi",
        Items = new()
        {
            ActionManual("BT_APU_OFF", "BEFORE_TAXI", "APU selector: OFF (if no longer needed)",
                (e, _) => e.SetApuSelector(0)),
            Manual("BT_ANTI_ICE", "BEFORE_TAXI", "Engine Anti-Ice selectors: As needed"),
            Manual("BT_FLAPS", "BEFORE_TAXI", "Flap lever: Set for takeoff (from SimBrief perf data)"),
            Manual("BT_FCTL_CHECK", "BEFORE_TAXI", "Flight controls: CHECK"),
            ActionManual("BT_RECALL", "BEFORE_TAXI", "Recall: Check",
                (e, _) => e.PushCancelRecall()),
            Manual("BT_PACKS", "BEFORE_TAXI", "Packs: Confirm configuration for takeoff"),
        }
    };

    // -----------------------------------------------------------------------
    // 8. Before Taxi Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildBeforeTaxiChecklist() => new()
    {
        Id = "BEFORE_TAXI_CL", Name = "Before Taxi Checklist",
        Items = new()
        {
            Manual("BTCL_ANTI_ICE", "BEFORE_TAXI_CL", "Anti-Ice: As required"),
            ActionManual("BTCL_RECALL", "BEFORE_TAXI_CL", "Recall: Checked",
                (e, _) => e.PushCancelRecall()),
            Auto("BTCL_AUTOBRAKE", "BEFORE_TAXI_CL", "Auto Brake: RTO",
                "BRAKES_AutobrakeSelector", v => Math.Abs(v) < 0.1, RevertBehavior.RevertToState,
                action: (e, _) => e.SetAutobrake(0)),
            Manual("BTCL_FCTL", "BEFORE_TAXI_CL", "Flight Controls: Checked"),
            Reminder("BTCL_GND_EQUIP", "BEFORE_TAXI_CL", "Ground Equipment: Clear"),
        }
    };

    // -----------------------------------------------------------------------
    // 9. Before Takeoff Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildBeforeTakeoffChecklist() => new()
    {
        Id = "BEFORE_TKOF_CL", Name = "Before Takeoff Checklist",
        Items = new()
        {
            // Flap target depends on SimBrief perf data — no fixed action value
            Auto("BTKOF_FLAPS", "BEFORE_TKOF_CL", "Flaps: Set for takeoff",
                "FCTL_Flaps_Lever", v => v >= 1 && v <= 3, RevertBehavior.RevertToState),
            Manual("BTKOF_V_SPEEDS", "BEFORE_TKOF_CL", "V speeds: Checked"),
            Reminder("BTKOF_RUNWAY", "BEFORE_TKOF_CL", "Takeoff runway: Confirmed"),
            Manual("BTKOF_ALT", "BEFORE_TKOF_CL", "Altitude: Set initial climb"),
        }
    };

    // -----------------------------------------------------------------------
    // 10. After Takeoff Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildAfterTakeoffChecklist() => new()
    {
        Id = "AFTER_TKOF_CL", Name = "After Takeoff Checklist",
        Items = new()
        {
            Auto("ATKOF_GEAR", "AFTER_TKOF_CL", "Landing Gear: UP",
                "GEAR_Lever", v => v < 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetGearLever(0)),
            Auto("ATKOF_FLAPS", "AFTER_TKOF_CL", "Flaps: UP",
                "FCTL_Flaps_Lever", v => v < 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetFlapsPosition(0)),
        }
    };

    // -----------------------------------------------------------------------
    // 11. Descent Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildDescentChecklist() => new()
    {
        Id = "DESCENT_CL", Name = "Descent Checklist",
        Items = new()
        {
            ActionManual("DSC_RECALL", "DESCENT_CL", "Recall: Checked",
                (e, _) => e.PushCancelRecall()),
            Manual("DSC_AUTOBRAKE", "DESCENT_CL", "Autobrake: As required"),
            Manual("DSC_LANDING_DATA", "DESCENT_CL", "Landing Data: Set VREF and minimums"),
            Reminder("DSC_BRIEFING", "DESCENT_CL", "Approach Briefing: Complete"),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Approach Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildApproachChecklist() => new()
    {
        Id = "APPROACH_CL", Name = "Approach Checklist",
        Items = new()
        {
            Manual("APP_ALTIMETERS", "APPROACH_CL", "Altimeters: SET"),
        }
    };

    // -----------------------------------------------------------------------
    // 13. Landing Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildLandingChecklist() => new()
    {
        Id = "LANDING_CL", Name = "Landing Checklist",
        Items = new()
        {
            Auto("LDG_SPEEDBRAKE", "LANDING_CL", "Speedbrake: ARMED",
                "FCTL_Speedbrake_Lever", v => v > 0.5 && v < 1.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetSpeedbrakeArmed()),
            Auto("LDG_GEAR", "LANDING_CL", "Landing Gear: DOWN",
                "GEAR_Lever", v => Math.Abs(v - 1) < 0.1, RevertBehavior.RevertToState,
                action: (e, _) => e.SetGearLever(1)),
            // Landing flap target depends on approach — no fixed action value
            Auto("LDG_FLAPS", "LANDING_CL", "Flaps: Set for landing",
                "FCTL_Flaps_Lever", v => v >= 4, RevertBehavior.RevertToState),
        }
    };

    // -----------------------------------------------------------------------
    // 14. After Landing
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_SPEEDBRAKE", "AFTER_LANDING", "Speed Brake lever: DOWN",
                "FCTL_Speedbrake_Lever", v => v < 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetSpeedbrakeDown()),
            Manual("AL_APU", "AFTER_LANDING", "APU: As needed"),
            ActionManual("AL_EXT_LIGHTS", "AFTER_LANDING", "Exterior Lights: Retract/off",
                (e, _) => { e.SetLandingLights(0); e.SetRunwayTurnoff(0); }),
            Manual("AL_WX_RADAR_OFF", "AFTER_LANDING", "WX Radar: OFF"),
            Auto("AL_AUTOBRAKE_OFF", "AFTER_LANDING", "Auto Brake: OFF",
                "BRAKES_AutobrakeSelector", v => Math.Abs(v - 1) < 0.1, RevertBehavior.StayComplete,
                action: (e, _) => e.SetAutobrake(1)),  // 1=Off
            Auto("AL_FLAPS_UP", "AFTER_LANDING", "Flap lever: UP",
                "FCTL_Flaps_Lever", v => v < 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetFlapsPosition(0)),
            Manual("AL_TRANSPONDER", "AFTER_LANDING", "Transponder: As required"),
        }
    };

    // -----------------------------------------------------------------------
    // 15. Shutdown
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            Auto("SD_PARK_BRAKE", "SHUTDOWN", "Parking Brake/Chocks: SET",
                "BRAKES_ParkingBrakeLeverOn", v => v > 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetParkingBrake(1)),
            Manual("SD_APU", "SHUTDOWN", "APU: START (if needed for power)"),
            Auto("SD_FUEL_CTRL", "SHUTDOWN", "Fuel Control switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5, RevertBehavior.RevertToState,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                (e, _) => e.SetFuelControlLevers(0)),
            Auto("SD_SEAT_BELTS_OFF", "SHUTDOWN", "Fasten Belts switch: OFF",
                "SIGNS_SeatBeltsSelector", v => v < 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetSeatBelts(0)),
            Auto("SD_FUEL_PUMPS", "SHUTDOWN", "Fuel Pump switches: OFF",
                "FUEL_PumpFwd_Sw_0", v => v < 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetWingFuelPumps(0)),
            Auto("SD_BEACON_OFF", "SHUTDOWN", "Beacon light: OFF",
                "LTS_Beacon_Sw_ON", v => v < 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetBeacon(0)),
        }
    };

    // -----------------------------------------------------------------------
    // 16. Shutdown Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildShutdownChecklist() => new()
    {
        Id = "SHUTDOWN_CL", Name = "Shutdown Checklist",
        Items = new()
        {
            ActionManual("SDCL_HYD", "SHUTDOWN_CL", "Hydraulic panel: SET",
                (e, _) => { e.SetEngPumps(0); e.SetElecPumps(0); e.SetDemandPumps(0); }),
            Auto("SDCL_FUEL_PUMPS", "SHUTDOWN_CL", "Fuel Pumps: OFF",
                "FUEL_PumpFwd_Sw_0", v => v < 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetWingFuelPumps(0)),
            Auto("SDCL_FLAPS_UP", "SHUTDOWN_CL", "Flaps: UP",
                "FCTL_Flaps_Lever", v => v < 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetFlapsPosition(0)),
            Auto("SDCL_PARK_BRAKE", "SHUTDOWN_CL", "Parking Brake: SET",
                "BRAKES_ParkingBrakeLeverOn", v => v > 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetParkingBrake(1)),
            Auto("SDCL_FUEL_CTRL", "SHUTDOWN_CL", "Fuel Control Switches: CUTOFF",
                "ENG_FuelControl_Sw_RUN_0", v => v < 0.5, RevertBehavior.RevertToState,
                new[] { "ENG_FuelControl_Sw_RUN_1" },
                (e, _) => e.SetFuelControlLevers(0)),
            Manual("SDCL_WX_RADAR", "SHUTDOWN_CL", "Weather Radar: OFF"),
        }
    };

    // -----------------------------------------------------------------------
    // 17. Secure
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildSecure() => new()
    {
        Id = "SECURE", Name = "Secure",
        Items = new()
        {
            Auto("SEC_ADIRU", "SECURE", "ADIRU switch: OFF",
                "ADIRU_Sw_On", v => v < 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetAdiru(0)),
            ActionManual("SEC_EMER_EXIT_OFF", "SECURE", "Emergency Exit Lights switch: OFF",
                (e, _) => { e.CloseEmerExitLightGuard(); e.SetEmerExitLights(0); }),
            Auto("SEC_PACKS_OFF", "SECURE", "Pack switches: OFF",
                "AIR_Pack_Sw_AUTO_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "AIR_Pack_Sw_AUTO_1" },
                (e, _) => e.SetPacks(0)),
        }
    };

    // -----------------------------------------------------------------------
    // 18. Secure Checklist
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildSecureChecklist() => new()
    {
        Id = "SECURE_CL", Name = "Secure Checklist",
        Items = new()
        {
            Auto("SECCL_ADIRU", "SECURE_CL", "ADIRU: OFF",
                "ADIRU_Sw_On", v => v < 0.5, RevertBehavior.RevertToState,
                action: (e, _) => e.SetAdiru(0)),
            ActionManual("SECCL_EMER_LIGHTS", "SECURE_CL", "Emergency Lights: OFF",
                (e, _) => { e.CloseEmerExitLightGuard(); e.SetEmerExitLights(0); }),
            Auto("SECCL_PACKS", "SECURE_CL", "Packs: OFF",
                "AIR_Pack_Sw_AUTO_0", v => v < 0.5, RevertBehavior.RevertToState,
                new[] { "AIR_Pack_Sw_AUTO_1" },
                (e, _) => e.SetPacks(0)),
        }
    };

    // -----------------------------------------------------------------------
    // 19. Electrical Power Down
    // -----------------------------------------------------------------------
    private static ChecklistGroup BuildElectricalPowerDown() => new()
    {
        Id = "ELEC_POWER_DOWN", Name = "Electrical Power Down",
        Items = new()
        {
            ActionManual("EPD_APU_GND_OFF", "ELEC_POWER_DOWN", "APU or Ground Power switches: OFF",
                (e, _) => { e.SetApuSelector(0); e.PushGroundPowerPrimary(); e.PushGroundPowerSecondary(); }),
            Auto("EPD_BATTERY_OFF", "ELEC_POWER_DOWN", "Battery switch: OFF",
                "ELEC_Battery_Sw_ON", v => v < 0.5, RevertBehavior.StayComplete,
                action: (e, _) => e.SetBattery(0)),
        }
    };

    // -----------------------------------------------------------------------
    // Builder helpers
    // -----------------------------------------------------------------------

    /// <summary>AutoDetectable item — state is read from sim vars; optional CheckAction fires on manual check.</summary>
    private static ChecklistItem Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition, RevertBehavior revert,
        string[]? additionalFields = null,
        Action<AircraftActionExecutor, AircraftStateEvaluator>? action = null) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = revert,
        AdditionalStateFields = additionalFields ?? Array.Empty<string>(),
        AdditionalStateCondition = condition,
        CheckAction = action,
    };

    /// <summary>Overload without additionalFields — makes call sites cleaner when only action is needed.</summary>
    private static ChecklistItem Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition, RevertBehavior revert,
        Action<AircraftActionExecutor, AircraftStateEvaluator>? action) =>
        Auto(id, groupId, label, field, condition, revert, null, action);

    /// <summary>Manual item with no linked sim action — user ticks after doing it themselves.</summary>
    private static ChecklistItem Manual(string id, string groupId, string label) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
    };

    /// <summary>Manual item WITH a linked sim action — checking the box fires the action.</summary>
    private static ChecklistItem ActionManual(string id, string groupId, string label,
        Action<AircraftActionExecutor, AircraftStateEvaluator> action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };

    /// <summary>Captain reminder — user reads/confirms and manually ticks. No sim action.</summary>
    private static ChecklistItem Reminder(string id, string groupId, string text) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
    };
}
