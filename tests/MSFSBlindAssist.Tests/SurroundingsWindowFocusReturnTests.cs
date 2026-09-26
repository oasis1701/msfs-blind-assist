// Where Escape hands the foreground back to from the surroundings window (review item ML-3).
//
// The window opens SECONDS after its key press, and the handle captured at the press can name a
// window closed — or merely hidden, as the hide-on-close taxi dialog is — in the meantime. Handed
// to SetForegroundWindow, a dead handle leaves Windows to pick the next window (rarely the
// simulator), and a hidden one puts keyboard focus in a window nobody can see.

using System.Runtime.InteropServices;
using System.Windows.Forms;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class SurroundingsWindowFocusReturnTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    private static readonly IntPtr Simulator = new(0x1001), SayIntentionsWindow = new(0x2002), OldSurroundings = new(0x3003);

    private static Func<IntPtr, bool> Live(params IntPtr[] live) => h => live.Contains(h);

    [Fact]
    public void The_window_that_had_the_foreground_at_the_press_is_kept_while_it_is_still_live()
        => Assert.Equal(Simulator,
            SayIntentionsInfoForm.ChooseFocusReturn(Simulator, SayIntentionsWindow, IntPtr.Zero, Live(Simulator, SayIntentionsWindow)));

    [Fact]
    public void A_press_time_window_closed_during_the_lookup_gives_way_to_the_foreground_now()
        // The SayIntentions window had the foreground at the press and was closed with Escape while
        // the catalog built; closing it handed the foreground back to the simulator.
        => Assert.Equal(Simulator,
            SayIntentionsInfoForm.ChooseFocusReturn(SayIntentionsWindow, Simulator, IntPtr.Zero, Live(Simulator)));

    [Fact]
    public void A_replacement_keeps_the_handle_it_inherited_while_that_is_still_live()
        => Assert.Equal(Simulator,
            SayIntentionsInfoForm.ChooseFocusReturn(Simulator, OldSurroundings, OldSurroundings, Live(Simulator, OldSurroundings)));

    [Fact]
    public void The_window_being_replaced_is_never_chosen_even_while_it_is_still_live()
        // A re-press from inside the old surroundings window: it holds the foreground, and the handle
        // it inherited has closed. It is about to be destroyed, so it cannot be the answer.
        => Assert.Equal(IntPtr.Zero,
            SayIntentionsInfoForm.ChooseFocusReturn(SayIntentionsWindow, OldSurroundings, OldSurroundings, Live(OldSurroundings)));

    [Fact]
    public void With_nothing_live_the_window_hands_back_nothing()
        => Assert.Equal(IntPtr.Zero,
            SayIntentionsInfoForm.ChooseFocusReturn(SayIntentionsWindow, IntPtr.Zero, IntPtr.Zero, Live()));

    [Fact]
    public void A_shown_window_is_live_and_zero_a_hidden_window_and_a_destroyed_one_are_not()
    {
        Assert.True(SayIntentionsInfoForm.IsLiveWindow(GetDesktopWindow()));   // always exists, always shown
        Assert.False(SayIntentionsInfoForm.IsLiveWindow(IntPtr.Zero));

        // Created but never shown (a parentless Control lives in WinForms' hidden parking window) —
        // the shape a hide-on-close dialog has once hidden: still a window, so a bare IsWindow would
        // have called it live. A Control rather than a Form: this suite already creates control
        // handles (DisplayTextSetPreserveCaretTests), and nothing here may put a window on screen.
        var control = new Control();
        IntPtr handle = control.Handle;
        Assert.False(SayIntentionsInfoForm.IsLiveWindow(handle));
        control.Dispose();                                                      // destroys the window
        Assert.False(SayIntentionsInfoForm.IsLiveWindow(handle));
    }
}
