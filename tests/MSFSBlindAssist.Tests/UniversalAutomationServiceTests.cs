using MSFSBlindAssist.Automation;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class UniversalAutomationServiceTests
{
    private readonly List<string> events = new();
    private readonly List<string> spoken = new();
    // What the service said through its separate success-confirmation delegate (see the last section).
    private readonly List<string> confirmations = new();
    private UniversalAutomationService Make() => new(events.Add, spoken.Add);
    // The service with a delegate of its own for the confirmation: `spoken` is then the interrupting one.
    private UniversalAutomationService MakeSplit(Action? engage = null, Func<bool?>? engaged = null, Func<long>? now = null)
        => new(events.Add, spoken.Add, engage, engaged, now, confirmations.Add);

    // One airborne or ground sample, the sim's SIM ON GROUND taken as below 20 ft AGL.
    private static void Fly(UniversalAutomationService s, double altitudeMsl, double verticalSpeedFpm, double altitudeAgl)
        => s.Update(altitudeMsl, verticalSpeedFpm, altitudeAgl, onGround: altitudeAgl < 20);

    // A ground sample: what arms the takeoff gates (gear up, AP engage) for the takeoff that follows.
    private static void OnGround(UniversalAutomationService s) => s.Update(0, 0, 0, onGround: true);

    [Fact]
    public void GearUp_FiresOnceOnPositiveRateAboveFiftyFeet()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        OnGround(s);
        Fly(s, altitudeMsl: 500, verticalSpeedFpm: 800, altitudeAgl: 100);
        Fly(s, altitudeMsl: 700, verticalSpeedFpm: 800, altitudeAgl: 300);
        Assert.Equal(new[] { "GEAR_UP" }, events);
    }

    [Fact]
    public void GearUp_DoesNotFireBelowFiftyFeetOrWhenLevel()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        OnGround(s);
        Fly(s, 200, 800, 40);    // too low
        Fly(s, 200, 0,   400);   // not climbing
        Assert.Empty(events);
    }

    [Fact]
    public void GearDown_FiresOnceDescendingThroughTwoThousandAgl()
    {
        var s = Make();
        s.AutoGearDownEnabled = true;
        Fly(s, 5000, 800, 5000);   // airborne
        Fly(s, 3000, -700, 1500);  // descending through the window
        Fly(s, 2800, -700, 1200);
        Assert.Equal(new[] { "GEAR_DOWN" }, events);
    }

    [Fact]
    public void GearDown_RearmsAboveThreeThousandForGoAround()
    {
        var s = Make();
        s.AutoGearDownEnabled = true;
        Fly(s, 5000, 800, 5000);
        Fly(s, 3000, -700, 1500);  // GEAR_DOWN #1
        Fly(s, 6000, 1200, 3500);  // go-around: above 3000 AGL re-arms
        Fly(s, 3000, -700, 1500);  // GEAR_DOWN #2
        Assert.Equal(new[] { "GEAR_DOWN", "GEAR_DOWN" }, events);
    }

    [Fact]
    public void Ap_EngagesOnceAtConfiguredAgl()
    {
        var s = Make();
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 350;
        OnGround(s);
        Fly(s, 300, 900, 300);   // below engage height
        Fly(s, 500, 900, 400);   // through 350
        Fly(s, 700, 900, 600);
        Assert.Equal(new[] { "AUTOPILOT_ON" }, events);
        Assert.Contains("350 feet. Autopilot engaged.", spoken);
    }

    // When an aircraft-routed AP-engage delegate is supplied (PMDG CMD A / A/P L), it is
    // used instead of the stock AUTOPILOT_ON event.
    [Fact]
    public void Ap_UsesInjectedEngageDelegateWhenProvided()
    {
        int engaged = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add, () => engaged++);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 350;
        OnGround(s);
        Fly(s, 300, 900, 300);
        Fly(s, 500, 900, 400);   // through 350
        Fly(s, 700, 900, 600);
        Assert.Equal(1, engaged);
        Assert.DoesNotContain("AUTOPILOT_ON", events);   // stock event NOT used
        Assert.Contains("350 feet. Autopilot engaged.", spoken);
    }

    // ---------------------------------------------------------------------
    // Per-aircraft engage floor + closed-loop verification (2026-08 fix).
    //
    // The 737's AFDS inhibits CMD engagement below 400 ft RA after takeoff, so a
    // press at the 350 ft default was silently rejected while the service announced
    // "Autopilot engaged" and latched — never retrying. Aircraft that expose an
    // engaged-readback are now verified and retried; aircraft that don't keep the
    // legacy announce-on-press behavior (the tests above).
    // ---------------------------------------------------------------------

    [Fact]
    public void Ap_HonoursAircraftMinimumEngageAltitudeAboveTheConfiguredOne()
    {
        var s = Make();
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 350;
        s.MinimumApEngageAltitudeAgl = 400;      // PMDG 737 floor
        Assert.Equal(400, s.EffectiveApEngageAltitudeAgl);
        OnGround(s);

        Fly(s, 500, 900, 380);   // through 350 but BELOW the aircraft floor — must not fire
        Assert.Empty(events);

        Fly(s, 700, 900, 420);   // through 400
        Assert.Equal(new[] { "AUTOPILOT_ON" }, events);
        Assert.Contains("400 feet. Autopilot engaged.", spoken);
    }

    [Fact]
    public void Ap_ConfiguredAltitudeWinsWhenAboveTheAircraftMinimum()
    {
        var s = Make();
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 1000;
        s.MinimumApEngageAltitudeAgl = 400;
        Assert.Equal(1000, s.EffectiveApEngageAltitudeAgl);
        OnGround(s);
        Fly(s, 900, 900, 500);
        Assert.Empty(events);
    }

    [Fact]
    public void Ap_WithReadback_AnnouncesOnlyAfterEngagementIsConfirmed()
    {
        bool engaged = false;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        Fly(s, 700, 900, 420);            // press #1 — readback still says not engaged
        Assert.Equal(1, presses);
        Assert.Empty(spoken);               // must NOT claim engagement it can't see

        engaged = true;
        Fly(s, 800, 900, 500);            // readback confirms
        Assert.Equal(1, presses);
        Assert.Contains("400 feet. Autopilot engaged.", spoken);

        Fly(s, 900, 900, 600);            // done — no repeat press or callout
        Assert.Equal(1, presses);
        Assert.Single(spoken);
    }

    [Fact]
    public void Ap_WithReadback_RetriesARejectedPressThenReportsFailure()
    {
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => false);   // never engages
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        for (int i = 0; i < 10; i++) Fly(s, 700 + i * 100, 900, 420 + i * 100);

        Assert.Equal(5, presses);            // bounded retry, not one-shot and not endless
        Assert.Contains(spoken, m => m.Contains("did not engage"));
        Assert.DoesNotContain(spoken, m => m.Contains("Autopilot engaged."));
        // An aircraft that does not wait (WaitForApState off) reaches the give-up only after pressing, so it
        // always gets the "did not engage" line, never the "state unknown" one of a leg that only waited.
        Assert.DoesNotContain(spoken, m => m.Contains("state unknown"));
    }

    [Fact]
    public void Ap_RetryStillResolvesAfterTheClimbFlattensOut()
    {
        bool engaged = false;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        Fly(s, 700, 900, 420);   // press while climbing
        engaged = true;
        Fly(s, 700, 0, 420);     // levelled off — verification must still complete
        Assert.Contains("400 feet. Autopilot engaged.", spoken);
    }

    [Fact]
    public void Ap_PilotAlreadyEngaged_NeverPressesAndStaysSilent()
    {
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => true);    // pilot engaged it before the trigger height
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        Fly(s, 700, 900, 420);
        Fly(s, 800, 900, 500);
        Assert.Equal(0, presses);            // pressing a toggle would DISCONNECT it
        Assert.Empty(spoken);
    }

    [Fact]
    public void Ap_ReadbackUnknown_KeepsAnnounceOnPressBehaviour()
    {
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => null);    // a null readback, and the aircraft does not wait (WaitForApState off)
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        Fly(s, 700, 900, 420);
        Fly(s, 800, 900, 500);
        Assert.Equal(1, presses);
        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, spoken);
    }

    [Fact]
    public void Ap_TouchdownRearmsTheVerifiedEngageForTheNextLeg()
    {
        bool engaged = false;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        Fly(s, 700, 900, 420);
        engaged = true;
        Fly(s, 800, 900, 500);          // leg 1 confirmed
        Fly(s, 0, 0, 5);                // touchdown
        engaged = false;
        Fly(s, 700, 900, 420);          // leg 2 presses again
        Assert.Equal(2, presses);
    }

    [Fact]
    public void Touchdown_ResetsGearUpAndApLatches()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        OnGround(s);
        Fly(s, 500, 800, 400);   // GEAR_UP #1
        Fly(s, 0, 0, 5);         // on ground (AGL<20) after being airborne -> reset
        Fly(s, 500, 800, 400);   // GEAR_UP #2
        Assert.Equal(new[] { "GEAR_UP", "GEAR_UP" }, events);
    }

    [Fact]
    public void Disabled_NeverActuates()
    {
        var s = Make();
        OnGround(s);
        Fly(s, 500, 800, 400);
        Assert.Empty(events);
        Assert.False(s.AnyEnabled);
    }

    // ---------------------------------------------------------------------
    // The takeoff gates (2026-10-18). Gear up and the AP engage fire only on a takeoff the service
    // watched: a ground sample arms them. They once fired on the first climbing sample wherever it
    // came, so an app restart, an aircraft re-pick or a setting switched on during a climb at FL150
    // raised the gear and pressed the autopilot there.
    // ---------------------------------------------------------------------

    [Fact]
    public void InFlightStart_NeitherRaisesTheGearNorPressesTheAutopilot()
    {
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add, () => presses++, () => false);
        s.AutoGearUpEnabled = true;
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 350;

        Fly(s, 15000, 1500, 14000);    // an app restart in a climb at FL150: no ground sample yet
        Fly(s, 15500, 1500, 14500);

        Assert.Empty(events);
        Assert.Equal(0, presses);
        Assert.Empty(spoken);
    }

    [Fact]
    public void GearUp_SwitchedOnInFlight_WaitsForTheNextTakeoff()
    {
        var s = Make();
        OnGround(s);
        Fly(s, 500, 800, 400);           // airborne with the setting off
        s.AutoGearUpEnabled = true;       // switched on in the air
        Fly(s, 1500, 800, 1400);
        Assert.Empty(events);

        Fly(s, 0, 0, 5);                  // the next landing ...
        Fly(s, 500, 800, 400);            // ... and takeoff
        Assert.Equal(new[] { "GEAR_UP" }, events);
    }

    [Fact]
    public void Ap_SwitchedOnInFlight_WaitsForTheNextTakeoff()
    {
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add, () => presses++, () => false);
        s.AutoApEngageAltitudeAgl = 350;
        OnGround(s);
        Fly(s, 300, 900, 200);
        s.AutoApEnabled = true;           // switched on in the air, below the engage height
        Fly(s, 700, 900, 600);            // the climb through 350 was not watched with it on
        Assert.Equal(0, presses);

        Fly(s, 0, 0, 5);
        Fly(s, 700, 900, 600);
        Assert.Equal(1, presses);
    }

    [Fact]
    public void ASettingSwitchedOnWhileOnTheGround_KeepsTheTakeoffArmed()
    {
        var s = Make();
        OnGround(s);
        s.AutoGearUpEnabled = true;       // set at the gate: the takeoff from here is watched
        Fly(s, 500, 800, 400);
        Assert.Equal(new[] { "GEAR_UP" }, events);
    }

    // No position samples come while every feature is off, so the service cannot know whether the
    // aircraft took off meanwhile: a gate switched on then waits for a ground sample.
    [Fact]
    public void MarkGroundStateUnknown_ASettingSwitchedOnAfterwardWaitsForAGroundSample()
    {
        var s = Make();
        OnGround(s);
        s.MarkGroundStateUnknown();       // features off: the samples stop
        s.AutoGearUpEnabled = true;       // switched on later, in the air
        Fly(s, 5000, 800, 4000);
        Assert.Empty(events);
    }

    [Fact]
    public void Reset_DisarmsTheTakeoffGatesUntilTheNextGroundSample()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        OnGround(s);
        s.Reset();                        // an aircraft switch or a flight loaded
        Fly(s, 500, 800, 400);
        Assert.Empty(events);
        Fly(s, 0, 0, 5);
        Fly(s, 500, 800, 400);
        Assert.Equal(new[] { "GEAR_UP" }, events);
    }

    // ---------------------------------------------------------------------
    // Gear down needs a descent (2026-10-18): between 100 and 2,000 ft AGL, at least GearDownDescentFt
    // below the highest point since the gear last came up or the leg began. A single descending sample
    // once lowered it during the climb-out, and a circuit flown below 3,000 ft after the service had
    // lowered the gear never re-armed it.
    // ---------------------------------------------------------------------

    [Fact]
    public void GearDown_ClimbOutWobble_NeverLowersTheGear()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.AutoGearDownEnabled = true;
        OnGround(s);
        Fly(s, 500, 800, 400);            // gear up
        Fly(s, 1600, 800, 1500);
        Fly(s, 1450, -400, 1350);         // a sink at a low level-off: 150 ft below the peak
        Fly(s, 1750, 0, 1650);
        Fly(s, 1550, -300, 1450);         // turbulence: 200 ft below the new peak
        Assert.Equal(new[] { "GEAR_UP" }, events);
    }

    [Fact]
    public void GearDown_NeedsADescentOfThreeHundredFeetBelowThePeak()
    {
        var s = Make();
        s.AutoGearDownEnabled = true;
        Fly(s, 1900, 0, 1800);
        Fly(s, 1600, -500, 1501);         // 299 ft below
        Assert.Empty(events);
        Fly(s, 1600, -500, 1500);         // 300 ft below
        Assert.Equal(new[] { "GEAR_DOWN" }, events);
    }

    [Fact]
    public void GearDown_NeverBelowTheBandBottom()
    {
        var s = Make();
        s.AutoGearDownEnabled = true;
        Fly(s, 1000, 0, 900);
        Fly(s, 200, -700, 90);            // descending, far below the peak, but under 100 ft AGL
        Assert.Empty(events);
    }

    [Fact]
    public void GearDown_TouchAndGo_LowersTheGearAgainOnTheNextApproach()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.AutoGearDownEnabled = true;
        OnGround(s);
        Fly(s, 500, 800, 400);            // gear up #1
        Fly(s, 1600, 800, 1500);          // a circuit at 1,500 ft, never above 3,000
        Fly(s, 1200, -600, 1100);         // gear down #1
        Fly(s, 0, 0, 5);                  // touch-and-go
        Fly(s, 500, 800, 400);            // gear up #2
        Fly(s, 1600, 800, 1500);
        Fly(s, 1200, -600, 1100);         // gear down #2
        Assert.Equal(new[] { "GEAR_UP", "GEAR_DOWN", "GEAR_UP", "GEAR_DOWN" }, events);
    }

    // A low go-around with no touchdown: the gear handle coming up re-arms the gear down and starts a
    // new descent to measure from, though the aircraft never climbs above 3,000 ft.
    [Fact]
    public void GearDown_LowGoAround_TheHandleComingUpRearmsIt()
    {
        bool? handleDown = true;
        var s = new UniversalAutomationService(events.Add, spoken.Add, isGearHandleDown: () => handleDown);
        s.AutoGearDownEnabled = true;
        handleDown = false;
        Fly(s, 1600, 0, 1500);
        Fly(s, 1200, -600, 1100);         // gear down #1
        handleDown = true;
        Fly(s, 400, -600, 300);           // on final ...
        Fly(s, 500, 1500, 400);           // ... go-around, gear still down
        handleDown = false;               // the pilot raises the gear at 400 ft
        Fly(s, 1600, 800, 1500);
        Fly(s, 1200, -600, 1100);         // gear down #2
        Assert.Equal(new[] { "GEAR_DOWN", "GEAR_DOWN" }, events);
    }

    [Fact]
    public void GearDown_HandleAlreadyDown_StaysSilent()
    {
        var s = new UniversalAutomationService(events.Add, spoken.Add, isGearHandleDown: () => true);
        s.AutoGearDownEnabled = true;
        Fly(s, 1900, 0, 1800);
        Fly(s, 1400, -700, 1300);
        Assert.Empty(events);
        Assert.Empty(spoken);             // never "Gear down." over a gear already down
    }

    [Fact]
    public void GearUp_HandleAlreadyUp_StaysSilent()
    {
        var s = new UniversalAutomationService(events.Add, spoken.Add, isGearHandleDown: () => false);
        s.AutoGearUpEnabled = true;
        OnGround(s);
        Fly(s, 500, 800, 400);
        Assert.Empty(events);
        Assert.Empty(spoken);
    }

    [Fact]
    public void UnknownAgl_ActsOnNothing()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.AutoGearDownEnabled = true;
        s.AutoApEnabled = true;
        OnGround(s);
        s.Update(500, 800, double.NaN, onGround: false);
        s.Update(3000, -700, double.NaN, onGround: false);
        Assert.Empty(events);
        Assert.Empty(spoken);
    }

    // ---------------------------------------------------------------------
    // Retry spacing (2026-10-09, FO-21). The FlyByWire AP 1 push is a TOGGLE and its engaged
    // state reaches the cache with the 1 Hz batch: a retry on the next 1 Hz tick could land on
    // an autopilot the first press already engaged and switch it off. An aircraft sets
    // ApRetryIntervalMs; 0 (every test above) keeps the every-tick retry.
    // ---------------------------------------------------------------------

    [Fact]
    public void Ap_WithRetryInterval_DoesNotPressAgainBeforeTheIntervalHasPassed()
    {
        long now = 0;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => false, () => now);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = 3000;
        OnGround(s);

        Fly(s, 700, 900, 420);                 // press #1 at t = 0
        now = 1000; Fly(s, 800, 900, 500);
        now = 2999; Fly(s, 900, 900, 600);
        Assert.Equal(1, presses);                // still settling: a second press could switch it off

        now = 3000; Fly(s, 1000, 900, 700);
        Assert.Equal(2, presses);                // the interval has passed: retry
    }

    [Fact]
    public void Ap_WithRetryInterval_ConfirmsAnEngagementThatArrivesWhileSettling()
    {
        long now = 0;
        bool engaged = false;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged, () => now);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = 3000;
        OnGround(s);

        Fly(s, 700, 900, 420);                 // press at t = 0
        now = 1000; engaged = true;
        Fly(s, 800, 900, 500);                 // the readback catches up inside the interval
        Assert.Equal(1, presses);
        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, spoken);
    }

    [Fact]
    public void Ap_WithRetryInterval_GivesUpOnlyOnceTheIntervalAfterTheFifthPressHasPassed()
    {
        long now = 0;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => false, () => now);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = 3000;
        OnGround(s);

        for (int i = 0; i < 5; i++) { now = i * 3000L; Fly(s, 700, 900, 420); }
        Assert.Equal(5, presses);

        now = 13000; Fly(s, 700, 900, 420);    // 1 s after the fifth press: still settling
        Assert.DoesNotContain(spoken, m => m.Contains("did not engage"));

        now = 15000; Fly(s, 700, 900, 420);    // the interval has passed: report it
        Assert.Equal(5, presses);
        Assert.Contains(spoken, m => m.Contains("did not engage"));
    }

    // ---------------------------------------------------------------------
    // Waiting for a definite state (2026-10-09, FO-21). On the FlyByWire family a null readback only
    // means "not read yet": the AP vars arrive with the 1 Hz batch, so the first tick after an app
    // restart or a mid-climb aircraft detection can come before them. The AP 1 push is a TOGGLE, so
    // a blind press over an autopilot that is already on would switch it off while the service says
    // "Autopilot engaged". With WaitForApState the service never presses on a null reading. A wait is
    // an attempt: the first at the climb through the engage height, then one per ApRetryIntervalMs (a
    // tick inside the interval counts for nothing). If no definite reading comes within
    // MaxApEngageAttempts the Captain call ends the leg: "state unknown" when nothing was pressed,
    // "did not engage" when the service pressed and the autopilot never came on. WaitForApState off
    // keeps the open-loop press on a null readback (Ap_ReadbackUnknown_KeepsAnnounceOnPressBehaviour).
    // ---------------------------------------------------------------------

    private const string StateUnknownCall = "Autopilot state unknown. Captain action required: check the autopilot.";
    private const string DidNotEngageCall = "Autopilot did not engage. Captain action required: engage the autopilot.";

    // The spacing the FlyByWire family sets, and when a leg that attempted at every interval gives up:
    // the last of MaxApEngageAttempts attempts is at (MaxApEngageAttempts - 1) intervals, and the
    // service gives up one interval after it.
    private const int WaitInterval = 3000;
    private const long GiveUpAfterMs = UniversalAutomationService.MaxApEngageAttempts * (long)WaitInterval;

    // The service as the FlyByWire family configures it (WaitForApState on, 3 s spacing, engage at
    // 400 ft), wired to a clock, an engaged-readback and a press counter the test drives, and given
    // the ground sample of a takeoff it watched. The readback starts as "the AP vars are not read yet"
    // (null) unless the test says otherwise.
    private sealed class WaitingRig
    {
        public long Now;
        public bool? Engaged;
        public int Presses;
        public UniversalAutomationService Service = null!;
    }

    private WaitingRig MakeWaiting(bool? engaged = null)
    {
        var rig = new WaitingRig { Engaged = engaged };
        rig.Service = new UniversalAutomationService(events.Add, spoken.Add,
            () => rig.Presses++, () => rig.Engaged, () => rig.Now)
        {
            AutoApEnabled = true,
            AutoApEngageAltitudeAgl = 400,
            ApRetryIntervalMs = WaitInterval,
            WaitForApState = true,
        };
        OnGround(rig.Service);
        return rig;
    }

    [Fact]
    public void Ap_WaitingForState_NeverPressesOnAnUnknownStateAndEndsWithTheStateUnknownCall()
    {
        var rig = MakeWaiting();                 // the AP vars are never read
        var s = rig.Service;

        for (int i = 0; i < UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            rig.Now = i * (long)WaitInterval;
            Fly(s, 700, 900, 420);             // climbing through the engage height, state unknown
        }
        Assert.Equal(0, rig.Presses);            // never presses blind: the autopilot may already be on
        Assert.Empty(events);                    // ... not even the stock event
        Assert.Empty(spoken);                    // ... and says nothing while it waits

        rig.Now = GiveUpAfterMs - 1; Fly(s, 700, 900, 420);   // 1 ms short of the interval after the last attempt
        Assert.Empty(spoken);

        rig.Now = GiveUpAfterMs; Fly(s, 700, 900, 420);       // no definite reading within the attempts: the Captain call
        Assert.Equal(new[] { StateUnknownCall }, spoken);       // nothing was pressed, so it cannot say a press failed
        Assert.DoesNotContain(spoken, m => m.Contains("did not engage"));

        rig.Now = GiveUpAfterMs + WaitInterval; Fly(s, 700, 900, 420);       // the leg is resolved: said once
        rig.Now = GiveUpAfterMs + 2 * WaitInterval; Fly(s, 700, 900, 420);
        Assert.Equal(new[] { StateUnknownCall }, spoken);
        Assert.Equal(0, rig.Presses);
        Assert.Empty(events);
    }

    // The other give-up: the state read a definite OFF, the service pressed, and the presses never took
    // (an engage inhibit, a dropped dispatch). The autopilot is off for certain, so this is the real
    // "did not engage" and the Captain is told to engage it.
    [Fact]
    public void Ap_WaitingForState_PressesThatNeverTakeEndWithTheDidNotEngageCall()
    {
        var rig = MakeWaiting();                 // the AP vars have not arrived yet
        var s = rig.Service;

        Fly(s, 700, 900, 420);                 // t = 0: unknown - waits (attempt 1, nothing pressed)
        Assert.Equal(0, rig.Presses);

        rig.Engaged = false;                     // the batch arrives: both autopilots off, and they stay off
        for (int i = 1; i < UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            rig.Now = i * (long)WaitInterval;
            Fly(s, 700, 900, 420);             // a press at every interval, attempts 2 .. MaxApEngageAttempts
        }
        Assert.Equal(UniversalAutomationService.MaxApEngageAttempts - 1, rig.Presses);
        Assert.Empty(spoken);                    // nothing claimed, nothing reported yet

        rig.Now = GiveUpAfterMs - 1; Fly(s, 700, 900, 420);   // 1 ms short of the interval after the last press
        Assert.Empty(spoken);

        rig.Now = GiveUpAfterMs; Fly(s, 700, 900, 420);
        Assert.Equal(new[] { DidNotEngageCall }, spoken);       // it pressed, so this is the existing line, unchanged
        Assert.DoesNotContain(spoken, m => m.Contains("state unknown"));

        rig.Now = GiveUpAfterMs + WaitInterval; Fly(s, 700, 900, 420);       // the leg is resolved: said once, no more presses
        Assert.Equal(new[] { DidNotEngageCall }, spoken);
        Assert.Equal(UniversalAutomationService.MaxApEngageAttempts - 1, rig.Presses);
    }

    // The give-up follows the LATEST reading, not only whether the service pressed. A press went out and
    // then the state went unknown again: a SimConnect disconnect clears the cache, and only an aircraft
    // switch resets the service. The autopilot may well be on by now, and "engage the autopilot" would
    // disconnect it (the push is a toggle), so a leg whose latest reading is unknown ends on "state
    // unknown". If the reading is back and says off, the press really never took.
    [Theory]
    [InlineData(null, StateUnknownCall)]
    [InlineData(false, DidNotEngageCall)]
    public void Ap_WaitingForState_AfterAPressTheGiveUpFollowsTheLatestReading(bool? latest, string expectedCall)
    {
        var rig = MakeWaiting(engaged: false);   // the AP vars have arrived: both autopilots off
        var s = rig.Service;

        Fly(s, 700, 900, 420);                 // t = 0: a definite OFF - presses (attempt 1)
        Assert.Equal(1, rig.Presses);

        rig.Engaged = null;                      // the cache is cleared: unknown again
        for (int i = 1; i < UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            rig.Now = i * (long)WaitInterval;
            Fly(s, 700, 900, 420);             // waits (attempts 2 .. MaxApEngageAttempts): never presses blind
        }
        Assert.Equal(1, rig.Presses);
        Assert.Empty(spoken);

        rig.Engaged = latest;                    // what the reading says when the service gives up
        rig.Now = GiveUpAfterMs - 1; Fly(s, 700, 900, 420);   // 1 ms short of the interval after the last attempt
        Assert.Empty(spoken);

        rig.Now = GiveUpAfterMs; Fly(s, 700, 900, 420);
        Assert.Equal(new[] { expectedCall }, spoken);
        Assert.Equal(1, rig.Presses);            // the give-up presses nothing
    }

    // An aircraft that does not wait (WaitForApState off) keeps the give-up it always had: it follows
    // whether the service pressed, not the latest reading. Every toggle engage waits.
    [Fact]
    public void Ap_NotWaiting_AGiveUpOnANullReadingStillSaysDidNotEngage()
    {
        long now = 0;
        bool? engaged = false;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged, () => now);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = WaitInterval;      // WaitForApState stays off
        OnGround(s);

        for (int i = 0; i < UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            now = i * (long)WaitInterval;
            Fly(s, 700, 900, 420);             // five presses that never take
        }
        Assert.Equal(UniversalAutomationService.MaxApEngageAttempts, presses);

        engaged = null;                          // the readback is not ready when it gives up
        now = GiveUpAfterMs; Fly(s, 700, 900, 420);
        Assert.Equal(new[] { DidNotEngageCall }, spoken);
        Assert.Equal(UniversalAutomationService.MaxApEngageAttempts, presses);
    }

    [Fact]
    public void Ap_WaitingForState_PressesOnceTheStateReadsOffAndAnnouncesOnlyOnConfirmation()
    {
        var rig = MakeWaiting();                 // the AP vars have not arrived yet
        var s = rig.Service;

        Fly(s, 700, 900, 420);                 // t = 0: unknown - waits
        Assert.Equal(0, rig.Presses);
        Assert.Empty(spoken);

        rig.Engaged = false;                     // the batch arrives: both autopilots off
        rig.Now = WaitInterval; Fly(s, 800, 900, 500);     // the wait's interval has passed: now it presses
        Assert.Equal(1, rig.Presses);
        Assert.Empty(spoken);                    // not claimed until the readback confirms

        rig.Engaged = true;
        rig.Now = WaitInterval + 1000; Fly(s, 900, 900, 600);     // confirmed
        Assert.Equal(1, rig.Presses);
        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, spoken);

        rig.Now = 2 * WaitInterval + 1000; Fly(s, 1000, 900, 700);
        Assert.Single(spoken);                   // once
    }

    [Fact]
    public void Ap_WaitingForState_AutopilotAlreadyOn_NeverPressesAndStaysSilent()
    {
        var rig = MakeWaiting();                 // the state not read yet at the engage height
        var s = rig.Service;

        Fly(s, 700, 900, 420);                 // t = 0: unknown - waits (an attempt, nothing pressed)
        rig.Engaged = true;                      // the batch arrives: the autopilot was already on
        rig.Now = 1000; Fly(s, 800, 900, 500);
        rig.Now = WaitInterval + 1000; Fly(s, 900, 900, 600);

        Assert.Equal(0, rig.Presses);            // pressing the toggle would DISCONNECT it
        Assert.Empty(events);
        Assert.Empty(spoken);                    // and the service engaged nothing, so it claims nothing
    }

    // "Autopilot engaged" is spoken only for an engage the service itself pressed. A waiting attempt
    // presses nothing, so what was pressed on an earlier leg (or before a Reset) must not carry over.
    [Fact]
    public void Ap_WaitingForState_TouchdownClearsTheFactThatALegPressed()
    {
        var rig = MakeWaiting();
        var s = rig.Service;

        Fly(s, 700, 900, 420);                 // leg 1: unknown - waits
        rig.Engaged = false;
        rig.Now = WaitInterval; Fly(s, 800, 900, 500);     // presses
        rig.Engaged = true;
        rig.Now = WaitInterval + 1000; Fly(s, 900, 900, 600);     // confirmed and announced
        Assert.Equal(1, rig.Presses);
        Assert.Single(spoken);

        Fly(s, 0, 0, 5);                       // touchdown

        rig.Engaged = null;
        rig.Now = 10000; Fly(s, 700, 900, 420);    // leg 2: unknown - waits (an attempt, nothing pressed)
        rig.Engaged = true;
        rig.Now = 11000; Fly(s, 800, 900, 500);    // found already on
        Assert.Equal(1, rig.Presses);            // leg 2 pressed nothing
        Assert.Single(spoken);                   // leg 1's call-out only: no second "engaged"
    }

    [Fact]
    public void Ap_WaitingForState_ResetClearsTheFactThatALegPressed()
    {
        var rig = MakeWaiting();
        var s = rig.Service;

        Fly(s, 700, 900, 420);                 // unknown - waits
        rig.Engaged = false;
        rig.Now = WaitInterval; Fly(s, 800, 900, 500);     // presses; not confirmed yet
        Assert.Equal(1, rig.Presses);

        s.Reset();                               // a fresh start, as on an aircraft switch: the takeoff gate disarms

        rig.Engaged = null;
        rig.Now = WaitInterval + 1000; Fly(s, 700, 900, 420);     // nothing attempted: no takeoff watched since the reset
        rig.Engaged = true;
        rig.Now = WaitInterval + 2000; Fly(s, 800, 900, 500);     // found already on
        Assert.Equal(1, rig.Presses);            // only the press from before the reset
        Assert.Empty(spoken);                    // and no "engaged" call-out for it
    }

    // ---------------------------------------------------------------------
    // The success confirmation does not interrupt (2026-10-10, FO-21). The aircraft announces its own
    // "Autopilot 1 on" with its 1 Hz batch, and the service's "{height} feet. Autopilot engaged." follows
    // 0-1 s later: an interrupting confirmation cut the aircraft's call off mid-word. So the confirmation
    // has a delegate of its own (MainForm wires it to the non-interrupting announce); the gear calls and
    // the two Captain calls keep the interrupting one. With no confirmation delegate given, the main
    // one is used, so every caller that does not pass one is unchanged.
    // ---------------------------------------------------------------------

    [Fact]
    public void Ap_ConfirmedEngagement_GoesToTheConfirmationDelegate()
    {
        bool engaged = false;
        var s = MakeSplit(engage: () => { }, engaged: () => engaged);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        Fly(s, 700, 900, 420);                 // press: the readback does not say engaged yet
        engaged = true;
        Fly(s, 800, 900, 500);                 // confirmed

        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, confirmations);
        Assert.Empty(spoken);                    // nothing here interrupts the aircraft's own call
    }

    [Fact]
    public void Ap_OpenLoopPress_ConfirmationGoesToTheConfirmationDelegate()
    {
        int presses = 0;
        var s = MakeSplit(engage: () => presses++, engaged: () => null);   // no readback, and the aircraft does not wait
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        Fly(s, 700, 900, 420);

        Assert.Equal(1, presses);
        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, confirmations);
        Assert.Empty(spoken);
    }

    [Fact]
    public void Ap_DidNotEngageCall_GoesToTheInterruptingDelegate()
    {
        long now = 0;
        var s = MakeSplit(engage: () => { }, engaged: () => false, now: () => now);   // never engages
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = WaitInterval;
        OnGround(s);

        for (int i = 0; i <= UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            now = i * (long)WaitInterval;
            Fly(s, 700, 900, 420);             // five presses that never take, then the give-up
        }

        Assert.Equal(new[] { DidNotEngageCall }, spoken);
        Assert.Empty(confirmations);
    }

    [Fact]
    public void Ap_StateUnknownCall_GoesToTheInterruptingDelegate()
    {
        long now = 0;
        var s = MakeSplit(engage: () => { }, engaged: () => null, now: () => now);   // the state is never read
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = WaitInterval;
        s.WaitForApState = true;
        OnGround(s);

        for (int i = 0; i <= UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            now = i * (long)WaitInterval;
            Fly(s, 700, 900, 420);             // five waits, then the give-up
        }

        Assert.Equal(new[] { StateUnknownCall }, spoken);
        Assert.Empty(confirmations);
    }

    [Fact]
    public void Gear_Calls_GoToTheInterruptingDelegate()
    {
        var s = MakeSplit();
        s.AutoGearUpEnabled = true;
        s.AutoGearDownEnabled = true;
        OnGround(s);

        Fly(s, 500, 800, 400);                 // positive rate: gear up
        Fly(s, 3000, 800, 2500);
        Fly(s, 2000, -700, 1500);              // descending below 2000 ft, 1,000 ft below the peak: gear down

        Assert.Equal(new[] { "Positive rate. Gear up.", "Gear down." }, spoken);
        Assert.Empty(confirmations);
    }

    [Fact]
    public void Ap_WithNoConfirmationDelegate_TheConfirmationUsesTheMainAnnounce()
    {
        var s = new UniversalAutomationService(events.Add, spoken.Add, () => { }, () => null);   // the pre-existing shape
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        OnGround(s);

        Fly(s, 700, 900, 420);

        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, spoken);
    }
}
