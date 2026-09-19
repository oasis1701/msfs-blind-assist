using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins what the MCDU window TELLS a pilot when a unit has nothing on it.
///
/// Live evidence this exists to cover (2026-09-05, MD-11F GE, build 4d09b482): across three
/// sessions the LEFT unit's export carried zero glyphs twice while Center and Right carried real
/// pages —
///
///   16:19  Left 204 glyphs 'A/C STATUS'   Center 102 'MENU'   Right 204 'A/C STATUS'
///   17:13  Left   0 glyphs ''             Center 102 'MENU'   Right 204 'A/C STATUS'
///   17:43  Left   0 glyphs ''             Center 102 'MENU'   Right 102 'MENU'
///
/// The window opens on Left. An all-zero screen rendered as "Title:", "1:" … "6:", "Scratchpad:"
/// with the status line reading "MCDU: connected" and NOTHING announced — eight blank rows and a
/// claim that all is well. To a blind pilot that is indistinguishable from a broken window, which
/// is exactly how it was reported: "my MCDU is not showing up in the app".
///
/// So a blank unit must SAY it is blank, and must name the units that do have something, because
/// the alternative — arrowing through eight empty rows — carries no information at all.
///
/// NOTE what this does NOT claim: it does not say WHY Left was empty. A genuinely unpowered CDU
/// and a delivery this app never received look identical here, and the app could not tell them
/// apart because it logged only the FIRST delivery per unit. Md11McduDataManager now logs every
/// blank/content transition; until a session with that build exists, attribution is open.
/// </summary>
public class Md11McduPresenceTests
{
    private static Md11McduScreen Screen(Md11McduUnit unit, params string[] rows)
    {
        var lines = new string[Md11McduLayout.Rows];
        for (var i = 0; i < lines.Length; i++) lines[i] = i < rows.Length ? rows[i] : string.Empty;
        return new Md11McduScreen { Unit = unit, Lines = lines };
    }

    [Fact]
    public void A_screen_that_never_arrived_is_NoData_not_Blank()
    {
        // The two are different facts and must stay different: "nothing has been delivered" is a
        // transport statement, "the unit is dark" is an aircraft statement.
        Assert.Equal(Md11McduPresenceState.NoData, Md11McduPresence.Classify(null));
    }

    [Fact]
    public void An_all_zero_screen_is_Blank()
    {
        Assert.Equal(Md11McduPresenceState.Blank, Md11McduPresence.Classify(Screen(Md11McduUnit.Left)));
    }

    [Fact]
    public void A_screen_of_only_spaces_is_Blank()
    {
        // Decode turns an empty cell (value 0) into a SPACE, so "blank" reaches us as whitespace,
        // never as an empty string. Testing IsNullOrEmpty here would classify the live 0-glyph
        // Left screen as content and defeat the whole check.
        var spaces = new string(' ', Md11McduLayout.Cols);
        Assert.Equal(Md11McduPresenceState.Blank,
            Md11McduPresence.Classify(Screen(Md11McduUnit.Left, spaces, spaces, spaces)));
    }

    [Fact]
    public void One_glyph_anywhere_is_Content()
    {
        Assert.Equal(Md11McduPresenceState.Content,
            Md11McduPresence.Classify(Screen(Md11McduUnit.Left, "", "", "          MENU")));
    }

