namespace MSFSBlindAssist.Aircraft;

// The ISIS's side of the Fenix display channel: the answer to its key, Alt+I. The window is
// Forms/Fenix/FenixChannelIsisForm.cs, its rows are Services/FenixChannelIsisRows.cs and the decoder with its variant gate
// is Services/FenixChannelIsis.cs. Two things open the window: Alt+I, answered here, and the "ISIS" entry of the Fenix
// windows list (Forms/Fenix/FenixWindowRegistry.ChannelIsis.cs). Both make it with the factory below, which wires the ISIS
// panel's controls the window carries after its list to PressIsisKey.
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
            monitor => CreateChannelIsisForm(monitor, ctx.SimConnect, ctx.Announcer));
    }

    /// <summary>
    /// The ISIS window on <paramref name="monitor"/>. It reads <see cref="Variant"/> at each refresh, on the UI thread
    /// where the variant changes. Its buttons press through <see cref="PressIsisKey"/> with <paramref name="simConnect"/>
    /// and <paramref name="announcer"/>, the panel's own code, which is the only confirmation.
    /// </summary>
    internal Forms.Fenix.FenixChannelIsisForm CreateChannelIsisForm(
        Services.FenixChannelMonitor monitor,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
        => new(monitor, () => Variant, key => PressIsisKey(key, simConnect, announcer));

    /// <summary>
    /// Presses one of the ISIS panel's controls for the ISIS window: the panel's own code for it,
    /// <see cref="HandleUIVariableSet"/> with the value 1, whose cases run <c>ExecuteButtonTransition</c> for a button, the
    /// full press-release ([FNX-6], [A320-3]), RST held 3 s and confirmed, the baro push confirmed, and a counter step for
    /// the baro knob ([FNX-5]); a Fenix whose variant lacks the ISIS is refused as on the panel. Nothing else is written or
    /// said here ([CORE-7]). Called on the UI thread, from a click.
    /// </summary>
    /// <param name="varKey">The key's variable, one of <c>FenixChannelIsisForm.IsisKeys</c>.</param>
    /// <returns>True when the panel's code handled the press; false for a name the definition has no variable for.</returns>
    internal bool PressIsisKey(
        string varKey, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (!GetVariables().TryGetValue(varKey, out SimConnect.SimVarDefinition? varDef)) return false;

        return HandleUIVariableSet(varKey, 1, varDef, simConnect, announcer);
    }
}
