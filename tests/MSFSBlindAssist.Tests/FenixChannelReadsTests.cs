// Characterization tests for FenixChannelReads, the fallback switch of the Fenix display channel: the table of
// display keys that have a channel read, and the pure choice between speaking that read and the AI read every
// display key has today. No sim and no gate: the snapshot (a display's verdict and the latest frame, taken
// together) is a function each test passes in, and every frame is built here.
//
// The contract: a key speaks its channel read only when a read exists for that key, the snapshot of that read's
// own display (display by display, never the channel as a whole) trusts it and holds a frame, and the read
// composes text from that very frame. Every other case answers null, which means "use the AI read": a wrong value
// spoken is the costliest defect of the channel, so doubt always falls back. The verdict and the frame come from
// one snapshot, so a read never composes a frame its verdict did not judge (FenixChannelMonitorTests pin that the
// monitor takes the two at one locked instant). Choose never throws and writes no log; a read or a snapshot that
// throws is a read that did not come.
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
        int taken = 0;

        string? text = FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, empty, _ =>
        {
            taken++;
            return Taken(Trusted, Frame());
        });

        Assert.Null(text);
        // A key with no read asks the monitor nothing.
        Assert.Equal(0, taken);
    }

    [Fact]
    public void A_read_for_another_key_does_not_answer_this_one()
    {
        // Only the E/WD key has a read, and every display is trusted: the PFD key still keeps its AI read.
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, _ => EwdText);

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayPFD, reads, TrustedWith(Frame())));
        Assert.Equal(EwdText, FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustedWith(Frame())));
    }

    // ---- When the read speaks -------------------------------------------------------------------

    [Fact]
    public void A_trusted_display_with_a_read_speaks_the_channel_text()
    {
        string? text = FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, StandIn, TrustedWith(Frame()));

        Assert.Equal("E/WD from the channel", text);
    }

    [Fact]
    public void The_read_composes_from_the_frame_of_its_snapshot()
    {
        FenixChannelFrame judged = Frame();
        FenixChannelFrame? composedFrom = null;
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, frame =>
        {
            composedFrom = frame;
            return EwdText;
        });

        FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustedWith(judged));

        // The very frame the snapshot gave with its verdict, never another one.
        Assert.Same(judged, composedFrom);
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
            _ => Taken(new FenixDisplayVerdict(false, "stream stalled"), Frame()));

        Assert.Null(text);
        // A read composes only from a display the gate trusts: a frame whose verdict is not trusted is never even
        // composed from, although the snapshot holds one.
        Assert.Equal(0, composed);
    }

    [Fact]
    public void Each_display_is_judged_on_its_own()
    {
        // A fuel mismatch untrusts the E/WD and leaves the PFD and the SD trusted (the gate's rule). Each key
        // follows the verdict of its own display: the other two still speak, the E/WD key falls back.
        FenixChannelFrame frame = Frame();
        FenixChannelSnapshot Snapshot(FenixChannelDisplay display) =>
            display == FenixChannelDisplay.Ewd
                ? Taken(new FenixDisplayVerdict(false, "fuel disagrees: channel 2,940 kg, sim 5,100 kg"), frame)
                : Taken(Trusted, frame);

        Assert.Equal(PfdText, FenixChannelReads.Choose(HotkeyAction.ReadDisplayPFD, StandIn, Snapshot));
        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, StandIn, Snapshot));
        Assert.Equal(SdText, FenixChannelReads.Choose(HotkeyAction.ReadDisplayLowerECAM, StandIn, Snapshot));
    }

    [Fact]
    public void A_trusted_verdict_with_no_frame_uses_the_ai_read()
    {
        // No frame yet (none has come since the connection opened): there is nothing to compose, whatever the
        // verdict says.
        int composed = 0;
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, _ =>
        {
            composed++;
            return EwdText;
        });

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustedWith(null)));
        Assert.Equal(0, composed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_read_that_composes_nothing_uses_the_ai_read(string? composed)
    {
        // Blank text is nothing to say: a screen reader would give the pilot silence where the AI read gives an answer.
        var reads = TableWith(HotkeyAction.ReadDisplayUpperECAM, FenixChannelDisplay.Ewd, _ => composed);

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustedWith(Frame())));
    }

    // ---- The snapshot ---------------------------------------------------------------------------

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, "Pfd")]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, "Ewd")]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, "Sd")]
    public void The_snapshot_asked_is_the_reads_own_display(HotkeyAction action, string display)
    {
        // The display travels as text: a public test method cannot take an internal type (CS0051).
        var asked = new List<FenixChannelDisplay>();

        FenixChannelReads.Choose(action, StandIn, requested =>
        {
            asked.Add(requested);
            return Taken(Trusted, Frame());
        });

        // Once, and for the read's own display only.
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

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, reads, TrustedWith(Frame())));
    }

    [Fact]
    public void A_snapshot_that_throws_uses_the_ai_read()
    {
        Func<FenixChannelDisplay, FenixChannelSnapshot> broken =
            _ => throw new InvalidOperationException("gate failed");

        Assert.Null(FenixChannelReads.Choose(HotkeyAction.ReadDisplayUpperECAM, StandIn, broken));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    // A frame as the monitor would hold it. The choice never looks inside a frame (the gate's verdict is what
    // keeps a damaged one from being read), so this one carries no values.
    private static FenixChannelFrame Frame()
        => new(false, [], FenixFdState.Default, [], [], [], 0, 0, 0, 0, "");

    // A snapshot that trusts every display, and gives this frame (null: no frame yet) with each verdict.
    private static Func<FenixChannelDisplay, FenixChannelSnapshot> TrustedWith(FenixChannelFrame? frame)
        => _ => Taken(Trusted, frame);

    // A snapshot as the monitor hands it out, reduced to what the choice reads: the verdict and the frame.
    private static FenixChannelSnapshot Taken(FenixDisplayVerdict verdict, FenixChannelFrame? frame)
        => new(verdict, frame, null, null, 0, 0);

    private static IReadOnlyDictionary<HotkeyAction, FenixChannelRead> TableWith(
        HotkeyAction action, FenixChannelDisplay display, Func<FenixChannelFrame, string?> compose)
        => new Dictionary<HotkeyAction, FenixChannelRead> { [action] = new FenixChannelRead(display, compose) };
}
