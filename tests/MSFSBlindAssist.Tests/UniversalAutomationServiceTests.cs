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

    [Fact]
    public void GearUp_FiresOnceOnPositiveRateAboveFiftyFeet()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.Update(altitudeMsl: 500, verticalSpeedFpm: 800, altitudeAgl: 100);
        s.Update(altitudeMsl: 700, verticalSpeedFpm: 800, altitudeAgl: 300);
        Assert.Equal(new[] { "GEAR_UP" }, events);
    }

    [Fact]
    public void GearUp_DoesNotFireBelowFiftyFeetOrWhenLevel()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.Update(200, 800, 40);    // too low
        s.Update(200, 0,   400);   // not climbing
        Assert.Empty(events);
    }

    [Fact]
    public void GearDown_FiresOnceDescendingThroughTwoThousandAgl()
    {
        var s = Make();
        s.AutoGearDownEnabled = true;
        s.Update(5000, 800, 5000);   // airborne, clear on-ground latch
        s.Update(3000, -700, 1500);  // descending through the window
        s.Update(2800, -700, 1200);
        Assert.Equal(new[] { "GEAR_DOWN" }, events);
    }

    [Fact]
    public void GearDown_RearmsAboveThreeThousandForGoAround()
    {
        var s = Make();
        s.AutoGearDownEnabled = true;
        s.Update(5000, 800, 5000);
        s.Update(3000, -700, 1500);  // GEAR_DOWN #1
        s.Update(6000, 1200, 3500);  // go-around: above 3000 AGL re-arms
        s.Update(3000, -700, 1500);  // GEAR_DOWN #2
        Assert.Equal(new[] { "GEAR_DOWN", "GEAR_DOWN" }, events);
    }

    [Fact]
    public void Ap_EngagesOnceAtConfiguredAgl()
    {
        var s = Make();
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 350;
        s.Update(300, 900, 300);   // below engage height
        s.Update(500, 900, 400);   // through 350
        s.Update(700, 900, 600);
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
        s.Update(300, 900, 300);
        s.Update(500, 900, 400);   // through 350
        s.Update(700, 900, 600);
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

        s.Update(500, 900, 380);   // through 350 but BELOW the aircraft floor — must not fire
        Assert.Empty(events);

        s.Update(700, 900, 420);   // through 400
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
        s.Update(900, 900, 500);
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

        s.Update(700, 900, 420);            // press #1 — readback still says not engaged
        Assert.Equal(1, presses);
        Assert.Empty(spoken);               // must NOT claim engagement it can't see

        engaged = true;
        s.Update(800, 900, 500);            // readback confirms
        Assert.Equal(1, presses);
        Assert.Contains("400 feet. Autopilot engaged.", spoken);

        s.Update(900, 900, 600);            // done — no repeat press or callout
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

        for (int i = 0; i < 10; i++) s.Update(700 + i * 100, 900, 420 + i * 100);

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

        s.Update(700, 900, 420);   // press while climbing
        engaged = true;
        s.Update(700, 0, 420);     // levelled off — verification must still complete
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

        s.Update(700, 900, 420);
        s.Update(800, 900, 500);
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

        s.Update(700, 900, 420);
        s.Update(800, 900, 500);
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

        s.Update(700, 900, 420);
        engaged = true;
        s.Update(800, 900, 500);          // leg 1 confirmed
        s.Update(0, 0, 5);                // touchdown
        engaged = false;
        s.Update(700, 900, 420);          // leg 2 presses again
        Assert.Equal(2, presses);
    }

    [Fact]
    public void Touchdown_ResetsGearUpAndApLatches()
    {
        var s = Make();
        s.AutoGearUpEnabled = true;
        s.Update(500, 800, 400);   // GEAR_UP #1
        s.Update(0, 0, 5);         // on ground (AGL<20) after being airborne -> reset
        s.Update(500, 800, 400);   // GEAR_UP #2
        Assert.Equal(new[] { "GEAR_UP", "GEAR_UP" }, events);
    }

    [Fact]
    public void Disabled_NeverActuates()
    {
        var s = Make();
        s.Update(500, 800, 400);
        Assert.Empty(events);
        Assert.False(s.AnyEnabled);
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

        s.Update(700, 900, 420);                 // press #1 at t = 0
        now = 1000; s.Update(800, 900, 500);
        now = 2999; s.Update(900, 900, 600);
        Assert.Equal(1, presses);                // still settling: a second press could switch it off

        now = 3000; s.Update(1000, 900, 700);
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

        s.Update(700, 900, 420);                 // press at t = 0
        now = 1000; engaged = true;
        s.Update(800, 900, 500);                 // the readback catches up inside the interval
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

        for (int i = 0; i < 5; i++) { now = i * 3000L; s.Update(700, 900, 420); }
        Assert.Equal(5, presses);

        now = 13000; s.Update(700, 900, 420);    // 1 s after the fifth press: still settling
        Assert.DoesNotContain(spoken, m => m.Contains("did not engage"));

        now = 15000; s.Update(700, 900, 420);    // the interval has passed: report it
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
    // 400 ft), wired to a clock, an engaged-readback and a press counter the test drives. The
    // readback starts as "the AP vars are not read yet" (null) unless the test says otherwise.
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
            s.Update(700, 900, 420);             // climbing through the engage height, state unknown
        }
        Assert.Equal(0, rig.Presses);            // never presses blind: the autopilot may already be on
        Assert.Empty(events);                    // ... not even the stock event
        Assert.Empty(spoken);                    // ... and says nothing while it waits

        rig.Now = GiveUpAfterMs - 1; s.Update(700, 900, 420);   // 1 ms short of the interval after the last attempt
        Assert.Empty(spoken);

        rig.Now = GiveUpAfterMs; s.Update(700, 900, 420);       // no definite reading within the attempts: the Captain call
        Assert.Equal(new[] { StateUnknownCall }, spoken);       // nothing was pressed, so it cannot say a press failed
        Assert.DoesNotContain(spoken, m => m.Contains("did not engage"));

        rig.Now = GiveUpAfterMs + WaitInterval; s.Update(700, 900, 420);       // the leg is resolved: said once
        rig.Now = GiveUpAfterMs + 2 * WaitInterval; s.Update(700, 900, 420);
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

        s.Update(700, 900, 420);                 // t = 0: unknown - waits (attempt 1, nothing pressed)
        Assert.Equal(0, rig.Presses);

        rig.Engaged = false;                     // the batch arrives: both autopilots off, and they stay off
        for (int i = 1; i < UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            rig.Now = i * (long)WaitInterval;
            s.Update(700, 900, 420);             // a press at every interval, attempts 2 .. MaxApEngageAttempts
        }
        Assert.Equal(UniversalAutomationService.MaxApEngageAttempts - 1, rig.Presses);
        Assert.Empty(spoken);                    // nothing claimed, nothing reported yet

        rig.Now = GiveUpAfterMs - 1; s.Update(700, 900, 420);   // 1 ms short of the interval after the last press
        Assert.Empty(spoken);

        rig.Now = GiveUpAfterMs; s.Update(700, 900, 420);
        Assert.Equal(new[] { DidNotEngageCall }, spoken);       // it pressed, so this is the existing line, unchanged
        Assert.DoesNotContain(spoken, m => m.Contains("state unknown"));

        rig.Now = GiveUpAfterMs + WaitInterval; s.Update(700, 900, 420);       // the leg is resolved: said once, no more presses
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

        s.Update(700, 900, 420);                 // t = 0: a definite OFF - presses (attempt 1)
        Assert.Equal(1, rig.Presses);

        rig.Engaged = null;                      // the cache is cleared: unknown again
        for (int i = 1; i < UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            rig.Now = i * (long)WaitInterval;
            s.Update(700, 900, 420);             // waits (attempts 2 .. MaxApEngageAttempts): never presses blind
        }
        Assert.Equal(1, rig.Presses);
        Assert.Empty(spoken);

        rig.Engaged = latest;                    // what the reading says when the service gives up
        rig.Now = GiveUpAfterMs - 1; s.Update(700, 900, 420);   // 1 ms short of the interval after the last attempt
        Assert.Empty(spoken);

        rig.Now = GiveUpAfterMs; s.Update(700, 900, 420);
        Assert.Equal(new[] { expectedCall }, spoken);
        Assert.Equal(1, rig.Presses);            // the give-up presses nothing
    }

    // An aircraft that does not wait (WaitForApState off) keeps the give-up it always had: it follows
    // whether the service pressed, not the latest reading. Only the FlyByWire family waits.
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

        for (int i = 0; i < UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            now = i * (long)WaitInterval;
            s.Update(700, 900, 420);             // five presses that never take
        }
        Assert.Equal(UniversalAutomationService.MaxApEngageAttempts, presses);

        engaged = null;                          // the readback is not ready when it gives up
        now = GiveUpAfterMs; s.Update(700, 900, 420);
        Assert.Equal(new[] { DidNotEngageCall }, spoken);
        Assert.Equal(UniversalAutomationService.MaxApEngageAttempts, presses);
    }

    [Fact]
    public void Ap_WaitingForState_PressesOnceTheStateReadsOffAndAnnouncesOnlyOnConfirmation()
    {
        var rig = MakeWaiting();                 // the AP vars have not arrived yet
        var s = rig.Service;

        s.Update(700, 900, 420);                 // t = 0: unknown - waits
        Assert.Equal(0, rig.Presses);
        Assert.Empty(spoken);

        rig.Engaged = false;                     // the batch arrives: both autopilots off
        rig.Now = WaitInterval; s.Update(800, 900, 500);     // the wait's interval has passed: now it presses
        Assert.Equal(1, rig.Presses);
        Assert.Empty(spoken);                    // not claimed until the readback confirms

        rig.Engaged = true;
        rig.Now = WaitInterval + 1000; s.Update(900, 900, 600);     // confirmed
        Assert.Equal(1, rig.Presses);
        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, spoken);

        rig.Now = 2 * WaitInterval + 1000; s.Update(1000, 900, 700);
        Assert.Single(spoken);                   // once
    }

    [Fact]
    public void Ap_WaitingForState_AutopilotAlreadyOn_NeverPressesAndStaysSilent()
    {
        var rig = MakeWaiting();                 // an app restart mid-climb: not read yet
        var s = rig.Service;

        s.Update(700, 900, 420);                 // t = 0: unknown - waits (an attempt, nothing pressed)
        rig.Engaged = true;                      // the batch arrives: the autopilot was already on
        rig.Now = 1000; s.Update(800, 900, 500);
        rig.Now = WaitInterval + 1000; s.Update(900, 900, 600);

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

        s.Update(700, 900, 420);                 // leg 1: unknown - waits
        rig.Engaged = false;
        rig.Now = WaitInterval; s.Update(800, 900, 500);     // presses
        rig.Engaged = true;
        rig.Now = WaitInterval + 1000; s.Update(900, 900, 600);     // confirmed and announced
        Assert.Equal(1, rig.Presses);
        Assert.Single(spoken);

        s.Update(0, 0, 5);                       // touchdown

        rig.Engaged = null;
        rig.Now = 10000; s.Update(700, 900, 420);    // leg 2: unknown - waits (an attempt, nothing pressed)
        rig.Engaged = true;
        rig.Now = 11000; s.Update(800, 900, 500);    // found already on
        Assert.Equal(1, rig.Presses);            // leg 2 pressed nothing
        Assert.Single(spoken);                   // leg 1's call-out only: no second "engaged"
    }

    [Fact]
    public void Ap_WaitingForState_ResetClearsTheFactThatALegPressed()
    {
        var rig = MakeWaiting();
        var s = rig.Service;

        s.Update(700, 900, 420);                 // unknown - waits
        rig.Engaged = false;
        rig.Now = WaitInterval; s.Update(800, 900, 500);     // presses; not confirmed yet
        Assert.Equal(1, rig.Presses);

        s.Reset();                               // a fresh start, as on an aircraft switch

        rig.Engaged = null;
        rig.Now = WaitInterval + 1000; s.Update(700, 900, 420);     // unknown again - waits (an attempt, nothing pressed)
        rig.Engaged = true;
        rig.Now = WaitInterval + 2000; s.Update(800, 900, 500);     // found already on
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

        s.Update(700, 900, 420);                 // press: the readback does not say engaged yet
        engaged = true;
        s.Update(800, 900, 500);                 // confirmed

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

        s.Update(700, 900, 420);

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

        for (int i = 0; i <= UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            now = i * (long)WaitInterval;
            s.Update(700, 900, 420);             // five presses that never take, then the give-up
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

        for (int i = 0; i <= UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            now = i * (long)WaitInterval;
            s.Update(700, 900, 420);             // five waits, then the give-up
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

        s.Update(500, 800, 400);                 // positive rate: gear up
        s.Update(3000, -700, 1500);              // descending through 2000 ft: gear down

        Assert.Equal(new[] { "Positive rate. Gear up.", "Two thousand feet. Gear down." }, spoken);
        Assert.Empty(confirmations);
    }

    [Fact]
    public void Ap_WithNoConfirmationDelegate_TheConfirmationUsesTheMainAnnounce()
    {
        var s = new UniversalAutomationService(events.Add, spoken.Add, () => { }, () => null);   // the pre-existing shape
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;

        s.Update(700, 900, 420);

        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, spoken);
    }
}
