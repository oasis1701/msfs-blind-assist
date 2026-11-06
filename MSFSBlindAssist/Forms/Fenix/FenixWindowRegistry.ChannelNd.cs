using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.Fenix;

// The ND's window in the "Fenix windows" list: "ND", the captain's navigation display from the Fenix's own display data
// (FenixChannelNdForm), with the EFIS controls. It opens through ShowChannelWindow, which shows it whatever the ND's
// verdict, since the window itself shows a status row while the display is not trusted.
public static partial class FenixWindowRegistry
{
    static partial void AddChannelNdWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "ND",
            "The captain's navigation display, live from the Fenix: the map, the route, navaids and traffic, with the EFIS controls",
            ctx => ctx.Definition.ShowChannelWindow(ctx.Announcer, "ND",
                monitor => new FenixChannelNdForm(
                    monitor, (key, value) => ctx.Definition.WriteNdControl(
                        key, value, ctx.SimConnect, ctx.Announcer, FenixA320Definition.PanelPressEcho(ctx.Owner))))));
}
