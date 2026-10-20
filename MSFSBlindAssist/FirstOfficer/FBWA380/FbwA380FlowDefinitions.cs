using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

using Flow = Models.FlowDefinition<FbwA380StateEvaluator>;
using Step = Models.FlowStep<FbwA380StateEvaluator>;

/// <summary>
/// Data-driven FlyByWire A380 First-Officer flow definitions, covering the 12 automated
/// phases (Cockpit Preparation through Securing). Flow steps write A380 varKeys via
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
/// - Checklist links (FO-6, FOA-9): every write step completes its own action line (<c>Done</c>) and any
///   read-back line it sets (<c>Also</c>); read-only checks (a WaitForField, skipped on timeout, never
///   writing) complete the lines only a later condition makes true: the Cockpit Preparation seat-belt
///   wait, the After Start rudder-trim check, the Taxi memo check, the Line-up packs check, the Landing
///   memo check, the Landing gear-down check (last in its flow, FO-13) and the Parking engines-off wait.
///   The two seat-belt waits and the engines-off wait also complete the action line their write ticked,
///   which reads the same field, so a timed-out wait keeps that line out of the latch too.
///   The Securing APU flap wait (SC_APU_WAIT) is a pure pacing wait and links no line.
///   A skipped step keeps its lines out of the end-of-flow latch (FO-8), and
///   FbwA380FlowChecklistLinkTests pins that no readable or action line in a latched group lacks a step.
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
        BuildSecure(),
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
            // A cold-and-dark flow: started with an engine turning (an engines-running start, or the
            // wrong flow picked in flight), CP_MASTERS_OFF below shut all four engines down and
            // CP_GEAR lowered the gear. Stop instead (the MD-11's PU_ENGINES_STOPPED): all four read
            // Off in FlyByWire's own engine state, unread failing the guard.
            WaitForField("CP_ENGINES_STOPPED", "Engines stopped", FbwA380StateEvaluator.EnginesStoppedField,
                v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop),
            Captain("CP_WIPERS", "Wipers off"),
            Done(Skip(SW("CP_GEAR", "Gear lever: DOWN", "A32NX_GEAR_HANDLE_POSITION", 1),
                s => s.IsPosition("A32NX_GEAR_HANDLE_POSITION", 1)), "CP_GEAR"),
            Captain("CP_FLAPS", "Flaps: confirm up"),
            Done(Skip(SW("CP_SPOILERS", "Spoilers: disarmed", "A380X_MSFSBA_SPOILERS_ARM", 0),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 0)), "CP_SPOILERS"),   // write the Act key, READ the real state var
            Done(Skip(SW("CP_PARKBRK", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")), "CP_PARKBRK"),
            Captain("CP_THROTTLES", "Confirm thrust levers idle"),
            Done(Skip(SW("CP_ENGMODE", "Engine mode: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)), "CP_ENGMODE"),
            Done(Multi("CP_MASTERS_OFF", "Engine masters 1 to 4: OFF",
                ("ENG_VALVE_SWITCH:1", 0), ("ENG_VALVE_SWITCH:2", 0),
                ("ENG_VALVE_SWITCH:3", 0), ("ENG_VALVE_SWITCH:4", 0)), "CP_MASTERS_OFF"),
            Done(Skip(SW("CP_WXR", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys", 0),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 0)), "CP_WXR"),
            // All FOUR A380 batteries to AUTO (BAT 1/2/ESS/APU) — the panel exposes all
            // four; setting only 1+2 left the ESS + APU batteries off.
            Done(Multi("CP_BAT", "Batteries: ON",
                ("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 1), ("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 1),
                ("A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", 1), ("A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO", 1)), "CP_BAT"),
            Wait("CP_STBY1", "Standby", 5),
            // Cockpit lights: annunciator + integral lights BRIGHT (position 1). SOP is
            // "as required"; Bright is the deterministic ground-prep setting on this build
            // (dome/flood/pedestal knobs are not L:var-settable). ANN LT: 0=Test/1=Bright/2=Dim.
            Done(Multi("CP_COCKPITLT", "Cockpit lights: set",
                ("A32NX_OVHD_INTLT_ANN", 1)), "CP_COCKPITLT"),
            Done(Multi("CP_GPU", "Ground power: ON",
                ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 1), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 1),
                ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 1), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 1)), "CP_GPU"),
            Captain("CP_INSTRBRT", "Instrument brightness: high"),
            Done(Multi("CP_ADIRS", "ADIRS: NAV",
                ("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 1),
                ("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 1),
                ("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 1)), "CP_ADIRS"),
            Done(Skip(SW("CP_OXY", "Crew oxygen: ON", "PUSH_OVHD_OXYGEN_CREW", 0),
                s => s.IsPosition("PUSH_OVHD_OXYGEN_CREW", 0)), "CP_OXY"),
            // Held fire test (executor pseudo-key): master warning + continuous
            // repetitive chime while held; the def's release write auto-acknowledges
            // the master warning. Fenix order: oxygen → fire test.
            Done(SW("CP_FIRETEST", "Fire test — listen for the fire warning", "FIRE_TEST", 1),
                "CP_FIRETEST"),
            // Recorder ground control: the CVR/recorder ground-test enable. A plain writable
            // bool (calc-path via the executor's SetLVar fallback). It is a GROUND-ONLY function
            // — the FBW system inhibits/reverts it in flight (live-verified), which is fine since
            // Cockpit Prep runs on the ground. No Skip (state isn't polled; the write is idempotent).
            Done(SW("CP_GNDCTL", "Ground control: on", "A32NX_RCDR_GROUND_CONTROL_ON", 1), "CP_GNDCTL"),
            Done(Multi("CP_NAVLOGO", "Nav and logo lights: ON", ("LIGHT_NAV", 1), ("LIGHT_LOGO", 1)), "CP_NAVLOGO"),
            Done(Skip(SW("CP_SEATBELT", "Seatbelt signs: ON", "SEATBELT_SIGN", 0), s => s.IsOn("SEATBELT_SIGN_LIGHT")),
                "CP_SEATBELT"),
            Done(Skip(SW("CP_NOSMOKE", "No smoking signs: AUTO", "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", 1),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", 1)), "CP_NOSMOKE"),
            Done(Skip(SW("CP_EMEREXIT", "Emergency exit lighting: ARM", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", 1),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", 1)), "CP_EMEREXIT"),
            Captain("CP_LTSTEST", "Lights test"),
            Done(Skip(SW("CP_WINGAI", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD", 0),
                s => s.IsPosition("WING_ANTI_ICE_OVHD", 0)), "CP_WINGAI"),
            Done(Skip(SW("CP_WINGLT", "Wing lights: OFF", "LIGHT_WING", 0), s => s.IsPosition("LIGHT_WING", 0)),
                "CP_WINGLT"),
            Done(Multi("CP_PACKS", "Packs: ON",
                ("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1), ("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 1)), "CP_PACKS"),
            Done(Skip(SW("CP_XBLEED", "Crossbleed: AUTO", "A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", 1),
                s => s.IsPosition("A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", 1)), "CP_XBLEED"),
            Done(Skip(SW("CP_PACKFLOW", "Pack flow: NORMAL", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION", 2),
                s => s.IsPosition("A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION", 2)), "CP_PACKFLOW"),
            Done(Multi("CP_HOTAIR", "Hot air: ON",
                ("A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON", 1), ("A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON", 1)), "CP_HOTAIR"),
            Captain("CP_AIRTEMP", "Cabin temperature: set as required"),
            // EFIS-CP hPa/inHg selector since FBW #10855: 1 = inHg, so hectopascals is 0.
            Done(Multi("CP_BARO", "Baro reference: hectopascals",
                ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0), ("A32NX_FCU_EFIS_R_BARO_IS_INHG", 0)), "CP_BARO"),
            Captain("CP_ALTIMETERS", "Altimeters: set QNH"),
            Done(Skip(SW("CP_ANTISKID", "Anti-skid: ON", "ANTISKID_BRAKES_ACTIVE", 1), s => s.IsOn("ANTISKID_BRAKES_ACTIVE")),
                "CP_ANTISKID"),
            // Surveillance reminders (owner's decision, 2026-10-09): Captain items set on the MFD SURV CONTROLS
            // page, for which MSFSBA has no variable or event write, so the First Officer reminds and never
            // writes or checks them. The evidence, and why "TCAS traffic: all" went: FOA-9
            // (docs/invariants/first-officer-airbus.md#foa-9).
            Captain("CP_ALTRPT", "Altitude reporting: on (MFD SURV page)"),
            Captain("CP_TCASMODE", "TCAS mode: standby (MFD SURV page)"),
            Captain("CP_XPDR", "Transponder: standby (MFD SURV page)"),
            Done(Multi("CP_EFISMODE", "EFIS mode: ARC", ("A32NX_EFIS_L_ND_MODE", 3), ("A32NX_EFIS_R_ND_MODE", 3)),
                "CP_EFISMODE"),
            Done(Multi("CP_EFISRANGE", "EFIS range: 40", ("A32NX_EFIS_L_ND_RANGE", 3), ("A32NX_EFIS_R_ND_RANGE", 3)),
                "CP_EFISRANGE"),
            // One FCU pushbutton since FBW #10855 — see A380FlightDirector. A toggle, so skip it when lit.
            Done(Skip(SW("CP_FD", "Flight directors: ON", A380FlightDirector.StateKey, 1),
                s => s.IsOn(A380FlightDirector.StateKey)), "CP_FD"),
            Captain("CP_CLOCK", "Clock: reset"),
            // ECAM SD page selection — real ECP write (A32NX_ECAM_SD_CURRENT_PAGE_INDEX:
            // door=5, APU=1, engine=0, F/Ctl=11). Selecting a manual page overrides the
            // Airbus auto-page until the next auto event; acceptable during ground prep.
            Done(SW("CP_ECAMPAGE", "ECAM page: door", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 5), "CP_ECAMPAGE"),
            // The Cockpit Preparation Checklist's "Seat belts: ON" reads the seat-belt SIGN (FOA-2). This wait
            // already reads that field, later in the flow than the CP_SEATBELT write, so it completes the line
            // (FO-6). It also completes the write's own "Seatbelt signs: ON", which reads the same sign: a sign
            // that never lights times the wait out and leaves both lines open, the write's tick included (FO-8).
            Also(Done(WaitForField("CP_WAIT_SB", "Waiting for seatbelt signs on", "SEATBELT_SIGN_LIGHT",
                    v => v > 0.5, 60), "CPC_SEATBELT"), "CP_SEATBELT"),
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
            Done(Skip(SW("BS_COCKPITDOOR", "Cockpit door: LOCKED", "A32NX_COCKPIT_DOOR_LOCKED", 1),
                s => s.IsOn("A32NX_COCKPIT_DOOR_LOCKED")), "BS_COCKPITDOOR"),
            Done(SW("BS_FCUSPD", "FCU speed: managed", "FCU_PUSH_SPEED", 1), "BS_FCUSPD"),
            Done(SW("BS_FCUHDG", "FCU heading: managed", "FCU_PUSH_HEADING", 1), "BS_FCUHDG"),
            Captain("BS_ALT", "Set cleared altitude on the FCU"),
            Done(SW("BS_FCUALT", "FCU altitude: pushed", "FCU_PUSH_ALT", 1), "BS_FCUALT"),
            Done(SW("BS_ECAMPAGE", "ECAM page: APU", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 1), "BS_ECAMPAGE"),
            // APU block: master on, dwell, START pushbutton, wait for AVAIL. The master
            // switch alone does NOT start the FBW APU — the START PB press is required —
            // and external power must stay on the buses until the APU is actually
            // available. Stop policy on the AVAIL wait: a start failure aborts the flow
            // BEFORE the external-power disconnect below. Steps skip once AVAIL is lit.
            // "APU: ON" reads the master switch on the A380 (FO-11), so the master write completes it.
            Done(Skip(SW("BS_APU", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON")), "BS_APU"),
            Skip(Wait("BS_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("BS_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            WaitForField("BS_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop),
            // Defensive: release the latched START PB (no repo evidence FBW auto-clears
            // it, and a stale latched 1 could surprise-start the APU on a later
            // master-ON). Skipped when FBW already cleared it; IsPosition(..., 0), never !IsOn(...):
            // an unread switch must not skip an absolute OFF write.
            Skip(SW("BS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_START_PB_IS_ON", 0)),
            Done(Skip(SW("BS_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")), "BS_APUBLEED"),
            Done(Multi("BS_GPU_OFF", "Ground power: OFF",
                ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0),
                ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0)), "BS_GPU_OFF"),
            Captain("BS_GPU_DISC", "Disconnect ground power on the EFB"),
            // ALL 20 pumps ON — 8 feed + 12 transfer (outer/mid/inner/trim). The transfer
            // pumps are manual on/off pumps like the feed pumps (same electrical-circuit
            // toggle), NOT auto-starting: FBW's own POWERUP_CONFIG preset turns all 20 on and
            // real A380 SOP runs the transfer pumps continuously (the FQMS schedules WHICH tank
            // transfers into the feed tanks, but the pumps stay on). Feeding only the 8 feed
            // pumps left the transfer pumps off — an incomplete config vs FBW's own preset.
            Done(Multi("BS_FUELPUMPS", "Fuel pumps: ON", FuelPumpWrites(1)), "BS_FUELPUMPS"),
            // Completes the Before Start Checklist's "Beacon: ON" too (FO-6): the flow latches both
            // groups, so a failed write must keep BSC_BEACON out of the latch as well.
            Also(Done(Skip(SW("BS_BEACON", "Beacon lights: ON", "LIGHT_BEACON", 1), s => s.IsOn("LIGHT_BEACON")),
                "BS_BEACON"), "BSC_BEACON"),
            Captain("BS_THROTTLES", "Confirm thrust levers idle"),
            Done(Skip(SW("BS_PARKBRK", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")), "BS_PARKBRK"),
            Captain("BS_XPDR", "Transponder: set for departure"),
            Captain("BS_TCAS", "TCAS mode: TA/RA (MFD SURV page)"),
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
            Done(SW("ES_ECAMPAGE", "ECAM page: engine", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 0), "ES_ECAMPAGE"),
            Done(Skip(SW("ES_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")), "ES_APUBLEED"),
            Done(Skip(SW("ES_ENGMODE", "Engine mode: IGN", "ENGINE_MODE_SELECTOR", 2),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 2)), "ES_ENGMODE"),
            Done(Skip(SW("ES_ENG1", "Engine 1 master: START", "ENG_VALVE_SWITCH:1", 1),
                s => s.IsOn("ENG_VALVE_SWITCH:1")), "ES_ENG1"),
            Done(Skip(SW("ES_ENG2", "Engine 2 master: START", "ENG_VALVE_SWITCH:2", 1),
                s => s.IsOn("ENG_VALVE_SWITCH:2")), "ES_ENG2"),
            Wait("ES_STBY1", "Standby", 60),
            Done(Skip(SW("ES_ENG3", "Engine 3 master: START", "ENG_VALVE_SWITCH:3", 1),
                s => s.IsOn("ENG_VALVE_SWITCH:3")), "ES_ENG3"),
            Done(Skip(SW("ES_ENG4", "Engine 4 master: START", "ENG_VALVE_SWITCH:4", 1),
                s => s.IsOn("ENG_VALVE_SWITCH:4")), "ES_ENG4"),
            Wait("ES_STBY2", "Standby", 60),
        }
    };

    // -----------------------------------------------------------------------
    // 4. After Start
    // -----------------------------------------------------------------------
    private static Flow BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Mode NORM, APU off, spoilers armed, taxi light, rudder trim reset and neutral check.",
        RelatedChecklistGroupIds = new[] { "AFTER_START_CL" },
        Steps = new()
        {
            Done(Skip(SW("AS_ENGMODE", "Engine mode: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)), "AS_ENGMODE"),
            Captain("AS_WINGAI", "Wing anti-ice: set as required"),
            Captain("AS_ENGAI", "Engine anti-ice: set as required"),
            Done(SW("AS_ECAMPAGE", "ECAM page: APU", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 1), "AS_ECAMPAGE"),
            // Release a still-latched START PB first — master-off must never coexist with
            // START=1 (a latched 1 would surprise-start the APU on the next master-ON).
            Skip(SW("AS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_START_PB_IS_ON", 0)),
            Done(Skip(SW("AS_APU_OFF", "APU: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)), "AS_APU_OFF"),
            Done(Skip(SW("AS_APUBLEED_OFF", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)), "AS_APUBLEED_OFF"),
            Done(Skip(SW("AS_NOSE_TAXI", "Nose light: TAXI", "NOSE_LIGHT", 1),
                s => s.IsPosition("NOSE_LIGHT", 1)), "AS_NOSE_TAXI"),
            // Cockpit lights DIM (position 2) for taxi/flight — the reduced setting
            // (ANN LT has no true OFF; 2=Dim is the in-flight position).
            Done(Multi("AS_COCKPITLT", "Cockpit lights: annunciator dim",
                ("A32NX_OVHD_INTLT_ANN", 2)), "AS_COCKPITLT"),
            // Left alone over an extended speed brake: FlyByWire's A380X SpoilersHandler handles the arm
            // (K:SPOILERS_ARM_ON) by setting the handle to 0, so arming would snap it in (FOA-11, the
            // A320 family's guard; AirbusGroundSpoilers).
            LeaveAlone(Done(Skip(SW("AS_SPOILERS_ARM", "Spoilers: ARMED", "A380X_MSFSBA_SPOILERS_ARM", 1),
                    s => s.IsPosition("A32NX_SPOILERS_ARMED", 1)), "AS_SPOILERS_ARM"),
                SpeedBrakeExtended, AirbusGroundSpoilers.LeaveAloneText),
            // The reset completes its own line only. The After Start Checklist's "Rudder trim: NEUTRAL" reads
            // the SEC trim word, which the reset only commands: the trim gets there at its own rate, and only
            // if the SEC and the actuator respond. So the read-only check right after it completes that line
            // (FO-6): 20 s (the gear check's figure; the reset's time to neutral is not measured), skipped
            // aloud on timeout, never writes. A trim that never reads neutral leaves the line open.
            Done(SW("AS_RUDDERTRIM", "Rudder trim: RESET", "A32NX_RUDDER_TRIM_RESET", 1), "AS_RUDDERTRIM"),
            Done(Skip(WaitForField("AS_RUDDERTRIM_CHECK", "Rudder trim: NEUTRAL", "FO_RUDDER_TRIM_NEUTRAL",
                    v => v > 0.5, 20),
                s => s.GetValue("FO_RUDDER_TRIM_NEUTRAL") > 0.5), "ASC_RUDDERTRIM"),
            Captain("AS_FLAPS", "Flaps: set for takeoff"),
        }
    };

    // -----------------------------------------------------------------------
    // 5. Taxi
    // -----------------------------------------------------------------------
    private static Flow BuildTaxi() => new()
    {
        Id = "TAXI", Name = "Taxi",
        Description = "Autobrake RTO, engine mode NORM, weather radar and predictive windshear on, takeoff memo check, takeoff config test.",
        RelatedChecklistGroupIds = new[] { "TAXI_CL" },
        Steps = new()
        {
            // RTO arm is a MOMENTARY press (the L:var auto-resets to 0 ~1.5 s later);
            // the latched armed state is the separate A32NX_AUTOBRAKES_RTO_ARMED
            // (Continuous+IsAnnounced, batch-cached). Skip on the ARMED state so a
            // re-run never re-presses the momentary while already armed (a second
            // press would disarm it). It also completes the takeoff memo's "Autobrake: RTO" (FO-6).
            Also(Done(Skip(SW("TX_AUTOBRAKE", "Autobrake: RTO", "A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED", 1),
                s => s.IsOn("A32NX_AUTOBRAKES_RTO_ARMED")), "TX_AUTOBRAKE"), "TXC_MEMO_AUTOBRK"),
            Done(Skip(SW("TX_ENGMODE", "Engine mode: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)), "TX_ENGMODE"),
            // Completes the Taxi Checklist's "Radar: ON" too: on the A380 that line is the radar alone.
            Also(Done(Skip(SW("TX_WXR", "Weather radar: ON", "XMLVAR_A320_WeatherRadar_Sys", 1),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 1)), "TX_WXR"), "TXC_WXR"),
            Done(Skip(SW("TX_PWS", "Predictive windshear: ON", "A32NX_SWITCH_RADAR_PWS_Position", 1),
                s => s.IsPosition("A32NX_SWITCH_RADAR_PWS_Position", 1)), "TX_PWS"),
            Captain("TX_FLAPS", "Flaps: set for takeoff"),
            Done(SW("TX_ECAMPAGE", "ECAM page: flight controls", "A32NX_ECAM_SD_CURRENT_PAGE_INDEX", 11), "TX_ECAMPAGE"),
            // The takeoff memo the pilot set earlier (seat belts, ground spoilers, flaps): a read-only check
            // (5 s, skipped aloud on timeout, never writes) on the same fields and conditions as the four
            // Taxi Checklist lines it completes (FOA-9). Flaps held back to the holding point (icing) time it
            // out: "Flaps: T.O" then stays open until the lever is set and "Flaps setting: SET" until the pilot
            // ticks it; lines that already read right (the window ticks them every second) tick with the rest.
            Also(Done(Skip(WaitForField("TX_MEMO_CHECK", "Seat belts, ground spoilers and flaps for takeoff",
                        "FO_TAXI_MEMO_OK", v => v > 0.5, 5),
                    s => s.GetValue("FO_TAXI_MEMO_OK") > 0.5), "TXC_MEMO_SIGNS"),
                "TXC_MEMO_SPLRS", "TXC_MEMO_FLAPS", "TXC_FLAPS"),
            // Takeoff config test: held ECP T.O CONFIG button via the executor's
            // TakeoffConfigTestAsync pseudo-key (press → 2 s → release; sim-verified the
            // A32NX_BTN_TOCONFIG L:var holds). Done after flaps/config so the FWC verifies
            // a complete takeoff configuration ("T.O CONFIG NORMAL" on the ECAM). It also completes
            // the takeoff memo's "T.O config: TEST" (FO-6).
            Also(Done(SW("TX_CONFIG", "Takeoff config test", "TO_CONFIG_TEST", 1), "TX_CONFIG"), "TXC_MEMO_TOCFG"),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Lineup
    // -----------------------------------------------------------------------
    private static Flow BuildLineup() => new()
    {
        Id = "LINEUP", Name = "Lineup",
        Description = "TCAS TA/RA reminder, packs check, strobe, landing/nose and turn-off lights for takeoff, cabin call.",
        RelatedChecklistGroupIds = new[] { "LINEUP_CL" },
        Steps = new()
        {
            Captain("LU_TCAS", "TCAS mode: TA/RA (MFD SURV page)"),
            // Packs 1 and 2 ON, read only (5 s, skipped aloud on timeout, never writes): Cockpit Preparation's
            // CP_PACKS switched them on, and this flow never switches them; a packs-off takeoff stays the
            // pilot's call, and the Line-up Checklist's "Packs 1 and 2: ON" then stays open (FOA-9).
            Done(Skip(WaitForField("LU_PACKS_CHECK", "Packs 1 and 2: ON", "FO_PACKS_ON", v => v > 0.5, 5),
                s => s.GetValue("FO_PACKS_ON") > 0.5), "LUC_PACKS"),
            Done(Skip(SW("LU_STROBE", "Strobe lights: ON", "LIGHT_STROBE", 1), s => s.IsOn("LIGHT_STROBE")), "LU_STROBE"),
            // Landing lights = wing LDG LT (LIGHT_LANDING 0/1); the nose T.O. beam is the
            // separate 3-position NOSE_LIGHT selector (0=T.O.) since the PR #139 split.
            Done(Skip(Multi("LU_LANDING", "Landing and nose lights: ON",
                    ("LIGHT_LANDING", 1), ("NOSE_LIGHT", 0)),
                s => s.IsOn("LIGHT_LANDING") && s.IsPosition("NOSE_LIGHT", 0)), "LU_LANDING"),
            // Runway turn-off lights ON (drives LIGHT TAXI:2 + :3 via the indexed
            // TAXI_LIGHTS_SET — live-verified 2026-07-13). Turned OFF in After Takeoff.
            Done(Skip(SW("LU_TURNOFF", "Runway turn-off lights: ON", "LIGHT_RWY_TURNOFF", 1),
                s => s.IsOn("LIGHT_RWY_TURNOFF")), "LU_TURNOFF"),
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
            Done(Skip(SW("AT_SPOILERS_DISARM", "Spoilers: DISARM", "A380X_MSFSBA_SPOILERS_ARM", 0),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 0)), "AT_SPOILERS_DISARM"),
            // Autobrake disarm — consolidated here from the former Climb phase (2026-07-12).
            Done(Skip(SW("AT_AUTOBRAKE", "Autobrake: disarm", "A32NX_AUTOBRAKES_SELECTED_MODE", 0),
                s => s.IsPosition("A32NX_AUTOBRAKES_SELECTED_MODE", 0)), "AT_AUTOBRAKE"),
            Done(Skip(SW("AT_NOSE_TAXI", "Nose light: TAXI", "NOSE_LIGHT", 1),
                s => s.IsPosition("NOSE_LIGHT", 1)), "AT_NOSE_TAXI"),
            Done(Skip(SW("AT_TURNOFF_OFF", "Runway turn-off lights: OFF", "LIGHT_RWY_TURNOFF", 0),
                s => s.IsPosition("LIGHT_RWY_TURNOFF", 0)), "AT_TURNOFF_OFF"),
        }
    };

    // -----------------------------------------------------------------------
    // 8. Approach
    // -----------------------------------------------------------------------
    private static Flow BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "Autobrake for landing, seatbelt signs, EFIS ILS mode, cabin call.",
        RelatedChecklistGroupIds = new[] { "APPROACH_CL" },
        Steps = new()
        {
            Captain("AP_AUTOBRAKE", "Set the landing autobrake — Instrument section, Autobrake panel"),
            // Completes the Approach Checklist's "Seat belts: ON" too (FO-6).
            Also(Done(Skip(SW("AP_SEATBELTS", "Seatbelt signs: ON", "SEATBELT_SIGN", 0), s => s.IsOn("SEATBELT_SIGN_LIGHT")),
                "AP_SEATBELTS"), "APC_SEATBELT"),
            Done(Multi("AP_EFISMODE", "EFIS mode: ILS", ("A32NX_EFIS_L_ND_MODE", 0), ("A32NX_EFIS_R_ND_MODE", 0)),
                "AP_EFISMODE"),
            // Notify the cabin crew for landing: pulse CALLS ALL through LU_CABIN's verified
            // A380X_MSFSBA_SIGNAL_CABIN_READY branch. The Approach group carried this line from
            // 2026-07-12 with no step behind it, so a finished flow ticked a call nobody made (FO-7).
            Done(SW("AP_CABIN", "Notify the cabin crew for landing (call all)",
                    "A380X_MSFSBA_SIGNAL_CABIN_READY", 1), "AP_CABIN"),
        }
    };

    // -----------------------------------------------------------------------
    // 10. Landing
    // -----------------------------------------------------------------------
    private static Flow BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Description = "Missed approach altitude reminder, spoilers armed, landing memo check, gear-down check.",
        RelatedChecklistGroupIds = new[] { "LANDING_CL" },
        Steps = new()
        {
            Captain("LD_MISSEDALT", "Set missed approach altitude"),
            // Completes the landing memo's "Ground spoilers: ARM" too (FO-6). Left alone over an extended
            // speed brake, which the arm would snap back to retracted on final (FOA-11): both lines then
            // stay open until the pilot arms.
            LeaveAlone(Also(Done(Skip(SW("LD_SPOILERS_ARM", "Spoilers: ARMED", "A380X_MSFSBA_SPOILERS_ARM", 1),
                    s => s.IsPosition("A32NX_SPOILERS_ARMED", 1)), "LD_SPOILERS_ARM"), "LDC_SPOILERS"),
                SpeedBrakeExtended, AirbusGroundSpoilers.LeaveAloneText),
            // The landing memo the pilot set (seat belts, flaps at the landing setting: FULL, or 3 with CONF 3
            // selected): a read-only check (5 s, skipped aloud on timeout, never writes) completing the Landing
            // Checklist's two lines on the same fields (FOA-9). Flaps short of the landing setting time it out:
            // "Flaps: LDG" then stays open until the flaps are set; a line that already reads right (the window
            // ticks it every second) ticks with the rest.
            Also(Done(Skip(WaitForField("LD_MEMO_CHECK", "Seat belts and landing flaps", "FO_LDG_MEMO_OK",
                        v => v > 0.5, 5),
                    s => s.GetValue("FO_LDG_MEMO_OK") > 0.5), "LDC_SEATBELT"),
                "LDC_FLAPS"),
            // LAST (FO-13): the gear confirmed down from the legs, read only (20 s, skipped aloud on
            // timeout, never writes). It completes the Landing Checklist's "Landing gear: DOWN".
            Done(Skip(WaitForField("LD_GEAR_DOWN_CHECK", "Landing gear: DOWN", FbwA380GearConfirmation.DownField,
                    v => v > 0.5, 20),
                s => s.GetValue(FbwA380GearConfirmation.DownField) > 0.5), "LDC_GEAR"),
        }
    };

    // -----------------------------------------------------------------------
    // 11. After Landing
    // -----------------------------------------------------------------------
    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Clean up after vacating: radar, engine mode, flaps, APU, anti-ice, spoilers, lights.",
        // No read-back of its own: the A380 ECL has no After Landing checklist (FOA-9).
        RelatedChecklistGroupIds = new string[] { },
        Steps = new()
        {
            Done(Skip(SW("AL_WXR_OFF", "Weather radar: OFF", "XMLVAR_A320_WeatherRadar_Sys", 0),
                s => s.IsPosition("XMLVAR_A320_WeatherRadar_Sys", 0)), "AL_WXR_OFF"),
            Done(Skip(SW("AL_PWS_OFF", "Predictive windshear: OFF", "A32NX_SWITCH_RADAR_PWS_Position", 0),
                s => s.IsPosition("A32NX_SWITCH_RADAR_PWS_Position", 0)), "AL_PWS_OFF"),
            Done(Skip(SW("AL_ENGMODE", "Engine mode: NORM", "ENGINE_MODE_SELECTOR", 1),
                s => s.IsPosition("ENGINE_MODE_SELECTOR", 1)), "AL_ENGMODE"),
            Captain("AL_FLAPS_UP", "Flaps: up"),
            // APU for the gate — same master → START → AVAIL sequence as Before Start,
            // but Skip policy on the wait: engines are running (no power-loss hazard) and
            // an abort would kill the unrelated cleanup steps below. Whole block no-ops
            // when the APU is already available (Fenix After Landing parity).
            // "APU: ON" reads the master switch on the A380 (FO-11), so the master write completes it.
            Done(Skip(SW("AL_APU", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")), "AL_APU"),
            Skip(Wait("AL_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("AL_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            WaitForField("AL_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180),
            Skip(SW("AL_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_START_PB_IS_ON", 0)),
            Done(Multi("AL_ENGAI_OFF", "Engine anti-ice: OFF",
                ("ENG1_ANTI_ICE", 0), ("ENG2_ANTI_ICE", 0), ("ENG3_ANTI_ICE", 0), ("ENG4_ANTI_ICE", 0)), "AL_ENGAI_OFF"),
            Done(Skip(SW("AL_WINGAI_OFF", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD", 0),
                s => s.IsPosition("WING_ANTI_ICE_OVHD", 0)), "AL_WINGAI_OFF"),
            Done(Skip(SW("AL_SPOILERS_OFF", "Spoilers: OFF", "A380X_MSFSBA_SPOILERS_ARM", 0),
                s => s.IsPosition("A32NX_SPOILERS_ARMED", 0)), "AL_SPOILERS_OFF"),
            Done(Skip(SW("AL_LANDING_OFF", "Landing lights: OFF", "LIGHT_LANDING", 0),
                s => s.IsPosition("LIGHT_LANDING", 0)), "AL_LANDING_OFF"),
            Done(Skip(SW("AL_STROBE_OFF", "Strobe lights: OFF", "LIGHT_STROBE", 0),
                s => s.IsPosition("LIGHT_STROBE", 0)), "AL_STROBE_OFF"),
            Done(Skip(SW("AL_NOSE_TAXI", "Nose light: TAXI", "NOSE_LIGHT", 1),
                s => s.IsPosition("NOSE_LIGHT", 1)), "AL_NOSE_TAXI"),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Parking
    // -----------------------------------------------------------------------
    private static Flow BuildParking() => new()
    {
        Id = "PARKING", Name = "Parking",
        Description = "Parking brake, APU generators, engine shutdown, lights, fuel pumps, signs, cockpit door. Leaves the aircraft powered — Securing shuts it down.",
        RelatedChecklistGroupIds = new[] { "PARKING_CL" },
        Steps = new()
        {
            // Started by mistake in flight, the masters below would shut all four engines down and
            // the fuel pumps go off: never airborne (the MD-11's SD_ON_GROUND).
            OnGroundGuard("PK_ON_GROUND"),
            Done(Skip(SW("PK_PARKBRK", "Parking brake: ON", "A32NX_PARK_BRAKE_LEVER_POS", 1),
                s => s.IsOn("A32NX_PARK_BRAKE_LEVER_POS")), "PK_PARKBRK"),
            Done(Multi("PK_APU_GEN", "APU generators: ON", ("ELEC_APU_GEN:1", 1), ("ELEC_APU_GEN:2", 1)), "PK_APU_GEN"),
            // The masters take the engine generators off the buses, so the APU or external power
            // must be able to carry them first: After Landing's APU wait only skips on a timeout,
            // and without this the masters went off onto the batteries. Up to two minutes for an
            // APU still starting, then the flow stops with the engines running (the MD-11's
            // PK_POWER_SOURCE). Passes at once with all four masters off.
            Skip(WaitForField("PK_POWER_SOURCE", "APU or external power available",
                    FbwA380StateEvaluator.ApuOrExtPowerField, v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop),
                s => s.IsOn("FO_ENGINES_OFF")),
            Done(Multi("PK_MASTERS_OFF", "Engine masters 1 to 4: OFF",
                ("ENG_VALVE_SWITCH:1", 0), ("ENG_VALVE_SWITCH:2", 0),
                ("ENG_VALVE_SWITCH:3", 0), ("ENG_VALVE_SWITCH:4", 0)), "PK_MASTERS_OFF"),
            // Read only: completes the Parking Checklist's "Engines: OFF", on the same field (FO-6), and the
            // masters' own line above, which reads the same four switches: a master that never reads off times
            // the wait out and leaves both lines open, the write's tick included (FO-8).
            Also(Done(WaitForField("PK_WAIT_ENG", "Waiting for engines off", "FO_ENGINES_OFF", v => v > 0.5, 120),
                "PKC_ENGINES"), "PK_MASTERS_OFF"),
            Wait("PK_STBY1", "Standby", 5),
            Done(Multi("PK_COCKPITLT", "Cockpit lights: set",
                ("A32NX_OVHD_INTLT_ANN", 1)), "PK_COCKPITLT"),
            Done(Skip(SW("PK_BEACON_OFF", "Beacon lights: OFF", "LIGHT_BEACON", 0), s => s.IsPosition("LIGHT_BEACON", 0)),
                "PK_BEACON_OFF"),
            // Completes the Parking Checklist's "Wing lights: OFF" too (FO-6).
            Also(Done(Skip(SW("PK_WINGLT_OFF", "Wing lights: OFF", "LIGHT_WING", 0), s => s.IsPosition("LIGHT_WING", 0)),
                "PK_WINGLT_OFF"), "PKC_WINGLT"),
            Done(Skip(SW("PK_NOSE_OFF", "Nose light: OFF", "NOSE_LIGHT", 2), s => s.IsPosition("NOSE_LIGHT", 2)),
                "PK_NOSE_OFF"),
            Done(Multi("PK_ENGAI_OFF", "Engine anti-ice: OFF",
                ("ENG1_ANTI_ICE", 0), ("ENG2_ANTI_ICE", 0), ("ENG3_ANTI_ICE", 0), ("ENG4_ANTI_ICE", 0)), "PK_ENGAI_OFF"),
            Done(Skip(SW("PK_WINGAI_OFF", "Wing anti-ice: OFF", "WING_ANTI_ICE_OVHD", 0),
                s => s.IsPosition("WING_ANTI_ICE_OVHD", 0)), "PK_WINGAI_OFF"),
            Done(Skip(SW("PK_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")), "PK_APUBLEED"),
            // The fuel goes off only once all four engines have spun down (FO-25): the masters above
            // leave them turning for a while. Two minutes, then the flow stops with the pumps on.
            WaitForField("PK_ENGINES_STOPPED", "Engines stopped", FbwA380StateEvaluator.EnginesStoppedField,
                v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop),
            // ALL 20 pumps OFF at parking — mirrors POWERUP_CONFIG_OFF (feed + transfer). Completes the
            // Parking Checklist's "Fuel pumps: OFF" too (FO-6), which reads the same 20.
            Also(Done(Multi("PK_FUELPUMPS_OFF", "Fuel pumps: OFF", FuelPumpWrites(0)), "PK_FUELPUMPS_OFF"),
                "PKC_FUELPUMPS"),
            Done(Skip(SW("PK_SEATBELTS_OFF", "Seatbelt signs: OFF", "SEATBELT_SIGN", 2),
                s => s.IsPosition("SEATBELT_SIGN_LIGHT", 0)), "PK_SEATBELTS_OFF"),
            Captain("PK_XPDR", "Transponder: standby (MFD SURV page)"),
            Captain("PK_TCAS", "TCAS mode: standby (MFD SURV page)"),
            Done(Skip(SW("PK_COCKPITDOOR", "Cockpit door: UNLOCKED", "A32NX_COCKPIT_DOOR_LOCKED", 0),
                s => s.IsPosition("A32NX_COCKPIT_DOOR_LOCKED", 0)), "PK_COCKPITDOOR"),
            // Read only: completes "Seatbelt signs: OFF" above, which reads this same sign: a sign that stays lit
            // times the wait out and leaves the line open, the write's tick included (FO-8).
            Done(WaitForField("PK_WAIT_SB", "Waiting for seatbelt signs off", "SEATBELT_SIGN_LIGHT", v => v < 0.5, 60),
                "PK_SEATBELTS_OFF"),
        }
    };

    // -----------------------------------------------------------------------
    // 13. Securing
    // -----------------------------------------------------------------------
    // The power-down phase. PARKING deliberately leaves the aircraft POWERED (it selects
    // the APU generators and APU bleed on for the gate); this is where the jet actually
    // goes cold and dark. The content comes from the aircraft's own shipped reference text,
    // Checklists/FBW_A380_Checklist.txt "[Securing the Aircraft]" (retired 2026-10-17 with the text
    // checklist window, FO-24; it is in git history). The ORDER of the last four
    // steps does not: that text lists external power BEFORE the APU master, while FlyByWire's A32NX
    // guide switches the APU master off, waits for the APU flap to close ("requires either APU or
    // external power"), then drops external power and the batteries. The owner chose that order
    // for every Airbus on 2026-10-14 (FOA-10).
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Securing",
        Description = "Oxygen, ADIRS, signs, exterior lights, APU bleed, APU master, wait for the APU flap, ground power, batteries.",
        RelatedChecklistGroupIds = new[] { "SECURING_CL" },
        Steps = new()
        {
            // The IRS and the batteries go off below: never airborne (the MD-11's SD_ON_GROUND).
            OnGroundGuard("SC_ON_GROUND"),
            // ...nor with an engine turning (FO-25): the whole flow stops, not just the IRS.
            WaitForField("SC_ENGINES_STOPPED", "Engines stopped", FbwA380StateEvaluator.EnginesStoppedField,
                v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop),
            // Crew oxygen is INVERTED: 0 = Auto/on, 1 = Off. Cockpit Prep's CP_OXY writes 0.
            // Completes the Securing the Aircraft Checklist's "Oxygen: OFF" too (FO-6).
            Also(Done(Skip(SW("SC_OXY", "Crew oxygen supply: OFF", "PUSH_OVHD_OXYGEN_CREW", 1),
                s => s.IsPosition("PUSH_OVHD_OXYGEN_CREW", 1)), "SC_OXY"), "SCC_OXY"),
            Captain("SC_EFB", "EFB tablets and flyPad: stowed"),
            Done(Skip(Multi("SC_ADIRS", "IRS 1, 2 and 3: OFF",
                    ("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 0),
                    ("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 0),
                    ("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 0)),
                s => s.IsPosition("A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", 0)
                  && s.IsPosition("A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB", 0)
                  && s.IsPosition("A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB", 0)), "SC_ADIRS"),
            // Both sign switches are 3-position 0=On / 1=Auto|Arm / 2=Off. Cockpit Prep
            // writes 1 (Auto / Arm); securing writes 2 (Off). The emergency-exit write also
            // completes the checklist's "Emergency exit light: OFF" (FO-6).
            Also(Done(Skip(SW("SC_EMEREXIT", "Emergency exit lighting: OFF",
                    "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", 2),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position", 2)), "SC_EMEREXIT"), "SCC_EMEREXIT"),
            Done(Skip(SW("SC_NOSMOKE", "No smoking signs: OFF",
                    "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", 2),
                s => s.IsPosition("XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", 2)), "SC_NOSMOKE"),
            // Nav + logo, and the runway turn-off lights: beacon / wing / nose went off in PARKING,
            // landing + strobe in AFTER_LANDING, but the turn-off lights Lineup switched on go off only
            // in AFTER_TAKEOFF, which a pilot can skip (or an in-flight start latches), and no arrival
            // flow touches them; so they are switched off here too, or "Exterior lights: OFF" was
            // spoken with them still on.
            Done(Skip(Multi("SC_EXTLT_OFF", "Exterior lights: OFF",
                    ("LIGHT_NAV", 0), ("LIGHT_LOGO", 0), ("LIGHT_RWY_TURNOFF", 0)),
                s => s.IsPosition("LIGHT_NAV", 0) && s.IsPosition("LIGHT_LOGO", 0)
                  && s.IsPosition("LIGHT_RWY_TURNOFF", 0)), "SC_EXTLT_OFF"),
            // IsPosition(..., 0), not !IsOn(...): both read false on an unread (NaN) variable,
            // but that means opposite things for a Skip condition on an OFF step — !IsOn(NaN)
            // is true (skips the write, leaving a switch that was never actually read as OFF
            // possibly still on), while IsPosition(NaN, 0) is false (the write still happens).
            // Their siblings SC_APU_OFF/SC_BAT_OFF already use IsPosition for this reason; the
            // two below matched them.
            Done(Skip(SW("SC_APUBLEED_OFF", "APU bleed: OFF", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 0)), "SC_APUBLEED_OFF"),
            Done(Skip(SW("SC_APU_OFF", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)), "SC_APU_OFF"),
            // Wait for the APU flap to close BEFORE external power goes off (owner, 2026-10-14; FOA-10):
            // the flap needs APU or external power to close. Read-only, so it links no line. Instant when
            // the APU never ran (flap reads 0); an unread flap simply times out and the flow carries on.
            WaitForField("SC_APU_WAIT", "APU flap to close", "A32NX_APU_FLAP_OPEN_PERCENTAGE", v => v < 0.5, 150),
            Done(Skip(Multi("SC_EXTPWR_OFF", "External power: OFF",
                    ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0),
                    ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0)),
                s => s.IsPosition("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0)
                  && s.IsPosition("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0)
                  && s.IsPosition("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0)
                  && s.IsPosition("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0)), "SC_EXTPWR_OFF"),
            // Batteries LAST — the final power source, and the step that makes the jet dark. It also
            // completes the checklist's "Batteries: OFF" (FO-6), which reads the same four.
            Also(Done(Skip(Multi("SC_BAT_OFF", "Batteries: OFF",
                    ("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 0), ("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 0),
                    ("A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", 0), ("A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO", 0)),
                s => s.IsPosition("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", 0)
                  && s.IsPosition("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", 0)
                  && s.IsPosition("A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", 0)
                  && s.IsPosition("A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO", 0)), "SC_BAT_OFF"), "SCC_BAT"),
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
        TargetValue = target,        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Provider(string id, string label, string eventName, Func<FbwA380StateEvaluator, int?> provider) => new()
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

    // Every one of the 20 fuel pumps (8 feed, 12 transfer) to one target, in the order of
    // FbwA380ChecklistDefinitions.FuelPumps: the one list the three fuel-pump checklist lines read, so
    // the Before Start and Parking pump steps cannot switch a different set from the one those lines read.
    private static (string EventName, int? TargetValue)[] FuelPumpWrites(int target) =>
        FbwA380ChecklistDefinitions.FuelPumps.Select(pump => (EventName: pump, TargetValue: (int?)target)).ToArray();

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

    // A further line the step delivers, beyond Done's: the same line in the flow's own *_CL read-back,
    // or the other lines a read-only check confirms (FlowStep.AlsoCompletesChecklistItemIds; FlowManager
    // marks and, on a skip, excludes every linked id — FO-6). The A32NX file's helper.
    private static Step Also(Step step, params string[] alsoCompletes)
    {
        step.AlsoCompletesChecklistItemIds = alsoCompletes;
        return step;
    }

    // A step the First Officer must not perform in the aircraft's current state
    // (FlowStep.LeaveAloneWhen): nothing is sent, `text` is spoken, its lines stay open.
    private static Step LeaveAlone(Step step, Func<FbwA380StateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }

    /// <summary>The speed brake reads extended (the A380 handle, A32NX_SPOILERS_HANDLE_POSITION, the
    /// A32NX's own): a spoiler arm is left alone (FOA-11).</summary>
    internal static bool SpeedBrakeExtended(FbwA380StateEvaluator s) =>
        AirbusGroundSpoilers.FbwSpeedBrakeExtended(s.GetValue("A32NX_SPOILERS_HANDLE_POSITION"));

    // Stops a flow that switches engines, IRS or batteries off unless the aircraft reads on the
    // ground (the stock SIM_ON_GROUND; unread fails the guard): the MD-11's SD_ON_GROUND.
    private static Step OnGroundGuard(string id) =>
        WaitForField(id, "On the ground", "SIM_ON_GROUND", v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop);
}
