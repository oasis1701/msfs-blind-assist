using Xunit;
using Rig = MSFSBlindAssist.Tests.VisualGuidanceTestRig;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Joining final through the real manager on the shared rig (runway 09, 140 kt): what BA decides when
/// Ctrl+V is pressed, and the bank it asks for from there. Left and right as VG-18 pins them: right
/// of final and parallel, the guidance turns LEFT toward the line; right of final on a heading that
/// crosses it, it holds that heading, then turns RIGHT onto final. The rig does not move the
/// aircraft, so each new place is held 5 s before the bank is asserted (the cross-track rate window
/// is 4 s, and a jump between two places would read as a huge rate).
/// </summary>
public class VisualGuidanceJoiningTests
{
    private static Rig PressedAt(double along, double cross, double rel, double bankDeg = 0)
    {
        var rig = new Rig();
        rig.Place(along, cross, rel, bankDeg: bankDeg, altOffsetFt: AboveFieldOffset(along));
        rig.UpdateAt(0);
        return rig;
    }

    // The rig puts the aircraft on the 3° path, which is below the field past the threshold: there,
    // the offset that puts it 2,000 ft above the field instead (the harness's far-side height).
    internal static double AboveFieldOffset(double along)
        => along >= 0 ? 0 : 2000 - (along * 6076.12 * Math.Tan(3.0 * Math.PI / 180) + 60.0);

    [Fact]
    public void Pressing_on_final_goes_straight_to_final()
    {
        var rig = PressedAt(8, 0, 0);
        Assert.Equal("Final", rig.Phase);
        Assert.Equal("OnFinal", rig.Decision);
    }

    // ATC's 30° cut from 3 nm right crosses final 6.8 nm out: BA holds the heading, wings level.
    [Fact]
    public void Right_of_final_on_an_ATC_heading_holds_it()
    {
        var rig = PressedAt(12, 3, -30);
        Assert.Equal("Joining", rig.Decision);
        rig.FlyFor(10, 30, afterEach: _ =>
        {
            Assert.Equal("Joining", rig.Phase);
            Assert.InRange(rig.Bank, -0.1, 0.1);
        });
    }

    // A 15° cut crossing final 8.8 nm out: a fixed 30° intercept would have asked for a left turn.
    [Fact]
    public void A_shallow_ATC_heading_is_never_steepened()
    {
        var rig = PressedAt(20, 3, -15);
        rig.FlyFor(10, 30, afterEach: _ =>
        {
            Assert.Equal("Joining", rig.Phase);
            Assert.InRange(rig.Bank, -0.1, 0.1);
        });
    }

    // 0.3 nm right on a 30° cut: inside the 0.341 nm turn cue, so the turn onto final is due, and it is right.
    [Fact]
    public void Right_of_final_on_an_ATC_heading_turns_right_onto_final()
    {
        var rig = PressedAt(12, 0.3, -30);
        Assert.Equal("Joining", rig.Decision);
        Assert.True(rig.Bank > 1, $"bank {rig.Bank}");
        rig.FlyFor(5, 30);
        Assert.True(rig.Bank > 1, $"bank {rig.Bank}");
    }

    // At 65 kt the gain is 158° per nm and the bank per degree 0.756 (the speed factor), so the cue on
    // a 30° cut comes at 0.148 nm: 0.12 nm is inside it.
    [Fact]
    public void Right_of_final_on_an_ATC_heading_turns_right_onto_final_at_65_knots()
    {
        var rig = new Rig();
        rig.Place(8, 0.12, -30, gsKt: 65);
        rig.UpdateAt(0);
        Assert.Equal("Joining", rig.Decision);
        Assert.True(rig.Bank > 1, $"bank {rig.Bank}");
    }

    // The pilot turns parallel: the heading no longer crosses final. BA keeps holding it, and never
    // starts vectoring by itself.
    [Fact]
    public void A_heading_that_stops_crossing_is_held_and_joining_never_becomes_vectoring()
    {
        var rig = PressedAt(12, 3, -30);
        rig.FlyFor(5, 30);
        rig.FlyFor(20, 30, beforeEach: _ => rig.Place(12, 3, 0), afterEach: _ =>
        {
            Assert.Equal("Joining", rig.Phase);
            Assert.InRange(rig.Bank, -0.1, 0.1);
        });
    }

    [Fact]
    public void Right_of_final_flying_parallel_is_vectored_left()
    {
        var rig = PressedAt(10, 2, 0);
        Assert.Equal("Vectoring", rig.Decision);
        Assert.Equal("Vectoring", rig.Phase);
        Assert.True(rig.Bank < -1, $"bank {rig.Bank}");
    }

    [Fact]
    public void Left_of_final_flying_parallel_is_vectored_right()
    {
        var rig = PressedAt(10, -2, 0);
        Assert.Equal("Vectoring", rig.Decision);
        Assert.True(rig.Bank > 1, $"bank {rig.Bank}");
    }

    // Heading outbound 2 nm right: the turn back is 150° to the right, through the runway's side.
    [Fact]
    public void Heading_away_is_vectored_round()
    {
        var rig = PressedAt(10, 2, 180);
        Assert.Equal("Vectoring", rig.Decision);
        Assert.True(rig.Bank > 1, $"bank {rig.Bank}");
    }

    // Until the simple pattern exists (stage 4, Task 6), the far side says so and holds the heading.
    [Fact]
    public void A_position_that_needs_the_pattern_holds_its_heading_for_now()
    {
        var rig = PressedAt(-1, 3, 180);
        rig.FlyFor(5, 30);
        Assert.Equal("Pattern", rig.Decision);
        Assert.Equal("Unable", rig.Phase);
        Assert.InRange(rig.Bank, -0.1, 0.1);
    }

    // Ctrl+V in a turn: no state until the wings are nearly level; both tones sit on the aircraft.
    [Fact]
    public void Pressed_in_a_turn_decides_once_the_wings_are_level()
    {
        var rig = PressedAt(12, 3, -30, bankDeg: 20);
        rig.FlyFor(3, 30);
        Assert.Equal("Deciding", rig.Phase);
        Assert.Equal("None", rig.Decision);
        Assert.Equal(20.0, rig.Bank, 0.01);
        Assert.Equal(2.0, rig.Pitch, 0.01);

        rig.Place(12, 3, -30, bankDeg: 3);
        rig.UpdateAt(rig.Now + 1.0 / 30);
        Assert.Equal("Joining", rig.Decision);
    }

    // While joining the altitude is ATC's or the pilot's: the pitch tone sits on the aircraft's own pitch.
    [Fact]
    public void Joining_sits_the_pitch_on_the_aircraft()
    {
        var rig = PressedAt(12, 3, -30);
        rig.FlyFor(5, 30, afterEach: _ => Assert.Equal(2.0, rig.Pitch, 0.01));
    }

    [Fact]
    public void Established_after_three_seconds_on_the_line()
    {
        var rig = PressedAt(8, 0.2, 0);
        Assert.Equal("Vectoring", rig.Phase);
        rig.FlyFor(2.9, 30, beforeEach: _ => rig.Place(8, 0.05, 0));
        Assert.Equal("Vectoring", rig.Phase);
        rig.FlyFor(0.2, 30);
        Assert.Equal("Final", rig.Phase);
    }
}
