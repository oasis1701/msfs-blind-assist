namespace MSFSBlindAssist.Forms.Fenix;

// The PFD's window in the "Fenix windows" list: "PFD", the captain's primary flight display from the Fenix's own display
// data (FenixChannelPfdForm). It opens through ShowChannelWindow, which shows the window whatever the PFD's verdict, since
// the window itself shows a status row while the display is not trusted.
public static partial class FenixWindowRegistry
{
    static partial void AddChannelPfdWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "PFD",
            "The captain's PFD from the Fenix's own display data: FMA, speeds, altitude, attitude, flight director and heading",
            ctx => ctx.Definition.ShowChannelWindow(ctx.Announcer, "PFD", monitor => new FenixChannelPfdForm(monitor))));
}
