using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins where the MCDU window's cursor goes when the page under it is redrawn.
///
/// Reported (2026-09-07): "Placing focus on line 6, and hitting Alt+Up more than a few times
/// will cause the focus to jump off of line 6." Two mechanisms, both live on the F-PLN page:
///
///   1. The window restored the cursor by INDEX, but the list is not positionally stable: a
///      blank label row is dropped (a run of empty lines is noise to arrow through), so the
///      index of "6:" depends on how many of the six labels above it are blank. Measured with a
///      read-only probe of the live MD11MCDU area (2026-09-07 16:57): on the Left unit's
///      ACT F-PLN page every label is present and "6:" is item 12; on the Center unit's MENU
///      page five labels are blank and "6:" is item 7. Restoring index 12 after a redraw that
///      blanked one label lands on the scratchpad row.
///
///   2. Every title change was treated as a page change — cursor forced to line 1, title
///      announced. But the live title is <c>ACT F-PLN     1/2</c>: MD-11 titles carry a page
///      counter, so a slew across a page boundary changes the title text while the pilot is
///      still reading the same page.
///
/// So: the cursor follows the ROW IT WAS ON (title / label n / line n / scratchpad), and only a
/// change of the page NAME — the title without its counter — moves it to line 1.
/// </summary>
public class Md11McduCursorTests
{
    private static Md11McduScreen Screen(params string[] rows)
    {
        var lines = new string[Md11McduLayout.Rows];
        for (var i = 0; i < lines.Length; i++) lines[i] = i < rows.Length ? rows[i] : string.Empty;
        return new Md11McduScreen { Unit = Md11McduUnit.Left, Lines = lines };
    }

    /// <summary>The live Left unit, 2026-09-07 16:57, verbatim from the probe. Every label present.</summary>
    private static Md11McduScreen LiveFplnPage1() => Screen(
        "      ACT F-PLN     1/2",
        " FROM    ATO  SPD   ALT",
        "DF407   2056  310/ FL160",
        " KERA5A  ETO",
        "DF408     58  310/ FL140",
        " KERA5A",
        "DF411     58    \"/     \"",
        " KERA5A",
        "DF412     58    \"/     \"",
        " KERA5A",
        "DF413     58    \"/     \"",
        " KERA5A",
        "DF414     58    \"/     \"",
        "");

    /// <summary>The live Center unit at the same moment. Labels 2-6 blank.</summary>
    private static Md11McduScreen LiveMenuPage() => Screen(
        "          MENU",
        "                 STANDBY",
        "                NAV/RAD*",
        "",
        "<ACARS",
        "",
        "<ADAS",
        "",
        "<CFDS",
        "",
        "                  MAINT>",
        "",
        "",
        "");

