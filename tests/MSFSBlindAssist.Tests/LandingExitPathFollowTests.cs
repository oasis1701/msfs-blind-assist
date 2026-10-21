// Pins the "turning onto the exit's own path" hold-off for both missed-exit detectors
// (Navigation/LandingExitPathFollow). Geometry is the real YPPH 21 → C9 route from
// the user's navdata (node 849 → hold line node 1102), and the aircraft positions are
// the live taxi_guidance.log frames of 2026-09-18, when the post-handoff monitor called
// C9 missed with the aircraft on it and the tone centred.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitPathFollowTests
{
    // The handoff route as LoadRoute built it (16 segments, 162 m).
    private static readonly (double Lat, double Lon)[] C9 =
    {
        (-31.9499283, 115.9622498), (-31.9500046, 115.9622498), (-31.9500656, 115.9622498),
        (-31.9501343, 115.9622498), (-31.9501877, 115.9622498), (-31.9502487, 115.9622803),
        (-31.9503250, 115.9622803), (-31.9503860, 115.9622803), (-31.9504700, 115.9623413),
        (-31.9505615, 115.9624023), (-31.9506226, 115.9624329), (-31.9506912, 115.9625244),
        (-31.9507523, 115.9625854), (-31.9508057, 115.9626770), (-31.9508743, 115.9627686),
        (-31.9509277, 115.9628296), (-31.9509811, 115.9631348),
    };

    // Runway 21: threshold used by the rollout, true heading.
    private const double ThrLat = -31.928604125976562, ThrLon = 115.96849060058594, RwyHdg = 193.93;

    private static (double lat, double lon) OnCentreline(double alongM, double lateralRightM = 0)
    {
        const double k = 111195;
        double hr = RwyHdg * Math.PI / 180, cl = Math.Cos(ThrLat * Math.PI / 180);
        double n = alongM * Math.Cos(hr) - lateralRightM * Math.Sin(hr);
        double e = alongM * Math.Sin(hr) + lateralRightM * Math.Cos(hr);
        return (ThrLat + n / k, ThrLon + e / (k * cl));
    }

    [Fact]
    public void The_live_frame_that_was_called_missed_is_on_C9()
    {
        // 10:59:37.970 — 95 ft past the junction, heading 181.7°, tone reading -1.8°.
        // The next frame declared "Missed C9. Retargeting C11."
        Assert.True(LandingExitPathFollow.HoldsOffMiss(C9, -31.9501976, 115.9621909,
            headingTrue: 181.7, runwayHeadingTrue: RwyHdg, signedAlongPastFt: 100, overshootMarginFt: 100));
    }

    [Fact]
    public void Every_live_frame_on_C9_after_the_handoff_holds_the_miss_off()
    {
        // taxi_guidance.log 10:59:35.9 → 10:59:37.97, once the pilot was turned onto C9.
        (double lat, double lon, double hdg)[] frames =
        {
            (-31.9499253, 115.9622083, 185.3), (-31.9499826, 115.9622017, 183.3), (-31.9500302, 115.9621981, 182.2),
            (-31.9500784, 115.9621956, 181.7), (-31.9501212, 115.9621939, 181.8), (-31.9501618, 115.9621923, 181.8),
            (-31.9501976, 115.9621909, 181.7),
        };
        foreach (var (lat, lon, hdg) in frames)
            Assert.True(LandingExitPathFollow.HoldsOffMiss(C9, lat, lon, hdg, RwyHdg, 100, 100),
                $"frame {lat},{lon} hdg {hdg} ({LandingExitPathFollow.DistanceToPathMeters(C9, lat, lon):F1} m off C9)");
    }

    [Fact]
    public void Rolling_straight_at_runway_heading_is_called_exactly_as_before()
    {
        // Even 100 ft past the junction, where C9 is only 6 m from the centreline and the
        // aircraft is within the path tolerance, runway heading means no hold-off — the
        // miss call and the retarget it drives are unchanged for a straight roll.
        var (lat, lon) = OnCentreline(2443 + 31);
        Assert.True(LandingExitPathFollow.DistanceToPathMeters(C9, lat, lon) <= LandingExitPathFollow.ToleranceMetres);
        Assert.False(LandingExitPathFollow.HoldsOffMiss(C9, lat, lon, RwyHdg, RwyHdg, 101, 100));
        Assert.False(LandingExitPathFollow.HoldsOffMiss(C9, lat, lon, RwyHdg - 2, RwyHdg, 101, 100));
    }

    [Fact]
    public void Turning_the_wrong_way_is_not_following_the_exit()
    {
        var (lat, lon) = OnCentreline(2443 + 31);
        Assert.False(LandingExitPathFollow.HoldsOffMiss(C9, lat, lon, RwyHdg + 12, RwyHdg, 101, 100));
    }

    [Fact]
    public void A_stretch_drawn_along_the_centreline_never_holds_a_miss_off()
    {
        // EGLL/LFPG draw many exits along the centreline for 60-75 m before the curve.
        // On that stretch a follower and a straight roller are indistinguishable, so it
        // must not hold a miss off even for a turning aircraft.
        var stub = new[] { OnCentreline(2443), OnCentreline(2443, -1.5), OnCentreline(2443 + 80, -2) };
        var (lat, lon) = OnCentreline(2443 + 31, -1);
        Assert.False(LandingExitPathFollow.HoldsOffMiss(stub, lat, lon, RwyHdg - 8, RwyHdg, 101, 100));
    }

    [Fact]
    public void Rolling_straight_past_C9_is_still_a_miss_once_it_has_diverged()
    {
        // Junction is 2,443 m from the threshold. On the centreline 70 m past it the
        // aircraft is well clear of C9's line — a genuine miss must still be declared.
        var (lat, lon) = OnCentreline(2443 + 70);
        Assert.False(LandingExitPathFollow.HoldsOffMiss(C9, lat, lon, RwyHdg, RwyHdg, signedAlongPastFt: 230, overshootMarginFt: 100));
    }

    [Fact]
    public void A_right_angle_exit_is_unchanged_at_the_100_ft_margin()
    {
        // A 90° exit leaving the centreline at 2,443 m: rolling straight on, 31 m past
        // the junction the aircraft is 31 m from the exit path, so the miss stands
        // exactly where it always did.
        var exit = new[] { OnCentreline(2443), OnCentreline(2443, -60), OnCentreline(2443, -120) };
        var (lat, lon) = OnCentreline(2443 + 31);
        Assert.False(LandingExitPathFollow.HoldsOffMiss(exit, lat, lon, RwyHdg, RwyHdg, signedAlongPastFt: 101, overshootMarginFt: 100));
    }

    [Fact]
    public void A_45_degree_exit_is_unchanged_at_the_100_ft_margin()
    {
        var exit = new[] { OnCentreline(2443), OnCentreline(2443 + 100, -100) };
        var (lat, lon) = OnCentreline(2443 + 31);
        Assert.False(LandingExitPathFollow.HoldsOffMiss(exit, lat, lon, RwyHdg, RwyHdg, signedAlongPastFt: 101, overshootMarginFt: 100));
    }

    [Fact]
    public void No_path_leaves_the_detectors_exactly_as_before()
    {
        var (lat, lon) = OnCentreline(2443 + 20);
        Assert.False(LandingExitPathFollow.HoldsOffMiss(null, lat, lon, 181, RwyHdg, 100, 100));
        Assert.False(LandingExitPathFollow.HoldsOffMiss(new[] { C9[0] }, lat, lon, 181, RwyHdg, 100, 100));
    }

    [Fact]
    public void The_hold_off_is_bounded_so_it_can_never_suppress_a_miss_indefinitely()
    {
        // Even ON the path, past margin + SuppressWindowFeet the runway tests decide.
        Assert.False(LandingExitPathFollow.HoldsOffMiss(C9, C9[3].Lat, C9[3].Lon, 180.0, RwyHdg,
            signedAlongPastFt: 100 + LandingExitPathFollow.SuppressWindowFeet + 1, overshootMarginFt: 100));
    }
}

