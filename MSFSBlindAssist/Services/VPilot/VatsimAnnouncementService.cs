using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.VPilot;

/// <summary>
/// Owns the VATSIM feature's lifecycle: the pipe server, the master switch and the
/// session mute. Wording and per-event gating live in
/// <see cref="VatsimAnnouncementFormatter"/> — that split is what lets the wording be
/// tested without a pipe or a screen reader.
/// </summary>
public sealed class VatsimAnnouncementService : IDisposable
{
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Control _uiContext;
    private readonly VPilotPipeServer _server = new();

    /// <summary>
    /// How deep the shared speech queue (ECAM messages and VATSIM chatter both go
    /// through ScreenReaderAnnouncer.AnnounceWithQueue) may get before a VATSIM message
    /// is dropped rather than queued. The queue drains one entry per QUEUE_INTERVAL_MS
    /// (900 ms), so an unbounded backlog of radio chatter would delay the next ECAM
    /// failure callout by many seconds — unacceptable for a pilot who cannot see the
    /// ECAM. 5 is a judgement call: about 4.5 s of backlog, enough to ride out a short
    /// burst of frequency traffic without dropping anything, short enough that a system
    /// message queued behind it is never meaningfully late. Dropping — not queuing
    /// without bound, and NOT switching to AnnounceImmediate — is deliberate: VATSIM
    /// text is chatter, the newest transmission is the one worth hearing, and the
    /// plugin's own sender already drops its oldest queued message under backlog for
    /// the same reason (see PipeClient.Send).
    /// </summary>
    private const int MaxSharedQueueDepth = 5;

    // Written from the UI thread (ApplySettings, ToggleMute); read from the pipe
    // listener thread inside OnMessageReceived. Volatile for the same cross-thread-flag
    // reason the sibling VPilotPipeServer marks _running/_clientConnected volatile — a
    // lock here would serialize the listener thread against the UI thread just to read
    // a bool. VatsimAnnouncementOptions is a "record" (a reference type — only "record
    // struct" is a value type), so volatile applies to it exactly like any other
    // reference-typed field.
    private volatile VatsimAnnouncementOptions _options = new();
    private volatile bool _enabled;
    private volatile bool _muted;

    public VatsimAnnouncementService(ScreenReaderAnnouncer announcer, Control uiContext)
    {
        _announcer = announcer;
        _uiContext = uiContext;
        _server.MessageReceived += OnMessageReceived;
    }

    public bool IsEnabled => _enabled;
    public bool IsMuted => _muted;

    /// <summary>
    /// Brings the feature into line with the saved settings: installs or refreshes the
    /// plugin when enabled, and starts or stops the pipe server. Returns the install
    /// result when an install was attempted, so the caller can announce the outcome;
    /// null when the feature is off.
    ///
    /// Turning the feature OFF deliberately leaves the DLL in vPilot's folder. Removal
    /// fails while vPilot is running, so "off" would otherwise become a close-vPilot
    /// chore that silently does nothing — and the plugin costs vPilot nothing with
    /// nobody listening (its sender never blocks the event thread).
    /// </summary>
    public VPilotInstallResult? ApplySettings(UserSettings settings)
    {
        bool wasEnabled = _enabled;
        _options = VatsimAnnouncementOptions.From(settings);
        _enabled = settings.VatsimAnnouncementsEnabled;

        // Turning the feature on is an explicit request for it to speak again. Without
        // this, a mute set with Alt+V in an earlier session of the feature (master on,
        // muted, master off, master back on) survives the off->on cycle: _enabled comes
        // back true but _muted is still true too, so the pilot who just asked for the
        // feature back gets permanent, unexplained silence instead.
        if (!wasEnabled && _enabled)
            _muted = false;

        if (!_enabled)
        {
            _server.Stop();
            return null;
        }

        VPilotInstallResult result = VPilotPluginInstaller.Install();
        _server.Start();
        return result;
    }

    /// <summary>Flips the session mute. Not persisted — like the standalone app, a
    /// restart always comes back unmuted, so a mute can never be forgotten about.</summary>
    public bool ToggleMute()
    {
        _muted = !_muted;
        return _muted;
    }

    public VatsimStatus GetStatus()
    {
        string? folder = VPilotPluginInstaller.FindPluginsFolder();
        bool installed = folder != null && VPilotPluginInstaller.IsPluginInstalled(folder);
        bool current = installed && VPilotPluginInstaller.IsPluginCurrent(folder!);
        return new VatsimStatus(
            Enabled: _enabled,
            PluginsFolder: folder,
            PluginInstalled: installed,
            PluginCurrent: current,
            ClientConnected: _server.IsClientConnected,
            Muted: _muted);
    }

    private void OnMessageReceived(string type, string from, string message)
    {
        // Raised directly on VPilotPipeServer's listener thread. The WHOLE body must
        // stay inside this try/catch, not just the BeginInvoke marshal it used to wrap —
        // an exception from the enabled/mute check or from
        // VatsimAnnouncementFormatter.Format used to escape uncaught onto the listener
        // thread, where ListenLoop's own catch treats it exactly like a broken pipe and
        // tears the connection down for a 500 ms reconnect. IsHandleCreated alone still
        // races a concurrent handle destroy on shutdown, so InvalidOperationException
        // keeps its own catch — the same SafeBeginInvoke pattern the A380 bridge forms
        // use.
        try
        {
            if (!_enabled || _muted)
                return;

            string? text = VatsimAnnouncementFormatter.Format(type, from, message, _options);
            if (text == null)
                return;

            // VATSIM chatter shares ScreenReaderAnnouncer's queue with ECAM messages and
            // must never head-of-line-block a system message behind a busy frequency.
            // Drop rather than queue once the shared queue is already this deep — see
            // MaxSharedQueueDepth's doc comment for why dropping, not queuing without
            // bound, is correct here.
            int depth = _announcer.QueuedAnnouncementCount;
            if (depth >= MaxSharedQueueDepth)
            {
                Log.Debug("VPilot", $"Dropped a VATSIM announcement, shared queue depth {depth}");
                return;
            }

            if (!_uiContext.IsHandleCreated || _uiContext.IsDisposed)
                return;
            _uiContext.BeginInvoke(new Action(() =>
            {
                // QUEUED, never AnnounceImmediate: VATSIM chatter must never interrupt a
                // landing callout or a taxi instruction.
                _announcer.AnnounceWithQueue(text);
            }));
        }
        catch (InvalidOperationException)
        {
            // Handle went away between the check and the call. Nothing to do.
        }
        catch (Exception ex)
        {
            Log.Debug("VPilot", $"Announcement marshal failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _server.MessageReceived -= OnMessageReceived;
        _server.Dispose();
    }
}
