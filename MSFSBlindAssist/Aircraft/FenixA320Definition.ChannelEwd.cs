namespace MSFSBlindAssist.Aircraft;

// The E/WD's side of the Fenix display channel: the window of the upper ECAM and the answer to its key, Alt+E. The window
// is Forms/Fenix/FenixChannelEwdForm.cs, its rows are Services/FenixChannelEwdRows.cs and its entry in the Fenix windows
// list is Forms/Fenix/FenixWindowRegistry.ChannelEwd.cs. The answer is the seam of FenixA320Definition.Hooks.cs, which
// only hands this file the key. The window also carries the ECAM control panel's T.O CONFIG, CLR, RCL and EMER CANC keys
// after its list, pressed through PressEcamKey (FenixA320Definition.ChannelSd.cs); the factory below wires them.
public partial class FenixA320Definition
{
    // The E/WD's key seam (declared in FenixA320Definition.Hooks.cs): HandleHotkeyAction asks it first for Alt+E. The
    // window opens while the E/WD is trusted at the press, or dark (on its one status row), and the key is then
    // answered. Otherwise handled stays false and the key goes on to its AI read, with no words of its own.
    partial void ChannelEwdKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
        => handled = OpenChannelWindow(
            ctx, Services.FenixChannelDisplay.Ewd, Services.FenixChannelEwdRows.DisplayName,
            monitor => CreateChannelEwdForm(monitor, ctx.SimConnect, ctx.Announcer));

    /// <summary>
    /// The E/WD window on <paramref name="monitor"/>. It reads <see cref="Variant"/>'s engine family at each refresh, on
    /// the UI thread where the variant changes, so the engine block follows a variant that is detected after the window
    /// opened. Its ECAM keys (T.O CONFIG, CLR, RCL, EMER CANC) press through <see cref="PressEcamKey"/> with
    /// <paramref name="simConnect"/> and <paramref name="announcer"/>, the panel's own code, which is the only
    /// confirmation.
    /// </summary>
    internal Forms.Fenix.FenixChannelEwdForm CreateChannelEwdForm(
        Services.FenixChannelMonitor monitor,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
        => new(monitor, () => Variant.Engine, key => PressEcamKey(key, simConnect, announcer));
}