/// <summary>
/// The on-centreline stub measured by <see cref="LandingExitPathFollow.OnAxisRunMetres"/> — how far the
/// exit's own path runs down the centreline before it leaves it. Shapes are the handoff routes the
/// VirtualPilot harness printed for real exits (runway frame: along from the junction, lateral + = right).
/// </summary>
public class OnAxisRunTests
{
    private const double Lat0 = 41.98, Lon0 = -87.90, Hdg = 270.0;   // a west-facing runway

    private static (double Lat, double Lon) P(double along, double lateral)
    {
        const double k = 111132.0;
        double h = Hdg * Math.PI / 180, cl = Math.Cos(Lat0 * Math.PI / 180);
        double n = along * Math.Cos(h) - lateral * Math.Sin(h), e = along * Math.Sin(h) + lateral * Math.Cos(h);
        return (Lat0 + n / k, Lon0 + e / (k * cl));
    }

    private static double Run(params (double a, double l)[] pts)
    {
        var path = pts.Select(p => P(p.a, p.l)).ToList();
        var j = P(0, 0);
        return LandingExitPathFollow.OnAxisRunMetres(path, j.Lat, j.Lon, Lat0, Lon0, Hdg);
    }

    [Fact]
    public void Kord_27L_M_runs_78_m_down_the_centreline_before_turning_off()
        => Assert.Equal(78, Run((0, 0), (16, -2), (78, -2), (95, -8), (106, -18), (115, -43), (115, -86)), 1);

    [Fact]
    public void Egll_27L_N7_counts_its_3_m_jog_as_part_of_the_stub()
        => Assert.Equal(50, Run((0, 0), (3, 2), (39, 2), (42, 2), (46, 3), (50, 4), (54, 6), (56, 8), (63, 12)), 1);

    [Fact]
    public void A_right_angle_exit_has_no_stub()
        => Assert.Equal(0, Run((0, 0), (0, -20), (0, -60)), 1);

    [Fact]
    public void Ypph_21_C9_leaves_at_14_degrees_so_its_stub_is_short()
        => Assert.InRange(Run((0, 1), (8, -1), (15, -3), (22, -5), (28, -6), (34, -11), (42, -13), (49, -14)), 20, 30);

    [Fact]
    public void Kphl_35_E4_hairpin_is_not_a_stub()
        => Assert.Equal(0, Run((0, 0), (45, -1), (35, -4), (27, -7), (19, -11), (12, -17), (7, -23)));

    [Fact]
    public void Ksdf_29_F_path_that_comes_back_across_the_centreline_is_not_a_stub()
        => Assert.Equal(0, Run((0, 2), (70, 2), (86, 10), (97, 23), (82, 1), (81, -1), (43, -54)));

    [Fact]
    public void Kstl_30L_V_loops_back_only_once_well_out_so_its_stub_counts()
        // 64 m along the centreline, then round to the right and back east 50+ m out on the apron.
        => Assert.Equal(64, Run((0, 0), (64, 0), (83, -9), (90, -28), (80, -46), (62, -58), (19, -67)), 1);

    [Fact]
    public void A_path_turning_back_while_still_beside_the_runway_is_a_hairpin()
        => Assert.Equal(0, Run((0, 0), (64, 0), (70, -10), (40, -20), (10, -25)));

    [Fact]
    public void The_run_is_capped()
        => Assert.Equal(LandingExitPathFollow.OnAxisMaxRunMetres, Run((0, 0), (500, 0), (520, -40)));
}
