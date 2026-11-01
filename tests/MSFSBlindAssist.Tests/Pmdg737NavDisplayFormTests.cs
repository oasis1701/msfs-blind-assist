// The Navigation display window's logic: the map buttons show their state in their text (as the PMDG and
// FBW autopilot windows' buttons do), NVDA reads a changed text itself, a press that changed nothing is read
// back after 3 s (the 737 MCP windows' rule, P737-7), and a provider failure keeps the last rows.

using MSFSBlindAssist.Forms.PMDG737;
using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplayFormTests
{
    private static readonly NdPanelState Panel = new(Mode: 2, Range: 2, VorAdfLeft: 0, VorAdfRight: 0, Centered: false, Tfc: true, Wxr: false,
        Sta: false, Wpt: false, Arpt: false, Data: false, Pos: false, Terr: false, Vsd: false, Powered: true);

    [Fact]
    public void Ctr_text_has_three_states()
    {
        Assert.Equal("CTR: normal", PMDG737NavDisplayForm.ButtonText(NdButton.Ctr, Panel));
        Assert.Equal("CTR: centred", PMDG737NavDisplayForm.ButtonText(NdButton.Ctr, Panel with { Centered = true }));
        Assert.Equal("CTR: VSD on", PMDG737NavDisplayForm.ButtonText(NdButton.Ctr, Panel with { Centered = true, Vsd = true }));
    }

    [Fact]
    public void Button_text_unread_is_the_name()
    {
        Assert.Equal("STA", PMDG737NavDisplayForm.ButtonText(NdButton.Sta, null));
        Assert.Equal("CTR", PMDG737NavDisplayForm.ButtonText(NdButton.Ctr, null));
        Assert.Equal("STA: off", PMDG737NavDisplayForm.ButtonText(NdButton.Sta, Panel));
        Assert.Equal("TFC: on", PMDG737NavDisplayForm.ButtonText(NdButton.Tfc, Panel));
        Assert.Equal("ARPT: on", PMDG737NavDisplayForm.ButtonText(NdButton.Arpt, Panel with { Arpt = true }));
        foreach (var b in Enum.GetValues<NdButton>())
            Assert.StartsWith(PMDG737NavDisplayForm.ButtonText(b, null), PMDG737NavDisplayForm.ButtonText(b, Panel), StringComparison.Ordinal);
    }

    [Fact]
    public void Readback_texts()
    {
        Assert.Equal("STA on", PMDG737NavDisplayForm.Readback(NdButton.Sta, Panel with { Sta = true }));
        Assert.Equal("TERR off", PMDG737NavDisplayForm.Readback(NdButton.Terr, Panel));
        Assert.Equal("CTR: centred", PMDG737NavDisplayForm.Readback(NdButton.Ctr, Panel with { Centered = true }));
        Assert.Equal("CTR: VSD on", PMDG737NavDisplayForm.Readback(NdButton.Ctr, Panel with { Centered = true, Vsd = true }));
        Assert.Equal("CTR: normal", PMDG737NavDisplayForm.Readback(NdButton.Ctr, Panel));
    }

    [Fact]
    public void A_press_that_changed_the_state_is_left_to_nvda()
    {
        // NVDA reads a focused button's new text itself ("FPV: on" in the PFD window, ground session 2), so a read-back of
        // a change would say it twice ([CORE-7], the owner, 2026-10-30).
        var r = new NdButtonReadback();
        r.Pressed(NdButton.Sta, Panel, nowMs: 0);
        Assert.Null(r.Poll(Panel with { Sta = true }, nowMs: 1000));
        Assert.Null(r.Poll(Panel with { Sta = true }, nowMs: 3000));
        r.Pressed(NdButton.Ctr, Panel, nowMs: 5000);
        Assert.Null(r.Poll(Panel with { Centered = true }, nowMs: 6000));
        Assert.Null(r.Poll(Panel with { Centered = true }, nowMs: 9000));
    }

    [Fact]
    public void Readback_waits_for_the_change_then_says_the_state_anyway()
    {
        var r = new NdButtonReadback();
        r.Pressed(NdButton.Ctr, Panel, nowMs: 0);
        Assert.Null(r.Poll(Panel, nowMs: 1000)); // no change yet
        Assert.Null(r.Poll(Panel, nowMs: 2500));
        Assert.Equal("CTR: normal", r.Poll(Panel, nowMs: 3000)); // 3 s: the current state
        Assert.Null(r.Poll(Panel, nowMs: 4000));
    }

    [Fact]
    public void Readback_unread_panel_says_nothing()
    {
        // Pressed unread, the button's text goes from its bare name to its state, which NVDA reads.
        var r = new NdButtonReadback();
        r.Pressed(NdButton.Wxr, null, nowMs: 0);
        Assert.Null(r.Poll(null, nowMs: 3000));
        Assert.Null(r.Poll(Panel with { Wxr = true }, nowMs: 3500));
    }

    [Fact]
    public void A_second_press_replaces_the_first()
    {
        var r = new NdButtonReadback();
        r.Pressed(NdButton.Sta, Panel, nowMs: 0);
        r.Pressed(NdButton.Wpt, Panel, nowMs: 400);
        // STA changed, but WPT is the pending one, and WPT has not taken: it is read back at 3 s from its press.
        Assert.Null(r.Poll(Panel with { Sta = true }, nowMs: 1100));
        Assert.Null(r.Poll(Panel with { Sta = true }, nowMs: 3000));
        Assert.Equal("WPT off", r.Poll(Panel with { Sta = true }, nowMs: 3400));
    }

    [Fact]
    public void Provider_failure_keeps_the_last_lines()
    {
        var current = new[] { "Captain ND: MAP, range 20" };
        Assert.Same(current, PMDG737NavDisplayForm.NextLines(() => throw new InvalidOperationException("gone"), current));
        Assert.Equal(new[] { "Navigation display starting" },
            PMDG737NavDisplayForm.NextLines(() => new NavDisplayView(new[] { "Navigation display starting" }, null, null), current));
    }
}
