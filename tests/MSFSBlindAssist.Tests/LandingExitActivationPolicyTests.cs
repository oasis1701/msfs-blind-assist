// What a touchdown does to the exit plan the pilot set before they flew.
//
// PR #236 follow-up. Every verdict except "cannot tell which runway this is" starts guidance, and
// having started it the plan is spent. The odd one out used to be spent too: the app said the plan
// was cancelled and refused to look again, off a SINGLE position sample taken at the instant the
// wheels touched. A firm landing that bounces, or a touch-and-go, or a go-around after a sample
// that fell short of the pavement, then flew the next approach with no exit guidance at all and no
// second word about why — on a plan the pilot had deliberately set up.
//
// The plan now survives a touchdown the app could not place, and the pilot is told once.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitActivationPolicyTests
{
    [Theory]
    [InlineData(LandingRunwayVerdict.Matches)]
    [InlineData(LandingRunwayVerdict.ReciprocalEnd)]
    [InlineData(LandingRunwayVerdict.DifferentRunway)]
    public void A_landing_that_starts_guidance_uses_the_plan_up(LandingRunwayVerdict verdict)
        => Assert.True(LandingExitActivationPolicy.ConsumesPlan(verdict));

    [Fact]
    public void A_touchdown_the_app_could_not_place_leaves_the_plan_set_for_the_next_one()
        => Assert.False(LandingExitActivationPolicy.ConsumesPlan(LandingRunwayVerdict.Unknown));

    [Fact]
    public void The_pilot_is_told_once_that_the_runway_was_not_identified()
    {
        Assert.True(LandingExitActivationPolicy.AnnouncesUnidentifiedRunway(
            LandingRunwayVerdict.Unknown, alreadySaidThisPlan: false));
        Assert.False(LandingExitActivationPolicy.AnnouncesUnidentifiedRunway(
            LandingRunwayVerdict.Unknown, alreadySaidThisPlan: true));
    }

    [Theory]
    [InlineData(LandingRunwayVerdict.Matches)]
    [InlineData(LandingRunwayVerdict.ReciprocalEnd)]
    [InlineData(LandingRunwayVerdict.DifferentRunway)]
    public void Nothing_is_said_about_identification_when_the_runway_was_identified(LandingRunwayVerdict verdict)
        => Assert.False(LandingExitActivationPolicy.AnnouncesUnidentifiedRunway(verdict, alreadySaidThisPlan: false));

    [Fact]
    public void What_the_pilot_hears_no_longer_claims_the_plan_was_thrown_away()
    {
        string message = LandingExitActivationPolicy.UnidentifiedRunwayMessage;

        Assert.DoesNotContain("cancel", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("this landing", message, StringComparison.OrdinalIgnoreCase);
    }
}
