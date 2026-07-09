using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>SimBrief section of the unified Settings dialog. Extracted from the retired
/// standalone SimBrief settings form — same controls, same AccessibleNames.</summary>
public class SimBriefPanel : UserControl, ISettingsPanel
{
    private TextBox usernameTextBox = null!;
    private Label instructionsLabel = null!;
    private Label usernameLabel = null!;

    public string TabTitle => "SimBrief";

    public SimBriefPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        // Instructions label
        instructionsLabel = new Label
        {
            Text = "Configure your SimBrief username to enable flight plan loading in the Electronic Flight Bag.\n\n" +
                   "Your SimBrief username is the one you use to log into www.simbrief.com",
            Location = new System.Drawing.Point(20, 20),
            Size = new System.Drawing.Size(440, 60),
            AccessibleName = "Instructions",
            AccessibleDescription = "SimBrief settings instructions"
        };

        // Username label
        usernameLabel = new Label
        {
            Text = "SimBrief Username:",
            Location = new System.Drawing.Point(20, 95),
            Size = new System.Drawing.Size(150, 20),
            AccessibleName = "Username Label"
        };

        // Username text box
        usernameTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, 120),
            Size = new System.Drawing.Size(440, 25),
            AccessibleName = "SimBrief Username",
            AccessibleDescription = "Enter your SimBrief username"
        };

        Controls.Add(instructionsLabel);
        Controls.Add(usernameLabel);
        Controls.Add(usernameTextBox);
    }

    public void LoadFrom(UserSettings settings)
    {
        usernameTextBox.Text = settings.SimbriefUsername ?? "";
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.SimbriefUsername = usernameTextBox.Text.Trim();
    }

    public void OnLeaving()
    {
    }
}
