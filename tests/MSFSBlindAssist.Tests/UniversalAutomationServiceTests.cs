using MSFSBlindAssist.Automation;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class UniversalAutomationServiceTests
{
    private readonly List<string> events = new();
    private readonly List<string> spoken = new();
    private UniversalAutomationService Make() => new(events.Add, spoken.Add);

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
            () => presses++, () => null);    // aircraft exposes no engaged state
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
    // "Autopilot engaged". With WaitForApState the service never presses on a null reading: each tick
    // spent waiting is an attempt, spaced by ApRetryIntervalMs, and if no definite reading comes
    // within MaxApEngageAttempts the Captain call ends the leg. WaitForApState off keeps the
    // open-loop press on a null readback (Ap_ReadbackUnknown_KeepsAnnounceOnPressBehaviour).
    // ---------------------------------------------------------------------

    private const string CaptainCall = "Autopilot did not engage. Captain action required: engage the autopilot.";

    [Fact]
    public void Ap_WaitingForState_NeverPressesOnAnUnknownStateAndEndsWithTheCaptainCall()
    {
        long now = 0;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => null, () => now);   // the AP vars are never read
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = 3000;
        s.WaitForApState = true;

        for (int i = 0; i < UniversalAutomationService.MaxApEngageAttempts; i++)
        {
            now = i * 3000L;
            s.Update(700, 900, 420);             // climbing through the engage height, state unknown
        }
        Assert.Equal(0, presses);                // never presses blind: the autopilot may already be on
        Assert.Empty(events);                    // ... not even the stock event
        Assert.Empty(spoken);                    // ... and says nothing while it waits

        now = 14999; s.Update(700, 900, 420);    // 1 ms short of the interval after the last attempt
        Assert.Empty(spoken);

        now = 15000; s.Update(700, 900, 420);    // no definite reading within the attempts: the Captain's
        Assert.Equal(new[] { CaptainCall }, spoken);

        now = 18000; s.Update(700, 900, 420);    // the leg is resolved: said once
        now = 21000; s.Update(700, 900, 420);
        Assert.Equal(new[] { CaptainCall }, spoken);
        Assert.Equal(0, presses);
        Assert.Empty(events);
    }

    [Fact]
    public void Ap_WaitingForState_PressesOnceTheStateReadsOffAndAnnouncesOnlyOnConfirmation()
    {
        long now = 0;
        bool? engaged = null;                    // the AP vars have not arrived yet
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged, () => now);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = 3000;
        s.WaitForApState = true;

        s.Update(700, 900, 420);                 // t = 0: unknown - waits
        Assert.Equal(0, presses);
        Assert.Empty(spoken);

        engaged = false;                         // the batch arrives: both autopilots off
        now = 3000; s.Update(800, 900, 500);     // the wait's interval has passed: now it presses
        Assert.Equal(1, presses);
        Assert.Empty(spoken);                    // not claimed until the readback confirms

        engaged = true;
        now = 4000; s.Update(900, 900, 600);     // confirmed
        Assert.Equal(1, presses);
        Assert.Equal(new[] { "400 feet. Autopilot engaged." }, spoken);

        now = 7000; s.Update(1000, 900, 700);
        Assert.Single(spoken);                   // once
    }

    [Fact]
    public void Ap_WaitingForState_AutopilotAlreadyOn_NeverPressesAndStaysSilent()
    {
        long now = 0;
        bool? engaged = null;                    // an app restart mid-climb: not read yet
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged, () => now);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = 3000;
        s.WaitForApState = true;

        s.Update(700, 900, 420);                 // t = 0: unknown - waits (an attempt, nothing pressed)
        engaged = true;                          // the batch arrives: the autopilot was already on
        now = 1000; s.Update(800, 900, 500);
        now = 4000; s.Update(900, 900, 600);

        Assert.Equal(0, presses);                // pressing the toggle would DISCONNECT it
        Assert.Empty(events);
        Assert.Empty(spoken);                    // and the service engaged nothing, so it claims nothing
    }

    // "Autopilot engaged" is spoken only for an engage the service itself pressed. A waiting attempt
    // presses nothing, so what was pressed on an earlier leg (or before a Reset) must not carry over.
    [Fact]
    public void Ap_WaitingForState_TouchdownClearsTheFactThatALegPressed()
    {
        long now = 0;
        bool? engaged = null;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged, () => now);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = 3000;
        s.WaitForApState = true;

        s.Update(700, 900, 420);                 // leg 1: unknown - waits
        engaged = false;
        now = 3000; s.Update(800, 900, 500);     // presses
        engaged = true;
        now = 4000; s.Update(900, 900, 600);     // confirmed and announced
        Assert.Equal(1, presses);
        Assert.Single(spoken);

        s.Update(0, 0, 5);                       // touchdown

        engaged = null;
        now = 10000; s.Update(700, 900, 420);    // leg 2: unknown - waits (an attempt, nothing pressed)
        engaged = true;
        now = 11000; s.Update(800, 900, 500);    // found already on
        Assert.Equal(1, presses);                // leg 2 pressed nothing
        Assert.Single(spoken);                   // leg 1's call-out only: no second "engaged"
    }

    [Fact]
    public void Ap_WaitingForState_ResetClearsTheFactThatALegPressed()
    {
        long now = 0;
        bool? engaged = null;
        int presses = 0;
        var s = new UniversalAutomationService(events.Add, spoken.Add,
            () => presses++, () => engaged, () => now);
        s.AutoApEnabled = true;
        s.AutoApEngageAltitudeAgl = 400;
        s.ApRetryIntervalMs = 3000;
        s.WaitForApState = true;

        s.Update(700, 900, 420);                 // unknown - waits
        engaged = false;
        now = 3000; s.Update(800, 900, 500);     // presses; not confirmed yet
        Assert.Equal(1, presses);

        s.Reset();                               // a fresh start, as on an aircraft switch

        engaged = null;
        now = 4000; s.Update(700, 900, 420);     // unknown again - waits (an attempt, nothing pressed)
        engaged = true;
        now = 5000; s.Update(800, 900, 500);     // found already on
        Assert.Equal(1, presses);                // only the press from before the reset
        Assert.Empty(spoken);                    // and no "engaged" call-out for it
    }
}
