using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.Fenix;

using Flow = Models.FlowDefinition<FenixStateEvaluator>;
using Step = Models.FlowStep<FenixStateEvaluator>;

/// <summary>
/// Data-driven Fenix A320 First-Officer flow definitions — Airbus SOPs reconciled from
/// JD's Guide v1.31.1, the FSFO FENA320 flow data, and the user's own Fenix checklist.
/// Flow steps reference Fenix L:var names directly (the executor's default dispatch is a
/// held L:var write; momentary buttons are in its pulse table; FIRE_TEST_* and
/// FCU_PUSH_*_MANAGED are pseudo-keys intercepted by FenixActionExecutor).
///
/// Fenix value conventions (from FenixA320Definition, panel-proven):
/// - S_ENG_MODE 0=Crank,1=Norm,2=Ign/Start; masters 0=Off,1=On.
/// - IRS mode 0=Off,1=Nav,2=Att. Landing lights 0=Retract,1=Off,2=On; nose 0/1=Taxi/2=TO;
///   strobe 0/1=Auto/2=On. Signs: seatbelts 0/1; smoking 0/1=Auto/2; emer exit 0/1=Arm/2.
/// - A_FC_SPEEDBRAKE 0=Armed,1=Disarmed,2=Half,3=Full. Flaps L:var S_FC_FLAPS 0..4.
/// - XPDR mode 0=STBY,1=TA,2=TA/RA; operation 0=STBY,1=AUTO,2=ON; TCAS range 1=ALL.
/// - X-bleed 1=Auto; pack flow 1=Normal; press mode 1=Auto.
/// </summary>
public static class FenixFlowDefinitions
{
    /// <summary>N2 percent at/above which an engine counts as started (CFM56 idle ~58-60).</summary>
    private const double EngRunningN2 = FenixStateEvaluator.EngineRunningN2;

