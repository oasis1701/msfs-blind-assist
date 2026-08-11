using MSFSBlindAssist.Services.VPilot;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>
/// VATSIM section of the unified Settings dialog: the master switch, one toggle per
/// vPilot event, and a status readout.
///
/// The panel has NO side effects. ApplyTo only writes settings; the plugin install runs
/// in MainForm.ApplyRuntimeSettings() after OK, so pressing Cancel really does cancel.
///
/// There is deliberately no Browse button and no folder override. vPilot has no portable
/// install mode and always writes HKCU\Software\vPilot\Install_Dir, so a user-chosen path
/// could only ever point somewhere vPilot isn't — see VPilotPluginInstaller.ResolvePluginsFolder.
/// </summary>
public class VatsimPanel : UserControl, ISettingsPanel
{
    private readonly Func<VatsimStatus?>? _statusProvider;

    private CheckBox _enabled = null!;
    private GroupBox _eventsGroup = null!;
    private CheckBox _connect = null!;
    private CheckBox _disconnect = null!;
    private CheckBox _privateMessages = null!;
    private CheckBox _radioMessages = null!;
    private CheckBox _selcal = null!;
    private TextBox _status = null!;

    public string TabTitle => "VATSIM";

    public VatsimPanel(Func<VatsimStatus?>? statusProvider = null)
    {
        _statusProvider = statusProvider;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AutoScroll = true;

        _enabled = new CheckBox
        {
            Text = "Announce VATSIM events from vPilot",
            Location = new Point(12, 12),
            Size = new Size(460, 25),
            AccessibleName = "Announce VATSIM events from vPilot",
            AccessibleDescription = "Turns VATSIM announcements on. When you press OK, the vPilot plugin is installed into your vPilot Plugins folder.",
        };
        _enabled.CheckedChanged += (_, _) =>
        {
            _eventsGroup.Enabled = _enabled.Checked;
            RefreshStatus();
        };

        _eventsGroup = new GroupBox
        {
            Text = "Announce",
            Location = new Point(12, _enabled.Bottom + 8),
            Size = new Size(460, 165),
            AccessibleName = "Announce",
            AccessibleDescription = "Choose which vPilot events are spoken",
        };

        _connect = MakeCheck("Connected to the network", 25,
            "Spoken when vPilot connects, naming your callsign");
        _disconnect = MakeCheck("Disconnected from the network", 50,
            "Spoken when vPilot disconnects");
        _privateMessages = MakeCheck("Private messages", 75,
            "Spoken when a controller or pilot sends you a private message");
        _radioMessages = MakeCheck("Radio messages (on-frequency)", 100,
            "Spoken for text messages on the frequencies you are tuned to");
        _selcal = MakeCheck("SELCAL alerts", 125,
            "Spoken when a station sends your aircraft a SELCAL alert");

        _eventsGroup.Controls.AddRange(new Control[]
        {
            _connect, _disconnect, _privateMessages, _radioMessages, _selcal
        });

        // Positions are relative to the control above (.Bottom + N) rather than
        // hardcoded — the same approach WeatherPanel takes. This is NOT font/DPI
        // resilience: _enabled, _eventsGroup and _status all carry an explicit Size, so
        // each .Bottom below is a compile-time constant (37 / 210 / 240) at any font or
        // DPI, numerically identical to the hardcoded 45 / 220 / 242 it replaces — the
        // group box's own children are still hardcoded at y = 25…125 inside a fixed
        // 165 px box, exactly where a real font/DPI change would still collide. What the
        // chain actually buys is that moving or resizing ONE control here no longer
        // requires hand-editing every Y coordinate below it. AutoScroll only stops
        // clipping at the panel edge; it does nothing about controls colliding with each
        // other.
        var statusLabel = new Label
        {
            Text = "Status:",
            Location = new Point(12, _eventsGroup.Bottom + 10),
            Size = new Size(200, 20),
        };

        // A read-only TextBox, deliberately NOT a Label: a Label is not in the tab order,
        // so with a screen reader it has to be hunted for with the review cursor.
        _status = new TextBox
        {
            Location = new Point(12, statusLabel.Bottom + 2),
            Size = new Size(460, 90),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            // OFF, matching the SayIntentions flight-information window's precedent:
            // wrapped, the plugins-folder path becomes several visual lines and a
            // screen reader's down-arrow walks the fragments instead of the line.
            WordWrap = false,
            AccessibleName = "vPilot status",
            AccessibleDescription = "Whether vPilot was found, whether the plugin is installed, and whether vPilot is connected",
        };

        Controls.AddRange(new Control[] { _enabled, _eventsGroup, statusLabel, _status });
    }

