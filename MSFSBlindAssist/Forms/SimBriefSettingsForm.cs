using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;
/// <summary>
/// Settings form for SimBrief integration
/// </summary>
public partial class SimBriefSettingsForm : Form
{
    private TextBox usernameTextBox = null!;
    private Button saveButton = null!;
    private Button cancelButton = null!;
    private Label instructionsLabel = null!;
    private Label usernameLabel = null!;

    public SimBriefSettingsForm()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void InitializeComponent()
    {
        Text = "SimBrief Settings";
        Size = new System.Drawing.Size(500, 250);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

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

        // Save button
        saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(280, 165),
            Size = new System.Drawing.Size(90, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Save",
            AccessibleDescription = "Save SimBrief settings"
        };
        saveButton.Click += SaveButton_Click;

        // Cancel button
        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(380, 165),
            Size = new System.Drawing.Size(90, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel without saving"
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Controls.Add(instructionsLabel);
        Controls.Add(usernameLabel);
        Controls.Add(usernameTextBox);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);
    }

    private void LoadCurrentSettings()
    {
        var settings = SettingsManager.Current;
        usernameTextBox.Text = settings.SimbriefUsername ?? "";
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        var settings = SettingsManager.Current;
        settings.SimbriefUsername = usernameTextBox.Text.Trim();
        SettingsManager.Save(settings);

        MessageBox.Show("SimBrief settings saved successfully.", "Settings Saved",
                      MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
