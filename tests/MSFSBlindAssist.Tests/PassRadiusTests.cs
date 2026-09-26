using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services;

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
/// <para>⚠ Clearing the cluster was not enough on its own, and the table above was measured
/// without the rest. The gate used to start TRACKING a feature only once it was inside its
/// radius, so its first range was at most the radius and the most a pass could ever close was the
/// radius minus the closest point: 2 m for EHAM's 223 m pier, 13 m for LOWI's 137 m hangar — both
/// short of the <see cref="PassingCalloutGate.MinApproachMetres"/> an arm needs, so the two
/// features x1.5 was chosen to clear could never be called. Tracking now starts at
/// <see cref="PassingCalloutGate.RankRadiusMetres"/> and the radius is applied to the CLOSEST
/// POINT when a pass arms; the simulations below pin both ends of that.</para>
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

    private static AirportFeature Named(FeatureKind kind, string name)
        => new() { Kind = kind, Name = name, Lat = 52.31, Lon = 4.76, Source = FeatureSource.Scenery };

    /// <summary>
    /// A straight taxi past ONE feature at <paramref name="knots"/>, sampled at the monitor's own
    /// poll (<see cref="AirportSurroundingsMonitor.PollMs"/> — never a literal, so the simulation
    /// follows the cadence production runs at; answers other features ask for only ADD samples),
    /// handing the gate only what SurroundingsReport.Rank would — the feature while it lies inside
    /// RankRadiusMetres. The aircraft heads north on a line <paramref name="cpaMetres"/> west of the
    /// feature, from 600 m before its closest point to 600 m past it, so the feature passes off the
    /// right wing. Returns the first callout, or null.
    /// </summary>
    private static NearbyFeature? StraightPass(AirportFeature f, double cpaMetres, double knots)
    {
        var gate = new PassingCalloutGate();
        var t0 = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        double pollS = AirportSurroundingsMonitor.PollMs / 1000.0;
        double step = knots * 1852.0 / 3600.0 * pollS;                      // metres per monitor poll
        NearbyFeature? said = null;
        for (int i = 0; ; i++)
        {
            double along = -600.0 + i * step;                                // negative: the closest point is still ahead
            if (along > 600.0) break;
            double range = Math.Sqrt(cpaMetres * cpaMetres + along * along);
            double rel = Math.Atan2(cpaMetres, -along) * 180.0 / Math.PI;     // relative bearing, heading north
            var ranked = range <= PassingCalloutGate.RankRadiusMetres
                ? new[] { new NearbyFeature(f, range, rel) }
                : Array.Empty<NearbyFeature>();
            var hit = gate.Evaluate(ranked, knots, t0.AddSeconds(pollS * i));
            said ??= hit;
        }
        return said;
    }

    [Theory]
    [InlineData(FeatureKind.Concourse, "Pier C", 223.0)]          // EHAM's furthest pier of its eight
    [InlineData(FeatureKind.Hangar, "Tyrolean Hangar", 137.0)]    // LOWI's furthest hangar of its ten
    [InlineData(FeatureKind.Tower, "Control Tower", 299.0)]       // 1 m inside the widest radius
    public void A_closest_point_just_inside_its_kind_s_radius_is_called_on_a_straight_pass(FeatureKind kind, string name, double cpaMetres)
    {
        // Before tracking started at the rank window none of these could arm: a feature first seen
        // inside its radius can close at most radius - closest point (2 m, 13 m, 1 m), short of the
        // 15 m MinApproachMetres an arm needs.
        var hit = StraightPass(Named(kind, name), cpaMetres, 15.0);
        Assert.NotNull(hit);
        Assert.Equal(cpaMetres, hit!.DistanceMetres, 0);          // reported at its own closest point, to the metre
        Assert.InRange(hit.RelativeBearingDeg, 45.0, 135.0);      // off the right wing
    }

    [Theory]
    [InlineData(FeatureKind.Hangar, 155.0)]       // 5 m outside Hangar's 150 m
    [InlineData(FeatureKind.Concourse, 230.0)]    // 5 m outside Concourse's 225 m
    [InlineData(FeatureKind.Tower, 305.0)]        // 5 m outside Tower's 300 m
    public void A_closest_point_just_outside_its_kind_s_radius_is_still_not_called(FeatureKind kind, double cpaMetres)
        => Assert.Null(StraightPass(Named(kind, "Out Of Reach"), cpaMetres, 15.0));

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
    public void Every_radius_leaves_room_inside_the_rank_window_to_close_MinApproachMetres()
    {
        // AirportSurroundingsMonitor ranks within RankRadiusMetres and hands THAT list to the gate,
        // which starts tracking a feature there. A radius above the window is a number the gate can
        // never see (the widened 300 m tower radius was exactly that trap against the old 250 m
        // literal), and one closer than MinApproachMetres below it would leave a feature passed at
        // its radius no room to show the closing an arm needs.
        foreach (FeatureKind k in Enum.GetValues<FeatureKind>())
            Assert.True(PassingCalloutGate.PassRadiusMetres(k) + PassingCalloutGate.MinApproachMetres <= PassingCalloutGate.RankRadiusMetres,
                $"{k} radius {PassingCalloutGate.PassRadiusMetres(k)} leaves less than {PassingCalloutGate.MinApproachMetres} m below the rank window {PassingCalloutGate.RankRadiusMetres}");
    }
}
