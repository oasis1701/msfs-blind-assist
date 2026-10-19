using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.FBWA320;

using Flow = Models.FlowDefinition<FbwA320StateEvaluator>;
using Step = Models.FlowStep<FbwA320StateEvaluator>;

/// <summary>
/// Data-driven FlyByWire A320 (A32NX) First-Officer flow definitions — the same 14
/// phases as <see cref="Fenix.FenixFlowDefinitions"/> (Electrical Power Up through
/// Secure, Landing since 2026-10-16), re-keyed to A32NX control keys confirmed against
/// <see cref="MSFSBlindAssist.Aircraft.FlyByWireA320Definition.GetVariables"/>. Flow
/// steps write A320 varKeys via <see cref="FbwA320ActionExecutor"/>, which delegates
/// most writes to <see cref="MSFSBlindAssist.Aircraft.FlyByWireA320Definition.ApplyUIVariable"/>
/// — the same verified panel-write path the FBW A320 panels use — plus the executor's
/// pseudo-keys (FIRE_TEST_APU/ENG1/ENG2, CVR_TEST, TO_CONFIG_TEST, CABIN_CALL_ALL,
/// ECAM_PAGE_* (the page's ECP key press), BARO_STD/QNH, FCU_PUSH_SPEED/HEADING/ALT,
/// AP1_ENGAGE) for non-combo actions.
///
/// Value conventions (from <see cref="FbwA320StateEvaluator"/>, <see cref="FbwA320ActionExecutor"/>,
/// and the FlyByWireA320Definition panel definitions — the A320 is 2 engines, no ESS/APU
/// battery, and seatbelts are a genuine 2-position switch (no AUTO), unlike the A380):
/// - ENGINE_MODE_SELECTOR 0=Crank,1=Norm,2=Ign/Start.
/// - ENGINE_1_MASTER / ENGINE_2_MASTER 0=Off,1=On (FUELSYSTEM VALVE SWITCH:n underneath).
/// - SPOILERS_ARM_TOGGLE is a genuine TOGGLE event (no settable position) — Skip guards on
///   the real state var A32NX_SPOILERS_ARMED so a re-run never double-fires it. After Start arms
///   the ground spoilers, After Takeoff disarms them and the Landing flow arms them again, leaving
///   an extended speed brake alone (FOA-11: the arm snaps the handle to retracted).
/// - Nose light LIGHTING_LANDING_1: 0=T.O., 1=Taxi, 2=Off (NOTE: differs from the
///   executor's SetCockpitLighting-adjacent SetNoseLight doc-comment, which describes
///   0=On/1=Off/2=Retract — that comment looks stale/wrong; flows here use the
///   authoritative ValueDescriptions from GetVariables(). Flag for the Task 12 audit).
/// - Landing lights: LANDING_LIGHTS_ON_THIRD_PARTY / _OFF_THIRD_PARTY are momentary
///   RenderAsButton events (any nonzero fires); no Skip guard needed (idempotent), same
///   as the Fenix source content.
/// - Strobe LIGHTING_STROBE_0: 0=On, 1=Auto, 2=Off. Weather radar XMLVAR_A320_WeatherRadar_Sys:
///   0=System 1 (on), 1=Off, 2=System 2. PWS A32NX_SWITCH_RADAR_PWS_POSITION: 0=Off, 1=Auto.
/// - Transponder A32NX_TRANSPONDER_MODE: 0=STBY,1=AUTO,2=ON. TCAS mode is the SWITCH
///   A32NX_SWITCH_TCAS_POSITION (0=STBY,1=TA,2=TA/RA) — NOT A32NX_TCAS_MODE, which is the
///   computed system OUTPUT (Continuous+Announced, read-only); this resolves the mapping
///   table's ⚠️ for TCAS mode.
/// - Crew oxygen PUSH_OVHD_OXYGEN_CREW is INVERTED: 0=On, 1=Off (pushbutton-out=on).
/// - Autobrake write key AUTOBRAKE_MODE (backed by A32NX_AUTOBRAKES_ARMED_MODE): 0=DIS,
///   1=LO, 2=MED, 3=MAX.
/// - Recorder ground control (A32NX_RCDR_GROUND_CONTROL_ON, plain bool), CVR test (held
///   A32NX_RCDR_TEST via the executor's CvrTestAsync), and the takeoff-config test (held
///   ECP A32NX_ECP_TO_CONF_TEST_PRESSED/RELEASED via TakeoffConfigTestAsync) are all
///   sim + source verified and automated (2026-07). The F/CTL ECAM page itself still has
///   no dedicated key and stays a Captain reminder ("no F/CTL ECP key").
/// - Takeoff flaps come from SimBrief (the Fenix's Provider step, AS_FLAPS); a Captain reminder
///   (AS_FLAPS_CAPT) speaks unless the flap handle already reads a takeoff position (1-3).
/// - Cockpit lighting (§4.1), at four points (power-up bright, after start dim, shutdown bright,
///   secure off), one step per action line (FO-7), in the Fenix's order: the annunciator
///   (A32NX_OVHD_INTLT_ANN: 1=Bright, 2=Dim), the dome (A32NX_OVHD_INTLT_DOME, a percentage:
///   100 bright / 20 dim / 0 off), the standby-compass light (A32NX_STBY_COMPASS_LIGHT_TOGGLE,
///   a plain 0/1 L:var despite its name) and the scene. The scene step passes the
///   COCKPIT_LIGHT_SCENE_* pseudo-key (FbwA320ActionExecutor.CockpitLightSceneKey), which the
///   executor maps to SetCockpitLighting — the same call the scene checklist line makes, so
///   the tick and the step write alike (FO-3). The dome and compass steps skip when the light
///   already reads the line's own state; the scene has nothing readable to skip on.
/// - Engine start waits for FlyByWire's own engine state (A32NX_ENGINE_STATE:n = On, read as the
///   synthetic FO_ENGn_RUNNING), not a raw N2 threshold: both FBW airframes publish the state
///   and the A330's idle N2 is not known.
/// </summary>
public static class FbwA320FlowDefinitions
{
    public static List<Flow> Build() => new()
    {
        BuildElectricalPowerUp(),
        BuildPreflight(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildTaxi(),
        BuildLineup(),
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
        Description = "Safety checks, batteries on, external power if available, nav lights, cockpit lights.",
        RelatedChecklistGroupIds = new[] { "ELEC_POWER_UP" },
        Steps = new()
        {
            // A cold-and-dark flow: started with an engine turning (a runway or engines-running
            // start, or the wrong flow picked in flight), the master checks below would shut the
            // engines down. Stop instead (the MD-11's PU_ENGINES_STOPPED).
            EnginesStoppedGuard("EPU_ENGINES_STOPPED"),
            // Safety-check prefix: every step skips quietly when already correct.
            Skip(SW("EPU_CHK_MASTER1", "Engine 1 master: OFF", "ENGINE_1_MASTER", 0),
                s => s.IsPosition("ENGINE_1_MASTER", 0)),
            Skip(SW("EPU_CHK_MASTER2", "Engine 2 master: OFF", "ENGINE_2_MASTER", 0),
                s => s.IsPosition("ENGINE_2_MASTER", 0)),
            Skip(SW("EPU_CHK_MODE", "Engine mode selector: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)),
            // Gear lever has no settable A32NX key (GEAR_HANDLE_POSITION is a read-only stock
            // SimVar), so this safety check READS it, where the Fenix writes its lever: "Already
            // set" with the lever down, else a 5 s wait that is skipped aloud. It never writes, and
            // it completes the detect-only "Gear lever: DOWN" line, so a lever not read down keeps
            // that line out of the latch (FO-6).
            Done(Skip(WaitForField("EPU_CHK_GEAR", "Gear lever: DOWN", "GEAR_HANDLE_POSITION",
                    v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Skip),
                s => s.GetValue("GEAR_HANDLE_POSITION") > 0.5), "EPU_CHK_GEAR"),
            // The write keys are the definition's synthetic wiper combos, which nothing writes back:
            // the skip reads the wiper circuit switches (WIPER_L_SW / WIPER_R_SW, circuits 77 and
            // 80; A320-28), because a skip on the combos always said "Already set" over running
            // wipers. Off is the switch alone: the power setting stays 100% while off.
            Skip(Multi("EPU_CHK_WIPERS", "Wipers: OFF",
                    ("WIPER_LEFT", 0), ("WIPER_RIGHT", 0)),
                s => s.IsPosition("WIPER_L_SW", 0) && s.IsPosition("WIPER_R_SW", 0)),
            Skip(SW("EPU_CHK_WXR", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys", 1),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 1)),
            Skip(SW("EPU_CHK_PARK", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")),
            // Power up
            Done(Skip(SW("EPU_BAT1", "Battery 1: ON", "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 1),
                s => s.IsOn("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO")), "EPU_BAT1"),
            Done(Skip(SW("EPU_BAT2", "Battery 2: ON", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 1),
                s => s.IsOn("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO")), "EPU_BAT2"),
            // Harmless no-op if no ground power exists; skipped when already on the bus.
            Done(Skip(SW("EPU_EXTPWR", "External power: ON", "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON")), "EPU_EXTPWR"),
            Done(Skip(SW("EPU_NAVLOGO", "Nav and logo lights: ON", "A32NX_LIGHTS_NAV_LOGO", 1),
                s => s.GetValue("A32NX_LIGHTS_NAV_LOGO") > 0.5), "EPU_NAVLOGO"),
            // ★ Cockpit lighting (spec §4.1): Bright for ground prep. Each lighting line has its
            // own step (FO-7), in the Fenix's order: annunciator, dome, standby compass, scene.
            Done(SW("EPU_COCKPITLT", "Cockpit lights: set", "A32NX_OVHD_INTLT_ANN", 1), "EPU_COCKPITLT"),
            Done(Skip(SW("EPU_DOME", "Dome light: bright", "A32NX_OVHD_INTLT_DOME", 100),
                s => s.GetValue("A32NX_OVHD_INTLT_DOME") >= 90), "EPU_DOME"),
            Done(Skip(SW("EPU_STBYCOMPASS", "Standby compass light: ON", "A32NX_STBY_COMPASS_LIGHT_TOGGLE", 1),
                s => s.IsOn("A32NX_STBY_COMPASS_LIGHT_TOGGLE")), "EPU_STBYCOMPASS"),
            Done(Scene("EPU_LTSCENE", FbwA320ActionExecutor.CockpitLightScene.DayPrep), "EPU_LTSCENE"),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Preflight
    // -----------------------------------------------------------------------
    private static Flow BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "Cockpit preparation: IRS, oxygen, fire tests, air conditioning, signs, transponder setup.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT" },
        Steps = new()
        {
            // Recorder ground control (plain bool, latches on write) + CVR test (held
            // button via the executor's CvrTestAsync pseudo-key) — sim + source verified.
            Done(Skip(SW("PF_GNDCTL", "Recorder ground control: ON", "A32NX_RCDR_GROUND_CONTROL_ON", 1),
                s => s.IsOn("A32NX_RCDR_GROUND_CONTROL_ON")), "PF_GNDCTL"),
            Done(SW("PF_CVR", "CVR test: listen for the test tone", "CVR_TEST", 1), "PF_CVR"),
            // ADIRS
            Done(Skip(Multi("PF_IRS", "IRS 1, 2 and 3: NAV",
                    ("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 1), ("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 1),
                    ("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 1)),
                s => s.IsPosition("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 1)
                     && s.IsPosition("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 1)
                     && s.IsPosition("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 1)), "PF_IRS"),
            // Crew oxygen: INVERTED convention (0=On, 1=Off, pushbutton-out=on).
            Done(Skip(SW("PF_OXY", "Crew oxygen supply: ON", "PUSH_OVHD_OXYGEN_CREW", 0),
                s => s.IsPosition("PUSH_OVHD_OXYGEN_CREW", 0)), "PF_OXY"),
            // Fire tests (held pseudo-keys via the executor; fire bell audible)
            Done(SW("PF_FIRE_APU", "APU fire test", "FIRE_TEST_APU", 1), "PF_FIRE_APU"),
            Done(SW("PF_FIRE_ENG1", "Engine 1 fire test", "FIRE_TEST_ENG1", 1), "PF_FIRE_ENG1"),
            Done(SW("PF_FIRE_ENG2", "Engine 2 fire test", "FIRE_TEST_ENG2", 1), "PF_FIRE_ENG2"),
            // ★ ECAM page: door (spec §4)
            Done(SW("PF_ECAMDOOR", "ECAM page: door", "ECAM_PAGE_DOOR", 1), "PF_ECAMDOOR"),
            // Air conditioning / pressurization
            Done(Skip(Multi("PF_PACKS", "Packs 1 and 2: ON",
                    ("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1), ("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 1)),
                s => s.IsOn("A32NX_OVHD_COND_PACK_1_PB_IS_ON") && s.IsOn("A32NX_OVHD_COND_PACK_2_PB_IS_ON")), "PF_PACKS"),
            Done(Skip(SW("PF_XBLEED", "Crossbleed: AUTO", "A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position", 1),
                s => s.IsPosition("A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position", 1)), "PF_XBLEED"),
            Done(Skip(SW("PF_PACKFLOW", "Pack flow: NORMAL", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position", 1),
                s => s.IsPosition("A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position", 1)), "PF_PACKFLOW"),
            Done(Skip(SW("PF_HOTAIR", "Hot air: ON", "A32NX_OVHD_COND_HOT_AIR_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_COND_HOT_AIR_PB_IS_ON")), "PF_HOTAIR"),
            Done(Skip(SW("PF_PRESSMODE", "Cabin pressure mode: AUTO", "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO", 1),
                s => s.IsOn("A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO")), "PF_PRESSMODE"),
            // Signs and lights
            Done(Skip(SW("PF_STROBE", "Strobes: AUTO", "LIGHTING_STROBE_0", 1),
                s => s.IsPosition("LIGHTING_STROBE_0", 1)), "PF_STROBE"),
            Done(Skip(SW("PF_WING_LT", "Wing lights: OFF", "WING_LIGHTS_SET", 0),
                s => s.IsPosition("LIGHT WING", 0)), "PF_WING_LT"),
            Done(Skip(SW("PF_NOSMOKE", "No smoking: AUTO", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 1),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 1)), "PF_NOSMOKE"),
            Done(Skip(SW("PF_EMEREXIT", "Emergency exit lights: ARM", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION", 1),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION", 1)), "PF_EMEREXIT"),
            // A32NX_SWITCH_ATC_ALT confirmed registered in FlyByWireA320Definition
            // (Task 12 audit) — automated, mirroring the Fenix S_XPDR_ALTREPORTING step.
            Done(Skip(SW("PF_ALTRPTG", "Altitude reporting: ON", "A32NX_SWITCH_ATC_ALT", 1),
                s => s.IsOn("A32NX_SWITCH_ATC_ALT")), "PF_ALTRPTG"),
            Done(Skip(SW("PF_TCASTRAFFIC", "TCAS traffic: ALL", "A32NX_SWITCH_TCAS_TRAFFIC_POSITION", 1),
                s => s.IsPosition("A32NX_SWITCH_TCAS_TRAFFIC_POSITION", 1)), "PF_TCASTRAFFIC"),
            // Flight directors — push event, verify on via the FD light L:var.
            Skip(SW("PF_FD1", "Flight director 1: ON", "A32NX.FCU_EFIS_L_FD_PUSH", 1),
                s => s.IsOn("A32NX_FMGC_1_FD_ENGAGED")),
            Skip(SW("PF_FD2", "Flight director 2: ON", "A32NX.FCU_EFIS_R_FD_PUSH", 1),
                s => s.IsOn("A32NX_FMGC_2_FD_ENGAGED")),
            // Captain items
            Captain("PF_BARO", "Set QNH on both altimeters and the standby altimeter"),
            Captain("PF_FCUALT", "Set the initial cleared altitude on the FCU"),
            Captain("PF_SQUAWK", "Set the squawk code"),
            Captain("PF_EFB", "EFB setup — import SimBrief, load fuel and payload"),
            Captain("PF_MCDU", "MCDU setup — INIT, flight plan, and PERF pages"),
        }
    };

    // -----------------------------------------------------------------------
    // 3. Before Start (APU + fuel pumps here by user decision)
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "ECAM APU page, APU start, fuel pumps, external power off, signs, beacon, FCU managed modes.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START" },
        Steps = new()
        {
            // ★ ECAM page: APU (spec §4)
            Done(SW("BS_ECAMAPU", "ECAM page: APU", "ECAM_PAGE_APU", 1), "BS_ECAMAPU"),
            // APU block: master on, dwell, start pulse, wait for AVAIL. Stop policy: an
            // APU start failure aborts the flow HERE, before external power is pulsed
            // off the bus below — never a silent transfer to batteries.
            // The line "APU: ON and available" is the AVAIL state, so the AVAIL WAIT completes
            // it, never the master write: a master that is on with an APU that never comes up
            // must not tick the line (FO-6/FO-7). The master write links nothing on purpose.
            Skip(SW("BS_APU_MASTER", "APU master: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON")),
            Skip(Wait("BS_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("BS_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Done(WaitForField("BS_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop), "BS_APU"),
            // Defensive: release the latched START PB (mirrors the A380 flow's guard
            // against a stale 1 surprise-starting the APU on a later master-ON).
            // IsPosition(..., 0), never !IsOn(...): an unread switch must not skip an absolute OFF write.
            Skip(SW("BS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_START_PB_IS_ON", 0)),
            Done(Skip(SW("BS_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")), "BS_APUBLEED"),
            // Fuel pumps immediately after the APU block (user decision, matches Fenix/FSFO)
            Done(Skip(Multi("BS_FUELPUMPS", "Fuel pumps: ALL ON",
                    ("FUEL_PUMP_L1", 1), ("FUEL_PUMP_L2", 1),
                    ("FUEL_PUMP_C1", 1), ("FUEL_PUMP_C2", 1),
                    ("FUEL_PUMP_R1", 1), ("FUEL_PUMP_R2", 1)),
                s => s.IsOn("FUEL_PUMP_L1") && s.IsOn("FUEL_PUMP_L2")
                     && s.IsOn("FUEL_PUMP_C1") && s.IsOn("FUEL_PUMP_C2")
                     && s.IsOn("FUEL_PUMP_R1") && s.IsOn("FUEL_PUMP_R2")), "BS_FUELPUMPS"),
            // External power off; skip when it reads off. IsPosition(..., 0), never !IsOn(...): an
            // unread switch was "Already set" and latched with the GPU still connected.
            Done(Skip(SW("BS_EXTPWR_OFF", "External power: OFF", "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 0)), "BS_EXTPWR_OFF"),
            // Seatbelt signs: 2-position on the A320 (no AUTO, unlike the A380). Routed through
            // the SEATBELT_SIGN pseudo-key to FbwA320ActionExecutor.SetSeatbeltSign, the guarded
            // toggle. The bare CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE reaches no write branch, so
            // it was refused and this step was skipped with the sign untouched (2026-09-29).
            Done(Skip(SW("BS_SEATBELTS", "Seatbelt signs: ON", "SEATBELT_SIGN", 1),
                s => s.IsOn("CABIN SEATBELTS ALERT SWITCH")), "BS_SEATBELTS"),
            // Completes the Before Start Checklist's "Beacon: ON" too (FO-6): the flow latches both
            // groups, so a failed write must keep BSC_BEACON out of the latch as well.
            Also(Done(Skip(SW("BS_BEACON", "Beacon: ON", "BEACON_LIGHTS_SET", 1),
                s => s.IsOn("LIGHT BEACON")), "BS_BEACON"), "BSC_BEACON"),
            // FCU managed modes (pseudo-keys → atomic knob-push calc)
            Done(SW("BS_FCUSPD", "FCU speed: managed", "FCU_PUSH_SPEED", 1), "BS_FCUSPD"),
            Done(SW("BS_FCUHDG", "FCU heading: managed", "FCU_PUSH_HEADING", 1), "BS_FCUHDG"),
            Captain("BS_ALT", "Set cleared altitude on the FCU"),
            Done(SW("BS_FCUALT", "FCU altitude: pushed", "FCU_PUSH_ALT", 1), "BS_FCUALT"),
            // Cockpit door: closed and locked
            Done(SW("BS_COCKPITDOOR", "Cockpit door: closed and locked", "A32NX_COCKPIT_DOOR_LOCKED", 1), "BS_COCKPITDOOR"),
            Captain("BS_DOORS", "Close doors and remove ground services on the EFB"),
            Captain("BS_THRLEVERS", "Confirm thrust levers idle"),
            Captain("BS_ACARS", "Start ACARS"),
            Captain("BS_CLEARANCE", "Obtain pushback and start clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 4. Engine Start (ECAM engine page + mode IGN + engine 1, then engine 2)
    // -----------------------------------------------------------------------
    private static Flow BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Description = "ECAM engine page, engine mode IGN/START, then engine 1 and engine 2, each waited until running.",
        RelatedChecklistGroupIds = new[] { "ENGINE_START" },
        Steps = new()
        {
            // ★ ECAM page: engine (spec §4)
            Done(SW("ES_ECAMENG", "ECAM page: engine", "ECAM_PAGE_ENG", 1), "ES_ECAMENG"),
            Done(Skip(SW("ES_MODE", "Engine mode selector: IGN START", "ENGINE_MODE_SELECTOR", 2),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 2)), "ES_MODE"),
            // Engine 1 first, then engine 2. Each wait is on FlyByWire's own engine state
            // (A32NX_ENGINE_STATE:n = On, via FO_ENGn_RUNNING), not a raw N2: both FBW airframes
            // publish it and the A330's idle N2 is unknown. 120 s, and a timeout stops the flow
            // (the Fenix's shape), so nothing is latched. Each wait completes its own "Engine n:
            // running" line, which reads the same state, so the line a finished flow latches has a
            // step behind it (FO-6).
            Done(Skip(SW("ES_ENG1", "Engine 1 master: ON", "ENGINE_1_MASTER", 1),
                s => s.IsOn("ENGINE_1_MASTER")), "ES_ENG1"),
            Done(WaitForField("ES_ENG1_N2", "Engine 1 starting — waiting for the engine to stabilize",
                "FO_ENG1_RUNNING", v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop), "ES_ENG1_RUN"),
            Done(Skip(SW("ES_ENG2", "Engine 2 master: ON", "ENGINE_2_MASTER", 1),
                s => s.IsOn("ENGINE_2_MASTER")), "ES_ENG2"),
            Done(WaitForField("ES_ENG2_N2", "Engine 2 starting — waiting for the engine to stabilize",
                "FO_ENG2_RUNNING", v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop), "ES_ENG2_RUN"),
        }
    };

    // -----------------------------------------------------------------------
    // 5. After Start
    // -----------------------------------------------------------------------
    private static Flow BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Mode NORM, APU off, spoilers armed, cockpit lights dim, ECAM status page, rudder trim, taxi light.",
        RelatedChecklistGroupIds = new[] { "AFTER_START" },
        Steps = new()
        {
            Done(Skip(SW("AS_MODE_NORM", "Engine mode selector: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)), "AS_MODE_NORM"),
            Done(Skip(SW("AS_APUBLEED_OFF", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)), "AS_APUBLEED_OFF"),
            // Defensive: release a still-latched START PB before master-off (mirrors
            // Before Start / the A380 flow's guard).
            Skip(SW("AS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_START_PB_IS_ON", 0)),
            Done(Skip(SW("AS_APUMASTER_OFF", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)), "AS_APUMASTER_OFF"),
            // Ground spoilers arm: SPOILERS_ARM_TOGGLE is a genuine toggle event (no
            // settable position) — Skip guards on the real state var so a re-run never
            // double-fires it back to disarmed.
            Done(Skip(SW("AS_SPOILERS_ARM", "Ground spoilers: ARMED", "SPOILERS_ARM_TOGGLE", 1),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 1)), "AS_SPOILERS_ARM"),
            // Completes the After Start Checklist's "Rudder trim: NEUTRAL" too (FO-6): a failed
            // reset must keep ASC_RUDDER out of the latch as well.
            Also(Done(SW("AS_RUDDERTRIM", "Rudder trim: RESET", "A32NX_RUDDER_TRIM_RESET", 1), "AS_RUDDERTRIM"),
                "ASC_RUDDER"),
            // Takeoff flaps from SimBrief (the Fenix's step): quiet skip without a plan. The
            // Captain reminder below speaks unless the lever already reads a takeoff position
            // (1-3): no plan, or a write that did not take, and the pilot is asked. Keyed on the
            // LEVER, never on the plan: keyed on the plan, a failed write was heard as
            // "Skipping…" then a false "Already set". Unread (NaN) is not set.
            Done(Provider("AS_FLAPS", "Flaps: takeoff setting", "A32NX_FLAPS_HANDLE_INDEX",
                s => { int f = s.TakeoffFlapsLeverIndex(); return f >= 1 ? f : (int?)null; }), "AS_FLAPS"),
            Skip(Captain("AS_FLAPS_CAPT", "Flaps: set for takeoff"),
                s => IsTakeoffFlapsLever(s.GetValue("A32NX_FLAPS_HANDLE_INDEX"))),
            Done(Skip(SW("AS_NOSE_TAXI", "Nose light: TAXI", "LIGHTING_LANDING_1", 1),
                s => s.IsPosition("LIGHTING_LANDING_1", 1)), "AS_NOSE_TAXI"),
            Captain("AS_ANTIICE", "Set engine and wing anti-ice as required"),
            Captain("AS_PITCHTRIM", "Set pitch trim per the loadsheet"),
            // ★ Cockpit lighting: Dim for taxi/flight (spec §4.1), + ECAM status page (spec §4)
            Done(SW("AS_COCKPITLT", "Cockpit lights: dim", "A32NX_OVHD_INTLT_ANN", 2), "AS_COCKPITLT"),
            Done(Skip(SW("AS_DOME", "Dome light: dim", "A32NX_OVHD_INTLT_DOME", 20),
                s => s.GetValue("A32NX_OVHD_INTLT_DOME") <= 30), "AS_DOME"),
            Done(Skip(SW("AS_STBYCOMPASS", "Standby compass light: ON", "A32NX_STBY_COMPASS_LIGHT_TOGGLE", 1),
                s => s.IsOn("A32NX_STBY_COMPASS_LIGHT_TOGGLE")), "AS_STBYCOMPASS"),
            Done(Scene("AS_LTSCENE", FbwA320ActionExecutor.CockpitLightScene.DimFlight), "AS_LTSCENE"),
            Done(SW("AS_ECAMSTS", "ECAM page: status", "ECAM_PAGE_STS", 1), "AS_ECAMSTS"),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Taxi (run while taxiing, before the Taxi checklist; FOA-8)
    // -----------------------------------------------------------------------
    private static Flow BuildTaxi() => new()
    {
        Id = "TAXI", Name = "Taxi",
        Description = "Autobrake MAX, weather radar and predictive windshear, transponder AUTO, cabin call, a check of the takeoff memo items, then the takeoff config test.",
        RelatedChecklistGroupIds = new[] { "TAXI" },
        Steps = new()
        {
            // ★ Autobrake MAX (spec §4). Also the takeoff memo's "Autobrake: MAX" (FO-6): the flow
            // latches the Taxi Checklist too, so a failed write keeps that line open.
            Also(Done(Skip(SW("TX_AUTOBRAKE", "Autobrake: MAX", "AUTOBRAKE_MODE", 3),
                s => s.IsPosition("A32NX_AUTOBRAKES_ARMED_MODE", 3)), "TX_AUTOBRAKE"), "TXC_MEMO_AUTOBRK"),
            // "Radar and predictive windshear: ON and AUTO" (TXC_WXR) is true only with BOTH set, so
            // neither write completes it: the read-only TX_WXR_PWS_CHECK after them does (the
            // AL_WXR_PWS_CHECK shape: 5 s, skipped aloud on timeout, never writes).
            Done(Skip(SW("TX_WXR", "Weather radar: SYSTEM 1", "XMLVAR_A320_WeatherRadar_Sys", 0),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 0)), "TX_WXR"),
            Done(Skip(SW("TX_PWS", "Predictive windshear: AUTO", "A32NX_SWITCH_RADAR_PWS_POSITION", 1),
                s => s.IsPosition("A32NX_SWITCH_RADAR_PWS_POSITION", 1)), "TX_PWS"),
            Done(Skip(WaitForField("TX_WXR_PWS_CHECK", "Radar and predictive windshear: ON and AUTO", "FO_WXR_ON_AUTO",
                    v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Skip),
                s => s.GetValue("FO_WXR_ON_AUTO") > 0.5), "TXC_WXR"),
            Done(Skip(SW("TX_XPDRAUTO", "Transponder: AUTO", "A32NX_TRANSPONDER_MODE", 1),
                s => s.IsPosition("A32NX_TRANSPONDER_MODE", 1)), "TX_XPDRAUTO"),
            // ★ Cabin call for takeoff (spec §4): the ONE call (owner, 2026-10-09), which makes
            // FlyByWire's memo show CABIN READY. Also the memo's "Cabin: READY", a line the pilot
            // confirms: a call that could not be sent keeps it open.
            Also(Done(SW("TX_CABIN", "Advise the cabin crew for takeoff (call all)", "CABIN_CALL_ALL", 1), "TX_CABIN"),
                "TXC_MEMO_CABIN"),
            // Read-only check of the takeoff memo items set earlier (FO_TAXI_MEMO_OK): the ground
            // spoilers armed and a takeoff flap handle (After Start sets both) and the memo's seat
            // belts on (Before Start's seat belts, Preflight's no smoking). 5 s, skipped aloud on
            // timeout, never writes: flaps held until the holding point (icing) time it out. Of its
            // four lines the ones that read wrong then stay unticked and out of the latch, following
            // the aircraft ("Flaps: T.O" with the lever up); "Flaps setting: SET", a Reminder, waits
            // for the pilot's tick; lines already reading right (the window ticks them every second)
            // latch with the rest.
            Also(Done(Skip(WaitForField("TX_MEMO_CHECK", "Spoilers, flaps and seat belts: set for takeoff", "FO_TAXI_MEMO_OK",
                    v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Skip),
                s => s.GetValue("FO_TAXI_MEMO_OK") > 0.5), "TXC_MEMO_SPLRS"),
                "TXC_MEMO_FLAPS", "TXC_FLAPS", "TXC_MEMO_SIGNS"),
            // Takeoff config test, LAST (FOA-8 owner choice 12: the memo is read after it): held ECP
            // TO_CONF_TEST H-event via the executor's TakeoffConfigTestAsync pseudo-key (sim + source
            // verified). Also the memo's "T.O config: NORMAL".
            Also(Done(SW("TX_CONFIG", "Takeoff config test", "TO_CONFIG_TEST", 1), "TX_CONFIG"), "TXC_MEMO_TOCFG"),
        }
    };

    // -----------------------------------------------------------------------
    // 7. Line-up (run when cleared onto the runway, before the Line-up checklist)
    // -----------------------------------------------------------------------
    private static Flow BuildLineup() => new()
    {
        Id = "LINEUP", Name = "Line-up",
        Description = "TCAS TA/RA, strobes, runway turn-off, nose and landing lights, then the takeoff clearance reminder.",
        RelatedChecklistGroupIds = new[] { "LINEUP" },
        Steps = new()
        {
            // Also the Line-up Checklist's "TCAS: TA/RA" (FO-6).
            Also(Done(Skip(SW("LU_TCAS", "TCAS: TA/RA", "A32NX_SWITCH_TCAS_POSITION", 2),
                s => s.IsPosition("A32NX_SWITCH_TCAS_POSITION", 2)), "LU_TCAS"), "LUC_TCAS"),
            Done(Skip(SW("LU_STROBE", "Strobes: ON", "LIGHTING_STROBE_0", 0),
                s => s.IsPosition("LIGHTING_STROBE_0", 0)), "LU_STROBE"),
            Done(Skip(SW("LU_TURNOFF", "Runway turn-off lights: ON", "LIGHT TAXI:2", 1),
                s => s.IsOn("LIGHT TAXI:2")), "LU_TURNOFF"),
            Done(Skip(SW("LU_NOSE_TO", "Nose light: TAKEOFF", "LIGHTING_LANDING_1", 0),
                s => s.IsPosition("LIGHTING_LANDING_1", 0)), "LU_NOSE_TO"),
            // Landing lights here, a little before the takeoff clearance (owner, 2026-10-09: as the A380).
            Done(SW("LU_LANDING_LT", "Landing lights: ON", "LANDING_LIGHTS_ON_THIRD_PARTY", 1), "LU_LANDING_LT"),
            Captain("LU_CLEARANCE", "Obtain takeoff clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 8. After Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Spoilers disarm, packs on, turn-off lights off. Gear and autopilot are handled by the auto-managers. Ends by confirming the gear is up.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF" },
        Steps = new()
        {
            Done(Skip(SW("AT_SPOILERS_DISARM", "Ground spoilers: DISARM", "SPOILERS_ARM_TOGGLE", 0),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 0)), "AT_SPOILERS_DISARM"),
            Done(Skip(Multi("AT_PACKS", "Packs 1 and 2: ON",
                    ("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1), ("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 1)),
                s => s.IsOn("A32NX_OVHD_COND_PACK_1_PB_IS_ON") && s.IsOn("A32NX_OVHD_COND_PACK_2_PB_IS_ON")), "AT_PACKS"),
            Done(Skip(SW("AT_TURNOFF_OFF", "Runway turn-off lights: OFF", "LIGHT TAXI:2", 0),
                s => s.IsPosition("LIGHT TAXI:2", 0)), "AT_TURNOFF_OFF"),
            // Read-only confirmation (FO-13): handle up and every leg retracted. 20 s, then skip.
            Skip(WaitForField("AT_GEAR_UP_CHECK", "Landing gear: UP", FbwA320GearConfirmation.UpField,
                    v => v > 0.5, 20, onTimeout: FlowStepFailurePolicy.Skip),
                s => s.GetValue(FbwA320GearConfirmation.UpField) > 0.5),
        }
    };

    // -----------------------------------------------------------------------
    // 9. Descent
    // -----------------------------------------------------------------------
    private static Flow BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Description = "Seatbelt signs, arrival preparation reminders; landing autobrake is a Captain item.",
        RelatedChecklistGroupIds = new[] { "DESCENT" },
        Steps = new()
        {
            // Landing autobrake is ALWAYS a Captain item (project-wide rule) — never
            // automated in a descent/approach flow.
            Captain("DC_AUTOBRAKE", "Set the landing autobrake — Instrument section, Autobrake panel"),
            // SEATBELT_SIGN pseudo-key — see BS_SEATBELTS.
            Done(Skip(SW("DC_SEATBELTS", "Seatbelt signs: ON", "SEATBELT_SIGN", 1),
                s => s.IsOn("CABIN SEATBELTS ALERT SWITCH")), "DC_SEATBELTS"),
            // ONE descent-preparation item — see FbwA320ChecklistDefinitions.BuildDescent.
            Captain("DC_MCDU",
                "Descent preparation: MCDU PERF APPR set — QNH, temperature, wind and minimums; landing configuration reviewed"),
        }
    };

    // -----------------------------------------------------------------------
    // 10. Approach
    // -----------------------------------------------------------------------
    private static Flow BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "LS on both sides, seat belts on, notify the cabin for landing, approach reminders.",
        RelatedChecklistGroupIds = new[] { "APPROACH" },
        Steps = new()
        {
            Done(Skip(SW("AP_LS1", "LS captain: ON", "A32NX_EFIS_L_LS_BUTTON_IS_ON", 1),
                s => s.IsOn("A32NX_EFIS_L_LS_BUTTON_IS_ON")), "AP_LS1"),
            Done(Skip(SW("AP_LS2", "LS first officer: ON", "A32NX_EFIS_R_LS_BUTTON_IS_ON", 1),
                s => s.IsOn("A32NX_EFIS_R_LS_BUTTON_IS_ON")), "AP_LS2"),
            // SEATBELT_SIGN pseudo-key — see BS_SEATBELTS. Also the Approach Checklist's "Seat belts:
            // ON" (FO-6): usually "Already set" after Descent, and a write that fails keeps both lines
            // out of the latch, following the sign.
            Also(Done(Skip(SW("AP_SEATBELTS", "Seat belts: ON", "SEATBELT_SIGN", 1),
                s => s.IsOn("CABIN SEATBELTS ALERT SWITCH")), "AP_SEATBELTS"), "APC_SEATBELTS"),
            // ★ Cabin notify for landing (spec §4)
            Done(SW("AP_CABIN", "Notify the cabin crew for landing (call all)", "CABIN_CALL_ALL", 1), "AP_CABIN"),
            Captain("AP_MINIMUMS", "Check minimums set on the MCDU approach page"),
            Captain("AP_ENGMODE", "Set engine mode selector as required"),
        }
    };

    // -----------------------------------------------------------------------
    // 10a. Landing (run on final once the gear is down, before the Landing checklist; FOA-11)
    // -----------------------------------------------------------------------
    private static Flow BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Description = "Ground spoilers armed, then a check of the landing memo and the gear.",
        RelatedChecklistGroupIds = new[] { "LANDING" },
        Steps = new()
        {
            // After Takeoff disarmed the ground spoilers and nothing armed them again, so the pilot
            // landed with them disarmed (report, 2026-10-16). After Start's write, also the Landing
            // Checklist's "Spoilers: ARMED" (FO-6). Left alone over an extended speed brake, which the
            // arm would snap back to retracted: the line stays open until the pilot arms.
            LeaveAlone(Also(Done(Skip(SW("LD_SPOILERS_ARM", "Ground spoilers: ARMED", "SPOILERS_ARM_TOGGLE", 1),
                    s => s.IsPosition("A32NX_SPOILERS_ARMED", 1)), "LD_SPOILERS_ARM"), "LDC_MEMO_SPLRS"),
                s => AirbusGroundSpoilers.FbwSpeedBrakeExtended(s.GetValue("A32NX_SPOILERS_HANDLE_POSITION")),
                AirbusGroundSpoilers.LeaveAloneText),
            // The landing memo's seat belts and flaps (FO_LDG_MEMO_OK), read only, the Taxi memo check's
            // shape: 5 s, skipped aloud on timeout, never writes. Flaps short of the landing setting time
            // it out, and "Flaps: SET" stays open, ticking itself once the flaps are set.
            Also(Done(Skip(WaitForField("LD_MEMO_CHECK", "Seat belts and flaps: set for landing", "FO_LDG_MEMO_OK",
                    v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Skip),
                s => s.GetValue("FO_LDG_MEMO_OK") > 0.5), "LDC_MEMO_SIGNS"), "LDC_MEMO_FLAPS"),
            // LAST (FO-13): the gear confirmed down (handle down, every leg out), read only. 20 s, then skip.
            Done(Skip(WaitForField("LD_GEAR_DOWN_CHECK", "Landing gear: DOWN", FbwA320GearConfirmation.DownField,
                    v => v > 0.5, 20, onTimeout: FlowStepFailurePolicy.Skip),
                s => s.GetValue(FbwA320GearConfirmation.DownField) > 0.5), "LDC_MEMO_GEAR"),
        }
    };

    // -----------------------------------------------------------------------
    // 11. After Landing (pilot-triggered once clear of the runway)
    // -----------------------------------------------------------------------
    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Clean up after vacating: spoilers, flaps, radar, transponder, lights, APU start.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING" },
        Steps = new()
        {
            Done(Skip(SW("AL_SPOILERS", "Ground spoilers: DISARM", "SPOILERS_ARM_TOGGLE", 0),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 0)), "AL_SPOILERS"),
            Done(Skip(SW("AL_FLAPS_UP", "Flaps: UP", "A32NX_FLAPS_HANDLE_INDEX", 0),
                s => s.IsPosition("A32NX_FLAPS_HANDLE_INDEX", 0)), "AL_FLAPS_UP"),
            // The After Landing Checklist's "Radar and predictive windshear: OFF" (ALC_WXR) is true
            // only when BOTH are off, so neither write completes it (FO-6): it is that read-back's
            // only line, so a write linking it latched the whole read-back with the other switch
            // still on. The read-only AL_WXR_PWS_CHECK after both completes it (the FO-13 shape,
            // never writes). 5 s: it starts after AL_PWS_OFF's 2 s pause and reads once a second,
            // the rate both switches are polled, so its last read comes some 6 s after the windshear
            // write, well past the second or two a polled switch lags its write. A timeout is
            // skipped aloud and keeps the line out of the latch.
            Done(Skip(SW("AL_WXR_OFF", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys", 1),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 1)), "AL_WXR_OFF"),
            Done(Skip(SW("AL_PWS_OFF", "Predictive windshear: OFF", "A32NX_SWITCH_RADAR_PWS_POSITION", 0),
                s => s.IsPosition("A32NX_SWITCH_RADAR_PWS_POSITION", 0)), "AL_PWS_OFF"),
            Done(Skip(WaitForField("AL_WXR_PWS_CHECK", "Radar and predictive windshear: OFF", "FO_WXR_PWS_OFF",
                    v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Skip),
                s => s.GetValue("FO_WXR_PWS_OFF") > 0.5), "ALC_WXR"),
            Done(Skip(SW("AL_STROBE_AUTO", "Strobes: AUTO", "LIGHTING_STROBE_0", 1),
                s => s.IsPosition("LIGHTING_STROBE_0", 1)), "AL_STROBE_AUTO"),
            Done(SW("AL_LANDING_OFF", "Landing lights: OFF", "LANDING_LIGHTS_OFF_THIRD_PARTY", 1), "AL_LANDING_OFF"),
            Done(Skip(SW("AL_NOSE_TAXI", "Nose light: TAXI", "LIGHTING_LANDING_1", 1),
                s => s.IsPosition("LIGHTING_LANDING_1", 1)), "AL_NOSE_TAXI"),
            // APU for the gate (skip the whole block when already available). The AVAIL wait
            // completes "APU: ON and available", never the master write (see BS_APU_MASTER): a
            // timed-out wait is skipped aloud and keeps the line out of the latch.
            Skip(SW("AL_APU_MASTER", "APU master: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(Wait("AL_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("AL_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Done(WaitForField("AL_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180), "AL_APU"),
            Skip(SW("AL_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_START_PB_IS_ON", 0)),
            Done(Skip(Multi("AL_ANTIICE_OFF", "Engine and wing anti-ice: OFF",
                    ("ENG_ANTI_ICE:1", 0), ("ENG_ANTI_ICE:2", 0), ("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", 0)),
                s => s.IsPosition("ENG_ANTI_ICE:1", 0) && s.IsPosition("ENG_ANTI_ICE:2", 0)
                     && s.IsPosition("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", 0)), "AL_ANTIICE_OFF"),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Shutdown / Parking
    // -----------------------------------------------------------------------
    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Parking brake, APU bleed, engine masters off, signs, beacon, fuel pumps, cockpit lights, ECAM door page.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN" },
        Steps = new()
        {
            // Started by mistake in flight, the masters below would shut the engines down and the
            // fuel pumps go off: never airborne (the MD-11's SD_ON_GROUND).
            OnGroundGuard("SD_ON_GROUND"),
            Done(Skip(SW("SD_PARKBRAKE", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")), "SD_PARKBRAKE"),
            Done(Skip(SW("SD_APUBLEED_ON", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")), "SD_APUBLEED_ON"),
            // The masters take the engine generators off the buses, so the APU or external power
            // must be able to carry them first: After Landing's APU wait only skips on a timeout,
            // and without this the masters went off onto the batteries. Up to two minutes for an
            // APU still starting, then the flow stops with the engines running (the MD-11's
            // PK_POWER_SOURCE, the 737s' SD_POWER_WAIT). Passes at once with both masters off.
            Skip(WaitForField("SD_POWER_SOURCE", "APU or external power available",
                    FbwA320StateEvaluator.ApuOrExtPowerField, v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop),
                s => s.IsPosition("ENGINE_1_MASTER", 0) && s.IsPosition("ENGINE_2_MASTER", 0)),
            Done(Skip(SW("SD_ENG1_OFF", "Engine 1 master: OFF", "ENGINE_1_MASTER", 0),
                s => s.IsPosition("ENGINE_1_MASTER", 0)), "SD_ENG1_OFF"),
            Done(Skip(SW("SD_ENG2_OFF", "Engine 2 master: OFF", "ENGINE_2_MASTER", 0),
                s => s.IsPosition("ENGINE_2_MASTER", 0)), "SD_ENG2_OFF"),
            // Transponder/TCAS to standby at shutdown (moved from After Landing so they
            // stay active until the aircraft is parked).
            Done(Skip(SW("SD_XPDR_STBY", "Transponder: STANDBY", "A32NX_TRANSPONDER_MODE", 0),
                s => s.IsPosition("A32NX_TRANSPONDER_MODE", 0)), "SD_XPDR_STBY"),
            Done(Skip(SW("SD_TCAS_STBY", "TCAS: STANDBY", "A32NX_SWITCH_TCAS_POSITION", 0),
                s => s.IsPosition("A32NX_SWITCH_TCAS_POSITION", 0)), "SD_TCAS_STBY"),
            // LS pushbuttons off at shutdown (mirrors approach AP_LS1/AP_LS2, inverted to 0).
            // IsPosition(..., 0), never !IsOn(...): an unread switch must not skip an absolute OFF write.
            Done(Skip(SW("SD_LS1", "LS captain: OFF", "A32NX_EFIS_L_LS_BUTTON_IS_ON", 0),
                s => s.IsPosition("A32NX_EFIS_L_LS_BUTTON_IS_ON", 0)), "SD_LS1"),
            Done(Skip(SW("SD_LS2", "LS first officer: OFF", "A32NX_EFIS_R_LS_BUTTON_IS_ON", 0),
                s => s.IsPosition("A32NX_EFIS_R_LS_BUTTON_IS_ON", 0)), "SD_LS2"),
            // SEATBELT_SIGN pseudo-key — see BS_SEATBELTS. "Already set" only on a sign read off; an
            // unread sign reaches the guarded toggle, which refuses it (SeatbeltToggleNeeded).
            Done(Skip(SW("SD_SEATBELTS_OFF", "Seatbelt signs: OFF", "SEATBELT_SIGN", 0),
                s => s.IsPosition("CABIN SEATBELTS ALERT SWITCH", 0)), "SD_SEATBELTS_OFF"),
            Done(Skip(SW("SD_BEACON_OFF", "Beacon: OFF", "BEACON_LIGHTS_SET", 0),
                s => s.IsPosition("LIGHT BEACON", 0)), "SD_BEACON_OFF"),
            Done(Skip(Multi("SD_FUELPUMPS_OFF", "Fuel pumps: ALL OFF",
                    ("FUEL_PUMP_L1", 0), ("FUEL_PUMP_L2", 0),
                    ("FUEL_PUMP_C1", 0), ("FUEL_PUMP_C2", 0),
                    ("FUEL_PUMP_R1", 0), ("FUEL_PUMP_R2", 0)),
                s => s.IsPosition("FUEL_PUMP_L1", 0) && s.IsPosition("FUEL_PUMP_L2", 0)
                     && s.IsPosition("FUEL_PUMP_C1", 0) && s.IsPosition("FUEL_PUMP_C2", 0)
                     && s.IsPosition("FUEL_PUMP_R1", 0) && s.IsPosition("FUEL_PUMP_R2", 0)), "SD_FUELPUMPS_OFF"),
            Done(Skip(SW("SD_NOSE_OFF", "Nose light: OFF", "LIGHTING_LANDING_1", 2),
                s => s.IsPosition("LIGHTING_LANDING_1", 2)), "SD_NOSE_OFF"),
            Done(Skip(SW("SD_TURNOFF_OFF", "Runway turn-off lights: OFF", "LIGHT TAXI:2", 0),
                s => s.IsPosition("LIGHT TAXI:2", 0)), "SD_TURNOFF_OFF"),
            Done(SW("SD_COCKPITDOOR", "Cockpit door: unlocked", "A32NX_COCKPIT_DOOR_LOCKED", 0), "SD_COCKPITDOOR"),
            // ★ Cockpit lighting: Bright for parking, + ECAM door page (spec §4)
            Done(SW("SD_COCKPITLT", "Cockpit lights: set", "A32NX_OVHD_INTLT_ANN", 1), "SD_COCKPITLT"),
            Done(Skip(SW("SD_DOME", "Dome light: bright", "A32NX_OVHD_INTLT_DOME", 100),
                s => s.GetValue("A32NX_OVHD_INTLT_DOME") >= 90), "SD_DOME"),
            Done(Skip(SW("SD_STBYCOMPASS", "Standby compass light: ON", "A32NX_STBY_COMPASS_LIGHT_TOGGLE", 1),
                s => s.IsOn("A32NX_STBY_COMPASS_LIGHT_TOGGLE")), "SD_STBYCOMPASS"),
            Done(Scene("SD_LTSCENE", FbwA320ActionExecutor.CockpitLightScene.ParkingBright), "SD_LTSCENE"),
            Done(SW("SD_ECAMDOOR", "ECAM page: door", "ECAM_PAGE_DOOR", 1), "SD_ECAMDOOR"),
        }
    };

    // -----------------------------------------------------------------------
    // 13. Securing
    // -----------------------------------------------------------------------
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Securing",
        Description = "ADIRS, oxygen, emergency lights, signs, APU off, wait for the APU flap, external power and batteries off, cockpit lights off.",
        // SECURING_CL: the Securing Checklist read-back, latched by a finished run through the Also links below.
        RelatedChecklistGroupIds = new[] { "SECURE", "SECURING_CL" },
        Steps = new()
        {
            // The IRS and the batteries go off below: never airborne (the MD-11's SD_ON_GROUND).
            OnGroundGuard("SC_ON_GROUND"),
            Done(Skip(Multi("SC_ADIRS", "IRS 1, 2 and 3: OFF",
                    ("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 0), ("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 0),
                    ("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 0)),
                s => s.IsPosition("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 0)
                     && s.IsPosition("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 0)
                     && s.IsPosition("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 0)), "SC_ADIRS"),
            // Crew oxygen OFF = 1 (inverted convention, see PF_OXY).
            Also(Done(Skip(SW("SC_OXY", "Crew oxygen supply: OFF", "PUSH_OVHD_OXYGEN_CREW", 1),
                s => s.IsPosition("PUSH_OVHD_OXYGEN_CREW", 1)), "SC_OXY"), "SCC_OXY"),
            Also(Done(Skip(SW("SC_EMEREXIT", "Emergency exit lights: OFF", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION", 2),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION", 2)), "SC_EMEREXIT"), "SCC_EMEREXIT"),
            Done(Skip(SW("SC_NOSMOKE", "No smoking: OFF", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 2),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 2)), "SC_NOSMOKE"),
            Done(Skip(SW("SC_APUBLEED", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)), "SC_APUBLEED"),
            Done(Skip(SW("SC_APUMASTER", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)), "SC_APUMASTER"),
            // FlyByWire's order (owner, 2026-10-14): wait for the APU flap to close BEFORE external
            // power goes off — the flap needs APU or external power to close. Instant when the APU
            // never ran (flap reads 0); an unread flap simply times out and the flow carries on (FOA-10).
            WaitForField("SC_APU_WAIT", "APU flap to close", "A32NX_APU_FLAP_OPEN_PERCENTAGE", v => v < 0.5, 150),
            // IsPosition(..., 0), never !IsOn(...): an unread switch must not skip an absolute OFF write.
            Done(Skip(SW("SC_EXTPWR_OFF", "External power: OFF", "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 0)), "SC_EXTPWR_OFF"),
            Also(Done(Skip(SW("SC_BAT1", "Battery 1: OFF", "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 0),
                s => s.IsPosition("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 0)), "SC_BAT1"), "SCC_BAT"),
            Also(Done(Skip(SW("SC_BAT2", "Battery 2: OFF", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 0),
                s => s.IsPosition("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 0)), "SC_BAT2"), "SCC_BAT"),
            // ★ Cockpit lighting off (spec §4.1): annunciator left bright for the next power-up,
            // dome and compass light off, scene off (FO-7: a step behind every line).
            Done(SW("SC_COCKPITLT", "Cockpit lights: annunciator bright", "A32NX_OVHD_INTLT_ANN", 1), "SC_COCKPITLT"),
            Done(Skip(SW("SC_DOME", "Dome light: off", "A32NX_OVHD_INTLT_DOME", 0),
                s => s.GetValue("A32NX_OVHD_INTLT_DOME") <= 5), "SC_DOME"),
            Done(Skip(SW("SC_STBYCOMPASS", "Standby compass light: OFF", "A32NX_STBY_COMPASS_LIGHT_TOGGLE", 0),
                s => s.IsPosition("A32NX_STBY_COMPASS_LIGHT_TOGGLE", 0)), "SC_STBYCOMPASS"),
            Done(Scene("SC_LTSCENE", FbwA320ActionExecutor.CockpitLightScene.Off), "SC_LTSCENE"),
        }
    };

    /// <summary>True when the flap handle (<c>A32NX_FLAPS_HANDLE_INDEX</c>, 0..4) reads a takeoff
    /// position, 1-3 — the same test as the AS_FLAPS line. 0, FULL and an unread handle (NaN) are
    /// not, so the AS_FLAPS_CAPT reminder speaks.</summary>
    internal static bool IsTakeoffFlapsLever(double lever) => lever is >= 0.5 and <= 3.5;

    // -----------------------------------------------------------------------
    // Step builders (mirror the Fenix/A380 files' helpers, retyped to FbwA320StateEvaluator)
    // -----------------------------------------------------------------------

    private static Step SW(string id, string label, string eventName, int target) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    // One lighting scene's knobs, through the executor's COCKPIT_LIGHT_SCENE_* pseudo-key (the
    // Fenix's spelling); labelled as the scene's checklist line.
    private static Step Scene(string id, FbwA320ActionExecutor.CockpitLightScene scene) =>
        SW(id, "Panel and integral brightness: SET", FbwA320ActionExecutor.CockpitLightSceneKey(scene), 1);

    private static Step Provider(string id, string label, string eventName, Func<FbwA320StateEvaluator, int?> provider) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValueProvider = provider,        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Multi(string id, string label, params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Wait(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,    };

    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,    };

    private static Step Skip(Step step, Func<FbwA320StateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }

    private static Step Done(Step step, string checklistItemId)
    {
        step.CompletesChecklistItemId = checklistItemId;
        return step;
    }

    // A further line the step delivers, beyond Done's: the same line in the flow's own *_CL
    // read-back (FlowStep.AlsoCompletesChecklistItemIds; FlowManager marks and, on a skip,
    // excludes every linked id).
    private static Step Also(Step step, params string[] alsoCompletes)
    {
        step.AlsoCompletesChecklistItemIds = alsoCompletes;
        return step;
    }

    // The aircraft's state says the First Officer must not act (FlowStep.LeaveAloneWhen): nothing is
    // sent, the text is spoken and the step's lines stay out of the latch.
    private static Step LeaveAlone(Step step, Func<FbwA320StateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }

    // Stops a cold-and-dark flow unless both engines read stopped (FO_ENGINES_OFF, FlyByWire's own
    // engine state; unread fails the guard): the MD-11's EnginesStoppedGuard, the Fenix's twin.
    private static Step EnginesStoppedGuard(string id) =>
        WaitForField(id, "Engines stopped", "FO_ENGINES_OFF", v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop);

    // Stops a flow that switches engines, IRS or batteries off unless the aircraft reads on the
    // ground (the stock SIM_ON_GROUND; unread fails the guard).
    private static Step OnGroundGuard(string id) =>
        WaitForField(id, "On the ground", "SIM_ON_GROUND", v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop);
}
