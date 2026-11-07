namespace MSFSBlindAssist.Aircraft;

// The Fenix E/WD alerts' side of the display channel: the sampler that speaks the ECAM's warnings, cautions and memos
// (Services/FenixChannelEwdAlertAnnouncer.cs, rows in Services/FenixChannelAnnouncementRows.cs). Nothing here opens the
// channel: the sampler only takes the monitor's snapshot.
public partial class FenixA320Definition
{
    // The alert announcer, one per definition instance so that an aircraft switch starts fresh (UI thread only).
    private Services.FenixChannelEwdAlertAnnouncer? _ewdAlerts;

    // The E/WD alert sampler (declared in FenixA320Definition.Hooks.cs), run by the announcer host on every tick on the UI
    // thread, inside the host's own try/catch. One snapshot gives the verdict, the frame, the entries and the connection
    // together. The announcer decides what is new, cleared or muted; what it returns is spoken in order, the first red
    // failure interrupting. What it reports (an entry that is no item, a line not found) goes to the channel's log.
    partial void SampleChannelEwdAlerts(ChannelAnnounceContext ctx)
    {
        Services.FenixChannelSnapshot s = ctx.Monitor.Snapshot(Services.FenixChannelDisplay.Ewd);

        IReadOnlyList<Services.FenixEwdAlertLine> lines = (_ewdAlerts ??= new Services.FenixChannelEwdAlertAnnouncer())
            .Step(s, ctx.NowMs, Services.FenixChannelAnnouncementRows.IsMuted,
                p => ctx.Monitor.ReportFeatureProblem("E/WD alerts", p));

        Services.FenixChannelEwdAlertAnnouncer.Speak(lines, ctx.Announcer);
    }
}
