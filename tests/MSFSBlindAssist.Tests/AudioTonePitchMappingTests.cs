// Pins the pitch -> frequency mapping that HandFly's attitude tone rides on.
//
// WHY THIS FILE EXISTS: the mapping used to be symmetric (+/-10 degrees onto
// 200-800 Hz) and was inline in UpdatePitch, so nothing could see it. A liftoff
// auto-handoff activates HandFly AT ROTATION, where an airliner sits at 12-18
// degrees nose up -- outside +/-10, so Math.Clamp pinned the tone at 800 Hz and it
// could not move for the whole climb-out. Measured on a live Fenix A320 takeoff
// (2026-08-25 19:01:59): pitch crossed 10 degrees 0.7 s BEFORE the handoff and
// every HandFly reading afterwards was +13, +14, +15, +16 -- the tone was
// mathematically frozen from its very first update.
//
// The mapping is now ASYMMETRIC and PIECEWISE, anchored at the centre frequency:
//   - 0 degrees always sits at the centre frequency. Level flight is the reference a
//     pilot listens for; it must not move.
//   - The NOSE-DOWN half is unchanged from the old symmetric mapping, so nothing
//     already learned by ear has to be relearned.
//   - Only the NOSE-UP half is rescaled, because that is the half that saturated.
// The kink at 0 degrees is deliberate: it buys the level-flight anchor plus an
// unchanged lower half, and it is the reason this is not one straight line.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AudioTonePitchMappingTests
{
    // HandFly's shipping mapping: 200-800 Hz, 10 degrees nose-down, 20 degrees nose-up.
    private const float Min = 200f;
    private const float Max = 800f;
    private const double Down = 10.0;
    private const double Up = 20.0;

    private static double Map(double pitch) =>
        AudioToneGenerator.PitchToFrequency(pitch, Min, Max, Down, Up);

    [Fact]
    public void LevelFlightSitsAtTheCentreFrequency()
    {
        Assert.Equal(500.0, Map(0.0), 6);
    }

    // The half that already worked. These are the exact values the old symmetric
    // +/-10 mapping produced, so a pilot's learned nose-down reference is untouched.
    [Theory]
    [InlineData(-10.0, 200.0)]
    [InlineData(-5.0, 350.0)]
    [InlineData(-1.0, 470.0)]
    public void NoseDownIsUnchangedFromTheOldSymmetricMapping(double pitch, double expected)
    {
        Assert.Equal(expected, Map(pitch), 6);
    }

    // The bug, pinned. A 12-18 degree rotation attitude must produce DIFFERENT
    // frequencies -- under the old mapping every one of these clamped to 800 Hz.
    [Theory]
    [InlineData(20.0, 800.0)]
    [InlineData(15.0, 725.0)]
    [InlineData(12.0, 680.0)]
    [InlineData(10.0, 650.0)]
    [InlineData(5.0, 575.0)]
    public void NoseUpReachesTwentyDegreesBeforeSaturating(double pitch, double expected)
    {
        Assert.Equal(expected, Map(pitch), 6);
    }

    [Fact]
    public void ARotationAttitudeIsNoLongerFrozenAtMaximum()
    {
        // The four pitch readings the live Fenix takeoff actually produced after the
        // handoff. Under the old mapping all four were 800 Hz; the tone carried no
        // information at all. Each must now be audibly distinct and below maximum.
        double[] climbOut = { 12.6, 13.6, 14.7, 15.7 };
        var frequencies = climbOut.Select(Map).ToArray();

        Assert.All(frequencies, f => Assert.True(f < Max, $"expected below {Max} Hz, got {f}"));
        Assert.Equal(frequencies.Length, frequencies.Distinct().Count());
    }

    [Theory]
    [InlineData(25.0, 800.0)]   // beyond nose-up range
    [InlineData(-15.0, 200.0)]  // beyond nose-down range
    public void BeyondTheRangeStillSaturates(double pitch, double expected)
    {
        Assert.Equal(expected, Map(pitch), 6);
    }

    // Every other tone owner (visual guidance, the landing-flare assist) configures a
    // SYMMETRIC range through the 4-argument Configure overload. Equal down/up ranges
    // must reproduce the old single-slope line exactly, or this change silently
    // re-tunes guidance that was measured and signed off separately.
    [Theory]
    [InlineData(0.0, 500.0)]
    [InlineData(6.0, 800.0)]
    [InlineData(3.0, 650.0)]
    [InlineData(-3.0, 350.0)]
    [InlineData(-6.0, 200.0)]
    public void ASymmetricRangeIsStillOneStraightLine(double pitch, double expected)
    {
        Assert.Equal(expected, AudioToneGenerator.PitchToFrequency(pitch, Min, Max, 6.0, 6.0), 6);
    }

    // ---- The SHIPPED hand-fly mapping -------------------------------------------------
    // HandFlyManager never calls Configure, and neither does the settings panel's preview
    // tone — they are the only two consumers of the class defaults (visual guidance and the
    // landing-flare assist both configure their own ranges). So these defaults ARE hand fly's
    // mapping, and they are what the liftoff handoff hands the pilot at rotation.

    private static double MapDefault(double pitch) =>
        AudioToneGenerator.PitchToFrequency(pitch,
            AudioToneGenerator.DefaultMinFrequencyHz,
            AudioToneGenerator.DefaultMaxFrequencyHz,
            AudioToneGenerator.DefaultPitchDownRangeDeg,
            AudioToneGenerator.DefaultPitchUpRangeDeg);

    [Fact]
    public void TheDefaultMappingKeepsARotationAttitudeBelowSaturation()
    {
        // 18° covers the steepest rotation the supported airframes fly. If this saturates,
        // the liftoff handoff hands the pilot a frozen tone again.
        Assert.True(MapDefault(18.0) < AudioToneGenerator.DefaultMaxFrequencyHz,
            $"18° must not saturate, got {MapDefault(18.0)} Hz");
        Assert.True(MapDefault(12.0) < MapDefault(15.0));
        Assert.True(MapDefault(15.0) < MapDefault(18.0));
    }

    [Fact]
    public void TheDefaultMappingLeavesLevelFlightAndNoseDownWhereTheyWere()
    {
        // Unchanged from the shipped symmetric mapping, so nothing learned by ear moves.
        Assert.Equal(500.0, MapDefault(0.0), 6);
        Assert.Equal(350.0, MapDefault(-5.0), 6);
        Assert.Equal(200.0, MapDefault(-10.0), 6);
    }
}
