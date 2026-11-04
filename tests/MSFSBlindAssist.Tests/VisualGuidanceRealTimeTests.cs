using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Volanta;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Visual guidance updates on every sim frame, so its rate limits and filters run on the measured
/// frame time (VG-20): the guidance behaves the same at any update rate, the first update after
/// activation is not rate-limited, two updates at one instant move nothing, and a long gap counts
/// as one second. Each test runs a headless manager on its own fake clock.
/// </summary>
public class VisualGuidanceRealTimeTests
{
    [Fact]
    public void The_first_update_takes_the_command_without_a_rate_limit()
    {
        var rig = new Rig();
        rig.Place(8, 1.0, 0);
        rig.UpdateAt(0);
        // Right of final and parallel: the arc capture asks for its full 15° left at once.
        Assert.Equal(-15.0, rig.Bank, 2);
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

    // The vertical speed steps 58 fpm deeper. With 0.7 per second, the smoothed value has moved
    // 1 − 0.7^0.5 = 16.3 % of the step after 0.5 s at any update rate, so the command rises by
    // P 0.005 × 58 × 0.163 = 0.047° plus D 0.002 × (the same 9.5 fpm over the 1 s window) = 0.019°.
    // The step is small enough that the 2.5°/s pitch limit never binds: per-update smoothing
    // (about 0.34° at 10 Hz, 0.40° at 30) would show.
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void Vertical_speed_smoothing_has_one_time_constant_at_every_update_rate(int hz)
    {
        var rig = new Rig();
        rig.Place(8, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(2, hz);
        double p0 = rig.Pitch;
        rig.Place(8, 0, 0, vsFpm: -800);
        rig.FlyFor(0.5, hz);
        Assert.InRange(rig.Pitch - p0, 0.066 - 0.01, 0.066 + 0.01);
    }

    // The angle of attack steps 1° up. The nominal pitch follows the smoothed AoA, which has moved
    // 1 − 0.7^0.5 = 0.163° after 0.5 s at any update rate (per-update smoothing: about 0.83° at 10 Hz).
    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    public void Angle_of_attack_smoothing_has_one_time_constant_at_every_update_rate(int hz)
    {
        var rig = new Rig();
        rig.Place(8, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(2, hz);
        double p0 = rig.Pitch;
        rig.Place(8, 0, 0, aoaDeg: 6.0);
        rig.FlyFor(0.5, hz);
        Assert.InRange(rig.Pitch - p0, 0.163 - 0.01, 0.163 + 0.01);
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
    public void The_flare_cue_reaches_the_flare_pitch_within_half_a_second(int hz)
    {
        var rig = new Rig();
        rig.Place(0.3, 0, 0);
        rig.UpdateAt(0);
        rig.FlyFor(2, hz);
        Assert.InRange(rig.Pitch, 1.0, 3.0);   // on the approach law, not yet flaring

        // 0.1 nm out and 55 ft below the path: the main gear about 25 ft up, under the 30 ft trigger.
        rig.Place(0.1, 0, 0, altOffsetFt: -55);
        rig.FlyFor(0.5, hz);
        Assert.Equal(new VisualGuidanceProfile().FlareTargetPitchDeg, rig.Pitch, 6);
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

    /// <summary>
    /// A headless manager on runway 09 (true 090, no variation, threshold 47.0 N 8.0 E, elevation
    /// 500 ft, 3°) with its own clock. The aircraft does not move between updates; only what a test
    /// changes with <see cref="Place"/> changes.
    /// </summary>
    private sealed class Rig
    {
        private const double ThrLat = 47.0, ThrLon = 8.0, Elev = 500.0, Course = 90.0;
        private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private readonly VisualGuidanceManager _vg;
        private DateTime _clock = Start;
        private double _seconds;

        public Rig()
        {
            var (endLat, endLon) = VolantaTrafficCalculator.Project(ThrLat, ThrLon, Course, 2.0);
            var runway = new Runway
            {
                AirportICAO = "ZZVG", RunwayID = "09", Heading = Course, HeadingMag = Course,
                StartLat = ThrLat, StartLon = ThrLon, EndLat = endLat, EndLon = endLon,
                ThresholdElevation = Elev, GlideslopeAngleDeg = 3.0,
            };
            var airport = new Airport { ICAO = "ZZVG", Name = "Test", Latitude = ThrLat, Longitude = ThrLon, Altitude = Elev, MagVar = 0.0 };
            _vg = new VisualGuidanceManager(new SpeechCapture()) { HeadlessForHarness = true, ClockForHarness = () => _clock };
            _vg.Toggle();
            _vg.Initialize(runway, airport, HandFlyWaveType.Triangle, 0.0, HandFlyWaveType.Sine, 0.0, false, new VisualGuidanceProfile());
        }

        public double Bank => _vg.DesiredBankForHarness;
        public double Pitch => _vg.DesiredPitchForHarness;

        /// <summary>alongNm back from the threshold, crossNm right of the centreline, heading and track course + headingRelDeg, 140 kt.</summary>
        public void Place(double alongNm, double crossNm, double headingRelDeg, double vsFpm = -742, double altOffsetFt = 0, double aoaDeg = 5.0)
        {
            static double Norm(double d) => (d % 360 + 360) % 360;
            var back = VolantaTrafficCalculator.Project(ThrLat, ThrLon, Norm(Course + 180), alongNm);
            var pos = crossNm >= 0
                ? VolantaTrafficCalculator.Project(back.Latitude, back.Longitude, Norm(Course + 90), crossNm)
                : VolantaTrafficCalculator.Project(back.Latitude, back.Longitude, Norm(Course - 90), -crossNm);
            double alt = Elev + alongNm * 6076.12 * Math.Tan(3.0 * Math.PI / 180) + new VisualGuidanceProfile().GlideslopeAltitudeBiasFt + altOffsetFt;
            double heading = Norm(Course + headingRelDeg);

            _vg.UpdateLatitude(pos.Latitude); _vg.UpdateLongitude(pos.Longitude); _vg.UpdateAltitudeMSL(alt);
            _vg.UpdateHeading(heading); _vg.UpdateGroundSpeed(140); _vg.UpdateVerticalSpeed(vsFpm);
            _vg.UpdateGroundTrack(heading);
            _vg.UpdatePitch(VisualGuidanceManager.PitchDegreesFromSimConnect(-2.0 * Math.PI / 180));
            _vg.UpdateBank(VisualGuidanceManager.BankDegreesFromSimConnect(0.0));
            _vg.UpdateAoA(VisualGuidanceManager.AoaDegreesFromSimConnect(aoaDeg * Math.PI / 180));
            _vg.UpdateAGL(alt - Elev);
        }

        /// <summary>Sets the clock to <paramref name="seconds"/> after the start and runs one update.</summary>
        public void UpdateAt(double seconds)
        {
            _seconds = seconds;
            _clock = Start.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            _vg.ProcessUpdate();
        }

        /// <summary>
        /// Carries on from the current clock with one update every 1/hz s until <paramref name="seconds"/>
        /// have passed. <paramref name="beforeEach"/>, given the update's time in seconds, can move the aircraft first.
        /// </summary>
        public void FlyFor(double seconds, int hz, Action<double>? beforeEach = null)
        {
            double from = _seconds;
            int steps = (int)Math.Round(seconds * hz);
            for (int i = 1; i <= steps; i++)
            {
                double t = from + (double)i / hz;
                beforeEach?.Invoke(t);
                UpdateAt(t);
            }
        }
    }
}
