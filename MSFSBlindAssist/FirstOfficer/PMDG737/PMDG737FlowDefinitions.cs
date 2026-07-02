using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

using Flow = Models.FlowDefinition<AircraftStateEvaluator>;
using Step = Models.FlowStep<AircraftStateEvaluator>;

/// <summary>
/// Data-driven PMDG 737 NG3 First-Officer flow definitions, derived from the FSFO V6
/// flow data (PMDG737.dat). Flows reference raw PMDG event-name strings; the
/// <see cref="AircraftActionExecutor"/> dispatch table resolves each to the correct
/// guarded / directional / selector / fuel-lever / mouse-flag dispatch.
///
/// PMDG 737 value conventions:
/// - Battery 1=ON (up detent reports byte 1; byte 2 is an unreachable enum phantom); Standby power 2=AUTO; Emergency exit 1=ARMED,2=ON.
/// - Packs 0=OFF,1=AUTO,2=HIGH; Isolation valve 0=CLOSE,1=AUTO,2=OPEN.
/// - Engine start selector 0=GRD,1=OFF,2=CONT,3=FLT; ignition 0=IGN L,1=BOTH,2=IGN R.
/// - Fuel-control levers 1=Run, 0=Cutoff (executor converts to the directional flag).
/// - Seatbelt/No-smoking signs 0=OFF,1=AUTO,2=ON.
/// - Landing lights (retractable) 0=RETRACT,1=EXTEND,2=ON; Position lights 0=STEADY,1=OFF,2=STROBE&STEADY.
/// - EFIS mode 0=APP,1=VOR,2=MAP,3=PLAN; EFIS range index 0=5,1=10,2=20,3=40,4=80…7=640 nm.
/// - Autobrake 0=RTO,1=OFF,2=1,3=2,4=3,5=MAX; Transponder 0=STBY,3=TA,4=TA/RA.
/// - IRS mode 2=NAV. Gear 0=UP,2=DOWN.
/// </summary>
public static class PMDG737FlowDefinitions
{
    // N2 (percent) the engine must reach while motoring before the start lever introduces
    // fuel — shared with the checklist's StartEngineAsync (see the evaluator const).
    private const double EngStartFuelN2 = AircraftStateEvaluator.EngStartFuelN2;

