using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

// The E/WD's window in the "Fenix windows" list: "E/WD", the upper ECAM from the Fenix's own display data
// (FenixChannelEwdForm). It opens through ShowChannelWindow, which shows the window whatever the E/WD's verdict, since the
// window itself shows a status row while the display is not trusted. The window's ECAM keys press through the SimConnect
// manager and announcer the entry's context carries, as the key's seam hands them.
public static partial class FenixWindowRegistry
{
    static partial void AddChannelEwdWindows(List<FenixWindowEntry> entries)
        => entries.Add(new FenixWindowEntry(
            "E/WD",
            "Engines, thrust limit, fuel on board, flaps, warnings and memos, as the Fenix's E/WD shows them",
            ctx => ctx.Definition.ShowChannelWindow(
                ctx.Announcer,
                FenixChannelEwdRows.DisplayName,
                monitor => ctx.Definition.CreateChannelEwdForm(monitor, ctx.SimConnect, ctx.Announcer))));
}
