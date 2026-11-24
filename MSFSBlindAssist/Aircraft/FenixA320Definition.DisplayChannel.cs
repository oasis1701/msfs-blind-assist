using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

// The Fenix display channel's side of the definition (the seams are declared in FenixA320Definition.Hooks.cs).
// The channel is the Fenix's local display feed on 127.0.0.1:18083, read only. Services/FenixChannelMonitor.cs
// owns one session of it: it decodes what comes and decides, display by display, whether that can be trusted. It
// never speaks and never touches a control. This partial gives the monitor its home: Start and Stop beside the
// cabin's, the key dispatch, the first look at a channel key, which hands it to the seam of the feature that owns it,
// and the two ways a display's window opens: a Fenix windows list entry (ShowChannelWindow) and a display key
// (OpenChannelWindow). It also holds the announcer host (Services/FenixChannelAnnouncers.cs), a timer on the UI thread
// that, every 100 ms, runs the samplers: first the session notice's (SampleChannelNotice, below), then one for each
// announcer (those are seams too, declared in FenixA320Definition.Hooks.cs). The host starts with the monitor and ends
// before it, in the same Start and Stop: the announcers exist exactly while the channel does.
// A key whose seam throws does what it would with no feature: a display key gives its AI read, ShowDCDU reaches
// MainForm's sentence for other aircraft, and the others do nothing. The session notice is no seam: it says one line per
// Fenix load, when the channel cannot be used.
public partial class FenixA320Definition
{
    // This instance's monitor: made by StartDisplayChannelBackground, disposed and cleared by Stop. Start, Stop,
    // the value peek, the display key and the window list entry all run on the UI thread (the sim's updates, a
    // hotkey, a list entry, and the quit or aircraft switch that calls OnSwitchedAway), so a plain field is enough;
    // each method reads it once.
    private Services.FenixChannelMonitor? _displayChannelMonitor;

    /// <summary>
    /// This instance's display channel monitor; null until Start, and after Stop. A Fenix windows list entry reads it
    /// too, through <see cref="ShowChannelWindow{TForm}"/>, since an entry has no key context to take the monitor
    /// from; and the tests read it.
    /// </summary>
    internal Services.FenixChannelMonitor? DisplayChannelMonitor => _displayChannelMonitor;

    // This instance's announcer host: made by StartDisplayChannelBackground beside the monitor, disposed and cleared
    // by Stop before it. UI thread only, like the monitor field.
    private Services.FenixChannelAnnouncers? _channelAnnouncers;

    /// <summary>
    /// What an announcer's sampler is given on every tick of the announcer host: this instance's channel monitor, which
    /// the sampler takes its snapshot from; the announcer it speaks through; and the tick's clock reading in
    /// milliseconds (<see cref="Environment.TickCount64"/>, the clock of the monitor's snapshots too), the same for every
    /// sampler of the tick. The monitor is never null here, unlike <see cref="ChannelKeyContext"/>'s: the host is made
    /// with the monitor and ends before it.
    /// </summary>
    internal sealed record ChannelAnnounceContext(
        Services.FenixChannelMonitor Monitor, Accessibility.ScreenReaderAnnouncer Announcer, long NowMs);

    // Starts the channel with the Fenix (EnsureFenixBackground: UI thread, once per definition instance). The
    // monitor asks the data service for the Fenix build on each connection. The announcer host starts beside it,
    // with the same lifetime, and gives the announcer to each sampler: it is the samplers' voice. The host runs the
    // samplers in the order of the list below, each a lambda, since C# cannot make a delegate from a partial method
    // nothing implements. A throw in a sampler is reported through the monitor under the name "announcer" and costs
    // only itself. The session
    // notice is made beside the monitor and lives as long as it: this definition is one Fenix load, and an aircraft
    // switch and back makes a new definition, so a new monitor and a new notice, which has said nothing yet.
    partial void StartDisplayChannelBackground(Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (_displayChannelMonitor is not null) return;

        var monitor = new Services.FenixChannelMonitor(ct => DataService.GetSoftwareVersionAsync(ct));
        _displayChannelMonitor = monitor;
        monitor.Start();

        var notice = new Services.FenixChannelNotice();
        var announcers = new Services.FenixChannelAnnouncers(
            new Action<long>[]
            {
                // The session notice (Services/FenixChannelNotice.cs): first in the list. Its sampler is a method of
                // its own below, since it is no seam and nothing implements it later.
                now => SampleChannelNotice(notice, new ChannelAnnounceContext(monitor, announcer, now)),
                now => SampleChannelFma(new ChannelAnnounceContext(monitor, announcer, now)),
                now => SampleChannelEwdAlerts(new ChannelAnnounceContext(monitor, announcer, now)),
                now => SampleChannelDcdu(new ChannelAnnounceContext(monitor, announcer, now)),
                now => SampleChannelNd(new ChannelAnnounceContext(monitor, announcer, now)),
            },
            problem => monitor.ReportFeatureProblem("announcer", problem),
            () => Environment.TickCount64);
        _channelAnnouncers = announcers;
        announcers.Start();
    }

