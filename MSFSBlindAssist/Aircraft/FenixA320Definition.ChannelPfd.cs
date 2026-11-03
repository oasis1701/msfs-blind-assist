namespace MSFSBlindAssist.Aircraft;

// The PFD's side of the Fenix display channel: the answer to its keys, Alt+P and the six speed reads (Shift+1 to
// Shift+6). The window is Forms/Fenix/FenixChannelPfdForm.cs, its rows are Services/FenixChannelPfdRows.cs, and a speed
// key reads the tape row of its speed (Services/FenixChannelSpeedKeys.cs). The answer is a method of its own, and the
// seam of FenixA320Definition.Hooks.cs only hands it the key, so that the tests can call it.
public partial class FenixA320Definition
{
    // The PFD's key seam (declared in FenixA320Definition.Hooks.cs): HandleHotkeyAction asks it first for Alt+P and the
    // six speed reads, and a key it answers goes no further.
    partial void ChannelPfdKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
        => handled = AnswerChannelPfdKey(action, ctx);

    /// <summary>
    /// The PFD's answer to <paramref name="action"/>, true when it answered the key.
    /// Alt+P (ReadDisplayPFD) opens the PFD window through <see cref="OpenChannelWindow{TForm}"/>, so it answers while
    /// the PFD is trusted at the press, or dark (the window then shows its one status row), and for any other reason,
    /// or with no monitor, says nothing and leaves the key to its AI read (false).
    /// A speed key is always answered: it speaks its tape row at once, or "&lt;name&gt; not available" in the same way
    /// when there is no monitor, the PFD is not trusted or cannot be read, or the tape draws no row for that speed.
    /// The key takes one snapshot of the PFD, which decides both whether the display is trusted and what it holds.
    /// A PFD that cannot be read is reported under the window's name. Any other action is not the PFD's (false, silent).
    /// </summary>
    internal bool AnswerChannelPfdKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx)
    {
        if (action == Hotkeys.HotkeyAction.ReadDisplayPFD)
            return OpenChannelWindow(ctx, Services.FenixChannelDisplay.Pfd, "PFD",
                monitor => new Forms.Fenix.FenixChannelPfdForm(monitor));

        if (Services.FenixChannelSpeedKeys.NameOf(action) is not { } name) return false;

        string? text = ctx.Monitor is { } channel
            ? Services.FenixChannelSpeedKeys.Spoken(
                action,
                channel.Snapshot(Services.FenixChannelDisplay.Pfd),
                problem => channel.ReportFeatureProblem(Services.FenixChannelPfdRows.Feature, problem))
            : null;

        if (text is not null)
            ctx.Announcer.AnnounceImmediate(text);
        else
            SpeakNotAvailable(ctx, name);

        return true;
    }
}
