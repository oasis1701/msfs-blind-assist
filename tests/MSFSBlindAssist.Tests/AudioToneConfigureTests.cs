// Pins that Configure lands each argument on the field it names, and that a configured
// instance ends up with the symmetric mapping its callers depend on.
//
// WHY THIS FILE EXISTS: Configure had NO coverage at all. AudioTonePitchMappingTests only ever
// calls the static PitchToFrequency with numbers it supplies itself, so a transposition inside
// Configure -- landing the bank range on the nose-up range, say -- would have left every test
// in the suite green while visual guidance's approach tones silently re-tuned themselves. That
// mattered most while a second, asymmetric Configure overload existed whose 4th positional
// parameter meant something different from the 4th parameter of this one; the overload is gone
// now, and this file is what keeps the remaining one honest.
//
// No audio endpoint is needed: the mapping is captured into fields at Configure time and read
// back through the internal MappingForTests seam. That seam exists for exactly the reason
// AudioToneGeneratorTests documents for the registration contract -- some invariants are simply
// not observable from outside without one.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AudioToneConfigureTests
{
    [Fact]
    public void AFreshGeneratorCarriesTheAsymmetricHandFlyDefaults()
    {
        using var tone = new AudioToneGenerator();

        var m = tone.MappingForTests;

        Assert.Equal(200f, m.Min);
        Assert.Equal(800f, m.Max);
        Assert.Equal(10.0, m.Down);
        Assert.Equal(20.0, m.Up);
        Assert.Equal(10.0, m.Bank);
    }

    [Fact]
    public void ConfigureLandsEachArgumentOnTheFieldItNames()
    {
        // The transposition guard. Deliberately uses four DIFFERENT numbers, so any swap
        // between pitch and bank -- or between the two halves of the pitch range -- fails.
        using var tone = new AudioToneGenerator();

        tone.Configure(300f, 900f, pitchRangeDegrees: 6.0, bankRangeDegrees: 5.0);
        var m = tone.MappingForTests;

        Assert.Equal(300f, m.Min);
        Assert.Equal(900f, m.Max);
        Assert.Equal(6.0, m.Down);
        Assert.Equal(6.0, m.Up);
        Assert.Equal(5.0, m.Bank);
    }

    [Fact]
    public void AConfiguredRangeIsSymmetricSoTheMappingHasNoKink()
    {
        // Visual guidance and the landing-flare assist both configure a symmetric range, and
        // their zero-beat design rests on one straight line. Equal halves are what deliver it.
        using var tone = new AudioToneGenerator();

        tone.Configure(200f, 800f, pitchRangeDegrees: 6.0, bankRangeDegrees: 5.0);
        var m = tone.MappingForTests;

        Assert.Equal(m.Down, m.Up);
        Assert.Equal(200.0, AudioToneGenerator.PitchToFrequency(-6.0, m.Min, m.Max, m.Down, m.Up), 6);
        Assert.Equal(500.0, AudioToneGenerator.PitchToFrequency(0.0, m.Min, m.Max, m.Down, m.Up), 6);
        Assert.Equal(800.0, AudioToneGenerator.PitchToFrequency(6.0, m.Min, m.Max, m.Down, m.Up), 6);
    }

    [Theory]
    [InlineData(0f, 800f, 6.0, 5.0)]      // min not positive
    [InlineData(800f, 200f, 6.0, 5.0)]    // max below min
    [InlineData(200f, 800f, 0.0, 5.0)]    // zero pitch range -- would divide by zero
    [InlineData(200f, 800f, 6.0, 0.0)]    // zero bank range
    [InlineData(200f, 800f, -6.0, 5.0)]   // negative pitch range
    public void AnInvalidConfigurationIsRejectedWholesale(float min, float max, double pitch, double bank)
    {
        // Rejection keeps the CLASS DEFAULTS, which are asymmetric -- NOT the symmetric range
        // the caller asked for. That is the documented (and logged) behaviour; this pins that a
        // bad value can never half-apply and leave a mixed mapping behind.
        using var tone = new AudioToneGenerator();

        tone.Configure(min, max, pitch, bank);
        var m = tone.MappingForTests;

        Assert.Equal(200f, m.Min);
        Assert.Equal(800f, m.Max);
        Assert.Equal(10.0, m.Down);
        Assert.Equal(20.0, m.Up);
        Assert.Equal(10.0, m.Bank);
    }
}
