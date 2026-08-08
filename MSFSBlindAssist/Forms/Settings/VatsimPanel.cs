using MSFSBlindAssist.Services.VPilot;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>
/// VATSIM section of the unified Settings dialog: the master switch, one toggle per
/// vPilot event, and a status readout.
///
/// The panel has NO side effects. ApplyTo only writes settings; the plugin install runs
/// in MainForm.ApplyRuntimeSettings() after OK, so pressing Cancel really does cancel.
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
    private Button _browse = null!;

    private string _pluginsFolderOverride = "";

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
            Location = new Point(12, 45),
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

        var statusLabel = new Label
        {
            Text = "Status:",
            Location = new Point(12, 220),
            Size = new Size(200, 20),
        };

        // A read-only TextBox, deliberately NOT a Label: a Label is not in the tab order,
        // so with a screen reader it has to be hunted for with the review cursor.
        _status = new TextBox
        {
            Location = new Point(12, 242),
            Size = new Size(460, 90),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "vPilot status",
            AccessibleDescription = "Whether vPilot was found, whether the plugin is installed, and whether vPilot is connected",
        };

        _browse = new Button
        {
            Text = "Browse...",
            Location = new Point(12, 340),
            Size = new Size(120, 28),
            AccessibleName = "Browse for the vPilot folder",
            AccessibleDescription = "Select your vPilot installation folder, or its Plugins folder, if it was not found automatically",
        };
        _browse.Click += OnBrowseClicked;

        Controls.AddRange(new Control[] { _enabled, _eventsGroup, statusLabel, _status, _browse });
    }

    private static CheckBox MakeCheck(string text, int y, string description) => new()
    {
        Text = text,
        Location = new Point(16, y),
        Size = new Size(420, 25),
        AccessibleName = text,
        AccessibleDescription = description,
    };

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select your vPilot folder (or its Plugins folder)",
            UseDescriptionForTitle = true,
        };
        if (!string.IsNullOrWhiteSpace(_pluginsFolderOverride))
            dialog.SelectedPath = _pluginsFolderOverride;

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _pluginsFolderOverride = dialog.SelectedPath;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        // Prefer the live service — only it knows whether vPilot is actually attached.
        // Before it exists (or with the feature off at startup) fall back to a static
        // probe, which still answers "where is vPilot" and "is the plugin there".
        VatsimStatus? status = _statusProvider?.Invoke();

        if (status == null)
        {
            string? folder = ResolveFolderForPreview();
            bool installed = folder != null && VPilotPluginInstaller.IsPluginInstalled(folder);
            bool current = installed && VPilotPluginInstaller.IsPluginCurrent(folder!);
            status = new VatsimStatus(_enabled.Checked, folder, installed, current,
                ClientConnected: false, Muted: false);
        }
        else
        {
            // Reflect the unsaved state of the dialog, not the last-applied one.
            status = status with { Enabled = _enabled.Checked };
            if (!string.IsNullOrWhiteSpace(_pluginsFolderOverride))
            {
                string? preview = ResolveFolderForPreview();
                if (preview != null) status = status with { PluginsFolder = preview };
            }
        }

        _status.Text = VatsimStatusText.Compose(status);
    }

    /// <summary>Resolves the folder using the value currently typed into the dialog,
    /// so Browse shows its effect before OK is pressed.</summary>
    private string? ResolveFolderForPreview()
    {
        if (string.IsNullOrWhiteSpace(_pluginsFolderOverride))
            return VPilotPluginInstaller.FindPluginsFolder();

        return VPilotPluginInstaller.ResolvePluginsFolder(
            _pluginsFolderOverride, null,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Directory.Exists);
    }

    public void LoadFrom(UserSettings settings)
    {
        _enabled.Checked = settings.VatsimAnnouncementsEnabled;
        _connect.Checked = settings.VatsimAnnounceConnect;
        _disconnect.Checked = settings.VatsimAnnounceDisconnect;
        _privateMessages.Checked = settings.VatsimAnnouncePrivateMessages;
        _radioMessages.Checked = settings.VatsimAnnounceRadioMessages;
        _selcal.Checked = settings.VatsimAnnounceSelcal;
        _pluginsFolderOverride = settings.VPilotPluginsFolderOverride ?? "";
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
        settings.VPilotPluginsFolderOverride = _pluginsFolderOverride;
    }

    public void OnLeaving()
    {
    }
}
