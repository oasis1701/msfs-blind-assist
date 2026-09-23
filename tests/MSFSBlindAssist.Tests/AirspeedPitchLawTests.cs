// Pins the pitch law behind visual guidance's AIRSPEED MODE.
//
// WHY THIS FILE EXISTS: visual guidance's vertical tone normally commands the pitch that
// holds a 3° glidepath and leaves airspeed to the pilot's throttle. On a fixed-power
// approach (the MSFS 2024 career landing lesson locks the throttle; an engine-out glide
// has none) pitch is the ONLY speed control, and following a nose-up glidepath command
// trades airspeed for altitude with nothing to buy it back. Measured live in a C172 on
// 2026-09-22: three lesson attempts, each arriving over the threshold slow and nose-high
// after the glidepath law asked for +3 to +6.6° inside the last mile.
//
// In airspeed mode the triangle tone commands the pitch that holds the target speed:
// fast means nose UP, slow means nose DOWN, the classic light-aircraft "pitch for speed,
// power for path" technique. The law is pure so it can be pinned here without the sim.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AirspeedPitchLawTests
{
    [Fact]
    public void OnSpeedWithNoTrendCommandsTheCurrentPitch()
    {
        double pitch = AirspeedPitchLaw.DesiredPitch(currentPitchDeg: 3.0, iasKnots: 65, iasTrendKtPerSec: 0, targetKnots: 65);
        Assert.Equal(3.0, pitch, 3);
    }

    [Fact]
    public void FastCommandsNoseUp()
    {
        // 10 kt fast at 0.25°/kt → 2.5° above the current pitch.
        double pitch = AirspeedPitchLaw.DesiredPitch(2.0, 75, 0, 65);
        Assert.Equal(2.0 + 10 * AirspeedPitchLaw.DegPerKnot, pitch, 3);
        Assert.True(pitch > 2.0);
    }

    [Fact]
    public void SlowCommandsNoseDown()
    {
        double pitch = AirspeedPitchLaw.DesiredPitch(4.0, 58, 0, 65);
        Assert.Equal(4.0 - 7 * AirspeedPitchLaw.DegPerKnot, pitch, 3);
        Assert.True(pitch < 4.0);
    }

    [Fact]
    public void AcceleratingAddsNoseUpAheadOfTheError()
    {
        // On speed but gaining 2 kt/s: anticipate with a nose-up lead so the pilot is not
        // chasing the number after it has already overshot.
        double pitch = AirspeedPitchLaw.DesiredPitch(3.0, 65, 2.0, 65);
        Assert.Equal(3.0 + 2.0 * AirspeedPitchLaw.TrendDampingDegPerKnotPerSec, pitch, 3);
        Assert.True(pitch > 3.0);
    }

    [Fact]
    public void DeceleratingSubtractsTheSameLead()
    {
        double pitch = AirspeedPitchLaw.DesiredPitch(3.0, 65, -2.0, 65);
        Assert.Equal(3.0 - 2.0 * AirspeedPitchLaw.TrendDampingDegPerKnotPerSec, pitch, 3);
    }

    [Fact]
    public void TheOffsetFromCurrentPitchIsBoundedBothWays()
    {
        // 60 kt fast would ask for +15°; the law never asks for more than MaxOffsetDeg away
        // from where the nose already is, so the tone stays a nudge and never a lurch.
        double up = AirspeedPitchLaw.DesiredPitch(1.0, 125, 0, 65);
        double down = AirspeedPitchLaw.DesiredPitch(1.0, 20, 0, 65);
        Assert.Equal(1.0 + AirspeedPitchLaw.MaxOffsetDeg, up, 3);
        Assert.Equal(1.0 - AirspeedPitchLaw.MaxOffsetDeg, down, 3);
    }

    [Theory]
    [InlineData(65, 65)]
    [InlineData(30, AirspeedPitchLaw.MinTargetKnots)]
    [InlineData(500, AirspeedPitchLaw.MaxTargetKnots)]
    [InlineData(double.NaN, AirspeedPitchLaw.MinTargetKnots)]
    public void TheTargetIsClampedToAFlyableRange(double requested, double expected)
    {
        Assert.Equal(expected, AirspeedPitchLaw.ClampTarget(requested), 3);
    }

    [Theory]
    [InlineData(62.0, 65, "airspeed 62, 3 slow")]
    [InlineData(70.4, 65, "airspeed 70, 5 fast")]
    [InlineData(65.4, 65, "airspeed 65, on speed")]
    [InlineData(null, 65, "airspeed unavailable")]
    public void TheSpeedReadoutNamesTheErrorDirection(double? ias, double target, string expected)
    {
        Assert.Equal(expected, AirspeedPitchLaw.DescribeSpeed(ias, target));
    }

    [Theory]
    [InlineData(40.0, "on profile")]
    [InlineData(-99.0, "on profile")]
    [InlineData(204.0, "200 high")]
    [InlineData(-146.0, "150 low")]
    public void TheProfileWordRoundsToTenFeet(double errorFt, string expected)
    {
        Assert.Equal(expected, AirspeedPitchLaw.DescribeProfile(errorFt, onProfileBandFt: 100.0));
    }

    [Fact]
    public void TrendEstimatorReadsZeroForASteadySpeed()
    {
        var trend = new AirspeedPitchLaw.TrendEstimator();
        double t = 0;
        double last = 0;
        for (int i = 0; i < 50; i++, t += 0.02)
            last = trend.Feed(65.0, t);
        Assert.Equal(0.0, last, 3);
    }

    [Fact]
    public void TrendEstimatorConvergesOnALinearRamp()
    {
        // 1 kt/s ramp sampled at 50 Hz: after a second the smoothed trend is within a
        // tenth of a knot per second of the true slope.
        var trend = new AirspeedPitchLaw.TrendEstimator();
        double last = 0;
        for (int i = 0; i <= 100; i++)
            last = trend.Feed(60.0 + i * 0.02, i * 0.02);
        Assert.InRange(last, 0.9, 1.1);
    }

    [Fact]
    public void TrendEstimatorIgnoresASampleWithNoElapsedTime()
    {
        var trend = new AirspeedPitchLaw.TrendEstimator();
        trend.Feed(65, 1.0);
        double again = trend.Feed(80, 1.0); // same timestamp — a divide by zero, not a trend
        Assert.Equal(0.0, again, 3);
    }

    [Fact]
    public void TrendEstimatorResetForgetsTheLastSample()
    {
        var trend = new AirspeedPitchLaw.TrendEstimator();
        trend.Feed(65, 1.0);
        trend.Reset();
        // First sample after a reset has nothing to difference against.
        Assert.Equal(0.0, trend.Feed(90, 1.5), 3);
    }
}
