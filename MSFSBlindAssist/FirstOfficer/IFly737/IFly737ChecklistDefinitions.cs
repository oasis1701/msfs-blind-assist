using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

using Item = Models.ChecklistItem<IFly737ActionExecutor, IFly737StateEvaluator>;
using Group = Models.ChecklistGroup<IFly737ActionExecutor, IFly737StateEvaluator>;
using Act = System.Action<IFly737ActionExecutor, IFly737StateEvaluator>;

/// <summary>
/// Data-driven iFly 737 MAX8 First-Officer checklist definitions — same 24-group / two-layer
/// structure as <see cref="MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions"/>
/// (the template this file ports item-for-item): auto-detect "state" groups whose CheckActions
/// fire the same dispatch a flow would use, plus challenge-response readback checklists.
///
/// This is the SAME airframe (737-800 vs MAX8) with the same procedures, so ids/labels/order are
/// unchanged from the PMDG template; only detection fields and executor calls are swapped, per
/// the adaptation table (.superpowers/sdd/control-mapping.md) and the Task 5 brief. Every raw SDK
/// field name used here is verified against <c>IFlySdkFields.cs</c>/<c>IFlySdkOffsets.cs</c>.
///
/// Every auto-detect item is <see cref="RevertBehavior.RevertToState"/> (live state mirror) —
/// StayComplete was abandoned fleet-wide for the same reason as the PMDG 737 (an item whose
/// target state coincidentally matches an EARLIER phase would falsely latch complete).
///
/// Deviations from the PMDG 737 template (see task-5-report.md for the full rationale on each):
///  - GPU ON/OFF, generator ON/OFF and APU-generator ON/OFF are stateless directional presses
///    (momentary click buttons, not switches) — GPU items stay action-only like the PMDG; the
///    generator STATE items (BT_GEN/BTC_GEN) detect on ENG_GEN_OFF_BUS_Light_Status_{0,1}
///    (lit = generator NOT on the bus, so "Generators: ON" is the light being OFF/0).
///  - Engine start is pilot-paced (PMDG 737 convention) exactly as the template, but the start
///    lever's "idle" detection is Engine_Start_Lever_Status_{0,1} &gt;= 3 (a 0-5 switch+fire-light
///    composite: 0-2 = Cutoff, 3-5 = Idle), not a derived 1=RUN field.
///  - Speedbrake ARM (Landing) is a Captain reminder here, not an ActionManual press — this
///    aircraft's speedbrake-lever write has an unverified scale mismatch and is deliberately
///    read-only. Its Landing Checklist twin (LDC_SPDBRK) auto-detects on
///    SPEED_BRAKE_ARMED_Light_Status instead of staying a reminder, since the iFly DOES expose
///    that readback (the PMDG NG3 struct has none at all).
///  - Weather radar test: REMOVED from this aircraft's checklist and flow entirely (user
///    decision 2026-08-18 — do not re-add). The command exists (`FMS_WXR_SYS_CTRL_SET`,
///    Value2 0 TEST/1 NORM, readable back via `Weather_Radar_System_Control_Switch_Status`)
///    but remains deliberately unwired — see IFly737ActionExecutor.PseudoKeys.
///  - Gear lever has only Up(0)/Down(1) — no OFF detent (RegisterLandingGear,
///    IFly737MAXDefinition.ForwardPedestal.cs:24-25 — `new[] { "Up", "Down" }`). ATKO_GEAR_OFF
///    and its After Takeoff Checklist twin ATC_GEAR command/detect GearUp only and are labelled
///    "Gear lever: UP" / "Landing gear: UP" (Fix pass 1, 2026-08) — the PMDG-ported "OFF"/
///    "UP and OFF" wording named a position this airframe's switch does not have.
///  - Transponder STBY wording was likewise ported wrong: this airframe's resting/ground
///    position is ALT OFF, not STBY (RegisterTransponder, IFly737MAXDefinition.cs:596-597 —
///    `new[] { "ALT OFF", "XPNDR", "TA Only", "TA/RA" }`). PF_XPDR and SD_XPDR are labelled
///    "Transponder: ALT OFF" (Fix pass 1, 2026-08).
///  - Probe heat has no OFF position either — only Auto/On (RegisterAntiIce,
///    IFly737MAXDefinition.Overhead.cs:424-428 — `new[] { "Auto", "On" }`). PF_PROBE_OFF,
///    AL_PROBE and SDC_PROBE are labelled "Probe heat: AUTO" (Fix pass 1, 2026-08).
///  - "Lower display unit: SYS" (BT_LOWERDU) has no iFly counterpart — no lower-DU/EICAS
///    synoptic-page-select field exists in IFlySdkFields.cs (searched for LOWER/EICAS/Lower
///    Display) — so it is a Captain reminder here.
///  - Two items (Yaw damper ON, Flaps UP) use the executor's generic <see cref="IFly737ActionExecutor.Set"/>
///    rather than a typed wrapper — Task 4 didn't add SetYawDamper/SetFlapsPosition wrappers, but
///    both varKeys ("Yaw_Damper_Switch_Status", "FLAP_Status") ARE registered/writable controls
///    (IFly737MAXDefinition.LightsMisc.cs / ForwardPedestal.cs), so the generic path the
///    executor's own doc comment reserves for exactly this case is used instead of inventing a
///    parallel wrapper.
///  - Baro STD: a Before Takeoff Checklist item (BTOC_BARO, confirms QNH — not standard —
///    before departure) was ADDED — the PMDG 737 template has no baro-STD checklist item
///    anywhere. It is action-free either way, so it carries no risk of commanding the wrong
///    pressure reference. Fix pass 2 (2026-08) DOWNGRADED it from Auto to a Captain reminder:
///    its detection field BARO_STD_Status is MOMENTARY, so it read 0 (= "QNH") always and the
///    item ticked itself regardless of the real reference — see the inline note.
///  - Fix pass 1 (2026-08) REMOVED a Descent state item (DSA_BARO) that had actioned
///    SetAltimetersStandardAsync() — i.e. it commanded STANDARD pressure DURING THE DESCENT,
///    which is backwards: standard is set climbing through the transition altitude, and local
///    QNH is set descending through the transition level (see
///    <see cref="MSFSBlindAssist.FirstOfficer.PMDG737.FlightPhaseMonitor"/> for the correct,
///    already-shipped 737 behaviour). Being RevertToState, DSA_BARO auto-ticked at top of
///    descent (standard was still set from cruise) and then UN-ticked itself the moment the
///    pilot correctly set QNH at the transition level — telling a blind pilot an item was
///    outstanding precisely because they had done the right thing — and a manual tick drove
///    both altimeters back to 1013/29.92 below the transition level with no visual cross-check
///    to catch it. Transition-altitude handling for this airframe belongs to a future
///    IFly737FlightPhaseMonitor (PMDG parity), not the checklist.
/// </summary>
public static class IFly737ChecklistDefinitions
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
    // as the matching flow step). Item order mirrors the PMDG 737 template.
    // =======================================================================

    private static Group BuildElectricalPowerUp() => new()
    {
        Id = "ELEC_POWER_UP", Name = "Electrical Power Up",
        Items = new()
        {
            // Battery_Switch_Mode: 1=Off/2=On (guarded switch; the executor special-cases
            // the write, sending the guard-bypass and verifying it moved).
            Auto("EPU_BATTERY", "ELEC_POWER_UP", "Battery: ON", "Battery_Switch_Mode", v => v > 1.5,
                (e, _) => e.SetBattery(true)),
            // STANDBY_POWER_Switch_Mode: 1=Battery/2=Off/3=Auto.
            Auto("EPU_STBY", "ELEC_POWER_UP", "Standby power: AUTO", "STANDBY_POWER_Switch_Mode", v => v > 2.5,
                (e, _) => e.SetStandbyPower(IFly737ActionExecutor.StandbyPowerAuto)),
            // Stateless press (momentary click buttons, no reliable "on bus" signal) — same
            // shape as the PMDG's own GPU item.
            ActionManual("EPU_GPU", "ELEC_POWER_UP", "Ground power: ON",
                (e, _) => e.PressGroundPower(true)),
            // IRS_Mode_Switch_Status_{0,1}: 0 Off/1 Align/2 Nav/3 Att — same numbering as the
            // PMDG template, so the threshold carries over unchanged.
            Auto("EPU_IRS", "ELEC_POWER_UP", "IRS mode selectors: NAV", "IRS_Mode_Switch_Status_0", v => v > 1.5,
                new[] { "IRS_Mode_Switch_Status_1" }, (e, _) => e.SetIrsMode(IFly737ActionExecutor.IrsNav)),
        }
    };

    private static Group BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = new()
        {
            ActionManualAsync("PF_FIRE_TEST", "PREFLIGHT", "Fire warning test",
                (e, _) => e.FireTestAsync()),
            ActionManualAsync("PF_STALL_TEST1", "PREFLIGHT", "Stall warning test 1",
                (e, _) => e.StallTestAsync(1)),
            ActionManualAsync("PF_STALL_TEST2", "PREFLIGHT", "Stall warning test 2",
                (e, _) => e.StallTestAsync(2)),
            ActionManualAsync("PF_OVSPD_TEST1", "PREFLIGHT", "Overspeed warning test 1",
                (e, _) => e.OverspeedTestAsync(1)),
            ActionManualAsync("PF_OVSPD_TEST2", "PREFLIGHT", "Overspeed warning test 2",
                (e, _) => e.OverspeedTestAsync(2)),
            ActionManualAsync("PF_TCAS_TEST", "PREFLIGHT", "TCAS test",
                (e, _) => e.TcasTestAsync()),
            // Yaw_Damper_Switch_Status: 0 Off/1 On. No typed wrapper exists (Task 4 didn't add
            // one) — the varKey IS registered/writable (LightsMisc.cs), so this uses the
            // executor's generic Set() path exactly as its own doc comment allows.
            Auto("PF_YD", "PREFLIGHT", "Yaw damper: ON", "Yaw_Damper_Switch_Status", v => v > 0.5,
                (e, _) => e.Set("Yaw_Damper_Switch_Status", 1)),
            // Same wing-before-centre ordering requirement as SD_FUEL below — see that item's
            // comment for why the un-awaited call order is load-bearing (SemaphoreSlim.WaitAsync
            // registers waiters in call order) and must never be reordered or awaited.
            Auto("PF_FUEL_OFF", "PREFLIGHT", "Fuel pumps: OFF", "Fuel_L_AFT_Switch_Status", v => v < 0.5,
                new[] { "Fuel_L_FWD_Switch_Status", "Fuel_R_FWD_Switch_Status", "Fuel_R_AFT_Switch_Status",
                        "Fuel_CENTER_L_Switch_Status", "Fuel_CENTER_R_Switch_Status" },
                (e, _) => { e.SetWingFuelPumps(0); e.SetCenterFuelPumps(0); }),
            // Emergency_Light_Switch_Status: 0 Guard closed/1 Off/2 Armed/3 On
            // (IFly737MAXDefinition.LightsMisc.cs:96-104). On the real 737 the guard physically
            // sits OVER the ARMED detent: with the guard down the switch cannot be moved to Off
            // or On, so a guard-closed reading (0) IS armed, not merely "not yet confirmed
            // armed" — it must be accepted here alongside the explicit Armed position (2), and
            // (see SE_EMER/SEC_EMER) must NOT also satisfy OFF. v==1 (guard lifted, switch at
            // Off) and v==3 (On) are the only non-armed readings.
            Auto("PF_EMER", "PREFLIGHT", "Emergency exit lights: ARMED", "Emergency_Light_Switch_Status",
                v => v < 0.5 || (v > 1.5 && v < 2.5), (e, _) => e.SetEmerExitLights(IFly737ActionExecutor.EmerExitArmed)),
            // Fasten_Belts_Switch_Status: 0 Off/1 Auto/2 On — same numbering as the PMDG.
            Auto("PF_BELTS", "PREFLIGHT", "Seatbelt signs: ON", "Fasten_Belts_Switch_Status", v => v > 1.5,
                (e, _) => e.SetSeatBelts(IFly737ActionExecutor.SignOn)),
            Auto("PF_WINHEAT", "PREFLIGHT", "Window heat: ON", "Window_Heat_Switch_1_Status", v => v > 0.5,
                new[] { "Window_Heat_Switch_2_Status", "Window_Heat_Switch_3_Status", "Window_Heat_Switch_4_Status" },
                (e, _) => e.SetWindowHeat(1)),
            // Probe_Heat_Switch_{1,2}_Status: 0 AUTO/1 On (no separate OFF position on this
            // airframe — RegisterAntiIce, IFly737MAXDefinition.Overhead.cs:424-428, positions
            // "Auto"/"On"). Label says AUTO, matching the switch's own wording, not the PMDG's
            // OFF (Fix pass 1, 2026-08).
            Auto("PF_PROBE_OFF", "PREFLIGHT", "Probe heat: AUTO", "Probe_Heat_Switch_1_Status", v => v < 0.5,
                new[] { "Probe_Heat_Switch_2_Status" }, (e, _) => e.SetProbeHeat(IFly737ActionExecutor.ProbeHeatAuto)),
            Auto("PF_WAI", "PREFLIGHT", "Wing anti-ice: OFF", "Wing_AntiIce_Switch_Status", v => v < 0.5,
                (e, _) => e.SetWingAntiIce(0)),
            Auto("PF_EAI_OFF", "PREFLIGHT", "Engine anti-ice: OFF", "Eng_1_AntiIce_Switch_Status", v => v < 0.5,
                new[] { "Eng_2_AntiIce_Switch_Status" }, (e, _) => e.SetEngAntiIce(0)),
            // RecircFan_Switch_Status_{0,1}: 0 Off/1 Auto (the ON position is labelled AUTO).
            Auto("PF_RECIRC", "PREFLIGHT", "Recirculation fans: AUTO", "RecircFan_Switch_Status_0", v => v > 0.5,
                new[] { "RecircFan_Switch_Status_1" }, (e, _) => e.SetRecircFans(1)),
            // Pack_Switch_Status_{0,1}: 0 Off/1 Auto/2 High.
            Auto("PF_PACKS", "PREFLIGHT", "Packs: AUTO", "Pack_Switch_Status_0", v => v > 0.5 && v < 1.5,
                new[] { "Pack_Switch_Status_1" }, (e, _) => e.SetPacks(IFly737ActionExecutor.PackAuto)),
            // Isolation_Valve_Switch_Status: 0 Close/1 Auto/2 Open.
            Auto("PF_ISO", "PREFLIGHT", "Isolation valve: OPEN", "Isolation_Valve_Switch_Status", v => v > 1.5,
                (e, _) => e.SetIsolationValve(IFly737ActionExecutor.IsolationValveOpen)),
            Auto("PF_BLEEDS", "PREFLIGHT", "Engine bleeds: ON", "Engine_Bleed_Air_Switch_Status_0", v => v > 0.5,
                new[] { "Engine_Bleed_Air_Switch_Status_1" }, (e, _) => e.SetEngBleeds(1)),
            AutoAsync("PF_PRESS", "PREFLIGHT", "Flight and landing altitudes: SET",
                "FO_PRESS_ALTS_MATCH", v => v > 0.5,
                (e, s) => e.SetPressurizationAltitudesAsync(s)),
            Auto("PF_LOGO", "PREFLIGHT", "Logo lights: ON", "Logo_Light_Switch_Status", v => v > 0.5,
                (e, _) => e.SetLogo(1)),
            // FD_1/2_Switch_Status is an ABSOLUTE SET (no toggle hazard), so unlike the PMDG's
            // guarded AutoAsync this is a plain fire-and-forget Auto over both sides.
            Auto("PF_FD", "PREFLIGHT", "Flight directors: ON", "FD_1_Switch_Status", v => v > 0.5,
                new[] { "FD_2_Switch_Status" }, (e, _) => { e.SetFDLeft(true); e.SetFDRight(true); }),
            // Autobrake_Selector_Status: 0 RTO/1 Off/2 "1"/3 "2"/4 "3"/5 Max Auto — same
            // numbering as the PMDG template.
            Auto("PF_AB", "PREFLIGHT", "Autobrake: RTO", "Autobrake_Selector_Status", v => v < 0.5,
                (e, _) => e.SetAutobrake(IFly737ActionExecutor.AutobrakeRto)),
            // Transponder_Mode_Switch_Status: 0 ALT OFF/1 XPNDR/2 TA Only/3 TA-RA (RegisterTransponder,
            // IFly737MAXDefinition.cs:596-597, positions "ALT OFF"/"XPNDR"/"TA Only"/"TA/RA").
            // Label says ALT OFF, matching the switch's own wording, not the PMDG's STBY
            // (Fix pass 1, 2026-08).
            Auto("PF_XPDR", "PREFLIGHT", "Transponder: ALT OFF", "Transponder_Mode_Switch_Status", v => v < 0.5,
                (e, _) => e.SetTransponderMode(IFly737ActionExecutor.XpdrAltOff)),
            // ND_Mode_Status_0: 0 Approach/1 VOR/2 Map/3 Plan.
            Auto("PF_EFIS_MODE", "PREFLIGHT", "EFIS mode: MAP", "ND_Mode_Status_0", v => v > 1.5 && v < 2.5,
                (e, _) => e.SetEFISModeCapt(IFly737ActionExecutor.NdModeMap)),
            // NO EFIS-range item (removed entirely, user decision 2026-08-18 — do not
            // re-add): the ND range cannot be commanded absolutely on this SDK (RANGE_SET
            // dead, ND_Range_Status net-clicks mod 3, the cockpit knob L:var a free-spinning
            // rotation counter — no absolute position anywhere; see IFly737ActionExecutor's
            // SetEFISRangeCapt note), so an item here could only nag. The panel's Range
            // Increase/Decrease buttons remain for the pilot.
            Reminder("PF_ALT", "PREFLIGHT", "Altimeters: SET to local QNH"),
            ActionManualAsync("PF_GPWS_TEST", "PREFLIGHT", "GPWS system test",
                (e, _) => e.GpwsTestAsync()),
        }
    };

    private static Group BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            Reminder("BS_MCP", "BEFORE_START", "Set MCP airspeed, heading and initial altitude"),
            // APU_Switch_Status: 0 Off/1 On/2 Start — StartApuAsync dwells at ON then presses
            // START; writing START directly never spools the APU up.
            AutoAsync("BS_APU", "BEFORE_START", "APU: ON line", "APU_Switch_Status", v => v > 0.5,
                (e, _) => e.StartApuAsync()),
            // APU generator switches are stateless momentary click pairs — action-only, like
            // the PMDG's own APU-gen item (which has no state detection either).
            ActionManual("BS_APUGEN", "BEFORE_START", "APU generators: ON",
                (e, _) => { e.PressApuGenerator(1, true); e.PressApuGenerator(2, true); }),
            Auto("BS_FUEL", "BEFORE_START", "Fuel pumps: ON", "FO_FUEL_PUMPS_BS_OK", v => v > 0.5,
                (e, _) => { e.SetWingFuelPumps(1); e.SetCenterFuelPumps(1); }),
            Auto("BS_HYD", "BEFORE_START", "Electric hydraulic pumps: ON", "ELEC_1_HYD_Switch_Status", v => v > 0.5,
                new[] { "ELEC_2_HYD_Switch_Status" }, (e, _) => e.SetElecHydPumps(1)),
            Auto("BS_HYDENG", "BEFORE_START", "Engine hydraulic pumps: ON", "ENG_1_HYD_Switch_Status", v => v > 0.5,
                new[] { "ENG_2_HYD_Switch_Status" }, (e, _) => e.SetEngHydPumps(1)),
            Auto("BS_APUBLEED", "BEFORE_START", "APU bleed air: ON", "APU_Bleed_Air_Switch_Status", v => v > 0.5,
                (e, _) => e.SetApuBleed(1)),
            Auto("BS_ANTICOL", "BEFORE_START", "Anti-collision light: ON", "Anti_Collision_Light_Switch_Status",
                v => v > 0.5, (e, _) => e.SetBeacon(1)),
            // Transponder_Mode_Switch_Status TA/RA = 3 (max position — 4 positions total, not
            // the PMDG's 5).
            Auto("BS_XPDR", "BEFORE_START", "Transponder: TA/RA", "Transponder_Mode_Switch_Status", v => v > 2.5,
                (e, _) => e.SetTransponderMode(IFly737ActionExecutor.XpdrTaRa)),
            Reminder("BS_GND", "BEFORE_START", "Confirm ground power and chocks removed, doors closed"),
            Reminder("BS_ACARS", "BEFORE_START", "Start ACARS"),
            Reminder("BS_CLEARANCE", "BEFORE_START", "Obtain pushback and start clearance"),
        }
    };

    private static Group BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            Auto("ES_PACKS", "ENGINE_START", "Packs: OFF", "Pack_Switch_Status_0", v => v < 0.5,
                new[] { "Pack_Switch_Status_1" }, (e, _) => e.SetPacks(IFly737ActionExecutor.PackOff)),
            // Pilot-paced start (PMDG 737 convention). Start-switch items are action-only — GRD
            // is a momentary that springs back; the lever items detect off
            // Engine_Start_Lever_Status_{0,1} (0-5 composite: 0-2 Cutoff, 3-5 Idle — >=3 covers
            // every fire-light variant of Idle), so a lever moved from the cockpit auto-ticks
            // too.
            ActionManual("ES_E2_GRD", "ENGINE_START", "Engine 2 start switch: GRD",
                (e, _) => e.SetEngStartSelector2(IFly737ActionExecutor.EngStartGround)),
            Auto("ES_E2_RUN", "ENGINE_START", "Engine 2 start lever: IDLE (at 25 percent N2)",
                "Engine_Start_Lever_Status_1", v => v >= 3, (e, _) => e.SetFuelControl2(1)),
            ActionManual("ES_E1_GRD", "ENGINE_START", "Engine 1 start switch: GRD",
                (e, _) => e.SetEngStartSelector1(IFly737ActionExecutor.EngStartGround)),
            Auto("ES_E1_RUN", "ENGINE_START", "Engine 1 start lever: IDLE (at 25 percent N2)",
                "Engine_Start_Lever_Status_0", v => v >= 3, (e, _) => e.SetFuelControl1(1)),
        }
    };

    private static Group BuildBeforeTaxi() => new()
    {
        Id = "BEFORE_TAXI", Name = "Before Taxi",
        Items = new()
        {
            // ENG_GEN_OFF_BUS_Light_Status_{0,1}: 0 light Off / nonzero DIM|BRIGHT — lit means
            // the generator is NOT on the bus, so "Generators: ON" is the light being OFF.
            Auto("BT_GEN", "BEFORE_TAXI", "Generators: ON", "ENG_GEN_OFF_BUS_Light_Status_0", v => v < 0.5,
                new[] { "ENG_GEN_OFF_BUS_Light_Status_1" },
                (e, _) => { e.PressGenerator(1, true); e.PressGenerator(2, true); }),
            Auto("BT_APUBLEED_OFF", "BEFORE_TAXI", "APU bleed air: OFF", "APU_Bleed_Air_Switch_Status", v => v < 0.5,
                (e, _) => e.SetApuBleed(0)),
            Auto("BT_APU", "BEFORE_TAXI", "APU: OFF", "APU_Switch_Status", v => v < 0.5,
                (e, _) => e.SetApuSelector(IFly737ActionExecutor.ApuOff)),
            Auto("BT_PROBE", "BEFORE_TAXI", "Probe heat: ON", "Probe_Heat_Switch_1_Status", v => v > 0.5,
                new[] { "Probe_Heat_Switch_2_Status" }, (e, _) => e.SetProbeHeat(IFly737ActionExecutor.ProbeHeatOn)),
            Auto("BT_PACKS", "BEFORE_TAXI", "Packs: AUTO", "Pack_Switch_Status_0", v => v > 0.5 && v < 1.5,
                new[] { "Pack_Switch_Status_1" }, (e, _) => e.SetPacks(IFly737ActionExecutor.PackAuto)),
            Auto("BT_ISO", "BEFORE_TAXI", "Isolation valve: AUTO", "Isolation_Valve_Switch_Status",
                v => v > 0.5 && v < 1.5, (e, _) => e.SetIsolationValve(IFly737ActionExecutor.IsolationValveAuto)),
            // Engine_Start_Switch_Status_{0,1}: 0 Ground/1 Off/2 Continuous/3 Flight — same
            // numbering as the PMDG template, threshold carries over unchanged.
            Auto("BT_START", "BEFORE_TAXI", "Engine start switches: CONT", "Engine_Start_Switch_Status_0",
                v => v > 1.5 && v < 2.5, new[] { "Engine_Start_Switch_Status_1" },
                (e, _) => { e.SetEngStartSelector1(IFly737ActionExecutor.EngStartContinuous);
                            e.SetEngStartSelector2(IFly737ActionExecutor.EngStartContinuous); }),
            Reminder("BT_ANTIICE", "BEFORE_TAXI", "Set engine and wing anti-ice as required for conditions"),
            Auto("BT_TAXI", "BEFORE_TAXI", "Taxi light: ON", "Taxi_Light_Switch_Status", v => v > 0.5,
                (e, _) => e.SetTaxiLights(1)),
            Auto("BT_TURNOFF", "BEFORE_TAXI", "Runway turnoff lights: ON", "Runway_Turnoff_Light_1_Switch_Status",
                v => v > 0.5, new[] { "Runway_Turnoff_Light_2_Switch_Status" }, (e, _) => e.SetRunwayTurnoff(1)),
            Reminder("BT_FLAPS", "BEFORE_TAXI", "Set the takeoff flaps"),
            // No lower-DU/EICAS synoptic-page-select field exists in IFlySdkFields.cs (searched
            // LOWER/EICAS/"Lower Display") — Captain reminder, not an action.
            Reminder("BT_LOWERDU", "BEFORE_TAXI", "Lower display unit: SYS"),
            ActionManual("BT_RECALL", "BEFORE_TAXI", "Recall: checked", (e, _) => e.PressRecall()),
        }
    };

    private static Group BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Items = new()
        {
            // Landing_Light_{1,2}_Switch_Status: 0 Off/1 Flash/2 On (probe-verified, PR #196 —
            // 1 is FLASH, not On, so "ON" means status 2). No retractable/fixed split.
            Auto("BTKO_LAND", "BEFORE_TAKEOFF", "Landing lights: ON", "Landing_Light_1_Switch_Status", v => v > 1.5,
                new[] { "Landing_Light_2_Switch_Status" },
                (e, _) => e.SetLandingLights(IFly737ActionExecutor.LandingLightsOn)),
            // Position_Light_Switch_Status: 0 Strobe & Steady/1 Off/2 Steady (probe-verified,
            // PR #196 — the SDK status doc's labels were reversed; NOT the PMDG numbering).
            Auto("BTKO_STROBE", "BEFORE_TAKEOFF", "Position lights: STROBE & STEADY", "Position_Light_Switch_Status",
                v => v < 0.5, (e, _) => e.SetPositionLights(IFly737ActionExecutor.PositionLightsStrobeAndSteady)),
            // AT_Switch_Status is an absolute SET (0 Off/1 Armed) — no guard/state param needed.
            Auto("BTKO_AT", "BEFORE_TAKEOFF", "Autothrottle: ARM", "AT_Switch_Status", v => v > 0.5,
                (e, _) => e.SetATArm(true)),
            Auto("BTKO_XPDR", "BEFORE_TAKEOFF", "Transponder: TA/RA", "Transponder_Mode_Switch_Status", v => v > 2.5,
                (e, _) => e.SetTransponderMode(IFly737ActionExecutor.XpdrTaRa)),
            ActionManual("BTKO_CABIN", "BEFORE_TAKEOFF", "Advise the cabin crew for takeoff (call all)",
                (e, _) => e.CabinCall()),
        }
    };

    private static Group BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = new()
        {
            Auto("ATKO_PACKS", "AFTER_TAKEOFF", "Packs: AUTO", "Pack_Switch_Status_0", v => v > 0.5 && v < 1.5,
                new[] { "Pack_Switch_Status_1" }, (e, _) => e.SetPacks(IFly737ActionExecutor.PackAuto)),
            Auto("ATKO_START_OFF", "AFTER_TAKEOFF", "Engine start switches: OFF", "Engine_Start_Switch_Status_0",
                v => v > 0.5 && v < 1.5, new[] { "Engine_Start_Switch_Status_1" },
                (e, _) => { e.SetEngStartSelector1(IFly737ActionExecutor.EngStartOff);
                            e.SetEngStartSelector2(IFly737ActionExecutor.EngStartOff); }),
            Auto("ATKO_TURNOFF", "AFTER_TAKEOFF", "Runway turnoff lights: OFF", "Runway_Turnoff_Light_1_Switch_Status",
                v => v < 0.5, new[] { "Runway_Turnoff_Light_2_Switch_Status" }, (e, _) => e.SetRunwayTurnoff(0)),
            // Gear_Lever_Status has only 0 Up/1 Down — no OFF detent on this airframe
            // (RegisterLandingGear, IFly737MAXDefinition.ForwardPedestal.cs:24-25, positions
            // "Up"/"Down"), so this commands UP and is labelled "UP" rather than the PMDG's
            // "OFF", which names a position that does not exist here (Fix pass 1, 2026-08).
            Auto("ATKO_GEAR_OFF", "AFTER_TAKEOFF", "Gear lever: UP", "Gear_Lever_Status", v => v < 0.5,
                (e, _) => e.SetGearLever(IFly737ActionExecutor.GearUp)),
            Auto("ATKO_AB_OFF", "AFTER_TAKEOFF", "Autobrake: OFF", "Autobrake_Selector_Status",
                v => v > 0.5 && v < 1.5, (e, _) => e.SetAutobrake(IFly737ActionExecutor.AutobrakeOff)),
        }
    };

    private static Group BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Items = new()
        {
            ActionManual("DSA_RECALL", "DESCENT", "Recall: checked", (e, _) => e.PressRecall()),
            Auto("DSA_BELTS", "DESCENT", "Seatbelt signs: ON", "Fasten_Belts_Switch_Status", v => v > 1.5,
                (e, _) => e.SetSeatBelts(IFly737ActionExecutor.SignOn)),
            Reminder("DSA_AB", "DESCENT", "Set the landing autobrake — Forward Panel, Autobrake"),
            Reminder("DSA_ILS", "DESCENT", "Set the ILS frequencies and course"),
        }
    };

    private static Group BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = new()
        {
            Auto("APA_EFIS_MODE", "APPROACH", "EFIS mode: APP", "ND_Mode_Status_0", v => v < 0.5,
                (e, _) => e.SetEFISModeCapt(IFly737ActionExecutor.NdModeApproach)),
            // NO EFIS-range item — removed with the Preflight one (see that group's note).
            ActionManual("AP_CABIN", "APPROACH", "Notify the cabin crew for landing (call all)",
                (e, _) => e.CabinCall()),
            Reminder("APA_ALT", "APPROACH", "Set the altimeters"),
        }
    };

    private static Group BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Items = new()
        {
            Auto("LDA_START", "LANDING", "Engine start switches: CONT", "Engine_Start_Switch_Status_0",
                v => v > 1.5 && v < 2.5, new[] { "Engine_Start_Switch_Status_1" },
                (e, _) => { e.SetEngStartSelector1(IFly737ActionExecutor.EngStartContinuous);
                            e.SetEngStartSelector2(IFly737ActionExecutor.EngStartContinuous); }),
            // Speedbrake ARM is a Captain reminder on this aircraft — the lever write has an
            // unverified scale mismatch and is deliberately read-only (see class doc).
            Reminder("LDA_SPDBRK", "LANDING", "Speedbrake: ARMED"),
            Reminder("LDA_MISSED", "LANDING", "Set the missed approach altitude"),
        }
    };

    private static Group BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_LAND_OFF", "AFTER_LANDING", "Landing lights: OFF", "Landing_Light_1_Switch_Status", v => v < 0.5,
                new[] { "Landing_Light_2_Switch_Status" },
                (e, _) => e.SetLandingLights(IFly737ActionExecutor.LandingLightsOff)),
            Auto("AL_TURNOFF", "AFTER_LANDING", "Runway turnoff lights: ON", "Runway_Turnoff_Light_1_Switch_Status",
                v => v > 0.5, new[] { "Runway_Turnoff_Light_2_Switch_Status" }, (e, _) => e.SetRunwayTurnoff(1)),
            Auto("AL_TAXI", "AFTER_LANDING", "Taxi light: ON", "Taxi_Light_Switch_Status", v => v > 0.5,
                (e, _) => e.SetTaxiLights(1)),
            // Steady = status 2 under the verified encoding (PR #196) — see BTKO_STROBE.
            Auto("AL_STROBE", "AFTER_LANDING", "Position lights: STEADY", "Position_Light_Switch_Status", v => v > 1.5,
                (e, _) => e.SetPositionLights(IFly737ActionExecutor.PositionLightsSteady)),
            Auto("AL_EAI", "AFTER_LANDING", "Engine anti-ice: OFF", "Eng_1_AntiIce_Switch_Status", v => v < 0.5,
                new[] { "Eng_2_AntiIce_Switch_Status" }, (e, _) => e.SetEngAntiIce(0)),
            Auto("AL_WAI", "AFTER_LANDING", "Wing anti-ice: OFF", "Wing_AntiIce_Switch_Status", v => v < 0.5,
                (e, _) => e.SetWingAntiIce(0)),
            // No OFF position on this switch — AUTO is the resting position (see PF_PROBE_OFF).
            Auto("AL_PROBE", "AFTER_LANDING", "Probe heat: AUTO", "Probe_Heat_Switch_1_Status", v => v < 0.5,
                new[] { "Probe_Heat_Switch_2_Status" }, (e, _) => e.SetProbeHeat(IFly737ActionExecutor.ProbeHeatAuto)),
            AutoAsync("AL_APU", "AFTER_LANDING", "APU: ON line", "APU_Switch_Status", v => v > 0.5,
                (e, _) => e.StartApuAsync()),
            Auto("AL_START_OFF", "AFTER_LANDING", "Engine start switches: OFF", "Engine_Start_Switch_Status_0",
                v => v > 0.5 && v < 1.5, new[] { "Engine_Start_Switch_Status_1" },
                (e, _) => { e.SetEngStartSelector1(IFly737ActionExecutor.EngStartOff);
                            e.SetEngStartSelector2(IFly737ActionExecutor.EngStartOff); }),
            // FLAP_Status: 0 Lever UP..8 Lever 40. No typed wrapper exists (see PF_YD) — the
            // varKey IS registered/writable (ForwardPedestal.cs), so this uses the generic Set().
            Auto("AL_FLAPS", "AFTER_LANDING", "Flaps: UP", "FLAP_Status", v => v < 0.5,
                (e, _) => e.Set("FLAP_Status", 0)),
            Auto("AL_AB", "AFTER_LANDING", "Autobrake: OFF", "Autobrake_Selector_Status", v => v > 0.5 && v < 1.5,
                (e, _) => e.SetAutobrake(IFly737ActionExecutor.AutobrakeOff)),
        }
    };

    private static Group BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            ActionManual("SD_APUGEN", "SHUTDOWN", "APU generators: ON",
                (e, _) => { e.PressApuGenerator(1, true); e.PressApuGenerator(2, true); }),
            // Engine_Start_Lever_Status_{0,1} < 3 = Cutoff (0-2 composite range) — reads the
            // lever position directly rather than a separate valve-closed annunciator (the
            // PMDG NG3 struct has no lever-position field, only the annunciator).
            Auto("SD_LEVERS", "SHUTDOWN", "Engine start levers: CUTOFF", "Engine_Start_Lever_Status_0", v => v < 3,
                new[] { "Engine_Start_Lever_Status_1" }, (e, _) => { e.SetFuelControl1(0); e.SetFuelControl2(0); }),
            Auto("SD_BELTS", "SHUTDOWN", "Seatbelt signs: OFF", "Fasten_Belts_Switch_Status", v => v < 0.5,
                (e, _) => e.SetSeatBelts(IFly737ActionExecutor.SignOff)),
            Auto("SD_TURNOFF", "SHUTDOWN", "Runway turnoff lights: OFF", "Runway_Turnoff_Light_1_Switch_Status",
                v => v < 0.5, new[] { "Runway_Turnoff_Light_2_Switch_Status" }, (e, _) => e.SetRunwayTurnoff(0)),
            Auto("SD_TAXI", "SHUTDOWN", "Taxi light: OFF", "Taxi_Light_Switch_Status", v => v < 0.5,
                (e, _) => e.SetTaxiLights(0)),
            Auto("SD_LOGO", "SHUTDOWN", "Logo lights: OFF", "Logo_Light_Switch_Status", v => v < 0.5,
                (e, _) => e.SetLogo(0)),
            Auto("SD_APUBLEED", "SHUTDOWN", "APU bleed air: ON", "APU_Bleed_Air_Switch_Status", v => v > 0.5,
                (e, _) => e.SetApuBleed(1)),
            // Ordering REQUIRED: wing-off before centre-off, so the centre pump's falling edge
            // sees wing already off (no spurious manual-off latch — CLAUDE.md center-pump
            // ordering invariant). This lambda is synchronous and calls SetWingFuelPumps then
            // SetCenterFuelPumps WITHOUT awaiting either Task; the guarantee holds only because
            // SemaphoreSlim.WaitAsync registers its waiters in call order even when the
            // returned Task is never awaited, so the two MultiAsync dispatches queue onto
            // _gate in this exact order and DispatchCoreAsync serializes them accordingly.
            // Converting this lambda to async-with-awaits, or swapping the two lines, breaks
            // the ordering SILENTLY (both calls still "succeed", just interleaved/reordered).
            Auto("SD_FUEL", "SHUTDOWN", "Fuel pumps: OFF", "Fuel_L_AFT_Switch_Status", v => v < 0.5,
                new[] { "Fuel_L_FWD_Switch_Status", "Fuel_R_FWD_Switch_Status", "Fuel_R_AFT_Switch_Status",
                        "Fuel_CENTER_L_Switch_Status", "Fuel_CENTER_R_Switch_Status" },
                (e, _) => { e.SetWingFuelPumps(0); e.SetCenterFuelPumps(0); }),
            Auto("SD_EAI", "SHUTDOWN", "Engine anti-ice: OFF", "Eng_1_AntiIce_Switch_Status", v => v < 0.5,
                new[] { "Eng_2_AntiIce_Switch_Status" }, (e, _) => e.SetEngAntiIce(0)),
            Auto("SD_HYDELEC", "SHUTDOWN", "Electric hydraulic pumps: OFF", "ELEC_1_HYD_Switch_Status", v => v < 0.5,
                new[] { "ELEC_2_HYD_Switch_Status" }, (e, _) => e.SetElecHydPumps(0)),
            Auto("SD_HYDENG", "SHUTDOWN", "Engine hydraulic pumps: OFF", "ENG_1_HYD_Switch_Status", v => v < 0.5,
                new[] { "ENG_2_HYD_Switch_Status" }, (e, _) => e.SetEngHydPumps(0)),
            Auto("SD_WINHEAT", "SHUTDOWN", "Window heat: OFF", "Window_Heat_Switch_1_Status", v => v < 0.5,
                new[] { "Window_Heat_Switch_2_Status", "Window_Heat_Switch_3_Status", "Window_Heat_Switch_4_Status" },
                (e, _) => e.SetWindowHeat(0)),
            // See PF_XPDR — this airframe's resting position is ALT OFF, not STBY.
            Auto("SD_XPDR", "SHUTDOWN", "Transponder: ALT OFF", "Transponder_Mode_Switch_Status", v => v < 0.5,
                (e, _) => e.SetTransponderMode(IFly737ActionExecutor.XpdrAltOff)),
        }
    };

    private static Group BuildSecure() => new()
    {
        Id = "SECURE", Name = "Secure",
        Items = new()
        {
            Auto("SE_IRS", "SECURE", "IRS mode selectors: OFF", "IRS_Mode_Switch_Status_0", v => v < 0.5,
                new[] { "IRS_Mode_Switch_Status_1" }, (e, _) => e.SetIrsMode(IFly737ActionExecutor.IrsOff)),
            // Emergency_Light_Switch_Status: 0 Guard closed/1 Off/2 Armed/3 On — see PF_EMER: a
            // closed guard physically holds the real switch at ARMED, so guard-closed (0) must
            // read as ARMED, never OFF (the previous `< 1.5` here wrongly counted 0 as OFF,
            // disagreeing with PF_EMER's ARMED test on the very same reading). OFF detection
            // therefore accepts ONLY the explicit Off position (1).
            Auto("SE_EMER", "SECURE", "Emergency exit lights: OFF", "Emergency_Light_Switch_Status",
                v => v > 0.5 && v < 1.5, (e, _) => e.SetEmerExitLights(IFly737ActionExecutor.EmerExitOff)),
            Auto("SE_WINHEAT", "SECURE", "Window heat: OFF", "Window_Heat_Switch_1_Status", v => v < 0.5,
                new[] { "Window_Heat_Switch_2_Status", "Window_Heat_Switch_3_Status", "Window_Heat_Switch_4_Status" },
                (e, _) => e.SetWindowHeat(0)),
            Auto("SE_PACKS", "SECURE", "Packs: OFF", "Pack_Switch_Status_0", v => v < 0.5,
                new[] { "Pack_Switch_Status_1" }, (e, _) => e.SetPacks(IFly737ActionExecutor.PackOff)),
            Auto("SE_APU_OFF", "SECURE", "APU: OFF", "APU_Switch_Status", v => v < 0.5,
                (e, _) => e.SetApuSelector(IFly737ActionExecutor.ApuOff)),
            // No reliable "GPU on bus" readback exists (same gap as the PMDG) — unlike the
            // PMDG, IFly737StateEvaluator exposes no IsGpuOn() guard at all, so this is an
            // unconditional stateless press, matching the ON item's shape (deviation: loses
            // the PMDG's "no-op when no GPU present" guard).
            ActionManual("SE_GND_PWR_OFF", "SECURE", "Ground power: OFF",
                (e, _) => e.PressGroundPower(false)),
        }
    };

    private static Group BuildElectricalPowerDown() => new()
    {
        Id = "ELEC_POWER_DOWN", Name = "Electrical Power Down",
        Items = new()
        {
            Auto("EPD_BAT", "ELEC_POWER_DOWN", "Battery: OFF", "Battery_Switch_Mode", v => v < 1.5,
                (e, _) => e.SetBattery(false)),
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
            Auto("PFC_WINHEAT", "PREFLIGHT_CL", "Window heat: ON", "Window_Heat_Switch_1_Status", v => v > 0.5,
                new[] { "Window_Heat_Switch_2_Status", "Window_Heat_Switch_3_Status", "Window_Heat_Switch_4_Status" },
                action: null),
            // Pressurization_Mode_Selector_Status: 0 Auto/1 Altn/2 Man.
            Auto("PFC_PRESS", "PREFLIGHT_CL", "Pressurization mode selector: AUTO",
                "Pressurization_Mode_Selector_Status", v => v < 0.5, action: null),
            Reminder("PFC_INST", "PREFLIGHT_CL", "Flight instruments: heading and altimeter checked"),
            Auto("PFC_PARK", "PREFLIGHT_CL", "Parking brake: SET", "Parking_Brake_Lever_Status", v => v > 0.5,
                action: null),
            Auto("PFC_LEVERS", "PREFLIGHT_CL", "Engine start levers: CUTOFF", "Engine_Start_Lever_Status_0",
                v => v < 3, new[] { "Engine_Start_Lever_Status_1" }, action: null),
        }
    };

    private static Group BuildBeforeStartChecklist() => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = new()
        {
            Reminder("BSC_DOORS", "BEFORE_START_CL", "Flight deck door: closed and locked"),
            Reminder("BSC_FUEL", "BEFORE_START_CL", "Fuel: quantity checked, pumps ON"),
            Auto("BSC_BELTS", "BEFORE_START_CL", "Passenger signs: ON", "Fasten_Belts_Switch_Status", v => v > 1.5,
                action: null),
            Reminder("BSC_WINDOWS", "BEFORE_START_CL", "Windows: locked"),
            Reminder("BSC_MCP", "BEFORE_START_CL", "MCP: speed, heading and altitude set"),
            Reminder("BSC_SPEEDS", "BEFORE_START_CL", "Takeoff speeds: V1, VR and V2 checked"),
            Reminder("BSC_CDU", "BEFORE_START_CL", "CDU preflight: complete"),
            Reminder("BSC_TRIM", "BEFORE_START_CL", "Rudder and aileron trim: free and zero"),
            Auto("BSC_ANTICOL", "BEFORE_START_CL", "Anti-collision light: ON", "Anti_Collision_Light_Switch_Status",
                v => v > 0.5, action: null),
        }
    };

    private static Group BuildBeforeTaxiChecklist() => new()
    {
        Id = "BEFORE_TAXI_CL", Name = "Before Taxi Checklist",
        Items = new()
        {
            Auto("BTC_GEN", "BEFORE_TAXI_CL", "Generators: ON", "ENG_GEN_OFF_BUS_Light_Status_0", v => v < 0.5,
                new[] { "ENG_GEN_OFF_BUS_Light_Status_1" }, action: null),
            Auto("BTC_PROBE", "BEFORE_TAXI_CL", "Probe heat: ON", "Probe_Heat_Switch_1_Status", v => v > 0.5,
                new[] { "Probe_Heat_Switch_2_Status" }, action: null),
            Reminder("BTC_ANTIICE", "BEFORE_TAXI_CL", "Anti-ice: as required"),
            Auto("BTC_ISO", "BEFORE_TAXI_CL", "Isolation valve: AUTO", "Isolation_Valve_Switch_Status",
                v => v > 0.5 && v < 1.5, action: null),
            Auto("BTC_START", "BEFORE_TAXI_CL", "Engine start switches: CONT", "Engine_Start_Switch_Status_0",
                v => v > 1.5 && v < 2.5, new[] { "Engine_Start_Switch_Status_1" }, action: null),
            Reminder("BTC_RECALL", "BEFORE_TAXI_CL", "Recall: checked"),
            Auto("BTC_AB", "BEFORE_TAXI_CL", "Autobrake: RTO", "Autobrake_Selector_Status", v => v < 0.5, action: null),
            Reminder("BTC_FCTL", "BEFORE_TAXI_CL", "Flight controls: checked"),
            Reminder("BTC_GND", "BEFORE_TAXI_CL", "Ground equipment: clear"),
        }
    };

    private static Group BuildBeforeTakeoffChecklist() => new()
    {
        Id = "BEFORE_TAKEOFF_CL", Name = "Before Takeoff Checklist",
        Items = new()
        {
            Reminder("BTOC_FLAPS", "BEFORE_TAKEOFF_CL", "Flaps: set for takeoff"),
            Reminder("BTOC_TRIM", "BEFORE_TAKEOFF_CL", "Stabilizer trim: units checked"),
            // ADDED (no PMDG 737 precedent — see class doc): confirms altimeters are still on
            // QNH (not standard) immediately before takeoff.
            //
            // Was an Auto detecting BARO_STD_Status_{0,1} < 0.5. That detection is WORTHLESS:
            // BARO_STD_Status is MOMENTARY ("0:switch released / 1:switch pressed" in v1.5
            // SDK_Defines.h, matching iFly's other momentary press-buttons; no persistent
            // STD-mode variable exists in the model XML) — so it reads 0 essentially always
            // and this item auto-ticked "Baro STD: QNH" whether or not the altimeters were on
            // standard. A confirmation that is always true is worse than none for a blind
            // pilot. There is no substitute readback (the stock ALTIMETER_SETTING keeps
            // reporting the underlying QNH while STD is displayed), so it becomes a reminder
            // the pilot answers from the altimeter readout (Ctrl+B).
            Reminder("BTOC_BARO", "BEFORE_TAKEOFF_CL", "Baro reference: QNH set, not standard"),
        }
    };

    private static Group BuildAfterTakeoffChecklist() => new()
    {
        Id = "AFTER_TAKEOFF_CL", Name = "After Takeoff Checklist",
        Items = new()
        {
            Auto("ATC_BLEEDS", "AFTER_TAKEOFF_CL", "Engine bleeds: ON", "Engine_Bleed_Air_Switch_Status_0",
                v => v > 0.5, new[] { "Engine_Bleed_Air_Switch_Status_1" }, action: null),
            Auto("ATC_PACKS", "AFTER_TAKEOFF_CL", "Packs: AUTO", "Pack_Switch_Status_0", v => v > 0.5 && v < 1.5,
                new[] { "Pack_Switch_Status_1" }, action: null),
            // No OFF detent exists (Gear_Lever_Status is 0 Up/1 Down only) — see ATKO_GEAR_OFF.
            Auto("ATC_GEAR", "AFTER_TAKEOFF_CL", "Landing gear: UP", "Gear_Lever_Status", v => v < 0.5,
                action: null),
            Reminder("ATC_FLAPS", "AFTER_TAKEOFF_CL", "Flaps: UP, no lights"),
        }
    };

    private static Group BuildDescentChecklist() => new()
    {
        Id = "DESCENT_CL", Name = "Descent Checklist",
        Items = new()
        {
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
            Auto("LDC_START", "LANDING_CL", "Engine start switches: CONT", "Engine_Start_Switch_Status_0",
                v => v > 1.5 && v < 2.5, new[] { "Engine_Start_Switch_Status_1" }, action: null),
            // Unlike the PMDG (no state field at all), the iFly HAS a readback —
            // SPEED_BRAKE_ARMED_Light_Status (0 Off / nonzero DIM|BRIGHT = armed) — so this
            // upgrades from a reminder to a live auto-detect.
            Auto("LDC_SPDBRK", "LANDING_CL", "Speedbrake: ARMED", "SPEED_BRAKE_ARMED_Light_Status", v => v > 0.5,
                action: null),
            Auto("LDC_GEAR", "LANDING_CL", "Landing gear: DOWN", "Gear_Lever_Status", v => v > 0.5, action: null),
            Reminder("LDC_FLAPS", "LANDING_CL", "Flaps: set for landing"),
        }
    };

    private static Group BuildShutdownChecklist() => new()
    {
        Id = "SHUTDOWN_CL", Name = "Shutdown Checklist",
        Items = new()
        {
            Auto("SDC_FUEL", "SHUTDOWN_CL", "Fuel pumps: OFF", "Fuel_L_AFT_Switch_Status", v => v < 0.5,
                new[] { "Fuel_L_FWD_Switch_Status", "Fuel_R_FWD_Switch_Status", "Fuel_R_AFT_Switch_Status",
                        "Fuel_CENTER_L_Switch_Status", "Fuel_CENTER_R_Switch_Status" }, action: null),
            // No OFF position on this switch — see PF_PROBE_OFF.
            Auto("SDC_PROBE", "SHUTDOWN_CL", "Probe heat: AUTO", "Probe_Heat_Switch_1_Status", v => v < 0.5,
                new[] { "Probe_Heat_Switch_2_Status" }, action: null),
            Reminder("SDC_HYD", "SHUTDOWN_CL", "Hydraulic panel: set"),
            Reminder("SDC_FLAPS", "SHUTDOWN_CL", "Flaps: UP"),
            Auto("SDC_PARK", "SHUTDOWN_CL", "Parking brake: as required", "Parking_Brake_Lever_Status", v => v > 0.5,
                action: null),
            Auto("SDC_LEVERS", "SHUTDOWN_CL", "Engine start levers: CUTOFF", "Engine_Start_Lever_Status_0",
                v => v < 3, new[] { "Engine_Start_Lever_Status_1" }, action: null),
        }
    };

    private static Group BuildSecureChecklist() => new()
    {
        Id = "SECURE_CL", Name = "Secure Checklist",
        Items = new()
        {
            Auto("SEC_IRS", "SECURE_CL", "IRS: OFF", "IRS_Mode_Switch_Status_0", v => v < 0.5,
                new[] { "IRS_Mode_Switch_Status_1" }, action: null),
            // See SE_EMER — OFF must accept only the explicit Off position (1), never the
            // guard-closed reading (0), which is really ARMED.
            Auto("SEC_EMER", "SECURE_CL", "Emergency exit lights: OFF", "Emergency_Light_Switch_Status",
                v => v > 0.5 && v < 1.5, action: null),
            Auto("SEC_WINHEAT", "SECURE_CL", "Window heat: OFF", "Window_Heat_Switch_1_Status", v => v < 0.5,
                new[] { "Window_Heat_Switch_2_Status", "Window_Heat_Switch_3_Status", "Window_Heat_Switch_4_Status" },
                action: null),
            Auto("SEC_PACKS", "SECURE_CL", "Packs: OFF", "Pack_Switch_Status_0", v => v < 0.5,
                new[] { "Pack_Switch_Status_1" }, action: null),
        }
    };

    // =======================================================================
    // Helpers (mirror PMDG737ChecklistDefinitions; CheckActions are async via
    // AsCheckAction). Every auto-detect item is RevertToState.
    // =======================================================================

    private static Func<IFly737ActionExecutor, IFly737StateEvaluator, Task>? AsCheckAction(Act? action)
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

    private static Item AutoAsync(string id, string groupId, string label,
        string field, Func<double, bool> condition,
        Func<IFly737ActionExecutor, IFly737StateEvaluator, Task> action) =>
        AutoAsync(id, groupId, label, field, condition, null, action);

    private static Item AutoAsync(string id, string groupId, string label,
        string field, Func<double, bool> condition, string[]? additionalFields,
        Func<IFly737ActionExecutor, IFly737StateEvaluator, Task> action) => new()
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

    private static Item ActionManualAsync(string id, string groupId, string label,
        Func<IFly737ActionExecutor, IFly737StateEvaluator, Task> action) => new()
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
}