    // The session notice's sampler, run by the host on every tick. It hands the notice the monitor's session (the
    // build, the refusal, the first data) and whether the Fenix is ready: it has published its config, which is when
    // Variant knows the airframe. The minute is counted from the Fenix being ready and not from the monitor's
    // start, because the monitor starts with the first sim update, which can come in the sim's main menu before any
    // Fenix is loaded: a minute with no data counted from there would speak a notice that is false, and spend the one
    // the load has (FenixChannelNotice.Sample says the rest). The notice returns its text once at most. It is spoken
    // with Announce, which does not interrupt, and written to the channel's log as "session notice: <text>", so that a
    // check in the sim can measure whether it spoke without ears. Nothing else is logged. Internal for the tests.
    internal void SampleChannelNotice(Services.FenixChannelNotice notice, ChannelAnnounceContext ctx)
    {
        // Announce drops its line while Suppressed, so asking now would spend the notice unheard: wait for a free tick.
        if (ctx.Announcer.Suppressed) return;

        string? text = notice.Sample(ctx.Monitor.Session, Variant.Type != FenixAircraftType.Unknown, ctx.NowMs);
        if (text is null) return;

        ctx.Announcer.Announce(text);
        ctx.Monitor.ReportFeatureProblem("session notice", text);
    }

    // Stops the channel in OnSwitchedAway, before the data service is disposed: the host first, so that no sampler
    // runs against a monitor that is ending, then the monitor. Dispose ends both calls and the build loop at once,
    // takes no lock and never throws; the host's never throws either, so the monitor is always disposed.
    partial void StopDisplayChannelBackground()
    {
        Services.FenixChannelAnnouncers? announcers = _channelAnnouncers;
        _channelAnnouncers = null;
        announcers?.Dispose();

        Services.FenixChannelMonitor? monitor = _displayChannelMonitor;
        _displayChannelMonitor = null;
        monitor?.Dispose();
    }

    /// <summary>
    /// What a channel key's seam is given: the pieces HandleHotkeyAction was called with, and this instance's channel
    /// monitor, which is null before Start, after Stop and in the tests, so a seam that reads it handles null.
    /// </summary>
    internal sealed record ChannelKeyContext(
        SimConnect.SimConnectManager SimConnect,
        Accessibility.ScreenReaderAnnouncer Announcer,
        System.Windows.Forms.Form ParentForm,
        Hotkeys.HotkeyManager HotkeyManager,
        Services.FenixChannelMonitor? Monitor);

    /// <summary>
    /// The feature that answers a channel key, which is the seam the key reaches. None is every other key.
    /// </summary>
    internal enum ChannelKeyOwner { None, Pfd, Nd, Ewd, Sd, Isis, Distances, Dcdu }

