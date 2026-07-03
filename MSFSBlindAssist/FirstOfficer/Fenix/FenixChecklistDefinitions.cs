using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

using Item = Models.ChecklistItem<FenixActionExecutor, FenixStateEvaluator>;
using Group = Models.ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>;
using CheckFn = System.Func<FenixActionExecutor, FenixStateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// Data-driven Fenix A320 First-Officer checklist definitions — the two-layer structure
/// shared with the PMDG profiles: auto-detect STATE groups (mirroring 1:1 what each flow
/// sets; ticking an item fires the SAME write the flow uses) plus action-free readback
/// (*_CL) checklists in JD's Guide wording.
///
/// Every state item is RevertToState (live mirror). State reads come from the SimConnect
/// L:var cache — OnRequest vars are polled by the FO window via OnRequestPollFields, and
/// an uncached var reads NaN (indeterminate: no tick, no revert).
/// </summary>
public static class FenixChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        BuildElectricalPowerUp(),
        BuildPreflight(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildBeforeTakeoff(),
        BuildAfterTakeoff(),
        BuildDescent(),
        BuildApproach(),
        BuildAfterLanding(),
        BuildShutdown(),
        BuildSecure(),
    };

    // -----------------------------------------------------------------------
    // State groups (mirror the flows; IDs match CompletesChecklistItemId)
    // -----------------------------------------------------------------------

    private static Group BuildElectricalPowerUp() => new()
    {
        Id = "ELEC_POWER_UP", Name = "Electrical Power Up",
        Items = new()
        {
            Auto("EPU_BAT1", "ELEC_POWER_UP", "Battery 1: ON", "S_OH_ELEC_BAT1", v => v > 0.5,
                (e, _) => e.Set("S_OH_ELEC_BAT1", 1)),
            Auto("EPU_BAT2", "ELEC_POWER_UP", "Battery 2: ON", "S_OH_ELEC_BAT2", v => v > 0.5,
                (e, _) => e.Set("S_OH_ELEC_BAT2", 1)),
            // Momentary pushbutton; the blue ON light is the readable on-bus state.
            Auto("EPU_EXTPWR", "ELEC_POWER_UP", "External power: ON (if available)",
                "I_OH_ELEC_EXT_PWR_L", v => v > 0.5,
                (e, _) => e.Pulse("S_OH_ELEC_EXT_PWR")),
            Auto("EPU_NAVLOGO", "ELEC_POWER_UP", "Nav and logo lights: ON",
                "S_OH_EXT_LT_NAV_LOGO", v => v > 0.5,
                (e, _) => e.Set("S_OH_EXT_LT_NAV_LOGO", 1)),
        }
    };

    private static Group BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = new()
        {
            Auto("PF_GNDCTL", "PREFLIGHT", "Recorder ground control: ON",
                "I_OH_RCRD_GND_CTL_L", v => v > 0.5, (e, _) => e.Pulse("S_OH_RCRD_GND_CTL")),
            ActionManual("PF_CVR", "PREFLIGHT", "CVR test (listen for the test tone)",
                (e, _) => e.Set("S_OH_RCRD_TEST", 1)),
            Auto("PF_IRS", "PREFLIGHT", "IRS 1, 2 and 3: NAV",
                "S_OH_NAV_IR1_MODE", v => Math.Abs(v - 1) < 0.5,
                new[] { "S_OH_NAV_IR2_MODE", "S_OH_NAV_IR3_MODE" },
                async (e, _) =>
                {
                    await e.Set("S_OH_NAV_IR1_MODE", 1);
                    await e.Set("S_OH_NAV_IR2_MODE", 1);
                    await e.Set("S_OH_NAV_IR3_MODE", 1);
                }),
            Auto("PF_OXY", "PREFLIGHT", "Crew oxygen supply: ON",
                "S_OH_OXYGEN_CREW_OXYGEN", v => v > 0.5, (e, _) => e.Set("S_OH_OXYGEN_CREW_OXYGEN", 1)),
            ActionManual("PF_FIRE_APU", "PREFLIGHT", "APU fire test", (e, _) => e.FireTest("S_OH_FIRE_APU_TEST")),
            ActionManual("PF_FIRE_ENG1", "PREFLIGHT", "Engine 1 fire test", (e, _) => e.FireTest("S_OH_FIRE_ENG1_TEST")),
            ActionManual("PF_FIRE_ENG2", "PREFLIGHT", "Engine 2 fire test", (e, _) => e.FireTest("S_OH_FIRE_ENG2_TEST")),
            Auto("PF_PACKS", "PREFLIGHT", "Packs 1 and 2: ON",
                "S_OH_PNEUMATIC_PACK_1", v => v > 0.5, new[] { "S_OH_PNEUMATIC_PACK_2" },
                async (e, _) =>
                {
                    await e.Set("S_OH_PNEUMATIC_PACK_1", 1);
                    await e.Set("S_OH_PNEUMATIC_PACK_2", 1);
                }),
            Auto("PF_XBLEED", "PREFLIGHT", "Crossbleed: AUTO",
                "S_OH_PNEUMATIC_XBLEED_SELECTOR", v => Math.Abs(v - 1) < 0.5,
                (e, _) => e.Set("S_OH_PNEUMATIC_XBLEED_SELECTOR", 1)),
            Auto("PF_PACKFLOW", "PREFLIGHT", "Pack flow: NORMAL",
                "S_OH_PNEUMATIC_PACK_FLOW", v => Math.Abs(v - 1) < 0.5,
                (e, _) => e.Set("S_OH_PNEUMATIC_PACK_FLOW", 1)),
            Auto("PF_HOTAIR", "PREFLIGHT", "Hot air: ON",
                "S_OH_PNEUMATIC_HOT_AIR", v => v > 0.5, (e, _) => e.Set("S_OH_PNEUMATIC_HOT_AIR", 1)),
            Auto("PF_PRESSMODE", "PREFLIGHT", "Cabin pressure mode: AUTO",
                "S_OH_PNEUMATIC_PRESS_MODE", v => Math.Abs(v - 1) < 0.5,
                (e, _) => e.Set("S_OH_PNEUMATIC_PRESS_MODE", 1)),
            Auto("PF_STROBE", "PREFLIGHT", "Strobes: AUTO",
                "S_OH_EXT_LT_STROBE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_OH_EXT_LT_STROBE", 1)),
            Auto("PF_WING_LT", "PREFLIGHT", "Wing lights: OFF",
                "S_OH_EXT_LT_WING", v => v < 0.5, (e, _) => e.Set("S_OH_EXT_LT_WING", 0)),
            Auto("PF_NOSMOKE", "PREFLIGHT", "No smoking: AUTO",
                "S_OH_SIGNS_SMOKING", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_OH_SIGNS_SMOKING", 1)),
            Auto("PF_EMEREXIT", "PREFLIGHT", "Emergency exit lights: ARM",
                "S_OH_INT_LT_EMER", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_OH_INT_LT_EMER", 1)),
            Auto("PF_ALTRPTG", "PREFLIGHT", "Altitude reporting: ON",
                "S_XPDR_ALTREPORTING", v => v > 0.5, (e, _) => e.Set("S_XPDR_ALTREPORTING", 1)),
            Auto("PF_TCASTRAFFIC", "PREFLIGHT", "TCAS traffic: ALL",
                "S_TCAS_RANGE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_TCAS_RANGE", 1)),
            Reminder("PF_BARO", "PREFLIGHT", "Set QNH on both altimeters and the standby altimeter"),
            Reminder("PF_FCUALT", "PREFLIGHT", "Set the initial cleared altitude on the FCU"),
            Reminder("PF_SQUAWK", "PREFLIGHT", "Set the squawk code"),
            Reminder("PF_EFB", "PREFLIGHT", "EFB setup — import SimBrief, load fuel and payload"),
            Reminder("PF_MCDU", "PREFLIGHT", "MCDU setup — INIT, flight plan, and PERF pages"),
        }
    };

    private static Group BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            // Master ON → dwell → START pulse; the AVAIL light is the running state.
            AutoAsync("BS_APU", "BEFORE_START", "APU: ON and available",
                "I_OH_ELEC_APU_START_U", v => v > 0.5, (e, _) => e.StartApuAsync()),
            Auto("BS_APUBLEED", "BEFORE_START", "APU bleed: ON",
                "S_OH_PNEUMATIC_APU_BLEED", v => v > 0.5, (e, _) => e.Set("S_OH_PNEUMATIC_APU_BLEED", 1)),
            Auto("BS_FUELPUMPS", "BEFORE_START", "Fuel pumps: ALL ON",
                "S_OH_FUEL_LEFT_1", v => v > 0.5,
                new[] { "S_OH_FUEL_LEFT_2", "S_OH_FUEL_CENTER_1", "S_OH_FUEL_CENTER_2",
                        "S_OH_FUEL_RIGHT_1", "S_OH_FUEL_RIGHT_2" },
                async (e, _) =>
                {
                    await e.Set("S_OH_FUEL_LEFT_1", 1);
                    await e.Set("S_OH_FUEL_LEFT_2", 1);
                    await e.Set("S_OH_FUEL_CENTER_1", 1);
                    await e.Set("S_OH_FUEL_CENTER_2", 1);
                    await e.Set("S_OH_FUEL_RIGHT_1", 1);
                    await e.Set("S_OH_FUEL_RIGHT_2", 1);
                }),
            Auto("BS_EXTPWR_OFF", "BEFORE_START", "External power: OFF",
                "I_OH_ELEC_EXT_PWR_L", v => v < 0.5, (e, _) => e.Pulse("S_OH_ELEC_EXT_PWR")),
            Auto("BS_SEATBELTS", "BEFORE_START", "Seatbelt signs: ON",
                "S_OH_SIGNS", v => v > 0.5, (e, _) => e.Set("S_OH_SIGNS", 1)),
            Auto("BS_BEACON", "BEFORE_START", "Beacon: ON",
                "S_OH_EXT_LT_BEACON", v => v > 0.5, (e, _) => e.Set("S_OH_EXT_LT_BEACON", 1)),
            ActionManual("BS_FCUSPD", "BEFORE_START", "FCU speed: managed",
                (e, _) => e.PushFcuManaged("S_FCU_SPEED")),
            ActionManual("BS_FCUHDG", "BEFORE_START", "FCU heading: managed",
                (e, _) => e.PushFcuManaged("S_FCU_HEADING")),
            Reminder("BS_DOORS", "BEFORE_START", "Close doors and remove ground services on the EFB"),
            Reminder("BS_THRLEVERS", "BEFORE_START", "Confirm thrust levers idle"),
            Reminder("BS_CLEARANCE", "BEFORE_START", "Obtain pushback and start clearance"),
        }
    };

    private static Group BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            Auto("ES_MODE", "ENGINE_START", "Engine mode selector: IGN START",
                "S_ENG_MODE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("S_ENG_MODE", 2)),
            Auto("ES_ENG2", "ENGINE_START", "Engine 2 master: ON",
                "S_ENG_MASTER_2", v => v > 0.5, (e, _) => e.Set("S_ENG_MASTER_2", 1)),
            Auto("ES_ENG2_RUN", "ENGINE_START", "Engine 2: running",
                "FO_ENG2_N2", v => v >= FenixStateEvaluator.EngineRunningN2, action: null),
            Auto("ES_ENG1", "ENGINE_START", "Engine 1 master: ON",
                "S_ENG_MASTER_1", v => v > 0.5, (e, _) => e.Set("S_ENG_MASTER_1", 1)),
            Auto("ES_ENG1_RUN", "ENGINE_START", "Engine 1: running",
                "FO_ENG1_N2", v => v >= FenixStateEvaluator.EngineRunningN2, action: null),
        }
    };

    private static Group BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Items = new()
        {
            Auto("AS_MODE_NORM", "AFTER_START", "Engine mode selector: NORM",
                "S_ENG_MODE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_ENG_MODE", 1)),
            Auto("AS_APUBLEED_OFF", "AFTER_START", "APU bleed: OFF",
                "S_OH_PNEUMATIC_APU_BLEED", v => v < 0.5, (e, _) => e.Set("S_OH_PNEUMATIC_APU_BLEED", 0)),
            Auto("AS_APUMASTER_OFF", "AFTER_START", "APU master: OFF",
                "S_OH_ELEC_APU_MASTER", v => v < 0.5, (e, _) => e.Set("S_OH_ELEC_APU_MASTER", 0)),
            Auto("AS_SPOILERS_ARM", "AFTER_START", "Ground spoilers: ARMED",
                "A_FC_SPEEDBRAKE", v => v < 0.5, (e, _) => e.Set("A_FC_SPEEDBRAKE", 0)),
            ActionManual("AS_RUDDERTRIM", "AFTER_START", "Rudder trim: RESET",
                (e, _) => e.Pulse("S_FC_RUDDER_TRIM_RESET")),
            // Auto-detects "flaps not up" (S_FC_FLAPS is Continuous — always cached);
            // ticking sets the SimBrief flaps when loaded, else announces nothing (no-op).
            Auto("AS_FLAPS", "AFTER_START", "Flaps: takeoff setting",
                "S_FC_FLAPS", v => v is >= 0.5 and <= 3.5,
                async (e, s) =>
                {
                    int f = s.TakeoffFlapsLeverIndex();
                    if (f >= 1) await e.Set("S_FC_FLAPS", f);
                }),
            Auto("AS_NOSE_TAXI", "AFTER_START", "Nose light: TAXI",
                "S_OH_EXT_LT_NOSE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_OH_EXT_LT_NOSE", 1)),
            Reminder("AS_ANTIICE", "AFTER_START", "Set engine and wing anti-ice as required"),
            Reminder("AS_PITCHTRIM", "AFTER_START", "Set pitch trim per the loadsheet"),
        }
    };

    private static Group BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Items = new()
        {
            Auto("BT_AUTOBRAKE", "BEFORE_TAKEOFF", "Autobrake: MAX",
                "I_MIP_AUTOBRAKE_MAX_L", v => v > 0.5, (e, _) => e.Pulse("S_MIP_AUTOBRAKE_MAX")),
            Auto("BT_WXR", "BEFORE_TAKEOFF", "Weather radar: ON",                      // [RADAR]
                "S_WR_SYS", v => v < 0.5, (e, _) => e.Set("S_WR_SYS", 0)),             // [RADAR]
            Auto("BT_PWS", "BEFORE_TAKEOFF", "Predictive windshear: AUTO",             // [RADAR]
                "S_WR_PRED_WS", v => v > 0.5, (e, _) => e.Set("S_WR_PRED_WS", 1)),     // [RADAR]
            Auto("BT_TCAS", "BEFORE_TAKEOFF", "TCAS: TA/RA",
                "S_XPDR_MODE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("S_XPDR_MODE", 2)),
            Auto("BT_XPDRAUTO", "BEFORE_TAKEOFF", "Transponder: AUTO",
                "S_XPDR_OPERATION", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_XPDR_OPERATION", 1)),
            Reminder("BT_FCCHECK", "BEFORE_TAKEOFF", "Perform the flight control check"),
            ActionManual("BT_CONFIG", "BEFORE_TAKEOFF", "Takeoff config test",
                (e, _) => e.Set("S_ECAM_TO_CONFIG", 1)),
            Auto("BT_TURNOFF", "BEFORE_TAKEOFF", "Runway turn-off lights: ON",
                "S_OH_EXT_LT_RWY_TURNOFF", v => v > 0.5, (e, _) => e.Set("S_OH_EXT_LT_RWY_TURNOFF", 1)),
            Auto("BT_LANDING_LT", "BEFORE_TAKEOFF", "Landing lights: ON",
                "S_OH_EXT_LT_LANDING_L", v => Math.Abs(v - 2) < 0.5, new[] { "S_OH_EXT_LT_LANDING_R" },
                (e, _) => e.SetLandingLights(2)),
            Auto("BT_NOSE_TO", "BEFORE_TAKEOFF", "Nose light: TAKEOFF",
                "S_OH_EXT_LT_NOSE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.SetNoseLight(2)),
            Auto("BT_STROBE", "BEFORE_TAKEOFF", "Strobes: ON",
                "S_OH_EXT_LT_STROBE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("S_OH_EXT_LT_STROBE", 2)),
            Reminder("BT_CABIN", "BEFORE_TAKEOFF", "Advise the cabin crew and obtain takeoff clearance"),
        }
    };

    private static Group BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = new()
        {
            Auto("AT_SPOILERS_DISARM", "AFTER_TAKEOFF", "Ground spoilers: DISARM",
                "A_FC_SPEEDBRAKE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A_FC_SPEEDBRAKE", 1)),
            Auto("AT_PACKS", "AFTER_TAKEOFF", "Packs 1 and 2: ON",
                "S_OH_PNEUMATIC_PACK_1", v => v > 0.5, new[] { "S_OH_PNEUMATIC_PACK_2" },
                async (e, _) =>
                {
                    await e.Set("S_OH_PNEUMATIC_PACK_1", 1);
                    await e.Set("S_OH_PNEUMATIC_PACK_2", 1);
                }),
            Auto("AT_TURNOFF_OFF", "AFTER_TAKEOFF", "Runway turn-off lights: OFF",
                "S_OH_EXT_LT_RWY_TURNOFF", v => v < 0.5, (e, _) => e.Set("S_OH_EXT_LT_RWY_TURNOFF", 0)),
        }
    };

    private static Group BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Items = new()
        {
            Auto("DC_AUTOBRAKE", "DESCENT", "Autobrake: MED",
                "I_MIP_AUTOBRAKE_MED_L", v => v > 0.5, (e, _) => e.Pulse("S_MIP_AUTOBRAKE_MED")),
            Auto("DC_SEATBELTS", "DESCENT", "Seatbelt signs: ON",
                "S_OH_SIGNS", v => v > 0.5, (e, _) => e.Set("S_OH_SIGNS", 1)),
            Reminder("DC_ARRPERF", "DESCENT", "Calculate arrival performance on the EFB"),
            Reminder("DC_MCDU", "DESCENT", "Complete the MCDU approach page and minimums before top of descent"),
            Reminder("DC_LDGELEV", "DESCENT", "Check landing elevation auto on the ECAM"),
        }
    };

    private static Group BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = new()
        {
            Auto("AP_LS1", "APPROACH", "LS captain: ON",
                "I_FCU_EFIS1_LS", v => v > 0.5, (e, _) => e.Pulse("S_FCU_EFIS1_LS_PRESS")),
            Auto("AP_LS2", "APPROACH", "LS first officer: ON",
                "I_FCU_EFIS2_LS", v => v > 0.5, (e, _) => e.Pulse("S_FCU_EFIS2_LS_PRESS")),
            Reminder("AP_MINIMUMS", "APPROACH", "Check minimums set on the MCDU approach page"),
            Reminder("AP_BARO", "APPROACH", "Confirm QNH set at transition level"),
            Reminder("AP_ENGMODE", "APPROACH", "Set engine mode selector as required"),
        }
    };

    private static Group BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_SPOILERS", "AFTER_LANDING", "Ground spoilers: DISARM",
                "A_FC_SPEEDBRAKE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A_FC_SPEEDBRAKE", 1)),
            Auto("AL_FLAPS_UP", "AFTER_LANDING", "Flaps: UP",
                "S_FC_FLAPS", v => v < 0.5, (e, _) => e.Set("S_FC_FLAPS", 0)),
            Auto("AL_WXR_OFF", "AFTER_LANDING", "Weather radar: OFF",                  // [RADAR]
                "S_WR_SYS", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_WR_SYS", 1)), // [RADAR]
            Auto("AL_PWS_OFF", "AFTER_LANDING", "Predictive windshear: OFF",           // [RADAR]
                "S_WR_PRED_WS", v => v < 0.5, (e, _) => e.Set("S_WR_PRED_WS", 0)),     // [RADAR]
            Auto("AL_XPDR_STBY", "AFTER_LANDING", "Transponder: STANDBY",
                "S_XPDR_OPERATION", v => v < 0.5, (e, _) => e.Set("S_XPDR_OPERATION", 0)),
            Auto("AL_STROBE_AUTO", "AFTER_LANDING", "Strobes: AUTO",
                "S_OH_EXT_LT_STROBE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_OH_EXT_LT_STROBE", 1)),
            Auto("AL_LANDING_OFF", "AFTER_LANDING", "Landing lights: OFF",
                "S_OH_EXT_LT_LANDING_L", v => Math.Abs(v - 1) < 0.5, new[] { "S_OH_EXT_LT_LANDING_R" },
                (e, _) => e.SetLandingLights(1)),
            Auto("AL_NOSE_TAXI", "AFTER_LANDING", "Nose light: TAXI",
                "S_OH_EXT_LT_NOSE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.SetNoseLight(1)),
            AutoAsync("AL_APU", "AFTER_LANDING", "APU: ON and available",
                "I_OH_ELEC_APU_START_U", v => v > 0.5, (e, _) => e.StartApuAsync()),
            Auto("AL_ANTIICE_OFF", "AFTER_LANDING", "Engine and wing anti-ice: OFF",
                "S_OH_PNEUMATIC_ENG1_ANTI_ICE", v => v < 0.5,
                new[] { "S_OH_PNEUMATIC_ENG2_ANTI_ICE", "S_OH_PNEUMATIC_WING_ANTI_ICE" },
                async (e, _) =>
                {
                    await e.Set("S_OH_PNEUMATIC_ENG1_ANTI_ICE", 0);
                    await e.Set("S_OH_PNEUMATIC_ENG2_ANTI_ICE", 0);
                    await e.Set("S_OH_PNEUMATIC_WING_ANTI_ICE", 0);
                }),
        }
    };

    private static Group BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            Auto("SD_PARKBRAKE", "SHUTDOWN", "Parking brake: ON",
                "S_MIP_PARKING_BRAKE", v => v > 0.5, (e, _) => e.Set("S_MIP_PARKING_BRAKE", 1)),
            Auto("SD_APUBLEED_ON", "SHUTDOWN", "APU bleed: ON",
                "S_OH_PNEUMATIC_APU_BLEED", v => v > 0.5, (e, _) => e.Set("S_OH_PNEUMATIC_APU_BLEED", 1)),
            Auto("SD_ENG1_OFF", "SHUTDOWN", "Engine 1 master: OFF",
                "S_ENG_MASTER_1", v => v < 0.5, (e, _) => e.Set("S_ENG_MASTER_1", 0)),
            Auto("SD_ENG2_OFF", "SHUTDOWN", "Engine 2 master: OFF",
                "S_ENG_MASTER_2", v => v < 0.5, (e, _) => e.Set("S_ENG_MASTER_2", 0)),
            Auto("SD_SEATBELTS_OFF", "SHUTDOWN", "Seatbelt signs: OFF",
                "S_OH_SIGNS", v => v < 0.5, (e, _) => e.Set("S_OH_SIGNS", 0)),
            Auto("SD_BEACON_OFF", "SHUTDOWN", "Beacon: OFF",
                "S_OH_EXT_LT_BEACON", v => v < 0.5, (e, _) => e.Set("S_OH_EXT_LT_BEACON", 0)),
            Auto("SD_FUELPUMPS_OFF", "SHUTDOWN", "Fuel pumps: ALL OFF",
                "S_OH_FUEL_LEFT_1", v => v < 0.5,
                new[] { "S_OH_FUEL_LEFT_2", "S_OH_FUEL_CENTER_1", "S_OH_FUEL_CENTER_2",
                        "S_OH_FUEL_RIGHT_1", "S_OH_FUEL_RIGHT_2" },
                async (e, _) =>
                {
                    await e.Set("S_OH_FUEL_LEFT_1", 0);
                    await e.Set("S_OH_FUEL_LEFT_2", 0);
                    await e.Set("S_OH_FUEL_CENTER_1", 0);
                    await e.Set("S_OH_FUEL_CENTER_2", 0);
                    await e.Set("S_OH_FUEL_RIGHT_1", 0);
                    await e.Set("S_OH_FUEL_RIGHT_2", 0);
                }),
            Auto("SD_NOSE_OFF", "SHUTDOWN", "Nose light: OFF",
                "S_OH_EXT_LT_NOSE", v => v < 0.5, (e, _) => e.SetNoseLight(0)),
            Auto("SD_TURNOFF_OFF", "SHUTDOWN", "Runway turn-off lights: OFF",
                "S_OH_EXT_LT_RWY_TURNOFF", v => v < 0.5, (e, _) => e.Set("S_OH_EXT_LT_RWY_TURNOFF", 0)),
        }
    };

    private static Group BuildSecure() => new()
    {
        Id = "SECURE", Name = "Securing",
        Items = new()
        {
            Auto("SC_ADIRS", "SECURE", "IRS 1, 2 and 3: OFF",
                "S_OH_NAV_IR1_MODE", v => v < 0.5, new[] { "S_OH_NAV_IR2_MODE", "S_OH_NAV_IR3_MODE" },
                async (e, _) =>
                {
                    await e.Set("S_OH_NAV_IR1_MODE", 0);
                    await e.Set("S_OH_NAV_IR2_MODE", 0);
                    await e.Set("S_OH_NAV_IR3_MODE", 0);
                }),
            Auto("SC_OXY", "SECURE", "Crew oxygen supply: OFF",
                "S_OH_OXYGEN_CREW_OXYGEN", v => v < 0.5, (e, _) => e.Set("S_OH_OXYGEN_CREW_OXYGEN", 0)),
            Auto("SC_EMEREXIT", "SECURE", "Emergency exit lights: OFF",
                "S_OH_INT_LT_EMER", v => v < 0.5, (e, _) => e.Set("S_OH_INT_LT_EMER", 0)),
            Auto("SC_NOSMOKE", "SECURE", "No smoking: OFF",
                "S_OH_SIGNS_SMOKING", v => v < 0.5, (e, _) => e.Set("S_OH_SIGNS_SMOKING", 0)),
            Auto("SC_APUBLEED", "SECURE", "APU bleed: OFF",
                "S_OH_PNEUMATIC_APU_BLEED", v => v < 0.5, (e, _) => e.Set("S_OH_PNEUMATIC_APU_BLEED", 0)),
            Auto("SC_APUMASTER", "SECURE", "APU master: OFF",
                "S_OH_ELEC_APU_MASTER", v => v < 0.5, (e, _) => e.Set("S_OH_ELEC_APU_MASTER", 0)),
            Auto("SC_BAT1", "SECURE", "Battery 1: OFF",
                "S_OH_ELEC_BAT1", v => v < 0.5, (e, _) => e.Set("S_OH_ELEC_BAT1", 0)),
            Auto("SC_BAT2", "SECURE", "Battery 2: OFF",
                "S_OH_ELEC_BAT2", v => v < 0.5, (e, _) => e.Set("S_OH_ELEC_BAT2", 0)),
        }
    };

    // -----------------------------------------------------------------------
    // Item builders (737 idioms adapted to the Fenix generic types)
    // -----------------------------------------------------------------------

    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition,
        string[]? additionalFields, CheckFn? action) => new()
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

    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition, CheckFn? action) =>
        Auto(id, groupId, label, field, condition, null, action);

    private static Item AutoAsync(string id, string groupId, string label,
        string field, Func<double, bool> condition, CheckFn action) =>
        Auto(id, groupId, label, field, condition, null, action);

    private static Item ActionManual(string id, string groupId, string label, CheckFn action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };

    private static Item Reminder(string id, string groupId, string text) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
    };

    private static Item Info(string id, string groupId, string text) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.Informational,
    };
}
