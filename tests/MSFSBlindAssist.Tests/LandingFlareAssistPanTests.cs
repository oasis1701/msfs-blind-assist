using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The manual landing assist pans by the same "steer toward the tone" setting as takeoff assist
/// (TakeoffAssistSteerTowardTone). It shipped reading the retired TakeoffAssistInvertPanning. The July
/// tone migration seeds SteerTowardTone FROM InvertPanning, and the assist's own pan is already on the
/// steer side, so for every migrated settings file the extra flip reversed the flare and rollout tones
/// against takeoff assist. Each case sets the two settings to disagree, so reading the retired one
/// again fails it.
/// </summary>
public class LandingFlareAssistPanTests
{
    private static UserSettings Settings(bool steerToward, bool legacyInvert, bool hardPan = false) => new()
    {
        TakeoffAssistSteerTowardTone = steerToward,
        TakeoffAssistInvertPanning = legacyInvert,
        TakeoffAssistHardPanTone = hardPan,
    };

    [Fact]
    public void A_migrated_steer_toward_pilot_hears_the_tone_on_the_steer_side()
    {
        // The migration left InvertPanning = true behind for this pilot.
        Assert.Equal(0.5, LandingFlareAssistManager.PanFor(2.5, Settings(steerToward: true, legacyInvert: true)), 3);
    }

    [Fact]
    public void A_migrated_steer_away_pilot_hears_the_tone_on_the_other_side()
    {
        Assert.Equal(-0.5, LandingFlareAssistManager.PanFor(2.5, Settings(steerToward: false, legacyInvert: false)), 3);
    }

    [Fact]
    public void Proportional_pan_is_clamped_to_full_scale()
    {
        var settings = Settings(steerToward: true, legacyInvert: true);
        Assert.Equal(1.0, LandingFlareAssistManager.PanFor(12.0, settings), 3);
        Assert.Equal(-1.0, LandingFlareAssistManager.PanFor(-12.0, settings), 3);
    }

    [Fact]
    public void Hard_pan_steer_toward_slams_to_the_steer_side()
    {
        var settings = Settings(steerToward: true, legacyInvert: true, hardPan: true);
        Assert.Equal(1.0, LandingFlareAssistManager.PanFor(0.1, settings), 3);
        Assert.Equal(-1.0, LandingFlareAssistManager.PanFor(-0.1, settings), 3);
        Assert.Equal(0.0, LandingFlareAssistManager.PanFor(0.0, settings), 3);
    }

    [Fact]
    public void Hard_pan_steer_away_slams_to_the_other_side()
    {
        Assert.Equal(-1.0,
            LandingFlareAssistManager.PanFor(0.1, Settings(steerToward: false, legacyInvert: false, hardPan: true)), 3);
    }
}