    /// <summary>
    /// Who owns an action. The PFD owns its display key and the six speed reads, the ND its display key and Ctrl+W,
    /// the E/WD, the SD and the ISIS their display keys, the distances D and Shift+D, and the DCDU the app-wide
    /// ShowDCDU. Every other action has no owner and never reaches a seam. Pure, so a test can pin the table.
    /// </summary>
    internal static ChannelKeyOwner ChannelKeyOwnerOf(Hotkeys.HotkeyAction action) => action switch
    {
        Hotkeys.HotkeyAction.ReadDisplayPFD
            or Hotkeys.HotkeyAction.ReadSpeedGD
            or Hotkeys.HotkeyAction.ReadSpeedS
            or Hotkeys.HotkeyAction.ReadSpeedF
            or Hotkeys.HotkeyAction.ReadSpeedVLS
            or Hotkeys.HotkeyAction.ReadSpeedVS
            or Hotkeys.HotkeyAction.ReadSpeedVFE => ChannelKeyOwner.Pfd,
        Hotkeys.HotkeyAction.ReadDisplayND
            or Hotkeys.HotkeyAction.ReadNDWaypoint => ChannelKeyOwner.Nd,
        Hotkeys.HotkeyAction.ReadDisplayUpperECAM => ChannelKeyOwner.Ewd,
        Hotkeys.HotkeyAction.ReadDisplayLowerECAM => ChannelKeyOwner.Sd,
        Hotkeys.HotkeyAction.ReadDisplayISIS => ChannelKeyOwner.Isis,
        Hotkeys.HotkeyAction.ReadDistanceToDest
            or Hotkeys.HotkeyAction.ReadDistanceToTOD => ChannelKeyOwner.Distances,
        Hotkeys.HotkeyAction.ShowDCDU => ChannelKeyOwner.Dcdu,
        _ => ChannelKeyOwner.None,
    };

    /// <summary>
    /// One channel key's seam call. It gets the key's handled flag by reference and sets it when it answers.
    /// </summary>
    internal delegate void ChannelKeyCall(ref bool handled);

    /// <summary>
    /// Runs one channel key's seam call so that a seam which throws costs only its own key: a fault in one feature
    /// stops that feature only, and the pilot never loses a key to it. Without this an exception would reach the
    /// app's UI-thread handler and the key would do nothing at all, not even its AI read. The exception is caught and
    /// reported, and <paramref name="handled"/> ends false whatever the seam had set before it threw: a key that
    /// failed is not an answered key. It then does what it does with no feature: a display key gives its AI read,
    /// ShowDCDU reaches MainForm's sentence, and the others do nothing. Nothing is said of the failure itself. The
    /// report goes to the monitor's log when there is a monitor ("Pfd: key failed: InvalidOperationException: ...",
    /// which the monitor writes once for each distinct line, so a seam that throws at every press is one line), and
    /// otherwise to debug.log, as RunSeam does for the background seams.
    /// </summary>
    internal static void RunChannelKeySeam(
        ChannelKeyOwner owner, ChannelKeyContext ctx, ref bool handled, ChannelKeyCall call)
    {
        try
        {
            call(ref handled);
        }
        catch (Exception ex)
        {
            handled = false;
            string problem = $"key failed: {ex.GetType().Name}: {ex.Message}";
            if (ctx.Monitor is { } monitor)
                monitor.ReportFeatureProblem(owner.ToString(), problem);
            else
                Log.Error("Fenix", $"{owner}: {problem}", ex);
        }
    }

