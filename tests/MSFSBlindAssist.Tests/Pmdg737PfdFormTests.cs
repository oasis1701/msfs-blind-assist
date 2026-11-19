// The Primary flight display window's logic: the buttons show their state in their text (as the Navigation display
// window's do), a press is read back once after it settles (MINS RST: the minimums as the PFD then shows them), a
// provider failure keeps the last rows, and the speed reference value is usable only where the knob sets something.

using MSFSBlindAssist.Forms.PMDG737;
using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.Pmdg737PfdTestMemory;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdFormTests
{
    private static readonly PfdButtonState State = new(BaroStd: true, Fpv: false, Mtrs: false, FlightDirector: true, Minimums: "BARO 200");

    [Fact]
    public void Button_text_unread_is_the_name()
    {
        Assert.Equal("MINS RST", PMDG737PfdForm.ButtonText(PfdButton.MinsReset, null));
        Assert.Equal("STD", PMDG737PfdForm.ButtonText(PfdButton.Std, null));
        Assert.Equal("FPV", PMDG737PfdForm.ButtonText(PfdButton.Fpv, null));
        Assert.Equal("MTRS", PMDG737PfdForm.ButtonText(PfdButton.Mtrs, null));
        Assert.Equal("Flight director", PMDG737PfdForm.ButtonText(PfdButton.FlightDirector, null));
        // The FD switch unread while the panel reads.
        Assert.Equal("Flight director", PMDG737PfdForm.ButtonText(PfdButton.FlightDirector, State with { FlightDirector = null }));
        foreach (var b in Enum.GetValues<PfdButton>())
            Assert.StartsWith(PMDG737PfdForm.ButtonText(b, null), PMDG737PfdForm.ButtonText(b, State), StringComparison.Ordinal);
    }

    [Fact]
    public void Button_texts_show_state()
    {
        Assert.Equal("STD: on", PMDG737PfdForm.ButtonText(PfdButton.Std, State));
        Assert.Equal("FPV: off", PMDG737PfdForm.ButtonText(PfdButton.Fpv, State));
        Assert.Equal("FPV: on", PMDG737PfdForm.ButtonText(PfdButton.Fpv, State with { Fpv = true }));
        Assert.Equal("MTRS: off", PMDG737PfdForm.ButtonText(PfdButton.Mtrs, State));
        Assert.Equal("Flight director: on", PMDG737PfdForm.ButtonText(PfdButton.FlightDirector, State));
        Assert.Equal("Flight director: off", PMDG737PfdForm.ButtonText(PfdButton.FlightDirector, State with { FlightDirector = false }));
        Assert.Equal("MINS RST", PMDG737PfdForm.ButtonText(PfdButton.MinsReset, State));
    }

    [Fact]
    public void Readback_texts()
    {
        Assert.Equal("STD on", PMDG737PfdForm.Readback(PfdButton.Std, State));
        Assert.Equal("FPV off", PMDG737PfdForm.Readback(PfdButton.Fpv, State));
        Assert.Equal("MTRS on", PMDG737PfdForm.Readback(PfdButton.Mtrs, State with { Mtrs = true }));
        Assert.Equal("Flight director on", PMDG737PfdForm.Readback(PfdButton.FlightDirector, State));
        Assert.Equal("Flight director", PMDG737PfdForm.Readback(PfdButton.FlightDirector, State with { FlightDirector = null }));
        Assert.Equal("Minimums: BARO 200", PMDG737PfdForm.Readback(PfdButton.MinsReset, State));
        Assert.Equal("Minimums: off", PMDG737PfdForm.Readback(PfdButton.MinsReset, State with { Minimums = null }));
    }

    [Fact]
    public void A_press_that_changed_the_state_is_left_to_nvda()
    {
        // NVDA reads a focused button's new text itself ("FPV: on", ground session 2), so a read-back of a change would
        // say it twice ([CORE-7]).
        foreach (var (b, changed) in new[] { (PfdButton.Std, State with { BaroStd = false }), (PfdButton.Fpv, State with { Fpv = true }),
                     (PfdButton.Mtrs, State with { Mtrs = true }), (PfdButton.FlightDirector, State with { FlightDirector = false }) })
        {
            var r = new PfdButtonReadback();
            r.Pressed(b, State, nowMs: 0);
            Assert.Null(r.Poll(changed, nowMs: 1000));
            Assert.Null(r.Poll(changed, nowMs: 3000));
            Assert.Null(r.Poll(changed, nowMs: 5000));
        }
    }

    [Fact]
    public void A_press_that_changed_nothing_is_read_back_at_3_s()
    {
        // The label does not change, so NVDA says nothing: the window says the state, so the press is never silent (the
        // owner, 2026-10-30; the 737 MCP windows' rule, P737-7).
        foreach (var b in new[] { PfdButton.Std, PfdButton.Fpv, PfdButton.Mtrs, PfdButton.FlightDirector })
        {
            var r = new PfdButtonReadback();
            r.Pressed(b, State, nowMs: 0);
            Assert.Null(r.Poll(State, nowMs: 1000));
            Assert.Null(r.Poll(State, nowMs: 2500));
            Assert.Equal(PMDG737PfdForm.Readback(b, State), r.Poll(State, nowMs: 3000));
            Assert.Null(r.Poll(State, nowMs: 4000)); // once
        }
    }

    [Fact]
    public void Mins_reset_reads_back_a_change_too()
    {
        // MINS RST's text never changes, so NVDA has nothing to read: the window says the minimums the PFD shows.
        var r = new PfdButtonReadback();
        r.Pressed(PfdButton.MinsReset, State, nowMs: 0);
        Assert.Null(r.Poll(State with { Minimums = null }, nowMs: 500)); // too soon: the state may still move
        Assert.Equal("Minimums: off", r.Poll(State with { Minimums = null }, nowMs: 1000));
        Assert.Null(r.Poll(State with { Minimums = null }, nowMs: 2000)); // once
    }

    [Fact]
    public void Readback_gives_up_at_3_s_with_the_state_then()
    {
        var r = new PfdButtonReadback();
        r.Pressed(PfdButton.Std, State, nowMs: 0);
        Assert.Null(r.Poll(State, nowMs: 1000)); // no change yet
        Assert.Null(r.Poll(State, nowMs: 2500));
        Assert.Equal("STD on", r.Poll(State, nowMs: 3000)); // 3 s: the current state
        Assert.Null(r.Poll(State, nowMs: 4000));
    }

    [Fact]
    public void Mins_reset_reads_back_the_minimums_the_pfd_shows()
    {
        // PMDG's MINS RST turns minimums that are off back on (BARO 200) and clears the alert; it never zeroes them.
        var r = new PfdButtonReadback();
        r.Pressed(PfdButton.MinsReset, State with { Minimums = null }, nowMs: 0);
        Assert.Equal("Minimums: BARO 200", r.Poll(State, nowMs: 1000));
        // Already on: nothing changes on the PFD, the minimums are said at 3 s.
        r.Pressed(PfdButton.MinsReset, State with { Minimums = "BARO 201" }, nowMs: 5000);
        Assert.Null(r.Poll(State with { Minimums = "BARO 201" }, nowMs: 6000));
        Assert.Equal("Minimums: BARO 201", r.Poll(State with { Minimums = "BARO 201" }, nowMs: 8000));
    }

    [Fact]
    public void Readback_unread_state_says_nothing_until_it_reads()
    {
        var r = new PfdButtonReadback();
        r.Pressed(PfdButton.MinsReset, null, nowMs: 0);
        Assert.Null(r.Poll(null, nowMs: 3000));
        Assert.Equal("Minimums: BARO 200", r.Poll(State, nowMs: 3500));
        // A stateful button pressed unread: its label goes from the bare name to its state, which NVDA reads.
        r.Pressed(PfdButton.Mtrs, null, nowMs: 5000);
        Assert.Null(r.Poll(null, nowMs: 8000));
        Assert.Null(r.Poll(State with { Mtrs = true }, nowMs: 8500));
    }

    [Fact]
    public void Provider_failure_keeps_the_last_lines()
    {
        var current = new[] { "Speed tape: 160 knots" };
        Assert.Same(current, PMDG737PfdForm.NextLines(() => throw new InvalidOperationException("gone"), current));
        Assert.Equal(new[] { "Primary flight display starting" },
            PMDG737PfdForm.NextLines(() => new PfdView(new[] { "Primary flight display starting" }, null, null), current));
    }

    [Fact]
    public void Speed_reference_value_only_where_the_knob_sets_something()
    {
        // SDK positions 0 SET, 1 AUTO, 2 V1, 3 VR, 4 WT, 5 VREF, 6 BUG; the knob sets V1, VR, WT, VREF and BUG.
        var layout = M.Layout;
        Assert.True(PMDG737PfdForm.SpeedReferenceFieldEnabled(5, layout));
        Assert.True(PMDG737PfdForm.SpeedReferenceFieldEnabled(4, layout));
        Assert.False(PMDG737PfdForm.SpeedReferenceFieldEnabled(1, layout));
        Assert.False(PMDG737PfdForm.SpeedReferenceFieldEnabled(0, layout));
        Assert.False(PMDG737PfdForm.SpeedReferenceFieldEnabled(null, layout));
        Assert.False(PMDG737PfdForm.SpeedReferenceFieldEnabled(5, null)); // the reader not running
    }
}
