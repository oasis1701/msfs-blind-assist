using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Unified AI settings: choose the cloud provider (Google Gemini or Anthropic Claude) and configure
/// its API key, model, and route web-search grounding. The selected provider powers ALL three AI
/// features (display reading, scene description, route briefing). User-supplied keys only — MSFSBA
/// never pays centrally. Edits to both providers are kept in working state, so switching the provider
/// dropdown back and forth never loses what you typed; Save persists both providers plus the choice.
/// </summary>
public partial class AiSettingsForm : Form
{
    private ComboBox providerComboBox = null!;
    private Label providerLabel = null!;
    private Label instructionsLabel = null!;
    private LinkLabel linkLabel = null!;
    private Label apiKeyLabel = null!;
    private TextBox apiKeyTextBox = null!;
    private Label modelLabel = null!;
    private ComboBox modelComboBox = null!;
    private Button refreshModelsButton = null!;
    private Label modelStatusLabel = null!;
    private CheckBox searchGroundingCheckBox = null!;
    private Button saveButton = null!;
    private Button cancelButton = null!;

    private static readonly string[] GeminiFallbackModels =
        { "gemini-flash-latest", "gemini-3.5-flash", "gemini-2.5-flash" };
    private static readonly string[] ClaudeFallbackModels =
        { "claude-opus-4-8", "claude-sonnet-4-6", "claude-haiku-4-5" };

    private const string GeminiKeyUrl = "https://aistudio.google.com/apikey";
    private const string ClaudeKeyUrl = "https://console.anthropic.com/settings/keys";

    // Per-provider working state so edits to one provider survive switching to the other.
    private string _geminiKey = "", _geminiModel = "";
    private bool _geminiSearch;
    private string _claudeKey = "", _claudeModel = "";
    private bool _claudeSearch;

    private AiProvider _shownProvider;
    private bool _populatingModels;
    private bool _suppressProviderEvent;

