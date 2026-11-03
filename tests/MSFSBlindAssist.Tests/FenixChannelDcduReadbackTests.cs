// Characterization tests for FenixChannelDcduReadback, which gives the pilot what the DCDU changed after the pilot's own key
// press, once. Pure logic: the screens are constructed here, and the read-back is driven by hand with its own clock (a plain
// number of milliseconds).
//
// The contract: a press the window does not show on the row under the cursor is still heard ([CORE-7]: what the screen reader
// cannot say). NotePress takes no clock; the first sample after it is the "before" reading, and a press lands at least 200 ms
// later, so that sample is always before its effect. A soft key watches the status (the header's right half) and the first
// info line; any other key watches the first info line only. At the first change to something with words the read-back gives
// that text in plain words (both, status first, joined by ", " when both changed in one sample) and clears the press. It
// gives up silently when the window of 3 s since the before reading is over, and on a screen that is not ok. A change to
// nothing says nothing, a change with no press pending says nothing, and the key's name is never said.
//
// Measured on 3.0.0.7613 (2026-11-03): after a message key with nothing to step to the first info line reads "NO MORE MSG"
// for about 5 s; after one press of WILCO the status reads "ciWILCO" about 1.2 s later.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduReadbackTests
{
    private static readonly FenixDcduKey[] SoftKeys =
        [FenixDcduKey.LeftKey1, FenixDcduKey.LeftKey2, FenixDcduKey.RightKey1, FenixDcduKey.RightKey2];

    private static readonly FenixDcduKey[] OtherKeys =
    [
        FenixDcduKey.PageMinus, FenixDcduKey.PagePlus, FenixDcduKey.MessageBack, FenixDcduKey.MessageForward,
        FenixDcduKey.Print
    ];

    // A clearance on the DCDU with the status and the first info line given, and the keys of an open message. The key labels
    // are not read back, so a test can change them freely.
    private static FenixChannelSection<FenixDcduScreen> Screen(string status = "cOPEN", string info1 = "",
        string rightKey2 = "cWILCO*")
        => FenixChannelSection<FenixDcduScreen>.Ok(new FenixDcduScreen(
            "g 2021Z FROM EDDF", status,
            ["CLRD TO cEGSSw.".PadRight(24), "", "", "", ""], [info1, "MSG".PadRight(14), "2/2".PadRight(14)],
            "c*UNABLE", "", "cSTBY*", rightKey2));

    private static FenixChannelSection<FenixDcduScreen> Problem(string reason = "display off") => new(default, reason);

    // A read-back with a press noted and its before reading taken at 0 ms, on the given screen.
    private static FenixChannelDcduReadback Pressed(FenixDcduKey key, FenixChannelSection<FenixDcduScreen> before)
    {
        var readback = new FenixChannelDcduReadback();
        readback.NotePress(key);
        Assert.Null(readback.Sample(before, 0));
        return readback;
    }

    private static void InCulture(string name, Action action)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- The constants ----------------------------------------------------------------------

    [Fact]
    public void The_window_is_three_seconds()
    {
        Assert.Equal(3000L, FenixChannelDcduReadback.WindowMs);
    }

    // ---- What it says -----------------------------------------------------------------------

    [Fact]
    public void After_a_message_key_with_nothing_to_step_to_it_says_no_more_msg()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.MessageBack, Screen());

        // The Fenix draws it a moment after the press and keeps it for about 5 s: said once.
        Assert.Null(readback.Sample(Screen(), 100));
        Assert.Equal("NO MORE MSG", readback.Sample(Screen(info1: "NO MORE MSG"), 200));
        Assert.Null(readback.Sample(Screen(info1: "NO MORE MSG"), 300));
        Assert.Null(readback.Sample(Screen(info1: "NO MORE MSG"), 4000));
    }

    [Fact]
    public void After_wilco_it_says_wilco_about_a_second_after_the_press()
    {
        // The press is noted, the before reading is the first sample (status OPEN); the key's own label flickers at 0.3 s and
        // the status becomes WILCO at 1.2 s, with the keys relabeled: only the status is said.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());

        Assert.Null(readback.Sample(Screen(rightKey2: "ciWILCO*"), 300));
        Assert.Null(readback.Sample(Screen(rightKey2: "cWILCO*"), 800));
        Assert.Null(readback.Sample(Screen(rightKey2: "cWILCO*"), 1100));
        Assert.Equal("WILCO", readback.Sample(Screen(status: "ciWILCO", rightKey2: "cSEND*"), 1200));
        Assert.Null(readback.Sample(Screen(status: "ciWILCO", rightKey2: "cSEND*"), 1300));
    }

    [Fact]
    public void A_change_is_said_in_plain_words()
    {
        // No codes, no color names, runs of spaces collapsed, the ends trimmed.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.PagePlus, Screen());

        Assert.Equal("NO MORE MSG", readback.Sample(Screen(info1: "g NO MORE   MSG    "), 100));
    }

    [Fact]
    public void Both_changes_in_one_sample_are_said_together_status_first()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey1, Screen());

        Assert.Equal("STBY, NO MORE MSG", readback.Sample(Screen(status: "cSTBY", info1: "NO MORE MSG"), 500));
        Assert.Null(readback.Sample(Screen(status: "cSTBY", info1: "NO MORE MSG"), 600));
    }

    [Fact]
    public void It_never_says_the_keys_name_and_every_key_reads_the_first_info_line()
    {
        foreach (FenixDcduKey key in Enum.GetValues<FenixDcduKey>())
        {
            FenixChannelDcduReadback readback = Pressed(key, Screen());

            Assert.Equal("NO MORE MSG", readback.Sample(Screen(info1: "NO MORE MSG"), 200));
        }
    }

    // ---- What it watches --------------------------------------------------------------------

    [Fact]
    public void A_soft_key_watches_the_status_and_any_other_key_does_not()
    {
        foreach (FenixDcduKey key in SoftKeys)
        {
            FenixChannelDcduReadback readback = Pressed(key, Screen());
            Assert.Equal("WILCO", readback.Sample(Screen(status: "ciWILCO"), 1200));
        }

        foreach (FenixDcduKey key in OtherKeys)
        {
            // The status changes under a page, message or print key: not theirs to say. The first info line still is.
            FenixChannelDcduReadback readback = Pressed(key, Screen());
            Assert.Null(readback.Sample(Screen(status: "ciWILCO"), 1200));
            Assert.Equal("NO MORE MSG", readback.Sample(Screen(status: "ciWILCO", info1: "NO MORE MSG"), 1300));
        }
    }

    [Fact]
    public void The_keys_labels_and_the_message_lines_are_not_read_back()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());

        // Relabeled keys and new lines: the window shows those, and a pilot reads them from the list.
        FenixDcduScreen other = Screen().Value! with
        {
            Message = ["CLRD TO cEGSSw.".PadRight(24), "cMNTN 4000", "", "", ""],
            Info = ["", "MSG".PadRight(14), "1/2".PadRight(14)],
            LeftKey1 = "c*CANCEL",
            RightKey2 = "cSEND*"
        };
        Assert.Null(readback.Sample(FenixChannelSection<FenixDcduScreen>.Ok(other), 1000));
    }

    [Fact]
    public void A_change_of_spacing_alone_is_no_change()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "cOPEN  ", info1: "NO MORE MSG"));

        Assert.Null(readback.Sample(Screen(status: "g OPEN", info1: "NO MORE   MSG   "), 1000));
    }

    // ---- When it says nothing ---------------------------------------------------------------

    [Fact]
    public void With_no_press_pending_a_change_says_nothing()
    {
        var readback = new FenixChannelDcduReadback();

        Assert.Null(readback.Sample(Screen(), 0));
        Assert.Null(readback.Sample(Screen(status: "ciWILCO", info1: "NO MORE MSG"), 100));
        Assert.Null(readback.Sample(Screen(status: "cSENT", info1: "ANOTHER"), 200));
    }

    [Fact]
    public void The_first_sample_after_the_press_is_the_before_reading_even_when_it_already_shows_the_change()
    {
        // The press lands at least 200 ms after the key is noted, so the first sample is before the effect; what it shows is the
        // state to compare with, not a change.
        var readback = new FenixChannelDcduReadback();
        readback.NotePress(FenixDcduKey.MessageBack);

        Assert.Null(readback.Sample(Screen(info1: "NO MORE MSG"), 0));
        Assert.Null(readback.Sample(Screen(info1: "NO MORE MSG"), 100));
        Assert.Null(readback.Sample(Screen(info1: "NO MORE MSG"), 2000));
    }

    [Fact]
    public void The_first_info_line_going_blank_says_nothing_and_a_later_return_is_said()
    {
        // The line was up from an earlier press when this one was made; it goes blank, which is not read, and when it comes
        // back that is a change worth saying.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.MessageBack, Screen(info1: "NO MORE MSG"));

        Assert.Null(readback.Sample(Screen(info1: ""), 100));
        Assert.Null(readback.Sample(Screen(info1: "          "), 200));
        Assert.Equal("NO MORE MSG", readback.Sample(Screen(info1: "NO MORE MSG"), 300));
    }

    [Fact]
    public void A_status_going_blank_says_nothing_and_the_other_change_still_speaks()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "cOPEN"));

        Assert.Null(readback.Sample(Screen(status: ""), 100));
        Assert.Equal("NO MORE MSG", readback.Sample(Screen(status: "", info1: "NO MORE MSG"), 200));
    }

    [Fact]
    public void A_change_that_goes_blank_in_the_same_sample_as_another_says_only_the_other()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "cOPEN", info1: "NO MORE MSG"));

        Assert.Equal("WILCO", readback.Sample(Screen(status: "ciWILCO", info1: ""), 1200));
    }

    [Fact]
    public void It_gives_up_when_the_window_is_over()
    {
        // The window runs from the before reading and is over at exactly WindowMs: a change a moment before it is said, at it
        // it is not, and a press that gave up stays given up.
        FenixChannelDcduReadback inside = Pressed(FenixDcduKey.RightKey2, Screen());
        Assert.Null(inside.Sample(Screen(), 2999));
        Assert.Equal("WILCO", inside.Sample(Screen(status: "ciWILCO"), 2999));

        FenixChannelDcduReadback over = Pressed(FenixDcduKey.RightKey2, Screen());
        Assert.Null(over.Sample(Screen(status: "ciWILCO"), FenixChannelDcduReadback.WindowMs));
        Assert.Null(over.Sample(Screen(status: "ciWILCO", info1: "NO MORE MSG"), FenixChannelDcduReadback.WindowMs + 100));
        Assert.Null(over.Sample(Screen(status: "cSENT", info1: "ANOTHER"), 10_000));
    }

    [Fact]
    public void The_window_runs_from_the_before_reading_and_not_from_the_note()
    {
        // The note has no clock: the pilot may press at 10,000 ms, and the first sample, at 10,050, is the before reading.
        var readback = new FenixChannelDcduReadback();
        readback.NotePress(FenixDcduKey.MessageBack);

        Assert.Null(readback.Sample(Screen(), 10_050));
        Assert.Equal("NO MORE MSG", readback.Sample(Screen(info1: "NO MORE MSG"), 10_050 + 2900));
    }

    [Fact]
    public void It_gives_up_on_a_screen_that_is_not_ok()
    {
        // A problem sample ends the press at once; the screen that comes back, with a change, says nothing.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());
        Assert.Null(readback.Sample(Problem(), 100));
        Assert.Null(readback.Sample(Screen(status: "ciWILCO", info1: "NO MORE MSG"), 200));
        Assert.Null(readback.Sample(Screen(status: "ciWILCO", info1: "NO MORE MSG"), 300));

        // Every kind of problem, before the before reading as well as after it.
        FenixChannelSection<FenixDcduScreen>[] problems =
        [
            Problem(), FenixChannelSection<FenixDcduScreen>.Missing("DCDU content"),
            FenixChannelSection<FenixDcduScreen>.WrongShape("186.4"),
            FenixChannelSection<FenixDcduScreen>.UnknownCode('h', "DCDU message"), default,
            new(Screen().Value, "display off")
        ];
        foreach (FenixChannelSection<FenixDcduScreen> problem in problems)
        {
            var beforeReading = new FenixChannelDcduReadback();
            beforeReading.NotePress(FenixDcduKey.MessageBack);
            Assert.Null(beforeReading.Sample(problem, 0));
            Assert.Null(beforeReading.Sample(Screen(), 100));
            Assert.Null(beforeReading.Sample(Screen(info1: "NO MORE MSG"), 200));
        }
    }

    // ---- Presses ----------------------------------------------------------------------------

    [Fact]
    public void A_press_is_said_once_and_the_next_press_is_a_new_one()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.MessageBack, Screen());
        Assert.Equal("NO MORE MSG", readback.Sample(Screen(info1: "NO MORE MSG"), 200));

        // Said and cleared: a later change with no press says nothing.
        Assert.Null(readback.Sample(Screen(info1: "OTHER"), 300));

        // The next press takes its own before reading (the line as it stands then) and says its own change.
        readback.NotePress(FenixDcduKey.MessageBack);
        Assert.Null(readback.Sample(Screen(info1: "OTHER"), 6000));
        Assert.Equal("NO MORE MSG", readback.Sample(Screen(info1: "NO MORE MSG"), 6200));
    }

    [Fact]
    public void A_second_press_while_the_line_is_still_up_says_nothing_new()
    {
        // NO MORE MSG is on the first info line for about 5 s: a second press inside that finds it already there, unchanged.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.MessageBack, Screen());
        Assert.Equal("NO MORE MSG", readback.Sample(Screen(info1: "NO MORE MSG"), 200));

        readback.NotePress(FenixDcduKey.MessageBack);
        Assert.Null(readback.Sample(Screen(info1: "NO MORE MSG"), 1500));
        Assert.Null(readback.Sample(Screen(info1: "NO MORE MSG"), 1700));
    }

    [Fact]
    public void A_new_press_replaces_a_pending_one()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());
        Assert.Null(readback.Sample(Screen(), 100));

        // A page key before anything changed: it watches the first info line only, and its window starts at its own before
        // reading (the next sample, 2,500 ms), so the soft key's status change is not its to say and a change at 5,000 ms is
        // inside it though 5,000 ms is far past the first press's window.
        readback.NotePress(FenixDcduKey.PagePlus);
        Assert.Null(readback.Sample(Screen(), 2500));
        Assert.Null(readback.Sample(Screen(status: "ciWILCO"), 3000));
        Assert.Equal("NO MORE MSG", readback.Sample(Screen(status: "ciWILCO", info1: "NO MORE MSG"), 5000));
    }

    [Fact]
    public void A_new_press_replaces_a_pending_one_that_had_changed_nothing_yet_by_a_soft_key()
    {
        // A page key first, then a soft key: the soft key's status is now watched, from its own before reading.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.PagePlus, Screen());
        readback.NotePress(FenixDcduKey.RightKey2);

        Assert.Null(readback.Sample(Screen(), 100));
        Assert.Equal("WILCO", readback.Sample(Screen(status: "ciWILCO"), 1300));
    }

    // ---- Culture ----------------------------------------------------------------------------

    [Fact]
    public void The_read_back_reads_the_same_in_de_DE_and_sv_SE()
    {
        // CI runs under en-US: it writes no figure, only the DCDU's own words.
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            InCulture(culture, () =>
            {
                FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());
                Assert.Equal("WILCO, NO MORE MSG", readback.Sample(Screen(status: "ciWILCO", info1: "NO MORE MSG"), 1200));
            });
        }
    }
}
