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
//
// The nose-up rescale is NOT free, and this file pins the price alongside the fix.
// Below the old +10 degree saturation edge the tone was working perfectly well, and
// widening the range halves its resolution there (30 -> 15 Hz/degree) for the whole
// flight, cruise included -- not just on the climb-out. See
// TheNoseUpHalfBelowTheOldSaturationEdgeWasDeliberatelyReTuned: 5 degrees nose up used
// to read 650 Hz and now reads 575 Hz, a 75 Hz shift in a cue a pilot learns by ear.
// Recording that as a deliberate trade is what stops it being rediscovered as a bug.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AudioTonePitchMappingTests
{
    // The SHIPPED hand-fly mapping, read from the real constants rather than re-typed --
    // a hand-typed copy keeps passing after someone re-tunes the real ones.
    private const float Min = AudioToneGenerator.DEFAULT_MIN_FREQUENCY;
    private const float Max = AudioToneGenerator.DEFAULT_MAX_FREQUENCY;
    private const double Down = AudioToneGenerator.DEFAULT_PITCH_DOWN_RANGE_DEG;
    private const double Up = AudioToneGenerator.DEFAULT_PITCH_UP_RANGE_DEG;

    private static double Map(double pitch) =>
        AudioToneGenerator.PitchToFrequency(pitch, Min, Max, Down, Up);

    // The mapping this replaced: symmetric +/-10 degrees onto the same 200-800 Hz.
    // Used by the before/after rows so both the fix and its price are stated in one place.
    private static double MapOldSymmetric(double pitch) =>
        AudioToneGenerator.PitchToFrequency(pitch, Min, Max, 10.0, 10.0);

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
        Assert.Equal(MapOldSymmetric(pitch), Map(pitch), 6);
    }

    // THE BUG, pinned: a rotation attitude used to sit AT maximum and could not move.
    // Every row here really was clamped to 800 Hz before.
    [Theory]
    [InlineData(12.0, 680.0)]
    [InlineData(15.0, 725.0)]
    [InlineData(18.0, 770.0)]
    public void ARotationAttitudeUsedToBeClampedAtMaximumAndNoLongerIs(double pitch, double expected)
    {
        Assert.Equal(800.0, MapOldSymmetric(pitch), 6);   // frozen before
        Assert.Equal(expected, Map(pitch), 6);            // moving now
    }

    // THE PRICE, pinned separately and honestly. Below the old +10 degree saturation edge
    // the tone was NOT broken, and widening the nose-up half re-tuned it: 5 degrees used to
    // read 650 Hz and now reads 575 Hz. Filing these rows under "the bug" would hide the
    // one trade this change actually makes.
    [Theory]
    [InlineData(5.0, 650.0, 575.0)]
    [InlineData(10.0, 800.0, 650.0)]
    public void TheNoseUpHalfBelowTheOldSaturationEdgeWasDeliberatelyReTuned(
        double pitch, double oldHz, double newHz)
    {
        Assert.Equal(oldHz, MapOldSymmetric(pitch), 6);
        Assert.Equal(newHz, Map(pitch), 6);
    }

    [Fact]
    public void ARotationAttitudeIsNoLongerFrozenAtMaximum()
    {
        // The four pitch readings the live Fenix takeoff actually produced after the
        // handoff. Under the old mapping all four were 800 Hz; the tone carried no
        // information at all.
        double[] climbOut = { 12.6, 13.6, 14.7, 15.7 };
        var frequencies = climbOut.Select(Map).ToArray();

        Assert.All(frequencies, f => Assert.True(f < Max, $"expected below {Max} Hz, got {f}"));

        // AUDIBLY distinct, not merely unequal. Distinct() would be exact double bit-equality,
        // so it passes on a one-ULP difference -- four readings within a microhertz of each
        // other would satisfy it while the tone sat acoustically frozen at the centre, which
        // is the very defect this test is named for. ~5 Hz is roughly where a step becomes a
        // perceptible change on a bare tone with no reference to beat against.
        const double AudibleStepHz = 5.0;
        for (int i = 1; i < frequencies.Length; i++)
        {
            double step = frequencies[i] - frequencies[i - 1];
            Assert.True(step >= AudibleStepHz,
                $"{climbOut[i - 1]}° → {climbOut[i]}° moved only {step:F2} Hz, below {AudibleStepHz} Hz");
        }
    }

    [Theory]
    [InlineData(20.0, 800.0)]   // at the nose-up edge
    [InlineData(25.0, 800.0)]   // beyond it
    [InlineData(-15.0, 200.0)]  // beyond the nose-down edge
    public void BeyondTheRangeSaturates(double pitch, double expected)
    {
        Assert.Equal(expected, Map(pitch), 6);
    }

    // Every other tone owner (visual guidance, the landing-flare assist) configures a
    // SYMMETRIC range. Equal down/up ranges must reproduce the old single-slope line exactly,
    // or this change silently re-tunes guidance that was measured and signed off separately.
    //
    // SCOPE: this pins the MAPPING FUNCTION for equal ranges. That Configure actually delivers
    // equal ranges from a symmetric call is a different claim, pinned by AudioToneConfigureTests
    // -- this file cannot see Configure at all.
    [Theory]
    [InlineData(0.0, 500.0)]
    [InlineData(6.0, 800.0)]
    [InlineData(3.0, 650.0)]
    [InlineData(-3.0, 350.0)]
    [InlineData(-6.0, 200.0)]
    public void AnEqualDownUpRangeIsStillOneStraightLine(double pitch, double expected)
    {
        Assert.Equal(expected, AudioToneGenerator.PitchToFrequency(pitch, Min, Max, 6.0, 6.0), 6);
    }

    [Fact]
    public void TheShippedDefaultKeepsARotationAttitudeBelowSaturation()
    {
        // 18° covers the steepest rotation the supported airframes fly. If this saturates,
        // the liftoff handoff hands the pilot a frozen tone again.
        Assert.True(Map(18.0) < Max, $"18° must not saturate, got {Map(18.0)} Hz");
        Assert.True(Map(12.0) < Map(15.0));
        Assert.True(Map(15.0) < Map(18.0));
    }

    [Fact]
    public void TheShippedDefaultLeavesLevelFlightAndNoseDownWhereTheyWere()
    {
        // Unchanged from the shipped symmetric mapping, so nothing learned by ear moves.
        Assert.Equal(500.0, Map(0.0), 6);
        Assert.Equal(350.0, Map(-5.0), 6);
        Assert.Equal(200.0, Map(-10.0), 6);
    }
}
