using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
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
        // From the decision the guidance tone asks for wings level at once: following the aircraft's
        // bank down at the rate limit would ask a pilot rolling out for more bank than they have.
        Assert.Equal(0.0, rig.Bank, 9);
    }

    // Vectoring times the turn onto final on the angle BA chose (about 32° here), which the guidance is
    // turning the aircraft onto, not on the 10° it is cutting at so far: about 80 s, not 245.
    [Fact]
    public void Vectoring_times_the_turn_on_the_chosen_intercept()
    {
        var rig = PressedAt(10, 2, 0);
        double cut = VisualGuidanceJoin.Decide(new VisualGuidanceApproachPoint(10, 2), 0, 140, VisualGuidanceJoin.DefaultCaptureGain,
            new VisualGuidanceProfile()).ClampDeg;
        double seconds = VisualGuidanceJoin.SecondsToTurn(2, cut, cut, 140, VisualGuidanceJoin.DefaultCaptureGain, 1.0)!.Value;
        rig.Place(10, 2, -10);
        rig.UpdateAt(1.0 / 30);
        string fives = (Math.Round(seconds / 5, MidpointRounding.AwayFromZero) * 5).ToString("0", CultureInfo.InvariantCulture);
        Assert.Contains($"turn in about {fives} seconds", rig.Readout);
        Assert.InRange(seconds, 70, 90);
    }

    // F while vectoring says where the aircraft will be lined up counting the turn onto the cut, the
    // same sum BA picked the cut with: from 1.8 nm beside a 10 nm final, 6.2 nm, not the 6.5 the
    // straight-cut formula alone gives (both well inside their rounding).
    [Fact]
    public void F_while_vectoring_counts_the_turn_in_where_it_joins()
    {
        var rig = PressedAt(10, 1.8, 0);
        var profile = new VisualGuidanceProfile();
        var at = new VisualGuidanceApproachPoint(10, 1.8);
        double cut = VisualGuidanceJoin.Decide(at, 0, 140, VisualGuidanceJoin.DefaultCaptureGain, profile).ClampDeg;
        Assert.Equal(6.19, VisualGuidanceJoin.JoinAlongNm(at, 0, cut, 140, VisualGuidanceJoin.DefaultCaptureGain, profile), 0.01);
        Assert.StartsWith("Vectoring to final, joining 6.2 miles out.", rig.Readout);
    }

    // Heading away from final there is no time to the turn yet.
    [Fact]
    public void Vectoring_heading_away_gives_no_time_to_the_turn()
    {
        var rig = PressedAt(10, 2, 180);
        Assert.Equal("Vectoring", rig.Decision);
        Assert.DoesNotContain("turn in about", rig.Readout);
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

    // ── What BA says ──────────────────────────────────────────────────────────────────────────

    private static readonly Regex SayLine = new(@"^VGREC say t=(\S+) imm=([01]) text=(.*)$", RegexOptions.CultureInvariant);

    private static List<(double T, string Text)> Said(List<string> recording)
        => recording.Select(l => SayLine.Match(l)).Where(m => m.Success)
            .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[3].Value)).ToList();

    private static (Rig Rig, List<string> Lines) RecordedAt(double along, double cross, double rel, double gsKt = 140)
    {
        var lines = new List<string>();
        var rig = new Rig(record: lines.Add);
        rig.Place(along, cross, rel, altOffsetFt: AboveFieldOffset(along), gsKt: gsKt);
        rig.UpdateAt(0);
        return (rig, lines);
    }

    // ATC's 30° cut from 3 nm right, flown straight at 140 kt: "Joining final", the countdown at 30 and
    // 15 s, then "Turn onto final" where the capture law first asks 5° of bank (136.7 s from 3 nm).
    [Fact]
    public void Joining_counts_down_and_turns()
    {
        var (rig, lines) = RecordedAt(12, 3, -30);
        double tCue = VisualGuidanceJoin.SecondsToTurn(3, 30, 30, 140, VisualGuidanceJoin.DefaultCaptureGain, 1.0)!.Value;
        double nmPerS = 140.0 / 3600;
        rig.FlyFor(tCue + 2, 30, beforeEach: t =>
            rig.Place(12 - nmPerS * Math.Cos(30 * Math.PI / 180) * t, 3 - nmPerS * 0.5 * t, -30));
        var said = Said(lines);
        Assert.Single(said, s => s.Text == "Joining final");
        Assert.InRange(said.Single(s => s.Text == "Turn onto final in 30 seconds").T, tCue - 30 - 0.5, tCue - 30 + 0.5);
        Assert.InRange(said.Single(s => s.Text == "Turn onto final in 15 seconds").T, tCue - 15 - 0.5, tCue - 15 + 0.5);
        Assert.InRange(said.Single(s => s.Text == "Turn onto final").T, tCue - 0.5, tCue + 0.5);
    }

    [Fact]
    public void Heading_does_not_cross_final_is_said_once()
    {
        var (rig, lines) = RecordedAt(12, 3, -30);
        rig.FlyFor(5, 30);
        double turnedAt = rig.Now + 1.0 / 30;
        rig.FlyFor(20, 30, beforeEach: _ => rig.Place(12, 3, 0));
        var call = Assert.Single(Said(lines), s => s.Text == "Heading does not cross final");
        Assert.InRange(call.T - turnedAt, 3.0 - 0.001, 3.1);   // the record keeps 3 decimals of t
    }

    [Fact]
    public void On_final_is_said_and_established_is_not()
    {
        var (rig, lines) = RecordedAt(8, 0, 0);
        rig.FlyFor(5, 30);
        var said = Said(lines);
        Assert.Single(said, s => s.Text == "On final");
        Assert.DoesNotContain(said, s => s.Text == "Established on final");
    }

    // 0.2 nm off is inside the turn cue at the decision: the turn is already under way, so "Turn onto
    // final" is not said (decision 3); the state line and the tone say it.
    [Fact]
    public void Vectored_onto_final_hears_established()
    {
        var (rig, lines) = RecordedAt(8, 0.2, 0);
        rig.FlyFor(2.9, 30, beforeEach: _ => rig.Place(8, 0.05, 0));
        rig.FlyFor(2, 30);
        var said = Said(lines);
        var vectoring = said.Single(s => s.Text == "Vectoring to final");
        var established = said.Single(s => s.Text == "Established on final");
        Assert.True(established.T > vectoring.T);
        Assert.DoesNotContain(said, s => s.Text == "Turn onto final");
    }

    [Fact]
    public void Unable_is_said_once_and_F_says_where_final_is()
    {
        var (rig, lines) = RecordedAt(-1, 3, 180);
        rig.FlyFor(5, 30);
        Assert.Single(Said(lines), s => s.Text == "Unable to join final from here");
        Assert.Equal("Unable to join final from here. Final 3.0 miles to your right.", rig.Readout);
    }

    [Fact]
    public void The_mile_calls_say_out()
    {
        var (rig, lines) = RecordedAt(5.3, 0, 0);
        double nmPerS = 140.0 / 3600;
        rig.FlyFor(15, 30, beforeEach: t => rig.Place(5.3 - nmPerS * t, 0, 0));
        Assert.Single(Said(lines), s => s.Text == "5 miles out");
    }

    // Pressed inside a mark, the mile calls start with the next mark crossed: at 0.3 nm, no "5 miles
    // out", "3 miles out" and "1 mile out" one after another; at 4 nm, "3 miles out" when it is.
    [Fact]
    public void A_mile_call_is_said_only_when_its_mark_is_crossed()
    {
        var (close, closeLines) = RecordedAt(0.3, 0, 0);
        close.FlyFor(4, 30);
        Assert.DoesNotContain(Said(closeLines), s => s.Text.EndsWith(" out", StringComparison.Ordinal));

        var (four, fourLines) = RecordedAt(4.0, 0, 0);
        double nmPerS = 140.0 / 3600;
        four.FlyFor(30, 30, beforeEach: t => four.Place(4.0 - nmPerS * t, 0, 0));
        var calls = Said(fourLines).Where(s => s.Text.EndsWith(" out", StringComparison.Ordinal)).Select(s => s.Text).ToList();
        Assert.Equal(new[] { "3 miles out" }, calls);
    }

    // F while joining: where the aircraft will be lined up on final, from the heading flown now. 2.9 nm
    // off, so the join (6.62 nm) and the time (132 s) sit well inside their rounding: the rig's point
    // is projected at the threshold's course, which moves about 0.01 nm into along-track 12 nm out.
    [Fact]
    public void F_while_joining_says_where_it_joins()
    {
        var (rig, _) = RecordedAt(12, 2.9, -30);
        double join = 12 - VisualGuidanceJoin.AlongToEstablishNm(2.9, 30, 140, VisualGuidanceJoin.DefaultCaptureGain);
        double seconds = VisualGuidanceJoin.SecondsToTurn(2.9, 30, 30, 140, VisualGuidanceJoin.DefaultCaptureGain, 1.0)!.Value;
        string expected = $"Joining final {join.ToString("0.0", CultureInfo.InvariantCulture)} miles out. 30 degree intercept, " +
            $"turn in about {(Math.Round(seconds / 5, MidpointRounding.AwayFromZero) * 5).ToString("0", CultureInfo.InvariantCulture)} seconds.";
        Assert.Equal(expected, rig.Readout);
    }

    // Checkpoint review C1: a join whose crossing point drifts inside the minimum final (a 25° cut from
    // 3 nm, meeting final 5.57 nm out) still converges, so the turn onto final still comes where the
    // capture law first asks 5° of bank, not 300 ft from the line with a sudden steep demand.
    [Fact]
    public void A_join_that_drifts_inside_the_minimum_final_still_gets_its_turn()
    {
        var (rig, lines) = RecordedAt(12, 3, -30);
        rig.FlyFor(5, 30);
        double t0 = rig.Now, v = 140.0 / 3600, c = Math.Cos(25 * Math.PI / 180), s = Math.Sin(25 * Math.PI / 180);
        // 170 s on the 25° cut closes 2.79 nm, ending 0.21 nm off, inside the 0.273 nm cue for 25°.
        rig.FlyFor(170, 30, beforeEach: t => rig.Place(12 - v * c * (t - t0), 3 - v * s * (t - t0), -25));
        Assert.True(rig.Bank > 1, $"bank {rig.Bank}");
        Assert.Contains(Said(lines), x => x.Text == "Turn onto final");
    }

    // Checkpoint review I1: unable to join, the altitude is the pilot's, as while joining: the pitch
    // tone sits on the aircraft's own pitch instead of diving for a path far below the far-side start.
    [Fact]
    public void Unable_sits_the_pitch_on_the_aircraft()
    {
        var (rig, _) = RecordedAt(-1, 3, 180);
        rig.FlyFor(5, 30, afterEach: _ => Assert.Equal(2.0, rig.Pitch, 0.01));
    }

    // Checkpoint review I2: a mile call passed once is said again after the distance has risen back
    // above it: pressed 1.6 nm from the threshold, flown out past 5 nm and back in.
    [Fact]
    public void A_mile_call_is_said_again_after_flying_back_out_past_it()
    {
        var (rig, lines) = RecordedAt(-1, 1.2, 180);
        rig.FlyFor(2, 30);
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(5.5, 0, 0));
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(4.9, 0, 0));
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(2.9, 0, 0));
        var calls = Said(lines).Where(x => x.Text.EndsWith(" out", StringComparison.Ordinal)).Select(x => x.Text).ToList();
        Assert.Equal(new[] { "5 miles out", "3 miles out" }, calls);
    }

    // Checkpoint review I3: joining holds the heading being flown, ATC's or the pilot's. A pilot who
    // turns on purpose (a new heading from ATC) is not told to roll back while banked, as while deciding.
    [Fact]
    public void Joining_does_not_call_against_a_deliberate_turn()
    {
        var (rig, lines) = RecordedAt(12, 3, -30);
        rig.FlyFor(6, 30);
        rig.FlyFor(4, 30, beforeEach: _ => rig.Place(12, 3, -30, bankDeg: -20));
        Assert.DoesNotContain(Said(lines), x => Regex.IsMatch(x.Text, @"^\d+ (left|right)$"));
    }

    [Fact]
    public void F_while_deciding_says_it_waits_for_wings_level()
    {
        var rig = PressedAt(12, 3, -30, bankDeg: 20);
        Assert.Equal("Visual guidance starting, waiting for wings level.", rig.Readout);
    }
}
