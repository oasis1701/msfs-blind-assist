using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Forms.Fenix;

// The DCDU's window in the "Fenix windows" list: "DCDU", the captain's datalink display from the Fenix's own display data
// (FenixChannelDcduForm), with its soft keys, print and brightness. An airframe that says it has no DCDU is refused in the
// words of the Ctrl+Shift+D key; any other opens the window through OpenDcduWindow, which shows it whatever the DCDU's
// verdict, since the window itself shows a status row while the display is not trusted.
public static partial class FenixWindowRegistry
{
    static partial void AddChannelDcduWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "DCDU",
            "Datalink messages on the DCDU screen, with its soft keys, print and brightness",
            ctx =>
            {
                if (FenixA320Definition.TryRefuseNoDcdu(ctx.Definition.Variant, ctx.Announcer)) return;

                ctx.Definition.OpenDcduWindow(ctx.SimConnect, ctx.Announcer);
            }));
}