    [Fact]
    public void A_blank_unit_names_itself_and_the_units_that_have_content()
    {
        // The live 17:13 shape: Left blank, the other two carrying pages one keystroke away.
        var rows = Md11McduPresence.Describe(
            Md11McduUnit.Left,
            Md11McduPresenceState.Blank,
            new[] { Md11McduUnit.Center, Md11McduUnit.Right });

        var text = string.Join(" ", rows);
        Assert.Contains("Left", text);
        Assert.Contains("blank", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Center", text);
        Assert.Contains("Right", text);
        // The chord is the whole point of naming them - saying "Center has content" without
        // saying how to get there leaves the pilot exactly where they were.
        Assert.Contains("Ctrl+Shift+C", text);
        Assert.Contains("Ctrl+Shift+R", text);
    }

    [Fact]
    public void A_blank_unit_with_no_other_content_does_not_invent_somewhere_to_go()
    {
        var text = string.Join(" ", Md11McduPresence.Describe(
            Md11McduUnit.Left, Md11McduPresenceState.Blank, Array.Empty<Md11McduUnit>()));

        Assert.Contains("blank", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ctrl+Shift+", text);
    }

    [Fact]
    public void A_blank_unit_never_offers_a_jump_back_to_itself()
    {
        var text = string.Join(" ", Md11McduPresence.Describe(
            Md11McduUnit.Left, Md11McduPresenceState.Blank,
            new[] { Md11McduUnit.Left, Md11McduUnit.Right }));

        Assert.DoesNotContain("Ctrl+Shift+L", text);
        Assert.Contains("Ctrl+Shift+R", text);
    }

    [Fact]
    public void NoData_says_nothing_has_arrived_rather_than_claiming_the_unit_is_dark()
    {
        var text = string.Join(" ", Md11McduPresence.Describe(
            Md11McduUnit.Center, Md11McduPresenceState.NoData, Array.Empty<Md11McduUnit>()));

        Assert.Contains("Center", text);
        Assert.DoesNotContain("blank", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Content_produces_no_advisory_rows_at_all()
    {
        // The normal path must stay untouched - a working CDU gains no extra line to arrow past.
        Assert.Empty(Md11McduPresence.Describe(
            Md11McduUnit.Right, Md11McduPresenceState.Content, new[] { Md11McduUnit.Right }));
    }
}

/// <summary>
/// Pins the ERASE-then-REDRAW behaviour of the MD-11's CDU.
///
/// Measured live (MD-11F GE, 2026-09-05 19:11-19:24, 37 page changes): every page change
/// publishes an ALL-ZERO frame and then the new page — min 202 ms, median 418 ms, max 438 ms —
/// and not one blank frame in the whole session failed to be followed by content. The aircraft
/// clears the screen before drawing on it.
///
/// So a blank frame is a repaint artifact, NOT a dark CDU, and the first version of the blank
/// handling treated each one as a state change worth speaking: the pilot got "Left MCDU is
/// blank" followed about 400 ms later by the page title, on every single page change, because
/// the blank branch also cleared the title latch. Reported as "very spammy... it says blank,
/// then something there".
///
/// It is not only speech: rendering the advisory would swap a 9-row list for a 1-row list and
/// back on every page change, destroying the screen-reader cursor twice per change.
///
/// Hence: a blank must SETTLE before it is believed. The threshold is measured, not guessed —
/// see BlankSettleHasRealMarginOverTheMeasuredEraseWindow.
/// </summary>
public class Md11McduEraseRedrawTests
{
    /// <summary>The longest erase window actually observed, in ms. The constant must clear this.</summary>
    private const int MeasuredMaxEraseMs = 438;

    [Fact]
    public void Content_always_renders_immediately()
    {
        Assert.Equal(Md11McduDisplayAction.ShowContent,
            Md11McduPresence.Decide(Md11McduPresenceState.Content, TimeSpan.Zero));
    }

    [Fact]
    public void A_just_arrived_blank_holds_the_last_page_instead_of_flickering()
    {
        Assert.Equal(Md11McduDisplayAction.HoldLastPage,
            Md11McduPresence.Decide(Md11McduPresenceState.Blank, TimeSpan.Zero));
    }

    [Fact]
    public void A_blank_lasting_the_longest_MEASURED_erase_window_still_holds()
    {
        // 438 ms is the worst real erase seen. If this ever returns ShowAdvisory the pilot gets
        // the flicker back on their longest page changes only - an intermittent bug, the worst
        // kind to chase.
        Assert.Equal(Md11McduDisplayAction.HoldLastPage,
            Md11McduPresence.Decide(Md11McduPresenceState.Blank, TimeSpan.FromMilliseconds(MeasuredMaxEraseMs)));
    }

    [Fact]
    public void A_blank_that_outlasts_the_settle_window_is_finally_believed()
    {
        Assert.Equal(Md11McduDisplayAction.ShowAdvisory,
            Md11McduPresence.Decide(Md11McduPresenceState.Blank,
                TimeSpan.FromMilliseconds(Md11McduPresence.BlankSettleMs)));

        Assert.Equal(Md11McduDisplayAction.ShowAdvisory,
            Md11McduPresence.Decide(Md11McduPresenceState.Blank, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void BlankSettle_has_real_margin_over_the_measured_erase_window()
    {
        // Not a round number picked for looks: it must sit clear of the measured maximum with
        // room for a slower frame, and clear of the form's own 250 ms poll so a settle decision
        // is never made on a single tick.
        Assert.True(Md11McduPresence.BlankSettleMs >= MeasuredMaxEraseMs * 3,
            $"settle {Md11McduPresence.BlankSettleMs} ms is too close to the {MeasuredMaxEraseMs} ms erase window");
        Assert.True(Md11McduPresence.BlankSettleMs >= 250 * 4);
    }

    [Fact]
    public void NoData_shows_the_advisory_at_once_because_there_is_no_page_to_hold()
    {
        Assert.Equal(Md11McduDisplayAction.ShowAdvisory,
            Md11McduPresence.Decide(Md11McduPresenceState.NoData, TimeSpan.Zero));
    }
}

/// <summary>
/// What the window shows the moment it STARTS following a unit — a unit switch, a first open, a
/// re-show (review round 2, C5).
///
/// Before, the switch drew only a unit with content; anything else waited for the poll, whose
/// "hold the page through a repaint" then held the PREVIOUS unit's rows under the new unit's
/// name — for up to the blank settle on a unit caught mid-erase, because each unit's blank clock
/// runs only while the window polls. A first open onto a blank unit showed an empty list for as
/// long. Now the list is replaced at once: the unit's page, its advisory, or a waiting row.
/// </summary>
public class Md11McduResyncTests
{
    [Fact]
    public void A_unit_with_a_page_is_drawn_at_once() =>
        Assert.Equal(Md11McduDisplayAction.ShowContent,
            Md11McduPresence.DecideOnResync(Md11McduPresenceState.Content, TimeSpan.Zero));

    [Fact]
    public void A_unit_that_never_delivered_shows_its_no_data_row_at_once() =>
        Assert.Equal(Md11McduDisplayAction.ShowAdvisory,
            Md11McduPresence.DecideOnResync(Md11McduPresenceState.NoData, TimeSpan.Zero));

    [Theory]
    [InlineData(0)]
    [InlineData(438)]
    [InlineData(Md11McduPresence.BlankSettleMs - 1)]
    public void A_blank_that_has_not_settled_shows_the_waiting_row_never_the_previous_page(int ms) =>
        Assert.Equal(Md11McduDisplayAction.ShowWaiting,
            Md11McduPresence.DecideOnResync(Md11McduPresenceState.Blank, TimeSpan.FromMilliseconds(ms)));

    [Fact]
    public void A_settled_blank_shows_the_blank_advisory_at_once() =>
        Assert.Equal(Md11McduDisplayAction.ShowAdvisory,
            Md11McduPresence.DecideOnResync(Md11McduPresenceState.Blank,
                TimeSpan.FromMilliseconds(Md11McduPresence.BlankSettleMs)));

    [Fact]
    public void The_per_tick_decision_never_answers_with_the_waiting_row()
    {
        // Poll keeps holding what the list shows (this unit's page, or the waiting row) through a
        // repaint; only the resync may put the waiting row up.
        foreach (var state in new[] { Md11McduPresenceState.NoData, Md11McduPresenceState.Blank, Md11McduPresenceState.Content })
            foreach (var ms in new[] { 0, 438, Md11McduPresence.BlankSettleMs, 30_000 })
                Assert.NotEqual(Md11McduDisplayAction.ShowWaiting,
                    Md11McduPresence.Decide(state, TimeSpan.FromMilliseconds(ms)));
    }

    [Theory]
    [InlineData(Md11McduUnit.Left, "Left")]
    [InlineData(Md11McduUnit.Center, "Center")]
    [InlineData(Md11McduUnit.Right, "Right")]
    public void The_waiting_row_names_the_unit_and_claims_nothing_about_it(Md11McduUnit unit, string name)
    {
        // The unit may be mid-repaint or genuinely dark; which is not known yet.
        var row = Md11McduPresence.Waiting(unit);
        Assert.Contains(name, row);
        Assert.DoesNotContain("blank", row, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no data", row, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_waiting_row_reads_as_a_sentence() =>
        Assert.Equal("Waiting for the Right MCDU.", Md11McduPresence.Waiting(Md11McduUnit.Right));
}
