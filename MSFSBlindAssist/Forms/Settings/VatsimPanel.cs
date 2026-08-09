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

        // Positions are relative to the control above, not hardcoded, so a larger system
        // font or a display-scaling change grows the panel instead of overlapping it —
        // the same approach WeatherPanel takes. AutoScroll only stops clipping at the
        // panel edge; it does nothing about controls colliding with each other.
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

    private void RefreshStatus()
    {
        // Prefer the live service — only it knows whether vPilot is actually attached.
        // Before it exists (or with the feature off at startup) fall back to a static
        // probe, which still answers "where is vPilot" and "is the plugin there".
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
