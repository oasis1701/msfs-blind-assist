namespace MSFSBlindAssist.Forms.Fenix;

// The ISIS window in the "Fenix windows" list. On a Fenix with the round standby gauges the entry says so and opens
// nothing. Otherwise it opens through ShowChannelWindow, which shows the window whatever the ISIS's verdict: the window
// shows one status row while the ISIS is not trusted. Alt+I is the other way in (FenixA320Definition.ChannelIsis.cs), and
// opens it while the ISIS is trusted or off.
public static partial class FenixWindowRegistry
{
    static partial void AddChannelIsisWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "ISIS",
            "Speed, altitude, baro, attitude, bugs and ILS deviations, live from the Fenix's display channel",
            ctx =>
            {
                if (Services.FenixChannelIsis.RefusalFor(ctx.Definition.Variant) is { } refusal)
                {
                    ctx.Announcer.AnnounceImmediate(refusal);
                    return;
                }

                ctx.Definition.ShowChannelWindow(ctx.Announcer, Services.FenixChannelIsisRows.Feature,
                    monitor => ctx.Definition.CreateChannelIsisForm(monitor, ctx.SimConnect, ctx.Announcer));
            }));
}