    private static CheckBox MakeCheck(string text, int y, string description) => new()
    {
        Text = text,
        Location = new Point(16, y),
        Size = new Size(420, 25),
        AccessibleName = text,
        AccessibleDescription = description,
    };

    /// <summary>
    /// Refreshes the status box when this panel becomes the active tab, not only when
    /// the Settings dialog first opens. SettingsForm hosts every panel as a UserControl
    /// docked Fill inside its own TabPage; a TabControl shows exactly one page's content
    /// at a time, and switching tabs flips the effective Visible state of the page that
    /// left and the page that arrived, which propagates down to Visible children exactly
    /// like this one (confirmed against a throwaway WinForms probe reproducing this
    /// SettingsForm/TabPage/UserControl-Dock-Fill shape before relying on it here — the
    /// same idiom AccessGSXForm, FBWA380RmpForm and HS787FMCForm already use on
    /// top-level Forms, just not, until now, on a TabPage-hosted panel). Without this,
    /// a pilot who opens Settings, starts vPilot, then tabs back to VATSIM kept hearing
    /// the box's stale first read from before vPilot was running.
    ///
    /// RefreshStatus only reads the registry and calls File.Exists twice, then writes
    /// _status.Text — it never announces anything, so firing it again on every visit
    /// (including the couple of extra times WinForms raises this while the control is
    /// still being parented into its TabPage, before the dialog is even shown) costs
    /// nothing and breaks no rule: the screen reader is the one announcing the tab
    /// change, same as any other tab; this only keeps the box's TEXT from being stale
    /// once the pilot's own Tab key or review cursor gets to it.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
            RefreshStatus();
    }

    private void RefreshStatus()
    {
        // Prefer the live service — only it knows whether vPilot is actually attached.
        // The fallback below is for a VatsimPanel constructed WITHOUT a status provider
        // at all (there is currently one caller, SettingsForm, and it always passes
        // one) — NOT for the feature being off: GetStatus() answers just fine either
        // way, switch on or off. If _statusProvider is null, or is set but its target
        // (e.g. vatsimService) is gone, fall back to a static probe, which still
        // answers "where is vPilot" and "is the plugin there" on its own.
        VatsimStatus? status = _statusProvider?.Invoke();

        if (status == null)
        {
            string? folder = VPilotPluginInstaller.FindPluginsFolder();
            bool installed = folder != null && VPilotPluginInstaller.IsPluginInstalled(folder);
            bool current = installed && VPilotPluginInstaller.IsPluginCurrent(folder!);
            status = new VatsimStatus(_enabled.Checked, folder, installed, current,
                ClientConnected: false, Muted: false);
        }
        else
        {
            // Reflect the unsaved state of the dialog, not the last-applied one. The
            // folder and install state need no such adjustment any more: with no override
            // to preview, the provider already resolved the same folder the install will
            // use.
            status = status with { Enabled = _enabled.Checked };
        }

        _status.Text = VatsimStatusText.Compose(status);
    }

    public void LoadFrom(UserSettings settings)
    {
        _enabled.Checked = settings.VatsimAnnouncementsEnabled;
        _connect.Checked = settings.VatsimAnnounceConnect;
        _disconnect.Checked = settings.VatsimAnnounceDisconnect;
        _privateMessages.Checked = settings.VatsimAnnouncePrivateMessages;
        _radioMessages.Checked = settings.VatsimAnnounceRadioMessages;
        _selcal.Checked = settings.VatsimAnnounceSelcal;
        _eventsGroup.Enabled = _enabled.Checked;
        RefreshStatus();
    }

    public bool Validate(out string error, out Control? focus)
    {
        // Deliberately never fails. A vPilot that cannot be found must not stop the pilot
        // saving unrelated settings — the status field explains what is wrong.
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.VatsimAnnouncementsEnabled = _enabled.Checked;
        settings.VatsimAnnounceConnect = _connect.Checked;
        settings.VatsimAnnounceDisconnect = _disconnect.Checked;
        settings.VatsimAnnouncePrivateMessages = _privateMessages.Checked;
        settings.VatsimAnnounceRadioMessages = _radioMessages.Checked;
        settings.VatsimAnnounceSelcal = _selcal.Checked;
    }

    public void OnLeaving()
    {
    }
}
