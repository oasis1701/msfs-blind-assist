// Characterization tests for FenixCabinReadback: after a press in the Cabin window the window says the
// resulting line once when it changes, or "<line>, unchanged" for the numeric presses (volume,
// temperature, preselect), and nothing else, so it never repeats what the screen reader already says
// (CORE-7). The caller passes the clock; there are no timers. The texts are the ones FenixCabinLines
// builds: "Volume: 5", "Door 1 left: closed, slide armed". In state mode
// the press waits for the line's State (the part the press changes) rather than its whole text.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinReadbackTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Short = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    private static FenixCabinLine Volume(string text) => new(FenixCabinSection.Audio, "audio.volume", text);
    private static FenixCabinLine Forward(string text) => new(FenixCabinSection.Temperature, "temp.forward", text);
    private static FenixCabinLine Door1Left(string text) => new(FenixCabinSection.Doors, "door.d1l", text);
    private static FenixCabinLine Aft(string text) => new(FenixCabinSection.Temperature, "temp.aft", text);

    private static IReadOnlyList<FenixCabinLine> Lines(params FenixCabinLine[] lines) => lines;

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Speaks_the_new_line_once()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);

        Assert.Empty(readback.Collect(Lines(Volume("Volume: 5")), T0 + Seconds(1)));
        Assert.Equal(["Volume: 6"], readback.Collect(Lines(Volume("Volume: 6")), T0 + Seconds(2)));
        Assert.Empty(readback.Collect(Lines(Volume("Volume: 7")), T0 + Seconds(3)));
    }

    [Fact]
    public void A_numeric_press_that_changes_nothing_still_answers()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);

        Assert.Empty(readback.Collect(Lines(Volume("Volume: 5")), T0 + Seconds(4)));
        Assert.Equal(["Volume: 5, unchanged"], readback.Collect(Lines(Volume("Volume: 5")), T0 + Seconds(6)));

        // Answered once: neither the same nor a later change says it again.
        Assert.Empty(readback.Collect(Lines(Volume("Volume: 5")), T0 + Seconds(7)));
        Assert.Empty(readback.Collect(Lines(Volume("Volume: 9")), T0 + Seconds(8)));
    }

    [Fact]
    public void Other_presses_time_out_silently()
    {
        var readback = new FenixCabinReadback();
        const string text = "Door 1 left: closed, slide armed";
        readback.Expect("door.d1l", text, T0, Long, speakIfUnchanged: false);

        Assert.Empty(readback.Collect(Lines(Door1Left(text)), T0 + Seconds(10)));
        Assert.Empty(readback.Collect(Lines(Door1Left(text)), T0 + Seconds(31)));

        // Gone: a later change is not an answer to that press.
        Assert.Empty(readback.Collect(Lines(Door1Left(text)), T0 + Seconds(32)));
        Assert.Empty(readback.Collect(Lines(Door1Left("Door 1 left: open, slide armed")), T0 + Seconds(60)));
    }

    [Fact]
    public void A_missing_line_counts_as_unchanged()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);

        // Only another line is there: the waiting goes on, then the numeric press answers from the press-time text.
        Assert.Empty(readback.Collect(Lines(Forward("Forward zone: 24.0 degrees, target 24.0 degrees, no offset")), T0 + Seconds(1)));
        Assert.Equal(["Volume: 5, unchanged"], readback.Collect(Lines(Forward("Forward zone: 24.0 degrees, target 24.0 degrees, no offset")), T0 + Seconds(6)));
        Assert.Empty(readback.Collect(Lines(), T0 + Seconds(7)));
    }

    [Fact]
    public void A_missing_line_is_silent_when_the_press_does_not_answer_unchanged()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("door.d1l", "Door 1 left: closed, slide armed", T0, Long, speakIfUnchanged: false);

        Assert.Empty(readback.Collect(Lines(), T0 + Seconds(31)));
        Assert.Empty(readback.Collect(Lines(), T0 + Seconds(32)));
    }

    [Fact]
    public void A_missing_line_with_no_press_time_text_has_nothing_to_say()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", null, T0, Short, speakIfUnchanged: true);

        Assert.Empty(readback.Collect(Lines(), T0 + Seconds(6)));
    }

    [Fact]
    public void A_line_that_did_not_exist_when_pressed_speaks_the_first_text_that_appears()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("door.d1l", null, T0, Long, speakIfUnchanged: false);

        Assert.Empty(readback.Collect(Lines(Volume("Volume: 5")), T0 + Seconds(1)));
        Assert.Equal(["Door 1 left: closed, slide armed"],
            readback.Collect(Lines(Door1Left("Door 1 left: closed, slide armed")), T0 + Seconds(2)));
        Assert.Empty(readback.Collect(Lines(Door1Left("Door 1 left: open, slide armed")), T0 + Seconds(3)));
    }

    [Fact]
    public void Two_keys_answer_in_press_order()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);
        readback.Expect("temp.forward", "Forward zone: 24.0 degrees, target 24.0 degrees, no offset", T0 + Seconds(1), Short, speakIfUnchanged: true);

        // The temperature line comes first in the list and both changed in one look: the volume was pressed first.
        string[] answers = readback.Collect(
            Lines(
                Forward("Forward zone: 24.0 degrees, target 25.0 degrees, no offset"),
                Volume("Volume: 6")),
            T0 + Seconds(2)).ToArray();

        Assert.Equal(["Volume: 6", "Forward zone: 24.0 degrees, target 25.0 degrees, no offset"], answers);
    }

    [Fact]
    public void Two_keys_answer_as_each_one_changes_and_the_other_keeps_waiting()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);
        readback.Expect("temp.forward", "Forward zone: 24.0 degrees, target 24.0 degrees, no offset", T0, Short, speakIfUnchanged: true);

        Assert.Equal(["Forward zone: 24.0 degrees, target 25.0 degrees, no offset"],
            readback.Collect(
                Lines(
                    Volume("Volume: 5"),
                    Forward("Forward zone: 24.0 degrees, target 25.0 degrees, no offset")),
                T0 + Seconds(1)));
        Assert.Equal(["Volume: 5, unchanged"],
            readback.Collect(
                Lines(
                    Volume("Volume: 5"),
                    Forward("Forward zone: 24.0 degrees, target 25.0 degrees, no offset")),
                T0 + Seconds(6)));
    }

    [Fact]
    public void Two_keys_that_time_out_together_answer_in_press_order()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("temp.aft", "Aft zone: 24.0 degrees, target 24.0 degrees, no offset", T0, Short, speakIfUnchanged: true);
        readback.Expect("audio.volume", "Volume: 5", T0 + Seconds(1), Short, speakIfUnchanged: true);

        string[] answers = readback.Collect(
            Lines(
                Volume("Volume: 5"),
                Aft("Aft zone: 24.0 degrees, target 24.0 degrees, no offset")),
            T0 + Seconds(7)).ToArray();

        Assert.Equal(["Aft zone: 24.0 degrees, target 24.0 degrees, no offset, unchanged", "Volume: 5, unchanged"], answers);
    }

    [Fact]
    public void A_repeated_press_replaces_the_expectation()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);
        readback.Expect("audio.volume", "Volume: 6", T0 + Seconds(3), Short, speakIfUnchanged: true);

        // The first press would have timed out at 5 s; the second one runs to 8 s and is the one that counts.
        Assert.Empty(readback.Collect(Lines(Volume("Volume: 6")), T0 + Seconds(6)));
        Assert.Equal(["Volume: 7"], readback.Collect(Lines(Volume("Volume: 7")), T0 + Seconds(7)));

        // One answer for the two presses, not two.
        Assert.Empty(readback.Collect(Lines(Volume("Volume: 7")), T0 + Seconds(9)));
    }

    [Fact]
    public void A_repeated_press_that_changes_nothing_answers_unchanged_once_when_its_own_window_ends()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 10", T0, Short, speakIfUnchanged: true);
        readback.Expect("audio.volume", "Volume: 10", T0 + Seconds(4), Short, speakIfUnchanged: true);

        Assert.Empty(readback.Collect(Lines(Volume("Volume: 10")), T0 + Seconds(6)));
        Assert.Equal(["Volume: 10, unchanged"], readback.Collect(Lines(Volume("Volume: 10")), T0 + Seconds(9)));
        Assert.Empty(readback.Collect(Lines(Volume("Volume: 10")), T0 + Seconds(10)));
    }

    [Fact]
    public void A_repeated_press_counts_as_the_latest_press()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);
        readback.Expect("temp.forward", "Forward zone: 24.0 degrees, target 24.0 degrees, no offset", T0 + Seconds(1), Short, speakIfUnchanged: true);
        readback.Expect("audio.volume", "Volume: 5", T0 + Seconds(2), Short, speakIfUnchanged: true);

        string[] answers = readback.Collect(
            Lines(
                Volume("Volume: 6"),
                Forward("Forward zone: 24.0 degrees, target 25.0 degrees, no offset")),
            T0 + Seconds(3)).ToArray();

        Assert.Equal(["Forward zone: 24.0 degrees, target 25.0 degrees, no offset", "Volume: 6"], answers);
    }

    [Fact]
    public void A_change_on_another_key_is_not_an_answer()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);

        Assert.Empty(readback.Collect(
            Lines(
                Volume("Volume: 5"),
                Forward("Forward zone: 24.0 degrees, target 25.0 degrees, no offset"),
                Door1Left("Door 1 left: open, slide armed")),
            T0 + Seconds(2)));
    }

    [Fact]
    public void A_door_has_the_whole_long_window_to_change()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("door.d1l", "Door 1 left: closed, slide armed", T0, Long, speakIfUnchanged: false);

        Assert.Empty(readback.Collect(Lines(Door1Left("Door 1 left: closed, slide armed")), T0 + Seconds(20)));
        Assert.Equal(["Door 1 left: open, slide armed"],
            readback.Collect(Lines(Door1Left("Door 1 left: open, slide armed")), T0 + Seconds(29)));
    }

    [Fact]
    public void A_change_seen_in_the_first_look_after_the_window_still_answers()
    {
        // The window ends the waiting; a change that is there at that look is the press's answer, not lost.
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);

        Assert.Equal(["Volume: 6"], readback.Collect(Lines(Volume("Volume: 6")), T0 + Seconds(6)));
    }

    [Fact]
    public void The_window_ends_after_the_given_time_not_before()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);

        Assert.Empty(readback.Collect(Lines(Volume("Volume: 5")), T0 + Seconds(4.999)));
        Assert.Equal(["Volume: 5, unchanged"], readback.Collect(Lines(Volume("Volume: 5")), T0 + Seconds(5)));
    }

    [Fact]
    public void Nothing_pending_says_nothing()
    {
        var readback = new FenixCabinReadback();

        Assert.Empty(readback.Collect(Lines(Volume("Volume: 5")), T0));
        Assert.Empty(readback.Collect(Lines(), T0 + Seconds(60)));
    }

    // ---- State mode: the press waits for the part of the line it changes ------------------------

    private static FenixCabinLine Door1Left(string text, string? state) => new(FenixCabinSection.Doors, "door.d1l", text, state);
    private static FenixCabinLine Cargo(string text, string? state) => new(FenixCabinSection.Doors, "cargo.forward", text, state);
    private static FenixCabinLine Forward(string text, string? state) => new(FenixCabinSection.Temperature, "temp.forward", text, state);

    [Fact]
    public void A_state_change_speaks_the_whole_current_line_once()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("door.d1l", "Door 1 left: closed, slide armed", T0, Long, speakIfUnchanged: false,
            currentState: "closed", byState: true);

        Assert.Equal(["Door 1 left: open, slide disarmed"],
            readback.Collect(Lines(Door1Left("Door 1 left: open, slide disarmed", "open")), T0 + Seconds(10)));
        Assert.Empty(readback.Collect(Lines(Door1Left("Door 1 left: closed, slide disarmed", "closed")), T0 + Seconds(11)));
    }

    [Fact]
    public void A_text_change_with_the_same_state_says_nothing()
    {
        // The slide disarms a poll before the door opens: not the press's answer.
        var readback = new FenixCabinReadback();
        readback.Expect("door.d1l", "Door 1 left: closed, slide armed", T0, Long, speakIfUnchanged: false,
            currentState: "closed", byState: true);

        Assert.Empty(readback.Collect(Lines(Door1Left("Door 1 left: closed, slide disarmed", "closed")), T0 + Seconds(1)));
        Assert.Equal(["Door 1 left: open, slide disarmed"],
            readback.Collect(Lines(Door1Left("Door 1 left: open, slide disarmed", "open")), T0 + Seconds(12)));

        // A silent press whose state never changes ends silently, whatever its text did.
        readback.Expect("door.d1l", "Door 1 left: open, slide disarmed", T0 + Seconds(20), Long, speakIfUnchanged: false,
            currentState: "open", byState: true);
        Assert.Empty(readback.Collect(Lines(Door1Left("Door 1 left: open, slide armed", "open")), T0 + Seconds(51)));
        Assert.Empty(readback.Collect(Lines(Door1Left("Door 1 left: closed, slide armed", "closed")), T0 + Seconds(52)));
    }

    [Fact]
    public void A_null_state_is_not_settled_and_the_later_settled_state_speaks()
    {
        // A cargo door goes closed, moving (no state), open: the answer is the open line, never "moving".
        var readback = new FenixCabinReadback();
        readback.Expect("cargo.forward", "Forward cargo door: closed", T0, Long, speakIfUnchanged: false,
            currentState: "closed", byState: true);

        Assert.Empty(readback.Collect(Lines(Cargo("Forward cargo door: moving", null)), T0 + Seconds(2)));
        Assert.Empty(readback.Collect(Lines(Cargo("Forward cargo door: no data", null)), T0 + Seconds(4)));
        Assert.Equal(["Forward cargo door: open"],
            readback.Collect(Lines(Cargo("Forward cargo door: open", "open")), T0 + Seconds(15)));
        Assert.Empty(readback.Collect(Lines(Cargo("Forward cargo door: moving", null)), T0 + Seconds(16)));
    }

    [Fact]
    public void A_press_without_a_state_speaks_the_first_settled_state()
    {
        // As a missing press-time text in text mode: the first state that appears is the answer.
        var readback = new FenixCabinReadback();
        readback.Expect("temp.forward", "Forward zone: no data", T0, Short, speakIfUnchanged: true, currentState: null, byState: true);

        Assert.Empty(readback.Collect(Lines(Forward("Forward zone: 24.0 degrees, target no data, offset no data", null)), T0 + Seconds(1)));
        Assert.Equal(["Forward zone: 24.0 degrees, target 24.0 degrees, no offset"],
            readback.Collect(Lines(Forward("Forward zone: 24.0 degrees, target 24.0 degrees, no offset", "24.0|0.0")), T0 + Seconds(2)));
    }

    [Fact]
    public void An_unchanged_state_at_the_window_end_says_the_current_line_unchanged()
    {
        // Only the measured air temperature drifts: the target did not move, and the line as it reads now is said.
        var readback = new FenixCabinReadback();
        readback.Expect("temp.forward", "Forward zone: 24.0 degrees, target 24.0 degrees, no offset", T0, Short,
            speakIfUnchanged: true, currentState: "24.0|0.0", byState: true);

        Assert.Empty(readback.Collect(Lines(Forward("Forward zone: 24.3 degrees, target 24.0 degrees, no offset", "24.0|0.0")), T0 + Seconds(1)));
        Assert.Equal(["Forward zone: 24.3 degrees, target 24.0 degrees, no offset, unchanged"],
            readback.Collect(Lines(Forward("Forward zone: 24.3 degrees, target 24.0 degrees, no offset", "24.0|0.0")), T0 + Seconds(5)));
        Assert.Empty(readback.Collect(Lines(Forward("Forward zone: 24.3 degrees, target 24.5 degrees, offset plus 0.5", "24.5|0.5")), T0 + Seconds(6)));
    }

    [Fact]
    public void A_state_that_stays_unsettled_to_the_window_end_answers_by_the_unchanged_rule()
    {
        var readback = new FenixCabinReadback();
        readback.Expect("temp.forward", "Forward zone: 24.0 degrees, target 24.0 degrees, no offset", T0, Short,
            speakIfUnchanged: true, currentState: "24.0|0.0", byState: true);

        Assert.Empty(readback.Collect(Lines(Forward("Forward zone: 24.0 degrees, target no data, offset no data", null)), T0 + Seconds(1)));
        Assert.Equal(["Forward zone: 24.0 degrees, target no data, offset no data, unchanged"],
            readback.Collect(Lines(Forward("Forward zone: 24.0 degrees, target no data, offset no data", null)), T0 + Seconds(5)));
    }

    [Fact]
    public void Text_mode_ignores_the_state()
    {
        // The default mode: the line's text decides, as before state mode existed.
        var readback = new FenixCabinReadback();
        readback.Expect("door.d1l", "Door 1 left: closed, slide armed", T0, Long, speakIfUnchanged: false);

        Assert.Equal(["Door 1 left: closed, slide disarmed"],
            readback.Collect(Lines(Door1Left("Door 1 left: closed, slide disarmed", "closed")), T0 + Seconds(1)));
    }

    [Fact]
    public void Missing_arguments_are_refused()
    {
        var readback = new FenixCabinReadback();

        Assert.Throws<ArgumentNullException>(() => readback.Expect(null!, "Volume: 5", T0, Short, true));
        Assert.Throws<ArgumentException>(() => readback.Expect("", "Volume: 5", T0, Short, true));
        Assert.Throws<ArgumentNullException>(() => readback.Collect(null!, T0));
        Assert.Throws<ArgumentNullException>(() => readback.CollectEnded(null!, T0));
    }

    // ---- Which expectation ended, and how (the own-press mark follows it) -----------------------

    [Fact]
    public void Each_ended_expectation_is_reported_with_what_it_said_or_null_when_it_ended_silently()
    {
        // A door that answered, a door that timed out silently, a numeric press that answered "unchanged",
        // and one still waiting, which is not reported.
        var readback = new FenixCabinReadback();
        readback.Expect("door.d1l", "Door 1 left: closed, slide armed", T0, Long, speakIfUnchanged: false, currentState: "closed", byState: true);
        readback.Expect("door.d4r", "Door 4 right: closed, slide armed", T0, Long, speakIfUnchanged: false, currentState: "closed", byState: true);
        readback.Expect("audio.volume", "Volume: 5", T0, Short, speakIfUnchanged: true);
        readback.Expect("temp.forward", "Forward zone: 24.0 degrees, target 24.0 degrees, no offset", T0 + Seconds(28), Short, speakIfUnchanged: true);

        var ended = readback.CollectEnded(Lines(
                new FenixCabinLine(FenixCabinSection.Doors, "door.d1l", "Door 1 left: open, slide armed", "open"),
                new FenixCabinLine(FenixCabinSection.Doors, "door.d4r", "Door 4 right: closed, slide armed", "closed"),
                Volume("Volume: 5"),
                Forward("Forward zone: 24.0 degrees, target 24.0 degrees, no offset")),
            T0 + Seconds(30));

        Assert.Equal(new[]
        {
            new FenixCabinReadbackEnd("door.d1l", "Door 1 left: open, slide armed"),
            new FenixCabinReadbackEnd("door.d4r", null),
            new FenixCabinReadbackEnd("audio.volume", "Volume: 5, unchanged")
        }, ended);
        Assert.True(readback.IsWaiting("temp.forward"));
    }

    [Fact]
    public void Collect_says_exactly_what_collect_ended_reports_as_said()
    {
        static FenixCabinReadback Pressed(DateTime at)
        {
            var readback = new FenixCabinReadback();
            readback.Expect("door.d1l", "Door 1 left: closed, slide armed", at, Long, speakIfUnchanged: false);
            readback.Expect("audio.volume", "Volume: 5", at, Short, speakIfUnchanged: true);
            return readback;
        }
        var lines = Lines(Door1Left("Door 1 left: open, slide armed"), Volume("Volume: 5"));

        Assert.Equal(
            Pressed(T0).CollectEnded(lines, T0 + Seconds(6)).Where(e => e.Text != null).Select(e => e.Text!).ToArray(),
            Pressed(T0).Collect(lines, T0 + Seconds(6)));
    }

    [Fact]
    public void A_key_is_waiting_from_its_press_until_its_expectation_ends()
    {
        var readback = new FenixCabinReadback();
        Assert.False(readback.IsWaiting("door.d1l"));

        readback.Expect("door.d1l", "Door 1 left: closed, slide armed", T0, Long, speakIfUnchanged: false, currentState: "closed", byState: true);
        Assert.True(readback.IsWaiting("door.d1l"));
        Assert.False(readback.IsWaiting("door.d1r"));

        // Still waiting while the line has not changed; a repeated press keeps it waiting.
        readback.CollectEnded(Lines(Door1Left("Door 1 left: closed, slide armed")), T0 + Seconds(10));
        readback.Expect("door.d1l", "Door 1 left: closed, slide armed", T0 + Seconds(10), Long, speakIfUnchanged: false, currentState: "closed", byState: true);
        Assert.True(readback.IsWaiting("door.d1l"));

        // Ended silently at its window's end: no longer waiting.
        readback.CollectEnded(Lines(Door1Left("Door 1 left: closed, slide armed")), T0 + Seconds(40));
        Assert.False(readback.IsWaiting("door.d1l"));
    }
}
