namespace MSFSBlindAssist.Aircraft;

// The SD's side of the Fenix display channel: the window of the lower ECAM. The window is
// Forms/Fenix/FenixChannelSdForm.cs, its rows are Services/FenixChannelSd.cs and the pages' rows are the
// Services/FenixChannelSdPage.*.cs partials. Two things open the window: Alt+S, answered by ChannelSdKey below, and the
// "SD" entry of the Fenix windows list (Forms/Fenix/FenixWindowRegistry.ChannelSd.cs). This file holds the window's
// factory, the press of the page keys the window carries, and the key's seam: Alt+S opens the window while the SD is
// trusted at the press, or dark (on its one status row), and otherwise it is left to its AI read.
public partial class FenixA320Definition
{
    /// <summary>
    /// The SD's key, Alt+S (ReadDisplayLowerECAM): opens the SD window through <see cref="OpenChannelWindow{TForm}"/>, so
    /// the key answers while the SD is trusted at the press, or dark (the window then shows its one status row), and
    /// with an SD that is not trusted for any other reason (or no monitor) it says nothing and leaves
    /// <paramref name="handled"/> false, so the key goes on to its AI read with no extra words (spec 2.4). It is the only
    /// key the SD owns, and it never calls <see cref="ShowChannelWindow{TForm}"/>, which shows the window whatever the
    /// verdict and is the windows list's way in. Any other action is left as it was.
    /// </summary>
    partial void ChannelSdKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
    {
        if (action == Hotkeys.HotkeyAction.ReadDisplayLowerECAM)
            handled = OpenChannelWindow(ctx, Services.FenixChannelDisplay.Sd, Services.FenixChannelSd.DisplayName,
                monitor => CreateChannelSdForm(monitor, ctx.SimConnect, ctx.Announcer));
    }

    /// <summary>
    /// The SD window on <paramref name="monitor"/>. It reads <see cref="Variant"/> at each refresh, on the UI thread
    /// where the variant changes, so a page whose layout follows the airframe (FUEL) follows a variant that is detected
    /// after the window opened. Its page keys press through <see cref="PressEcamKey"/> with
    /// <paramref name="simConnect"/> and <paramref name="announcer"/>.
    /// </summary>
    internal Forms.Fenix.FenixChannelSdForm CreateChannelSdForm(
        Services.FenixChannelMonitor monitor,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
        => new(monitor, () => Variant, key => PressEcamKey(key, simConnect, announcer));

    /// <summary>
    /// Presses one of the ECAM control panel's keys for the channel's windows (the SD's page keys, the E/WD's T.O CONFIG,
    /// CLR, RCL and EMER CANC): the panel's own code for it,
    /// <see cref="HandleUIVariableSet"/> with the value 1, whose <c>S_ECAM_*</c> cases run
    /// <c>ExecuteButtonTransition</c>, the full press-release ([FNX-6], [A320-3]). Nothing else is written or said here
    /// ([CORE-7]): what the panel's code and the key's light announce is the one confirmation, as when the pilot presses
    /// the panel's button, and the window's list shows the new page at its next refresh. Called on the UI thread, from a
    /// click.
    /// </summary>
    /// <param name="varKey">
    /// The key's variable, one of <c>FenixChannelSdForm.PageKeys</c> or <c>FenixChannelEwdForm.EcamKeys</c>.
    /// </param>
    /// <returns>True when the panel's code handled the press; false for a name the definition has no variable for.</returns>
    internal bool PressEcamKey(
        string varKey, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (!GetVariables().TryGetValue(varKey, out SimConnect.SimVarDefinition? varDef)) return false;

        return HandleUIVariableSet(varKey, 1, varDef, simConnect, announcer);
    }
}
