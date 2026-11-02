// Characterization tests for FenixChannelReads, the fallback switch of the Fenix display channel: the table of
// display keys that have a channel read, and the pure choice between speaking that read and the AI read every
// display key has today. No sim and no gate: the verdict is a function each test passes in, and every frame is
// built here.
//
// The contract: a key speaks its channel read only when a read exists for that key, a frame has come, the trust
// gate trusts that read's own display (display by display, never the channel as a whole) and the read composes
// text from that frame. Every other case answers null, which means "use the AI read": a wrong value spoken is
// the costliest defect of the channel, so doubt always falls back. Choose never throws and writes no log; a read
// or a verdict that throws is a read that did not come.
//
// The table is empty in this work, so every display key keeps its AI read; the choice is pinned with a stand-in
// table. Choose formats nothing (the text is whatever the read composes), so the comma-decimal culture tests
// belong to each read that formats a number.

using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelReadsTests
{
    private const string PfdText = "PFD from the channel";
    private const string EwdText = "E/WD from the channel";
    private const string SdText = "SD from the channel";

    private static readonly FenixDisplayVerdict Trusted = new(true, "trusted");

    private static readonly Func<FenixChannelDisplay, FenixDisplayVerdict> TrustsEverything = _ => Trusted;

    // A stand-in table with a read for the key of each display, each speaking a text of its own.
    private static readonly IReadOnlyDictionary<HotkeyAction, FenixChannelRead> StandIn =
        new Dictionary<HotkeyAction, FenixChannelRead>
        {
            [HotkeyAction.ReadDisplayPFD] = new(FenixChannelDisplay.Pfd, _ => PfdText),
            [HotkeyAction.ReadDisplayUpperECAM] = new(FenixChannelDisplay.Ewd, _ => EwdText),
            [HotkeyAction.ReadDisplayLowerECAM] = new(FenixChannelDisplay.Sd, _ => SdText),
        };

    // ---- The table ------------------------------------------------------------------------------

    [Fact]
    public void The_table_is_empty_in_this_work()
    {
        // Every display key keeps its AI read until a channel read is added here, which is a decision with its
        // own in-sim check and its own test, never a side effect of an edit.
        Assert.Empty(FenixChannelReads.All);
    }

    [Fact]
    public void The_table_cannot_be_changed_from_outside()
    {
        // The one way to change a read-only view is to cast it back to a mutable interface.
        Assert.True(
            FenixChannelReads.All is not ICollection<KeyValuePair<HotkeyAction, FenixChannelRead>> collection
            || collection.IsReadOnly);
    }

    // ---- Which key has a read -------------------------------------------------------------------

    [Fact]
    public void With_no_read_the_key_uses_the_ai_read()
    {
        var empty = new Dictionary<HotkeyAction, FenixChannelRead>();

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, empty, TrustsEverything, Frame()));
    }

    [Fact]
    public void A_read_for_another_key_does_not_answer_this_one()
    {
        // Only the E/WD key has a read, and every display is trusted: the PFD key still keeps its AI read.
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, _ => EwdText);

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayPFD, reads, TrustsEverything, Frame()));
        Assert.Equal(EwdText, FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustsEverything, Frame()));
    }

    // ---- When the read speaks -------------------------------------------------------------------

    [Fact]
    public void A_trusted_display_with_a_read_speaks_the_channel_text()
    {
        string? text = FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, StandIn, TrustsEverything, Frame());

        Assert.Equal("E/WD from the channel", text);
    }

    [Fact]
    public void The_read_composes_from_the_frame_it_was_given()
    {
        FenixChannelFrame latest = Frame();
        FenixChannelFrame? composedFrom = null;
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, frame =>
        {
            composedFrom = frame;
            return EwdText;
        });

        FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustsEverything, latest);

        // The very frame the caller read before it asked for the verdict, never another one.
        Assert.Same(latest, composedFrom);
    }

    [Fact]
    public void An_untrusted_display_uses_the_ai_read()
    {
        int composed = 0;
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, _ =>
        {
            composed++;
            return EwdText;
        });

        string? text = FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads,
            _ => new FenixDisplayVerdict(false, "stream stalled"), Frame());

        Assert.Null(text);
        // A read composes only from a display the gate trusts: a display it refused is never even composed from.
        Assert.Equal(0, composed);
    }

    [Fact]
    public void Each_display_is_judged_on_its_own()
    {
        // A fuel mismatch untrusts the E/WD and leaves the PFD and the SD trusted (the gate's rule). Each key
        // follows the verdict of its own display: the other two still speak, the E/WD key falls back.
        FenixDisplayVerdict VerdictFor(FenixChannelDisplay display) => display == FenixChannelDisplay.Ewd
            ? new FenixDisplayVerdict(false, "fuel disagrees: channel 2,940 kg, sim 5,100 kg")
            : Trusted;

        Assert.Equal(PfdText, FenixChannelReads.Choose(HotkeyAction.ReadDisplayPFD, StandIn, VerdictFor, Frame()));
        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, StandIn, VerdictFor, Frame()));
        Assert.Equal(SdText, FenixChannelReads.Choose(HotkeyAction.ReadDisplayLowerECAM, StandIn, VerdictFor, Frame()));
    }

    [Fact]
    public void No_frame_yet_uses_the_ai_read()
    {
        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, StandIn, TrustsEverything, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_read_that_composes_nothing_uses_the_ai_read(string? composed)
    {
        // Blank text is nothing to say: a screen reader would give the pilot silence where the AI read gives an answer.
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, _ => composed);

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustsEverything, Frame()));
    }

    // ---- The verdict ----------------------------------------------------------------------------

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, "Pfd")]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, "Ewd")]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, "Sd")]
    public void The_verdict_asked_is_the_reads_own_display(HotkeyAction action, string display)
    {
        // The display travels as text: a public test method cannot take an internal type (CS0051).
        var asked = new List<FenixChannelDisplay>();

        FenixChannelReads.Choose(action, StandIn, requested =>
        {
            asked.Add(requested);
            return Trusted;
        }, Frame());

        Assert.Equal(new[] { Enum.Parse<FenixChannelDisplay>(display) }, asked);
    }

    // ---- Nothing throws -------------------------------------------------------------------------

    [Fact]
    public void A_read_that_throws_uses_the_ai_read()
    {
        // A read with a bug, here one that indexes past the FMA slots its frame has.
        Func<FenixChannelFrame, string?> buggy = frame => frame.Fma[15].Text;
        Assert.ThrowsAny<Exception>(() => buggy(Frame()));   // it must really throw, or the test below proves nothing
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, buggy);

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustsEverything, Frame()));
    }

    [Fact]
    public void A_verdict_that_throws_uses_the_ai_read()
    {
        Func<FenixChannelDisplay, FenixDisplayVerdict> broken = _ => throw new InvalidOperationException("gate failed");

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, StandIn, broken, Frame()));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    // A frame as the monitor would hold it. The choice never looks inside a frame (the gate's verdict is what
    // keeps a damaged one from being read), so this one carries no values.
    private static FenixChannelFrame Frame()
        => new(false, [], FenixFdState.Default, [], [], [], 0, 0, 0, 0, "");

    private static IReadOnlyDictionary<HotkeyAction, FenixChannelRead> TableWith(
        HotkeyAction action, FenixChannelDisplay display, Func<FenixChannelFrame, string?> compose)
        => new Dictionary<HotkeyAction, FenixChannelRead> { [action] = new FenixChannelRead(display, compose) };
}
