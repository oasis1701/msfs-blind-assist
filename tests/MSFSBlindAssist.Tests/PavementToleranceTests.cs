// Characterization tests for PavementTolerance — the one definition of "within a taxi
// edge's pavement", shared by off-route detection and RouteReachability so the two can
// never disagree about whether an aircraft is on a taxiway.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class PavementToleranceTests
{
    [Fact]
    public void A_zero_width_row_uses_the_75_foot_default()
    {
        // 75 ft = 22.86 m wide -> 11.43 m half-width + 15 m margin.
        Assert.Equal(26.43, PavementTolerance.ForWidthFeet(0), 2);
    }

    [Fact]
    public void A_narrow_edge_never_drops_below_the_25_metre_floor()
    {
        // 20 ft -> 3.048 m half-width + 15 = 18.05 m, below the floor.
        Assert.Equal(25.0, PavementTolerance.ForWidthFeet(20), 6);
    }

    [Fact]
    public void A_wide_edge_adds_the_margin_to_its_half_width()
    {
        // 100 ft -> 15.24 m half-width + 15 m.
        Assert.Equal(30.24, PavementTolerance.ForWidthFeet(100), 2);
    }

    [Fact]
    public void A_bogus_huge_width_is_capped_at_300_feet()
    {
        // 4000 ft rows exist on mis-tagged aprons; capped to 300 ft -> 45.72 + 15.
        Assert.Equal(60.72, PavementTolerance.ForWidthFeet(4000), 2);
        Assert.Equal(PavementTolerance.ForWidthFeet(300), PavementTolerance.ForWidthFeet(4000), 9);
    }
}
