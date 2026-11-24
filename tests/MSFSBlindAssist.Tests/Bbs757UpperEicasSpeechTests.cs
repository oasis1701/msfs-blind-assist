// What the pilot hears when the 757's upper EICAS changes, and the rows the Alt+E window lists: BlueBird's text with
// its leading space dropped; new messages one line per level, warnings first; a message going "EICAS gone"; CANC read
// back as a count and RECALL as what came back, never as gone or new; the thrust reference and the N1 and EGT colors
// on rows of their own; nothing else. Every test runs under de-DE and sv-SE as well: CI runs under en-US, so a culture
// leak would pass unnoticed otherwise.

using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757UpperEicasSpeechTests
{
    // ---- readings ----

    internal static Bbs757EicasMessage Message(int id, string text, string level, bool cancelled = false, bool? shown = null) =>
        new(id, text, level, cancelled, shown ?? !cancelled);

    internal static Bbs757EicasMessage Warning(int id, string text) => Message(id, text, "warning");

    internal static Bbs757EicasMessage Caution(int id, string text) => Message(id, text, "caution");

    internal static Bbs757EicasMessage Advisory(int id, string text, bool? shown = null) => Message(id, text, "advisory", shown: shown);

    internal static Bbs757EicasMessage Communication(int id, string text) => Message(id, text, "communication");

    /// <summary>The message as CANC leaves it: in the list, marked, not shown.</summary>
    internal static Bbs757EicasMessage Cancelled(Bbs757EicasMessage m) => m with { Cancelled = true, Shown = false };

    // The fire test's messages, as BlueBird lists them (warnings and cautions unindented, advisories one space in).
    internal static readonly Bbs757EicasMessage FireL = Warning(1, "L ENGINE FIRE"), FireR = Warning(2, "R ENGINE FIRE"),
        OvhtL = Caution(30, "L ENG OVHT"), OvhtR = Caution(31, "R ENG OVHT"), FireSys = Advisory(120, " FIRE/OVHT SYS");

    internal static readonly Bbs757EicasMessage ShutdownL = Caution(40, "L ENG SHUTDOWN"), ShutdownR = Caution(41, "R ENG SHUTDOWN"),
        HydPress = Caution(50, "HYD SYS PRESS");

    // The second capture's advisories, and a communication message.
    internal static readonly Bbs757EicasMessage BleedL = Advisory(141, " L ENG BLEED OFF"), BleedR = Advisory(199, " R ENG BLEED OFF"),
        PackR = Advisory(222, " R PACK OFF"), PackL = Advisory(164, " L PACK OFF"), ParkingBrake = Advisory(160, " PARKING BRAKE"),
        Cabin = Communication(258, " CABIN ALERT");

    internal static Bbs757EicasValue V(string label, string? left, string? right = null, string? leftColor = null, string? rightColor = null) =>
        new(label, left, right, leftColor, rightColor);

    /// <summary>The live screen's gauges at idle, in the reading's order (TAT before the EPR reference).</summary>
    internal static readonly IReadOnlyList<Bbs757EicasValue> Idle = new[]
    {
        V("EPR", "1.03", "1.03"), V("N1", "19.9", "19.9"), V("EGT", "358", "360"), V("TAT", "+11c"), V("EPR reference", "1.71", "1.71"),
    };

    /// <summary>A lit, good reading: the idle gauges, TO, and <paramref name="messages"/> in screen order.</summary>
    internal static Bbs757UpperEicasReading Eicas(IReadOnlyList<Bbs757EicasMessage>? messages = null, string? thrust = "TO",
        double recallTime = 4023.89, bool powered = true)
    {
        var list = messages ?? Array.Empty<Bbs757EicasMessage>();
        return new Bbs757UpperEicasReading(list, Page: 1, PageShown: false, PageLine: null, Recall: false, RecallLine: null, RecallTime: recallTime,
            Cancelled: list.Count(m => m.Cancelled), Values: Idle, ThrustReference: thrust, Envelope: Array.Empty<string>(),
            Powered: powered, Corrupt: false);
    }

    /// <summary><paramref name="r"/> with its row of <paramref name="row"/>'s label replaced, or added after the others.</summary>
    internal static Bbs757UpperEicasReading Gauge(Bbs757UpperEicasReading r, Bbs757EicasValue row) => r with
    {
        Values = r.Values.Any(v => v.Label == row.Label)
            ? r.Values.Select(v => v.Label == row.Label ? row : v).ToArray()
            : r.Values.Append(row).ToArray(),
    };

    private static Bbs757UpperEicasReading Without(Bbs757UpperEicasReading r, string label) =>
        r with { Values = r.Values.Where(v => v.Label != label).ToArray() };

    // ---- lines ----

    private static Bbs757SpokenLine Msg(string text) => new(Bbs757UpperEicasDisplay.MessagesKey, text);

    private static Bbs757SpokenLine Thrust(string text) => new(Bbs757UpperEicasDisplay.ThrustKey, text);

    private static Bbs757SpokenLine Colors(string text) => new(Bbs757UpperEicasDisplay.ColorsKey, text);

    private static Bbs757SpokenLine[] Said(string culture, Bbs757UpperEicasReading heard, Bbs757UpperEicasReading now) =>
        TestCulture.Run(culture, () => Bbs757UpperEicasSpeech.Changes(heard, now).ToArray());

    private static string[] Rows(string culture, Bbs757UpperEicasReading r) =>
        TestCulture.Run(culture, () => Bbs757UpperEicasSpeech.Rows(r).ToArray());

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void New_messages_of_one_level_share_a_line(string culture)
    {
        // Both engines shut down and the packs off: one line per level, each in screen order, the indent dropped.
        var before = Eicas(new[] { ParkingBrake });
        var after = Eicas(new[] { ShutdownL, ShutdownR, PackR, PackL, ParkingBrake });
        Assert.Equal(new[] { Msg("EICAS caution: L ENG SHUTDOWN, R ENG SHUTDOWN"), Msg("EICAS: R PACK OFF, L PACK OFF") },
            Said(culture, before, after));
        // One new message is a line of its own.
        Assert.Equal(new[] { Msg("EICAS: PARKING BRAKE") }, Said(culture, Eicas(), Eicas(new[] { ParkingBrake })));
        // Nothing changed: nothing said.
        Assert.Empty(Said(culture, after, after));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Warnings_are_said_before_cautions_before_the_rest(string culture)
    {
        // The engine fire test: warning, caution, advisory, then a communication message on its own line (a level of
        // its own).
        var before = Eicas(new[] { ParkingBrake });
        var after = Eicas(new[] { FireL, FireR, OvhtL, OvhtR, FireSys, ParkingBrake, Cabin });
        Assert.Equal(new[]
        {
            Msg("EICAS warning: L ENGINE FIRE, R ENGINE FIRE"), Msg("EICAS caution: L ENG OVHT, R ENG OVHT"),
            Msg("EICAS: FIRE/OVHT SYS"), Msg("EICAS: CABIN ALERT"),
        }, Said(culture, before, after));
        // The order is the levels', whatever the list's.
        Assert.Equal(new[] { Msg("EICAS warning: R ENGINE FIRE"), Msg("EICAS caution: R ENG OVHT"), Msg("EICAS: FIRE/OVHT SYS"), Msg("EICAS: CABIN ALERT") },
            Said(culture, Eicas(), Eicas(new[] { Cabin, FireSys, OvhtR, FireR })));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_message_going_is_EICAS_gone(string culture)
    {
        // One line whatever the levels, in the order the pilot last heard them.
        var before = Eicas(new[] { OvhtL, BleedL, ParkingBrake, Cabin });
        Assert.Equal(new[] { Msg("EICAS gone: L ENG OVHT, L ENG BLEED OFF, CABIN ALERT") },
            Said(culture, before, Eicas(new[] { ParkingBrake })));
        // New lines come first.
        Assert.Equal(new[] { Msg("EICAS: L PACK OFF"), Msg("EICAS gone: L ENG BLEED OFF") },
            Said(culture, Eicas(new[] { BleedL }), Eicas(new[] { PackL })));
        // A message pushed past the first page is still in the list: not gone.
        Assert.Empty(Said(culture, Eicas(new[] { BleedL }), Eicas(new[] { BleedL with { Shown = false } })));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_cancelled_message_leaving_says_nothing(string culture)
    {
        // It was not on the screen.
        var before = Eicas(new[] { Cancelled(ShutdownL), Cancelled(BleedL), ParkingBrake });
        Assert.Empty(Said(culture, before, Eicas(new[] { ParkingBrake })));
        Assert.Equal(new[] { Msg("EICAS gone: PARKING BRAKE") }, Said(culture, before, Eicas()));
    }

    // Review Focus: CANC marks the cautions and advisories and hides them. The pilot hears how many, once; none is
    // "gone", and none is when it later leaves the list.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Cancel_hides_without_gone_and_says_the_count_once(string culture)
    {
        var before = Eicas(new[] { FireL, ShutdownL, BleedL, ParkingBrake });
        var cancelled = Eicas(new[] { FireL, Cancelled(ShutdownL), Cancelled(BleedL), Cancelled(ParkingBrake) });
        Assert.Equal(new[] { Msg("EICAS: 3 messages cancelled") }, Said(culture, before, cancelled));
        Assert.Empty(Said(culture, cancelled, cancelled));
        Assert.Empty(Said(culture, cancelled, Eicas(new[] { FireL, Cancelled(ShutdownL) })));

        Assert.Equal(new[] { Msg("EICAS: 1 message cancelled") },
            Said(culture, Eicas(new[] { FireL, BleedL }), Eicas(new[] { FireL, Cancelled(BleedL) })));
    }

    // The owner's ruling after the 2026-11-10 flight: the CDU window already speaks the FMC's scratchpad message ("CDU
    // Messages While Closed"), so the EICAS advisory that says one is waiting is never said as new or gone. The window
    // lists it, and CANC's count and RECALL's read-back still include it: they read back what the press did.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Fmc_message_is_never_said_as_new_or_gone(string culture)
    {
        var fmc = Advisory(150, " FMC MESSAGE");
        Assert.Empty(Said(culture, Eicas(), Eicas(new[] { fmc })));
        Assert.Empty(Said(culture, Eicas(new[] { fmc }), Eicas()));
        // Posted or gone with other messages: only theirs are said.
        Assert.Equal(new[] { Msg("EICAS: L PACK OFF") }, Said(culture, Eicas(), Eicas(new[] { PackL, fmc })));
        Assert.Equal(new[] { Msg("EICAS gone: L PACK OFF") }, Said(culture, Eicas(new[] { PackL, fmc }), Eicas()));
        // Only a message reading exactly FMC MESSAGE: the FMC's own fault advisory is still said.
        Assert.Equal(new[] { Msg("EICAS: FMC") }, Said(culture, Eicas(), Eicas(new[] { Advisory(151, " FMC") })));
        // Listed, counted by CANC and read back by RECALL.
        Assert.Contains("Advisory: FMC MESSAGE", Rows(culture, Eicas(new[] { fmc })));
        Assert.Equal(new[] { Msg("EICAS: 1 message cancelled") },
            Said(culture, Eicas(new[] { fmc }), Eicas(new[] { Cancelled(fmc) })));
        Assert.Equal(new[] { Msg("EICAS recall: FMC MESSAGE") },
            Said(culture, Eicas(new[] { Cancelled(fmc) }, recallTime: 1000), Eicas(new[] { fmc }, recallTime: 1975.767)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Recall_says_what_came_back(string culture)
    {
        // RECALL (its time changed) clears the marks: the messages that came back, in screen order, never also as new.
        var cancelled = Eicas(new[] { FireL, Cancelled(ShutdownL), Cancelled(BleedL), Cancelled(ParkingBrake) }, recallTime: 1000);
        var recalled = Eicas(new[] { FireL, ShutdownL, ParkingBrake, BleedL }, recallTime: 1975.767);
        Assert.Equal(new[] { Msg("EICAS recall: L ENG SHUTDOWN, PARKING BRAKE, L ENG BLEED OFF") },
            Said(culture, cancelled, recalled));
        // A message posted by the same press is new, said first.
        Assert.Equal(new[] { Msg("EICAS caution: HYD SYS PRESS"), Msg("EICAS recall: L ENG BLEED OFF") },
            Said(culture, Eicas(new[] { Cancelled(BleedL) }, recallTime: 1000), Eicas(new[] { HydPress, BleedL }, recallTime: 1975.767)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Recall_with_nothing_cancelled_says_so(string culture)
    {
        var before = Eicas(new[] { FireL, ParkingBrake }, recallTime: 1000);
        Assert.Equal(new[] { Msg("EICAS recall: no cancelled messages") },
            Said(culture, before, before with { RecallTime = 1975.767 }));
        // A time that is not a number is no press.
        Assert.Empty(Said(culture, before, before with { RecallTime = double.NaN }));
        Assert.Empty(Said(culture, before with { RecallTime = double.PositiveInfinity }, before));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_cancelled_message_back_without_a_recall_press_is_new(string culture)
    {
        // Its condition cleared and came back (BlueBird posted it again): said as new.
        Assert.Equal(new[] { Msg("EICAS: L ENG BLEED OFF") },
            Said(culture, Eicas(new[] { Cancelled(BleedL) }, recallTime: 1000), Eicas(new[] { BleedL }, recallTime: 1000)));
    }

    // Review Focus: the page shows ten lines; a message past them is still on the EICAS, so it is spoken and listed.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_message_beyond_the_first_page_is_listed_and_spoken(string culture)
    {
        var page = new[]
        {
            BleedL, BleedR, PackR, PackL, ParkingBrake, Advisory(170, " L HYD QTY"), Advisory(171, " R HYD QTY"),
            Advisory(172, " APU DOOR"), Advisory(173, " L FWD ENTRY"), Advisory(174, " R FWD ENTRY"),
        };
        var beyond = Advisory(175, " AFT CARGO DOOR", shown: false);
        var after = Eicas(page.Append(beyond).ToArray());
        Assert.Equal(new[] { Msg("EICAS: AFT CARGO DOOR") }, Said(culture, Eicas(page), after));
        Assert.Contains("Advisory: AFT CARGO DOOR", Rows(culture, after));

        // A caution pushes the last advisory past the page: only the caution is said.
        var pushed = Eicas(page.Take(9).Append(page[9] with { Shown = false }).Prepend(HydPress).ToArray());
        Assert.Equal(new[] { Msg("EICAS caution: HYD SYS PRESS") }, Said(culture, Eicas(page), pushed));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_thrust_reference_change_is_said_once(string culture)
    {
        Assert.Equal(new[] { Thrust("Thrust reference: CLB") }, Said(culture, Eicas(thrust: "TO"), Eicas(thrust: "CLB")));
        Assert.Empty(Said(culture, Eicas(thrust: "CLB"), Eicas(thrust: "CLB")));
        // Nothing drawn is "none"; the words as drawn.
        Assert.Equal(new[] { Thrust("Thrust reference: none") }, Said(culture, Eicas(thrust: "CLB"), Eicas(thrust: null)));
        Assert.Equal(new[] { Thrust("Thrust reference: D-TO 1, +25c") }, Said(culture, Eicas(thrust: null), Eicas(thrust: "D-TO 1, +25c")));
        Assert.Equal(new[] { Thrust("Thrust reference: MAN") }, Said(culture, Eicas(thrust: "CRZ 2"), Eicas(thrust: "MAN")));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Engine_gauge_colors_are_one_line_in_engine_order(string culture)
    {
        var normal = Eicas();
        var hot = Gauge(Gauge(normal, V("N1", "108.6", "108.5", "amber", "amber")), V("EGT", "851", "812", "red", "amber"));
        Assert.Equal(new[] { Colors("EICAS: L N1 amber, R N1 amber, L EGT red, R EGT amber") }, Said(culture, normal, hot));
        // Back to no color is "normal".
        Assert.Equal(new[] { Colors("EICAS: L N1 normal, R N1 normal, L EGT normal, R EGT normal") }, Said(culture, hot, normal));
        // One side changing says that side.
        var egtAmber = Gauge(normal, V("EGT", "800", "360", "amber"));
        Assert.Equal(new[] { Colors("EICAS: L EGT amber") }, Said(culture, normal, egtAmber));
        Assert.Equal(new[] { Colors("EICAS: L EGT red") }, Said(culture, egtAmber, Gauge(normal, V("EGT", "851", "360", "red"))));
        // The value changing with its color unchanged says nothing.
        Assert.Empty(Said(culture, egtAmber, Gauge(normal, V("EGT", "805", "360", "amber"))));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_value_going_blank_says_no_color(string culture)
    {
        var amber = Gauge(Eicas(), V("EGT", "800", "360", "amber"));
        var blank = Gauge(Eicas(), V("EGT", null, "360"));
        Assert.Empty(Said(culture, amber, blank));
        var n1Red = Gauge(Eicas(), V("N1", "109.0", "98.0", "red"));
        Assert.Empty(Said(culture, n1Red, Gauge(Eicas(), V("N1", null, "98.0"))));
        // A row missing from a reading says nothing either.
        Assert.Empty(Said(culture, amber, Without(amber, "EGT")));
        Assert.Empty(Said(culture, Without(amber, "EGT"), amber));
    }

    // A sighted pilot sees a value come back amber or red, so its color is said; one coming back with no color (a
    // PW engine's readouts at its start) says nothing.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_value_coming_back_from_blank_says_only_a_color(string culture)
    {
        var blank = Gauge(Eicas(), V("EGT", null, "360"));
        Assert.Equal(new[] { Colors("EICAS: L EGT red") }, Said(culture, blank, Gauge(Eicas(), V("EGT", "851", "360", "red"))));
        Assert.Equal(new[] { Colors("EICAS: L EGT amber") }, Said(culture, blank, Gauge(Eicas(), V("EGT", "800", "360", "amber"))));
        Assert.Empty(Said(culture, blank, Gauge(Eicas(), V("EGT", "702", "360"))));

        // Amber, blank for a tick, then back red: the red is said (the monitor's baseline moved to the blank reading).
        var amber = Gauge(Eicas(), V("EGT", "800", "360", "amber"));
        var red = Gauge(Eicas(), V("EGT", "851", "360", "red"));
        Assert.Empty(Said(culture, amber, blank));
        Assert.Equal(new[] { Colors("EICAS: L EGT red") }, Said(culture, blank, red));

        // In the one colors line and its order, beside a color change on a value drawn throughout.
        var bothBlank = Gauge(Gauge(Eicas(), V("N1", "19.9", "19.9")), V("EGT", null, null));
        var back = Gauge(Gauge(Eicas(), V("N1", "19.9", "108.5", null, "amber")), V("EGT", "851", "812", "red", "amber"));
        Assert.Equal(new[] { Colors("EICAS: R N1 amber, L EGT red, R EGT amber") }, Said(culture, bothBlank, back));
        // The N1 coming back red on one side, white on the other.
        var n1Blank = Gauge(Eicas(), V("N1", null, null));
        Assert.Equal(new[] { Colors("EICAS: R N1 red") }, Said(culture, n1Blank, Gauge(Eicas(), V("N1", "26.3", "109.0", null, "red"))));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Nothing_but_messages_the_thrust_reference_and_gauge_colors_is_spoken(string culture)
    {
        // S8: no gauge value, TAT, TAI, REV (whatever its color), envelope, page or RECALL word.
        var r = Eicas(new[] { BleedL, ParkingBrake });
        var quiet = new[]
        {
            Gauge(r, V("EPR", "1.45", "1.44")), Gauge(r, V("N1", "88.2", "88.1")), Gauge(r, V("EGT", "702", "698")),
            Gauge(r, V("EPR", "1.03", "1.03", "amber")), Gauge(r, V("TAT", "+12c")), Gauge(r, V("TAI", "TAI", null)),
            Gauge(r, V("REV", "REV", "REV", "amber", "amber")), Gauge(r, V("EPR reference", "1.65", "1.65")),
            Gauge(r, V("N1 max", "109.2", null)), Gauge(r, V("EGT max", "870", null)), Without(r, "EPR reference"),
            r with { Envelope = new[] { "IN-FLT START", "FL 250 260-360 KTS" } }, r with { Page = 2, PageShown = true, PageLine = "PAGE 2" },
            r with { Recall = true, RecallLine = "RECALL" },
            Eicas(new[] { BleedL with { Shown = false }, ParkingBrake }),
        };
        foreach (var now in quiet) Assert.Empty(Said(culture, r, now));
        var rev = Gauge(r, V("REV", "REV", "REV", "amber", "amber"));
        Assert.Empty(Said(culture, rev, Gauge(r, V("REV", "REV", "REV", "green", "green"))));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void The_lines_come_in_the_rules_order_each_on_its_row(string culture)
    {
        var heard = Eicas(new[] { ShutdownL, Cancelled(BleedL), ParkingBrake }, thrust: "TO", recallTime: 1000);
        var now = Gauge(Eicas(new[] { FireL, Cancelled(ShutdownL), BleedL }, thrust: "CLB", recallTime: 1975.767),
            V("EGT", "800", "360", "amber"));
        Assert.Equal(new[]
        {
            Msg("EICAS warning: L ENGINE FIRE"), Msg("EICAS gone: PARKING BRAKE"), Msg("EICAS: 1 message cancelled"),
            Msg("EICAS recall: L ENG BLEED OFF"), Thrust("Thrust reference: CLB"), Colors("EICAS: L EGT amber"),
        }, Said(culture, heard, now));
    }

    // ---- the window's rows ----

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Rows_list_the_gauges_the_thrust_reference_tat_and_the_messages(string culture)
    {
        var r = Eicas(new[] { ShutdownL, BleedL, ParkingBrake, Cabin });
        var rows = new[]
        {
            "EPR: 1.03, 1.03", "N1: 19.9, 19.9", "EGT: 358, 360", "Thrust reference: TO", "EPR reference: 1.71, 1.71",
            "TAT: +11c", "Caution: L ENG SHUTDOWN", "Advisory: L ENG BLEED OFF", "Advisory: PARKING BRAKE",
            "Communication: CABIN ALERT",
        };
        Assert.Equal(rows, Rows(culture, r));
        Assert.Equal("Warning: L ENGINE FIRE", Assert.Single(Rows(culture, Eicas(new[] { FireL })), row => row.StartsWith("Warning", StringComparison.Ordinal)));
        // No message: one row says so.
        Assert.Equal("Messages: none", Rows(culture, Eicas())[^1]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void A_blank_value_reads_blank_and_a_color_follows_its_value(string culture)
    {
        Assert.Equal("EGT: blank, 360", Rows(culture, Gauge(Eicas(), V("EGT", null, "360")))[2]);
        Assert.Equal("EGT: 800 amber, 360", Rows(culture, Gauge(Eicas(), V("EGT", "800", "360", "amber")))[2]);
        Assert.Equal("N1: 109.0 red, 98.0", Rows(culture, Gauge(Eicas(), V("N1", "109.0", "98.0", "red")))[1]);
        Assert.Equal("N1: 98.0, 108.5 amber", Rows(culture, Gauge(Eicas(), V("N1", "98.0", "108.5", null, "amber")))[1]);
        Assert.Equal("EPR: blank, blank", Rows(culture, Gauge(Eicas(), V("EPR", null, null)))[0]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Tai_rev_and_the_max_rows_are_listed_only_while_drawn(string culture)
    {
        var r = Gauge(Gauge(Gauge(Gauge(Eicas(), V("TAI", "TAI", null)), V("REV", "REV", null, "amber")),
            V("N1 max", "109.2", null)), V("EGT max", "870", null));
        Assert.Equal(new[]
        {
            "EPR: 1.03, 1.03", "N1: 19.9, 19.9", "EGT: 358, 360", "TAI: L", "REV: L amber", "N1 max: 109.2, blank",
            "EGT max: 870, blank", "Thrust reference: TO", "EPR reference: 1.71, 1.71", "TAT: +11c", "Messages: none",
        }, Rows(culture, r));
        Assert.Equal("TAI: R", Rows(culture, Gauge(Eicas(), V("TAI", null, "TAI")))[3]);
        Assert.Equal("TAI: L, R", Rows(culture, Gauge(Eicas(), V("TAI", "TAI", "TAI")))[3]);
        Assert.Equal("REV: L green, R green", Rows(culture, Gauge(Eicas(), V("REV", "REV", "REV", "green", "green")))[3]);
        Assert.Equal("REV: R amber", Rows(culture, Gauge(Eicas(), V("REV", null, "REV", null, "amber")))[3]);
        Assert.Equal("EGT max: blank, 865", Rows(culture, Gauge(Eicas(), V("EGT max", null, "865")))[3]);
        Assert.DoesNotContain(Rows(culture, Eicas()), row =>
            row.StartsWith("TAI", StringComparison.Ordinal) || row.StartsWith("REV", StringComparison.Ordinal) || row.Contains("max"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void The_thrust_reference_row_says_none_when_nothing_is_drawn(string culture)
    {
        var hidden = Without(Eicas(thrust: null), "EPR reference");
        Assert.Equal(new[] { "EPR: 1.03, 1.03", "N1: 19.9, 19.9", "EGT: 358, 360", "Thrust reference: none", "TAT: +11c", "Messages: none" },
            Rows(culture, hidden));
        Assert.Equal(new[] { "Thrust reference: D-TO 1, +25c", "EPR reference: 1.71, blank" },
            Rows(culture, Gauge(Eicas(thrust: "D-TO 1, +25c"), V("EPR reference", "1.71", null)))[3..5]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void The_envelope_the_messages_page_and_recall_close_the_rows(string culture)
    {
        var r = Eicas(new[] { ShutdownL, Cancelled(BleedL), ParkingBrake }) with
        {
            Envelope = new[] { "IN-FLT START", "FL 250 260-360 KTS", "FL 230 255-360 KTS" },
            Page = 2,
            PageShown = true,
            PageLine = "PAGE 2",
            Recall = true,
            RecallLine = "RECALL",
        };
        Assert.Equal(new[]
        {
            "TAT: +11c", "IN-FLT START", "FL 250 260-360 KTS", "FL 230 255-360 KTS", "Caution: L ENG SHUTDOWN",
            "Advisory: L ENG BLEED OFF (cancelled)", "Advisory: PARKING BRAKE", "PAGE 2", "RECALL",
        }, Rows(culture, r)[5..]);
        // PAGE only while BlueBird draws it.
        Assert.DoesNotContain(Rows(culture, r with { PageShown = false, PageLine = null, Recall = false, RecallLine = null }),
            row => row.StartsWith("PAGE", StringComparison.Ordinal) || row == "RECALL");
        // The rows are the lines as the reading carries them, in the layout's words; the window writes none of its own.
        Assert.Equal(new[] { "SEITE 2", "ZURUECK" }, Rows(culture, r with { PageLine = "SEITE 2", RecallLine = "ZURUECK" })[^2..]);
    }

    // ---- numbers, through the reader's own decode ----

    private static readonly Bbs757EicasRawEngine Running = new(Valid: 0, Type: 0, Egt: 812.6, N1: 108.62, N2: 98.1,
        Epr: 1.5512, AlternateFlagged: false, N1Alternate: 0, Combusting: true, MaxN1: 109.2f, MaxEgt: 870.6f, Reverser: 0,
        FireSwitch: false, AntiIce: new Bbs757EicasRawValve(100, 0));

    /// <summary>In flight, the left engine at its N1 and EGT amber limits with its anti-ice on, the right engine out
    /// (the in-flight start block drawn) with its fire warning and overheat caution up, CRZ 2 with a derate, TAT −15,
    /// page 2 just after RECALL. The entries carry the raw levels (4 warning, 3 caution, 2 advisory, 1 communication),
    /// so the words come from the layout's level table.</summary>
    private static Bbs757UpperEicasRaw InFlight() => new(
        Torn: false,
        Main: new[]
        {
            new Bbs757EicasRawEntry(2, "R ENGINE FIRE", 4, false), new Bbs757EicasRawEntry(31, "R ENG OVHT", 3, false),
            new Bbs757EicasRawEntry(141, " L ENG BLEED OFF", 2, true), new Bbs757EicasRawEntry(164, " L PACK OFF", 2, false),
        },
        Communication: new[] { new Bbs757EicasRawEntry(258, " CABIN ALERT", 1, false) },
        Page: 2, RecallTime: 4023.89, Blank: 0, Clock: 4024.14, Powered: true, Failed: false, TimedBlank: false, Compact: false,
        EgtRed: 850, EgtAmber: 795, N1Red: 108.8f, N1Amber: 108.4f,
        Left: Running,
        Right: Running with { Egt = 402.7, N1 = 26.31, Epr = 1.4049, Combusting = false, MaxN1 = 0, MaxEgt = 0, AntiIce = new Bbs757EicasRawValve(0, 2) },
        TmcFailed: false, TmcChannelLeftFailed: false, TmcChannelRightFailed: false, ThrustMode: 7, ThrustNumber: 2, Derate: 25,
        DtoFlagged: false, DerateShown: true, ReferenceEpr: 1.7073, ManualEprLeft: 1.5, ManualEprRight: 1.5, Tat: -15.25,
        OnGround: false, LowerMode: 1,
        EnvelopeRows: new[] { new Bbs757EicasRawEnvelopeRow(250, 260, 360), new Bbs757EicasRawEnvelopeRow(230, 255, 360), default });

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Numbers_format_as_drawn_under_de_DE_and_sv_SE(string culture)
    {
        var layout = Bbs757UpperEicasLayoutTests.Sample();
        var raw = InFlight();
        var now = TestCulture.Run(culture, () => Bbs757UpperEicasReader.Decode(layout, raw, manual: false));
        Assert.False(now.Corrupt);
        Assert.Equal(new[]
        {
            "EPR: 1.55, 1.40", "N1: 108.6 amber, 26.3", "EGT: 812 amber, 402", "TAI: L", "N1 max: 109.2, blank",
            "EGT max: 870, blank", "Thrust reference: CRZ 2, +25c", "EPR reference: 1.71, 1.71", "TAT: -15c", "IN-FLT START",
            "FL 250 260-360 KTS", "FL 230 255-360 KTS", "Warning: R ENGINE FIRE", "Caution: R ENG OVHT",
            "Advisory: L ENG BLEED OFF (cancelled)", "Advisory: L PACK OFF", "Communication: CABIN ALERT", "PAGE 2", "RECALL",
        }, Rows(culture, now));

        // From climb, the left engine below its limits, the two advisories up, no fire and no communication message.
        var heard = TestCulture.Run(culture, () => Bbs757UpperEicasReader.Decode(layout, raw with
        {
            Main = new[] { new Bbs757EicasRawEntry(141, " L ENG BLEED OFF", 2, false), new Bbs757EicasRawEntry(164, " L PACK OFF", 2, false) },
            Communication = Array.Empty<Bbs757EicasRawEntry>(),
            Left = Running with { N1 = 98.04, Egt = 702.3 },
            ThrustMode = 4,
        }, manual: false));
        Assert.Equal(new[]
        {
            Msg("EICAS warning: R ENGINE FIRE"), Msg("EICAS caution: R ENG OVHT"), Msg("EICAS: CABIN ALERT"),
            Msg("EICAS: 1 message cancelled"), Thrust("Thrust reference: CRZ 2, +25c"), Colors("EICAS: L N1 amber, L EGT amber"),
        }, Said(culture, heard, now));
    }
}
