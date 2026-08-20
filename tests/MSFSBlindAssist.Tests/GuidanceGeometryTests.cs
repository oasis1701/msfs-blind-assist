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

    // --- KLAS 2026-08-20 frozen-target replica ------------------------------
    //
    // Real geometry from the KLAS 26R departure-taxi incident (taxi_guidance.log
    // 17:36-17:43): taxiway B's short east piece (55 m, brg 270) ending at the
    // B/B1/C junction, then the long 345 m westward B segment. The aircraft
    // missed the 25 m waypoint capture on a wide corner, the segment index
    // pinned on the short piece, and the walk target froze at junction + 50 m
    // while the pilot orbited it. Coordinates are the logged ones.

    private static readonly double[] KlasBLats = { 36.0775642, 36.0775642, 36.0775604 };
    private static readonly double[] KlasBLons = { -115.1216583, -115.1222763, -115.1261063 };

    [Fact]
    public void HasPassedOntoNextSegment_fires_for_the_KLAS_pinned_aircraft()
    {
        // Logged aircraft position 17:40:23 - stopped 45 m past the junction,
        // ~2 m south of B's centerline, index still on the short east piece.
        Assert.True(GuidanceGeometry.HasPassedOntoNextSegment(
            KlasBLats, KlasBLons, 0, 36.0775466, -115.1227803, 30.0));
    }

    [Fact]
    public void HasPassedOntoNextSegment_fires_for_the_KLAS_orbit_position()
    {
        // Logged position 17:39:11, mid-orbit, ~6 m south of the centerline.
        Assert.True(GuidanceGeometry.HasPassedOntoNextSegment(
            KlasBLats, KlasBLons, 0, 36.0775104, -115.1227790, 30.0));
    }

    [Fact]
    public void HasPassedOntoNextSegment_stays_false_mid_current_segment()
    {
        // Aircraft squarely inside the short east piece: normal tracking,
        // the ordinary advance paths own this.
        Assert.False(GuidanceGeometry.HasPassedOntoNextSegment(
            KlasBLats, KlasBLons, 0, 36.0775642, -115.1219000, 30.0));
    }

    [Fact]
    public void HasPassedOntoNextSegment_stays_false_when_cross_track_exceeds_the_bound()
    {
        // Past the junction but ~51 m south of B - that is a different taxiway
        // (B1's mouth), not progress along B. Off-route detection owns it.
        Assert.False(GuidanceGeometry.HasPassedOntoNextSegment(
            KlasBLats, KlasBLons, 0, 36.0771000, -115.1227803, 30.0));
    }

    [Fact]
    public void HasPassedOntoNextSegment_stays_false_before_the_current_segment_start()
    {
        Assert.False(GuidanceGeometry.HasPassedOntoNextSegment(
            KlasBLats, KlasBLons, 0, 36.0775642, -115.1214000, 30.0));
    }

    [Fact]
    public void HasPassedOntoNextSegment_stays_false_on_the_final_segment()
    {
        Assert.False(GuidanceGeometry.HasPassedOntoNextSegment(
            KlasBLats, KlasBLons, 1, 36.0775604, -115.1262000, 30.0));
    }

    [Fact]
    public void HasPassedOntoNextSegment_treats_a_degenerate_current_segment_as_passed()
    {
        // Restarted-route shape from the same incident: a sub-metre segment 0
        // with the aircraft alongside the real segment that follows it.
        double dLon05 = 0.5 / (MPD * Math.Cos(33.0 * Math.PI / 180.0));
        double dLon60 = 60.0 / (MPD * Math.Cos(33.0 * Math.PI / 180.0));
        var lats = new[] { 33.0, 33.0, 33.0 };
        var lons = new[] { -84.0, -84.0 + dLon05, -84.0 + dLon05 + dLon60 };

        // Aircraft 20 m along the 60 m eastward segment, 3 m north of it.
        Assert.True(GuidanceGeometry.HasPassedOntoNextSegment(
            lats, lons, 0, 33.0 + 3.0 / MPD, -84.0 + dLon05 + 20.0 / (MPD * Math.Cos(33.0 * Math.PI / 180.0)), 30.0));
    }

    [Fact]
    public void WalkTarget_skips_a_short_snap_segment_instead_of_extrapolating_its_axis()
    {
        // Restarted-route failure shape: segment 0 is a 0.5 m snap segment, the
        // route then turns north. With the aircraft well west of the snap point,
        // the old walk projected onto the 0.5 m axis (t ~ -110) and interpolated
        // WITHIN it, returning a phantom target extrapolated behind the route.
        // A sub-metre segment must be skipped: the walk continues into the real
        // segment and the target leads 50 m up the northward leg.
        double cos33 = Math.Cos(33.0 * Math.PI / 180.0);
        double dLon05 = 0.5 / (MPD * cos33);
        var lats = new[] { 33.0, 33.0, 33.0 + 100.0 / MPD };
        var lons = new[] { -84.0, -84.0 + dLon05, -84.0 + dLon05 };

        double acLon = -84.0 - 55.0 / (MPD * cos33); // 55 m west of the snap point

        var tgt = GuidanceGeometry.WalkTarget(lats, lons, 0, 33.0, acLon, 50.0);

        double northOfP1 = (tgt.lat - lats[1]) * MPD;
        double eastOfP1 = (tgt.lon - lons[1]) * MPD * cos33;
        Assert.Equal(50.0, northOfP1, 1.0);
        Assert.Equal(0.0, eastOfP1, 1.0);
    }

    [Fact]
    public void WalkTarget_never_returns_a_point_behind_the_current_segment_start()
    {
        // Aircraft 80 m behind a 200 m northward segment with a 50 m look-ahead:
        // the unclamped projection put the target 30 m BEHIND the route start,
        // steering the pilot backwards. It must clamp at the segment start.
        var (lats, lons) = BuildPolyline(33.0, -84.0, new (double, double)[] { (0, 200) });
        double acLat = 33.0 - 80.0 / MPD;

        var tgt = GuidanceGeometry.WalkTarget(lats, lons, 0, acLat, -84.0, 50.0);

        Assert.True(DistM(tgt.lat, tgt.lon, lats[0], lons[0]) < 1.0,
            $"target was {DistM(tgt.lat, tgt.lon, lats[0], lons[0]):F1} m from the route start");
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