    public static List<Flow> Build() => new()
    {
        BuildElectricalPowerUp(),
        BuildPreflight(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildBeforeTaxi(),
        BuildBeforeTakeoff(),
        BuildAfterTakeoff(),
        BuildDescent(),
        BuildApproach(),
        BuildLanding(),
        BuildAfterLanding(),
        BuildShutdown(),
        BuildSecure(),
    };

    // -----------------------------------------------------------------------
    // 1. Electrical Power Up
    // -----------------------------------------------------------------------
    private static Flow BuildElectricalPowerUp() => new()
    {
        Id = "ELECTRICAL_POWER_UP", Name = "Electrical Power Up",
        Description = "Cold-and-dark electrical power up: battery, ground power, standby power, IRS to NAV.",
        RelatedChecklistGroupIds = new[] { "ELEC_POWER_UP" },
        Steps = new()
        {
            Skip(SW("EPU_BAT", "Battery: ON", "EVT_OH_ELEC_BATTERY_SWITCH", 1, "ELEC_BatSelector", v => v > 0.5, "EPU_BATTERY"),
                s => s.IsBatteryOn()),
            Skip(SW("EPU_STBY", "Standby power: AUTO", "EVT_OH_ELEC_STBY_PWR_SWITCH", 2),
                s => s.StandbyPower() == 2),
            // Ground power detection reads the FO_GPU_ON synthetic (GRD POWER AVAILABLE +
            // ground-service buses hot) — NEVER the raw ELEC_GrdPwrSw struct bool, which
            // reads TRUE with no GPU at the stand (live-verified 2026-07-02) and made this
            // step skip as "Already set" so external power never came on. The follow-up
            // wait announces a timeout when no GPU is available, so a stand without ground
            // power is never a silent no-op.
            Skip(SW("EPU_GPU", "Ground power: ON", "EVT_OH_ELEC_GRD_PWR_SWITCH", 1),
                s => s.IsGpuOn()),
            Skip(WaitForField("EPU_GPU_WAIT", "Ground power on the buses", "FO_GPU_ON", v => v > 0.5, 10),
                s => s.IsGpuOn()),
            // IRS to NAV — alignment runs in the background; no wait. "IRS aligned" auto-detects later.
            Multi("EPU_IRS", "IRS mode selectors: NAV", ("EVT_IRU_MSU_LEFT", 2), ("EVT_IRU_MSU_RIGHT", 2)),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Preflight (overhead / MIP / pedestal setup)
    // -----------------------------------------------------------------------
    private static Flow BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "Full overhead, MIP and pedestal preflight setup.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT" },
        Steps = new()
        {
            WalkAround("PF_WALK", "Exterior walk-around", 120),
            SW("PF_YD", "Yaw damper: ON", "EVT_OH_YAW_DAMPER", 1),
            Multi("PF_FUEL_OFF", "Fuel pumps: OFF",
                ("EVT_OH_FUEL_PUMP_1_FORWARD", 0), ("EVT_OH_FUEL_PUMP_2_FORWARD", 0),
                ("EVT_OH_FUEL_PUMP_1_AFT", 0), ("EVT_OH_FUEL_PUMP_2_AFT", 0),
                ("EVT_OH_FUEL_PUMP_L_CENTER", 0), ("EVT_OH_FUEL_PUMP_R_CENTER", 0)),
            SW("PF_EMER", "Emergency exit lights: ARMED", "EVT_OH_EMER_EXIT_LIGHT_SWITCH", 1),
            SW("PF_BELTS", "Seatbelt signs: ON", "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", 2),
            Multi("PF_WINHEAT", "Window heat: ON",
                ("EVT_OH_ICE_WINDOW_HEAT_1", 1), ("EVT_OH_ICE_WINDOW_HEAT_2", 1),
                ("EVT_OH_ICE_WINDOW_HEAT_3", 1), ("EVT_OH_ICE_WINDOW_HEAT_4", 1)),
            Multi("PF_PROBE_OFF", "Probe heat: OFF", ("EVT_OH_ICE_PROBE_HEAT_1", 0), ("EVT_OH_ICE_PROBE_HEAT_2", 0)),
            SW("PF_WAI_OFF", "Wing anti-ice: OFF", "EVT_OH_ICE_WING_ANTIICE", 0),
            Multi("PF_EAI_OFF", "Engine anti-ice: OFF", ("EVT_OH_ICE_ENGINE_ANTIICE_1", 0), ("EVT_OH_ICE_ENGINE_ANTIICE_2", 0)),
            Multi("PF_RECIRC", "Recirculation fans: AUTO", ("EVT_OH_BLEED_RECIRC_FAN_L_SWITCH", 1), ("EVT_OH_BLEED_RECIRC_FAN_R_SWITCH", 1)),
            Multi("PF_PACKS", "Packs: AUTO", ("EVT_OH_BLEED_PACK_L_SWITCH", 1), ("EVT_OH_BLEED_PACK_R_SWITCH", 1)),
            SW("PF_ISO", "Isolation valve: OPEN", "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH", 2),
            Multi("PF_BLEEDS", "Engine bleeds: ON", ("EVT_OH_BLEED_ENG_1_SWITCH", 1), ("EVT_OH_BLEED_ENG_2_SWITCH", 1)),
            // Pressurization FLT/LAND ALT from the SimBrief plan (PMDG Direct Control
            // events take literal feet; values pre-rounded to the knob steps at storage —
            // see AircraftStateEvaluator.SetPlannedPressurizationAltitudes). Quietly
            // skipped when no plan is loaded — the Captain fallback below announces
            // instead. Two separate steps keep the CDA writes in separate sim frames.
            // The PR #120 window monitors announce the resulting values automatically.
            DynSW("PF_FLT_ALT", "Flight altitude: set", "EVT_OH_PRESS_FLT_ALT_SET",
                s => s.PlannedFltAltFt, skipWhen: s => s.FltAltMatches()),
            DynSW("PF_LAND_ALT", "Landing altitude: set", "EVT_OH_PRESS_LAND_ALT_SET",
                s => s.PlannedLandAltFt, skipWhen: s => s.LandAltMatches()),
            Skip(Captain("PF_PRESS", "Flight and landing altitudes",
                    "Set flight and landing altitudes on the pressurization panel."),
                s => s.HasPressurizationPlan),
            SW("PF_LOGO", "Logo lights: ON", "EVT_OH_LIGHTS_LOGO", 1),
            MouseFlag("PF_FD1", "Flight director 1: ON", "EVT_MCP_FD_SWITCH_L", s => s.IsFDLeftOn()),
            MouseFlag("PF_FD2", "Flight director 2: ON", "EVT_MCP_FD_SWITCH_R", s => s.IsFDRightOn()),
            Momentary("PF_FF", "Fuel flow: RESET", "EVT_MPM_FUEL_FLOW_SWITCH"),
            SW("PF_AB_RTO", "Autobrake: RTO", "EVT_MPM_AUTOBRAKE_SELECTOR", 0),
            SW("PF_XPDR", "Transponder: STBY", "EVT_TCAS_MODE", 0),
            SW("PF_EFIS_MODE", "EFIS mode: MAP", "EVT_EFIS_CPT_MODE", 2),
            SW("PF_EFIS_RANGE", "EFIS range: 40", "EVT_EFIS_CPT_RANGE", 3),
            Captain("PF_ALT", "Set the altimeters to the local QNH."),
            Captain("PF_TESTS", "Perform the overhead and fire tests as required."),
        }
    };

    // -----------------------------------------------------------------------
    // 3. Before Start
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "APU start, fuel pumps, hydraulics, anti-collision, MCP set, transponder TA/RA.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START" },
        Steps = new()
        {
            Captain("BS_MCP", "Set MCP airspeed, heading and initial altitude."),
            // APU start: ON → dwell → momentary START. A direct write to START (skipping ON)
            // does not spool the APU up. START springs back to ON when self-sustaining.
            SW("BS_APU_ON", "APU selector: ON", "EVT_OH_LIGHTS_APU_START", 1),
            Wait("BS_APU_DWELL", "APU spinning up before start", 2),
            SW("BS_APU_START", "APU selector: START", "EVT_OH_LIGHTS_APU_START", 2),
            WaitForField("BS_APU_WAIT", "Waiting for the APU to come on line", "APU_Selector", v => Math.Abs(v - 1) < 0.1, 90),
            // Transfer the electrical load to the APU: the 737's APU GEN switches are
            // momentary bus-transfer buttons that must be pressed AFTER the APU is on
            // line (unlike the 777, whose gen switch is armed during preflight). Then
            // drop ground power — skipped when no GPU was ever connected.
            Multi("BS_APUGEN", "APU generators: ON",
                ("EVT_OH_ELEC_APU_GEN1_SWITCH", 1), ("EVT_OH_ELEC_APU_GEN2_SWITCH", 1)),
            Skip(SW("BS_GPU_OFF", "Ground power: OFF", "EVT_OH_ELEC_GRD_PWR_SWITCH", 0),
                s => !s.IsGpuOn()),
            Multi("BS_FUELON", "Fuel pumps: ON",
                ("EVT_OH_FUEL_PUMP_1_FORWARD", 1), ("EVT_OH_FUEL_PUMP_2_FORWARD", 1),
                ("EVT_OH_FUEL_PUMP_1_AFT", 1), ("EVT_OH_FUEL_PUMP_2_AFT", 1)),
            Multi("BS_HYDENG", "Engine hydraulic pumps: ON", ("EVT_OH_HYD_ENG1", 1), ("EVT_OH_HYD_ENG2", 1)),
            Multi("BS_HYD", "Electric hydraulic pumps: ON", ("EVT_OH_HYD_ELEC1", 1), ("EVT_OH_HYD_ELEC2", 1)),
            SW("BS_APUBLEED", "APU bleed air: ON", "EVT_OH_BLEED_APU_SWITCH", 1),
            SW("BS_ANTICOL", "Anti-collision light: ON", "EVT_OH_LIGHTS_ANT_COL", 1),
            SW("BS_XPDR", "Transponder: TA/RA", "EVT_TCAS_MODE", 4),
            Captain("BS_GND", "Confirm ground power and chocks removed, doors closed, and taxi clearance."),
        }
    };

    // -----------------------------------------------------------------------
    // 4. Engine Start
    // -----------------------------------------------------------------------
    private static Flow BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Description = "Starts engines 2 then 1 with condition waits on the start valve.",
        RelatedChecklistGroupIds = new[] { "ENGINE_START" },
        Steps = new()
        {
            // Starter air insurance: the start NEEDS bleed pressure (normally the APU).
            // Without it the GRD position motors nothing and the old flow sat in a
            // confusing 60 s N2 wait. Skipped quietly when APU bleed is already on
            // (crossbleed starts still work — the start-valve waits below are the gate).
            Skip(SW("ES_APUBLEED", "APU bleed air: ON", "EVT_OH_BLEED_APU_SWITCH", 1),
                s => s.IsApuBleedOn()),
            Multi("ES_PACKS_OFF", "Packs: OFF", ("EVT_OH_BLEED_PACK_L_SWITCH", 0), ("EVT_OH_BLEED_PACK_R_SWITCH", 0)),
            // --- Engine 2 ---
            SW("ES_E2_GRD", "Engine 2 start switch: GRD", "EVT_OH_LIGHTS_R_ENGINE_START", 0),
            // Prove the starter actually engaged (start valve OPEN) before anything else.
            // If GRD didn't latch or there's no duct pressure, this aborts within 15 s with
            // a clear announcement instead of a 60 s silent N2 wait — and, critically,
            // instead of ever introducing fuel.
            WaitForField("ES_E2_VALVE", "Engine 2 start valve open",
                "ENG_StartValve_1", v => v > 0.5, 15, onTimeout: FlowStepFailurePolicy.Stop),
            // Introduce fuel ONLY after N2 has spun up (~25%); moving the start lever to IDLE
            // before that hangs/aborts the start. FO_ENG2_N2 is the timer-fed N2 (percent).
            // On timeout (starter/bleed failure → N2 never builds) ABORT the flow rather than
            // introduce fuel into an under-rotating engine (a hung/hot start).
            WaitForField("ES_E2_N2", "Engine 2 motoring — waiting for N2 before introducing fuel",
                "FO_ENG2_N2", v => v >= EngStartFuelN2, 60, onTimeout: FlowStepFailurePolicy.Stop),
            SW("ES_E2_RUN", "Engine 2 start lever: IDLE", "EVT_CONTROL_STAND_ENG2_START_LEVER", 1),
            WaitForField("ES_E2_WAIT", "Engine 2 starting — waiting for the start valve to close",
                "ENG_StartValve_1", v => v < 0.5, 120),
            // --- Engine 1 ---
            SW("ES_E1_GRD", "Engine 1 start switch: GRD", "EVT_OH_LIGHTS_L_ENGINE_START", 0),
            WaitForField("ES_E1_VALVE", "Engine 1 start valve open",
                "ENG_StartValve_0", v => v > 0.5, 15, onTimeout: FlowStepFailurePolicy.Stop),
            WaitForField("ES_E1_N2", "Engine 1 motoring — waiting for N2 before introducing fuel",
                "FO_ENG1_N2", v => v >= EngStartFuelN2, 60, onTimeout: FlowStepFailurePolicy.Stop),
            SW("ES_E1_RUN", "Engine 1 start lever: IDLE", "EVT_CONTROL_STAND_ENG1_START_LEVER", 1),
            WaitForField("ES_E1_WAIT", "Engine 1 starting — waiting for the start valve to close",
                "ENG_StartValve_0", v => v < 0.5, 120),
        }
    };

    // -----------------------------------------------------------------------
    // 5. Before Taxi (includes the after-start power transfer)
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeTaxi() => new()
    {
        Id = "BEFORE_TAXI", Name = "Before Taxi",
        Description = "After-start power transfer (generators on, APU off), then probe heat, packs/isolation auto, start switches CONT, taxi lights, flaps.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAXI" },
        Steps = new()
        {
            // --- After-start power transfer (folded in from the former After Start flow) ---
            Multi("BT_GEN", "Generators: ON", ("EVT_OH_ELEC_GEN1_SWITCH", 1), ("EVT_OH_ELEC_GEN2_SWITCH", 1)),
            SW("BT_APUBLEED_OFF", "APU bleed air: OFF", "EVT_OH_BLEED_APU_SWITCH", 0),
            SW("BT_APU_OFF", "APU selector: OFF", "EVT_OH_LIGHTS_APU_START", 0),
            // --- Before-taxi setup ---
            Multi("BT_PROBE", "Probe heat: ON", ("EVT_OH_ICE_PROBE_HEAT_1", 1), ("EVT_OH_ICE_PROBE_HEAT_2", 1)),
            Multi("BT_PACKS", "Packs: AUTO", ("EVT_OH_BLEED_PACK_L_SWITCH", 1), ("EVT_OH_BLEED_PACK_R_SWITCH", 1)),
            SW("BT_ISO", "Isolation valve: AUTO", "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH", 1),
            Multi("BT_START_CONT", "Engine start switches: CONT", ("EVT_OH_LIGHTS_L_ENGINE_START", 2), ("EVT_OH_LIGHTS_R_ENGINE_START", 2)),
            Captain("BT_ANTIICE", "Set engine and wing anti-ice as required for conditions."),
            SW("BT_TAXI", "Taxi light: ON", "EVT_OH_LIGHTS_TAXI", 1),
            Multi("BT_TURNOFF", "Runway turnoff lights: ON", ("EVT_OH_LIGHTS_L_TURNOFF", 1), ("EVT_OH_LIGHTS_R_TURNOFF", 1)),
            Captain("BT_FLAPS", "Set the takeoff flaps."),
            SW("BT_LOWERDU", "Lower display unit: SYS", "EVT_DSP_CPT_LOWER_DU_SELECTOR", 1),
        }
    };

    // -----------------------------------------------------------------------
    // 7. Before Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Description = "Landing lights, strobes, autothrottle arm, transponder TA/RA.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL" },
        Steps = new()
        {
            Multi("BTO_LAND", "Landing lights: ON", ("EVT_OH_LIGHTS_L_RETRACT", 2), ("EVT_OH_LIGHTS_R_RETRACT", 2)),
            SW("BTO_STROBE", "Position lights: STROBE & STEADY", "EVT_OH_LIGHTS_POS_STROBE", 2),
            MouseFlag("BTO_AT", "Autothrottle: ARM", "EVT_MCP_AT_ARM_SWITCH", s => s.IsATArmOn()),
            SW("BTO_XPDR", "Transponder: TA/RA", "EVT_TCAS_MODE", 4),
            Captain("BTO_BRIEF", "Confirm takeoff runway, trim set, and cabin crew notified."),
        }
    };

    // -----------------------------------------------------------------------
    // 8. After Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Packs auto, start switches off, turnoff lights off, gear off, autobrake off.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL" },
        Steps = new()
        {
            Multi("AT_PACKS", "Packs: AUTO", ("EVT_OH_BLEED_PACK_L_SWITCH", 1), ("EVT_OH_BLEED_PACK_R_SWITCH", 1)),
            Multi("AT_START_OFF", "Engine start switches: OFF", ("EVT_OH_LIGHTS_L_ENGINE_START", 1), ("EVT_OH_LIGHTS_R_ENGINE_START", 1)),
            Multi("AT_TURNOFF", "Runway turnoff lights: OFF", ("EVT_OH_LIGHTS_L_TURNOFF", 0), ("EVT_OH_LIGHTS_R_TURNOFF", 0)),
            SW("AT_GEAR_OFF", "Gear lever: OFF", "EVT_GEAR_LEVER", 1),
            SW("AT_AB_OFF", "Autobrake: OFF", "EVT_MPM_AUTOBRAKE_SELECTOR", 1),
        }
    };

    // -----------------------------------------------------------------------
    // 9. Descent
    // -----------------------------------------------------------------------
    private static Flow BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Description = "Seatbelt sign on, autobrake, ILS, approach setup.",
        RelatedChecklistGroupIds = new[] { "DESCENT", "DESCENT_CL" },
        Steps = new()
        {
            SW("DS_BELTS", "Seatbelt signs: ON", "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", 2),
            Captain("DS_AB", "Set the landing autobrake."),
            Captain("DS_ILS", "Set the ILS frequencies and course."),
            Captain("DS_DATA", "Confirm landing data, VREF and minimums."),
        }
    };

    // -----------------------------------------------------------------------
    // 10. Approach
    // -----------------------------------------------------------------------
    private static Flow BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "EFIS approach mode, range 20, altimeters.",
        RelatedChecklistGroupIds = new[] { "APPROACH", "APPROACH_CL" },
        Steps = new()
        {
            SW("AP_EFIS_MODE", "EFIS mode: APP", "EVT_EFIS_CPT_MODE", 0),
            SW("AP_EFIS_RANGE", "EFIS range: 20", "EVT_EFIS_CPT_RANGE", 2),
            Captain("AP_ALT", "Set the altimeters."),
        }
    };

    // -----------------------------------------------------------------------
    // 11. Landing
    // -----------------------------------------------------------------------
    private static Flow BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Description = "Start switches CONT, speedbrake armed, missed approach altitude.",
        RelatedChecklistGroupIds = new[] { "LANDING", "LANDING_CL" },
        Steps = new()
        {
            Multi("LD_START_CONT", "Engine start switches: CONT", ("EVT_OH_LIGHTS_L_ENGINE_START", 2), ("EVT_OH_LIGHTS_R_ENGINE_START", 2)),
            SW("LD_SPDBRK", "Speedbrake: ARMED", "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM", null, isMomentary: true),
            Captain("LD_MISSED", "Set the missed approach altitude."),
        }
    };

    // -----------------------------------------------------------------------
    // 12. After Landing
    // -----------------------------------------------------------------------
    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Lights, anti-ice off, probe heat off, APU on, start switches off, autothrottle/FD off, flaps up.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING" },
        Steps = new()
        {
            Multi("AL_LAND_OFF", "Landing lights: RETRACT", ("EVT_OH_LIGHTS_L_RETRACT", 0), ("EVT_OH_LIGHTS_R_RETRACT", 0)),
            Multi("AL_TURNOFF", "Runway turnoff lights: ON", ("EVT_OH_LIGHTS_L_TURNOFF", 1), ("EVT_OH_LIGHTS_R_TURNOFF", 1)),
            SW("AL_TAXI", "Taxi light: ON", "EVT_OH_LIGHTS_TAXI", 1),
            SW("AL_STROBE_OFF", "Position lights: STEADY", "EVT_OH_LIGHTS_POS_STROBE", 0),
            Multi("AL_EAI_OFF", "Engine anti-ice: OFF", ("EVT_OH_ICE_ENGINE_ANTIICE_1", 0), ("EVT_OH_ICE_ENGINE_ANTIICE_2", 0)),
            SW("AL_WAI_OFF", "Wing anti-ice: OFF", "EVT_OH_ICE_WING_ANTIICE", 0),
            Multi("AL_PROBE_OFF", "Probe heat: OFF", ("EVT_OH_ICE_PROBE_HEAT_1", 0), ("EVT_OH_ICE_PROBE_HEAT_2", 0)),
            // APU START for gate power — ON alone never spools it up (same ON → dwell →
            // momentary START sequence as Before Start; no on-line wait, the aircraft is
            // taxiing in and the Shutdown flow's gen transfer happens minutes later).
            SW("AL_APU_ON", "APU selector: ON", "EVT_OH_LIGHTS_APU_START", 1),
            Wait("AL_APU_DWELL", "APU spinning up before start", 2),
            SW("AL_APU_START", "APU selector: START", "EVT_OH_LIGHTS_APU_START", 2),
            Multi("AL_START_OFF", "Engine start switches: OFF", ("EVT_OH_LIGHTS_L_ENGINE_START", 1), ("EVT_OH_LIGHTS_R_ENGINE_START", 1)),
            SW("AL_AB_OFF", "Autobrake: OFF", "EVT_MPM_AUTOBRAKE_SELECTOR", 1),
        }
    };

    // -----------------------------------------------------------------------
    // 13. Shutdown
    // -----------------------------------------------------------------------
    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Engines cutoff, signs/lights off, fuel pumps off, anti-ice off, transponder STBY.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN" },
        Steps = new()
        {
            // BOTH APU generator transfer buttons (the old single-event step left
            // transfer bus 2 on its engine generator until the levers cut it off).
            Multi("SD_APUGEN", "APU generators: ON",
                ("EVT_OH_ELEC_APU_GEN1_SWITCH", 1), ("EVT_OH_ELEC_APU_GEN2_SWITCH", 1)),
            Multi("SD_LEVERS", "Engine start levers: CUTOFF",
                ("EVT_CONTROL_STAND_ENG1_START_LEVER", 0), ("EVT_CONTROL_STAND_ENG2_START_LEVER", 0)),
            WaitForField("SD_ENG_OFF", "Waiting for the engines to spool down", "ENG_StartValve_0", v => v < 0.5, 60),
            SW("SD_BELTS", "Seatbelt signs: OFF", "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", 0),
            Multi("SD_TURNOFF", "Runway turnoff lights: OFF", ("EVT_OH_LIGHTS_L_TURNOFF", 0), ("EVT_OH_LIGHTS_R_TURNOFF", 0)),
            SW("SD_TAXI_OFF", "Taxi light: OFF", "EVT_OH_LIGHTS_TAXI", 0),
            SW("SD_LOGO_OFF", "Logo lights: OFF", "EVT_OH_LIGHTS_LOGO", 0),
            SW("SD_APUBLEED", "APU bleed air: ON", "EVT_OH_BLEED_APU_SWITCH", 1),
            Multi("SD_FUEL_OFF", "Fuel pumps: OFF",
                ("EVT_OH_FUEL_PUMP_1_FORWARD", 0), ("EVT_OH_FUEL_PUMP_2_FORWARD", 0),
                ("EVT_OH_FUEL_PUMP_1_AFT", 0), ("EVT_OH_FUEL_PUMP_2_AFT", 0)),
            Multi("SD_EAI_OFF", "Engine anti-ice: OFF", ("EVT_OH_ICE_ENGINE_ANTIICE_1", 0), ("EVT_OH_ICE_ENGINE_ANTIICE_2", 0)),
            Multi("SD_HYDELEC_OFF", "Electric hydraulic pumps: OFF", ("EVT_OH_HYD_ELEC1", 0), ("EVT_OH_HYD_ELEC2", 0)),
            Multi("SD_HYDENG_OFF", "Engine hydraulic pumps: OFF", ("EVT_OH_HYD_ENG1", 0), ("EVT_OH_HYD_ENG2", 0)),
            Multi("SD_WINHEAT_OFF", "Window heat: OFF",
                ("EVT_OH_ICE_WINDOW_HEAT_1", 0), ("EVT_OH_ICE_WINDOW_HEAT_2", 0),
                ("EVT_OH_ICE_WINDOW_HEAT_3", 0), ("EVT_OH_ICE_WINDOW_HEAT_4", 0)),
            SW("SD_XPDR", "Transponder: STBY", "EVT_TCAS_MODE", 0),
        }
    };

    // -----------------------------------------------------------------------
    // 14. Secure
    // -----------------------------------------------------------------------
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Secure",
        Description = "IRS off, emergency exit off, window heat off, packs off.",
        RelatedChecklistGroupIds = new[] { "SECURE" },
        Steps = new()
        {
            Multi("SE_IRS_OFF", "IRS mode selectors: OFF", ("EVT_IRU_MSU_LEFT", 0), ("EVT_IRU_MSU_RIGHT", 0)),
            SW("SE_EMER_OFF", "Emergency exit lights: OFF", "EVT_OH_EMER_EXIT_LIGHT_SWITCH", 0),
            Multi("SE_WINHEAT_OFF", "Window heat: OFF",
                ("EVT_OH_ICE_WINDOW_HEAT_1", 0), ("EVT_OH_ICE_WINDOW_HEAT_2", 0),
                ("EVT_OH_ICE_WINDOW_HEAT_3", 0), ("EVT_OH_ICE_WINDOW_HEAT_4", 0)),
            Multi("SE_PACKS_OFF", "Packs: OFF", ("EVT_OH_BLEED_PACK_L_SWITCH", 0), ("EVT_OH_BLEED_PACK_R_SWITCH", 0)),
        }
    };

    // -----------------------------------------------------------------------
    // Step builder helpers
    // -----------------------------------------------------------------------

    private static Step SW(string id, string label, string eventName, int? target,
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

    // SetSwitch whose target resolves at DISPATCH time from evaluator state — for
    // SimBrief-derived values unknown when these static definitions are built. A null
    // provider result quietly skips the step (see FlowStep.TargetValueProvider).
    private static Step DynSW(string id, string label, string eventName,
        Func<AircraftStateEvaluator, int?> provider,
        Func<AircraftStateEvaluator, bool>? skipWhen = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValueProvider = provider,
        SkipCondition = skipWhen,
        PostActionDelayMs = 350,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    // FD / AT Arm are mouse-flag TOGGLES (no absolute target), so firing one while the
    // switch is already in the desired state flips it the wrong way. The skip predicate
    // is required so the step is no-op'd when already correct — the same guard the panel
    // (HandleUIVariableSet) and the checklists (SetFDLeft(target, state)) already apply.
    private static Step MouseFlag(string id, string label, string eventName,
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

    private static Step Momentary(string id, string label, string eventName, string? checklistItemId = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        IsMomentary = true,
        CompletesChecklistItemId = checklistItemId,
        PostActionDelayMs = 350,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Multi(string id, string label, params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),
        PostActionDelayMs = 400,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Wait(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,
        PostActionDelayMs = 0,
    };

    private static Step WalkAround(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WalkAround,
        WaitSeconds = seconds,
        PostActionDelayMs = 0,
    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
        PostActionDelayMs = 0,
    };

    // reminderText: what "Captain action required: …" speaks (defaults to label). A
    // separate short label matters when the step can be SKIPPED — the skip path reads
    // "Already set: {label}", where an imperative sentence would compose badly.
    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,
        PostActionDelayMs = 200,
    };

    private static Step Skip(Step step, Func<AircraftStateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }
}
