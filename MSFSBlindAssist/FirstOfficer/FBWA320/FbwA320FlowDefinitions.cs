using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.FBWA320;

using Flow = Models.FlowDefinition<FbwA320StateEvaluator>;
using Step = Models.FlowStep<FbwA320StateEvaluator>;

/// <summary>
/// Data-driven FlyByWire A320 (A32NX) First-Officer flow definitions — the same 12
/// phases as <see cref="Fenix.FenixFlowDefinitions"/> (Electrical Power Up through
/// Secure), re-keyed to A32NX control keys confirmed against
/// <see cref="MSFSBlindAssist.Aircraft.FlyByWireA320Definition.GetVariables"/>. Flow
/// steps write A320 varKeys via <see cref="FbwA320ActionExecutor"/>, which delegates
/// most writes to <see cref="MSFSBlindAssist.Aircraft.FlyByWireA320Definition.ApplyUIVariable"/>
/// — the same verified panel-write path the FBW A320 panels use — plus the executor's
/// pseudo-keys (FIRE_TEST_APU/ENG1/ENG2, CVR_TEST, TO_CONFIG_TEST, CABIN_CALL_ALL,
/// ECAM_PAGE_* (direct SD page-index write), BARO_STD/QNH, FCU_PUSH_SPEED/HEADING/ALT,
/// AP1_ENGAGE) for non-combo actions.
///
/// Value conventions (from <see cref="FbwA320StateEvaluator"/>, <see cref="FbwA320ActionExecutor"/>,
/// and the FlyByWireA320Definition panel definitions — the A320 is 2 engines, no ESS/APU
/// battery, and seatbelts are a genuine 2-position switch (no AUTO), unlike the A380):
/// - ENGINE_MODE_SELECTOR 0=Crank,1=Norm,2=Ign/Start.
/// - ENGINE_1_MASTER / ENGINE_2_MASTER 0=Off,1=On (FUELSYSTEM VALVE SWITCH:n underneath).
/// - SPOILERS_ARM_TOGGLE is a genuine TOGGLE event (no settable position) — Skip guards on
///   the real state var A32NX_SPOILERS_ARMED so a re-run never double-fires it.
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
/// - Takeoff flaps are NEVER auto-set (project-wide "no takeoff-flap automation" rule) —
///   unlike the Fenix source, AS_FLAPS here is a Captain reminder (matches the A380 flow).
/// - Cockpit lighting (§4.1): flow steps only pulse the ANN light key
///   (A32NX_OVHD_INTLT_ANN: 1=Bright, 2=Dim) per phase; the full multi-var scene
///   (dome/compass/integ/flood) is a Task 7 checklist CheckAction calling
///   FbwA320ActionExecutor.SetCockpitLighting directly (per the design brief).
/// - Engine start has no live N2 gate (A32NX_ENGINE_N2:n exists but is UpdateFrequency.OnRequest
///   and isn't in FbwA320StateEvaluator's PollFields) — mirrors the A380 flow's fixed-dwell
///   approach rather than the Fenix/PMDG N2-verification pattern.
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
        BuildBeforeTakeoff(),
        BuildAfterTakeoff(),
        BuildDescent(),
        BuildApproach(),
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
            // Safety-check prefix: every step skips quietly when already correct.
            Skip(SW("EPU_CHK_MASTER1", "Engine 1 master: OFF", "ENGINE_1_MASTER", 0),
                s => s.IsPosition("ENGINE_1_MASTER", 0)),
            Skip(SW("EPU_CHK_MASTER2", "Engine 2 master: OFF", "ENGINE_2_MASTER", 0),
                s => s.IsPosition("ENGINE_2_MASTER", 0)),
            Skip(SW("EPU_CHK_MODE", "Engine mode selector: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)),
            // Gear lever has no settable A32NX key (GEAR_HANDLE_POSITION is a read-only
            // stock SimVar) — Captain item, same safety intent as the Fenix source.
            Captain("EPU_CHK_GEAR", "Gear lever: DOWN"),
            Skip(Multi("EPU_CHK_WIPERS", "Wipers: OFF",
                    ("WIPER_LEFT", 0), ("WIPER_RIGHT", 0)),
                s => s.IsPosition("WIPER_LEFT", 0) && s.IsPosition("WIPER_RIGHT", 0)),
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
            // ★ Cockpit lighting (spec §4.1): Bright for ground prep. Flow pulses only the
            // ANN light key; the full scene is a Task 7 checklist CheckAction.
            Done(SW("EPU_COCKPITLT", "Cockpit lights: set", "A32NX_OVHD_INTLT_ANN", 1), "EPU_COCKPITLT"),
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
            Done(SW("PF_CVR", "CVR test — listen for the test tone", "CVR_TEST", 1), "PF_CVR"),
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
            Done(Skip(SW("BS_APU_MASTER", "APU master: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON")), "BS_APU"),
            Skip(Wait("BS_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("BS_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            WaitForField("BS_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop),
            // Defensive: release the latched START PB (mirrors the A380 flow's guard
            // against a stale 1 surprise-starting the APU on a later master-ON).
            Skip(SW("BS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
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
            // External power off; skip when not on the bus
            Done(Skip(SW("BS_EXTPWR_OFF", "External power: OFF", "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON")), "BS_EXTPWR_OFF"),
            // Seatbelt signs: 2-position toggle event on the A320 (no AUTO, unlike the A380).
            Done(Skip(SW("BS_SEATBELTS", "Seatbelt signs: ON", "CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 1),
                s => s.IsOn("CABIN SEATBELTS ALERT SWITCH")), "BS_SEATBELTS"),
            Done(Skip(SW("BS_BEACON", "Beacon: ON", "BEACON_LIGHTS_SET", 1),
                s => s.IsOn("LIGHT BEACON")), "BS_BEACON"),
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
        Description = "ECAM engine page, engine mode IGN/START, then engine 1 and engine 2 with dwell periods.",
        RelatedChecklistGroupIds = new[] { "ENGINE_START" },
        Steps = new()
        {
            // ★ ECAM page: engine (spec §4)
            Done(SW("ES_ECAMENG", "ECAM page: engine", "ECAM_PAGE_ENG", 1), "ES_ECAMENG"),
            Done(Skip(SW("ES_MODE", "Engine mode selector: IGN START", "ENGINE_MODE_SELECTOR", 2),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 2)), "ES_MODE"),
            // Engine 1 first, then engine 2. No live N2 gate is available on this
            // evaluator (A32NX_ENGINE_N2:n is UpdateFrequency.OnRequest and not polled) —
            // fixed dwell periods, matching the A380 flow's approach.
            Done(Skip(SW("ES_ENG1", "Engine 1 master: ON", "ENGINE_1_MASTER", 1),
                s => s.IsOn("ENGINE_1_MASTER")), "ES_ENG1"),
            Wait("ES_ENG1_DWELL", "Engine 1 starting — standby", 60),
            Done(Skip(SW("ES_ENG2", "Engine 2 master: ON", "ENGINE_2_MASTER", 1),
                s => s.IsOn("ENGINE_2_MASTER")), "ES_ENG2"),
            Wait("ES_ENG2_DWELL", "Engine 2 starting — standby", 60),
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
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
            Done(Skip(SW("AS_APUMASTER_OFF", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)), "AS_APUMASTER_OFF"),
            // Ground spoilers arm: SPOILERS_ARM_TOGGLE is a genuine toggle event (no
            // settable position) — Skip guards on the real state var so a re-run never
            // double-fires it back to disarmed.
            Done(Skip(SW("AS_SPOILERS_ARM", "Ground spoilers: ARMED", "SPOILERS_ARM_TOGGLE", 1),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 1)), "AS_SPOILERS_ARM"),
            Done(SW("AS_RUDDERTRIM", "Rudder trim: RESET", "A32NX_RUDDER_TRIM_RESET", 1), "AS_RUDDERTRIM"),
            // Takeoff flaps are NEVER auto-set (project-wide rule) — Captain item, unlike
            // the Fenix source's SimBrief-driven Provider step.
            Captain("AS_FLAPS", "Flaps: set for takeoff"),
            Done(Skip(SW("AS_NOSE_TAXI", "Nose light: TAXI", "LIGHTING_LANDING_1", 1),
                s => s.IsPosition("LIGHTING_LANDING_1", 1)), "AS_NOSE_TAXI"),
            Captain("AS_ANTIICE", "Set engine and wing anti-ice as required"),
            Captain("AS_PITCHTRIM", "Set pitch trim per the loadsheet"),
            // ★ Cockpit lighting: Dim for taxi/flight (spec §4.1), + ECAM status page (spec §4)
            Done(SW("AS_COCKPITLT", "Cockpit lights: dim", "A32NX_OVHD_INTLT_ANN", 2), "AS_COCKPITLT"),
            Done(SW("AS_ECAMSTS", "ECAM page: status", "ECAM_PAGE_STS", 1), "AS_ECAMSTS"),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Before Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Description = "Autobrake MAX, radar, TCAS, exterior lights, cabin notify for takeoff.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF" },
        Steps = new()
        {
            // ★ Autobrake MAX (spec §4)
            Done(Skip(SW("BT_AUTOBRAKE", "Autobrake: MAX", "AUTOBRAKE_MODE", 3),
                s => s.IsPosition("A32NX_AUTOBRAKES_ARMED_MODE", 3)), "BT_AUTOBRAKE"),
            Done(Skip(SW("BT_WXR", "Weather radar: SYSTEM 1", "XMLVAR_A320_WeatherRadar_Sys", 0),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 0)), "BT_WXR"),
            Done(Skip(SW("BT_PWS", "Predictive windshear: AUTO", "A32NX_SWITCH_RADAR_PWS_POSITION", 1),
                s => s.IsPosition("A32NX_SWITCH_RADAR_PWS_POSITION", 1)), "BT_PWS"),
            Done(Skip(SW("BT_TCAS", "TCAS: TA/RA", "A32NX_SWITCH_TCAS_POSITION", 2),
                s => s.IsPosition("A32NX_SWITCH_TCAS_POSITION", 2)), "BT_TCAS"),
            Done(Skip(SW("BT_XPDRAUTO", "Transponder: AUTO", "A32NX_TRANSPONDER_MODE", 1),
                s => s.IsPosition("A32NX_TRANSPONDER_MODE", 1)), "BT_XPDRAUTO"),
            // Takeoff config test: held ECP TO_CONF_TEST H-event via the executor's
            // TakeoffConfigTestAsync pseudo-key (sim + source verified; the F/CTL ECAM
            // page itself still has no dedicated key and stays out of scope here).
            Done(SW("BT_CONFIG", "Takeoff config test", "TO_CONFIG_TEST", 1), "BT_CONFIG"),
            Done(Skip(SW("BT_TURNOFF", "Runway turn-off lights: ON", "LIGHT TAXI:2", 1),
                s => s.IsOn("LIGHT TAXI:2")), "BT_TURNOFF"),
            Done(SW("BT_LANDING_LT", "Landing lights: ON", "LANDING_LIGHTS_ON_THIRD_PARTY", 1), "BT_LANDING_LT"),
            Done(Skip(SW("BT_NOSE_TO", "Nose light: TAKEOFF", "LIGHTING_LANDING_1", 0),
                s => s.IsPosition("LIGHTING_LANDING_1", 0)), "BT_NOSE_TO"),
            Done(Skip(SW("BT_STROBE", "Strobes: ON", "LIGHTING_STROBE_0", 0),
                s => s.IsPosition("LIGHTING_STROBE_0", 0)), "BT_STROBE"),
            // ★ Cabin notify for takeoff (spec §4)
            Done(SW("BT_CABIN", "Advise the cabin crew for takeoff (call all)", "CABIN_CALL_ALL", 1), "BT_CABIN"),
            Captain("BT_CLEARANCE", "Obtain takeoff clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 7. After Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Spoilers disarm, packs on, turn-off lights off. Gear and autopilot are handled by the auto-managers.",
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
        }
    };

    // -----------------------------------------------------------------------
    // 8. Descent
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
            Done(Skip(SW("DC_SEATBELTS", "Seatbelt signs: ON", "CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 1),
                s => s.IsOn("CABIN SEATBELTS ALERT SWITCH")), "DC_SEATBELTS"),
            Captain("DC_ARRPERF", "Calculate arrival performance on the EFB"),
            Captain("DC_MCDU", "Complete the MCDU approach page and minimums before top of descent"),
        }
    };

    // -----------------------------------------------------------------------
    // 9. Approach
    // -----------------------------------------------------------------------
    private static Flow BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "LS on both sides, notify the cabin for landing, approach reminders.",
        RelatedChecklistGroupIds = new[] { "APPROACH" },
        Steps = new()
        {
            Done(Skip(SW("AP_LS1", "LS captain: ON", "A32NX_EFIS_L_LS_BUTTON_IS_ON", 1),
                s => s.IsOn("A32NX_EFIS_L_LS_BUTTON_IS_ON")), "AP_LS1"),
            Done(Skip(SW("AP_LS2", "LS first officer: ON", "A32NX_EFIS_R_LS_BUTTON_IS_ON", 1),
                s => s.IsOn("A32NX_EFIS_R_LS_BUTTON_IS_ON")), "AP_LS2"),
            // ★ Cabin notify for landing (spec §4)
            Done(SW("AP_CABIN", "Notify the cabin crew for landing (call all)", "CABIN_CALL_ALL", 1), "AP_CABIN"),
            Captain("AP_MINIMUMS", "Check minimums set on the MCDU approach page"),
            Captain("AP_ENGMODE", "Set engine mode selector as required"),
        }
    };

    // -----------------------------------------------------------------------
    // 10. After Landing (pilot-triggered once clear of the runway)
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
            Done(Skip(SW("AL_WXR_OFF", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys", 1),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 1)), "AL_WXR_OFF"),
            Done(Skip(SW("AL_PWS_OFF", "Predictive windshear: OFF", "A32NX_SWITCH_RADAR_PWS_POSITION", 0),
                s => s.IsPosition("A32NX_SWITCH_RADAR_PWS_POSITION", 0)), "AL_PWS_OFF"),
            Done(Skip(SW("AL_STROBE_AUTO", "Strobes: AUTO", "LIGHTING_STROBE_0", 1),
                s => s.IsPosition("LIGHTING_STROBE_0", 1)), "AL_STROBE_AUTO"),
            Done(SW("AL_LANDING_OFF", "Landing lights: OFF", "LANDING_LIGHTS_OFF_THIRD_PARTY", 1), "AL_LANDING_OFF"),
            Done(Skip(SW("AL_NOSE_TAXI", "Nose light: TAXI", "LIGHTING_LANDING_1", 1),
                s => s.IsPosition("LIGHTING_LANDING_1", 1)), "AL_NOSE_TAXI"),
            // APU for the gate (skip the whole block when already available)
            Done(Skip(SW("AL_APU_MASTER", "APU master: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")), "AL_APU"),
            Skip(Wait("AL_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("AL_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            WaitForField("AL_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180),
            Skip(SW("AL_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
            Done(Skip(Multi("AL_ANTIICE_OFF", "Engine and wing anti-ice: OFF",
                    ("ENG_ANTI_ICE:1", 0), ("ENG_ANTI_ICE:2", 0), ("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", 0)),
                s => s.IsPosition("ENG_ANTI_ICE:1", 0) && s.IsPosition("ENG_ANTI_ICE:2", 0)
                     && s.IsPosition("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", 0)), "AL_ANTIICE_OFF"),
        }
    };

    // -----------------------------------------------------------------------
    // 11. Shutdown / Parking
    // -----------------------------------------------------------------------
    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Parking brake, APU bleed, engine masters off, signs, beacon, fuel pumps, cockpit lights, ECAM door page.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN" },
        Steps = new()
        {
            Done(Skip(SW("SD_PARKBRAKE", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")), "SD_PARKBRAKE"),
            Done(Skip(SW("SD_APUBLEED_ON", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")), "SD_APUBLEED_ON"),
            Done(Skip(SW("SD_ENG1_OFF", "Engine 1 master: OFF", "ENGINE_1_MASTER", 0),
                s => s.IsPosition("ENGINE_1_MASTER", 0)), "SD_ENG1_OFF"),
            Done(Skip(SW("SD_ENG2_OFF", "Engine 2 master: OFF", "ENGINE_2_MASTER", 0),
                s => s.IsPosition("ENGINE_2_MASTER", 0)), "SD_ENG2_OFF"),
            // Transponder/TCAS to standby at shutdown (moved from After Landing so they
            // stay active until the aircraft is parked).
            Done(Skip(SW("SD_XPDR_STBY", "Transponder: STANDBY", "A32NX_TRANSPONDER_MODE", 0),
                s => s.IsPosition("A32NX_TRANSPONDER_MODE", 0)), "SD_XPDR_STBY"),
            Skip(SW("SD_TCAS_STBY", "TCAS: STANDBY", "A32NX_SWITCH_TCAS_POSITION", 0),
                s => s.IsPosition("A32NX_SWITCH_TCAS_POSITION", 0)),
            // LS pushbuttons off at shutdown (mirrors approach AP_LS1/AP_LS2, inverted to 0).
            Done(Skip(SW("SD_LS1", "LS captain: OFF", "A32NX_EFIS_L_LS_BUTTON_IS_ON", 0),
                s => !s.IsOn("A32NX_EFIS_L_LS_BUTTON_IS_ON")), "SD_LS1"),
            Done(Skip(SW("SD_LS2", "LS first officer: OFF", "A32NX_EFIS_R_LS_BUTTON_IS_ON", 0),
                s => !s.IsOn("A32NX_EFIS_R_LS_BUTTON_IS_ON")), "SD_LS2"),
            Done(Skip(SW("SD_SEATBELTS_OFF", "Seatbelt signs: OFF", "CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 0),
                s => !s.IsOn("CABIN SEATBELTS ALERT SWITCH")), "SD_SEATBELTS_OFF"),
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
            Done(SW("SD_ECAMDOOR", "ECAM page: door", "ECAM_PAGE_DOOR", 1), "SD_ECAMDOOR"),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Securing
    // -----------------------------------------------------------------------
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Securing",
        Description = "ADIRS, oxygen, emergency lights, signs, APU and batteries off, cockpit lights off.",
        RelatedChecklistGroupIds = new[] { "SECURE" },
        Steps = new()
        {
            Done(Skip(Multi("SC_ADIRS", "IRS 1, 2 and 3: OFF",
                    ("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 0), ("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 0),
                    ("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 0)),
                s => s.IsPosition("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 0)
                     && s.IsPosition("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 0)
                     && s.IsPosition("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 0)), "SC_ADIRS"),
            // Crew oxygen OFF = 1 (inverted convention, see PF_OXY).
            Done(Skip(SW("SC_OXY", "Crew oxygen supply: OFF", "PUSH_OVHD_OXYGEN_CREW", 1),
                s => s.IsPosition("PUSH_OVHD_OXYGEN_CREW", 1)), "SC_OXY"),
            Done(Skip(SW("SC_EMEREXIT", "Emergency exit lights: OFF", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION", 2),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION", 2)), "SC_EMEREXIT"),
            Done(Skip(SW("SC_NOSMOKE", "No smoking: OFF", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 2),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", 2)), "SC_NOSMOKE"),
            Done(Skip(SW("SC_APUBLEED", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)), "SC_APUBLEED"),
            Done(Skip(SW("SC_APUMASTER", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)), "SC_APUMASTER"),
            Skip(SW("SC_EXTPWR_OFF", "External power: OFF", "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON")),
            Done(Skip(SW("SC_BAT1", "Battery 1: OFF", "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 0),
                s => s.IsPosition("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 0)), "SC_BAT1"),
            Done(Skip(SW("SC_BAT2", "Battery 2: OFF", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 0),
                s => s.IsPosition("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 0)), "SC_BAT2"),
            // ★ Cockpit lighting off (spec §4.1) — the full off-scene is a Task 7 checklist item.
            Done(SW("SC_COCKPITLT", "Cockpit lights: annunciator bright", "A32NX_OVHD_INTLT_ANN", 1), "SC_COCKPITLT"),
        }
    };

    // -----------------------------------------------------------------------
    // Step builders (mirror the Fenix/A380 files' helpers, retyped to FbwA320StateEvaluator)
    // -----------------------------------------------------------------------

    private static Step SW(string id, string label, string eventName, int target) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,
        PostActionDelayMs = 300,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Provider(string id, string label, string eventName, Func<FbwA320StateEvaluator, int?> provider) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValueProvider = provider,
        PostActionDelayMs = 300,
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

    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,
        PostActionDelayMs = 200,
    };

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
}
