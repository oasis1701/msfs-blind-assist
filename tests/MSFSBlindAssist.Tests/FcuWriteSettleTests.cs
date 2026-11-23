// Pins the hold of an FCU readout that follows an MSFSBA FCU event ([A320-42]). The readouts read
// their numbers on request, answered within a frame, but their words (dashes or a value, the A380's
// derived altitude mode) come from what the once-a-second batches last delivered, and the FCU needs
// a frame or two to act on a push or pull at all. Read straight after the press, a readout spoke
// the state from before it: the A380's panel push/pull read-back had no delay, and the A32NX's
// fixed 300 ms left the dashes-or-value word to a batch that had usually not arrived yet. So the
// reads are held until the FCU has had its settle and every continuous batch has then delivered.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FcuWriteSettleTests
{
    private const string Event = "A32NX.FCU_HDG_PULL";
    private static readonly string[] NoKeys = Array.Empty<string>();   // a pull moves no value var

    /// <summary>An announcer that has seen these continuous batches delivered.</summary>
    private static FcuValueAnnouncer WithBatches(params int[] batches)
    {
        var announcer = new FcuValueAnnouncer();
        foreach (int batch in batches) announcer.OnBatchAfterWrite(batch, 0);
        return announcer;
    }

    [Fact]
    public void Nothing_is_held_when_no_event_was_sent()
    {
        var announcer = WithBatches(1, 2);

        Assert.False(announcer.IsWriteUnsettled(1000));
        Assert.False(announcer.OnBatchAfterWrite(1, 1000));
    }

    [Fact]
    public void An_event_holds_a_readout_until_every_batch_has_delivered_after_the_settle()
    {
        var announcer = WithBatches(1, 2);

        announcer.SuppressEcho(NoKeys, 1000, forEvent: Event);

        Assert.True(announcer.IsWriteUnsettled(1000));
        Assert.False(announcer.OnBatchAfterWrite(1, 1100));   // inside the settle: the FCU may not have acted
        Assert.False(announcer.OnBatchAfterWrite(2, 1200));
        Assert.False(announcer.OnBatchAfterWrite(1, 1400));   // batch 2 still carries the old state
        Assert.True(announcer.IsWriteUnsettled(1500));
        Assert.True(announcer.OnBatchAfterWrite(2, 1700));    // the batch that completes it
        Assert.False(announcer.IsWriteUnsettled(1700));
        Assert.False(announcer.OnBatchAfterWrite(1, 2400));   // released once
    }

    [Fact]
    public void A_readouts_own_echo_holds_nothing()
    {
        // A readout arms the echo for the value it speaks, with no event: it must not hold the next readout.
        var announcer = WithBatches(1);

        announcer.SuppressEcho(new[] { "A32NX_AUTOPILOT_HEADING_SELECTED" }, 1000);

        Assert.False(announcer.IsWriteUnsettled(1000));
    }

    [Fact]
    public void Before_the_first_batch_there_is_no_stream_to_wait_for()
    {
        var announcer = new FcuValueAnnouncer();

        announcer.SuppressEcho(NoKeys, 1000, forEvent: Event);

        Assert.False(announcer.IsWriteUnsettled(1000));
    }

    [Fact]
    public void A_queued_event_holds_from_the_moment_it_is_sent()
    {
        var announcer = WithBatches(1);
        announcer.SuppressEcho(NoKeys, 1000, forEvent: Event);
        Assert.True(announcer.OnBatchAfterWrite(1, 1500));

        announcer.RearmEcho(Event, 60000);                    // the probe concluded: the event goes out now

        Assert.True(announcer.IsWriteUnsettled(60000));
        Assert.False(announcer.OnBatchAfterWrite(1, 60100));
        Assert.True(announcer.OnBatchAfterWrite(1, 61000));
    }

    [Fact]
    public void A_stream_that_stopped_does_not_hold_readouts_for_good()
    {
        var announcer = WithBatches(1, 2);
        announcer.SuppressEcho(NoKeys, 1000, forEvent: Event);
        long late = 1000 + FcuValueAnnouncer.WriteHoldMaxMs;

        Assert.False(announcer.IsWriteUnsettled(late));        // a new readout reads at once
        Assert.True(announcer.OnBatchAfterWrite(1, late));     // and one held is let go by the next batch
    }

    [Fact]
    public void A_context_reset_forgets_the_event()
    {
        var announcer = WithBatches(1);
        announcer.SuppressEcho(NoKeys, 1000, forEvent: Event);

        announcer.ForgetWrite();

        Assert.False(announcer.IsWriteUnsettled(1100));
        Assert.False(announcer.OnBatchAfterWrite(1, 1500));
    }
}
