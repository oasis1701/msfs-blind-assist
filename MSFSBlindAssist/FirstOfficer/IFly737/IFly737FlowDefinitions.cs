using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.IFly737;

using Flow = Models.FlowDefinition<IFly737StateEvaluator>;
using Step = Models.FlowStep<IFly737StateEvaluator>;

/// <summary>
/// Data-driven iFly 737 MAX8 First-Officer flow definitions — a step-for-step port of
/// <see cref="MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions"/> (same airframe,
/// same 13 phases, same ids/RelatedChecklistGroupIds/step order/step text), with control
/// targets swapped per the adaptation table (.superpowers/sdd/control-mapping.md). Flow
/// steps write iFly def varKeys (or executor pseudo-keys) via
/// <see cref="IFly737ActionExecutor"/>, which delegates most writes to
/// <see cref="MSFSBlindAssist.Aircraft.IFly737MAXDefinition.ApplyUIVariable"/> — the same
/// verified panel-write path the iFly panels use.
///
/// <see cref="SW"/>'s <c>eventName</c> is a def varKey OR a declared executor pseudo-key
/// (<see cref="IFly737ActionExecutor.PseudoKeys"/>) — NOT a raw PMDG event-name string like
/// the template. There is no MouseFlag/Momentary-toggle-hazard helper here: this airframe's
/// flight directors and autothrottle arm are ABSOLUTE SET switches (FD_1/2_Switch_Status,
/// AT_Switch_Status), so a plain SW write with no skip guard is correct and safe to re-run.
/// <c>SW</c>'s <c>isMomentary</c> flag is metadata ONLY — <see cref="IFly737ActionExecutor.
/// ExecuteStepAsync"/> never reads <c>IFlowStepDispatch.IsMomentary</c>, so it has no effect
/// on dispatch. A genuinely momentary control (a click button, a spring-loaded switch) is
/// safe to write unconditionally because the DEFINITION's own write path handles the
/// press/click semantics (a Btn-registered key IS the click; a spring-loaded field's SET
/// command is a one-shot pulse) — nothing in this flow layer needs to hold or release it.
///
/// Value conventions (see IFly737ActionExecutor's per-method doc comments for the exact
/// registration line each cites):
/// - Battery_Switch_Mode 1=Off/2=On; STANDBY_POWER_Switch_Mode 1=Battery/2=Off/3=Auto (both
///   1-based, unlike the PMDG's 0-based equivalents).
/// - Packs 0=Off/1=Auto/2=High; Isolation valve 0=Close/1=Auto/2=Open (same numbering as PMDG).
/// - Engine start selector 0=Ground/1=Off/2=Continuous/3=Flight (same numbering as PMDG).
/// - Engine_Start_Lever_Status_{0,1} is a 0-5 switch+fire-light COMPOSITE: the combo position
///   is 0=Cutoff / 3=Idle (matching IFly737ActionExecutor.SetFuelControl1/2's private
///   StartLeverCutoff/StartLeverIdle consts, duplicated here since those are private to the
///   executor) — the def's own `map` lambda inverts this to the command's Value2 (0 IDLE / 1
///   CUTOFF), so a SW step here always sends the COMBO position, never the command value.
/// - Seatbelt/No-smoking signs 0=Off/1=Auto/2=On (same numbering as PMDG).
/// - Emergency exit lights 0=Guard closed/1=Off/2=Armed/3=On — NOT the PMDG's 0/1/2 numbering;
///   use IFly737ActionExecutor.EmerExit* constants, never a bare literal.
/// - Position lights 0=Steady/1=Off/2=Strobe&amp;Steady (same numbering as PMDG).
/// - Landing lights are a plain 0=Off/1=On STATUS on two switches (Landing_Light_1/2) — no
///   retractable/fixed split like the PMDG's four lights.
/// - EFIS (ND) mode 0=Approach/1=VOR/2=Map/3=Plan; ND range index 5=20nm, 6=40nm (0..10 scale).
/// - Autobrake 0=RTO/1=Off/2.."1"/3.."2"/4.."3"/5=Max Auto (same numbering as PMDG).
/// - Transponder is a 4-position ABSOLUTE SET (0 ALT OFF/1 XPNDR/2 TA Only/3 TA-RA) — no walked
///   rotary, unlike the PMDG NG3's CDA-deaf EVT_TCAS_MODE.
/// - Gear lever has only TWO positions (0 Up/1 Down) — no OFF detent like the PMDG's three.
/// - Probe heat has only Auto(0)/On(1) — no OFF detent like the PMDG's Off/On.
/// - IRS mode 0=Off/1=Align/2=Nav/3=Attitude (Nav=2 happens to match the PMDG's numbering).
///
/// Deviations from the PMDG 737 template beyond Task 5's list (full rationale in
/// task-6-report.md):
///  - Ground power ON's follow-on "wait for the buses" step has no iFly equivalent field (no
///    GPU/ground-power-availability readback exists on this SDK — confirmed by the checklist's
///    own SECURE group comment: "unlike the PMDG, IFly737StateEvaluator exposes no IsGpuOn()
///    guard at all") — downgraded to a Captain reminder rather than dropped outright.
///  - APU start collapses the PMDG's THREE steps (ON, dwell, START) into ONE — the
///    IFly737ActionExecutor.KeyApuStart pseudo-key already sequences ON → 2s dwell → START
///    internally (StartApuCoreAsync) — followed by a single WaitForField on
///    APU_GEN_OFF_BUS_Light_Status (lit = available), Stop policy, ~120s timeout (the PMDG's
///    two separate waits — starter-cutout then generator-availability — collapse into this one
///    generator-availability gate since iFly exposes no starter-cutout-equivalent "APU
///    selector reads ON" signal distinct from the switch position the pseudo-key already set).
///  - Ground power OFF (Before Start, Secure) and Ground power ON/OFF (Electrical Power Up,
///    Secure) lose the PMDG's Skip guard — no GPU-on readback exists to skip on (same gap as
///    above); the press is unconditional and idempotent, matching the checklist's own
///    ActionManual (no-skip) shape for these items.
///  - Engine Start drops the PMDG's early "start valve open" confirmation wait per engine — no
///    start-valve field exists in this SDK. The N2 gate (Stop policy) is what remains as the
///    fuel-introduction gate; the starter-cutout wait (start switch springing back out of GRD)
///    stays in its ORIGINAL position, AFTER the start lever moves to Idle, gating the NEXT
///    engine's GRD press — exactly like the PMDG's ES_E2_CUTOUT/ES_E1_CUTOUT.
///  - Fuel flow reset (PF_FF) DOES have an iFly equivalent (Fuel_Flow_Switch_Status, position
///    0=Reset, spring-loaded — Overhead.cs:553-555) — not a Captain reminder.
///  - Flight directors (PF_FD1/PF_FD2) and autothrottle arm (BTO_AT) are plain absolute SW
///    writes — no skip guard needed (see class doc).
///  - Speedbrake ARM (LD_SPDBRK) is a Captain reminder — Spoiler_Lever_Status is registered
///    read-only (Disp, ForwardPedestal.cs:822: "raw position (int 0-225) ... nearest-detent
///    decode" — no write command exists at all) and the executor deliberately exposes no write
///    for it (unverified scale mismatch).
///  - Weather radar test (PF_WXR_TEST) is a Captain reminder. A WXR TEST command DOES exist
///    (`FMS_WXR_SYS_CTRL_SET`, Value2 0 TEST/1 NORM, with the readable
///    `Weather_Radar_System_Control_Switch_Status`) — an earlier grep looked for a test CLICK
///    and wrongly concluded otherwise. It is deliberately unwired pending in-sim verification
///    that the TEST position is modelled at all (see IFly737ActionExecutor.PseudoKeys).
///  - Lower display unit (BT_LOWERDU) is a Captain reminder — no lower-DU/EICAS synoptic-page-
///    select field exists in IFlySdkFields.cs.
///  - Labels changed to match this airframe's own switch wording (Fix-pass-1 convention
///    already used by IFly737ChecklistDefinitions): "Probe heat: AUTO" (not OFF — no OFF
///    detent), "Transponder: ALT OFF" (not STBY — that position doesn't exist),
///    "Gear lever: UP" (not OFF — no OFF detent).
///  - Flaps UP (AL_FLAPS_UP) writes FLAP_Status directly to position 0 (Up) — a real absolute
///    combo position on this airframe (ForwardPedestal.cs:815-817), not a per-detent momentary
///    click event like the PMDG's flap lever.
///  - Pressurization (PF_PRESS_ALTS) routes through the executor's KeyPressAlts pseudo-key
///    (SetPressurizationAltitudesCoreAsync) — the SAME sanctioned bypass the CHECKLIST's
///    PF_PRESS auto-detect item uses, NOT the definition's PRESS_FLT_ALT_SET/PRESS_LDG_ALT_SET
///    NumSet keys. Fix pass 1 (2026-08) correction: this flow originally targeted those NumSet
///    keys directly via a DynSW+TargetValueProvider step (on the premise that a FLOW step
///    dispatches exactly once, so the checklist's "re-invoked every tick" repeat-announce
///    hazard doesn't apply here) — but HandleUIVariableSet's NumSet branch fires its numeric-
///    entry confirmation via announcer.AnnounceImmediate, which is unconditionally
///    interrupt:true with NO Suppressed check (ScreenReaderAnnouncer.AnnounceImmediate), so it
///    cuts off the step's OWN "Flight and landing altitudes: set" narration mid-word on every
///    run regardless of repeat count. The bypass avoids that entirely: it sends
///    AIRSYSTEM_FLT_ALT_SET/AIRSYSTEM_LDG_ALT_SET straight to the SDK client with no announce.
/// </summary>
public static class IFly737FlowDefinitions
{
    // N2 (percent) the engine must reach while motoring before the start lever introduces
    // fuel — shared with the checklist's Engine Start group (see the evaluator const).
    private const double EngStartFuelN2 = IFly737StateEvaluator.EngStartFuelN2;

