// Characterization tests for the Fenix ISIS's window and its variant gate: FenixChannelIsisForm (Forms/Fenix) and
// FenixChannelIsis.KeyMayOpen and RefusalFor (Services/FenixChannelIsis.cs). No sim and no socket: a monitor that is never
// started trusts nothing. The window is built, never shown (StartLive runs the first open's work without putting anything
// on screen), as the other display windows are tested.
//
// The contract: a Fenix with the round standby gauges (HasIsis false) never opens the window from Alt+I, which keeps its
// AI read, and its list entry says "This Fenix has no ISIS." instead; a standby not yet read (null) is left to the ISIS
// verdict. The window is a class of its own, titled "Fenix ISIS", its list named "ISIS display", and while the ISIS is
// not trusted it shows the one status row "ISIS: not available, <reason>".

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelIsisWindowTests
{
    private static DisplayListBox ListOf(Control form) => form.Controls.OfType<DisplayListBox>().Single();

    [Fact]
    public void Only_a_fenix_that_may_have_an_isis_opens_the_window()
    {
        Assert.True(FenixChannelIsis.KeyMayOpen(FenixVariant.Unknown with { HasIsis = true }));
        Assert.True(FenixChannelIsis.KeyMayOpen(FenixVariant.Unknown));
        Assert.False(FenixChannelIsis.KeyMayOpen(FenixVariant.Unknown with { HasIsis = false }));
    }

    [Fact]
    public void A_round_gauge_fenix_refuses_the_list_entry()
    {
        Assert.Equal("This Fenix has no ISIS.", FenixChannelIsis.RefusalFor(FenixVariant.Unknown with { HasIsis = false }));
        Assert.Null(FenixChannelIsis.RefusalFor(FenixVariant.Unknown with { HasIsis = true }));
        Assert.Null(FenixChannelIsis.RefusalFor(FenixVariant.Unknown));
    }

    [Fact]
    public void The_isis_window_is_named_for_its_display()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new FenixChannelIsisForm(neverStarted, () => FenixVariant.Unknown);

        Assert.Equal("Fenix ISIS", form.Text);
        Assert.Equal("ISIS display", ListOf(form).AccessibleName);

        form.StartLive();

        string[] rows = ListOf(form).Items.Cast<string>().ToArray();
        Assert.Single(rows);
        Assert.Equal($"ISIS: not available, {neverStarted.VerdictFor(FenixChannelDisplay.Isis).Reason}", rows[0]);
    }
}
