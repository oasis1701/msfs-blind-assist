using System.Globalization;
using System.Text;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 08 Task 1: the CDU window's rows carry an identity, so the cursor stays on the pilot's line.</summary>
public class Bbs757CduRowsTests
{
    private static Bbs757CduScreen Fixture(string name) =>
        Bbs757CduFrame.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Encoding.UTF8));

    /// <summary>The screen with some of its 14 lines replaced (each padded or cut to 24 characters).</summary>
    private static Bbs757CduScreen With(Bbs757CduScreen screen, params (int Line, string Text)[] changes)
    {
        var lines = screen.Lines.ToArray();
        foreach (var (line, text) in changes)
            lines[line] = text.PadRight(Bbs757CduFrame.Columns)[..Bbs757CduFrame.Columns];
        return new Bbs757CduScreen(lines);
    }

    private static Bbs757CduScreen Legs => Fixture("bbs757-cdu-legs.json");

    [Fact]
    public void Kinds_and_lines()
    {
        var rows = Bbs757CduRows.Build(Legs, 1);

        Assert.Equal(Bbs757CduRowKind.Title, rows[0].Kind);
        Assert.Equal(Bbs757CduRowKind.Scratchpad, rows[^1].Kind);
        Assert.Equal("Scratchpad: FLT NUMBER UPLINK", rows[^1].Text);
        for (int line = 1; line <= 6; line++)
            Assert.Single(rows, r => r.Kind == Bbs757CduRowKind.Value && r.Line == line);
        // LEGS shows a label (the course and distance) above lines 1-5; the row of dashes above line 6 is dropped (R71).
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Where(r => r.Kind == Bbs757CduRowKind.Label).Select(r => r.Line));
        Assert.All(rows.Where(r => r.Kind is Bbs757CduRowKind.Title or Bbs757CduRowKind.Scratchpad), r => Assert.Equal(0, r.Line));
        // The texts are what the display lines list.
        Assert.Equal(Bbs757CduFrame.ToDisplayLines(Legs), rows.Select(r => r.Text).ToList());
        Assert.StartsWith("1: SEA", rows.Single(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 1).Text);
    }

    [Fact]
    public void Restore_follows_the_row_when_a_label_above_goes()
    {
        var before = Bbs757CduRows.Build(Legs, 1);
        int onLine4 = before.ToList().FindIndex(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 4);

        // Line 2's label goes blank: every row below it moves up one.
        var after = Bbs757CduRows.Build(With(Legs, (3, "")), 1);
        int restored = Bbs757CduRows.Restore(before, onLine4, after);

        Assert.Equal(onLine4 - 1, restored);
        Assert.Equal(Bbs757CduRowKind.Value, after[restored].Kind);
        Assert.Equal(4, after[restored].Line);
    }

    private static Bbs757CduScreen NavRadio => Fixture("bbs757-cdu-navradio.json");

    [Fact]
    public void Nav_radio_lists_only_the_labels_it_shows()
    {
        // Captured live 2026-10-27 (first officer's CDU): labels on lines 1, 2 and 6 only.
        var rows = Bbs757CduRows.Build(NavRadio, 1);
        Assert.Equal("Title: NAV RADIO", rows[0].Text);
        Assert.Equal(new[] { 1, 2, 6 }, rows.Where(r => r.Kind == Bbs757CduRowKind.Label).Select(r => r.Line));
        Assert.Equal(6, rows.Count(r => r.Kind == Bbs757CduRowKind.Value));
        Assert.Equal("Scratchpad: NAV DATA OUT OF DATE", rows[^1].Text);
        Assert.Equal(Bbs757CduFrame.ToDisplayLines(NavRadio), rows.Select(r => r.Text).ToList());
    }

    [Fact]
    public void Restore_follows_the_row_when_a_label_above_appears()
    {
        // On NAV RADIO's PRESELECT line (6), a label appearing on line 3 pushes every row below it down one.
        var before = Bbs757CduRows.Build(NavRadio, 1);
        int onLine6 = before.ToList().FindIndex(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 6);

        var after = Bbs757CduRows.Build(With(NavRadio, (5, "ADF L              ADF R")), 1);
        int restored = Bbs757CduRows.Restore(before, onLine6, after);

        Assert.Equal(onLine6 + 1, restored);
        Assert.Equal(Bbs757CduRowKind.Value, after[restored].Kind);
        Assert.Equal(6, after[restored].Line);
    }

    [Fact]
    public void Restore_on_a_label_that_goes_lands_on_its_value()
    {
        var before = Bbs757CduRows.Build(Legs, 1);
        int onLabel2 = before.ToList().FindIndex(r => r.Kind == Bbs757CduRowKind.Label && r.Line == 2);

        var after = Bbs757CduRows.Build(With(Legs, (3, "")), 1);
        int restored = Bbs757CduRows.Restore(before, onLabel2, after);

        Assert.Equal(Bbs757CduRowKind.Value, after[restored].Kind);
        Assert.Equal(2, after[restored].Line);
    }

    [Fact]
    public void Restore_keeps_the_title_and_the_scratchpad()
    {
        var before = Bbs757CduRows.Build(Legs, 1);
        var after = Bbs757CduRows.Build(With(Legs, (3, ""), (5, "")), 1);

        Assert.Equal(0, Bbs757CduRows.Restore(before, 0, after));
        Assert.Equal(after.Count - 1, Bbs757CduRows.Restore(before, before.Count - 1, after));
    }

    [Fact]
    public void Restore_with_nothing_to_go_on_keeps_the_index_clamped()
    {
        var after = Bbs757CduRows.Build(Legs, 1);
        Assert.Equal(3, Bbs757CduRows.Restore(Array.Empty<Bbs757CduRow>(), 3, after));
        Assert.Equal(after.Count - 1, Bbs757CduRows.Restore(Array.Empty<Bbs757CduRow>(), 99, after));
        Assert.Equal(0, Bbs757CduRows.Restore(after, -1, after));
        Assert.Equal(-1, Bbs757CduRows.Restore(after, 2, Array.Empty<Bbs757CduRow>()));
    }

    [Fact]
    public void Act_to_mod_is_not_a_page_change()
    {
        var mod = With(Legs, (0, "  MOD RTE 1 LEGS     1/2"));
        Assert.False(Bbs757CduRows.IsPageChange(Legs.Lines[0], mod.Lines[0]));

        // An entry that turns ACT into MOD (a speed on line 2) keeps the cursor on the line just edited.
        var before = Bbs757CduRows.Build(Legs, 1);
        int onLine2 = before.ToList().FindIndex(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 2);
        var after = Bbs757CduRows.Build(With(mod, (4, "NORMY         250/------")), 1);
        Assert.Equal(onLine2, Bbs757CduRows.Restore(before, onLine2, after));
    }

    [Fact]
    public void Next_page_is_a_page_change()
    {
        Assert.True(Bbs757CduRows.IsPageChange("  ACT RTE 1 LEGS     1/2", "  ACT RTE 1 LEGS     2/2"));
        Assert.True(Bbs757CduRows.IsPageChange("  ACT RTE 1 LEGS     1/2", "       NAV RADIO"));
        Assert.True(Bbs757CduRows.IsPageChange("", "  ACT RTE 1 LEGS     1/2"));
        Assert.False(Bbs757CduRows.IsPageChange("  ACT RTE 1 LEGS     1/2", "  ACT RTE 1 LEGS     1/2"));
    }

    [Fact]
    public void A_page_total_change_is_the_same_page()
    {
        // Inserting a waypoint can add a page while the pilot stays on page 1: the cursor stays on the line
        // (the MD-11's rule, [MD11-17]); only another page, or another page number, moves it to line 1.
        Assert.False(Bbs757CduRows.IsPageChange("  MOD RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     1/3"));
        Assert.False(Bbs757CduRows.IsPageChange("  ACT RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     1/3"));
        Assert.True(Bbs757CduRows.IsPageChange("  MOD RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     2/3"));
        // A route made active keeps its page.
        Assert.False(Bbs757CduRows.IsPageChange("     RTE 1           1/2", "   ACT RTE 1         1/2"));
    }

    // The first officer's VNAV pages in cruise, read live 2026-10-27 (FL380): the three pages and their counters.
    private const string VnavClb = "               CLB  1/3 ";
    private const string VnavCrz = "     ACT ECON CRZ    2/3";
    private const string VnavDes = "              DES   3/3 ";

    [Fact]
    public void A_speed_entered_on_a_vnav_page_keeps_the_page()
    {
        // A VNAV page retitles when a speed is entered (the owner, 2026-10-27; e.g. ECON CLB -> 250KT CLB on
        // Boeing FMCs, the BlueBird's exact words not captured): the same page, so the cursor stays and the
        // new title is said.
        Assert.False(Bbs757CduRows.IsPageChange("     ACT ECON CLB    1/3", "    MOD 250KT CLB    1/3"));
        Assert.False(Bbs757CduRows.IsPageChange(VnavDes, "     MOD 300KT DES   3/3"));
        Assert.False(Bbs757CduRows.IsPageChange(VnavCrz, "     MOD M.800 CRZ   2/3"));
        Assert.False(Bbs757CduRows.IsPageChange(VnavCrz, "     ACT LRC CRZ     2/3"));
        // A new cruise altitude on the CRZ page: still page 2.
        Assert.False(Bbs757CduRows.IsPageChange(VnavCrz, "     MOD CRZ CLB     2/3"));
        Assert.True(Bbs757CduRows.IsTitleChange("     ACT ECON CLB    1/3", "    MOD 250KT CLB    1/3"));
    }

    [Fact]
    public void Another_vnav_page_is_a_page_change()
    {
        Assert.True(Bbs757CduRows.IsPageChange(VnavCrz, VnavClb));   // PREV PAGE
        Assert.True(Bbs757CduRows.IsPageChange(VnavCrz, VnavDes));   // NEXT PAGE twice
        Assert.True(Bbs757CduRows.IsPageChange("        NAV RADIO       ", VnavCrz));   // the VNAV key
        Assert.True(Bbs757CduRows.IsPageChange(VnavCrz, "  ACT RTE 1 LEGS     1/2"));
    }

    [Fact]
    public void Any_changed_title_is_a_title_change()
    {
        // Every other aircraft's CDU window speaks a changed title, ACT to MOD and a new page total included
        // (owner, 2026-10-27: "however the other planes have it").
        Assert.True(Bbs757CduRows.IsTitleChange("  ACT RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     1/2"));
        Assert.True(Bbs757CduRows.IsTitleChange("  MOD RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     1/3"));
        Assert.True(Bbs757CduRows.IsTitleChange("", "       NAV RADIO"));
        // The same title, or the same words spaced differently, is not; nor is a blank one.
        Assert.False(Bbs757CduRows.IsTitleChange("  ACT RTE 1 LEGS     1/2", "  ACT RTE 1 LEGS     1/2"));
        Assert.False(Bbs757CduRows.IsTitleChange("  ACT RTE 1 LEGS     1/2", " ACT RTE 1 LEGS      1/2"));
        Assert.False(Bbs757CduRows.IsTitleChange("  ACT RTE 1 LEGS     1/2", "   "));
    }

    // ---- Plan 09 Task 1: the shared Boeing row shape ----------------------------------------------

    private static IReadOnlyList<string> Texts(Bbs757CduScreen screen, int cdu = 1) =>
        Bbs757CduRows.Build(screen, cdu).Select(r => r.Text).ToList();

    [Fact]
    public void Legs_rows_exact()
    {
        // Session 8's LEGS frame (ACT RTE 1 LEGS, 1/2): the course and distance labels, "n:" data rows, the
        // 24-dash row above line 6 left out (R71), the page counter set off from the name by three spaces.
        Assert.Equal(new[]
        {
            "Title: ACT RTE 1 LEGS   1/2",
            "    307°",
            "1: SEA           ---/------",
            "    076°     24NM",
            "2: NORMY         ---/------",
            "    077°     53NM",
            "3: BLUIT         ---/------",
            "    090°     46NM",
            "4: MWH           ---/------",
            "    112°    275NM",
            "5: KU87M         ---/------",
            "6: <RTE 2 LEGS    RTE DATA>",
            "Scratchpad: FLT NUMBER UPLINK",
        }, Texts(Legs));
    }

    [Fact]
    public void Navradio_rows_exact()
    {
        // The first officer's NAV RADIO frame: no page counter, three blank data rows said as "n:", an
        // interior run of spaces kept so the right-hand value stays in its column, dashes in a data row kept.
        Assert.Equal(new[]
        {
            "Title: NAV RADIO",
            "   VOR L              VOR R",
            "1: 113.60ALON        ------",
            "   CRS      RADIAL      CRS",
            "2:         139",
            "3:",
            "4:",
            "5:",
            "           PRESELECT",
            "6: ------            ------",
            "Scratchpad: NAV DATA OUT OF DATE",
        }, Texts(NavRadio, 2));
    }

    [Fact]
    public void Ident_separator_dropped()
    {
        // IDENT's line 6 label row is 24 dashes: dropped, as the PMDG windows do not (R71, braille noise).
        var ident = With(Bbs757CduFrame.Blank,
            (0, "          IDENT"), (11, new string('-', 24)), (12, "<INDEX         POS INIT>"));
        Assert.Equal(new[] { "Title: IDENT", "1:", "2:", "3:", "4:", "5:", "6: <INDEX         POS INIT>", "Scratchpad:" }, Texts(ident));

        // The same on a real frame: ACT ECON CRZ 2/3 has 24 dashes above line 5's data row.
        Assert.Equal(new[]
        {
            "Title: ACT ECON CRZ   2/3",
            "    CRZ ALT",
            "1: FL350",
            "    ECON SPD         TO T/D",
            "2: .801",
            "    EPR       KJFK ETA/FUEL",
            "3: 1.427        2247Z/ 19.0",
            "    STEP   OPT   MAX  RECMD",
            "4: ICAO   FL345 FL379 FL370",
            "5:                 ENG OUT>",
            "6: <RTA PROGRESS       LRC>",
            "Scratchpad:",
        }, Texts(Fixture("bbs757-cdu-vnav-crz.json")));
    }

    [Fact]
    public void A_label_row_is_dropped_only_when_it_is_nothing_but_20_dashes()
    {
        string Label(Bbs757CduScreen s) => Bbs757CduRows.Build(s, 1).Single(r => r.Kind == Bbs757CduRowKind.Label).Text;
        Assert.Empty(Bbs757CduRows.Build(With(Bbs757CduFrame.Blank, (1, new string('-', 20))), 1).Where(r => r.Kind == Bbs757CduRowKind.Label));
        Assert.Empty(Bbs757CduRows.Build(With(Bbs757CduFrame.Blank, (1, "   " + new string('-', 21))), 1).Where(r => r.Kind == Bbs757CduRowKind.Label));

        // 19 dashes are a label like any other; so are 20 with other text among them.
        Assert.Equal("   " + new string('-', 19), Label(With(Bbs757CduFrame.Blank, (1, new string('-', 19)))));
        Assert.Equal("   ---------- ----------", Label(With(Bbs757CduFrame.Blank, (1, "---------- ----------"))));
        // Progress's line 6 label: 14 dashes then GPS stays, as the screen draws it.
        Assert.Contains("   --------------       GPS", Texts(Fixture("bbs757-cdu-progress.json")));

        // A data row of dashes always stays.
        var rows = Bbs757CduRows.Build(With(Bbs757CduFrame.Blank, (2, new string('-', 24))), 1);
        Assert.Equal("1: " + new string('-', 24), rows.Single(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 1).Text);
    }

    [Theory]
    [InlineData(1, "Captain CDU screen blank")]
    [InlineData(2, "First officer CDU screen blank")]
    public void Blank_screen_row(int cdu, string said)
    {
        var rows = Bbs757CduRows.Build(Bbs757CduFrame.Blank, cdu);

        var row = Assert.Single(rows);
        Assert.Equal(Bbs757CduRowKind.Blank, row.Kind);
        Assert.Equal(0, row.Line);
        Assert.Equal(said, row.Text);
        // A frame of nothing but spaces, or one that never parsed, is the same screen.
        Assert.Equal(said, Assert.Single(Bbs757CduRows.Build(Bbs757CduFrame.Parse(null), cdu)).Text);
        Assert.Equal(new[] { said }, Bbs757CduFrame.ToDisplayLines(Bbs757CduFrame.Blank, cdu));
    }

    [Fact]
    public void A_screen_with_something_on_it_is_not_blank_even_without_a_title()
    {
        // Text on one line and nothing in the title: the rows are listed, the title as "Title:".
        var rows = Bbs757CduRows.Build(With(Bbs757CduFrame.Blank, (4, "ALPHA")), 1);

        Assert.Equal(Bbs757CduRowKind.Title, rows[0].Kind);
        Assert.Equal(0, rows[0].Line);
        Assert.Equal("Title:", rows[0].Text);
        Assert.Equal(new[] { "Title:", "1:", "2: ALPHA", "3:", "4:", "5:", "6:", "Scratchpad:" }, rows.Select(r => r.Text));
        Assert.DoesNotContain(rows, r => r.Kind == Bbs757CduRowKind.Blank);

        // Only a scratchpad message is something too.
        rows = Bbs757CduRows.Build(With(Bbs757CduFrame.Blank, (13, "NAV DATA OUT OF DATE")), 1);
        Assert.Equal("Scratchpad: NAV DATA OUT OF DATE", rows[^1].Text);
        Assert.Equal(8, rows.Count);
    }

    [Fact]
    public void No_row_over_40_cells_or_trailing_space()
    {
        // Every real frame, the widest screen a 24 x 14 grid can hold, and a screen of nothing: a 40-cell
        // braille line takes every row whole (a 24-column row gets at most 12 characters of prefix, the
        // scratchpad's), and none ends in a space.
        string wide = new('W', Bbs757CduFrame.Columns);
        var screens = new List<Bbs757CduScreen>
        {
            Bbs757CduFrame.Blank,
            new(Enumerable.Repeat(wide, Bbs757CduFrame.Rows).ToArray()),
            With(Bbs757CduFrame.Blank, (0, "WWWWWWWWWWWWWWWWWW 10/12"), (1, wide), (13, wide)),
        };
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "bbs757-cdu-*.json"))
            screens.Add(Bbs757CduFrame.Parse(File.ReadAllText(file, Encoding.UTF8)));
        Assert.True(screens.Count >= 8);

        foreach (var screen in screens)
            foreach (int cdu in new[] { 1, 2 })
                Assert.All(Texts(screen, cdu), text =>
                {
                    Assert.True(text.Length <= 40, $"{text.Length} cells: \"{text}\"");
                    Assert.Equal(text.TrimEnd(), text);
                });
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_rows_do_not_depend_on_the_culture(string culture)
    {
        // CI runs under en-US: the page counter and the line numbers must not take the PC's number format.
        var expected = Texts(Legs);
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(expected, Texts(Legs));
            Assert.Equal("Title: ACT RTE 1 LEGS   1/2", Texts(Legs)[0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void PageStart_is_line_one_value()
    {
        var rows = Bbs757CduRows.Build(Legs, 1);
        int start = Bbs757CduRows.PageStart(rows);
        Assert.Equal(Bbs757CduRowKind.Value, rows[start].Kind);
        Assert.Equal(1, rows[start].Line);
        // LEGS has a label above line 1, so its value is the third row.
        Assert.Equal(2, start);
        Assert.Equal(0, Bbs757CduRows.PageStart(Array.Empty<Bbs757CduRow>()));
    }

    // ---- Plan 09 Task 5: CDU messages spoken while the window is closed ------------------------------

    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    /// <summary>A watch that has read the scratchpad once, as the window leaves it when it is hidden.</summary>
    private static Bbs757CduMessageWatch Seeded(string pad)
    {
        var watch = new Bbs757CduMessageWatch();
        watch.Seed(pad);
        return watch;
    }

    [Theory]
    [InlineData("", "TAKEOFF DATA UPLINK", "CDU: TAKEOFF DATA UPLINK")]     // a message on an empty scratchpad
    [InlineData("KSEA", "INVALID ENTRY", "CDU: INVALID ENTRY")]             // a message over typed text
    [InlineData("INVALID ENTRY", "NOT IN DATABASE", "CDU: NOT IN DATABASE")]
    [InlineData("", "  RESET MCP ALT  ", "CDU: RESET MCP ALT")]            // trimmed
    public void NewMessage_speaks_a_message(string oldPad, string newPad, string said) =>
        Assert.Equal(said, Bbs757CduRows.NewMessage(oldPad, newPad, keysPending: false));

    [Theory]
    [InlineData("", "TAKEOFF DATA UPLINK", true)]    // keys queued or in their cycle: the scratchpad is mid-entry
    [InlineData(null, "TAKEOFF DATA UPLINK", false)] // a first reading is silent
    [InlineData("TAKEOFF DATA UPLINK", "", false)]   // cleared
    [InlineData("TAKEOFF DATA UPLINK", "   ", false)]
    [InlineData("KSEA", "KSEA", false)]              // unchanged
    [InlineData("KSEA", " KSEA ", false)]
    [InlineData("25", "250", false)]                 // typing grew it
    [InlineData("KSEA", "KSEA/PDX", false)]
    [InlineData("250", "25", false)]                 // CLR / DEL took characters off
    [InlineData("250", "2", false)]
    public void NewMessage_is_silent_for_typing_clearing_and_a_first_reading(string? oldPad, string newPad, bool keysPending) =>
        Assert.Null(Bbs757CduRows.NewMessage(oldPad, newPad, keysPending));

    [Fact]
    public void Message_spoken_once()
    {
        var watch = Seeded("");

        Assert.Equal("CDU: TAKEOFF DATA UPLINK", watch.OnScreen("TAKEOFF DATA UPLINK", false, At(10), muted: false));
        // The scratchpad keeps showing it push after push: said once.
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", false, At(11), muted: false));
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", false, At(12), muted: false));
        // Another message is a new one.
        Assert.Equal("CDU: INVALID ENTRY", watch.OnScreen("INVALID ENTRY", false, At(20), muted: false));
    }

    [Fact]
    public void A_message_already_on_the_scratchpad_when_nothing_was_known_is_not_said()
    {
        // The first push after the window is hidden, or after the link came back, only seeds.
        var watch = new Bbs757CduMessageWatch();
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", false, At(1), muted: false));
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", false, At(2), muted: false));

        watch.Seed(null);   // the screen was lost
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", false, At(3), muted: false));
        Assert.Equal("CDU: INVALID ENTRY", watch.OnScreen("INVALID ENTRY", false, At(4), muted: false));
    }

    [Fact]
    public void A_message_the_pilot_already_heard_in_the_open_window_is_not_said_again()
    {
        // The window seeds the watch with what the scratchpad shows when it is hidden.
        var watch = Seeded("TAKEOFF DATA UPLINK");
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", false, At(1), muted: false));
    }

    [Fact]
    public void Typing_is_not_a_message()
    {
        // The window queued "250" (it always tells the watch first) and the client still holds some of the
        // keys: every push is mid-entry.
        var watch = Seeded("");
        watch.KeysQueued(At(0), queuedMs: 3 * (Bbs757CduTyping.HoldMs + Bbs757CduTyping.GapMs));
        Assert.Null(watch.OnScreen("2", keysPending: true, At(1), muted: false));
        Assert.Null(watch.OnScreen("25", keysPending: true, At(2), muted: false));
        // The last key has gone out and the screen shows the whole entry: still the entry, not a message.
        Assert.Null(watch.OnScreen("250", keysPending: false, At(2.4), muted: false));
        Assert.Null(watch.OnScreen("250", keysPending: false, At(5), muted: false));

        // Text that grew between two pushes with no key pending at either.
        watch = Seeded("2");
        Assert.Null(watch.OnScreen("250", keysPending: false, At(1), muted: false));
        // ... and text the pilot took back.
        Assert.Null(watch.OnScreen("25", keysPending: false, At(2), muted: false));
    }

    [Fact]
    public void Typing_that_ends_between_two_scrapes_is_not_a_message()
    {
        // The window queued "250" (three typed keys, 400 ms each) and nothing was pending at the last push
        // before them: a hidden scrape comes once a second, so the first push that sees the entry can come
        // after the last key went out, with no push in between to show it growing.
        var watch = Seeded("");
        watch.KeysQueued(At(0), queuedMs: 3 * (Bbs757CduTyping.HoldMs + Bbs757CduTyping.GapMs));

        Assert.Null(watch.OnScreen("250", keysPending: false, At(1.5), muted: false));
        // Still inside the settle window after the keys (1.2 s + SettleMs): a change is the keys' echo.
        Assert.Null(watch.OnScreen("2500", keysPending: false, At(3.0), muted: false));
        // A real message after the settle window speaks.
        Assert.Equal("CDU: INVALID ENTRY", watch.OnScreen("INVALID ENTRY", keysPending: false, At(4.5), muted: false));
        Assert.Null(watch.OnScreen("INVALID ENTRY", keysPending: false, At(5.5), muted: false));
    }

    [Fact]
    public void A_push_with_keys_pending_extends_the_settle_window()
    {
        var watch = Seeded("");
        watch.KeysQueued(At(10), queuedMs: 0);   // the window's own keys: busy until 10 s + SettleMs
        // The client still holds them 1.5 s on (slower than the window's estimate): busy until that push + SettleMs.
        var lastPending = At(10) + TimeSpan.FromMilliseconds(Bbs757CduMessageWatch.SettleMs - 500);
        Assert.Null(watch.OnScreen("", keysPending: true, lastPending, muted: false));   // keys queued, nothing landed yet

        // The keys finished a moment later and the entry is all there; the push is inside SettleMs of the last pending one.
        Assert.Null(watch.OnScreen("250", keysPending: false, lastPending + TimeSpan.FromMilliseconds(Bbs757CduMessageWatch.SettleMs - 1), muted: false));
        Assert.Equal("CDU: INVALID ENTRY", watch.OnScreen("INVALID ENTRY", keysPending: false, lastPending + TimeSpan.FromMilliseconds(Bbs757CduMessageWatch.SettleMs + 1), muted: false));
    }

    [Fact]
    public void Keys_pending_for_the_first_officers_cdu_do_not_hold_back_a_captain_message()
    {
        // The client has one queue for both CDUs: the page service's reads (D, Shift+D, Ctrl+N) and the
        // window's keys to the first officer's CDU keep PendingKeyCount above 0, but nothing the window
        // queued for the captain's CDU is in flight (KeysQueued was never called). A message the FMC posts
        // meanwhile is still a message, said once, and does not make the next one deaf.
        var watch = Seeded("");
        Assert.Equal("CDU: TAKEOFF DATA UPLINK", watch.OnScreen("TAKEOFF DATA UPLINK", keysPending: true, At(1), muted: false));
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", keysPending: true, At(2), muted: false));
        Assert.Equal("CDU: INVALID ENTRY", watch.OnScreen("INVALID ENTRY", keysPending: true, At(3), muted: false));
        Assert.Equal("CDU: NOT IN DATABASE", watch.OnScreen("NOT IN DATABASE", keysPending: false, At(3.5), muted: false));
    }

    [Fact]
    public void Queueing_keys_never_shortens_the_settle_window()
    {
        var watch = Seeded("");
        watch.KeysQueued(At(0), queuedMs: 10_000);       // busy until 12 s
        watch.KeysQueued(At(1), queuedMs: 100);          // would end at 3.1 s: the longer one stays

        Assert.Null(watch.OnScreen("INVALID ENTRY", keysPending: false, At(11), muted: false));
        Assert.Equal("CDU: NOT IN DATABASE", watch.OnScreen("NOT IN DATABASE", keysPending: false, At(12.5), muted: false));
    }

    [Fact]
    public void Cleared_is_silent()
    {
        var watch = Seeded("TAKEOFF DATA UPLINK");

        Assert.Null(watch.OnScreen("", false, At(1), muted: false));          // CLR on a message
        Assert.Null(watch.OnScreen("   ", false, At(2), muted: false));
        // The scratchpad was clear, so the same message arriving again is a new one.
        Assert.Equal("CDU: TAKEOFF DATA UPLINK", watch.OnScreen("TAKEOFF DATA UPLINK", false, At(3), muted: false));

        // A typed entry the pilot wiped is silent too, whole or a character at a time.
        watch = Seeded("250");
        Assert.Null(watch.OnScreen("25", false, At(1), muted: false));
        Assert.Null(watch.OnScreen("", false, At(2), muted: false));
    }

    [Fact]
    public void Muted_in_ctrl_m_is_silent()
    {
        // The Ctrl+M row's own key is the one the window checks: mute it as the monitor manager does.
        string key = new MSFSBlindAssist.Aircraft.BBS757Definition().MonitorRows()
            .Single(r => r.Label == "CDU Messages While Closed").Key;
        var settings = new MSFSBlindAssist.Settings.UserSettings { BBS757DisabledMonitorVariables = new() { key } };
        settings.RebuildDisabledMonitorVariableCaches();
        bool muted = settings.BBS757DisabledMonitorVariablesSet.Contains(Bbs757CduRows.MessagesMonitorKey);
        Assert.True(muted);

        var watch = Seeded("");
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", false, At(1), muted));
        // Ticked again in Ctrl+M: the message that came while it was off is not said late.
        Assert.Null(watch.OnScreen("TAKEOFF DATA UPLINK", false, At(2), muted: false));
        // A message after that is.
        Assert.Equal("CDU: INVALID ENTRY", watch.OnScreen("INVALID ENTRY", false, At(3), muted: false));

        // Default: not in the list, spoken.
        var fresh = new MSFSBlindAssist.Settings.UserSettings();
        fresh.RebuildDisabledMonitorVariableCaches();
        Assert.DoesNotContain(Bbs757CduRows.MessagesMonitorKey, fresh.BBS757DisabledMonitorVariablesSet);
    }

    [Fact]
    public void Cdu_monitor_row_added()
    {
        var def = new MSFSBlindAssist.Aircraft.BBS757Definition();
        var rows = def.MonitorRows();

        var row = Assert.Single(rows, r => r.Key == Bbs757CduRows.MessagesMonitorKey);
        Assert.Equal("BBS757_CDU_MESSAGES", row.Key);
        Assert.Equal("CDU Messages While Closed", row.Label);
        // One row per key, and building the list again adds no second one.
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Single(def.MonitorRows(), r => r.Key == Bbs757CduRows.MessagesMonitorKey);
        // It is no variable's row: the key names speech that comes from the CDU window, not a SimVar.
        Assert.DoesNotContain(def.GetVariables().Keys, k => k == Bbs757CduRows.MessagesMonitorKey);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_message_does_not_depend_on_the_culture(string culture)
    {
        // The scratchpad text is passed through as the CDU shows it: a comma-decimal PC changes nothing.
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("CDU: SET 0.82 MACH 1,5", Bbs757CduRows.NewMessage("", "SET 0.82 MACH 1,5", keysPending: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
