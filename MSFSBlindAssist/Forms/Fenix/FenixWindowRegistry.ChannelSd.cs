namespace MSFSBlindAssist.Forms.Fenix;

// The SD window in the "Fenix windows" list. It opens through ShowChannelWindow, which shows the window whatever the
// SD's verdict: the window shows one status row while the SD is not trusted. Alt+S is the other way in
// (FenixA320Definition.ChannelSd.cs), and opens it while the SD is trusted or no powered unit shows it.
public static partial class FenixWindowRegistry
{
    static partial void AddChannelSdWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "SD",
            "The System Display from the Fenix: the page shown, its values, STATUS and the bottom line",
            ctx => ctx.Definition.ShowChannelWindow(
                ctx.Announcer, Services.FenixChannelSd.DisplayName,
                monitor => ctx.Definition.CreateChannelSdForm(monitor, ctx.SimConnect, ctx.Announcer))));
}
