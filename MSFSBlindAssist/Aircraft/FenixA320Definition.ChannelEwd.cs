namespace MSFSBlindAssist.Aircraft;

// The E/WD's side of the Fenix display channel: the window of the upper ECAM. The window is
// Forms/Fenix/FenixChannelEwdForm.cs and its rows are Services/FenixChannelEwdRows.cs. Nothing opens the window yet:
// Alt+E keeps its AI read and the Fenix windows list has no E/WD entry until the E/WD's in-sim checks pass, and the
// key's answer then joins this file.
public partial class FenixA320Definition
{
    /// <summary>
    /// The E/WD window on <paramref name="monitor"/>. It reads <see cref="Variant"/>'s engine family at each refresh, on
    /// the UI thread where the variant changes, so the engine block follows a variant that is detected after the window
    /// opened.
    /// </summary>
    internal Forms.Fenix.FenixChannelEwdForm CreateChannelEwdForm(Services.FenixChannelMonitor monitor)
        => new(monitor, () => Variant.Engine);
}
