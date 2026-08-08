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

    private VatsimAnnouncementOptions _options = new();
    private bool _enabled;
    private bool _muted;

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
        _options = VatsimAnnouncementOptions.From(settings);
        _enabled = settings.VatsimAnnouncementsEnabled;

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
        if (!_enabled || _muted)
            return;

        string? text = VatsimAnnouncementFormatter.Format(type, from, message, _options);
        if (text == null)
            return;

        // Raised on the pipe listener thread. IsHandleCreated alone races a concurrent
        // handle destroy on shutdown, so catch the InvalidOperationException too — the
        // same SafeBeginInvoke pattern the A380 bridge forms use.
        try
        {
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
