using MSFSBlindAssist.Aircraft;
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
/// - Position lights 0=Strobe&amp;Steady/1=Off/2=Steady (probe-verified, PR #196 — the SDK status
///   doc's reversed labels were wrong; NOT the PMDG numbering). Use the PositionLights* constants.
/// - Landing lights are a 3-state 0=Off/1=Flash/2=On STATUS on two switches (Landing_Light_1/2 —
///   probe-verified, PR #196; 1 is FLASH, not On) — no retractable/fixed split like the PMDG's
///   four lights. Use the LandingLights* constants.
/// - EFIS (ND) mode 0=Approach/1=VOR/2=Map/3=Plan. The ND range is NOT commandable on this SDK
///   (RANGE_SET dead, no absolute position anywhere — see IFly737ActionExecutor's
///   SetEFISRangeCapt note) — range steps were REMOVED from the flows/checklists entirely
///   (user decision 2026-08-18, do not re-add); the panel's step buttons remain.
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
///    engine's GRD press — exactly like the PMDG's ES_E2_CUTOUT/ES_E1_CUTOUT. Both waits are
///    run by the executor (IFly737ActionExecutor.StartWaitAsync, 2026-10-18), not FlowManager:
///    the LEAP-1B's bowed rotor motoring can hold a start for minutes, and only there can a
///    wait start its clock again while the BRM flag reads active.
///  - Fuel flow reset (PF_FF) DOES have an iFly equivalent (Fuel_Flow_Switch_Status, position
///    0=Reset, spring-loaded — Overhead.cs:553-555) — not a Captain reminder.
///  - Flight directors (PF_FD1/PF_FD2) and autothrottle arm (BTO_AT) are plain absolute SW
///    writes — no skip guard needed (see class doc).
///  - Speedbrake ARM (LD_SPDBRK) is a verified First Officer arm through the SPEEDBRAKE_ARM
///    pseudo-key (IFly737ActionExecutor.ArmSpeedbrakeCoreAsync), the PMDG 737's shape. It
///    writes ARM through main's measured lever write (FLTCTRL_SPOILER, the same 0-224 scale as
///    Spoiler_Lever_Status, ARM exactly 34 — PR #261) and is judged by the lever at ARM AND
///    the SPEED BRAKE ARMED light (FO_SPEEDBRAKE_ARMED; the light alone is lit from 34 to
///    224). It completes both "Speedbrake: ARMED" lines. A deployed speed brake is left
///    alone ("Speedbrake extended, not armed. Left as it is."). History: this step was a
///    Captain reminder plus a read-only LD_SPDBRK_CHECK wait while the write scale was
///    unverified; both are gone.
///  - Weather radar test: REMOVED from this aircraft's flow and checklist entirely (user
///    decision 2026-08-18 — do not re-add). A WXR TEST command does exist
///    (`FMS_WXR_SYS_CTRL_SET`, Value2 0 TEST/1 NORM, readable back via
///    `Weather_Radar_System_Control_Switch_Status`) but remains deliberately unwired — see
///    IFly737ActionExecutor.PseudoKeys.
///  - Crew oxygen tests: none here, unlike the PMDG's PF_OXY_TEST_CAPT/PF_OXY_TEST_FO steps and
///    items (added to the PMDG 2026-08-16) — the iFly SDK has no crew oxygen mask test command
///    (IFlyKeyCommand's only oxygen commands are the PASSENGER oxygen switch and light), so there
///    is nothing to press. The Preflight Checklist's "Oxygen: TESTED, 100%" read-back stays a
///    Reminder on both aircraft (Pmdg737IFly737ReadbackLinkTests pins the difference).
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
///    The step is plan-gated (2026-10-06): with no SimBrief plan it has no target, a silent skip
///    (FO-20), and it names PF_PRESS, so a no-plan run leaves that line open instead of latching
///    it (the PMDG's two per-window steps name it the same way).
/// </summary>
public static class IFly737FlowDefinitions
{
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
            Skip(SW("EPU_BAT", "Battery: ON", "Battery_Switch_Mode", 2, checklistItemId: "EPU_BATTERY"),
                s => s.IsPosition("Battery_Switch_Mode", 2)),
            // STANDBY_POWER_Switch_Mode: 1=Battery/2=Off/3=Auto.
            Skip(SW("EPU_STBY", "Standby power: AUTO", "STANDBY_POWER_Switch_Mode",
                    IFly737ActionExecutor.StandbyPowerAuto, checklistItemId: "EPU_STBY"),
                s => s.IsPosition("STANDBY_POWER_Switch_Mode", IFly737ActionExecutor.StandbyPowerAuto)),
            // Ground power ON: momentary click (BTN_GRD_PWR_ON — the _SET command only moves
            // the animation, live-verified dead). Pressed unconditionally like the PMDG
            // template — no reliable "on bus" signal exists to skip on (see class doc).
            SW("EPU_GPU", "Ground power: ON", "BTN_GRD_PWR_ON", 1, isMomentary: true, checklistItemId: "EPU_GPU"),
            // No GPU/ground-power-availability readback exists on this SDK — downgraded from
            // the PMDG's automated WaitForField to a Captain reminder (see class doc).
            Captain("EPU_GPU_WAIT", "Confirm ground power connected",
                "Confirm ground power is connected — this aircraft has no automatic way to " +
                "verify it."),
            // IRS_Mode_Switch_Status_{0,1}: 0 Off/1 Align/2 Nav/3 Attitude. Alignment runs in
            // the background; no wait — "IRS aligned" auto-detects later.
            Multi("EPU_IRS", "IRS mode selectors: NAV", "EPU_IRS",
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
            SW("PF_YD", "Yaw damper: ON", "Yaw_Damper_Switch_Status", 1, checklistItemId: "PF_YD"),
            Multi("PF_FUEL_OFF", "Fuel pumps: OFF", "PF_FUEL_OFF",
                ("Fuel_L_AFT_Switch_Status", 0), ("Fuel_L_FWD_Switch_Status", 0),
                ("Fuel_R_FWD_Switch_Status", 0), ("Fuel_R_AFT_Switch_Status", 0),
                ("Fuel_CENTER_L_Switch_Status", 0), ("Fuel_CENTER_R_Switch_Status", 0)),
            // Emergency_Light_Switch_Status: 0 Guard closed/1 Off/2 Armed/3 On — NOT the
            // PMDG's numbering; use the executor's EmerExitArmed constant.
            SW("PF_EMER", "Emergency exit lights: ARMED", "Emergency_Light_Switch_Status",
                IFly737ActionExecutor.EmerExitArmed, checklistItemId: "PF_EMER"),
            // Fasten_Belts_Switch_Status: 0 Off/1 Auto/2 On — same numbering as the PMDG.
            SW("PF_BELTS", "Seatbelt signs: ON", "Fasten_Belts_Switch_Status",
                IFly737ActionExecutor.SignOn, checklistItemId: "PF_BELTS"),
            Also(Multi("PF_WINHEAT", "Window heat: ON", "PF_WINHEAT",
                ("Window_Heat_Switch_1_Status", 1), ("Window_Heat_Switch_2_Status", 1),
                ("Window_Heat_Switch_3_Status", 1), ("Window_Heat_Switch_4_Status", 1)), "PFC_WINHEAT"),
            // No OFF position exists on this switch — only Auto(0)/On(1). Label says AUTO,
            // matching the switch's own wording (see class doc).
            Multi("PF_PROBE_OFF", "Probe heat: AUTO", "PF_PROBE_OFF",
                ("Probe_Heat_Switch_1_Status", IFly737ActionExecutor.ProbeHeatAuto),
                ("Probe_Heat_Switch_2_Status", IFly737ActionExecutor.ProbeHeatAuto)),
            SW("PF_WAI_OFF", "Wing anti-ice: OFF", "Wing_AntiIce_Switch_Status", 0, checklistItemId: "PF_WAI"),
            Multi("PF_EAI_OFF", "Engine anti-ice: OFF", "PF_EAI_OFF",
                ("Eng_1_AntiIce_Switch_Status", 0), ("Eng_2_AntiIce_Switch_Status", 0)),
            // RecircFan_Switch_Status_{0,1}: 0 Off/1 Auto (the ON position is labelled AUTO).
            Multi("PF_RECIRC", "Recirculation fans: AUTO", "PF_RECIRC",
                ("RecircFan_Switch_Status_0", 1), ("RecircFan_Switch_Status_1", 1)),
            Multi("PF_PACKS", "Packs: AUTO", "PF_PACKS",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackAuto),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackAuto)),
            SW("PF_ISO", "Isolation valve: OPEN", "Isolation_Valve_Switch_Status",
                IFly737ActionExecutor.IsolationValveOpen, checklistItemId: "PF_ISO"),
            Multi("PF_BLEEDS", "Engine bleeds: ON", "PF_BLEEDS",
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
            // step covers both altitudes. The Captain fallback below covers the no-plan case
            // and a write that did not take.
            //
            // PLAN-GATED (2026-10-06): with no plan the pseudo-key is a quiet no-op that still
            // reports success, so the step used to say "Flight and landing altitudes: set" over
            // windows it never touched, and nothing kept PF_PRESS out of the end-of-flow latch
            // (FO-7). The step now has no target without a plan, which FlowManager treats as a
            // SILENT skip (FO-20: nothing sent or spoken, the Captain fallback is the speech),
            // and it names PF_PRESS, so a no-plan run, or a write that did not take, leaves
            // "Flight and landing altitudes: SET" open for the pilot. The target itself is a
            // token (the pseudo-key reads the plan off the evaluator, not the step).
            Plan(SW("PF_PRESS_ALTS", "Flight and landing altitudes: set", IFly737ActionExecutor.KeyPressAlts, 1,
                checklistItemId: "PF_PRESS")),
            // The Captain is asked unless the windows READ the plan (FO_PRESS_ALTS_MATCH, the
            // line's own test): with no plan the step above skips silently, and a write that did
            // not take leaves the windows where they were. Keyed on the windows, never on the
            // plan: keyed on the plan, a failed write was heard as "Skipping…" then a false
            // "Already set", and the reminder was lost (the A320 family's flap fix, fe2360b6).
            // Unread (NaN) is not set.
            Skip(Captain("PF_PRESS", "Flight and landing altitudes",
                    "Set flight and landing altitudes on the pressurization panel."),
                s => s.GetValue("FO_PRESS_ALTS_MATCH") > 0.5),
            SW("PF_LOGO", "Logo lights: ON", "Logo_Light_Switch_Status", 1, checklistItemId: "PF_LOGO"),
            // Flight directors: absolute SETs, no toggle-guard skip predicate needed.
            SW("PF_FD1", "Flight director 1: ON", "FD_1_Switch_Status", 1, checklistItemId: "PF_FD"),
            SW("PF_FD2", "Flight director 2: ON", "FD_2_Switch_Status", 1, checklistItemId: "PF_FD"),
            // Fuel_Flow_Switch_Status: 0 Reset/1 Rate/2 Used (spring-loaded RESET).
            SW("PF_FF", "Fuel flow: RESET", "Fuel_Flow_Switch_Status", 0, isMomentary: true),
            SW("PF_AB_RTO", "Autobrake: RTO", "Autobrake_Selector_Status",
                IFly737ActionExecutor.AutobrakeRto, checklistItemId: "PF_AB"),
            // Transponder_Mode_Switch_Status: 0 ALT OFF/1 XPNDR/2 TA Only/3 TA-RA — this
            // airframe's resting/ground position is ALT OFF, not STBY.
            SW("PF_XPDR", "Transponder: ALT OFF", "Transponder_Mode_Switch_Status",
                IFly737ActionExecutor.XpdrAltOff, checklistItemId: "PF_XPDR"),
            // ND_Mode_Status_0: 0 Approach/1 VOR/2 Map/3 Plan.
            SW("PF_EFIS_MODE", "EFIS mode: MAP", "ND_Mode_Status_0", IFly737ActionExecutor.NdModeMap, checklistItemId: "PF_EFIS_MODE"),
            // NO EFIS-range step (removed entirely, user decision 2026-08-18 — do not
            // re-add): the ND range cannot be commanded absolutely on this SDK (RANGE_SET
            // dead for every Value2, ND_Range_Status is net-clicks mod 3, and the cockpit
            // knob L:var is a free-spinning rotation counter — no absolute position
            // anywhere; see IFly737ActionExecutor's SetEFISRangeCapt note), so a step here
            // could only nag. The panel's Range Increase/Decrease buttons remain for the
            // pilot.
            Captain("PF_ALT", "Set the altimeters to the local QNH."),
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
            // Links no line: "APU: ON line" reads the APU on line (FO_APU_ON), which the wait below
            // completes, never the START press.
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
            //
            // It reads FO_APU_ON (2026-10-19), the "APU: ON line" line's own state, which it
            // completes: the APU GEN OFF BUS light as before, and also the buses carried off the
            // engine generators with no ground power available, so a re-run after a completed
            // transfer with the GPU gone passes instead of timing out. With the GPU still available
            // that case stays unknown (NaN) and the re-run still times out, as documented above.
            WaitForField("BS_APU_WAIT", "Waiting for the APU to come on line",
                IFly737StateEvaluator.ApuOnField, v => v > 0.5, 120,
                onTimeout: FlowStepFailurePolicy.Stop, checklistItemId: "BS_APU"),
            // Transfer the electrical load to the APU: momentary bus-transfer buttons.
            Multi("BS_APUGEN", "APU generators: ON", "BS_APUGEN",
                ("BTN_APU_GEN_1_ON", 1), ("BTN_APU_GEN_2_ON", 1)),
            // Ground power OFF: no GPU-on readback exists to skip on (see class doc) — pressed
            // unconditionally, matching the checklist's own action-only shape for this item.
            SW("BS_GPU_OFF", "Ground power: OFF", "BTN_GRD_PWR_OFF", 1, isMomentary: true),
            Multi("BS_FUELON", "Fuel pumps: ON", "BS_FUEL",
                ("Fuel_L_AFT_Switch_Status", 1), ("Fuel_L_FWD_Switch_Status", 1),
                ("Fuel_R_FWD_Switch_Status", 1), ("Fuel_R_AFT_Switch_Status", 1),
                ("Fuel_CENTER_L_Switch_Status", 1), ("Fuel_CENTER_R_Switch_Status", 1)),
            Multi("BS_HYDENG", "Engine hydraulic pumps: ON", "BS_HYDENG",
                ("ENG_1_HYD_Switch_Status", 1), ("ENG_2_HYD_Switch_Status", 1)),
            Multi("BS_HYD", "Electric hydraulic pumps: ON", "BS_HYD",
                ("ELEC_1_HYD_Switch_Status", 1), ("ELEC_2_HYD_Switch_Status", 1)),
            SW("BS_APUBLEED", "APU bleed air: ON", "APU_Bleed_Air_Switch_Status", 1, checklistItemId: "BS_APUBLEED"),
            Also(SW("BS_ANTICOL", "Anti-collision light: ON", "Anti_Collision_Light_Switch_Status", 1, checklistItemId: "BS_ANTICOL"), "BSC_ANTICOL"),
            // Transponder_Mode_Switch_Status TA/RA = 3 (max position — 4 positions total,
            // not the PMDG's 5).
            SW("BS_XPDR", "Transponder: TA/RA", "Transponder_Mode_Switch_Status",
                IFly737ActionExecutor.XpdrTaRa, checklistItemId: "BS_XPDR"),
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
            Multi("ES_PACKS_OFF", "Packs: OFF", "ES_PACKS",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackOff),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackOff)),
            // --- Engine 2 ---
            // An engine already RUNNING (N2 at or above EngineRunningN2) is never put back on GRD,
            // which re-engaged its starter at idle on a re-run: "Already set", and the start steps
            // below, which follow only a GRD this run sent, pass (the 777's shape).
            Skip(SW("ES_E2_GRD", "Engine 2 start switch: GRD", "Engine_Start_Switch_Status_1",
                IFly737ActionExecutor.EngStartGround), s => EngineRunning(s, 2)),
            // No start-valve field exists on this SDK (see class doc) — the early
            // starter-engaged confirmation the PMDG has is dropped. The start lever goes to IDLE
            // once MOTORING IS DONE (owner, 2026-10-19), never at a fixed 25 %: the LEAP-1B's bowed
            // rotor motoring no longer reads active, with N2 at the motoring point to prove the
            // starter is turning the engine. On timeout (starter/bleed failure — N2 never builds)
            // ABORT the flow rather than introduce fuel into an under-rotating engine. The executor
            // runs the wait (StartWaitAsync): 60 s, restarted while bowed rotor motoring reads
            // active, which holds a hot engine at motoring speed for minutes.
            OnlyIfSent(StartWaitStep("ES_E2_N2", "Engine 2 motoring — waiting for motoring to finish before introducing fuel",
                IFly737ActionExecutor.KeyEng2FuelN2), "ES_E2_GRD"),
            OnlyIfSent(SW("ES_E2_RUN", "Engine 2 start lever: IDLE", "Engine_Start_Lever_Status_1", StartLeverIdle,
                checklistItemId: "ES_E2_RUN"), "ES_E2_GRD"),
            // Starter cutout: the start switch springs back out of GRD as the starter
            // disengages. Wait on the switch itself, gating engine 1's GRD press so it's never
            // set while engine 2's starter is still engaged (PMDG 737 parity). Stop on a timeout,
            // as the PMDG's does: carrying on put engine 1 on GRD over a starter still engaged.
            // 120 s, aware of bowed rotor motoring the same way.
            OnlyIfSent(StartWaitStep("ES_E2_CUTOUT", "Engine 2 starting — waiting for the start switch to cut out",
                IFly737ActionExecutor.KeyEng2StarterCutout), "ES_E2_GRD"),
            // --- Engine 1 ---
            Skip(SW("ES_E1_GRD", "Engine 1 start switch: GRD", "Engine_Start_Switch_Status_0",
                IFly737ActionExecutor.EngStartGround), s => EngineRunning(s, 1)),
            OnlyIfSent(StartWaitStep("ES_E1_N2", "Engine 1 motoring — waiting for motoring to finish before introducing fuel",
                IFly737ActionExecutor.KeyEng1FuelN2), "ES_E1_GRD"),
            OnlyIfSent(SW("ES_E1_RUN", "Engine 1 start lever: IDLE", "Engine_Start_Lever_Status_0", StartLeverIdle,
                checklistItemId: "ES_E1_RUN"), "ES_E1_GRD"),
            OnlyIfSent(StartWaitStep("ES_E1_CUTOUT", "Engine 1 starting — waiting for the start switch to cut out",
                IFly737ActionExecutor.KeyEng1StarterCutout), "ES_E1_GRD"),
        }
    };

    // Engine n reads RUNNING: N2 at or above EngineRunningN2. Unread (NaN) is not running.
    internal static bool EngineRunning(IFly737StateEvaluator s, int engine) =>
        s.GetValue(engine == 1 ? "FO_ENG1_N2" : "FO_ENG2_N2") >= IFly737StateEvaluator.EngineRunningN2;

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
            Also(Multi("BT_GEN", "Generators: ON", "BT_GEN", ("BTN_GEN_1_ON", 1), ("BTN_GEN_2_ON", 1)), "BTC_GEN"),
            SW("BT_APUBLEED_OFF", "APU bleed air: OFF", "APU_Bleed_Air_Switch_Status", 0, checklistItemId: "BT_APUBLEED_OFF"),
            SW("BT_APU_OFF", "APU selector: OFF", "APU_Switch_Status", IFly737ActionExecutor.ApuOff, checklistItemId: "BT_APU"),
            // --- Before-taxi setup ---
            Also(Multi("BT_PROBE", "Probe heat: ON", "BT_PROBE",
                ("Probe_Heat_Switch_1_Status", IFly737ActionExecutor.ProbeHeatOn),
                ("Probe_Heat_Switch_2_Status", IFly737ActionExecutor.ProbeHeatOn)), "BTC_PROBE"),
            Multi("BT_PACKS", "Packs: AUTO", "BT_PACKS",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackAuto),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackAuto)),
            Also(SW("BT_ISO", "Isolation valve: AUTO", "Isolation_Valve_Switch_Status",
                IFly737ActionExecutor.IsolationValveAuto, checklistItemId: "BT_ISO"), "BTC_ISO"),
            Also(Multi("BT_START_CONT", "Engine start switches: CONT", "BT_START",
                ("Engine_Start_Switch_Status_0", IFly737ActionExecutor.EngStartContinuous),
                ("Engine_Start_Switch_Status_1", IFly737ActionExecutor.EngStartContinuous)), "BTC_START"),
            Captain("BT_ANTIICE", "Set engine and wing anti-ice as required for conditions."),
            SW("BT_TAXI", "Taxi light: ON", "Taxi_Light_Switch_Status", 1, checklistItemId: "BT_TAXI"),
            Multi("BT_TURNOFF", "Runway turnoff lights: ON", "BT_TURNOFF",
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
            // Landing_Light_{1,2}_Switch_Status: 0 Off/1 Flash/2 On (probe-verified, PR #196 —
            // 1 is FLASH, not On). No retractable/fixed split.
            Multi("BTO_LAND", "Landing lights: ON", "BTKO_LAND",
                ("Landing_Light_1_Switch_Status", IFly737ActionExecutor.LandingLightsOn),
                ("Landing_Light_2_Switch_Status", IFly737ActionExecutor.LandingLightsOn)),
            SW("BTO_STROBE", "Position lights: STROBE & STEADY", "Position_Light_Switch_Status",
                IFly737ActionExecutor.PositionLightsStrobeAndSteady, checklistItemId: "BTKO_STROBE"),
            // Autothrottle arm: absolute SET, no toggle-guard skip predicate needed.
            SW("BTO_AT", "Autothrottle: ARM", "AT_Switch_Status", 1, checklistItemId: "BTKO_AT"),
            SW("BTO_XPDR", "Transponder: TA/RA", "Transponder_Mode_Switch_Status",
                IFly737ActionExecutor.XpdrTaRa, checklistItemId: "BTKO_XPDR"),
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
        Description = "Packs auto, start switches off, turnoff lights off, gear up, autobrake off, then confirms the gear is up.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL" },
        Steps = new()
        {
            Also(Multi("AT_PACKS", "Packs: AUTO", "ATKO_PACKS",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackAuto),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackAuto)), "ATC_PACKS"),
            Multi("AT_START_OFF", "Engine start switches: OFF", "ATKO_START_OFF",
                ("Engine_Start_Switch_Status_0", IFly737ActionExecutor.EngStartOff),
                ("Engine_Start_Switch_Status_1", IFly737ActionExecutor.EngStartOff)),
            Multi("AT_TURNOFF", "Runway turnoff lights: OFF", "ATKO_TURNOFF",
                ("Runway_Turnoff_Light_1_Switch_Status", 0), ("Runway_Turnoff_Light_2_Switch_Status", 0)),
            // Gear_Lever_Status has only 0 Up/1 Down — no OFF detent on this airframe, so this
            // commands UP and is labelled "UP" rather than the PMDG's "OFF". Completes its own
            // action group's "Gear lever: UP" (ATKO_GEAR_OFF) — never the checklist's "Landing
            // gear: UP" (ATC_GEAR), which the read-only AT_GEAR_UP_CHECK below completes — so a
            // failed write is skipped aloud and left live rather than latched complete anyway by
            // MarkGroupComplete's blanket sweep of the AFTER_TAKEOFF group.
            // UP only when DEFINITELY airborne (SIM_ON_GROUND reads 0): After Takeoff started on
            // the runway by mistake, one Down Arrow below Before Takeoff, sent the lever UP on the
            // ground, and an SDK SET can skip the cockpit logic (IFLY-3), so the ground lock may
            // not stop it. On the ground, or unread, nothing is sent, the reason is spoken and the
            // line stays open; the executor refuses the write as well (RefusesGearUp).
            LeaveAlone(SW("AT_GEAR_OFF", "Gear lever: UP", "Gear_Lever_Status", IFly737ActionExecutor.GearUp, "ATKO_GEAR_OFF"),
                s => !s.IsDefinitelyAirborne, IFly737ActionExecutor.GearUpLeaveAloneText),
            SW("AT_AB_OFF", "Autobrake: OFF", "Autobrake_Selector_Status",
                IFly737ActionExecutor.AutobrakeOff, checklistItemId: "ATKO_AB_OFF"),
            // Read-only gear-up confirmation — it never moves the lever (AT_GEAR_OFF above
            // already did). Confirms the gear the way a crew does, "gear up, lights out"
            // (IFly737GearConfirmation), and completes the After Takeoff Checklist's
            // "Landing gear: UP". LAST, so gear still retracting does not hold up the steps
            // above; waits up to 20 s. If the gear is not confirmed up the step is announced
            // as skipped and FlowManager keeps ATC_GEAR out of MarkGroupComplete's latch, so
            // the line keeps mirroring the real gear instead of reading complete over gear
            // that is still down.
            Skip(WaitForField("AT_GEAR_UP_CHECK", "Landing gear: UP", IFly737GearConfirmation.UpField, v => v > 0.5, 20,
                    checklistItemId: "ATC_GEAR"),
                s => s.GetValue(IFly737GearConfirmation.UpField) > 0.5),
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
            SW("DS_BELTS", "Seatbelt signs: ON", "Fasten_Belts_Switch_Status", IFly737ActionExecutor.SignOn, checklistItemId: "DSA_BELTS"),
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
        Description = "EFIS approach mode, cabin notification, altimeters.",
        RelatedChecklistGroupIds = new[] { "APPROACH", "APPROACH_CL" },
        Steps = new()
        {
            SW("AP_EFIS_MODE", "EFIS mode: APP", "ND_Mode_Status_0", IFly737ActionExecutor.NdModeApproach, checklistItemId: "APA_EFIS_MODE"),
            // NO EFIS-range step — removed with the Preflight one (see BuildPreflight).
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
        Description = "Start switches CONT, speedbrake armed, missed approach altitude, then confirms the gear is down.",
        RelatedChecklistGroupIds = new[] { "LANDING", "LANDING_CL" },
        Steps = new()
        {
            Also(Multi("LD_START_CONT", "Engine start switches: CONT", "LDA_START",
                ("Engine_Start_Switch_Status_0", IFly737ActionExecutor.EngStartContinuous),
                ("Engine_Start_Switch_Status_1", IFly737ActionExecutor.EngStartContinuous)), "LDC_START"),
            // Verified arm via the SPEEDBRAKE_ARM pseudo-key (IFly737ActionExecutor.
            // ArmSpeedbrakeCoreAsync), the PMDG 737's shape: it writes ARM through main's
            // measured lever write and proves it against the lever AND the ARMED light. It
            // completes BOTH "Speedbrake: ARMED" lines, so a failed arm leaves neither latched.
            // Armed is "Already set"; a deployed speed brake is left alone with its reason.
            LeaveAlone(Skip(Also(SW("LD_SPDBRK", "Speedbrake: ARMED", IFly737ActionExecutor.KeySpeedbrakeArm, null,
                    "LDA_SPDBRK"), "LDC_SPDBRK"),
                s => s.IsSpeedbrakeArmed()),
                s => s.IsSpeedbrakeDeployed(), SpeedbrakeLeverState.LeaveAloneText),
            Captain("LD_MISSED", "Set the missed approach altitude."),
            // Read-only gear-down confirmation — this flow writes no gear lever at all.
            // Confirms the gear the way a crew does, "three green" (IFly737GearConfirmation),
            // and completes the Landing Checklist's "Landing gear: DOWN". LAST, so the steps
            // above are not held up; waits up to 20 s. If the gear is not confirmed down the
            // step is announced as skipped and FlowManager keeps LDC_GEAR out of
            // MarkGroupComplete's latch, so the line keeps mirroring the real gear instead of
            // reading complete over gear that is still up.
            Skip(WaitForField("LD_GEAR_DOWN_CHECK", "Landing gear: DOWN", IFly737GearConfirmation.DownField, v => v > 0.5, 20,
                    checklistItemId: "LDC_GEAR"),
                s => s.GetValue(IFly737GearConfirmation.DownField) > 0.5),
        }
    };

    // -----------------------------------------------------------------------
    // 11. After Landing
    // -----------------------------------------------------------------------
    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Speed brake down, lights, anti-ice off, probe heat off, APU on, start switches off, flaps up, autobrake off.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING" },
        Steps = new()
        {
            // PMDG's B738 After Landing Procedure opens with "Speed Brake Lever: Down" (18.1); after
            // an auto-speedbrake landing the lever sits at UP and nothing stowed it (owner, 2026-10-19,
            // the PMDG 737's AL_SPDBRK_DOWN). Spoiler_Lever_Status 0 is DOWN, written through the
            // panel's own lever write (the verified ARM writes 34 the same way), verified on the
            // lever. "Already set" only over a lever read DOWN; a lever nobody can read is never written.
            LeaveAlone(Skip(Verify(SW("AL_SPDBRK_DOWN", "Speed brake lever: DOWN", IFly737SpeedBrakeLever.FieldName,
                        (int)IFly737SpeedBrakeLever.Detents[0].Value, checklistItemId: "AL_SPDBRK"),
                    IFly737SpeedBrakeLever.FieldName, IFly737StateEvaluator.IsSpeedbrakeLeverDown),
                s => s.SpeedbrakePosition() == SpeedbrakeLeverPosition.Down),
                s => s.SpeedbrakePosition() == SpeedbrakeLeverPosition.Unknown,
                "Skipping: Speed brake lever: DOWN. The lever cannot be read yet."),
            Multi("AL_LAND_OFF", "Landing lights: OFF", "AL_LAND_OFF",
                ("Landing_Light_1_Switch_Status", IFly737ActionExecutor.LandingLightsOff),
                ("Landing_Light_2_Switch_Status", IFly737ActionExecutor.LandingLightsOff)),
            Multi("AL_TURNOFF", "Runway turnoff lights: ON", "AL_TURNOFF",
                ("Runway_Turnoff_Light_1_Switch_Status", 1), ("Runway_Turnoff_Light_2_Switch_Status", 1)),
            SW("AL_TAXI", "Taxi light: ON", "Taxi_Light_Switch_Status", 1, checklistItemId: "AL_TAXI"),
            SW("AL_STROBE_OFF", "Position lights: STEADY", "Position_Light_Switch_Status",
                IFly737ActionExecutor.PositionLightsSteady, checklistItemId: "AL_STROBE"),
            Multi("AL_EAI_OFF", "Engine anti-ice: OFF", "AL_EAI",
                ("Eng_1_AntiIce_Switch_Status", 0), ("Eng_2_AntiIce_Switch_Status", 0)),
            SW("AL_WAI_OFF", "Wing anti-ice: OFF", "Wing_AntiIce_Switch_Status", 0, checklistItemId: "AL_WAI"),
            // No OFF position on this switch — AUTO is the resting position.
            Multi("AL_PROBE_OFF", "Probe heat: AUTO", "AL_PROBE",
                ("Probe_Heat_Switch_1_Status", IFly737ActionExecutor.ProbeHeatAuto),
                ("Probe_Heat_Switch_2_Status", IFly737ActionExecutor.ProbeHeatAuto)),
            // APU START for gate power — same collapsed pseudo-key sequence as Before Start;
            // no on-line wait (the aircraft is taxiing in, matching the PMDG template): the
            // Shutdown flow waits for the APU generator before its transfer (SD_POWER_WAIT).
            // Links no line: AL_APU_ON_LINE, last, completes "APU: ON line".
            SW("AL_APU", "APU: START", IFly737ActionExecutor.KeyApuStart, 1, isMomentary: true),
            Multi("AL_START_OFF", "Engine start switches: OFF", "AL_START_OFF",
                ("Engine_Start_Switch_Status_0", IFly737ActionExecutor.EngStartOff),
                ("Engine_Start_Switch_Status_1", IFly737ActionExecutor.EngStartOff)),
            // FLAP_Status: 0 Lever UP..8 Lever 40 — a real absolute combo position on this
            // airframe (not a per-detent momentary click like the PMDG's flap lever).
            SW("AL_FLAPS_UP", "Flaps: UP", "FLAP_Status", 0, checklistItemId: "AL_FLAPS"),
            SW("AL_AB_OFF", "Autobrake: OFF", "Autobrake_Selector_Status",
                IFly737ActionExecutor.AutobrakeOff, checklistItemId: "AL_AB"),
            // Read-only, LAST so the APU's spool-up holds nothing up: completes "APU: ON line" once
            // the APU reads on line (FO_APU_ON); a timeout is heard skipped and keeps the line out
            // of the latch, so an APU that never started is never latched ON. Never writes.
            Skip(WaitForField("AL_APU_ON_LINE", "APU: ON line", IFly737StateEvaluator.ApuOnField, v => v > 0.5, 120,
                    checklistItemId: "AL_APU"),
                s => s.GetValue(IFly737StateEvaluator.ApuOnField) > 0.5),
        }
    };

    // -----------------------------------------------------------------------
    // 12. Shutdown
    // -----------------------------------------------------------------------
    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Once the APU generator or ground power can take the buses: engines cutoff, signs/lights off, fuel pumps off, anti-ice off, flight directors off, transponder ALT OFF.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN" },
        Steps = new()
        {
            // Started by mistake in flight with the APU running, the power wait below passes and
            // the levers cut both engines: never airborne (the MD-11's SD_ON_GROUND).
            OnGroundGuard("SD_ON_GROUND"),
            // The levers take the engine generators off the buses, so something else must be able
            // to carry them first; Before Start guards its reverse transfer the same way
            // (BS_APU_WAIT, Stop). Passes on the APU generator waiting off the buses (the blue APU
            // GEN OFF BUS light), on buses already off the engine generators (the APU or ground
            // power carries them) or on levers already at CUTOFF (nothing left to protect); stops
            // the flow on a timeout. Without it, an APU started in After Landing that was not yet
            // on line, or never started, was followed by "APU generators: ON" and the cut, which
            // left the aircraft on the battery.
            WaitForField("SD_POWER_WAIT", "APU generator or ground power", IFly737StateEvaluator.ShutdownPowerField,
                v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop),
            // BOTH APU generator transfer buttons, pressed only while the APU generator waits off
            // the buses: otherwise there is nothing for them to transfer, and "APU generators: ON"
            // would claim an APU that may not be running.
            LeaveAlone(Multi("SD_APUGEN", "APU generators: ON", "SD_APUGEN",
                    ("BTN_APU_GEN_1_ON", 1), ("BTN_APU_GEN_2_ON", 1)),
                s => !s.IsOn("APU_GEN_OFF_BUS_Light_Status"),
                "APU generators left as they are: no APU generator is waiting to take the buses."),
            // The transfer TOOK: the buses off the engine generators (or the levers already at
            // CUTOFF). A transfer that did not take STOPS the flow here (the PMDG 737's verified
            // SD_APUGEN, Stop): left alone, the levers below kept the engines running and the rest of
            // Shutdown then switched the fuel and hydraulic pumps off under them.
            WaitForField("SD_LOAD_OFF_ENGINES", "APU generator or ground power on the buses",
                IFly737StateEvaluator.LoadOffEnginesField, v => v > 0.5, 10, onTimeout: FlowStepFailurePolicy.Stop),
            // Never cut while the engine generators still carry the buses (the APU generator press
            // did not take): that leaves the aircraft on the battery. "Already set" when both levers
            // already read CUTOFF.
            LeaveAlone(Skip(Also(Multi("SD_LEVERS", "Engine start levers: CUTOFF", "SD_LEVERS",
                    ("Engine_Start_Lever_Status_0", StartLeverCutoff),
                    ("Engine_Start_Lever_Status_1", StartLeverCutoff)), "SDC_LEVERS"),
                    s => s.AreStartLeversAtCutoff()),
                s => !s.AreBusesOffEngineGenerators(),
                "No APU generator or ground power on the buses, so the engines stay running."),
            SW("SD_BELTS", "Seatbelt signs: OFF", "Fasten_Belts_Switch_Status", IFly737ActionExecutor.SignOff, checklistItemId: "SD_BELTS"),
            // PMDG's B738 Shutdown Procedure: "ANTI COLLISION light switch: OFF" right after the belts
            // (19.8). Before Start turned it ON and nothing turned it off (owner, 2026-10-19).
            SW("SD_ANTICOL_OFF", "Anti-collision light: OFF", "Anti_Collision_Light_Switch_Status", 0, checklistItemId: "SD_ANTICOL"),
            Multi("SD_TURNOFF", "Runway turnoff lights: OFF", "SD_TURNOFF",
                ("Runway_Turnoff_Light_1_Switch_Status", 0), ("Runway_Turnoff_Light_2_Switch_Status", 0)),
            SW("SD_TAXI_OFF", "Taxi light: OFF", "Taxi_Light_Switch_Status", 0, checklistItemId: "SD_TAXI"),
            SW("SD_LOGO_OFF", "Logo lights: OFF", "Logo_Light_Switch_Status", 0, checklistItemId: "SD_LOGO"),
            SW("SD_APUBLEED", "APU bleed air: ON", "APU_Bleed_Air_Switch_Status", 1, checklistItemId: "SD_APUBLEED"),
            Also(Multi("SD_FUEL_OFF", "Fuel pumps: OFF", "SD_FUEL",
                ("Fuel_L_AFT_Switch_Status", 0), ("Fuel_L_FWD_Switch_Status", 0),
                ("Fuel_R_FWD_Switch_Status", 0), ("Fuel_R_AFT_Switch_Status", 0),
                ("Fuel_CENTER_L_Switch_Status", 0), ("Fuel_CENTER_R_Switch_Status", 0)), "SDC_FUEL"),
            Multi("SD_EAI_OFF", "Engine anti-ice: OFF", "SD_EAI",
                ("Eng_1_AntiIce_Switch_Status", 0), ("Eng_2_AntiIce_Switch_Status", 0)),
            Multi("SD_HYDELEC_OFF", "Electric hydraulic pumps: OFF", "SD_HYDELEC",
                ("ELEC_1_HYD_Switch_Status", 0), ("ELEC_2_HYD_Switch_Status", 0)),
            Multi("SD_HYDENG_OFF", "Engine hydraulic pumps: OFF", "SD_HYDENG",
                ("ENG_1_HYD_Switch_Status", 0), ("ENG_2_HYD_Switch_Status", 0)),
            Multi("SD_WINHEAT_OFF", "Window heat: OFF", "SD_WINHEAT",
                ("Window_Heat_Switch_1_Status", 0), ("Window_Heat_Switch_2_Status", 0),
                ("Window_Heat_Switch_3_Status", 0), ("Window_Heat_Switch_4_Status", 0)),
            // PMDG's B738 Shutdown Procedure: "FLIGHT DIRECTOR switches: OFF" (19.21), then the
            // transponder (19.22). Preflight turned them ON and nothing turned them off (owner,
            // 2026-10-19). Absolute SETs on this airframe, so no toggle guard.
            SW("SD_FD1", "Flight director 1: OFF", "FD_1_Switch_Status", 0, checklistItemId: "SD_FD"),
            SW("SD_FD2", "Flight director 2: OFF", "FD_2_Switch_Status", 0, checklistItemId: "SD_FD"),
            // This airframe's resting position is ALT OFF, not STBY.
            SW("SD_XPDR", "Transponder: ALT OFF", "Transponder_Mode_Switch_Status",
                IFly737ActionExecutor.XpdrAltOff, checklistItemId: "SD_XPDR"),
        }
    };

    // -----------------------------------------------------------------------
    // 13. Secure
    // -----------------------------------------------------------------------
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Secure",
        Description = "IRS off, emergency exit off, window heat off, packs off, APU and ground power off, battery off after the APU cool-down.",
        RelatedChecklistGroupIds = new[] { "SECURE", "ELEC_POWER_DOWN" },
        Steps = new()
        {
            // The IRS and the battery go off below: never airborne (the MD-11's SD_ON_GROUND).
            OnGroundGuard("SE_ON_GROUND"),
            Also(Multi("SE_IRS_OFF", "IRS mode selectors: OFF", "SE_IRS",
                ("IRS_Mode_Switch_Status_0", IFly737ActionExecutor.IrsOff),
                ("IRS_Mode_Switch_Status_1", IFly737ActionExecutor.IrsOff)), "SEC_IRS"),
            // Emergency_Light_Switch_Status: 0 Guard closed/1 Off/2 Armed/3 On — NOT the
            // PMDG's numbering; use the executor's EmerExitOff constant (1, not 0).
            Also(SW("SE_EMER_OFF", "Emergency exit lights: OFF", "Emergency_Light_Switch_Status",
                IFly737ActionExecutor.EmerExitOff, checklistItemId: "SE_EMER"), "SEC_EMER"),
            Also(Multi("SE_WINHEAT_OFF", "Window heat: OFF", "SE_WINHEAT",
                ("Window_Heat_Switch_1_Status", 0), ("Window_Heat_Switch_2_Status", 0),
                ("Window_Heat_Switch_3_Status", 0), ("Window_Heat_Switch_4_Status", 0)), "SEC_WINHEAT"),
            Also(Multi("SE_PACKS_OFF", "Packs: OFF", "SE_PACKS",
                ("Pack_Switch_Status_0", IFly737ActionExecutor.PackOff),
                ("Pack_Switch_Status_1", IFly737ActionExecutor.PackOff)), "SEC_PACKS"),
            // APU selector to OFF (absolute set — idempotent when already off). It skips on a
            // switch that DEFINITELY reads OFF (unread is NaN, so it is still sent), so the
            // cool-down wait below, which follows only a switch-off this run sent, never runs
            // for an APU that was not running (owner, 2026-10-14).
            Skip(SW("SE_APU_OFF", "APU: OFF", "APU_Switch_Status", IFly737ActionExecutor.ApuOff, checklistItemId: "SE_APU_OFF"),
                s => s.IsPosition("APU_Switch_Status", IFly737ActionExecutor.ApuOff)),
            // No GPU-on readback exists to skip on (see class doc) — unconditional press,
            // matching the checklist's own action-only shape for this item.
            SW("SE_GND_PWR_OFF", "Ground power: OFF", "BTN_GRD_PWR_OFF", 1, isMomentary: true, checklistItemId: "SE_GND_PWR_OFF"),
            // Cold and dark (owner, 2026-10-14). The iFly SDK has no APU EGT or speed field, so
            // the 737 NG cool-down (60 s, then the inlet door) is a fixed 2 minutes — a
            // judgement — and only after an APU this run switched off.
            OnlyIfSent(Wait("SE_APU_WAIT", "APU to stop and its inlet door to close", 120), "SE_APU_OFF"),
            // Battery_Switch_Mode 1 = OFF / 2 = ON. LAST, never with an engine turning.
            LeaveAlone(Skip(SW("SE_BAT_OFF", "Battery: OFF", "Battery_Switch_Mode", 1, checklistItemId: "EPD_BAT"),
                    s => s.IsPosition("Battery_Switch_Mode", 1)),
                s => !EnginesStopped(s), "Engines running, so the battery stays on."),
        }
    };

    // Stops a flow that cuts the engines or switches the IRS and battery off unless the aircraft
    // reads on the ground (SIM_ON_GROUND from SimConnect's cache; unread fails the guard).
    private static Step OnGroundGuard(string id) =>
        WaitForField(id, "On the ground", IFly737StateEvaluator.OnGroundField, v => v > 0.5, 5,
            onTimeout: FlowStepFailurePolicy.Stop);

    // Both engines read and below 10 % N2. NaN (no sample yet) is NOT stopped.
    internal static bool EnginesStopped(IFly737StateEvaluator s)
    {
        double n1 = s.GetValue("FO_ENG1_N2"), n2 = s.GetValue("FO_ENG2_N2");
        return !double.IsNaN(n1) && !double.IsNaN(n2) && n1 < 10 && n2 < 10;
    }

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
        CompletesChecklistItemId = checklistItemId,        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    // checklistItemId is REQUIRED (nullable) so no multi-switch write can be added without
    // deciding which line it sets (IFly737FlowChecklistLinkTests audits the choice).
    private static Step Multi(string id, string label, string? checklistItemId,
        params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),
        CompletesChecklistItemId = checklistItemId,        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    // An engine-start wait the executor runs (IFly737ActionExecutor.StartWaitAsync): read-only,
    // aware of bowed rotor motoring, and its timeout stops the flow. A switch step only so that it
    // reaches the executor; it writes nothing and names no checklist line.
    private static Step StartWaitStep(string id, string label, string waitKey) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = waitKey,
        TargetValue = 1,        FailurePolicy = FlowStepFailurePolicy.Stop,
    };

    private static Step Wait(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip, string? checklistItemId = null,
        string[]? alsoChecklistItemIds = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
        CompletesChecklistItemId = checklistItemId,
        AlsoCompletesChecklistItemIds = alsoChecklistItemIds ?? Array.Empty<string>(),    };

    // reminderText: what "Captain action required: …" speaks (defaults to label). A
    // separate short label matters when the step can be SKIPPED — the skip path reads
    // "Already set: {label}", where an imperative sentence would compose badly.
    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,    };

    private static Step Skip(Step step, Func<IFly737StateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }

    // A write judged by what it achieved (FlowStep.VerifyFieldName): FlowManager reads `field`
    // against `condition` for a short window after the write, and a write that did not get there
    // is heard skipped and keeps its lines out of the latch.
    private static Step Verify(Step step, string field, Func<double, bool> condition)
    {
        step.VerifyFieldName = field;
        step.VerifyCondition = condition;
        return step;
    }

    // A step that needs the SimBrief pressurization plan: without one it has no target, a SILENT
    // skip in FlowManager (FO-20) that keeps its lines out of the latch; with one it sends its own
    // fixed target unchanged.
    private static Step Plan(Step step)
    {
        int? target = step.TargetValue;
        step.TargetValueProvider = s => s.HasPressurizationPlan ? target : null;
        return step;
    }

    // A step the First Officer must not perform in the aircraft's current state
    // (FlowStep.LeaveAloneWhen): nothing is sent, `text` is spoken, its lines stay open.
    private static Step LeaveAlone(Step step, Func<IFly737StateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }

    // A step that runs only when an EARLIER switch step of this run actually sent its write
    // (FlowStep.RunOnlyIfStepSent): otherwise it passes silently as done.
    private static Step OnlyIfSent(Step step, string sentStepId)
    {
        step.RunOnlyIfStepSent = sentStepId;
        return step;
    }

    // A write whose line appears in BOTH its own action group and the phase's read-back
    // (*_CL) checklist, and which achieves both (same field, same condition): the extra
    // line is delivered, and on a skipped step kept out of the latch, alongside
    // CompletesChecklistItemId (FlowStep.AlsoCompletesChecklistItemIds).
    private static Step Also(Step step, params string[] alsoCompletes)
    {
        step.AlsoCompletesChecklistItemIds = alsoCompletes;
        return step;
    }
}
