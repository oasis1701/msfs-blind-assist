using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Settings form for Gemini AI API key configuration
/// </summary>
public partial class GeminiApiKeySettingsForm : Form
{
    private TextBox apiKeyTextBox = null!;
    private Button saveButton = null!;
    private Button cancelButton = null!;
    private Label instructionsLabel = null!;
    private Label apiKeyLabel = null!;
    private LinkLabel linkLabel = null!;

    public GeminiApiKeySettingsForm()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void InitializeComponent()
    {
        Text = "Gemini API Key Settings";
        Size = new System.Drawing.Size(550, 310);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // Instructions label
        instructionsLabel = new Label
        {
            Text = "Configure your Google Gemini API key to enable AI-powered cockpit display reading.\n\n" +
                   "This feature allows you to capture and analyze cockpit displays (PFD, ECAM, ND, ISIS)\n" +
                   "using Google's Gemini 1.5 Flash AI model for detailed, screen-reader-friendly descriptions.\n\n" +
                   "To get a free API key, visit:",
            Location = new System.Drawing.Point(20, 20),
            Size = new System.Drawing.Size(500, 90),
            AccessibleName = "Instructions",
            AccessibleDescription = "Gemini API key settings instructions"
        };

        // Link to get API key
        linkLabel = new LinkLabel
        {
            Text = "https://aistudio.google.com/apikey",
            Location = new System.Drawing.Point(20, 115),
            Size = new System.Drawing.Size(300, 20),
            AccessibleName = "API Key Website Link",
            AccessibleDescription = "Visit aistudio.google.com to get your free Gemini API key"
        };
        linkLabel.LinkClicked += LinkLabel_LinkClicked;

        // API Key label
        apiKeyLabel = new Label
        {
            Text = "Gemini API Key:",
            Location = new System.Drawing.Point(20, 150),
            Size = new System.Drawing.Size(150, 20),
            AccessibleName = "API Key Label"
        };

        // API Key text box
        apiKeyTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, 175),
            Size = new System.Drawing.Size(500, 25),
            AccessibleName = "Gemini API Key",
            AccessibleDescription = "Enter your Google Gemini API key",
            UseSystemPasswordChar = false  // Show the key (it's not a password)
        };

        // Save button
        saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(330, 225),
            Size = new System.Drawing.Size(90, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Save",
            AccessibleDescription = "Save Gemini API key settings"
        };
        saveButton.Click += SaveButton_Click;

        // Cancel button
        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(430, 225),
            Size = new System.Drawing.Size(90, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel without saving"
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Controls.Add(instructionsLabel);
        Controls.Add(linkLabel);
        Controls.Add(apiKeyLabel);
        Controls.Add(apiKeyTextBox);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);

        // Set tab order
        instructionsLabel.TabIndex = 0;
        linkLabel.TabIndex = 1;
        apiKeyLabel.TabIndex = 2;
        apiKeyTextBox.TabIndex = 3;
        saveButton.TabIndex = 4;
        cancelButton.TabIndex = 5;

        // Focus on API key textbox when form loads
        Load += (sender, e) =>
        {
            apiKeyTextBox.Focus();
        };
    }

    private void LoadCurrentSettings()
    {
        var settings = SettingsManager.Current;
        apiKeyTextBox.Text = settings.GeminiApiKey ?? "";
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        var settings = SettingsManager.Current;
        settings.GeminiApiKey = apiKeyTextBox.Text.Trim();
        SettingsManager.Save(settings);

        MessageBox.Show("Gemini API key saved successfully.\n\n" +
                       "You can now use Input Hotkey Mode (press '[') followed by:\n" +
                       "  1 - Read PFD\n" +
                       "  2 - Read Lower ECAM\n" +
                       "  3 - Read Upper ECAM/EWD\n" +
                       "  4 - Read ND\n" +
                       "  5 - Read ISIS",
                       "Settings Saved",
                       MessageBoxButtons.OK,
                       MessageBoxIcon.Information);
    }

    private void LinkLabel_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://aistudio.google.com/apikey",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open browser: {ex.Message}",
                          "Error",
                          MessageBoxButtons.OK,
                          MessageBoxIcon.Error);
        }
    }
}
