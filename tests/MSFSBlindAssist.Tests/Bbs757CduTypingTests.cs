using System.Globalization;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 08 Task 2: typed entries go out faster and a missed key is said.</summary>
public class Bbs757CduTypingTests
{
    private static Bbs757CduScreen WithScratchpad(string text)
    {
        var lines = Bbs757CduFrame.Blank.Lines.ToArray();
        lines[Bbs757CduFrame.ScratchpadRow] = text.PadRight(Bbs757CduFrame.Columns)[..Bbs757CduFrame.Columns];
        return new Bbs757CduScreen(lines);
    }

    [Fact]
    public void Typing_keys_are_the_cdu_keys()
    {
        Assert.Equal(new[] { "KEYK", "KEYA", "KEYT", "KEYL", "NUM1", "NUM1", "NUM0", "NUM0", Bbs757CduKeys.Period, "NUM5" },
            Bbs757CduTyping.Keys("katl1100.5"));
        // As the window always typed: Bbs757CduKeys.ForChar of each character, one with no key skipped.
        Assert.Equal("katl/1".SelectMany(Bbs757CduKeys.ForChar).ToList(), Bbs757CduTyping.Keys("katl/1"));
        Assert.Equal(new[] { "KEYA" }, Bbs757CduTyping.Keys("a#"));
        Assert.Empty(Bbs757CduTyping.Keys(""));
    }

    [Fact]
    public void The_typing_timing_is_the_measured_one()
    {
        // Measured 2026-10-27 ([B757-58]): powered, 100/100 lost repeated digits (6/10); 100/200 15/15; 100/300 keeps a step of margin.
        Assert.Equal(100, Bbs757CduTyping.HoldMs);
        Assert.Equal(300, Bbs757CduTyping.GapMs);
        Assert.True(CoherentBbs757TabletClient.CycleMs(Bbs757CduTyping.HoldMs, Bbs757CduTyping.GapMs) < CoherentBbs757TabletClient.KeyCycleMs);
    }

    [Fact]
    public void Mismatch_cases()
    {
        // A match is silent, whatever the case typed.
        Assert.Null(Bbs757CduTyping.Mismatch("katl", WithScratchpad("KATL")));
        Assert.Null(Bbs757CduTyping.Mismatch("1100.5", WithScratchpad("1100.5")));
        // A missed key says what the scratchpad shows.
        Assert.Equal("Scratchpad shows KAT", Bbs757CduTyping.Mismatch("KATL", WithScratchpad("KAT")));
        Assert.Equal("Scratchpad shows 10.5", Bbs757CduTyping.Mismatch("100.5", WithScratchpad("10.5")));
        // An empty scratchpad.
        Assert.Equal("Scratchpad is empty", Bbs757CduTyping.Mismatch("KATL", WithScratchpad("")));
        // A message over the entry is said as the scratchpad shows it.
        Assert.Equal("Scratchpad shows INVALID ENTRY", Bbs757CduTyping.Mismatch("KATL", WithScratchpad("INVALID ENTRY")));
        // Typed onto what the scratchpad already held: the CDU adds to an entry.
        Assert.Null(Bbs757CduTyping.Mismatch("TL", WithScratchpad("KATL"), scratchpadBefore: "KA"));
        Assert.Equal("Scratchpad shows KAT", Bbs757CduTyping.Mismatch("TL", WithScratchpad("KAT"), scratchpadBefore: "KA"));
        // A character with no key is not expected in the scratchpad.
        Assert.Null(Bbs757CduTyping.Mismatch("KA#TL", WithScratchpad("KATL")));
        // Nothing typable typed: nothing to check.
        Assert.Null(Bbs757CduTyping.Mismatch("#", WithScratchpad("")));
    }

    [Fact]
    public void A_landed_entry_is_read_back_as_the_scratchpad_shows_it()
    {
        // The guide's promise, as every other CDU window does: after typing, the pilot hears what the CDU took.
        Assert.Equal("KATL", Bbs757CduTyping.ReadBack("katl", WithScratchpad("KATL")));
        Assert.Equal("1100.5", Bbs757CduTyping.ReadBack("1100.5", WithScratchpad("1100.5")));
        // Typed onto an entry already there: the whole scratchpad, as the CDU shows it.
        Assert.Equal("KATL", Bbs757CduTyping.ReadBack("TL", WithScratchpad("KATL"), scratchpadBefore: "KA"));
    }

    [Fact]
    public void A_missed_key_is_read_back_as_the_mismatch()
    {
        Assert.Equal("Scratchpad shows KAT", Bbs757CduTyping.ReadBack("KATL", WithScratchpad("KAT")));
        Assert.Equal("Scratchpad is empty", Bbs757CduTyping.ReadBack("KATL", WithScratchpad("")));
        Assert.Equal("Scratchpad shows INVALID ENTRY", Bbs757CduTyping.ReadBack("KATL", WithScratchpad("INVALID ENTRY")));
    }

