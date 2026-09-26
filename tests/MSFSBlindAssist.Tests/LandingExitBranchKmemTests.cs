// KMEM 36L exit list after branch measurement (the landing of 2026-09-26).
// Before the fix this list read: M5 4,404 ft High-speed 14°, M6 6,596 ft Normal 52° (the 18R arm,
// bearing ~127° true), M7 7,334 ft High-speed 15°, M8 8,840 ft End 71°.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitBranchKmemTests
{
    private static List<LandingExit> Exits()
        => KmemRunway36LFixture.BuildGraph().GetLandingExits(KmemRunway36LFixture.Runway36L());

    private static LandingExit Exit(string name) => Exits().Single(e => e.TaxiwayName == name);

    private static double RelativeBearing(LandingExit e)
    {
        double d = (e.ExitBearingTrue == 360.0 ? 0.0 : e.ExitBearingTrue) - KmemRunway36LFixture.RunwayHeadingTrue;
        while (d > 180) d -= 360;
        while (d < -180) d += 360;
        return d;
    }

    [Fact]
    public void The_list_is_M5_to_M8_in_order()
        => Assert.Equal(new[] { "M5", "M6", "M7", "M8" }, Exits().Select(e => e.TaxiwayName).ToArray());

    [Fact]
    public void M6_is_its_36L_branch_not_the_18R_one()
    {
        var m6 = Exit("M6");
        Assert.InRange(m6.DistanceFromThresholdFeet, 6245.0, 6290.0);
        Assert.Equal("Normal", m6.ExitType);
        Assert.InRange(m6.ExitAngleDegrees, 71.5, 74.5);
        Assert.Equal("Right", m6.ExitSide);
        Assert.InRange(RelativeBearing(m6), 0.0, 90.0);
    }

    [Fact]
    public void M5_is_a_normal_exit_not_a_high_speed_one()
    {
        var m5 = Exit("M5");
        Assert.InRange(m5.DistanceFromThresholdFeet, 4385.0, 4425.0);
        Assert.Equal("Normal", m5.ExitType);
        Assert.InRange(m5.ExitAngleDegrees, 71.0, 74.0);
    }

    [Fact]
    public void M7_stays_a_high_speed_exit_at_its_centerline_junction()
    {
        var m7 = Exit("M7");
        Assert.InRange(m7.DistanceFromThresholdFeet, 7315.0, 7355.0);
        Assert.Equal("High-speed", m7.ExitType);
        Assert.InRange(m7.ExitAngleDegrees, 21.0, 24.5);
    }

    [Fact]
    public void M8_moves_to_where_its_branch_leaves_the_runway()
    {
        var m8 = Exit("M8");
        Assert.InRange(m8.DistanceFromThresholdFeet, 8650.0, 8690.0);
        Assert.Equal("End", m8.ExitType); // past 85% of the 9,310 ft runway, as before
        Assert.InRange(m8.ExitAngleDegrees, 52.5, 55.5);
    }

    [Fact]
    public void Every_listed_exit_turns_forward()
        => Assert.All(Exits(), e => Assert.InRange(RelativeBearing(e), -110.0, 110.0));

    [Fact]
    public void Rescue_scan_past_the_old_M6_node_offers_M7_then_M8()
    {
        var found = KmemRunway36LFixture.BuildGraph()
            .FindDownfieldExits(KmemRunway36LFixture.Runway36L(), afterDistanceFromThresholdFeet: 6707.0);
        Assert.Equal(new[] { "M7", "M8" }, found.Select(e => e.TaxiwayName).ToArray());
    }

    [Fact]
    public void Rescue_scan_never_offers_the_18R_branch_of_M6()
    {
        var found = KmemRunway36LFixture.BuildGraph()
            .FindDownfieldExits(KmemRunway36LFixture.Runway36L(), afterDistanceFromThresholdFeet: 6000.0);
        var m6 = Assert.Single(found, e => e.TaxiwayName == "M6");
        Assert.InRange(m6.DistanceFromThresholdFeet, 6245.0, 6290.0);
    }
}
