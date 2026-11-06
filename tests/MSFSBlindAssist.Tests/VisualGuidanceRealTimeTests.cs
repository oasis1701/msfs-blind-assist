using MSFSBlindAssist.Aircraft;
using Xunit;
using Rig = MSFSBlindAssist.Tests.VisualGuidanceTestRig;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Visual guidance updates on every sim frame, so its rate limits and filters run on the measured
/// frame time (VG-20): the guidance behaves the same at any update rate, the first update after
/// activation is not rate-limited, two updates at one instant move nothing, and a long gap counts
/// as one second. Each test runs a headless manager on its own fake clock (<see cref="VisualGuidanceTestRig"/>).
/// </summary>
public class VisualGuidanceRealTimeTests
{
    [Fact]
    public void The_first_update_takes_the_command_without_a_rate_limit()
    {
        var rig = new Rig();
        rig.Place(8, 1.0, 0);
        rig.UpdateAt(0);
        // Right of final and parallel: vectored onto final, the capture law asks for the profile's
        // full 25° left at once (MaxApproachBankDeg; today's arc capture stopped at 15°).
        Assert.Equal(-new VisualGuidanceProfile().MaxApproachBankDeg, rig.Bank, 2);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(60)]
    public void Bank_command_moves_at_the_profile_rate_at_any_update_rate(int hz)
    {
        var rig = new Rig();
        rig.Place(8, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(1, hz);
        Assert.Equal(0.0, rig.Bank, 2);

        // Track 30° right of the course: the precision track asks for a left bank, at 3°/s.
        rig.Place(8, 0, 30);
        rig.FlyFor(1, hz);
        Assert.InRange(rig.Bank, -3.1, -2.9);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(60)]
    public void Pitch_command_moves_at_the_profile_rate_at_any_update_rate(int hz)
    {
        var rig = new Rig();
        rig.Place(8, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(2, hz);
        double p0 = rig.Pitch;

        // 300 ft above the path: the command drops, at 2.5°/s.
        rig.Place(8, 0, 0, altOffsetFt: 300);
        rig.FlyFor(0.5, hz);
        Assert.InRange(rig.Pitch, p0 - 1.25 - 0.05, p0 - 1.25 + 0.05);
    }

    // The vertical speed steps from 742 to 800 fpm (the flight path 3.00° to 3.23°) with the pitch held:
    // the learned trim (pitch minus flight path) rises by 0.234°, through the flight path's 1 s smoothing
    // and the trim's 6 s. After 6 s two cascaded lags have passed 1 − (6e^−1 − e^−6) / 5 = 55.9 % of it,
    // 0.131°, and the cue (trim − target descent) rises by as much, at any update rate. The aircraft does
    // not move, so the height error, and the target, stay put. (Before stage 5 this pinned the old law's
    // 0.7-per-second vertical-speed smoothing.)
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void The_trim_learns_with_its_time_constants_at_every_update_rate(int hz)
    {
        var rig = new Rig();
        rig.Place(8, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(2, hz);
        double p0 = rig.Pitch;
        rig.Place(8, 0, 0, vsFpm: -800);
        rig.FlyFor(6, hz);
        Assert.InRange(rig.Pitch - p0, 0.131 - 0.005, 0.131 + 0.005);
    }

    // The angle of attack steps 1° up: the cue does not move (VG-25). The old law built its nominal pitch
    // on the live AoA, and so moved with it.
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void The_angle_of_attack_never_moves_the_cue(int hz)
    {
        var rig = new Rig();
        rig.Place(8, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(2, hz);
        double p0 = rig.Pitch;
        rig.Place(8, 0, 0, aoaDeg: 6.0);
        rig.FlyFor(2, hz);
        Assert.Equal(p0, rig.Pitch, 9);
    }

    // Still on the centreline for 5 s, then drifting right at 0.02 nm/s for 2 s. The precision
    // track's damping term is −12 × the cross-track rate, measured over 4 s and smoothed at 0.85 per
    // second: the raw rate ramps as 0.02 × τ / 4, so the smoothed rate at τ = 2 s is
    // (0.02 / 4) × (2 − (1 − e^(−2k)) / k), k = ln(1 / 0.85), about 0.00146 nm/s, and the damping
    // −0.0175°. A rig placed at the same final spot with no history has no damping term, so the
    // difference between the two banks is the damping alone. Per-update smoothing gives about
    // −0.09° at 10 Hz and −0.11° at 60.
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void Cross_track_rate_damping_has_one_time_constant_at_every_update_rate(int hz)
    {
        const double rateNmPerS = 0.02;
        var moving = new Rig();
        moving.Place(8, 0, 0);
        moving.UpdateAt(0);
        moving.FlyFor(5, hz);
        moving.FlyFor(2, hz, t => moving.Place(8, rateNmPerS * (t - 5), 0));

        var still = new Rig();
        still.Place(8, rateNmPerS * 2, 0);
        still.UpdateAt(7);

        Assert.InRange(moving.Bank - still.Bank, -0.0175 - 0.006, -0.0175 + 0.006);
    }

    // A jet passes the 30 ft flare height about 2.4 s before the wheels touch, so the flare cue has
    // to arrive at once to be any use: within half a second it asks for the full flare pitch (the
    // default profile's 6°, from about 2° on the approach). At 1.5°/s it took 2.7 s and finished
    // after touchdown, and the harness's jets landed about 230 fpm firmer.
    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    [InlineData(60)]
    public void The_flare_cue_starts_where_the_aircraft_is_at_any_update_rate(int hz)
    {
        var rig = new Rig();
        rig.Place(0.3, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(2, hz);
        double approach = rig.Pitch;
        Assert.InRange(approach, 1.0, 3.0);   // on the approach law, not yet flaring

        // 0.1 nm out and 55 ft below the path: the main gear about 25 ft up, 2 s from touchdown at 742 fpm.
        // The flare's sink profile starts at the aircraft's own 742 fpm, so the cue stays where it was
        // (stage 6; the fixed flare pitch jumped to 6°).
        rig.Place(0.1, 0, 0, altOffsetFt: -55);
        rig.FlyFor(0.5, hz);
        Assert.Equal("Flare", rig.Phase);
        Assert.Equal(approach, rig.Pitch, 0.05);
    }

    [Fact]
    public void Two_updates_at_the_same_instant_leave_the_command_where_it_was()
    {
        var rig = new Rig();
        rig.Place(8, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(1, 20);
        rig.Place(8, 0, 30);
        rig.UpdateAt(1.05);
        double bank = rig.Bank;
        rig.UpdateAt(1.05);
        Assert.Equal(bank, rig.Bank, 9);
    }

    [Fact]
    public void A_long_gap_moves_the_command_one_seconds_worth()
    {
        var rig = new Rig();
        rig.Place(8, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(1, 20);
        rig.Place(8, 0, 30);
        rig.UpdateAt(11);
        Assert.InRange(rig.Bank, -3.1, -2.9);
    }
}
