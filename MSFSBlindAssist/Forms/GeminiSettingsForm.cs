using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Settings form for Gemini AI configuration
/// </summary>
public partial class GeminiSettingsForm : Form
{
    private TextBox apiKeyTextBox = null!;
    private CheckBox searchGroundingCheckBox = null!;
    private Button saveButton = null!;
    private Button cancelButton = null!;
    private Label instructionsLabel = null!;
    private Label apiKeyLabel = null!;
    private LinkLabel linkLabel = null!;

    private Label modelLabel = null!;
    private ComboBox modelComboBox = null!;
    private Button refreshModelsButton = null!;
    private Label modelStatusLabel = null!;

    private static readonly string[] FallbackModelIds =
        { "gemini-flash-latest", "gemini-3.5-flash", "gemini-2.5-flash" };

    public GeminiSettingsForm()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void InitializeComponent()
    {
        Text = "Gemini Settings";
        Size = new System.Drawing.Size(550, 430);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // Instructions label
        instructionsLabel = new Label
        {
            Text = "Configure your Google Gemini API key to enable AI-powered cockpit display reading.\n\n" +
                   "This feature allows you to capture and analyze cockpit displays (PFD, ECAM, ND, ISIS)\n" +
                   "using Google's Gemini AI for detailed, screen-reader-friendly descriptions.\n\n" +
                   "To get a free API key, visit:",
            Location = new System.Drawing.Point(20, 20),
            Size = new System.Drawing.Size(500, 90),
            AccessibleName = "Instructions",
            AccessibleDescription = "Gemini settings instructions"
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

        // Model label
        modelLabel = new Label
        {
            Text = "Gemini Model:",
            Location = new System.Drawing.Point(20, 210),
            Size = new System.Drawing.Size(150, 20),
            AccessibleName = "Model Label"
        };

        // Model dropdown
        modelComboBox = new ComboBox
        {
            Location = new System.Drawing.Point(20, 235),
            Size = new System.Drawing.Size(380, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Gemini Model",
            AccessibleDescription = "Select the Gemini model used for AI cockpit-display reading, scene description, and route briefing."
        };

        // Refresh button
        refreshModelsButton = new Button
        {
            Text = "Refresh models",
            Location = new System.Drawing.Point(410, 234),
            Size = new System.Drawing.Size(110, 27),
            AccessibleName = "Refresh models",
            AccessibleDescription = "Re-fetch the list of available Gemini models for your API key."
        };
        refreshModelsButton.Click += async (sender, e) => await PopulateModelsAsync();

        // Model status
        modelStatusLabel = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(20, 263),
            Size = new System.Drawing.Size(500, 18),
            AccessibleName = "Model list status"
        };

        // Google Search grounding checkbox
        searchGroundingCheckBox = new CheckBox
        {
            Text = "Enable Grounding with Google Search (paid feature)",
            Location = new System.Drawing.Point(20, 290),
            Size = new System.Drawing.Size(500, 25),
            AccessibleName = "Enable Grounding with Google Search",
            AccessibleDescription = "Enables Google Search grounding for route descriptions. This is a paid Gemini API feature that requires a billing account."
        };

        // Save button
        saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(330, 340),
            Size = new System.Drawing.Size(90, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Save",
            AccessibleDescription = "Save Gemini settings"
        };
        saveButton.Click += SaveButton_Click;

        // Cancel button
        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(430, 340),
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
        Controls.Add(modelLabel);
        Controls.Add(modelComboBox);
        Controls.Add(refreshModelsButton);
        Controls.Add(modelStatusLabel);
        Controls.Add(searchGroundingCheckBox);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);

        // Set tab order
        instructionsLabel.TabIndex = 0;
        linkLabel.TabIndex = 1;
        apiKeyLabel.TabIndex = 2;
        apiKeyTextBox.TabIndex = 3;
        modelLabel.TabIndex = 4;
        modelComboBox.TabIndex = 5;
        refreshModelsButton.TabIndex = 6;
        modelStatusLabel.TabIndex = 7;
        searchGroundingCheckBox.TabIndex = 8;
        saveButton.TabIndex = 9;
        cancelButton.TabIndex = 10;

        // Focus on API key textbox when form loads, then populate model list
        Load += async (sender, e) =>
        {
            apiKeyTextBox.Focus();
            await PopulateModelsAsync();
        };
    }

    private void LoadCurrentSettings()
    {
        var settings = SettingsManager.Current;
        apiKeyTextBox.Text = settings.GeminiApiKey ?? "";
        searchGroundingCheckBox.Checked = settings.GeminiSearchGrounding;
    }

    private async Task PopulateModelsAsync()
    {
        string savedModel = SettingsManager.Current.GeminiModel ?? "";
        string key = apiKeyTextBox.Text.Trim();

        IReadOnlyList<GeminiService.GeminiModelInfo>? models = null;
        if (!string.IsNullOrEmpty(key))
        {
            try
            {
                var service = new GeminiService(key);
                models = await service.ListAvailableModelsAsync();
            }
            catch
            {
                models = null; // fall back to the curated list below
            }
        }

        modelComboBox.BeginUpdate();
        modelComboBox.Items.Clear();
        if (models != null && models.Count > 0)
        {
            foreach (var m in models)
            {
                modelComboBox.Items.Add(m);
            }
            modelStatusLabel.Text = "";
        }
        else
        {
            foreach (var id in FallbackModelIds)
            {
                modelComboBox.Items.Add(new GeminiService.GeminiModelInfo(id, id));
            }
            modelStatusLabel.Text = string.IsNullOrEmpty(key)
                ? "Enter an API key and choose Refresh models to load the current list."
                : "Using offline model list (could not fetch from Gemini).";
        }

        EnsureModelPresent(savedModel);
        SelectModel(savedModel);
        modelComboBox.EndUpdate();
    }

    private void EnsureModelPresent(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        foreach (GeminiService.GeminiModelInfo item in modelComboBox.Items)
        {
            if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) return;
        }
        modelComboBox.Items.Insert(0, new GeminiService.GeminiModelInfo(id, id + " (saved)"));
    }

    private void SelectModel(string id)
    {
        for (int i = 0; i < modelComboBox.Items.Count; i++)
        {
            if (modelComboBox.Items[i] is GeminiService.GeminiModelInfo m &&
                string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                modelComboBox.SelectedIndex = i;
                return;
            }
        }
        if (modelComboBox.Items.Count > 0)
        {
            modelComboBox.SelectedIndex = 0;
        }
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        var settings = SettingsManager.Current;
        settings.GeminiApiKey = apiKeyTextBox.Text.Trim();
        settings.GeminiSearchGrounding = searchGroundingCheckBox.Checked;
        if (modelComboBox.SelectedItem is GeminiService.GeminiModelInfo selectedModel)
        {
            settings.GeminiModel = selectedModel.Id;
        }
        SettingsManager.Save(settings);

        MessageBox.Show("Gemini settings saved successfully.\n\n" +
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
