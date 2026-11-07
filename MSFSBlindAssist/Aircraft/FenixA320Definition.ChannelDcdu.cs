namespace MSFSBlindAssist.Aircraft;

// The DCDU's side of the Fenix display channel: the calls its window makes (Forms/Fenix/FenixChannelDcduForm.cs, whose rows
// are Services/FenixChannelDcduRows.cs and whose keys are Services/FenixChannelDcduKeys.cs), the key that opens it
// (Ctrl+Shift+D) and the sampler that calls a new message (Services/FenixChannelDcduAnnouncer.cs). Nothing here opens the
// channel (the sampler only takes the monitor's snapshot), and no press is written here: a key or a brightness press goes
// through HandleUIVariableSet, the S_DCDU1_* branches, which press and release ([A320-3]) and carry the variant's refusal
// in front of them.
public partial class FenixA320Definition
{
    /// <summary>The captain's DCDU brightness switch, the variable the window's Bright and Dim buttons step.</summary>
    internal const string DcduBrightnessKey = "S_DCDU1_DIM_BRT";

    /// <summary>
    /// How long after a Bright or Dim press the switch is back in the middle and the brightness has moved: the hold before the
    /// return write and the gap before the forced read (<see cref="PulseSpringSwitch"/>). The window reads the brightness then.
    /// </summary>
    internal const int DcduSpringBackMs = ButtonPressHoldMs + ButtonReleaseGapMs;

    // The tracker of the pilot's own presses, made by the first press of any key or the first sample (UI thread only).
    private Services.FenixChannelDcduAnnouncer? _dcduAnnouncer;

    // The read-back of the first info line and of the pilot's own presses, made by the first press of any key or the first
    // sample (UI thread only).
    private Services.FenixChannelDcduReadback? _dcduReadback;

    /// <summary>
    /// Whether the new-message call is muted: the Ctrl+M row is unticked, or the variant says there is no DCDU, which the
    /// channel still reads trusted (measured 2026-11-03). An unread field (null) mutes nothing ([FNX-11]).
    /// </summary>
    internal static bool DcduCallMuted(FenixVariant variant, bool rowMuted) => rowMuted || variant.HasDcdu == false;

    // The DCDU's key seam (declared in FenixA320Definition.Hooks.cs): Ctrl+Shift+D, the one action it is given. The Fenix
    // always answers it, whether the window opens, the airframe has no DCDU or there is no monitor, so MainForm's "only
    // available on the A32NX and the Headwind A330" sentence never reaches a Fenix pilot. The refusal comes first and leaves
    // input mode on, as after MainForm's own refusals. A window about to open leaves both hotkey modes, so that its chords
    // and page keys reach it and not the global registrations (ShowA32NXDcduDialog does the same).
    partial void ChannelDcduKey(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
    {
        handled = true;

        if (TryRefuseNoDcdu(Variant, ctx.Announcer)) return;

        ctx.HotkeyManager.ExitInputHotkeyMode();
        ctx.HotkeyManager.ExitOutputHotkeyMode();
        OpenDcduWindow(ctx.SimConnect, ctx.Announcer);
    }

    // The DCDU announcer's sampler (declared in FenixA320Definition.Hooks.cs), run by the announcer host on every tick. One
    // snapshot gives the verdict, the screen and the connection together. The monitor already logs why a display is not
    // trusted, so only a problem on a trusted DCDU is reported here. The announcer tracks on every tick, muted or not, and
    // answers with the call when one is due: spoken queued, a background change the pilot did not trigger ([CORE-7]). The
    // read-back reads the same screen and tracks on every tick too: it says every new first info line (SENDING, RECEIVD BY
    // ATC, NO MORE MSG), queued, and the status after the pilot's own soft key. The mute (the Ctrl+M row, or an airframe with
    // no DCDU) silences a background info line, never one inside the window after the pilot's own press and never the status.
    partial void SampleChannelDcdu(ChannelAnnounceContext ctx)
    {
        Services.FenixChannelSnapshot s = ctx.Monitor.Snapshot(Services.FenixChannelDisplay.Dcdu);
        Services.FenixChannelSection<Services.FenixDcduScreen> section = Services.FenixChannelDcdu.Read(s);
        if (s.Verdict.Trusted && !section.IsOk)
            ctx.Monitor.ReportFeatureProblem(Services.FenixChannelDcdu.DisplayName, section.Problem ?? "no data");

        bool rowMuted = Services.FenixChannelAnnouncementRows.IsMuted(Services.FenixChannelAnnouncementRows.DcduKey);
        bool muted = DcduCallMuted(Variant, rowMuted);

        if ((_dcduReadback ??= new Services.FenixChannelDcduReadback()).Sample(section, muted, ctx.NowMs) is { } readBack)
            ctx.Announcer.Announce(readBack);

        string? call = (_dcduAnnouncer ??= new Services.FenixChannelDcduAnnouncer())
            .Sample(section, s.Connection, muted, ctx.NowMs);

        // No Suppressed check, unlike the session notice's sampler: on a Fenix only MainForm's synchronous wrap sets it, and
        // this timer tick cannot run inside that wrap.
        if (call is not null) ctx.Announcer.Announce(call);
    }

    /// <summary>
    /// Opens the DCDU window on this instance's channel monitor, whatever the DCDU's verdict: the window shows one status
    /// row while the display is not trusted. With no monitor (before Start, after Stop) the pilot is told "DCDU display
    /// not available" at once and nothing opens (<see cref="ShowChannelWindow{TForm}"/>).
    /// </summary>
    internal void OpenDcduWindow(SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        ShowChannelWindow(announcer, Services.FenixChannelDcdu.DisplayName,
            monitor => new Forms.Fenix.FenixChannelDcduForm(monitor, this, simConnect, announcer));
    }

    /// <summary>
    /// Presses the DCDU key <paramref name="key"/> through the panel's button for it. Every key tells the read-back first,
    /// so that what the press changes away from the row under the cursor is heard once and a first info line it brings is
    /// heard even with the DCDU muted; and the announcer as well, so that what the key brings onto the screen (the next page
    /// or message, or the answered message drawn again with a new first line) is read in the window and never called as a
    /// new message.
    /// </summary>
    internal void PressDcdu(
        Services.FenixDcduKey key, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        (_dcduReadback ??= new Services.FenixChannelDcduReadback()).NotePress(key);
        (_dcduAnnouncer ??= new Services.FenixChannelDcduAnnouncer()).NoteOwnPress();

        string varKey = Services.FenixChannelDcduKeys.VariableOf(key);
        HandleUIVariableSet(varKey, 1, GetVariables()[varKey], simConnect, announcer);
    }

    /// <summary>
    /// Moves the brightness switch to <paramref name="position"/> (0 Dim, 2 Bright) through the panel's branch, which
    /// steps it once (about 50 in the Fenix's own count behind <see cref="Services.FenixChannelDcduBrightness.LevelKey"/>) and
    /// lets it spring back to the middle.
    /// </summary>
    internal void SetDcduBrightness(
        double position, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        HandleUIVariableSet(DcduBrightnessKey, position, GetVariables()[DcduBrightnessKey], simConnect, announcer);
    }
}
