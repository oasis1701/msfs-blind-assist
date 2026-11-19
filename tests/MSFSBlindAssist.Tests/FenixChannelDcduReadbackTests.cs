// Characterization tests for FenixChannelDcduReadback, which gives the pilot what the DCDU changed away from the row under the
// cursor: every new first info line, and the status after the pilot's own soft key. Pure logic: the screens are constructed
// here, and the read-back is driven by hand with its own clock (a plain number of milliseconds).
//
// The contract ([CORE-7]: what the screen reader cannot say). The first info line: any change to a line with words is said once
// as it appears, whatever caused it (SENDING, then RECEIVD BY ATC, then nothing), through the "DCDU messages" mute, except inside
// the window of 3 s after the pilot's own press, so that a key is never silent. A line that goes blank says nothing and the same
// line coming back after a blank is said again. The first ok reading after the start, a gap or a screen that is not ok is a
// silent baseline: a line already showing is not said. A muted line is tracked, never replayed on unmute. The status (the
// header's right half): after a soft key, its first change to something with words, once, inside the window, whatever the
// mute. NotePress takes no clock; the first sample after it is the "before" reading, and a press lands at least 200 ms later,
// so that sample is always before its effect. Both in one sample are said together, the status first, joined by ", ". A change
// of spacing alone is no change, and the key's name is never said.
//
// Measured on 3.0.0.7613 (2026-11-03): after a message key with nothing to step to the first info line reads "NO MORE MSG" for
// about 5 s; after one press of WILCO the status reads "ciWILCO" about 1.2 s later; SEND, pressed at 0 on a message whose keys
// were CANCEL and SEND, draws SENDING on the first info line at 1.2 s and RECEIVD BY ATC at 3.3 s, blank at 8.3 s, the status
// word staying WILCO; CLOSE with another message behind it draws that message with the status OPEN at 1.2 s.

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

    // One sample, on the DCDU that is not muted unless the test says so.
    private static string? At(FenixChannelDcduReadback readback, FenixChannelSection<FenixDcduScreen> screen, long nowMs,
        bool muted = false)
        => readback.Sample(screen, muted, nowMs);

    // A read-back with a press noted and its before reading taken at 0 ms, on the given screen.
    private static FenixChannelDcduReadback Pressed(FenixDcduKey key, FenixChannelSection<FenixDcduScreen> before,
        bool muted = false)
    {
        var readback = new FenixChannelDcduReadback();
        readback.NotePress(key);
        Assert.Null(At(readback, before, 0, muted));
        return readback;
    }

    // A read-back past its baseline on the given screen: sampled at 0 ms, with no press noted.
    private static FenixChannelDcduReadback Baselined(FenixChannelSection<FenixDcduScreen> screen, bool muted = false)
    {
        var readback = new FenixChannelDcduReadback();
        Assert.Null(At(readback, screen, 0, muted));
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

    // SEND, pressed at 0 on an open answer, as the Fenix drew it: the key flickers at 0.25 s and 0.7 s, the status is WILCO again
    // at 1.2 s (a new color) with SENDING on the first info line and CLOSE the only key, RECEIVD BY ATC from 3.3 s, blank from 8.3 s.
    private static void SendTimeline(Action<FenixChannelSection<FenixDcduScreen>, long, string?> sample)
    {
        sample(Screen(status: "ciWILCO", rightKey2: "ciSEND*"), 250, null);
        sample(Screen(status: "ciWILCO", rightKey2: "cSEND*"), 700, null);
        sample(Screen(status: "giWILCO", info1: "SENDING", rightKey2: "cCLOSE*"), 1200, "SENDING");
        sample(Screen(status: "giWILCO", info1: "SENDING", rightKey2: "cCLOSE*"), 1300, null);
        sample(Screen(status: "giWILCO", info1: "SENDING", rightKey2: "cCLOSE*"), 3200, null);
        sample(Screen(status: "giWILCO", info1: "RECEIVD BY ATC", rightKey2: "cCLOSE*"), 3300, "RECEIVD BY ATC");
        sample(Screen(status: "giWILCO", info1: "RECEIVD BY ATC", rightKey2: "cCLOSE*"), 3400, null);
        sample(Screen(status: "giWILCO", info1: "RECEIVD BY ATC", rightKey2: "cCLOSE*"), 8200, null);
        sample(Screen(status: "giWILCO", info1: "", rightKey2: "cCLOSE*"), 8300, null);
        sample(Screen(status: "giWILCO", info1: "", rightKey2: "cCLOSE*"), 8400, null);
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
        Assert.Null(At(readback, Screen(), 100));
        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 200));
        Assert.Null(At(readback, Screen(info1: "NO MORE MSG"), 300));
        Assert.Null(At(readback, Screen(info1: "NO MORE MSG"), 4000));
    }

    [Fact]
    public void After_wilco_it_says_wilco_about_a_second_after_the_press()
    {
        // The press is noted, the before reading is the first sample (status OPEN); the key's own label flickers at 0.3 s and
        // the status becomes WILCO at 1.2 s, with the keys relabeled: only the status is said.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());

        Assert.Null(At(readback, Screen(rightKey2: "ciWILCO*"), 300));
        Assert.Null(At(readback, Screen(rightKey2: "cWILCO*"), 800));
        Assert.Null(At(readback, Screen(rightKey2: "cWILCO*"), 1100));
        Assert.Equal("WILCO", At(readback, Screen(status: "ciWILCO", rightKey2: "cSEND*"), 1200));
        Assert.Null(At(readback, Screen(status: "ciWILCO", rightKey2: "cSEND*"), 1300));
    }

    [Fact]
    public void Send_says_sending_and_then_received_by_atc_each_once()
    {
        // The status word did not change (WILCO before and after), so only the first info line speaks, and both of its texts do.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "ciWILCO", rightKey2: "cSEND*"));

        SendTimeline((screen, ms, expected) => Assert.Equal(expected, At(readback, screen, ms)));
    }

    [Fact]
    public void Close_with_a_message_behind_it_says_open()
    {
        // CLOSE on an answered message with another behind it: 1.2 s later the other message is on the screen with the status
        // OPEN, and with one message left the counter label goes while the page counter keeps its column. The owner: OPEN is right.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "giWILCO", rightKey2: "cCLOSE*"));
        FenixDcduScreen other = Screen(status: "cOPEN").Value! with { Info = ["", "           PGE", "           1/2"] };

        Assert.Null(At(readback, Screen(status: "giWILCO", rightKey2: "cCLOSE*"), 700));
        Assert.Equal("OPEN", At(readback, FenixChannelSection<FenixDcduScreen>.Ok(other), 1200));
        Assert.Null(At(readback, FenixChannelSection<FenixDcduScreen>.Ok(other), 1300));
    }

    [Fact]
    public void A_change_is_said_in_plain_words()
    {
        // No codes, no color names, runs of spaces collapsed, the ends trimmed.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.PagePlus, Screen());

        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "g NO MORE   MSG    "), 100));
    }

    [Fact]
    public void Both_changes_in_one_sample_are_said_together_status_first()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey1, Screen());

        Assert.Equal("STBY, NO MORE MSG", At(readback, Screen(status: "cSTBY", info1: "NO MORE MSG"), 500));
        Assert.Null(At(readback, Screen(status: "cSTBY", info1: "NO MORE MSG"), 600));
    }

    [Fact]
    public void It_never_says_the_keys_name_and_every_key_reads_the_first_info_line()
    {
        foreach (FenixDcduKey key in Enum.GetValues<FenixDcduKey>())
        {
            FenixChannelDcduReadback readback = Pressed(key, Screen());

            Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 200));
        }
    }

    // ---- What it watches --------------------------------------------------------------------

    [Fact]
    public void A_soft_key_watches_the_status_and_any_other_key_does_not()
    {
        foreach (FenixDcduKey key in SoftKeys)
        {
            FenixChannelDcduReadback readback = Pressed(key, Screen());
            Assert.Equal("WILCO", At(readback, Screen(status: "ciWILCO"), 1200));
        }

        foreach (FenixDcduKey key in OtherKeys)
        {
            // The status changes under a page, message or print key: not theirs to say. The first info line still is.
            FenixChannelDcduReadback readback = Pressed(key, Screen());
            Assert.Null(At(readback, Screen(status: "ciWILCO"), 1200));
            Assert.Equal("NO MORE MSG", At(readback, Screen(status: "ciWILCO", info1: "NO MORE MSG"), 1300));
        }
    }

    [Fact]
    public void The_status_is_said_once_even_after_an_info_line_was()
    {
        // The status is the key's result whatever else changed first: an info line said does not end the watch.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());

        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 500));
        Assert.Equal("WILCO", At(readback, Screen(status: "ciWILCO", info1: "NO MORE MSG"), 1200));
        Assert.Null(At(readback, Screen(status: "cSENT", info1: "NO MORE MSG"), 1300));
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
        Assert.Null(At(readback, FenixChannelSection<FenixDcduScreen>.Ok(other), 1000));
    }

    [Fact]
    public void A_change_of_spacing_alone_is_no_change()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "cOPEN  ", info1: "NO MORE MSG"));

        Assert.Null(At(readback, Screen(status: "g OPEN", info1: "NO MORE   MSG   "), 1000));
    }

    // ---- The first info line, whatever caused it --------------------------------------------

    [Fact]
    public void A_new_first_info_line_with_no_press_is_said_but_the_status_is_not()
    {
        var readback = new FenixChannelDcduReadback();

        Assert.Null(At(readback, Screen(), 0));
        Assert.Equal("NO MORE MSG", At(readback, Screen(status: "ciWILCO", info1: "NO MORE MSG"), 100));
        Assert.Equal("ANOTHER", At(readback, Screen(status: "cSENT", info1: "ANOTHER"), 200));
        Assert.Null(At(readback, Screen(status: "cSENT", info1: "ANOTHER"), 300));
    }

    [Fact]
    public void A_line_changing_to_another_line_is_said_at_once()
    {
        FenixChannelDcduReadback readback = Baselined(Screen());

        Assert.Equal("SENDING", At(readback, Screen(info1: "SENDING"), 100));
        Assert.Equal("RECEIVD BY ATC", At(readback, Screen(info1: "RECEIVD BY ATC"), 200));
    }

    [Fact]
    public void A_line_going_blank_says_nothing_and_the_same_line_coming_back_is_said_again()
    {
        FenixChannelDcduReadback readback = Baselined(Screen());

        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 100));
        Assert.Null(At(readback, Screen(info1: ""), 200));
        Assert.Null(At(readback, Screen(info1: "          "), 300));
        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 400));
        Assert.Null(At(readback, Screen(info1: "NO MORE MSG"), 500));
    }

    [Fact]
    public void The_first_reading_is_a_silent_baseline_even_with_a_line_showing()
    {
        // A line already up when the app starts or the aircraft loads is not news.
        var readback = new FenixChannelDcduReadback();

        Assert.Null(At(readback, Screen(info1: "SENDING"), 0));
        Assert.Null(At(readback, Screen(info1: "SENDING"), 100));
        Assert.Null(At(readback, Screen(info1: "SENDING"), 5000));

        // And what changes after it is news.
        Assert.Equal("RECEIVD BY ATC", At(readback, Screen(info1: "RECEIVD BY ATC"), 5100));
    }

    [Fact]
    public void A_screen_that_is_not_ok_ends_the_baseline_and_the_next_ok_screen_is_a_new_one()
    {
        // Every kind of problem: a display-off gap, a missing section, a wrong shape, an unknown code, an unset section.
        FenixChannelSection<FenixDcduScreen>[] problems =
        [
            Problem(), FenixChannelSection<FenixDcduScreen>.Missing("DCDU content"),
            FenixChannelSection<FenixDcduScreen>.WrongShape("186.4"),
            FenixChannelSection<FenixDcduScreen>.UnknownCode('h', "DCDU message"), default,
            new(Screen().Value, "display off")
        ];

        foreach (FenixChannelSection<FenixDcduScreen> problem in problems)
        {
            FenixChannelDcduReadback readback = Baselined(Screen());
            Assert.Equal("SENDING", At(readback, Screen(info1: "SENDING"), 100));

            // A gap, after which another line is already up: it is the baseline, not news.
            Assert.Null(At(readback, problem, 200));
            Assert.Null(At(readback, Screen(info1: "RECEIVD BY ATC"), 300));
            Assert.Null(At(readback, Screen(info1: "RECEIVD BY ATC"), 400));

            // The same line as before the gap is a baseline too; a later change is news.
            Assert.Null(At(readback, problem, 500));
            Assert.Null(At(readback, Screen(info1: "RECEIVD BY ATC"), 600));
            Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 700));
        }
    }

    // ---- The mute ---------------------------------------------------------------------------

    [Fact]
    public void A_muted_background_line_is_not_said_and_is_not_replayed_on_unmute()
    {
        FenixChannelDcduReadback readback = Baselined(Screen());

        // Shown while muted: tracked, not said.
        Assert.Null(At(readback, Screen(info1: "SENDING"), 100, muted: true));
        Assert.Null(At(readback, Screen(info1: "RECEIVD BY ATC"), 200, muted: true));

        // Unmuted with the line still up: it is not a change, so it is not said now either.
        Assert.Null(At(readback, Screen(info1: "RECEIVD BY ATC"), 300));
        Assert.Null(At(readback, Screen(info1: "RECEIVD BY ATC"), 5000));

        // A line after that is heard as usual.
        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 5100));
    }

    [Fact]
    public void A_muted_line_coming_back_after_a_blank_is_not_said_either_until_unmuted()
    {
        FenixChannelDcduReadback readback = Baselined(Screen());

        Assert.Null(At(readback, Screen(info1: "SENDING"), 100, muted: true));
        Assert.Null(At(readback, Screen(info1: ""), 200, muted: true));
        Assert.Null(At(readback, Screen(info1: "SENDING"), 300, muted: true));
        Assert.Null(At(readback, Screen(info1: ""), 400));
        Assert.Equal("SENDING", At(readback, Screen(info1: "SENDING"), 500));
    }

    [Fact]
    public void A_muted_line_inside_the_window_after_the_pilots_press_is_said()
    {
        // The pilot pressed SEND with the DCDU muted: a key is never silent, so SENDING is heard. RECEIVD BY ATC comes at 3.3 s,
        // after the window of 3 s since the before reading, and is a background line again: muted, not said.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "ciWILCO", rightKey2: "cSEND*"), muted: true);

        SendTimeline((screen, ms, expected) =>
            Assert.Equal(expected == "RECEIVD BY ATC" ? null : expected, At(readback, screen, ms, muted: true)));
    }

    [Fact]
    public void The_window_that_lets_a_muted_line_through_is_over_at_exactly_three_seconds()
    {
        FenixChannelDcduReadback inside = Pressed(FenixDcduKey.MessageBack, Screen());
        Assert.Equal("NO MORE MSG", At(inside, Screen(info1: "NO MORE MSG"), FenixChannelDcduReadback.WindowMs - 1, muted: true));

        FenixChannelDcduReadback over = Pressed(FenixDcduKey.MessageBack, Screen());
        Assert.Null(At(over, Screen(info1: "NO MORE MSG"), FenixChannelDcduReadback.WindowMs, muted: true));
        Assert.Null(At(over, Screen(info1: "NO MORE MSG"), FenixChannelDcduReadback.WindowMs + 100));
    }

    [Fact]
    public void A_muted_line_inside_the_window_is_said_after_a_second_press_too()
    {
        // The window is the last press's: a press at 5,000 ms (its before reading at 5,050) lets a muted line through until 8,050.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.MessageBack, Screen(), muted: true);
        Assert.Null(At(readback, Screen(), 4000, muted: true));

        readback.NotePress(FenixDcduKey.MessageBack);
        Assert.Null(At(readback, Screen(), 5050, muted: true));
        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 8049, muted: true));
    }

    [Fact]
    public void The_status_is_the_pilots_press_and_the_mute_leaves_it_alone()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(), muted: true);

        Assert.Equal("WILCO", At(readback, Screen(status: "ciWILCO"), 1200, muted: true));
    }

    // ---- When it says nothing ---------------------------------------------------------------

    [Fact]
    public void With_no_press_pending_a_status_change_says_nothing()
    {
        var readback = new FenixChannelDcduReadback();

        Assert.Null(At(readback, Screen(), 0));
        Assert.Null(At(readback, Screen(status: "ciWILCO"), 100));
        Assert.Null(At(readback, Screen(status: "cSENT"), 200));
    }

    [Fact]
    public void The_first_sample_after_the_press_is_the_before_reading_even_when_it_already_shows_the_change()
    {
        // The press lands at least 200 ms after the key is noted, so the first sample is before the effect; what it shows is the
        // state to compare with, not a change.
        var readback = new FenixChannelDcduReadback();
        readback.NotePress(FenixDcduKey.MessageBack);

        Assert.Null(At(readback, Screen(info1: "NO MORE MSG"), 0));
        Assert.Null(At(readback, Screen(info1: "NO MORE MSG"), 100));
        Assert.Null(At(readback, Screen(info1: "NO MORE MSG"), 2000));
    }

    [Fact]
    public void The_first_info_line_going_blank_says_nothing_and_a_later_return_is_said()
    {
        // The line was up from an earlier press when this one was made; it goes blank, which is not read, and when it comes
        // back that is a change worth saying.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.MessageBack, Screen(info1: "NO MORE MSG"));

        Assert.Null(At(readback, Screen(info1: ""), 100));
        Assert.Null(At(readback, Screen(info1: "          "), 200));
        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 300));
    }

    [Fact]
    public void A_status_going_blank_says_nothing_and_the_other_change_still_speaks()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "cOPEN"));

        Assert.Null(At(readback, Screen(status: ""), 100));
        Assert.Equal("NO MORE MSG", At(readback, Screen(status: "", info1: "NO MORE MSG"), 200));
    }

    [Fact]
    public void A_change_that_goes_blank_in_the_same_sample_as_another_says_only_the_other()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen(status: "cOPEN", info1: "NO MORE MSG"));

        Assert.Equal("WILCO", At(readback, Screen(status: "ciWILCO", info1: ""), 1200));
    }

    [Fact]
    public void The_status_watch_is_given_up_when_the_window_is_over()
    {
        // The window runs from the before reading and is over at exactly WindowMs: a status change a moment before it is said,
        // at it it is not, and a watch that gave up stays given up. The first info line is no part of the press: it is said.
        FenixChannelDcduReadback inside = Pressed(FenixDcduKey.RightKey2, Screen());
        Assert.Null(At(inside, Screen(), 2999));
        Assert.Equal("WILCO", At(inside, Screen(status: "ciWILCO"), 2999));

        FenixChannelDcduReadback over = Pressed(FenixDcduKey.RightKey2, Screen());
        Assert.Null(At(over, Screen(status: "ciWILCO"), FenixChannelDcduReadback.WindowMs));
        Assert.Equal("NO MORE MSG",
            At(over, Screen(status: "ciWILCO", info1: "NO MORE MSG"), FenixChannelDcduReadback.WindowMs + 100));
        Assert.Null(At(over, Screen(status: "cSENT", info1: "NO MORE MSG"), 10_000));
        Assert.Equal("ANOTHER", At(over, Screen(status: "cSENT", info1: "ANOTHER"), 10_100));
    }

    [Fact]
    public void The_window_runs_from_the_before_reading_and_not_from_the_note()
    {
        // The note has no clock: the pilot may press at 10,000 ms, and the first sample, at 10,050, is the before reading.
        var readback = new FenixChannelDcduReadback();
        readback.NotePress(FenixDcduKey.RightKey2);

        Assert.Null(At(readback, Screen(), 10_050));
        Assert.Equal("WILCO", At(readback, Screen(status: "ciWILCO"), 10_050 + 2900));
    }

    [Fact]
    public void A_screen_that_is_not_ok_gives_up_the_press()
    {
        // A problem sample ends the press at once; the screen that comes back is a new baseline, and the status it shows is
        // not the press's result.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());
        Assert.Null(At(readback, Problem(), 100));
        Assert.Null(At(readback, Screen(status: "ciWILCO", info1: "NO MORE MSG"), 200));
        Assert.Null(At(readback, Screen(status: "ciWILCO", info1: "NO MORE MSG"), 300));

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
            beforeReading.NotePress(FenixDcduKey.RightKey2);
            Assert.Null(At(beforeReading, problem, 0));
            Assert.Null(At(beforeReading, Screen(), 100));
            Assert.Null(At(beforeReading, Screen(status: "ciWILCO"), 200));

            // The line is a background change, not the press's.
            Assert.Equal("NO MORE MSG", At(beforeReading, Screen(status: "ciWILCO", info1: "NO MORE MSG"), 300));
        }
    }

    // ---- Presses ----------------------------------------------------------------------------

    [Fact]
    public void A_press_is_said_once_and_the_next_press_is_a_new_one()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.MessageBack, Screen());
        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 200));

        // Said and cleared: a later change with no press is a background line, said as one.
        Assert.Equal("OTHER", At(readback, Screen(info1: "OTHER"), 300));

        // The next press takes its own before reading (the line as it stands then) and says its own change.
        readback.NotePress(FenixDcduKey.MessageBack);
        Assert.Null(At(readback, Screen(info1: "OTHER"), 6000));
        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 6200));
    }

    [Fact]
    public void A_second_press_while_the_line_is_still_up_says_nothing_new()
    {
        // NO MORE MSG is on the first info line for about 5 s: a second press inside that finds it already there, unchanged.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.MessageBack, Screen());
        Assert.Equal("NO MORE MSG", At(readback, Screen(info1: "NO MORE MSG"), 200));

        readback.NotePress(FenixDcduKey.MessageBack);
        Assert.Null(At(readback, Screen(info1: "NO MORE MSG"), 1500));
        Assert.Null(At(readback, Screen(info1: "NO MORE MSG"), 1700));
    }

    [Fact]
    public void A_new_press_replaces_a_pending_one()
    {
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.RightKey2, Screen());
        Assert.Null(At(readback, Screen(), 100));

        // A page key before anything changed: it watches the first info line only, and its window starts at its own before
        // reading (the next sample, 2,500 ms), so the soft key's status change is not its to say and a change at 5,000 ms is
        // inside it though 5,000 ms is far past the first press's window.
        readback.NotePress(FenixDcduKey.PagePlus);
        Assert.Null(At(readback, Screen(), 2500));
        Assert.Null(At(readback, Screen(status: "ciWILCO"), 3000));
        Assert.Equal("NO MORE MSG", At(readback, Screen(status: "ciWILCO", info1: "NO MORE MSG"), 5000));
    }

    [Fact]
    public void A_new_press_replaces_a_pending_one_that_had_changed_nothing_yet_by_a_soft_key()
    {
        // A page key first, then a soft key: the soft key's status is now watched, from its own before reading.
        FenixChannelDcduReadback readback = Pressed(FenixDcduKey.PagePlus, Screen());
        readback.NotePress(FenixDcduKey.RightKey2);

        Assert.Null(At(readback, Screen(), 100));
        Assert.Equal("WILCO", At(readback, Screen(status: "ciWILCO"), 1300));
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
                Assert.Equal("WILCO, NO MORE MSG", At(readback, Screen(status: "ciWILCO", info1: "NO MORE MSG"), 1200));

                FenixChannelDcduReadback send = Pressed(FenixDcduKey.RightKey2, Screen(status: "ciWILCO", rightKey2: "cSEND*"));
                SendTimeline((screen, ms, expected) => Assert.Equal(expected, At(send, screen, ms)));

                FenixChannelDcduReadback background = Baselined(Screen());
                Assert.Equal("SENDING", At(background, Screen(info1: "SENDING"), 100));
                Assert.Null(At(background, Screen(info1: "RECEIVD BY ATC"), 200, muted: true));
            });
        }
    }
}