    // Engine_Start_Lever_Status_{0,1} combo positions (Overhead.cs:493-505). Duplicated from
    // IFly737ActionExecutor's PRIVATE StartLeverCutoff/StartLeverIdle consts — a flow step
    // dispatches the raw varKey+value directly (bypassing the executor's typed
    // SetFuelControl1/2 wrapper), so it must send the same COMBO position that wrapper does.
    private const int StartLeverCutoff = 0;
    private const int StartLeverIdle = 3;

    public static List<Flow> Build() => new()
    {
        BuildElectricalPowerUp(),
        BuildPreflight(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildBeforeTaxi(),
        BuildBeforeTakeoff(),
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
        Description = "Cold-and-dark electrical power up: battery, ground power, standby power, IRS to NAV.",
        RelatedChecklistGroupIds = new[] { "ELEC_POWER_UP" },
        Steps = new()
        {
            // Battery_Switch_Mode: 1=Off/2=On (guarded switch; ApplyUIVariable special-cases
            // the write and verifies it moved — see IFly737ActionExecutor.SetBattery).
            Skip(SW("EPU_BAT", "Battery: ON", "Battery_Switch_Mode", 2),
                s => s.IsPosition("Battery_Switch_Mode", 2)),
            // STANDBY_POWER_Switch_Mode: 1=Battery/2=Off/3=Auto.
            Skip(SW("EPU_STBY", "Standby power: AUTO", "STANDBY_POWER_Switch_Mode",
                    IFly737ActionExecutor.StandbyPowerAuto),
                s => s.IsPosition("STANDBY_POWER_Switch_Mode", IFly737ActionExecutor.StandbyPowerAuto)),
            // Ground power ON: momentary click (BTN_GRD_PWR_ON — the _SET command only moves
            // the animation, live-verified dead). Pressed unconditionally like the PMDG
            // template — no reliable "on bus" signal exists to skip on (see class doc).
            SW("EPU_GPU", "Ground power: ON", "BTN_GRD_PWR_ON", 1, isMomentary: true),
            // No GPU/ground-power-availability readback exists on this SDK — downgraded from
            // the PMDG's automated WaitForField to a Captain reminder (see class doc).
            Captain("EPU_GPU_WAIT", "Confirm ground power connected",
                "Confirm ground power is connected — this aircraft has no automatic way to " +
                "verify it."),
            // IRS_Mode_Switch_Status_{0,1}: 0 Off/1 Align/2 Nav/3 Attitude. Alignment runs in
            // the background; no wait — "IRS aligned" auto-detects later.
            Multi("EPU_IRS", "IRS mode selectors: NAV",
                ("IRS_Mode_Switch_Status_0", IFly737ActionExecutor.IrsNav),
                ("IRS_Mode_Switch_Status_1", IFly737ActionExecutor.IrsNav)),
        }
    };

    // -----------------------------------------------------------------------
    // 2. Preflight (overhead / MIP / pedestal setup)
    // -----------------------------------------------------------------------
    private static Flow BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "Full overhead, MIP and pedestal preflight setup.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT" },
        Steps = new()
        {
            // Fire + warning tests (held self-completing tests via executor pseudo-keys —
            // the fire bell / stick shaker / overspeed clacker are the blind-pilot
            // verification, PMDG 737 parity).
            SW("PF_FIRE_TEST", "Fire warning test — listen for the fire bell",
                IFly737ActionExecutor.KeyFireTest, 1, checklistItemId: "PF_FIRE_TEST"),
            SW("PF_STALL_TEST1", "Stall warning test 1 — listen for the stick shaker",
                IFly737ActionExecutor.KeyStallTest1, 1, checklistItemId: "PF_STALL_TEST1"),
            SW("PF_STALL_TEST2", "Stall warning test 2 — listen for the stick shaker",
                IFly737ActionExecutor.KeyStallTest2, 1, checklistItemId: "PF_STALL_TEST2"),
            SW("PF_OVSPD_TEST1", "Overspeed warning test 1 — listen for the clacker",
                IFly737ActionExecutor.KeyOverspeedTest1, 1, checklistItemId: "PF_OVSPD_TEST1"),
            SW("PF_OVSPD_TEST2", "Overspeed warning test 2 — listen for the clacker",
                IFly737ActionExecutor.KeyOverspeedTest2, 1, checklistItemId: "PF_OVSPD_TEST2"),
            // TCAS test stays here with the other warning tests (its "TEST PASS" callout plays
            // several seconds after the press; GPWS is deliberately LAST so the two test
            // audios never overlap — the ~15 preflight steps in between give TCAS's callout
            // room to finish).
            SW("PF_TCAS_TEST", "TCAS test — listen for TCAS test pass",
                IFly737ActionExecutor.KeyTcasTest, 1, checklistItemId: "PF_TCAS_TEST"),
            // Yaw_Damper_Switch_Status: 0 Off/1 On. No typed executor wrapper exists — the
            // varKey IS registered/writable, so this uses the executor's generic Set() path.
            SW("PF_YD", "Yaw damper: ON", "Yaw_Damper_Switch_Status", 1),
            Multi("PF_FUEL_OFF", "Fuel pumps: OFF",
                ("Fuel_L_AFT_Switch_Status", 0), ("Fuel_L_FWD_Switch_Status", 0),
                ("Fuel_R_FWD_Switch_Status", 0), ("Fuel_R_AFT_Switch_Status", 0),
                ("Fuel_CENTER_L_Switch_Status", 0), ("Fuel_CENTER_R_Switch_Status", 0)),
            // Emergency_Light_Switch_Status: 0 Guard closed/1 Off/2 Armed/3 On — NOT the
            // PMDG's numbering; use the executor's EmerExitArmed constant.
            SW("PF_EMER", "Emergency exit lights: ARMED", "Emergency_Light_Switch_Status",
                IFly737ActionExecutor.EmerExitArmed),
            // Fasten_Belts_Switch_Status: 0 Off/1 Auto/2 On — same numbering as the PMDG.
            SW("PF_BELTS", "Seatbelt signs: ON", "Fasten_Belts_Switch_Status",
                IFly737ActionExecutor.SignOn),
            Multi("PF_WINHEAT", "Window heat: ON",
                ("Window_Heat_Switch_1_Status", 1), ("Window_Heat_Switch_2_Status", 1),
                ("Window_Heat_Switch_3_Status", 1), ("Window_Heat_Switch_4_Status", 1)),
            // No OFF position exists on this switch — only Auto(0)/On(1). Label says AUTO,
            // matching the switch's own wording (see class doc).
            Multi("PF_PROBE_OFF", "Probe heat: AUTO",
                ("Probe_Heat_Switch_1_Status", IFly737ActionExecutor.ProbeHeatAuto),
                ("Probe_Heat_Switch_2_Status", IFly737ActionExecutor.ProbeHeatAuto)),
            SW("PF_WAI_OFF", "Wing anti-ice: OFF", "Wing_AntiIce_Switch_Status", 0),
            Multi("PF_EAI_OFF", "Engine anti-ice: OFF",
                ("Eng_1_AntiIce_Switch_Status", 0), ("Eng_2_AntiIce_Switch_Status", 0)),
            // RecircFan_Switch_Status_{0,1}: 0 Off/1 Auto (the ON position is labelled AUTO).
            Multi("PF_RECIRC", "Recirculation fans: AUTO",
                ("RecircFan_Switch_Status_0", 1), ("RecircFan_Switch_Status_1", 1)),
            Multi("PF_PACKS", "Packs: AUTO",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackAuto),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackAuto)),
            SW("PF_ISO", "Isolation valve: OPEN", "Isolation_Valve_Switch_Status",
                IFly737ActionExecutor.IsolationValveOpen),
            Multi("PF_BLEEDS", "Engine bleeds: ON",
                ("Engine_Bleed_Air_Switch_Status_0", 1), ("Engine_Bleed_Air_Switch_Status_1", 1)),
            // Pressurization FLT/LAND ALT from the SimBrief plan — routed through the
            // executor's PRESS_ALTS pseudo-key (SetPressurizationAltitudesCoreAsync), the SAME
            // sanctioned bypass the checklist's PF_PRESS auto-detect item uses. Fix pass 1
            // (2026-08), Fix 1: this used to target the definition's PRESS_FLT_ALT_SET /
            // PRESS_LDG_ALT_SET NumSet keys directly — those fire an unsuppressible
            // announcer.AnnounceImmediate numeric-entry confirmation from inside
            // HandleUIVariableSet (interrupt: true, no Suppressed check), which talks over
            // this step's own "Flight and landing altitudes: set" narration mid-word every
            // run. PRESS_ALTS reads the plan straight off the wired state evaluator and sends
            // AIRSYSTEM_FLT_ALT_SET/AIRSYSTEM_LDG_ALT_SET directly (no announce) — a single
            // step covers both altitudes; a quiet no-op (no write, no announce) when no plan
            // is loaded, same as before. The Captain fallback below still covers that case.
            SW("PF_PRESS_ALTS", "Flight and landing altitudes: set", IFly737ActionExecutor.KeyPressAlts, 1),
            Skip(Captain("PF_PRESS", "Flight and landing altitudes",
                    "Set flight and landing altitudes on the pressurization panel."),
                s => s.HasPressurizationPlan),
            SW("PF_LOGO", "Logo lights: ON", "Logo_Light_Switch_Status", 1),
            // Flight directors: absolute SETs, no toggle-guard skip predicate needed.
            SW("PF_FD1", "Flight director 1: ON", "FD_1_Switch_Status", 1),
            SW("PF_FD2", "Flight director 2: ON", "FD_2_Switch_Status", 1),
            // Fuel_Flow_Switch_Status: 0 Reset/1 Rate/2 Used (spring-loaded RESET).
            SW("PF_FF", "Fuel flow: RESET", "Fuel_Flow_Switch_Status", 0, isMomentary: true),
            SW("PF_AB_RTO", "Autobrake: RTO", "Autobrake_Selector_Status",
                IFly737ActionExecutor.AutobrakeRto),
            // Transponder_Mode_Switch_Status: 0 ALT OFF/1 XPNDR/2 TA Only/3 TA-RA — this
            // airframe's resting/ground position is ALT OFF, not STBY.
            SW("PF_XPDR", "Transponder: ALT OFF", "Transponder_Mode_Switch_Status",
                IFly737ActionExecutor.XpdrAltOff),
            // ND_Mode_Status_0: 0 Approach/1 VOR/2 Map/3 Plan.
            SW("PF_EFIS_MODE", "EFIS mode: MAP", "ND_Mode_Status_0", IFly737ActionExecutor.NdModeMap),
            // ND_Range_Status_0: 0..10 = 0.5/1/2/5/10/20/40/80/160/320/640 nm; 40 nm = index 6.
            SW("PF_EFIS_RANGE", "EFIS range: 40", "ND_Range_Status_0", IFly737ActionExecutor.NdRange40Nm),
            Captain("PF_ALT", "Set the altimeters to the local QNH."),
            // FMS_WXR_SYS_CTRL_SET (0 TEST/1 NORM) exists but is deliberately unwired pending
            // in-sim verification that the TEST position is modelled — see the class doc.
            Captain("PF_WXR_TEST", "Weather radar test"),
            // GPWS test goes LAST — its self-test callouts trail for several seconds after the
            // step completes, so nothing must follow it (PMDG 737 parity).
            SW("PF_GPWS_TEST", "GPWS system test — listen for the self-test callouts",
                IFly737ActionExecutor.KeyGpwsTest, 1, checklistItemId: "PF_GPWS_TEST"),
        }
    };

    // -----------------------------------------------------------------------
    // 3. Before Start
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "APU start, fuel pumps, hydraulics, anti-collision, MCP set, transponder TA/RA.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START" },
        Steps = new()
        {
            Captain("BS_MCP", "Set MCP airspeed, heading and initial altitude."),
            // APU start: the APU_START pseudo-key sequences ON -> 2s dwell -> START internally
            // (IFly737ActionExecutor.StartApuCoreAsync) — a single step replaces the PMDG's
            // three (ON, dwell, START).
            SW("BS_APU_START", "APU: START", IFly737ActionExecutor.KeyApuStart, 1, isMomentary: true),
            // Generator availability: APU_GEN_OFF_BUS_Light_Status lit = the APU generator is
            // up and able to take a bus. Stop policy — if the APU never comes on line, abort
            // the flow HERE, before the generator transfer and ground-power drop below.
            //
            // Fix pass 1 (2026-08), Fix 4 — documented, NOT fixed: the PMDG 737 template
            // splits this into a starter-cutout wait (Stop, safe to re-run — the light is
            // absent on a cold APU too) and a SEPARATE generator-availability wait (Skip, with
            // a comment explaining "light off" is ambiguous between APU-not-up-yet and
            // already-transferred-to-the-bus). This port merged the two into the one wait
            // above and kept it Stop — correct for the cold-start case this flow is written
            // for, and the mandated behaviour — but that means a RE-RUN of Before Start after
            // the generator transfer has already completed (APU on the bus, ground power
            // already dropped) will sit here for the full 120 s timeout reading a light that
            // may never re-illuminate the way this wait expects, then abort: every later step
            // in this flow (fuel pumps, hydraulics, anti-collision light, transponder) never
            // runs. APU_Generator_Switch_Status (IFlySdkFields.cs:341) looks like a candidate
            // "already on the bus, skip this wait" guard, but it is UNVERIFIED against a live
            // sim and must not be added without an in-sim test first.
            WaitForField("BS_APU_WAIT", "Waiting for the APU to come on line",
                "APU_GEN_OFF_BUS_Light_Status", v => v > 0.5, 120,
                onTimeout: FlowStepFailurePolicy.Stop),
            // Transfer the electrical load to the APU: momentary bus-transfer buttons.
            Multi("BS_APUGEN", "APU generators: ON",
                ("BTN_APU_GEN_1_ON", 1), ("BTN_APU_GEN_2_ON", 1)),
            // Ground power OFF: no GPU-on readback exists to skip on (see class doc) — pressed
            // unconditionally, matching the checklist's own action-only shape for this item.
            SW("BS_GPU_OFF", "Ground power: OFF", "BTN_GRD_PWR_OFF", 1, isMomentary: true),
            Multi("BS_FUELON", "Fuel pumps: ON",
                ("Fuel_L_AFT_Switch_Status", 1), ("Fuel_L_FWD_Switch_Status", 1),
                ("Fuel_R_FWD_Switch_Status", 1), ("Fuel_R_AFT_Switch_Status", 1),
                ("Fuel_CENTER_L_Switch_Status", 1), ("Fuel_CENTER_R_Switch_Status", 1)),
            Multi("BS_HYDENG", "Engine hydraulic pumps: ON",
                ("ENG_1_HYD_Switch_Status", 1), ("ENG_2_HYD_Switch_Status", 1)),
            Multi("BS_HYD", "Electric hydraulic pumps: ON",
                ("ELEC_1_HYD_Switch_Status", 1), ("ELEC_2_HYD_Switch_Status", 1)),
            SW("BS_APUBLEED", "APU bleed air: ON", "APU_Bleed_Air_Switch_Status", 1),
            SW("BS_ANTICOL", "Anti-collision light: ON", "Anti_Collision_Light_Switch_Status", 1),
            // Transponder_Mode_Switch_Status TA/RA = 3 (max position — 4 positions total,
            // not the PMDG's 5).
            SW("BS_XPDR", "Transponder: TA/RA", "Transponder_Mode_Switch_Status",
                IFly737ActionExecutor.XpdrTaRa),
            Captain("BS_GND", "Confirm ground power and chocks removed, doors closed."),
            Captain("BS_ACARS", "Start ACARS"),
            Captain("BS_CLEARANCE", "Obtain pushback and start clearance"),
        }
    };

    // -----------------------------------------------------------------------
    // 4. Engine Start
    // -----------------------------------------------------------------------
    private static Flow BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Description = "Starts engines 2 then 1, waiting for each start switch to cut out before continuing.",
        RelatedChecklistGroupIds = new[] { "ENGINE_START" },
        Steps = new()
        {
            // Starter air insurance: the start NEEDS bleed pressure (normally the APU).
            Skip(SW("ES_APUBLEED", "APU bleed air: ON", "APU_Bleed_Air_Switch_Status", 1),
                s => s.IsOn("APU_Bleed_Air_Switch_Status")),
            Multi("ES_PACKS_OFF", "Packs: OFF",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackOff),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackOff)),
            // --- Engine 2 ---
            SW("ES_E2_GRD", "Engine 2 start switch: GRD", "Engine_Start_Switch_Status_1",
                IFly737ActionExecutor.EngStartGround),
            // No start-valve field exists on this SDK (see class doc) — the early
            // starter-engaged confirmation the PMDG has is dropped; N2 is the only gate before
            // fuel introduction. On timeout (starter/bleed failure — N2 never builds) ABORT
            // the flow rather than introduce fuel into an under-rotating engine.
            WaitForField("ES_E2_N2", "Engine 2 motoring — waiting for N2 before introducing fuel",
                "FO_ENG2_N2", v => v >= EngStartFuelN2, 60, onTimeout: FlowStepFailurePolicy.Stop),
            SW("ES_E2_RUN", "Engine 2 start lever: IDLE", "Engine_Start_Lever_Status_1", StartLeverIdle),
            // Starter cutout: the start switch springs back out of GRD as the starter
            // disengages. Wait on the switch itself, gating engine 1's GRD press so it's never
            // set while engine 2's starter is still engaged (PMDG 737 parity).
            WaitForField("ES_E2_CUTOUT", "Engine 2 starting — waiting for the start switch to cut out",
                "Engine_Start_Switch_Status_1", v => System.Math.Abs(v - IFly737ActionExecutor.EngStartOff) < 0.1, 120),
            // --- Engine 1 ---
            SW("ES_E1_GRD", "Engine 1 start switch: GRD", "Engine_Start_Switch_Status_0",
                IFly737ActionExecutor.EngStartGround),
            WaitForField("ES_E1_N2", "Engine 1 motoring — waiting for N2 before introducing fuel",
                "FO_ENG1_N2", v => v >= EngStartFuelN2, 60, onTimeout: FlowStepFailurePolicy.Stop),
            SW("ES_E1_RUN", "Engine 1 start lever: IDLE", "Engine_Start_Lever_Status_0", StartLeverIdle),
            WaitForField("ES_E1_CUTOUT", "Engine 1 starting — waiting for the start switch to cut out",
                "Engine_Start_Switch_Status_0", v => System.Math.Abs(v - IFly737ActionExecutor.EngStartOff) < 0.1, 120),
        }
    };

    // -----------------------------------------------------------------------
    // 5. Before Taxi (includes the after-start power transfer)
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeTaxi() => new()
    {
        Id = "BEFORE_TAXI", Name = "Before Taxi",
        Description = "After-start power transfer (generators on, APU off), then probe heat, packs/isolation auto, start switches CONT, taxi lights, flaps.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAXI" },
        Steps = new()
        {
            // --- After-start power transfer ---
            Multi("BT_GEN", "Generators: ON", ("BTN_GEN_1_ON", 1), ("BTN_GEN_2_ON", 1)),
            SW("BT_APUBLEED_OFF", "APU bleed air: OFF", "APU_Bleed_Air_Switch_Status", 0),
            SW("BT_APU_OFF", "APU selector: OFF", "APU_Switch_Status", IFly737ActionExecutor.ApuOff),
            // --- Before-taxi setup ---
            Multi("BT_PROBE", "Probe heat: ON",
                ("Probe_Heat_Switch_1_Status", IFly737ActionExecutor.ProbeHeatOn),
                ("Probe_Heat_Switch_2_Status", IFly737ActionExecutor.ProbeHeatOn)),
            Multi("BT_PACKS", "Packs: AUTO",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackAuto),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackAuto)),
            SW("BT_ISO", "Isolation valve: AUTO", "Isolation_Valve_Switch_Status",
                IFly737ActionExecutor.IsolationValveAuto),
            Multi("BT_START_CONT", "Engine start switches: CONT",
                ("Engine_Start_Switch_Status_0", IFly737ActionExecutor.EngStartContinuous),
                ("Engine_Start_Switch_Status_1", IFly737ActionExecutor.EngStartContinuous)),
            Captain("BT_ANTIICE", "Set engine and wing anti-ice as required for conditions."),
            SW("BT_TAXI", "Taxi light: ON", "Taxi_Light_Switch_Status", 1),
            Multi("BT_TURNOFF", "Runway turnoff lights: ON",
                ("Runway_Turnoff_Light_1_Switch_Status", 1), ("Runway_Turnoff_Light_2_Switch_Status", 1)),
            Captain("BT_FLAPS", "Set the takeoff flaps."),
            // No lower-DU/EICAS synoptic-page-select field exists in IFlySdkFields.cs.
            Captain("BT_LOWERDU", "Lower display unit: SYS"),
        }
    };

    // -----------------------------------------------------------------------
    // 6. Before Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Description = "Landing lights, strobes, autothrottle arm, transponder TA/RA.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL" },
        Steps = new()
        {
            // Landing_Light_{1,2}_Switch_Status: 0 Off/1 On only — no retractable/fixed split.
            Multi("BTO_LAND", "Landing lights: ON",
                ("Landing_Light_1_Switch_Status", 1), ("Landing_Light_2_Switch_Status", 1)),
            SW("BTO_STROBE", "Position lights: STROBE & STEADY", "Position_Light_Switch_Status",
                IFly737ActionExecutor.PositionLightsStrobeAndSteady),
            // Autothrottle arm: absolute SET, no toggle-guard skip predicate needed.
            SW("BTO_AT", "Autothrottle: ARM", "AT_Switch_Status", 1),
            SW("BTO_XPDR", "Transponder: TA/RA", "Transponder_Mode_Switch_Status",
                IFly737ActionExecutor.XpdrTaRa),
            // Advise the cabin crew for takeoff — the attendant-call button.
            SW("BTKO_CABIN", "Advise the cabin crew for takeoff", "BTN_ATTENDANT_CALL", 1,
                checklistItemId: "BTKO_CABIN", isMomentary: true),
            Captain("BTO_BRIEF", "Confirm takeoff runway and trim set."),
        }
    };

    // -----------------------------------------------------------------------
    // 7. After Takeoff
    // -----------------------------------------------------------------------
    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Packs auto, start switches off, turnoff lights off, gear up, autobrake off.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL" },
        Steps = new()
        {
            Multi("AT_PACKS", "Packs: AUTO",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackAuto),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackAuto)),
            Multi("AT_START_OFF", "Engine start switches: OFF",
                ("Engine_Start_Switch_Status_0", IFly737ActionExecutor.EngStartOff),
                ("Engine_Start_Switch_Status_1", IFly737ActionExecutor.EngStartOff)),
            Multi("AT_TURNOFF", "Runway turnoff lights: OFF",
                ("Runway_Turnoff_Light_1_Switch_Status", 0), ("Runway_Turnoff_Light_2_Switch_Status", 0)),
            // Gear_Lever_Status has only 0 Up/1 Down — no OFF detent on this airframe, so this
            // commands UP and is labelled "UP" rather than the PMDG's "OFF".
            SW("AT_GEAR_OFF", "Gear lever: UP", "Gear_Lever_Status", IFly737ActionExecutor.GearUp),
            SW("AT_AB_OFF", "Autobrake: OFF", "Autobrake_Selector_Status",
                IFly737ActionExecutor.AutobrakeOff),
        }
    };

    // -----------------------------------------------------------------------
    // 8. Descent
    // -----------------------------------------------------------------------
    private static Flow BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Description = "Seatbelt sign on, autobrake, ILS, approach setup.",
        RelatedChecklistGroupIds = new[] { "DESCENT", "DESCENT_CL" },
        Steps = new()
        {
            SW("DS_BELTS", "Seatbelt signs: ON", "Fasten_Belts_Switch_Status", IFly737ActionExecutor.SignOn),
            Captain("DS_AB", "Set the landing autobrake — Forward Panel, Autobrake"),
            Captain("DS_ILS", "Set the ILS frequencies and course."),
            Captain("DS_DATA", "Confirm landing data, VREF and minimums."),
        }
    };

    // -----------------------------------------------------------------------
    // 9. Approach
    // -----------------------------------------------------------------------
    private static Flow BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "EFIS approach mode, range 20, altimeters.",
        RelatedChecklistGroupIds = new[] { "APPROACH", "APPROACH_CL" },
        Steps = new()
        {
            SW("AP_EFIS_MODE", "EFIS mode: APP", "ND_Mode_Status_0", IFly737ActionExecutor.NdModeApproach),
            SW("AP_EFIS_RANGE", "EFIS range: 20", "ND_Range_Status_0", IFly737ActionExecutor.NdRange20Nm),
            // Notify the cabin crew for landing — the attendant-call button.
            SW("AP_CABIN", "Notify the cabin crew for landing", "BTN_ATTENDANT_CALL", 1,
                checklistItemId: "AP_CABIN", isMomentary: true),
            Captain("AP_ALT", "Set the altimeters."),
        }
    };

    // -----------------------------------------------------------------------
    // 10. Landing
    // -----------------------------------------------------------------------
    private static Flow BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Description = "Start switches CONT, speedbrake armed, missed approach altitude.",
        RelatedChecklistGroupIds = new[] { "LANDING", "LANDING_CL" },
        Steps = new()
        {
            Multi("LD_START_CONT", "Engine start switches: CONT",
                ("Engine_Start_Switch_Status_0", IFly737ActionExecutor.EngStartContinuous),
                ("Engine_Start_Switch_Status_1", IFly737ActionExecutor.EngStartContinuous)),
            // Speedbrake ARM is a Captain reminder on this aircraft — the lever's write path
            // is deliberately read-only (see class doc).
            Captain("LD_SPDBRK", "Speedbrake: ARMED"),
            Captain("LD_MISSED", "Set the missed approach altitude."),
        }
    };

    // -----------------------------------------------------------------------
    // 11. After Landing
    // -----------------------------------------------------------------------
    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Lights, anti-ice off, probe heat off, APU on, start switches off, flaps up, autobrake off.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING" },
        Steps = new()
        {
            Multi("AL_LAND_OFF", "Landing lights: OFF",
                ("Landing_Light_1_Switch_Status", 0), ("Landing_Light_2_Switch_Status", 0)),
            Multi("AL_TURNOFF", "Runway turnoff lights: ON",
                ("Runway_Turnoff_Light_1_Switch_Status", 1), ("Runway_Turnoff_Light_2_Switch_Status", 1)),
            SW("AL_TAXI", "Taxi light: ON", "Taxi_Light_Switch_Status", 1),
            SW("AL_STROBE_OFF", "Position lights: STEADY", "Position_Light_Switch_Status",
                IFly737ActionExecutor.PositionLightsSteady),
            Multi("AL_EAI_OFF", "Engine anti-ice: OFF",
                ("Eng_1_AntiIce_Switch_Status", 0), ("Eng_2_AntiIce_Switch_Status", 0)),
            SW("AL_WAI_OFF", "Wing anti-ice: OFF", "Wing_AntiIce_Switch_Status", 0),
            // No OFF position on this switch — AUTO is the resting position.
            Multi("AL_PROBE_OFF", "Probe heat: AUTO",
                ("Probe_Heat_Switch_1_Status", IFly737ActionExecutor.ProbeHeatAuto),
                ("Probe_Heat_Switch_2_Status", IFly737ActionExecutor.ProbeHeatAuto)),
            // APU START for gate power — same collapsed pseudo-key sequence as Before Start;
            // no on-line wait (the aircraft is taxiing in, and the Shutdown flow's generator
            // transfer happens minutes later, matching the PMDG template).
            SW("AL_APU", "APU: START", IFly737ActionExecutor.KeyApuStart, 1, isMomentary: true),
            Multi("AL_START_OFF", "Engine start switches: OFF",
                ("Engine_Start_Switch_Status_0", IFly737ActionExecutor.EngStartOff),
                ("Engine_Start_Switch_Status_1", IFly737ActionExecutor.EngStartOff)),
            // FLAP_Status: 0 Lever UP..8 Lever 40 — a real absolute combo position on this
            // airframe (not a per-detent momentary click like the PMDG's flap lever).
            SW("AL_FLAPS_UP", "Flaps: UP", "FLAP_Status", 0),
            SW("AL_AB_OFF", "Autobrake: OFF", "Autobrake_Selector_Status",
                IFly737ActionExecutor.AutobrakeOff),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Shutdown
    // -----------------------------------------------------------------------
    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Engines cutoff, signs/lights off, fuel pumps off, anti-ice off, transponder ALT OFF.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN" },
        Steps = new()
        {
            // BOTH APU generator transfer buttons.
            Multi("SD_APUGEN", "APU generators: ON",
                ("BTN_APU_GEN_1_ON", 1), ("BTN_APU_GEN_2_ON", 1)),
            Multi("SD_LEVERS", "Engine start levers: CUTOFF",
                ("Engine_Start_Lever_Status_0", StartLeverCutoff),
                ("Engine_Start_Lever_Status_1", StartLeverCutoff)),
            SW("SD_BELTS", "Seatbelt signs: OFF", "Fasten_Belts_Switch_Status", IFly737ActionExecutor.SignOff),
            Multi("SD_TURNOFF", "Runway turnoff lights: OFF",
                ("Runway_Turnoff_Light_1_Switch_Status", 0), ("Runway_Turnoff_Light_2_Switch_Status", 0)),
            SW("SD_TAXI_OFF", "Taxi light: OFF", "Taxi_Light_Switch_Status", 0),
            SW("SD_LOGO_OFF", "Logo lights: OFF", "Logo_Light_Switch_Status", 0),
            SW("SD_APUBLEED", "APU bleed air: ON", "APU_Bleed_Air_Switch_Status", 1),
            Multi("SD_FUEL_OFF", "Fuel pumps: OFF",
                ("Fuel_L_AFT_Switch_Status", 0), ("Fuel_L_FWD_Switch_Status", 0),
                ("Fuel_R_FWD_Switch_Status", 0), ("Fuel_R_AFT_Switch_Status", 0),
                ("Fuel_CENTER_L_Switch_Status", 0), ("Fuel_CENTER_R_Switch_Status", 0)),
            Multi("SD_EAI_OFF", "Engine anti-ice: OFF",
                ("Eng_1_AntiIce_Switch_Status", 0), ("Eng_2_AntiIce_Switch_Status", 0)),
            Multi("SD_HYDELEC_OFF", "Electric hydraulic pumps: OFF",
                ("ELEC_1_HYD_Switch_Status", 0), ("ELEC_2_HYD_Switch_Status", 0)),
            Multi("SD_HYDENG_OFF", "Engine hydraulic pumps: OFF",
                ("ENG_1_HYD_Switch_Status", 0), ("ENG_2_HYD_Switch_Status", 0)),
            Multi("SD_WINHEAT_OFF", "Window heat: OFF",
                ("Window_Heat_Switch_1_Status", 0), ("Window_Heat_Switch_2_Status", 0),
                ("Window_Heat_Switch_3_Status", 0), ("Window_Heat_Switch_4_Status", 0)),
            // This airframe's resting position is ALT OFF, not STBY.
            SW("SD_XPDR", "Transponder: ALT OFF", "Transponder_Mode_Switch_Status",
                IFly737ActionExecutor.XpdrAltOff),
        }
    };

    // -----------------------------------------------------------------------
    // 13. Secure
    // -----------------------------------------------------------------------
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Secure",
        Description = "IRS off, emergency exit off, window heat off, packs off.",
        RelatedChecklistGroupIds = new[] { "SECURE" },
        Steps = new()
        {
            Multi("SE_IRS_OFF", "IRS mode selectors: OFF",
                ("IRS_Mode_Switch_Status_0", IFly737ActionExecutor.IrsOff),
                ("IRS_Mode_Switch_Status_1", IFly737ActionExecutor.IrsOff)),
            // Emergency_Light_Switch_Status: 0 Guard closed/1 Off/2 Armed/3 On — NOT the
            // PMDG's numbering; use the executor's EmerExitOff constant (1, not 0).
            SW("SE_EMER_OFF", "Emergency exit lights: OFF", "Emergency_Light_Switch_Status",
                IFly737ActionExecutor.EmerExitOff),
            Multi("SE_WINHEAT_OFF", "Window heat: OFF",
                ("Window_Heat_Switch_1_Status", 0), ("Window_Heat_Switch_2_Status", 0),
                ("Window_Heat_Switch_3_Status", 0), ("Window_Heat_Switch_4_Status", 0)),
            Multi("SE_PACKS_OFF", "Packs: OFF",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackOff),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackOff)),
            // APU selector to OFF (absolute set — idempotent when already off).
            SW("SE_APU_OFF", "APU: OFF", "APU_Switch_Status", IFly737ActionExecutor.ApuOff),
            // No GPU-on readback exists to skip on (see class doc) — unconditional press,
            // matching the checklist's own action-only shape for this item.
            SW("SE_GND_PWR_OFF", "Ground power: OFF", "BTN_GRD_PWR_OFF", 1, isMomentary: true),
        }
    };

    // -----------------------------------------------------------------------
    // Step builder helpers
    // -----------------------------------------------------------------------

    private static Step SW(string id, string label, string eventName, int? target,
        string? checklistItemId = null, bool isMomentary = false) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,
        IsMomentary = isMomentary || target == null,
        CompletesChecklistItemId = checklistItemId,
        PostActionDelayMs = 350,
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

    // reminderText: what "Captain action required: …" speaks (defaults to label). A
    // separate short label matters when the step can be SKIPPED — the skip path reads
    // "Already set: {label}", where an imperative sentence would compose badly.
    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,
        PostActionDelayMs = 200,
    };

    private static Step Skip(Step step, Func<IFly737StateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }
}
