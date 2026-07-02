using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.PMDG737;

using Item = Models.ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator>;
using Group = Models.ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator>;
using Act = System.Action<AircraftActionExecutor, AircraftStateEvaluator>;

/// <summary>
/// Data-driven PMDG 737 NG3 First-Officer checklist definitions — the same two-layer
/// structure as the 777: auto-detect "state" groups (mirroring 1:1 what each flow sets,
/// with CheckActions that fire the SAME dispatch the flow uses when ticked) plus
/// challenge-response readback checklists. State fields are verified against
/// <c>PMDGNG3DataStruct.cs</c>.
///
/// Every auto-detect item is <see cref="RevertBehavior.RevertToState"/> (live state
/// mirror). StayComplete was abandoned for state groups (2026-07): items whose target
/// state coincidentally matched an EARLIER phase (isolation valve AUTO at cold-and-dark,
/// APU OFF before it was ever started, the whole Shutdown/Secure set at session start)
/// latched checked and then showed complete while the switch was NOT in the stated
/// position — the reported "falsely checked" bug. A short grace window in
/// ChecklistManager keeps a fresh manual tick from reverting before its frame-spaced
/// writes land.
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
        BuildBeforeTakeoff(),
        BuildBeforeTakeoffChecklist(),
        BuildAfterTakeoff(),
        BuildAfterTakeoffChecklist(),
        BuildDescent(),
        BuildDescentChecklist(),
        BuildApproach(),
        BuildApproachChecklist(),
        BuildLanding(),
        BuildLandingChecklist(),
        BuildAfterLanding(),
        BuildShutdown(),
        BuildShutdownChecklist(),
        BuildSecure(),
        BuildSecureChecklist(),
        BuildElectricalPowerDown(),
    };

    // =======================================================================
    // State groups (auto-detect; CheckAction fires the switch, same dispatch
    // as the matching flow step). Item order mirrors the flow's step order.
    // =======================================================================

    private static Group BuildElectricalPowerUp() => new()
    {
        Id = "ELEC_POWER_UP", Name = "Electrical Power Up",
        Items = new()
        {
            Auto("EPU_BATTERY", "ELEC_POWER_UP", "Battery: ON", "ELEC_BatSelector", v => v > 0.5,
                (e, _) => e.SetBattery(1)),
            Auto("EPU_STBY", "ELEC_POWER_UP", "Standby power: AUTO", "ELEC_StandbyPowerSelector", v => v > 1.5,
                (e, _) => e.SetStandbyPower(2)),
            // FO_GPU_ON = GRD POWER AVAILABLE annun + ground-service buses hot. The raw
            // ELEC_GrdPwrSw struct bool LIES (reads TRUE with no GPU at the stand —
            // live-verified 2026-07-02), which used to false-tick this item.
            Auto("EPU_GPU", "ELEC_POWER_UP", "Ground power: ON", "FO_GPU_ON", v => v > 0.5,
                (e, _) => e.SetGroundPower(1)),
            Auto("EPU_IRS", "ELEC_POWER_UP", "IRS mode selectors: NAV", "IRS_ModeSelector_0", v => v > 1.5,
                new[] { "IRS_ModeSelector_1" }, (e, _) => e.SetIrsMode(2)),
            // (The former "IRS aligned" status item was removed 2026-07 — alignment runs in
            // the background and there is nothing for the pilot to DO; it only cluttered the
            // group and blocked its completion for ~10 minutes.)
        }
    };

    private static Group BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = new()
        {
            Auto("PF_YD", "PREFLIGHT", "Yaw damper: ON", "FCTL_YawDamper_Sw", v => v > 0.5, (e, _) => e.SetYawDamper(1)),
            Auto("PF_FUEL_OFF", "PREFLIGHT", "Fuel pumps: OFF", "FUEL_PumpFwdSw_0", v => v < 0.5,
                new[] { "FUEL_PumpFwdSw_1", "FUEL_PumpAftSw_0", "FUEL_PumpAftSw_1", "FUEL_PumpCtrSw_0", "FUEL_PumpCtrSw_1" },
                (e, _) => { e.SetWingFuelPumps(0); e.SetCenterFuelPumps(0); }),
            Auto("PF_EMER", "PREFLIGHT", "Emergency exit lights: ARMED", "LTS_EmerExitSelector", v => v > 0.5, (e, _) => e.SetEmerExitLights(1)),
            Auto("PF_BELTS", "PREFLIGHT", "Seatbelt signs: ON", "COMM_FastenBeltsSelector", v => v > 1.5, (e, _) => e.SetSeatBelts(2)),
            Auto("PF_WINHEAT", "PREFLIGHT", "Window heat: ON", "ICE_WindowHeatSw_0", v => v > 0.5,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, (e, _) => e.SetWindowHeat(1)),
            Auto("PF_PROBE_OFF", "PREFLIGHT", "Probe heat: OFF", "ICE_ProbeHeatSw_0", v => v < 0.5,
                new[] { "ICE_ProbeHeatSw_1" }, (e, _) => e.SetProbeHeat(0)),
            Auto("PF_WAI", "PREFLIGHT", "Wing anti-ice: OFF", "ICE_WingAntiIceSw", v => v < 0.5, (e, _) => e.SetWingAntiIce(0)),
            Auto("PF_EAI_OFF", "PREFLIGHT", "Engine anti-ice: OFF", "ICE_EngAntiIceSw_0", v => v < 0.5,
                new[] { "ICE_EngAntiIceSw_1" }, (e, _) => e.SetEngAntiIce(0)),
            Auto("PF_RECIRC", "PREFLIGHT", "Recirculation fans: AUTO", "AIR_RecircFanSwitch_0", v => v > 0.5,
                new[] { "AIR_RecircFanSwitch_1" }, (e, _) => e.SetRecircFans(1)),
            Auto("PF_PACKS", "PREFLIGHT", "Packs: AUTO", "AIR_PackSwitch_0", v => v > 0.5 && v < 1.5,
                new[] { "AIR_PackSwitch_1" }, (e, _) => e.SetPacks(1)),
            Auto("PF_ISO", "PREFLIGHT", "Isolation valve: OPEN", "AIR_IsolationValveSwitch", v => v > 1.5, (e, _) => e.SetIsolationValve(2)),
            Auto("PF_BLEEDS", "PREFLIGHT", "Engine bleeds: ON", "AIR_BleedAirSwitch_0", v => v > 0.5,
                new[] { "AIR_BleedAirSwitch_1" }, (e, _) => e.SetEngBleeds(1)),
            // Auto-ticks when both FLT/LAND ALT windows match the SimBrief plan (synthetic
            // field — see AircraftStateEvaluator); ticking fires both direct-set events,
            // SPACED (two CDA writes must not share a frame). No plan loaded → the action
            // no-ops and it behaves like the old reminder.
            AutoAsync("PF_PRESS", "PREFLIGHT", "Flight and landing altitudes: SET",
                "FO_PRESS_ALTS_MATCH", v => v > 0.5,
                (e, s) => e.SetPressurizationAltitudesAsync(s)),
            Auto("PF_LOGO", "PREFLIGHT", "Logo lights: ON", "LTS_LogoSw", v => v > 0.5, (e, _) => e.SetLogo(1)),
            // FD switches are TOGGLES — the async action presses only the side(s) whose
            // current state differs from ON (same guard as the flow's PF_FD1/PF_FD2 steps).
            // Was action:null (ticking did nothing — the "FDs don't come on" bug).
            AutoAsync("PF_FD", "PREFLIGHT", "Flight directors: ON", "MCP_FDSw_0", v => v > 0.5,
                new[] { "MCP_FDSw_1" }, (e, s) => e.SetFlightDirectorsAsync(1, s)),
            Auto("PF_AB", "PREFLIGHT", "Autobrake: RTO", "MAIN_AutobrakeSelector", v => v < 0.5, (e, _) => e.SetAutobrake(0)),
            Auto("PF_XPDR", "PREFLIGHT", "Transponder: STBY", "XPDR_ModeSel", v => v < 0.5, (e, _) => e.SetTransponderMode(0)),
            Auto("PF_EFIS_MODE", "PREFLIGHT", "EFIS mode: MAP", "EFIS_ModeSel_0", v => v > 1.5 && v < 2.5, (e, _) => e.SetEFISModeCapt(2)),
            Auto("PF_EFIS_RANGE", "PREFLIGHT", "EFIS range: 40", "EFIS_RangeSel_0", v => v > 2.5 && v < 3.5, (e, _) => e.SetEFISRangeCapt(3)),
            Reminder("PF_ALT", "PREFLIGHT", "Altimeters: SET to local QNH"),
            Reminder("PF_TESTS", "PREFLIGHT", "Perform the overhead and fire tests as required"),
        }
    };

    private static Group BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            Reminder("BS_MCP", "BEFORE_START", "Set MCP airspeed, heading and initial altitude"),
            // APU start must go ON → dwell → momentary START (StartApuAsync); writing START
            // directly never spools the APU up. Auto-detects ON-line from APU_Selector.
            AutoAsync("BS_APU", "BEFORE_START", "APU: ON line", "APU_Selector", v => v > 0.5, (e, _) => e.StartApuAsync()),
            Auto("BS_FUEL", "BEFORE_START", "Fuel pumps: ON", "FUEL_PumpFwdSw_0", v => v > 0.5,
                new[] { "FUEL_PumpFwdSw_1", "FUEL_PumpAftSw_0", "FUEL_PumpAftSw_1" }, (e, _) => e.SetWingFuelPumps(1)),
            Auto("BS_HYD", "BEFORE_START", "Electric hydraulic pumps: ON", "HYD_PumpSw_elec_0", v => v > 0.5,
                new[] { "HYD_PumpSw_elec_1" }, (e, _) => e.SetElecHydPumps(1)),
            Auto("BS_HYDENG", "BEFORE_START", "Engine hydraulic pumps: ON", "HYD_PumpSw_eng_0", v => v > 0.5,
                new[] { "HYD_PumpSw_eng_1" }, (e, _) => e.SetEngHydPumps(1)),
            Auto("BS_APUBLEED", "BEFORE_START", "APU bleed air: ON", "AIR_APUBleedAirSwitch", v => v > 0.5, (e, _) => e.SetApuBleed(1)),
            Auto("BS_ANTICOL", "BEFORE_START", "Anti-collision light: ON", "LTS_AntiCollisionSw", v => v > 0.5, (e, _) => e.SetBeacon(1)),
            Auto("BS_XPDR", "BEFORE_START", "Transponder: TA/RA", "XPDR_ModeSel", v => v > 3.5, (e, _) => e.SetTransponderMode(4)),
            Reminder("BS_GND", "BEFORE_START", "Confirm ground power and chocks removed, doors closed, and taxi clearance"),
        }
    };

    private static Group BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            Auto("ES_PACKS", "ENGINE_START", "Packs: OFF", "AIR_PackSwitch_0", v => v < 0.5,
                new[] { "AIR_PackSwitch_1" }, (e, _) => e.SetPacks(0)),
            // Ticking an engine item RUNS the start (StartEngineAsync: GRD → motor to
            // fuel-intro N2 → start lever IDLE; no-ops on an already-running engine, and
            // never introduces fuel if N2 fails to build). Auto-ticks from real N2
            // (FO_ENG{1,2}_N2, fed by the FO timer), NOT the fuel-valve byte: that byte
            // reads 0 both when running AND cold/unpowered, so the old condition
            // auto-ticked at cold-and-dark. "Run Related Flow" runs the full flow instead.
            AutoAsync("ES_E2", "ENGINE_START", "Engine 2: START", "FO_ENG2_N2",
                v => v >= AircraftStateEvaluator.EngineRunningN2, (e, s) => e.StartEngineAsync(2, s)),
            AutoAsync("ES_E1", "ENGINE_START", "Engine 1: START", "FO_ENG1_N2",
                v => v >= AircraftStateEvaluator.EngineRunningN2, (e, s) => e.StartEngineAsync(1, s)),
        }
    };

    private static Group BuildBeforeTaxi() => new()
    {
        Id = "BEFORE_TAXI", Name = "Before Taxi",
        Items = new()
        {
            // After-start power transfer (folded in from the former After Start flow).
            // Generator state is the locally-tracked ELEC_GenSw (PMDG's raw bool lies);
            // the FO executor now notifies it on every GEN dispatch so this ticks.
            Auto("BT_GEN", "BEFORE_TAXI", "Generators: ON", "ELEC_GenSw_0", v => v > 0.5,
                new[] { "ELEC_GenSw_1" }, (e, _) => e.SetGenerators(1)),
            Auto("BT_APUBLEED_OFF", "BEFORE_TAXI", "APU bleed air: OFF", "AIR_APUBleedAirSwitch", v => v < 0.5, (e, _) => e.SetApuBleed(0)),
            Auto("BT_APU", "BEFORE_TAXI", "APU: OFF", "APU_Selector", v => v < 0.5, (e, _) => e.SetApuSelector(0)),
            Auto("BT_PROBE", "BEFORE_TAXI", "Probe heat: ON", "ICE_ProbeHeatSw_0", v => v > 0.5,
                new[] { "ICE_ProbeHeatSw_1" }, (e, _) => e.SetProbeHeat(1)),
            Auto("BT_PACKS", "BEFORE_TAXI", "Packs: AUTO", "AIR_PackSwitch_0", v => v > 0.5 && v < 1.5,
                new[] { "AIR_PackSwitch_1" }, (e, _) => e.SetPacks(1)),
            Auto("BT_ISO", "BEFORE_TAXI", "Isolation valve: AUTO", "AIR_IsolationValveSwitch", v => v > 0.5 && v < 1.5, (e, _) => e.SetIsolationValve(1)),
            Auto("BT_START", "BEFORE_TAXI", "Engine start switches: CONT", "ENG_StartSelector_0", v => v > 1.5 && v < 2.5,
                new[] { "ENG_StartSelector_1" }, (e, _) => { e.SetEngStartSelector1(2); e.SetEngStartSelector2(2); }),
            Reminder("BT_ANTIICE", "BEFORE_TAXI", "Set engine and wing anti-ice as required for conditions"),
            Auto("BT_TAXI", "BEFORE_TAXI", "Taxi light: ON", "LTS_TaxiSw", v => v > 0.5, (e, _) => e.SetTaxiLights(1)),
            Auto("BT_TURNOFF", "BEFORE_TAXI", "Runway turnoff lights: ON", "LTS_RunwayTurnoffSw_0", v => v > 0.5,
                new[] { "LTS_RunwayTurnoffSw_1" }, (e, _) => e.SetRunwayTurnoff(1)),
            Reminder("BT_FLAPS", "BEFORE_TAXI", "Set the takeoff flaps"),
            ActionManual("BT_LOWERDU", "BEFORE_TAXI", "Lower display unit: SYS", (e, _) => e.SetLowerDUCapt(1)),
            // Presses the six-pack: every active annunciator + master caution latch on and
            // are announced by the app's monitors; the captain resets with Master Caution.
            ActionManual("BT_RECALL", "BEFORE_TAXI", "Recall: checked", (e, _) => e.PressRecall()),
        }
    };

    private static Group BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Items = new()
        {
            Auto("BTKO_LAND", "BEFORE_TAKEOFF", "Landing lights: ON", "LTS_LandingLtRetractableSw_0", v => v > 1.5,
                new[] { "LTS_LandingLtRetractableSw_1" }, (e, _) => e.SetLandingLights(2)),
            Auto("BTKO_STROBE", "BEFORE_TAKEOFF", "Position lights: STROBE & STEADY", "LTS_PositionSw", v => v > 1.5,
                (e, _) => e.SetPositionLights(2)),
            Auto("BTKO_AT", "BEFORE_TAKEOFF", "Autothrottle: ARM", "MCP_ATArmSw", v => v > 0.5, (e, s) => e.SetATArm(1, s)),
            Auto("BTKO_XPDR", "BEFORE_TAKEOFF", "Transponder: TA/RA", "XPDR_ModeSel", v => v > 3.5, (e, _) => e.SetTransponderMode(4)),
            Reminder("BTKO_BRIEF", "BEFORE_TAKEOFF", "Confirm takeoff runway, trim set, cabin crew notified"),
        }
    };

    private static Group BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = new()
        {
            Auto("ATKO_PACKS", "AFTER_TAKEOFF", "Packs: AUTO", "AIR_PackSwitch_0", v => v > 0.5 && v < 1.5,
                new[] { "AIR_PackSwitch_1" }, (e, _) => e.SetPacks(1)),
            Auto("ATKO_START_OFF", "AFTER_TAKEOFF", "Engine start switches: OFF", "ENG_StartSelector_0", v => v > 0.5 && v < 1.5,
                new[] { "ENG_StartSelector_1" },
                (e, _) => { e.SetEngStartSelector1(1); e.SetEngStartSelector2(1); }),
            Auto("ATKO_TURNOFF", "AFTER_TAKEOFF", "Runway turnoff lights: OFF", "LTS_RunwayTurnoffSw_0", v => v < 0.5,
                new[] { "LTS_RunwayTurnoffSw_1" }, (e, _) => e.SetRunwayTurnoff(0)),
            Auto("ATKO_GEAR_OFF", "AFTER_TAKEOFF", "Gear lever: OFF", "MAIN_GearLever", v => v > 0.5 && v < 1.5,
                (e, _) => e.SetGearLever(1)),
            Auto("ATKO_AB_OFF", "AFTER_TAKEOFF", "Autobrake: OFF", "MAIN_AutobrakeSelector", v => v > 0.5 && v < 1.5,
                (e, _) => e.SetAutobrake(1)),
        }
    };

    private static Group BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Items = new()
        {
            ActionManual("DSA_RECALL", "DESCENT", "Recall: checked", (e, _) => e.PressRecall()),
            Auto("DSA_BELTS", "DESCENT", "Seatbelt signs: ON", "COMM_FastenBeltsSelector", v => v > 1.5,
                (e, _) => e.SetSeatBelts(2)),
            Reminder("DSA_AB", "DESCENT", "Set the landing autobrake"),
            Reminder("DSA_ILS", "DESCENT", "Set the ILS frequencies and course"),
            Reminder("DSA_DATA", "DESCENT", "Confirm landing data, VREF and minimums"),
        }
    };

    private static Group BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = new()
        {
            Auto("APA_EFIS_MODE", "APPROACH", "EFIS mode: APP", "EFIS_ModeSel_0", v => v < 0.5, (e, _) => e.SetEFISModeCapt(0)),
            Auto("APA_EFIS_RANGE", "APPROACH", "EFIS range: 20", "EFIS_RangeSel_0", v => v > 1.5 && v < 2.5, (e, _) => e.SetEFISRangeCapt(2)),
            Reminder("APA_ALT", "APPROACH", "Set the altimeters"),
        }
    };

    private static Group BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Items = new()
        {
            Auto("LDA_START", "LANDING", "Engine start switches: CONT", "ENG_StartSelector_0", v => v > 1.5 && v < 2.5,
                new[] { "ENG_StartSelector_1" },
                (e, _) => { e.SetEngStartSelector1(2); e.SetEngStartSelector2(2); }),
            // No speedbrake-lever state field exists in the NG3 CDA struct — action-only.
            ActionManual("LDA_SPDBRK", "LANDING", "Speedbrake: ARMED", (e, _) => e.SetSpeedbrakeArmed()),
            Reminder("LDA_MISSED", "LANDING", "Set the missed approach altitude"),
        }
    };

    private static Group BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_LAND_OFF", "AFTER_LANDING", "Landing lights: RETRACT", "LTS_LandingLtRetractableSw_0", v => v < 0.5,
                new[] { "LTS_LandingLtRetractableSw_1" }, (e, _) => e.SetLandingLights(0)),
            Auto("AL_TURNOFF", "AFTER_LANDING", "Runway turnoff lights: ON", "LTS_RunwayTurnoffSw_0", v => v > 0.5,
                new[] { "LTS_RunwayTurnoffSw_1" }, (e, _) => e.SetRunwayTurnoff(1)),
            Auto("AL_TAXI", "AFTER_LANDING", "Taxi light: ON", "LTS_TaxiSw", v => v > 0.5, (e, _) => e.SetTaxiLights(1)),
            Auto("AL_STROBE", "AFTER_LANDING", "Position lights: STEADY", "LTS_PositionSw", v => v < 0.5,
                (e, _) => e.SetPositionLights(0)),
            Auto("AL_EAI", "AFTER_LANDING", "Engine anti-ice: OFF", "ICE_EngAntiIceSw_0", v => v < 0.5,
                new[] { "ICE_EngAntiIceSw_1" }, (e, _) => e.SetEngAntiIce(0)),
            Auto("AL_WAI", "AFTER_LANDING", "Wing anti-ice: OFF", "ICE_WingAntiIceSw", v => v < 0.5, (e, _) => e.SetWingAntiIce(0)),
            Auto("AL_PROBE", "AFTER_LANDING", "Probe heat: OFF", "ICE_ProbeHeatSw_0", v => v < 0.5,
                new[] { "ICE_ProbeHeatSw_1" }, (e, _) => e.SetProbeHeat(0)),
            Auto("AL_APU", "AFTER_LANDING", "APU: ON", "APU_Selector", v => v > 0.5, (e, _) => e.SetApuSelector(1)),
            Auto("AL_START_OFF", "AFTER_LANDING", "Engine start switches: OFF", "ENG_StartSelector_0", v => v > 0.5 && v < 1.5,
                new[] { "ENG_StartSelector_1" },
                (e, _) => { e.SetEngStartSelector1(1); e.SetEngStartSelector2(1); }),
            Auto("AL_AB", "AFTER_LANDING", "Autobrake: OFF", "MAIN_AutobrakeSelector", v => v > 0.5 && v < 1.5,
                (e, _) => e.SetAutobrake(1)),
        }
    };

    private static Group BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            // APU GEN switches are stateless push pairs (PMDG exposes no reliable
            // per-switch state) — action-only, like the panel's two-button design.
            ActionManual("SD_APUGEN", "SHUTDOWN", "APU generators: ON", (e, _) => e.SetApuGenerators(1)),
            Auto("SD_LEVERS", "SHUTDOWN", "Engine start levers: CUTOFF", "FUEL_annunENG_VALVE_CLOSED_0", v => v > 0.5,
                new[] { "FUEL_annunENG_VALVE_CLOSED_1" }, (e, _) => { e.SetFuelControl1(0); e.SetFuelControl2(0); }),
            Auto("SD_BELTS", "SHUTDOWN", "Seatbelt signs: OFF", "COMM_FastenBeltsSelector", v => v < 0.5, (e, _) => e.SetSeatBelts(0)),
            Auto("SD_TURNOFF", "SHUTDOWN", "Runway turnoff lights: OFF", "LTS_RunwayTurnoffSw_0", v => v < 0.5,
                new[] { "LTS_RunwayTurnoffSw_1" }, (e, _) => e.SetRunwayTurnoff(0)),
            Auto("SD_TAXI", "SHUTDOWN", "Taxi light: OFF", "LTS_TaxiSw", v => v < 0.5, (e, _) => e.SetTaxiLights(0)),
            Auto("SD_LOGO", "SHUTDOWN", "Logo lights: OFF", "LTS_LogoSw", v => v < 0.5, (e, _) => e.SetLogo(0)),
            Auto("SD_APUBLEED", "SHUTDOWN", "APU bleed air: ON", "AIR_APUBleedAirSwitch", v => v > 0.5, (e, _) => e.SetApuBleed(1)),
            Auto("SD_FUEL", "SHUTDOWN", "Fuel pumps: OFF", "FUEL_PumpFwdSw_0", v => v < 0.5,
                new[] { "FUEL_PumpFwdSw_1", "FUEL_PumpAftSw_0", "FUEL_PumpAftSw_1" }, (e, _) => e.SetWingFuelPumps(0)),
            Auto("SD_EAI", "SHUTDOWN", "Engine anti-ice: OFF", "ICE_EngAntiIceSw_0", v => v < 0.5,
                new[] { "ICE_EngAntiIceSw_1" }, (e, _) => e.SetEngAntiIce(0)),
            Auto("SD_HYDELEC", "SHUTDOWN", "Electric hydraulic pumps: OFF", "HYD_PumpSw_elec_0", v => v < 0.5,
                new[] { "HYD_PumpSw_elec_1" }, (e, _) => e.SetElecHydPumps(0)),
            Auto("SD_HYDENG", "SHUTDOWN", "Engine hydraulic pumps: OFF", "HYD_PumpSw_eng_0", v => v < 0.5,
                new[] { "HYD_PumpSw_eng_1" }, (e, _) => e.SetEngHydPumps(0)),
            Auto("SD_WINHEAT", "SHUTDOWN", "Window heat: OFF", "ICE_WindowHeatSw_0", v => v < 0.5,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, (e, _) => e.SetWindowHeat(0)),
            Auto("SD_XPDR", "SHUTDOWN", "Transponder: STBY", "XPDR_ModeSel", v => v < 0.5, (e, _) => e.SetTransponderMode(0)),
        }
    };

    private static Group BuildSecure() => new()
    {
        Id = "SECURE", Name = "Secure",
        Items = new()
        {
            Auto("SE_IRS", "SECURE", "IRS mode selectors: OFF", "IRS_ModeSelector_0", v => v < 0.5,
                new[] { "IRS_ModeSelector_1" }, (e, _) => e.SetIrsMode(0)),
            Auto("SE_EMER", "SECURE", "Emergency exit lights: OFF", "LTS_EmerExitSelector", v => v < 0.5, (e, _) => e.SetEmerExitLights(0)),
            Auto("SE_WINHEAT", "SECURE", "Window heat: OFF", "ICE_WindowHeatSw_0", v => v < 0.5,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, (e, _) => e.SetWindowHeat(0)),
            Auto("SE_PACKS", "SECURE", "Packs: OFF", "AIR_PackSwitch_0", v => v < 0.5,
                new[] { "AIR_PackSwitch_1" }, (e, _) => e.SetPacks(0)),
        }
    };

    private static Group BuildElectricalPowerDown() => new()
    {
        Id = "ELEC_POWER_DOWN", Name = "Electrical Power Down",
        Items = new()
        {
            Auto("EPD_BAT", "ELEC_POWER_DOWN", "Battery: OFF", "ELEC_BatSelector", v => v < 0.5, (e, _) => e.SetBattery(0)),
            Reminder("EPD_PWR", "ELEC_POWER_DOWN", "APU or ground power: OFF (after the 2-minute APU cooldown)"),
        }
    };

    // =======================================================================
    // Readback checklists (challenge-response) — ACTION-FREE by invariant:
    // ticking a readback item never fires a switch (the state group / flow
    // does the work); items auto-tick from live sim state.
    // =======================================================================

    private static Group BuildPreflightChecklist() => new()
    {
        Id = "PREFLIGHT_CL", Name = "Preflight Checklist",
        Items = new()
        {
            Reminder("PFC_OXY", "PREFLIGHT_CL", "Oxygen: TESTED, 100%"),
            Auto("PFC_WINHEAT", "PREFLIGHT_CL", "Window heat: ON", "ICE_WindowHeatSw_0", v => v > 0.5,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, action: null),
            Auto("PFC_PRESS", "PREFLIGHT_CL", "Pressurization mode selector: AUTO",
                "AIR_PressurizationModeSelector", v => v < 0.5, action: null),
            Reminder("PFC_INST", "PREFLIGHT_CL", "Flight instruments: heading and altimeter checked"),
            Auto("PFC_PARK", "PREFLIGHT_CL", "Parking brake: SET", "PED_annunParkingBrake", v => v > 0.5, action: null),
            Auto("PFC_LEVERS", "PREFLIGHT_CL", "Engine start levers: CUTOFF", "FUEL_annunENG_VALVE_CLOSED_0", v => v > 0.5,
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
            Auto("BSC_BELTS", "BEFORE_START_CL", "Passenger signs: ON", "COMM_FastenBeltsSelector", v => v > 1.5, action: null),
            Reminder("BSC_WINDOWS", "BEFORE_START_CL", "Windows: locked"),
            Reminder("BSC_MCP", "BEFORE_START_CL", "MCP: speed, heading and altitude set"),
            Reminder("BSC_SPEEDS", "BEFORE_START_CL", "Takeoff speeds: V1, VR and V2 checked"),
            Reminder("BSC_CDU", "BEFORE_START_CL", "CDU preflight: complete"),
            Reminder("BSC_TRIM", "BEFORE_START_CL", "Rudder and aileron trim: free and zero"),
            Auto("BSC_ANTICOL", "BEFORE_START_CL", "Anti-collision light: ON", "LTS_AntiCollisionSw", v => v > 0.5, action: null),
        }
    };

    private static Group BuildBeforeTaxiChecklist() => new()
    {
        Id = "BEFORE_TAXI_CL", Name = "Before Taxi Checklist",
        Items = new()
        {
            Auto("BTC_GEN", "BEFORE_TAXI_CL", "Generators: ON", "ELEC_GenSw_0", v => v > 0.5, new[] { "ELEC_GenSw_1" }, action: null),
            Auto("BTC_PROBE", "BEFORE_TAXI_CL", "Probe heat: ON", "ICE_ProbeHeatSw_0", v => v > 0.5, new[] { "ICE_ProbeHeatSw_1" }, action: null),
            Reminder("BTC_ANTIICE", "BEFORE_TAXI_CL", "Anti-ice: as required"),
            Auto("BTC_ISO", "BEFORE_TAXI_CL", "Isolation valve: AUTO", "AIR_IsolationValveSwitch", v => v > 0.5 && v < 1.5, action: null),
            Auto("BTC_START", "BEFORE_TAXI_CL", "Engine start switches: CONT", "ENG_StartSelector_0", v => v > 1.5 && v < 2.5, new[] { "ENG_StartSelector_1" }, action: null),
            Reminder("BTC_RECALL", "BEFORE_TAXI_CL", "Recall: checked"),
            Auto("BTC_AB", "BEFORE_TAXI_CL", "Autobrake: RTO", "MAIN_AutobrakeSelector", v => v < 0.5, action: null),
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
            Auto("ATC_BLEEDS", "AFTER_TAKEOFF_CL", "Engine bleeds: ON", "AIR_BleedAirSwitch_0", v => v > 0.5, new[] { "AIR_BleedAirSwitch_1" }, action: null),
            Auto("ATC_PACKS", "AFTER_TAKEOFF_CL", "Packs: AUTO", "AIR_PackSwitch_0", v => v > 0.5 && v < 1.5, new[] { "AIR_PackSwitch_1" }, action: null),
            // "UP and OFF": after takeoff the lever goes to OFF (1) — accept UP(0) OR OFF(1),
            // exclude DOWN(2). (The old v<0.5 only matched UP=0, so it never ticked once the
            // action/flow set the lever to OFF=1.)
            Auto("ATC_GEAR", "AFTER_TAKEOFF_CL", "Landing gear: UP and OFF", "MAIN_GearLever", v => v < 1.5, action: null),
            Reminder("ATC_FLAPS", "AFTER_TAKEOFF_CL", "Flaps: UP, no lights"),
        }
    };

    private static Group BuildDescentChecklist() => new()
    {
        Id = "DESCENT_CL", Name = "Descent Checklist",
        Items = new()
        {
            // Auto-verifies (action-free, per the *_CL invariant): ticks when the LAND ALT
            // window matches the SimBrief destination elevation. No plan → manual tick.
            Auto("DC_PRESS", "DESCENT_CL", "Pressurization: landing altitude set",
                "FO_PRESS_LAND_ALT_MATCH", v => v > 0.5, action: null),
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
            Auto("LDC_START", "LANDING_CL", "Engine start switches: CONT", "ENG_StartSelector_0", v => v > 1.5 && v < 2.5, new[] { "ENG_StartSelector_1" }, action: null),
            Reminder("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED"),
            Auto("LDC_GEAR", "LANDING_CL", "Landing gear: down, three green", "MAIN_GearLever", v => v > 1.5, action: null),
            Reminder("LDC_FLAPS", "LANDING_CL", "Flaps: set, green light"),
        }
    };

    private static Group BuildShutdownChecklist() => new()
    {
        Id = "SHUTDOWN_CL", Name = "Shutdown Checklist",
        Items = new()
        {
            Auto("SDC_FUEL", "SHUTDOWN_CL", "Fuel pumps: OFF", "FUEL_PumpFwdSw_0", v => v < 0.5,
                new[] { "FUEL_PumpFwdSw_1", "FUEL_PumpAftSw_0", "FUEL_PumpAftSw_1" }, action: null),
            Auto("SDC_PROBE", "SHUTDOWN_CL", "Probe heat: OFF", "ICE_ProbeHeatSw_0", v => v < 0.5, new[] { "ICE_ProbeHeatSw_1" }, action: null),
            Reminder("SDC_HYD", "SHUTDOWN_CL", "Hydraulic panel: set"),
            Reminder("SDC_FLAPS", "SHUTDOWN_CL", "Flaps: UP"),
            Auto("SDC_PARK", "SHUTDOWN_CL", "Parking brake: as required", "PED_annunParkingBrake", v => v > 0.5, action: null),
            Auto("SDC_LEVERS", "SHUTDOWN_CL", "Engine start levers: CUTOFF", "FUEL_annunENG_VALVE_CLOSED_0", v => v > 0.5,
                new[] { "FUEL_annunENG_VALVE_CLOSED_1" }, action: null),
        }
    };

    private static Group BuildSecureChecklist() => new()
    {
        Id = "SECURE_CL", Name = "Secure Checklist",
        Items = new()
        {
            Auto("SEC_IRS", "SECURE_CL", "IRS: OFF", "IRS_ModeSelector_0", v => v < 0.5, new[] { "IRS_ModeSelector_1" }, action: null),
            Auto("SEC_EMER", "SECURE_CL", "Emergency exit lights: OFF", "LTS_EmerExitSelector", v => v < 0.5, action: null),
            Auto("SEC_WINHEAT", "SECURE_CL", "Window heat: OFF", "ICE_WindowHeatSw_0", v => v < 0.5,
                new[] { "ICE_WindowHeatSw_1", "ICE_WindowHeatSw_2", "ICE_WindowHeatSw_3" }, action: null),
            Auto("SEC_PACKS", "SECURE_CL", "Packs: OFF", "AIR_PackSwitch_0", v => v < 0.5, new[] { "AIR_PackSwitch_1" }, action: null),
        }
    };

    // =======================================================================
    // Helpers (mirror the 777; CheckActions are async via AsCheckAction).
    // Every auto-detect item is RevertToState — see the class doc comment for
    // why StayComplete was abandoned (false latching from earlier phases).
    // =======================================================================

    private static Func<AircraftActionExecutor, AircraftStateEvaluator, Task>? AsCheckAction(Act? action)
        => action == null ? null : (e, s) => { action(e, s); return Task.CompletedTask; };

    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition,
        string[]? additionalFields, Act? action) => new()
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

    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition, Act? action) =>
        Auto(id, groupId, label, field, condition, null, action);

    // Auto-detect item whose manual-tick CheckAction is ASYNC (e.g. a spaced StartApuAsync
    // or a full StartEngineAsync). ChecklistManager.ToggleItem fires the Task and lets it
    // run; auto-detection still works (with the manual-tick revert grace).
    private static Item AutoAsync(string id, string groupId, string label,
        string field, Func<double, bool> condition,
        Func<AircraftActionExecutor, AircraftStateEvaluator, Task> action) =>
        AutoAsync(id, groupId, label, field, condition, null, action);

    private static Item AutoAsync(string id, string groupId, string label,
        string field, Func<double, bool> condition, string[]? additionalFields,
        Func<AircraftActionExecutor, AircraftStateEvaluator, Task> action) => new()
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
        CheckAction = action,
    };

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