    // [RADAR] Weather radar OFF/ON values — confirmed against the def's registered
    // ValueDescriptions (FenixA320Definition.cs: S_WR_SYS {0="1",1="Off",2="2"},
    // S_WR_PRED_WS {0="Off",1="Auto"}).
    private const int WxOff = 1;
    private const int WxSys1 = 0;
    private const int PwsOff = 0;
    private const int PwsAuto = 1;

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
        Description = "Safety checks, batteries on, external power if available, nav lights.",
        RelatedChecklistGroupIds = new[] { "ELEC_POWER_UP" },
        Steps = new()
        {
            // Safety-check prefix (JD preliminary cockpit prep / FSFO phase 1): every step
            // skips quietly when already correct, so a warm start no-ops through them.
            Skip(SW("EPU_CHK_MASTER1", "Engine 1 master: OFF", "S_ENG_MASTER_1", 0),
                s => s.IsPosition("S_ENG_MASTER_1", 0)),
            Skip(SW("EPU_CHK_MASTER2", "Engine 2 master: OFF", "S_ENG_MASTER_2", 0),
                s => s.IsPosition("S_ENG_MASTER_2", 0)),
            Skip(SW("EPU_CHK_MODE", "Engine mode selector: NORM", "S_ENG_MODE", 1),
                s => s.IsPosition("S_ENG_MODE", 1)),
            Skip(SW("EPU_CHK_GEAR", "Gear lever: DOWN", "S_MIP_GEAR", 1),
                s => s.IsGearDown()),
            Skip(Multi("EPU_CHK_WIPERS", "Wipers: OFF",
                    ("S_MISC_WIPER_CAPT", 0), ("S_MISC_WIPER_FO", 0)),
                s => s.IsPosition("S_MISC_WIPER_CAPT", 0) && s.IsPosition("S_MISC_WIPER_FO", 0)),
            Skip(SW("EPU_CHK_WXR", "Weather radar: OFF", "S_WR_SYS", WxOff),   // [RADAR]
                s => s.IsPosition("S_WR_SYS", WxOff)),                          // [RADAR]
            Skip(SW("EPU_CHK_PARK", "Parking brake: ON", "S_MIP_PARKING_BRAKE", 1),
                s => s.IsOn("S_MIP_PARKING_BRAKE")),
            // Power up
            Done(Skip(SW("EPU_BAT1", "Battery 1: ON", "S_OH_ELEC_BAT1", 1),
                s => s.IsOn("S_OH_ELEC_BAT1")), "EPU_BAT1"),
            Done(Skip(SW("EPU_BAT2", "Battery 2: ON", "S_OH_ELEC_BAT2", 1),
                s => s.IsOn("S_OH_ELEC_BAT2")), "EPU_BAT2"),
            // Momentary pushbutton (pulse table). Harmless no-op if no ground power exists;
            // skipped when already on the bus (blue ON light).
            Done(Skip(SW("EPU_EXTPWR", "External power: ON", "S_OH_ELEC_EXT_PWR", 1),
                s => s.IsOn("I_OH_ELEC_EXT_PWR_L")), "EPU_EXTPWR"),
            Done(Skip(SW("EPU_NAVLOGO", "Nav and logo lights: ON", "S_OH_EXT_LT_NAV_LOGO", 1),
                s => s.GetValue("S_OH_EXT_LT_NAV_LOGO") > 0.5), "EPU_NAVLOGO"),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Preflight
    // -----------------------------------------------------------------------
    private static Flow BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "Cockpit preparation: recorder, IRS, oxygen, fire tests, air conditioning, signs, transponder setup.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT" },
        Steps = new()
        {
            // Recorder: latching press with an ON light readback, then the CVR test
            // (held 3 s via the CVR_TEST pseudo-key — audible test tone, self-completing so
            // it never sticks in TEST).
            Done(Skip(SW("PF_GNDCTL", "Recorder ground control: ON", "S_OH_RCRD_GND_CTL", 1),
                s => s.IsOn("I_OH_RCRD_GND_CTL_L")), "PF_GNDCTL"),
            Done(SW("PF_CVR", "CVR test — listen for the test tone", "CVR_TEST", 1), "PF_CVR"),
            // ADIRS
            Done(Skip(Multi("PF_IRS", "IRS 1, 2 and 3: NAV",
                    ("S_OH_NAV_IR1_MODE", 1), ("S_OH_NAV_IR2_MODE", 1), ("S_OH_NAV_IR3_MODE", 1)),
                s => s.IsPosition("S_OH_NAV_IR1_MODE", 1) && s.IsPosition("S_OH_NAV_IR2_MODE", 1)
                     && s.IsPosition("S_OH_NAV_IR3_MODE", 1)), "PF_IRS"),
            Done(Skip(SW("PF_OXY", "Crew oxygen supply: ON", "S_OH_OXYGEN_CREW_OXYGEN", 1),
                s => s.IsOn("S_OH_OXYGEN_CREW_OXYGEN")), "PF_OXY"),
            // Fire tests (held switches via the executor pseudo-keys; fire bell audible)
            Done(SW("PF_FIRE_APU", "APU fire test", "FIRE_TEST_APU", 1), "PF_FIRE_APU"),
            Done(SW("PF_FIRE_ENG1", "Engine 1 fire test", "FIRE_TEST_ENG1", 1), "PF_FIRE_ENG1"),
            Done(SW("PF_FIRE_ENG2", "Engine 2 fire test", "FIRE_TEST_ENG2", 1), "PF_FIRE_ENG2"),
            // Air conditioning / pressurization
            Done(Skip(Multi("PF_PACKS", "Packs 1 and 2: ON",
                    ("S_OH_PNEUMATIC_PACK_1", 1), ("S_OH_PNEUMATIC_PACK_2", 1)),
                s => s.IsOn("S_OH_PNEUMATIC_PACK_1") && s.IsOn("S_OH_PNEUMATIC_PACK_2")), "PF_PACKS"),
            Done(Skip(SW("PF_XBLEED", "Crossbleed: AUTO", "S_OH_PNEUMATIC_XBLEED_SELECTOR", 1),
                s => s.IsPosition("S_OH_PNEUMATIC_XBLEED_SELECTOR", 1)), "PF_XBLEED"),
            Done(Skip(SW("PF_PACKFLOW", "Pack flow: NORMAL", "S_OH_PNEUMATIC_PACK_FLOW", 1),
                s => s.IsPosition("S_OH_PNEUMATIC_PACK_FLOW", 1)), "PF_PACKFLOW"),
            Done(Skip(SW("PF_HOTAIR", "Hot air: ON", "S_OH_PNEUMATIC_HOT_AIR", 1),
                s => s.IsOn("S_OH_PNEUMATIC_HOT_AIR")), "PF_HOTAIR"),
            Done(Skip(SW("PF_PRESSMODE", "Cabin pressure mode: AUTO", "S_OH_PNEUMATIC_PRESS_MODE", 1),
                s => s.IsPosition("S_OH_PNEUMATIC_PRESS_MODE", 1)), "PF_PRESSMODE"),
            // Signs and lights
            Done(Skip(SW("PF_STROBE", "Strobes: AUTO", "S_OH_EXT_LT_STROBE", 1),
                s => s.IsPosition("S_OH_EXT_LT_STROBE", 1)), "PF_STROBE"),
            Done(Skip(SW("PF_WING_LT", "Wing lights: OFF", "S_OH_EXT_LT_WING", 0),
                s => s.IsPosition("S_OH_EXT_LT_WING", 0)), "PF_WING_LT"),
            Done(Skip(SW("PF_NOSMOKE", "No smoking: AUTO", "S_OH_SIGNS_SMOKING", 1),
                s => s.IsPosition("S_OH_SIGNS_SMOKING", 1)), "PF_NOSMOKE"),
            Done(Skip(SW("PF_EMEREXIT", "Emergency exit lights: ARM", "S_OH_INT_LT_EMER", 1),
                s => s.IsPosition("S_OH_INT_LT_EMER", 1)), "PF_EMEREXIT"),
            // Transponder setup
            Done(Skip(SW("PF_ALTRPTG", "Altitude reporting: ON", "S_XPDR_ALTREPORTING", 1),
                s => s.IsOn("S_XPDR_ALTREPORTING")), "PF_ALTRPTG"),
            Done(Skip(SW("PF_TCASTRAFFIC", "TCAS traffic: ALL", "S_TCAS_RANGE", 1),
                s => s.IsPosition("S_TCAS_RANGE", 1)), "PF_TCASTRAFFIC"),
            // Flight directors — verify on (skip when the FD lights are lit). The actuator is
            // the BASE var S_FCU_EFISn_FD, NOT "_PRESS" (the synthetic panel key is a no-op
            // written directly — same class of bug as the LS buttons).
            Skip(SW("PF_FD1", "Flight director 1: ON", "S_FCU_EFIS1_FD", 1),
                s => s.IsOn("I_FCU_EFIS1_FD")),
            Skip(SW("PF_FD2", "Flight director 2: ON", "S_FCU_EFIS2_FD", 1),
                s => s.IsOn("I_FCU_EFIS2_FD")),
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
        Description = "APU start, fuel pumps, external power off, signs, beacon, FCU managed modes.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START" },
        Steps = new()
        {
            // APU block: master on, dwell, start pulse, wait for AVAIL (green light).
            Done(Skip(SW("BS_APU_MASTER", "APU master: ON", "S_OH_ELEC_APU_MASTER", 1),
                s => s.IsOn("S_OH_ELEC_APU_MASTER")), "BS_APU"),
            Wait("BS_APU_DWELL", "Waiting before APU start", 3),
            Skip(SW("BS_APU_START", "APU start", "S_OH_ELEC_APU_START", 1),
                s => s.IsOn("I_OH_ELEC_APU_START_U")),
            // Stop policy: an APU start failure aborts the flow HERE, before external
            // power is pulsed off the bus below — never a silent transfer to batteries.
            WaitForField("BS_APU_AVAIL", "Waiting for APU available",
                "I_OH_ELEC_APU_START_U", v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop),
            Done(Skip(SW("BS_APUBLEED", "APU bleed: ON", "S_OH_PNEUMATIC_APU_BLEED", 1),
                s => s.IsOn("S_OH_PNEUMATIC_APU_BLEED")), "BS_APUBLEED"),
            // Fuel pumps immediately after the APU block (user decision, matches FSFO)
            Done(Skip(Multi("BS_FUELPUMPS", "Fuel pumps: ALL ON",
                    ("S_OH_FUEL_LEFT_1", 1), ("S_OH_FUEL_LEFT_2", 1),
                    ("S_OH_FUEL_CENTER_1", 1), ("S_OH_FUEL_CENTER_2", 1),
                    ("S_OH_FUEL_RIGHT_1", 1), ("S_OH_FUEL_RIGHT_2", 1)),
                s => s.IsOn("S_OH_FUEL_LEFT_1") && s.IsOn("S_OH_FUEL_LEFT_2")
                     && s.IsOn("S_OH_FUEL_CENTER_1") && s.IsOn("S_OH_FUEL_CENTER_2")
                     && s.IsOn("S_OH_FUEL_RIGHT_1") && s.IsOn("S_OH_FUEL_RIGHT_2")), "BS_FUELPUMPS"),
            // External power off (pulse toggles it off the bus); skip when not on the bus
            Done(Skip(SW("BS_EXTPWR_OFF", "External power: OFF", "S_OH_ELEC_EXT_PWR", 1),
                s => !s.IsOn("I_OH_ELEC_EXT_PWR_L")), "BS_EXTPWR_OFF"),
            Done(Skip(SW("BS_SEATBELTS", "Seatbelt signs: ON", "S_OH_SIGNS", 1),
                s => s.IsOn("S_OH_SIGNS")), "BS_SEATBELTS"),
            Done(Skip(SW("BS_BEACON", "Beacon: ON", "S_OH_EXT_LT_BEACON", 1),
                s => s.IsOn("S_OH_EXT_LT_BEACON")), "BS_BEACON"),
            // FCU managed modes (pseudo-keys → atomic knob-push calc)
            Done(SW("BS_FCUSPD", "FCU speed: managed", "FCU_PUSH_SPEED_MANAGED", 1), "BS_FCUSPD"),
            Done(SW("BS_FCUHDG", "FCU heading: managed", "FCU_PUSH_HEADING_MANAGED", 1), "BS_FCUHDG"),
            // Cockpit door: closed (S_COCKPIT_DOOR=0, live-verified actuator 2026-07-05).
            Done(SW("BS_COCKPITDOOR", "Cockpit door: closed and locked", "S_COCKPIT_DOOR", 0), "BS_COCKPITDOOR"),
            Captain("BS_DOORS", "Close doors and remove ground services on the EFB"),
            Captain("BS_THRLEVERS", "Confirm thrust levers idle"),
            Captain("BS_ACARS", "Start ACARS"),
            Captain("BS_CLEARANCE", "Obtain pushback and start clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 4. Engine Start (FADEC-managed: mode IGN, master on, N2 verification)
    // -----------------------------------------------------------------------
    private static Flow BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Description = "Engine mode IGN/START, then engine 1 and engine 2 with N2 verification.",
        RelatedChecklistGroupIds = new[] { "ENGINE_START" },
        Steps = new()
        {
            Done(Skip(SW("ES_MODE", "Engine mode selector: IGN START", "S_ENG_MODE", 2),
                s => s.IsPosition("S_ENG_MODE", 2)), "ES_MODE"),
            // Engine 1 first, then engine 2 (user preference). The FADEC runs the whole
            // start; each N2 wait verifies the engine actually spooled (stock TURB ENG N2,
            // pushed per second) before the next master goes on.
            Done(Skip(SW("ES_ENG1", "Engine 1 master: ON", "S_ENG_MASTER_1", 1),
                s => s.IsOn("S_ENG_MASTER_1")), "ES_ENG1"),
            WaitForField("ES_ENG1_N2", "Engine 1 starting — waiting for stabilized N2",
                "FO_ENG1_N2", v => v >= EngRunningN2, 120, onTimeout: FlowStepFailurePolicy.Stop),
            Done(Skip(SW("ES_ENG2", "Engine 2 master: ON", "S_ENG_MASTER_2", 1),
                s => s.IsOn("S_ENG_MASTER_2")), "ES_ENG2"),
            WaitForField("ES_ENG2_N2", "Engine 2 starting — waiting for stabilized N2",
                "FO_ENG2_N2", v => v >= EngRunningN2, 120, onTimeout: FlowStepFailurePolicy.Stop),
        }
    };

    // -----------------------------------------------------------------------
    // 5. After Start
    // -----------------------------------------------------------------------
    private static Flow BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Mode NORM, APU off, spoilers armed, takeoff flaps, trims, taxi light.",
        RelatedChecklistGroupIds = new[] { "AFTER_START" },
        Steps = new()
        {
            Done(Skip(SW("AS_MODE_NORM", "Engine mode selector: NORM", "S_ENG_MODE", 1),
                s => s.IsPosition("S_ENG_MODE", 1)), "AS_MODE_NORM"),
            Done(Skip(SW("AS_APUBLEED_OFF", "APU bleed: OFF", "S_OH_PNEUMATIC_APU_BLEED", 0),
                s => s.IsPosition("S_OH_PNEUMATIC_APU_BLEED", 0)), "AS_APUBLEED_OFF"),
            Done(Skip(SW("AS_APUMASTER_OFF", "APU master: OFF", "S_OH_ELEC_APU_MASTER", 0),
                s => s.IsPosition("S_OH_ELEC_APU_MASTER", 0)), "AS_APUMASTER_OFF"),
            Done(Skip(SW("AS_SPOILERS_ARM", "Ground spoilers: ARMED", "A_FC_SPEEDBRAKE", 0),
                s => s.IsPosition("A_FC_SPEEDBRAKE", 0)), "AS_SPOILERS_ARM"),
            Done(SW("AS_RUDDERTRIM", "Rudder trim: RESET", "S_FC_RUDDER_TRIM_RESET", 1), "AS_RUDDERTRIM"),
            // Takeoff flaps from SimBrief (quiet skip when no plan loaded)
            Done(Provider("AS_FLAPS", "Flaps: takeoff setting", "S_FC_FLAPS",
                s => { int f = s.TakeoffFlapsLeverIndex(); return f >= 1 ? f : (int?)null; }), "AS_FLAPS"),
            Done(Skip(SW("AS_NOSE_TAXI", "Nose light: TAXI", "S_OH_EXT_LT_NOSE", 1),
                s => s.IsPosition("S_OH_EXT_LT_NOSE", 1)), "AS_NOSE_TAXI"),
            Captain("AS_ANTIICE", "Set engine and wing anti-ice as required"),
            Captain("AS_PITCHTRIM", "Set pitch trim per the loadsheet"),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Before Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Description = "Autobrake MAX, radar, TCAS, config test, exterior lights.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF" },
        Steps = new()
        {
            // Momentary button with a latched ON light — skip when already armed so the
            // pulse can't toggle it back off.
            Done(Skip(SW("BT_AUTOBRAKE", "Autobrake: MAX", "S_MIP_AUTOBRAKE_MAX", 1),
                s => s.IsOn("I_MIP_AUTOBRAKE_MAX_L")), "BT_AUTOBRAKE"),
            Done(Skip(SW("BT_WXR", "Weather radar: SYSTEM 1", "S_WR_SYS", WxSys1),        // [RADAR]
                s => s.IsPosition("S_WR_SYS", WxSys1)), "BT_WXR"),                         // [RADAR]
            Done(Skip(SW("BT_PWS", "Predictive windshear: AUTO", "S_WR_PRED_WS", PwsAuto), // [RADAR]
                s => s.IsPosition("S_WR_PRED_WS", PwsAuto)), "BT_PWS"),                    // [RADAR]
            Done(Skip(SW("BT_TCAS", "TCAS: TA/RA", "S_XPDR_MODE", 2),
                s => s.IsPosition("S_XPDR_MODE", 2)), "BT_TCAS"),
            Done(Skip(SW("BT_XPDRAUTO", "Transponder: AUTO", "S_XPDR_OPERATION", 1),
                s => s.IsPosition("S_XPDR_OPERATION", 1)), "BT_XPDRAUTO"),
            Done(SW("BT_CONFIG", "Takeoff config test", "S_ECAM_TO", 1), "BT_CONFIG"),
            Done(Skip(SW("BT_TURNOFF", "Runway turn-off lights: ON", "S_OH_EXT_LT_RWY_TURNOFF", 1),
                s => s.IsOn("S_OH_EXT_LT_RWY_TURNOFF")), "BT_TURNOFF"),
            Done(SW("BT_LANDING_LT", "Landing lights: ON", "LANDING_LIGHTS_BOTH", 2), "BT_LANDING_LT"),
            Done(Skip(SW("BT_NOSE_TO", "Nose light: TAKEOFF", "S_OH_EXT_LT_NOSE", 2),
                s => s.IsPosition("S_OH_EXT_LT_NOSE", 2)), "BT_NOSE_TO"),
            Done(Skip(SW("BT_STROBE", "Strobes: ON", "S_OH_EXT_LT_STROBE", 2),
                s => s.IsPosition("S_OH_EXT_LT_STROBE", 2)), "BT_STROBE"),
            // Advise the cabin crew: hit CALL ALL and release (momentary chime).
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
        Description = "Spoilers disarm, packs on, turn-off lights off. Gear and autopilot are handled by the auto-managers; 10,000 ft lights and transition-altitude STD by the phase monitor.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF" },
        Steps = new()
        {
            Done(Skip(SW("AT_SPOILERS_DISARM", "Ground spoilers: DISARM", "A_FC_SPEEDBRAKE", 1),
                s => s.IsPosition("A_FC_SPEEDBRAKE", 1)), "AT_SPOILERS_DISARM"),
            Done(Skip(Multi("AT_PACKS", "Packs 1 and 2: ON",
                    ("S_OH_PNEUMATIC_PACK_1", 1), ("S_OH_PNEUMATIC_PACK_2", 1)),
                s => s.IsOn("S_OH_PNEUMATIC_PACK_1") && s.IsOn("S_OH_PNEUMATIC_PACK_2")), "AT_PACKS"),
            Done(Skip(SW("AT_TURNOFF_OFF", "Runway turn-off lights: OFF", "S_OH_EXT_LT_RWY_TURNOFF", 0),
                s => s.IsPosition("S_OH_EXT_LT_RWY_TURNOFF", 0)), "AT_TURNOFF_OFF"),
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
            Captain("DC_AUTOBRAKE", "Set the landing autobrake — Main Instrument Panel, Auto Brakes"),
            Done(Skip(SW("DC_SEATBELTS", "Seatbelt signs: ON", "S_OH_SIGNS", 1),
                s => s.IsOn("S_OH_SIGNS")), "DC_SEATBELTS"),
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
            // LS actuator is the BASE var S_FCU_EFISn_LS (NOT "_PRESS" — writing the synthetic
            // panel key directly is a no-op; that was the "LS not on in approach" bug).
            Done(Skip(SW("AP_LS1", "LS captain: ON", "S_FCU_EFIS1_LS", 1),
                s => s.IsOn("I_FCU_EFIS1_LS")), "AP_LS1"),
            Done(Skip(SW("AP_LS2", "LS first officer: ON", "S_FCU_EFIS2_LS", 1),
                s => s.IsOn("I_FCU_EFIS2_LS")), "AP_LS2"),
            // Notify the cabin crew for landing: hit CALL ALL and release (momentary chime).
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
            Done(Skip(SW("AL_SPOILERS", "Ground spoilers: DISARM", "A_FC_SPEEDBRAKE", 1),
                s => s.IsPosition("A_FC_SPEEDBRAKE", 1)), "AL_SPOILERS"),
            Done(Skip(SW("AL_FLAPS_UP", "Flaps: UP", "S_FC_FLAPS", 0),
                s => s.IsPosition("S_FC_FLAPS", 0)), "AL_FLAPS_UP"),
            Done(Skip(SW("AL_WXR_OFF", "Weather radar: OFF", "S_WR_SYS", WxOff),      // [RADAR]
                s => s.IsPosition("S_WR_SYS", WxOff)), "AL_WXR_OFF"),                  // [RADAR]
            Done(Skip(SW("AL_PWS_OFF", "Predictive windshear: OFF", "S_WR_PRED_WS", PwsOff), // [RADAR]
                s => s.IsPosition("S_WR_PRED_WS", PwsOff)), "AL_PWS_OFF"),             // [RADAR]
            Done(Skip(SW("AL_XPDR_STBY", "Transponder: STANDBY", "S_XPDR_OPERATION", 0),
                s => s.IsPosition("S_XPDR_OPERATION", 0)), "AL_XPDR_STBY"),
            Skip(SW("AL_TCAS_STBY", "TCAS: STANDBY", "S_XPDR_MODE", 0),
                s => s.IsPosition("S_XPDR_MODE", 0)),
            Done(Skip(SW("AL_STROBE_AUTO", "Strobes: AUTO", "S_OH_EXT_LT_STROBE", 1),
                s => s.IsPosition("S_OH_EXT_LT_STROBE", 1)), "AL_STROBE_AUTO"),
            Done(SW("AL_LANDING_OFF", "Landing lights: OFF", "LANDING_LIGHTS_BOTH", 1), "AL_LANDING_OFF"),
            Done(Skip(SW("AL_NOSE_TAXI", "Nose light: TAXI", "S_OH_EXT_LT_NOSE", 1),
                s => s.IsPosition("S_OH_EXT_LT_NOSE", 1)), "AL_NOSE_TAXI"),
            // APU for the gate (skip the whole block when already available)
            Done(Skip(SW("AL_APU_MASTER", "APU master: ON", "S_OH_ELEC_APU_MASTER", 1),
                s => s.IsOn("I_OH_ELEC_APU_START_U")), "AL_APU"),
            Skip(Wait("AL_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("I_OH_ELEC_APU_START_U")),
            Skip(SW("AL_APU_START", "APU start", "S_OH_ELEC_APU_START", 1),
                s => s.IsOn("I_OH_ELEC_APU_START_U")),
            WaitForField("AL_APU_AVAIL", "Waiting for APU available",
                "I_OH_ELEC_APU_START_U", v => v > 0.5, 180),
            Done(Skip(Multi("AL_ANTIICE_OFF", "Engine and wing anti-ice: OFF",
                    ("S_OH_PNEUMATIC_ENG1_ANTI_ICE", 0), ("S_OH_PNEUMATIC_ENG2_ANTI_ICE", 0),
                    ("S_OH_PNEUMATIC_WING_ANTI_ICE", 0)),
                s => s.IsPosition("S_OH_PNEUMATIC_ENG1_ANTI_ICE", 0)
                     && s.IsPosition("S_OH_PNEUMATIC_ENG2_ANTI_ICE", 0)
                     && s.IsPosition("S_OH_PNEUMATIC_WING_ANTI_ICE", 0)), "AL_ANTIICE_OFF"),
        }
    };

    // -----------------------------------------------------------------------
    // 11. Shutdown / Parking
    // -----------------------------------------------------------------------
    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Parking brake, APU bleed, engine masters off, signs, beacon, fuel pumps.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN" },
        Steps = new()
        {
            Done(Skip(SW("SD_PARKBRAKE", "Parking brake: ON", "S_MIP_PARKING_BRAKE", 1),
                s => s.IsOn("S_MIP_PARKING_BRAKE")), "SD_PARKBRAKE"),
            Done(Skip(SW("SD_APUBLEED_ON", "APU bleed: ON", "S_OH_PNEUMATIC_APU_BLEED", 1),
                s => s.IsOn("S_OH_PNEUMATIC_APU_BLEED")), "SD_APUBLEED_ON"),
            Done(Skip(SW("SD_ENG1_OFF", "Engine 1 master: OFF", "S_ENG_MASTER_1", 0),
                s => s.IsPosition("S_ENG_MASTER_1", 0)), "SD_ENG1_OFF"),
            Done(Skip(SW("SD_ENG2_OFF", "Engine 2 master: OFF", "S_ENG_MASTER_2", 0),
                s => s.IsPosition("S_ENG_MASTER_2", 0)), "SD_ENG2_OFF"),
            Done(Skip(SW("SD_SEATBELTS_OFF", "Seatbelt signs: OFF", "S_OH_SIGNS", 0),
                s => s.IsPosition("S_OH_SIGNS", 0)), "SD_SEATBELTS_OFF"),
            Done(Skip(SW("SD_BEACON_OFF", "Beacon: OFF", "S_OH_EXT_LT_BEACON", 0),
                s => s.IsPosition("S_OH_EXT_LT_BEACON", 0)), "SD_BEACON_OFF"),
            Done(Skip(Multi("SD_FUELPUMPS_OFF", "Fuel pumps: ALL OFF",
                    ("S_OH_FUEL_LEFT_1", 0), ("S_OH_FUEL_LEFT_2", 0),
                    ("S_OH_FUEL_CENTER_1", 0), ("S_OH_FUEL_CENTER_2", 0),
                    ("S_OH_FUEL_RIGHT_1", 0), ("S_OH_FUEL_RIGHT_2", 0)),
                s => s.IsPosition("S_OH_FUEL_LEFT_1", 0) && s.IsPosition("S_OH_FUEL_LEFT_2", 0)
                     && s.IsPosition("S_OH_FUEL_CENTER_1", 0) && s.IsPosition("S_OH_FUEL_CENTER_2", 0)
                     && s.IsPosition("S_OH_FUEL_RIGHT_1", 0) && s.IsPosition("S_OH_FUEL_RIGHT_2", 0)), "SD_FUELPUMPS_OFF"),
            Done(Skip(SW("SD_NOSE_OFF", "Nose light: OFF", "S_OH_EXT_LT_NOSE", 0),
                s => s.IsPosition("S_OH_EXT_LT_NOSE", 0)), "SD_NOSE_OFF"),
            Done(Skip(SW("SD_TURNOFF_OFF", "Runway turn-off lights: OFF", "S_OH_EXT_LT_RWY_TURNOFF", 0),
                s => s.IsPosition("S_OH_EXT_LT_RWY_TURNOFF", 0)), "SD_TURNOFF_OFF"),
            // Cockpit door: open for disembark (S_COCKPIT_DOOR=1, live-verified actuator 2026-07-05).
            Done(SW("SD_COCKPITDOOR", "Cockpit door: unlocked", "S_COCKPIT_DOOR", 1), "SD_COCKPITDOOR"),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Securing
    // -----------------------------------------------------------------------
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Securing",
        Description = "ADIRS, oxygen, emergency lights, signs, APU and batteries off.",
        RelatedChecklistGroupIds = new[] { "SECURE" },
        Steps = new()
        {
            Done(Skip(Multi("SC_ADIRS", "IRS 1, 2 and 3: OFF",
                    ("S_OH_NAV_IR1_MODE", 0), ("S_OH_NAV_IR2_MODE", 0), ("S_OH_NAV_IR3_MODE", 0)),
                s => s.IsPosition("S_OH_NAV_IR1_MODE", 0) && s.IsPosition("S_OH_NAV_IR2_MODE", 0)
                     && s.IsPosition("S_OH_NAV_IR3_MODE", 0)), "SC_ADIRS"),
            Done(Skip(SW("SC_OXY", "Crew oxygen supply: OFF", "S_OH_OXYGEN_CREW_OXYGEN", 0),
                s => s.IsPosition("S_OH_OXYGEN_CREW_OXYGEN", 0)), "SC_OXY"),
            Done(Skip(SW("SC_EMEREXIT", "Emergency exit lights: OFF", "S_OH_INT_LT_EMER", 0),
                s => s.IsPosition("S_OH_INT_LT_EMER", 0)), "SC_EMEREXIT"),
            Done(Skip(SW("SC_NOSMOKE", "No smoking: OFF", "S_OH_SIGNS_SMOKING", 0),
                s => s.IsPosition("S_OH_SIGNS_SMOKING", 0)), "SC_NOSMOKE"),
            Done(Skip(SW("SC_APUBLEED", "APU bleed: OFF", "S_OH_PNEUMATIC_APU_BLEED", 0),
                s => s.IsPosition("S_OH_PNEUMATIC_APU_BLEED", 0)), "SC_APUBLEED"),
            Done(Skip(SW("SC_APUMASTER", "APU master: OFF", "S_OH_ELEC_APU_MASTER", 0),
                s => s.IsPosition("S_OH_ELEC_APU_MASTER", 0)), "SC_APUMASTER"),
            Skip(SW("SC_EXTPWR_OFF", "External power: OFF", "S_OH_ELEC_EXT_PWR", 1),
                s => !s.IsOn("I_OH_ELEC_EXT_PWR_L")),
            Done(Skip(SW("SC_BAT1", "Battery 1: OFF", "S_OH_ELEC_BAT1", 0),
                s => s.IsPosition("S_OH_ELEC_BAT1", 0)), "SC_BAT1"),
            Done(Skip(SW("SC_BAT2", "Battery 2: OFF", "S_OH_ELEC_BAT2", 0),
                s => s.IsPosition("S_OH_ELEC_BAT2", 0)), "SC_BAT2"),
        }
    };

    // -----------------------------------------------------------------------
    // Step builders (mirror the 737 file's helpers)
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

    private static Step Provider(string id, string label, string eventName, Func<FenixStateEvaluator, int?> provider) => new()
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

    private static Step Skip(Step step, Func<FenixStateEvaluator, bool> cond)
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
