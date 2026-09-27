// What the FlyByWire A32NX MCDU window says, as pure logic (PR #253 review).
//
// A closed window keeps reading the screen now, and it must speak FMS scratchpad messages
// ("DEST EFOB BELOW MIN") — the one thing a pilot with the window closed needs — but not
// page changes or "Scratchpad cleared". And over the Coherent transport a frame arrives only
// every 250 ms plus the eval round trip, while FBW's keypad applies each key 150-200 ms after
// it lands, so the read-back holds while a typed entry's keys land or its halves are read back.
//
// The window's timer re-samples the LAST frame on every tick, so these tests play frames by
// how long each one showed, not by ticks: what a pilot hears must not depend on where the
// tick happened to fall.

using MSFSBlindAssist.Forms.FlyByWireA320;

namespace MSFSBlindAssist.Tests;

public class FbwMcduReadBackTests
{
    private static readonly DateTime T0 = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime At(int ms) => T0.AddMilliseconds(ms);

    /// <summary>
    /// Show each scratchpad for its duration, starting at <paramref name="fromMs"/>, and tick the
    /// read-back as the window's timer does — the first tick <paramref name="phaseMs"/> after the
    /// first frame. Returns everything the read-back said.
    /// </summary>
    private static List<string> Play(FbwMcduReadBack readBack, bool windowVisible, int fromMs, int phaseMs,
        params (string Scratchpad, int Ms)[] frames)
    {
        var said = new List<string>();
        int end = frames.Sum(f => f.Ms);
        for (int t = phaseMs; t < end; t += FbwMcduReadBack.TickMs)
        {
            int startsAt = 0;
            string showing = frames[^1].Scratchpad;
            foreach (var frame in frames)
            {
                if (t < startsAt + frame.Ms) { showing = frame.Scratchpad; break; }
                startsAt += frame.Ms;
            }
            var say = readBack.OnScratchpadTick(showing, windowVisible, At(fromMs + t));
            if (say != null) { said.Add(say); }
        }
        return said;
    }

    private static List<string> Play(FbwMcduReadBack readBack, bool windowVisible, params (string Scratchpad, int Ms)[] frames)
        => Play(readBack, windowVisible, fromMs: 0, phaseMs: 0, frames);

    // ------------------------------------------------------------------ page titles