    [Fact]
    public void Nothing_to_read_back_when_nothing_typable_landed_on_an_empty_scratchpad()
    {
        Assert.Null(Bbs757CduTyping.ReadBack("#", WithScratchpad("")));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_read_back_is_the_same_in_any_culture(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("1100.5", Bbs757CduTyping.ReadBack("1100.5", WithScratchpad("1100.5")));
            Assert.Equal("Scratchpad shows 100.5", Bbs757CduTyping.ReadBack("1100.5", WithScratchpad("100.5")));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- The window's check of a typed entry (Bbs757TypedEntryCheck) ----------------------------------

    private static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime At(int ms) => T0.AddMilliseconds(ms);

    [Fact]
    public void A_landed_entry_is_read_back_once_its_keys_have_settled()
    {
        var check = new Bbs757TypedEntryCheck();
        check.Typed(2, "KATL", scratchpadNow: "", otherKeysQueued: false);
        Assert.True(check.IsPending);
        Assert.Equal(2, check.Cdu);

        Assert.Null(check.Poll(At(0), pendingKeys: 4, WithScratchpad("KA"), At(0)));
        // The last key's cycle ended: the scratchpad settles first.
        Assert.Null(check.Poll(At(1600), pendingKeys: 0, WithScratchpad("KATL"), At(1500)));
        Assert.Null(check.Poll(At(1800), pendingKeys: 0, WithScratchpad("KATL"), At(1500)));

        var verdict = check.Poll(At(1950), pendingKeys: 0, WithScratchpad("KATL"), At(1500));
        Assert.Equal(new Bbs757TypedEntryVerdict("KATL", Missed: false), verdict);
        Assert.False(check.IsPending);
        Assert.Null(check.Poll(At(2300), pendingKeys: 0, WithScratchpad("KATL"), At(1500)));
    }

    [Fact]
    public void A_missed_key_waits_for_a_screen_newer_than_the_last_key()
    {
        var check = new Bbs757TypedEntryCheck();
        check.Typed(1, "KATL", scratchpadNow: "", otherKeysQueued: false);
        Assert.Null(check.Poll(At(1600), pendingKeys: 0, WithScratchpad("KAT"), At(1400)));
        // Settled, but the screen held is older than the last key: it may not show the last key yet.
        Assert.Null(check.Poll(At(1950), pendingKeys: 0, WithScratchpad("KAT"), At(1400)));
        // A newer screen still shows KAT: the CDU missed the L.
        Assert.Equal(new Bbs757TypedEntryVerdict("Scratchpad shows KAT", Missed: true),
            check.Poll(At(2300), pendingKeys: 0, WithScratchpad("KAT"), At(2100)));
        Assert.False(check.IsPending);
    }

    [Fact]
    public void A_missed_key_is_said_from_the_held_screen_after_the_longest_wait()
    {
        var check = new Bbs757TypedEntryCheck();
        check.Typed(1, "KATL", scratchpadNow: "", otherKeysQueued: false);
        Assert.Null(check.Poll(At(1600), pendingKeys: 0, WithScratchpad("KAT"), At(1400)));
        Assert.Null(check.Poll(At(1600 + Bbs757TypedEntryCheck.MaxWaitMs - 1), pendingKeys: 0, WithScratchpad("KAT"), At(1400)));
        Assert.Equal(new Bbs757TypedEntryVerdict("Scratchpad shows KAT", Missed: true),
            check.Poll(At(1600 + Bbs757TypedEntryCheck.MaxWaitMs), pendingKeys: 0, WithScratchpad("KAT"), At(1400)));
    }

    [Fact]
    public void A_key_queued_again_restarts_the_settle()
    {
        var check = new Bbs757TypedEntryCheck();
        check.Typed(1, "KA", scratchpadNow: "", otherKeysQueued: false);
        Assert.Null(check.Poll(At(800), pendingKeys: 0, WithScratchpad("KA"), At(700)));
        check.Typed(1, "TL", scratchpadNow: "KA", otherKeysQueued: true);   // the second entry, before the check ended
        Assert.Null(check.Poll(At(1150), pendingKeys: 2, WithScratchpad("KA"), At(700)));
        Assert.Null(check.Poll(At(1700), pendingKeys: 0, WithScratchpad("KATL"), At(1650)));
        // Checked as one entry: "KA" + "TL" landed on the empty scratchpad.
        Assert.Equal(new Bbs757TypedEntryVerdict("KATL", Missed: false),
            check.Poll(At(2000), pendingKeys: 0, WithScratchpad("KATL"), At(1650)));
    }

    [Fact]
    public void Another_key_ends_the_check()
    {
        // KATL then a line select key at once: the line takes the entry and the scratchpad empties. The
        // check ends instead of saying "Scratchpad is empty"; the ordinary read-back speaks what settles,
        // as on every other aircraft (owner, 2026-10-27).
        var check = new Bbs757TypedEntryCheck();
        check.Typed(1, "KATL", scratchpadNow: "", otherKeysQueued: false);
        check.Cancel();
        Assert.False(check.IsPending);
        Assert.Null(check.Cdu);
        Assert.Null(check.Poll(At(3000), pendingKeys: 0, WithScratchpad(""), At(2900)));
    }

    [Fact]
    public void An_entry_typed_behind_other_queued_keys_is_not_checked()
    {
        // A page key or a line key still queued may change the scratchpad before the typing: what it held is
        // unknown, so the ordinary read-back speaks what settles.
        var check = new Bbs757TypedEntryCheck();
        check.Typed(1, "KATL", scratchpadNow: "", otherKeysQueued: true);
        Assert.False(check.IsPending);
    }

    [Fact]
    public void An_entry_typed_into_the_other_cdu_ends_the_check()
    {
        var check = new Bbs757TypedEntryCheck();
        check.Typed(1, "KATL", scratchpadNow: "", otherKeysQueued: false);
        check.Typed(2, "KJFK", scratchpadNow: "", otherKeysQueued: true);
        Assert.False(check.IsPending);
    }
}
