using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>
/// AI section of the unified Settings dialog. Picks the AI PROVIDER (Google Gemini or
/// Anthropic Claude) and configures each — API key + model (live-refreshable) + the
/// per-provider grounding/web-search toggle. Merges main's GeminiPanel with the Claude
/// provider added by the AI-provider feature; the standalone AiSettingsForm is retired.
/// The provider dropdown shows/hides the matching provider's fields. Save/Cancel belong
/// to the dialog (ISettingsPanel never persists).
/// </summary>
public class AiSettingsPanel : UserControl, ISettingsPanel
{
    private Label providerLabel = null!;
    private ComboBox providerComboBox = null!;
    private Label instructionsLabel = null!;

    // Gemini controls
    private Label geminiKeyLabel = null!;
    private TextBox geminiKeyTextBox = null!;
    private LinkLabel geminiLink = null!;
    private Label geminiModelLabel = null!;
    private ComboBox geminiModelComboBox = null!;
    private Button geminiRefreshButton = null!;
    private Label geminiStatusLabel = null!;
    private CheckBox geminiGroundingCheckBox = null!;

    // Claude controls
    private Label claudeKeyLabel = null!;
    private TextBox claudeKeyTextBox = null!;
    private LinkLabel claudeLink = null!;
    private Label claudeModelLabel = null!;
    private ComboBox claudeModelComboBox = null!;
    private Button claudeRefreshButton = null!;
    private Label claudeStatusLabel = null!;
    private CheckBox claudeWebSearchCheckBox = null!;

    private static readonly string[] GeminiFallbackModelIds =
        { "gemini-flash-latest", "gemini-3.5-flash", "gemini-2.5-flash" };
    private static readonly string[] ClaudeFallbackModelIds =
        { "claude-opus-4-8", "claude-sonnet-5", "claude-haiku-4-5-20251001" };

    private bool _populatingGemini;
    private bool _populatingClaude;

    public string TabTitle => "AI";

