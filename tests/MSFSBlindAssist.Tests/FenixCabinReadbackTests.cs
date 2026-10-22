// Characterization tests for FenixCabinReadback: after a press in the Cabin window the window says the
// resulting line once when it changes, or "<line>, unchanged" for the numeric presses (volume,
// temperature, preselect), and nothing else, so it never repeats what the screen reader already says
// (CORE-7). The caller passes the clock; there are no timers. The texts are the ones FenixCabinLines
// builds (plan 08 Task 4): "Volume: 5", "Door 1 left: closed, slide armed".

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

    [Fact]
    public void Missing_arguments_are_refused()
    {
        var readback = new FenixCabinReadback();

        Assert.Throws<ArgumentNullException>(() => readback.Expect(null!, "Volume: 5", T0, Short, true));
        Assert.Throws<ArgumentException>(() => readback.Expect("", "Volume: 5", T0, Short, true));
        Assert.Throws<ArgumentNullException>(() => readback.Collect(null!, T0));
    }
}
