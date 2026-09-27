// What the FlyByWire A32NX MCDU window says, as pure logic (PR #253 review).
//
// A closed window keeps reading the screen now, and it must speak FMS scratchpad messages
// ("DEST EFOB BELOW MIN") — the one thing a pilot with the window closed needs — but not
// page changes or "Scratchpad cleared". And over the Coherent transport a frame arrives only
// every 250 ms plus the eval round trip, while FBW's keypad applies each key 150-200 ms after
// it lands, so a typed entry must be held until it has landed or its halves are read back.

using MSFSBlindAssist.Forms.FlyByWireA320;

namespace MSFSBlindAssist.Tests;

public class FbwMcduReadBackTests
{
    private static readonly DateTime T0 = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime At(int ms) => T0.AddMilliseconds(ms);

    /// <summary>Feed one scratchpad value per window tick, starting at <paramref name="fromMs"/>;
    /// returns everything the read-back said.</summary>
    private static List<string> Ticks(FbwMcduReadBack readBack, bool windowVisible, int fromMs, params string[] scratchpadPerTick)
    {
        var said = new List<string>();
        for (int i = 0; i < scratchpadPerTick.Length; i++)
        {
            var say = readBack.OnScratchpadTick(scratchpadPerTick[i], windowVisible, At(fromMs + i * FbwMcduReadBack.TickMs));
            if (say != null) { said.Add(say); }
        }
        return said;
    }

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
            Ticks(readBack, windowVisible: false, 0, "", "DEST EFOB BELOW MIN", "DEST EFOB BELOW MIN"));
    }

    [Fact]
    public void A_message_clearing_while_the_window_is_closed_is_not_spoken()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Equal(new[] { "CHECK DEST DATA" },
            Ticks(readBack, windowVisible: false, 0, "CHECK DEST DATA", "CHECK DEST DATA", "", "", ""));

        // ...and the next message is still spoken.
        Assert.Equal(new[] { "SET HOLD SPEED" },
            Ticks(readBack, windowVisible: false, 1000, "SET HOLD SPEED", "SET HOLD SPEED"));
    }

    [Fact]
    public void A_scratchpad_clearing_while_the_window_is_open_says_scratchpad_cleared()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Equal(new[] { "KJFK", FbwMcduReadBack.ClearedText },
            Ticks(readBack, windowVisible: true, 0, "KJFK", "KJFK", "", ""));
    }

    [Fact]
    public void A_message_already_showing_when_the_window_first_opens_is_read()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Equal(new[] { "CHECK DEST DATA" },
            Ticks(readBack, windowVisible: true, 0, "CHECK DEST DATA", "CHECK DEST DATA"));
    }

    [Fact]
    public void A_one_tick_flicker_is_not_spoken()
    {
        var readBack = new FbwMcduReadBack();
        Assert.Empty(Ticks(readBack, windowVisible: true, 0, "", "NOT ALLOWED", "", "NOT ALLOWED", ""));
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

        said.AddRange(Ticks(readBack, windowVisible: true, lastKeyMs + 10,
            Enumerable.Repeat("KJFK/EGLL", 8).ToArray()));

        Assert.Equal(new[] { "KJFK/EGLL" }, said);
    }
}
