namespace MSFSBlindAssist.Aircraft;

// The ND's side of the Fenix display channel: the answer to its keys (Alt+P's sibling Alt+N and Ctrl+W), the sampler that
// speaks the ND's messages and next waypoint (Services/FenixChannelNdMessages.cs), and the write the window's EFIS controls
// make. The window is Forms/Fenix/FenixChannelNdForm.cs. The answer is a method of its own, and the seam of
// FenixA320Definition.Hooks.cs only hands it the key, so that the tests can call it.
public partial class FenixA320Definition
{
    // The ND messages announcer, made by the first sample and kept for the definition's life (UI thread only).
    private Services.FenixChannelNdMessages? _ndMessages;

    // The ND's key seam (declared in FenixA320Definition.Hooks.cs): Alt+N and Ctrl+W.
    partial void ChannelNdKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
        => handled = AnswerChannelNdKey(action, ctx);

    /// <summary>
    /// The ND's answer to <paramref name="action"/>, true when it answered the key.
    /// Alt+N (ReadDisplayND) opens the ND window through <see cref="OpenChannelWindow{TForm}"/>, so it answers while the
    /// ND is trusted or dark, and otherwise (or with no monitor) says nothing and leaves the key to its AI read (false).
    /// Ctrl+W (ReadNDWaypoint) is always answered: the next-waypoint box read at once, or "Next waypoint not available"
    /// when there is no monitor, the ND is not trusted or cannot be read, or the box shows no waypoint.
    /// Any other action is not the ND's (false, silent).
    /// </summary>
    internal bool AnswerChannelNdKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx)
    {
        switch (action)
        {
            case Hotkeys.HotkeyAction.ReadDisplayND:
                return OpenChannelWindow(ctx, Services.FenixChannelDisplay.Nd, "ND",
                    monitor => new Forms.Fenix.FenixChannelNdForm(
                        monitor, (key, value) => WriteNdControl(
                            key, value, ctx.SimConnect, ctx.Announcer, PanelPressEcho(ctx.ParentForm))));

            case Hotkeys.HotkeyAction.ReadNDWaypoint:
                SpeakNextWaypoint(ctx, ctx.Monitor);
                return true;

            default:
                return false;
        }
    }

    // Ctrl+W's words: the readout of one snapshot of the ND, spoken at once, or "Next waypoint not available".
    private void SpeakNextWaypoint(ChannelKeyContext ctx, Services.FenixChannelMonitor? monitor)
    {
        string? text = monitor is null
            ? null
            : Services.FenixChannelNdWaypoint.Readout(
                monitor.Snapshot(Services.FenixChannelDisplay.Nd),
                problem => monitor.ReportFeatureProblem(Services.FenixChannelNdRows.Feature, problem));

        if (text is not null)
            ctx.Announcer.AnnounceImmediate(text);
        else
            SpeakNotAvailable(ctx, "Next waypoint");
    }

    // The ND announcer's sampler (declared in FenixA320Definition.Hooks.cs), run by the announcer host on every tick. It
    // reports no feature problem itself: the window's composer already reports the ND's.
    partial void SampleChannelNd(ChannelAnnounceContext ctx)
        => SpeakNdMessages(ctx.Announcer, ctx.Monitor.Snapshot(Services.FenixChannelDisplay.Nd));

    /// <summary>
    /// Hands one snapshot of the ND to the messages announcer and speaks what it answers, queued (never interrupting): a
    /// background change the pilot did not trigger ([CORE-7]). The announcer tracks on every tick, muted or not.
    /// </summary>
    internal void SpeakNdMessages(Accessibility.ScreenReaderAnnouncer announcer, Services.FenixChannelSnapshot snapshot)
    {
        bool muted = Services.FenixChannelAnnouncementRows.IsMuted(Services.FenixChannelAnnouncementRows.NdKey);
        if ((_ndMessages ??= new Services.FenixChannelNdMessages()).Sample(snapshot, muted) is { } line)
            announcer.Announce(line);
    }

    /// <summary>
    /// A write from the ND window's EFIS controls, by the panel's own path (MainForm's panel write): first
    /// <paramref name="markPressEcho"/> with the key's definition, as the panel marks a press before it goes out, then the
    /// definition's HandleUIVariableSet, and the plain L:var write only when it did not handle the key. A key the
    /// definition does not know writes and marks nothing.
    /// </summary>
    /// <param name="markPressEcho">The panel's echo mark (<see cref="PanelPressEcho"/>): it marks only a button whose press
    /// flips its own light (the six buttons; never the combos), whose button text the reader already reads, so the light
    /// does not say it again (LS and CSTR heard twice on 2026-11-05, [CORE-7]). Null marks nothing.</param>
    internal void WriteNdControl(
        string key, double value, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer,
        Action<SimConnect.SimVarDefinition>? markPressEcho)
    {
        if (!GetVariables().TryGetValue(key, out SimConnect.SimVarDefinition? definition))
        {
            Utils.Logging.Log.Warn("Fenix", $"ND control {key} is not a variable of the definition: nothing written");
            return;
        }

        markPressEcho?.Invoke(definition);
        if (!HandleUIVariableSet(key, value, definition, simConnect, announcer))
            simConnect.SetLVar(definition.Name, value);
    }

    /// <summary>
    /// The panel's own echo mark for a press, from the main form that owns the window (<see cref="MainForm.MarkStateLightEchoForPress"/>);
    /// nothing for any other form.
    /// </summary>
    internal static Action<SimConnect.SimVarDefinition> PanelPressEcho(System.Windows.Forms.Form owner)
        => definition => (owner as MainForm)?.MarkStateLightEchoForPress(definition);
}
