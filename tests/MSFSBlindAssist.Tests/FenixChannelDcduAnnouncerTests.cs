// Characterization tests for FenixChannelDcduAnnouncer, which decides when a message on the captain's DCDU is new and what
// the call says. Pure logic: the screens are constructed here, and the announcer is driven by hand with its own clock (a
// plain number of milliseconds).
//
// The contract: the identity of a message is its header-left text and its first message line (the parsed text, trimmed;
// none while header-left is blank), so a status change, a page of the message or an answer is never a new message. The first
// ok screen after a start, a problem or a reconnect is a silent baseline. After it an identity counts once it has held 300 ms:
// it is remembered then (up to 64, the oldest dropped) and called once, unless the DCDU is muted or the call falls within
// 3 s of the first sample after the pilot's own window key. A call is "DCDU: " and the header-left line, then the first
// message line.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduAnnouncerTests
{
    private const string Spaces14 = "              ";

    // Three messages from one station, told apart by their header time and first line.
    private static readonly FenixDcduScreen MessageA = Message("w1426Z FROM EDGG", "cCLIMB TO", "cFL350");
    private static readonly FenixDcduScreen MessageB = Message("w1431Z FROM EDGG", "cMAINTAIN", "cFL330");
    private static readonly FenixDcduScreen MessageC = Message("w1440Z FROM EDGG", "cCONTACT", "cEDGG CTR");

    private const string CallA = "DCDU: 1426Z FROM EDGG, white, CLIMB TO, cyan";
    private const string CallB = "DCDU: 1431Z FROM EDGG, white, MAINTAIN, cyan";
    private const string CallC = "DCDU: 1440Z FROM EDGG, white, CONTACT, cyan";

    // A constructed screen: the header halves, the message lines and info lines given from the first (the rest blank), then
    // the keys.
    private static FenixDcduScreen Screen(string headerLeft = "", string headerRight = "", string[]? message = null,
        string[]? info = null, string leftKey1 = "", string leftKey2 = "", string rightKey1 = "", string rightKey2 = "")
        => new(headerLeft, headerRight, Fill(message, 5), Fill(info, 3), leftKey1, leftKey2, rightKey1, rightKey2);

    private static string[] Fill(string[]? lines, int count)
        => Enumerable.Range(0, count).Select(i => lines is not null && i < lines.Length ? lines[i] : "").ToArray();

    // An open uplink with the answer keys drawn, as the DCDU shows a new message.
    private static FenixDcduScreen Message(string headerLeft, params string[] lines)
        => Screen(headerLeft, "gOPEN", lines, leftKey1: "c*UNABLE", rightKey1: "cWILCO*", leftKey2: "c*STBY");

    private static FenixChannelSection<FenixDcduScreen> Ok(FenixDcduScreen screen) => FenixChannelSection<FenixDcduScreen>.Ok(screen);

    // One sample of an ok screen on connection 1 unless the test says otherwise.
    private static string? At(FenixChannelDcduAnnouncer announcer, long nowMs, FenixDcduScreen screen,
        int connection = 1, bool muted = false)
        => announcer.Sample(Ok(screen), connection, muted, nowMs);

    // Samples the screen every 100 ms from one time to another, both included, and gives each call with the time it came.
    private static List<(long Ms, string Text)> Calls(FenixChannelDcduAnnouncer announcer, long fromMs, long toMs,
        FenixDcduScreen screen, int connection = 1, bool muted = false)
    {
        var calls = new List<(long, string)>();
        for (long ms = fromMs; ms <= toMs; ms += 100)
        {
            if (At(announcer, ms, screen, connection, muted) is { } text) calls.Add((ms, text));
        }

        return calls;
    }

    // An announcer past its baseline with the given screen on it: sampled at 0 ms, so the next sample is at 100 ms or later.
    private static FenixChannelDcduAnnouncer Baselined(FenixDcduScreen screen)
    {
        var announcer = new FenixChannelDcduAnnouncer();
        Assert.Null(At(announcer, 0, screen));
        return announcer;
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
    public void The_settle_the_quiet_and_the_memory_are_the_measured_ones()
    {
        Assert.Equal(300L, FenixChannelDcduAnnouncer.SettleMs);
        Assert.Equal(3000L, FenixChannelDcduAnnouncer.NavigationQuietMs);
        Assert.Equal(64, FenixChannelDcduAnnouncer.SeenCapacity);
    }

    // ---- The identity -----------------------------------------------------------------------

    [Fact]
    public void The_identity_is_the_header_left_text_and_the_first_message_line()
    {
        Assert.Equal("1426Z FROM EDGG\nCLIMB TO", FenixChannelDcduAnnouncer.IdentityOf(MessageA));

        // The first line with words: the blank ones before it are not it, and the lines after it are not part of it.
        Assert.Equal("1426Z FROM EDGG\nCLIMB TO",
            FenixChannelDcduAnnouncer.IdentityOf(Screen("w1426Z FROM EDGG", message: ["", "c  ", "cCLIMB TO", "cFL350"])));

        // Parsed and trimmed: colors, a braille spacer or spaces around the text change nothing.
        Assert.Equal("1426Z FROM EDGG\nCLIMB TO",
            FenixChannelDcduAnnouncer.IdentityOf(Screen("w  1426Z FROM EDGG ", message: ["g⠀CLIMB TO  "])));
        Assert.Equal(FenixChannelDcduAnnouncer.IdentityOf(MessageA),
            FenixChannelDcduAnnouncer.IdentityOf(Screen("g1426Z FROM EDGG", message: ["aCLIMB TO"])));
    }

    [Fact]
    public void The_identity_leaves_out_the_status_the_info_lines_and_the_keys()
    {
        string? identity = FenixChannelDcduAnnouncer.IdentityOf(MessageA);

        Assert.Equal(identity, FenixChannelDcduAnnouncer.IdentityOf(MessageA with { HeaderRight = "gWILCO" }));
        Assert.Equal(identity, FenixChannelDcduAnnouncer.IdentityOf(MessageA with { HeaderRight = "" }));
        Assert.Equal(identity, FenixChannelDcduAnnouncer.IdentityOf(MessageA with { Info = ["gSENT", "", ""] }));
        Assert.Equal(identity, FenixChannelDcduAnnouncer.IdentityOf(MessageA with { LeftKey1 = "", RightKey1 = "cSEND*" }));
    }

    [Fact]
    public void Messages_differ_by_their_header_or_by_their_first_line()
    {
        Assert.NotEqual(FenixChannelDcduAnnouncer.IdentityOf(MessageA), FenixChannelDcduAnnouncer.IdentityOf(MessageB));

        // One header, two first lines.
        string? climb = FenixChannelDcduAnnouncer.IdentityOf(Screen("w1426Z FROM EDGG", message: ["cCLIMB TO"]));
        string? maintain = FenixChannelDcduAnnouncer.IdentityOf(Screen("w1426Z FROM EDGG", message: ["cMAINTAIN"]));
        Assert.NotEqual(climb, maintain);

        // One first line, two headers; and a header with no message line is not a header with a first line.
        Assert.NotEqual(climb, FenixChannelDcduAnnouncer.IdentityOf(Screen("w1431Z FROM EDGG", message: ["cCLIMB TO"])));
        Assert.NotEqual(climb, FenixChannelDcduAnnouncer.IdentityOf(Screen("w1426Z FROM EDGG")));
    }

    [Fact]
    public void A_header_with_no_message_line_is_still_an_identity()
    {
        Assert.Equal("1426Z FROM EDGG\n", FenixChannelDcduAnnouncer.IdentityOf(Screen("w1426Z FROM EDGG", "gOPEN")));
    }

    [Fact]
    public void There_is_no_identity_while_header_left_is_blank()
    {
        Assert.Null(FenixChannelDcduAnnouncer.IdentityOf(Screen()));
        Assert.Null(FenixChannelDcduAnnouncer.IdentityOf(Screen(Spaces14, "gOPEN", ["cCLIMB TO"])));
        Assert.Null(FenixChannelDcduAnnouncer.IdentityOf(Screen("w", message: ["cCLIMB TO"], leftKey1: "c*UNABLE")));
        Assert.Null(FenixChannelDcduAnnouncer.IdentityOf(Screen("w⠀  ", message: ["cCLIMB TO"])));
    }

    // ---- The call ---------------------------------------------------------------------------

    [Fact]
    public void The_call_is_dcdu_the_header_left_line_and_the_first_message_line()
    {
        Assert.Equal(CallA, FenixChannelDcduAnnouncer.CallText(MessageA));
        Assert.Equal("DCDU: 1426Z FROM EDGG, white, CLIMB TO, cyan",
            FenixChannelDcduAnnouncer.CallText(Screen("w1426Z FROM EDGG", message: ["", "c  ", "cCLIMB TO", "cFL350"])));

        // A header of two colors names each, as the window's rows do.
        Assert.Equal("DCDU: 1426Z FROM white, EDGG green, CLIMB TO, cyan",
            FenixChannelDcduAnnouncer.CallText(Screen("w1426Z FROM gEDGG", message: ["cCLIMB TO"])));
    }

    [Fact]
    public void The_call_leaves_out_the_status_the_second_line_and_the_keys()
    {
        // Spec answer 45: a new message's status adds nothing. Whatever the status says, the call is the same.
        Assert.Equal(CallA, FenixChannelDcduAnnouncer.CallText(MessageA with { HeaderRight = "gWILCO" }));
        Assert.Equal(CallA, FenixChannelDcduAnnouncer.CallText(MessageA with { HeaderRight = "" }));
        Assert.DoesNotContain("OPEN", FenixChannelDcduAnnouncer.CallText(MessageA));
        Assert.DoesNotContain("FL350", FenixChannelDcduAnnouncer.CallText(MessageA));
        Assert.DoesNotContain("UNABLE", FenixChannelDcduAnnouncer.CallText(MessageA));
    }

    [Fact]
    public void A_header_with_no_message_line_is_called_by_its_header_alone()
    {
        Assert.Equal("DCDU: 1426Z FROM EDGG, white",
            FenixChannelDcduAnnouncer.CallText(Screen("w1426Z FROM EDGG", "gOPEN", info: ["gINFO"], leftKey1: "c*UNABLE")));
    }

    [Fact]
    public void There_is_no_call_without_a_header()
    {
        Assert.Null(FenixChannelDcduAnnouncer.CallText(Screen()));
        Assert.Null(FenixChannelDcduAnnouncer.CallText(Screen(Spaces14, "gOPEN", ["cCLIMB TO"])));
    }

    // ---- The baseline -----------------------------------------------------------------------

    [Fact]
    public void The_first_reading_is_a_silent_baseline()
    {
        // A message already on the DCDU when the app starts (or the aircraft loads) is not news.
        var announcer = new FenixChannelDcduAnnouncer();

        Assert.Null(At(announcer, 0, MessageA));
        Assert.Empty(Calls(announcer, 100, 5000, MessageA));
    }

    [Fact]
    public void An_empty_first_reading_is_a_baseline_too()
    {
        // The measured empty DCDU has no header: the baseline remembers nothing, and the first message to come is new.
        var announcer = new FenixChannelDcduAnnouncer();
        FenixDcduScreen empty = Screen(info: ["", Spaces14, Spaces14]);

        Assert.Empty(Calls(announcer, 0, 1000, empty));
        Assert.Equal(new[] { (1300L, CallA) }, Calls(announcer, 1000, 2000, MessageA).ToArray());
    }

    // ---- A new message ----------------------------------------------------------------------

    [Fact]
    public void A_new_message_is_called_once_after_it_settles()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        // Shown from 1,000 ms: it has not held 300 ms until 1,300.
        Assert.Null(At(announcer, 900, Screen()));
        Assert.Null(At(announcer, 1000, MessageA));
        Assert.Null(At(announcer, 1100, MessageA));
        Assert.Null(At(announcer, 1200, MessageA));
        Assert.Null(At(announcer, 1299, MessageA));
        Assert.Equal(CallA, At(announcer, 1300, MessageA));

        // Once: not again while it stays, nor later.
        Assert.Null(At(announcer, 1400, MessageA));
        Assert.Null(At(announcer, 1700, MessageA));
        Assert.Empty(Calls(announcer, 1800, 20_000, MessageA));
    }

    [Fact]
    public void A_message_on_the_screen_at_the_baseline_is_not_called_when_another_comes_and_goes()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        Assert.Equal(new[] { (1300L, CallB) }, Calls(announcer, 1000, 2000, MessageB).ToArray());
        Assert.Empty(Calls(announcer, 2100, 3000, MessageB));
    }

    [Fact]
    public void A_message_that_does_not_hold_the_settle_is_not_called_and_its_hold_starts_again()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        // 1,000 to 1,200 is 200 ms: it goes before it counts, and the clear screen ends its hold.
        Assert.Empty(Calls(announcer, 1000, 1200, MessageA));
        Assert.Null(At(announcer, 1300, Screen()));

        // Back from 1,400: the hold begins again, so the call is at 1,700 and not before.
        Assert.Null(At(announcer, 1400, MessageA));
        Assert.Null(At(announcer, 1600, MessageA));
        Assert.Equal(CallA, At(announcer, 1700, MessageA));
    }

    [Fact]
    public void A_message_that_changes_under_the_hold_is_the_one_that_settles()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        // A shown for 200 ms, then B: B is called 300 ms after it showed, and A never is.
        Assert.Empty(Calls(announcer, 1000, 1200, MessageA));
        Assert.Null(At(announcer, 1300, MessageB));
        Assert.Null(At(announcer, 1500, MessageB));
        Assert.Equal(CallB, At(announcer, 1600, MessageB));
    }

    [Fact]
    public void A_status_change_is_not_a_new_message()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        // "gOPEN" to "gWILCO" and on to "gSENT": the same message the whole time, however long.
        FenixDcduScreen wilco = MessageA with { HeaderRight = "gWILCO" };
        FenixDcduScreen sent = MessageA with { HeaderRight = "gSENT" };

        Assert.Empty(Calls(announcer, 100, 1000, MessageA));
        Assert.Empty(Calls(announcer, 1100, 2000, wilco));
        Assert.Empty(Calls(announcer, 2100, 3000, sent));
        Assert.Empty(Calls(announcer, 3100, 4000, MessageA));
    }

    [Fact]
    public void A_new_messages_status_changing_under_the_hold_does_not_restart_it()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        Assert.Null(At(announcer, 1000, MessageA));
        Assert.Null(At(announcer, 1100, MessageA with { HeaderRight = "gWILCO" }));
        Assert.Equal(CallA, At(announcer, 1300, MessageA with { HeaderRight = "gSENT" }));
    }

    [Fact]
    public void Two_messages_under_one_header_are_told_apart()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());
        FenixDcduScreen climb = Screen("w1426Z FROM EDGG", "gOPEN", ["cCLIMB TO", "cFL350"]);
        FenixDcduScreen maintain = Screen("w1426Z FROM EDGG", "gOPEN", ["cMAINTAIN", "cFL350"]);

        Assert.Equal(new[] { (1300L, "DCDU: 1426Z FROM EDGG, white, CLIMB TO, cyan") },
            Calls(announcer, 1000, 1500, climb).ToArray());
        Assert.Equal(new[] { (2300L, "DCDU: 1426Z FROM EDGG, white, MAINTAIN, cyan") },
            Calls(announcer, 2000, 2500, maintain).ToArray());
    }

    [Fact]
    public void A_later_line_changing_is_not_a_new_message()
    {
        // A change in a line after the first is the same message: only the header and the first line make the identity.
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        Assert.Empty(Calls(announcer, 100, 1000, MessageA with { Message = ["cCLIMB TO", "cFL370", "", "", ""] }));
    }

    [Fact]
    public void A_screen_with_no_header_is_never_called()
    {
        // The headless screen carries no identity: nothing to call whatever its lines say, and a blank DCDU between two
        // messages leaves each of them new.
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());
        FenixDcduScreen headless = Screen("", "gOPEN", ["cCLIMB TO", "cFL350"], leftKey1: "c*UNABLE");

        Assert.Empty(Calls(announcer, 100, 2000, headless));
        Assert.Equal(new[] { (2300L, CallA) }, Calls(announcer, 2000, 2400, MessageA).ToArray());
        Assert.Empty(Calls(announcer, 2500, 3000, Screen()));
        Assert.Equal(new[] { (3300L, CallB) }, Calls(announcer, 3000, 3400, MessageB).ToArray());
    }

    // ---- The pilot's own keys ---------------------------------------------------------------

    [Fact]
    public void Paging_or_stepping_after_a_window_key_is_silent()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        // The key, and the first sample after it at 0: the quiet runs from there to 3,000.
        announcer.NoteNavigation();
        Assert.Null(At(announcer, 0, MessageA));

        // A new first line 500 ms after the key (stepping to another message): shown, settled and silent.
        Assert.Empty(Calls(announcer, 500, 1500, MessageB));

        // Another header at 3,500 ms, shown from 3,200 once the quiet is over: called.
        Assert.Null(At(announcer, 3200, MessageC));
        Assert.Null(At(announcer, 3300, MessageC));
        Assert.Equal(CallC, At(announcer, 3500, MessageC));
    }

    [Fact]
    public void The_quiet_begins_at_the_first_sample_after_the_key_and_not_at_the_key()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        // The key is pressed with no clock; the first sample after it comes at 10,000 ms. The quiet is 10,000 to 13,000.
        announcer.NoteNavigation();
        Assert.Null(At(announcer, 10_000, MessageA));

        // B settles at 12,900: within the quiet, silent. It is remembered, so it is not called when the quiet ends.
        Assert.Empty(Calls(announcer, 12_600, 14_000, MessageB));
    }

    [Fact]
    public void The_quiet_ends_when_three_seconds_have_passed_since_that_sample()
    {
        // C settles at exactly 13,000: the quiet is over, so it is called.
        FenixChannelDcduAnnouncer exactly = Baselined(MessageA);
        exactly.NoteNavigation();
        Assert.Null(At(exactly, 10_000, MessageA));
        Assert.Null(At(exactly, 12_700, MessageC));
        Assert.Null(At(exactly, 12_800, MessageC));
        Assert.Null(At(exactly, 12_900, MessageC));
        Assert.Equal(CallC, At(exactly, 13_000, MessageC));

        // D settles at 12,999, a moment sooner: still within it, so it is silent.
        FenixChannelDcduAnnouncer sooner = Baselined(MessageA);
        sooner.NoteNavigation();
        Assert.Null(At(sooner, 10_000, MessageA));
        Assert.Null(At(sooner, 12_699, MessageC));
        Assert.Null(At(sooner, 12_999, MessageC));
        Assert.Empty(Calls(sooner, 13_000, 14_000, MessageC));
    }

    [Fact]
    public void A_second_key_extends_the_quiet()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        announcer.NoteNavigation();
        Assert.Null(At(announcer, 0, MessageA));

        // A second key at the pilot's next step, 2,500 ms in: the quiet runs on to 5,500. B settles at 3,300, after the
        // first quiet would have ended, and is silent.
        announcer.NoteNavigation();
        Assert.Null(At(announcer, 2500, MessageA));
        Assert.Empty(Calls(announcer, 3000, 5000, MessageB));

        Assert.Null(At(announcer, 5100, MessageC));
        Assert.Null(At(announcer, 5300, MessageC));
        Assert.Equal(CallC, At(announcer, 5500, MessageC));
    }

    [Fact]
    public void Messages_stepped_through_in_the_quiet_are_remembered_and_the_next_new_one_is_called()
    {
        // The pilot steps to B in the quiet and back to A, then a new message C arrives later: B and A are seen, C is new.
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        announcer.NoteNavigation();
        Assert.Null(At(announcer, 0, MessageA));
        Assert.Empty(Calls(announcer, 100, 800, MessageB));
        Assert.Empty(Calls(announcer, 900, 1600, MessageA));
        Assert.Equal(new[] { (4300L, CallC) }, Calls(announcer, 4000, 5000, MessageC).ToArray());
    }

    [Fact]
    public void Stepping_back_to_a_message_seen_before_is_silent()
    {
        // Outside any quiet: the other aircraft systems or the sim itself can show an earlier message again. It was
        // remembered when it counted (A at the baseline, B when it was called), so it is not new.
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        Assert.Equal(new[] { (1300L, CallB) }, Calls(announcer, 1000, 1500, MessageB).ToArray());
        Assert.Empty(Calls(announcer, 2000, 3000, MessageA));
        Assert.Empty(Calls(announcer, 3100, 4000, MessageB));
        Assert.Empty(Calls(announcer, 4100, 5000, MessageA));
    }

    // ---- The mute ---------------------------------------------------------------------------

    [Fact]
    public void A_muted_message_is_not_replayed_on_unmute()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        // Shown and settled while muted: silent, and remembered.
        Assert.Empty(Calls(announcer, 1000, 2000, MessageA, muted: true));

        // Unmuted with the message still on the screen: still silent, for as long as it stays.
        Assert.Empty(Calls(announcer, 2100, 5000, MessageA));

        // And a later message is called as usual.
        Assert.Equal(new[] { (6300L, CallB) }, Calls(announcer, 6000, 6500, MessageB).ToArray());
    }

    [Fact]
    public void A_message_shown_while_muted_stays_silent_when_the_mute_ends_before_it_settles()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        Assert.Null(At(announcer, 1000, MessageA, muted: true));
        Assert.Null(At(announcer, 1100, MessageA));
        Assert.Null(At(announcer, 1300, MessageA));
        Assert.Empty(Calls(announcer, 1400, 3000, MessageA));
    }

    [Fact]
    public void A_mute_beginning_under_the_hold_silences_the_call()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        Assert.Null(At(announcer, 1000, MessageA));
        Assert.Null(At(announcer, 1200, MessageA));
        Assert.Null(At(announcer, 1300, MessageA, muted: true));
        Assert.Empty(Calls(announcer, 1400, 3000, MessageA));
    }

    [Fact]
    public void A_muted_message_is_silent_and_the_next_one_after_the_unmute_is_called()
    {
        // The mute silences message B, which came while it was on; C came after it ended and is called.
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        Assert.Empty(Calls(announcer, 1000, 2000, MessageB, muted: true));
        Assert.Equal(new[] { (3300L, CallC) }, Calls(announcer, 3000, 3500, MessageC).ToArray());
    }

    // ---- Gaps -------------------------------------------------------------------------------

    [Fact]
    public void A_trust_gap_takes_a_new_silent_baseline()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        // The DCDU goes untrusted for a stretch (a display-off gap): nothing is said, and a message that came during it
        // is on the screen when it ends, not new.
        var gap = new FenixChannelSection<FenixDcduScreen>(default, "display off");
        Assert.Null(announcer.Sample(gap, 1, false, 1000));
        Assert.Null(announcer.Sample(gap, 1, false, 1100));

        // The first ok screen is the baseline, and so is nothing more than that: B holds past the settle and is silent.
        Assert.Null(At(announcer, 2000, MessageB));
        Assert.Empty(Calls(announcer, 2100, 3500, MessageB));

        // A message after that is new again.
        Assert.Equal(new[] { (4300L, CallC) }, Calls(announcer, 4000, 4500, MessageC).ToArray());
    }

    [Fact]
    public void A_gap_too_short_to_hold_a_message_still_ends_the_baseline()
    {
        // A message that was settling when the gap came is the baseline after it, not a call: the gap ends the hold.
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        Assert.Null(At(announcer, 1000, MessageA));
        Assert.Null(At(announcer, 1100, MessageA));
        Assert.Null(announcer.Sample(new FenixChannelSection<FenixDcduScreen>(default, "display off"), 1, false, 1200));
        Assert.Empty(Calls(announcer, 1300, 3000, MessageA));
    }

    [Fact]
    public void A_gap_forgets_what_was_seen_before_it()
    {
        // The baseline starts the memory afresh: a message shown again after the gap that was not on the screen at its
        // baseline is called like any message new since the baseline.
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);
        Assert.Equal(new[] { (1300L, CallB) }, Calls(announcer, 1000, 1500, MessageB).ToArray());

        Assert.Null(announcer.Sample(new FenixChannelSection<FenixDcduScreen>(default, "display off"), 1, false, 2000));
        Assert.Null(At(announcer, 2100, MessageB));
        Assert.Equal(new[] { (3300L, CallA) }, Calls(announcer, 3000, 3500, MessageA).ToArray());
    }

    [Fact]
    public void A_reconnect_takes_a_new_silent_baseline()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        // The channel reconnects (connection 1 to 2) with a new message on the screen: nothing is said then, nor when the
        // message has held its settle on the new connection.
        Assert.Null(At(announcer, 1000, MessageB, connection: 2));
        Assert.Empty(Calls(announcer, 1100, 3000, MessageB, connection: 2));

        // A message after that, on the same connection, is new.
        Assert.Equal(new[] { (4300L, CallC) }, Calls(announcer, 4000, 4500, MessageC, connection: 2).ToArray());
    }

    [Fact]
    public void A_reconnect_during_a_settle_does_not_make_the_message_new()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        Assert.Null(At(announcer, 1000, MessageA));
        Assert.Null(At(announcer, 1200, MessageA));
        Assert.Null(At(announcer, 1300, MessageA, connection: 2));
        Assert.Empty(Calls(announcer, 1400, 3000, MessageA, connection: 2));
    }

    [Fact]
    public void The_connection_is_judged_against_the_last_one_seen_even_on_a_problem()
    {
        // A problem sample on the new connection ends the baseline and is the last connection seen: the next ok screen on
        // that connection is the baseline, not a second reconnect and not new.
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);

        Assert.Null(announcer.Sample(FenixChannelSection<FenixDcduScreen>.Missing("DCDU content"), 2, false, 1000));
        Assert.Null(At(announcer, 1100, MessageB, connection: 2));

        // B was the baseline, so C, shown from 1,200, is new and is called at 1,500.
        Assert.Equal(new[] { (1500L, CallC) }, Calls(announcer, 1200, 1700, MessageC, connection: 2).ToArray());
    }

    [Fact]
    public void A_screen_with_a_problem_says_nothing()
    {
        FenixChannelDcduAnnouncer announcer = Baselined(Screen());

        FenixChannelSection<FenixDcduScreen>[] problems =
        [
            FenixChannelSection<FenixDcduScreen>.Missing("DCDU content"),
            FenixChannelSection<FenixDcduScreen>.WrongShape("186.4"),
            FenixChannelSection<FenixDcduScreen>.UnknownCode('i', "DCDU message"),
            new(default, "display off"),
            default,
        ];

        long ms = 1000;
        foreach (FenixChannelSection<FenixDcduScreen> problem in problems)
        {
            Assert.Null(announcer.Sample(problem, 1, false, ms));
            ms += 1000;
        }

        // Not even the value of a section that has a problem as well as a value.
        Assert.Null(announcer.Sample(new FenixChannelSection<FenixDcduScreen>(MessageA, "display off"), 1, false, ms));
    }

    [Fact]
    public void A_problem_before_any_reading_says_nothing_and_the_first_ok_screen_is_the_baseline()
    {
        var announcer = new FenixChannelDcduAnnouncer();

        Assert.Null(announcer.Sample(FenixChannelSection<FenixDcduScreen>.Missing("DCDU content"), 1, false, 0));
        Assert.Null(At(announcer, 100, MessageA));
        Assert.Empty(Calls(announcer, 200, 2000, MessageA));
    }

    // ---- The memory -------------------------------------------------------------------------

    [Fact]
    public void The_oldest_message_is_forgotten_past_the_capacity()
    {
        // The baseline remembers A; then 64 more messages are called, which is one past the capacity, and A goes.
        FenixChannelDcduAnnouncer announcer = Baselined(MessageA);
        long ms = 1000;
        FenixDcduScreen last = MessageA;
        for (int i = 1; i <= FenixChannelDcduAnnouncer.SeenCapacity; i++)
        {
            last = Message($"w{1500 + i}Z FROM EDGG", "cCLIMB TO");
            Assert.Equal(new[] { (ms + 300, FenixChannelDcduAnnouncer.CallText(last)!) },
                Calls(announcer, ms, ms + 400, last).ToArray());
            ms += 1000;
        }

        // The last one called is still remembered, and the first of the 64 too; A, the oldest, is not.
        Assert.Empty(Calls(announcer, ms, ms + 1000, Message("w1501Z FROM EDGG", "cCLIMB TO")));
        ms += 2000;
        Assert.Empty(Calls(announcer, ms, ms + 1000, last));
        ms += 2000;
        Assert.Equal(new[] { (ms + 300, CallA) }, Calls(announcer, ms, ms + 400, MessageA).ToArray());
    }

    // ---- Culture ----------------------------------------------------------------------------

    [Fact]
    public void The_call_reads_the_same_in_de_DE()
    {
        // CI runs under en-US: a call built with the PC's culture would read differently here. It holds no number the
        // app writes, only the DCDU's own text.
        InCulture("de-DE", () =>
        {
            Assert.Equal(CallA, FenixChannelDcduAnnouncer.CallText(MessageA));
            Assert.Equal("1426Z FROM EDGG\nCLIMB TO", FenixChannelDcduAnnouncer.IdentityOf(MessageA));

            FenixChannelDcduAnnouncer announcer = Baselined(Screen());
            Assert.Equal(new[] { (1300L, CallA) }, Calls(announcer, 1000, 1500, MessageA).ToArray());
        });

        InCulture("sv-SE", () =>
        {
            Assert.Equal(CallA, FenixChannelDcduAnnouncer.CallText(MessageA));
        });
    }
}
