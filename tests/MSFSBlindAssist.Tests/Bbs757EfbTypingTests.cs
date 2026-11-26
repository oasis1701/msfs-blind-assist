using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757EfbTypingTests
{
    private static readonly Bbs757EfbKeyboard Kb =
        Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25-navigation-perf-calc"))!.Keyboard!;

    private static readonly Bbs757EfbKeyboardState Rest = new(true, true, false, "t", false, false, false, Rectangle.Empty);

    private static Point Lower(string s) => Kb.Keys.First(k => k.Lower == s).Hit;
    private static Point Upper(string s) => Kb.Keys.First(k => k.Upper == s).Hit;
    private static Point Third(string s) => Kb.Keys.First(k => k.Third == s).Hit;
    private static Point Named(string n) => Kb.Keys.First(k => k.Name == n).Hit;

    private static IReadOnlyList<Point> Taps(string text, int caret = 0, int length = 0, Bbs757EfbKeyboardState? state = null)
    {
        var plan = Bbs757EfbTyping.Plan(Kb, state ?? Rest, text, caret, length, out var missing);
        Assert.NotNull(plan);
        Assert.Null(missing);
        return plan!.Taps;
    }

    [Fact]
    public void Lower_case_letters_tap_their_keys() =>
        Assert.Equal(new[] { Lower("k"), Lower("j"), Lower("f"), Lower("k") }, Taps("kjfk"));

    [Fact]
    public void A_capital_takes_one_shift() =>
        Assert.Equal(new[] { Named("btn_shift"), Upper("K"), Lower("j") }, Taps("Kj"));

    [Fact]
    public void Mixed_case_and_digits_take_the_right_layers() =>
        Assert.Equal(new[]
        {
            Named("btn_shift"), Upper("A"), Named("btn_numeric_switch"), Third("1"), Third("2"),
            Named("btn_numeric_switch"), Lower("b"),
        }, Taps("A12b"));

    [Fact]
    public void A_space_taps_the_space_bar() =>
        Assert.Equal(new[] { Lower("a"), Named("btn_space"), Lower("b") }, Taps("a b"));

    [Fact]
    public void Clearing_taps_backspace_before_the_caret_and_delete_after_it() =>
        Assert.Equal(new[] { Named("btn_backspace"), Named("btn_backspace"), Named("btn_delete"), Named("btn_delete"), Lower("a") },
            Taps("a", caret: 2, length: 4));

    [Fact]
    public void A_character_on_no_key_plans_nothing_and_names_it()
    {
        var plan = Bbs757EfbTyping.Plan(Kb, Rest, "ké", 0, 0, out var missing);
        Assert.Null(plan);
        Assert.Equal('é', missing);
    }

    [Fact]
    public void A_long_text_is_split_into_runs_without_losing_the_layer()
    {
        var plan = Bbs757EfbTyping.Plan(Kb, Rest, new string('a', 45) + "1" + new string('b', 10), 0, 0, out _)!;
        var runs = Bbs757EfbTyping.Runs(plan);
        Assert.All(runs, r => Assert.True(r.Count <= Bbs757EfbTyping.MaxTapsPerRun));
        Assert.Equal(plan.Taps, runs.SelectMany(r => r).ToList());
        Assert.Equal(2, runs.Count);
        // the layer switch sits in the plan itself, so the second run needs no state: the 1 follows its numeric switch
        int one = plan.Taps.ToList().IndexOf(Third("1"));
        Assert.Equal(Named("btn_numeric_switch"), plan.Taps[one - 1]);
    }

    [Fact]
    public void Nothing_to_type_and_nothing_to_clear_is_an_empty_plan()
    {
        var plan = Bbs757EfbTyping.Plan(Kb, Rest, "", 0, 0, out var missing);
        Assert.NotNull(plan);
        Assert.Empty(plan!.Taps);
        Assert.Null(missing);
        Assert.Empty(Bbs757EfbTyping.Runs(plan));
    }

    [Fact]
    public void Caps_on_at_the_start_is_turned_off_before_a_lower_letter() =>
        Assert.Equal(new[] { Named("btn_double_shift"), Lower("a") },
            Taps("a", state: Rest with { Caps = true }));

    [Fact]
    public void Caps_on_types_a_capital_as_is() =>
        Assert.Equal(new[] { Upper("A") }, Taps("A", state: Rest with { Caps = true }));

    [Fact]
    public void Consecutive_digits_switch_the_layer_once() =>
        Assert.Equal(new[] { Named("btn_numeric_switch"), Third("1"), Third("2"), Third("3") }, Taps("123"));

    [Fact]
    public void The_third_layer_already_on_needs_no_switch() =>
        Assert.Equal(new[] { Third("1") }, Taps("1", state: Rest with { Numeric = true }));

    [Fact]
    public void A_missing_command_key_plans_nothing_without_naming_a_character()
    {
        var bare = Kb with { Keys = Kb.Keys.Where(k => k.Name != "btn_shift").ToList() };
        var plan = Bbs757EfbTyping.Plan(bare, Rest, "A", 0, 0, out var missing);
        Assert.Null(plan);
        Assert.Null(missing);
    }

    [Fact]
    public void The_keypad_clears_with_DEL_then_taps_each_digit()
    {
        // centers 655.5 / 435.5 truncate to 655 / 435; DEL is row 4, column 1 (775,735)
        var plan = Bbs757EfbTyping.PlanKeypad(new Rectangle(595, 348, 360, 430), "70", out var missing)!;
        Assert.Null(missing);
        Assert.Equal(new[] { new Point(775, 735), new Point(655, 435), new Point(775, 660) }, plan.Taps);
    }

    [Fact]
    public void The_keypad_reaches_the_minus_and_the_point()
    {
        var plan = Bbs757EfbTyping.PlanKeypad(new Rectangle(595, 348, 360, 430), "-.", out _)!;
        Assert.Equal(new[] { new Point(775, 735), new Point(655, 660), new Point(895, 660) }, plan.Taps);
    }

    [Fact]
    public void A_letter_has_no_keypad_key()
    {
        Assert.Null(Bbs757EfbTyping.PlanKeypad(new Rectangle(595, 348, 360, 430), "1a", out var missing));
        Assert.Equal('a', missing);
    }

    [Fact]
    public void An_empty_keypad_rectangle_plans_nothing()
    {
        Assert.Null(Bbs757EfbTyping.PlanKeypad(Rectangle.Empty, "1", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void The_keypads_OK_is_row_four_column_two()
    {
        // The live OK key was measured at 895,735 under the fuel input dialog's keypad [595.5,348 360x430] (the reader truncates to 595).
        Assert.Equal(new Point(895, 735), Bbs757EfbTyping.KeypadOk(new Rectangle(595, 348, 360, 430)));
        Assert.Equal(new Point(1337, 653), Bbs757EfbTyping.KeypadOk(new Rectangle(1037, 266, 360, 430)));   // the TOD pause keypad
        Assert.Null(Bbs757EfbTyping.KeypadOk(Rectangle.Empty));
    }

    [Fact]
    public void A_digit_between_letters_switches_the_layer_and_back() =>
        Assert.Equal(new[]
        {
            Lower("a"), Named("btn_numeric_switch"), Third("1"), Named("btn_numeric_switch"), Named("btn_shift"), Upper("B"),
        }, Taps("a1B"));

    [Fact]
    public void Shift_on_at_the_start_is_turned_off_before_a_lower_letter() =>
        Assert.Equal(new[] { Named("btn_shift"), Lower("a") }, Taps("a", state: Rest with { Shift = true }));

    [Fact]
    public void A_backspace_clears_the_shift_so_no_shift_tap_comes_before_a_lower_letter() =>
        Assert.Equal(new[] { Named("btn_backspace"), Named("btn_backspace"), Lower("a") },
            Taps("a", caret: 2, length: 2, state: Rest with { Shift = true }));

    [Fact]
    public void The_pace_is_the_measured_one() => Assert.Equal(100, Bbs757EfbTyping.TapGapMs);
}