    public AiSettingsPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        providerLabel = new Label
        {
            Text = "AI Provider:",
            Location = new System.Drawing.Point(20, 18),
            Size = new System.Drawing.Size(120, 20),
            AccessibleName = "AI Provider label"
        };
        providerComboBox = new ComboBox
        {
            Location = new System.Drawing.Point(140, 15),
            Size = new System.Drawing.Size(200, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "AI Provider",
            AccessibleDescription = "Choose which AI service powers cockpit-display reading, scene description, and route briefing. Hard switch — all three use the selected provider."
        };
        providerComboBox.Items.Add("Google Gemini");
        providerComboBox.Items.Add("Anthropic Claude");
        providerComboBox.SelectedIndexChanged += (_, _) => UpdateProviderVisibility();

        instructionsLabel = new Label
        {
            Text = "Configure the API key for your chosen AI provider to enable AI cockpit-display reading,\n" +
                   "scene description, and route briefing. Each provider uses your own API key.",
            Location = new System.Drawing.Point(20, 48),
            Size = new System.Drawing.Size(520, 40),
            AccessibleName = "Instructions"
        };

        // ── Gemini ──────────────────────────────────────────────────────────
        geminiKeyLabel = new Label { Text = "Gemini API Key:", Location = new System.Drawing.Point(20, 100), Size = new System.Drawing.Size(150, 20), AccessibleName = "Gemini API Key Label" };
        geminiKeyTextBox = new TextBox { Location = new System.Drawing.Point(20, 122), Size = new System.Drawing.Size(500, 25), AccessibleName = "Gemini API Key", AccessibleDescription = "Enter your Google Gemini API key" };
        geminiLink = new LinkLabel { Text = "Get a free Gemini key: https://aistudio.google.com/apikey", Location = new System.Drawing.Point(20, 150), Size = new System.Drawing.Size(500, 20), AccessibleName = "Gemini API Key Website Link" };
        geminiLink.LinkClicked += (_, _) => OpenUrl("https://aistudio.google.com/apikey");
        geminiModelLabel = new Label { Text = "Gemini Model:", Location = new System.Drawing.Point(20, 180), Size = new System.Drawing.Size(150, 20), AccessibleName = "Gemini Model Label" };
        geminiModelComboBox = new ComboBox { Location = new System.Drawing.Point(20, 202), Size = new System.Drawing.Size(380, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Gemini Model", AccessibleDescription = "Select the Gemini model." };
        geminiRefreshButton = new Button { Text = "Refresh models", Location = new System.Drawing.Point(410, 201), Size = new System.Drawing.Size(110, 27), AccessibleName = "Refresh Gemini models" };
        geminiRefreshButton.Click += async (_, _) => await PopulateGeminiModelsAsync();
        geminiStatusLabel = new Label { Text = "", Location = new System.Drawing.Point(20, 230), Size = new System.Drawing.Size(500, 18), AccessibleName = "Gemini model list status" };
        geminiGroundingCheckBox = new CheckBox { Text = "Enable Grounding with Google Search (paid feature)", Location = new System.Drawing.Point(20, 254), Size = new System.Drawing.Size(500, 25), AccessibleName = "Enable Grounding with Google Search", AccessibleDescription = "Paid Gemini feature for route grounding; requires a billing account." };

        // ── Claude ──────────────────────────────────────────────────────────
        claudeKeyLabel = new Label { Text = "Claude API Key:", Location = new System.Drawing.Point(20, 100), Size = new System.Drawing.Size(150, 20), AccessibleName = "Claude API Key Label" };
        claudeKeyTextBox = new TextBox { Location = new System.Drawing.Point(20, 122), Size = new System.Drawing.Size(500, 25), AccessibleName = "Claude API Key", AccessibleDescription = "Enter your Anthropic Claude API key" };
        claudeLink = new LinkLabel { Text = "Get a Claude key: https://console.anthropic.com/settings/keys", Location = new System.Drawing.Point(20, 150), Size = new System.Drawing.Size(500, 20), AccessibleName = "Claude API Key Website Link" };
        claudeLink.LinkClicked += (_, _) => OpenUrl("https://console.anthropic.com/settings/keys");
        claudeModelLabel = new Label { Text = "Claude Model:", Location = new System.Drawing.Point(20, 180), Size = new System.Drawing.Size(150, 20), AccessibleName = "Claude Model Label" };
        claudeModelComboBox = new ComboBox { Location = new System.Drawing.Point(20, 202), Size = new System.Drawing.Size(380, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Claude Model", AccessibleDescription = "Select the Claude model. Opus is most capable; Sonnet/Haiku are cheaper." };
        claudeRefreshButton = new Button { Text = "Refresh models", Location = new System.Drawing.Point(410, 201), Size = new System.Drawing.Size(110, 27), AccessibleName = "Refresh Claude models" };
        claudeRefreshButton.Click += async (_, _) => await PopulateClaudeModelsAsync();
        claudeStatusLabel = new Label { Text = "", Location = new System.Drawing.Point(20, 230), Size = new System.Drawing.Size(500, 18), AccessibleName = "Claude model list status" };
        claudeWebSearchCheckBox = new CheckBox { Text = "Enable web search for NOTAM route grounding (extra cost)", Location = new System.Drawing.Point(20, 254), Size = new System.Drawing.Size(500, 25), AccessibleName = "Enable Claude web search", AccessibleDescription = "Lets Claude search the web when composing the route briefing; billed per search." };

        Controls.AddRange(new Control[]
        {
            providerLabel, providerComboBox, instructionsLabel,
            geminiKeyLabel, geminiKeyTextBox, geminiLink, geminiModelLabel, geminiModelComboBox, geminiRefreshButton, geminiStatusLabel, geminiGroundingCheckBox,
            claudeKeyLabel, claudeKeyTextBox, claudeLink, claudeModelLabel, claudeModelComboBox, claudeRefreshButton, claudeStatusLabel, claudeWebSearchCheckBox
        });

        int t = 0;
        providerComboBox.TabIndex = t++;
        geminiKeyTextBox.TabIndex = t++; geminiLink.TabIndex = t++; geminiModelComboBox.TabIndex = t++; geminiRefreshButton.TabIndex = t++; geminiGroundingCheckBox.TabIndex = t++;
        claudeKeyTextBox.TabIndex = t++; claudeLink.TabIndex = t++; claudeModelComboBox.TabIndex = t++; claudeRefreshButton.TabIndex = t++; claudeWebSearchCheckBox.TabIndex = t++;
    }

    private void UpdateProviderVisibility()
    {
        bool claude = providerComboBox.SelectedIndex == 1;
        foreach (var c in new Control[] { geminiKeyLabel, geminiKeyTextBox, geminiLink, geminiModelLabel, geminiModelComboBox, geminiRefreshButton, geminiStatusLabel, geminiGroundingCheckBox })
            c.Visible = !claude;
        foreach (var c in new Control[] { claudeKeyLabel, claudeKeyTextBox, claudeLink, claudeModelLabel, claudeModelComboBox, claudeRefreshButton, claudeStatusLabel, claudeWebSearchCheckBox })
            c.Visible = claude;
    }

    public void LoadFrom(UserSettings settings)
    {
        providerComboBox.SelectedIndex = settings.AiProvider == AiProvider.Claude ? 1 : 0;
        geminiKeyTextBox.Text = settings.GeminiApiKey ?? "";
        geminiGroundingCheckBox.Checked = settings.GeminiSearchGrounding;
        claudeKeyTextBox.Text = settings.ClaudeApiKey ?? "";
        claudeWebSearchCheckBox.Checked = settings.ClaudeWebSearch;
        UpdateProviderVisibility();
        _ = PopulateGeminiModelsAsync();
        _ = PopulateClaudeModelsAsync();
    }

    private async Task PopulateGeminiModelsAsync()
    {
        if (_populatingGemini) return;
        _populatingGemini = true;
        geminiRefreshButton.Enabled = false;
        try
        {
            string savedModel = SettingsManager.Current.GeminiModel ?? "";
            string key = geminiKeyTextBox.Text.Trim();
            IReadOnlyList<AiModelInfo>? models = null;
            if (!string.IsNullOrEmpty(key))
            {
                try { models = await new GeminiService(key).ListAvailableModelsAsync(); }
                catch { models = null; }
            }
            if (IsDisposed || Disposing) return;
            geminiModelComboBox.BeginUpdate();
            try
            {
                geminiModelComboBox.Items.Clear();
                if (models != null && models.Count > 0)
                {
                    foreach (var m in models) geminiModelComboBox.Items.Add(m);
                    geminiStatusLabel.Text = "";
                }
                else
                {
                    foreach (var id in GeminiFallbackModelIds) geminiModelComboBox.Items.Add(new AiModelInfo(id, id));
                    geminiStatusLabel.Text = string.IsNullOrEmpty(key)
                        ? "Enter an API key and choose Refresh models to load the current list."
                        : "Using offline model list (could not fetch from Gemini).";
                }
                EnsureAndSelect(geminiModelComboBox, savedModel, id => new AiModelInfo(id, id + " (saved)"), o => (o as AiModelInfo)?.Id);
            }
            finally { geminiModelComboBox.EndUpdate(); }
        }
        finally
        {
            _populatingGemini = false;
            if (!IsDisposed && !Disposing) geminiRefreshButton.Enabled = true;
        }
    }

    private async Task PopulateClaudeModelsAsync()
    {
        if (_populatingClaude) return;
        _populatingClaude = true;
        claudeRefreshButton.Enabled = false;
        try
        {
            string savedModel = SettingsManager.Current.ClaudeModel ?? "";
            string key = claudeKeyTextBox.Text.Trim();
            IReadOnlyList<AiModelInfo>? models = null;
            if (!string.IsNullOrEmpty(key))
            {
                try { models = await new ClaudeService(key).ListAvailableModelsAsync(); }
                catch { models = null; }
            }
            if (IsDisposed || Disposing) return;
            claudeModelComboBox.BeginUpdate();
            try
            {
                claudeModelComboBox.Items.Clear();
                if (models != null && models.Count > 0)
                {
                    foreach (var m in models) claudeModelComboBox.Items.Add(m);
                    claudeStatusLabel.Text = "";
                }
                else
                {
                    foreach (var id in ClaudeFallbackModelIds) claudeModelComboBox.Items.Add(new AiModelInfo(id, id));
                    claudeStatusLabel.Text = string.IsNullOrEmpty(key)
                        ? "Enter an API key and choose Refresh models to load the current list."
                        : "Using offline model list (could not fetch from Claude).";
                }
                EnsureAndSelect(claudeModelComboBox, savedModel, id => new AiModelInfo(id, id + " (saved)"), o => (o as AiModelInfo)?.Id);
            }
            finally { claudeModelComboBox.EndUpdate(); }
        }
        finally
        {
            _populatingClaude = false;
            if (!IsDisposed && !Disposing) claudeRefreshButton.Enabled = true;
        }
    }

    private static void EnsureAndSelect(ComboBox combo, string id, Func<string, object> make, Func<object?, string?> idOf)
    {
        if (!string.IsNullOrEmpty(id))
        {
            bool present = false;
            foreach (var item in combo.Items)
                if (string.Equals(idOf(item), id, StringComparison.OrdinalIgnoreCase)) { present = true; break; }
            if (!present) combo.Items.Insert(0, make(id));
            for (int i = 0; i < combo.Items.Count; i++)
                if (string.Equals(idOf(combo.Items[i]), id, StringComparison.OrdinalIgnoreCase)) { combo.SelectedIndex = i; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = ""; focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.AiProvider = providerComboBox.SelectedIndex == 1 ? AiProvider.Claude : AiProvider.Gemini;
        settings.GeminiApiKey = geminiKeyTextBox.Text.Trim();
        settings.GeminiSearchGrounding = geminiGroundingCheckBox.Checked;
        if (geminiModelComboBox.SelectedItem is AiModelInfo gm) settings.GeminiModel = gm.Id;
        settings.ClaudeApiKey = claudeKeyTextBox.Text.Trim();
        settings.ClaudeWebSearch = claudeWebSearchCheckBox.Checked;
        if (claudeModelComboBox.SelectedItem is AiModelInfo cm) settings.ClaudeModel = cm.Id;
    }

    public void OnLeaving() { }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Could not open browser: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
