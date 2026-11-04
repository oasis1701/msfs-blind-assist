namespace MSFSBlindAssist.Aircraft;

// The ISIS's side of the Fenix display channel: the answer to its key, Alt+I. The window is
// Forms/Fenix/FenixChannelIsisForm.cs, its rows are Services/FenixChannelIsisRows.cs and the decoder with its variant gate
// is Services/FenixChannelIsis.cs. Two things open the window: Alt+I, answered here, and the "ISIS" entry of the Fenix
// windows list (Forms/Fenix/FenixWindowRegistry.ChannelIsis.cs).
public partial class FenixA320Definition
{
    // The ISIS's key seam (declared in FenixA320Definition.Hooks.cs): HandleHotkeyAction asks it first for Alt+I.
    partial void ChannelIsisKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
        => handled = AnswerChannelIsisKey(action, ctx);

    /// <summary>
    /// The ISIS's answer to <paramref name="action"/>, true when it answered the key. Alt+I (ReadDisplayISIS) asks the
    /// variant first: a Fenix with the round standby gauges never opens the window and keeps the AI read with its
    /// round-gauge prompt (<see cref="FenixA320DisplayReads.For"/>). Otherwise it opens the ISIS window through
    /// <see cref="OpenChannelWindow{TForm}"/>, so it answers while the ISIS is trusted at the press, or off (the window
    /// then shows its one status row), and for any other reason, or with no monitor, says nothing and leaves the key to
    /// its AI read (false). Any other action is not the ISIS's (false, silent).
    /// </summary>
    internal bool AnswerChannelIsisKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx)
    {
        if (action != Hotkeys.HotkeyAction.ReadDisplayISIS) return false;
        if (!Services.FenixChannelIsis.KeyMayOpen(Variant)) return false;

        return OpenChannelWindow(ctx, Services.FenixChannelDisplay.Isis, Services.FenixChannelIsisRows.Feature,
            monitor => new Forms.Fenix.FenixChannelIsisForm(monitor, () => Variant));
    }
}
