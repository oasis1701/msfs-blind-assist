using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

using Item = Models.ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>;
using Group = Models.ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator>;
using CheckFn = System.Func<FbwA380ActionExecutor, FbwA380StateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// Data-driven FlyByWire A380 First-Officer checklist definitions — the STATE/ACTION layer
/// that mirrors <see cref="FbwA380FlowDefinitions"/> 1:1. Every drivable flow step becomes an
/// <c>Auto</c> item whose tick fires the SAME write the flow uses and whose auto-detect
/// condition mirrors the flow's own Skip condition; every Captain flow step becomes a
/// <see cref="Reminder"/>. Readback (*_CL) groups are added to <see cref="Build"/> by Task 7.
///
/// Every state item is RevertToState (live mirror), matching the Fenix/PMDG convention. State
/// reads come from the SimConnect L:var cache via <see cref="FbwA380StateEvaluator"/> —
/// OnRequest vars are polled by the FO window via OnRequestPollFields; an uncached var reads
/// NaN (indeterminate: no tick, no revert).
/// </summary>
public static class FbwA380ChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        BuildCockpitPrep(),
        BuildCockpitPrepCL(),
        BuildBeforeStart(),
        BuildBeforeStartCL(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildAfterStartCL(),
        BuildTaxi(),
        BuildTaxiCL(),
        BuildLineup(),
        BuildLineupCL(),
        BuildBeforeTakeoffCL(),
        BuildAfterTakeoff(),
        BuildApproach(),
        BuildApproachCL(),
        BuildLanding(),
        BuildLandingCL(),
        BuildAfterLanding(),
        BuildAfterLandingCL(),
        BuildParking(),
        BuildParkingCL(),
    };

    // -----------------------------------------------------------------------
    // 1. Cockpit Preparation
    // -----------------------------------------------------------------------
    private static Group BuildCockpitPrep() => new()
    {
        Id = "COCKPIT_PREP", Name = "Cockpit Preparation",
        Items = new()
        {
            Reminder("CP_WIPERS", "COCKPIT_PREP", "Wipers off"),
            Auto("CP_GEAR", "COCKPIT_PREP", "Gear lever: DOWN", "A32NX_GEAR_HANDLE_POSITION",
                v => v > 0.5, (e, _) => e.Set("A32NX_GEAR_HANDLE_POSITION", 1)),
            Reminder("CP_FLAPS", "COCKPIT_PREP", "Flaps: confirm up"),
            Auto("CP_SPOILERS", "COCKPIT_PREP", "Spoilers: disarmed", "A32NX_SPOILERS_ARMED",
                v => v < 0.5, (e, _) => e.Set("A380X_MSFSBA_SPOILERS_ARM", 0)),   // detect on real state, write the Act key
            Auto("CP_PARKBRK", "COCKPIT_PREP", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS",
                v => v > 0.5, (e, _) => e.Set("A32NX_PARK_BRAKE_LEVER_POS", 1)),
            Reminder("CP_THROTTLES", "COCKPIT_PREP", "Confirm thrust levers idle"),
            Auto("CP_ENGMODE", "COCKPIT_PREP", "Engine mode: NORM", "ENGINE_MODE_SELECTOR",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("ENGINE_MODE_SELECTOR", 1)),
            Multi("CP_MASTERS_OFF", "COCKPIT_PREP", "Engine masters 1 to 4: OFF",
                "ENG_VALVE_SWITCH:1", v => v < 0.5,
                new[] { "ENG_VALVE_SWITCH:2", "ENG_VALVE_SWITCH:3", "ENG_VALVE_SWITCH:4" },
                async (e, _) =>
                {
                    await e.Set("ENG_VALVE_SWITCH:1", 0);
                    await e.Set("ENG_VALVE_SWITCH:2", 0);
                    await e.Set("ENG_VALVE_SWITCH:3", 0);
                    await e.Set("ENG_VALVE_SWITCH:4", 0);
                }),
            Auto("CP_WXR", "COCKPIT_PREP", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("XMLVAR_A320_WeatherRadar_Sys", 0)),
            // All FOUR A380 batteries (BAT 1/2/ESS/APU) — the panel exposes all four; the
            // earlier 1+2-only item left ESS + APU off yet still reported "Batteries: ON".
            Multi("CP_BAT", "COCKPIT_PREP", "Batteries: ON", "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO",
                v => v > 0.5,
                new[] { "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO",
                        "A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO" },
                async (e, _) => { await e.Set("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 1);
                                  await e.Set("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 1);
                                  await e.Set("A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", 1);
                                  await e.Set("A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO", 1); }),
            // Annunciator + integral lights BRIGHT (1) — SOP "as required", Bright chosen
            // for a deterministic ground-prep setting (0=Test/1=Bright/2=Dim).
            Multi("CP_COCKPITLT", "COCKPIT_PREP", "Cockpit lights: set", "A380X_OVHD_ANN_LT_POSITION",
                v => Math.Abs(v - 1) < 0.5, new[] { "A32NX_OVHD_INTLT_ANN" },
                async (e, _) => { await e.Set("A380X_OVHD_ANN_LT_POSITION", 1);
                                  await e.Set("A32NX_OVHD_INTLT_ANN", 1); }),
            Multi("CP_GPU", "COCKPIT_PREP", "Ground power: ON",
                "A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", v => v > 0.5,
                new[] { "A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON",
                        "A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON" },
                async (e, _) =>
                {
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 1);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 1);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 1);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 1);
                }),
            Reminder("CP_INSTRBRT", "COCKPIT_PREP", "Instrument brightness: high"),
            Auto("CP_ADIRS", "COCKPIT_PREP", "ADIRS: NAV", "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
                v => Math.Abs(v - 1) < 0.5,
                new[] { "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB" },
                async (e, _) =>
                {
                    await e.Set("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 1);
                    await e.Set("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 1);
                    await e.Set("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 1);
                }),
            Auto("CP_OXY", "COCKPIT_PREP", "Crew oxygen: ON", "PUSH_OVHD_OXYGEN_CREW",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("PUSH_OVHD_OXYGEN_CREW", 0)),
            // Manual-tick held test (no persistent "test performed" state; ticking runs
            // the same held test the flow runs — Fenix PF_FIRE_* pattern).
            ActionManual("CP_FIRETEST", "COCKPIT_PREP", "Fire test",
                (e, _) => e.FireTestAsync()),
            // Recorder ground control — writable bool (ground-only; reverts in flight). Manual
            // tick fires the write, matching the flow's CP_GNDCTL automation.
            ActionManual("CP_GNDCTL", "COCKPIT_PREP", "Ground control: on",
                (e, _) => e.Set("A32NX_RCDR_GROUND_CONTROL_ON", 1)),
            Multi("CP_NAVLOGO", "COCKPIT_PREP", "Nav and logo lights: ON", "LIGHT_NAV", v => v > 0.5,
                new[] { "LIGHT_LOGO" },
                async (e, _) => { await e.Set("LIGHT_NAV", 1); await e.Set("LIGHT_LOGO", 1); }),
            // Seat belts is a 3-position SWITCH (SEATBELT_SIGN 0=On/1=Auto/2=Off); detect on
            // the actual sign illumination (SEATBELT_SIGN_LIGHT) so AUTO with the sign lit
            // also counts, and write the switch to the ON position.
            Auto("CP_SEATBELT", "COCKPIT_PREP", "Seatbelt signs: ON", "SEATBELT_SIGN_LIGHT",
                v => v > 0.5, (e, _) => e.Set("SEATBELT_SIGN", 0)),
            Auto("CP_NOSMOKE", "COCKPIT_PREP", "No smoking signs: AUTO", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", 1)),
            Auto("CP_EMEREXIT", "COCKPIT_PREP", "Emergency exit lighting: ARM", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", 1)),
            Reminder("CP_LTSTEST", "COCKPIT_PREP", "Lights test"),
            Auto("CP_WINGAI", "COCKPIT_PREP", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("WING_ANTI_ICE_OVHD", 0)),
            Auto("CP_WINGLT", "COCKPIT_PREP", "Wing lights: OFF", "LIGHT_WING",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHT_WING", 0)),
            Multi("CP_PACKS", "COCKPIT_PREP", "Packs: ON", "A32NX_OVHD_COND_PACK_1_PB_IS_ON", v => v > 0.5,
                new[] { "A32NX_OVHD_COND_PACK_2_PB_IS_ON" },
                async (e, _) => { await e.Set("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1);
                                  await e.Set("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 1); }),
            Auto("CP_XBLEED", "COCKPIT_PREP", "Crossbleed: AUTO", "A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", 1)),
            Auto("CP_PACKFLOW", "COCKPIT_PREP", "Pack flow: NORMAL", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION",
                v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION", 2)),
            Multi("CP_HOTAIR", "COCKPIT_PREP", "Hot air: ON", "A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON", v => v > 0.5,
                new[] { "A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON" },
                async (e, _) => { await e.Set("A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON", 1);
                                  await e.Set("A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON", 1); }),
            Reminder("CP_AIRTEMP", "COCKPIT_PREP", "Cabin temperature: set as required"),
            Multi("CP_BARO", "COCKPIT_PREP", "Baro reference: hectopascals", "XMLVAR_Baro_Selector_HPA_1", v => v > 0.5,
                new[] { "XMLVAR_Baro_Selector_HPA_2" },
                async (e, _) => { await e.Set("XMLVAR_Baro_Selector_HPA_1", 1);
                                  await e.Set("XMLVAR_Baro_Selector_HPA_2", 1); }),
            Reminder("CP_ALTIMETERS", "COCKPIT_PREP", "Altimeters: set QNH"),
            Auto("CP_ANTISKID", "COCKPIT_PREP", "Anti-skid: ON", "ANTISKID_BRAKES_ACTIVE",
                v => v > 0.5, (e, _) => e.Set("ANTISKID_BRAKES_ACTIVE", 1)),
            Reminder("CP_ALTRPT", "COCKPIT_PREP", "Altitude reporting: on"),
            Reminder("CP_TCASTRAFFIC", "COCKPIT_PREP", "TCAS traffic: all"),
            Reminder("CP_TCASMODE", "COCKPIT_PREP", "TCAS mode: standby"),
            Reminder("CP_XPDR", "COCKPIT_PREP", "Transponder: standby"),
            Multi("CP_EFISMODE", "COCKPIT_PREP", "EFIS mode: ARC", "A32NX_EFIS_L_ND_MODE", v => Math.Abs(v - 3) < 0.5,
                new[] { "A32NX_EFIS_R_ND_MODE" },
                async (e, _) => { await e.Set("A32NX_EFIS_L_ND_MODE", 3); await e.Set("A32NX_EFIS_R_ND_MODE", 3); }),
            Multi("CP_EFISRANGE", "COCKPIT_PREP", "EFIS range: 40", "A32NX_EFIS_L_ND_RANGE", v => Math.Abs(v - 3) < 0.5,
                new[] { "A32NX_EFIS_R_ND_RANGE" },
                async (e, _) => { await e.Set("A32NX_EFIS_L_ND_RANGE", 3); await e.Set("A32NX_EFIS_R_ND_RANGE", 3); }),
            Multi("CP_FD", "COCKPIT_PREP", "Flight directors: ON", "FD_1_CTL", v => v > 0.5,
                new[] { "FD_2_CTL" },
                async (e, _) => { await e.Set("FD_1_CTL", 1); await e.Set("FD_2_CTL", 1); }),
            Reminder("CP_CLOCK", "COCKPIT_PREP", "Clock: reset"),
            // ECAM SD page — real ECP write (door=5). Auto/RevertToState live mirror; the
            // page index sticks on this build (re-checked live 2026-06-13).
            Auto("CP_ECAMPAGE", "COCKPIT_PREP", "ECAM page: door", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX",
                v => Math.Abs(v - 5) < 0.5, (e, _) => e.Set("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 5)),
            Reminder("CP_IFR", "COCKPIT_PREP", "Obtain IFR clearance"),
            Reminder("CP_PAYLOAD", "COCKPIT_PREP", "Load payload on the EFB"),
            Reminder("CP_MCDU", "COCKPIT_PREP", "Program the MCDU"),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Before Start
    // -----------------------------------------------------------------------
    private static Group BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            Auto("BS_COCKPITDOOR", "BEFORE_START", "Cockpit door: LOCKED", "A32NX_COCKPIT_DOOR_LOCKED",
                v => v > 0.5, (e, _) => e.Set("A32NX_COCKPIT_DOOR_LOCKED", 1)),
            ActionManual("BS_FCUSPD", "BEFORE_START", "FCU speed: managed",
                (e, _) => e.Set("FCU_PUSH_SPEED", 1)),
            ActionManual("BS_FCUHDG", "BEFORE_START", "FCU heading: managed",
                (e, _) => e.Set("FCU_PUSH_HEADING", 1)),
            Reminder("BS_ALT", "BEFORE_START", "Set cleared altitude on the FCU"),
            ActionManual("BS_FCUALT", "BEFORE_START", "FCU altitude: pushed",
                (e, _) => e.Set("FCU_PUSH_ALT", 1)),
            Auto("BS_ECAMPAGE", "BEFORE_START", "ECAM page: APU", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 1)),
            // Master alone does not start the FBW APU — the tick also presses the START
            // PB (unless AVAIL is already lit). Auto-detect stays master-based; the
            // checklist is pilot-paced, so there is no inline AVAIL wait (the After Start
            // flow's AS_APU_START_OFF step releases a still-latched START PB).
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
            Multi("BS_GPU_OFF", "BEFORE_START", "Ground power: OFF", "A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON",
                v => v < 0.5,
                new[] { "A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON",
                        "A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON" },
                async (e, _) =>
                {
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0);
                }),
            Reminder("BS_GPU_DISC", "BEFORE_START", "Disconnect ground power on the EFB"),
            // ALL 20 pumps ON — 8 feed + 12 transfer (outer/mid/inner/trim). Transfer pumps are
            // manual on/off pumps like the feed pumps; FBW's POWERUP_CONFIG preset turns all 20
            // on and A380 SOP runs the transfer pumps continuously (see the flow's BS_FUELPUMPS).
            Multi("BS_FUELPUMPS", "BEFORE_START", "Fuel pumps: ON", "FUELPUMP_FEEDTK1_MAIN", v => v > 0.5,
                new[]
                {
                    "FUELPUMP_FEEDTK1_STBY", "FUELPUMP_FEEDTK2_MAIN", "FUELPUMP_FEEDTK2_STBY",
                    "FUELPUMP_FEEDTK3_MAIN", "FUELPUMP_FEEDTK3_STBY", "FUELPUMP_FEEDTK4_MAIN", "FUELPUMP_FEEDTK4_STBY",
                    "FUELPUMP_OUTR_L", "FUELPUMP_MID_L_FWD", "FUELPUMP_MID_L_AFT",
                    "FUELPUMP_INR_L_FWD", "FUELPUMP_INR_L_AFT",
                    "FUELPUMP_OUTR_R", "FUELPUMP_MID_R_FWD", "FUELPUMP_MID_R_AFT",
                    "FUELPUMP_INR_R_FWD", "FUELPUMP_INR_R_AFT",
                    "FUELPUMP_TRIM_L", "FUELPUMP_TRIM_R",
                },
                async (e, _) =>
                {
                    await e.Set("FUELPUMP_FEEDTK1_MAIN", 1);
                    await e.Set("FUELPUMP_FEEDTK1_STBY", 1);
                    await e.Set("FUELPUMP_FEEDTK2_MAIN", 1);
                    await e.Set("FUELPUMP_FEEDTK2_STBY", 1);
                    await e.Set("FUELPUMP_FEEDTK3_MAIN", 1);
                    await e.Set("FUELPUMP_FEEDTK3_STBY", 1);
                    await e.Set("FUELPUMP_FEEDTK4_MAIN", 1);
                    await e.Set("FUELPUMP_FEEDTK4_STBY", 1);
                    await e.Set("FUELPUMP_OUTR_L", 1);
                    await e.Set("FUELPUMP_MID_L_FWD", 1);
                    await e.Set("FUELPUMP_MID_L_AFT", 1);
                    await e.Set("FUELPUMP_INR_L_FWD", 1);
                    await e.Set("FUELPUMP_INR_L_AFT", 1);
                    await e.Set("FUELPUMP_OUTR_R", 1);
                    await e.Set("FUELPUMP_MID_R_FWD", 1);
                    await e.Set("FUELPUMP_MID_R_AFT", 1);
                    await e.Set("FUELPUMP_INR_R_FWD", 1);
                    await e.Set("FUELPUMP_INR_R_AFT", 1);
                    await e.Set("FUELPUMP_TRIM_L", 1);
                    await e.Set("FUELPUMP_TRIM_R", 1);
                }),
            Auto("BS_BEACON", "BEFORE_START", "Beacon lights: ON", "LIGHT_BEACON",
                v => v > 0.5, (e, _) => e.Set("LIGHT_BEACON", 1)),
            Reminder("BS_THROTTLES", "BEFORE_START", "Confirm thrust levers idle"),
            Auto("BS_PARKBRK", "BEFORE_START", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS",
                v => v > 0.5, (e, _) => e.Set("A32NX_PARK_BRAKE_LEVER_POS", 1)),
            Reminder("BS_XPDR", "BEFORE_START", "Transponder: set for departure"),
            Reminder("BS_TCAS", "BEFORE_START", "TCAS mode: TA/RA"),
            Reminder("BS_MCDUPERF", "BEFORE_START", "Program the MCDU PERF page"),
            Reminder("BS_TRIM", "BEFORE_START", "Set trim"),
            Reminder("BS_ACARS", "BEFORE_START", "Start ACARS"),
            Reminder("BS_TAXICLR", "BEFORE_START", "Obtain pushback and start clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 3. Engine Start
    // -----------------------------------------------------------------------
    private static Group BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            Auto("ES_ECAMPAGE", "ENGINE_START", "ECAM page: engine", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 0)),
            Auto("ES_APUBLEED", "ENGINE_START", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1)),
            Auto("ES_ENGMODE", "ENGINE_START", "Engine mode: IGN", "ENGINE_MODE_SELECTOR",
                v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("ENGINE_MODE_SELECTOR", 2)),
            Auto("ES_ENG1", "ENGINE_START", "Engine 1 master: START", "ENG_VALVE_SWITCH:1",
                v => v > 0.5, (e, _) => e.Set("ENG_VALVE_SWITCH:1", 1)),
            Auto("ES_ENG2", "ENGINE_START", "Engine 2 master: START", "ENG_VALVE_SWITCH:2",
                v => v > 0.5, (e, _) => e.Set("ENG_VALVE_SWITCH:2", 1)),
            Auto("ES_ENG3", "ENGINE_START", "Engine 3 master: START", "ENG_VALVE_SWITCH:3",
                v => v > 0.5, (e, _) => e.Set("ENG_VALVE_SWITCH:3", 1)),
            Auto("ES_ENG4", "ENGINE_START", "Engine 4 master: START", "ENG_VALVE_SWITCH:4",
                v => v > 0.5, (e, _) => e.Set("ENG_VALVE_SWITCH:4", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 4. After Start
    // -----------------------------------------------------------------------
    private static Group BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Items = new()
        {
            Auto("AS_ENGMODE", "AFTER_START", "Engine mode: NORM", "ENGINE_MODE_SELECTOR",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("ENGINE_MODE_SELECTOR", 1)),
            Reminder("AS_WINGAI", "AFTER_START", "Wing anti-ice: set as required"),
            Reminder("AS_ENGAI", "AFTER_START", "Engine anti-ice: set as required"),
            Auto("AS_ECAMPAGE", "AFTER_START", "ECAM page: APU", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 1)),
            Auto("AS_APU_OFF", "AFTER_START", "APU: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)),
            Auto("AS_APUBLEED_OFF", "AFTER_START", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)),
            // Nose light is the faithful 3-position selector (NOSE_LIGHT: 0=T.O./1=Taxi/2=Off)
            // since the PR #139 API audit — the old on/off LIGHT_TAXI_OVHD key is gone.
            Auto("AS_NOSE_TAXI", "AFTER_START", "Nose light: TAXI", "NOSE_LIGHT",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("NOSE_LIGHT", 1)),
            // DIM (2) for taxi/flight — ANN LT has no true OFF.
            Multi("AS_COCKPITLT", "AFTER_START", "Cockpit lights: off", "A380X_OVHD_ANN_LT_POSITION",
                v => Math.Abs(v - 2) < 0.5, new[] { "A32NX_OVHD_INTLT_ANN" },
                async (e, _) => { await e.Set("A380X_OVHD_ANN_LT_POSITION", 2);
                                  await e.Set("A32NX_OVHD_INTLT_ANN", 2); }),
            Auto("AS_SPOILERS_ARM", "AFTER_START", "Spoilers: ARMED", "A32NX_SPOILERS_ARMED",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A380X_MSFSBA_SPOILERS_ARM", 1)),
            ActionManual("AS_RUDDERTRIM", "AFTER_START", "Rudder trim: RESET",
                (e, _) => e.Set("A32NX_RUDDER_TRIM_RESET", 1)),
            Reminder("AS_FLAPS", "AFTER_START", "Flaps: set for takeoff"),
        }
    };

    // -----------------------------------------------------------------------
    // 5. Taxi
    // -----------------------------------------------------------------------
    private static Group BuildTaxi() => new()
    {
        Id = "TAXI", Name = "Taxi",
        Items = new()
        {
            // RTO arm: detect on the LATCHED armed state (the _IS_PRESSED L:var is a
            // momentary that auto-resets to 0, so it could never hold a tick — and
            // RevertToState would un-tick it seconds after arming). Guarded action:
            // a retick while already armed must not re-press — a second press disarms.
            Auto("TX_AUTOBRAKE", "TAXI", "Autobrake: MAX", "A32NX_AUTOBRAKES_RTO_ARMED",
                v => v > 0.5, (e, s) => s.IsOn("A32NX_AUTOBRAKES_RTO_ARMED")
                    ? Task.CompletedTask
                    : e.Set("A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED", 1)),
            Auto("TX_ENGMODE", "TAXI", "Engine mode: NORM", "ENGINE_MODE_SELECTOR",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("ENGINE_MODE_SELECTOR", 1)),
            Auto("TX_WXR", "TAXI", "Weather radar: ON", "XMLVAR_A320_WeatherRadar_Sys",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("XMLVAR_A320_WeatherRadar_Sys", 1)),
            Auto("TX_PWS", "TAXI", "Predictive windshear: ON", "A32NX_SWITCH_RADAR_PWS_Position",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A32NX_SWITCH_RADAR_PWS_Position", 1)),
            Reminder("TX_FLAPS", "TAXI", "Flaps: set for takeoff"),
            Auto("TX_ECAMPAGE", "TAXI", "ECAM page: flight controls", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX",
                v => Math.Abs(v - 11) < 0.5, (e, _) => e.Set("A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 11)),
            // Takeoff config test — held ECP button (no persistent state, so ActionManual:
            // ticking runs the press/release test; mirrors the flow's TX_CONFIG).
            ActionManual("TX_CONFIG", "TAXI", "Takeoff config test", (e, _) => e.TakeoffConfigTest()),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Lineup
    // -----------------------------------------------------------------------
    private static Group BuildLineup() => new()
    {
        Id = "LINEUP", Name = "Lineup",
        Items = new()
        {
            Reminder("LU_TCAS", "LINEUP", "TCAS mode: TA/RA"),
            Reminder("LU_PACKS", "LINEUP", "Packs: set for takeoff as required"),
            Auto("LU_STROBE", "LINEUP", "Strobe lights: ON", "LIGHT_STROBE",
                v => v > 0.5, (e, _) => e.Set("LIGHT_STROBE", 1)),
            // Landing lights = wing LDG LT (LIGHT_LANDING 0/1); the nose T.O. beam is the
            // separate NOSE_LIGHT selector (0=T.O.). Detection keys on the wing landing
            // lights (the additional-fields condition is shared, and NOSE_LIGHT's T.O.
            // encoding is 0, so it can't share a v>0.5 test); the action drives both.
            Auto("LU_LANDING", "LINEUP", "Landing and nose lights: ON", "LIGHT_LANDING",
                v => v > 0.5, async (e, _) =>
                {
                    await e.Set("LIGHT_LANDING", 1);
                    await e.Set("NOSE_LIGHT", 0);   // T.O.
                }),
            Auto("LU_TURNOFF", "LINEUP", "Runway turn-off lights: ON", "LIGHT_RWY_TURNOFF",
                v => v > 0.5, (e, _) => e.Set("LIGHT_RWY_TURNOFF", 1)),
            // Advise the cabin crew for takeoff — momentary CALLS ALL chime (no persistent
            // state, so ActionManual: ticking pulses the button; no auto-detect / revert).
            ActionManual("LU_CABIN", "LINEUP", "Advise the cabin crew for takeoff (call all)",
                (e, _) => e.Set("A380X_MSFSBA_SIGNAL_CABIN_READY", 1)),
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
            Auto("AT_SPOILERS_DISARM", "AFTER_TAKEOFF", "Spoilers: DISARM", "A32NX_SPOILERS_ARMED",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A380X_MSFSBA_SPOILERS_ARM", 0)),
            // Autobrake disarm — consolidated here from the former Climb phase (2026-07-12).
            Auto("AT_AUTOBRAKE", "AFTER_TAKEOFF", "Autobrake: disarm", "A32NX_AUTOBRAKES_SELECTED_MODE",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A32NX_AUTOBRAKES_SELECTED_MODE", 0)),
            Auto("AT_NOSE_TAXI", "AFTER_TAKEOFF", "Nose light: TAXI", "NOSE_LIGHT",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("NOSE_LIGHT", 1)),
            Auto("AT_TURNOFF_OFF", "AFTER_TAKEOFF", "Runway turn-off lights: OFF", "LIGHT_RWY_TURNOFF",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHT_RWY_TURNOFF", 0)),
        }
    };

    // -----------------------------------------------------------------------
    // 8. Approach
    // -----------------------------------------------------------------------
    private static Group BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = new()
        {
            Reminder("AP_AUTOBRAKE", "APPROACH", "Set the landing autobrake — Instrument section, Autobrake panel"),
            Auto("AP_SEATBELTS", "APPROACH", "Seatbelt signs: ON", "SEATBELT_SIGN_LIGHT",
                v => v > 0.5, (e, _) => e.Set("SEATBELT_SIGN", 0)),
            Multi("AP_EFISMODE", "APPROACH", "EFIS mode: ILS", "A32NX_EFIS_L_ND_MODE", v => Math.Abs(v - 0) < 0.5,
                new[] { "A32NX_EFIS_R_ND_MODE" },
                async (e, _) => { await e.Set("A32NX_EFIS_L_ND_MODE", 0); await e.Set("A32NX_EFIS_R_ND_MODE", 0); }),
            // Notify the cabin crew for landing — momentary CALLS ALL chime (Fenix parity).
            ActionManual("AP_CABIN", "APPROACH", "Notify the cabin crew for landing (call all)",
                (e, _) => e.Set("A380X_MSFSBA_SIGNAL_CABIN_READY", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 10. Landing
    // -----------------------------------------------------------------------
    private static Group BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Items = new()
        {
            Reminder("LD_MISSEDALT", "LANDING", "Set missed approach altitude"),
            Auto("LD_SPOILERS_ARM", "LANDING", "Spoilers: ARMED", "A32NX_SPOILERS_ARMED",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A380X_MSFSBA_SPOILERS_ARM", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 11. After Landing
    // -----------------------------------------------------------------------
    private static Group BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_WXR_OFF", "AFTER_LANDING", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("XMLVAR_A320_WeatherRadar_Sys", 0)),
            Auto("AL_PWS_OFF", "AFTER_LANDING", "Predictive windshear: OFF", "A32NX_SWITCH_RADAR_PWS_Position",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A32NX_SWITCH_RADAR_PWS_Position", 0)),
            Auto("AL_ENGMODE", "AFTER_LANDING", "Engine mode: NORM", "ENGINE_MODE_SELECTOR",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("ENGINE_MODE_SELECTOR", 1)),
            Reminder("AL_FLAPS_UP", "AFTER_LANDING", "Flaps: up"),
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
            // Engine anti-ice: ENGn_ANTI_ICE are write-only Act() keys (no backing
            // L:var — reading them returns a stale 0). Detect on the stock readout
            // ENG_ANTI_ICE:n (ENG ANTI ICE:n) instead; keep writing the Act keys.
            Multi("AL_ENGAI_OFF", "AFTER_LANDING", "Engine anti-ice: OFF", "ENG_ANTI_ICE:1", v => Math.Abs(v - 0) < 0.5,
                new[] { "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4" },
                async (e, _) =>
                {
                    await e.Set("ENG1_ANTI_ICE", 0);
                    await e.Set("ENG2_ANTI_ICE", 0);
                    await e.Set("ENG3_ANTI_ICE", 0);
                    await e.Set("ENG4_ANTI_ICE", 0);
                }),
            Auto("AL_WINGAI_OFF", "AFTER_LANDING", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("WING_ANTI_ICE_OVHD", 0)),
            Auto("AL_SPOILERS_OFF", "AFTER_LANDING", "Spoilers: OFF", "A32NX_SPOILERS_ARMED",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A380X_MSFSBA_SPOILERS_ARM", 0)),
            Auto("AL_LANDING_OFF", "AFTER_LANDING", "Landing lights: OFF", "LIGHT_LANDING",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHT_LANDING", 0)),
            Auto("AL_STROBE_OFF", "AFTER_LANDING", "Strobe lights: OFF", "LIGHT_STROBE",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHT_STROBE", 0)),
            Auto("AL_NOSE_TAXI", "AFTER_LANDING", "Nose light: TAXI", "NOSE_LIGHT",
                v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("NOSE_LIGHT", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Parking
    // -----------------------------------------------------------------------
    private static Group BuildParking() => new()
    {
        Id = "PARKING", Name = "Parking",
        Items = new()
        {
            Auto("PK_PARKBRK", "PARKING", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS",
                v => v > 0.5, (e, _) => e.Set("A32NX_PARK_BRAKE_LEVER_POS", 1)),
            Multi("PK_APU_GEN", "PARKING", "APU generators: ON", "ELEC_APU_GEN:1", v => v > 0.5,
                new[] { "ELEC_APU_GEN:2" },
                async (e, _) => { await e.Set("ELEC_APU_GEN:1", 1); await e.Set("ELEC_APU_GEN:2", 1); }),
            Multi("PK_MASTERS_OFF", "PARKING", "Engine masters 1 to 4: OFF", "ENG_VALVE_SWITCH:1", v => v < 0.5,
                new[] { "ENG_VALVE_SWITCH:2", "ENG_VALVE_SWITCH:3", "ENG_VALVE_SWITCH:4" },
                async (e, _) =>
                {
                    await e.Set("ENG_VALVE_SWITCH:1", 0);
                    await e.Set("ENG_VALVE_SWITCH:2", 0);
                    await e.Set("ENG_VALVE_SWITCH:3", 0);
                    await e.Set("ENG_VALVE_SWITCH:4", 0);
                }),
            Multi("PK_COCKPITLT", "PARKING", "Cockpit lights: set", "A380X_OVHD_ANN_LT_POSITION",
                v => Math.Abs(v - 1) < 0.5, new[] { "A32NX_OVHD_INTLT_ANN" },
                async (e, _) => { await e.Set("A380X_OVHD_ANN_LT_POSITION", 1);
                                  await e.Set("A32NX_OVHD_INTLT_ANN", 1); }),
            Auto("PK_BEACON_OFF", "PARKING", "Beacon lights: OFF", "LIGHT_BEACON",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHT_BEACON", 0)),
            Auto("PK_WINGLT_OFF", "PARKING", "Wing lights: OFF", "LIGHT_WING",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("LIGHT_WING", 0)),
            Auto("PK_NOSE_OFF", "PARKING", "Nose light: OFF", "NOSE_LIGHT",
                v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("NOSE_LIGHT", 2)),
            // Detect on the stock ENG_ANTI_ICE:n readouts, write the Act keys (see AL_ENGAI_OFF).
            Multi("PK_ENGAI_OFF", "PARKING", "Engine anti-ice: OFF", "ENG_ANTI_ICE:1", v => Math.Abs(v - 0) < 0.5,
                new[] { "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4" },
                async (e, _) =>
                {
                    await e.Set("ENG1_ANTI_ICE", 0);
                    await e.Set("ENG2_ANTI_ICE", 0);
                    await e.Set("ENG3_ANTI_ICE", 0);
                    await e.Set("ENG4_ANTI_ICE", 0);
                }),
            Auto("PK_WINGAI_OFF", "PARKING", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("WING_ANTI_ICE_OVHD", 0)),
            Auto("PK_APUBLEED", "PARKING", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1)),
            // ALL 20 pumps OFF — mirrors POWERUP_CONFIG_OFF (feed + transfer).
            Multi("PK_FUELPUMPS_OFF", "PARKING", "Fuel pumps: OFF", "FUELPUMP_FEEDTK1_MAIN", v => v < 0.5,
                new[]
                {
                    "FUELPUMP_FEEDTK1_STBY", "FUELPUMP_FEEDTK2_MAIN", "FUELPUMP_FEEDTK2_STBY",
                    "FUELPUMP_FEEDTK3_MAIN", "FUELPUMP_FEEDTK3_STBY", "FUELPUMP_FEEDTK4_MAIN", "FUELPUMP_FEEDTK4_STBY",
                    "FUELPUMP_OUTR_L", "FUELPUMP_MID_L_FWD", "FUELPUMP_MID_L_AFT",
                    "FUELPUMP_INR_L_FWD", "FUELPUMP_INR_L_AFT",
                    "FUELPUMP_OUTR_R", "FUELPUMP_MID_R_FWD", "FUELPUMP_MID_R_AFT",
                    "FUELPUMP_INR_R_FWD", "FUELPUMP_INR_R_AFT",
                    "FUELPUMP_TRIM_L", "FUELPUMP_TRIM_R",
                },
                async (e, _) =>
                {
                    await e.Set("FUELPUMP_FEEDTK1_MAIN", 0);
                    await e.Set("FUELPUMP_FEEDTK1_STBY", 0);
                    await e.Set("FUELPUMP_FEEDTK2_MAIN", 0);
                    await e.Set("FUELPUMP_FEEDTK2_STBY", 0);
                    await e.Set("FUELPUMP_FEEDTK3_MAIN", 0);
                    await e.Set("FUELPUMP_FEEDTK3_STBY", 0);
                    await e.Set("FUELPUMP_FEEDTK4_MAIN", 0);
                    await e.Set("FUELPUMP_FEEDTK4_STBY", 0);
                    await e.Set("FUELPUMP_OUTR_L", 0);
                    await e.Set("FUELPUMP_MID_L_FWD", 0);
                    await e.Set("FUELPUMP_MID_L_AFT", 0);
                    await e.Set("FUELPUMP_INR_L_FWD", 0);
                    await e.Set("FUELPUMP_INR_L_AFT", 0);
                    await e.Set("FUELPUMP_OUTR_R", 0);
                    await e.Set("FUELPUMP_MID_R_FWD", 0);
                    await e.Set("FUELPUMP_MID_R_AFT", 0);
                    await e.Set("FUELPUMP_INR_R_FWD", 0);
                    await e.Set("FUELPUMP_INR_R_AFT", 0);
                    await e.Set("FUELPUMP_TRIM_L", 0);
                    await e.Set("FUELPUMP_TRIM_R", 0);
                }),
            Auto("PK_SEATBELTS_OFF", "PARKING", "Seatbelt signs: OFF", "SEATBELT_SIGN_LIGHT",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("SEATBELT_SIGN", 2)),
            Reminder("PK_XPDR", "PARKING", "Transponder: standby"),
            Reminder("PK_TCAS", "PARKING", "TCAS mode: standby"),
            Auto("PK_COCKPITDOOR", "PARKING", "Cockpit door: UNLOCKED", "A32NX_COCKPIT_DOOR_LOCKED",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A32NX_COCKPIT_DOOR_LOCKED", 0)),
            // Power-down: external power OFF (mirrors BS_GPU_OFF), then APU master OFF
            // (mirrors AS_APU_OFF) — 1:1 mirror of the flow's PK_EXTPWR_OFF / PK_APU_OFF.
            Multi("PK_EXTPWR_OFF", "PARKING", "External power: OFF", "A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON",
                v => v < 0.5,
                new[] { "A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON",
                        "A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON" },
                async (e, _) =>
                {
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0);
                    await e.Set("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0);
                }),
            Auto("PK_APU_OFF", "PARKING", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => Math.Abs(v - 0) < 0.5, (e, _) => e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)),
        }
    };

    // -----------------------------------------------------------------------
    // Readback (*_CL) groups — challenge/response checklists.
    // HARD INVARIANT: every item here is action-free (Reminder, or Auto(..., action: null)).
    // No ActionManual, no non-null CheckAction, anywhere in a *_CL group.
    // -----------------------------------------------------------------------

    // 1a. Cockpit Preparation Checklist
    private static Group BuildCockpitPrepCL() => new()
    {
        Id = "COCKPIT_PREP_CL", Name = "Cockpit Preparation Checklist",
        Items = new()
        {
            Reminder("CPC_PREP", "COCKPIT_PREP_CL", "Cockpit preparation: COMPLETED"),
            Reminder("CPC_FUEL", "COCKPIT_PREP_CL", "Fuel: READBACK"),
            Auto("CPC_SEATBELT", "COCKPIT_PREP_CL", "Seatbelt signs: ON", "SEATBELT_SIGN_LIGHT",
                v => v > 0.5, action: null),
            Auto("CPC_ADIRS", "COCKPIT_PREP_CL", "ADIRS: NAV", "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB",
                v => Math.Abs(v - 1) < 0.5,
                new[] { "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB" },
                action: null),
            Reminder("CPC_ALTIMETERS", "COCKPIT_PREP_CL", "Altimeters: SET"),
            Reminder("CPC_FMGS", "COCKPIT_PREP_CL", "F.M.G.S: SET"),
        }
    };

    // 2a. Before Start Checklist
    private static Group BuildBeforeStartCL() => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = new()
        {
            Reminder("BSC_TOSPEEDS", "BEFORE_START_CL", "Takeoff speeds: READBACK"),
            Info("BSC_LINE", "BEFORE_START_CL", "— Down to the line —"),
            Reminder("BSC_DOORS", "BEFORE_START_CL", "Doors: CLOSED"),
            Auto("BSC_BEACON", "BEFORE_START_CL", "Beacon: ON", "LIGHT_BEACON",
                v => v > 0.5, action: null),
            Auto("BSC_PARKBRK", "BEFORE_START_CL", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS",
                v => v > 0.5, action: null),
        }
    };

    // 4a. After Start Checklist
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

    // 5a. Taxi Checklist
    private static Group BuildTaxiCL() => new()
    {
        Id = "TAXI_CL", Name = "Taxi Checklist",
        Items = new()
        {
            Reminder("TXC_TAXI", "TAXI_CL", "Taxi checklist"),
            Reminder("TXC_FCTEST", "TAXI_CL", "Flight control test: CHECKED"),
            Reminder("TXC_FLAPS", "TAXI_CL", "Flaps: SET"),
            Auto("TXC_WXR", "TAXI_CL", "Weather radar: ON", "XMLVAR_A320_WeatherRadar_Sys",
                v => v > 0.5, action: null),
            Auto("TXC_ENGMODE", "TAXI_CL", "Engine mode: SET", "ENGINE_MODE_SELECTOR",
                v => Math.Abs(v - 1) < 0.5, action: null),
            Reminder("TXC_ECAMMEMO", "TAXI_CL", "ECAM memo: no blue, takeoff config normal"),
        }
    };

    // 6a. Lineup Checklist
    private static Group BuildLineupCL() => new()
    {
        Id = "LINEUP_CL", Name = "Lineup Checklist",
        Items = new()
        {
            Reminder("LUC_LINEUP", "LINEUP_CL", "Lineup checklist"),
            Reminder("LUC_RUNWAY", "LINEUP_CL", "Takeoff runway: Confirmed"),
            Reminder("LUC_TCAS", "LINEUP_CL", "TCAS: SET"),
            Reminder("LUC_PACKS", "LINEUP_CL", "Packs: SET"),
            Reminder("LUC_CABINCREW", "LINEUP_CL", "Cabin crew: NOTIFIED"),
        }
    };

    // 13. Before Takeoff Checklist (readback-only briefing; no matching STATE/ACTION group)
    private static Group BuildBeforeTakeoffCL() => new()
    {
        Id = "BEFORE_TAKEOFF_CL", Name = "Before Takeoff Checklist",
        Items = new()
        {
            Reminder("BTC_FLAPS", "BEFORE_TAKEOFF_CL", "Flaps: SET"),
            Reminder("BTC_TOSPEEDS", "BEFORE_TAKEOFF_CL", "Takeoff speeds: READBACK"),
            Reminder("BTC_ALTITUDE", "BEFORE_TAKEOFF_CL", "Altitude: READBACK"),
        }
    };

    // 9a. Approach Checklist
    private static Group BuildApproachCL() => new()
    {
        Id = "APPROACH_CL", Name = "Approach Checklist",
        Items = new()
        {
            Reminder("APC_ALTIMETERS", "APPROACH_CL", "Altimeters: SET"),
            Auto("APC_SEATBELT", "APPROACH_CL", "Seatbelt signs: ON", "SEATBELT_SIGN_LIGHT",
                v => v > 0.5, action: null),
            Reminder("APC_MINIMUMS", "APPROACH_CL", "Minimums: READBACK"),
            Auto("APC_AUTOBRAKE", "APPROACH_CL", "Autobrakes: SET", "A32NX_AUTOBRAKES_SELECTED_MODE",
                v => v > 0.5, action: null),
            Auto("APC_ENGMODE", "APPROACH_CL", "Engine mode: SET", "ENGINE_MODE_SELECTOR",
                v => Math.Abs(v - 1) < 0.5, action: null),
        }
    };

    // 10a. Landing Checklist
    private static Group BuildLandingCL() => new()
    {
        Id = "LANDING_CL", Name = "Landing Checklist",
        Items = new()
        {
            Reminder("LDC_MISSEDALT", "LANDING_CL", "Missed approach altitude: SET"),
            // Real state var — NOT the write-only A380X_MSFSBA_SPOILERS_ARM Act key.
            Auto("LDC_SPOILERS", "LANDING_CL", "Spoilers: ARMED", "A32NX_SPOILERS_ARMED",
                v => v > 0.5, action: null),
        }
    };

    // 11a. After Landing Checklist
    private static Group BuildAfterLandingCL() => new()
    {
        Id = "AFTER_LANDING_CL", Name = "After Landing Checklist",
        Items = new()
        {
            Auto("ALC_WXR", "AFTER_LANDING_CL", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys",
                v => v < 0.5, action: null),
        }
    };

    // 12a. Parking Checklist
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
            Auto("PKC_WINGLT", "PARKING_CL", "Wing lights: OFF", "LIGHT_WING",
                v => v < 0.5, action: null),
            Auto("PKC_FUELPUMPS", "PARKING_CL", "Fuel pumps: OFF", "FUELPUMP_FEEDTK1_MAIN",
                v => v < 0.5, action: null),
        }
    };

    // -----------------------------------------------------------------------
    // Item builders (mirror the Fenix file's helpers, retyped to FbwA380ActionExecutor/FbwA380StateEvaluator)
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
        // the Fenix Info builder — keep both in sync.
        ManualCompletionAllowed = false,
    };
}
