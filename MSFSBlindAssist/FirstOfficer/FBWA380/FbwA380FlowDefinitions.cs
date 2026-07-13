using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

using Flow = Models.FlowDefinition<FbwA380StateEvaluator>;
using Step = Models.FlowStep<FbwA380StateEvaluator>;

/// <summary>
/// Data-driven FlyByWire A380 First-Officer flow definitions, covering the 12 automated
/// phases (Cockpit Preparation through Parking). Flow steps write A380 varKeys via
/// <see cref="FbwA380ActionExecutor"/>, which delegates most writes to
/// <see cref="MSFSBlindAssist.Aircraft.FlyByWireA380Definition.ApplyUIVariable"/> — the same
/// verified panel-write path the FBWA380 panels use — plus a handful of pseudo-keys
/// (FCU_PUSH_SPEED / FCU_PUSH_HEADING / FCU_PUSH_ALT / BARO_STD / BARO_QNH) intercepted by
/// the executor for non-combo actions.
///
/// Value conventions (from <see cref="FbwA380StateEvaluator"/> and the FlyByWire A380
/// panel definitions):
/// - ENGINE_MODE_SELECTOR 1=Norm, 2=Ign/Start (fanned to all 4 engines by MSFSBA's watchdog).
/// - ENG_VALVE_SWITCH:n 0=Off, 1=On/Start (fuel valve per engine, n=1..4).
/// - A380X_MSFSBA_SPOILERS_ARM is a WRITE-ONLY act key (0=disarm, 1=arm); the real armed
///   STATE is read from A32NX_SPOILERS_ARMED — never Skip on the write key's own value.
/// - Landing lights 0=Off, 1=On (LIGHT_LANDING = wing LDG LT, indexed LIGHT LANDING:2).
///   The NOSE light is a faithful 3-position selector (NOSE_LIGHT: 0=T.O., 1=Taxi, 2=Off) —
///   the old on/off LIGHT_TAXI_OVHD key no longer exists (PR #139 API audit).
/// - Signs: SEATBELT_SIGN is the 3-position SWITCH (0=On, 1=Auto, 2=Off); the actual sign
///   illumination is the read-only SEATBELT_SIGN_LIGHT (stock CABIN SEATBELTS ALERT SWITCH,
///   0/1) — skip/wait conditions key on the LIGHT so AUTO with the sign lit also counts.
///   Smoking XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position 0/1=Auto;
///   emergency exit XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position 0/1=Arm.
/// - EFIS ND mode 0=ILS/Rose ILS, 3=ARC; range index 3=40 (A32NX_EFIS_{L,R}_ND_MODE/_RANGE).
/// - This aircraft has no FO_ENGINE_N2 gating in these flows (unlike the Fenix/PMDG 737) —
///   the two ENGINE_START waits are fixed dwell periods, matching the source content list.
/// </summary>
public static class FbwA380FlowDefinitions
{
    public static List<Flow> Build() => new()
    {
        BuildCockpitPrep(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildTaxi(),
        BuildLineup(),
        BuildAfterTakeoff(),
        BuildApproach(),
        BuildLanding(),
        BuildAfterLanding(),
        BuildParking(),
    };

    // -----------------------------------------------------------------------
    // 1. Cockpit Preparation
    // -----------------------------------------------------------------------
    private static Flow BuildCockpitPrep() => new()
    {
        Id = "COCKPIT_PREP", Name = "Cockpit Preparation",
        Description = "Safety checks, batteries and ground power, overhead setup, EFIS, FCU.",
        RelatedChecklistGroupIds = new[] { "COCKPIT_PREP_CL" },
        Steps = new()
        {
            Captain("CP_WIPERS", "Wipers off"),
            Skip(SW("CP_GEAR", "Gear lever: DOWN", "A32NX_GEAR_HANDLE_POSITION", 1),
                s => s.IsPosition("A32NX_GEAR_HANDLE_POSITION", 1)),
            Captain("CP_FLAPS", "Flaps: confirm up"),
            Skip(SW("CP_SPOILERS", "Spoilers: disarmed", "A380X_MSFSBA_SPOILERS_ARM", 0),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 0)),   // write the Act key, READ the real state var
            Skip(SW("CP_PARKBRK", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")),
            Captain("CP_THROTTLES", "Confirm thrust levers idle"),
            Skip(SW("CP_ENGMODE", "Engine mode: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)),
            Multi("CP_MASTERS_OFF", "Engine masters 1 to 4: OFF",
                ("ENG_VALVE_SWITCH:1", 0), ("ENG_VALVE_SWITCH:2", 0),
                ("ENG_VALVE_SWITCH:3", 0), ("ENG_VALVE_SWITCH:4", 0)),
            Skip(SW("CP_WXR", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys", 0),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 0)),
            // All FOUR A380 batteries to AUTO (BAT 1/2/ESS/APU) — the panel exposes all
            // four; setting only 1+2 left the ESS + APU batteries off.
            Multi("CP_BAT", "Batteries: ON",
                ("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 1), ("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 1),
                ("A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", 1), ("A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO", 1)),
            Wait("CP_STBY1", "Standby", 5),
            // Cockpit lights: annunciator + integral lights BRIGHT (position 1). SOP is
            // "as required"; Bright is the deterministic ground-prep setting on this build
            // (dome/flood/pedestal knobs are not L:var-settable). ANN LT: 0=Test/1=Bright/2=Dim.
            Multi("CP_COCKPITLT", "Cockpit lights: set",
                ("A380X_OVHD_ANN_LT_POSITION", 1), ("A32NX_OVHD_INTLT_ANN", 1)),
            Multi("CP_GPU", "Ground power: ON",
                ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 1), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 1),
                ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 1), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 1)),
            Captain("CP_INSTRBRT", "Instrument brightness: high"),
            Multi("CP_ADIRS", "ADIRS: NAV",
                ("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 1),
                ("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 1),
                ("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 1)),
            Skip(SW("CP_OXY", "Crew oxygen: ON", "PUSH_OVHD_OXYGEN_CREW", 0),
                s => s.IsPosition("PUSH_OVHD_OXYGEN_CREW", 0)),
            // Held fire test (executor pseudo-key): master warning + continuous
            // repetitive chime while held; the def's release write auto-acknowledges
            // the master warning. Fenix order: oxygen → fire test.
            Done(SW("CP_FIRETEST", "Fire test — listen for the fire warning", "FIRE_TEST", 1),
                "CP_FIRETEST"),
            // Recorder ground control: the CVR/recorder ground-test enable. A plain writable
            // bool (calc-path via the executor's SetLVar fallback). It is a GROUND-ONLY function
            // — the FBW system inhibits/reverts it in flight (live-verified), which is fine since
            // Cockpit Prep runs on the ground. No Skip (state isn't polled; the write is idempotent).
            SW("CP_GNDCTL", "Ground control: on", "A32NX_RCDR_GROUND_CONTROL_ON", 1),
            Multi("CP_NAVLOGO", "Nav and logo lights: ON", ("LIGHT_NAV", 1), ("LIGHT_LOGO", 1)),
            Skip(SW("CP_SEATBELT", "Seatbelt signs: ON", "SEATBELT_SIGN", 0), s => s.IsOn("SEATBELT_SIGN_LIGHT")),
            Skip(SW("CP_NOSMOKE", "No smoking signs: AUTO", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", 1),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", 1)),
            Skip(SW("CP_EMEREXIT", "Emergency exit lighting: ARM", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", 1),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", 1)),
            Captain("CP_LTSTEST", "Lights test"),
            Skip(SW("CP_WINGAI", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD", 0),
                s => s.IsPosition("WING_ANTI_ICE_OVHD", 0)),
            Skip(SW("CP_WINGLT", "Wing lights: OFF", "LIGHT_WING", 0), s => s.IsPosition("LIGHT_WING", 0)),
            Multi("CP_PACKS", "Packs: ON",
                ("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1), ("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 1)),
            Skip(SW("CP_XBLEED", "Crossbleed: AUTO", "A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", 1),
                s => s.IsPosition("A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", 1)),
            Skip(SW("CP_PACKFLOW", "Pack flow: NORMAL", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION", 2),
                s => s.IsPosition("A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION", 2)),
            Multi("CP_HOTAIR", "Hot air: ON",
                ("A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON", 1), ("A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON", 1)),
            Captain("CP_AIRTEMP", "Cabin temperature: set as required"),
            Multi("CP_BARO", "Baro reference: hectopascals",
                ("XMLVAR_Baro_Selector_HPA_1", 1), ("XMLVAR_Baro_Selector_HPA_2", 1)),
            Captain("CP_ALTIMETERS", "Altimeters: set QNH"),
            Skip(SW("CP_ANTISKID", "Anti-skid: ON", "ANTISKID_BRAKES_ACTIVE", 1), s => s.IsOn("ANTISKID_BRAKES_ACTIVE")),
            Captain("CP_ALTRPT", "Altitude reporting: on"),
            Captain("CP_TCASTRAFFIC", "TCAS traffic: all"),
            Captain("CP_TCASMODE", "TCAS mode: standby"),
            Captain("CP_XPDR", "Transponder: standby"),
            Multi("CP_EFISMODE", "EFIS mode: ARC", ("A32NX_EFIS_L_ND_MODE", 3), ("A32NX_EFIS_R_ND_MODE", 3)),
            Multi("CP_EFISRANGE", "EFIS range: 40", ("A32NX_EFIS_L_ND_RANGE", 3), ("A32NX_EFIS_R_ND_RANGE", 3)),
            Multi("CP_FD", "Flight directors: ON", ("FD_1_CTL", 1), ("FD_2_CTL", 1)),
            Captain("CP_CLOCK", "Clock: reset"),
            // ECAM SD page selection — real ECP write (A32NX_ECAM_SD_CURRENT_PAGE_INDEX:
            // door=5, APU=1, engine=0, F/Ctl=11). Selecting a manual page overrides the
            // Airbus auto-page until the next auto event; acceptable during ground prep.
            SW("CP_ECAMPAGE", "ECAM page: door", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 5),
            WaitForField("CP_WAIT_SB", "Waiting for seatbelt signs on", "SEATBELT_SIGN_LIGHT", v => v > 0.5, 60),
            Captain("CP_IFR", "Obtain IFR clearance"),
            Captain("CP_PAYLOAD", "Load payload on the EFB"),
            Captain("CP_MCDU", "Program the MCDU"),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Before Start
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "Cockpit door, FCU managed modes, APU start, ground power off, fuel pumps, beacon.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START_CL" },
        Steps = new()
        {
            Skip(SW("BS_COCKPITDOOR", "Cockpit door: LOCKED", "A32NX_COCKPIT_DOOR_LOCKED", 1),
                s => s.IsOn("A32NX_COCKPIT_DOOR_LOCKED")),
            SW("BS_FCUSPD", "FCU speed: managed", "FCU_PUSH_SPEED", 1),
            SW("BS_FCUHDG", "FCU heading: managed", "FCU_PUSH_HEADING", 1),
            Captain("BS_ALT", "Set cleared altitude on the FCU"),
            SW("BS_FCUALT", "FCU altitude: pushed", "FCU_PUSH_ALT", 1),
            SW("BS_ECAMPAGE", "ECAM page: APU", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 1),
            // APU block: master on, dwell, START pushbutton, wait for AVAIL. The master
            // switch alone does NOT start the FBW APU — the START PB press is required —
            // and external power must stay on the buses until the APU is actually
            // available. Stop policy on the AVAIL wait: a start failure aborts the flow
            // BEFORE the external-power disconnect below. Steps skip once AVAIL is lit.
            Skip(SW("BS_APU", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON")),
            Skip(Wait("BS_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("BS_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            WaitForField("BS_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop),
            // Defensive: release the latched START PB (no repo evidence FBW auto-clears
            // it, and a stale latched 1 could surprise-start the APU on a later
            // master-ON). Silently skipped when FBW already cleared it.
            Skip(SW("BS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
            Skip(SW("BS_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")),
            Multi("BS_GPU_OFF", "Ground power: OFF",
                ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0),
                ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0)),
            Captain("BS_GPU_DISC", "Disconnect ground power on the EFB"),
            // ALL 20 pumps ON — 8 feed + 12 transfer (outer/mid/inner/trim). The transfer
            // pumps are manual on/off pumps like the feed pumps (same electrical-circuit
            // toggle), NOT auto-starting: FBW's own POWERUP_CONFIG preset turns all 20 on and
            // real A380 SOP runs the transfer pumps continuously (the FQMS schedules WHICH tank
            // transfers into the feed tanks, but the pumps stay on). Feeding only the 8 feed
            // pumps left the transfer pumps off — an incomplete config vs FBW's own preset.
            Multi("BS_FUELPUMPS", "Fuel pumps: ON",
                ("FUELPUMP_FEEDTK1_MAIN", 1), ("FUELPUMP_FEEDTK1_STBY", 1),
                ("FUELPUMP_FEEDTK2_MAIN", 1), ("FUELPUMP_FEEDTK2_STBY", 1),
                ("FUELPUMP_FEEDTK3_MAIN", 1), ("FUELPUMP_FEEDTK3_STBY", 1),
                ("FUELPUMP_FEEDTK4_MAIN", 1), ("FUELPUMP_FEEDTK4_STBY", 1),
                ("FUELPUMP_OUTR_L", 1), ("FUELPUMP_MID_L_FWD", 1), ("FUELPUMP_MID_L_AFT", 1),
                ("FUELPUMP_INR_L_FWD", 1), ("FUELPUMP_INR_L_AFT", 1),
                ("FUELPUMP_OUTR_R", 1), ("FUELPUMP_MID_R_FWD", 1), ("FUELPUMP_MID_R_AFT", 1),
                ("FUELPUMP_INR_R_FWD", 1), ("FUELPUMP_INR_R_AFT", 1),
                ("FUELPUMP_TRIM_L", 1), ("FUELPUMP_TRIM_R", 1)),
            Skip(SW("BS_BEACON", "Beacon lights: ON", "LIGHT_BEACON", 1), s => s.IsOn("LIGHT_BEACON")),
            Captain("BS_THROTTLES", "Confirm thrust levers idle"),
            Skip(SW("BS_PARKBRK", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")),
            Captain("BS_XPDR", "Transponder: set for departure"),
            Captain("BS_TCAS", "TCAS mode: TA/RA"),
            Captain("BS_MCDUPERF", "Program the MCDU PERF page"),
            Captain("BS_TRIM", "Set trim"),
            Captain("BS_ACARS", "Start ACARS"),
            Captain("BS_TAXICLR", "Obtain pushback and start clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 3. Engine Start
    // -----------------------------------------------------------------------
    private static Flow BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Description = "Engine mode IGN/START, engines 1 and 2, then 3 and 4, with dwell periods.",
        RelatedChecklistGroupIds = new string[] { },
        Steps = new()
        {
            SW("ES_ECAMPAGE", "ECAM page: engine", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 0),
            Skip(SW("ES_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")),
            Skip(SW("ES_ENGMODE", "Engine mode: IGN", "ENGINE_MODE_SELECTOR", 2),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 2)),
            Skip(SW("ES_ENG1", "Engine 1 master: START", "ENG_VALVE_SWITCH:1", 1),
                s => s.IsOn("ENG_VALVE_SWITCH:1")),
            Skip(SW("ES_ENG2", "Engine 2 master: START", "ENG_VALVE_SWITCH:2", 1),
                s => s.IsOn("ENG_VALVE_SWITCH:2")),
            Wait("ES_STBY1", "Standby", 60),
            Skip(SW("ES_ENG3", "Engine 3 master: START", "ENG_VALVE_SWITCH:3", 1),
                s => s.IsOn("ENG_VALVE_SWITCH:3")),
            Skip(SW("ES_ENG4", "Engine 4 master: START", "ENG_VALVE_SWITCH:4", 1),
                s => s.IsOn("ENG_VALVE_SWITCH:4")),
            Wait("ES_STBY2", "Standby", 60),
        }
    };

    // -----------------------------------------------------------------------
    // 4. After Start
    // -----------------------------------------------------------------------
    private static Flow BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Mode NORM, APU off, spoilers armed, taxi light, rudder trim reset.",
        RelatedChecklistGroupIds = new[] { "AFTER_START_CL" },
        Steps = new()
        {
            Skip(SW("AS_ENGMODE", "Engine mode: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)),
            Captain("AS_WINGAI", "Wing anti-ice: set as required"),
            Captain("AS_ENGAI", "Engine anti-ice: set as required"),
            SW("AS_ECAMPAGE", "ECAM page: APU", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 1),
            // Release a still-latched START PB first — master-off must never coexist with
            // START=1 (a latched 1 would surprise-start the APU on the next master-ON).
            Skip(SW("AS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
            Skip(SW("AS_APU_OFF", "APU: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)),
            Skip(SW("AS_APUBLEED_OFF", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)),
            Skip(SW("AS_NOSE_TAXI", "Nose light: TAXI", "NOSE_LIGHT", 1),
                s => s.IsPosition("NOSE_LIGHT", 1)),
            // Cockpit lights DIM (position 2) for taxi/flight — the reduced setting
            // (ANN LT has no true OFF; 2=Dim is the in-flight position).
            Multi("AS_COCKPITLT", "Cockpit lights: off",
                ("A380X_OVHD_ANN_LT_POSITION", 2), ("A32NX_OVHD_INTLT_ANN", 2)),
            Skip(SW("AS_SPOILERS_ARM", "Spoilers: ARMED", "A380X_MSFSBA_SPOILERS_ARM", 1),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 1)),
            SW("AS_RUDDERTRIM", "Rudder trim: RESET", "A32NX_RUDDER_TRIM_RESET", 1),
            Captain("AS_FLAPS", "Flaps: set for takeoff"),
        }
    };

    // -----------------------------------------------------------------------
    // 5. Taxi
    // -----------------------------------------------------------------------
    private static Flow BuildTaxi() => new()
    {
        Id = "TAXI", Name = "Taxi",
        Description = "Autobrake MAX, engine mode NORM, weather radar and predictive windshear on.",
        RelatedChecklistGroupIds = new[] { "TAXI_CL" },
        Steps = new()
        {
            // RTO arm is a MOMENTARY press (the L:var auto-resets to 0 ~1.5 s later);
            // the latched armed state is the separate A32NX_AUTOBRAKES_RTO_ARMED
            // (Continuous+IsAnnounced, batch-cached). Skip on the ARMED state so a
            // re-run never re-presses the momentary while already armed (a second
            // press would disarm it).
            Skip(SW("TX_AUTOBRAKE", "Autobrake: MAX", "A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED", 1),
                s => s.IsOn("A32NX_AUTOBRAKES_RTO_ARMED")),
            Skip(SW("TX_ENGMODE", "Engine mode: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)),
            Skip(SW("TX_WXR", "Weather radar: ON", "XMLVAR_A320_WeatherRadar_Sys", 1),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 1)),
            Skip(SW("TX_PWS", "Predictive windshear: ON", "A32NX_SWITCH_RADAR_PWS_Position", 1),
                s => s.IsPosition("A32NX_SWITCH_RADAR_PWS_Position", 1)),
            Captain("TX_FLAPS", "Flaps: set for takeoff"),
            SW("TX_ECAMPAGE", "ECAM page: flight controls", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 11),
            // Takeoff config test: held ECP T.O CONFIG button via the executor's
            // TakeoffConfigTestAsync pseudo-key (press → 2 s → release; sim-verified the
            // A32NX_BTN_TOCONFIG L:var holds). Done after flaps/config so the FWC verifies
            // a complete takeoff configuration ("T.O CONFIG NORMAL" on the ECAM).
            SW("TX_CONFIG", "Takeoff config test", "TO_CONFIG_TEST", 1),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Lineup
    // -----------------------------------------------------------------------
    private static Flow BuildLineup() => new()
    {
        Id = "LINEUP", Name = "Lineup",
        Description = "TCAS TA/RA, packs, strobe and landing/nose lights for takeoff.",
        RelatedChecklistGroupIds = new[] { "LINEUP_CL" },
        Steps = new()
        {
            Captain("LU_TCAS", "TCAS mode: TA/RA"),
            Captain("LU_PACKS", "Packs: set for takeoff as required"),
            Skip(SW("LU_STROBE", "Strobe lights: ON", "LIGHT_STROBE", 1), s => s.IsOn("LIGHT_STROBE")),
            // Landing lights = wing LDG LT (LIGHT_LANDING 0/1); the nose T.O. beam is the
            // separate 3-position NOSE_LIGHT selector (0=T.O.) since the PR #139 split.
            Skip(Multi("LU_LANDING", "Landing and nose lights: ON",
                    ("LIGHT_LANDING", 1), ("NOSE_LIGHT", 0)),
                s => s.IsOn("LIGHT_LANDING") && s.IsPosition("NOSE_LIGHT", 0)),
            // Runway turn-off lights ON (drives LIGHT TAXI:2 + :3 via the indexed
            // TAXI_LIGHTS_SET — live-verified 2026-07-13). Turned OFF in After Takeoff.
            Skip(SW("LU_TURNOFF", "Runway turn-off lights: ON", "LIGHT_RWY_TURNOFF", 1),
                s => s.IsOn("LIGHT_RWY_TURNOFF")),
            // Advise the cabin crew for takeoff: pulse CALLS ALL (the verified
            // A380X_MSFSBA_SIGNAL_CABIN_READY branch presses + releases it → cabin chime).
            Done(SW("LU_CABIN", "Advise the cabin crew for takeoff (call all)",
                    "A380X_MSFSBA_SIGNAL_CABIN_READY", 1), "LU_CABIN"),
        }
    };

    // -----------------------------------------------------------------------
    // 7. After Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Spoilers disarm, autobrake disarm, nose light to taxi.",
        RelatedChecklistGroupIds = new string[] { },
        Steps = new()
        {
            Skip(SW("AT_SPOILERS_DISARM", "Spoilers: DISARM", "A380X_MSFSBA_SPOILERS_ARM", 0),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 0)),
            // Autobrake disarm — consolidated here from the former Climb phase (2026-07-12).
            Skip(SW("AT_AUTOBRAKE", "Autobrake: disarm", "A32NX_AUTOBRAKES_SELECTED_MODE", 0),
                s => s.IsPosition("A32NX_AUTOBRAKES_SELECTED_MODE", 0)),
            Skip(SW("AT_NOSE_TAXI", "Nose light: TAXI", "NOSE_LIGHT", 1),
                s => s.IsPosition("NOSE_LIGHT", 1)),
            Skip(SW("AT_TURNOFF_OFF", "Runway turn-off lights: OFF", "LIGHT_RWY_TURNOFF", 0),
                s => s.IsPosition("LIGHT_RWY_TURNOFF", 0)),
        }
    };

    // -----------------------------------------------------------------------
    // 8. Approach
    // -----------------------------------------------------------------------
    private static Flow BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "Autobrake for landing, seatbelt signs, EFIS ILS mode.",
        RelatedChecklistGroupIds = new[] { "APPROACH_CL" },
        Steps = new()
        {
            Captain("AP_AUTOBRAKE", "Set the landing autobrake — Instrument section, Autobrake panel"),
            Skip(SW("AP_SEATBELTS", "Seatbelt signs: ON", "SEATBELT_SIGN", 0), s => s.IsOn("SEATBELT_SIGN_LIGHT")),
            Multi("AP_EFISMODE", "EFIS mode: ILS", ("A32NX_EFIS_L_ND_MODE", 0), ("A32NX_EFIS_R_ND_MODE", 0)),
        }
    };

    // -----------------------------------------------------------------------
    // 10. Landing
    // -----------------------------------------------------------------------
    private static Flow BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Description = "Missed approach altitude reminder, spoilers armed.",
        RelatedChecklistGroupIds = new[] { "LANDING_CL" },
        Steps = new()
        {
            Captain("LD_MISSEDALT", "Set missed approach altitude"),
            Skip(SW("LD_SPOILERS_ARM", "Spoilers: ARMED", "A380X_MSFSBA_SPOILERS_ARM", 1),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 11. After Landing
    // -----------------------------------------------------------------------
    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Clean up after vacating: radar, engine mode, flaps, APU, anti-ice, spoilers, lights.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING_CL" },
        Steps = new()
        {
            Skip(SW("AL_WXR_OFF", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys", 0),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 0)),
            Skip(SW("AL_PWS_OFF", "Predictive windshear: OFF", "A32NX_SWITCH_RADAR_PWS_Position", 0),
                s => s.IsPosition("A32NX_SWITCH_RADAR_PWS_Position", 0)),
            Skip(SW("AL_ENGMODE", "Engine mode: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)),
            Captain("AL_TCAS", "TCAS mode: standby"),
            Captain("AL_FLAPS_UP", "Flaps: up"),
            // APU for the gate — same master → START → AVAIL sequence as Before Start,
            // but Skip policy on the wait: engines are running (no power-loss hazard) and
            // an abort would kill the unrelated cleanup steps below. Whole block no-ops
            // when the APU is already available (Fenix After Landing parity).
            Skip(SW("AL_APU", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(Wait("AL_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("AL_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            WaitForField("AL_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180),
            Skip(SW("AL_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
            Multi("AL_ENGAI_OFF", "Engine anti-ice: OFF",
                ("ENG1_ANTI_ICE", 0), ("ENG2_ANTI_ICE", 0), ("ENG3_ANTI_ICE", 0), ("ENG4_ANTI_ICE", 0)),
            Skip(SW("AL_WINGAI_OFF", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD", 0),
                s => s.IsPosition("WING_ANTI_ICE_OVHD", 0)),
            Skip(SW("AL_SPOILERS_OFF", "Spoilers: OFF", "A380X_MSFSBA_SPOILERS_ARM", 0),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 0)),
            Skip(SW("AL_LANDING_OFF", "Landing lights: OFF", "LIGHT_LANDING", 0),
                s => s.IsPosition("LIGHT_LANDING", 0)),
            Skip(SW("AL_STROBE_OFF", "Strobe lights: OFF", "LIGHT_STROBE", 0),
                s => s.IsPosition("LIGHT_STROBE", 0)),
            Skip(SW("AL_NOSE_TAXI", "Nose light: TAXI", "NOSE_LIGHT", 1),
                s => s.IsPosition("NOSE_LIGHT", 1)),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Parking
    // -----------------------------------------------------------------------
    private static Flow BuildParking() => new()
    {
        Id = "PARKING", Name = "Parking",
        Description = "Parking brake, APU generators, engine shutdown, lights, fuel pumps, signs, cockpit door.",
        RelatedChecklistGroupIds = new[] { "PARKING_CL" },
        Steps = new()
        {
            Skip(SW("PK_PARKBRK", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")),
            Multi("PK_APU_GEN", "APU generators: ON", ("ELEC_APU_GEN:1", 1), ("ELEC_APU_GEN:2", 1)),
            Multi("PK_MASTERS_OFF", "Engine masters 1 to 4: OFF",
                ("ENG_VALVE_SWITCH:1", 0), ("ENG_VALVE_SWITCH:2", 0),
                ("ENG_VALVE_SWITCH:3", 0), ("ENG_VALVE_SWITCH:4", 0)),
            WaitForField("PK_WAIT_ENG", "Waiting for engines off", "FO_ENGINES_OFF", v => v > 0.5, 120),
            Wait("PK_STBY1", "Standby", 5),
            Multi("PK_COCKPITLT", "Cockpit lights: set",
                ("A380X_OVHD_ANN_LT_POSITION", 1), ("A32NX_OVHD_INTLT_ANN", 1)),
            Skip(SW("PK_BEACON_OFF", "Beacon lights: OFF", "LIGHT_BEACON", 0), s => s.IsPosition("LIGHT_BEACON", 0)),
            Skip(SW("PK_WINGLT_OFF", "Wing lights: OFF", "LIGHT_WING", 0), s => s.IsPosition("LIGHT_WING", 0)),
            Skip(SW("PK_NOSE_OFF", "Nose light: OFF", "NOSE_LIGHT", 2), s => s.IsPosition("NOSE_LIGHT", 2)),
            Multi("PK_ENGAI_OFF", "Engine anti-ice: OFF",
                ("ENG1_ANTI_ICE", 0), ("ENG2_ANTI_ICE", 0), ("ENG3_ANTI_ICE", 0), ("ENG4_ANTI_ICE", 0)),
            Skip(SW("PK_WINGAI_OFF", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD", 0),
                s => s.IsPosition("WING_ANTI_ICE_OVHD", 0)),
            Skip(SW("PK_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")),
            // ALL 20 pumps OFF at parking — mirrors POWERUP_CONFIG_OFF (feed + transfer).
            Multi("PK_FUELPUMPS_OFF", "Fuel pumps: OFF",
                ("FUELPUMP_FEEDTK1_MAIN", 0), ("FUELPUMP_FEEDTK1_STBY", 0),
                ("FUELPUMP_FEEDTK2_MAIN", 0), ("FUELPUMP_FEEDTK2_STBY", 0),
                ("FUELPUMP_FEEDTK3_MAIN", 0), ("FUELPUMP_FEEDTK3_STBY", 0),
                ("FUELPUMP_FEEDTK4_MAIN", 0), ("FUELPUMP_FEEDTK4_STBY", 0),
                ("FUELPUMP_OUTR_L", 0), ("FUELPUMP_MID_L_FWD", 0), ("FUELPUMP_MID_L_AFT", 0),
                ("FUELPUMP_INR_L_FWD", 0), ("FUELPUMP_INR_L_AFT", 0),
                ("FUELPUMP_OUTR_R", 0), ("FUELPUMP_MID_R_FWD", 0), ("FUELPUMP_MID_R_AFT", 0),
                ("FUELPUMP_INR_R_FWD", 0), ("FUELPUMP_INR_R_AFT", 0),
                ("FUELPUMP_TRIM_L", 0), ("FUELPUMP_TRIM_R", 0)),
            Skip(SW("PK_SEATBELTS_OFF", "Seatbelt signs: OFF", "SEATBELT_SIGN", 2),
                s => s.IsPosition("SEATBELT_SIGN_LIGHT", 0)),
            Captain("PK_XPDR", "Transponder: standby"),
            Captain("PK_TCAS", "TCAS mode: standby"),
            Skip(SW("PK_COCKPITDOOR", "Cockpit door: UNLOCKED", "A32NX_COCKPIT_DOOR_LOCKED", 0),
                s => s.IsPosition("A32NX_COCKPIT_DOOR_LOCKED", 0)),
            WaitForField("PK_WAIT_SB", "Waiting for seatbelt signs off", "SEATBELT_SIGN_LIGHT", v => v < 0.5, 60),
        }
    };

    // -----------------------------------------------------------------------
    // Step builders (mirror the Fenix file's helpers, retyped to FbwA380StateEvaluator)
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

    private static Step Provider(string id, string label, string eventName, Func<FbwA380StateEvaluator, int?> provider) => new()
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

    private static Step Skip(Step step, Func<FbwA380StateEvaluator, bool> cond)
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
