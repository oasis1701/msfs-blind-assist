namespace MSFSBlindAssist.Aircraft;

// The SD's side of the Fenix display channel: the window of the lower ECAM. The window is
// Forms/Fenix/FenixChannelSdForm.cs, its rows are Services/FenixChannelSd.cs and the pages' rows are the
// Services/FenixChannelSdPage.*.cs partials. Nothing opens the window yet: Alt+S keeps its AI read until the SD's in-sim
// checks pass, and the key's answer then joins this file. This file holds the window's factory and the press of the
// page keys the window carries.
public partial class FenixA320Definition
{
    /// <summary>
    /// The SD window on <paramref name="monitor"/>. It reads <see cref="Variant"/> at each refresh, on the UI thread
    /// where the variant changes, so a page whose layout follows the airframe (FUEL) follows a variant that is detected
    /// after the window opened. Its page keys press through <see cref="PressEcamPageKey"/> with
    /// <paramref name="simConnect"/> and <paramref name="announcer"/>.
    /// </summary>
    internal Forms.Fenix.FenixChannelSdForm CreateChannelSdForm(
        Services.FenixChannelMonitor monitor,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
        => new(monitor, () => Variant, key => PressEcamPageKey(key, simConnect, announcer));

    /// <summary>
    /// Presses one of the ECAM control panel's page keys for the SD window: the panel's own code for it,
    /// <see cref="HandleUIVariableSet"/> with the value 1, whose <c>S_ECAM_*</c> cases run
    /// <c>ExecuteButtonTransition</c>, the full press-release ([FNX-6], [A320-3]). Nothing else is written or said here
    /// ([CORE-7]): what the panel's code and the key's light announce is the one confirmation, as when the pilot presses
    /// the panel's button, and the window's list shows the new page at its next refresh. Called on the UI thread, from a
    /// click.
    /// </summary>
    /// <param name="varKey">The key's variable, one of <c>FenixChannelSdForm.PageKeys</c>.</param>
    /// <returns>True when the panel's code handled the press; false for a name the definition has no variable for.</returns>
    internal bool PressEcamPageKey(
        string varKey, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (!GetVariables().TryGetValue(varKey, out SimConnect.SimVarDefinition? varDef)) return false;

        return HandleUIVariableSet(varKey, 1, varDef, simConnect, announcer);
    }
}
