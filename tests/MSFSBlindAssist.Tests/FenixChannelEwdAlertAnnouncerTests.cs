// Characterization tests for FenixChannelEwdAlertAnnouncer, which turns the Fenix's ECAM entries stream into spoken warnings,
// cautions and memos. Pure logic: the snapshots are constructed here, and the announcer is driven by hand with its own clock (a
// plain number of milliseconds), its mute rows and its report.
//
// The contract: an entries message other than the last one read replaces the remembered list on every step, whatever the
// verdict, unless it is a bare heartbeat or has a wrong shape. An item is keyed by its Ata and Name: "-STS-" is never one, a
// "MEMO-" name is a memo, except that "MEMO-LAND ASAP..." is the one LAND ASAP failure, and an IsAlert entry with a Title is a
// failure. Not trusted, no frame or a new connection says nothing and forgets everything; the next trusted step with a list is a
// silent baseline. A new item waits up to 500 ms for its E/WD line, is due at once when it has one and without a color when it
// has none. A failure is red under warnings, amber under cautions, and any other color or none is heard unless both rows are
// muted; a memo is under memos whatever its color. A failure out of the list for 4,500 ms says "<words> cleared" under the row of
// its latest color, and a memo says nothing. One step says the due red failures (the first spoken interrupting), the other due
// failures, the cleared lines, then the due memos, with LAND ASAP last of its own group. A muted line is dropped and never replayed.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelEwdAlertAnnouncerTests
{
    // The entries and lines the rules were measured on (A321 IAE, build 3.0.0.7613, 2026-11-04); ENG 2 FIRE and the amber LAND
    // ASAP's Name are made after them.
    private static readonly FenixEcamEntry EngFire = new("01-26- 10", "FIRE - ENG 1 FIRE", "ENG 1 FIRE", true);
    private static readonly FenixEcamEntry Eng2Fire = new("01-26- 20", "FIRE - ENG 2 FIRE", "ENG 2 FIRE", true);
    private static readonly FenixEcamEntry Gen1Fault = new("01-24-  1", "ELEC-GEN 1 FAULT", "ELEC GEN 1 FAULT", true);
    private static readonly FenixEcamEntry Gen1Status = new("01-24-190", "ELEC-STS-GEN 1 INOP", "", false);
    private static readonly FenixEcamEntry SeatBelts = new("01- 0-250", "MEMO-SEAT BELTS", "", false);
    private static readonly FenixEcamEntry LandAsapRed = new("01- 0-400", "MEMO-LAND ASAP (RED)", "", false);
    private static readonly FenixEcamEntry LandAsapAmber = new("01- 0-400", "MEMO-LAND ASAP (AMBER)", "", false);

    private const string EngFireLine = "urENG 1 FIREu ";
    private const string Eng2FireLine = "urENG 2 FIREu ";
    private const string Gen1Line = "uaELECu aGEN 1 FAULT";
    private const string SeatBeltsLine = "gSEAT BELTS";

    private static readonly FenixDisplayVerdict Trusted = new(true, "trusted");
    private static readonly FenixDisplayVerdict DisplayOff = new(false, "display off");

    private static FenixEwdAlertLine Queued(string text) => new(text, false);
    private static FenixEwdAlertLine Interrupting(string text) => new(text, true);

    // The E/WD with its two columns, the frame built positionally.
    private static FenixChannelFrame Ewd(string[]? left = null, string[]? right = null)
        => new(false, [], FenixFdState.Default, [], left ?? [], right ?? [], 0, 0, 0, "") { EwdLive = true };

    private static FenixEcamEntries List(params FenixEcamEntry[] entries) => new(false, [], entries, false);

    // What the channel sends every 5 s: the heartbeat set and no entries.
    private static FenixEcamEntries Heartbeat() => new(false, [], [], true);

    // An announcer with its mute rows and its report, driven by hand.
    private sealed class Rig
    {
        private readonly FenixChannelEwdAlertAnnouncer _announcer = new();

        public HashSet<string> Muted { get; } = new(StringComparer.Ordinal);

        public List<string> Reports { get; } = [];

        public FenixEwdAlertLine[] At(long ms, FenixEcamEntries? entries, FenixChannelFrame? frame, bool trusted = true,
            int connection = 1, int ecamConnection = 1)
            => _announcer.Step(
                new FenixChannelSnapshot(trusted ? Trusted : DisplayOff, frame, null, entries, ms, connection, ecamConnection),
                ms, Muted.Contains, Reports.Add).ToArray();

        // Steps every 100 ms from one time to another, both included, each step saying nothing.
        public void Silent(long fromMs, long toMs, FenixEcamEntries? entries, FenixChannelFrame? frame, bool trusted = true,
            int connection = 1, int ecamConnection = 1)
        {
            for (long ms = fromMs; ms <= toMs; ms += 100)
            {
                Assert.Empty(At(ms, entries, frame, trusted, connection, ecamConnection));
            }
        }
    }

    // A rig past its baseline on the given list and E/WD: stepped at 0 ms, so the next step is at 100 ms or later.
    private static Rig Baselined(FenixEcamEntries entries, FenixChannelFrame frame, params string[] muted)
    {
        var rig = new Rig();
        foreach (string key in muted) rig.Muted.Add(key);

        Assert.Empty(rig.At(0, entries, frame));
        return rig;
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

    // ---- Trust, the baseline and the list --------------------------------------------------

    [Fact]
    public void Nothing_is_said_while_the_ewd_is_not_trusted()
    {
        Rig rig = Baselined(List(), Ewd());

        // Not trusted, with a new failure and its line in plain view.
        rig.Silent(1000, 2000, List(EngFire), Ewd(left: [EngFireLine]), trusted: false);

        // Trusted, but no frame to find a line in.
        rig.Silent(2100, 3000, List(EngFire, Gen1Fault), null);

        // Trusted with a frame, the next step is the baseline; only what comes after it is spoken.
        FenixChannelFrame frame = Ewd(left: [EngFireLine, Gen1Line, Eng2FireLine]);
        Assert.Empty(rig.At(3100, List(EngFire, Gen1Fault), frame));
        Assert.Equal([Interrupting("ENG 2 FIRE, red")], rig.At(3200, List(EngFire, Gen1Fault, Eng2Fire), frame));
    }

    [Fact]
    public void The_first_trusted_reading_is_a_silent_baseline()
    {
        // A fire already on the E/WD when the app starts (or the aircraft loads) is not news.
        Rig rig = Baselined(List(EngFire), Ewd(left: [EngFireLine]));
        rig.Silent(100, 2000, List(EngFire), Ewd(left: [EngFireLine]));

        // With no list yet the baseline waits, and the first list is taken silently when it comes.
        var waiting = new Rig();
        waiting.Silent(0, 1000, null, Ewd(left: [EngFireLine]));
        Assert.Empty(waiting.At(1100, List(EngFire), Ewd(left: [EngFireLine])));
        waiting.Silent(1200, 2000, List(EngFire), Ewd(left: [EngFireLine]));

        // An empty list is a real message: it is the baseline, and the next item is new.
        var empty = new Rig();
        Assert.Empty(empty.At(0, List(), Ewd()));
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber")], empty.At(100, List(Gen1Fault), Ewd(left: [Gen1Line])));
    }

    [Fact]
    public void The_list_is_followed_while_untrusted()
    {
        var rig = new Rig();
        FenixEcamEntries fire = List(EngFire);
        FenixChannelFrame frame = Ewd(left: [EngFireLine, Gen1Line]);

        // A list read while untrusted, then another with no frame as well: each remembered all the same.
        Assert.Empty(rig.At(0, List(Gen1Fault), frame, trusted: false));
        Assert.Empty(rig.At(100, fire, null, trusted: false));

        // Then a heartbeat with no entries in the snapshot, and trust: the baseline holds the list.
        Assert.Empty(rig.At(200, Heartbeat(), frame));
        rig.Silent(300, 1000, Heartbeat(), frame);

        // Its next change is spoken: the fire stays said, the fault is new.
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber")], rig.At(1100, List(EngFire, Gen1Fault), frame));
    }

    [Fact]
    public void A_new_ecam_connection_forgets_the_remembered_list()
    {
        // The Fenix restarted: the data call reconnects and the E/WD is trusted again before the ECAM entries call sends
        // its first list. That list belongs to a new ECAM connection, so it is a silent baseline, and the earlier
        // session's fire is never said "cleared" nor the new memo said as news.
        FenixChannelFrame before = Ewd(left: [EngFireLine]);
        FenixChannelFrame after = Ewd(right: [SeatBeltsLine]);
        Rig rig = Baselined(List(EngFire), before);

        // The data call reconnects first: the snapshot still holds the old list, with no ECAM reconnect yet.
        rig.Silent(100, 500, List(EngFire), after, connection: 2);

        // Then the ECAM call reconnects with no list yet, and its first list comes.
        rig.Silent(600, 1000, null, after, connection: 2, ecamConnection: 2);
        FenixEcamEntries fresh = List(SeatBelts);
        rig.Silent(1100, 8000, fresh, after, connection: 2, ecamConnection: 2);

        // What changes after that baseline is spoken as usual.
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber")],
            rig.At(8100, List(SeatBelts, Gen1Fault), Ewd(left: [Gen1Line], right: [SeatBeltsLine]),
                connection: 2, ecamConnection: 2));
    }

    [Fact]
    public void A_new_ecam_connection_whose_first_list_comes_at_once_is_still_a_baseline()
    {
        // The step that first shows the new ECAM connection may already hold its first list: it is still not news.
        Rig rig = Baselined(List(EngFire), Ewd(left: [EngFireLine]));
        rig.Silent(100, 8000, List(SeatBelts), Ewd(right: [SeatBeltsLine]), ecamConnection: 2);
    }

    [Fact]
    public void An_item_waiting_for_its_line_when_a_gap_comes_is_not_spoken_after_it()
    {
        Rig rig = Baselined(List(), Ewd());
        FenixEcamEntries fire = List(EngFire);

        // New with no line yet, so it waits; then the gap, and the baseline after it takes the item silently.
        Assert.Empty(rig.At(1000, fire, Ewd()));
        Assert.Empty(rig.At(1100, fire, Ewd(), trusted: false));
        rig.Silent(1200, 3000, fire, Ewd(left: [EngFireLine]));
    }

    [Fact]
    public void After_a_power_up_the_first_new_list_is_heard_like_any_other()
    {
        // Measured in two blackouts: the list does not change while the power is off, and the first new one comes 18 to 24 s
        // after the E/WD is trusted. The owner's ruling (spec row 62): its start-up failures are spoken, as a sighted pilot sees
        // them on the display, and what left it says "cleared"; no wait for that list.
        var speeds = new FenixEcamEntry("01-22-720", "T.O - SPEEDS NOT INSERTED", "T.O SPEEDS NOT INSERTED", true);
        var cids = new FenixEcamEntry("01-23- 30", "COM-CIDS FAULT", "COM CIDS 1+2 FAULT", true);
        FenixEcamEntries before = List(speeds);
        Rig rig = Baselined(before, Ewd(left: ["uaT.Ou aSPEEDS NOT INSERTED"]));

        // The blackout, then the E/WD trusted with blank columns: the old list, taken silently.
        rig.Silent(1000, 2000, before, Ewd(), trusted: false);
        rig.Silent(2100, 3000, before, Ewd());

        // The first new list, 24 s later.
        FenixEcamEntries first = List(cids);
        FenixChannelFrame startUp = Ewd(left: ["uaCOMu aCIDS 1+2 FAULT"]);
        Assert.Equal([Queued("COM CIDS 1+2 FAULT, amber")], rig.At(27_000, first, startUp));

        rig.Silent(27_100, 31_400, first, startUp);
        Assert.Equal([Queued("T.O SPEEDS NOT INSERTED cleared")], rig.At(31_500, first, startUp));
    }

    // ---- A new failure ----------------------------------------------------------------------

    [Fact]
    public void A_new_warning_interrupts_with_its_color()
    {
        Rig rig = Baselined(List(), Ewd());
        FenixChannelFrame frame = Ewd(left: [EngFireLine]);

        Assert.Equal([Interrupting("ENG 1 FIRE, red")], rig.At(1000, List(EngFire), frame));
        rig.Silent(1100, 3000, List(EngFire), frame);

        // A title the E/WD wraps is spoken from both its lines, the parts as Find gave them.
        var wrapped = new FenixEcamEntry("01-27-  1", "F/CTL-FLAPS NOT IN T.O CONFIG", "CONFIG FLAPS NOT IN T.O CONFIG", true);
        Rig wrap = Baselined(List(), Ewd());
        Assert.Equal([Interrupting("CONFIG FLAPS NOT IN T.O CONFIG, red")],
            wrap.At(1000, List(wrapped), Ewd(left: ["urCONFIGu ", "rFLAPS NOT IN T.O CONFIG"])));
    }

    [Fact]
    public void A_new_caution_is_queued_with_its_color()
    {
        Rig rig = Baselined(List(), Ewd());

        // A key that comes twice in one list counts once.
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber")],
            rig.At(1000, List(Gen1Fault, Gen1Fault), Ewd(left: [Gen1Line])));
        rig.Silent(1100, 2000, List(Gen1Fault), Ewd(left: [Gen1Line]));
    }

    [Fact]
    public void A_new_title_waits_for_its_line()
    {
        // Its line 400 ms later: spoken then, with its color, and nothing reported.
        Rig found = Baselined(List(), Ewd());
        found.Silent(1000, 1300, List(Gen1Fault), Ewd());
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber")], found.At(1400, List(Gen1Fault), Ewd(left: [Gen1Line])));
        Assert.Empty(found.Reports);

        // No line by the wait: spoken without one, reported once, and not spoken again when its line appears.
        Rig none = Baselined(List(), Ewd());
        none.Silent(1000, 1400, List(Gen1Fault), Ewd());
        Assert.Empty(none.At(1000 + FenixChannelEwdAlertAnnouncer.LineWaitMs - 1, List(Gen1Fault), Ewd()));
        Assert.Equal([Queued("ELEC GEN 1 FAULT")], none.At(1000 + FenixChannelEwdAlertAnnouncer.LineWaitMs, List(Gen1Fault), Ewd()));
        none.Silent(1600, 3000, List(Gen1Fault), Ewd());
        none.Silent(3100, 4000, List(Gen1Fault), Ewd(left: [Gen1Line]));
        Assert.Equal(["no line on the E/WD for ELEC GEN 1 FAULT; spoken without a color"], none.Reports);
    }

    [Fact]
    public void A_title_that_goes_while_waiting_says_nothing()
    {
        Rig rig = Baselined(List(), Ewd());

        rig.Silent(1000, 1200, List(Gen1Fault), Ewd());
        rig.Silent(1300, 8000, List(), Ewd());
        Assert.Empty(rig.Reports);

        // Back later, it is new again and its wait starts over.
        rig.Silent(9000, 9400, List(Gen1Fault), Ewd());
        Assert.Equal([Queued("ELEC GEN 1 FAULT")], rig.At(9500, List(Gen1Fault), Ewd()));
    }

    [Fact]
    public void Only_the_first_red_title_of_a_tick_interrupts()
    {
        FenixChannelFrame frame = Ewd(left: [EngFireLine, Eng2FireLine, Gen1Line]);
        FenixEcamEntries list = List(Gen1Fault, EngFire, Eng2Fire);

        // Two red and one amber, the amber first in the list: the reds come first, and only the first of them interrupts.
        Rig rig = Baselined(List(), Ewd());
        Assert.Equal(
            [Interrupting("ENG 1 FIRE, red"), Queued("ENG 2 FIRE, red"), Queued("ELEC GEN 1 FAULT, amber")],
            rig.At(1000, list, frame));

        // Warnings muted: the amber does not take the interrupt.
        Rig muted = Baselined(List(), Ewd(), FenixChannelAnnouncementRows.WarningsKey);
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber")], muted.At(1000, list, frame));

        // A red the speech refuses (an unknown letter) is not said, is reported, and does not take the interrupt either.
        Rig refused = Baselined(List(), Ewd());
        Assert.Equal(
            [Interrupting("ENG 2 FIRE, red")],
            refused.At(1000, List(EngFire, Eng2Fire), Ewd(left: ["rENG 1 xFIRE", Eng2FireLine])));
        Assert.Contains("ENG 1 FIRE", Assert.Single(refused.Reports));
    }

    // ---- A failure that goes ----------------------------------------------------------------

    [Fact]
    public void A_cleared_failure_is_said_once()
    {
        Rig rig = Baselined(List(Gen1Fault), Ewd(left: [Gen1Line]));
        const long t = 1000;

        rig.Silent(t, t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs - 100, List(), Ewd());
        Assert.Empty(rig.At(t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs - 1, List(), Ewd()));
        Assert.Equal([Queued("ELEC GEN 1 FAULT cleared")], rig.At(t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs, List(), Ewd()));
        rig.Silent(t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs + 100, t + 20_000, List(), Ewd());
    }

    [Fact]
    public void A_failure_missing_from_one_list_is_not_cleared()
    {
        // During a power transfer ENG 1 FIRE was missing from one list, 3.84 s, with the fire still set and the E/WD trusted.
        Rig rig = Baselined(List(), Ewd());
        FenixChannelFrame frame = Ewd(left: [EngFireLine]);
        Assert.Equal([Interrupting("ENG 1 FIRE, red")], rig.At(100, List(EngFire), frame));

        const long t = 1000;
        rig.Silent(t, t + 3800, List(), frame);
        rig.Silent(t + 3840, t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs + 2000, List(EngFire), frame);
        Assert.Empty(rig.Reports);
    }

    [Fact]
    public void A_failure_gone_for_the_hold_is_cleared_once()
    {
        Rig rig = Baselined(List(), Ewd());
        FenixChannelFrame frame = Ewd(left: [EngFireLine]);
        Assert.Equal([Interrupting("ENG 1 FIRE, red")], rig.At(100, List(EngFire), frame));

        // A list without it at t, another without it at t + 3,840: the second does not restart the hold.
        const long t = 1000;
        Assert.Empty(rig.At(t, List(), frame));
        Assert.Empty(rig.At(t + 3840, List(), frame));
        Assert.Empty(rig.At(t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs - 1, List(), frame));
        Assert.Equal([Queued("ENG 1 FIRE cleared")], rig.At(t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs, List(), frame));
        rig.Silent(t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs + 100, t + 20_000, List(), frame);

        // Gone, it is new again when it comes back.
        Assert.Equal([Interrupting("ENG 1 FIRE, red")], rig.At(t + 21_000, List(EngFire), frame));

        // A title the speech refuses is not said as cleared either, and goes to the report.
        var refusedEntry = new FenixEcamEntry("01-26- 30", "FIRE - APU FIRE", "APU xFIRE", true);
        Rig refused = Baselined(List(refusedEntry), Ewd());
        refused.Silent(100, 100 + FenixChannelEwdAlertAnnouncer.LeaveHoldMs + 100, List(), Ewd());
        Assert.Contains("APU xFIRE", Assert.Single(refused.Reports));
    }

    [Fact]
    public void A_cleared_line_follows_the_latest_color()
    {
        var entry = new FenixEcamEntry("01-29-  1", "HYD-G SYS LO PR", "HYD G SYS LO PR", true);
        long hold = FenixChannelEwdAlertAnnouncer.LeaveHoldMs;

        // A line turning from red to amber clears under cautions: silent with cautions muted, said with warnings muted.
        foreach ((string muted, bool said) in new[]
                 {
                     (FenixChannelAnnouncementRows.CautionsKey, false),
                     (FenixChannelAnnouncementRows.WarningsKey, true)
                 })
        {
            Rig rig = Baselined(List(), Ewd(), muted);
            rig.At(1000, List(entry), Ewd(left: ["rHYD G SYS LO PR"]));
            rig.Silent(1100, 2000, List(entry), Ewd(left: ["aHYD G SYS LO PR"]));

            // The line going keeps the color it had.
            rig.Silent(2100, 3000, List(entry), Ewd());
            Assert.Empty(rig.At(3100, List(), Ewd()));
            Assert.Equal(said ? new[] { Queued("HYD G SYS LO PR cleared") } : Array.Empty<FenixEwdAlertLine>(),
                rig.At(3100 + hold, List(), Ewd()));
        }

        // Its line is still looked up while the item is held: the color it turns to then is the one that clears it.
        Rig held = Baselined(List(), Ewd(), FenixChannelAnnouncementRows.WarningsKey);
        held.At(1000, List(entry), Ewd(left: ["rHYD G SYS LO PR"]));
        Assert.Empty(held.At(2000, List(), Ewd(left: ["rHYD G SYS LO PR"])));
        Assert.Empty(held.At(2100, List(), Ewd(left: ["aHYD G SYS LO PR"])));
        Assert.Equal([Queued("HYD G SYS LO PR cleared")], held.At(2000 + hold, List(), Ewd()));

        // The baseline looks the line up too: a failure taken as said clears under its own color.
        Rig baseline = Baselined(List(entry), Ewd(left: ["rHYD G SYS LO PR"]), FenixChannelAnnouncementRows.WarningsKey);
        Assert.Empty(baseline.At(1000, List(), Ewd()));
        Assert.Empty(baseline.At(1000 + hold, List(), Ewd()));
    }

    [Fact]
    public void A_color_change_is_never_spoken()
    {
        var entry = new FenixEcamEntry("01-29-  1", "HYD-G SYS LO PR", "HYD G SYS LO PR", true);
        Rig rig = Baselined(List(), Ewd());

        Assert.Equal([Interrupting("HYD G SYS LO PR, red")], rig.At(1000, List(entry), Ewd(left: ["rHYD G SYS LO PR"])));
        rig.Silent(1100, 2000, List(entry), Ewd(left: ["aHYD G SYS LO PR"]));
        rig.Silent(2100, 3000, List(entry), Ewd(left: ["rHYD G SYS LO PR"]));
    }

    // ---- What is no item --------------------------------------------------------------------

    [Fact]
    public void Status_items_and_action_lines_are_never_said()
    {
        // A status item beside the GEN 1 fault.
        Rig rig = Baselined(List(), Ewd());
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber")],
            rig.At(1000, List(Gen1Status, Gen1Fault), Ewd(left: [Gen1Line], right: ["wGEN 1 INOP"])));
        rig.Silent(1100, 3000, List(Gen1Status, Gen1Fault), Ewd(left: [Gen1Line], right: ["wGEN 1 INOP"]));
        Assert.Empty(rig.Reports);

        // The fire drill's lines beside the fire entry alone.
        FenixChannelFrame drill = Ewd(
            left: [EngFireLine, "c -GEN 1......OFF THEN ON", "w   .IF UNSUCCESSFUL:", "w -AGENT1 AFTER 4S.DISCH"],
            right: ["rLAND ASAP"]);
        Rig fire = Baselined(List(), Ewd());
        Assert.Equal([Interrupting("ENG 1 FIRE, red")], fire.At(1000, List(EngFire), drill));
        fire.Silent(1100, 3000, List(EngFire), drill);
        Assert.Empty(fire.Reports);
    }

    [Fact]
    public void Alert_entries_with_no_title_are_reported_not_said()
    {
        Rig rig = Baselined(List(), Ewd());
        FenixEcamEntries message = List(new FenixEcamEntry("01-22-310", "AUTO FLIGHT-ALTITUDE ALERT", "", true));

        rig.Silent(1000, 2000, message, Ewd());

        // Classified once per message, however many steps carry it.
        Assert.Equal(["alert entry with no title: 01-22-310 AUTO FLIGHT-ALTITUDE ALERT"], rig.Reports);
    }

    [Fact]
    public void Entries_of_no_known_kind_are_reported_not_said()
    {
        Rig rig = Baselined(List(), Ewd());

        // Not a memo, for "MEMOS" has no dash, and no alert, so its Title does not matter.
        FenixEcamEntries message = List(
            new FenixEcamEntry("01-99-  1", "OTHER-THING", "SOME THING", false),
            new FenixEcamEntry("01-99-  2", "MEMOS", "", false));

        rig.Silent(1000, 2000, message, Ewd(left: ["wSOME THING"]));
        Assert.Equal(["entry of no known kind: 01-99-  1 OTHER-THING", "entry of no known kind: 01-99-  2 MEMOS"], rig.Reports);
    }

    [Fact]
    public void A_heartbeat_without_entries_changes_nothing()
    {
        Rig rig = Baselined(List(Gen1Fault), Ewd(left: [Gen1Line]));

        // Past the hold on heartbeats: the list stands, so nothing cleared.
        rig.Silent(1000, 8000, Heartbeat(), Ewd(left: [Gen1Line]));

        // A message with the wrong shape is not read either: the gate latches on it anyway.
        rig.Silent(8100, 14000, new FenixEcamEntries(true, [], [], false), Ewd(left: [Gen1Line]));
        rig.Silent(14100, 20000, new FenixEcamEntries(false, ["1"], [], false), Ewd(left: [Gen1Line]));
        Assert.Empty(rig.Reports);

        // A heartbeat that does carry entries is a list.
        Assert.Equal([Interrupting("ENG 1 FIRE, red")],
            rig.At(20100, new FenixEcamEntries(false, [], [Gen1Fault, EngFire], true), Ewd(left: [EngFireLine, Gen1Line])));
    }

    // ---- Memos ------------------------------------------------------------------------------

    [Fact]
    public void A_memo_speaks_only_while_its_row_is_on()
    {
        long hold = FenixChannelEwdAlertAnnouncer.LeaveHoldMs;
        FenixChannelFrame frame = Ewd(right: [SeatBeltsLine]);

        // Muted: nothing, and nothing when it goes.
        Rig muted = Baselined(List(), Ewd(), FenixChannelAnnouncementRows.MemosKey);
        muted.Silent(1000, 2000, List(SeatBelts), frame);
        muted.Silent(2100, 2100 + hold + 1000, List(), frame);

        // Unmuted: its words with its color, queued, and nothing when it goes.
        Rig on = Baselined(List(), Ewd());
        Assert.Equal([Queued("SEAT BELTS, green")], on.At(1000, List(SeatBelts), frame));
        on.Silent(1100, 2000, List(SeatBelts), frame);
        on.Silent(2100, 2100 + hold + 1000, List(), Ewd());

        // A memo's color is never used for a row: a red one is under memos, and interrupts nothing.
        Rig red = Baselined(List(), Ewd(), FenixChannelAnnouncementRows.WarningsKey, FenixChannelAnnouncementRows.CautionsKey);
        Assert.Equal([Queued("SEAT BELTS, red")], red.At(1000, List(SeatBelts), Ewd(right: ["rSEAT BELTS"])));
    }

    [Fact]
    public void A_memo_with_a_blank_title_takes_its_name()
    {
        Rig rig = Baselined(List(), Ewd());

        Assert.Equal([Queued("PARK BRK, green")],
            rig.At(1000, List(new FenixEcamEntry("01- 0-300", "MEMO-PARK BRK", "", false)), Ewd(right: ["gPARK BRK"])));

        // With a Title its text and its line are the Title (measured: the LDG memo, whose Title keeps its dots).
        Rig titled = Baselined(List(), Ewd());
        Assert.Equal([Queued("LDG LDG GEAR DN, green")],
            titled.At(1000, List(new FenixEcamEntry("01- 0-180", "MEMO-LDG MEMO", "LDG LDG GEAR......DN", false)),
                Ewd(right: ["guLDGu LDG GEAR DN"])));
    }

    [Fact]
    public void A_memo_with_spaces_round_its_dash_is_a_memo()
    {
        Rig rig = Baselined(List(), Ewd());

        Assert.Equal([Queued("COMPANY MSG, green")],
            rig.At(1000, List(new FenixEcamEntry("01- 0-500", "MEMO - COMPANY MSG", "", false)), Ewd(right: ["gCOMPANY MSG"])));
        Assert.Empty(rig.Reports);
    }

    [Fact]
    public void A_memo_speaks_the_words_the_ewd_draws()
    {
        var fuel = new FenixEcamEntry("01- 0-260", "MEMO-FUEL < 3 T", "", false);
        var brake = new FenixEcamEntry("01- 0-270", "MEMO-AUTO BRK", "", false);
        long wait = FenixChannelEwdAlertAnnouncer.LineWaitMs;

        // In the other column, and with the E/WD's words rather than the entry's.
        Rig fuelLine = Baselined(List(), Ewd());
        Assert.Equal([Queued("FOB BELOW 3 T, green")], fuelLine.At(1000, List(fuel), Ewd(left: ["gFOB BELOW 3 T"])));

        Rig fuelNone = Baselined(List(), Ewd());
        Assert.Empty(fuelNone.At(1000, List(fuel), Ewd()));
        Assert.Equal([Queued("FOB BELOW 3 T")], fuelNone.At(1000 + wait, List(fuel), Ewd()));
        Assert.Equal(["no line on the E/WD for FOB BELOW 3 T; spoken without a color"], fuelNone.Reports);

        Rig brakeLine = Baselined(List(), Ewd());
        Assert.Equal([Queued("AUTO BRK LO, green")], brakeLine.At(1000, List(brake), Ewd(right: ["gAUTO BRK LO"])));

        Rig brakeNone = Baselined(List(), Ewd());
        Assert.Empty(brakeNone.At(1000, List(brake), Ewd()));
        Assert.Equal([Queued("AUTO BRK")], brakeNone.At(1000 + wait, List(brake), Ewd()));
    }

    [Fact]
    public void A_memo_back_inside_the_hold_is_not_spoken_again()
    {
        Rig rig = Baselined(List(), Ewd());
        FenixChannelFrame frame = Ewd(right: [SeatBeltsLine]);
        Assert.Equal([Queued("SEAT BELTS, green")], rig.At(100, List(SeatBelts), frame));

        const long t = 1000;
        rig.Silent(t, t + 3800, List(), frame);
        rig.Silent(t + 3840, t + FenixChannelEwdAlertAnnouncer.LeaveHoldMs + 2000, List(SeatBelts), frame);
    }

    // ---- LAND ASAP --------------------------------------------------------------------------

    [Fact]
    public void Land_asap_is_heard_by_its_color()
    {
        // Its name is a memo's, but it is a failure by the owner's ruling: heard with memos muted.
        Rig red = Baselined(List(), Ewd(), FenixChannelAnnouncementRows.MemosKey);
        Assert.Equal([Interrupting("LAND ASAP, red")], red.At(1000, List(LandAsapRed), Ewd(right: ["rLAND ASAP"])));

        Rig amber = Baselined(List(), Ewd(), FenixChannelAnnouncementRows.MemosKey);
        Assert.Equal([Queued("LAND ASAP, amber")], amber.At(1000, List(LandAsapRed), Ewd(right: ["aLAND ASAP"])));
    }

    [Fact]
    public void Land_asap_follows_a_new_failure()
    {
        // Its entry sorts ahead of the failure that caused it: its Ata is a memo's. It comes after it all the same.
        Rig red = Baselined(List(), Ewd());
        Assert.Equal([Interrupting("ENG 1 FIRE, red"), Queued("LAND ASAP, red")],
            red.At(1000, List(LandAsapRed, EngFire), Ewd(left: [EngFireLine], right: ["rLAND ASAP"])));

        // The same in the amber group.
        Rig amber = Baselined(List(), Ewd());
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber"), Queued("LAND ASAP, amber")],
            amber.At(1000, List(LandAsapAmber, Gen1Fault), Ewd(left: [Gen1Line], right: ["aLAND ASAP"])));
    }

    [Fact]
    public void Land_asap_changing_its_name_is_only_a_color_change()
    {
        long hold = FenixChannelEwdAlertAnnouncer.LeaveHoldMs;

        foreach ((string muted, bool said) in new[]
                 {
                     (FenixChannelAnnouncementRows.WarningsKey, true),
                     (FenixChannelAnnouncementRows.CautionsKey, false)
                 })
        {
            Rig rig = Baselined(List(), Ewd(), muted);
            rig.At(1000, List(LandAsapRed), Ewd(right: ["rLAND ASAP"]));

            // Neither appeared nor cleared, and its one key holds through the new Name.
            rig.Silent(1100, 7000, List(LandAsapAmber), Ewd(right: ["aLAND ASAP"]));

            Assert.Empty(rig.At(7100, List(), Ewd()));
            Assert.Equal(said ? new[] { Queued("LAND ASAP cleared") } : Array.Empty<FenixEwdAlertLine>(),
                rig.At(7100 + hold, List(), Ewd()));
        }
    }

    // ---- Gaps -------------------------------------------------------------------------------

    [Fact]
    public void A_failure_inside_a_trust_gap_is_never_spoken()
    {
        Rig rig = Baselined(List(), Ewd());
        FenixChannelFrame frame = Ewd(left: [EngFireLine, Gen1Line]);

        // One comes and goes while untrusted, and one comes and stays.
        Assert.Empty(rig.At(1000, List(EngFire), frame, trusted: false));
        Assert.Empty(rig.At(1100, List(), frame, trusted: false));
        Assert.Empty(rig.At(1200, List(Gen1Fault), frame, trusted: false));

        rig.Silent(1300, 8000, List(Gen1Fault), frame);

        // The one that came and went is new when it comes back after the gap.
        Assert.Equal([Interrupting("ENG 1 FIRE, red")], rig.At(8100, List(Gen1Fault, EngFire), frame));
    }

    [Fact]
    public void A_reconnect_takes_the_next_reading_silently()
    {
        Rig rig = Baselined(List(), Ewd());
        FenixChannelFrame frame = Ewd(left: [EngFireLine, Gen1Line]);

        // Trusted on both steps, a new failure in the second: the new connection says nothing, and is not the baseline.
        Assert.Empty(rig.At(1000, List(EngFire), frame, connection: 2));

        // The next trusted step is.
        rig.Silent(1100, 2000, List(EngFire), frame, connection: 2);

        // A new one after that is spoken.
        Assert.Equal([Queued("ELEC GEN 1 FAULT, amber")], rig.At(2100, List(EngFire, Gen1Fault), frame, connection: 2));

        // What was said and held before a reconnect is forgotten: nothing clears after it.
        Assert.Empty(rig.At(3000, List(), frame, connection: 2));
        Assert.Empty(rig.At(3100, List(), frame, connection: 3));
        rig.Silent(3200, 10_000, List(), frame, connection: 3);
    }

    // ---- The mute ---------------------------------------------------------------------------

    [Fact]
    public void Unmuting_replays_nothing()
    {
        Rig rig = Baselined(List(), Ewd(), FenixChannelAnnouncementRows.CautionsKey);
        FenixChannelFrame frame = Ewd(left: [Gen1Line]);

        rig.Silent(1000, 2000, List(Gen1Fault), frame);

        rig.Muted.Clear();
        rig.Silent(2100, 3000, List(Gen1Fault), frame);

        Assert.Empty(rig.At(3100, List(), Ewd()));
        Assert.Equal([Queued("ELEC GEN 1 FAULT cleared")], rig.At(3100 + FenixChannelEwdAlertAnnouncer.LeaveHoldMs, List(), Ewd()));
    }

    [Fact]
    public void A_failure_with_no_color_is_heard_unless_both_rows_are_muted()
    {
        long wait = FenixChannelEwdAlertAnnouncer.LineWaitMs;
        long hold = FenixChannelEwdAlertAnnouncer.LeaveHoldMs;
        string warnings = FenixChannelAnnouncementRows.WarningsKey;
        string cautions = FenixChannelAnnouncementRows.CautionsKey;

        foreach (string[] muted in new[] { Array.Empty<string>(), [warnings], [cautions], [FenixChannelAnnouncementRows.MemosKey] })
        {
            // No line: heard, with no color, and heard when it clears.
            Rig none = Baselined(List(), Ewd(), muted);
            Assert.Empty(none.At(1000, List(Gen1Fault), Ewd()));
            Assert.Equal([Queued("ELEC GEN 1 FAULT")], none.At(1000 + wait, List(Gen1Fault), Ewd()));
            Assert.Empty(none.At(2000, List(), Ewd()));
            Assert.Equal([Queued("ELEC GEN 1 FAULT cleared")], none.At(2000 + hold, List(), Ewd()));

            // Another color is the same.
            Rig cyan = Baselined(List(), Ewd(), muted);
            Assert.Equal([Queued("ELEC GEN 1 FAULT, cyan")], cyan.At(1000, List(Gen1Fault), Ewd(left: ["cELEC GEN 1 FAULT"])));
        }

        // Both muted: silent, and silent when it clears.
        Rig both = Baselined(List(), Ewd(), warnings, cautions);
        both.Silent(1000, 2000, List(Gen1Fault), Ewd());
        both.Silent(2100, 2100 + hold + 1000, List(), Ewd());
    }

    // ---- Speaking ---------------------------------------------------------------------------

    [Fact]
    public void Speak_interrupts_only_for_an_interrupting_line()
    {
        var capture = new SpeechCapture();

        FenixChannelEwdAlertAnnouncer.Speak(
            [Interrupting("ENG 1 FIRE, red"), Queued("ENG 2 FIRE, red"), Queued("ELEC GEN 1 FAULT cleared")], capture);

        Assert.Equal(["ENG 1 FIRE, red"], capture.Interrupts);
        Assert.Equal(["ENG 1 FIRE, red", "ENG 2 FIRE, red", "ELEC GEN 1 FAULT cleared"], capture.All);
    }

    [Fact]
    public void Speech_is_the_same_in_every_culture()
    {
        // CI runs under en-US: a line built with the PC's culture would read differently here.
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            InCulture(culture, () =>
            {
                Rig rig = Baselined(List(), Ewd());
                FenixChannelFrame frame = Ewd(left: [EngFireLine, Gen1Line], right: [SeatBeltsLine, "rLAND ASAP"]);

                Assert.Equal(
                    [
                        Interrupting("ENG 1 FIRE, red"), Queued("LAND ASAP, red"), Queued("ELEC GEN 1 FAULT, amber"),
                        Queued("SEAT BELTS, green")
                    ],
                    rig.At(1000, List(LandAsapRed, SeatBelts, EngFire, Gen1Fault), frame));

                Assert.Empty(rig.At(2000, List(), frame));
                Assert.Equal([Queued("ENG 1 FIRE cleared"), Queued("ELEC GEN 1 FAULT cleared"), Queued("LAND ASAP cleared")],
                    rig.At(2000 + FenixChannelEwdAlertAnnouncer.LeaveHoldMs, List(), frame));
            });
        }
    }
}
