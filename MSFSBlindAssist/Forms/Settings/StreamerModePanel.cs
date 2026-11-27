using System.Drawing;
using System.Windows.Forms;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>
/// Streamer Mode section of the unified Settings dialog: toggles camera head movement
/// for cockpit actions, flows, checklists, and MCDU/EFB camera focus during live streaming.
/// </summary>
public class StreamerModePanel : UserControl, ISettingsPanel
{
    private CheckBox _streamerModeCheckBox = null!;
    private TextBox _descriptionTextBox = null!;

    public string TabTitle => "Streamer Mode";

    public StreamerModePanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        const int labelHeight = 23;
        var yPos = 20;

        var mainGroup = new GroupBox
        {
            Text = "Streamer Mode options",
            Location = new Point(20, yPos),
            Size = new Size(600, 220),
            AccessibleName = "Streamer Mode options section"
        };

        _streamerModeCheckBox = new CheckBox
        {
            Text = "Enable &Streamer mode (cockpit camera movements for live streaming)",
            Location = new Point(16, 30),
            Size = new Size(560, labelHeight),
            AutoSize = true,
            AccessibleName = "Enable Streamer mode",
            AccessibleDescription = "Enables automatic cockpit camera movement when controls are operated, simulating pilot head movement for live streaming."
        };
        mainGroup.Controls.Add(_streamerModeCheckBox);

        // Read-only TextBox for accessible explanation (VAT-13: in tab order for screen readers)
        _descriptionTextBox = new TextBox
        {
            Text = "Streamer mode enhances the viewing experience for sighted audiences watching live flight simulator streams by blind content creators.\r\n\r\n" +
                   "• Moves the cockpit camera to the relevant panel or instrument when controls are operated (manually or via First Officer flows and checklists).\r\n" +
                   "• Holds camera view during grouped actions and smoothly returns looking forward after actions finish (2 seconds for manual actions).\r\n" +
                   "• Adds a 'Focus Camera' toggle button to MCDU and EFB windows to frame them in close-up for viewers.",
            Location = new Point(16, 65),
            Size = new Size(560, 135),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Streamer mode feature description",
            AccessibleRole = AccessibleRole.StaticText
        };
        mainGroup.Controls.Add(_descriptionTextBox);

        Controls.Add(mainGroup);
    }

    public void LoadFrom(UserSettings settings)
    {
        _streamerModeCheckBox.Checked = settings.StreamerModeEnabled;
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = string.Empty;
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.StreamerModeEnabled = _streamerModeCheckBox.Checked;
    }

    public void OnLeaving()
    {
        // No transient audio or resources to tear down
    }
}

