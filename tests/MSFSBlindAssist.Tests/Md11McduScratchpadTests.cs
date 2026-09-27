using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11 MCDU window's scratchpad read-back and whole-scratchpad clear (review round 2, C2).
///
/// The window announced the scratchpad through a 300 ms debounce, and its Delete clear spoke
/// "Scratchpad cleared" itself. The clear never updated the debounce's baseline, so the debounce
/// then said "Scratchpad cleared" a second time; the clear said it even after its presses had run
/// out with text still on the pad; and a typed entry longer than the debounce leaked its
/// half-typed text. The window now feeds the shared CduScratchpadAnnouncer on every poll tick and
/// holds it for each key burst — these pin the pure half of that.
/// </summary>
public class Md11McduScratchpadTests
{
    private static readonly DateTime T0 = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    // ------------------------------------------------------------ hold arithmetic

    [Fact]
    public void A_burst_is_held_for_every_DOWN_and_UP_the_bus_writes_plus_the_settle_margin()
    {
        Assert.Equal(Md11McduScratchpad.SettleMarginMs, Md11McduScratchpad.SuppressMs(0));
        Assert.Equal(4 * 2 * Md11EventBus.MinGapMs + Md11McduScratchpad.SettleMarginMs,
            Md11McduScratchpad.SuppressMs(4));
    }

    [Fact]
    public void The_hold_outlasts_the_last_write_by_more_than_one_poll_tick()
    {
        // n keys are 2n ids written at t = 0, 60, …, (2n-1)·60 ms; the read-back must not speak
        // before the last one has been written, delivered and seen by the window's 250 ms poll.
        for (var n = 1; n <= 24; n++)
            Assert.True(Md11McduScratchpad.SuppressMs(n) - (2 * n - 1) * Md11EventBus.MinGapMs > 250, $"{n} keys");
    }

    [Fact]
    public void With_no_hold_running_the_hold_starts_now()
    {
        var expected = T0.AddMilliseconds(Md11McduScratchpad.SuppressMs(3));
        Assert.Equal(expected, Md11McduScratchpad.HoldUntil(default, T0, 3));
        Assert.Equal(expected, Md11McduScratchpad.HoldUntil(T0.AddSeconds(-5), T0, 3));   // an expired hold is no hold
    }

    [Fact]
    public void A_key_pressed_during_a_long_burst_extends_the_hold_and_never_shortens_it()
    {
        // A 24-key entry, then a Backspace 100 ms later: the Backspace queues BEHIND the entry,
        // so it must not end the entry's hold early and let its half-typed text be read.
        var entry = Md11McduScratchpad.HoldUntil(default, T0, 24);
        var backspace = Md11McduScratchpad.HoldUntil(entry, T0.AddMilliseconds(100), 1);

        Assert.Equal(entry.AddMilliseconds(2 * Md11EventBus.MinGapMs), backspace);
        Assert.True(backspace > entry);
    }

    // ------------------------------------------------------------ the clear's verdict

    [Fact]
    public void A_clear_that_emptied_the_pad_says_nothing_itself() =>
        Assert.Null(Md11McduScratchpad.ClearVerdict(readable: true, empty: true, presses: 4));

    [Fact]
    public void A_clear_that_ran_out_of_presses_says_so() =>
        Assert.Equal("Could not clear the scratchpad",
            Md11McduScratchpad.ClearVerdict(readable: true, empty: false, presses: Md11McduScratchpad.MaxClearPresses));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_clear_that_could_not_read_the_screen_never_claims_an_empty_pad(int presses) =>
        Assert.Equal(Md11McduScratchpad.CouldNotClearText,
            Md11McduScratchpad.ClearVerdict(readable: false, empty: false, presses));

    [Fact]
    public void A_clear_with_nothing_to_clear_says_the_pad_is_already_empty() =>
        Assert.Equal("Scratchpad already empty",
            Md11McduScratchpad.ClearVerdict(readable: true, empty: true, presses: 0));

    [Fact]
    public void The_clear_is_capped_at_the_scratchpad_width_plus_a_margin() =>
        Assert.Equal(Md11McduLayout.Cols + 4, Md11McduScratchpad.MaxClearPresses);

    // ------------------------------------------------------------ the read-back the window runs
    //
    // Built exactly as the window builds it — Md11McduScratchpad.CreateReadBack(): the burst hold
    // AND two stable polls (review round 2, C13).

    [Fact]
    public void A_typed_entry_is_read_once_settled_and_none_of_its_intermediate_text_is()
    {
        var readBack = Md11McduScratchpad.CreateReadBack();
        readBack.OnPoll("", T0);                                               // the window's silent seed
        readBack.SuppressUntil = Md11McduScratchpad.HoldUntil(readBack.SuppressUntil, T0, 4);

        var said = new List<string>();
        void Poll(string pad, int ms)
        {
            if (readBack.OnPoll(pad, T0.AddMilliseconds(ms)) is { } s) said.Add(s);
        }
        Poll("K", 250); Poll("KJ", 500); Poll("KJF", 750);                     // inside the 880 ms hold
        Poll("KJFK", 1000);                                                    // the hold has ended: one stable poll
        Poll("KJFK", 1250);                                                    // the second: read here
        Poll("KJFK", 1500);                                                    // and never again

        Assert.Equal(new[] { "KJFK" }, said);
    }

    [Fact]
    public void A_clear_burst_ends_in_one_Scratchpad_cleared()
    {
        var readBack = Md11McduScratchpad.CreateReadBack();
        readBack.OnPoll("KJFK", T0);

        // One CLR every 150 ms, each extending the hold — the clear loop's own rhythm.
        var said = new List<string>();
        var pads = new[] { "KJF", "KJ", "K", "" };
        for (var i = 0; i < pads.Length; i++)
        {
            var now = T0.AddMilliseconds(150 * i);
            readBack.SuppressUntil = Md11McduScratchpad.HoldUntil(readBack.SuppressUntil, now, 1);
            if (readBack.OnPoll(pads[i], now.AddMilliseconds(100)) is { } s) said.Add(s);
        }
        // The last CLR's hold ends at 970 ms. The empty pad is read on the SECOND poll past it
        // (1200 ms is the first, 1450 ms the second), and the polls after that stay silent.
        for (var ms = 700; ms <= 2000; ms += 250)
            if (readBack.OnPoll("", T0.AddMilliseconds(ms)) is { } s) said.Add(s);

        Assert.Equal(new[] { "Scratchpad cleared" }, said);
    }

    [Fact]
    public void The_window_read_back_never_reads_a_one_poll_redraw_flicker()
    {
        // A redraw frame that blanks the scratchpad for one poll between two identical ones. With
        // one stable poll (the iFly's) the blank is read as "Scratchpad cleared"; with the window's
        // two it is neither that nor a re-read of the text. Fails if CreateReadBack loses its 2.
        var readBack = Md11McduScratchpad.CreateReadBack();
        readBack.OnPoll("KJFK", T0);                                           // the window's silent seed

        Assert.Null(readBack.OnPoll("", T0.AddMilliseconds(250)));             // the redraw frame
        Assert.Null(readBack.OnPoll("KJFK", T0.AddMilliseconds(500)));         // back: nothing happened
        Assert.Null(readBack.OnPoll("KJFK", T0.AddMilliseconds(750)));
    }
}
