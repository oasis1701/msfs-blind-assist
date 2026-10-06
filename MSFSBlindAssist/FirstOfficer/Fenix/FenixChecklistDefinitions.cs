using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

using Item = Models.ChecklistItem<FenixActionExecutor, FenixStateEvaluator>;
using Group = Models.ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>;
using CheckFn = System.Func<FenixActionExecutor, FenixStateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// Data-driven Fenix A320 First-Officer checklist definitions — the two-layer structure
/// shared with the PMDG profiles: auto-detect STATE groups (mirroring 1:1 what each flow
/// sets; ticking an item fires the SAME write the flow uses) plus action-free readback
/// (*_CL) checklists: the Airbus A320 normal checklist (Nov 2021 revision) — Cockpit
/// Preparation, Before Start, After Start, Taxi, Line-up, Approach, Landing, After Landing,
/// Parking and Securing — kept line for line equal to the FlyByWire A32NX's.
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
        BuildCockpitPrepCL(),
        BuildBeforeStart(),
        BuildBeforeStartCL(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildAfterStartCL(),
        BuildTaxiCL(),
        BuildBeforeTakeoff(),
        BuildLineupCL(),
        BuildAfterTakeoff(),
        BuildDescent(),
        BuildApproach(),
        BuildApproachCL(),
        BuildLandingCL(),
        BuildAfterLanding(),
        BuildAfterLandingCL(),
        BuildShutdown(),
        BuildParkingCL(),
        BuildSecure(),
        BuildSecuringCL(),
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
            // Guarded: a retick while already on-bus must not re-pulse (that would
            // disconnect external power) — act only when the light disagrees.
            Auto("EPU_EXTPWR", "ELEC_POWER_UP", "External power: ON (if available)",
                "I_OH_ELEC_EXT_PWR_L", v => v > 0.5,
                (e, s) => s.IsOn("I_OH_ELEC_EXT_PWR_L") ? Task.CompletedTask : e.Pulse("S_OH_ELEC_EXT_PWR")),
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
            // Guarded: momentary pulse on a latching light — act only when off.
            Auto("PF_GNDCTL", "PREFLIGHT", "Recorder ground control: ON",
                "I_OH_RCRD_GND_CTL_L", v => v > 0.5,
                (e, s) => s.IsOn("I_OH_RCRD_GND_CTL_L") ? Task.CompletedTask : e.Pulse("S_OH_RCRD_GND_CTL")),
            // Held 3 s test (like the fire tests) — ticking runs a self-completing test and
            // the switch returns to NORMAL, so it never sticks in TEST.
            ActionManual("PF_CVR", "PREFLIGHT", "CVR test (listen for the test tone)",
                (e, _) => e.CvrTest("S_OH_RCRD_TEST")),
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
            // "Available" is FenixActionExecutor.ApuAvailField — the ONE spelling every
            // site here references, never a repeated literal, because the executor and
            // these definitions disagreeing about which lamp meant AVAIL is exactly how
            // this went wrong before. MEASURED live 2026-09-04 (Fenix A319, cold APU
            // started in flight, both legends polled throughout): at the START press the
            // _L legend goes 1 and _U stays 0; when the APU becomes available ~50 s later
            // they EXCHANGE and stay that way — _U 1, _L 0. So _L is the TRANSIENT ON
            // legend and _U is the PERSISTENT AVAIL legend.
            // ⚠️ That is REVERSED against the real A320 (upper = ON, lower = AVAIL) AND
            // against the Fenix's own APU MASTER pushbutton, measured in the same session
            // as _U = FAULT dark / _L = ON lit, matching the real jet exactly — so the
            // suffixes look trustworthy right up until this button. Reading the wrong one
            // makes a running APU report "not available" forever: the skip check below
            // fails to skip (re-pressing START on a live APU) and the wait then sits on a
            // lamp that will never light again. Both readings have now shipped once each on
            // reasoning alone, each reproducing the other's bug, so: on the Fenix a legend's
            // meaning is NEVER inferred — not from its suffix, not from the real aircraft,
            // not from a sibling pushbutton — it is read in the sim.
            AutoAsync("BS_APU", "BEFORE_START", "APU: ON and available",
                FenixActionExecutor.ApuAvailField, v => v > 0.5, (e, _) => e.StartApuAsync()),
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
            // Guarded: a retick while already off must not re-pulse (that would
            // reconnect external power) — act only when the light disagrees. The
            // !IsOn guard also no-ops during the NaN window (unknown state), the
            // safe direction for a target of OFF.
            Auto("BS_EXTPWR_OFF", "BEFORE_START", "External power: OFF",
                "I_OH_ELEC_EXT_PWR_L", v => v < 0.5,
                (e, s) => !s.IsOn("I_OH_ELEC_EXT_PWR_L") ? Task.CompletedTask : e.Pulse("S_OH_ELEC_EXT_PWR")),
            Auto("BS_SEATBELTS", "BEFORE_START", "Seatbelt signs: ON",
                "S_OH_SIGNS", v => v > 0.5, (e, _) => e.Set("S_OH_SIGNS", 1)),
            Auto("BS_BEACON", "BEFORE_START", "Beacon: ON",
                "S_OH_EXT_LT_BEACON", v => v > 0.5, (e, _) => e.Set("S_OH_EXT_LT_BEACON", 1)),
            ActionManual("BS_FCUSPD", "BEFORE_START", "FCU speed: managed",
                (e, _) => e.PushFcuManaged("S_FCU_SPEED")),
            ActionManual("BS_FCUHDG", "BEFORE_START", "FCU heading: managed",
                (e, _) => e.PushFcuManaged("S_FCU_HEADING")),
            // Cockpit door: closed (S_COCKPIT_DOOR=0, live-verified actuator 2026-07-05).
            ActionManual("BS_COCKPITDOOR", "BEFORE_START", "Cockpit door: closed and locked",
                (e, _) => e.SetCockpitDoor(false)),
            Reminder("BS_DOORS", "BEFORE_START", "Close doors and remove ground services on the EFB"),
            Reminder("BS_THRLEVERS", "BEFORE_START", "Confirm thrust levers idle"),
            Reminder("BS_ACARS", "BEFORE_START", "Start ACARS"),
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
            // Engine 1 first, then engine 2 (user preference).
            Auto("ES_ENG1", "ENGINE_START", "Engine 1 master: ON",
                "S_ENG_MASTER_1", v => v > 0.5, (e, _) => e.Set("S_ENG_MASTER_1", 1)),
            Auto("ES_ENG1_RUN", "ENGINE_START", "Engine 1: running",
                "FO_ENG1_N2", v => v >= FenixStateEvaluator.EngineRunningN2, action: null),
            Auto("ES_ENG2", "ENGINE_START", "Engine 2 master: ON",
                "S_ENG_MASTER_2", v => v > 0.5, (e, _) => e.Set("S_ENG_MASTER_2", 1)),
            Auto("ES_ENG2_RUN", "ENGINE_START", "Engine 2: running",
                "FO_ENG2_N2", v => v >= FenixStateEvaluator.EngineRunningN2, action: null),
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
            // Guarded: momentary pulse on a latching light — act only when not armed
            // (mirrors the BT_AUTOBRAKE flow step's own skip guard).
            Auto("BT_AUTOBRAKE", "BEFORE_TAKEOFF", "Autobrake: MAX",
                "I_MIP_AUTOBRAKE_MAX_L", v => v > 0.5,
                (e, s) => s.IsOn("I_MIP_AUTOBRAKE_MAX_L") ? Task.CompletedTask : e.Pulse("S_MIP_AUTOBRAKE_MAX")),
            Auto("BT_WXR", "BEFORE_TAKEOFF", "Weather radar: ON",                      // [RADAR]
                "S_WR_SYS", v => v < 0.5, (e, _) => e.Set("S_WR_SYS", 0)),             // [RADAR]
            Auto("BT_PWS", "BEFORE_TAKEOFF", "Predictive windshear: AUTO",             // [RADAR]
                "S_WR_PRED_WS", v => v > 0.5, (e, _) => e.Set("S_WR_PRED_WS", 1)),     // [RADAR]
            Auto("BT_TCAS", "BEFORE_TAKEOFF", "TCAS: TA/RA",
                "S_XPDR_MODE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("S_XPDR_MODE", 2)),
            Auto("BT_XPDRAUTO", "BEFORE_TAKEOFF", "Transponder: AUTO",
                "S_XPDR_OPERATION", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_XPDR_OPERATION", 1)),
            // Level-triggered test: TakeoffConfigTest holds 1.5 s, announces the result
            // ("Takeoff config normal." / "check configuration."), then RELEASES — a plain
            // pulse left the button stuck at 1 and re-fired the config check after landing.
            ActionManual("BT_CONFIG", "BEFORE_TAKEOFF", "Takeoff config test",
                (e, _) => e.TakeoffConfigTest()),
            Auto("BT_TURNOFF", "BEFORE_TAKEOFF", "Runway turn-off lights: ON",
                "S_OH_EXT_LT_RWY_TURNOFF", v => v > 0.5, (e, _) => e.Set("S_OH_EXT_LT_RWY_TURNOFF", 1)),
            Auto("BT_LANDING_LT", "BEFORE_TAKEOFF", "Landing lights: ON",
                "S_OH_EXT_LT_LANDING_L", v => Math.Abs(v - 2) < 0.5, new[] { "S_OH_EXT_LT_LANDING_R" },
                (e, _) => e.SetLandingLights(2)),
            Auto("BT_NOSE_TO", "BEFORE_TAKEOFF", "Nose light: TAKEOFF",
                "S_OH_EXT_LT_NOSE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.SetNoseLight(2)),
            Auto("BT_STROBE", "BEFORE_TAKEOFF", "Strobes: ON",
                "S_OH_EXT_LT_STROBE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("S_OH_EXT_LT_STROBE", 2)),
            // Advise the cabin crew: hit CALL ALL and release (momentary chime).
            ActionManual("BT_CABIN", "BEFORE_TAKEOFF", "Advise the cabin crew for takeoff (call all)",
                (e, _) => e.CabinCall("S_OH_CALLS_ALL")),
            Reminder("BT_CLEARANCE", "BEFORE_TAKEOFF", "Obtain takeoff clearance"),
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
            Reminder("DC_AUTOBRAKE", "DESCENT", "Set the landing autobrake — Main Instrument Panel, Auto Brakes"),
            Auto("DC_SEATBELTS", "DESCENT", "Seatbelt signs: ON",
                "S_OH_SIGNS", v => v > 0.5, (e, _) => e.Set("S_OH_SIGNS", 1)),
            // ONE descent-preparation item. The EFB carries no landing-performance answer
            // on the A320 — VAPP comes off the MCDU PERF APPR page from the QNH /
            // temperature / wind / minimums the crew enters. And there is no CRUISE group
            // on this profile, so THIS group is the pre-TOD preparation: an item that says
            // "before top of descent" contradicts where it lives.
            Reminder("DC_MCDU", "DESCENT",
                "Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing configuration reviewed"),
        }
    };

    private static Group BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = new()
        {
            // Guarded: momentary pulse on a latching light — act only when not lit
            // (a retick on an already-lit side must not turn LS back off).
            // LS actuator is the BASE var S_FCU_EFISn_LS (NOT "_PRESS" — that synthetic panel
            // key is a no-op when written directly; see FenixActionExecutor).
            Auto("AP_LS1", "APPROACH", "LS captain: ON",
                "I_FCU_EFIS1_LS", v => v > 0.5,
                (e, s) => s.IsOn("I_FCU_EFIS1_LS") ? Task.CompletedTask : e.Pulse("S_FCU_EFIS1_LS")),
            Auto("AP_LS2", "APPROACH", "LS first officer: ON",
                "I_FCU_EFIS2_LS", v => v > 0.5,
                (e, s) => s.IsOn("I_FCU_EFIS2_LS") ? Task.CompletedTask : e.Pulse("S_FCU_EFIS2_LS")),
            // Notify the cabin crew for landing: hit CALL ALL and release (momentary chime).
            ActionManual("AP_CABIN", "APPROACH", "Notify the cabin crew for landing (call all)",
                (e, _) => e.CabinCall("S_OH_CALLS_ALL")),
            Reminder("AP_MINIMUMS", "APPROACH", "Check minimums set on the MCDU approach page"),
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
            Auto("AL_STROBE_AUTO", "AFTER_LANDING", "Strobes: AUTO",
                "S_OH_EXT_LT_STROBE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("S_OH_EXT_LT_STROBE", 1)),
            Auto("AL_LANDING_OFF", "AFTER_LANDING", "Landing lights: OFF",
                "S_OH_EXT_LT_LANDING_L", v => Math.Abs(v - 1) < 0.5, new[] { "S_OH_EXT_LT_LANDING_R" },
                (e, _) => e.SetLandingLights(1)),
            Auto("AL_NOSE_TAXI", "AFTER_LANDING", "Nose light: TAXI",
                "S_OH_EXT_LT_NOSE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.SetNoseLight(1)),
            // AVAIL lamp (_L), not the transient ON lamp (_U) — see BS_APU above.
            AutoAsync("AL_APU", "AFTER_LANDING", "APU: ON and available",
                FenixActionExecutor.ApuAvailField, v => v > 0.5, (e, _) => e.StartApuAsync()),
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
            // Transponder/TCAS to STANDBY here (moved from After Landing).
            Auto("SD_XPDR_STBY", "SHUTDOWN", "Transponder: STANDBY",
                "S_XPDR_OPERATION", v => v < 0.5, (e, _) => e.Set("S_XPDR_OPERATION", 0)),
            // Guarded: momentary pulse on a latching light — act only when currently on
            // (mirrors the APPROACH AP_LSn item inverted). Base var S_FCU_EFISn_LS, NOT "_PRESS".
            Auto("SD_LS1", "SHUTDOWN", "LS captain: OFF",
                "I_FCU_EFIS1_LS", v => v < 0.5,
                (e, s) => !s.IsOn("I_FCU_EFIS1_LS") ? Task.CompletedTask : e.Pulse("S_FCU_EFIS1_LS")),
            Auto("SD_LS2", "SHUTDOWN", "LS first officer: OFF",
                "I_FCU_EFIS2_LS", v => v < 0.5,
                (e, s) => !s.IsOn("I_FCU_EFIS2_LS") ? Task.CompletedTask : e.Pulse("S_FCU_EFIS2_LS")),
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
            // Cockpit door: open for disembark (S_COCKPIT_DOOR=1, live-verified actuator 2026-07-05).
            ActionManual("SD_COCKPITDOOR", "SHUTDOWN", "Cockpit door: unlocked",
                (e, _) => e.SetCockpitDoor(true)),
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
    // Read-back (*_CL) groups — the Airbus A320 normal checklist (Nov 2021 revision), with
    // each "ECAM MEMO ... NO BLUE" line expanded into the memo's own lines. Sources and
    // confidence: docs/invariants/first-officer-airbus.md#foa-8. The same ten groups, ids,
    // labels and order as the FlyByWire A32NX (the Fenix prints "signs" on the memo's seat-belt
    // lines and has no readable rudder trim, so that line is a Reminder here). HARD INVARIANT
    // (FO-3): every item is action-free (Reminder, or Auto(..., action: null)).
    // -----------------------------------------------------------------------

    private static Group BuildCockpitPrepCL() => new()
    {
        Id = "COCKPIT_PREP_CL", Name = "Cockpit Preparation Checklist",
        Items = new()
        {
            Reminder("CPC_PINS", "COCKPIT_PREP_CL", "Gear pins and covers: REMOVED"),
            Reminder("CPC_FUEL", "COCKPIT_PREP_CL", "Fuel quantity: CHECKED", s => s.FuelText()),
            Auto("CPC_SEATBELTS", "COCKPIT_PREP_CL", "Seat belts: ON",
                "S_OH_SIGNS", v => v > 0.5, action: null),
            Auto("CPC_ADIRS", "COCKPIT_PREP_CL", "ADIRS: NAV",
                "S_OH_NAV_IR1_MODE", v => Math.Abs(v - 1) < 0.5,
                new[] { "S_OH_NAV_IR2_MODE", "S_OH_NAV_IR3_MODE" }, action: null),
            Reminder("CPC_BARO", "COCKPIT_PREP_CL", "Baro reference: SET (both)", s => s.BaroText()),
        }
    };

    private static Group BuildBeforeStartCL() => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = new()
        {
            Reminder("BSC_PARKBRK", "BEFORE_START_CL", "Parking brake: SET",
                s => AirbusReadbackFormat.OnOff(s.GetValue("S_MIP_PARKING_BRAKE"))),
            Reminder("BSC_TOSPEEDS", "BEFORE_START_CL", "Takeoff speeds and thrust: SET (both)",
                s => AirbusReadbackFormat.TakeoffSpeeds(s.GetValue("FNX2PLD_speedV1"),
                    s.GetValue("FNX2PLD_speedVR"), s.GetValue("FNX2PLD_speedV2"),
                    s.GetValue("N_MISC_PERF_TO_FLEX"))),
            Reminder("BSC_WINDOWS", "BEFORE_START_CL", "Windows: CLOSED (both)"),
            Auto("BSC_BEACON", "BEFORE_START_CL", "Beacon: ON",
                "S_OH_EXT_LT_BEACON", v => v > 0.5, action: null),
        }
    };

    private static Group BuildAfterStartCL() => new()
    {
        Id = "AFTER_START_CL", Name = "After Start Checklist",
        Items = new()
        {
            Reminder("ASC_ANTIICE", "AFTER_START_CL", "Anti-ice: SET",
                s => AirbusReadbackFormat.AntiIce(s.GetValue("S_OH_PNEUMATIC_ENG1_ANTI_ICE"),
                    s.GetValue("S_OH_PNEUMATIC_ENG2_ANTI_ICE"), s.GetValue("S_OH_PNEUMATIC_WING_ANTI_ICE"))),
            Reminder("ASC_ECAMSTS", "AFTER_START_CL", "ECAM status: CHECKED"),
            Reminder("ASC_PITCH", "AFTER_START_CL", "Pitch trim: SET"),
            // A Reminder (owner decision 2026-10-06): the A32NX ticks this line from the FAC's
            // rudder trim word, but no Fenix var has been MEASURED as a neutral test (the
            // registered N_FC_RUDDER_TRIM_DECIMAL display value has no verified unit or zero,
            // and a Fenix var's meaning is read in the sim, never inferred).
            Reminder("ASC_RUDDER", "AFTER_START_CL", "Rudder trim: NEUTRAL"),
        }
    };

    private static Group BuildTaxiCL() => new()
    {
        Id = "TAXI_CL", Name = "Taxi Checklist",
        Items = new()
        {
            Reminder("TXC_FCTEST", "TAXI_CL", "Flight controls: CHECKED (both)"),
            Reminder("TXC_FLAPS", "TAXI_CL", "Flaps setting: SET (both)",
                s => AirbusReadbackFormat.FlapsLever(s.GetValue("S_FC_FLAPS"))),
            Auto("TXC_WXR", "TAXI_CL", "Radar and predictive windshear: ON and AUTO",
                "FO_WXR_ON_AUTO", v => v > 0.5, action: null),
            Reminder("TXC_ENGMODE", "TAXI_CL", "Engine mode selector: SET",
                s => AirbusReadbackFormat.EngineMode(s.GetValue("S_ENG_MODE"))),
            Auto("TXC_MEMO_AUTOBRK", "TAXI_CL", "Takeoff memo, autobrake: MAX",
                "I_MIP_AUTOBRAKE_MAX_L", v => v > 0.5, action: null),
            // The Fenix's memo prints "SIGNS": seat belts ON and the no-smoking sign lit (the
            // switch at Auto or On). S_OH_SIGNS_SMOKING is 0 Off / 1 Auto / 2 On.
            Auto("TXC_MEMO_SIGNS", "TAXI_CL", "Takeoff memo, signs: ON",
                "S_OH_SIGNS", v => v > 0.5, new[] { "S_OH_SIGNS_SMOKING" }, action: null),
            Reminder("TXC_MEMO_CABIN", "TAXI_CL", "Takeoff memo, cabin: READY"),
            // A_FC_SPEEDBRAKE: 0 = ARMED.
            Auto("TXC_MEMO_SPLRS", "TAXI_CL", "Takeoff memo, spoilers: ARMED",
                "A_FC_SPEEDBRAKE", v => Math.Abs(v) < 0.5, action: null),
            Auto("TXC_MEMO_FLAPS", "TAXI_CL", "Takeoff memo, flaps: T.O",
                "S_FC_FLAPS", v => v > 0.5 && v < 3.5, action: null),
            Reminder("TXC_MEMO_TOCFG", "TAXI_CL", "Takeoff memo, T.O config: NORMAL"),
        }
    };

    private static Group BuildLineupCL() => new()
    {
        Id = "LINEUP_CL", Name = "Line-up Checklist",
        Items = new()
        {
            Reminder("LUC_RUNWAY", "LINEUP_CL", "Takeoff runway: CONFIRMED (both)"),
            Auto("LUC_TCAS", "LINEUP_CL", "TCAS: TA/RA",
                "S_XPDR_MODE", v => Math.Abs(v - 2) < 0.5, action: null,
                live: s => AirbusReadbackFormat.Tcas(s.GetValue("S_XPDR_MODE"))),
            Reminder("LUC_PACKS", "LINEUP_CL", "Packs 1 and 2: SET",
                s => AirbusReadbackFormat.Packs(s.GetValue("S_OH_PNEUMATIC_PACK_1"),
                    s.GetValue("S_OH_PNEUMATIC_PACK_2"))),
        }
    };

    private static Group BuildApproachCL() => new()
    {
        Id = "APPROACH_CL", Name = "Approach Checklist",
        Items = new()
        {
            Reminder("APC_BARO", "APPROACH_CL", "Baro reference: SET (both)", s => s.BaroText()),
            Auto("APC_SEATBELTS", "APPROACH_CL", "Seat belts: ON",
                "S_OH_SIGNS", v => v > 0.5, action: null),
            Reminder("APC_MINIMUM", "APPROACH_CL", "Minimum: SET"),
            // Read only: the landing autobrake is the Captain's (FO-14).
            Reminder("APC_AUTOBRAKE", "APPROACH_CL", "Autobrake: SET", s => s.AutobrakeText()),
            Reminder("APC_ENGMODE", "APPROACH_CL", "Engine mode selector: SET",
                s => AirbusReadbackFormat.EngineMode(s.GetValue("S_ENG_MODE"))),
        }
    };

    private static Group BuildLandingCL() => new()
    {
        Id = "LANDING_CL", Name = "Landing Checklist",
        Items = new()
        {
            // "Landing gear: DOWN" is confirmed the way a crew confirms it — three green, no
            // red — through the FenixGearConfirmation synthetic (lever DOWN, each wheel's
            // lower legend lit, its upper legend and the red arrow out — the greens measured
            // live 2026-09-25), never the lever alone (owner decision 2026-09-22). The Fenix
            // has no Landing flow, so nothing latches this line: it ticks from its own state.
            Auto("LDC_MEMO_GEAR", "LANDING_CL", "Landing memo, landing gear: DOWN",
                FenixGearConfirmation.DownField, v => v > 0.5, action: null),
            Auto("LDC_MEMO_SIGNS", "LANDING_CL", "Landing memo, signs: ON",
                "S_OH_SIGNS", v => v > 0.5, action: null),
            Reminder("LDC_MEMO_CABIN", "LANDING_CL", "Landing memo, cabin: READY"),
            Auto("LDC_MEMO_SPLRS", "LANDING_CL", "Landing memo, spoilers: ARMED",
                "A_FC_SPEEDBRAKE", v => Math.Abs(v) < 0.5, action: null),
            // FULL, or 3 with the GPWS LDG FLAP 3 switch on; the live value is the lever itself.
            Auto("LDC_MEMO_FLAPS", "LANDING_CL", "Landing memo, flaps: SET",
                "FO_LDG_FLAPS_SET", v => v > 0.5, action: null,
                live: s => AirbusReadbackFormat.FlapsLever(s.GetValue("S_FC_FLAPS"))),
        }
    };

    private static Group BuildAfterLandingCL() => new()
    {
        Id = "AFTER_LANDING_CL", Name = "After Landing Checklist",
        Items = new()
        {
            Auto("ALC_WXR", "AFTER_LANDING_CL", "Radar and predictive windshear: OFF",
                "FO_WXR_PWS_OFF", v => v > 0.5, action: null),
        }
    };

    private static Group BuildParkingCL() => new()
    {
        Id = "PARKING_CL", Name = "Parking Checklist",
        Items = new()
        {
            Reminder("PKC_PARKBRK", "PARKING_CL", "Parking brake or chocks: SET",
                s => AirbusReadbackFormat.OnOff(s.GetValue("S_MIP_PARKING_BRAKE"))),
            Auto("PKC_ENGINES", "PARKING_CL", "Engines: OFF",
                "FO_ENGINES_OFF", v => v > 0.5, action: null),
            Auto("PKC_WINGLT", "PARKING_CL", "Wing lights: OFF",
                "S_OH_EXT_LT_WING", v => v < 0.5, action: null),
            Auto("PKC_FUELPUMPS", "PARKING_CL", "Fuel pumps: OFF",
                "S_OH_FUEL_LEFT_1", v => v < 0.5,
                new[] { "S_OH_FUEL_LEFT_2", "S_OH_FUEL_CENTER_1", "S_OH_FUEL_CENTER_2",
                        "S_OH_FUEL_RIGHT_1", "S_OH_FUEL_RIGHT_2" }, action: null),
        }
    };

    private static Group BuildSecuringCL() => new()
    {
        Id = "SECURING_CL", Name = "Securing Checklist",
        Items = new()
        {
            Auto("SCC_OXY", "SECURING_CL", "Oxygen: OFF",
                "S_OH_OXYGEN_CREW_OXYGEN", v => Math.Abs(v) < 0.5, action: null),
            // S_OH_INT_LT_EMER: 0 Off / 1 Arm / 2 On.
            Auto("SCC_EMEREXIT", "SECURING_CL", "Emergency exit lights: OFF",
                "S_OH_INT_LT_EMER", v => Math.Abs(v) < 0.5, action: null),
            Reminder("SCC_EFBS", "SECURING_CL", "EFBs: OFF"),
            Auto("SCC_BAT", "SECURING_CL", "Batteries: OFF",
                "S_OH_ELEC_BAT1", v => v < 0.5, new[] { "S_OH_ELEC_BAT2" }, action: null),
        }
    };

    // -----------------------------------------------------------------------
    // Item builders (737 idioms adapted to the Fenix generic types)
    // -----------------------------------------------------------------------

    // `live` is the optional value the read-back speaks on a tick and shows in the status line
    // ("Flaps setting, flaps 2: Complete") — never part of the tree text (ChecklistItem.LiveValue).
    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition,
        string[]? additionalFields, CheckFn? action,
        Func<FenixStateEvaluator, string?>? live = null) => new()
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
        LiveValue = live,
    };

    // The short overload gains `live` too, so `Auto(..., v => ..., action: null, live: ...)`
    // (no additionalFields) compiles.
    private static Item Auto(string id, string groupId, string label,
        string field, Func<double, bool> condition, CheckFn? action,
        Func<FenixStateEvaluator, string?>? live = null) =>
        Auto(id, groupId, label, field, condition, null, action, live);

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

    private static Item Reminder(string id, string groupId, string text,
        Func<FenixStateEvaluator, string?>? live = null) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
        LiveValue = live,
    };
}
