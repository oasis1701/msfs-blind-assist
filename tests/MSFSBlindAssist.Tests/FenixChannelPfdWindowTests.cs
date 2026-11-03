// Characterization tests for the Fenix PFD's window and the answer to its keys: FenixChannelPfdForm (Forms/Fenix) and
// FenixA320Definition.AnswerChannelPfdKey (Aircraft/FenixA320Definition.ChannelPfd.cs). No sim and no socket: a monitor
// that is never started trusts nothing, and one the test feeds by hand trusts what the gate lets it. The window is
// built, never shown (StartLive runs the first open's work without putting anything on screen), as the other display
// windows are tested (FenixChannelWindowTests). These tests call the answer and the window directly, not through the
// definition's key seam or the Fenix windows list that lead to them, and what a pilot hears from the keys is checked in
// the sim ([CORE-5]).
//
// The contract: Alt+P (ReadDisplayPFD) is answered only by opening the window, which needs a trusted PFD: while the PFD
// is not trusted, or there is no monitor, it is false and nothing is said, so the key goes on to its AI read. Each of
// the six speed keys is always answered (true): it speaks its tape row with an interrupting announcement, or "<name> not
// available" in the same way when the PFD is not trusted, cannot be read, draws no row for that speed, or there is no
// monitor. One snapshot of the PFD decides both whether it is trusted and what it holds. A PFD that cannot be read is
// reported under the window's name, as the window reports it. Any other action is false and silent. The window is a
// class of its own for the PFD, titled "Fenix PFD", and shows the PFD's rows. Both the keys and the window read the
// PFD's own verdict, not another display's: with the captain's PFD dark and the other units lit, Alt+P is false and
// silent, a speed key says it is not available, and the window shows the one status row.

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelPfdWindowTests
{
    // ---- The key's answer without a trusted PFD -------------------------------------------------

    // A context with only the pieces the PFD's answer touches: the announcer and the monitor. The hotkey manager is
    // null, so a call to it would throw: a key that opens nothing must not reach it.
    private static FenixA320Definition.ChannelKeyContext Context(SpeechCapture speech, FenixChannelMonitor? monitor)
        => new(null!, speech, null!, null!, monitor);

    [Fact]
    public void Alt_p_says_nothing_and_opens_nothing_while_the_pfd_is_not_trusted()
    {
        // No monitor (before Start, after Stop), and a monitor that was never started, which has nothing connected: false,
        // and not a word, so the key goes on to the AI read as it does with no feature.
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        Assert.False(neverStarted.VerdictFor(FenixChannelDisplay.Pfd).Trusted);
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(definition.AnswerChannelPfdKey(HotkeyAction.ReadDisplayPFD, Context(speech, null)));
        Assert.False(definition.AnswerChannelPfdKey(HotkeyAction.ReadDisplayPFD, Context(speech, neverStarted)));

        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadSpeedGD, "Green dot not available")]
    [InlineData(HotkeyAction.ReadSpeedS, "S speed not available")]
    [InlineData(HotkeyAction.ReadSpeedF, "F speed not available")]
    [InlineData(HotkeyAction.ReadSpeedVLS, "VLS not available")]
    [InlineData(HotkeyAction.ReadSpeedVS, "Alpha prot not available")]
    [InlineData(HotkeyAction.ReadSpeedVFE, "VFE next not available")]
    public void Each_speed_key_is_answered(HotkeyAction action, string words)
    {
        // The key is always answered, never left to carry on: with no monitor, and with one that trusts nothing, it says
        // what it cannot read, at once.
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        var definition = new FenixA320Definition();

        foreach (FenixChannelMonitor? monitor in new[] { null, neverStarted })
        {
            var speech = new SpeechCapture();

            Assert.True(definition.AnswerChannelPfdKey(action, Context(speech, monitor)));

            Assert.Equal(new[] { words }, speech.Interrupts);
            // Everything went out through AnnounceImmediate: nothing was queued beside it.
            Assert.Equal(speech.Interrupts, speech.All);
        }
    }

    [Theory]
    [InlineData(HotkeyAction.ReadSpeed)]            // the aircraft's own speed key, not one of the tape's
    [InlineData(HotkeyAction.ReadHeading)]
    [InlineData(HotkeyAction.ReadDisplayND)]        // another display's key
    [InlineData(HotkeyAction.ReadDisplayUpperECAM)]
    [InlineData(HotkeyAction.ShowDCDU)]
    public void Another_key_is_not_the_pfds(HotkeyAction action)
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        Assert.False(definition.AnswerChannelPfdKey(action, Context(speech, null)));
        Assert.False(definition.AnswerChannelPfdKey(action, Context(speech, neverStarted)));

        Assert.Empty(speech.All);
    }

    // ---- The key's answer with a trusted PFD ----------------------------------------------------

    // The cruise of PfdPushes with its green dot, VLS and alpha prot drawn and the S, F and VFE next speeds not.
    private static PfdPushes.Push Cruise() => new();

    // A whole display push that passes the gate (FenixChannelProtoWriter.GoodPush) with the PFD's own fields in the same
    // RemoteData message: the push of a real Fenix, which has both. The PFD's heading is left out, since GoodPush carries
    // the one the gate cross-checks.
    private static byte[] PushWith(PfdPushes.Push pfd)
    {
        byte[] good = FenixChannelProtoWriter.GoodPush();
        int at = 1;
        while ((good[at] & 0x80) != 0) at++;
        at++;

        byte[] remote = [.. good[at..], .. pfd.Omit("194").RemoteBytes()];
        return new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().Raw(remote)).ToArray();
    }

    // A monitor with SimConnect's values agreeing with GoodPush, the data call connected and the build checked, before
    // any push has arrived.
    private static FenixChannelMonitor ConnectedMonitor(MonitorTestKit.Lines lines)
    {
        FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        MonitorTestKit.AgreeWithGoodPush(monitor);
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.True(
            SpinWait.SpinUntil(
                () => lines.All.Contains($"build {MonitorTestKit.CheckedBuild}: checked"), MonitorTestKit.Generous),
            "the build was never checked. The log: " + lines);
        return monitor;
    }

    // A monitor the PFD trusts: SimConnect's values agree with the push, the data call is connected, the build is checked
    // and one good push, with the PFD's fields, has arrived.
    private static FenixChannelMonitor TrustedPfdMonitor(MonitorTestKit.Lines lines, PfdPushes.Push pfd)
    {
        FenixChannelMonitor monitor = ConnectedMonitor(lines);
        monitor.OnDataMessage(PushWith(pfd));
        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), monitor.VerdictFor(FenixChannelDisplay.Pfd));
        return monitor;
    }

    // The display units of GoodPush, as its DUData part (field 12): all eleven powered with the content they showed on
    // 2026-11-02, or all but the first, the captain's PFD (content 0), which is dark.
    private static byte[] DuDataPart(bool captainsPfdPowered)
    {
        bool[] power = Enumerable.Repeat(true, 11).ToArray();
        power[0] = captainsPfdPowered;
        return new FenixChannelProtoWriter()
            .Message(12, FenixChannelProtoWriter.DuData(power, FenixChannelProtoWriter.PoweredContent))
            .ToArray();
    }

    // PushWith's push with the captain's PFD unit unpowered and every other unit as it was. A bool is one byte whatever
    // it holds, so the part is the same length lit or dark and is written over the lit one in place.
    private static byte[] PushWithTheCaptainsPfdDark(PfdPushes.Push pfd)
    {
        byte[] push = PushWith(pfd);
        int at = push.AsSpan().IndexOf(DuDataPart(captainsPfdPowered: true));
        Assert.True(at >= 0, "the push has no DUData part of the eleven powered units");

        DuDataPart(captainsPfdPowered: false).CopyTo(push, at);
        return push;
    }

    [Fact]
    public void A_trusted_pfd_speaks_the_speed_each_key_reads_and_refuses_the_one_it_does_not_draw()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, Cruise());
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        FenixA320Definition.ChannelKeyContext context = Context(speech, monitor);

        // Green dot 237.4975, VLS 232.4935 and alpha prot 196.6389 are drawn, whole knots; S, F and VFE next are NaN.
        foreach (HotkeyAction action in new[]
                 {
                     HotkeyAction.ReadSpeedGD, HotkeyAction.ReadSpeedS, HotkeyAction.ReadSpeedF,
                     HotkeyAction.ReadSpeedVLS, HotkeyAction.ReadSpeedVS, HotkeyAction.ReadSpeedVFE
                 })
        {
            Assert.True(definition.AnswerChannelPfdKey(action, context));
        }

        Assert.Equal(
            new[]
            {
                "Green dot 237 knots", "S speed not available", "F speed not available", "VLS 232 knots",
                "Alpha prot 197 knots", "VFE next not available"
            },
            speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_trusted_pfd_speaks_the_same_speeds_where_the_pc_writes_a_decimal_comma(string culture)
    {
        // A half knot rounds away from zero and is written without a decimal place whatever the PC's culture is.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, new PfdPushes.Push { GreenDot = 237.5 });
        var definition = new FenixA320Definition();

        SpeechCapture speech = TestCulture.Run(culture, () =>
        {
            var capture = new SpeechCapture();
            definition.AnswerChannelPfdKey(HotkeyAction.ReadSpeedGD, Context(capture, monitor));
            return capture;
        });

        Assert.Equal(new[] { "Green dot 238 knots" }, speech.Interrupts);
    }

    [Fact]
    public void A_pfd_that_cannot_be_read_is_refused_and_reported_under_its_name_once()
    {
        // "k" is a format letter the app does not know: the window shows one status row for it, and a key has nothing to
        // read. The monitor writes the window's problem under "PFD", one line however often the key is pressed.
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, new PfdPushes.Push { MachText = "k.775" });
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        FenixA320Definition.ChannelKeyContext context = Context(speech, monitor);

        Assert.True(definition.AnswerChannelPfdKey(HotkeyAction.ReadSpeedGD, context));
        Assert.True(definition.AnswerChannelPfdKey(HotkeyAction.ReadSpeedVLS, context));

        Assert.Equal(new[] { "Green dot not available", "VLS not available" }, speech.Interrupts);
        Assert.Single(lines.All, line => line == "PFD: unknown code 'k' in the Mach line");
    }

    // ---- The window -----------------------------------------------------------------------------

    private static DisplayListBox ListOf(Control form) => form.Controls.OfType<DisplayListBox>().Single();

    [Fact]
    public void The_window_is_the_pfds_and_shows_the_pfds_rows()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedPfdMonitor(lines, Cruise());
        using var form = new FenixChannelPfdForm(monitor);

        Assert.Equal("Fenix PFD", form.Text);
        Assert.Equal("PFD display", ListOf(form).AccessibleName);

        form.StartLive();

        // The rows are the composer's, for the PFD's snapshot: the tape's speeds as the keys read them.
        string[] rows = ListOf(form).Items.Cast<string>().ToArray();
        Assert.Contains("Green dot 237 knots", rows);
        Assert.Contains("VLS 232 knots", rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("PFD: not available"));
    }

    [Fact]
    public void The_window_shows_one_status_row_while_the_pfd_is_not_trusted()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new FenixChannelPfdForm(neverStarted);

        form.StartLive();

        Assert.Equal(
            new[] { $"PFD: not available, {neverStarted.VerdictFor(FenixChannelDisplay.Pfd).Reason}" },
            ListOf(form).Items.Cast<string>().ToArray());
    }

    [Fact]
    public void The_window_is_its_own_class_so_the_pfd_has_a_window_of_its_own()
    {
        // Fenix windows are tracked one for each type: a display that shared the base class would share its window.
        Assert.NotEqual(typeof(FenixChannelDisplayForm), typeof(FenixChannelPfdForm));
        Assert.True(typeof(FenixChannelPfdForm).IsSubclassOf(typeof(FenixChannelDisplayForm)));
    }

    // ---- The PFD's own verdict ------------------------------------------------------------------

    [Fact]
    public void The_window_and_the_keys_read_the_pfds_own_verdict()
    {
        // The captain's PFD unit is dark and the other units are lit, with the PFD's fields in the push all the same. A
        // key or a window that read another display's verdict (the E/WD's, the ND's) would find that display trusted and
        // speak or show the speeds. They read the PFD's: "display off".
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = ConnectedMonitor(lines);
        monitor.OnDataMessage(PushWithTheCaptainsPfdDark(Cruise()));

        Assert.Equal(new FenixDisplayVerdict(false, "display off"), monitor.VerdictFor(FenixChannelDisplay.Pfd));
        foreach (FenixChannelDisplay lit in new[] { FenixChannelDisplay.Ewd, FenixChannelDisplay.Sd, FenixChannelDisplay.Nd })
        {
            Assert.True(monitor.VerdictFor(lit).Trusted, $"the {lit} should stay lit. The log: " + lines);
        }

        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();
        FenixA320Definition.ChannelKeyContext context = Context(speech, monitor);

        // Alt+P: false and silent, so the key goes on to its AI read.
        Assert.False(definition.AnswerChannelPfdKey(HotkeyAction.ReadDisplayPFD, context));
        Assert.Empty(speech.All);

        // A speed key: answered, with the words of a display it cannot read.
        Assert.True(definition.AnswerChannelPfdKey(HotkeyAction.ReadSpeedGD, context));
        Assert.Equal(new[] { "Green dot not available" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);

        // The window: the one status row, the PFD's reason.
        using var form = new FenixChannelPfdForm(monitor);
        form.StartLive();
        Assert.Equal(
            new[] { "PFD: not available, display off" },
            ListOf(form).Items.Cast<string>().ToArray());
    }
}
