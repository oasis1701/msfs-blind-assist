namespace MSFSBlindAssist.Aircraft;

// The SD's side of the Fenix display channel: the window of the lower ECAM. The window is
// Forms/Fenix/FenixChannelSdForm.cs, its rows are Services/FenixChannelSd.cs and the pages' rows are the
// Services/FenixChannelSdPage.*.cs partials. Nothing opens the window yet: Alt+S keeps its AI read until the SD's in-sim
// checks pass, and the key's answer then joins this file.
public partial class FenixA320Definition
{
    /// <summary>
    /// The SD window on <paramref name="monitor"/>. It reads <see cref="Variant"/> at each refresh, on the UI thread
    /// where the variant changes, so a page whose layout follows the airframe (FUEL) follows a variant that is detected
    /// after the window opened.
    /// </summary>
    internal Forms.Fenix.FenixChannelSdForm CreateChannelSdForm(Services.FenixChannelMonitor monitor)
        => new(monitor, () => Variant);
}
