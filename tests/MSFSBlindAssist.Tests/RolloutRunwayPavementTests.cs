// The runway being landed on always counts as pavement for the "Off pavement." alert
// (TaxiGuidanceManager.IsOnRolloutRunwayPavement): laterally through IsWithinRolloutRunwayLaterally, and
// along-track through TaxiGuidanceManager.IsWithinRunwayLength, pinned here. KDEN 07/25 has no start row
// for 07, so the graph carries no centerline for it and the pavement map alone called the whole runway
// grass: a simulated centerline landing roll on 25 drew "Off pavement." 4 s after touchdown, at 122 kt.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RolloutRunwayPavementTests
{
    private const double LengthFt = 9310.0;                                   // KMEM 36L
    private const double MarginFt = PavementMap.RunwayMarginMetres / 0.3048;  // 10 m

    private static bool WithinLength(Runway rwy, double alongFt, double lateralM = 0.0)
    {
        var (lat, lon) = KmemRunway36LFixture.PointAt(alongFt, lateralM);
        return TaxiGuidanceManager.IsWithinRunwayLength(rwy, rwy.Heading, lat, lon);
    }

    [Fact]
    public void The_whole_length_counts_with_ten_metres_beyond_each_end()
    {
        var rwy = KmemRunway36LFixture.Runway36L();
        Assert.True(WithinLength(rwy, 0));
        Assert.True(WithinLength(rwy, 5000));
        Assert.True(WithinLength(rwy, LengthFt));
        Assert.True(WithinLength(rwy, -(MarginFt - 1)));
        Assert.False(WithinLength(rwy, -(MarginFt + 1)));
        Assert.True(WithinLength(rwy, LengthFt + MarginFt - 1));
        Assert.False(WithinLength(rwy, LengthFt + MarginFt + 1));
    }

    [Fact]
    public void A_row_without_a_length_falls_back_to_threshold_to_threshold()
    {
        // RunwayFrame's fallback (~2,839 m here), never the raw 0, which would call everything more than
        // 10 m past the start grass — and length-0 rows are the ones that reach the runway-end countdown.
        var rwy = KmemRunway36LFixture.Runway36L();
        rwy.Length = 0;
        Assert.True(WithinLength(rwy, 5000));
        Assert.True(WithinLength(rwy, LengthFt));
        Assert.False(WithinLength(rwy, LengthFt + 100));
    }

    [Fact]
    public void It_is_the_along_track_half_only()
        => Assert.True(WithinLength(KmemRunway36LFixture.Runway36L(), 5000, lateralM: 200));

    [Fact]
    public void Without_a_start_row_at_one_end_the_map_alone_calls_the_runway_grass()
    {
        // KDEN's shape on the KMEM fixture: drop the 18R start row, and 36L/18R gets no centerline.
        var starts = KmemRunway36LFixture.Starts().Where(s => s.RunwayName == "36L").ToList();
        var rwy = KmemRunway36LFixture.Runway36L();
        var graph = TaxiGraph.Build(KmemRunway36LFixture.Paths(), new List<ParkingSpot>(), starts,
            new[] { rwy, KmemRunway36LFixture.Runway18R() });
        Assert.Empty(graph.RunwayCenterlines);

        var (lat, lon) = KmemRunway36LFixture.PointAt(3000, 0);   // mid-runway, on the centerline
        Assert.False(PavementMap.Build(graph).IsOnMappedPavement(lat, lon));
        Assert.True(TaxiGuidanceManager.IsWithinRunwayLength(rwy, rwy.Heading, lat, lon));
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(0.0, rwy.Width));
    }

    [Theory]
    // ZBAT: an asphalt runway recorded 546 ft wide. Capped at 400 ft for the alert: 61 m + 10 m = 71 m.
    [InlineData(80.0, 546.0, 4, false)]
    [InlineData(70.0, 546.0, 4, true)]
    // A grass field that wide can be real, and keeps its width: 83.2 m + 10 m.
    [InlineData(80.0, 546.0, 1, true)]
    // An ordinary width is exactly the rollout's own line (the complement of IsLaterallyClearOfRunway).
    [InlineData(32.8, 150.0, 4, true)]
    [InlineData(32.95, 150.0, 4, false)]
    public void The_alert_caps_a_malformed_paved_runways_width(
        double absLateralM, double widthFt, int surface, bool within)
    {
        Assert.Equal(within, RolloutExitGate.IsWithinRunwayPavementLaterally(absLateralM, widthFt, surface));
        if (widthFt <= 400.0)
            Assert.Equal(within, !RolloutExitGate.IsLaterallyClearOfRunway(absLateralM, widthFt));
    }
}
