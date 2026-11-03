namespace MSFSBlindAssist.Aircraft;

// The DCDU's side of the Fenix display channel: the calls its window makes (Forms/Fenix/FenixChannelDcduForm.cs, whose rows
// are Services/FenixChannelDcduRows.cs and whose keys are Services/FenixChannelDcduKeys.cs), the key that opens it
// (Ctrl+Shift+D) and the sampler that calls a new message (Services/FenixChannelDcduAnnouncer.cs). Nothing here opens the
// channel (the sampler only takes the monitor's snapshot), and no press is written here: a key or a brightness pick goes
// through HandleUIVariableSet, the S_DCDU1_* branches, which press and release ([A320-3]) and carry the variant's refusal
// in front of them.
public partial class FenixA320Definition
{
    /// <summary>The captain's DCDU brightness switch, the variable the window's combo steps.</summary>
    internal const string DcduBrightnessKey = "S_DCDU1_DIM_BRT";

    /// <summary>
    /// How long after a brightness pick the switch is back in the middle and read again: the hold before the return write
    /// and the gap before the forced read (<see cref="PulseSpringSwitch"/>). The window shows Mid again then.
    /// </summary>
    internal const int DcduSpringBackMs = ButtonPressHoldMs + ButtonReleaseGapMs;

    // The tracker of the pilot's own page and message keys, made by the first press of one (UI thread only).
    private Services.FenixChannelDcduAnnouncer? _dcduAnnouncer;

    /// <summary>
    /// Whether the new-message call is muted: the Ctrl+M row is unticked, or the variant says there is no DCDU, which the
    /// channel still reads trusted (measured in plan 01). An unread field (null) mutes nothing ([FNX-11]).
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
    // answers with the call when one is due: spoken queued, a background change the pilot did not trigger ([CORE-7]).
    partial void SampleChannelDcdu(ChannelAnnounceContext ctx)
    {
        Services.FenixChannelSnapshot s = ctx.Monitor.Snapshot(Services.FenixChannelDisplay.Dcdu);
        Services.FenixChannelSection<Services.FenixDcduScreen> section = Services.FenixChannelDcdu.Read(s);
        if (s.Verdict.Trusted && !section.IsOk)
            ctx.Monitor.ReportFeatureProblem(Services.FenixChannelDcdu.DisplayName, section.Problem ?? "no data");

        bool rowMuted = Services.FenixChannelAnnouncementRows.IsMuted(Services.FenixChannelAnnouncementRows.DcduKey);
        string? call = (_dcduAnnouncer ??= new Services.FenixChannelDcduAnnouncer())
            .Sample(section, s.Connection, DcduCallMuted(Variant, rowMuted), ctx.NowMs);
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
    /// Presses the DCDU key <paramref name="key"/> through the panel's button for it. A page or message key tells the
    /// announcer first, so that what the key brings onto the screen is read in the window and never called as a new
    /// message; the announcer is made by the first such key.
    /// </summary>
    internal void PressDcdu(
        Services.FenixDcduKey key, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (Services.FenixChannelDcduKeys.IsNavigation(key)) (_dcduAnnouncer ??= new Services.FenixChannelDcduAnnouncer()).NoteNavigation();

        string varKey = Services.FenixChannelDcduKeys.VariableOf(key);
        HandleUIVariableSet(varKey, 1, GetVariables()[varKey], simConnect, announcer);
    }

    /// <summary>
    /// Moves the brightness switch to <paramref name="position"/> (0 Dim, 2 Bright) through the panel's branch, which
    /// steps it once and lets it spring back to the middle.
    /// </summary>
    internal void SetDcduBrightness(
        double position, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        HandleUIVariableSet(DcduBrightnessKey, position, GetVariables()[DcduBrightnessKey], simConnect, announcer);
    }
}
