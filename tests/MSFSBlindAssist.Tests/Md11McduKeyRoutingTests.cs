// Characterization tests for the MD-11 MCDU window's form-wide key table
// (Md11McduForm.KeyRouting).
//
// WHY THIS SUITE EXISTS. The window sets KeyPreview, so every chord reaches the form BEFORE the
// focused control — and it is the first CDU window in this app to host a combo box (the three MD-11
// MCDUs need a unit selector). The slew bindings copied from the FBW MCDU form matched Alt+Down
// with no guard on what had focus, so the canonical gesture for opening a combo box slewed the
// aircraft's MCDU and suppressed the key: a blind pilot could not open the unit selector at all,
// and got an aircraft command instead. The same branches matched PageUp/PageDown with no modifier
// guard, so Ctrl+PageDown slewed too.
//
// Both halves are pinned below, plus the same two questions asked of every other binding in the
// table. Nothing here constructs the form: KeyRouting is a pure function of the chord, which kind
// of control has focus, and the LSK-layout setting.

using System.Windows.Forms;
using KeyRouting = MSFSBlindAssist.Forms.MD11.Md11McduForm.KeyRouting;

namespace MSFSBlindAssist.Tests;

public class Md11McduKeyRoutingTests
{
    private static KeyRouting.Action Resolve(
        Keys key,
        bool alt = false,
        bool control = false,
        bool shift = false,
        KeyRouting.ComboFocus combo = KeyRouting.ComboFocus.None,
        bool alternateLsk = false)
        => KeyRouting.Resolve(key, alt, control, shift, combo, alternateLsk);

    private static void AssertPress(string expectedKey, KeyRouting.Action action)
    {
        Assert.Equal(KeyRouting.ActionKind.Press, action.Kind);
        Assert.Equal(expectedKey, action.Key);
    }

    private static void AssertPass(KeyRouting.Action action) =>
        Assert.Equal(KeyRouting.ActionKind.Pass, action.Kind);

    // -----------------------------------------------------------------------------
    // The defect: a focused combo box must keep the gestures that open its own list
    // -----------------------------------------------------------------------------

    [Fact]
    public void Alt_Down_slews_when_the_combo_does_not_have_focus()
    {
        AssertPress("DOWN", Resolve(Keys.Down, alt: true));
    }

    [Fact]
    public void Alt_Down_reaches_a_focused_combo_instead_of_slewing()
    {
        AssertPass(Resolve(Keys.Down, alt: true, combo: KeyRouting.ComboFocus.Closed));
    }

    [Fact]
    public void Alt_Up_reaches_a_focused_combo_instead_of_slewing()
    {
        AssertPress("UP", Resolve(Keys.Up, alt: true));
        AssertPass(Resolve(Keys.Up, alt: true, combo: KeyRouting.ComboFocus.Closed));
    }

    [Fact]
    public void Alt_Down_still_reaches_the_combo_once_its_list_is_dropped()
    {
        // The same chord closes the list again; taking it here would strand a pilot inside it.
        AssertPass(Resolve(Keys.Down, alt: true, combo: KeyRouting.ComboFocus.Dropped));
    }

    [Fact]
    public void The_page_keys_reach_a_focused_combo_which_uses_them_to_move_its_selection()
    {
        AssertPass(Resolve(Keys.PageUp, combo: KeyRouting.ComboFocus.Closed));
        AssertPass(Resolve(Keys.PageDown, combo: KeyRouting.ComboFocus.Closed));
    }

    [Fact]
    public void F4_reaches_a_focused_combo_rather_than_pressing_L4()
    {
        // F4 is the other open/close gesture, and the alternate LSK layout binds it to L4.
        AssertPress("LSK_4L", Resolve(Keys.F4, alternateLsk: true));
        AssertPass(Resolve(Keys.F4, combo: KeyRouting.ComboFocus.Closed, alternateLsk: true));
    }

