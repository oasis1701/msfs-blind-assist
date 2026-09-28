// The traffic's lead along the route, judged over a few seconds (GroundTrafficLogic.LeadRateMps).
// One-second leads jitter with the pilot's own route projection in a turn, and a single shrinking
// sample released a held "Stop" about a leader pulling away round a bend (VirtualPilot traffic A/B,
// KPHX 08 → A8, 2026-09-24).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class LeadRateTests
{
    private static readonly DateTime T0 = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static List<(DateTime Utc, double AheadM)> Feed(params double[] leads)
    {
        var s = new List<(DateTime Utc, double AheadM)>();
        for (int i = 0; i < leads.Length; i++) GroundTrafficLogic.AddLeadSample(s, T0.AddSeconds(i), leads[i]);
        return s;
    }

    [Fact]
    public void One_shrinking_sample_does_not_turn_a_growing_lead_into_closing()
    {
        // KPHX: 150, 160, 171, 181, then 177 for one sample while the leader is 5 m/s faster.
        double? rate = GroundTrafficLogic.LeadRateMps(Feed(150, 160, 171, 181, 177));
        Assert.NotNull(rate);
        Assert.True(rate >= GroundTrafficLogic.MovingAwayMinOpeningMps);
    }

    [Fact]
    public void A_lead_shrinking_throughout_the_window_is_closing()
        => Assert.True(GroundTrafficLogic.LeadRateMps(Feed(200, 196, 192, 188, 184)) < 0);

    [Fact]
    public void Too_short_a_span_gives_no_rate()
    {
        Assert.Null(GroundTrafficLogic.LeadRateMps(Feed(100)));
        Assert.Null(GroundTrafficLogic.LeadRateMps(Feed(100, 110)));   // 1 s < LeadRateMinSpanSeconds
    }

    [Fact]
    public void Samples_older_than_the_window_are_dropped()
    {
        var s = Feed(0, 10, 20, 30, 40, 50, 60);
        Assert.True((s[^1].Utc - s[0].Utc).TotalSeconds <= GroundTrafficLogic.LeadRateWindowSeconds + 0.05);
        Assert.Equal(10.0, GroundTrafficLogic.LeadRateMps(s)!.Value, 6);
    }

    [Fact]
    public void A_ten_second_gap_restarts_the_history()
    {
        var s = Feed(100, 110, 120);
        GroundTrafficLogic.AddLeadSample(s, T0.AddSeconds(15), 50);
        Assert.Single(s);
        Assert.Null(GroundTrafficLogic.LeadRateMps(s));
    }
}
