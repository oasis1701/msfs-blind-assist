using MSFSBlindAssist.Services;
using Call = MSFSBlindAssist.Services.LandingFlareAssistManager.FlareCall;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When the manual landing assist says "Flare" (LandingFlareAssistManager.DecideFlareCallout, ROL-43).
/// </summary>
public class LandingFlareCalloutTests
{
    private static Call Decide(double gear, double flareFt, double vs, double sinceEngage = 10.0,
        double engagePhrase = 1.0, bool said = false)
        => LandingFlareAssistManager.DecideFlareCallout(said, gear, flareFt, vs, sinceEngage, engagePhrase);

    [Fact]
    public void ItIsSaidAtTheFlareHeightLedBySpeechLatency()
    {
        // DA40: 15 ft at 360 fpm (6 ft/s): 0.3 s ahead is 16.8 ft.
        Assert.Equal(Call.Wait, Decide(17.0, 15.0, -360.0));
        Assert.Equal(Call.Now, Decide(16.7, 15.0, -360.0));
    }

    [Fact]
    public void ItIsSaidOnceAndNeverClimbingOrLevel()
    {
        Assert.Equal(Call.Wait, Decide(14.0, 15.0, -360.0, said: true));
        Assert.Equal(Call.Wait, Decide(14.0, 15.0, 0.0));
        Assert.Equal(Call.Wait, Decide(14.0, 15.0, 150.0));
    }

    [Fact]
    public void ItQueuesBehindAnEngageSentenceStillBeingSpoken()
    {
        // 777: engaged at 50 ft at 700 fpm, flare height 40 ft, due about half a second later. The
        // runway correction is still being said, so "Flare" follows it rather than cutting it off.
        Assert.Equal(Call.AfterEngagePhrase, Decide(43.0, 40.0, -700.0, sinceEngage: 0.5, engagePhrase: 3.0));
        Assert.Equal(Call.Now, Decide(43.0, 40.0, -700.0, sinceEngage: 3.1, engagePhrase: 3.0));
        // A silent engage (visual guidance spoke) has no sentence to wait for.
        Assert.Equal(Call.Now, Decide(43.0, 40.0, -700.0, sinceEngage: double.PositiveInfinity, engagePhrase: 0.0));
    }

    [Fact]
    public void TooFarBelowTheFlareHeightItIsDropped()
    {
        Assert.Equal(Call.Now, Decide(7.5, 15.0, -100.0));
        Assert.Equal(Call.TooLate, Decide(6.9, 15.0, -100.0));
    }
}