    [Fact]
    public void A_character_key_reaches_a_focused_combo_for_type_ahead()
    {
        AssertPass(Resolve(Keys.L, combo: KeyRouting.ComboFocus.Closed));
        AssertPass(Resolve(Keys.C, shift: true, combo: KeyRouting.ComboFocus.Closed));
    }

    [Fact]
    public void The_guard_is_narrow_and_leaves_the_window_keys_live_on_the_combo()
    {
        // Every one of these is a window binding a pilot sitting on the selector may still want,
        // and none is a gesture the list itself uses. A blanket "focus is on a combo, take
        // nothing" guard would silently kill them.
        AssertPress("NEXTPAGE", Resolve(Keys.Right, alt: true, combo: KeyRouting.ComboFocus.Closed));
        AssertPress("SEC_FPLN", Resolve(Keys.F, alt: true, shift: true, combo: KeyRouting.ComboFocus.Closed));
        AssertPress("LSK_1R", Resolve(Keys.D1, alt: true, combo: KeyRouting.ComboFocus.Closed));
        AssertPress("LSK_1L", Resolve(Keys.D1, control: true, combo: KeyRouting.ComboFocus.Closed));

        Assert.Equal(KeyRouting.ActionKind.SelectUnit,
            Resolve(Keys.C, control: true, shift: true, combo: KeyRouting.ComboFocus.Closed).Kind);
        Assert.Equal(KeyRouting.ActionKind.FocusScratchpad,
            Resolve(Keys.S, alt: true, combo: KeyRouting.ComboFocus.Closed).Kind);
    }

    // -----------------------------------------------------------------------------
    // The second half: a binding matches its own chord and nothing more
    // -----------------------------------------------------------------------------

    [Fact]
    public void Plain_page_keys_slew()
    {
        // Every CDU window in this app binds the UNMODIFIED page keys to the content being read;
        // this one must not move that behind a modifier either.
        AssertPress("UP", Resolve(Keys.PageUp));
        AssertPress("DOWN", Resolve(Keys.PageDown));
    }

    [Fact]
    public void Ctrl_PageDown_does_not_slew()
    {
        AssertPass(Resolve(Keys.PageDown, control: true));
        AssertPass(Resolve(Keys.PageUp, control: true));
    }

    [Fact]
    public void Shift_and_Alt_page_keys_do_not_slew()
    {
        AssertPass(Resolve(Keys.PageDown, shift: true));
        AssertPass(Resolve(Keys.PageUp, shift: true));
        AssertPass(Resolve(Keys.PageDown, alt: true));
    }

    [Fact]
    public void A_modified_Alt_slew_chord_does_not_slew()
    {
        // AltGr arrives as Ctrl+Alt on the layouts this app's users type on, so an Alt binding
        // that ignores Ctrl is reachable from an ordinary character key.
        AssertPass(Resolve(Keys.Down, alt: true, control: true));
        AssertPass(Resolve(Keys.Up, alt: true, control: true));
        AssertPass(Resolve(Keys.Down, alt: true, shift: true));
        AssertPass(Resolve(Keys.Right, alt: true, control: true));
    }

    [Fact]
    public void Shift_F10_opens_the_context_menu_rather_than_pressing_R4()
    {
        // The alternate layout binds F7..F12 to R1..R6, and Shift+F10 is how a screen-reader user
        // reaches the scratchpad's edit commands.
        AssertPress("LSK_4R", Resolve(Keys.F10, alternateLsk: true));
        AssertPass(Resolve(Keys.F10, shift: true, alternateLsk: true));
    }

    [Fact]
    public void AltGr_chords_do_not_press_a_line_select_key_or_SEC_FPLN()
    {
        AssertPass(Resolve(Keys.D1, alt: true, control: true));                 // AltGr+1
        AssertPass(Resolve(Keys.D1, alt: true, shift: true));
        AssertPass(Resolve(Keys.F, alt: true, control: true, shift: true));     // AltGr+Shift+F
        AssertPass(Resolve(Keys.S, alt: true, control: true));                  // AltGr+S
        AssertPass(Resolve(Keys.L, control: true, shift: true, alt: true));     // AltGr+Shift+L
    }

