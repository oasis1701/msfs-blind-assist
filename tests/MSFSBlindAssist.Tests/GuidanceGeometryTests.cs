// Characterization tests for MSFSBlindAssist.Navigation.GuidanceGeometry.
//
// Ports the golden cases from tools/TaxiGuidanceProbe/Program.cs (KATL 2026-06-10
// micro-segment curve, a discrete 90-degree turn, a degenerate-segment robustness
// case, and the KIAH hairpin replica). This is characterization, not spec
// verification: values are taken from the probe / derived by reasoning about the
// source and confirmed by running the tests; if a literal ever disagrees with
// actual output, the test must be corrected to match real output.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class GuidanceGeometryTests
{
    private const double MPD = 111132.0; // metres per degree latitude (matches the source)

    private static (double[] lats, double[] lons) BuildPolyline(
        double lat0, double lon0, (double brgDeg, double lenM)[] legs)
    {
        var lats = new double[legs.Length + 1];
        var lons = new double[legs.Length + 1];
        lats[0] = lat0; lons[0] = lon0;
        for (int i = 0; i < legs.Length; i++)
        {
            double rad = legs[i].brgDeg * Math.PI / 180.0;
            double cosLat = Math.Cos(lats[i] * Math.PI / 180.0);
            lats[i + 1] = lats[i] + legs[i].lenM * Math.Cos(rad) / MPD;
            lons[i + 1] = lons[i] + legs[i].lenM * Math.Sin(rad) / (MPD * cosLat);
        }
        return (lats, lons);
    }

    private static double DistM(double lat1, double lon1, double lat2, double lon2)
    {
        double klat = MPD, klon = MPD * Math.Cos(lat1 * Math.PI / 180.0);
        return Math.Sqrt(Math.Pow((lat2 - lat1) * klat, 2) + Math.Pow((lon2 - lon1) * klon, 2));
    }

    // --- CumulativeTurnDeg: KATL curve replica ------------------------------

    private static readonly (double, double)[] CurveLegs =
    {
        (318, 25), (313, 25), (303, 25), (298, 25), (288, 25), (277, 25), (270, 25),
        (270, 200)
    };

    [Fact]
    public void CumulativeTurnDeg_sums_step_deltas_within_the_window()
    {
        var (lats, lons) = BuildPolyline(33.6350, -84.4150, CurveLegs);

        // First 100 m covers junctions at 25/50/75/100 m: deltas -5,-10,-5,-10 = -30.
        double cum = GuidanceGeometry.CumulativeTurnDeg(lats, lons, 0, lats[0], lons[0], 100.0, out bool discrete);

        Assert.Equal(-30.0, cum, 1.0);
        Assert.False(discrete);
    }

    [Fact]
    public void CumulativeTurnDeg_sums_all_junctions_over_a_wider_window()
    {
        var (lats, lons) = BuildPolyline(33.6350, -84.4150, CurveLegs);

        double cum = GuidanceGeometry.CumulativeTurnDeg(lats, lons, 0, lats[0], lons[0], 175.0, out _);

        Assert.Equal(-48.0, cum, 1.0);
    }

    [Fact]
    public void CumulativeTurnDeg_projects_the_window_start_from_a_mid_segment_position()
    {
        var (lats, lons) = BuildPolyline(33.6350, -84.4150, CurveLegs);
        double rad0 = 318 * Math.PI / 180.0, cl0 = Math.Cos(lats[0] * Math.PI / 180.0);
        double aLat = lats[0] + 12.0 * Math.Cos(rad0) / MPD;
        double aLon = lons[0] + 12.0 * Math.Sin(rad0) / (MPD * cl0);

        // 12 m along leg 0, 90 m window -> reaches junctions at 13/38/63/88 m -> -30 total.
        double cum = GuidanceGeometry.CumulativeTurnDeg(lats, lons, 0, aLat, aLon, 90.0, out bool discrete);

        Assert.Equal(-30.0, cum, 1.0);
        Assert.False(discrete);
    }

    [Fact]
    public void WalkTarget_curve_replica_never_jumps_more_than_8m_per_2m_step()
    {
        var (lats, lons) = BuildPolyline(33.6350, -84.4150, CurveLegs);
        double maxJump = 0;
        (double lat, double lon)? prevTgt = null;
        int seg = 0;

        for (double s = 0; s <= 350; s += 2.0)
        {
            double remaining = s; int i = 0; double aLat = lats[0], aLon = lons[0];
            while (i < CurveLegs.Length && remaining > CurveLegs[i].Item2)
            { remaining -= CurveLegs[i].Item2; i++; }
            if (i < CurveLegs.Length)
            {
                double rad = CurveLegs[i].Item1 * Math.PI / 180.0;
                double cosLat = Math.Cos(lats[i] * Math.PI / 180.0);
                aLat = lats[i] + remaining * Math.Cos(rad) / MPD;
                aLon = lons[i] + remaining * Math.Sin(rad) / (MPD * cosLat);
            }
            else { aLat = lats[^1]; aLon = lons[^1]; }

            while (seg < CurveLegs.Length - 1 && DistM(aLat, aLon, lats[seg + 1], lons[seg + 1]) < 25.0)
                seg++;

            var tgt = GuidanceGeometry.WalkTarget(lats, lons, seg, aLat, aLon, 52.0);
            if (prevTgt is { } p)
                maxJump = Math.Max(maxJump, DistM(p.lat, p.lon, tgt.lat, tgt.lon));
            prevTgt = tgt;
        }

        Assert.True(maxJump < 8.0, $"max frame-to-frame jump was {maxJump:F1} m (old code jumped 70+ m)");
    }

    // --- Discrete 90-degree turn ------------------------------------------

    [Fact]
    public void CumulativeTurnDeg_flags_a_discrete_90_degree_turn()
    {
        var (lats, lons) = BuildPolyline(33.0, -84.0, new (double, double)[] { (0, 100), (90, 100) });

        double cum = GuidanceGeometry.CumulativeTurnDeg(lats, lons, 0, lats[0], lons[0], 150.0, out bool discrete);

        Assert.True(discrete);
        Assert.Equal(90.0, cum, 1.0);
    }

    [Fact]
    public void WalkTarget_wraps_past_a_junction_once_within_look_ahead()
    {
        var (lats, lons) = BuildPolyline(33.0, -84.0, new (double, double)[] { (0, 100), (90, 100) });
        double aLat = lats[0] + 70.0 / MPD; // 70 m up the north leg

        var tgt = GuidanceGeometry.WalkTarget(lats, lons, 0, aLat, lons[0], 52.0);

        double eastM = (tgt.lon - lons[1]) * MPD * Math.Cos(lats[1] * Math.PI / 180.0);
        double northOfJunction = (tgt.lat - lats[1]) * MPD;
        Assert.Equal(22.0, eastM, 1.5);
        Assert.Equal(0.0, northOfJunction, 1.5);
    }

    // --- Straight route ------------------------------------------------------

    [Fact]
    public void CumulativeTurnDeg_is_zero_on_a_straight_route()
    {
        var (lats, lons) = BuildPolyline(33.0, -84.0, new (double, double)[] { (45, 500) });

        double cum = GuidanceGeometry.CumulativeTurnDeg(lats, lons, 0, lats[0], lons[0], 100.0, out bool discrete);

        Assert.Equal(0.0, cum, 0.5);
        Assert.False(discrete);
    }

    [Fact]
    public void WalkTarget_on_a_straight_route_stays_lookahead_distance_ahead()
    {
        var (lats, lons) = BuildPolyline(33.0, -84.0, new (double, double)[] { (45, 500) });

        var tgt = GuidanceGeometry.WalkTarget(lats, lons, 0, lats[0], lons[0], 80.0);

        Assert.Equal(80.0, DistM(lats[0], lons[0], tgt.lat, tgt.lon), 1.0);
    }

    [Fact]
    public void WalkTarget_clamps_to_the_final_node_when_route_remaining_is_shorter_than_lookahead()
    {
        var (lats, lons) = BuildPolyline(33.0, -84.0, new (double, double)[] { (45, 500) });

        var tgt = GuidanceGeometry.WalkTarget(lats, lons, 0, lats[1], lons[1], 80.0);

        Assert.True(DistM(tgt.lat, tgt.lon, lats[1], lons[1]) < 0.5);
    }

    // --- Degenerate (zero-length) segment robustness ------------------------

    private static (double[] lats, double[] lons) BuildDegeneratePolyline()
    {
        // East-going on purpose: a degenerate segment's phantom bearing is atan2(0,0)=0
        // (north). On an east-going route that phantom would inject +-90 deltas and flip
        // hasDiscreteStep, so this genuinely pins the degenerate-segment guards.
        double dLon100 = 100.0 / (MPD * Math.Cos(33.0 * Math.PI / 180.0));
        var lats = new[] { 33.0, 33.0, 33.0, 33.0 };
        var lons = new[] { -84.0, -84.0 + dLon100, -84.0 + dLon100, -84.0 + 2 * dLon100 };
        return (lats, lons);
    }

    [Fact]
    public void WalkTarget_passes_through_a_degenerate_zero_length_segment()
    {
        var (lats, lons) = BuildDegeneratePolyline();

        var tgt = GuidanceGeometry.WalkTarget(lats, lons, 0, lats[0], lons[0], 150.0);

        Assert.Equal(150.0, DistM(lats[0], lons[0], tgt.lat, tgt.lon), 1.0);
    }

    [Fact]
    public void CumulativeTurnDeg_has_no_phantom_turn_from_a_degenerate_bearing()
    {
        var (lats, lons) = BuildDegeneratePolyline();

        double cum = GuidanceGeometry.CumulativeTurnDeg(lats, lons, 0, lats[0], lons[0], 200.0, out bool discrete);

        Assert.Equal(0.0, cum, 0.5);
        Assert.False(discrete);
    }

    [Fact]
    public void WalkTarget_handles_a_degenerate_current_segment_without_NaN()
    {
        var (lats, lons) = BuildDegeneratePolyline();

        // segIdx points at the zero-length joint (seg 1) -- pins the t=1.0 ternary.
        var tgt = GuidanceGeometry.WalkTarget(lats, lons, 1, lats[1], lons[1], 50.0);

        Assert.Equal(50.0, DistM(lats[1], lons[1], tgt.lat, tgt.lon), 1.0);
        Assert.False(double.IsNaN(tgt.lat));
        Assert.False(double.IsNaN(tgt.lon));
    }

    [Fact]
    public void CumulativeTurnDeg_skips_the_degenerate_joint_when_it_is_the_current_segment()
    {
        var (lats, lons) = BuildDegeneratePolyline();

        double cum = GuidanceGeometry.CumulativeTurnDeg(lats, lons, 1, lats[1], lons[1], 200.0, out bool discrete);

        Assert.Equal(0.0, cum, 0.5);
        Assert.False(discrete);
    }

    // --- Hairpin + stationary aircraft (KIAH replica) -----------------------

    [Fact]
    public void WalkTarget_is_stationary_for_a_stationary_aircraft_near_a_hairpin_apex()
    {
        var (lats, lons) = BuildPolyline(29.995, -95.354, new (double, double)[] { (95, 100), (267, 102) });
        double rad = 95 * Math.PI / 180.0, cosLat = Math.Cos(lats[0] * Math.PI / 180.0);
        double aLat = lats[0] + 49.95 * Math.Cos(rad) / MPD;
        double aLon = lons[0] + 49.95 * Math.Sin(rad) / (MPD * cosLat);

        var t1 = GuidanceGeometry.WalkTarget(lats, lons, 0, aLat, aLon, 52.0);
        var t2 = GuidanceGeometry.WalkTarget(lats, lons, 0, aLat, aLon, 52.0);

        Assert.Equal(t1, t2);
    }

    [Fact]
    public void WalkTarget_creeps_smoothly_through_the_old_50m_branch_boundary()
    {
        var (lats, lons) = BuildPolyline(29.995, -95.354, new (double, double)[] { (95, 100), (267, 102) });
        double rad = 95 * Math.PI / 180.0, cosLat = Math.Cos(lats[0] * Math.PI / 180.0);
        (double lat, double lon)? prev = null;
        double maxJump = 0;

        for (double s = 49.0; s <= 51.0; s += 0.05)
        {
            double pLat = lats[0] + s * Math.Cos(rad) / MPD;
            double pLon = lons[0] + s * Math.Sin(rad) / (MPD * cosLat);
            var tgt = GuidanceGeometry.WalkTarget(lats, lons, 0, pLat, pLon, 52.0);
            if (prev is { } p) maxJump = Math.Max(maxJump, DistM(p.lat, p.lon, tgt.lat, tgt.lon));
            prev = tgt;
        }

        Assert.True(maxJump < 1.0, $"max jump was {maxJump:F2} m (old code jumped 102 m at this boundary)");
    }

    // --- ProjectHeadingError (rollout anticipation) -------------------------

    [Fact]
    public void ProjectHeadingError_centres_the_tone_early_when_yawing_toward_target()
    {
        // Turning right at 8 deg/s toward a target 12 deg right: projected error is
        // 12 - 8*1.5 = 0 -> tone centred while still 12 deg short.
        Assert.Equal(0.0, GuidanceGeometry.ProjectHeadingError(12.0, 8.0, 1.5, 30.0), 0.01);
    }

    [Fact]
    public void ProjectHeadingError_is_a_no_op_when_not_yawing()
    {
        Assert.Equal(12.0, GuidanceGeometry.ProjectHeadingError(12.0, 0.0, 1.5, 30.0), 0.01);
    }

    [Fact]
    public void ProjectHeadingError_clamps_against_yaw_rate_noise_spikes()
    {
        Assert.Equal(-30.0, GuidanceGeometry.ProjectHeadingError(0.0, 100.0, 1.5, 30.0), 0.01);
    }
}