    // HandleHotkeyAction's first look at any action, before its own switch. A channel key goes to the seam of the
    // feature that owns it (ChannelKeyOwnerOf), which sets handled when it answered the key; every other key carries
    // on untouched. A seam nothing implements is no call at all (C# drops it), so handled stays false and the key does
    // what it did before the feature: a display key gives its AI read, ShowDCDU reaches MainForm's sentence, and the
    // others do nothing. The switch runs inside RunChannelKeySeam, which gives the seam the dispatch's own handled by
    // reference, so its answer reaches HandleHotkeyAction, and keeps a seam that throws from costing more than its own
    // key. Whether a display is trusted is for the seam to ask the monitor ([FNX-22]): nothing here reads channel data.
    partial void TryChannelDisplayRead(Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
    {
        ChannelKeyOwner owner = ChannelKeyOwnerOf(action);
        if (owner == ChannelKeyOwner.None) return;

        RunChannelKeySeam(owner, ctx, ref handled,
            (ref bool seamHandled) => CallChannelKeySeam(owner, action, ctx, ref seamHandled));
    }

    // The switch: one case per owner, each the one call of that owner's seam, with the key's handled flag by reference.
    private void CallChannelKeySeam(
        ChannelKeyOwner owner, Hotkeys.HotkeyAction action, ChannelKeyContext ctx, ref bool handled)
    {
        switch (owner)
        {
            case ChannelKeyOwner.Pfd:
                ChannelPfdKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Nd:
                ChannelNdKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Ewd:
                ChannelEwdKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Sd:
                ChannelSdKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Isis:
                ChannelIsisKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Distances:
                ChannelDistancesKey(action, ctx, ref handled);
                break;
            case ChannelKeyOwner.Dcdu:
                ChannelDcduKey(action, ctx, ref handled);
                break;
        }
    }

    /// <summary>
    /// Says that a key's value cannot be read: "&lt;name&gt; not available", the words of the other aircraft's key
    /// readouts ("Top of descent not available"), spoken at once because a key's answer interrupts. A seam says it
    /// when its display is not trusted or the value is not shown, so every channel key refuses in the same words.
    /// </summary>
    internal void SpeakNotAvailable(ChannelKeyContext ctx, string name)
        => ctx.Announcer.AnnounceImmediate($"{name} not available");

    /// <summary>
    /// Opens a display's window on this instance's channel monitor, whatever the display's verdict: the window shows
    /// one status row, "&lt;display&gt;: not available, &lt;reason&gt;", while the display is not trusted, and its
    /// rows once it is. A Fenix windows list entry calls this directly, and a display key calls it through
    /// <see cref="OpenChannelWindow{TForm}"/>, for a trusted or a dark display. It opens the window through
    /// <see cref="ShowFenixWindow{T}"/>, so there is one window for each type of <typeparamref name="TForm"/>, a second
    /// call brings the open one forward, and the aircraft switch closes it: each display's window is a class of its own
    /// for that reason. With no monitor (before Start, after Stop) there is nothing to show: the pilot is told
    /// "&lt;displayName&gt; display not available" at once and nothing opens.
    /// </summary>
    /// <param name="announcer">Where the refusal is spoken.</param>
    /// <param name="displayName">The display's short name: "PFD", "ND", "E/WD", "SD", "ISIS" or "DCDU".</param>
    /// <param name="factory">Builds the display's window, a class of its own, on the monitor it is given. Called only
    /// when no window of that type is open.</param>
    internal void ShowChannelWindow<TForm>(
        Accessibility.ScreenReaderAnnouncer announcer,
        string displayName,
        Func<Services.FenixChannelMonitor, TForm> factory) where TForm : Forms.Fenix.FenixChannelDisplayForm
    {
        Services.FenixChannelMonitor? monitor = _displayChannelMonitor;
        if (monitor is null)
        {
            announcer.AnnounceImmediate($"{displayName} display not available");
            return;
        }

        ShowFenixWindow(() => factory(monitor), f => f.ShowForm());
    }

    /// <summary>
    /// A display key's way to its window: opens it while the display is trusted at the press, or off, and says whether
    /// it did. An off display (<see cref="Services.FenixDisplayVerdict.IsDisplayOff"/>: the channel is healthy and no
    /// powered unit shows the display, whether it is dark or shown only on another unit) opens on its one status row,
    /// "&lt;display&gt;: not available, display off", as its Fenix windows list entry does, because there is nothing on
    /// the pilot's screens for an AI read to read (the owner's ruling, spec 2.4). False, having done nothing at all,
    /// when there is no monitor or the display is not trusted for
    /// any other reason (the channel stopped, refused or silent, a build not checked, a shape latch, a
    /// text code the app does not know), so the key goes on to the AI read with no extra words (the PMDG 737's silent
    /// fallback, [WASM-29]), because the screen is live, and the key context's hotkey manager is not even touched.
    /// Otherwise, trusted or off, both hotkey modes are left first, because a window opened with a mode still on loses
    /// its keys to it (the PMDG 737's display windows and <see cref="ShowFenixWindows"/> leave theirs too), and then the
    /// window opens through <see cref="ShowChannelWindow{TForm}"/>, or the open one comes forward, and the result is
    /// true. The verdict is judged at the press on the key context's monitor and the window is built on this
    /// instance's own: in the app they are one object, because HandleHotkeyAction makes the context from that field.
    /// </summary>
    internal bool OpenChannelWindow<TForm>(
        ChannelKeyContext ctx,
        Services.FenixChannelDisplay display,
        string displayName,
        Func<Services.FenixChannelMonitor, TForm> factory) where TForm : Forms.Fenix.FenixChannelDisplayForm
    {
        if (ctx.Monitor is not { } monitor) return false;

        Services.FenixDisplayVerdict verdict = monitor.Snapshot(display).Verdict;
        if (!verdict.Trusted && !verdict.IsDisplayOff) return false;

        ctx.HotkeyManager.ExitOutputHotkeyMode();
        ctx.HotkeyManager.ExitInputHotkeyMode();
        ShowChannelWindow(ctx.Announcer, displayName, factory);
        return true;
    }
}