    [Fact]
    public void A_modified_Escape_does_not_close_the_window()
    {
        Assert.Equal(KeyRouting.ActionKind.Close, Resolve(Keys.Escape).Kind);
        AssertPass(Resolve(Keys.Escape, control: true));
        AssertPass(Resolve(Keys.Escape, alt: true));
        AssertPass(Resolve(Keys.Escape, shift: true));
    }

    // -----------------------------------------------------------------------------
    // The rest of the table, unchanged by the fix
    // -----------------------------------------------------------------------------

    [Fact]
    public void Escape_closes_the_window_but_a_dropped_list_keeps_it()
    {
        Assert.Equal(KeyRouting.ActionKind.Close, Resolve(Keys.Escape).Kind);
        Assert.Equal(KeyRouting.ActionKind.Close,
            Resolve(Keys.Escape, combo: KeyRouting.ComboFocus.Closed).Kind);
        AssertPass(Resolve(Keys.Escape, combo: KeyRouting.ComboFocus.Dropped));
    }

    [Theory]
    [InlineData(Keys.F1, "LSK_1L")]
    [InlineData(Keys.F6, "LSK_6L")]
    [InlineData(Keys.F7, "LSK_1R")]
    [InlineData(Keys.F12, "LSK_6R")]
    public void The_alternate_layout_maps_the_function_keys_to_both_sides(Keys key, string expected)
    {
        AssertPress(expected, Resolve(key, alternateLsk: true));
    }

    [Fact]
    public void The_alternate_layout_replaces_the_default_one_rather_than_adding_to_it()
    {
        AssertPass(Resolve(Keys.F1));                                     // default layout: no F-keys
        AssertPass(Resolve(Keys.D1, control: true, alternateLsk: true));  // alternate layout: no digits
        AssertPass(Resolve(Keys.D1, alt: true, alternateLsk: true));
    }

    [Theory]
    [InlineData(Keys.D1, "LSK_1L")]
    [InlineData(Keys.D6, "LSK_6L")]
    public void The_default_layout_maps_Ctrl_digits_to_the_left_keys(Keys key, string expected)
    {
        AssertPress(expected, Resolve(key, control: true));
    }

    [Theory]
    [InlineData(Keys.D1, "LSK_1R")]
    [InlineData(Keys.D6, "LSK_6R")]
    public void The_default_layout_maps_Alt_digits_to_the_right_keys(Keys key, string expected)
    {
        AssertPress(expected, Resolve(key, alt: true));
    }

    [Fact]
    public void Digit_seven_is_not_a_line_select_key_on_either_layout()
    {
        // Six rows a side: D7 and F13 must not run off the end of the table.
        AssertPass(Resolve(Keys.D7, control: true));
        AssertPass(Resolve(Keys.D7, alt: true));
        AssertPass(Resolve(Keys.F13, alternateLsk: true));
    }

    [Fact]
    public void Alt_Right_pages_forward_and_Alt_Left_is_unbound()
    {
        // The MD-11 has no PREV PAGE key, so Alt+Left is deliberately unbound rather than faked.
        AssertPress("NEXTPAGE", Resolve(Keys.Right, alt: true));
        AssertPass(Resolve(Keys.Left, alt: true));
    }

    [Fact]
    public void Alt_Shift_F_presses_SEC_FPLN()
    {
        AssertPress("SEC_FPLN", Resolve(Keys.F, alt: true, shift: true));
    }

    [Theory]
    [InlineData(Keys.L, 0)]
    [InlineData(Keys.C, 1)]
    [InlineData(Keys.R, 2)]
    public void Ctrl_Shift_L_C_R_select_the_three_units(Keys key, int expectedIndex)
    {
        var action = Resolve(key, control: true, shift: true);

        Assert.Equal(KeyRouting.ActionKind.SelectUnit, action.Kind);
        Assert.Equal(expectedIndex, action.Unit);
    }

