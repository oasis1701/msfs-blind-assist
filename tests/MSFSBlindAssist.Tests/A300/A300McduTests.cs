using System.Globalization;
using System.Text;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Forms.A300;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300McduTests
{
    /// <summary>A 1008-byte area holding <paramref name="rows"/> (index = screen row), green, large font.</summary>
    private static byte[] Area(params (int Row, string Text)[] rows)
    {
        var bytes = new byte[A300McduText.DataSize];
        foreach (var (row, text) in rows)
        {
            var ascii = Encoding.ASCII.GetBytes(text);
            for (int col = 0; col < ascii.Length && col < A300McduText.Cols; col++)
            {
                int at = (row * A300McduText.Cols + col) * A300McduText.CellBytes;
                bytes[at] = ascii[col];
                bytes[at + 1] = 3;
            }
        }
        return bytes;
    }

    // The INIT page as the captain's MCDU showed it at the gate (2026-10-03).
    private static byte[] InitPage() => Area(
        (0, "          INIT      b   "),
        (1, " CO RTE          FROM/TO"),
        (2, "!!!!!!!/!!!!   !!!!/!!!!"),
        (9, "CRZ FL        TEMP/TROPO"),
        (10, "!!!           -56^/36090"),
        (13, "NAV ACCUR DOWNGRADED"));

    [Fact]
    public void The_screen_decodes_with_the_mcdu_fonts_symbols()
    {
        var screen = A300McduText.Decode(A300McduUnit.Captain, InitPage())!;
        Assert.Equal("          INIT      →", screen.Title);
        Assert.Equal("#######/####   ####/####", screen.Lines[2]);
        Assert.Equal("###           -56°/36090", screen.Lines[10]);
        Assert.Equal("NAV ACCUR DOWNGRADED", screen.Scratchpad);
        Assert.False(screen.IsBlank);
    }

    [Theory]
    [InlineData((byte)'a', '←')]
    [InlineData((byte)'l', '←')]
    [InlineData((byte)'b', '→')]
    [InlineData((byte)'r', '→')]
    [InlineData((byte)'{', '↑')]
    [InlineData((byte)'}', '↓')]
    [InlineData((byte)'~', 'Δ')]
    [InlineData((byte)'c', 'C')]
    [InlineData((byte)'<', '<')]
    [InlineData((byte)0, ' ')]
    [InlineData((byte)200, ' ')]
    public void Each_cell_reads_as_the_font_draws_it(byte cell, char glyph) => Assert.Equal(glyph, A300McduText.Glyph(cell));

    [Fact]
    public void A_short_payload_is_refused_and_an_empty_one_is_blank()
    {
        Assert.Null(A300McduText.Decode(A300McduUnit.Captain, new byte[100]));
        Assert.True(A300McduText.Decode(A300McduUnit.Captain, new byte[A300McduText.DataSize])!.IsBlank);
    }

    [Fact]
    public void The_list_drops_blank_labels_and_numbers_every_line()
    {
        var rows = A300McduRows.Build(A300McduText.Decode(A300McduUnit.Captain, InitPage())!);
        Assert.Equal("Title: INIT      →", rows[0].Text);
        Assert.Equal("    CO RTE          FROM/TO", rows[1].Text);
        Assert.Equal("1: #######/####   ####/####", rows[2].Text);
        Assert.Equal("2: ", rows[3].Text);   // a blank line is kept: it is a line-select key
        Assert.Equal("Scratchpad: NAV ACCUR DOWNGRADED", rows[^1].Text);
        Assert.Equal(1, A300McduRows.PageStart(rows) - 1);   // line 1's value row, not its label
    }

    [Fact]
    public void The_cursor_follows_its_row_and_a_vanished_label_hands_it_to_its_line()
    {
        var before = A300McduRows.Build(A300McduText.Decode(A300McduUnit.Captain, InitPage())!);
        var after = A300McduRows.Build(A300McduText.Decode(A300McduUnit.Captain, Area((0, "INIT"), (2, "X")))!);
        var labelOfLine1 = before.Single(r => r.Kind == A300McduRowKind.Label && r.Line == 1);
        Assert.Equal(after.ToList().FindIndex(r => r.Kind == A300McduRowKind.Value && r.Line == 1),
            A300McduRows.Restore(after, labelOfLine1));
        Assert.Equal(-1, A300McduRows.Restore(after, null));
    }

    [Theory]
    [InlineData('a', "A")]
    [InlineData('7', "7")]
    [InlineData('.', "DOT")]
    [InlineData('/', "SLASH")]
    [InlineData('+', "PLUS")]
    [InlineData('-', "MINUS")]
    [InlineData(' ', "SPACE")]
    [InlineData(',', null)]
    public void Typed_characters_map_to_mcdu_keys(char c, string? key) => Assert.Equal(key, A300McduKeys.ForChar(c));

    [Fact]
    public void An_entry_with_a_missing_key_is_refused_whole()
    {
        Assert.Null(A300McduKeys.RefusalFor("KJFK/KLAX 12"));
        Assert.Equal("Not sent. The MCDU keyboard has no comma key.", A300McduKeys.RefusalFor("KJFK,KLAX"));
        Assert.Equal("Not sent. The MCDU keyboard has no comma or asterisk key.", A300McduKeys.RefusalFor("A,B*,"));
    }

    [Fact]
    public void A_refused_character_is_named_as_the_md11_names_it()
    {
        // A screen reader's symbol level can drop a bare backslash or bracket, so each one is named.
        Assert.Equal("Not sent. The MCDU keyboard has no backslash key.", A300McduKeys.RefusalFor("KJFK\\"));
        Assert.Equal("Not sent. The MCDU keyboard has no left bracket or right bracket key.", A300McduKeys.RefusalFor("A[B]"));
    }

    [Theory]
    [InlineData(',', "comma")]
    [InlineData('*', "asterisk")]
    [InlineData('#', "hash")]
    [InlineData('%', "percent")]
    [InlineData(':', "colon")]
    [InlineData(';', "semicolon")]
    [InlineData('\'', "apostrophe")]
    [InlineData('"', "quote")]
    [InlineData('(', "left parenthesis")]
    [InlineData(')', "right parenthesis")]
    [InlineData('_', "underscore")]
    [InlineData('?', "question mark")]
    [InlineData('!', "exclamation mark")]
    [InlineData('&', "ampersand")]
    [InlineData('@', "at sign")]
    [InlineData('=', "equals")]
    [InlineData('<', "less than")]
    [InlineData('>', "greater than")]
    [InlineData('\\', "backslash")]
    [InlineData('[', "left bracket")]
    [InlineData(']', "right bracket")]
    [InlineData('{', "left brace")]
    [InlineData('}', "right brace")]
    [InlineData('|', "vertical bar")]
    [InlineData('^', "caret")]
    [InlineData('~', "tilde")]
    [InlineData('`', "backtick")]
    [InlineData('$', "dollar")]
    [InlineData('\t', "tab")]
    public void Every_refused_character_is_spoken_by_the_md11s_name(char c, string name)
    {
        Assert.Equal($"Not sent. The MCDU keyboard has no {name} key.", A300McduKeys.RefusalFor(c.ToString()));
        // Two windows, one wording: the MD-11's MCDU window names the same 29 characters the same way.
        Assert.Equal(Md11McduKeys.RefusalFor(c.ToString()), A300McduKeys.RefusalFor(c.ToString()));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_refusal_reads_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("Not sent. The MCDU keyboard has no percent, dollar or tab key.", A300McduKeys.RefusalFor("1%2$3\t"));
            Assert.Equal("Not sent. The MCDU keyboard has no comma key.", A300McduKeys.RefusalFor("KJFK,KLAX"));
            Assert.Null(A300McduKeys.RefusalFor("KJFK/KLAX 12.5"));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Keys_are_the_cockpits_own_lvars()
    {
        Assert.Equal("1 (>L:INI_MCDU1_LSK3R)", A300McduKeys.PressRpn(A300McduUnit.Captain, A300McduKeys.Lsk(3, right: true)));
        Assert.Equal("0 (>L:INI_MCDU2_CLR)", A300McduKeys.ReleaseRpn(A300McduUnit.FirstOfficer, "CLR"));
        Assert.Equal("iniAirbusMCDU_2", A300McduText.AreaName(A300McduUnit.FirstOfficer));
    }

    [Fact]
    public void Page_buttons_have_distinct_accelerators_and_leave_alt_s_to_the_input_box()
    {
        var accelerators = A300McduKeys.PageButtons
            .Select(b => char.ToUpperInvariant(b.Label[b.Label.IndexOf('&') + 1])).ToList();
        Assert.Equal(accelerators.Count, accelerators.Distinct().Count());
        Assert.DoesNotContain('S', accelerators);
    }

    [Theory]
    [InlineData(Keys.D3, false, true, false, false, "LSK3L")]
    [InlineData(Keys.D3, true, false, false, false, "LSK3R")]
    [InlineData(Keys.F2, false, false, false, true, "LSK2L")]
    [InlineData(Keys.F12, false, false, false, true, "LSK6R")]
    [InlineData(Keys.PageUp, false, false, false, false, "UARROW")]
    [InlineData(Keys.PageDown, false, false, false, false, "DOWN")]
    [InlineData(Keys.Up, true, false, false, false, "UARROW")]
    [InlineData(Keys.Down, true, false, false, false, "DOWN")]
    [InlineData(Keys.Right, true, false, false, false, "NEXT")]
    public void Chords_press_mcdu_keys(Keys key, bool alt, bool control, bool shift, bool alternate, string mcduKey)
    {
        var action = A300McduKeyRouting.Resolve(key, alt, control, shift, A300McduKeyRouting.ComboFocus.None, alternate);
        Assert.Equal(A300McduKeyRouting.ActionKind.Press, action.Kind);
        Assert.Equal(mcduKey, action.Key);
    }

    [Theory]
    [InlineData(Keys.Up, true, false, true)]      // Alt+Shift+Up
    [InlineData(Keys.Down, true, true, false)]    // Ctrl+Alt+Down: how AltGr arrives on some layouts
    [InlineData(Keys.Up, false, false, false)]    // plain Up: the display list's own arrow
    [InlineData(Keys.Down, false, true, false)]   // Ctrl+Down
    public void Up_and_down_slew_only_behind_alt_alone(Keys key, bool alt, bool control, bool shift)
    {
        var action = A300McduKeyRouting.Resolve(key, alt, control, shift, A300McduKeyRouting.ComboFocus.None, useAlternateLskKeys: false);
        Assert.Equal(A300McduKeyRouting.ActionKind.Pass, action.Kind);
    }

    [Theory]
    [InlineData(Keys.Up, true)]
    [InlineData(Keys.Down, true)]
    [InlineData(Keys.PageDown, false)]
    [InlineData(Keys.F4, false)]
    public void A_focused_unit_selector_keeps_its_own_keys(Keys key, bool alt)
    {
        var action = A300McduKeyRouting.Resolve(key, alt, false, false, A300McduKeyRouting.ComboFocus.Closed, useAlternateLskKeys: true);
        Assert.Equal(A300McduKeyRouting.ActionKind.Pass, action.Kind);
    }

    [Fact]
    public void Ctrl_shift_l_and_r_pick_the_unit()
    {
        Assert.Equal(A300McduUnit.FirstOfficer,
            A300McduKeyRouting.Resolve(Keys.R, false, true, true, A300McduKeyRouting.ComboFocus.None, false).Unit);
        Assert.Equal(A300McduKeyRouting.ActionKind.SelectUnit,
            A300McduKeyRouting.Resolve(Keys.L, false, true, true, A300McduKeyRouting.ComboFocus.Closed, false).Kind);
    }

    [Fact]
    public void An_identical_delivery_keeps_the_old_screen_and_reset_forgets_it()
    {
        var manager = new A300McduDataManager();
        manager.Deliver(A300McduUnit.Captain, InitPage());
        var first = manager.GetScreen(A300McduUnit.Captain);
        manager.Deliver(A300McduUnit.Captain, InitPage());
        Assert.Same(first, manager.GetScreen(A300McduUnit.Captain));
        Assert.Null(manager.GetScreen(A300McduUnit.FirstOfficer));
        manager.Reset();
        Assert.Null(manager.GetScreen(A300McduUnit.Captain));
    }

    private static readonly DateTime T0 = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static A300McduScreen Page(A300McduUnit unit = A300McduUnit.Captain) => A300McduText.Decode(unit, InitPage())!;
    private static A300McduScreen Blank(A300McduUnit unit = A300McduUnit.Captain) =>
        A300McduText.Decode(unit, new byte[A300McduText.DataSize])!;

    [Fact]
    public void The_blank_frame_between_pages_holds_the_page()
    {
        // The FMS publishes a blank frame of 70 to 370 ms on every page key (measured in flight, 2026-10-04).
        var clocks = new A300McduBlankClocks();
        var unit = A300McduUnit.Captain;
        Assert.Equal(A300McduDisplayAction.ShowContent, clocks.Judge(unit, Page(), T0));
        Assert.Equal(A300McduDisplayAction.HoldLastPage, clocks.Judge(unit, Blank(), T0.AddMilliseconds(250)));
        Assert.Equal(A300McduDisplayAction.HoldLastPage, clocks.Judge(unit, Blank(), T0.AddMilliseconds(620)));
        Assert.Equal(A300McduDisplayAction.ShowContent, clocks.Judge(unit, Page(), T0.AddMilliseconds(750)));
    }

    [Fact]
    public void A_blank_that_lasts_a_second_is_believed()
    {
        var clocks = new A300McduBlankClocks();
        var unit = A300McduUnit.Captain;
        Assert.Equal(A300McduDisplayAction.HoldLastPage, clocks.Judge(unit, Blank(), T0));
        Assert.Equal(A300McduDisplayAction.HoldLastPage, clocks.Judge(unit, Blank(), T0.AddMilliseconds(999)));
        Assert.Equal(A300McduDisplayAction.ShowBlank, clocks.Judge(unit, Blank(), T0.AddMilliseconds(1000)));
    }

    [Fact]
    public void A_page_between_two_blanks_starts_the_wait_again()
    {
        var clocks = new A300McduBlankClocks();
        var unit = A300McduUnit.Captain;
        clocks.Judge(unit, Blank(), T0);
        clocks.Judge(unit, Page(), T0.AddMilliseconds(500));
        Assert.Equal(A300McduDisplayAction.HoldLastPage, clocks.Judge(unit, Blank(), T0.AddMilliseconds(900)));
        Assert.Equal(A300McduDisplayAction.HoldLastPage, clocks.Judge(unit, Blank(), T0.AddMilliseconds(1800)));
        Assert.Equal(A300McduDisplayAction.ShowBlank, clocks.Judge(unit, Blank(), T0.AddMilliseconds(1900)));
    }

    [Fact]
    public void Starting_to_follow_a_unit_on_a_blank_waits_rather_than_holding_another_page()
    {
        // An open or a re-show has no page of THIS unit on screen to hold, and every clock starts over.
        var clocks = new A300McduBlankClocks();
        var unit = A300McduUnit.Captain;
        clocks.Judge(unit, Blank(), T0);
        clocks.Reset();
        Assert.Equal(A300McduDisplayAction.ShowWaiting, clocks.Follow(unit, Blank(), T0.AddMilliseconds(5000)));
        Assert.Equal(A300McduDisplayAction.HoldLastPage, clocks.Judge(unit, Blank(), T0.AddMilliseconds(5500)));
        Assert.Equal(A300McduDisplayAction.ShowBlank, clocks.Judge(unit, Blank(), T0.AddMilliseconds(6000)));
        Assert.Equal(A300McduDisplayAction.ShowContent, clocks.Follow(unit, Page(), T0.AddMilliseconds(6100)));
    }

    [Fact]
    public void A_unit_blank_for_a_while_is_judged_blank_at_once_when_followed()
    {
        // The window sat on the captain's MCDU while the first officer's stayed blank: switching to it is no new blank.
        var clocks = new A300McduBlankClocks();
        var fo = A300McduUnit.FirstOfficer;
        clocks.Tick(fo, Blank(fo), T0);
        Assert.Equal(A300McduDisplayAction.ShowBlank, clocks.Follow(fo, Blank(fo), T0.AddSeconds(2)));
    }

    [Theory]
    [InlineData(0, A300McduDisplayAction.ShowWaiting)]
    [InlineData(999, A300McduDisplayAction.ShowWaiting)]
    [InlineData(1000, A300McduDisplayAction.ShowBlank)]
    [InlineData(5000, A300McduDisplayAction.ShowBlank)]
    public void Following_a_blank_shows_waiting_until_it_has_lasted_a_second_and_never_holds_a_page(int ms, A300McduDisplayAction expected)
    {
        var clocks = new A300McduBlankClocks();
        var fo = A300McduUnit.FirstOfficer;
        clocks.Tick(fo, Blank(fo), T0);
        Assert.Equal(expected, clocks.Follow(fo, Blank(fo), T0.AddMilliseconds(ms)));
    }

    [Fact]
    public void A_reshow_starts_every_clock_again()
    {
        var clocks = new A300McduBlankClocks();
        var cpt = A300McduUnit.Captain;
        var fo = A300McduUnit.FirstOfficer;
        clocks.Tick(cpt, Blank(cpt), T0);
        clocks.Tick(fo, Blank(fo), T0);
        clocks.Reset();
        Assert.Equal(A300McduDisplayAction.ShowWaiting, clocks.Follow(fo, Blank(fo), T0.AddSeconds(2)));
        Assert.Equal(A300McduDisplayAction.ShowWaiting, clocks.Follow(cpt, Blank(cpt), T0.AddSeconds(2)));
    }

    [Fact]
    public void Each_unit_keeps_its_own_clock()
    {
        var clocks = new A300McduBlankClocks();
        var cpt = A300McduUnit.Captain;
        var fo = A300McduUnit.FirstOfficer;
        clocks.Tick(cpt, Blank(cpt), T0);
        clocks.Tick(fo, Page(fo), T0.AddMilliseconds(500));   // a page on one unit does not end the other's blank
        Assert.Equal(A300McduDisplayAction.ShowBlank, clocks.Judge(cpt, Blank(cpt), T0.AddMilliseconds(1000)));
        // The first officer going blank at 2 s starts ITS wait there, whatever the captain's clock reads.
        Assert.Equal(A300McduDisplayAction.HoldLastPage, clocks.Judge(fo, Blank(fo), T0.AddSeconds(2)));
        Assert.Equal(A300McduDisplayAction.ShowBlank, clocks.Judge(fo, Blank(fo), T0.AddMilliseconds(3000)));
    }

    [Fact]
    public void A_unit_with_no_screen_has_no_clock()
    {
        // A flight load or a reconnect forgets the screens, and with them how long a unit had been blank.
        var clocks = new A300McduBlankClocks();
        var fo = A300McduUnit.FirstOfficer;
        clocks.Tick(fo, Blank(fo), T0);
        clocks.Tick(fo, null, T0.AddMilliseconds(500));
        Assert.Equal(A300McduDisplayAction.ShowWaiting, clocks.Follow(fo, Blank(fo), T0.AddSeconds(2)));
    }

    [Fact]
    public void A_page_ticked_in_ends_that_units_blank()
    {
        var clocks = new A300McduBlankClocks();
        var fo = A300McduUnit.FirstOfficer;
        clocks.Tick(fo, Blank(fo), T0);
        clocks.Tick(fo, Page(fo), T0.AddMilliseconds(500));
        Assert.Equal(A300McduDisplayAction.ShowContent, clocks.Follow(fo, Page(fo), T0.AddSeconds(2)));
        Assert.Equal(A300McduDisplayAction.ShowWaiting, clocks.Follow(fo, Blank(fo), T0.AddMilliseconds(2100)));
    }

    [Theory]
    [InlineData(0x41330201u, A300McduUnit.Captain)]
    [InlineData(0x41330212u, A300McduUnit.FirstOfficer)]
    [InlineData(0x4D443201u, null)]
    public void Only_the_feeds_own_requests_are_claimed(uint request, A300McduUnit? unit) =>
        Assert.Equal(unit, A300McduDataManager.UnitForRequest(request));
}

/// <summary>The definition's MCDU key queue, against a recording sender and hand-released waits.</summary>
public class IniA300McduKeyTests
{
    private readonly IniA300Definition _def;
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly List<string> _sent = new();
    private readonly List<TaskCompletionSource> _waits = new();

    public IniA300McduKeyTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Send = (_, rpn) => _sent.Add(rpn),
            Delay = _ =>
            {
                var wait = new TaskCompletionSource();
                _waits.Add(wait);
                return wait.Task;
            },
        };
        _def.Attach(_sim);
    }

    private void Elapse() => _waits[^1].SetResult();

    [Fact]
    public void Keys_are_pressed_held_and_released_one_at_a_time()
    {
        Assert.True(_def.PressMcduKeys(A300McduUnit.FirstOfficer, new[] { "A", "B" }));
        Assert.Equal(new[] { "1 (>L:INI_MCDU2_A)" }, _sent);
        Elapse();   // hold
        Assert.Equal(new[] { "1 (>L:INI_MCDU2_A)", "0 (>L:INI_MCDU2_A)" }, _sent);
        Elapse();   // gap
        Assert.Equal("1 (>L:INI_MCDU2_B)", _sent[^1]);
        Elapse();
        Elapse();
        Assert.Equal(4, _sent.Count);
        Assert.Equal(0, _def.McduKeysPending);
    }

    [Fact]
    public void Nothing_is_queued_when_a_write_cannot_land()
    {
        _def.CanLand = _ => false;
        Assert.False(_def.PressMcduKeys(A300McduUnit.Captain, new[] { "A" }));
        Assert.False(_def.EnsureMcduExport());
        Assert.Empty(_sent);
    }

    [Fact]
    public void Disposing_while_a_key_is_held_releases_it_and_drops_the_rest()
    {
        _def.PressMcduKeys(A300McduUnit.Captain, new[] { "A", "B" });
        _def.Dispose();
        Assert.Equal(new[] { "1 (>L:INI_MCDU1_A)", "0 (>L:INI_MCDU1_A)" }, _sent);
        Elapse();
        Assert.Equal(2, _sent.Count);
    }

    // The aircraft publishes the screens only while L:INI_MCDU_OPTION is 1, and a flight load clears it
    // ([A300-4]): the definition remembers whether the export is on for this connection and flight.

    [Fact]
    public void Ensure_sends_once_and_again_after_a_context_reset()
    {
        Assert.True(_def.EnsureMcduExport());
        Assert.Equal(new[] { "1 (>L:INI_MCDU_OPTION)" }, _sent);
        Assert.True(_def.EnsureMcduExport());   // on already: nothing more to send
        Assert.Single(_sent);
        _def.OnSimContextReset();               // a disconnect or a flight load switches the option off
        Assert.True(_def.EnsureMcduExport());
        Assert.Equal(new[] { "1 (>L:INI_MCDU_OPTION)", "1 (>L:INI_MCDU_OPTION)" }, _sent);
    }

    [Fact]
    public void A_failed_export_is_retried()
    {
        _def.CanLand = _ => false;
        Assert.False(_def.EnsureMcduExport());
        Assert.Empty(_sent);
        _def.CanLand = _ => true;
        Assert.True(_def.EnsureMcduExport());
        Assert.Equal(new[] { "1 (>L:INI_MCDU_OPTION)" }, _sent);
    }

    [Fact]
    public void A_forced_ensure_always_sends()
    {
        Assert.True(_def.EnsureMcduExport());
        Assert.True(_def.EnsureMcduExport(force: true));
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public void A_forced_send_that_cannot_land_forgets_the_export()
    {
        Assert.True(_def.EnsureMcduExport());
        _def.CanLand = _ => false;
        Assert.True(_def.EnsureMcduExport());          // a plain ensure while on asks nothing of the write path
        Assert.Single(_sent);
        Assert.False(_def.EnsureMcduExport(force: true));   // a send that cannot land: no longer known to be on
        Assert.Single(_sent);
        _def.CanLand = _ => true;
        Assert.True(_def.EnsureMcduExport());          // so the next plain ensure sends again
        Assert.Equal(2, _sent.Count);
    }
}
