using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.FBWA320;

using Item = Models.ChecklistItem<FbwA320ActionExecutor, FbwA320StateEvaluator>;
using Group = Models.ChecklistGroup<FbwA320ActionExecutor, FbwA320StateEvaluator>;
using CheckFn = System.Func<FbwA320ActionExecutor, FbwA320StateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// Data-driven FlyByWire A320 (A32NX) First-Officer checklist definitions — the
/// STATE/ACTION layer that mirrors <see cref="FbwA320FlowDefinitions"/> 1:1 (every
/// flow step's <c>CompletesChecklistItemId</c> resolves to a matching item here whose
/// <c>CheckAction</c> fires the SAME write the flow uses) plus the action-free
/// readback (*_CL) checklists.
///
/// Every state item is RevertToState (live mirror), matching the Fenix/A380
/// convention. State reads come from the SimConnect L:var cache via
/// <see cref="FbwA320StateEvaluator"/> — OnRequest vars are polled by the FO window
/// via OnRequestPollFields; an uncached var reads NaN (indeterminate: no tick, no
/// revert).
///
/// Cockpit lighting (spec §4.1) and ECAM-page (spec §4) items are the two ★ patterns
/// borrowed from the A380 template: ANN/dome/standby-compass get their own Auto
/// items (each independently settable), plus one ActionManual "Panel and integral
/// brightness: SET" per phase that fires the full <see cref="FbwA320ActionExecutor.SetCockpitLighting"/>
/// scene; ECAM page items are ActionManual (a direct SD page-index write, held-safe —
/// see FbwA320ActionExecutor.FireEcamPageAsync — but still fired as a one-shot checklist
/// action here rather than an auto-detected state, since auto-SD logic can override the
/// manual index on the next event).
/// </summary>
public static class FbwA320ChecklistDefinitions
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
        BuildLineupCL(),
        BuildBeforeTakeoff(),
        BuildBeforeTakeoffCL(),
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
    // 1. Electrical Power Up
    // -----------------------------------------------------------------------
    private static Group BuildElectricalPowerUp() => new()
    {
        Id = "ELEC_POWER_UP", Name = "Electrical Power Up",
        Items = new()
        {
            Auto("EPU_BAT1", "ELEC_POWER_UP", "Battery 1: ON", "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 1)),
            Auto("EPU_BAT2", "ELEC_POWER_UP", "Battery 2: ON", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 1)),
            Auto("EPU_EXTPWR", "ELEC_POWER_UP", "External power: ON (if available)", "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 1)),
            Auto("EPU_NAVLOGO", "ELEC_POWER_UP", "Nav and logo lights: ON", "A32NX_LIGHTS_NAV_LOGO",
                v => v > 0.5, (e, _) => e.Set("A32NX_LIGHTS_NAV_LOGO", 1)),
            // Gear lever has no settable A32NX key, but GEAR_HANDLE_POSITION (stock,
            // 0=Up/1=Down) is readable — detect-only Auto (no CheckAction; the flow
            // step stays a Captain reminder since the lever can't be written).
            Auto("EPU_CHK_GEAR", "ELEC_POWER_UP", "Gear lever: DOWN", "GEAR_HANDLE_POSITION",
                v => v > 0.5, null),
            // ★ Cockpit lighting (spec §4.1): Bright for ground prep. ANN matches the
            // flow's actual write; dome + standby compass are additional independently
            // settable Auto items; the scene ActionManual fires the full batched write.
            Auto("EPU_COCKPITLT", "ELEC_POWER_UP", "Cockpit lights: ANN bright", "A32NX_OVHD_INTLT_ANN",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_OVHD_INTLT_ANN", 1)),
            Auto("EPU_DOME", "ELEC_POWER_UP", "Cockpit lights: dome bright", "A32NX_OVHD_INTLT_DOME",
                v => v >= 90, (e, _) => e.Set("A32NX_OVHD_INTLT_DOME", 100)),
            Auto("EPU_STBYCOMPASS", "ELEC_POWER_UP", "Standby compass light: ON", "A32NX_STBY_COMPASS_LIGHT_TOGGLE",
                v => v > 0.5, (e, _) => e.Set("A32NX_STBY_COMPASS_LIGHT_TOGGLE", 1)),
            ActionManual("EPU_LTSCENE", "ELEC_POWER_UP", "Panel and integral brightness: SET",
                (e, _) => e.SetCockpitLighting(FbwA320ActionExecutor.CockpitLightScene.DayPrep)),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Preflight
    // -----------------------------------------------------------------------
    private static Group BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = new()
        {
            // Recorder ground control + CVR test — sim + source verified (2026-07).
            Auto("PF_GNDCTL", "PREFLIGHT", "Recorder ground control: ON", "A32NX_RCDR_GROUND_CONTROL_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_RCDR_GROUND_CONTROL_ON", 1)),
            ActionManual("PF_CVR", "PREFLIGHT", "CVR test (listen for the test tone)", (e, _) => e.CvrTest()),
            Auto("PF_IRS", "PREFLIGHT", "IRS 1, 2 and 3: NAV", "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
                v => System.Math.Abs(v - 1) < 0.5,
                new[] { "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB" },
                async (e, _) =>
                {
                    await e.Set("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 1);
                    await e.Set("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 1);
                    await e.Set("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 1);
                }),
            // Crew oxygen: INVERTED convention (0=On, 1=Off, pushbutton-out=on).
            Auto("PF_OXY", "PREFLIGHT", "Crew oxygen supply: ON", "PUSH_OVHD_OXYGEN_CREW",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("PUSH_OVHD_OXYGEN_CREW", 0)),
            // Held self-completing fire tests (no persistent state) — manual-tick only.
            ActionManual("PF_FIRE_APU", "PREFLIGHT", "APU fire test", (e, _) => e.FireTestAsync("FIRE_TEST_APU")),
            ActionManual("PF_FIRE_ENG1", "PREFLIGHT", "Engine 1 fire test", (e, _) => e.FireTestAsync("FIRE_TEST_ENG1")),
            ActionManual("PF_FIRE_ENG2", "PREFLIGHT", "Engine 2 fire test", (e, _) => e.FireTestAsync("FIRE_TEST_ENG2")),
            // ★ ECAM page: door — momentary ECP press, no persistent page index on this
            // build; ActionManual (no CheckAction/state, matches the design brief).
            ActionManual("PF_ECAMDOOR", "PREFLIGHT", "ECAM page: door", (e, _) => e.Set("ECAM_PAGE_DOOR", 1)),
            Auto("PF_PACKS", "PREFLIGHT", "Packs 1 and 2: ON", "A32NX_OVHD_COND_PACK_1_PB_IS_ON",
                v => v > 0.5, new[] { "A32NX_OVHD_COND_PACK_2_PB_IS_ON" },
                async (e, _) =>
                {
                    await e.Set("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1);
                    await e.Set("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 1);
                }),
            Auto("PF_XBLEED", "PREFLIGHT", "Crossbleed: AUTO", "A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position", 1)),
            Auto("PF_PACKFLOW", "PREFLIGHT", "Pack flow: NORMAL", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position", 1)),
            Auto("PF_HOTAIR", "PREFLIGHT", "Hot air: ON", "A32NX_OVHD_COND_HOT_AIR_PB_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_COND_HOT_AIR_PB_IS_ON", 1)),
            Auto("PF_PRESSMODE", "PREFLIGHT", "Cabin pressure mode: AUTO", "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO", 1)),
            Auto("PF_STROBE", "PREFLIGHT", "Strobes: AUTO", "LIGHTING_STROBE_0",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("LIGHTING_STROBE_0", 1)),
            // Read the stock readback var, write the panel key (they differ on this build).
            Auto("PF_WING_LT", "PREFLIGHT", "Wing lights: OFF", "LIGHT WING",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("WING_LIGHTS_SET", 0)),
            Auto("PF_NOSMOKE", "PREFLIGHT", "No smoking: AUTO", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 1)),
            Auto("PF_EMEREXIT", "PREFLIGHT", "Emergency exit lights: ARM", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION", 1)),
            Auto("PF_ALTRPTG", "PREFLIGHT", "Altitude reporting: ON", "A32NX_SWITCH_ATC_ALT",
                v => v > 0.5, (e, _) => e.Set("A32NX_SWITCH_ATC_ALT", 1)),
            Auto("PF_TCASTRAFFIC", "PREFLIGHT", "TCAS traffic: ALL", "A32NX_SWITCH_TCAS_TRAFFIC_POSITION",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_SWITCH_TCAS_TRAFFIC_POSITION", 1)),
            Reminder("PF_BARO", "PREFLIGHT", "Set QNH on both altimeters and the standby altimeter"),
            Reminder("PF_FCUALT", "PREFLIGHT", "Set the initial cleared altitude on the FCU"),
            Reminder("PF_SQUAWK", "PREFLIGHT", "Set the squawk code"),
            Reminder("PF_EFB", "PREFLIGHT", "EFB setup — import SimBrief, load fuel and payload"),
            Reminder("PF_MCDU", "PREFLIGHT", "MCDU setup — INIT, flight plan, and PERF pages"),
        }
    };

    // -----------------------------------------------------------------------
    // 3. Before Start
    // -----------------------------------------------------------------------
    private static Group BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            ActionManual("BS_ECAMAPU", "BEFORE_START", "ECAM page: APU", (e, _) => e.Set("ECAM_PAGE_APU", 1)),
            // Master → dwell → START pulse, same pattern as the flow.
            Auto("BS_APU", "BEFORE_START", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v > 0.5, async (e, s) =>
                {
                    await e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1);
                    if (!s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE"))
                    {
                        await System.Threading.Tasks.Task.Delay(3000);
                        await e.Set("A32NX_OVHD_APU_START_PB_IS_ON", 1);
                    }
                }),
            Auto("BS_APUBLEED", "BEFORE_START", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1)),
            Auto("BS_FUELPUMPS", "BEFORE_START", "Fuel pumps: ALL ON", "FUEL_PUMP_L1", v => v > 0.5,
                new[] { "FUEL_PUMP_L2", "FUEL_PUMP_C1", "FUEL_PUMP_C2", "FUEL_PUMP_R1", "FUEL_PUMP_R2" },
                async (e, _) =>
                {
                    await e.Set("FUEL_PUMP_L1", 1);
                    await e.Set("FUEL_PUMP_L2", 1);
                    await e.Set("FUEL_PUMP_C1", 1);
                    await e.Set("FUEL_PUMP_C2", 1);
                    await e.Set("FUEL_PUMP_R1", 1);
                    await e.Set("FUEL_PUMP_R2", 1);
                }),
            Auto("BS_EXTPWR_OFF", "BEFORE_START", "External power: OFF", "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON",
                v => v < 0.5, (e, _) => e.Set("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 0)),
            // Seatbelt signs: genuine 2-position toggle event on the A320 (no AUTO, unlike
            // the A380) — guarded so a retick while already on doesn't toggle it back off.
            Auto("BS_SEATBELTS", "BEFORE_START", "Seatbelt signs: ON", "CABIN SEATBELTS ALERT SWITCH",
                v => v > 0.5, (e, s) => s.IsOn("CABIN SEATBELTS ALERT SWITCH")
                    ? Task.CompletedTask : e.Set("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 1)),
            Auto("BS_BEACON", "BEFORE_START", "Beacon: ON", "LIGHT BEACON",
                v => v > 0.5, (e, _) => e.Set("BEACON_LIGHTS_SET", 1)),
            ActionManual("BS_FCUSPD", "BEFORE_START", "FCU speed: managed", (e, _) => e.Set("FCU_PUSH_SPEED", 1)),
            ActionManual("BS_FCUHDG", "BEFORE_START", "FCU heading: managed", (e, _) => e.Set("FCU_PUSH_HEADING", 1)),
            Reminder("BS_ALT", "BEFORE_START", "Set cleared altitude on the FCU"),
            ActionManual("BS_FCUALT", "BEFORE_START", "FCU altitude: pushed", (e, _) => e.Set("FCU_PUSH_ALT", 1)),
            Auto("BS_COCKPITDOOR", "BEFORE_START", "Cockpit door: LOCKED", "A32NX_COCKPIT_DOOR_LOCKED",
                v => v > 0.5, (e, _) => e.Set("A32NX_COCKPIT_DOOR_LOCKED", 1)),
            Reminder("BS_DOORS", "BEFORE_START", "Close doors and remove ground services on the EFB"),
            Reminder("BS_THRLEVERS", "BEFORE_START", "Confirm thrust levers idle"),
            Reminder("BS_ACARS", "BEFORE_START", "Start ACARS"),
            Reminder("BS_CLEARANCE", "BEFORE_START", "Obtain pushback and start clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 4. Engine Start
    // -----------------------------------------------------------------------
    private static Group BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            ActionManual("ES_ECAMENG", "ENGINE_START", "ECAM page: engine", (e, _) => e.Set("ECAM_PAGE_ENG", 1)),
            Auto("ES_MODE", "ENGINE_START", "Engine mode selector: IGN START", "ENGINE_MODE_SELECTOR",
                v => System.Math.Abs(v - 2) < 0.5, (e, _) => e.Set("ENGINE_MODE_SELECTOR", 2)),
            Auto("ES_ENG1", "ENGINE_START", "Engine 1 master: ON", "ENGINE_1_MASTER",
                v => v > 0.5, (e, _) => e.Set("ENGINE_1_MASTER", 1)),
            Auto("ES_ENG2", "ENGINE_START", "Engine 2 master: ON", "ENGINE_2_MASTER",
                v => v > 0.5, (e, _) => e.Set("ENGINE_2_MASTER", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 5. After Start
    // -----------------------------------------------------------------------
    private static Group BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Items = new()
        {
            Auto("AS_MODE_NORM", "AFTER_START", "Engine mode selector: NORM", "ENGINE_MODE_SELECTOR",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("ENGINE_MODE_SELECTOR", 1)),
            Auto("AS_APUBLEED_OFF", "AFTER_START", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
                v => v < 0.5, (e, _) => e.Set("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)),
            Auto("AS_APUMASTER_OFF", "AFTER_START", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v < 0.5, (e, _) => e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)),
            // Genuine toggle event — guarded so a retick while already armed doesn't
            // disarm it.
            Auto("AS_SPOILERS_ARM", "AFTER_START", "Ground spoilers: ARMED", "A32NX_SPOILERS_ARMED",
                v => v > 0.5, (e, s) => s.IsOn("A32NX_SPOILERS_ARMED")
                    ? Task.CompletedTask : e.Set("SPOILERS_ARM_TOGGLE", 1)),
            ActionManual("AS_RUDDERTRIM", "AFTER_START", "Rudder trim: RESET", (e, _) => e.Set("A32NX_RUDDER_TRIM_RESET", 1)),
            // Takeoff flaps are NEVER auto-set (project-wide rule) — Captain reminder,
            // matches the flow's decision.
            Reminder("AS_FLAPS", "AFTER_START", "Flaps: set for takeoff"),
            Auto("AS_NOSE_TAXI", "AFTER_START", "Nose light: TAXI", "LIGHTING_LANDING_1",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("LIGHTING_LANDING_1", 1)),
            Reminder("AS_ANTIICE", "AFTER_START", "Set engine and wing anti-ice as required"),
            Reminder("AS_PITCHTRIM", "AFTER_START", "Set pitch trim per the loadsheet"),
            // ★ Cockpit lighting: Dim for taxi/flight.
            Auto("AS_COCKPITLT", "AFTER_START", "Cockpit lights: ANN dim", "A32NX_OVHD_INTLT_ANN",
                v => System.Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A32NX_OVHD_INTLT_ANN", 2)),
            Auto("AS_DOME", "AFTER_START", "Cockpit lights: dome dim", "A32NX_OVHD_INTLT_DOME",
                v => v <= 30, (e, _) => e.Set("A32NX_OVHD_INTLT_DOME", 20)),
            Auto("AS_STBYCOMPASS", "AFTER_START", "Standby compass light: ON", "A32NX_STBY_COMPASS_LIGHT_TOGGLE",
                v => v > 0.5, (e, _) => e.Set("A32NX_STBY_COMPASS_LIGHT_TOGGLE", 1)),
            ActionManual("AS_LTSCENE", "AFTER_START", "Panel and integral brightness: SET",
                (e, _) => e.SetCockpitLighting(FbwA320ActionExecutor.CockpitLightScene.DimFlight)),
            ActionManual("AS_ECAMSTS", "AFTER_START", "ECAM page: status", (e, _) => e.Set("ECAM_PAGE_STS", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Before Takeoff
    // -----------------------------------------------------------------------
    private static Group BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Items = new()
        {
            // ★ Autobrake MAX.
            Auto("BT_AUTOBRAKE", "BEFORE_TAKEOFF", "Autobrake: MAX", "A32NX_AUTOBRAKES_ARMED_MODE",
                v => System.Math.Abs(v - 3) < 0.5, (e, _) => e.Set("AUTOBRAKE_MODE", 3)),
            Auto("BT_WXR", "BEFORE_TAKEOFF", "Weather radar: SYSTEM 1", "XMLVAR_A320_WeatherRadar_Sys",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("XMLVAR_A320_WeatherRadar_Sys", 0)),
            Auto("BT_PWS", "BEFORE_TAKEOFF", "Predictive windshear: AUTO", "A32NX_SWITCH_RADAR_PWS_POSITION",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_SWITCH_RADAR_PWS_POSITION", 1)),
            Auto("BT_TCAS", "BEFORE_TAKEOFF", "TCAS: TA/RA", "A32NX_SWITCH_TCAS_POSITION",
                v => System.Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A32NX_SWITCH_TCAS_POSITION", 2)),
            Auto("BT_XPDRAUTO", "BEFORE_TAKEOFF", "Transponder: AUTO", "A32NX_TRANSPONDER_MODE",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_TRANSPONDER_MODE", 1)),
            // Takeoff config test — sim + source verified held ECP button (2026-07).
            ActionManual("BT_CONFIG", "BEFORE_TAKEOFF", "Takeoff config test", (e, _) => e.TakeoffConfigTest()),
            Auto("BT_TURNOFF", "BEFORE_TAKEOFF", "Runway turn-off lights: ON", "LIGHT TAXI:2",
                v => v > 0.5, (e, _) => e.Set("LIGHT TAXI:2", 1)),
            Auto("BT_LANDING_LT", "BEFORE_TAKEOFF", "Landing lights: ON", "LIGHTING_LANDING_2",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LANDING_LIGHTS_ON_THIRD_PARTY", 1)),
            Auto("BT_NOSE_TO", "BEFORE_TAKEOFF", "Nose light: TAKEOFF", "LIGHTING_LANDING_1",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHTING_LANDING_1", 0)),
            Auto("BT_STROBE", "BEFORE_TAKEOFF", "Strobes: ON", "LIGHTING_STROBE_0",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHTING_STROBE_0", 0)),
            // ★ Cabin notify for takeoff.
            ActionManual("BT_CABIN", "BEFORE_TAKEOFF", "Advise the cabin crew for takeoff (call all)",
                (e, _) => e.CabinCall()),
            Reminder("BT_CLEARANCE", "BEFORE_TAKEOFF", "Obtain takeoff clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 7. After Takeoff
    // -----------------------------------------------------------------------
    private static Group BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = new()
        {
            Auto("AT_SPOILERS_DISARM", "AFTER_TAKEOFF", "Ground spoilers: DISARM", "A32NX_SPOILERS_ARMED",
                v => System.Math.Abs(v - 0) < 0.5, (e, s) => !s.IsOn("A32NX_SPOILERS_ARMED")
                    ? Task.CompletedTask : e.Set("SPOILERS_ARM_TOGGLE", 0)),
            Auto("AT_PACKS", "AFTER_TAKEOFF", "Packs 1 and 2: ON", "A32NX_OVHD_COND_PACK_1_PB_IS_ON",
                v => v > 0.5, new[] { "A32NX_OVHD_COND_PACK_2_PB_IS_ON" },
                async (e, _) =>
                {
                    await e.Set("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1);
                    await e.Set("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 1);
                }),
            Auto("AT_TURNOFF_OFF", "AFTER_TAKEOFF", "Runway turn-off lights: OFF", "LIGHT TAXI:2",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHT TAXI:2", 0)),
        }
    };

    // -----------------------------------------------------------------------
    // 8. Descent
    // -----------------------------------------------------------------------
    private static Group BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Items = new()
        {
            // Landing autobrake is ALWAYS a Captain item (project-wide rule).
            Reminder("DC_AUTOBRAKE", "DESCENT", "Set the landing autobrake — Instrument section, Autobrake panel"),
            Auto("DC_SEATBELTS", "DESCENT", "Seatbelt signs: ON", "CABIN SEATBELTS ALERT SWITCH",
                v => v > 0.5, (e, s) => s.IsOn("CABIN SEATBELTS ALERT SWITCH")
                    ? Task.CompletedTask : e.Set("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 1)),
            Reminder("DC_ARRPERF", "DESCENT", "Calculate arrival performance on the EFB"),
            Reminder("DC_MCDU", "DESCENT", "Complete the MCDU approach page and minimums before top of descent"),
        }
    };

    // -----------------------------------------------------------------------
    // 9. Approach
    // -----------------------------------------------------------------------
    private static Group BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = new()
        {
            Auto("AP_LS1", "APPROACH", "LS captain: ON", "A32NX_EFIS_L_LS_BUTTON_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_EFIS_L_LS_BUTTON_IS_ON", 1)),
            Auto("AP_LS2", "APPROACH", "LS first officer: ON", "A32NX_EFIS_R_LS_BUTTON_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_EFIS_R_LS_BUTTON_IS_ON", 1)),
            // ★ Cabin notify for landing.
            ActionManual("AP_CABIN", "APPROACH", "Notify the cabin crew for landing (call all)",
                (e, _) => e.CabinCall()),
            Reminder("AP_MINIMUMS", "APPROACH", "Check minimums set on the MCDU approach page"),
            Reminder("AP_ENGMODE", "APPROACH", "Set engine mode selector as required"),
        }
    };

    // -----------------------------------------------------------------------
    // 10. After Landing
    // -----------------------------------------------------------------------
    private static Group BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_SPOILERS", "AFTER_LANDING", "Ground spoilers: DISARM", "A32NX_SPOILERS_ARMED",
                v => System.Math.Abs(v - 0) < 0.5, (e, s) => !s.IsOn("A32NX_SPOILERS_ARMED")
                    ? Task.CompletedTask : e.Set("SPOILERS_ARM_TOGGLE", 0)),
            Auto("AL_FLAPS_UP", "AFTER_LANDING", "Flaps: UP", "A32NX_FLAPS_HANDLE_INDEX",
                v => v < 0.5, (e, _) => e.Set("A32NX_FLAPS_HANDLE_INDEX", 0)),
            Auto("AL_WXR_OFF", "AFTER_LANDING", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("XMLVAR_A320_WeatherRadar_Sys", 1)),
            Auto("AL_PWS_OFF", "AFTER_LANDING", "Predictive windshear: OFF", "A32NX_SWITCH_RADAR_PWS_POSITION",
                v => v < 0.5, (e, _) => e.Set("A32NX_SWITCH_RADAR_PWS_POSITION", 0)),
            Auto("AL_XPDR_STBY", "AFTER_LANDING", "Transponder: STANDBY", "A32NX_TRANSPONDER_MODE",
                v => v < 0.5, (e, _) => e.Set("A32NX_TRANSPONDER_MODE", 0)),
            Auto("AL_STROBE_AUTO", "AFTER_LANDING", "Strobes: AUTO", "LIGHTING_STROBE_0",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("LIGHTING_STROBE_0", 1)),
            Auto("AL_LANDING_OFF", "AFTER_LANDING", "Landing lights: OFF", "LIGHTING_LANDING_2",
                v => System.Math.Abs(v - 2) < 0.5, (e, _) => e.Set("LANDING_LIGHTS_OFF_THIRD_PARTY", 1)),
            Auto("AL_NOSE_TAXI", "AFTER_LANDING", "Nose light: TAXI", "LIGHTING_LANDING_1",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("LIGHTING_LANDING_1", 1)),
            // Same master → dwell → START press as the Before Start item.
            Auto("AL_APU", "AFTER_LANDING", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v > 0.5, async (e, s) =>
                {
                    await e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1);
                    if (!s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE"))
                    {
                        await System.Threading.Tasks.Task.Delay(3000);
                        await e.Set("A32NX_OVHD_APU_START_PB_IS_ON", 1);
                    }
                }),
            Auto("AL_ANTIICE_OFF", "AFTER_LANDING", "Engine and wing anti-ice: OFF", "ENG_ANTI_ICE:1",
                v => System.Math.Abs(v - 0) < 0.5, new[] { "ENG_ANTI_ICE:2", "A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION" },
                async (e, _) =>
                {
                    await e.Set("ENG_ANTI_ICE:1", 0);
                    await e.Set("ENG_ANTI_ICE:2", 0);
                    await e.Set("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", 0);
                }),
        }
    };

    // -----------------------------------------------------------------------
    // 11. Shutdown
    // -----------------------------------------------------------------------
    private static Group BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            Auto("SD_PARKBRAKE", "SHUTDOWN", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS",
                v => v > 0.5, (e, _) => e.Set("A32NX_PARK_BRAKE_LEVER_POS", 1)),
            Auto("SD_APUBLEED_ON", "SHUTDOWN", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1)),
            Auto("SD_ENG1_OFF", "SHUTDOWN", "Engine 1 master: OFF", "ENGINE_1_MASTER",
                v => v < 0.5, (e, _) => e.Set("ENGINE_1_MASTER", 0)),
            Auto("SD_ENG2_OFF", "SHUTDOWN", "Engine 2 master: OFF", "ENGINE_2_MASTER",
                v => v < 0.5, (e, _) => e.Set("ENGINE_2_MASTER", 0)),
            Auto("SD_SEATBELTS_OFF", "SHUTDOWN", "Seatbelt signs: OFF", "CABIN SEATBELTS ALERT SWITCH",
                v => v < 0.5, (e, s) => !s.IsOn("CABIN SEATBELTS ALERT SWITCH")
                    ? Task.CompletedTask : e.Set("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 0)),
            Auto("SD_BEACON_OFF", "SHUTDOWN", "Beacon: OFF", "LIGHT BEACON",
                v => v < 0.5, (e, _) => e.Set("BEACON_LIGHTS_SET", 0)),
            Auto("SD_FUELPUMPS_OFF", "SHUTDOWN", "Fuel pumps: ALL OFF", "FUEL_PUMP_L1", v => v < 0.5,
                new[] { "FUEL_PUMP_L2", "FUEL_PUMP_C1", "FUEL_PUMP_C2", "FUEL_PUMP_R1", "FUEL_PUMP_R2" },
                async (e, _) =>
                {
                    await e.Set("FUEL_PUMP_L1", 0);
                    await e.Set("FUEL_PUMP_L2", 0);
                    await e.Set("FUEL_PUMP_C1", 0);
                    await e.Set("FUEL_PUMP_C2", 0);
                    await e.Set("FUEL_PUMP_R1", 0);
                    await e.Set("FUEL_PUMP_R2", 0);
                }),
            Auto("SD_NOSE_OFF", "SHUTDOWN", "Nose light: OFF", "LIGHTING_LANDING_1",
                v => System.Math.Abs(v - 2) < 0.5, (e, _) => e.Set("LIGHTING_LANDING_1", 2)),
            Auto("SD_TURNOFF_OFF", "SHUTDOWN", "Runway turn-off lights: OFF", "LIGHT TAXI:2",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHT TAXI:2", 0)),
            Auto("SD_COCKPITDOOR", "SHUTDOWN", "Cockpit door: UNLOCKED", "A32NX_COCKPIT_DOOR_LOCKED",
                v => System.Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A32NX_COCKPIT_DOOR_LOCKED", 0)),
            // ★ Cockpit lighting: Bright for parking.
            Auto("SD_COCKPITLT", "SHUTDOWN", "Cockpit lights: ANN bright", "A32NX_OVHD_INTLT_ANN",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_OVHD_INTLT_ANN", 1)),
            Auto("SD_DOME", "SHUTDOWN", "Cockpit lights: dome bright", "A32NX_OVHD_INTLT_DOME",
                v => v >= 90, (e, _) => e.Set("A32NX_OVHD_INTLT_DOME", 100)),
            Auto("SD_STBYCOMPASS", "SHUTDOWN", "Standby compass light: ON", "A32NX_STBY_COMPASS_LIGHT_TOGGLE",
                v => v > 0.5, (e, _) => e.Set("A32NX_STBY_COMPASS_LIGHT_TOGGLE", 1)),
            ActionManual("SD_LTSCENE", "SHUTDOWN", "Panel and integral brightness: SET",
                (e, _) => e.SetCockpitLighting(FbwA320ActionExecutor.CockpitLightScene.ParkingBright)),
            ActionManual("SD_ECAMDOOR", "SHUTDOWN", "ECAM page: door", (e, _) => e.Set("ECAM_PAGE_DOOR", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Securing
    // -----------------------------------------------------------------------
    private static Group BuildSecure() => new()
    {
        Id = "SECURE", Name = "Securing",
        Items = new()
        {
            Auto("SC_ADIRS", "SECURE", "IRS 1, 2 and 3: OFF", "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
                v => v < 0.5, new[] { "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB" },
                async (e, _) =>
                {
                    await e.Set("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 0);
                    await e.Set("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 0);
                    await e.Set("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 0);
                }),
            // Crew oxygen OFF = 1 (inverted convention, see PF_OXY).
            Auto("SC_OXY", "SECURE", "Crew oxygen supply: OFF", "PUSH_OVHD_OXYGEN_CREW",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("PUSH_OVHD_OXYGEN_CREW", 1)),
            Auto("SC_EMEREXIT", "SECURE", "Emergency exit lights: OFF", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION",
                v => System.Math.Abs(v - 2) < 0.5, (e, _) => e.Set("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION", 2)),
            Auto("SC_NOSMOKE", "SECURE", "No smoking: OFF", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION",
                v => System.Math.Abs(v - 2) < 0.5, (e, _) => e.Set("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 2)),
            Auto("SC_APUBLEED", "SECURE", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
                v => v < 0.5, (e, _) => e.Set("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)),
            Auto("SC_APUMASTER", "SECURE", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v < 0.5, (e, _) => e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)),
            Auto("SC_BAT1", "SECURE", "Battery 1: OFF", "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
                v => v < 0.5, (e, _) => e.Set("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 0)),
            Auto("SC_BAT2", "SECURE", "Battery 2: OFF", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
                v => v < 0.5, (e, _) => e.Set("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 0)),
            // ★ Cockpit lighting off (full off-scene).
            Auto("SC_COCKPITLT", "SECURE", "Cockpit lights: ANN bright", "A32NX_OVHD_INTLT_ANN",
                v => System.Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_OVHD_INTLT_ANN", 1)),
            Auto("SC_DOME", "SECURE", "Cockpit lights: dome off", "A32NX_OVHD_INTLT_DOME",
                v => v <= 5, (e, _) => e.Set("A32NX_OVHD_INTLT_DOME", 0)),
            Auto("SC_STBYCOMPASS", "SECURE", "Standby compass light: OFF", "A32NX_STBY_COMPASS_LIGHT_TOGGLE",
                v => v < 0.5, (e, _) => e.Set("A32NX_STBY_COMPASS_LIGHT_TOGGLE", 0)),
            ActionManual("SC_LTSCENE", "SECURE", "Panel and integral brightness: SET",
                (e, _) => e.SetCockpitLighting(FbwA320ActionExecutor.CockpitLightScene.Off)),
        }
    };

    // -----------------------------------------------------------------------
    // Readback (*_CL) groups — challenge/response checklists.
    // HARD INVARIANT: every item here is action-free (Reminder, or Auto(..., action: null)).
    // No ActionManual, no non-null CheckAction, anywhere in a *_CL group.
    // -----------------------------------------------------------------------

    // Cockpit Preparation Checklist (Electrical Power Up + Preflight readback)
    private static Group BuildCockpitPrepCL() => new()
    {
        Id = "COCKPIT_PREP_CL", Name = "Cockpit Preparation Checklist",
        Items = new()
        {
            Reminder("CPC_PREP", "COCKPIT_PREP_CL", "Cockpit preparation: COMPLETED"),
            Reminder("CPC_FUEL", "COCKPIT_PREP_CL", "Fuel: READBACK"),
            Auto("CPC_ADIRS", "COCKPIT_PREP_CL", "ADIRS: NAV", "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
                v => System.Math.Abs(v - 1) < 0.5,
                new[] { "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB" },
                action: null),
            Reminder("CPC_ALTIMETERS", "COCKPIT_PREP_CL", "Altimeters: SET"),
            Reminder("CPC_FMGS", "COCKPIT_PREP_CL", "F.M.G.S: SET"),
        }
    };

    // Before Start Checklist
    private static Group BuildBeforeStartCL() => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = new()
        {
            Reminder("BSC_TOSPEEDS", "BEFORE_START_CL", "Takeoff speeds: READBACK"),
            Auto("BSC_SEATBELTS", "BEFORE_START_CL", "Seatbelt signs: ON", "CABIN SEATBELTS ALERT SWITCH",
                v => v > 0.5, action: null),
            Info("BSC_LINE", "BEFORE_START_CL", "— Down to the line —"),
            Reminder("BSC_DOORS", "BEFORE_START_CL", "Doors: CLOSED"),
            Auto("BSC_BEACON", "BEFORE_START_CL", "Beacon: ON", "LIGHT BEACON",
                v => v > 0.5, action: null),
            Auto("BSC_PARKBRK", "BEFORE_START_CL", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS",
                v => v > 0.5, action: null),
        }
    };

    // After Start Checklist
    private static Group BuildAfterStartCL() => new()
    {
        Id = "AFTER_START_CL", Name = "After Start Checklist",
        Items = new()
        {
            Reminder("ASC_ANTIICE", "AFTER_START_CL", "Anti-ice: SET"),
            Reminder("ASC_ECAM", "AFTER_START_CL", "ECAM status: CHECKED"),
            Reminder("ASC_TRIM", "AFTER_START_CL", "Trim: SET"),
        }
    };

    // Taxi Checklist (readback-only briefing; no matching STATE/ACTION group)
    private static Group BuildTaxiCL() => new()
    {
        Id = "TAXI_CL", Name = "Taxi Checklist",
        Items = new()
        {
            Reminder("TXC_TAXI", "TAXI_CL", "Taxi checklist"),
            Reminder("TXC_FCTEST", "TAXI_CL", "Flight control test: CHECKED"),
            Reminder("TXC_FLAPS", "TAXI_CL", "Flaps: SET"),
            Auto("TXC_WXR", "TAXI_CL", "Weather radar: ON", "XMLVAR_A320_WeatherRadar_Sys",
                v => System.Math.Abs(v - 0) < 0.5, action: null),
            Auto("TXC_ENGMODE", "TAXI_CL", "Engine mode: SET", "ENGINE_MODE_SELECTOR",
                v => System.Math.Abs(v - 1) < 0.5, action: null),
            Reminder("TXC_ECAMMEMO", "TAXI_CL", "ECAM memo: no blue, takeoff config normal"),
        }
    };

    // Lineup Checklist (readback-only briefing; no matching STATE/ACTION group)
    private static Group BuildLineupCL() => new()
    {
        Id = "LINEUP_CL", Name = "Lineup Checklist",
        Items = new()
        {
            Reminder("LUC_LINEUP", "LINEUP_CL", "Lineup checklist"),
            Reminder("LUC_RUNWAY", "LINEUP_CL", "Takeoff runway: Confirmed"),
            Auto("LUC_TCAS", "LINEUP_CL", "TCAS: SET", "A32NX_SWITCH_TCAS_POSITION",
                v => System.Math.Abs(v - 2) < 0.5, action: null),
            Reminder("LUC_PACKS", "LINEUP_CL", "Packs: SET"),
            Reminder("LUC_CABINCREW", "LINEUP_CL", "Cabin crew: NOTIFIED"),
        }
    };

    // Before Takeoff Checklist
    private static Group BuildBeforeTakeoffCL() => new()
    {
        Id = "BEFORE_TAKEOFF_CL", Name = "Before Takeoff Checklist",
        Items = new()
        {
            Reminder("BTC_FLAPS", "BEFORE_TAKEOFF_CL", "Flaps: SET"),
            Reminder("BTC_TOSPEEDS", "BEFORE_TAKEOFF_CL", "Takeoff speeds: READBACK"),
            Reminder("BTC_ALTITUDE", "BEFORE_TAKEOFF_CL", "Altitude: READBACK"),
            Info("BTC_LINE", "BEFORE_TAKEOFF_CL", "— Below the line —"),
            Auto("BTC_AUTOBRAKE", "BEFORE_TAKEOFF_CL", "Autobrake: MAX", "A32NX_AUTOBRAKES_ARMED_MODE",
                v => System.Math.Abs(v - 3) < 0.5, action: null),
            Auto("BTC_TCAS", "BEFORE_TAKEOFF_CL", "TCAS: SET", "A32NX_SWITCH_TCAS_POSITION",
                v => System.Math.Abs(v - 2) < 0.5, action: null),
        }
    };

    // Approach Checklist
    private static Group BuildApproachCL() => new()
    {
        Id = "APPROACH_CL", Name = "Approach Checklist",
        Items = new()
        {
            Reminder("APC_ALTIMETERS", "APPROACH_CL", "Altimeters: SET"),
            Auto("APC_SEATBELT", "APPROACH_CL", "Seatbelt signs: ON", "CABIN SEATBELTS ALERT SWITCH",
                v => v > 0.5, action: null),
            Reminder("APC_MINIMUMS", "APPROACH_CL", "Minimums: READBACK"),
            Auto("APC_AUTOBRAKE", "APPROACH_CL", "Autobrakes: SET", "A32NX_AUTOBRAKES_ARMED_MODE",
                v => v > 0.5, action: null),
            Auto("APC_ENGMODE", "APPROACH_CL", "Engine mode: SET", "ENGINE_MODE_SELECTOR",
                v => System.Math.Abs(v - 1) < 0.5, action: null),
        }
    };

    // Landing Checklist (readback-only briefing; no matching STATE/ACTION group)
    private static Group BuildLandingCL() => new()
    {
        Id = "LANDING_CL", Name = "Landing Checklist",
        Items = new()
        {
            Reminder("LDC_MISSEDALT", "LANDING_CL", "Missed approach altitude: SET"),
            Auto("LDC_SPOILERS", "LANDING_CL", "Spoilers: ARMED", "A32NX_SPOILERS_ARMED",
                v => v > 0.5, action: null),
        }
    };

    // After Landing Checklist
    private static Group BuildAfterLandingCL() => new()
    {
        Id = "AFTER_LANDING_CL", Name = "After Landing Checklist",
        Items = new()
        {
            Auto("ALC_WXR", "AFTER_LANDING_CL", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys",
                v => System.Math.Abs(v - 1) < 0.5, action: null),
        }
    };

    // Parking Checklist (readback-only briefing; no matching STATE/ACTION group)
    private static Group BuildParkingCL() => new()
    {
        Id = "PARKING_CL", Name = "Parking Checklist",
        Items = new()
        {
            Reminder("PKC_PARKING", "PARKING_CL", "Parking checklist"),
            Auto("PKC_PARKBRK", "PARKING_CL", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS",
                v => v > 0.5, action: null),
            Auto("PKC_ENGINES", "PARKING_CL", "Engines: OFF", "FO_ENGINES_OFF",
                v => v > 0.5, action: null),
            Auto("PKC_WINGLT", "PARKING_CL", "Wing lights: OFF", "LIGHT WING",
                v => v < 0.5, action: null),
            Auto("PKC_FUELPUMPS", "PARKING_CL", "Fuel pumps: OFF", "FUEL_PUMP_L1",
                v => v < 0.5, action: null),
        }
    };

    // Securing the Aircraft Checklist
    private static Group BuildSecuringCL() => new()
    {
        Id = "SECURING_CL", Name = "Securing the Aircraft Checklist",
        Items = new()
        {
            Auto("SCC_ADIRS", "SECURING_CL", "ADIRS: OFF", "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
                v => v < 0.5, new[] { "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB" },
                action: null),
            Auto("SCC_OXY", "SECURING_CL", "Oxygen: OFF", "PUSH_OVHD_OXYGEN_CREW",
                v => System.Math.Abs(v - 1) < 0.5, action: null),
            Auto("SCC_PARK", "SECURING_CL", "Parking brake: SET", "A32NX_PARK_BRAKE_LEVER_POS",
                v => v > 0.5, action: null),
            Auto("SCC_APU", "SECURING_CL", "APU: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v < 0.5, action: null),
            Auto("SCC_BAT", "SECURING_CL", "Batteries 1 and 2: OFF", "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
                v => v < 0.5, new[] { "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO" }, action: null),
        }
    };

    // -----------------------------------------------------------------------
    // Item builders (mirror the Fenix/A380 files' helpers, retyped to FbwA320ActionExecutor/FbwA320StateEvaluator)
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

    private static Item Multi(string id, string groupId, string label,
        string primaryField, Func<double, bool> condition,
        string[] additionalFields, CheckFn action) =>
        Auto(id, groupId, label, primaryField, condition, additionalFields, action);

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
        // Separators are structure, not work items: not tickable (and the FO window
        // hides their checkbox), so they can never inflate CompletedCount. Matches
        // the Fenix/A380 Info builder — keep all three in sync.
        ManualCompletionAllowed = false,
    };
}
