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
}