    public AiSettingsForm()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void InitializeComponent()
    {
        Text = "AI Settings";
        Size = new System.Drawing.Size(560, 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        providerLabel = new Label
        {
            Text = "AI Provider:",
            Location = new System.Drawing.Point(20, 20),
            Size = new System.Drawing.Size(150, 20),
            AccessibleName = "AI Provider Label"
        };

        providerComboBox = new ComboBox
        {
            Location = new System.Drawing.Point(20, 45),
            Size = new System.Drawing.Size(380, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "AI Provider",
            AccessibleDescription = "Choose which cloud AI service powers display reading, scene description, and route briefing. When you switch, everything routes through the selected provider."
        };
        providerComboBox.Items.Add("Google Gemini");
        providerComboBox.Items.Add("Anthropic Claude");
        providerComboBox.SelectedIndexChanged += ProviderComboBox_SelectedIndexChanged;

        instructionsLabel = new Label
        {
            Text = "Configure your provider's API key to enable AI cockpit-display reading, scene description, and route briefing.\n\nGet an API key here:",
            Location = new System.Drawing.Point(20, 80),
            Size = new System.Drawing.Size(510, 50),
            AccessibleName = "Instructions"
        };

        linkLabel = new LinkLabel
        {
            Text = GeminiKeyUrl,
            Location = new System.Drawing.Point(20, 135),
            Size = new System.Drawing.Size(420, 20),
            AccessibleName = "API Key Website Link",
            AccessibleDescription = "Open the selected provider's website to get an API key."
        };
        linkLabel.LinkClicked += LinkLabel_LinkClicked;

        apiKeyLabel = new Label
        {
            Text = "API Key:",
            Location = new System.Drawing.Point(20, 165),
            Size = new System.Drawing.Size(150, 20),
            AccessibleName = "API Key Label"
        };

        apiKeyTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, 190),
            Size = new System.Drawing.Size(510, 25),
            AccessibleName = "API Key",
            AccessibleDescription = "Enter the API key for the selected provider."
        };

        modelLabel = new Label
        {
            Text = "Model:",
            Location = new System.Drawing.Point(20, 225),
            Size = new System.Drawing.Size(150, 20),
            AccessibleName = "Model Label"
        };

        modelComboBox = new ComboBox
        {
            Location = new System.Drawing.Point(20, 250),
            Size = new System.Drawing.Size(390, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Model",
            AccessibleDescription = "Select the model used for AI cockpit-display reading, scene description, and route briefing."
        };

        refreshModelsButton = new Button
        {
            Text = "Refresh models",
            Location = new System.Drawing.Point(420, 249),
            Size = new System.Drawing.Size(110, 27),
            AccessibleName = "Refresh models",
            AccessibleDescription = "Re-fetch the list of available models for your API key."
        };
        refreshModelsButton.Click += async (sender, e) => await PopulateModelsAsync();

        modelStatusLabel = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(20, 278),
            Size = new System.Drawing.Size(510, 18),
            AccessibleName = "Model list status"
        };

        searchGroundingCheckBox = new CheckBox
        {
            Text = "Enable web search grounding for route NOTAMs (extra cost)",
            Location = new System.Drawing.Point(20, 305),
            Size = new System.Drawing.Size(510, 25),
            AccessibleName = "Enable web search grounding for route NOTAMs",
            AccessibleDescription = "Lets the route briefing look up live NOTAMs via web search. Incurs additional API cost on either provider."
        };

        saveButton = new Button
        {
            Text = "Save",
            Location = new System.Drawing.Point(340, 380),
            Size = new System.Drawing.Size(90, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Save",
            AccessibleDescription = "Save AI settings"
        };
        saveButton.Click += SaveButton_Click;

        cancelButton = new Button
        {
            Text = "Cancel",
            Location = new System.Drawing.Point(440, 380),
            Size = new System.Drawing.Size(90, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel without saving"
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Controls.Add(providerLabel);
        Controls.Add(providerComboBox);
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

        int tab = 0;
        providerLabel.TabIndex = tab++;
        providerComboBox.TabIndex = tab++;
        instructionsLabel.TabIndex = tab++;
        linkLabel.TabIndex = tab++;
        apiKeyLabel.TabIndex = tab++;
        apiKeyTextBox.TabIndex = tab++;
        modelLabel.TabIndex = tab++;
        modelComboBox.TabIndex = tab++;
        refreshModelsButton.TabIndex = tab++;
        modelStatusLabel.TabIndex = tab++;
        searchGroundingCheckBox.TabIndex = tab++;
        saveButton.TabIndex = tab++;
        cancelButton.TabIndex = tab++;

        Load += async (sender, e) =>
        {
            providerComboBox.Focus();
            await PopulateModelsAsync();
        };
    }

    private void LoadCurrentSettings()
    {
        var settings = SettingsManager.Current;
        _geminiKey = settings.GeminiApiKey ?? "";
        _geminiModel = settings.GeminiModel ?? "";
        _geminiSearch = settings.GeminiSearchGrounding;
        _claudeKey = settings.ClaudeApiKey ?? "";
        _claudeModel = settings.ClaudeModel ?? "";
        _claudeSearch = settings.ClaudeWebSearch;
        _shownProvider = settings.AiProvider;

        _suppressProviderEvent = true;
        providerComboBox.SelectedIndex = _shownProvider == AiProvider.Claude ? 1 : 0;
        _suppressProviderEvent = false;

        LoadProviderIntoFields(_shownProvider);
    }

    private void ProviderComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressProviderEvent) return;
        StashFieldsFromUi(); // capture edits for the provider we are leaving
        _shownProvider = providerComboBox.SelectedIndex == 1 ? AiProvider.Claude : AiProvider.Gemini;
        LoadProviderIntoFields(_shownProvider);
        _ = PopulateModelsAsync();
    }

    private void LoadProviderIntoFields(AiProvider provider)
    {
        if (provider == AiProvider.Claude)
        {
            apiKeyTextBox.Text = _claudeKey;
            searchGroundingCheckBox.Checked = _claudeSearch;
            linkLabel.Text = ClaudeKeyUrl;
        }
        else
        {
            apiKeyTextBox.Text = _geminiKey;
            searchGroundingCheckBox.Checked = _geminiSearch;
            linkLabel.Text = GeminiKeyUrl;
        }
    }

    private void StashFieldsFromUi()
    {
        string selectedModel = (modelComboBox.SelectedItem as AiModelInfo)?.Id ?? "";
        if (_shownProvider == AiProvider.Claude)
        {
            _claudeKey = apiKeyTextBox.Text.Trim();
            _claudeSearch = searchGroundingCheckBox.Checked;
            if (!string.IsNullOrEmpty(selectedModel)) _claudeModel = selectedModel;
        }
        else
        {
            _geminiKey = apiKeyTextBox.Text.Trim();
            _geminiSearch = searchGroundingCheckBox.Checked;
            if (!string.IsNullOrEmpty(selectedModel)) _geminiModel = selectedModel;
        }
    }

    private async Task PopulateModelsAsync()
    {
        if (_populatingModels) return;
        _populatingModels = true;
        refreshModelsButton.Enabled = false;
        try
        {
            bool claude = _shownProvider == AiProvider.Claude;
            string savedModel = claude ? _claudeModel : _geminiModel;
            string key = apiKeyTextBox.Text.Trim();
            string[] fallback = claude ? ClaudeFallbackModels : GeminiFallbackModels;

            IReadOnlyList<AiModelInfo>? models = null;
            if (!string.IsNullOrEmpty(key))
            {
                try
                {
                    IAiProvider service = claude ? new ClaudeService(key) : new GeminiService(key);
                    models = await service.ListAvailableModelsAsync();
                }
                catch
                {
                    models = null; // fall back to the curated list below
                }
            }

            modelComboBox.BeginUpdate();
            try
            {
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
                    foreach (var id in fallback)
                    {
                        modelComboBox.Items.Add(new AiModelInfo(id, id));
                    }
                    modelStatusLabel.Text = string.IsNullOrEmpty(key)
                        ? "Enter an API key and choose Refresh models to load the current list."
                        : "Using offline model list (could not fetch from the provider).";
                }

                EnsureModelPresent(savedModel);
                SelectModel(savedModel);
            }
            finally
            {
                modelComboBox.EndUpdate();
            }
        }
        finally
        {
            _populatingModels = false;
            refreshModelsButton.Enabled = true;
        }
    }

    private void EnsureModelPresent(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        foreach (AiModelInfo item in modelComboBox.Items)
        {
            if (string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) return;
        }
        modelComboBox.Items.Insert(0, new AiModelInfo(id, id + " (saved)"));
    }

    private void SelectModel(string id)
    {
        for (int i = 0; i < modelComboBox.Items.Count; i++)
        {
            if (modelComboBox.Items[i] is AiModelInfo m &&
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
        StashFieldsFromUi();
        var settings = SettingsManager.Current;
        settings.AiProvider = _shownProvider;
        settings.GeminiApiKey = _geminiKey;
        settings.GeminiModel = _geminiModel;
        settings.GeminiSearchGrounding = _geminiSearch;
        settings.ClaudeApiKey = _claudeKey;
        settings.ClaudeModel = _claudeModel;
        settings.ClaudeWebSearch = _claudeSearch;
        SettingsManager.Save(settings);
    }

    private void LinkLabel_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = linkLabel.Text,
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
