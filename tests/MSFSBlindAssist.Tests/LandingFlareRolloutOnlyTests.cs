using System.Globalization;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The landing assist's rollout-only cue (armed by a destination runway or a landing exit, no flare
/// phase): when a touchdown engages it (LandingFlareAssistManager.ShouldEngageRolloutOnly), and the
/// landing_assist.log trace line it writes (FormatTraceLine). KSEA 16R, 2026-10-15: an A300 autoland
/// left the runway at 70 kt with no centerline cue, because none was armed for an autoland.
/// </summary>
public class LandingFlareRolloutOnlyTests
{
    [Theory]
    [InlineData(LandingRunwayVerdict.Matches)]
    [InlineData(LandingRunwayVerdict.ReciprocalEnd)]
    [InlineData(LandingRunwayVerdict.DifferentRunway)]
    public void A_real_touchdown_at_the_armed_airport_engages(LandingRunwayVerdict verdict)
        => Assert.True(LandingFlareAssistManager.ShouldEngageRolloutOnly(true, 40.0, verdict));

    // A stale destination (armed for KSEA, landed at another airport) must never pan the pilot toward
    // a centerline that is not under the wheels.
    [Fact]
    public void A_touchdown_off_the_armed_airport_never_engages()
        => Assert.False(LandingFlareAssistManager.ShouldEngageRolloutOnly(true, 137.0, LandingRunwayVerdict.Unknown));

    // A fast taxi or a rejected takeoff never left the ground: not a landing.
    [Fact]
    public void Without_flying_first_nothing_engages()
        => Assert.False(LandingFlareAssistManager.ShouldEngageRolloutOnly(false, 137.0, LandingRunwayVerdict.Matches));

    // The landing-exit planner's real-landing floor: below it, a teleport or a taxi onto the ground.
    [Fact]
    public void Below_40_kt_nothing_engages()
        => Assert.False(LandingFlareAssistManager.ShouldEngageRolloutOnly(true, 39.9, LandingRunwayVerdict.Matches));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_trace_line_is_invariant_whatever_the_culture(string culture)
    {
        Assert.Equal("47.459977,-122.317925,86.0,3.50,-10.3,-0.23,-3.73,-0.75,1,cue",
            UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceLine(
                47.459977, -122.317925, 86.04, 3.5, -10.26, -0.23, -3.73, -0.75f, true, false)));
        Assert.EndsWith(",0,exit", UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceLine(
            0, 0, 0, 0, 0, 0, 0, 0f, false, true)));
    }

    // Why the cue ended, or why a touchdown did not engage it, goes to landing_assist.log beside the trace:
    // debug.log rotates within minutes, and the in-sim test plan reads this file.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_trace_event_line_is_invariant_whatever_the_culture(string culture)
    {
        Assert.Equal("=== Rollout end: stopped gs=2.5 ===",
            UnderCulture(culture, () => LandingFlareAssistManager.FormatTraceEvent("Rollout end: stopped", 2.5)));
    }

    private static string UnderCulture(string name, Func<string> render)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
