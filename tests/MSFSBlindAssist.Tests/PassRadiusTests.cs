using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The pass radii, and the measurement that set them.
///
/// <para>Shipped at 150 m (concourse/terminal), 200 m (tower) and 100 m (everything else), the
/// feature said almost nothing on a real taxi. Replaying two RECORDED tracks — a 5.59 km taxi at
/// EHAM and a 4.35 km one at LOWI, both flown by a pilot at 31 Hz, through the production
/// catalog, Rank and gate — it produced ONE callout at EHAM and TWO at LOWI.</para>
///
/// <para>The reason is not the abeam rule and not the closure rule: on both real tracks EVERY
/// announceable feature the aircraft went past was abeam at its closest point. It is the RADIUS.
/// What a taxiing aircraft passes clusters tightly just OUTSIDE the shipped numbers, and the
/// shipped numbers cut through the bottom of that cluster:</para>
///
/// <list type="bullet">
/// <item>EHAM, the eight nearest: 141, 161, 174, 175, 175, 194, 195, 223 m — against a 150 m
/// concourse radius. Taxiway Bravo is Schiphol's OUTER parallel and simply does not come closer
/// to the piers than that.</item>
/// <item>LOWI, the ten nearest: 106, 109, 116, 116, 121, 127, 128, 131, 135, 137 m — against a
/// 100 m hangar radius.</item>
/// </list>
///
/// <para>Measured effect of widening, per kilometre of real taxi:</para>
///
/// <code>
/// radius                    EHAM says   LOWI says    per km
/// 150/200/100 (shipped)             1           2   0.2-0.5
/// x1.5 = 225/300/150                7           7   1.3-1.6
/// x2.0 = 300/400/200                9           9   1.6-2.1
/// x3.0 = 450/600/300               11          10   2.0-2.3
/// x4.0 = 600/800/400               13          10   2.3
/// </code>
///
/// <para>x1.5 is chosen because it captures the WHOLE cluster at both airports (225 m clears
/// EHAM's 223 m furthest pier; 150 m clears LOWI's 137 m furthest hangar) and everything beyond
/// it buys two or three more callouts for a radius that starts naming buildings the pilot is
/// nowhere near. It saturates by x3 at both fields, so there is no case for going further.</para>
///
/// <para>Roughly one callout every 40 seconds of taxiing at 15 kt, and the gate's own limits
/// (10 s globally, 5 minutes per building) cap the worst case regardless.</para>
/// </summary>
public class PassRadiusTests
{
    [Theory]
    [InlineData(FeatureKind.Concourse, 225.0)]
    [InlineData(FeatureKind.Terminal, 225.0)]
    [InlineData(FeatureKind.Tower, 300.0)]
    [InlineData(FeatureKind.Hangar, 150.0)]
    [InlineData(FeatureKind.Fbo, 150.0)]
    [InlineData(FeatureKind.Fuel, 150.0)]
    [InlineData(FeatureKind.Cargo, 150.0)]
    [InlineData(FeatureKind.FireStation, 150.0)]
    public void The_measured_radii(FeatureKind kind, double expected)
        => Assert.Equal(expected, PassingCalloutGate.PassRadiusMetres(kind));

    [Fact]
    public void Every_radius_clears_the_cluster_its_airport_measured()
    {
        // The two numbers the choice rests on. EHAM's furthest pier of the eight-strong cluster
        // sat at 223 m; LOWI's furthest hangar of its ten at 137 m. A radius that does not clear
        // its own cluster leaves the frontage silent, which is the defect this fixes.
        Assert.True(PassingCalloutGate.PassRadiusMetres(FeatureKind.Concourse) > 223.0,
            "EHAM's pier cluster runs out to 223 m");
        Assert.True(PassingCalloutGate.PassRadiusMetres(FeatureKind.Hangar) > 137.0,
            "LOWI's hangar cluster runs out to 137 m");
    }

    [Fact]
    public void A_tower_still_reaches_furthest()
    {
        // Unchanged ordering: a control tower is visible from further away than a pier is
        // meaningful from, and is the one feature a pilot orients by across a whole airfield.
        Assert.True(PassingCalloutGate.PassRadiusMetres(FeatureKind.Tower)
                  > PassingCalloutGate.PassRadiusMetres(FeatureKind.Concourse));
        Assert.True(PassingCalloutGate.PassRadiusMetres(FeatureKind.Concourse)
                  > PassingCalloutGate.PassRadiusMetres(FeatureKind.Hangar));
    }

    [Fact]
    public void No_radius_reaches_beyond_the_rank_window_that_feeds_the_gate()
    {
        // AirportSurroundingsMonitor ranks within 250 m and hands THAT list to the gate, so a
        // pass radius above it is a number the gate can never see — the widened tower radius
        // (300 m) is exactly that trap, and is why the monitor's window moved with these.
        foreach (FeatureKind k in Enum.GetValues<FeatureKind>())
            Assert.True(PassingCalloutGate.PassRadiusMetres(k) <= PassingCalloutGate.RankRadiusMetres,
                $"{k} radius {PassingCalloutGate.PassRadiusMetres(k)} exceeds the rank window {PassingCalloutGate.RankRadiusMetres}");
    }
}
