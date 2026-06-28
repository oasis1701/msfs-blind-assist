using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

using Item = Models.ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator>;
using Group = Models.ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator>;
using Act = System.Action<AircraftActionExecutor, AircraftStateEvaluator>;

/// <summary>
/// Data-driven PMDG 737 NG3 First-Officer checklist definitions — the same two-layer
/// structure as the 777: ten auto-detect "state" groups (mirror what each flow sets, with
/// CheckActions that fire the switch when ticked) plus ten challenge-response readback
/// checklists. State fields are verified against <c>PMDGNG3DataStruct.cs</c>.
/// </summary>
public static class PMDG737ChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        // --- State groups + interleaved readbacks (flight order) ---
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

    // =======================================================================
    // State groups (auto-detect; CheckAction fires the switch)
    // =======================================================================

    private static Group BuildElectricalPowerUp() => new()
    {
        Id = "ELEC_POWER_UP", Name = "Electrical Power Up",
        Items = new()
        {
            Auto("EPU_BATTERY", "ELEC_POWER_UP", "Battery: ON", "ELEC_BatSelector", v => v > 1.5, RevertBehavior.StayComplete,
                (e, _) => e.SetBattery(2)),
            Auto("EPU_STBY", "ELEC_POWER_UP", "Standby power: AUTO", "ELEC_StandbyPowerSelector", v => v > 1.5, RevertBehavior.StayComplete,
                (e, _) => e.SetStandbyPower(2)),
            Auto("EPU_GPU", "ELEC_POWER_UP", "Ground power: ON", "ELEC_GrdPwrSw", v => v > 0.5, RevertBehavior.StayComplete,
                (e, _) => e.SetGroundPower(1)),
            Auto("EPU_IRS", "ELEC_POWER_UP", "IRS mode selectors: NAV", "IRS_ModeSelector_0", v => v > 1.5, RevertBehavior.StayComplete,
                new[] { "IRS_ModeSelector_1" }, (e, _) => e.SetIrsMode(2)),
            Auto("EPU_IRS_ALIGNED", "ELEC_POWER_UP", "IRS aligned", "IRS_aligned", v => v > 0.5, RevertBehavior.StayComplete, action: null),
        }
    };

    private static Group BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = new()
        {
            Auto("PF_YD", "PREFLIGHT", "Yaw damper: ON", "FCTL_YawDamper_Sw", v => v > 0.5, RevertBehavior.StayComplete, (e, _) => e.SetYawDamper(1)),
            Auto("PF_EMER", "PREFLIGHT", "Emergency exit lights: ARMED", "LTS_EmerExitSelector", v => v > 0.5, RevertBehavior.StayComplete, (e, _) => e.SetEmerExitLights(1)),
            Auto("PF_BELTS", "PREFLIGHT", "Seatbelt signs: ON", "COMM_FastenBeltsSelector", v => v > 1.5, RevertBehavior.StayComplete, (e, _) => e.SetSeatBelts(2)),
            Auto("PF_WINHEAT", "PREFLIGHT", "Window heat: ON", "ICE_WindowHeatSw_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, (e, _) => e.SetWindowHeat(1)),
            Auto("PF_WAI", "PREFLIGHT", "Wing anti-ice: OFF", "ICE_WingAntiIceSw", v => v < 0.5, RevertBehavior.StayComplete, (e, _) => e.SetWingAntiIce(0)),
            Auto("PF_PACKS", "PREFLIGHT", "Packs: AUTO", "AIR_PackSwitch_0", v => v > 0.5 && v < 1.5, RevertBehavior.StayComplete,
                new[] { "AIR_PackSwitch_1" }, (e, _) => e.SetPacks(1)),
            Auto("PF_ISO", "PREFLIGHT", "Isolation valve: OPEN", "AIR_IsolationValveSwitch", v => v > 1.5, RevertBehavior.StayComplete, (e, _) => e.SetIsolationValve(2)),
            Auto("PF_BLEEDS", "PREFLIGHT", "Engine bleeds: ON", "AIR_BleedAirSwitch_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "AIR_BleedAirSwitch_1" }, (e, _) => e.SetEngBleeds(1)),
            Auto("PF_FD", "PREFLIGHT", "Flight directors: ON", "MCP_FDSw_0", v => v > 0.5, RevertBehavior.StayComplete, new[] { "MCP_FDSw_1" }, action: null),
            Auto("PF_AB", "PREFLIGHT", "Autobrake: RTO", "MAIN_AutobrakeSelector", v => v < 0.5, RevertBehavior.RevertToState, (e, _) => e.SetAutobrake(0)),
            Reminder("PF_PRESS", "PREFLIGHT", "Flight and landing altitudes: SET"),
            Reminder("PF_ALT", "PREFLIGHT", "Altimeters: SET to local QNH"),
        }
    };

    private static Group BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            Auto("BS_APU", "BEFORE_START", "APU: ON line", "APU_Selector", v => v > 0.5, RevertBehavior.StayComplete, (e, _) => e.SetApuSelector(2)),
            Auto("BS_FUEL", "BEFORE_START", "Fuel pumps: ON", "FUEL_PumpFwdSw_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "FUEL_PumpFwdSw_1", "FUEL_PumpAftSw_0", "FUEL_PumpAftSw_1" }, (e, _) => e.SetWingFuelPumps(1)),
            Auto("BS_HYD", "BEFORE_START", "Electric hydraulic pumps: ON", "HYD_PumpSw_elec_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "HYD_PumpSw_elec_1" }, (e, _) => e.SetElecHydPumps(1)),
            Auto("BS_HYDENG", "BEFORE_START", "Engine hydraulic pumps: ON", "HYD_PumpSw_eng_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "HYD_PumpSw_eng_1" }, (e, _) => e.SetEngHydPumps(1)),
            Auto("BS_APUBLEED", "BEFORE_START", "APU bleed air: ON", "AIR_APUBleedAirSwitch", v => v > 0.5, RevertBehavior.StayComplete, (e, _) => e.SetApuBleed(1)),
            Auto("BS_ANTICOL", "BEFORE_START", "Anti-collision light: ON", "LTS_AntiCollisionSw", v => v > 0.5, RevertBehavior.StayComplete, (e, _) => e.SetBeacon(1)),
            Auto("BS_XPDR", "BEFORE_START", "Transponder: TA/RA", "XPDR_ModeSel", v => v > 3.5, RevertBehavior.StayComplete, (e, _) => e.SetTransponderMode(4)),
        }
    };

    private static Group BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            Auto("ES_E2", "ENGINE_START", "Engine 2: started", "FUEL_annunENG_VALVE_CLOSED_1", v => v < 0.5, RevertBehavior.StayComplete, action: null),
            Auto("ES_E1", "ENGINE_START", "Engine 1: started", "FUEL_annunENG_VALVE_CLOSED_0", v => v < 0.5, RevertBehavior.StayComplete, action: null),
        }
    };

    private static Group BuildBeforeTaxi() => new()
    {
        Id = "BEFORE_TAXI", Name = "Before Taxi",
        Items = new()
        {
            // After-start power transfer (folded in from the former After Start group)
            Auto("BT_GEN", "BEFORE_TAXI", "Generators: ON", "ELEC_GenSw_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "ELEC_GenSw_1" }, (e, _) => e.SetGenerators(1)),
            Auto("BT_APU", "BEFORE_TAXI", "APU: OFF", "APU_Selector", v => v < 0.5, RevertBehavior.StayComplete, (e, _) => e.SetApuSelector(0)),
            Auto("BT_PROBE", "BEFORE_TAXI", "Probe heat: ON", "ICE_ProbeHeatSw_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "ICE_ProbeHeatSw_1" }, (e, _) => e.SetProbeHeat(1)),
            Auto("BT_ISO", "BEFORE_TAXI", "Isolation valve: AUTO", "AIR_IsolationValveSwitch", v => v > 0.5 && v < 1.5, RevertBehavior.StayComplete, (e, _) => e.SetIsolationValve(1)),
            Auto("BT_START", "BEFORE_TAXI", "Engine start switches: CONT", "ENG_StartSelector_0", v => v > 1.5 && v < 2.5, RevertBehavior.StayComplete,
                new[] { "ENG_StartSelector_1" }, (e, _) => { e.SetEngStartSelector1(2); e.SetEngStartSelector2(2); }),
            Auto("BT_TAXI", "BEFORE_TAXI", "Taxi light: ON", "LTS_TaxiSw", v => v > 0.5, RevertBehavior.StayComplete, (e, _) => e.SetTaxiLights(1)),
        }
    };

    private static Group BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_PROBE", "AFTER_LANDING", "Probe heat: OFF", "ICE_ProbeHeatSw_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "ICE_ProbeHeatSw_1" }, (e, _) => e.SetProbeHeat(0)),
            Auto("AL_WAI", "AFTER_LANDING", "Wing anti-ice: OFF", "ICE_WingAntiIceSw", v => v < 0.5, RevertBehavior.StayComplete, (e, _) => e.SetWingAntiIce(0)),
            Auto("AL_APU", "AFTER_LANDING", "APU: ON", "APU_Selector", v => v > 0.5, RevertBehavior.StayComplete, (e, _) => e.SetApuSelector(1)),
            Auto("AL_AB", "AFTER_LANDING", "Autobrake: OFF", "MAIN_AutobrakeSelector", v => v > 0.5 && v < 1.5, RevertBehavior.StayComplete, (e, _) => e.SetAutobrake(1)),
            Auto("AL_TAXI", "AFTER_LANDING", "Taxi light: ON", "LTS_TaxiSw", v => v > 0.5, RevertBehavior.StayComplete, (e, _) => e.SetTaxiLights(1)),
        }
    };

    private static Group BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            Auto("SD_LEVERS", "SHUTDOWN", "Engine start levers: CUTOFF", "FUEL_annunENG_VALVE_CLOSED_0", v => v > 0.5, RevertBehavior.StayComplete,
                new[] { "FUEL_annunENG_VALVE_CLOSED_1" }, (e, _) => { e.SetFuelControl1(0); e.SetFuelControl2(0); }),
            Auto("SD_BELTS", "SHUTDOWN", "Seatbelt signs: OFF", "COMM_FastenBeltsSelector", v => v < 0.5, RevertBehavior.StayComplete, (e, _) => e.SetSeatBelts(0)),
            Auto("SD_FUEL", "SHUTDOWN", "Fuel pumps: OFF", "FUEL_PumpFwdSw_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "FUEL_PumpFwdSw_1", "FUEL_PumpAftSw_0", "FUEL_PumpAftSw_1" }, (e, _) => e.SetWingFuelPumps(0)),
            Auto("SD_WINHEAT", "SHUTDOWN", "Window heat: OFF", "ICE_WindowHeatSw_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, (e, _) => e.SetWindowHeat(0)),
            Auto("SD_XPDR", "SHUTDOWN", "Transponder: STBY", "XPDR_ModeSel", v => v < 0.5, RevertBehavior.StayComplete, (e, _) => e.SetTransponderMode(0)),
        }
    };

    private static Group BuildSecure() => new()
    {
        Id = "SECURE", Name = "Secure",
        Items = new()
        {
            Auto("SE_IRS", "SECURE", "IRS mode selectors: OFF", "IRS_ModeSelector_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "IRS_ModeSelector_1" }, (e, _) => e.SetIrsMode(0)),
            Auto("SE_EMER", "SECURE", "Emergency exit lights: OFF", "LTS_EmerExitSelector", v => v < 0.5, RevertBehavior.StayComplete, (e, _) => e.SetEmerExitLights(0)),
            Auto("SE_WINHEAT", "SECURE", "Window heat: OFF", "ICE_WindowHeatSw_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, (e, _) => e.SetWindowHeat(0)),
            Auto("SE_PACKS", "SECURE", "Packs: OFF", "AIR_PackSwitch_0", v => v < 0.5, RevertBehavior.StayComplete,
                new[] { "AIR_PackSwitch_1" }, (e, _) => e.SetPacks(0)),
        }
    };

    private static Group BuildElectricalPowerDown() => new()
    {
        Id = "ELEC_POWER_DOWN", Name = "Electrical Power Down",
        Items = new()
        {
            Auto("EPD_BAT", "ELEC_POWER_DOWN", "Battery: OFF", "ELEC_BatSelector", v => v < 0.5, RevertBehavior.StayComplete, (e, _) => e.SetBattery(0)),
            Reminder("EPD_PWR", "ELEC_POWER_DOWN", "APU or ground power: OFF (after the 2-minute APU cooldown)"),
        }
    };

    // =======================================================================
    // Readback checklists (challenge-response)
    // =======================================================================

    private static Group BuildPreflightChecklist() => new()
    {
        Id = "PREFLIGHT_CL", Name = "Preflight Checklist",
        Items = new()
        {
            Reminder("PFC_OXY", "PREFLIGHT_CL", "Oxygen: TESTED, 100%"),
            Auto("PFC_WINHEAT", "PREFLIGHT_CL", "Window heat: ON", "ICE_WindowHeatSw_0", v => v > 0.5, RevertBehavior.RevertToState,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, action: null),
            Reminder("PFC_PRESS", "PREFLIGHT_CL", "Pressurization mode selector: AUTO"),
            Reminder("PFC_INST", "PREFLIGHT_CL", "Flight instruments: heading and altimeter checked"),
            Auto("PFC_PARK", "PREFLIGHT_CL", "Parking brake: SET", "PED_annunParkingBrake", v => v > 0.5, RevertBehavior.RevertToState, action: null),
            Auto("PFC_LEVERS", "PREFLIGHT_CL", "Engine start levers: CUTOFF", "FUEL_annunENG_VALVE_CLOSED_0", v => v > 0.5, RevertBehavior.RevertToState,
                new[] { "FUEL_annunENG_VALVE_CLOSED_1" }, action: null),
        }
    };

    private static Group BuildBeforeStartChecklist() => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = new()
        {
            Reminder("BSC_DOORS", "BEFORE_START_CL", "Flight deck door: closed and locked"),
            Reminder("BSC_FUEL", "BEFORE_START_CL", "Fuel: quantity checked, pumps ON"),
            Auto("BSC_BELTS", "BEFORE_START_CL", "Passenger signs: ON", "COMM_FastenBeltsSelector", v => v > 1.5, RevertBehavior.RevertToState, action: null),
            Reminder("BSC_WINDOWS", "BEFORE_START_CL", "Windows: locked"),
            Reminder("BSC_MCP", "BEFORE_START_CL", "MCP: speed, heading and altitude set"),
            Reminder("BSC_SPEEDS", "BEFORE_START_CL", "Takeoff speeds: V1, VR and V2 checked"),
            Reminder("BSC_CDU", "BEFORE_START_CL", "CDU preflight: complete"),
            Reminder("BSC_TRIM", "BEFORE_START_CL", "Rudder and aileron trim: free and zero"),
            Auto("BSC_ANTICOL", "BEFORE_START_CL", "Anti-collision light: ON", "LTS_AntiCollisionSw", v => v > 0.5, RevertBehavior.RevertToState, action: null),
        }
    };

    private static Group BuildBeforeTaxiChecklist() => new()
    {
        Id = "BEFORE_TAXI_CL", Name = "Before Taxi Checklist",
        Items = new()
        {
            Auto("BTC_GEN", "BEFORE_TAXI_CL", "Generators: ON", "ELEC_GenSw_0", v => v > 0.5, RevertBehavior.RevertToState, new[] { "ELEC_GenSw_1" }, action: null),
            Auto("BTC_PROBE", "BEFORE_TAXI_CL", "Probe heat: ON", "ICE_ProbeHeatSw_0", v => v > 0.5, RevertBehavior.RevertToState, new[] { "ICE_ProbeHeatSw_1" }, action: null),
            Reminder("BTC_ANTIICE", "BEFORE_TAXI_CL", "Anti-ice: as required"),
            Auto("BTC_ISO", "BEFORE_TAXI_CL", "Isolation valve: AUTO", "AIR_IsolationValveSwitch", v => v > 0.5 && v < 1.5, RevertBehavior.RevertToState, action: null),
            Auto("BTC_START", "BEFORE_TAXI_CL", "Engine start switches: CONT", "ENG_StartSelector_0", v => v > 1.5 && v < 2.5, RevertBehavior.RevertToState, new[] { "ENG_StartSelector_1" }, action: null),
            Reminder("BTC_RECALL", "BEFORE_TAXI_CL", "Recall: checked"),
            Auto("BTC_AB", "BEFORE_TAXI_CL", "Autobrake: RTO", "MAIN_AutobrakeSelector", v => v < 0.5, RevertBehavior.RevertToState, action: null),
            Reminder("BTC_FCTL", "BEFORE_TAXI_CL", "Flight controls: checked"),
            Reminder("BTC_GND", "BEFORE_TAXI_CL", "Ground equipment: clear"),
        }
    };

    private static Group BuildBeforeTakeoffChecklist() => new()
    {
        Id = "BEFORE_TAKEOFF_CL", Name = "Before Takeoff Checklist",
        Items = new()
        {
            Reminder("BTOC_FLAPS", "BEFORE_TAKEOFF_CL", "Flaps: set, green light"),
            Reminder("BTOC_TRIM", "BEFORE_TAKEOFF_CL", "Stabilizer trim: units checked"),
        }
    };

    private static Group BuildAfterTakeoffChecklist() => new()
    {
        Id = "AFTER_TAKEOFF_CL", Name = "After Takeoff Checklist",
        Items = new()
        {
            Auto("ATC_BLEEDS", "AFTER_TAKEOFF_CL", "Engine bleeds: ON", "AIR_BleedAirSwitch_0", v => v > 0.5, RevertBehavior.RevertToState, new[] { "AIR_BleedAirSwitch_1" }, action: null),
            Auto("ATC_PACKS", "AFTER_TAKEOFF_CL", "Packs: AUTO", "AIR_PackSwitch_0", v => v > 0.5 && v < 1.5, RevertBehavior.RevertToState, new[] { "AIR_PackSwitch_1" }, action: null),
            Auto("ATC_GEAR", "AFTER_TAKEOFF_CL", "Landing gear: UP and OFF", "MAIN_GearLever", v => v < 0.5, RevertBehavior.RevertToState, action: null),
            Reminder("ATC_FLAPS", "AFTER_TAKEOFF_CL", "Flaps: UP, no lights"),
        }
    };

    private static Group BuildDescentChecklist() => new()
    {
        Id = "DESCENT_CL", Name = "Descent Checklist",
        Items = new()
        {
            Reminder("DC_PRESS", "DESCENT_CL", "Pressurization: landing altitude set"),
            Reminder("DC_RECALL", "DESCENT_CL", "Recall: checked"),
            Reminder("DC_AB", "DESCENT_CL", "Autobrake: as required"),
            Reminder("DC_DATA", "DESCENT_CL", "Landing data: VREF and minimums set"),
        }
    };

    private static Group BuildApproachChecklist() => new()
    {
        Id = "APPROACH_CL", Name = "Approach Checklist",
        Items = new()
        {
            Reminder("APC_ALT", "APPROACH_CL", "Altimeters: SET"),
        }
    };

    private static Group BuildLandingChecklist() => new()
    {
        Id = "LANDING_CL", Name = "Landing Checklist",
        Items = new()
        {
            Auto("LDC_START", "LANDING_CL", "Engine start switches: CONT", "ENG_StartSelector_0", v => v > 1.5 && v < 2.5, RevertBehavior.RevertToState, new[] { "ENG_StartSelector_1" }, action: null),
            Reminder("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED"),
            Auto("LDC_GEAR", "LANDING_CL", "Landing gear: down, three green", "MAIN_GearLever", v => v > 1.5, RevertBehavior.RevertToState, action: null),
            Reminder("LDC_FLAPS", "LANDING_CL", "Flaps: set, green light"),
        }
    };

    private static Group BuildShutdownChecklist() => new()
    {
        Id = "SHUTDOWN_CL", Name = "Shutdown Checklist",
        Items = new()
        {
            Auto("SDC_FUEL", "SHUTDOWN_CL", "Fuel pumps: OFF", "FUEL_PumpFwdSw_0", v => v < 0.5, RevertBehavior.RevertToState,
                new[] { "FUEL_PumpFwdSw_1", "FUEL_PumpAftSw_0", "FUEL_PumpAftSw_1" }, action: null),
            Auto("SDC_PROBE", "SHUTDOWN_CL", "Probe heat: OFF", "ICE_ProbeHeatSw_0", v => v < 0.5, RevertBehavior.RevertToState, new[] { "ICE_ProbeHeatSw_1" }, action: null),
            Reminder("SDC_HYD", "SHUTDOWN_CL", "Hydraulic panel: set"),
            Reminder("SDC_FLAPS", "SHUTDOWN_CL", "Flaps: UP"),
            Auto("SDC_PARK", "SHUTDOWN_CL", "Parking brake: as required", "PED_annunParkingBrake", v => v > 0.5, RevertBehavior.RevertToState, action: null),
            Auto("SDC_LEVERS", "SHUTDOWN_CL", "Engine start levers: CUTOFF", "FUEL_annunENG_VALVE_CLOSED_0", v => v > 0.5, RevertBehavior.RevertToState,
                new[] { "FUEL_annunENG_VALVE_CLOSED_1" }, action: null),
        }
    };

    private static Group BuildSecureChecklist() => new()
    {
        Id = "SECURE_CL", Name = "Secure Checklist",
        Items = new()
        {
            Auto("SEC_IRS", "SECURE_CL", "IRS: OFF", "IRS_ModeSelector_0", v => v < 0.5, RevertBehavior.RevertToState, new[] { "IRS_ModeSelector_1" }, action: null),
            Auto("SEC_EMER", "SECURE_CL", "Emergency exit lights: OFF", "LTS_EmerExitSelector", v => v < 0.5, RevertBehavior.RevertToState, action: null),
            Auto("SEC_WINHEAT", "SECURE_CL", "Window heat: OFF", "ICE_WindowHeatSw_0", v => v < 0.5, RevertBehavior.RevertToState,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, action: null),
            Auto("SEC_PACKS", "SECURE_CL", "Packs: OFF", "AIR_PackSwitch_0", v => v < 0.5, RevertBehavior.RevertToState, new[] { "AIR_PackSwitch_1" }, action: null),
        }
    };

    // =======================================================================
    // Helpers (mirror the 777; CheckActions are async via AsCheckAction)
    // =======================================================================

    private static Func<AircraftActionExecutor, AircraftStateEvaluator, Task>? AsCheckAction(Act? action)
        => action == null ? null : (e, s) => { action(e, s); return Task.CompletedTask; };

    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition, RevertBehavior revert,
        string[]? additionalFields, Act? action) => new()
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
        CheckAction = AsCheckAction(action),
    };

    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition, RevertBehavior revert, Act? action) =>
        Auto(id, groupId, label, field, condition, revert, null, action);

    private static Item Manual(string id, string groupId, string label) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
    };

    private static Item ActionManual(string id, string groupId, string label, Act action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = AsCheckAction(action),
    };

    private static Item Reminder(string id, string groupId, string text) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
    };
}