    [Fact]
    public void A_page_title_is_spoken_once_while_the_window_is_open()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Equal("INIT", readBack.OnTitle("INIT", windowVisible: true));
        Assert.Null(readBack.OnTitle(" INIT ", windowVisible: true));
    }

    [Fact]
    public void A_page_change_while_the_window_is_closed_is_not_spoken()
    {
        var readBack = new FbwMcduReadBack();
        readBack.OnTitle("INIT", windowVisible: true);
        Assert.Null(readBack.OnTitle("PERF", windowVisible: false));
    }

    [Fact]
    public void A_page_reached_while_the_window_was_closed_is_spoken_when_it_shows_again()
    {
        var readBack = new FbwMcduReadBack();
        readBack.OnTitle("INIT", windowVisible: true);
        readBack.OnTitle("PERF", windowVisible: false);

        Assert.Equal("PERF", readBack.OnTitle("PERF", windowVisible: true));
    }

    // ------------------------------------------------------------------ scratchpad

    [Fact]
    public void An_FMS_message_that_appears_while_the_window_is_closed_is_spoken()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Equal(new[] { "DEST EFOB BELOW MIN" },
            Play(readBack, windowVisible: false, ("", 500), ("DEST EFOB BELOW MIN", 1500)));
    }

    [Fact]
    public void A_message_clearing_while_the_window_is_closed_is_not_spoken()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Equal(new[] { "CHECK DEST DATA" },
            Play(readBack, windowVisible: false, ("CHECK DEST DATA", 1500), ("", 1500)));

        // ...and the next message is still spoken.
        Assert.Equal(new[] { "SET HOLD SPEED" },
            Play(readBack, windowVisible: false, fromMs: 3000, phaseMs: 0, ("SET HOLD SPEED", 1500)));
    }

    [Fact]
    public void A_scratchpad_clearing_while_the_window_is_open_says_scratchpad_cleared()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Equal(new[] { "KJFK", FbwMcduReadBack.ClearedText },
            Play(readBack, windowVisible: true, ("KJFK", 1500), ("", 1500)));
    }

    [Fact]
    public void A_message_already_showing_when_the_window_first_opens_is_read()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Equal(new[] { "CHECK DEST DATA" },
            Play(readBack, windowVisible: true, ("CHECK DEST DATA", 1500)));
    }

    [Fact]
    public void A_one_tick_flicker_is_not_spoken()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Empty(Play(readBack, windowVisible: true,
            ("", 1500), ("NOT ALLOWED", FbwMcduReadBack.TickMs), ("", 1500)));
    }

    /// <summary>One Coherent read with the window open: the 250 ms poll plus an eval round trip of
    /// 20, 50, 100 or 150 ms — the last is the documented limit. A value the window saw in one read
    /// shows in the window for that long.</summary>
    public static TheoryData<int> OneReadMs => new() { 270, 300, 350, 400 };

    [Fact]
    public void The_stability_window_is_never_shorter_than_StableMs()
    {
        // The count is of ticks, so it must round UP: with a tick that does not divide StableMs,
        // rounding down would speak a value that showed for less than StableMs.
        Assert.True((FbwMcduReadBack.StablePolls - 1) * FbwMcduReadBack.TickMs >= FbwMcduReadBack.StableMs);
    }

    [Theory]
    [MemberData(nameof(OneReadMs))]
    public void A_blank_seen_for_one_read_between_the_same_message_is_not_spoken(int oneReadMs)
    {
        // A redraw caught mid-way: whatever the tick's phase, the pilot hears the message once.
        for (int phase = 0; phase < FbwMcduReadBack.TickMs; phase += 10)
        {
            var readBack = new FbwMcduReadBack();
            Assert.Equal(new[] { "NOT ALLOWED" },
                Play(readBack, windowVisible: true, fromMs: 0, phaseMs: phase,
                    ("NOT ALLOWED", 1500), ("", oneReadMs), ("NOT ALLOWED", 1500)));
        }
    }

    [Theory]
    [MemberData(nameof(OneReadMs))]
    public void The_next_queued_message_follows_a_one_read_blank_without_scratchpad_cleared(int oneReadMs)
    {
        // FBW blanks the scratchpad for 150 ms before showing the next queued message; a read that
        // lands in that gap must not put "Scratchpad cleared" between the two messages.
        for (int phase = 0; phase < FbwMcduReadBack.TickMs; phase += 10)
        {
            var readBack = new FbwMcduReadBack();
            Assert.Equal(new[] { "CHECK DEST DATA", "SET HOLD SPEED" },
                Play(readBack, windowVisible: true, fromMs: 0, phaseMs: phase,
                    ("CHECK DEST DATA", 1500), ("", oneReadMs), ("SET HOLD SPEED", 1500)));
        }
    }

    [Fact]
    public void A_later_hold_extends_an_earlier_one()
    {
        // The window holds before each key is sent AND again once it has been delivered: a key
        // can wait out the relay settle before it goes, and the entry before it keeps showing
        // until the key lands and is read. The second hold must extend the first.
        var readBack = new FbwMcduReadBack();
        readBack.HoldForTyping(At(0));
        readBack.HoldForTyping(At(300));

        Assert.Equal(new[] { "KJFK/EGLL" },
            Play(readBack, windowVisible: true, ("KJFK/EG", 1150), ("KJFK/EGLL", 2000)));
    }

    [Fact]
    public void A_half_typed_entry_is_never_read_back_and_the_settled_one_is_read_once()
    {
        // "KJFK/EGLL" typed one key every 70 ms (the typing loop's 50 ms plus the eval); the
        // window's ticks see the scratchpad fill in behind the keypad's 150-200 ms delay.
        var readBack = new FbwMcduReadBack();
        var said = new List<string>();
        string[] shown = { "K", "KJF", "KJFK/", "KJFK/EG", "KJFK/EGLL" };
        int lastKeyMs = 0;
        for (int key = 0; key < 9; key++)
        {
            lastKeyMs = key * 70;
            readBack.HoldForTyping(At(lastKeyMs));
            if (key % 2 == 1)
            {
                // A tick between keys, seeing a partial entry.
                var say = readBack.OnScratchpadTick(shown[key / 2], windowVisible: true, At(lastKeyMs + 10));
                if (say != null) { said.Add(say); }
            }
        }

        said.AddRange(Play(readBack, windowVisible: true, fromMs: lastKeyMs + 10, phaseMs: 0, ("KJFK/EGLL", 2000)));

        Assert.Equal(new[] { "KJFK/EGLL" }, said);
    }
}
