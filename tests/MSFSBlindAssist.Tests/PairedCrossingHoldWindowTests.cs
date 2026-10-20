// Characterization tests for Navigation.PairedCrossingHoldWindow: when a second Continue counts as
// "cleared across both" for a paired-runway crossing. The window opens a delay after the offer, so a
// key bounce or a quick double press (milliseconds after the first) never clears the second runway's
// stop (PR #324 review).

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class PairedCrossingHoldWindowTests
{
    private static readonly DateTime Offered = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime From = Offered.AddSeconds(2.0);
    private static readonly DateTime Until = Offered.AddSeconds(12.0);

    [Theory]
    [InlineData(0.05)]  // a key bounce
    [InlineData(1.9)]   // a quick "to be sure" double press
    public void A_press_before_the_window_opens_is_not_a_second_clearance(double secondsAfterOffer)
        => Assert.False(PairedCrossingHoldWindow.Accepts(Offered.AddSeconds(secondsAfterOffer), From, Until));

    [Theory]
    [InlineData(2.0)]
    [InlineData(6.0)]
    [InlineData(12.0)]
    public void A_press_inside_the_window_is_the_second_clearance(double secondsAfterOffer)
        => Assert.True(PairedCrossingHoldWindow.Accepts(Offered.AddSeconds(secondsAfterOffer), From, Until));

    [Fact]
    public void A_press_after_the_window_closes_is_not_a_second_clearance()
        => Assert.False(PairedCrossingHoldWindow.Accepts(Offered.AddSeconds(12.1), From, Until));
}
