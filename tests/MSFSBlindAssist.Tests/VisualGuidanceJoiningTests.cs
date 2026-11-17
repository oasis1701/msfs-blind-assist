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

    // ── The simple pattern from the far side ───────────────────────────────────────────────────
    // Right of final is south of runway 09: a downwind there is flown west with the runway on the right,
    // so the base turn and the turn onto final are both RIGHT turns; the left side mirrors (VG-18).

    // The pattern's base on the default profile at 140 kt (7.61 nm out, VisualGuidanceJoinTests).
    private static double BaseAlong(double cross = 3)
        => VisualGuidanceJoin.Decide(new VisualGuidanceApproachPoint(-1, cross), 180, 140, VisualGuidanceJoin.DefaultCaptureGain,
            new VisualGuidanceProfile()).Legs[1].AlongNm;

    [Fact]
    public void A_far_side_start_is_vectored()
    {
        var (rig, lines) = RecordedAt(-1, 3, 180);
        rig.FlyFor(5, 30);
        Assert.Equal("Pattern", rig.Decision);
        Assert.Equal("Vectoring", rig.Phase);
        Assert.Single(Said(lines), s => s.Text == "Vectoring to final");
    }

    // Lined up with the downwind, BA asks for nothing: wings level.
    [Fact]
    public void On_the_downwind_the_guidance_holds_it()
    {
        var rig = PressedAt(-1, 3, 180);
        rig.FlyFor(5, 30, afterEach: _ => Assert.InRange(rig.Bank, -0.1, 0.1));
    }

    [Fact]
    public void Right_side_pattern_turns_base_right_and_final_right()
    {
        double aB = BaseAlong();
        var rig = PressedAt(-1, 3, 180);
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB - 0.5, 3, 180));
        Assert.True(rig.Bank > 1, $"base: bank {rig.Bank}");
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB, 0.5, -90));
        Assert.True(rig.Bank > 1, $"final: bank {rig.Bank}");
    }

    [Fact]
    public void Left_side_pattern_turns_base_left_and_final_left()
    {
        double aB = BaseAlong(-3);
        var rig = PressedAt(-1, -3, 180);
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB - 0.5, -3, 180));
        Assert.True(rig.Bank < -1, $"base: bank {rig.Bank}");
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB, -0.5, 90));
        Assert.True(rig.Bank < -1, $"final: bank {rig.Bank}");
    }

    // On the far side flying the landing direction, the downwind is the other way round: the turn onto
    // it is away from the runway (right on the right side, left on the left), never across the field.
    [Fact]
    public void A_reversed_downwind_turns_away_from_the_runway()
    {
        var right = PressedAt(-1, 3, 0);
        Assert.Equal("Pattern", right.Decision);
        Assert.True(right.Bank > 1, $"right side: bank {right.Bank}");
        var left = PressedAt(-1, -3, 0);
        Assert.True(left.Bank < -1, $"left side: bank {left.Bank}");
    }

    [Fact]
    public void Pattern_says_turn_base_then_turn_onto_final()
    {
        double aB = BaseAlong();
        var (rig, lines) = RecordedAt(-1, 3, 180);
        rig.FlyFor(5, 30);
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB - 0.5, 3, 180));
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB, 0.5, -90));
        var said = Said(lines).Select(s => s.Text).Where(t => t is "Vectoring to final" or "Turn base" or "Turn onto final").ToList();
        Assert.Equal(new[] { "Vectoring to final", "Turn base", "Turn onto final" }, said);
    }

    // Cutting in toward final at 45°, 2 nm beside it and too close in to join (it meets final 5.5 nm out):
    // the short way onto the downwind is a 135° turn toward the runway, which gave up a mile of the room the
    // pattern's turns need (the CLOSE jets swung 3,900 ft past the centreline). BA turns the other way,
    // away from the runway (the owner's choice, 2026-11-05). A turn onto the downwind that does not pass
    // through the heading straight at the centreline keeps the short way: heading 10° outbound of it, left.
    [Fact]
    public void Cutting_in_close_turns_onto_the_downwind_away_from_the_runway()
    {
        var right = PressedAt(7.5, 2.0, -45);
        Assert.Equal("Pattern", right.Decision);
        Assert.True(right.Bank > 1, $"right side: bank {right.Bank}");
        var left = PressedAt(7.5, -2.0, 45);
        Assert.Equal("Pattern", left.Decision);
        Assert.True(left.Bank < -1, $"left side: bank {left.Bank}");
        var shortWay = PressedAt(-1, 3, -100);
        Assert.Equal("Pattern", shortWay.Decision);
        Assert.True(shortWay.Bank < -1, $"short way: bank {shortWay.Bank}");
    }

    // Pressed on a short final a little off the line, heading in: BA turns the pilot toward the runway, not
    // round the pattern away from it (the final review's C1). Pointing straight at the field from the far
    // side, the turn onto the downwind is the short one, left on the right side (the review's I1).
    [Fact]
    public void A_short_final_off_the_line_turns_toward_the_runway()
    {
        var rig = PressedAt(1.5, 0.4, 0);
        Assert.Equal("Vectoring", rig.Decision);
        Assert.True(rig.Bank < -1, $"short final: bank {rig.Bank}");
        var far = PressedAt(-1, 3, -90);
        Assert.Equal("Pattern", far.Decision);
        Assert.True(far.Bank < -1, $"pointing at the field: bank {far.Bank}");
    }

    // A far-side start already closer to final than the turn cue (the harness's far-side jets, 1 nm
    // beside the runway): BA flies the downwind first, out to its 1.9 nm (a left turn, away from the
    // runway), not straight onto final.
    [Fact]
    public void A_tight_far_side_start_flies_the_downwind_first()
    {
        var (rig, lines) = RecordedAt(-4, 1.0, 180);
        rig.FlyFor(5, 30);
        Assert.Equal("Pattern", rig.Decision);
        Assert.True(rig.Bank < -1, $"bank {rig.Bank}");
        Assert.DoesNotContain(Said(lines), s => s.Text == "Turn onto final");
    }

    // The countdown to the turn onto final runs on the base (Task 4's rules); on the downwind there is none.
    [Fact]
    public void The_base_counts_down_to_the_turn_onto_final()
    {
        double aB = BaseAlong();
        var (rig, lines) = RecordedAt(-1, 3, 180);
        rig.FlyFor(5, 30);
        rig.FlyFor(1, 30, beforeEach: _ => rig.Place(aB - 0.5, 3, 180));
        double v = 140.0 / 3600, t0 = rig.Now;
        double tCue = (3 - VisualGuidanceJoin.TurnCueCrossNm(90, 140, VisualGuidanceJoin.DefaultCaptureGain, 1.0)) / v;
        rig.FlyFor(tCue + 2, 30, beforeEach: t => rig.Place(aB, 3 - v * (t - t0), -90));
        var said = Said(lines);
        Assert.InRange(said.Single(s => s.Text == "Turn onto final in 30 seconds").T - t0, tCue - 30 - 0.5, tCue - 30 + 0.5);
        Assert.InRange(said.Single(s => s.Text == "Turn onto final in 15 seconds").T - t0, tCue - 15 - 0.5, tCue - 15 + 0.5);
        Assert.InRange(said.Single(s => s.Text == "Turn onto final").T - t0, tCue - 0.5, tCue + 0.5);
    }

    // The downwind and the base fly BA's planned descent (stage 5, VisualGuidancePlannedDescentTests), not
    // the 3° path, which the old law dove toward. On the downwind 2,000 ft above the field, far under the
    // plan, the cue asks for level flight: the learned trim, 5.0° here. On the base 1,500 ft above the path
    // it asks for no more than the plan's steepest, the glide path + 2°. (Stage 4 sat the pitch on the
    // aircraft here until the vertical path existed; checkpoint review I1, final review C1(b).)
    [Fact]
    public void The_pattern_flies_the_plan_not_the_path()
    {
        double aB = BaseAlong();
        var (rig, _) = RecordedAt(-1, 3, 180);
        rig.FlyFor(5, 30, afterEach: _ => Assert.Equal(0.0, rig.TargetDescent, 9));
        Assert.Equal(5.0, rig.Pitch, 0.05);
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB - 0.5, 3, 180, altOffsetFt: 1500),
            afterEach: _ => Assert.InRange(rig.TargetDescent, 0.0, 5.0 + 1e-9));
        Assert.Equal("Plan", rig.VerticalMode);
    }

    // Then the path there (stage 5): 2,000 ft above the 500 ft field is 2,500 ft, within 30 ft of the path's
    // 2,470 ft at the 6.0 nm join point, and far under BA's plan, so "target level".
    [Fact]
    public void F_on_the_downwind_says_the_leg_and_where_it_joins()
    {
        var (rig, _) = RecordedAt(-1, 3, 180);
        rig.FlyFor(5, 30);
        // Right of final, flying away from the runway: the centreline is on the right.
        Assert.Equal("Vectoring to a 6.0 mile final, downwind. Centreline 3.0 miles to your right. Path there 2,470 feet, you're on it, target level.",
            rig.Readout);
    }

    // On the base 1.5 nm from final: the turn cue is 1.16 nm out, 8.7 s away at 140 kt.
    [Fact]
    public void F_on_the_base_says_the_turn()
    {
        double aB = BaseAlong();
        var (rig, _) = RecordedAt(-1, 3, 180);
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB - 0.5, 3, 180));
        rig.FlyFor(5, 30, beforeEach: _ => rig.Place(aB, 1.5, -90));
        double seconds = (1.5 - VisualGuidanceJoin.TurnCueCrossNm(90, 140, VisualGuidanceJoin.DefaultCaptureGain, 1.0)) / (140.0 / 3600);
        Assert.InRange(seconds, 8.0, 9.5);
        // On the 3° path at 7.6 nm, about 510 ft above the path's altitude at the 6.0 nm join point. Flying the
        // base straight at the centreline: it is ahead, not "to your left" by a rounding.
        Assert.Matches(@"^Vectoring to a 6\.0 mile final, base\. Centreline 1\.5 miles ahead, turn in about 10 seconds\. "
            + @"Path there 2,4[67]0 feet, you're 5[0-2]0 high, target ([0-9,]+ down|level)\.$", rig.Readout);
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
        Assert.StartsWith("Vectoring to a 6.2 mile final.", rig.Readout);
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
    public void The_mile_calls_say_out()
    {
        var (rig, lines) = RecordedAt(5.3, 0, 0);
        double nmPerS = 140.0 / 3600;
        rig.FlyFor(15, 30, beforeEach: t => rig.Place(5.3 - nmPerS * t, 0, 0));
        Assert.Single(Said(lines), s => s.Text == "5 miles out, on path");   // on final, with the path (stage 5)
    }

    private static bool IsMileCall(string text) => Regex.IsMatch(text, @"^\d+ miles? out", RegexOptions.CultureInvariant);

    // Pressed inside a mark, the mile calls start with the next mark crossed: at 0.3 nm, no "5 miles
    // out", "3 miles out" and "1 mile out" one after another; at 4 nm, "3 miles out" when it is.
    [Fact]
    public void A_mile_call_is_said_only_when_its_mark_is_crossed()
    {
        var (close, closeLines) = RecordedAt(0.3, 0, 0);
        close.FlyFor(4, 30);
        Assert.DoesNotContain(Said(closeLines), s => IsMileCall(s.Text));

        var (four, fourLines) = RecordedAt(4.0, 0, 0);
        double nmPerS = 140.0 / 3600;
        four.FlyFor(30, 30, beforeEach: t => four.Place(4.0 - nmPerS * t, 0, 0));
        var calls = Said(fourLines).Where(s => IsMileCall(s.Text)).Select(s => s.Text).ToList();
        Assert.Equal(new[] { "3 miles out, on path" }, calls);
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
        // About a 6.6 mile final, so "a".
        string expected = $"Joining a {join.ToString("0.0", CultureInfo.InvariantCulture)} mile final. 30 degree intercept, " +
            $"turn in about {(Math.Round(seconds / 5, MidpointRounding.AwayFromZero) * 5).ToString("0", CultureInfo.InvariantCulture)} seconds.";
        // Then the path there (stage 5): about 2,670 ft at the 6.6 nm join point, the aircraft 1,710 ft above
        // it (on the path's height for 12 nm); no target, the altitude being ATC's or the pilot's.
        Assert.StartsWith(expected + " Path there ", rig.Readout);
        Assert.Matches(@" Path there 2,6[67]0 feet, you're 1,7[012]0 high\.$", rig.Readout);
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

    // Checkpoint review I2: a mile call passed once is said again after the distance has risen back
    // above it: pressed 1.6 nm from the threshold, flown out past 5 nm and back in. (Pressed on final:
    // since the pattern exists, the mile calls wait for its final leg.)
    [Fact]
    public void A_mile_call_is_said_again_after_flying_back_out_past_it()
    {
        var (rig, lines) = RecordedAt(1.6, 0, 0);
        rig.FlyFor(2, 30);
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(5.5, 0, 0));
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(4.9, 0, 0));
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(2.9, 0, 0));
        var calls = Said(lines).Where(x => IsMileCall(x.Text)).Select(x => x.Text).ToList();
        Assert.Equal(new[] { "5 miles out, on path", "3 miles out, on path" }, calls);   // on final, with the path (stage 5)
    }

    // On the pattern the mile calls wait for final: a far-side downwind passing 3 nm from the threshold
    // while flying away from final says nothing; flown out past 5 nm, round the base and onto final, the
    // calls come on the way in.
    [Fact]
    public void The_mile_calls_wait_for_final_on_the_pattern()
    {
        double aB = BaseAlong();
        var (rig, lines) = RecordedAt(-4, 1.0, 180);
        rig.FlyFor(2, 30);
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(-2, 1.9, 180, altOffsetFt: AboveFieldOffset(-2)));
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(5.5, 1.9, 180));
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(aB - 0.5, 1.9, 180));
        rig.FlyFor(2, 30, beforeEach: _ => rig.Place(aB, 1.0, -90));
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
