using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When a joining aircraft counts as level (<see cref="VisualGuidanceLevelHold"/>): under 200 fpm for
/// 5 s holds that altitude; a pitch more than 1.5° from both the cue and the learned level attitude ends
/// the hold. Updates at 10 Hz; the level attitude (trim) is 2.0° unless a test says otherwise.
/// </summary>
public class VisualGuidanceLevelHoldTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static VisualGuidanceLevelHold Held(double alt = 3000)
    {
        var hold = new VisualGuidanceLevelHold(VisualGuidanceVerticalTuning.Default);
        for (int i = 0; i <= 50; i++) hold.Update(T0.AddSeconds(i * 0.1), alt, -100, 2.0, 2.0, 2.0);
        Assert.Equal(alt, hold.HeldAltitudeFt);
        return hold;
    }

    [Fact]
    public void Holds_after_five_seconds_level()
    {
        var hold = new VisualGuidanceLevelHold(VisualGuidanceVerticalTuning.Default);
        for (int i = 0; i <= 49; i++) hold.Update(T0.AddSeconds(i * 0.1), 3000 - i * 0.1, -100, 2.0, 2.0, 2.0);
        Assert.Null(hold.HeldAltitudeFt);
        hold.Update(T0.AddSeconds(5.0), 2995.0, -100, 2.0, 2.0, 2.0);
        Assert.Equal(2995.0, hold.HeldAltitudeFt);
    }

    [Fact]
    public void A_deliberate_pitch_ends_it()
    {
        var hold = Held();
        hold.Update(T0.AddSeconds(6.0), 3000, -100, 2.0 - 1.4, 2.0, 2.0);
        Assert.Equal(3000.0, hold.HeldAltitudeFt);
        hold.Update(T0.AddSeconds(6.1), 3000, -100, 2.0 - 1.6, 2.0, 2.0);
        Assert.Null(hold.HeldAltitudeFt);
    }

    // The pilot has not moved: the pitch is at the level attitude while the cue, asking back from a drift,
    // is 1.6° away. The air moved the aircraft, so the hold stays (the final review's finding 2).
    [Fact]
    public void The_cue_walking_away_from_a_steady_pilot_does_not_end_it()
    {
        var hold = Held();
        hold.Update(T0.AddSeconds(6.0), 3000, -100, 2.0, 2.0 - 1.6, 2.0);
        Assert.Equal(3000.0, hold.HeldAltitudeFt);
    }

    // Following the cue far from the level attitude (a 1.6° climb back) does not end it either.
    [Fact]
    public void Following_the_cue_does_not_end_it()
    {
        var hold = Held();
        hold.Update(T0.AddSeconds(6.0), 3000, -100, 2.0 + 1.6, 2.0 + 1.6, 2.0);
        Assert.Equal(3000.0, hold.HeldAltitudeFt);
    }

    [Fact]
    public void Climbing_or_descending_is_not_level()
    {
        var hold = new VisualGuidanceLevelHold(VisualGuidanceVerticalTuning.Default);
        for (int i = 0; i <= 300; i++) hold.Update(T0.AddSeconds(i * 0.1), 3000 - i * 0.4, -250, 2.0, 2.0, 2.0);
        Assert.Null(hold.HeldAltitudeFt);
    }

    [Fact]
    public void Reset_forgets_the_hold()
    {
        var hold = Held();
        hold.Reset();
        Assert.Null(hold.HeldAltitudeFt);
    }
}
