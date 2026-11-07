namespace MSFSBlindAssist.Aircraft;

// The FMA's side of the Fenix display channel: the sampler that speaks a change of the flight mode annunciator
// (Services/FenixChannelFmaAnnouncer.cs, whose words are Services/FenixChannelFmaSpeech.cs). Nothing here opens the
// channel (the sampler only takes the monitor's snapshot) and nothing is written to the sim.
public partial class FenixA320Definition
{
    // The FMA's state machine: what the pilot last heard, the change settling and the first reading's baseline. One per
    // definition instance, so each aircraft load starts with a silent first reading (UI thread only).
    private readonly Services.FenixChannelFmaAnnouncer _channelFma = new();

    // The FMA announcer's sampler (declared in FenixA320Definition.Hooks.cs), run by the announcer host on every tick. One
    // PFD snapshot gives the verdict and the 16 slots from one instant. The announcer tracks on every tick, muted or not,
    // and answers with the change when one has settled: spoken queued, never AnnounceImmediate, so it cuts off no other
    // call, a background change the pilot did not trigger ([CORE-7]). A slot the layout gives no place is never spoken;
    // it is named once for the log while the PFD is trusted and the frame has all its slots, so a later flight can place it
    // (the monitor writes each distinct line once).
    partial void SampleChannelFma(ChannelAnnounceContext ctx)
    {
        Services.FenixChannelSnapshot s = ctx.Monitor.Snapshot(Services.FenixChannelDisplay.Pfd);
        IReadOnlyList<Services.FenixFmaSlot>? slots = s.Frame?.Fma;

        bool muted = Services.FenixChannelAnnouncementRows.IsMuted(Services.FenixChannelAnnouncementRows.FmaKey);
        string? text = _channelFma.Sample(s.Verdict, slots, s.Connection, ctx.NowMs, muted);

        if (s.Verdict.Trusted && slots is not null && slots.Count == Services.FenixChannelFields.FmaEntry.SlotCount)
        {
            foreach (string line in Services.FenixChannelFmaSpeech.Unplaced(slots))
                ctx.Monitor.ReportFeatureProblem("FMA", line);
        }

        // No Suppressed check, as the DCDU's sampler: on a Fenix only MainForm's synchronous wrap sets it, and this timer
        // tick cannot run inside that wrap.
        if (text is not null) ctx.Announcer.Announce(text);
    }
}