    [Fact]
    public void Alt_S_and_Alt_Home_move_focus()
    {
        Assert.Equal(KeyRouting.ActionKind.FocusScratchpad, Resolve(Keys.S, alt: true).Kind);
        Assert.Equal(KeyRouting.ActionKind.FocusDisplay, Resolve(Keys.Home, alt: true).Kind);
        AssertPass(Resolve(Keys.S, alt: true, shift: true));
        AssertPass(Resolve(Keys.Home, alt: true, shift: true));
    }

    [Fact]
    public void The_scratchpad_edit_keys_are_never_taken_window_wide()
    {
        // Backspace and Delete are bound on the DISPLAY only: bound here, a forward-delete while
        // editing typed text fired a CLR loop at the aircraft's own scratchpad.
        AssertPass(Resolve(Keys.Back));
        AssertPass(Resolve(Keys.Delete));
        AssertPass(Resolve(Keys.Home));
        AssertPass(Resolve(Keys.End));
        AssertPass(Resolve(Keys.Left));
        AssertPass(Resolve(Keys.Right));
        AssertPass(Resolve(Keys.A, control: true));
        AssertPass(Resolve(Keys.V, control: true));
    }

    [Fact]
    public void The_display_keeps_its_own_reading_keys()
    {
        // Plain arrows and type-ahead belong to the list the pilot reads the screen with.
        AssertPass(Resolve(Keys.Up));
        AssertPass(Resolve(Keys.Down));
        AssertPass(Resolve(Keys.Space));
        AssertPass(Resolve(Keys.Return));
        AssertPass(Resolve(Keys.Tab));
    }

    // -----------------------------------------------------------------------------
    // The predicate itself
    // -----------------------------------------------------------------------------

    [Fact]
    public void The_combo_predicate_covers_the_whole_list_keyboard_interface()
    {
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.Down, alt: true, control: false, shift: false, droppedDown: false));
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.Up, alt: true, control: false, shift: false, droppedDown: false));
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.F4, alt: false, control: false, shift: false, droppedDown: false));
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.PageDown, alt: false, control: false, shift: false, droppedDown: false));
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.Home, alt: false, control: false, shift: false, droppedDown: false));
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.Space, alt: false, control: false, shift: false, droppedDown: false));
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.D1, alt: false, control: false, shift: false, droppedDown: false));
    }

    [Fact]
    public void The_combo_predicate_claims_Escape_and_Enter_only_while_the_list_is_dropped()
    {
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.Escape, alt: false, control: false, shift: false, droppedDown: true));
        Assert.False(KeyRouting.ComboBoxNeedsKey(Keys.Escape, alt: false, control: false, shift: false, droppedDown: false));
        Assert.True(KeyRouting.ComboBoxNeedsKey(Keys.Return, alt: false, control: false, shift: false, droppedDown: true));
        Assert.False(KeyRouting.ComboBoxNeedsKey(Keys.Return, alt: false, control: false, shift: false, droppedDown: false));
    }

    [Fact]
    public void The_combo_predicate_claims_no_chord_carrying_Ctrl()
    {
        Assert.False(KeyRouting.ComboBoxNeedsKey(Keys.Down, alt: true, control: true, shift: false, droppedDown: false));
        Assert.False(KeyRouting.ComboBoxNeedsKey(Keys.PageDown, alt: false, control: true, shift: false, droppedDown: false));
        Assert.False(KeyRouting.ComboBoxNeedsKey(Keys.D1, alt: false, control: true, shift: false, droppedDown: false));
        Assert.False(KeyRouting.ComboBoxNeedsKey(Keys.L, alt: false, control: true, shift: true, droppedDown: false));
    }
}
