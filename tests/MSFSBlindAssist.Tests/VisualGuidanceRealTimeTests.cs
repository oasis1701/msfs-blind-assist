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

    [Fact]
    public void Vertical_speed_smoothing_has_one_time_constant_at_every_update_rate()
    {
        var pitches = new List<double>();
        foreach (int hz in new[] { 10, 30, 60 })
        {
            var rig = new Rig();
            rig.Place(8, 0, 0);
            rig.UpdateAt(0);
            rig.FlyFor(2, hz);
            rig.Place(8, 0, 0, vsFpm: -1000);
            rig.FlyFor(0.5, hz);
            pitches.Add(rig.Pitch);
        }
        Assert.True(pitches.Max() - pitches.Min() < 0.05,
            $"pitch after 0.5 s at 10, 30 and 60 Hz: {string.Join(", ", pitches.Select(p => p.ToString("0.000")))}");
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
        public void Place(double alongNm, double crossNm, double headingRelDeg, double vsFpm = -742, double altOffsetFt = 0)
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
            _vg.UpdateAoA(VisualGuidanceManager.AoaDegreesFromSimConnect(5.0 * Math.PI / 180));
            _vg.UpdateAGL(alt - Elev);
        }

        /// <summary>Sets the clock to <paramref name="seconds"/> after the start and runs one update.</summary>
        public void UpdateAt(double seconds)
        {
            _seconds = seconds;
            _clock = Start.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
            _vg.ProcessUpdate();
        }

        /// <summary>Carries on from the current clock with one update every 1/hz s until <paramref name="seconds"/> have passed.</summary>
        public void FlyFor(double seconds, int hz)
        {
            double from = _seconds;
            int steps = (int)Math.Round(seconds * hz);
            for (int i = 1; i <= steps; i++) UpdateAt(from + (double)i / hz);
        }
    }
}