    private static int IndexOf(IReadOnlyList<Md11McduRow> rows, Md11McduRowKind kind, int line)
    {
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Kind == kind && rows[i].Line == line) return i;
        return -1;
    }

    // ---------------------------------------------------------------- Build

    [Fact]
    public void Line_6_sits_at_different_indexes_on_the_live_FPLN_and_MENU_pages()
    {
        // The whole reason an index cannot stand in for a line.
        Assert.Equal(12, IndexOf(Md11McduRows.Build(LiveFplnPage1()), Md11McduRowKind.Value, 6));
        Assert.Equal(7, IndexOf(Md11McduRows.Build(LiveMenuPage()), Md11McduRowKind.Value, 6));
    }

    [Fact]
    public void Build_drops_blank_label_rows_and_keeps_blank_value_rows()
    {
        var rows = Md11McduRows.Build(LiveMenuPage());

        // Title, one label, six values, scratchpad.
        Assert.Equal(9, rows.Count);
        Assert.Equal(Md11McduRowKind.Title, rows[0].Kind);
        Assert.Equal(Md11McduRowKind.Label, rows[1].Kind);
        Assert.Equal(1, rows[1].Line);
        for (var line = 1; line <= 6; line++)
            Assert.NotEqual(-1, IndexOf(rows, Md11McduRowKind.Value, line));
        Assert.Equal(Md11McduRowKind.Scratchpad, rows[^1].Kind);

        // An empty LSK row is a real, selectable state on a CDU - it keeps its number.
        Assert.Equal("6:", rows[IndexOf(rows, Md11McduRowKind.Value, 6)].Text.TrimEnd());
        Assert.Equal("5:                   MAINT>", rows[IndexOf(rows, Md11McduRowKind.Value, 5)].Text);
    }

    [Fact]
    public void Build_renders_the_same_text_the_window_always_showed()
    {
        var rows = Md11McduRows.Build(LiveFplnPage1());

        Assert.Equal("Title: ACT F-PLN     1/2", rows[0].Text);
        Assert.Equal("    FROM    ATO  SPD   ALT", rows[1].Text);      // "   " + label (label keeps its own leading space)
        Assert.Equal("1: DF407   2056  310/ FL160", rows[2].Text);
        Assert.Equal("Scratchpad: ", rows[^1].Text);
        Assert.Equal(14, rows.Count);
    }

    // -------------------------------------------------------------- Restore

    [Fact]
    public void The_cursor_follows_its_LSK_line_when_a_label_above_it_vanishes()
    {
        // The reported case: reading line 6 on the F-PLN page, slew, and the redraw has one
        // fewer label above it (a waypoint with no procedure label scrolled in).
        var before = Md11McduRows.Build(LiveFplnPage1());
        var cursor = before[IndexOf(before, Md11McduRowKind.Value, 6)];
        Assert.Equal(12, IndexOf(before, Md11McduRowKind.Value, 6));

        var after = Md11McduRows.Build(Screen(
            "      ACT F-PLN     1/2",
            " FROM    ATO  SPD   ALT",
            "DF408     58  310/ FL140",
            " KERA5A",
            "DF411     58    \"/     \"",
            " KERA5A",
            "DF412     58    \"/     \"",
            " KERA5A",
            "DF413     58    \"/     \"",
            " KERA5A",
            "DF414     58    \"/     \"",
            "",                                   // label 6 blank this time
            "DF415     58    \"/     \"",
            ""));

        var restored = Md11McduRows.Restore(after, cursor);

        Assert.Equal(11, restored);
        Assert.Equal(Md11McduRowKind.Value, after[restored].Kind);
        Assert.Equal(6, after[restored].Line);
        // Index restore would have put the pilot on the scratchpad row.
        Assert.Equal(Md11McduRowKind.Scratchpad, after[12].Kind);
    }

    [Fact]
    public void The_cursor_follows_its_line_across_the_MENU_and_FPLN_shapes()
    {
        var menu = Md11McduRows.Build(LiveMenuPage());
        var cursor = menu[IndexOf(menu, Md11McduRowKind.Value, 3)];

        var fpln = Md11McduRows.Build(LiveFplnPage1());
        var restored = Md11McduRows.Restore(fpln, cursor);

        Assert.Equal(IndexOf(fpln, Md11McduRowKind.Value, 3), restored);
    }

    [Fact]
    public void A_label_row_that_vanished_hands_the_cursor_to_the_line_it_labelled()
    {
        var fpln = Md11McduRows.Build(LiveFplnPage1());
        var cursor = fpln[IndexOf(fpln, Md11McduRowKind.Label, 3)];

        var menu = Md11McduRows.Build(LiveMenuPage());   // label 3 is blank here
        var restored = Md11McduRows.Restore(menu, cursor);

        Assert.Equal(IndexOf(menu, Md11McduRowKind.Value, 3), restored);
    }

    [Fact]
    public void Title_and_scratchpad_rows_are_followed_too()
    {
        var fpln = Md11McduRows.Build(LiveFplnPage1());
        var menu = Md11McduRows.Build(LiveMenuPage());

        Assert.Equal(0, Md11McduRows.Restore(menu, fpln[0]));
        Assert.Equal(menu.Count - 1, Md11McduRows.Restore(menu, fpln[^1]));
    }

    [Fact]
    public void Restore_has_no_opinion_without_a_previous_row()
    {
        Assert.Equal(-1, Md11McduRows.Restore(Md11McduRows.Build(LiveMenuPage()), null));
    }

    // ----------------------------------------------------------- Page start

    /// <summary>
    /// Review round 2 (C4): a page change put the cursor on list ITEM 1 — line 1's LABEL on the
    /// F-PLN page (" FROM    ATO  SPD   ALT"), a row no line-select key acts on. It lands on line
    /// 1's VALUE row, found by identity, and so does the "nothing selected" fallback.
    /// </summary>
    [Fact]
    public void A_page_change_lands_on_line_1s_value_row_not_on_item_1()
    {
        var fpln = Md11McduRows.Build(LiveFplnPage1());
        var start = Md11McduRows.PageStart(fpln);

        Assert.Equal(Md11McduRowKind.Value, fpln[start].Kind);
        Assert.Equal(1, fpln[start].Line);
        Assert.Equal(2, start);
        Assert.Equal(Md11McduRowKind.Label, fpln[1].Kind);   // what index 1 used to land on
    }

    [Fact]
    public void Line_1s_value_row_is_item_1_only_when_its_label_is_blank()
    {
        var rows = Md11McduRows.Build(Screen(
            "        INIT",
            "",
            "<INDEX"));

        Assert.Equal(1, Md11McduRows.PageStart(rows));
        Assert.Equal("1: <INDEX", rows[1].Text);
    }

    [Fact]
    public void Without_a_line_1_row_the_cursor_goes_to_the_title_then_to_the_first_row()
    {
        var titleAndScratchpad = new[]
        {
            new Md11McduRow("Title: MENU", Md11McduRowKind.Title, 0),
            new Md11McduRow("Scratchpad: ", Md11McduRowKind.Scratchpad, 0),
        };
        Assert.Equal(0, Md11McduRows.PageStart(titleAndScratchpad));

        var scratchpadOnly = new[] { new Md11McduRow("Scratchpad: ", Md11McduRowKind.Scratchpad, 0) };
        Assert.Equal(0, Md11McduRows.PageStart(scratchpadOnly));

        Assert.Equal(-1, Md11McduRows.PageStart(Array.Empty<Md11McduRow>()));
    }

    // --------------------------------------------------------------- Title

    [Fact]
    public void Slewing_across_a_page_boundary_keeps_the_page_name()
    {
        // The live title, and the one a slew past the sixth waypoint produces.
        Assert.True(Md11McduTitle.SamePage("ACT F-PLN     1/2", "ACT F-PLN     2/2"));
    }

    [Theory]
    [InlineData("ACT F-PLN     1/2", "ACT F-PLN")]
    [InlineData("ACT F-PLN 10/12", "ACT F-PLN")]
    [InlineData("MENU", "MENU")]
    [InlineData("F-PLN INIT", "F-PLN INIT")]
    [InlineData("TAKEOFF 2/3", "TAKEOFF")]
    [InlineData("RTE 1", "RTE 1")]             // a number that is not a counter
    [InlineData("DIR/INTC", "DIR/INTC")]       // a slash that is not a counter
    [InlineData("RWY 09/27", "RWY")]           // accepted residual: a trailing digit pair reads as a counter,
                                               // which can only KEEP a cursor on its row, never throw it
    [InlineData("", "")]
    public void PageName_strips_only_a_trailing_counter(string title, string expected)
    {
        Assert.Equal(expected, Md11McduTitle.PageName(title));
    }

    [Fact]
    public void A_counter_that_is_not_at_the_end_is_part_of_the_name()
    {
        // Nothing on the aircraft looks like this; the rule is anchored so it cannot eat a
        // value that merely contains a slash.
        Assert.Equal("1/2 ACT", Md11McduTitle.PageName("1/2 ACT"));
    }

    [Fact]
    public void A_different_page_is_a_page_change_even_with_the_same_counter()
    {
        Assert.False(Md11McduTitle.SamePage("ACT F-PLN     1/2", "SEC F-PLN     1/2"));
        Assert.False(Md11McduTitle.SamePage("ACT F-PLN     1/2", "F-PLN INIT"));
    }

    /// <summary>
    /// Entering a waypoint retitles ACT F-PLN as MOD F-PLN — the same page, the same lines, a
    /// modification pending. The cursor stays on the line being edited; the new title is still
    /// announced by the form. SEC F-PLN is another flight plan and stays a page change.
    /// </summary>
    [Fact]
    public void A_pending_modification_is_the_same_page()
    {
        Assert.True(Md11McduTitle.SamePage("ACT F-PLN     1/2", "MOD F-PLN     1/2"));
        Assert.True(Md11McduTitle.SamePage("MOD F-PLN     2/2", "ACT F-PLN     1/2"));
        Assert.Equal("F-PLN", Md11McduTitle.Identity("      ACT F-PLN     1/2"));
        Assert.Equal("SEC F-PLN", Md11McduTitle.Identity("SEC F-PLN 1/2"));
        Assert.False(Md11McduTitle.SamePage("ACT F-PLN     1/2", "SEC F-PLN     1/2"));
    }

    [Fact]
    public void The_first_title_after_none_is_a_page_change()
    {
        // The window opens with no title adopted; the first page must still land on line 1.
        Assert.False(Md11McduTitle.SamePage("", "          MENU".Trim()));
    }

    // ---------------------------------------------------------- Unit memory

    /// <summary>
    /// Found in review of PR #189: reading line 5 of ACT F-PLN on the Left, Ctrl+Shift+C to
    /// glance at the Center's MENU, Ctrl+Shift+L back — and the cursor was on line 1. The window
    /// kept ONE last title for all three units, so the Left's title was judged against the
    /// Center's ("MENU" vs "ACT F-PLN 1/2"), which is a page change. Each unit now remembers its
    /// own last title and its own cursor row.
    /// </summary>
    [Fact]
    public void Switching_units_and_back_restores_that_units_own_line()
    {
        var memory = new Md11McduUnitMemory();
        var fpln = Md11McduRows.Build(LiveFplnPage1());
        var menu = Md11McduRows.Build(LiveMenuPage());

        // Left: first page, reading line 5.
        Assert.True(memory.Adopt(Md11McduUnit.Left, "ACT F-PLN     1/2").PageChanged);
        memory.RememberCursor(Md11McduUnit.Left, fpln[IndexOf(fpln, Md11McduRowKind.Value, 5)]);

        // A glance at the Center's MENU, reading line 3 there.
        var center = memory.Adopt(Md11McduUnit.Center, "MENU");
        Assert.True(center.TitleChanged);
        Assert.True(center.PageChanged);
        memory.RememberCursor(Md11McduUnit.Center, menu[IndexOf(menu, Md11McduRowKind.Value, 3)]);

        // Back to the Left: the same page it last showed — nothing to announce, no jump to
        // line 1, and its own remembered row is what the cursor goes back to.
        var back = memory.Adopt(Md11McduUnit.Left, "ACT F-PLN     1/2");
        Assert.False(back.TitleChanged);
        Assert.False(back.PageChanged);
        Assert.Equal(IndexOf(fpln, Md11McduRowKind.Value, 5), Md11McduRows.Restore(fpln, memory.Cursor(Md11McduUnit.Left)));

        // The Center's line survives the round trip too.
        Assert.Equal(IndexOf(menu, Md11McduRowKind.Value, 3), Md11McduRows.Restore(menu, memory.Cursor(Md11McduUnit.Center)));
    }

    [Fact]
    public void A_unit_is_judged_against_its_own_last_title_not_the_unit_shown_before_it()
    {
        var memory = new Md11McduUnitMemory();
        memory.Adopt(Md11McduUnit.Left, "ACT F-PLN     1/2");
        memory.Adopt(Md11McduUnit.Center, "MENU");

        // While away the Left paged: the title text changed, the page did not — the caller
        // announces it, the cursor is kept. Against the Center's "MENU" it would have read as a
        // page change.
        var back = memory.Adopt(Md11McduUnit.Left, "ACT F-PLN     2/2");
        Assert.True(back.TitleChanged);
        Assert.False(back.PageChanged);
        Assert.Equal("ACT F-PLN     2/2", memory.LastTitle(Md11McduUnit.Left));
        Assert.Equal("MENU", memory.LastTitle(Md11McduUnit.Center));
    }

    [Fact]
    public void The_first_look_at_a_unit_is_a_page_change_with_no_row_to_return_to()
    {
        var memory = new Md11McduUnitMemory();
        memory.Adopt(Md11McduUnit.Left, "ACT F-PLN     1/2");

        var right = memory.Adopt(Md11McduUnit.Right, "MENU");
        Assert.True(right.TitleChanged);
        Assert.True(right.PageChanged);
        Assert.Null(memory.Cursor(Md11McduUnit.Right));
        Assert.Equal(-1, Md11McduRows.Restore(Md11McduRows.Build(LiveMenuPage()), memory.Cursor(Md11McduUnit.Right)));
    }

    [Fact]
    public void An_empty_title_is_never_adopted()
    {
        // A frame whose title row is blank is not a page — the window's rule since the title
        // latch existed, now per unit. Adopting it would announce the page that follows as new.
        var memory = new Md11McduUnitMemory();
        memory.Adopt(Md11McduUnit.Left, "MENU");

        var blank = memory.Adopt(Md11McduUnit.Left, "");
        Assert.False(blank.TitleChanged);
        Assert.False(blank.PageChanged);
        Assert.Equal("MENU", memory.LastTitle(Md11McduUnit.Left));
        Assert.False(memory.Adopt(Md11McduUnit.Left, "MENU").TitleChanged);
    }

    [Fact]
    public void An_advisory_does_not_overwrite_the_row_the_pilot_was_on()
    {
        // The form records CursorRow() at every transition, and CursorRow() is null while the
        // list shows the blank / no-data advisory. Recording that null would lose the row a
        // return to the page must land on.
        var memory = new Md11McduUnitMemory();
        var fpln = Md11McduRows.Build(LiveFplnPage1());
        var line5 = fpln[IndexOf(fpln, Md11McduRowKind.Value, 5)];

        memory.RememberCursor(Md11McduUnit.Left, line5);
        memory.RememberCursor(Md11McduUnit.Left, null);

        Assert.Equal<Md11McduRow?>(line5, memory.Cursor(Md11McduUnit.Left));
    }
}
